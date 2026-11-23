using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 入力デバイスから切り離した「意図」。Presentation 層の InputBridgeSystem が毎フレーム書く。
    /// ボタンは押した瞬間だけ true になる。同じボタンでも意味は状態で変わる(スロー中の Primary は「置く」)。
    /// </summary>
    public struct PlayerInput : IComponentData
    {
        /// <summary>移動(x=画面右, y=画面奥)。</summary>
        public float2 Move;

        /// <summary>通常時=攻撃、スロー中=ポーズを置く。</summary>
        public bool Primary;
        /// <summary>通常時=捌き、スロー中=直前のポーズを取り消す。</summary>
        public bool Secondary;
        public bool Dodge;
        /// <summary>スローに入る/解除する(止める・切る)。</summary>
        public bool Cut;

        /// <summary>直接選んだブリック(-1 なら選んでいない)。</summary>
        public int SelectBrick;
        /// <summary>ブリックの順送り(-1/0/+1)。</summary>
        public int CycleBrick;

        /// <summary>マウスの照準光線(ワールド)。</summary>
        public bool AimRayValid;
        public float3 AimRayOrigin;
        public float3 AimRayDirection;
        /// <summary>パッドの照準(アンカーからの相対、長さ0..1)。</summary>
        public float2 AimStick;

        // --- プレイテスト用の操作 ---
        public bool Retake;
        public bool NextWave;
        public bool ToggleCommitCondition;
    }
}
