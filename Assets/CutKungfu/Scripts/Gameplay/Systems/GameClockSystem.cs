using CutKungfu.Core;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// ゲーム時間の倍率を決める。スロー・ヒットストップ・NG/カット演出の停止はすべてここに集約する。
    /// </summary>
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(SessionSystem))]
    public partial struct GameClockSystem : ISystem
    {
        const float MaxFrameDelta = 1f / 20f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameClock>();
            state.RequireForUpdate<TuningSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float realDt = math.min(SystemAPI.Time.DeltaTime, MaxFrameDelta);
            var tuning = SystemAPI.GetSingleton<TuningSingleton>().Value;
            var session = SystemAPI.GetSingleton<SessionState>();
            ref var hitStop = ref SystemAPI.GetSingletonRW<HitStop>().ValueRW;
            hitStop.Remaining = math.max(0f, hitStop.Remaining - realDt);

            float scale = 1f;
            foreach (var cut in SystemAPI.Query<RefRO<CutState>>().WithAll<PlayerTag>())
            {
                if (cut.ValueRO.Phase == CutPhase.Slow) scale = tuning.SlowTimeScale;
            }

            if (session.Phase == SessionPhase.NG) scale = 0f;
            else if (session.Phase == SessionPhase.Cleared) scale = 0.35f;
            if (hitStop.Remaining > 0f) scale = 0f;

            ref var clock = ref SystemAPI.GetSingletonRW<GameClock>().ValueRW;
            clock.Scale = scale;
            clock.RealDeltaTime = realDt;
            clock.DeltaTime = realDt * scale;
            clock.ElapsedReal += realDt;
            clock.ElapsedGame += clock.DeltaTime;
        }
    }
}
