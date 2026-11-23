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
