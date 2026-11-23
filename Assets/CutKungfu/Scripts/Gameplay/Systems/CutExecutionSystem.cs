using CutKungfu.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 「切る」: 置いたポーズを順に辿り、接触フレームごとに1打を出す。最後のポーズの後はコミット時間だけ動けない。
    /// 動きの生成は <see cref="CutMotion"/>(MotionBricks の差し替え口)に任せ、ここは区間の進行・判定・中断だけを持つ。
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
            var fighter = SystemAPI.GetComponent<Fighter>(player);
            var toDestroy = new NativeList<Entity>(4, Allocator.Temp);

            if (cut.Phase == CutPhase.Executing && fighter.HitStun > 0f)
            {
                // 実行中に殴られた: 残りのポーズを捨てて被弾に移る(TDD 7章「中断」)
                var poses = SystemAPI.GetBuffer<PlacedPose>(player);
                for (int i = cut.SegmentIndex; i < poses.Length; i++) toDestroy.Add(poses[i].Ghost);
                cut.Record.Interrupted = true;
                Finish(ref state, player, ref cut);
            }
            else if (cut.Phase == CutPhase.Executing)
            {
                Advance(ref state, player, ref cut, ref fighter, tuning, dt, ref toDestroy);
                SystemAPI.SetComponent(player, fighter);
            }
            else
            {
                cut.CommitRemaining -= dt;
                if (cut.CommitRemaining <= 0f) Finish(ref state, player, ref cut);
            }

            SystemAPI.SetComponent(player, cut);
            foreach (var e in toDestroy)
                if (e != Entity.Null && state.EntityManager.Exists(e)) state.EntityManager.DestroyEntity(e);
        }

        void Advance(ref SystemState state, Entity player, ref CutState cut, ref Fighter fighter, in Tuning tuning, float dt,
            ref NativeList<Entity> toDestroy)
        {
            var poses = SystemAPI.GetBuffer<PlacedPose>(player);
            var pose = poses[cut.SegmentIndex];
            var brick = tuning.GetBrick(pose.Kind);
            cut.SegmentTime += dt;

            var sample = CutMotion.Sample(new MotionSegment
            {
                From = cut.SegmentFrom,
                FromYaw = cut.SegmentFromYaw,
                To = pose.Position,
                ToYaw = pose.Yaw,
                ArcHeight = brick.ArcHeight,
                Duration = cut.SegmentDuration,
            }, cut.SegmentTime);

            var lt = SystemAPI.GetComponent<LocalTransform>(player);
            lt.Position = sample.Position;
            lt.Rotation = sample.Rotation;
            SystemAPI.SetComponent(player, lt);
            fighter.Facing = math.mul(sample.Rotation, new float3(0, 0, 1));

            if (sample.Contact && !cut.StrikeFired)
            {
                cut.StrikeFired = true;
                Strike(ref state, player, cut, pose, brick, tuning, fighter.Facing);
                toDestroy.Add(pose.Ghost);
            }

            if (!sample.Finished) return;

            // 区間を越えた分の時間は次の区間(または硬直)に持ち越す。捨てると、1回の更新が長いほど技が遅れる
            float overshoot = math.max(0f, cut.SegmentTime - cut.SegmentDuration);
            cut.SegmentIndex++;
            cut.StrikeFired = false;
            if (cut.SegmentIndex < cut.PoseCount)
            {
                var next = poses[cut.SegmentIndex];
                cut.SegmentFrom = pose.Position;
                cut.SegmentFromYaw = pose.Yaw;
                cut.SegmentTime = overshoot;
                cut.SegmentDuration = CutRules.SegmentDuration(tuning, math.distance(pose.Position.xz, next.Position.xz));
            }
            else
            {
                cut.Phase = CutPhase.Commit;
                // 硬直 0 秒(条件 B)でも記録の確定は次の更新で行う(最後の1打の撃破数・映え点を DamageSystem が書き込むため)。
                // その1フレームを硬直扱いしないのは DamageSystem 側の責任(CommitDuration > 0 のときだけ硬直とみなす)
                cut.CommitRemaining = cut.CommitDuration - overshoot;
            }
        }

        void Strike(ref SystemState state, Entity player, in CutState cut, in PlacedPose pose, in BrickTuning brick, in Tuning tuning, float3 facing)
        {
            float2 center = pose.Position.xz + facing.xz * brick.StrikeForward;
            bool found = NearestEnemy(ref state, center, out float2 nearest, out Entity nearestEntity);
            // 命中補正が効く距離にいる最寄りの敵を「到達点で狙った敵」とする(飛び蹴りの巻き込みと区別するため)
            Entity aimed = found && math.distance(nearest, center) <= brick.StrikeRadius + tuning.StrikeAssist ? nearestEntity : Entity.Null;
            center = CutRules.AssistStrikeCenter(center, brick.StrikeRadius, tuning.StrikeAssist, nearest, found);
            SystemAPI.GetSingletonBuffer<DamageRequest>().Add(new DamageRequest
            {
                Attacker = player,
                AttackerTeam = Team.Player,
                Kind = StrikeKind.Cut,
                Brick = pose.Kind,
                A = brick.SweepPath ? cut.SegmentFrom.xz : center,
                B = center,
                Radius = brick.StrikeRadius,
                Damage = CutRules.StrikeDamage(tuning, pose.Kind, cut.PoseCount, pose.RepeatIndex),
                Knockback = brick.Knockback,
                HitStun = tuning.EnemyCutHitStun,
                HitStop = tuning.HitStopCut,
                FallbackDirection = facing.xz,
                AimedTarget = aimed,
            });
        }

        /// <summary>カットを終えて記録を確定し、置いたポーズを片付ける。</summary>
        void Finish(ref SystemState state, Entity player, ref CutState cut)
        {
            SystemAPI.GetSingletonBuffer<TelemetryEvent>().Add(new TelemetryEvent { Value = cut.Record });
            float3 pos = SystemAPI.GetComponent<LocalTransform>(player).Position;
            SystemAPI.GetSingletonBuffer<FeedbackEvent>().Add(new FeedbackEvent
            {
                Kind = FeedbackKind.CommitEnd, Position = pos, Value = cut.Record.Hae, Extra = cut.Record.Kills,
            });
            SystemAPI.GetBuffer<PlacedPose>(player).Clear();
            cut = new CutState { SelectedKind = cut.SelectedKind };
        }

        bool NearestEnemy(ref SystemState state, float2 from, out float2 nearest, out Entity entity)
        {
            nearest = default;
            entity = Entity.Null;
            float best = float.MaxValue;
            foreach (var (lt, e) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<EnemyTag>().WithNone<Dying>().WithEntityAccess())
            {
                float d = math.distancesq(lt.ValueRO.Position.xz, from);
                if (d < best) { best = d; nearest = lt.ValueRO.Position.xz; entity = e; }
            }
            return best < float.MaxValue;
        }
    }
}
