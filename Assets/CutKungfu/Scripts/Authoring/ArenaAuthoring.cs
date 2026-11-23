using System;
using System.Collections.Generic;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CutKungfu.Authoring
{
    /// <summary>部屋の歩ける範囲。</summary>
    public sealed class ArenaAuthoring : MonoBehaviour
    {
        public Vector2 min = new Vector2(-12f, -4f);
        public Vector2 max = new Vector2(12f, 4f);

        sealed class Baker : Baker<ArenaAuthoring>
        {
            public override void Bake(ArenaAuthoring a)
            {
                AddComponent(GetEntity(TransformUsageFlags.None), new ArenaBounds { Min = a.min, Max = a.max });
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            var c = new Vector3((min.x + max.x) * 0.5f, 0f, (min.y + max.y) * 0.5f);
            Gizmos.DrawWireCube(c, new Vector3(max.x - min.x, 0.01f, max.y - min.y));
        }
    }
}
