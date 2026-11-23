using CutKungfu.Core;
using CutKungfu.Gameplay;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace CutKungfu.Tests
{
    /// <summary>
    /// 描画も入力デバイスも無いワールドでゲームロジックを回す結合テスト。
    /// 入力は PlayerInput シングルトンに直接書く(デバイスから切り離した設計の確認も兼ねる)。
    /// </summary>
    public class GameplayWorldTests
    {
        World _world;
        EntityManager _em;
        Entity _player;

        [SetUp]
        public void SetUp()
        {
            _world = new World("CutKungfu.Tests");
            _em = _world.EntityManager;

            var init = _world.GetOrCreateSystemManaged<InitializationSystemGroup>();
            init.AddSystemToUpdateList(_world.CreateSystem<GameplayBootstrapSystem>());
            var sim = _world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            var gameplay = _world.GetOrCreateSystemManaged<GameplaySystemGroup>();
            sim.AddSystemToUpdateList(gameplay);
            foreach (var t in new[]
                     {
                         typeof(SessionSystem), typeof(GameClockSystem), typeof(FighterTimersSystem), typeof(CutControlSystem),
                         typeof(CutExecutionSystem), typeof(PlayerControlSystem), typeof(EnemyAISystem), typeof(DamageSystem),
                         typeof(BodyMotionSystem), typeof(DyingSystem), typeof(StrikeFxSystem), typeof(CharacterColorSystem),
                     })
                gameplay.AddSystemToUpdateList(_world.CreateSystem(t));
            sim.SortSystems();
            init.SortSystems();

            CreateScenario();
            _player = CreatePlayer();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        void Step(PlayerInput input = default, float dt = 1f / 60f)
        {
            input.SelectBrick = -1; // 0 はブリック「掌底」を選ぶ意味になるので、テストでは選ばない
            _world.SetTime(new TimeData(_world.Time.ElapsedTime + dt, dt));
            var q = _em.CreateEntityQuery(typeof(PlayerInput));
            if (q.CalculateEntityCount() > 0) q.SetSingleton(input);
            _world.GetExistingSystemManaged<InitializationSystemGroup>().Update();
            _world.GetExistingSystemManaged<SimulationSystemGroup>().Update();
        }

        T Singleton<T>() where T : unmanaged, IComponentData => _em.CreateEntityQuery(typeof(T)).GetSingleton<T>();

        void CreateScenario()
        {
            var enemy = _em.CreateEntity();
            _em.AddComponentData(enemy, LocalTransform.Identity);
            _em.AddComponentData(enemy, new Fighter { Team = Team.Enemy, Radius = 0.4f, Health = 30, MaxHealth = 30, Facing = new float3(-1, 0, 0) });
            _em.AddComponent<EnemyTag>(enemy);
            _em.AddComponentData(enemy, new EnemyBrain());
            _em.AddComponentData(enemy, new Knockback());
            _em.AddComponent<Prefab>(enemy);

            Entity Ghost()
            {
                var g = _em.CreateEntity();
                _em.AddComponentData(g, LocalTransform.Identity);
                _em.AddComponentData(g, new GhostPose());
                _em.AddComponent<Prefab>(g);
                return g;
            }

            var fx = _em.CreateEntity();
            _em.AddComponentData(fx, LocalTransform.Identity);
            _em.AddComponentData(fx, new StrikeFx());
            _em.AddComponentData(fx, new URPMaterialPropertyBaseColor());
            _em.AddComponent<Prefab>(fx);

            var scenario = _em.CreateEntity();
            _em.AddComponentData(scenario, new PrefabLibrary
            {
                Enemy = enemy, GhostPalm = Ghost(), GhostSpin = Ghost(), GhostFlyingKick = Ghost(), GhostTableLanding = Ghost(), StrikeFx = fx,
            });
            _em.AddBuffer<ScenarioWave>(scenario).Add(new ScenarioWave { PlayerStart = float3.zero, FirstSpawn = 0, SpawnCount = 1 });
            _em.AddBuffer<ScenarioSpawn>(scenario).Add(new ScenarioSpawn { Position = new float3(1.2f, 0, 0) });
        }

        Entity CreatePlayer()
        {
            var e = _em.CreateEntity();
            _em.AddComponentData(e, LocalTransform.Identity);
            _em.AddComponentData(e, new Fighter { Team = Team.Player, Radius = 0.4f, Health = 100, MaxHealth = 100, Facing = new float3(1, 0, 0) });
            _em.AddComponent<PlayerTag>(e);
            _em.AddComponentData(e, new PlayerAction());
            _em.AddComponentData(e, new FilmGauge { Value = 100, Max = 100 });
            _em.AddComponentData(e, new CutState());
            _em.AddBuffer<PlacedPose>(e);
            _em.AddComponentData(e, new Knockback());
            _em.AddComponentData(e, new VerticalBody());
            return e;
        }

        Entity FirstEnemy() => _em.CreateEntityQuery(typeof(EnemyTag)).GetSingletonEntity();

        [Test]
        public void WaveStart_SpawnsEnemyAndFillsGauge()
        {
            Step();
            Assert.That(Singleton<SessionState>().Phase, Is.EqualTo(SessionPhase.Playing));
            Assert.That(_em.CreateEntityQuery(typeof(EnemyTag)).CalculateEntityCount(), Is.EqualTo(1));
            Assert.That(_em.GetComponentData<FilmGauge>(_player).Value, Is.EqualTo(Tuning.Default.GaugeMax));
        }

        [Test]
        public void StopPlaceCut_ChargesGauge_AndSetsCommitTime()
        {
            Step();
            Step(new PlayerInput { Cut = true });
            Assert.That(_em.GetComponentData<CutState>(_player).Phase, Is.EqualTo(CutPhase.Slow));
            Assert.That(_em.GetComponentData<FilmGauge>(_player).Value, Is.EqualTo(80f).Within(1e-4f));

            // 正面の地面へ2つ置く
            var aim = new PlayerInput { AimStick = new float2(0.5f, 0f), Primary = true };
            Step(aim);
            Step(aim);
            Assert.That(_em.GetBuffer<PlacedPose>(_player).Length, Is.EqualTo(2));
            Assert.That(_em.GetComponentData<FilmGauge>(_player).Value, Is.EqualTo(60f).Within(1e-4f));
            Assert.That(_em.CreateEntityQuery(typeof(GhostPose)).CalculateEntityCount(), Is.EqualTo(3), "置いた2つ+照準プレビュー1つ");

            Step(new PlayerInput { Cut = true });
            var cut = _em.GetComponentData<CutState>(_player);
            Assert.That(cut.Phase, Is.EqualTo(CutPhase.Executing));
            Assert.That(cut.CommitDuration, Is.EqualTo(CutRules.CommitTime(Tuning.Default, 2, true)).Within(1e-5f));
        }

        [Test]
        public void Undo_RefundsPoseCost()
        {
            Step();
            Step(new PlayerInput { Cut = true });
            Step(new PlayerInput { AimStick = new float2(0.5f, 0f), Primary = true });
            Step(new PlayerInput { Secondary = true });
            Assert.That(_em.GetBuffer<PlacedPose>(_player).Length, Is.Zero);
            Assert.That(_em.GetComponentData<FilmGauge>(_player).Value, Is.EqualTo(80f).Within(1e-4f));
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
        public void HitDuringCommit_IsRecorded_AndEndsCommit()
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
            Assert.That(_em.GetComponentData<Fighter>(_player).Health, Is.EqualTo(88f));
            Assert.That(cut.Record.HitDuringCommit, Is.True);
            Assert.That(cut.CommitRemaining, Is.Zero);
        }

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
