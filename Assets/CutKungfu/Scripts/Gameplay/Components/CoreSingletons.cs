using CutKungfu.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// ゲーム側の時間。Time.timeScale は使わず、敵・判定・演出はここの DeltaTime だけを見る
    /// (TDD 10章: 対象ごとに倍率を変えられるようにするため)。UI と入力は RealDeltaTime。
    /// </summary>
    public struct GameClock : IComponentData
    {
        public float Scale;
        public float DeltaTime;
        public float RealDeltaTime;
        public double ElapsedGame;
        public double ElapsedReal;
    }

    /// <summary>ヒットストップ(全体停止)の残り実時間。</summary>
    public struct HitStop : IComponentData
    {
        public float Remaining;

        public void Request(float seconds) => Remaining = math.max(Remaining, seconds);
    }

    /// <summary>調整値。Presentation 層の TuningLoaderSystem が JSON から書き込む。</summary>
    public struct TuningSingleton : IComponentData
    {
        public Tuning Value;
        public int Version;
    }

    /// <summary>机の天板(地形連携)。机が無い場面では Exists=false のまま。</summary>
    public struct TableSingleton : IComponentData
    {
        public TableRect Rect;
    }

    /// <summary>移動できる範囲(XZ)。</summary>
    public struct ArenaBounds : IComponentData
    {
        public float2 Min;
        public float2 Max;
    }

    /// <summary>画面の揺れ要求。CameraRigSystem が消費して減衰させる。</summary>
    public struct CameraShake : IComponentData
    {
        public float Amplitude;

        public void Add(float amount) => Amplitude = math.min(1.2f, math.max(Amplitude, amount));
    }
}
