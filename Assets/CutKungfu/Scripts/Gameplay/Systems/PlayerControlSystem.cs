using CutKungfu.Core;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 通常の攻防(打つ・捌く・避ける)。GDD 4章では従来アニメ側。カット中は何もしない。
    /// </summary>
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(CutExecutionSystem))]
    public partial struct PlayerControlSystem : ISystem
    {
        /// <summary>攻撃の向きを近くの敵へ寄せる距離[m](狙いやすさの補正)。</summary>
        const float AutoFaceRange = 2.4f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<SessionState>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.GetSingleton<SessionState>().Phase != SessionPhase.Playing) return;

            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            if (SystemAPI.GetComponent<CutState>(player).IsActive) return;

            var input = SystemAPI.GetSingleton<PlayerInput>();
            var tuning = SystemAPI.GetSingleton<TuningSingleton>().Value;
            float dt = SystemAPI.GetSingleton<GameClock>().DeltaTime;

            ref var lt = ref SystemAPI.GetComponentRW<LocalTransform>(player).ValueRW;
            ref var fighter = ref SystemAPI.GetComponentRW<Fighter>(player).ValueRW;
            ref var action = ref SystemAPI.GetComponentRW<PlayerAction>(player).ValueRW;

            if (fighter.HitStun > 0f)
            {
                action = default;
                return;
            }

            float2 move = input.Move;
            if (math.lengthsq(move) > 1f) move = math.normalize(move);
            float3 moveDir = new float3(move.x, 0f, move.y);

            switch (action.State)
            {
                case PlayerActionState.Attack:
                    action.Timer += dt;
                    if (!action.StrikeFired && action.Timer >= tuning.AttackStartup)
                    {
                        action.StrikeFired = true;
                        float2 c = lt.Position.xz + fighter.Facing.xz * tuning.AttackForward;
                        SystemAPI.GetSingletonBuffer<DamageRequest>().Add(new DamageRequest
                        {
                            Attacker = player,
                            AttackerTeam = Team.Player,
                            Kind = StrikeKind.Light,
                            A = c,
                            B = c,
                            Radius = tuning.AttackRadius,
                            Damage = tuning.AttackDamage,
                            Knockback = tuning.AttackKnockback,
                            HitStun = tuning.EnemyHitStun,
                            HitStop = tuning.HitStopLight,
                            FallbackDirection = fighter.Facing.xz,
                        });
                    }
                    // 攻撃中もわずかに踏み込める
                    lt.Position += moveDir * tuning.PlayerSpeed * 0.25f * dt;
                    if (action.Timer >= tuning.AttackStartup + tuning.AttackRecovery) action = default;
                    break;

                case PlayerActionState.Parry:
                    action.Timer += dt;
                    if (action.Timer >= tuning.ParryWindow + tuning.ParryRecovery) action = default;
                    break;

                case PlayerActionState.Dodge:
                    action.Timer += dt;
                    lt.Position += action.DodgeDirection * tuning.DodgeSpeed * dt;
                    if (action.Timer >= tuning.DodgeDuration) action = default;
                    break;

                default:
                    lt.Position += moveDir * tuning.PlayerSpeed * dt;
                    if (math.lengthsq(moveDir) > 0.01f) fighter.Facing = math.normalize(moveDir);

                    if (input.Primary)
                    {
                        FaceNearestEnemy(ref state, lt.Position, ref fighter);
                        action = new PlayerAction { State = PlayerActionState.Attack };
                    }
                    else if (input.Secondary)
                    {
                        FaceNearestEnemy(ref state, lt.Position, ref fighter);
                        action = new PlayerAction { State = PlayerActionState.Parry };
                    }
                    else if (input.Dodge)
                    {
                        action = new PlayerAction
                        {
                            State = PlayerActionState.Dodge,
                            DodgeDirection = math.lengthsq(moveDir) > 0.01f ? math.normalize(moveDir) : -fighter.Facing,
                        };
                        fighter.Invulnerable = math.max(fighter.Invulnerable, tuning.DodgeInvulnerable);
                    }
                    break;
            }

            lt.Rotation = quaternion.LookRotationSafe(fighter.Facing, math.up());
        }

        void FaceNearestEnemy(ref SystemState state, float3 from, ref Fighter fighter)
        {
            float best = AutoFaceRange * AutoFaceRange;
            float3 dir = float3.zero;
            foreach (var lt in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<EnemyTag>().WithNone<Dying>())
            {
                float3 d = lt.ValueRO.Position - from;
                d.y = 0f;
                float dsq = math.lengthsq(d);
                if (dsq < best) { best = dsq; dir = d; }
            }
            if (math.lengthsq(dir) > 1e-4f) fighter.Facing = math.normalize(dir);
        }
    }
}
