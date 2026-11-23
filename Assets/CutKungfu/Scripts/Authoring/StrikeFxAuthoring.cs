using CutKungfu.Core;
using CutKungfu.Gameplay;
using Unity.Entities;
using UnityEngine;

namespace CutKungfu.Authoring
{
    /// <summary>攻撃判定の見える化(地面の円盤)。</summary>
    public sealed class StrikeFxAuthoring : MonoBehaviour
    {
        sealed class Baker : Baker<StrikeFxAuthoring>
        {
            public override void Bake(StrikeFxAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(e, new StrikeFx { Life = 0.2f, MaxLife = 0.2f, StartScale = 1f, EndScale = 1f });
            }
        }
    }
}
