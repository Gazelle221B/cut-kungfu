using System.IO;
using System.Text;
using CutKungfu.Core;
using CutKungfu.Presentation;
using NUnit.Framework;

namespace CutKungfu.Tests
{
    /// <summary>
    /// 計測用(合否なし): MotionBricks 用プリセット(区間 0.6〜0.9 秒)を読み込んで、包囲での被弾と全滅を測る。
    /// 既定値の計測(CommitPunishmentTests / SurroundBalanceTests)と同じ本体を、調整値だけ差し替えて流す。
    /// </summary>
    public class MotionBricksPresetMeasurements
    {
        const string PresetName = "motionbricks-segments";

        static Tuning LoadPreset()
        {
            string path = TuningLoaderSystem.PresetPath(PresetName);
            Assert.That(File.Exists(path), Is.True, $"プリセットがない: {path}");
            return TuningLoaderSystem.Parse(File.ReadAllText(path));
        }

        [Explicit("計測用: MotionBricks 用プリセットで、置く数ごとの実行中・硬直中の被弾と中断を測る(合否なし)")]
        [Test]
        public void Measure_CommitPunishmentByPoseCount_WithMotionBricksPreset()
        {
            var preset = LoadPreset();
            string report = $"[プリセット {PresetName}: 区間 {preset.CutSegmentMin}〜{preset.CutSegmentMax} 秒]\n" + CommitPunishmentTests.Report(preset);
            TestContext.WriteLine(report);
            UnityEngine.Debug.Log("[PUNISH-MB]\n" + report);
            Assert.Pass(report);
        }

        [Explicit("計測用: MotionBricks 用プリセットで、全探索(MixedAreaCombos・AreaThenPalmFinish)の3体全滅を数える(合否なし)")]
        [Test]
        public void Measure_FullSearchWipes_WithMotionBricksPreset()
        {
            var preset = LoadPreset();
            var sb = new StringBuilder($"[プリセット {PresetName}]\n");
            int totalWipes = 0;
            foreach (var setup in SurroundBalanceTests.Setups)
            foreach (float gauge in new[] { 50f, 60f })
            {
                var mixed = SurroundBalanceTests.MixedAreaWipes(setup, gauge, preset, out int mixedRuns);
                var palm = SurroundBalanceTests.AreaThenPalmWipes(setup, gauge, preset, out int palmRuns, out string summary);
                totalWipes += mixed.Count + palm.Count;
                sb.Append($"{setup.Name} ゲージ{gauge}: MixedAreaCombos {mixedRuns} 通り・全滅 {mixed.Count} / AreaThenPalmFinish {summary}\n");
                foreach (var m in mixed) sb.Append($"  全滅(Mixed): {m}\n");
            }
            sb.Append($"3体全滅の合計: {totalWipes}\n");
            TestContext.WriteLine(sb.ToString());
            UnityEngine.Debug.Log("[WIPES-MB]\n" + sb);
            Assert.Pass(sb.ToString());
        }
    }
}
