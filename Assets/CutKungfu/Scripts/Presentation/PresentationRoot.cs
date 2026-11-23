using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace CutKungfu.Presentation
{
    /// <summary>
    /// シーン上の見た目の部品(カメラ・HUD・スロー演出の Volume・効果音)を ECS の表示系システムへ渡す唯一の MonoBehaviour。
    /// ロジックは持たない。参照はインスペクタで明示的に配線する(暗黙の Find に頼らない)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PresentationRoot : MonoBehaviour
    {
        [SerializeField] Camera sceneCamera;
        [SerializeField] UIDocument hud;
        [Tooltip("スロー中に重みを上げるフィルム調の Volume")]
        [SerializeField] Volume slowVolume;
        [SerializeField] AudioSource sfxSource;

        public static PresentationRoot Current { get; private set; }

        public Camera Camera => sceneCamera != null ? sceneCamera : Camera.main;
        public UIDocument Hud => hud;
        public Volume SlowVolume => slowVolume;
        public AudioSource SfxSource => sfxSource;

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
        }
    }
}
