using CutKungfu.Gameplay;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace CutKungfu.Presentation
{
    /// <summary>
    /// 横視点寄りのカメラ(映画のフレーム)。主人公を追い、揺れとスロー中の寄りを足す。
    /// 注視点はキャラに貼り付けず、横は追従・縦は弱めにする。
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CameraRigSystem : SystemBase
    {
        static readonly Vector3 Offset = new(0f, 5.4f, -10.5f);
        const float LookHeight = 1.1f;
        const float FollowSharpness = 6f;
        const float NormalFov = 48f;
        const float SlowFov = 41f;
        const float ShakeDecay = 9f;

        Vector3 _focus;
        bool _initialized;
        Unity.Mathematics.Random _random = new(0x5EED);

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerTag>();
            RequireForUpdate<CameraShake>();
        }

        protected override void OnUpdate()
        {
            var root = PresentationRoot.Current;
            var cam = root != null ? root.Camera : Camera.main;
            if (cam == null) return;

            float dt = UnityEngine.Time.unscaledDeltaTime;
            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            float3 p = SystemAPI.GetComponent<LocalTransform>(player).Position;
            var cut = SystemAPI.GetComponent<CutState>(player);

            Vector3 target = new(p.x, 0f, p.z * 0.35f);
            if (SystemAPI.TryGetSingleton<ArenaBounds>(out var arena))
                target.x = math.clamp(target.x, arena.Min.x + 6f, arena.Max.x - 6f);

            float k = 1f - math.exp(-FollowSharpness * dt);
            _focus = _initialized ? Vector3.Lerp(_focus, target, k) : target;
            _initialized = true;

            ref var shake = ref SystemAPI.GetSingletonRW<CameraShake>().ValueRW;
            float amp = shake.Amplitude;
            shake.Amplitude *= math.exp(-ShakeDecay * dt);
            Vector3 jitter = amp > 0.01f ? (Vector3)(_random.NextFloat3Direction() * amp * 0.25f) : Vector3.zero;

            bool slow = cut.Phase == CutPhase.Slow;
            float zoom = slow ? 0.88f : 1f;
            cam.transform.position = _focus + Offset * zoom + jitter;
            cam.transform.LookAt(_focus + Vector3.up * LookHeight + jitter * 0.5f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, slow ? SlowFov : NormalFov, 1f - math.exp(-8f * dt));
        }
    }
}
