using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Gameplay
{
    /// <summary>被弾硬直・無敵・フラッシュの残り時間を減らす。</summary>
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(GameClockSystem))]
    public partial struct FighterTimersSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<GameClock>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<GameClock>();
            float dt = clock.DeltaTime;
            foreach (var fighter in SystemAPI.Query<RefRW<Fighter>>())
            {
                ref var f = ref fighter.ValueRW;
                f.HitStun = math.max(0f, f.HitStun - dt);
                f.Invulnerable = math.max(0f, f.Invulnerable - dt);
                f.Flash = math.max(0f, f.Flash - clock.RealDeltaTime);
            }
        }
    }
}
