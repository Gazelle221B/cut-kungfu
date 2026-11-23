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
        /// <summary>技の打撃を受けた。被弾硬直のあと、次の待ちに EnemyWaitScaleAfterCutHit を掛けて消す。</summary>
        public bool HitByCut;

        /// <summary>
        /// 次に殴ってくるまでの最短の見込み(ゲーム秒)。HUD とテレメトリ用。
        /// 間合いまで歩く時間と待ち時間は並行して減るので、大きい方に予備動作を足す。
        /// 戻り・よろけの後の待ち時間は乱数なので最短値で見積もる(楽観しないよう早めに出す)。
        /// </summary>
        public readonly float NextAttackEta(in CutKungfu.Core.Tuning t, float distanceToTarget)
        {
            float travel = math.max(0f, distanceToTarget - t.EnemyEngageRange) / math.max(0.01f, t.EnemySpeed);
            return State switch
            {
                EnemyState.Windup => math.max(0f, Timer),
                EnemyState.Approach => math.max(math.max(0f, Cooldown), travel) + t.EnemyWindup,
                EnemyState.Recover => math.max(0f, Timer) + math.max(t.EnemyCooldownMin, travel) + t.EnemyWindup,
                _ => math.max(0f, Timer) + math.max(t.EnemyCooldownMin * 0.5f, travel) + t.EnemyWindup,
            };
        }
    }

    /// <summary>倒れて消えるまでの演出中。各システムはこれを持つ敵を無視する。</summary>
    public struct Dying : IComponentData
    {
        public float Timer;
        public float3 FallAxis;
    }
}
