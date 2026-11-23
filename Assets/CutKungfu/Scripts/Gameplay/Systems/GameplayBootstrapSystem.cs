using CutKungfu.Core;
using Unity.Burst;
using Unity.Entities;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// ワールドに1つだけ要るシングルトン群を作る。シーンの作り方に依存させないため、オーサリングでなくここで用意する。
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial struct GameplayBootstrapSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var em = state.EntityManager;
            var e = em.CreateEntity();
            em.SetName(e, "GameplaySingletons");
            em.AddComponentData(e, new GameClock { Scale = 1f });
            em.AddComponentData(e, new HitStop());
            em.AddComponentData(e, new CameraShake());
            em.AddComponentData(e, new PlayerInput { SelectBrick = -1 });
            em.AddComponentData(e, new TuningSingleton { Value = Tuning.Default, Version = 0 });
            em.AddComponentData(e, new SessionState
            {
                Phase = SessionPhase.PendingStart,
                Take = 1,
                CommitEnabled = true,
            });
            em.AddBuffer<DamageRequest>(e);
            em.AddBuffer<FeedbackEvent>(e);
            em.AddBuffer<TelemetryEvent>(e);
            em.AddBuffer<CutAreaDamage>(e);

            state.Enabled = false;
        }
    }
}
