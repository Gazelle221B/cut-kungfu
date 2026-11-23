using CutKungfu.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 動作生成の差し替え口をワールドごとに持つ。Primary が本命(今は手続き補間、将来は MotionBricks)、
    /// Fallback は必ずすぐ準備できる代替(Primary が間に合わないときに使う)。テストでは任意の実装に差し替えられる。
    /// クラス型の IComponentData は Entities 6.6 で非推奨になったため、更新しない managed システムとして置く。
    /// 呼び出し側は state.World.GetOrCreateSystemManaged&lt;CutMotionService&gt;() で取る(Burst の外で)。
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public sealed partial class CutMotionService : SystemBase
    {
        public ICutMotionSource Primary { get; set; } = new ProceduralCutMotionSource();
        public ICutMotionSource Fallback { get; set; } = new ProceduralCutMotionSource();

        protected override void OnCreate() => Enabled = false;

        protected override void OnUpdate() { }

        public ICutMotionSource For(in PlacedPose pose) => pose.UsesFallback ? Fallback : Primary;

        /// <summary>置いたポーズの計画がすべて揃ったか。</summary>
        public bool AllReady(DynamicBuffer<PlacedPose> poses)
        {
            for (int i = 0; i < poses.Length; i++)
                if (!For(poses[i]).IsReady(poses[i].MotionHandle)) return false;
            return true;
        }

        /// <summary>from 番目以降の計画を捨てる。</summary>
        public void Discard(DynamicBuffer<PlacedPose> poses, int from = 0)
        {
            for (int i = from; i < poses.Length; i++) For(poses[i]).Discard(poses[i].MotionHandle);
        }
    }

    /// <summary>区間の組み立て(置いたときと、代替で計画し直すときで同じものを使う)。</summary>
    public static class CutMotionPlanning
    {
        public static MotionSegment Segment(in Tuning tuning, float3 from, float fromYaw, BrickKind kind, float3 to, float toYaw)
            => new MotionSegment
            {
                From = from,
                FromYaw = fromYaw,
                To = to,
                ToYaw = toYaw,
                ArcHeight = tuning.GetBrick(kind).ArcHeight,
                PreferredDuration = CutRules.SegmentDuration(tuning, Unity.Mathematics.math.distance(from.xz, to.xz)),
            };

        /// <summary>区間の始まりで表示している姿勢。今は根の位置と向きだけ。</summary>
        public static MotionContext Context(float3 rootPosition, float rootYaw)
            => new MotionContext { RootPosition = rootPosition, RootYaw = rootYaw };
    }
}
