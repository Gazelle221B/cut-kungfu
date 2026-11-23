using CutKungfu.Core;
using CutKungfu.Gameplay;
using Unity.Entities;
using UnityEngine;

namespace CutKungfu.Authoring
{
    /// <summary>ポーズブリックの人影(ゴースト)。</summary>
    public sealed class GhostAuthoring : MonoBehaviour
    {
        public BrickKind kind;
        [Tooltip("色を変える見た目の子")]
        public GameObject body;

        sealed class Baker : Baker<GhostAuthoring>
        {
            public override void Bake(GhostAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(e, new GhostPose { Order = -1 });
                AddComponent(e, new VisualBody { Body = a.body != null ? GetEntity(a.body, TransformUsageFlags.Dynamic) : Entity.Null });
            }
        }
    }
}
