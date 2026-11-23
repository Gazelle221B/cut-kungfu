using System.IO;
using CutKungfu.Core;
using CutKungfu.Presentation;
using NUnit.Framework;

namespace CutKungfu.Tests
{
    /// <summary>
    /// MotionBricks の区間長を想定したプリセット。生成1回 18〜58 フレーム(30Hz)のうち、試作で使う 0.6〜0.9 秒に区間を収める。
    /// 区間長の2項目以外は既定値のままであること、4個技の拘束時間がどれだけ伸びるかを固定する。
    /// </summary>
    public class TuningPresetTests
    {
        const string PresetName = "motionbricks-segments";

        static Tuning LoadPreset()
        {
            string path = TuningLoaderSystem.PresetPath(PresetName);
            Assert.That(File.Exists(path), Is.True, $"プリセットがない: {path}");
            return TuningLoaderSystem.Parse(File.ReadAllText(path));
        }

        [Test]
        public void Preset_OnlyChangesTheSegmentLength()
        {
            var preset = LoadPreset();
            Assert.That(preset.CutSegmentMin, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(preset.CutSegmentMax, Is.EqualTo(0.9f).Within(1e-5f));

            var expected = Tuning.Default;
            expected.CutSegmentMin = preset.CutSegmentMin;
            expected.CutSegmentMax = preset.CutSegmentMax;
            Assert.That(TuningLoaderSystem.ToJson(preset), Is.EqualTo(TuningLoaderSystem.ToJson(expected)), "区間長以外は既定値のまま");
        }

        [Test]
        public void DefaultTuningJson_IsUnchanged()
        {
            string json = File.ReadAllText(TuningLoaderSystem.FilePath);
            Assert.That(TuningLoaderSystem.ToJson(TuningLoaderSystem.Parse(json)), Is.EqualTo(TuningLoaderSystem.ToJson(Tuning.Default)));
        }

        /// <summary>
        /// 4個技の「実行(4区間)+硬直」。どのブリックも到達距離は 7m 未満で、突進速度 16m/s では
        /// 区間が最短長(0.6秒)を下回るため、置き方によらず 4×0.6 + (0.3+1.5) = 4.2 秒になる。
        /// 既定値(0.14〜0.38秒)では 1.8 + 4×(0.14〜0.38) = 2.36〜3.32 秒。
        /// </summary>
        [TestCase(0f)]
        [TestCase(2f)]
        [TestCase(5.5f)]
        public void FourPoseCut_TakesFourPointTwoSeconds_WithThePreset(float segmentDistance)
        {
            var t = LoadPreset();
            float total = 4 * CutRules.SegmentDuration(t, segmentDistance) + CutRules.CommitTime(t, 4, commitEnabled: true);
            Assert.That(total, Is.EqualTo(4.2f).Within(1e-4f));
        }
    }
}
