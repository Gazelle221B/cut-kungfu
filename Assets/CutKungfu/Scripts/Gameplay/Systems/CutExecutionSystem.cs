using CutKungfu.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 「切る」: 置いたときに計画した動き(<see cref="ICutMotionSource"/>)を順に再生し、接触の時刻に1打を出す。
    /// 区間の長さと接触の時刻は生成側が決める。最後の区間の後はコミット時間だけ動けない。
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

        // 動作生成ソース(managed)を呼ぶので Burst にしない
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.GetSingleton<SessionState>().Phase != SessionPhase.Playing) return;

            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            var cut = SystemAPI.GetComponent<CutState>(player);
            if (cut.Phase != CutPhase.Executing && cut.Phase != CutPhase.Commit) return;

            var tuning = SystemAPI.GetSingleton<TuningSingleton>().Value;
            float dt = SystemAPI.GetSingleton<GameClock>().DeltaTime;
            var fighter = SystemAPI.GetComponent<Fighter>(player);
            var sources = state.World.GetOrCreateSystemManaged<CutMotionService>();
            var toDestroy = new NativeList<Entity>(4, Allocator.Temp);

            if (cut.Phase == CutPhase.Executing && fighter.HitStun > 0f)
            {
                // 実行中に殴られた: 残りのポーズを捨てて被弾に移る(TDD 7章「中断」)
                var poses = SystemAPI.GetBuffer<PlacedPose>(player);
                for (int i = cut.SegmentIndex; i < poses.Length; i++) toDestroy.Add(poses[i].Ghost);
                cut.Record.Interrupted = true;
                Finish(ref state, player, ref cut, sources);
            }
            else if (cut.Phase == CutPhase.Executing)
            {
                Advance(ref state, player, ref cut, ref fighter, tuning, dt, sources, ref toDestroy);
                SystemAPI.SetComponent(player, fighter);
            }
            else
            {
                cut.CommitRemaining -= dt;
                if (cut.CommitRemaining <= 0f) Finish(ref state, player, ref cut, sources);
            }

            SystemAPI.SetComponent(player, cut);
            foreach (var e in toDestroy)
                if (e != Entity.Null && state.EntityManager.Exists(e)) state.EntityManager.DestroyEntity(e);
        }

        void Advance(ref SystemState state, Entity player, ref CutState cut, ref Fighter fighter, in Tuning tuning, float dt,
            CutMotionService sources, ref NativeList<Entity> toDestroy)
        {
            var poses = SystemAPI.GetBuffer<PlacedPose>(player);
            var pose = poses[cut.SegmentIndex];
            var source = sources.For(pose);
            int handle = pose.MotionHandle;
            float duration = source.Duration(handle);
            cut.SegmentTime += dt;

            // 計画した始点と、実際に区間を始めた位置のずれ(スロー中の押し合いなど)を区間の中で解消する
            var sample = source.Sample(handle, cut.SegmentTime);
            float3 drift = cut.SegmentFrom - source.Sample(handle, 0f).Position;
            float progress = duration > 1e-5f ? math.saturate(cut.SegmentTime / duration) : 1f;

            var lt = SystemAPI.GetComponent<LocalTransform>(player);
            lt.Position = sample.Position + drift * (1f - progress);
            lt.Rotation = sample.Rotation;
            SystemAPI.SetComponent(player, lt);
            fighter.Facing = math.mul(sample.Rotation, new float3(0, 0, 1));

            if (!cut.StrikeFired && cut.SegmentTime >= source.ContactTime(handle))
            {
                cut.StrikeFired = true;
                Strike(ref state, player, cut, pose, tuning.GetBrick(pose.Kind), tuning, lt.Position, fighter.Facing);
                toDestroy.Add(pose.Ghost);
            }

            if (cut.SegmentTime < duration) return;

            source.Discard(handle);
            cut.SegmentIndex++;
            cut.StrikeFired = false;
            cut.SegmentTime = 0f;
            cut.SegmentFrom = lt.Position;
            if (cut.SegmentIndex >= cut.PoseCount)
            {
                cut.Phase = CutPhase.Commit;
                // 硬直 0 秒(条件 B)でも記録の確定は次の更新で行う(最後の1打の撃破数・映え点を DamageSystem が書き込むため)。
                // その1フレームを硬直扱いしないのは DamageSystem 側の責任(CommitDuration > 0 のときだけ硬直とみなす)
                cut.CommitRemaining = cut.CommitDuration;
            }
        }

        void Strike(ref SystemState state, Entity player, in CutState cut, in PlacedPose pose, in BrickTuning brick, in Tuning tuning,
            float3 at, float3 facing)
        {
            float2 center = at.xz + facing.xz * brick.StrikeForward;
            bool found = NearestEnemy(ref state, center, out float2 nearest);
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
            });
        }

        /// <summary>カットを終えて記録を確定し、置いたポーズと残った計画を片付ける。</summary>
        void Finish(ref SystemState state, Entity player, ref CutState cut, CutMotionService sources)
        {
            SystemAPI.GetSingletonBuffer<TelemetryEvent>().Add(new TelemetryEvent { Value = cut.Record });
            float3 pos = SystemAPI.GetComponent<LocalTransform>(player).Position;
            SystemAPI.GetSingletonBuffer<FeedbackEvent>().Add(new FeedbackEvent
            {
                Kind = FeedbackKind.CommitEnd, Position = pos, Value = cut.Record.Hae, Extra = cut.Record.Kills,
            });
            var poses = SystemAPI.GetBuffer<PlacedPose>(player);
            sources.Discard(poses, math.min(cut.SegmentIndex, poses.Length));
            poses.Clear();
            cut = new CutState { SelectedKind = cut.SelectedKind };
        }

        bool NearestEnemy(ref SystemState state, float2 from, out float2 nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            foreach (var lt in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<EnemyTag>().WithNone<Dying>())
            {
                float d = math.distancesq(lt.ValueRO.Position.xz, from);
                if (d < best) { best = d; nearest = lt.ValueRO.Position.xz; }
            }
            return best < float.MaxValue;
        }
    }
}
