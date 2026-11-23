using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace CutKungfu.Gameplay
{
    /// <summary>倒れた敵を足元を軸に倒し、しばらくして消す。</summary>
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(BodyMotionSystem))]
    public partial struct DyingSystem : ISystem
    {
        const float FallTime = 0.35f;
        const float RemoveAfter = 1.4f;

        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<GameClock>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<GameClock>();
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (dying, lt, kb, f, e) in
                     SystemAPI.Query<RefRW<Dying>, RefRW<LocalTransform>, RefRW<Knockback>, RefRO<Fighter>>().WithEntityAccess())
            {
                ref var d = ref dying.ValueRW;
                d.Timer += clock.DeltaTime;
                float a = math.saturate(d.Timer / FallTime) * math.PI * 0.5f;
                quaternion upright = quaternion.LookRotationSafe(f.ValueRO.Facing, math.up());
                lt.ValueRW.Rotation = math.mul(quaternion.AxisAngle(d.FallAxis, a), upright);
                ref var v = ref kb.ValueRW.Velocity;
                lt.ValueRW.Position += v * clock.DeltaTime;
                v *= math.exp(-5f * clock.DeltaTime);
                if (d.Timer >= RemoveAfter) ecb.DestroyEntity(e);
            }
            ecb.Playback(state.EntityManager);
        }
    }

    /// <summary>判定の見える化エフェクトを広げながら消す。ヒットストップ中も動かす(実時間)。</summary>
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(DyingSystem))]
    public partial struct StrikeFxSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<GameClock>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.GetSingleton<GameClock>().RealDeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (fx, lt, color, e) in
                     SystemAPI.Query<RefRW<StrikeFx>, RefRW<LocalTransform>, RefRW<URPMaterialPropertyBaseColor>>().WithEntityAccess())
            {
                ref var f = ref fx.ValueRW;
                f.Life -= dt;
                float k = 1f - math.saturate(f.Life / math.max(1e-4f, f.MaxLife));
                lt.ValueRW.Scale = math.lerp(f.StartScale, f.EndScale, k);
                color.ValueRW.Value.w = (1f - k) * 0.6f;
                if (f.Life <= 0f) ecb.DestroyEntity(e);
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
