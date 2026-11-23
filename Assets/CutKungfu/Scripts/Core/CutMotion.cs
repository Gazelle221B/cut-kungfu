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
        /// <summary>
        /// 呼び出し側が望む長さ(ゲーム秒)。仮実装はこれをそのまま使う。
        /// 生成側が長さを決める実装(MotionBricks)は参考にとどめてよい。
        /// </summary>
        public float PreferredDuration;
    }

    /// <summary>
    /// 区間を生成するときの文脈(直前に表示していた姿勢)。今は根の位置と向きだけ。
    /// MotionBricks では直近4フレームの関節ポーズが要るので、そのときはここにフィールドを足す
    /// (呼び出し側は値を詰めるだけで、インターフェースは変わらない)。
    /// </summary>
    public struct MotionContext
    {
        public float3 RootPosition;
        public float RootYaw;
    }

    /// <summary>動作生成の出力1フレーム分(根の位置と向き)。</summary>
    public struct MotionSample
    {
        public float3 Position;
        public quaternion Rotation;
    }

    /// <summary>計画の識別子。0 は「計画なし」。</summary>
    public static class MotionHandle
    {
        public const int None = 0;
    }

    /// <summary>
    /// ポーズ間の動きを作る層の差し替え口。「計画」(スロー中、置いたときに呼ぶ)と「再生」(切った後)に分かれる。
    /// MotionBricks のように、直近の姿勢を文脈に取り、非同期に推論し、長さと接触の時刻を自分で決める生成器を想定する。
    /// </summary>
    public interface ICutMotionSource
    {
        /// <summary>区間の生成を始める。結果はすぐ揃わなくてよい。</summary>
        int Plan(in MotionSegment segment, in MotionContext context);

        /// <summary>生成が終わり、Duration・ContactTime・Sample が使えるか。捨てた計画は false。</summary>
        bool IsReady(int handle);

        /// <summary>生成側が決めた区間の長さ(ゲーム秒)。準備完了後に有効。</summary>
        float Duration(int handle);

        /// <summary>打撃が接触する時刻(区間の始めからのゲーム秒)。終点とは限らない。</summary>
        float ContactTime(int handle);

        MotionSample Sample(int handle, float time);

        /// <summary>計画を捨てる(取り消し・中断・再生後)。未知の識別子や二重の呼び出しは無視する。</summary>
        void Discard(int handle);
    }

    /// <summary>
    /// ポーズとポーズの間の根の動き(手続き的な補間)。MotionBricks が入るまでの仮の動き。
    /// </summary>
    public static class CutMotion
    {
        /// <summary>区間を長さ duration で time 時点まで進めたときの位置と向き。</summary>
        public static MotionSample Sample(in MotionSegment s, float duration, float time)
        {
            float t = duration <= 1e-5f ? 1f : math.saturate(time / duration);
            float e = EaseOutCubic(t);
            float3 p = math.lerp(s.From, s.To, e);
            p.y += s.ArcHeight * 4f * e * (1f - e);
            // 出だしで目標の向きへ素早く振り向く(構え→攻撃の瞬発力)
            float yaw = LerpAngle(s.FromYaw, s.ToYaw, math.saturate(t * 3f));
            return new MotionSample { Position = p, Rotation = quaternion.RotateY(yaw) };
        }

        /// <summary>区間 [from, to] 上の位置だけを返す簡易版。t は 0..1。</summary>
        public static float3 Evaluate(float3 from, float3 to, float arcHeight, float t)
            => Sample(new MotionSegment { From = from, To = to, ArcHeight = arcHeight }, 1f, t).Position;

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
