using System;
using System.Collections.Generic;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CutKungfu.Authoring
{
    /// <summary>机。天板の矩形をこの GameObject の Transform(位置とスケール)から求める。</summary>
    public sealed class TableAuthoring : MonoBehaviour
    {
        sealed class Baker : Baker<TableAuthoring>
        {
            public override void Bake(TableAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.None);
                var t = GetComponent<Transform>();
                Vector3 s = t.lossyScale;
                Vector3 p = t.position;
                AddComponent(e, new TableSingleton
                {
                    Rect = new TableRect
                    {
                        Exists = true,
                        Center = new float2(p.x, p.z),
                        HalfExtents = new float2(s.x, s.z) * 0.5f,
                        TopY = p.y + s.y * 0.5f,
                    },
                });
            }
        }
    }
}
