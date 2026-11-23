using CutKungfu.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace CutKungfu.Tests
{
    public class CoreUtilityTests
    {
        [Test]
        public void BrickSequence_RoundTripsInPlacementOrder()
        {
            int s = 0;
            s = BrickSequence.Append(s, BrickKind.Palm);
            s = BrickSequence.Append(s, BrickKind.Spin);
            s = BrickSequence.Append(s, BrickKind.TableLanding);
            Assert.That(BrickSequence.Count(s), Is.EqualTo(3));
            Assert.That(BrickSequence.ToCodeString(s), Is.EqualTo("palm>spin>table"));
            Assert.That(BrickSequence.ToCodeString(BrickSequence.RemoveLast(s)), Is.EqualTo("palm>spin"));
            Assert.That(BrickSequence.ToCodeString(0), Is.Empty);
        }

        [Test]
        public void Geometry_CapsuleOverlap()
        {
            Assert.That(Geometry2D.CapsuleOverlapsCircle(new float2(0, 0), new float2(4, 0), 0.5f, new float2(2, 0.8f), 0.4f), Is.True);
            Assert.That(Geometry2D.CapsuleOverlapsCircle(new float2(0, 0), new float2(4, 0), 0.5f, new float2(2, 1.0f), 0.4f), Is.False);
            Assert.That(Geometry2D.CapsuleOverlapsCircle(new float2(1, 1), new float2(1, 1), 1f, new float2(1, 2.3f), 0.4f), Is.True);
        }

        [Test]
        public void Geometry_PushOutOfRect_MovesAlongShortestAxis()
        {
            var p = Geometry2D.PushOutOfRect(new float2(0.9f, 0f), 0.4f, float2.zero, new float2(1f, 1f));
            Assert.That(p.x, Is.EqualTo(1.4f).Within(1e-5f));
            Assert.That(p.y, Is.EqualTo(0f));
        }

        [Test]
        public void Geometry_RayPlane()
        {
            Assert.That(Geometry2D.RayPlaneY(new float3(0, 5, -10), math.normalize(new float3(0, -1, 1)), 0f, out var hit), Is.True);
            Assert.That(hit.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(hit.z, Is.EqualTo(-5f).Within(1e-4f));
            Assert.That(Geometry2D.RayPlaneY(new float3(0, 5, 0), new float3(0, 1, 0), 0f, out _), Is.False);
        }

        [Test]
        public void CutMotion_StartsAndEndsOnPoses_WithArc()
        {
            var a = new float3(0, 0, 0);
            var b = new float3(4, 0, 0);
            Assert.That(math.distance(CutMotion.Evaluate(a, b, 1f, 0f), a), Is.LessThan(1e-5f));
            Assert.That(math.distance(CutMotion.Evaluate(a, b, 1f, 1f), b), Is.LessThan(1e-5f));
            Assert.That(CutMotion.Evaluate(a, b, 1f, 0.3f).y, Is.GreaterThan(0.5f));
        }

        [Test]
        public void PlaytestStats_AveragesPosesPerEnemyBucketAndCondition()
        {
            var stats = new PlaytestStats();
            stats.Add(new PlaytestEvent { Kind = PlaytestEventKind.Cut, CommitEnabled = true, EnemiesAlive = 1, Poses = 4 });
            stats.Add(new PlaytestEvent { Kind = PlaytestEventKind.Cut, CommitEnabled = true, EnemiesAlive = 3, Poses = 1 });
            stats.Add(new PlaytestEvent { Kind = PlaytestEventKind.Cut, CommitEnabled = true, EnemiesAlive = 5, Poses = 2 });
            stats.Add(new PlaytestEvent { Kind = PlaytestEventKind.CutCancelled, CommitEnabled = true });
            stats.Add(new PlaytestEvent { Kind = PlaytestEventKind.Cut, CommitEnabled = false, EnemiesAlive = 3, Poses = 4 });

            Assert.That(stats.TryAveragePoses(true, 0, out float a1), Is.True);
            Assert.That(a1, Is.EqualTo(4f));
            Assert.That(stats.TryAveragePoses(true, 2, out float a3), Is.True);
            Assert.That(a3, Is.EqualTo(1.5f));
            Assert.That(stats.TryAveragePoses(true, 1, out _), Is.False);
            Assert.That(stats.TryAveragePoses(false, 2, out float off3), Is.True);
            Assert.That(off3, Is.EqualTo(4f));
            Assert.That(stats.SlowUses(true), Is.EqualTo(4));
        }

        [Test]
        public void PlaytestCsv_RowMatchesHeaderColumns()
        {
            string row = PlaytestCsv.ToRow(new PlaytestEvent { Kind = PlaytestEventKind.Cut, Sequence = BrickSequence.Append(0, BrickKind.FlyingKick), GaugeBefore = 12.5f });
            Assert.That(row.Split(',').Length, Is.EqualTo(PlaytestCsv.Header.Split(',').Length));
            StringAssert.StartsWith("cut,", row);
            StringAssert.Contains(",fly,", row);
        }
    }
}
