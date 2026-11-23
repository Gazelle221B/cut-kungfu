using CutKungfu.Gameplay;
using Unity.Entities;
using Unity.Mathematics;

namespace CutKungfu.Presentation
{
    /// <summary>スロー中は画面をフィルム調(セピア・粒子・周辺減光)にする。Volume の重みだけを動かす。</summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class FilmLookSystem : SystemBase
    {
        const float Sharpness = 14f;

        protected override void OnCreate() => RequireForUpdate<PlayerTag>();

        protected override void OnUpdate()
        {
            var volume = PresentationRoot.Current != null ? PresentationRoot.Current.SlowVolume : null;
            if (volume == null) return;

            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            var cut = SystemAPI.GetComponent<CutState>(player);
            var session = SystemAPI.GetSingleton<SessionState>();
            float target = cut.Phase == CutPhase.Slow ? 1f : session.Phase == SessionPhase.NG ? 0.7f : 0f;
            float k = 1f - math.exp(-Sharpness * UnityEngine.Time.unscaledDeltaTime);
            volume.weight = math.lerp(volume.weight, target, k);
        }
    }
}
