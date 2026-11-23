using System.Collections.Generic;
using System.Text;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using NUnit.Framework;
using Unity.Mathematics;

namespace CutKungfu.Tests
{
    /// <summary>
    /// 盤面B(敵3体の包囲)で、範囲技を重ねれば1回の技で全滅できてしまわないか。
    /// 全滅できると、硬直で殴り返す相手が残らず、条件に関係なく「置けるだけ置く」が最善になって
    /// 主指標(平均ポーズ数)が動かなくなる。ゲームロジック一式(吹き飛ばし・命中補正込み)で実際に流して確かめる。
    /// </summary>
    public class SurroundBalanceTests
    {
        const BrickKind P = BrickKind.Palm;
        const BrickKind S = BrickKind.Spin;

        /// <summary>交戦距離で三方から囲まれた状態(敵は 90°/210°/330°)。</summary>
        static GameplayTestWorld Surrounded()
        {
            float r = Tuning.Default.EnemyEngageRange;
            float3 At(float deg) => new float3(math.cos(math.radians(deg)) * r, 0f, math.sin(math.radians(deg)) * r);
            return new GameplayTestWorld(float3.zero, At(90f), At(210f), At(330f));
        }

        static readonly object[] AreaStacks =
        {
            new object[] { "回し蹴り×3(開始時のゲージ50で撃てる)", 50f, new[] { S, S, S } },
            new object[] { "回し蹴り×4", 60f, new[] { S, S, S, S } },
            new object[] { "4個技に回し蹴り2発(回→掌→回→掌)", 60f, new[] { S, P, S, P } },
            new object[] { "4個技に回し蹴り3発", 60f, new[] { S, S, S, P } },
        };

        [TestCaseSource(nameof(AreaStacks))]
        public void StackingSpinsAtOneSpot_CannotWipeTheSurround(string label, float gauge, BrickKind[] bricks)
        {
            using var w = Surrounded();
            w.Step();
            w.SetGauge(gauge);

            var poses = new (BrickKind, float3)[bricks.Length];
            for (int i = 0; i < bricks.Length; i++) poses[i] = (bricks[i], float3.zero);
            w.PerformCut(poses);
            Assert.That(w.Em.GetComponentData<CutState>(w.Player).Phase, Is.EqualTo(CutPhase.Executing), "解除できていない(ゲージ不足など)");

            // 技の実行が終わるまで回す
            for (int i = 0; i < 300 && w.Em.GetComponentData<CutState>(w.Player).Phase == CutPhase.Executing; i++) w.Step();
            int alive = w.AliveEnemies();

            var log = new StringBuilder($"{label}: 生存 {alive}/3, 残り体力 [");
            foreach (var e in w.Enemies()) log.Append(w.Em.GetComponentData<Fighter>(e).Health.ToString("0.0")).Append(' ');
            log.Append(']');
            TestContext.WriteLine(log.ToString());

            Assert.That(alive, Is.GreaterThan(0), $"1回の技で包囲を全滅できてしまう — {log}");
        }

        const BrickKind T = BrickKind.TableLanding;
        const BrickKind F = BrickKind.FlyingKick;

        /// <summary>包囲の位置(机との位置関係)。敵は交戦距離で、指定の角度に立つ。</summary>
        public sealed class SurroundSetup
        {
            public string Name;
            public float3 Player;
            public float[] EnemyAngles;
            public override string ToString() => Name;
        }

        static readonly SurroundSetup[] Setups =
        {
            // 机(x 1.8〜4.2, z 0.2〜1.4)の左脇。机上の着地の範囲(半径2.0)に3体とも入る
            new SurroundSetup { Name = "机の左脇", Player = new float3(1.2f, 0f, 0.8f), EnemyAngles = new[] { 90f, 210f, 330f } },
            // 机の手前。机の上からは手前の1体に届きにくい
            new SurroundSetup { Name = "机の手前", Player = new float3(3f, 0f, -1.0f), EnemyAngles = new[] { 180f, 270f, 0f } },
            // 机に届かない(到達6mの外)。机上の着地は置けない
            new SurroundSetup { Name = "机に届かない", Player = new float3(-6f, 0f, 0f), EnemyAngles = new[] { 90f, 210f, 330f } },
        };

        static IEnumerable<TestCaseData> MixedCases()
        {
            foreach (var setup in Setups)
            foreach (var gauge in new[] { 50f, 60f })
                yield return new TestCaseData(setup, gauge).SetName($"MixedAreaCombos_CannotWipeTheSurround({setup.Name}, ゲージ{gauge})");
        }

        /// <summary>
        /// 回し蹴り・机上の着地・飛び蹴りを2〜4個並べたすべての組み合わせ(3^2+3^3+3^4)を流し、1回の技で全滅できないかを確かめる。
        /// 置き場所は、回し蹴り=自分の位置、机上の着地=天板で自分に最も近い点、飛び蹴り=敵を順に狙う。
        /// ゲージで置ける数を超える組み合わせは流さない(ゲージ50なら3個まで)。
        /// </summary>
        [TestCaseSource(nameof(MixedCases))]
        public void MixedAreaCombos_CannotWipeTheSurround(SurroundSetup setup, float gauge)
        {
            var table = GameplayTestWorld.StageTable;
            int affordable = CutRules.AffordablePoses(Tuning.Default, gauge - Tuning.Default.SlowFixedCost, 0);
            var wipes = new List<string>();
            int runs = 0;

            foreach (var seq in Sequences(new[] { S, T, F }, 2, affordable))
            {
                using var w = SurroundedAt(setup);
                w.AddTable(table);
                w.Step();
                w.SetGauge(gauge);

                w.PerformCut(PosesFor(setup, seq));
                var cut = w.Em.GetComponentData<CutState>(w.Player);
                if (cut.Phase != CutPhase.Executing) continue; // 1個も置けなかった
                string placed = BrickSequence.ToCodeString(cut.Record.Sequence);
                runs++;

                for (int i = 0; i < 300 && w.Em.GetComponentData<CutState>(w.Player).Phase == CutPhase.Executing; i++) w.Step();
                if (w.AliveEnemies() > 0) continue;

                var hp = new StringBuilder();
                foreach (var e in w.Enemies()) hp.Append(w.Em.GetComponentData<Fighter>(e).Health.ToString("0.0")).Append(' ');
                wipes.Add($"{placed} (残り体力 {hp.ToString().Trim()})");
            }

            TestContext.WriteLine($"{setup.Name} ゲージ{gauge}: {runs} 通り流して全滅 {wipes.Count} 通り");
            Assert.That(runs, Is.GreaterThan(0));
            Assert.That(wipes, Is.Empty, $"{setup.Name}・ゲージ{gauge}で、1回の技で包囲を全滅できる組み合わせ:\n" + string.Join("\n", wipes));
        }

        static IEnumerable<TestCaseData> PalmMixedCases()
        {
            foreach (var setup in Setups)
            foreach (var gauge in new[] { 50f, 60f })
                yield return new TestCaseData(setup, gauge).SetName($"AreaThenPalmFinish_CannotWipeTheSurround({setup.Name}, ゲージ{gauge})");
        }

        /// <summary>
        /// 範囲技で削って単体技(掌底・飛び蹴り)でとどめを刺す並び。1〜2体を倒すのは想定どおりで許容し、
        /// 1回の技で3体全滅できないことだけを確かめる。並びは {回し蹴り, 机上の着地, 飛び蹴り, 掌底} の2〜4個で、
        /// 掌底と範囲技(回し蹴りか机上の着地)を少なくとも1つずつ含むもの。
        /// 掌底は狙う敵の手前(自分側へ 0.7m)に置き、敵を順に狙う。
        /// </summary>
        [TestCaseSource(nameof(PalmMixedCases))]
        public void AreaThenPalmFinish_CannotWipeTheSurround(SurroundSetup setup, float gauge)
        {
            var table = GameplayTestWorld.StageTable;
            int affordable = CutRules.AffordablePoses(Tuning.Default, gauge - Tuning.Default.SlowFixedCost, 0);
            var wipes = new List<string>();
            var survivors = new int[4];
            int runs = 0;
            float minRemainingWhenNotWiped = float.MaxValue;

            foreach (var seq in Sequences(new[] { S, T, F, P }, 2, affordable))
            {
                bool hasPalm = false, hasArea = false;
                foreach (var k in seq) { hasPalm |= k == P; hasArea |= CutRules.IsAreaBrick(k); }
                if (!hasPalm || !hasArea) continue;

                using var w = SurroundedAt(setup);
                w.AddTable(table);
                w.Step();
                w.SetGauge(gauge);

                w.PerformCut(PosesFor(setup, seq));
                var cut = w.Em.GetComponentData<CutState>(w.Player);
                if (cut.Phase != CutPhase.Executing) continue;
                string placed = BrickSequence.ToCodeString(cut.Record.Sequence);
                runs++;
                for (int i = 0; i < 300 && w.Em.GetComponentData<CutState>(w.Player).Phase == CutPhase.Executing; i++) w.Step();

                int alive = w.AliveEnemies();
                survivors[math.clamp(alive, 0, 3)]++;
                var hp = new StringBuilder();
                float remaining = 0f;
                foreach (var e in w.Enemies())
                {
                    float h = w.Em.GetComponentData<Fighter>(e).Health;
                    hp.Append(h.ToString("0.0")).Append(' ');
                    if (h > 0f) remaining += h;
                }
                if (alive == 0) wipes.Add($"{placed} (残り体力 {hp.ToString().Trim()})");
                else minRemainingWhenNotWiped = math.min(minRemainingWhenNotWiped, remaining);
            }

            string summary = $"{setup.Name} ゲージ{gauge}: {runs} 通り / 生存0体 {survivors[0]}・1体 {survivors[1]}・2体 {survivors[2]}・3体 {survivors[3]}"
                             + (runs > survivors[0] ? $" / 全滅しなかった中で残り体力の合計の最小 {minRemainingWhenNotWiped:0.0}" : "")
                             + (wipes.Count > 0 ? "\n全滅:\n" + string.Join("\n", wipes) : "");
            TestContext.WriteLine(summary);
            Assert.That(runs, Is.GreaterThan(0));
            Assert.That(wipes, Is.Empty, "1回の技で包囲を全滅できる並び:\n" + summary);
        }

        /// <summary>範囲技1発+単体技3発で全滅できる既知の5通り(AreaThenPalmFinish_CannotWipeTheSurround の Ignore 2ケース)。</summary>
        static readonly (int setup, BrickKind[] seq)[] KnownWipes =
        {
            (0, new[] { S, F, P, F }),
            (0, new[] { S, F, P, P }),
            (1, new[] { S, F, P, F }),
            (1, new[] { S, F, P, P }),
            (1, new[] { T, F, P, P }),
        };

        const int PressureSeeds = 20;

        /// <summary>
        /// 計測用(合否なし): 既知の全滅5通りを、敵が技の実行中に攻撃してくる条件で流す。
        /// 回帰テストの設定では、ウェーブ開始直後の敵は最初の攻撃まで 2.4 秒以上かかり、実行(約1秒)中には届かない。
        /// ここでは技を出す直前に、各敵の攻撃開始の時刻を乱数の種ごとに振る(種は条件ごとに 20 通り)。
        /// 条件「待ちのみ」: 予備動作に入るまでの待ち(EnemyBrain.Cooldown)を 0〜EnemyCooldownMax。
        /// 条件「予備動作中を含む」: 攻撃開始の時刻を -EnemyWindup〜EnemyCooldownMax で振り、負なら技を出す時点ですでに予備動作中
        /// (残り EnemyWindup+時刻)。同時に予備動作に入れる数(2)は守る。予備動作の長さ(0.9 秒)はそのまま。ゲージは 60(4個技)。
        /// </summary>
        [Explicit("計測用: 既知の全滅5通りを、敵の攻撃開始を乱数で振って流す(合否なし)")]
        [Test]
        public void Measure_KnownWipes_UnderEnemyPressure()
        {
            var tuning = Tuning.Default;
            var report = new StringBuilder();
            report.Append("配置 | 並び | 基準(回帰テストと同じ) | 出し切って全滅 | 途中で中断 | 出し切ったが全滅せず | 置けず | 実行中に予備動作 | 実行中の被弾\n");

            foreach (bool includeWindups in new[] { false, true })
            {
            report.Append(includeWindups ? "\n[条件: 予備動作中を含む]\n" : "[条件: 待ちのみ]\n");
            foreach (var (setupIndex, seq) in KnownWipes)
            {
                var setup = Setups[setupIndex];
                string label = BrickSequence.ToCodeString(Pack(seq));
                var baseline = RunUnderPressure(setup, seq, seed: 0, includeWindups, tuning);
                int wiped = 0, interrupted = 0, survived = 0, unplaced = 0, windups = 0, hits = 0;
                for (int seed = 1; seed <= PressureSeeds; seed++)
                {
                    var r = RunUnderPressure(setup, seq, seed, includeWindups, tuning);
                    if (!r.Placed) { unplaced++; continue; }
                    if (r.Interrupted) interrupted++;
                    else if (r.Alive == 0) wiped++;
                    else survived++;
                    if (r.WindupDuringExecution) windups++;
                    if (r.HitsDuringExecution > 0) hits++;
                }
                report.Append($"{setup.Name} | {label} | {baseline.Outcome} | {wiped} | {interrupted} | {survived} | {unplaced} | {windups}/{PressureSeeds} | {hits}/{PressureSeeds}\n");
            }
            }

            TestContext.WriteLine(report.ToString());
            UnityEngine.Debug.Log("[PRESSURE]\n" + report);
            Assert.Pass(report.ToString());
        }

        /// <summary>
        /// 計測用(合否なし): 対策の候補を格子で比べる。既知の全滅5通り×種20通りを「予備動作中を含む」条件で流し、
        /// 調整値を上書きする: 範囲技で当てた敵の被弾硬直(EnemyAreaHitStun)× 4個技の威力倍率(PowerByPoseCount.w)。
        /// あわせて、各格子で前提が崩れないかを確かめる。
        /// </summary>
        [Explicit("計測用: 範囲技の被弾硬直×4個技の倍率の格子で、既知の全滅5通りを流す(合否なし)")]
        [Test]
        public void Measure_KnownWipes_CountermeasureGrid()
        {
            var report = new StringBuilder();
            report.Append("範囲技の被弾硬直 | 4個技の倍率 | 出し切って全滅 | 途中で中断 | 出し切ったが全滅せず | 全滅率 | 散らした4個で全滅できない | 1体に集中で3個撃破 | 飛び蹴りでとどめ | 4個技>3個技 | 全滅の内訳(配置:並び=件数)\n");
            foreach (float areaStun in new[] { 0.8f, 0.3f, 0f })
            foreach (float power4 in new[] { 3.5f, 3.0f, 2.8f })
            {
                var tuning = Tuning.Default;
                tuning.EnemyAreaHitStun = areaStun;
                tuning.PowerByPoseCount.w = power4;

                int wiped = 0, interrupted = 0, survived = 0, runs = 0;
                var breakdown = new StringBuilder();
                foreach (var (setupIndex, seq) in KnownWipes)
                {
                int wipedHere = 0;
                for (int seed = 1; seed <= PressureSeeds; seed++)
                {
                    var r = RunUnderPressure(Setups[setupIndex], seq, seed, includeWindups: true, tuning);
                    if (!r.Placed) continue;
                    runs++;
                    if (r.Interrupted) interrupted++;
                    else if (r.Alive == 0) { wiped++; wipedHere++; }
                    else survived++;
                }
                if (wipedHere > 0) breakdown.Append($"{Setups[setupIndex].Name}:{BrickSequence.ToCodeString(Pack(seq))}={wipedHere} ");
                }

                var p = CheckPremises(tuning);
                report.Append($"{areaStun:0.0} | {power4:0.0} | {wiped} | {interrupted} | {survived} | {(runs > 0 ? 100f * wiped / runs : 0f):0}% | ")
                      .Append($"{Mark(p.SpreadCannotWipe)} | {Mark(p.FocusThreeKills)} | {Mark(p.FlyingKickFinishes)} | {Mark(p.FourBeatsThree)} | {breakdown.ToString().Trim()}\n");
            }

            TestContext.WriteLine(report.ToString());
            UnityEngine.Debug.Log("[GRID]\n" + report);
            Assert.Pass(report.ToString());
        }

        static string Mark(bool ok) => ok ? "保つ" : "崩れる";

        struct Premises
        {
            public bool SpreadCannotWipe;
            public bool FocusThreeKills;
            public bool FlyingKickFinishes;
            public bool FourBeatsThree;
        }

        /// <summary>格子の値で、守るべき前提を確かめる(ルールの計算と、飛び蹴りのとどめはゲームロジック一式で)。</summary>
        static Premises CheckPremises(in Tuning t)
        {
            float hp = t.EnemyMaxHealth;
            float focus3 = 0f;
            for (int k = 0; k < 3; k++) focus3 += CutRules.StrikeDamage(t, BrickKind.Palm, 3, repeatIndex: k);
            // 4個目を置く得: 別々のブリック(繰り返しの減衰なし)で、4個技の合計が3個技の合計を上回る
            float four = 0f, three = 0f;
            var kinds = new[] { BrickKind.Palm, BrickKind.FlyingKick, BrickKind.Spin, BrickKind.TableLanding };
            for (int i = 0; i < 4; i++) four += CutRules.StrikeDamage(t, kinds[i], 4);
            for (int i = 0; i < 3; i++) three += CutRules.StrikeDamage(t, kinds[i], 3);
            return new Premises
            {
                SpreadCannotWipe = CutRules.StrikeDamage(t, BrickKind.Palm, 4) < hp,
                FocusThreeKills = focus3 >= hp,
                FlyingKickFinishes = FlyingKickFinishesAfterAreaCap(t),
                FourBeatsThree = four > three,
            };
        }

        /// <summary>回し蹴り×3で上限まで削った単体の敵に、飛び蹴りでとどめを刺せるか(FlyingKick_FinishesAWeakenedSingleEnemy と同じ流れ)。</summary>
        static bool FlyingKickFinishesAfterAreaCap(in Tuning t)
        {
            float3 enemyAt = new float3(2.5f, 0f, 0f);
            using var w = new GameplayTestWorld(float3.zero, enemyAt);
            var ts = w.Singleton<TuningSingleton>();
            ts.Value = t;
            w.SetSingleton(ts);
            w.Step();
            w.SetGauge(60f);
            w.PerformCut((S, float3.zero), (S, float3.zero), (S, float3.zero), (F, enemyAt));
            for (int i = 0; i < 300 && w.Em.GetComponentData<CutState>(w.Player).Phase == CutPhase.Executing; i++) w.Step();
            return w.AliveEnemies() == 0;
        }

        struct PressureResult
        {
            public bool Placed;
            public bool Interrupted;
            public int Alive;
            public bool WindupDuringExecution;
            public int HitsDuringExecution;
            public string Health;

            public string Outcome => (!Placed ? "置けず" : Interrupted ? "途中で中断" : Alive == 0 ? "出し切って全滅" : $"出し切ったが {Alive} 体残る") + $" [{Health}]";
        }

        static PressureResult RunUnderPressure(SurroundSetup setup, BrickKind[] seq, int seed, bool includeWindups, in Tuning tuning)
        {
            using var w = SurroundedAt(setup);
            w.AddTable(GameplayTestWorld.StageTable);
            var ts = w.Singleton<TuningSingleton>();
            ts.Value = tuning;
            w.SetSingleton(ts);
            w.Step();
            w.SetGauge(60f);

            if (seed > 0)
            {
                var rng = Unity.Mathematics.Random.CreateFromIndex((uint)(seed + (includeWindups ? 1000 : 0)));
                int windupsSet = 0;
                foreach (var e in w.Enemies())
                {
                    var brain = w.Em.GetComponentData<EnemyBrain>(e);
                    float start = rng.NextFloat(includeWindups ? -tuning.EnemyWindup : 0f, tuning.EnemyCooldownMax);
                    if (start < 0f && windupsSet < tuning.EnemyMaxConcurrentWindups)
                    {
                        brain.State = EnemyState.Windup;
                        brain.Timer = tuning.EnemyWindup + start;
                        windupsSet++;
                    }
                    else
                    {
                        brain.Cooldown = math.max(0f, start);
                    }
                    w.Em.SetComponentData(e, brain);
                }
            }

            w.PerformCut(PosesFor(setup, seq));
            var result = new PressureResult();
            var cut = w.Em.GetComponentData<CutState>(w.Player);
            result.Placed = cut.Phase == CutPhase.Executing && cut.Record.Sequence == Pack(seq);
            if (!result.Placed) return result;

            for (int i = 0; i < 300 && w.Em.GetComponentData<CutState>(w.Player).Phase == CutPhase.Executing; i++)
            {
                w.Step();
                foreach (var e in w.Enemies())
                    if (w.Em.GetComponentData<EnemyBrain>(e).State == EnemyState.Windup) result.WindupDuringExecution = true;
            }

            foreach (var t in w.Em.GetBuffer<TelemetryEvent>(w.Em.CreateEntityQuery(typeof(TelemetryEvent)).GetSingletonEntity()))
            {
                if (t.Value.Kind == PlaytestEventKind.PlayerHit) result.HitsDuringExecution++;
                if (t.Value.Kind == PlaytestEventKind.Cut && t.Value.Interrupted) result.Interrupted = true;
            }
            result.Alive = w.AliveEnemies();
            var hp = new StringBuilder();
            foreach (var e in w.Enemies()) hp.Append(w.Em.GetComponentData<Fighter>(e).Health.ToString("0.0")).Append(" ");
            result.Health = hp.ToString().Trim();
            return result;
        }

        static int Pack(BrickKind[] seq)
        {
            int packed = 0;
            foreach (var k in seq) packed = BrickSequence.Append(packed, k);
            return packed;
        }

        /// <summary>
        /// 探索で使う置き場所の規則: 回し蹴り=自分の位置、机上の着地=天板で自分に最も近い点、
        /// 飛び蹴り=敵を順に狙う、掌底=狙う敵の手前(自分側へ 0.7m)で敵を順に狙う。
        /// </summary>
        static (BrickKind, float3)[] PosesFor(SurroundSetup setup, BrickKind[] seq)
        {
            var table = GameplayTestWorld.StageTable;
            var spawns = EnemySpawns(setup);
            float2 tablePoint = math.clamp(setup.Player.xz, table.Center - table.HalfExtents + 0.2f, table.Center + table.HalfExtents - 0.2f);
            var poses = new (BrickKind, float3)[seq.Length];
            int flyTarget = 0, palmTarget = 0;
            for (int i = 0; i < seq.Length; i++)
            {
                if (seq[i] == P)
                {
                    float3 enemy = spawns[palmTarget++ % spawns.Length];
                    poses[i] = (P, enemy - math.normalizesafe(enemy - setup.Player) * 0.7f);
                    continue;
                }
                poses[i] = seq[i] switch
                {
                    S => (S, setup.Player),
                    T => (T, new float3(tablePoint.x, table.TopY, tablePoint.y)),
                    _ => (F, spawns[flyTarget++ % spawns.Length]),
                };
            }
            return poses;
        }

        static GameplayTestWorld SurroundedAt(SurroundSetup setup) => new GameplayTestWorld(setup.Player, EnemySpawns(setup));

        static float3[] EnemySpawns(SurroundSetup setup)
        {
            float r = Tuning.Default.EnemyEngageRange;
            var spawns = new float3[setup.EnemyAngles.Length];
            for (int i = 0; i < spawns.Length; i++)
            {
                float a = math.radians(setup.EnemyAngles[i]);
                spawns[i] = setup.Player + new float3(math.cos(a) * r, 0f, math.sin(a) * r);
            }
            return spawns;
        }

        /// <summary>kinds から長さ min〜max の並びをすべて作る(重複あり、順序あり)。</summary>
        static IEnumerable<BrickKind[]> Sequences(BrickKind[] kinds, int min, int max)
        {
            for (int len = min; len <= max; len++)
            {
                var idx = new int[len];
                while (true)
                {
                    var seq = new BrickKind[len];
                    for (int i = 0; i < len; i++) seq[i] = kinds[idx[i]];
                    yield return seq;
                    int k = len - 1;
                    while (k >= 0 && ++idx[k] == kinds.Length) { idx[k] = 0; k--; }
                    if (k < 0) break;
                }
            }
        }

        [Test]
        public void AreaCap_IsShownAndCountedInTheCutRecord()
        {
            var setup = Setups[0]; // 机の左脇
            using var w = SurroundedAt(setup);
            w.AddTable(GameplayTestWorld.StageTable);
            w.Step();
            w.SetGauge(60f);
            var table = GameplayTestWorld.StageTable;
            float2 tp = math.clamp(setup.Player.xz, table.Center - table.HalfExtents + 0.2f, table.Center + table.HalfExtents - 0.2f);
            w.PerformCut((T, new float3(tp.x, table.TopY, tp.y)), (S, setup.Player), (S, setup.Player), (S, setup.Player));

            bool cappedFeedback = false;
            for (int i = 0; i < 300 && w.Em.GetComponentData<CutState>(w.Player).Phase == CutPhase.Executing; i++)
            {
                w.Step();
                foreach (var f in w.Em.GetBuffer<FeedbackEvent>(w.Em.CreateEntityQuery(typeof(FeedbackEvent)).GetSingletonEntity()))
                    if (f.Kind == FeedbackKind.CutHitCapped) cappedFeedback = true;
                w.Em.GetBuffer<FeedbackEvent>(w.Em.CreateEntityQuery(typeof(FeedbackEvent)).GetSingletonEntity()).Clear();
            }
            Assert.That(cappedFeedback, Is.True, "上限に掛かった打撃はプレイヤーに分かる出来事として出る");
            Assert.That(w.Em.GetComponentData<CutState>(w.Player).Record.AreaCapHits, Is.GreaterThan(0));
            Assert.That(w.AliveEnemies(), Is.GreaterThan(0));
        }

        /// <summary>
        /// 飛び蹴りは、到達点で狙った敵への打撃は上限の対象外。体力の減った単体の敵には、
        /// 1発で(範囲技で上限まで削った後でも)とどめを刺せる。
        /// </summary>
        [TestCase("飛び蹴り1発で、体力15の敵を倒す", 15f, 50f, new[] { F })]
        [TestCase("回し蹴り×3で上限(72)まで削ってから、飛び蹴りでとどめ", 90f, 60f, new[] { S, S, S, F })]
        public void FlyingKick_FinishesAWeakenedSingleEnemy(string label, float enemyHealth, float gauge, BrickKind[] bricks)
        {
            float3 enemyAt = new float3(2.5f, 0f, 0f);
            using var w = new GameplayTestWorld(float3.zero, enemyAt);
            w.Step();
            w.SetGauge(gauge);
            var enemy = w.Enemies()[0];
            var f = w.Em.GetComponentData<Fighter>(enemy);
            f.Health = enemyHealth;
            w.Em.SetComponentData(enemy, f);

            var poses = new (BrickKind, float3)[bricks.Length];
            for (int i = 0; i < bricks.Length; i++) poses[i] = (bricks[i], bricks[i] == F ? enemyAt : float3.zero);
            w.PerformCut(poses);
            for (int i = 0; i < 300 && w.Em.GetComponentData<CutState>(w.Player).Phase == CutPhase.Executing; i++) w.Step();

            Assert.That(w.AliveEnemies(), Is.Zero, label);
        }

        /// <summary>
        /// 1回の技で3体撃破した件数は、CSV の kind=cut 行の kills 列で数えられる。
        /// 包囲の3体を体力10まで減らしておき、回し蹴り1発(16)で3体とも倒して確かめる。
        /// </summary>
        [Test]
        public void ThreeKillCut_IsRecordedAsKillsThree()
        {
            var setup = Setups[2]; // 机に届かない包囲
            using var w = SurroundedAt(setup);
            w.Step();
            foreach (var e in w.Enemies())
            {
                var f = w.Em.GetComponentData<Fighter>(e);
                f.Health = 10f;
                w.Em.SetComponentData(e, f);
            }
            w.PerformCut((S, setup.Player));

            PlaytestEvent cutRow = default;
            bool found = false;
            for (int i = 0; i < 600 && !found; i++)
            {
                w.Step();
                foreach (var t in w.Em.GetBuffer<TelemetryEvent>(w.Em.CreateEntityQuery(typeof(TelemetryEvent)).GetSingletonEntity()))
                    if (t.Value.Kind == PlaytestEventKind.Cut) { cutRow = t.Value; found = true; }
            }
            Assert.That(found, Is.True, "技の記録が出ていない");
            Assert.That(cutRow.Kills, Is.EqualTo(3));
            Assert.That(cutRow.EnemiesAlive, Is.EqualTo(3));
            StringAssert.Contains(",3,", PlaytestCsv.ToRow(cutRow));
        }

        [Test]
        public void PlacedPoses_RecordHowManyTimesTheBrickWasAlreadyUsed()
        {
            using var w = Surrounded();
            w.Step();
            w.SetGauge(60f);
            var enter = GameplayTestWorld.Neutral();
            enter.Cut = true;
            w.Step(enter);
            foreach (var kind in new[] { S, P, S, S })
            {
                var select = GameplayTestWorld.Neutral();
                select.SelectBrick = (int)kind;
                w.Step(select);
                var place = GameplayTestWorld.Neutral();
                place.AimStick = new float2(0.3f, 0f);
                place.Primary = true;
                w.Step(place);
            }
            var poses = w.Em.GetBuffer<PlacedPose>(w.Player);
            Assert.That(poses.Length, Is.EqualTo(4));
            Assert.That(new[] { poses[0].RepeatIndex, poses[1].RepeatIndex, poses[2].RepeatIndex, poses[3].RepeatIndex },
                Is.EqualTo(new[] { 0, 0, 1, 2 }));
        }
    }
}
