using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Gameplay
{
    public enum Team : byte
    {
        Player = 0,
        Enemy = 1,
    }

    /// <summary>殴り合う者(主人公・敵)に共通の状態。</summary>
    public struct Fighter : IComponentData
    {
        public Team Team;
        public float Radius;
        public float Health;
        public float MaxHealth;
        /// <summary>XZ の向き(正規化)。</summary>
        public float3 Facing;
        public float HitStun;
        public float Invulnerable;
        /// <summary>被弾フラッシュの残り時間。</summary>
        public float Flash;
    }

    /// <summary>吹き飛びの速度。BodyMotionSystem が積分・減衰する。</summary>
    public struct Knockback : IComponentData
    {
        public float3 Velocity;
    }

    /// <summary>重力で机から降りる者。</summary>
    public struct VerticalBody : IComponentData
    {
        public float VelocityY;
    }

    public struct PlayerTag : IComponentData { }

    public enum PlayerActionState : byte
    {
        Free = 0,
        Attack,
        Parry,
        Dodge,
    }

    /// <summary>従来アニメ側(通常の攻防)の行動。カット中は CutState が優先する。</summary>
    public struct PlayerAction : IComponentData
    {
        public PlayerActionState State;
        public float Timer;
        public bool StrikeFired;
        public float3 DodgeDirection;

        public readonly bool IsParryActive(float window) => State == PlayerActionState.Parry && Timer <= window;
    }

    /// <summary>フィルムゲージ(スローとポーズ配置の資源)。</summary>
    public struct FilmGauge : IComponentData
    {
        public float Value;
        public float Max;

        public void Add(float amount) => Value = math.clamp(Value + amount, 0f, Max);
    }

    public struct EnemyTag : IComponentData { }

    public enum EnemyState : byte
    {
        Approach = 0,
        /// <summary>攻撃の予備動作。この間に潰すか、捌くか、置くかを決める。</summary>
        Windup,
        Recover,
        /// <summary>捌かれてよろけている。</summary>
        Staggered,
    }

    public struct EnemyBrain : IComponentData
    {
        public EnemyState State;
        public float Timer;
        public float Cooldown;

        public Unity.Mathematics.Random Random;

        /// <summary>次に殴ってくるまでのおおよその時間(ゲーム時間)。HUD 用。</summary>
        public readonly float NextAttackEta(float windup) => State switch
        {
            EnemyState.Windup => Timer,
            EnemyState.Approach => math.max(0f, Cooldown) + windup,
            _ => Timer + windup,
        };
    }

    /// <summary>倒れて消えるまでの演出中。各システムはこれを持つ敵を無視する。</summary>
    public struct Dying : IComponentData
    {
        public float Timer;
        public float3 FallAxis;
    }
}
