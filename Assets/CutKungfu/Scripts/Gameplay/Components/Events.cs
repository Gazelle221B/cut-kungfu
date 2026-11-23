using CutKungfu.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Gameplay
{
    public enum StrikeKind : byte
    {
        /// <summary>通常攻撃(従来アニメ側)。</summary>
        Light = 0,
        /// <summary>カット技の1打。</summary>
        Cut,
        /// <summary>敵の攻撃。捌ける。</summary>
        EnemyStrike,
    }

    /// <summary>
    /// 攻撃判定の要求。攻撃する側はこれを積むだけで、当たった後の処理(捌き・無敵・ゲージ・NG理由)は
    /// DamageSystem が一手に引き受ける。判定形状は XZ 平面のカプセル(A==B なら円)。
    /// </summary>
    public struct DamageRequest : IBufferElementData
    {
        public Entity Attacker;
        public Team AttackerTeam;
        public StrikeKind Kind;
        public BrickKind Brick;
        public float2 A;
        public float2 B;
        public float Radius;
        public float Damage;
        public float Knockback;
        public float HitStun;
        public float HitStop;
        /// <summary>
        /// 到達点で狙った敵(命中補正の相手)。経路も判定になる技で、狙った敵と巻き込んだ敵を区別する。いなければ Entity.Null。
        /// </summary>
        public Entity AimedTarget;
        /// <summary>当たった相手が中心にいたときの吹き飛ばし方向。</summary>
        public float2 FallbackDirection;
    }

    public enum FeedbackKind : byte
    {
        Hit = 0,
        CutHit,
        Kill,
        Parry,
        PlayerHurt,
        SlowEnter,
        PosePlaced,
        PoseRejected,
        PoseUndone,
        CutRelease,
        CutCancelled,
        CommitEnd,
        NG,
        Clear,
        TakeStart,
        /// <summary>カット技の1打が、範囲技の上限に掛かって削られた。Value=実際のダメージ。</summary>
        CutHitCapped,
    }

    /// <summary>
    /// 見た目・音の手応えに使う出来事。ゲームロジックは結果を知らなくてよい。
    /// Presentation 層が毎フレーム読み出して空にする。
    /// </summary>
    public struct FeedbackEvent : IBufferElementData
    {
        public FeedbackKind Kind;
        public float3 Position;
        public float Value;
        public int Extra;
    }

    /// <summary>プレイテスト記録。Presentation 層の PlaytestLogSystem が CSV に書き出して空にする。</summary>
    public struct TelemetryEvent : IBufferElementData
    {
        public PlaytestEvent Value;
    }

    /// <summary>
    /// 1回の技の中で、範囲技が各敵に与えたダメージの台帳(上限の計算用)。シングルトンに付き、解除のたびに空にする。
    /// </summary>
    public struct CutAreaDamage : IBufferElementData
    {
        public Entity Enemy;
        public float Taken;
    }

    /// <summary>判定の見える化用の一瞬のエフェクト。</summary>
    public struct StrikeFx : IComponentData
    {
        public float Life;
        public float MaxLife;
        public float StartScale;
        public float EndScale;
    }

    /// <summary>状態で色を変える対象の基本色。</summary>
    public struct CharacterColor : IComponentData
    {
        public float4 Base;
    }
}
