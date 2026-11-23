using System.Collections.Generic;
using CutKungfu.Gameplay;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace CutKungfu.Tests
{
    /// <summary>
    /// ゲームロジック(GameplaySystemGroup)が固定タイムステップの中で、トランスフォーム更新(TransformSystemGroup)より前に回ること。
    /// 逆だと、ロジックが動かした位置が描画に1フレーム遅れて届く。
    /// </summary>
    public class SystemOrderTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void GameplayRunsBeforeTransforms_WithoutInvalidOrderingWarnings(bool fixedStep)
        {
            var warnings = new List<string>();
            void Capture(string message, string stack, LogType type)
            {
                if (type == LogType.Warning && message.Contains("Ignoring invalid")) warnings.Add(message);
            }

            Application.logMessageReceived += Capture;
            try
            {
                using var w = new GameplayTestWorld(fixedStep, float3.zero, new float3(2f, 0f, 0f));
                w.Step();

                var sim = w.World.GetExistingSystemManaged<SimulationSystemGroup>();
                var fixedGroup = w.World.GetExistingSystemManaged<FixedStepSimulationSystemGroup>();
                var gameplay = w.World.GetExistingSystemManaged<GameplaySystemGroup>();
                var transforms = w.World.GetExistingSystemManaged<TransformSystemGroup>();
                Assert.That(transforms, Is.Not.Null, "トランスフォーム更新がワールドに無い");

                var inFixed = new List<SystemHandle>();
                foreach (var h in fixedGroup.GetAllSystems()) inFixed.Add(h);
                Assert.That(inFixed, Does.Contain(gameplay.SystemHandle), "ゲームロジックは固定タイムステップの中で回る");

                var order = new List<SystemHandle>();
                foreach (var h in sim.GetAllSystems()) order.Add(h);
                int g = order.IndexOf(fixedGroup.SystemHandle);
                int t = order.IndexOf(transforms.SystemHandle);
                Assert.That(g, Is.GreaterThanOrEqualTo(0));
                Assert.That(t, Is.GreaterThanOrEqualTo(0), "TransformSystemGroup が SimulationSystemGroup の中に無い");
                Assert.That(g, Is.LessThan(t), "ゲームロジックはトランスフォーム更新より前に回る");
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }

            Assert.That(warnings, Is.Empty, "順序指定が無視されている:\n" + string.Join("\n", warnings));
        }
    }
}
