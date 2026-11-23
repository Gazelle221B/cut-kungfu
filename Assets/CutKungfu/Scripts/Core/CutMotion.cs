using Unity.Mathematics;

namespace CutKungfu.Core
{
    /// <summary>
    /// ポーズとポーズの間の動きを生成する層の「仮実装」。
    /// 本番では MotionBricks(直近4ポーズ+目標ポーズ → 30Hz の関節列)に差し替える想定で、
    /// ここでは根の位置だけを手続き的に補間する。呼び出し側は「区間の始点・終点・進行率」しか渡さないので、
    /// 差し替え時も CutExecutionSystem の外形は変わらない。
    /// </summary>
    public static class CutMotion
    {
        /// <summary>区間 [from, to] 上の位置を返す。t は 0..1。</summary>
        public static float3 Evaluate(float3 from, float3 to, float arcHeight, float t)
        {
            t = math.saturate(t);
            // 出だしを速く、到達で粘る(構え→攻撃の瞬発力と、決めポーズの見得)
            float e = EaseOutCubic(t);
            float3 p = math.lerp(from, to, e);
            p.y += arcHeight * 4f * e * (1f - e);
            return p;
        }

        public static float EaseOutCubic(float t)
        {
            float u = 1f - math.saturate(t);
            return 1f - u * u * u;
        }
    }
}
