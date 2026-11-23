using CutKungfu.Core;
using CutKungfu.Gameplay;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Tests
{
    /// <summary>
    /// 描画も入力デバイスも無いワールドでゲームロジックを回す結合テスト(<see cref="GameplayTestWorld"/>)。
    /// 入力は PlayerInput シングルトンに直接書く(デバイスから切り離した設計の確認も兼ねる)。
    /// </summary>
    public class GameplayWorldTests
    {
        GameplayTestWorld _w;
        EntityManager _em;
        Entity _player;

        [SetUp]
        public void SetUp()
        {
            _w = new GameplayTestWorld(float3.zero, new float3(1.2f, 0, 0));
            _em = _w.Em;
            _player = _w.Player;
        }

        [TearDown]
        public void TearDown() => _w.Dispose();

        void Step(PlayerInput input = default, float dt = 1f / 60f)
        {
            input.SelectBrick = -1; // 0 はブリック「掌底」を選ぶ意味になるので、テストでは選ばない
            _w.Step(input, dt);
        }

        T Singleton<T>() where T : unmanaged, IComponentData => _w.Singleton<T>();

        Entity FirstEnemy() => _em.CreateEntityQuery(typeof(EnemyTag)).GetSingletonEntity();

        [Test]
        public void WaveStart_SpawnsEnemyAndFillsGauge()
        {
            Step();
            Assert.That(Singleton<SessionState>().Phase, Is.EqualTo(SessionPhase.Playing));
            Assert.That(_em.CreateEntityQuery(typeof(EnemyTag)).CalculateEntityCount(), Is.EqualTo(1));
            Assert.That(_em.GetComponentData<FilmGauge>(_player).Value, Is.EqualTo(Tuning.Default.GaugeAtWaveStart));
        }

        [Test]
        public void StopPlaceCut_ChargesGauge_AndSetsCommitTime()
        {
            Step();
            Step(new PlayerInput { Cut = true });
            Assert.That(_em.GetComponentData<CutState>(_player).Phase, Is.EqualTo(CutPhase.Slow));
            float start = Tuning.Default.GaugeAtWaveStart;
            Assert.That(_em.GetComponentData<FilmGauge>(_player).Value, Is.EqualTo(start - 20f).Within(1e-4f));

            // 正面の地面へ2つ置く
            var aim = new PlayerInput { AimStick = new float2(0.5f, 0f), Primary = true };
            Step(aim);
            Step(aim);
            Assert.That(_em.GetBuffer<PlacedPose>(_player).Length, Is.EqualTo(2));
            Assert.That(_em.GetComponentData<FilmGauge>(_player).Value, Is.EqualTo(start - 40f).Within(1e-4f));
            Assert.That(_em.CreateEntityQuery(typeof(GhostPose)).CalculateEntityCount(), Is.EqualTo(3), "置いた2つ+照準プレビュー1つ");

            Step(new PlayerInput { Cut = true });
            var cut = _em.GetComponentData<CutState>(_player);
            Assert.That(cut.Phase, Is.EqualTo(CutPhase.Executing));
            Assert.That(cut.CommitDuration, Is.EqualTo(CutRules.CommitTime(Tuning.Default, 2, true)).Within(1e-5f));
        }

        [Test]
        public void SlowEntry_RecordsGaugeAndHowManyPosesWereAffordable()
        {
            Step();
            Step(new PlayerInput { Cut = true });
            Step(new PlayerInput { Cut = true }); // 1個も置かずに解除
            var events = _em.GetBuffer<TelemetryEvent>(_em.CreateEntityQuery(typeof(TelemetryEvent)).GetSingletonEntity());
            PlaytestEvent cancelled = default;
            foreach (var e in events) if (e.Value.Kind == PlaytestEventKind.CutCancelled) cancelled = e.Value;

            Assert.That(cancelled.Kind, Is.EqualTo(PlaytestEventKind.CutCancelled));
            Assert.That(cancelled.GaugeBefore, Is.EqualTo(Tuning.Default.GaugeAtWaveStart));
            Assert.That(cancelled.MaxAffordablePoses, Is.EqualTo(3), "ゲージ50 = 固定費20 + 3ポーズ分");
        }

        [Test]
        public void Undo_RefundsPoseCost()
        {
            Step();
            Step(new PlayerInput { Cut = true });
            Step(new PlayerInput { AimStick = new float2(0.5f, 0f), Primary = true });
            Step(new PlayerInput { Secondary = true });
            Assert.That(_em.GetBuffer<PlacedPose>(_player).Length, Is.Zero);
            Assert.That(_em.GetComponentData<FilmGauge>(_player).Value, Is.EqualTo(Tuning.Default.GaugeAtWaveStart - 20f).Within(1e-4f));
        }

        [Test]
        public void Parry_NegatesStrike_StaggersAttacker_AndRefillsGauge()
        {
            Step();
            var enemy = FirstEnemy();
            var gauge = _em.GetComponentData<FilmGauge>(_player);
            gauge.Value = 10f;
            _em.SetComponentData(_player, gauge);
            _em.SetComponentData(_player, new PlayerAction { State = PlayerActionState.Parry, Timer = 0.02f });
            _em.GetBuffer<DamageRequest>(_em.CreateEntityQuery(typeof(DamageRequest)).GetSingletonEntity()).Add(new DamageRequest
            {
                Attacker = enemy, AttackerTeam = Team.Enemy, Kind = StrikeKind.EnemyStrike, Radius = 1f, Damage = 12f, HitStun = 0.4f,
            });
            Step(dt: 0f);

            Assert.That(_em.GetComponentData<Fighter>(_player).Health, Is.EqualTo(100f));
            Assert.That(_em.GetComponentData<FilmGauge>(_player).Value, Is.EqualTo(10f + Tuning.Default.GaugeOnParry).Within(1e-4f));
            Assert.That(_em.GetComponentData<EnemyBrain>(enemy).State, Is.EqualTo(EnemyState.Staggered));
        }

        [Test]
        public void HitDuringCommit_IsCounterHit_AndDoesNotShortenCommit()
        {
            Step();
            var enemy = FirstEnemy();
            _em.SetComponentData(_player, new CutState { Phase = CutPhase.Commit, PoseCount = 4, CommitDuration = 1.4f, CommitRemaining = 1.2f });
            _em.GetBuffer<DamageRequest>(_em.CreateEntityQuery(typeof(DamageRequest)).GetSingletonEntity()).Add(new DamageRequest
            {
                Attacker = enemy, AttackerTeam = Team.Enemy, Kind = StrikeKind.EnemyStrike, Radius = 1f, Damage = 12f, HitStun = 0.4f,
            });
            Step(dt: 0f);

            var cut = _em.GetComponentData<CutState>(_player);
            Assert.That(_em.GetComponentData<Fighter>(_player).Health, Is.EqualTo(100f - 12f * Tuning.Default.CounterHitMultiplier).Within(1e-4f));
            Assert.That(cut.Record.HitDuringCommit, Is.True);
            Assert.That(cut.CommitRemaining, Is.EqualTo(1.2f).Within(1e-5f), "殴られても硬直は短くならない");
        }

        [Test]
        public void ZeroLengthCommit_IsNotTreatedAsCommitted()
        {
            Step();
            var enemy = FirstEnemy();
            _em.SetComponentData(_player, new CutState { Phase = CutPhase.Commit, PoseCount = 4, CommitDuration = 0f, CommitRemaining = 0f });
            QueueEnemyStrike(enemy);
            Step(dt: 0f);
            Assert.That(_em.GetComponentData<Fighter>(_player).Health, Is.EqualTo(88f), "カウンターにならない");
        }

        [Test]
        public void StrikeFromAttackerKnockedOutThisFrame_DoesNotLand()
        {
            Step();
            var enemy = FirstEnemy();
            var ef = _em.GetComponentData<Fighter>(enemy);
            ef.HitStun = 0.3f;
            _em.SetComponentData(enemy, ef);
            QueueEnemyStrike(enemy);
            Step(dt: 0f);
            Assert.That(_em.GetComponentData<Fighter>(_player).Health, Is.EqualTo(100f));
        }

        void QueueEnemyStrike(Entity enemy)
            => _em.GetBuffer<DamageRequest>(_em.CreateEntityQuery(typeof(DamageRequest)).GetSingletonEntity()).Add(new DamageRequest
            {
                Attacker = enemy, AttackerTeam = Team.Enemy, Kind = StrikeKind.EnemyStrike, Radius = 1f, Damage = 12f, HitStun = 0.4f,
            });

        [Test]
        public void PlayerDeath_GoesToNg_ThenRetakesSameWave()
        {
            Step();
            var f = _em.GetComponentData<Fighter>(_player);
            f.Health = 0f;
            _em.SetComponentData(_player, f);
            Step();
            Assert.That(Singleton<SessionState>().Phase, Is.EqualTo(SessionPhase.NG));

            for (int i = 0; i < 200 && Singleton<SessionState>().Phase != SessionPhase.Playing; i++) Step();
            var s = Singleton<SessionState>();
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Playing));
            Assert.That(s.Take, Is.EqualTo(2));
            Assert.That(_em.GetComponentData<Fighter>(_player).Health, Is.EqualTo(Tuning.Default.PlayerMaxHealth));
        }
    }
}
