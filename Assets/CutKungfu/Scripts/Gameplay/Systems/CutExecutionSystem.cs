using CutKungfu.Core;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 「切る」: 置いたポーズを順に辿り、到達ごとに1打を出す。最後のポーズの後はコミット時間だけ動けない。
    /// 動きの生成は <see cref="CutMotion"/>(MotionBricks の差し替え口)に任せ、ここは区間の進行と判定だけを持つ。
    /// </summary>
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(CutControlSystem))]
    public partial struct CutExecutionSystem : ISystem
    {
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
            var cut = SystemAPI.GetComponent<CutState>(player);
            if (cut.Phase != CutPhase.Executing && cut.Phase != CutPhase.Commit) return;

            var tuning = SystemAPI.GetSingleton<TuningSingleton>().Value;
            float dt = SystemAPI.GetSingleton<GameClock>().DeltaTime;
            var poses = SystemAPI.GetBuffer<PlacedPose>(player);
            Entity ghostToRemove = Entity.Null;

            if (cut.Phase == CutPhase.Executing)
            {
                var pose = poses[cut.SegmentIndex];
                var brick = tuning.GetBrick(pose.Kind);
                cut.SegmentTime += dt;
                float t = cut.SegmentTime / math.max(1e-4f, cut.SegmentDuration);

                ref var lt = ref SystemAPI.GetComponentRW<LocalTransform>(player).ValueRW;
                ref var fighter = ref SystemAPI.GetComponentRW<Fighter>(player).ValueRW;
                lt.Position = CutMotion.Evaluate(cut.SegmentFrom, pose.Position, brick.ArcHeight, t);
                float3 travel = pose.Position - cut.SegmentFrom;
                travel.y = 0f;
                if (math.lengthsq(travel) > 1e-4f) fighter.Facing = math.normalize(travel);
                lt.Rotation = quaternion.LookRotationSafe(fighter.Facing, math.up());

                if (t >= 1f)
                {
                    float2 center = pose.Position.xz + fighter.Facing.xz * brick.StrikeForward;
                    SystemAPI.GetSingletonBuffer<DamageRequest>().Add(new DamageRequest
                    {
                        Attacker = player,
                        AttackerTeam = Team.Player,
                        Kind = StrikeKind.Cut,
                        Brick = pose.Kind,
                        A = brick.SweepPath ? cut.SegmentFrom.xz : center,
                        B = center,
                        Radius = brick.StrikeRadius,
                        Damage = CutRules.StrikeDamage(tuning, pose.Kind, cut.PoseCount),
                        Knockback = brick.Knockback,
                        HitStun = tuning.EnemyCutHitStun,
                        HitStop = tuning.HitStopCut,
                        FallbackDirection = fighter.Facing.xz,
                    });
                    ghostToRemove = pose.Ghost;

                    cut.SegmentIndex++;
                    if (cut.SegmentIndex < cut.PoseCount)
                    {
                        cut.SegmentFrom = pose.Position;
                        cut.SegmentTime = 0f;
                        var next = poses[cut.SegmentIndex];
                        cut.SegmentDuration = CutRules.SegmentDuration(tuning, math.distance(pose.Position.xz, next.Position.xz));
                    }
                    else
                    {
                        cut.Phase = CutPhase.Commit;
                        cut.CommitRemaining = cut.CommitDuration;
                    }
                }
            }
            else
            {
                cut.CommitRemaining -= dt;
                if (cut.CommitRemaining <= 0f)
                {
                    SystemAPI.GetSingletonBuffer<TelemetryEvent>().Add(new TelemetryEvent { Value = cut.Record });
                    float3 pos = SystemAPI.GetComponent<LocalTransform>(player).Position;
                    SystemAPI.GetSingletonBuffer<FeedbackEvent>().Add(new FeedbackEvent
                    {
                        Kind = FeedbackKind.CommitEnd, Position = pos, Value = cut.Record.Hae, Extra = cut.Record.Kills,
                    });
                    poses.Clear();
                    var kind = cut.SelectedKind;
                    cut = new CutState { SelectedKind = kind };
                }
            }

            SystemAPI.SetComponent(player, cut);
            if (ghostToRemove != Entity.Null && state.EntityManager.Exists(ghostToRemove))
                state.EntityManager.DestroyEntity(ghostToRemove);
        }
    }
}
