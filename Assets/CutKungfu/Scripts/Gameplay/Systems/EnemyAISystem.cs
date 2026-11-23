using CutKungfu.Core;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 雑魚(素手)の行動: 間合いを詰める → 予備動作 → 殴る → 戻り。
    /// 同時に予備動作に入れる数を絞り、「誰が次に殴ってくるか」を読めるようにする(柱3)。
    /// 状態のタイマーが更新の途中で切れたら、切れた後の時間は次の状態に持ち越す(切り替わりを更新の区切りに揃えない。
    /// 揃えると、1回の更新が長いほど行動が遅れる)。
    /// </summary>
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(PlayerControlSystem))]
    public partial struct EnemyAISystem : ISystem
    {
        /// <summary>予備動作の終わりこの割合は向きを固定する(避けられる余地を作る)。</summary>
        const float WindupLockRatio = 0.35f;

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

            var tuning = SystemAPI.GetSingleton<TuningSingleton>().Value;
            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            float3 target = SystemAPI.GetComponent<LocalTransform>(player).Position;
            var requests = SystemAPI.GetSingletonBuffer<DamageRequest>();

            int windups = 0;
            foreach (var b in SystemAPI.Query<RefRO<EnemyBrain>>().WithNone<Dying>())
                if (b.ValueRO.State == EnemyState.Windup) windups++;

            foreach (var (brainRef, fighterRef, ltRef, self) in
                     SystemAPI.Query<RefRW<EnemyBrain>, RefRW<Fighter>, RefRW<LocalTransform>>()
                         .WithAll<EnemyTag>().WithNone<Dying>().WithEntityAccess())
            {
                ref var brain = ref brainRef.ValueRW;
                ref var f = ref fighterRef.ValueRW;
                ref var lt = ref ltRef.ValueRW;

                float3 toPlayer = target - lt.Position;
                toPlayer.y = 0f;
                float dist = math.length(toPlayer);
                float3 dirToPlayer = dist > 1e-4f ? toPlayer / dist : f.Facing;

                if (f.HitStun > 0f)
                {
                    // 殴られたら予備動作は潰れる(先に潰す、という選択肢)
                    if (brain.State == EnemyState.Windup)
                    {
                        brain.State = EnemyState.Recover;
                        brain.Timer = tuning.EnemyRecover;
                    }
                    continue;
                }
                // 硬直がこの更新の途中で明けたなら、明けた後の時間だけ動く
                float dt = f.FreeTime;

                // 技の打撃を受けた敵: 硬直が明けたら、残っていた待ちに倍率を掛ける(戻り中なら新しい待ちを引くときに掛ける)
                if (brain.HitByCut && brain.State == EnemyState.Approach)
                {
                    brain.Cooldown *= tuning.EnemyWaitScaleAfterCutHit;
                    brain.HitByCut = false;
                }

                switch (brain.State)
                {
                    case EnemyState.Approach:
                        brain.Cooldown -= dt;
                        f.Facing = dirToPlayer;
                        float gap = dist - tuning.EnemyEngageRange;
                        if (gap > 0.05f) lt.Position += dirToPlayer * math.min(gap, tuning.EnemySpeed * dt);
                        else if (gap < -0.4f) lt.Position -= dirToPlayer * math.min(-gap, tuning.EnemySpeed * 0.5f * dt);

                        if (brain.Cooldown <= 0f && gap <= 0.35f && windups < tuning.EnemyMaxConcurrentWindups)
                        {
                            brain.State = EnemyState.Windup;
                            // 待ちがこの更新の途中で切れていたら、その後の時間は予備動作に回す(待たされていた分は回さない)
                            brain.Timer = tuning.EnemyWindup - math.min(-brain.Cooldown, dt);
                            windups++;
                        }
                        break;

                    case EnemyState.Windup:
                        brain.Timer -= dt;
                        if (brain.Timer > tuning.EnemyWindup * WindupLockRatio) f.Facing = dirToPlayer;
                        if (brain.Timer <= 0f)
                        {
                            float2 c = lt.Position.xz + f.Facing.xz * tuning.EnemyStrikeForward;
                            requests.Add(new DamageRequest
                            {
                                Attacker = self,
                                AttackerTeam = Team.Enemy,
                                Kind = StrikeKind.EnemyStrike,
                                A = c,
                                B = c,
                                Radius = tuning.EnemyStrikeRadius,
                                Damage = tuning.EnemyDamage,
                                Knockback = tuning.EnemyKnockback,
                                HitStun = tuning.PlayerHitStun,
                                HitStop = tuning.HitStopPlayerHurt,
                                FallbackDirection = f.Facing.xz,
                            });
                            brain.State = EnemyState.Recover;
                            brain.Timer += tuning.EnemyRecover;
                        }
                        break;

                    case EnemyState.Recover:
                        brain.Timer -= dt;
                        if (brain.Timer <= 0f)
                        {
                            brain.State = EnemyState.Approach;
                            brain.Cooldown = brain.Random.NextFloat(tuning.EnemyCooldownMin, tuning.EnemyCooldownMax);
                            if (brain.HitByCut)
                            {
                                brain.Cooldown *= tuning.EnemyWaitScaleAfterCutHit;
                                brain.HitByCut = false;
                            }
                            brain.Cooldown += brain.Timer;
                        }
                        break;

                    case EnemyState.Staggered:
                        brain.Timer -= dt;
                        if (brain.Timer <= 0f)
                        {
                            brain.State = EnemyState.Approach;
                            brain.Cooldown = brain.Random.NextFloat(tuning.EnemyCooldownMin, tuning.EnemyCooldownMax) * 0.5f + brain.Timer;
                        }
                        break;
                }

                lt.Rotation = quaternion.LookRotationSafe(f.Facing, math.up());
            }
        }
    }
}
