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
        /// 1回流す。技を出す直前に各敵の攻撃開始を乱数で振る(-EnemyWindup〜EnemyCooldownMax、負なら予備動作中。同時に2体まで)。
        /// </summary>
        static Outcome Run(Plan plan, int seed, in Tuning tuning, bool commitEnabled)
        {
            using var w = new GameplayTestWorld(Player, Enemy(0), Enemy(1), Enemy(2));
            var ts = w.Singleton<TuningSingleton>();
            ts.Value = tuning;
            w.SetSingleton(ts);
            var session = w.Singleton<SessionState>();
            session.CommitEnabled = commitEnabled;
            w.SetSingleton(session);
            w.Step();
            w.SetGauge(60f);

            var rng = Random.CreateFromIndex((uint)(seed + 5000));
            int windups = 0;
            foreach (var e in w.Enemies())
            {
                var brain = w.Em.GetComponentData<EnemyBrain>(e);
                float start = rng.NextFloat(-tuning.EnemyWindup, tuning.EnemyCooldownMax);
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

            var poses = new (BrickKind, float3)[plan.Poses.Length];
            for (int i = 0; i < poses.Length; i++) poses[i] = (plan.Poses[i].kind, At(plan.Poses[i].kind, plan.Poses[i].target));
            w.PerformCut(poses);
            var cut = w.Em.GetComponentData<CutState>(w.Player);
            if (cut.Phase != CutPhase.Executing || cut.PoseCount != poses.Length) return Outcome.NotPlaced;

            bool hitInCommit = false;
            for (int i = 0; i < 900; i++)
            {
                var c = w.Em.GetComponentData<CutState>(w.Player);
                if (c.Phase == CutPhase.Commit && c.Record.HitDuringCommit) hitInCommit = true;
                if (!c.IsActive || w.Singleton<SessionState>().Phase != SessionPhase.Playing) break;
                w.Step();
            }

            foreach (var t in w.Em.GetBuffer<TelemetryEvent>(w.Em.CreateEntityQuery(typeof(TelemetryEvent)).GetSingletonEntity()))
            {
                if (t.Value.Kind == PlaytestEventKind.Cut && t.Value.Interrupted) return Outcome.HitDuringExecution;
                if (t.Value.Kind == PlaytestEventKind.Cut && t.Value.HitDuringCommit) hitInCommit = true;
            }
            return hitInCommit ? Outcome.HitDuringCommit : Outcome.NotHit;
        }

        /// <summary>置く数ごとの件数(実行中に被弾 / 硬直中に被弾 / 被弾なし)。各置く数 3並び×種 Seeds 通り。</summary>
        static int[,] Measure(in Tuning tuning, bool commitEnabled)
        {
            var counts = new int[4, 4];
            for (int n = 0; n < 4; n++)
            foreach (var plan in PlansByCount[n])
            for (int seed = 1; seed <= Seeds; seed++)
                counts[n, (int)Run(plan, seed, tuning, commitEnabled)]++;
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

        /// <summary>計測用(合否なし): 現状の調整値で、置く数ごとの被弾を測る(条件A=コミット時間あり)。</summary>
        [Explicit("計測用: 包囲で置く数ごとの、実行中・硬直中の被弾を測る(合否なし)")]
        [Test]
        public void Measure_CommitPunishmentByPoseCount()
        {
            var sb = new StringBuilder("[条件A・現状の調整値]\n");
            AppendTable(sb, Measure(Tuning.Default, commitEnabled: true));
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
