using System;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace CutKungfu.Tests
{
    /// <summary>
    /// 描画も入力デバイスも無い ECS ワールドに、ゲームロジックのシステム一式とシナリオを組む。
    /// 入力は PlayerInput シングルトンに書く(InputBridgeSystem と同じく、まだ使われていない押下に重ねる)。結合テストが共通で使う。
    /// 既定では、1回の Step でゲームロジックをちょうど1回、<see cref="StepDt"/> だけ進める(dt を変えた比較のため)。
    /// fixedStep を true にすると実行時と同じく固定タイムステップ(60Hz)で回し、表示の補間も入れる。
    /// このときの Step は「描画の1フレーム」で、ゲームロジックは 0 回のことも複数回のこともある。
    /// </summary>
    public sealed class GameplayTestWorld : IDisposable
    {
        public readonly World World;
        public readonly EntityManager Em;
        public readonly Entity Player;

        public readonly bool FixedStep;

        public GameplayTestWorld(float3 playerStart, params float3[] enemySpawns) : this(false, playerStart, enemySpawns)
        {
        }

        public GameplayTestWorld(bool fixedStep, float3 playerStart, params float3[] enemySpawns)
        {
            FixedStep = fixedStep;
            World = new World("CutKungfu.Tests");
            Em = World.EntityManager;

            var init = World.GetOrCreateSystemManaged<InitializationSystemGroup>();
            init.AddSystemToUpdateList(World.CreateSystem<GameplayBootstrapSystem>());
            var sim = World.GetOrCreateSystemManaged<SimulationSystemGroup>();
            sim.AddSystemToUpdateList(World.GetOrCreateSystemManaged<BeginSimulationEntityCommandBufferSystem>());
            // 実行時と同じく、ゲームロジックは FixedStepSimulationSystemGroup の中に置く
            var fixedGroup = World.GetOrCreateSystemManaged<FixedStepSimulationSystemGroup>();
            sim.AddSystemToUpdateList(fixedGroup);
            if (fixedStep) fixedGroup.Timestep = GameplaySystemGroup.StepSeconds;
            else fixedGroup.RateManager = null; // Step ごとにちょうど1回、渡した dt で回す
            var gameplay = World.GetOrCreateSystemManaged<GameplaySystemGroup>();
            fixedGroup.AddSystemToUpdateList(gameplay);
            foreach (var t in new[]
                     {
                         typeof(SessionSystem), typeof(GameClockSystem), typeof(FighterTimersSystem), typeof(CutControlSystem),
                         typeof(CutExecutionSystem), typeof(PlayerControlSystem), typeof(EnemyAISystem), typeof(DamageSystem),
                         typeof(BodyMotionSystem), typeof(DyingSystem), typeof(StrikeFxSystem), typeof(CharacterColorSystem),
                         typeof(PlayerInputConsumeSystem),
                     })
                gameplay.AddSystemToUpdateList(World.CreateSystem(t));
            if (fixedStep)
            {
                fixedGroup.AddSystemToUpdateList(World.CreateSystem<CutKungfu.Presentation.SmoothingRestoreSystem>());
                fixedGroup.AddSystemToUpdateList(World.CreateSystem<CutKungfu.Presentation.SmoothingCaptureSystem>());
                sim.AddSystemToUpdateList(World.CreateSystem<CutKungfu.Presentation.SmoothingApplySystem>());
            }

            // 実行時と同じく、トランスフォーム更新も同じグループに置く。
            // 無いと GameplaySystemGroup の [UpdateBefore(TransformSystemGroup)] が「対象がいない」として無視され、警告が出る
            var transforms = World.GetOrCreateSystemManaged<TransformSystemGroup>();
            sim.AddSystemToUpdateList(transforms);
            transforms.AddSystemToUpdateList(World.CreateSystem<ParentSystem>());
            transforms.AddSystemToUpdateList(World.CreateSystem<LocalToWorldSystem>());
            sim.SortSystems();
            init.SortSystems();

            CreateScenario(playerStart, enemySpawns);
            Player = CreatePlayer();
        }

        public void Dispose() => World.Dispose();

        /// <summary>ボタンを何も押していない入力(SelectBrick=0 は「掌底を選ぶ」なので -1 にする)。</summary>
        public static PlayerInput Neutral() => new PlayerInput { SelectBrick = -1 };

        /// <summary>1回の Step で進める実時間[秒]。フレームレートを変えた比較に使う。</summary>
        public float StepDt = 1f / 60f;

        /// <param name="dt">負なら <see cref="StepDt"/>(0 は「時間を進めない更新」)。</param>
        public void Step(PlayerInput input, float dt = -1f)
        {
            if (dt < 0f) dt = StepDt;
            World.SetTime(new TimeData(World.Time.ElapsedTime + dt, dt));
            var q = Em.CreateEntityQuery(typeof(PlayerInput));
            if (q.CalculateEntityCount() > 0) q.SetSingleton(PlayerInput.Accumulate(q.GetSingleton<PlayerInput>(), input));
            World.GetExistingSystemManaged<InitializationSystemGroup>().Update();
            World.GetExistingSystemManaged<SimulationSystemGroup>().Update();
        }

        public void Step() => Step(Neutral());

        public void StepFrames(int frames)
        {
            for (int i = 0; i < frames; i++) Step();
        }

        public T Singleton<T>() where T : unmanaged, IComponentData => Em.CreateEntityQuery(typeof(T)).GetSingleton<T>();

        public void SetSingleton<T>(T value) where T : unmanaged, IComponentData => Em.CreateEntityQuery(typeof(T)).SetSingleton(value);

        public int AliveEnemies()
        {
            var q = new EntityQueryBuilder(Allocator.Temp).WithAll<EnemyTag>().WithNone<Dying>().Build(Em);
            return q.CalculateEntityCount();
        }

        public NativeArray<Entity> Enemies() => Em.CreateEntityQuery(typeof(EnemyTag)).ToEntityArray(Allocator.Temp);

        /// <summary>机(地形連携の対象)を置く。ステージと同じ寸法にするなら <see cref="StageTable"/>。</summary>
        public void AddTable(TableRect rect)
        {
            var e = Em.CreateEntity();
            Em.AddComponentData(e, new TableSingleton { Rect = rect });
        }

        /// <summary>試作ステージの屋台の机(PrototypeBuilder と同じ: 中心 (3, 0.8)、2.4×1.2、高さ 0.8)。</summary>
        public static TableRect StageTable => new TableRect
        {
            Exists = true, Center = new float2(3f, 0.8f), HalfExtents = new float2(1.2f, 0.6f), TopY = 0.8f,
        };

        public void SetGauge(float value)
        {
            var g = Em.GetComponentData<FilmGauge>(Player);
            g.Value = value;
            Em.SetComponentData(Player, g);
        }

        /// <summary>
        /// スローに入り、指定のブリックを指定の位置へ順に置いて、解除する。
        /// 照準はマウスの光線を真上から当てる形で渡す(位置を正確に指定するため)。
        /// </summary>
        public void PerformCut(params (BrickKind kind, float3 position)[] poses)
        {
            PlacePoses(poses);
            ReleaseCut();
        }

        /// <summary>スローに入り、指定のブリックを指定の位置へ順に置く(解除はしない)。</summary>
        public void PlacePoses(params (BrickKind kind, float3 position)[] poses)
        {
            var enter = Neutral();
            enter.Cut = true;
            Step(enter);
            foreach (var (kind, position) in poses)
            {
                var select = AimAt(position);
                select.SelectBrick = (int)kind;
                Step(select);
                var place = AimAt(position);
                place.Primary = true;
                Step(place);
            }
        }

        /// <summary>スローを解除して、置いたポーズを実行させる。</summary>
        public void ReleaseCut()
        {
            var release = Neutral();
            release.Cut = true;
            Step(release);
        }

        static PlayerInput AimAt(float3 position)
        {
            var p = Neutral();
            p.AimRayValid = true;
            p.AimRayOrigin = new float3(position.x, 10f, position.z);
            p.AimRayDirection = new float3(0f, -1f, 0f);
            return p;
        }

        /// <summary>カットが硬直明けまで終わるのを待つ(上限つき)。</summary>
        public void RunUntilCutFinished(int maxFrames = 600)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (!Em.GetComponentData<CutState>(Player).IsActive) return;
                Step();
            }
        }

        void CreateScenario(float3 playerStart, float3[] enemySpawns)
        {
            var enemy = Em.CreateEntity();
            Em.AddComponentData(enemy, LocalTransform.Identity);
            Em.AddComponentData(enemy, new Fighter { Team = Team.Enemy, Radius = 0.4f, Health = 30, MaxHealth = 30, Facing = new float3(-1, 0, 0) });
            Em.AddComponent<EnemyTag>(enemy);
            Em.AddComponentData(enemy, new EnemyBrain());
            Em.AddComponentData(enemy, new Knockback());
            Em.AddComponent<Prefab>(enemy);

            Entity Ghost()
            {
                var g = Em.CreateEntity();
                Em.AddComponentData(g, LocalTransform.Identity);
                Em.AddComponentData(g, new GhostPose());
                Em.AddComponent<Prefab>(g);
                return g;
            }

            var fx = Em.CreateEntity();
            Em.AddComponentData(fx, LocalTransform.Identity);
            Em.AddComponentData(fx, new StrikeFx());
            Em.AddComponentData(fx, new URPMaterialPropertyBaseColor());
            Em.AddComponent<Prefab>(fx);

            var scenario = Em.CreateEntity();
            Em.AddComponentData(scenario, new PrefabLibrary
            {
                Enemy = enemy, GhostPalm = Ghost(), GhostSpin = Ghost(), GhostFlyingKick = Ghost(), GhostTableLanding = Ghost(), StrikeFx = fx,
            });
            Em.AddBuffer<ScenarioWave>(scenario).Add(new ScenarioWave { PlayerStart = playerStart, FirstSpawn = 0, SpawnCount = enemySpawns.Length });
            var spawns = Em.AddBuffer<ScenarioSpawn>(scenario);
            foreach (var p in enemySpawns) spawns.Add(new ScenarioSpawn { Position = p });
        }

        Entity CreatePlayer()
        {
            var e = Em.CreateEntity();
            Em.AddComponentData(e, LocalTransform.Identity);
            Em.AddComponentData(e, new Fighter { Team = Team.Player, Radius = 0.4f, Health = 100, MaxHealth = 100, Facing = new float3(1, 0, 0) });
            Em.AddComponent<PlayerTag>(e);
            Em.AddComponentData(e, new PlayerAction());
            Em.AddComponentData(e, new FilmGauge { Value = 100, Max = 100 });
            Em.AddComponentData(e, new CutState());
            Em.AddBuffer<PlacedPose>(e);
            Em.AddComponentData(e, new Knockback());
            Em.AddComponentData(e, new VerticalBody());
            return e;
        }
    }
}
