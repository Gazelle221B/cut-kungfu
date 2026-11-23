using Unity.Mathematics;

namespace CutKungfu.Core
{
    /// <summary>ポーズブリック1種ぶんの調整値。</summary>
    [System.Serializable]
    public struct BrickTuning
    {
        /// <summary>直前のアンカー(前のポーズか自分)からどこまで離して置けるか[m]。</summary>
        public float Reach;
        /// <summary>到達時の打撃判定の半径[m]。</summary>
        public float StrikeRadius;
        /// <summary>打撃判定の中心を進行方向へずらす量[m]。0 なら自分中心(回し蹴り)。</summary>
        public float StrikeForward;
        /// <summary>カット基礎ダメージに掛ける技ごとの係数。</summary>
        public float DamageScale;
        /// <summary>吹き飛ばしの初速[m/s]。</summary>
        public float Knockback;
        /// <summary>映え点に掛ける係数(地形連携で上げる)。</summary>
        public float HaeScale;
        /// <summary>移動の弧の高さ[m]。</summary>
        public float ArcHeight;
        /// <summary>true なら移動経路全体が判定になる(飛び蹴り)。</summary>
        public bool SweepPath;
        /// <summary>true なら机の上にしか置けない。</summary>
        public bool RequiresTable;
    }

    /// <summary>
    /// 試作の全調整値。GDD の【】仮値を1か所に集めたもの。
    /// ECS のシングルトンにそのまま載せられるよう blittable に保つ(配列は float4 で持つ)。
    /// JsonUtility でそのまま読み書きできる(Presentation 層の TuningLoaderSystem)。
    /// </summary>
    [System.Serializable]
    public struct Tuning
    {
        // --- カット(スロー→置く→切る) ---
        public float SlowTimeScale;
        public float SlowMaxRealSeconds;
        public int MaxPoses;
        public float GaugeMax;
        public float SlowFixedCost;
        public float CostPerPose;
        public float GaugeOnHit;
        public float GaugeOnParry;
        /// <summary>ウェーブ開始時のゲージ。満タン未満にして「今止めるか、溜めて待つか」(GDD 6章 第3の判断)を作る。</summary>
        public float GaugeAtWaveStart;
        /// <summary>解除後コミット時間の固定分[s]。</summary>
        public float CommitFixed;
        /// <summary>ポーズ数 1..4 ごとの追加コミット時間[s](x=1個 … w=4個)。</summary>
        public float4 CommitByPoseCount;
        /// <summary>コミット時間全体の倍率。プレイテストの「2倍の極端な値」用。</summary>
        public float CommitScale;
        /// <summary>ポーズ数 1..4 ごとの威力・映え倍率。</summary>
        public float4 PowerByPoseCount;
        public float CutBaseDamage;
        public float CutDashSpeed;
        public float CutSegmentMin;
        public float CutSegmentMax;
        /// <summary>
        /// 同じ技の中で同じブリックを繰り返したときの威力の倍率(2回目は×s、3回目は×s²…)。1 で無効。
        /// 範囲技を重ねて包囲を1回で全滅させる解を消すためのルール(SurroundBalanceTests)。
        /// </summary>
        public float RepeatBrickScale;
        /// <summary>
        /// 1回の技の中で、範囲技(回し蹴り・机上の着地)が同じ敵に与える合計ダメージの上限(敵の最大体力に対する割合)。
        /// 範囲技を混ぜて包囲を1回で全滅させる解を消す(とどめは単体技に任せる)。1 以上で無効。技ごとにリセットする。
        /// </summary>
        public float AreaDamageCapRatio;
        /// <summary>打点を近くの敵へ寄せる最大距離[m](GDD 15章「拳が敵に当たらない」への補正層)。0 で無効。</summary>
        public float StrikeAssist;
        public bool InvulnerableDuringSlow;
        public bool InvulnerableDuringExecution;
        /// <summary>硬直中に受けた攻撃の倍率(カウンターヒット)。硬直の罰を体力でも見せる。</summary>
        public float CounterHitMultiplier;

        public BrickTuning Palm;
        public BrickTuning Spin;
        public BrickTuning FlyingKick;
        public BrickTuning TableLanding;

        // --- プレイヤー ---
        public float PlayerMaxHealth;
        public float PlayerSpeed;
        public float PlayerRadius;
        public float AttackStartup;
        public float AttackRecovery;
        public float AttackRadius;
        public float AttackForward;
        public float AttackDamage;
        public float AttackKnockback;
        public float ParryWindow;
        public float ParryRecovery;
        public float DodgeDuration;
        public float DodgeSpeed;
        public float DodgeInvulnerable;
        public float PlayerHitStun;
        public float PlayerPostHitInvulnerable;

        // --- 敵(雑魚) ---
        public float EnemyMaxHealth;
        public float EnemySpeed;
        public float EnemyRadius;
        public float EnemyEngageRange;
        public float EnemyWindup;
        public float EnemyRecover;
        public float EnemyCooldownMin;
        public float EnemyCooldownMax;
        public float EnemyStrikeRadius;
        public float EnemyStrikeForward;
        public float EnemyDamage;
        public float EnemyKnockback;
        public float EnemyHitStun;
        public float EnemyCutHitStun;
        /// <summary>
        /// 範囲技(上限 AreaDamageCapRatio の対象になる打撃)で当てた敵の被弾硬直[s]。狙った敵への単体の打撃は EnemyCutHitStun。
        /// 被弾硬直は予備動作を潰すので、これを短くすると、範囲技で削った敵が技の途中で殴り返せる。
        /// </summary>
        public float EnemyAreaHitStun;
        /// <summary>
        /// 技で打撃を受けた敵が、被弾硬直のあとに取る「次の予備動作までの待ち」に掛ける倍率(1=従来、0=待たずに予備動作へ)。
        /// 予備動作を潰された敵は戻りのあとに引く新しい待ちに、接近中だった敵は残っていた待ちに掛かる。
        /// 同時に予備動作に入れる数(EnemyMaxConcurrentWindups)の制限はそのまま。
        /// </summary>
        public float EnemyWaitScaleAfterCutHit;
        public float EnemyStaggerOnParry;
        public int EnemyMaxConcurrentWindups;

        // --- 手応え ---
        public float HitStopLight;
        public float HitStopCut;
        public float HitStopParry;
        public float HitStopPlayerHurt;

        // --- 進行 ---
        public float NgDelay;
        public float ClearDelay;

        public readonly BrickTuning GetBrick(BrickKind kind) => kind switch
        {
            BrickKind.Palm => Palm,
            BrickKind.Spin => Spin,
            BrickKind.FlyingKick => FlyingKick,
            BrickKind.TableLanding => TableLanding,
            _ => Palm,
        };

        /// <summary>GDD v0.2 の仮値。JSON が無いときもこの値で動く。</summary>
        public static Tuning Default => new Tuning
        {
            SlowTimeScale = 0.1f,
            SlowMaxRealSeconds = 4f,
            MaxPoses = 4,
            GaugeMax = 100f,
            SlowFixedCost = 20f,
            CostPerPose = 10f,
            GaugeOnHit = 5f,
            GaugeOnParry = 20f,
            GaugeAtWaveStart = 50f,
            CommitFixed = 0.3f,
            // GDD 6章の仮値(0.2/0.4/0.7/1.1)では、最後のポーズで空き地へ逃げれば硬直を踏み倒せた(設計レビュー)。
            // 3〜4個の隙を、敵が詰めてくる時間より長くする。
            CommitByPoseCount = new float4(0.2f, 0.5f, 0.9f, 1.5f),
            CommitScale = 1f,
            // 4個は GDD の仮値 3.5 から 2.8 に下げた: 3.5 では範囲技1つで3体を削り、単体技3つで1体ずつとどめを刺して
            // 包囲を1回で全滅できた(Measure_KnownWipes_CountermeasureGrid で 85%)。2.8 で 0%、前提はすべて保つ
            PowerByPoseCount = new float4(1.0f, 1.6f, 2.4f, 2.8f),
            CutBaseDamage = 20f,
            CutDashSpeed = 16f,
            CutSegmentMin = 0.14f,
            CutSegmentMax = 0.38f,
            RepeatBrickScale = 0.5f,
            AreaDamageCapRatio = 0.8f,
            StrikeAssist = 1.2f,
            InvulnerableDuringSlow = true,
            // 実行中も殴られる(TDD 7章「中断」)。スロー中に敵の予備動作を読む意味を残す
            InvulnerableDuringExecution = false,
            CounterHitMultiplier = 1.5f,

            Palm = new BrickTuning
            {
                Reach = 4.5f, StrikeRadius = 1.1f, StrikeForward = 0.8f, DamageScale = 1.2f,
                Knockback = 11f, HaeScale = 1f, ArcHeight = 0f,
            },
            Spin = new BrickTuning
            {
                Reach = 4f, StrikeRadius = 2.1f, StrikeForward = 0f, DamageScale = 0.8f,
                Knockback = 8f, HaeScale = 1.1f, ArcHeight = 0.3f,
            },
            FlyingKick = new BrickTuning
            {
                Reach = 5.5f, StrikeRadius = 0.9f, StrikeForward = 0.5f, DamageScale = 1f,
                Knockback = 13f, HaeScale = 1.2f, ArcHeight = 1.4f, SweepPath = true,
            },
            TableLanding = new BrickTuning
            {
                Reach = 6f, StrikeRadius = 2.0f, StrikeForward = 0f, DamageScale = 0.9f,
                Knockback = 9f, HaeScale = 1.6f, ArcHeight = 1.2f, RequiresTable = true,
            },

            PlayerMaxHealth = 100f,
            PlayerSpeed = 5.2f,
            PlayerRadius = 0.4f,
            AttackStartup = 0.07f,
            AttackRecovery = 0.2f,
            AttackRadius = 0.85f,
            AttackForward = 0.85f,
            AttackDamage = 10f,
            AttackKnockback = 3.5f,
            ParryWindow = 0.18f,
            ParryRecovery = 0.25f,
            DodgeDuration = 0.28f,
            DodgeSpeed = 12f,
            DodgeInvulnerable = 0.24f,
            PlayerHitStun = 0.4f,
            PlayerPostHitInvulnerable = 0.6f,

            // 散らして1体1ポーズでは倒しきれず(4個でも67.2)、1体に集中すれば3ポーズで倒せる(同じ掌底3発でも100.8)。
            // 「全員を一度に片付けて硬直を踏み倒す」解を消し、置く数の判断を残すための値。
            EnemyMaxHealth = 90f,
            EnemySpeed = 3.2f,
            EnemyRadius = 0.42f,
            EnemyEngageRange = 1.35f,
            EnemyWindup = 0.9f,
            EnemyRecover = 0.6f,
            EnemyCooldownMin = 0.9f,
            EnemyCooldownMax = 2.4f,
            EnemyStrikeRadius = 0.85f,
            EnemyStrikeForward = 0.9f,
            EnemyDamage = 16f,
            EnemyKnockback = 5f,
            EnemyHitStun = 0.3f,
            EnemyCutHitStun = 0.8f,
            EnemyAreaHitStun = 0.8f,
            EnemyWaitScaleAfterCutHit = 1f,
            EnemyStaggerOnParry = 1.2f,
            EnemyMaxConcurrentWindups = 2,

            HitStopLight = 0.05f,
            HitStopCut = 0.09f,
            HitStopParry = 0.12f,
            HitStopPlayerHurt = 0.08f,

            NgDelay = 1.6f,
            ClearDelay = 1.6f,
        };
    }
}
