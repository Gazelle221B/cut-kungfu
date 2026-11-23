using CutKungfu.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace CutKungfu.Tests
{
    /// <summary>動作生成の差し替え口(計画→再生)と、その仮実装。</summary>
    public class MotionSourceTests
    {
        static MotionSegment Segment(float preferred = 0.25f) => new MotionSegment
        {
            From = new float3(0, 0, 0),
            FromYaw = 0f,
            To = new float3(3, 0, 0),
            ToYaw = math.PI * 0.5f,
            ArcHeight = 0.5f,
            PreferredDuration = preferred,
        };

        static MotionContext Context => new MotionContext { RootPosition = float3.zero, RootYaw = 0f };

        [Test]
        public void Procedural_IsReadyImmediately_AndKeepsTheRequestedLength()
        {
            var source = new ProceduralCutMotionSource();
            int h = source.Plan(Segment(0.25f), Context);
            Assert.That(h, Is.Not.EqualTo(MotionHandle.None));
            Assert.That(source.IsReady(h), Is.True);
            Assert.That(source.Duration(h), Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(source.ContactTime(h), Is.EqualTo(source.Duration(h)), "仮実装は到達=接触");
        }

        [Test]
        public void Procedural_SampleStartsAndEndsOnThePoses()
        {
            var source = new ProceduralCutMotionSource();
            var seg = Segment();
            int h = source.Plan(seg, Context);
            Assert.That(math.distance(source.Sample(h, 0f).Position, seg.From), Is.LessThan(1e-5f));
            Assert.That(math.distance(source.Sample(h, source.Duration(h)).Position, seg.To), Is.LessThan(1e-5f));
            float3 forward = math.mul(source.Sample(h, source.Duration(h)).Rotation, new float3(0, 0, 1));
            Assert.That(forward.x, Is.EqualTo(1f).Within(1e-4f), "終点では目標ポーズの向き(+X)を向く");
        }

        [Test]
        public void Procedural_MatchesThePreviousInterpolation()
        {
            // 差し替え前の CutMotion.Sample と同じ動き(ゲームの挙動を変えない)
            var source = new ProceduralCutMotionSource();
            var seg = Segment(0.3f);
            int h = source.Plan(seg, Context);
            for (float t = 0f; t <= 0.3f; t += 0.05f)
                Assert.That(math.distance(source.Sample(h, t).Position, CutMotion.Evaluate(seg.From, seg.To, seg.ArcHeight, t / 0.3f)), Is.LessThan(1e-5f));
        }

        [Test]
        public void Discard_InvalidatesTheHandle()
        {
            var source = new ProceduralCutMotionSource();
            int h = source.Plan(Segment(), Context);
            source.Discard(h);
            Assert.That(source.IsReady(h), Is.False);
            Assert.That(source.ActivePlans, Is.Zero);
            Assert.DoesNotThrow(() => source.Discard(h), "二重に捨てても落ちない");
            Assert.DoesNotThrow(() => source.Discard(MotionHandle.None));
        }

        [Test]
        public void Handles_AreNotReusedWhileAlive()
        {
            var source = new ProceduralCutMotionSource();
            int a = source.Plan(Segment(), Context);
            int b = source.Plan(Segment(), Context);
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(source.ActivePlans, Is.EqualTo(2));
        }
    }
}
