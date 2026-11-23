using System.Collections.Generic;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Tests
{
    /// <summary>
    /// 動作生成を「計画(置いたとき)」と「再生(切った後)」に分けた経路の結合テスト。
    /// MotionBricks を模した、結果がすぐ揃わない・長さと接触の時刻を自分で決める偽の生成器で確かめる。
    /// </summary>
    public class MotionPlanningTests
    {
        /// <summary>CompleteAll() を呼ぶまで準備が揃わない生成器。長さと接触の時刻を固定できる。</summary>
        sealed class DeferredMotionSource : ICutMotionSource
        {
            readonly ProceduralCutMotionSource _inner = new();
            readonly HashSet<int> _ready = new();
            readonly HashSet<int> _planned = new();
            public float FixedDuration = -1f;
            public float ContactRatio = 1f;
            public int PlanCalls;

            public int ActivePlans => _inner.ActivePlans;

            public int Plan(in MotionSegment segment, in MotionContext context)
            {
                PlanCalls++;
                int h = _inner.Plan(segment, context);
                _planned.Add(h);
                return h;
            }

            public void CompleteAll() => _ready.UnionWith(_planned);
            public bool IsReady(int handle) => _ready.Contains(handle) && _inner.IsReady(handle);
            public float Duration(int handle) => FixedDuration > 0f ? FixedDuration : _inner.Duration(handle);
            public float ContactTime(int handle) => Duration(handle) * ContactRatio;

            public MotionSample Sample(int handle, float time)
                => _inner.Sample(handle, time * _inner.Duration(handle) / math.max(1e-5f, Duration(handle)));

            public void Discard(int handle)
            {
                _inner.Discard(handle);
                _ready.Remove(handle);
                _planned.Remove(handle);
            }
        }

        GameplayTestWorld _w;
        DeferredMotionSource _deferred;
        ProceduralCutMotionSource _fallback;

        [SetUp]
        public void SetUp()
        {
            // 敵は遠くに1体だけ置き、テスト中に殴られないようにする
            _w = new GameplayTestWorld(float3.zero, new float3(10f, 0f, 3f));
            _w.Step();
        }

        [TearDown]
        public void TearDown() => _w.Dispose();

        CutMotionService Sources => _w.World.GetOrCreateSystemManaged<CutMotionService>();

        void UseDeferredSource()
        {
            _deferred = new DeferredMotionSource();
            _fallback = new ProceduralCutMotionSource();
            Sources.Primary = _deferred;
            Sources.Fallback = _fallback;
        }

        CutState Cut => _w.Em.GetComponentData<CutState>(_w.Player);
        DynamicBuffer<PlacedPose> Poses => _w.Em.GetBuffer<PlacedPose>(_w.Player);

        void EnterSlowAndPlace(int count)
        {
            var enter = GameplayTestWorld.Neutral();
            enter.Cut = true;
            _w.Step(enter);
            for (int i = 0; i < count; i++)
            {
                var place = GameplayTestWorld.Neutral();
                place.AimStick = new float2(0.4f, 0f);
                place.Primary = true;
                _w.Step(place);
            }
        }

        void Release()
        {
            var release = GameplayTestWorld.Neutral();
            release.Cut = true;
            _w.Step(release);
        }

        [Test]
        public void PlacingAPose_PlansItsMotion_AndUndoDiscardsIt()
        {
            var procedural = (ProceduralCutMotionSource)Sources.Primary;
            EnterSlowAndPlace(2);
            Assert.That(Poses.Length, Is.EqualTo(2));
            Assert.That(procedural.ActivePlans, Is.EqualTo(2));
            Assert.That(procedural.IsReady(Poses[0].MotionHandle) && procedural.IsReady(Poses[1].MotionHandle), Is.True);

            var undo = GameplayTestWorld.Neutral();
            undo.Secondary = true;
            _w.Step(undo);
            Assert.That(procedural.ActivePlans, Is.EqualTo(1));
        }

        [Test]
        public void FinishedCut_LeavesNoPlansBehind()
        {
            var procedural = (ProceduralCutMotionSource)Sources.Primary;
            EnterSlowAndPlace(3);
            Release();
            _w.RunUntilCutFinished();
            Assert.That(Cut.IsActive, Is.False);
            Assert.That(procedural.ActivePlans, Is.Zero);
        }

        [Test]
        public void Retake_DiscardsPlansOfTheAbandonedCut()
        {
            var procedural = (ProceduralCutMotionSource)Sources.Primary;
            EnterSlowAndPlace(2);
            var retake = GameplayTestWorld.Neutral();
            retake.Retake = true;
            _w.Step(retake);
            Assert.That(procedural.ActivePlans, Is.Zero);
        }

        [Test]
        public void ReleaseBeforePlansAreReady_WaitsInSlow_ThenExecutesWhenReady()
        {
            UseDeferredSource();
            EnterSlowAndPlace(2);
            Release();
            Assert.That(Cut.Phase, Is.EqualTo(CutPhase.Slow), "計画が揃うまでスローのまま待つ");
            Assert.That(Cut.ReleasePending, Is.True);

            // 待っている間は配置を締め切る
            var place = GameplayTestWorld.Neutral();
            place.AimStick = new float2(0.4f, 0f);
            place.Primary = true;
            _w.Step(place);
            Assert.That(Poses.Length, Is.EqualTo(2));

            _deferred.CompleteAll();
            _w.Step();
            Assert.That(Cut.Phase, Is.EqualTo(CutPhase.Executing));
            Assert.That(Poses[0].UsesFallback, Is.False);
        }

        [Test]
        public void PlansThatNeverArrive_AreReplacedByTheFallbackAfterTheTimeout()
        {
            UseDeferredSource();
            EnterSlowAndPlace(2);
            Release();
            int frames = (int)math.ceil(CutControlSystem.ReleaseWaitTimeoutReal * 60f) + 2;
            for (int i = 0; i < frames && Cut.Phase == CutPhase.Slow; i++) _w.Step();

            Assert.That(Cut.Phase, Is.EqualTo(CutPhase.Executing));
            Assert.That(Poses[0].UsesFallback && Poses[1].UsesFallback, Is.True);
            Assert.That(_deferred.ActivePlans, Is.Zero, "間に合わなかった計画は捨てる");
            Assert.That(_fallback.ActivePlans, Is.EqualTo(2));

            _w.RunUntilCutFinished();
            Assert.That(_fallback.ActivePlans, Is.Zero);
        }

        void QueueEnemyStrikeAtPlayer()
        {
            var enemy = _w.Em.CreateEntityQuery(typeof(EnemyTag)).GetSingletonEntity();
            float2 at = _w.Em.GetComponentData<Unity.Transforms.LocalTransform>(_w.Player).Position.xz;
            _w.Em.GetBuffer<DamageRequest>(_w.Em.CreateEntityQuery(typeof(DamageRequest)).GetSingletonEntity()).Add(new DamageRequest
            {
                Attacker = enemy, AttackerTeam = Team.Enemy, Kind = StrikeKind.EnemyStrike,
                A = at, B = at, Radius = 1f, Damage = 12f, HitStun = Tuning.Default.PlayerHitStun,
            });
        }

        /// <summary>解除待ちの間はまだスロー(CutPhase.Slow)なので、既定の InvulnerableDuringSlow=true に従って殴られない。</summary>
        [Test]
        public void WhileWaitingForPlans_PlayerIsNotHit_ByDefault()
        {
            UseDeferredSource();
            EnterSlowAndPlace(1);
            Release();
            Assert.That(Cut.ReleasePending, Is.True);

            QueueEnemyStrikeAtPlayer();
            _w.Step();
            Assert.That(_w.Em.GetComponentData<Fighter>(_w.Player).Health, Is.EqualTo(Tuning.Default.PlayerMaxHealth));
            Assert.That(Cut.Phase, Is.EqualTo(CutPhase.Slow));
        }

        /// <summary>
        /// InvulnerableDuringSlow を切ると、解除待ちの間にも殴られる。解除待ちはそのまま続き、
        /// 計画が揃って切った瞬間に被弾硬直が残っているので、技は出だしで中断される(記録は interrupted=1)。
        /// </summary>
        [Test]
        public void WhileWaitingForPlans_WithSlowInvulnerabilityOff_HitLands_AndTheCutIsInterruptedOnRelease()
        {
            var tuning = _w.Singleton<TuningSingleton>();
            tuning.Value.InvulnerableDuringSlow = false;
            _w.SetSingleton(tuning);

            UseDeferredSource();
            EnterSlowAndPlace(1);
            Release();
            QueueEnemyStrikeAtPlayer();
            _w.Step();
            Assert.That(_w.Em.GetComponentData<Fighter>(_w.Player).Health, Is.LessThan(Tuning.Default.PlayerMaxHealth), "解除待ちでも殴られる");
            Assert.That(Cut.Phase, Is.EqualTo(CutPhase.Slow));
            Assert.That(Cut.ReleasePending, Is.True, "殴られても解除待ちは続く");

            _deferred.CompleteAll();
            _w.Step();
            Assert.That(Cut.Phase, Is.EqualTo(CutPhase.None), "切った同じ更新で、被弾硬直により中断される");
            bool interrupted = false;
            foreach (var e in _w.Em.GetBuffer<TelemetryEvent>(_w.Em.CreateEntityQuery(typeof(TelemetryEvent)).GetSingletonEntity()))
                if (e.Value.Kind == PlaytestEventKind.Cut && e.Value.Interrupted) interrupted = true;
            Assert.That(interrupted, Is.True);
        }

        [Test]
        public void ExecutionUsesTheGeneratorsLength_AndStrikesAtItsContactTime()
        {
            UseDeferredSource();
            _deferred.FixedDuration = 0.6f;
            _deferred.ContactRatio = 0.5f;
            EnterSlowAndPlace(1);
            _deferred.CompleteAll();
            Release();
            Assert.That(Cut.Phase, Is.EqualTo(CutPhase.Executing));
            var ghost = Poses[0].Ghost;

            // 接触(0.3秒)の手前: まだ打っていない
            for (int i = 0; i < 15; i++) _w.Step();
            Assert.That(_w.Em.Exists(ghost), Is.True);
            Assert.That(Cut.StrikeFired, Is.False);

            // 接触を過ぎた: 区間の途中で打ち、人影が消える。区間はまだ終わらない
            for (int i = 0; i < 6; i++) _w.Step();
            Assert.That(_w.Em.Exists(ghost), Is.False);
            Assert.That(Cut.Phase, Is.EqualTo(CutPhase.Executing));
            Assert.That(Cut.SegmentIndex, Is.Zero);

            // 生成側の長さ(0.6秒)で区間が終わる
            for (int i = 0; i < 20 && Cut.Phase == CutPhase.Executing; i++) _w.Step();
            Assert.That(Cut.Phase, Is.Not.EqualTo(CutPhase.Executing));
        }
    }
}
