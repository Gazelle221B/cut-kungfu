using CutKungfu.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 積まれた攻撃判定(DamageRequest)をまとめて解決する。
    /// 捌き・無敵・ゲージ回収・映え点・NG理由・ヒットストップ・揺れ・判定の見える化は、ここ1か所で決める。
    /// </summary>
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(EnemyAISystem))]
    public partial struct DamageSystem : ISystem
    {
        static readonly float4 PlayerStrikeColor = new float4(1f, 0.85f, 0.4f, 0.55f);
        static readonly float4 CutStrikeColor = new float4(0.6f, 0.95f, 1f, 0.6f);
        static readonly float4 EnemyStrikeColor = new float4(1f, 0.35f, 0.2f, 0.5f);

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DamageRequest>();
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<PrefabLibrary>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var buffer = SystemAPI.GetSingletonBuffer<DamageRequest>();
            if (buffer.Length == 0) return;
            var requests = buffer.ToNativeArray(Allocator.Temp);
            buffer.Clear();

            var tuning = SystemAPI.GetSingleton<TuningSingleton>().Value;
            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var fxPrefab = SystemAPI.GetSingleton<PrefabLibrary>().StrikeFx;

            for (int i = 0; i < requests.Length; i++)
            {
                var req = requests[i];
                if (req.AttackerTeam == Team.Player) ResolveAgainstEnemies(ref state, req, player, tuning, ref ecb);
                else ResolveAgainstPlayer(ref state, req, player, tuning);
                SpawnStrikeFx(ref ecb, fxPrefab, req);
            }

            ecb.Playback(state.EntityManager);
        }

        void ResolveAgainstEnemies(ref SystemState state, in DamageRequest req, Entity player, in Tuning tuning, ref EntityCommandBuffer ecb)
        {
            var feedback = SystemAPI.GetSingletonBuffer<FeedbackEvent>();
            ref var hitStop = ref SystemAPI.GetSingletonRW<HitStop>().ValueRW;
            ref var shake = ref SystemAPI.GetSingletonRW<CameraShake>().ValueRW;
            ref var cut = ref SystemAPI.GetComponentRW<CutState>(player).ValueRW;
            ref var gauge = ref SystemAPI.GetComponentRW<FilmGauge>(player).ValueRW;
            ref var session = ref SystemAPI.GetSingletonRW<SessionState>().ValueRW;

            foreach (var (fRef, ltRef, kbRef, entity) in
                     SystemAPI.Query<RefRW<Fighter>, RefRO<LocalTransform>, RefRW<Knockback>>()
                         .WithAll<EnemyTag>().WithNone<Dying>().WithEntityAccess())
            {
                ref var f = ref fRef.ValueRW;
                if (f.Health <= 0f) continue;
                float2 p = ltRef.ValueRO.Position.xz;
                if (!Geometry2D.CapsuleOverlapsCircle(req.A, req.B, req.Radius, p, f.Radius)) continue;

                bool isCut = req.Kind == StrikeKind.Cut;
                float damage = req.Damage;
                bool capped = false;
                float hitStun = req.HitStun;
                if (isCut && CutRules.CountsTowardAreaCap(tuning, req.Brick, isAimedTarget: entity == req.AimedTarget))
                {
                    damage = CapAreaDamage(ref state, entity, f.MaxHealth, req.Damage, tuning, out capped);
                    // 範囲技で当てた敵の被弾硬直は別の値(予備動作を潰すかどうかを調整できる)
                    hitStun = tuning.EnemyAreaHitStun;
                }

                f.Health -= damage;
                f.Flash = 0.12f;
                f.HitStun = math.max(f.HitStun, hitStun);
                kbRef.ValueRW.Velocity = Push(req, p) * req.Knockback;
                hitStop.Request(req.HitStop);
                shake.Add(isCut ? 0.35f : 0.12f);

                float hae = 0f;
                if (isCut)
                {
                    hae = CutRules.HaePoints(tuning, req.Brick, damage);
                    if (capped) cut.Record.AreaCapHits++;
                    cut.Record.Hae += hae;
                    session.Score += hae;
                }
                else
                {
                    gauge.Add(tuning.GaugeOnHit);
                }

                feedback.Add(new FeedbackEvent
                {
                    Kind = capped ? FeedbackKind.CutHitCapped : isCut ? FeedbackKind.CutHit : FeedbackKind.Hit,
                    Position = ltRef.ValueRO.Position,
                    Value = damage,
                    Extra = (int)math.round(hae),
                });

                if (f.Health <= 0f)
                {
                    float3 push = Push(req, p);
                    // 吹き飛ぶ向きへ倒れる: 進行方向と上方向の外積が回転軸
                    float3 axis = math.normalizesafe(math.cross(math.up(), push), new float3(0, 0, 1));
                    ecb.AddComponent(entity, new Dying { FallAxis = axis });
                    feedback.Add(new FeedbackEvent { Kind = FeedbackKind.Kill, Position = ltRef.ValueRO.Position });
                    if (isCut) cut.Record.Kills++;
                }
            }
        }

        void ResolveAgainstPlayer(ref SystemState state, in DamageRequest req, Entity player, in Tuning tuning)
        {
            // 同じ更新で先に潰された(倒された・怯んだ)攻撃者の攻撃は出ない。「先に潰す」を成立させる
            if (SystemAPI.HasComponent<Fighter>(req.Attacker))
            {
                var attacker = SystemAPI.GetComponent<Fighter>(req.Attacker);
                if (attacker.Health <= 0f || attacker.HitStun > 0f || SystemAPI.HasComponent<Dying>(req.Attacker)) return;
            }

            var lt = SystemAPI.GetComponent<LocalTransform>(player);
            ref var f = ref SystemAPI.GetComponentRW<Fighter>(player).ValueRW;
            if (f.Health <= 0f) return;
            if (!Geometry2D.CapsuleOverlapsCircle(req.A, req.B, req.Radius, lt.Position.xz, f.Radius)) return;

            ref var cut = ref SystemAPI.GetComponentRW<CutState>(player).ValueRW;
            if (f.Invulnerable > 0f) return;
            if (cut.Phase == CutPhase.Slow && tuning.InvulnerableDuringSlow) return;
            if (cut.Phase == CutPhase.Executing && tuning.InvulnerableDuringExecution) return;

            var feedback = SystemAPI.GetSingletonBuffer<FeedbackEvent>();
            ref var hitStop = ref SystemAPI.GetSingletonRW<HitStop>().ValueRW;
            ref var shake = ref SystemAPI.GetSingletonRW<CameraShake>().ValueRW;
            var action = SystemAPI.GetComponent<PlayerAction>(player);

            if (cut.Phase == CutPhase.None && action.IsParryActive(tuning.ParryWindow))
            {
                // 捌き成功: 相手をよろけさせ、ゲージを大きく回収する
                SystemAPI.GetComponentRW<FilmGauge>(player).ValueRW.Add(tuning.GaugeOnParry);
                if (SystemAPI.HasComponent<EnemyBrain>(req.Attacker))
                {
                    ref var brain = ref SystemAPI.GetComponentRW<EnemyBrain>(req.Attacker).ValueRW;
                    brain.State = EnemyState.Staggered;
                    brain.Timer = tuning.EnemyStaggerOnParry;
                    var attackerPos = SystemAPI.GetComponent<LocalTransform>(req.Attacker).Position;
                    float2 away = math.normalizesafe(attackerPos.xz - lt.Position.xz, -req.FallbackDirection);
                    SystemAPI.GetComponentRW<Knockback>(req.Attacker).ValueRW.Velocity = new float3(away.x, 0f, away.y) * 4f;
                }
                hitStop.Request(tuning.HitStopParry);
                shake.Add(0.2f);
                feedback.Add(new FeedbackEvent { Kind = FeedbackKind.Parry, Position = lt.Position, Value = tuning.GaugeOnParry });
                return;
            }

            bool duringCommit = cut.IsCommitted;
            // 硬直中はカウンターヒット。硬直の罰を体力でも見せる
            float damage = req.Damage * (duringCommit ? tuning.CounterHitMultiplier : 1f);
            f.Health = math.max(0f, f.Health - damage);
            f.HitStun = req.HitStun;
            f.Invulnerable = tuning.PlayerPostHitInvulnerable;
            f.Flash = 0.15f;
            SystemAPI.GetComponentRW<Knockback>(player).ValueRW.Velocity = Push(req, lt.Position.xz) * req.Knockback;
            hitStop.Request(req.HitStop);
            shake.Add(0.45f);

            // 殴られても硬直は短くならない(被弾で救済しない)。記録だけ残す
            if (duringCommit) cut.Record.HitDuringCommit = true;

            ref var session = ref SystemAPI.GetSingletonRW<SessionState>().ValueRW;
            if (f.Health <= 0f) session.LastNgReason = duringCommit ? NgReason.HitDuringCommit : NgReason.HitWhileFree;

            feedback.Add(new FeedbackEvent
            {
                Kind = FeedbackKind.PlayerHurt, Position = lt.Position, Value = damage, Extra = duringCommit ? 1 : 0,
            });

            var clock = SystemAPI.GetSingleton<GameClock>();
            SystemAPI.GetSingletonBuffer<TelemetryEvent>().Add(new TelemetryEvent
            {
                Value = new PlaytestEvent
                {
                    Kind = PlaytestEventKind.PlayerHit,
                    Time = clock.ElapsedReal,
                    Wave = session.WaveIndex,
                    FormationEnemies = FormationSize(ref state, session.WaveIndex),
                    Take = session.Take,
                    CommitEnabled = session.CommitEnabled,
                    Poses = duringCommit ? cut.PoseCount : 0,
                    CommitTime = duringCommit ? cut.CommitDuration : 0f,
                    HitDuringCommit = duringCommit,
                    PlayerHealth = f.Health,
                },
            });
        }

        /// <summary>範囲技の1打を、この技でその敵へ与えた範囲技ダメージの台帳と照らして削る。</summary>
        float CapAreaDamage(ref SystemState state, Entity enemy, float maxHealth, float damage, in Tuning tuning, out bool capped)
        {
            var ledger = SystemAPI.GetSingletonBuffer<CutAreaDamage>();
            int index = -1;
            for (int i = 0; i < ledger.Length; i++)
                if (ledger[i].Enemy == enemy) { index = i; break; }
            float taken = index >= 0 ? ledger[index].Taken : 0f;
            float applied = CutRules.CapAreaDamage(tuning, maxHealth, taken, damage, out capped);
            if (index >= 0) ledger[index] = new CutAreaDamage { Enemy = enemy, Taken = taken + applied };
            else ledger.Add(new CutAreaDamage { Enemy = enemy, Taken = applied });
            return applied;
        }

        int FormationSize(ref SystemState state, int wave)
        {
            var waves = SystemAPI.GetSingletonBuffer<ScenarioWave>(true);
            return wave >= 0 && wave < waves.Length ? waves[wave].SpawnCount : 0;
        }

        static float3 Push(in DamageRequest req, float2 target)
        {
            float2 ab = req.B - req.A;
            float lenSq = math.lengthsq(ab);
            float t = lenSq < 1e-8f ? 0f : math.saturate(math.dot(target - req.A, ab) / lenSq);
            float2 d = math.normalizesafe(target - (req.A + ab * t), math.normalizesafe(req.FallbackDirection, new float2(1, 0)));
            return new float3(d.x, 0f, d.y);
        }

        static void SpawnStrikeFx(ref EntityCommandBuffer ecb, Entity prefab, in DamageRequest req)
        {
            if (prefab == Entity.Null) return;
            float4 color = req.Kind switch
            {
                StrikeKind.Cut => CutStrikeColor,
                StrikeKind.EnemyStrike => EnemyStrikeColor,
                _ => PlayerStrikeColor,
            };
            float2 c = req.B;
            float diameter = req.Radius * 2f;
            var e = ecb.Instantiate(prefab);
            ecb.SetComponent(e, LocalTransform.FromPositionRotationScale(new float3(c.x, 0.05f, c.y), quaternion.identity, diameter));
            ecb.SetComponent(e, new StrikeFx { Life = 0.18f, MaxLife = 0.18f, StartScale = diameter * 0.6f, EndScale = diameter });
            ecb.SetComponent(e, new URPMaterialPropertyBaseColor { Value = color });
        }
    }
}
