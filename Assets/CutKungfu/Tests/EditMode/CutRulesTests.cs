using CutKungfu.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace CutKungfu.Tests
{
    /// <summary>GDD 6章の表(ポーズ数ごとの倍率・硬直・消費)がそのまま実装されているか。</summary>
    public class CutRulesTests
    {
        static Tuning T => Tuning.Default;

        [TestCase(1, 1.0f)]
        [TestCase(2, 1.6f)]
        [TestCase(3, 2.4f)]
        [TestCase(4, 3.5f)]
        public void PowerMultiplier_FollowsGddTable(int poses, float expected)
            => Assert.That(CutRules.PowerMultiplier(T, poses), Is.EqualTo(expected).Within(1e-5f));

        [TestCase(1, 0.5f)]
        [TestCase(2, 0.8f)]
        [TestCase(3, 1.2f)]
        [TestCase(4, 1.8f)]
        public void CommitTime_IsFixedPlusPerCount(int poses, float expected)
            => Assert.That(CutRules.CommitTime(T, poses, commitEnabled: true), Is.EqualTo(expected).Within(1e-5f));

        [Test]
        public void CommitTime_IsZero_WhenConditionOff()
        {
            for (int n = 0; n <= 4; n++) Assert.That(CutRules.CommitTime(T, n, commitEnabled: false), Is.Zero);
        }

        [Test]
        public void CommitTime_ScalesForExtremeCondition()
        {
            var t = T;
            t.CommitScale = 2f;
            Assert.That(CutRules.CommitTime(t, 4, true), Is.EqualTo(3.6f).Within(1e-5f));
        }

        [Test]
        public void Multiplier_AndCommit_BothIncreaseMonotonically()
        {
            for (int n = 1; n < 4; n++)
            {
                Assert.That(CutRules.PowerMultiplier(T, n + 1), Is.GreaterThan(CutRules.PowerMultiplier(T, n)));
                Assert.That(CutRules.CommitTime(T, n + 1, true), Is.GreaterThan(CutRules.CommitTime(T, n, true)));
            }
        }

        [TestCase(1, 30f)]
        [TestCase(4, 60f)]
        public void TotalCost_IncludesFixedCost(int poses, float expected)
            => Assert.That(CutRules.TotalCost(T, poses), Is.EqualTo(expected).Within(1e-5f));

        [Test]
        public void CanEnterSlow_NeedsFixedCostPlusOnePose()
        {
            Assert.That(CutRules.CanEnterSlow(T, 29.9f), Is.False);
            Assert.That(CutRules.CanEnterSlow(T, 30f), Is.True);
        }

        [Test]
        public void AffordablePoses_IsLimitedByGaugeAndCap()
        {
            Assert.That(CutRules.AffordablePoses(T, 80f, 0), Is.EqualTo(4));
            Assert.That(CutRules.AffordablePoses(T, 25f, 0), Is.EqualTo(2));
            Assert.That(CutRules.AffordablePoses(T, 80f, 3), Is.EqualTo(1));
        }

        [Test]
        public void Placement_ClampsToBrickReach()
        {
            var r = CutRules.ValidatePlacement(T, BrickKind.Palm, 0, 80f, float3.zero, new float3(10f, 0f, 0f), TableRect.None, out var pos);
            Assert.That(r, Is.EqualTo(PlacementResult.Ok));
            Assert.That(pos.x, Is.EqualTo(T.Palm.Reach).Within(1e-4f));
        }

        [Test]
        public void Placement_RejectsWhenFull_OrOutOfGauge()
        {
            Assert.That(CutRules.ValidatePlacement(T, BrickKind.Palm, 4, 80f, float3.zero, float3.zero, TableRect.None, out _),
                Is.EqualTo(PlacementResult.TooManyPoses));
            Assert.That(CutRules.ValidatePlacement(T, BrickKind.Palm, 1, 5f, float3.zero, float3.zero, TableRect.None, out _),
                Is.EqualTo(PlacementResult.NotEnoughGauge));
        }

        [Test]
        public void TableBrick_OnlyOnTable_AndRaisedToTop()
        {
            var table = new TableRect { Exists = true, Center = new float2(3f, 0f), HalfExtents = new float2(1.2f, 0.6f), TopY = 0.8f };
            Assert.That(CutRules.ValidatePlacement(T, BrickKind.TableLanding, 0, 80f, float3.zero, new float3(-2f, 0, 0), table, out _),
                Is.EqualTo(PlacementResult.RequiresTable));
            var ok = CutRules.ValidatePlacement(T, BrickKind.TableLanding, 0, 80f, float3.zero, new float3(3f, 0, 0), table, out var pos);
            Assert.That(ok, Is.EqualTo(PlacementResult.Ok));
            Assert.That(pos.y, Is.EqualTo(0.8f).Within(1e-5f));
        }

        [Test]
        public void StrikeDamage_UsesPoseCountOfWholeCut()
        {
            float one = CutRules.StrikeDamage(T, BrickKind.Palm, 1);
            float four = CutRules.StrikeDamage(T, BrickKind.Palm, 4);
            Assert.That(four / one, Is.EqualTo(3.5f).Within(1e-4f));
        }

        [Test]
        public void StrikeDamage_RepeatedBrickInSameCut_IsScaledPerRepeat()
        {
            var t = T;
            float first = CutRules.StrikeDamage(t, BrickKind.Spin, 4, repeatIndex: 0);
            Assert.That(CutRules.StrikeDamage(t, BrickKind.Spin, 4, repeatIndex: 1), Is.EqualTo(first * t.RepeatBrickScale).Within(1e-4f));
            Assert.That(CutRules.StrikeDamage(t, BrickKind.Spin, 4, repeatIndex: 2),
                Is.EqualTo(first * t.RepeatBrickScale * t.RepeatBrickScale).Within(1e-4f));
        }

        [Test]
        public void StrikeDamage_RepeatScaleOfOne_DisablesTheRule()
        {
            var t = T;
            t.RepeatBrickScale = 1f;
            Assert.That(CutRules.StrikeDamage(t, BrickKind.Spin, 4, repeatIndex: 3), Is.EqualTo(CutRules.StrikeDamage(t, BrickKind.Spin, 4)).Within(1e-4f));
        }


        [Test]
        public void SegmentDuration_IsClamped()
        {
            Assert.That(CutRules.SegmentDuration(T, 0f), Is.EqualTo(T.CutSegmentMin));
            Assert.That(CutRules.SegmentDuration(T, 100f), Is.EqualTo(T.CutSegmentMax));
        }

        [Test]
        public void StrikeAssist_PullsTowardNearbyEnemy_ButNotFarOnes()
        {
            var c = new float2(0f, 0f);
            var near = CutRules.AssistStrikeCenter(c, 1f, 1.2f, new float2(1.5f, 0f), true);
            Assert.That(near.x, Is.EqualTo(1.2f).Within(1e-5f));
            var far = CutRules.AssistStrikeCenter(c, 1f, 1.2f, new float2(5f, 0f), true);
            Assert.That(far.x, Is.Zero);
            Assert.That(CutRules.AssistStrikeCenter(c, 1f, 0f, new float2(1f, 0f), true).x, Is.Zero);
        }

        /// <summary>
        /// 検証したい判断を壊さないための数値の前提(GDD 6章 盤面B)。
        /// 1体1ポーズに散らした4ポーズ技では誰も倒れない = 硬直中に殴り返される相手が残る。
        /// 1体に集中すれば3ポーズで倒せる = 欲張る価値がある。
        /// </summary>
        [Test]
        public void Balance_SpreadingCannotWipe_ButFocusingKills()
        {
            var t = T;
            float spreadPerEnemy = CutRules.StrikeDamage(t, BrickKind.Palm, 4);
            Assert.That(spreadPerEnemy, Is.LessThan(t.EnemyMaxHealth), "4ポーズを1体ずつに散らしても倒せない");

            // 同じブリックの繰り返しは減衰する。最も不利な「同じ掌底を3発」でも集中すれば倒せることを確かめる
            float focus3 = 0f;
            for (int k = 0; k < 3; k++) focus3 += CutRules.StrikeDamage(t, BrickKind.Palm, 3, repeatIndex: k);
            Assert.That(focus3, Is.GreaterThanOrEqualTo(t.EnemyMaxHealth), "3ポーズを1体に集中すれば倒せる");

            // 2ポーズで最も強い組み合わせ(減衰しない別々のブリック: 掌底+飛び蹴り)でも倒しきれない
            float focus2 = CutRules.StrikeDamage(t, BrickKind.Palm, 2) + CutRules.StrikeDamage(t, BrickKind.FlyingKick, 2);
            Assert.That(focus2, Is.LessThan(t.EnemyMaxHealth), "2ポーズでは倒しきれない");
        }
        [Test]
        public void AreaDamageCap_LimitsTheTotalAnAreaBrickDealsToOneEnemyPerCut()
        {
            var t = T;
            float cap = t.AreaDamageCapRatio * 90f; // 72
            Assert.That(CutRules.CapAreaDamage(t, 90f, alreadyTaken: 0f, damage: 56f, out bool c1), Is.EqualTo(56f).Within(1e-4f));
            Assert.That(c1, Is.False);
            Assert.That(CutRules.CapAreaDamage(t, 90f, alreadyTaken: 56f, damage: 63f, out bool c2), Is.EqualTo(cap - 56f).Within(1e-4f));
            Assert.That(c2, Is.True);
            Assert.That(CutRules.CapAreaDamage(t, 90f, alreadyTaken: cap, damage: 20f, out bool c3), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(c3, Is.True);
        }

        [Test]
        public void AreaDamageCap_OfOneOrMore_DisablesTheRule()
        {
            var t = T;
            t.AreaDamageCapRatio = 1f;
            Assert.That(CutRules.CapAreaDamage(t, 90f, alreadyTaken: 80f, damage: 63f, out bool capped), Is.EqualTo(63f));
            Assert.That(capped, Is.False);
        }

        [Test]
        public void AreaCap_CoversAreaBricks_AndBystandersOfThePathSweep()
        {
            var t = T;
            Assert.That(CutRules.CountsTowardAreaCap(t, BrickKind.Spin, isAimedTarget: true), Is.True);
            Assert.That(CutRules.CountsTowardAreaCap(t, BrickKind.TableLanding, isAimedTarget: false), Is.True);
            Assert.That(CutRules.CountsTowardAreaCap(t, BrickKind.FlyingKick, isAimedTarget: false), Is.True, "経路で巻き込んだ敵は対象");
            Assert.That(CutRules.CountsTowardAreaCap(t, BrickKind.FlyingKick, isAimedTarget: true), Is.False, "到達点で狙った敵は対象外");
            Assert.That(CutRules.CountsTowardAreaCap(t, BrickKind.Palm, isAimedTarget: false), Is.False);
        }

        [Test]
        public void AreaBricks_AreSpinAndTableLanding()
        {
            Assert.That(CutRules.IsAreaBrick(BrickKind.Spin), Is.True);
            Assert.That(CutRules.IsAreaBrick(BrickKind.TableLanding), Is.True);
            Assert.That(CutRules.IsAreaBrick(BrickKind.Palm), Is.False);
            Assert.That(CutRules.IsAreaBrick(BrickKind.FlyingKick), Is.False, "飛び蹴りは範囲技ではない(巻き込みだけが上限の対象: CountsTowardAreaCap)");
        }
    }
}
