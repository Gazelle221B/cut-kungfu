using Unity.Burst;
using Unity.Entities;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 1回の更新の最後に、使い終えた押下を消す。同じフレームで2回目の更新が回っても、押下は2度効かない。
    /// </summary>
    [UpdateInGroup(typeof(GameplaySystemGroup), OrderLast = true)]
    public partial struct PlayerInputConsumeSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<PlayerInput>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var input = ref SystemAPI.GetSingletonRW<PlayerInput>().ValueRW;
            input = input.WithoutPresses();
        }
    }
}
