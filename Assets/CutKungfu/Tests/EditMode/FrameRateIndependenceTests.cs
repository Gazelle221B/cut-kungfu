using CutKungfu.Core;
using CutKungfu.Gameplay;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Transforms;

namespace CutKungfu.Tests
{
    /// <summary>
    /// 1回の更新の長さ(dt)を変えても、同じ時間が経てば同じ結果になるか。直した箇所ごとに1つ。
    /// 出来事は起きた更新の終わりに観測するので、観測時刻は [本当の時刻, 本当の時刻 + dt) に入ることを確かめる。
    /// </summary>
    public class FrameRateIndependenceTests
    {
        const float Coarse = 1f / 30f;
        const float Fine = 1f / 240f;

        // --- 積分(Core) ---

        [Test]
        public void DampedStep_TravelsTheSameDistance_AtAnyStepLength()
        {
            float Travel(float dt)
            {
                var v = new float3(8f, 0f, 0f);
                float x = 0f;
                for (int i = 0; i < (int)math.round(0.5f / dt); i++) x += Kinematics.DampedStep(ref v, 7f, dt).x;
                return x;
            }
            float exact = 8f * (1f - math.exp(-7f * 0.5f)) / 7f;
            Assert.That(Travel(Coarse), Is.EqualTo(exact).Within(1e-4f));
            Assert.That(Travel(Fine), Is.EqualTo(exact).Within(1e-4f));
        }

        [Test]
        public void AcceleratedStep_FallsTheSameDistance_AtAnyStepLength()
        {
            float Fall(float dt)
            {
                float v = 2f, y = 0f;
                for (int i = 0; i < (int)math.round(0.4f / dt); i++) y += Kinematics.AcceleratedStep(ref v, -28f, dt);
                return y;
            }
            float exact = 2f * 0.4f - 0.5f * 28f * 0.4f * 0.4f;
            Assert.That(Fall(Coarse), Is.EqualTo(exact).Within(1e-4f));
            Assert.That(Fall(Fine), Is.EqualTo(exact).Within(1e-4f));
        }

        // --- ゲームの更新 ---

        /// <summary>主人公と敵1体だけのワールド。</summary>
        static GameplayTestWorld LoneWorld(float dt, float3 enemyAt, Tuning? tuning = null)
        {
            var w = new GameplayTestWorld(float3.zero, enemyAt) { StepDt = dt };
            if (tuning.HasValue)
            {
                var ts = w.Singleton<TuningSingleton>();
                ts.Value = tuning.Value;
                w.SetSingleton(ts);
            }
            w.Step();
            return w;
        }

        static double GameTime(GameplayTestWorld w) => w.Singleton<GameClock>().ElapsedGame;

        static void AssertObservedAt(double observed, double trueTime, float dt, string what)
        {
            Assert.That(observed, Is.GreaterThanOrEqualTo(trueTime - 1e-4), $"{what}: 早すぎる(dt 1/{math.round(1f / dt)})");
            Assert.That(observed, Is.LessThan(trueTime + dt + 1e-4), $"{what}: 遅すぎる(dt 1/{math.round(1f / dt)})");
        }

        /// <summary>吹き飛びの止まる位置は dt で変わらない(毎回速度に係数を掛けると、dt が大きいほど遠くへ飛ぶ)。</summary>
        [Test]
        public void Knockback_StopsAtTheSamePlace_AtAnyStepLength()
        {
            float Travel(float dt)
            {
                using var w = LoneWorld(dt, new float3(20f, 0f, 0f));
                var enemy = w.Enemies()[0];
                var f = w.Em.GetComponentData<Fighter>(enemy);
                f.HitStun = 5f; // 吹き飛びの間に歩かせない
                w.Em.SetComponentData(enemy, f);
                w.Em.SetComponentData(enemy, new Knockback { Velocity = new float3(8f, 0f, 0f) });
                float x0 = w.Em.GetComponentData<LocalTransform>(enemy).Position.x;
                w.StepFrames((int)math.ceil(1.5f / dt));
                return w.Em.GetComponentData<LocalTransform>(enemy).Position.x - x0;
            }
            Assert.That(Travel(Coarse), Is.EqualTo(Travel(Fine)).Within(0.02f));
        }

        /// <summary>技の長さ(解除から硬直明けまで)は dt で変わらない(区間の終わりで余った時間を捨てると、dt が大きいほど遅れる)。</summary>
        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 240f)]
        public void CutDuration_DoesNotGrowWithStepLength(float dt)
        {
            using var w = LoneWorld(dt, new float3(30f, 0f, 0f));
            w.SetGauge(100f);
            w.PlacePoses((BrickKind.Palm, new float3(1f, 0f, 0f)), (BrickKind.Palm, new float3(1f, 0f, 1f)),
                (BrickKind.Palm, new float3(0f, 0f, 1f)), (BrickKind.Palm, new float3(0f, 0f, 0f)));
            w.ReleaseCut();
            var cut = w.Em.GetComponentData<CutState>(w.Player);
            Assert.That(cut.IsActive, Is.True, "技が始まっていない");
            // 4区間とも 1m(最短の区間時間で頭打ち)+ 硬直
            var t = Tuning.Default;
            double expected = 4 * CutRules.SegmentDuration(t, 1f) + cut.CommitDuration;
            double start = GameTime(w) - w.Singleton<GameClock>().DeltaTime; // 解除の更新でも技は1回分進んでいる
            for (int i = 0; i < 10f / dt && w.Em.GetComponentData<CutState>(w.Player).IsActive; i++) w.Step();
            AssertObservedAt(GameTime(w) - start, expected, dt, "技の長さ");
        }

        /// <summary>敵の 待ち→予備動作→打撃→戻り→待ち→予備動作 の時刻は dt で変わらない(切り替わりで余った時間を捨てない)。</summary>
        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 240f)]
        public void EnemyAttackCycle_KeepsItsTiming_AtAnyStepLength(float dt)
        {
            // 更新の長さの倍数にならない長さにする(倍数だと、切り替わりで捨てる時間が 0 になって違いが出ない)
            var tuning = Tuning.Default;
            tuning.EnemyWindup = 0.91f;
            tuning.EnemyRecover = 0.61f;
            const float firstWait = 0.51f;
            var draw = Random.CreateFromIndex(7);
            float secondWait = draw.NextFloat(tuning.EnemyCooldownMin, tuning.EnemyCooldownMax);

            // 敵は交戦距離にいて歩かない。主人公は無敵にして、殴られても動かないようにする
            using var w = LoneWorld(dt, new float3(tuning.EnemyEngageRange, 0f, 0f), tuning);
            var enemy = w.Enemies()[0];
            var brain = w.Em.GetComponentData<EnemyBrain>(enemy);
            brain.State = EnemyState.Approach;
            brain.Cooldown = firstWait;
            brain.Random = Random.CreateFromIndex(7);
            w.Em.SetComponentData(enemy, brain);

            double t0 = GameTime(w);
            double strike = -1, windup = -1;
            var last = EnemyState.Approach;
            for (int i = 0; i < 8f / dt && windup < 0; i++)
            {
                var pf = w.Em.GetComponentData<Fighter>(w.Player);
                pf.Invulnerable = 100f;
                w.Em.SetComponentData(w.Player, pf);
                w.Step();
                var s = w.Em.GetComponentData<EnemyBrain>(enemy).State;
                if (last == EnemyState.Windup && s == EnemyState.Recover && strike < 0) strike = GameTime(w) - t0;
                if (strike >= 0 && last == EnemyState.Approach && s == EnemyState.Windup) windup = GameTime(w) - t0;
                last = s;
            }
            Assert.That(windup, Is.GreaterThan(0), "2回目の予備動作に入っていない");
            AssertObservedAt(strike, firstWait + tuning.EnemyWindup, dt, "1回目の打撃");
            AssertObservedAt(windup, firstWait + tuning.EnemyWindup + tuning.EnemyRecover + secondWait, dt, "2回目の予備動作の開始");
        }

        /// <summary>被弾硬直が更新の途中で明けたとき、待ちは明けた後の時間だけ減る(硬直の最後の更新で丸ごと減らさない)。</summary>
        [TestCase(1f / 30f)]
        [TestCase(1f / 240f)]
        public void WaitAfterHitStun_CountsOnlyTimeAfterTheStunEnds(float dt)
        {
            using var w = LoneWorld(dt, new float3(Tuning.Default.EnemyEngageRange, 0f, 0f));
            var enemy = w.Enemies()[0];
            var brain = w.Em.GetComponentData<EnemyBrain>(enemy);
            brain.State = EnemyState.Approach;
            // 待ちは更新の長さの倍数の少し手前にする(硬直の最後の更新で丸ごと減らすと、本当の時刻より早く予備動作に入る)
            brain.Cooldown = 0.395f;
            w.Em.SetComponentData(enemy, brain);
            var f = w.Em.GetComponentData<Fighter>(enemy);
            f.HitStun = 0.31f;
            w.Em.SetComponentData(enemy, f);

            double t0 = GameTime(w), windupAt = -1;
            for (int i = 0; i < 4f / dt && windupAt < 0; i++)
            {
                w.Step();
                if (w.Em.GetComponentData<EnemyBrain>(enemy).State == EnemyState.Windup) windupAt = GameTime(w) - t0;
            }
            // 硬直 0.31 秒 + 待ち 0.395 秒 = 0.705 秒後に予備動作に入る
            AssertObservedAt(windupAt, 0.705, dt, "予備動作の開始");
        }

        /// <summary>ヒットストップが更新の途中で明けたら、明けた後の分だけゲーム時間が進む(止まる長さが dt に丸められない)。</summary>
        [TestCase(1f / 30f)]
        [TestCase(1f / 240f)]
        public void HitStop_StopsGameTimeForItsDuration_AtAnyStepLength(float dt)
        {
            using var w = LoneWorld(dt, new float3(30f, 0f, 0f));
            w.SetSingleton(new HitStop { Remaining = Tuning.Default.HitStopCut });
            double t0 = GameTime(w);
            w.StepFrames((int)math.round(0.2f / dt));
            Assert.That(GameTime(w) - t0, Is.EqualTo(0.2 - Tuning.Default.HitStopCut).Within(1e-4));
        }
    }
}
