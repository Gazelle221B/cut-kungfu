using CutKungfu.Core;
using CutKungfu.Gameplay;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CutKungfu.Authoring
{
    /// <summary>
    /// 主人公・敵のオーサリング。根は足元に置き、見た目(色を変える胴体)を子として指定する。
    /// 数値は実行時に調整値(JSON)から入るので、ここでは役割と見た目だけを決める。
    /// </summary>
    public sealed class FighterAuthoring : MonoBehaviour
    {
        public Team team = Team.Enemy;
        [Tooltip("色を変える見た目の子(URPMaterialPropertyBaseColorAuthoring を付けておく)")]
        public GameObject body;
        public Color baseColor = Color.gray;

        sealed class Baker : Baker<FighterAuthoring>
        {
            public override void Bake(FighterAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.Dynamic);
                var tuning = Tuning.Default;
                bool isPlayer = a.team == Team.Player;
                float health = isPlayer ? tuning.PlayerMaxHealth : tuning.EnemyMaxHealth;

                AddComponent(e, new Fighter
                {
                    Team = a.team,
                    Radius = isPlayer ? tuning.PlayerRadius : tuning.EnemyRadius,
                    Health = health,
                    MaxHealth = health,
                    Facing = new float3(isPlayer ? 1f : -1f, 0f, 0f),
                });
                AddComponent(e, new Knockback());
                AddComponent(e, new CharacterColor { Base = (Vector4)a.baseColor.linear });
                AddComponent(e, new VisualBody { Body = a.body != null ? GetEntity(a.body, TransformUsageFlags.Dynamic) : Entity.Null });

                if (isPlayer)
                {
                    AddComponent<PlayerTag>(e);
                    AddComponent(e, new PlayerAction());
                    AddComponent(e, new FilmGauge { Value = tuning.GaugeMax, Max = tuning.GaugeMax });
                    AddComponent(e, new CutState { SelectedKind = BrickKind.Palm });
                    AddBuffer<PlacedPose>(e);
                    AddComponent(e, new VerticalBody());
                }
                else
                {
                    AddComponent<EnemyTag>(e);
                    AddComponent(e, new EnemyBrain { State = EnemyState.Approach, Random = Unity.Mathematics.Random.CreateFromIndex(1) });
                }
            }
        }
    }
}
