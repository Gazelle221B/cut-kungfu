using CutKungfu.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Gameplay
{
    public enum CutPhase : byte
    {
        None = 0,
        /// <summary>止める: 時間が遅くなり、ポーズを置ける。</summary>
        Slow,
        /// <summary>切る: 置いたポーズを順に辿って技を出す。</summary>
        Executing,
        /// <summary>解除後の硬直(コミット時間)。行動できない。</summary>
        Commit,
    }

    /// <summary>
    /// コア動詞「止める・置く・切る」の状態。主人公に付く。
    /// </summary>
    public struct CutState : IComponentData
    {
        public CutPhase Phase;
        public BrickKind SelectedKind;
        public float SlowRealElapsed;

        // --- 照準プレビュー(半透明の人影) ---
        public Entity Preview;
        public BrickKind PreviewKind;
        public float3 PreviewPosition;
        public PlacementResult PreviewResult;

        // --- 実行 ---
        public int PoseCount;
        public int SegmentIndex;
        public float SegmentTime;
        public float SegmentDuration;
        public float3 SegmentFrom;
        public float SegmentFromYaw;
        /// <summary>今の区間の打撃を出したか(接触フレームで一度だけ出す)。</summary>
        public bool StrikeFired;
        public float CommitDuration;
        public float CommitRemaining;

        /// <summary>このカットの記録(解除〜硬直明けで確定してテレメトリへ流す)。</summary>
        public PlaytestEvent Record;

        /// <summary>このフレームの入力をカット操作が使った(同じ押下で捌き等を出さない)。</summary>
        public bool InputConsumed;

        public readonly bool IsActive => Phase != CutPhase.None;

        /// <summary>殴られたら硬直の罰を受ける状態か。硬直 0 秒(条件 B)の確定待ち1フレームは含めない。</summary>
        public readonly bool IsCommitted => Phase == CutPhase.Commit && CommitDuration > 0f;
    }

    /// <summary>置いたポーズ(最大4)。置いた順に並ぶ。</summary>
    [InternalBufferCapacity(4)]
    public struct PlacedPose : IBufferElementData
    {
        public BrickKind Kind;
        public float3 Position;
        public float Yaw;
        /// <summary>この技の中で、同じブリックを前に何回置いたか(初回 0)。繰り返しの減衰に使う。</summary>
        public int RepeatIndex;
        public Entity Ghost;
    }

    /// <summary>空間に残る半透明の人影(置いたポーズ)。</summary>
    public struct GhostPose : IComponentData
    {
        public int Order;
        public bool IsPreview;
    }
}
