using Unity.Mathematics;

namespace CutKungfu.Core
{
    /// <summary>ポーズ間の1区間。動作生成への入力。</summary>
    public struct MotionSegment
    {
        public float3 From;
        public float FromYaw;
        public float3 To;
        /// <summary>目標ポーズの向き(Y 軸回り, rad)。置いたときに決まる。</summary>
        public float ToYaw;
        public float ArcHeight;
        public float Duration;
    }

    /// <summary>動作生成の出力1フレーム分。</summary>
    public struct MotionSample
    {
        public float3 Position;
        public quaternion Rotation;
        /// <summary>打撃の接触フレームに達したか(この区間で一度だけ判定を出す基準)。</summary>
        public bool Contact;
        /// <summary>区間を再生し終えたか。</summary>
        public bool Finished;
    }

    /// <summary>
    /// ポーズとポーズの間の動きを生成する層の「仮実装」。
    /// 本番では MotionBricks(直近4ポーズ+目標ポーズ → 30Hz の関節列)に差し替える。
    /// 呼び出し側(CutExecutionSystem)は区間を渡し、位置・向き・接触フレーム・終了だけを受け取るので、
    /// 生成した動きが独自の根の回転や接触タイミングを持っていても、戦闘側の処理は変わらない。
    /// 仮実装では接触=到達(区間の終わり)とする。
    /// </summary>
    public static class CutMotion
    {
        public static MotionSample Sample(in MotionSegment s, float time)
        {
            float t = s.Duration <= 1e-5f ? 1f : math.saturate(time / s.Duration);
            float e = EaseOutCubic(t);
            float3 p = math.lerp(s.From, s.To, e);
            p.y += s.ArcHeight * 4f * e * (1f - e);
            // 出だしで目標の向きへ素早く振り向く(構え→攻撃の瞬発力)
            float yaw = LerpAngle(s.FromYaw, s.ToYaw, math.saturate(t * 3f));
            bool done = t >= 1f;
            return new MotionSample
            {
                Position = p,
                Rotation = quaternion.RotateY(yaw),
                Contact = done,
                Finished = done,
            };
        }

        /// <summary>区間 [from, to] 上の位置だけを返す簡易版。t は 0..1。</summary>
        public static float3 Evaluate(float3 from, float3 to, float arcHeight, float t)
            => Sample(new MotionSegment { From = from, To = to, ArcHeight = arcHeight, Duration = 1f }, t).Position;

        public static float EaseOutCubic(float t)
        {
            float u = 1f - math.saturate(t);
            return 1f - u * u * u;
        }

        public static float YawOf(float3 forward) => math.atan2(forward.x, forward.z);

        static float LerpAngle(float a, float b, float t)
        {
            float d = math.fmod(b - a + math.PI * 3f, math.PI * 2f) - math.PI;
            return a + d * t;
        }
    }
}
