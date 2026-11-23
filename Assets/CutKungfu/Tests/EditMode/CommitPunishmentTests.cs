using System.Collections.Generic;
using System.Text;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using NUnit.Framework;
using Unity.Mathematics;

namespace CutKungfu.Tests
{
    /// <summary>
    /// 包囲時に、解除後の硬直(コミット時間)が実際に罰になっているか。
    /// 置く数ごとに、技の実行中・硬直中に殴られる割合を測る。検証したい仮説「硬直があると包囲で置く数が減る」は、
    /// 多く置くほど殴られやすい(硬直中の被弾が増える)ことが前提になる。
    /// </summary>
    public class CommitPunishmentTests
    {
        const BrickKind P = BrickKind.Palm;
        const BrickKind S = BrickKind.Spin;
        const BrickKind F = BrickKind.FlyingKick;
        const int Seeds = 20;
        const int Self = -1;

        /// <summary>机に届かない包囲(敵は交戦距離で 90°/210°/330°)。</summary>
        static readonly float3 Player = new float3(-6f, 0f, 0f);

        static float3 Enemy(int i)
        {
            float a = math.radians(90f + 120f * i);
            return Player + new float3(math.cos(a), 0f, math.sin(a)) * Tuning.Default.EnemyEngageRange;
        }

        /// <summary>置き場所: 自分(範囲技)、敵(飛び蹴り)、敵の手前 0.7m(掌底)。</summary>
        static float3 At(BrickKind kind, int target)
        {
            if (target == Self) return Player;
            float3 e = Enemy(target);
            return kind == P ? e - math.normalizesafe(e - Player) * 0.7f : e;
        }

        sealed class Plan
        {
            public string Name;
            public (BrickKind kind, int target)[] Poses;
        }

        /// <summary>置く数 1〜4 ごとの代表の並び(範囲技始動 / 単体技を別々の敵に散らす / 単体技を1体に集中)。</summary>
        static readonly Plan[][] PlansByCount =
        {
            new[]
            {
                new Plan { Name = "範囲技始動 spin", Poses = new[] { (S, Self) } },
                new Plan { Name = "単体 palm", Poses = new[] { (P, 0) } },
                new Plan { Name = "単体 fly", Poses = new[] { (F, 0) } },
            },
            new[]
            {
                new Plan { Name = "範囲技始動 spin>palm", Poses = new[] { (S, Self), (P, 0) } },
                new Plan { Name = "散らす palm>fly", Poses = new[] { (P, 0), (F, 1) } },
                new Plan { Name = "集中 palm>fly", Poses = new[] { (P, 0), (F, 0) } },
            },
            new[]
            {
                new Plan { Name = "範囲技始動 spin>fly>palm", Poses = new[] { (S, Self), (F, 0), (P, 1) } },
                new Plan { Name = "散らす palm>fly>palm", Poses = new[] { (P, 0), (F, 1), (P, 2) } },
                new Plan { Name = "集中 palm>fly>palm", Poses = new[] { (P, 0), (F, 0), (P, 0) } },
            },
            new[]
            {
                new Plan { Name = "範囲技始動 spin>fly>palm>palm", Poses = new[] { (S, Self), (F, 0), (P, 0), (P, 1) } },
                new Plan { Name = "散らす palm>fly>palm>fly", Poses = new[] { (P, 0), (F, 1), (P, 2), (F, 0) } },
                new Plan { Name = "集中 palm>fly>palm>fly", Poses = new[] { (P, 0), (F, 0), (P, 0), (F, 0) } },
            },
        };

        enum Outcome { HitDuringExecution, HitDuringCommit, NotHit, NotPlaced }

        /// <summary>
        /// 1回流す。技を出す直前に各敵の攻撃開始を乱数で振る。予備動作中を含む: -EnemyWindup〜EnemyCooldownMax(負なら予備動作中。同時に2体まで)。
        /// 待ちのみ: 0〜EnemyCooldownMax。
        /// </summary>
        /// <param name="dt">1回の更新で進める実時間。</param>
        /// <param name="trace">渡すと、解除からの敵ごとの出来事(ゲーム時間)を書き出す。</param>
        static Outcome Run(Plan plan, int seed, in Tuning tuning, bool commitEnabled, bool includeWindups = true,
            float dt = 1f / 60f, StringBuilder trace = null)
        {
            using var w = new GameplayTestWorld(Player, Enemy(0), Enemy(1), Enemy(2));
            w.StepDt = dt;
            var ts = w.Singleton<TuningSingleton>();
            ts.Value = tuning;
            w.SetSingleton(ts);
            var session = w.Singleton<SessionState>();
            session.CommitEnabled = commitEnabled;
            w.SetSingleton(session);
            w.Step();
            w.SetGauge(60f);

            var poses = new (BrickKind, float3)[plan.Poses.Length];
            for (int i = 0; i < poses.Length; i++) poses[i] = (plan.Poses[i].kind, At(plan.Poses[i].kind, plan.Poses[i].target));
            w.PlacePoses(poses);

            // 敵の攻撃開始は解除の直前に振る(スローの長さは更新の回数で決まるので、先に振ると dt で待ちの減り方が変わる)
            var rng = Random.CreateFromIndex((uint)(seed + 5000));
            int windups = 0;
            foreach (var e in w.Enemies())
            {
                var brain = w.Em.GetComponentData<EnemyBrain>(e);
                float start = rng.NextFloat(includeWindups ? -tuning.EnemyWindup : 0f, tuning.EnemyCooldownMax);
                if (start < 0f && windups < tuning.EnemyMaxConcurrentWindups)
                {
                    brain.State = EnemyState.Windup;
                    brain.Timer = tuning.EnemyWindup + start;
                    windups++;
                }
                else
                {
                    brain.State = EnemyState.Approach;
                    brain.Cooldown = math.max(0f, start);
                }
                w.Em.SetComponentData(e, brain);
            }

            w.ReleaseCut();
            var cut = w.Em.GetComponentData<CutState>(w.Player);
            if (cut.Phase != CutPhase.Executing || cut.PoseCount != poses.Length) return Outcome.NotPlaced;

            bool hitInCommit = false;
            var tracer = trace != null ? new Tracer(w, trace) : null;
            // 上限は実時間で決める(更新の回数で決めると、dt が小さいほど早く打ち切ってしまう)
            int maxSteps = (int)math.ceil(15f / dt);
            for (int i = 0; i < maxSteps; i++)
            {
                var c = w.Em.GetComponentData<CutState>(w.Player);
                if (c.Phase == CutPhase.Commit && c.Record.HitDuringCommit) hitInCommit = true;
                if (!c.IsActive || w.Singleton<SessionState>().Phase != SessionPhase.Playing) break;
                w.Step();
                tracer?.Sample();
            }

            foreach (var t in w.Em.GetBuffer<TelemetryEvent>(w.Em.CreateEntityQuery(typeof(TelemetryEvent)).GetSingletonEntity()))
            {
                if (t.Value.Kind == PlaytestEventKind.Cut && t.Value.Interrupted) return Outcome.HitDuringExecution;
                if (t.Value.Kind == PlaytestEventKind.Cut && t.Value.HitDuringCommit) hitInCommit = true;
            }
            return hitInCommit ? Outcome.HitDuringCommit : Outcome.NotHit;
        }

        /// <summary>置く数ごとの件数(実行中に被弾 / 硬直中に被弾 / 被弾なし)。各置く数 3並び×種 Seeds 通り。</summary>
        /// <param name="onlyPoses">0 ならすべての置く数、1〜4 ならその置く数だけ。</param>
        static int[,] Measure(in Tuning tuning, bool commitEnabled, bool includeWindups = true, int onlyPoses = 0)
        {
            var counts = new int[4, 4];
            for (int n = 0; n < 4; n++)
            if (onlyPoses == 0 || onlyPoses == n + 1)
            foreach (var plan in PlansByCount[n])
            for (int seed = 1; seed <= Seeds; seed++)
                counts[n, (int)Run(plan, seed, tuning, commitEnabled, includeWindups)]++;
            return counts;
        }

        static void AppendTable(StringBuilder sb, int[,] c)
        {
            sb.Append("置く数 | 実行中に被弾 | 硬直中に被弾 | 被弾なし | 置けず | 被弾率(実行中+硬直中)\n");
            for (int n = 0; n < 4; n++)
            {
                int placed = c[n, 0] + c[n, 1] + c[n, 2];
                float rate = placed > 0 ? 100f * (c[n, 0] + c[n, 1]) / placed : 0f;
                sb.Append($"{n + 1} | {c[n, 0]} | {c[n, 1]} | {c[n, 2]} | {c[n, 3]} | {rate:0}%\n");
            }
        }

        /// <summary>計測用(合否なし): 既定の調整値で、置く数ごとの被弾を測る(条件A の2条件と、条件B の4個技)。</summary>
        [Explicit("計測用: 包囲で置く数ごとの、実行中・硬直中の被弾を測る(合否なし)")]
        [Test]
        public void Measure_CommitPunishmentByPoseCount()
        {
            var sb = new StringBuilder("[条件A・予備動作中を含む]\n");
            AppendTable(sb, Measure(Tuning.Default, commitEnabled: true));
            sb.Append("\n[条件A・待ちのみ]\n");
            AppendTable(sb, Measure(Tuning.Default, commitEnabled: true, includeWindups: false));
            sb.Append("4個技の並び別(待ちのみ): ");
            foreach (var plan in PlansByCount[3])
            {
                int commitHits = 0;
                for (int seed = 1; seed <= Seeds; seed++)
                    if (Run(plan, seed, Tuning.Default, commitEnabled: true, includeWindups: false) == Outcome.HitDuringCommit) commitHits++;
                sb.Append($"{plan.Name} 硬直中に被弾 {commitHits}/{Seeds}  ");
            }
            sb.Append('\n');
            var b = Measure(Tuning.Default, commitEnabled: false, onlyPoses: 4);
            sb.Append($"\n[条件B・予備動作中を含む] 4個技: 実行中に被弾 {b[3, 0]} / 硬直中に被弾 {b[3, 1]} / 被弾なし {b[3, 2]}\n");
            TestContext.WriteLine(sb.ToString());
            UnityEngine.Debug.Log("[PUNISH]\n" + sb);
            Assert.Pass(sb.ToString());
        }

        /// <summary>
        /// 計測用(合否なし): 技の打撃を受けた敵の待ちの倍率(EnemyWaitScaleAfterCutHit)× 範囲技の被弾硬直(EnemyAreaHitStun)の格子。
        /// 条件B(コミット時間なし)で4個技の硬直中被弾が起きないことも各格子で見る。
        /// </summary>
        [Explicit("計測用: 待ちの倍率×範囲技の被弾硬直の格子で、置く数ごとの被弾を測る(合否なし)")]
        [Test]
        public void Measure_CommitPunishmentGrid()
        {
            var sb = new StringBuilder();
            foreach (float waitScale in new[] { 1f, 0.5f, 0f })
            foreach (float areaStun in new[] { 0.8f, 0.3f })
            {
                var t = Tuning.Default;
                t.EnemyWaitScaleAfterCutHit = waitScale;
                t.EnemyAreaHitStun = areaStun;
                sb.Append($"\n[待ちの倍率 {waitScale:0.0} / 範囲技の被弾硬直 {areaStun:0.0}]\n");
                var a = Measure(t, commitEnabled: true);
                AppendTable(sb, a);
                var b = Measure(t, commitEnabled: false);
                sb.Append($"条件B(コミット時間なし)の4個技: 硬直中に被弾 {b[3, 1]} / 実行中に被弾 {b[3, 0]} / 被弾なし {b[3, 2]}\n");
            }
            TestContext.WriteLine(sb.ToString());
            UnityEngine.Debug.Log("[PUNISH-GRID]\n" + sb);
            Assert.Pass(sb.ToString());
        }

        /// <summary>
        /// 回帰テスト(種を固定): 包囲で、4個技の硬直中の被弾が1〜3個技より明確に多い(最大の2倍以上)。
        /// 条件B(コミット時間なし)では4個技でも硬直中の被弾は起きない。罰が届くのは4個目の硬直だけ、という現状の前提を守る。
        /// </summary>
        [Test]
        public void FourPoseCommit_IsPunishedMoreThanSmallerCuts()
        {
            var a = Measure(Tuning.Default, commitEnabled: true);
            int smallerMax = math.max(a[0, 1], math.max(a[1, 1], a[2, 1]));
            var sb = new StringBuilder();
            AppendTable(sb, a);
            Assert.That(a[3, 1], Is.GreaterThan(0), sb.ToString());
            Assert.That(a[3, 1], Is.GreaterThanOrEqualTo(2 * smallerMax), "4個技の硬直中の被弾が1〜3個技より明確に多くない:\n" + sb);

            var b = Measure(Tuning.Default, commitEnabled: false, onlyPoses: 4);
            Assert.That(b[3, 1], Is.Zero, "条件Bで硬直中に殴られた");
        }

        /// <summary>
        /// 回帰テスト: 同じ並び(spin>fly>palm>palm)を dt 1/60 と 1/240 で流し、硬直中の被弾の件数がほぼ同じ(20回中の差が3以内)。
        /// 以前は区間の余り時間を捨てる・吹き飛びをフレームごとに減衰させる等で、1/60 で 18/20、1/240 で 2/20 と大きく違った。
        /// </summary>
        [TestCase(false, TestName = "CommitPunishment_DoesNotDependOnStepLength(待ちのみ)")]
        [TestCase(true, TestName = "CommitPunishment_DoesNotDependOnStepLength(予備動作中を含む)")]
        public void CommitPunishment_DoesNotDependOnStepLength(bool includeWindups)
        {
            var plan = PlansByCount[3][0];
            int CommitHits(float dt)
            {
                int n = 0;
                for (int seed = 1; seed <= Seeds; seed++)
                    if (Run(plan, seed, Tuning.Default, commitEnabled: true, includeWindups, dt) == Outcome.HitDuringCommit) n++;
                return n;
            }
            int at60 = CommitHits(1f / 60f), at240 = CommitHits(1f / 240f);
            Assert.That(math.abs(at60 - at240), Is.LessThanOrEqualTo(3), $"{plan.Name} 硬直中に被弾: dt 1/60 で {at60}/{Seeds}、1/240 で {at240}/{Seeds}");
        }

        /// <summary>解除からのゲーム時間で、敵ごとの出来事(被弾硬直の終了・戻りの終了・予備動作の開始・打撃・吹き飛びの停止位置)を書き出す。</summary>
        sealed class Tracer
        {
            readonly GameplayTestWorld _w;
            readonly StringBuilder _sb;
            readonly Unity.Collections.NativeArray<Unity.Entities.Entity> _enemies;
            readonly EnemyState[] _state;
            readonly float[] _stun;
            readonly bool[] _moving;
            readonly bool[] _dead;
            readonly float[] _hp;
            readonly double _t0;
            float _playerStun;
            CutPhase _phase;
            int _segment;

            public Tracer(GameplayTestWorld w, StringBuilder sb)
            {
                _w = w;
                _sb = sb;
                _enemies = w.Enemies();
                _state = new EnemyState[_enemies.Length];
                _stun = new float[_enemies.Length];
                _moving = new bool[_enemies.Length];
                _dead = new bool[_enemies.Length];
                _hp = new float[_enemies.Length];
                _t0 = w.Singleton<GameClock>().ElapsedGame;
                _phase = w.Em.GetComponentData<CutState>(w.Player).Phase;
                for (int i = 0; i < _enemies.Length; i++)
                {
                    _state[i] = w.Em.GetComponentData<EnemyBrain>(_enemies[i]).State;
                    var b = w.Em.GetComponentData<EnemyBrain>(_enemies[i]);
                    _hp[i] = w.Em.GetComponentData<Fighter>(_enemies[i]).Health;
                    sb.Append($"  敵{i} 開始 {b.State} 待ち {b.Cooldown:0.00} 残り {b.Timer:0.00}\n");
                }
            }

            public void Sample()
            {
                var em = _w.Em;
                float t = (float)(_w.Singleton<GameClock>().ElapsedGame - _t0);
                var cut = em.GetComponentData<CutState>(_w.Player);
                if (cut.Phase != _phase) { _sb.Append($"  {t:0.000} 主人公 {_phase}→{cut.Phase}\n"); _phase = cut.Phase; }
                if (cut.SegmentIndex != _segment)
                {
                    var pp = em.GetComponentData<Unity.Transforms.LocalTransform>(_w.Player).Position;
                    _sb.Append($"  {t:0.000} 主人公 区間{_segment}の終わり ({pp.x:0.00},{pp.z:0.00})\n");
                    _segment = cut.SegmentIndex;
                }
                float ps = em.GetComponentData<Fighter>(_w.Player).HitStun;
                if (ps > _playerStun) _sb.Append($"  {t:0.000} 主人公 被弾\n");
                _playerStun = ps;
                for (int i = 0; i < _enemies.Length; i++)
                {
                    var e = _enemies[i];
                    if (!em.Exists(e) || _dead[i]) continue;
                    var b = em.GetComponentData<EnemyBrain>(e);
                    var f = em.GetComponentData<Fighter>(e);
                    var p = em.GetComponentData<Unity.Transforms.LocalTransform>(e).Position;
                    bool moving = math.lengthsq(em.GetComponentData<Knockback>(e).Velocity) > 0f;
                    if (f.Health < _hp[i] - 1e-3f)
                        _sb.Append($"  {t:0.000} 敵{i} 被弾 ({p.x:0.00},{p.z:0.00}) 硬直 {f.HitStun:0.00} 体力 {_hp[i]:0}→{f.Health:0}\n");
                    _hp[i] = f.Health;
                    if (em.HasComponent<Dying>(e)) { _dead[i] = true; _sb.Append($"  {t:0.000} 敵{i} 撃破\n"); continue; }
                    if (_stun[i] > 0f && f.HitStun <= 0f) _sb.Append($"  {t:0.000} 敵{i} 被弾硬直の終了 待ち {b.Cooldown:0.00}\n");
                    if (_moving[i] && !moving) _sb.Append($"  {t:0.000} 敵{i} 吹き飛びの停止 ({p.x:0.00},{p.z:0.00}) 主人公から {math.distance(p.xz, em.GetComponentData<Unity.Transforms.LocalTransform>(_w.Player).Position.xz):0.00}m\n");
                    if (b.State != _state[i])
                    {
                        string what = (_state[i], b.State) switch
                        {
                            (EnemyState.Approach, EnemyState.Windup) => "予備動作の開始",
                            (EnemyState.Windup, EnemyState.Recover) => f.HitStun > 0f ? "予備動作が潰れた" : "打撃",
                            (EnemyState.Recover, EnemyState.Approach) => $"戻りの終了 待ち {b.Cooldown:0.00}",
                            _ => $"{_state[i]}→{b.State}",
                        };
                        _sb.Append($"  {t:0.000} 敵{i} {what}\n");
                    }
                    _state[i] = b.State;
                    _stun[i] = f.HitStun;
                    _moving[i] = moving;
                }
            }
        }

        /// <summary>計測用: spin>fly>palm>palm を dt を変えて流し、敵ごとの出来事の時刻を比べる。</summary>
        [Explicit("計測用: フレームレートで変わる箇所を探す(合否なし)")]
        [Test]
        public void Probe_FrameRateTimeline([Values(1, 2, 3)] int seed)
        {
            var plan = PlansByCount[3][0];
            var sb = new StringBuilder($"[{plan.Name} 種 {seed} 待ちのみ]\n");
            foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f, 1f / 240f })
            {
                sb.Append($"-- dt 1/{math.round(1f / dt)}\n");
                var o = Run(plan, seed, Tuning.Default, commitEnabled: true, includeWindups: false, dt: dt, trace: sb);
                sb.Append($"  結果 {o}\n");
            }
            int[] commitHits = new int[4];
            float[] dts = { 1f / 30f, 1f / 60f, 1f / 120f, 1f / 240f };
            if (seed == 1)
            {
                sb.Append("\n硬直中に被弾 / 20: ");
                for (int k = 0; k < 4; k++)
                {
                    for (int s = 1; s <= Seeds; s++)
                        if (Run(plan, s, Tuning.Default, true, includeWindups: false, dt: dts[k]) == Outcome.HitDuringCommit) commitHits[k]++;
                    sb.Append($"1/{math.round(1f / dts[k])}={commitHits[k]}  ");
                }
            }
            UnityEngine.Debug.Log("[PROBE]\n" + sb);
            Assert.Pass(sb.ToString());
        }

        /// <summary>技の打撃を受けた敵は、被弾硬直のあと、残っていた待ちに EnemyWaitScaleAfterCutHit が掛かる。</summary>
        [TestCase(1f, false)]
        [TestCase(0f, true)]
        public void CutHitEnemy_WaitIsScaledAfterHitStun(float scale, bool expectNoWait)
        {
            var t = Tuning.Default;
            t.EnemyWaitScaleAfterCutHit = scale;
            float3 enemyAt = new float3(1.2f, 0f, 0f);
            using var w = new GameplayTestWorld(float3.zero, enemyAt);
            var ts = w.Singleton<TuningSingleton>();
            ts.Value = t;
            w.SetSingleton(ts);
            w.Step();
            var enemy = w.Enemies()[0];
            var brain = w.Em.GetComponentData<EnemyBrain>(enemy);
            brain.State = EnemyState.Approach;
            brain.Cooldown = 2f;
            w.Em.SetComponentData(enemy, brain);

            w.PerformCut((F, enemyAt));
            // 被弾硬直が明けるまで回す
            bool wasStunned = false;
            for (int i = 0; i < 600; i++)
            {
                w.Step();
                float stun = w.Em.GetComponentData<Fighter>(enemy).HitStun;
                if (stun > 0f) wasStunned = true;
                else if (wasStunned) break;
            }
            Assert.That(wasStunned, Is.True, "技が当たっていない");
            w.Step();
            var after = w.Em.GetComponentData<EnemyBrain>(enemy);
            if (expectNoWait) Assert.That(after.Cooldown <= 0f || after.State == EnemyState.Windup, Is.True, $"待ちが残っている: {after.Cooldown}");
            else Assert.That(after.Cooldown, Is.GreaterThan(1f), "倍率1では待ちはそのまま残る");
        }
    }
}
