using System;
using System.Collections.Generic;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CutKungfu.Authoring
{
    /// <summary>
    /// 試作のシナリオ(GDD 13章: 部屋1つ、敵1体と3体の2配置)と、実行時に生成するプレハブ。
    /// </summary>
    public sealed class ScenarioAuthoring : MonoBehaviour
    {
        [Serializable]
        public sealed class Wave
        {
            public string name = "wave";
            public Vector3 playerStart;
            public List<Vector3> enemySpawns = new();
        }

        [Header("Prefabs")]
        public GameObject enemy;
        public GameObject ghostPalm;
        public GameObject ghostSpin;
        public GameObject ghostFlyingKick;
        public GameObject ghostTableLanding;
        public GameObject strikeFx;

        [Header("Waves (順に巡回)")]
        public List<Wave> waves = new();

        sealed class Baker : Baker<ScenarioAuthoring>
        {
            public override void Bake(ScenarioAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.None);
                AddComponent(e, new PrefabLibrary
                {
                    Enemy = Prefab(a.enemy),
                    GhostPalm = Prefab(a.ghostPalm),
                    GhostSpin = Prefab(a.ghostSpin),
                    GhostFlyingKick = Prefab(a.ghostFlyingKick),
                    GhostTableLanding = Prefab(a.ghostTableLanding),
                    StrikeFx = Prefab(a.strikeFx),
                });

                var waves = AddBuffer<ScenarioWave>(e);
                var spawns = AddBuffer<ScenarioSpawn>(e);
                foreach (var w in a.waves)
                {
                    waves.Add(new ScenarioWave { PlayerStart = w.playerStart, FirstSpawn = spawns.Length, SpawnCount = w.enemySpawns.Count });
                    foreach (var p in w.enemySpawns) spawns.Add(new ScenarioSpawn { Position = p });
                }
            }

            Entity Prefab(GameObject go) => go != null ? GetEntity(go, TransformUsageFlags.Dynamic) : Entity.Null;
        }
    }
}
