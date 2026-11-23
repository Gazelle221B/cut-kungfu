using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 入力デバイスから切り離した「意図」。Presentation 層の InputBridgeSystem が毎フレーム書く。
    /// 押下(ボタン・ブリックの選択・順送り)は、ゲームの更新で1回使われるまで残り、使われたら PlayerInputConsumeSystem が消す。
    /// ゲームは固定タイムステップで回るので、1フレームに更新が0回でも押下を落とさず、2回以上でも2度効かない。
    /// 同じボタンでも意味は状態で変わる(スロー中の Primary は「置く」)。
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

        /// <summary>まだ使われていない押下(pending)に、新しく読んだ入力(latest)を重ねる。移動・照準は新しい方を使う。</summary>
        public static PlayerInput Accumulate(in PlayerInput pending, in PlayerInput latest)
        {
            var r = latest;
            r.Primary |= pending.Primary;
            r.Secondary |= pending.Secondary;
            r.Dodge |= pending.Dodge;
            r.Cut |= pending.Cut;
            r.Retake |= pending.Retake;
            r.NextWave |= pending.NextWave;
            r.ToggleCommitCondition |= pending.ToggleCommitCondition;
            if (r.SelectBrick < 0) r.SelectBrick = pending.SelectBrick;
            r.CycleBrick += pending.CycleBrick;
            return r;
        }

        /// <summary>押下を消した入力(1回の更新で使い終えた後)。移動・照準は残す。</summary>
        public readonly PlayerInput WithoutPresses()
        {
            var r = this;
            r.Primary = r.Secondary = r.Dodge = r.Cut = false;
            r.Retake = r.NextWave = r.ToggleCommitCondition = false;
            r.SelectBrick = -1;
            r.CycleBrick = 0;
            return r;
        }
    }
}
