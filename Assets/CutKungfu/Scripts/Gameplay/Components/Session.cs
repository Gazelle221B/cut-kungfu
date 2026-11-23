using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Gameplay
{
    public enum SessionPhase : byte
    {
        /// <summary>次のフレームでウェーブを組み立てる。</summary>
        PendingStart = 0,
        Playing,
        /// <summary>体力ゼロ。「NG!」を出して同じウェーブの頭からテイクし直す。</summary>
        NG,
        /// <summary>敵を全滅させた。「カット!OK」を出して次のウェーブへ。</summary>
        Cleared,
    }

    public enum NgReason : byte
    {
        None = 0,
        HitDuringCommit,
        HitWhileFree,
    }

    /// <summary>撮影の進行。シングルトン。</summary>
    public struct SessionState : IComponentData
    {
        public SessionPhase Phase;
        public float PhaseTimer;
        public int WaveIndex;
        public int Take;
        /// <summary>プレイテストの比較条件: コミット時間あり/なし。</summary>
        public bool CommitEnabled;
        public float Score;

        // NG 理由の表示用(GDD 11章: 「4つ置いたから殴られた」が一目で分かること)
        public NgReason LastNgReason;
        public int LastCutPoses;
        public float LastCommitTime;
    }

    /// <summary>ウェーブ定義。シナリオ実体にバッファで並ぶ。</summary>
    public struct ScenarioWave : IBufferElementData
    {
        public float3 PlayerStart;
        public int FirstSpawn;
        public int SpawnCount;
    }

    /// <summary>敵の出現位置。<see cref="ScenarioWave"/> から範囲で参照する。</summary>
    public struct ScenarioSpawn : IBufferElementData
    {
        public float3 Position;
    }

    /// <summary>実行時に生成するプレハブ群。</summary>
    public struct PrefabLibrary : IComponentData
    {
        public Entity Enemy;
        public Entity GhostPalm;
        public Entity GhostSpin;
        public Entity GhostFlyingKick;
        public Entity GhostTableLanding;
        public Entity StrikeFx;

        public readonly Entity Ghost(CutKungfu.Core.BrickKind kind) => kind switch
        {
            CutKungfu.Core.BrickKind.Spin => GhostSpin,
            CutKungfu.Core.BrickKind.FlyingKick => GhostFlyingKick,
            CutKungfu.Core.BrickKind.TableLanding => GhostTableLanding,
            _ => GhostPalm,
        };
    }
}
