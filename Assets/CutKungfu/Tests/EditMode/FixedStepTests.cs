using CutKungfu.Core;
using CutKungfu.Gameplay;
using CutKungfu.Presentation;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Transforms;

namespace CutKungfu.Tests
{
    /// <summary>
    /// 実行時と同じ固定タイムステップ(60Hz)で回したときの、入力・表示の補間・スロー・ヒットストップ。
    /// ここでの Step は描画の1フレームで、その間にゲームロジックは 0 回のことも複数回のこともある。
    /// </summary>
    public class FixedStepTests
    {
        static GameplayTestWorld FixedWorld(float frameDt, float3 enemyAt)
        {
            var w = new GameplayTestWorld(true, float3.zero, enemyAt) { StepDt = frameDt };
            w.Step(); // 最初の固定更新(ウェーブ開始)
            return w;
        }

        static PlayerInput CutPress()
        {
            var i = GameplayTestWorld.Neutral();
            i.Cut = true;
            return i;
        }

        static float ElapsedReal(GameplayTestWorld w) => (float)w.Singleton<GameClock>().ElapsedReal;

        /// <summary>1フレームに固定更新が3回回っても、押下は1回しか効かない(止める→すぐ解除、にならない)。</summary>
        [Test]
        public void OnePress_ActsOnce_WhenAFrameRunsSeveralSteps()
        {
            using var w = FixedWorld(1f / 20f, new float3(20f, 0f, 0f));
            w.SetGauge(100f);
            float before = ElapsedReal(w);
            w.Step(CutPress());
            Assert.That(ElapsedReal(w) - before, Is.EqualTo(3f * GameplaySystemGroup.StepSeconds).Within(1e-4f), "1フレームで3回回っていない");
            Assert.That(w.Em.GetComponentData<CutState>(w.Player).Phase, Is.EqualTo(CutPhase.Slow), "押下が2回目の更新でも効いた");
        }

        /// <summary>固定更新が回らなかったフレームの押下は、次の固定更新まで残る(取りこぼさない)。</summary>
        [Test]
        public void Press_IsKept_UntilTheNextStep()
        {
            using var w = FixedWorld(1f / 240f, new float3(20f, 0f, 0f));
            w.SetGauge(100f);
            float before = ElapsedReal(w);
            w.Step(CutPress());
            bool steppedOnPressFrame = ElapsedReal(w) > before + 1e-6f;
            for (int i = 0; i < 8 && w.Em.GetComponentData<CutState>(w.Player).Phase == CutPhase.None; i++) w.Step();
            Assert.That(steppedOnPressFrame, Is.False, "押したフレームで更新が回った(取りこぼしを確かめられない)");
            Assert.That(w.Em.GetComponentData<CutState>(w.Player).Phase, Is.EqualTo(CutPhase.Slow), "押下を取りこぼした");
        }

        /// <summary>
        /// 表示は固定更新の間を補間する(フレームごとに少しずつ動く)。補間で書き換えた位置はゲームロジックに漏れない
        /// (60Hz で直接回した結果と同じ位置に着く)。
        /// </summary>
        [Test]
        public void Display_IsInterpolated_AndDoesNotLeakIntoTheSimulation()
        {
            var start = new float3(20f, 0f, 0f);
            using var w = FixedWorld(1f / 240f, start);
            var enemy = w.Enemies()[0];
            var f = w.Em.GetComponentData<Fighter>(enemy);
            f.HitStun = 5f; // 歩かせない
            w.Em.SetComponentData(enemy, f);
            w.Em.SetComponentData(enemy, new Knockback { Velocity = new float3(6f, 0f, 0f) });

            float real0 = ElapsedReal(w);
            float lastShown = w.Em.GetComponentData<LocalTransform>(enemy).Position.x;
            int frozenFrames = 0, movingFrames = 0;
            for (int i = 0; i < 120; i++)
            {
                w.Step();
                float shown = w.Em.GetComponentData<LocalTransform>(enemy).Position.x;
                // 補間は最新の更新から1回分遅れて見せるので、動き出してから最初の2回の更新(8フレーム)は数えない
                if (i >= 8 && math.lengthsq(w.Em.GetComponentData<Knockback>(enemy).Velocity) > 0f)
                {
                    movingFrames++;
                    if (shown <= lastShown) frozenFrames++;
                }
                lastShown = shown;
            }
            Assert.That(movingFrames, Is.GreaterThan(40));
            Assert.That(frozenFrames, Is.LessThanOrEqualTo(1), "表示が固定更新の区切りでしか動いていない(補間されていない)");

            // 同じ回数だけ 60Hz で直接回したワールドと、ゲームロジックの位置が一致する
            int steps = (int)math.round((ElapsedReal(w) - real0) / GameplaySystemGroup.StepSeconds);
            float simX = w.Em.GetComponentData<SmoothedTransform>(enemy).Position.x;
            using var direct = new GameplayTestWorld(float3.zero, start) { StepDt = GameplaySystemGroup.StepSeconds };
            direct.Step();
            var e2 = direct.Enemies()[0];
            var f2 = direct.Em.GetComponentData<Fighter>(e2);
            f2.HitStun = 5f;
            direct.Em.SetComponentData(e2, f2);
            direct.Em.SetComponentData(e2, new Knockback { Velocity = new float3(6f, 0f, 0f) });
            direct.StepFrames(steps);
            Assert.That(simX, Is.EqualTo(direct.Em.GetComponentData<LocalTransform>(e2).Position.x).Within(1e-4f));
        }

        /// <summary>スロー中のゲーム時間は、固定更新でも実時間 × SlowTimeScale で進む。</summary>
        [Test]
        public void Slow_StillScalesGameTime()
        {
            using var w = FixedWorld(1f / 144f, new float3(20f, 0f, 0f));
            w.SetGauge(100f);
            w.Step(CutPress());
            for (int i = 0; i < 4 && w.Em.GetComponentData<CutState>(w.Player).Phase != CutPhase.Slow; i++) w.Step();
            Assert.That(w.Em.GetComponentData<CutState>(w.Player).Phase, Is.EqualTo(CutPhase.Slow));

            var c0 = w.Singleton<GameClock>();
            w.StepFrames(72); // 0.5 秒
            var c1 = w.Singleton<GameClock>();
            double real = c1.ElapsedReal - c0.ElapsedReal;
            Assert.That(real, Is.EqualTo(0.5).Within(GameplaySystemGroup.StepSeconds + 1e-4));
            Assert.That(c1.ElapsedGame - c0.ElapsedGame, Is.EqualTo(real * Tuning.Default.SlowTimeScale).Within(1e-4));
        }

        /// <summary>ヒットストップ中はゲーム時間が止まり、その長さは固定更新の区切りに丸められない。</summary>
        [Test]
        public void HitStop_StillStopsGameTime()
        {
            using var w = FixedWorld(1f / 144f, new float3(20f, 0f, 0f));
            w.SetSingleton(new HitStop { Remaining = Tuning.Default.HitStopCut });
            var c0 = w.Singleton<GameClock>();
            w.StepFrames(36); // 0.25 秒
            var c1 = w.Singleton<GameClock>();
            double real = c1.ElapsedReal - c0.ElapsedReal;
            Assert.That(c1.ElapsedGame - c0.ElapsedGame, Is.EqualTo(real - Tuning.Default.HitStopCut).Within(1e-4));
        }
    }
}
