using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 状態を色で見せる(柱3「一目で読める殺陣」)。
    /// 敵: 予備動作=橙の明滅、よろけ=青、被弾=白。主人公: 硬直中=暗い明滅、無敵=明るく。
    /// </summary>
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(StrikeFxSystem))]
    public partial struct CharacterColorSystem : ISystem
    {
        static readonly float4 Windup = new float4(1f, 0.55f, 0.12f, 1f);
        static readonly float4 Stagger = new float4(0.35f, 0.55f, 1f, 1f);
        static readonly float4 Flash = new float4(1f, 1f, 1f, 1f);
        static readonly float4 DeadTint = new float4(0.22f, 0.24f, 0.25f, 1f);
        static readonly float4 CommitTint = new float4(0.25f, 0.12f, 0.12f, 1f);

        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<GameClock>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<GameClock>();
            float pulse = 0.5f + 0.5f * math.sin((float)clock.ElapsedReal * 18f);
            var colors = SystemAPI.GetComponentLookup<URPMaterialPropertyBaseColor>();

            foreach (var (visual, baseColor, fighter, e) in
                     SystemAPI.Query<RefRO<VisualBody>, RefRO<CharacterColor>, RefRO<Fighter>>().WithEntityAccess())
            {
                var body = visual.ValueRO.Body;
                if (!colors.HasComponent(body)) continue;
                float4 c = baseColor.ValueRO.Base;
                var f = fighter.ValueRO;

                if (SystemAPI.HasComponent<Dying>(e))
                {
                    c = math.lerp(c, DeadTint, 0.7f);
                }
                else if (SystemAPI.HasComponent<EnemyBrain>(e))
                {
                    var brain = SystemAPI.GetComponent<EnemyBrain>(e);
                    if (brain.State == EnemyState.Windup) c = math.lerp(c, Windup, 0.55f + 0.45f * pulse);
                    else if (brain.State == EnemyState.Staggered) c = math.lerp(c, Stagger, 0.6f);
                }
                else if (SystemAPI.HasComponent<CutState>(e))
                {
                    var cut = SystemAPI.GetComponent<CutState>(e);
                    if (cut.Phase == CutPhase.Commit) c = math.lerp(c, CommitTint, 0.35f + 0.4f * pulse);
                    else if (f.Invulnerable > 0f && cut.Phase == CutPhase.None) c = math.lerp(c, Flash, 0.35f);
                }

                if (f.Flash > 0f) c = Flash;
                colors[body] = new URPMaterialPropertyBaseColor { Value = c };
            }
        }
    }
}
