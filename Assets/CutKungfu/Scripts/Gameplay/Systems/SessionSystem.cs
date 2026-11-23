using CutKungfu.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 撮影の進行(GDD 11章): ウェーブの組み立て、NG→テイク2、カット!OK→次のウェーブ、
    /// それにプレイテスト用の条件切り替え(コミット時間あり/なし)。
    /// </summary>
    [UpdateInGroup(typeof(GameplaySystemGroup), OrderFirst = true)]
    public partial struct SessionSystem : ISystem
    {
        EntityQuery _resettable;
        EntityQuery _aliveEnemies;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SessionState>();
            state.RequireForUpdate<PrefabLibrary>();
            state.RequireForUpdate<ScenarioWave>();
            state.RequireForUpdate<PlayerTag>();

            _resettable = new EntityQueryBuilder(Allocator.Temp)
                .WithAny<EnemyTag, GhostPose, StrikeFx>()
                .Build(ref state);
            _aliveEnemies = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<EnemyTag>()
                .WithNone<Dying>()
                .Build(ref state);
        }

        // ウェーブの組み直しで動作生成ソース(managed)の計画を捨てるので Burst にしない
        public void OnUpdate(ref SystemState state)
        {
            var input = SystemAPI.GetSingleton<PlayerInput>();
            var clock = SystemAPI.GetSingleton<GameClock>();
            var tuning = SystemAPI.GetSingleton<TuningSingleton>().Value;
            var s = SystemAPI.GetSingleton<SessionState>();
            int waveCount = SystemAPI.GetSingletonBuffer<ScenarioWave>(true).Length;
            if (waveCount == 0) return;

            if (input.ToggleCommitCondition)
            {
                s.CommitEnabled = !s.CommitEnabled;
                s.Take = 1;
                s.Phase = SessionPhase.PendingStart;
                Telemetry(ref state, PlaytestEventKind.ConditionChanged, s, clock);
            }
            if (input.NextWave)
            {
                s.WaveIndex = (s.WaveIndex + 1) % waveCount;
                s.Take = 1;
                s.Phase = SessionPhase.PendingStart;
            }
            if (input.Retake && s.Phase == SessionPhase.Playing)
            {
                s.Take++;
                s.Phase = SessionPhase.PendingStart;
            }

            switch (s.Phase)
            {
                case SessionPhase.PendingStart:
                    StartWave(ref state, ref s, tuning);
                    s.Phase = SessionPhase.Playing;
                    Feedback(ref state, FeedbackKind.TakeStart, float3.zero, s.Take);
                    Telemetry(ref state, PlaytestEventKind.WaveStart, s, clock);
                    break;

                case SessionPhase.Playing:
                {
                    var player = SystemAPI.GetSingletonEntity<PlayerTag>();
                    var fighter = SystemAPI.GetComponent<Fighter>(player);
                    if (fighter.Health <= 0f)
                    {
                        s.Phase = SessionPhase.NG;
                        s.PhaseTimer = tuning.NgDelay;
                        Feedback(ref state, FeedbackKind.NG, SystemAPI.GetComponent<LocalTransform>(player).Position, s.Take);
                        Telemetry(ref state, PlaytestEventKind.NG, s, clock);
                    }
                    // 最後の敵を倒しても、技の途中なら最後まで見せてから「カット!」
                    else if (_aliveEnemies.IsEmpty && SystemAPI.GetComponent<CutState>(player).Phase != CutPhase.Executing)
                    {
                        s.Phase = SessionPhase.Cleared;
                        s.PhaseTimer = tuning.ClearDelay;
                        Feedback(ref state, FeedbackKind.Clear, SystemAPI.GetComponent<LocalTransform>(player).Position, s.Take);
                        Telemetry(ref state, PlaytestEventKind.WaveClear, s, clock);
                    }
                    break;
                }

                case SessionPhase.NG:
                    s.PhaseTimer -= clock.RealDeltaTime;
                    if (s.PhaseTimer <= 0f)
                    {
                        s.Take++;
                        s.Phase = SessionPhase.PendingStart;
                    }
                    break;

                case SessionPhase.Cleared:
                    s.PhaseTimer -= clock.RealDeltaTime;
                    if (s.PhaseTimer <= 0f)
                    {
                        s.WaveIndex = (s.WaveIndex + 1) % waveCount;
                        s.Take = 1;
                        s.Phase = SessionPhase.PendingStart;
                    }
                    break;
            }

            SystemAPI.SetSingleton(s);
        }

        void StartWave(ref SystemState state, ref SessionState s, in Tuning tuning)
        {
            var em = state.EntityManager;
            var player = SystemAPI.GetSingletonEntity<PlayerTag>();

            // 途中で終わったカット(NG で切れた多ポーズ技など)も1サンプルとして残す
            var cut = em.GetComponentData<CutState>(player);
            if (cut.Phase == CutPhase.Executing || cut.Phase == CutPhase.Commit)
            {
                SystemAPI.GetSingletonBuffer<TelemetryEvent>().Add(new TelemetryEvent { Value = cut.Record });
            }
            else if (cut.Phase == CutPhase.Slow)
            {
                // スロー中にテイクし直した: 固定費を払ったスロー使用として残す(平均ポーズ数には入れない)
                var r = cut.Record;
                r.Kind = PlaytestEventKind.CutCancelled;
                r.SlowSeconds = cut.SlowRealElapsed;
                SystemAPI.GetSingletonBuffer<TelemetryEvent>().Add(new TelemetryEvent { Value = r });
            }

            using (var leftovers = _resettable.ToEntityArray(Allocator.Temp))
                em.DestroyEntity(leftovers);

            var wave = SystemAPI.GetSingletonBuffer<ScenarioWave>(true)[s.WaveIndex];
            var lib = SystemAPI.GetSingleton<PrefabLibrary>();

            // --- 主人公を初期状態へ ---
            var lt = em.GetComponentData<LocalTransform>(player);
            lt.Position = wave.PlayerStart;
            lt.Rotation = quaternion.LookRotationSafe(new float3(1, 0, 0), math.up());
            em.SetComponentData(player, lt);
            var fighter = em.GetComponentData<Fighter>(player);
            fighter.MaxHealth = tuning.PlayerMaxHealth;
            fighter.Health = tuning.PlayerMaxHealth;
            fighter.Radius = tuning.PlayerRadius;
            fighter.Facing = new float3(1, 0, 0);
            fighter.HitStun = fighter.Invulnerable = fighter.Flash = 0f;
            em.SetComponentData(player, fighter);
            em.SetComponentData(player, new FilmGauge { Value = math.min(tuning.GaugeAtWaveStart, tuning.GaugeMax), Max = tuning.GaugeMax });
            em.SetComponentData(player, new PlayerAction());
            em.SetComponentData(player, new Knockback());
            em.SetComponentData(player, new VerticalBody());
            em.SetComponentData(player, new CutState { SelectedKind = cut.SelectedKind });
            var placed = em.GetBuffer<PlacedPose>(player);
            state.World.GetOrCreateSystemManaged<CutMotionService>().Discard(placed);
            placed.Clear();

            SystemAPI.GetSingletonRW<HitStop>().ValueRW.Remaining = 0f;
            SystemAPI.GetSingletonBuffer<DamageRequest>().Clear();

            // --- 敵を配置 ---
            for (int i = 0; i < wave.SpawnCount; i++)
            {
                float3 pos = SystemAPI.GetSingletonBuffer<ScenarioSpawn>(true)[wave.FirstSpawn + i].Position;
                var e = em.Instantiate(lib.Enemy);
                var elt = em.GetComponentData<LocalTransform>(e);
                elt.Position = pos;
                float3 toPlayer = wave.PlayerStart - pos;
                toPlayer.y = 0f;
                float3 facing = math.normalizesafe(toPlayer, new float3(-1, 0, 0));
                elt.Rotation = quaternion.LookRotationSafe(facing, math.up());
                em.SetComponentData(e, elt);

                var ef = em.GetComponentData<Fighter>(e);
                ef.MaxHealth = ef.Health = tuning.EnemyMaxHealth;
                ef.Radius = tuning.EnemyRadius;
                ef.Facing = facing;
                em.SetComponentData(e, ef);

                // 同じウェーブ・同じテイク番号なら同じ乱数列(条件比較で敵の動きを揃える)
                var rng = Random.CreateFromIndex((uint)(s.WaveIndex * 97 + i * 13 + 1));
                em.SetComponentData(e, new EnemyBrain
                {
                    State = EnemyState.Approach,
                    // 1発目は少し待たせる: 開幕に読む時間を作る
                    Cooldown = rng.NextFloat(tuning.EnemyCooldownMin, tuning.EnemyCooldownMax) + 0.6f + i * 0.35f,
                    Random = rng,
                });
            }
        }

        void Feedback(ref SystemState state, FeedbackKind kind, float3 pos, int extra)
            => SystemAPI.GetSingletonBuffer<FeedbackEvent>().Add(new FeedbackEvent { Kind = kind, Position = pos, Extra = extra });

        void Telemetry(ref SystemState state, PlaytestEventKind kind, in SessionState s, in GameClock clock)
        {
            var waves = SystemAPI.GetSingletonBuffer<ScenarioWave>(true);
            int formation = s.WaveIndex < waves.Length ? waves[s.WaveIndex].SpawnCount : 0;
            float hp = 0f;
            foreach (var f in SystemAPI.Query<RefRO<Fighter>>().WithAll<PlayerTag>()) hp = f.ValueRO.Health;
            SystemAPI.GetSingletonBuffer<TelemetryEvent>().Add(new TelemetryEvent
            {
                Value = new PlaytestEvent
                {
                    Kind = kind,
                    Time = clock.ElapsedReal,
                    Wave = s.WaveIndex,
                    FormationEnemies = formation,
                    Take = s.Take,
                    CommitEnabled = s.CommitEnabled,
                    EnemiesAlive = _aliveEnemies.CalculateEntityCount(),
                    PlayerHealth = hp,
                },
            });
        }
    }
}
