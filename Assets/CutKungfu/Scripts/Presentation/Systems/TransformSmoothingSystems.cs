using CutKungfu.Gameplay;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CutKungfu.Presentation
{
    /// <summary>
    /// 表示の補間に使う、直前2回の固定更新の結果。ゲームロジックの値(LocalTransform)とは別に持つ。
    /// </summary>
    public struct SmoothedTransform : IComponentData
    {
        public float3 PreviousPosition;
        public quaternion PreviousRotation;
        public float3 Position;
        public quaternion Rotation;
        /// <summary>最後に表示用に書いた値(書いた後に誰かが位置を変えたかを見分ける)。</summary>
        public float3 ShownPosition;
        public quaternion ShownRotation;
        /// <summary>LocalTransform に表示用の値が入っている(次の固定更新の前に戻す)。</summary>
        public bool Shown;
        public bool Initialized;
    }

    /// <summary>最後の固定更新のゲーム外の時刻(補間の割合を出すため)。</summary>
    public struct SmoothingClock : IComponentData
    {
        public double LastStepTime;
        public float Step;
        public bool HasStepped;
    }

    /// <summary>
    /// 固定更新の前に、表示用に書き換えた LocalTransform をゲームロジックの値へ戻す。
    /// 表示の後に外から位置を変えられていたら(テレポート・テスト用の配置)、その値をそのまま使う。
    /// </summary>
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
    public partial struct SmoothingRestoreSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (lt, s) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<SmoothedTransform>>())
            {
                ref var sm = ref s.ValueRW;
                if (!sm.Shown) continue;
                sm.Shown = false;
                if (TransformSmoothing.Unchanged(lt.ValueRO, sm))
                {
                    lt.ValueRW.Position = sm.Position;
                    lt.ValueRW.Rotation = sm.Rotation;
                }
                else
                {
                    TransformSmoothing.Snap(ref sm, lt.ValueRO);
                }
            }
        }
    }

    /// <summary>固定更新の後に、その結果を記録する。補間の対象(Fighter)が新しく現れたら印を付ける。</summary>
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderLast = true)]
    public partial struct SmoothingCaptureSystem : ISystem
    {
        EntityQuery _unmarked;

        public void OnCreate(ref SystemState state)
        {
            _unmarked = new EntityQueryBuilder(Allocator.Temp).WithAll<Fighter, LocalTransform>().WithNone<SmoothedTransform>().Build(ref state);
            state.EntityManager.CreateSingleton(new SmoothingClock());
        }

        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.AddComponent<SmoothedTransform>(_unmarked);
            foreach (var (lt, s) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<SmoothedTransform>>())
            {
                ref var sm = ref s.ValueRW;
                if (!sm.Initialized || math.distancesq(sm.Position, lt.ValueRO.Position) > TransformSmoothing.SnapDistance * TransformSmoothing.SnapDistance)
                {
                    // 初回と、1回の更新では動けない距離の移動(テイクのやり直しでの配置など)は補間しない
                    TransformSmoothing.Snap(ref sm, lt.ValueRO);
                    continue;
                }
                sm.PreviousPosition = sm.Position;
                sm.PreviousRotation = sm.Rotation;
                sm.Position = lt.ValueRO.Position;
                sm.Rotation = lt.ValueRO.Rotation;
            }
            SystemAPI.SetSingleton(new SmoothingClock
            {
                LastStepTime = SystemAPI.Time.ElapsedTime, Step = SystemAPI.Time.DeltaTime, HasStepped = true,
            });
        }
    }

    /// <summary>
    /// 描画の前に、直前2回の固定更新の間を補間した値を LocalTransform に書く(変換の確定はこの後)。
    /// 表示は最新の更新から最大1回分遅れるが、更新が0回・2回のフレームが混ざってもカクつかない。
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct SmoothingApplySystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<SmoothingClock>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<SmoothingClock>();
            if (!clock.HasStepped || clock.Step <= 0f) return;
            float alpha = math.saturate((float)((SystemAPI.Time.ElapsedTime - clock.LastStepTime) / clock.Step));
            foreach (var (lt, s) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<SmoothedTransform>>())
            {
                ref var sm = ref s.ValueRW;
                if (!sm.Initialized) continue;
                // 更新が回らなかったフレームに外から動かされた: その位置を受け入れる
                if (sm.Shown && !TransformSmoothing.Unchanged(lt.ValueRO, sm)) TransformSmoothing.Snap(ref sm, lt.ValueRO);
                lt.ValueRW.Position = math.lerp(sm.PreviousPosition, sm.Position, alpha);
                lt.ValueRW.Rotation = math.nlerp(sm.PreviousRotation, sm.Rotation, alpha);
                sm.ShownPosition = lt.ValueRO.Position;
                sm.ShownRotation = lt.ValueRO.Rotation;
                sm.Shown = true;
            }
        }
    }

    static class TransformSmoothing
    {
        /// <summary>これより大きく1回の更新で動いたら、補間せずに飛ばす[m]。</summary>
        public const float SnapDistance = 1.5f;

        public static bool Unchanged(in LocalTransform lt, in SmoothedTransform sm)
            => math.all(lt.Position == sm.ShownPosition) && math.all(lt.Rotation.value == sm.ShownRotation.value);

        public static void Snap(ref SmoothedTransform sm, in LocalTransform lt)
        {
            sm.PreviousPosition = sm.Position = lt.Position;
            sm.PreviousRotation = sm.Rotation = lt.Rotation;
            sm.Initialized = true;
        }
    }
}
