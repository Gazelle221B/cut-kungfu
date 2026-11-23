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
        [TestCase(2, 0.7f)]
        [TestCase(3, 1.0f)]
        [TestCase(4, 1.4f)]
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
            Assert.That(CutRules.CommitTime(t, 4, true), Is.EqualTo(2.8f).Within(1e-5f));
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
        public void SegmentDuration_IsClamped()
        {
            Assert.That(CutRules.SegmentDuration(T, 0f), Is.EqualTo(T.CutSegmentMin));
            Assert.That(CutRules.SegmentDuration(T, 100f), Is.EqualTo(T.CutSegmentMax));
        }
    }
}
