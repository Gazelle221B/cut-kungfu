using Unity.Mathematics;

namespace CutKungfu.Core
{
    public enum PlacementResult : byte
    {
        Ok = 0,
        TooManyPoses,
        NotEnoughGauge,
        RequiresTable,
    }

    /// <summary>
    /// GDD 6章「核の判断」のルールを純粋関数で表したもの。
    /// ECS・UI・テストのすべてがここを参照するので、数値の解釈がずれない。
    /// </summary>
    public static class CutRules
    {
        static int ClampCount(in Tuning t, int poseCount) => math.clamp(poseCount, 0, math.min(t.MaxPoses, 4));

        /// <summary>ポーズ数に応じた威力・映え倍率。0個なら0。</summary>
        public static float PowerMultiplier(in Tuning t, int poseCount)
        {
            int n = ClampCount(t, poseCount);
            return n == 0 ? 0f : t.PowerByPoseCount[n - 1];
        }

        /// <summary>解除後の硬直(コミット時間)。条件 OFF ではつねに0。</summary>
        public static float CommitTime(in Tuning t, int poseCount, bool commitEnabled)
        {
            int n = ClampCount(t, poseCount);
            if (!commitEnabled || n == 0) return 0f;
            return (t.CommitFixed + t.CommitByPoseCount[n - 1]) * t.CommitScale;
        }

        /// <summary>スローに入るために最低限必要なゲージ(固定費+1ポーズ分)。</summary>
        public static float MinimumGaugeToEnterSlow(in Tuning t) => t.SlowFixedCost + t.CostPerPose;

        public static bool CanEnterSlow(in Tuning t, float gauge) => gauge >= MinimumGaugeToEnterSlow(t);

        /// <summary>n個置いた場合の総消費(固定費込み)。</summary>
        public static float TotalCost(in Tuning t, int poseCount) => t.SlowFixedCost + ClampCount(t, poseCount) * t.CostPerPose;

        /// <summary>今のゲージで追加できるポーズ数(スロー突入後、固定費支払い済みのゲージで計算)。</summary>
        public static int AffordablePoses(in Tuning t, float gaugeAfterEntry, int placed)
        {
            int byGauge = t.CostPerPose <= 0f ? int.MaxValue : (int)math.floor(gaugeAfterEntry / t.CostPerPose + 1e-4f);
            return math.max(0, math.min(byGauge, math.min(t.MaxPoses, 4) - placed));
        }

        /// <summary>
        /// 置こうとしている位置を検証し、最終位置を返す。
        /// 位置は直前のアンカーから到達距離で丸め、机の天板上なら天板の高さに乗せる。
        /// </summary>
        /// <param name="anchor">直前のポーズ位置(1個目は自分の位置)。</param>
        /// <param name="aim">照準位置(XZ のみ使う)。</param>
        public static PlacementResult ValidatePlacement(
            in Tuning t, BrickKind kind, int placed, float gaugeAfterEntry,
            float3 anchor, float3 aim, in TableRect table, out float3 position)
        {
            var brick = t.GetBrick(kind);
            position = ClampToReach(anchor, aim, brick.Reach);
            bool onTable = table.Contains(position.xz);
            position.y = onTable ? table.TopY : 0f;

            if (placed >= math.min(t.MaxPoses, 4)) return PlacementResult.TooManyPoses;
            if (gaugeAfterEntry + 1e-4f < t.CostPerPose) return PlacementResult.NotEnoughGauge;
            if (brick.RequiresTable && !onTable) return PlacementResult.RequiresTable;
            return PlacementResult.Ok;
        }

        /// <summary>XZ 平面上で anchor から最大 reach までに丸める。高さは aim のまま。</summary>
        public static float3 ClampToReach(float3 anchor, float3 aim, float reach)
        {
            float2 d = aim.xz - anchor.xz;
            float len = math.length(d);
            if (len <= reach || len < 1e-5f) return aim;
            float2 xz = anchor.xz + d / len * reach;
            return new float3(xz.x, aim.y, xz.y);
        }

        /// <summary>1区間の移動にかける時間(ゲーム時間)。</summary>
        public static float SegmentDuration(in Tuning t, float distance)
            => math.clamp(distance / math.max(0.01f, t.CutDashSpeed), t.CutSegmentMin, t.CutSegmentMax);

        /// <summary>カット技1打のダメージ。倍率はその技全体のポーズ数で決まる。</summary>
        public static float StrikeDamage(in Tuning t, BrickKind kind, int poseCountInCut)
            => t.CutBaseDamage * t.GetBrick(kind).DamageScale * PowerMultiplier(t, poseCountInCut);

        /// <summary>1打で得る映え点。威力に技の映え係数を掛けたもの。</summary>
        public static float HaePoints(in Tuning t, BrickKind kind, float damageDealt)
            => damageDealt * t.GetBrick(kind).HaeScale;
    }
}
