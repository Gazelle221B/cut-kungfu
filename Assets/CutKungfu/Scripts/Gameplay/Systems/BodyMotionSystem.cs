using CutKungfu.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 体の物理まわりの簡易処理: 吹き飛びの積分、押し合い、机との衝突と机の上り下り、部屋の壁。
    /// 物理エンジンは使わない(数体の円の押し合いで足り、カット技の決定的な再生と相性が良いため)。
    /// </summary>
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(DamageSystem))]
    public partial struct BodyMotionSystem : ISystem
    {
        const float KnockbackDamping = 7f;
        const float Gravity = 28f;

        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<GameClock>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.GetSingleton<GameClock>().DeltaTime;
            if (dt <= 0f) return;
            var table = SystemAPI.TryGetSingleton<TableSingleton>(out var t) ? t.Rect : TableRect.None;
            bool hasArena = SystemAPI.TryGetSingleton<ArenaBounds>(out var arena);

            // 1) 吹き飛び
            foreach (var (lt, kb) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<Knockback>>().WithNone<Dying>())
            {
                ref var v = ref kb.ValueRW.Velocity;
                if (math.lengthsq(v) < 0.0025f) { v = float3.zero; continue; }
                lt.ValueRW.Position += Kinematics.DampedStep(ref v, KnockbackDamping, dt);
            }

            // 2) 押し合い(カット実行中の主人公はすり抜ける)
            var bodies = new NativeList<Body>(8, Allocator.Temp);
            foreach (var (lt, f, e) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<Fighter>>().WithNone<Dying>().WithEntityAccess())
            {
                bool passThrough = SystemAPI.HasComponent<CutState>(e) && SystemAPI.GetComponent<CutState>(e).Phase == CutPhase.Executing;
                if (!passThrough) bodies.Add(new Body { Entity = e, Position = lt.ValueRO.Position.xz, Radius = f.ValueRO.Radius });
            }
            for (int i = 0; i < bodies.Length; i++)
            for (int j = i + 1; j < bodies.Length; j++)
            {
                var a = bodies[i];
                var b = bodies[j];
                float2 d = b.Position - a.Position;
                float minDist = a.Radius + b.Radius;
                float distSq = math.lengthsq(d);
                if (distSq >= minDist * minDist) continue;
                float dist = math.sqrt(distSq);
                float2 n = dist > 1e-4f ? d / dist : new float2(1, 0);
                float2 push = n * (minDist - dist) * 0.5f;
                a.Position -= push;
                b.Position += push;
                bodies[i] = a;
                bodies[j] = b;
            }
            foreach (var body in bodies)
            {
                ref var lt = ref SystemAPI.GetComponentRW<LocalTransform>(body.Entity).ValueRW;
                lt.Position.x = body.Position.x;
                lt.Position.z = body.Position.y;
            }

            // 3) 机・重力・壁
            foreach (var (lt, f, e) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<Fighter>>().WithNone<Dying>().WithEntityAccess())
            {
                ref var p = ref lt.ValueRW.Position;
                bool executing = SystemAPI.HasComponent<CutState>(e) && SystemAPI.GetComponent<CutState>(e).Phase == CutPhase.Executing;
                if (executing) continue; // 生成された動きを優先する

                bool onTop = table.Exists && p.y >= table.TopY - 0.2f && table.Contains(p.xz);
                if (table.Exists && !onTop)
                {
                    float2 xz = Geometry2D.PushOutOfRect(p.xz, f.ValueRO.Radius, table.Center, table.HalfExtents);
                    p.x = xz.x;
                    p.z = xz.y;
                }

                if (SystemAPI.HasComponent<VerticalBody>(e))
                {
                    ref var vb = ref SystemAPI.GetComponentRW<VerticalBody>(e).ValueRW;
                    float floor = onTop ? table.TopY : 0f;
                    if (p.y > floor + 1e-3f)
                    {
                        p.y += Kinematics.AcceleratedStep(ref vb.VelocityY, -Gravity, dt);
                        if (p.y <= floor) { p.y = floor; vb.VelocityY = 0f; }
                    }
                    else
                    {
                        p.y = floor;
                        vb.VelocityY = 0f;
                    }
                }
                else
                {
                    p.y = 0f;
                }

                if (hasArena)
                {
                    float r = f.ValueRO.Radius;
                    p.x = math.clamp(p.x, arena.Min.x + r, arena.Max.x - r);
                    p.z = math.clamp(p.z, arena.Min.y + r, arena.Max.y - r);
                }
            }
        }

        struct Body
        {
            public Entity Entity;
            public float2 Position;
            public float Radius;
        }
    }
}
