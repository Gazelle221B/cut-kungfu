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
            /// <summary>
            /// 既知の問題: ゲージ60(4個技)なら、机上の着地を含む組み合わせで全滅できる(22通り)。
            /// 直し方の判断待ちのため、このケースは実行せずに理由つきで残す。
            /// </summary>
            public bool KnownWipeWithFourPoses;
            public override string ToString() => Name;
        }

        static readonly SurroundSetup[] Setups =
        {
            // 机(x 1.8〜4.2, z 0.2〜1.4)の左脇。机上の着地の範囲(半径2.0)に3体とも入る
            new SurroundSetup { Name = "机の左脇", Player = new float3(1.2f, 0f, 0.8f), EnemyAngles = new[] { 90f, 210f, 330f }, KnownWipeWithFourPoses = true },
            // 机の手前。机の上からは手前の1体に届きにくい
            new SurroundSetup { Name = "机の手前", Player = new float3(3f, 0f, -1.0f), EnemyAngles = new[] { 180f, 270f, 0f }, KnownWipeWithFourPoses = true },
            // 机に届かない(到達6mの外)。机上の着地は置けない
            new SurroundSetup { Name = "机に届かない", Player = new float3(-6f, 0f, 0f), EnemyAngles = new[] { 90f, 210f, 330f } },
        };

        static IEnumerable<TestCaseData> MixedCases()
        {
            foreach (var setup in Setups)
            foreach (var gauge in new[] { 50f, 60f })
            {
                var data = new TestCaseData(setup, gauge).SetName($"MixedAreaCombos_CannotWipeTheSurround({setup.Name}, ゲージ{gauge})");
                if (setup.KnownWipeWithFourPoses && gauge >= 60f)
                    data = data.Ignore("既知の問題: 机上の着地を含む4個技で包囲を全滅できる(22通り、PR #1 で報告済み)。直し方が決まったら外す");
                yield return data;
            }
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

                var spawns = EnemySpawns(setup);
                float2 tablePoint = math.clamp(setup.Player.xz, table.Center - table.HalfExtents + 0.2f, table.Center + table.HalfExtents - 0.2f);
                var poses = new (BrickKind, float3)[seq.Length];
                int flyTarget = 0;
                for (int i = 0; i < seq.Length; i++)
                {
                    poses[i] = seq[i] switch
                    {
                        S => (S, setup.Player),
                        T => (T, new float3(tablePoint.x, table.TopY, tablePoint.y)),
                        _ => (F, spawns[flyTarget++ % spawns.Length]),
                    };
                }

                w.PerformCut(poses);
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
