using CutKungfu.Gameplay;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = CutKungfu.Gameplay.PlayerInput;

namespace CutKungfu.Presentation
{
    /// <summary>
    /// Input System の状態を、デバイスに依存しない <see cref="PlayerInput"/> に写す。
    /// ゲームロジックはこのシングルトンしか見ないので、入力の差し替え(リプレイ・自動テスト)が効く。
    /// 押下は前のフレームのまだ使われていない分に重ねる(ゲームの更新が回らなかったフレームの押下を落とさない)。
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(GameplayBootstrapSystem))]
    public partial class InputBridgeSystem : SystemBase
    {
        GameInputActions _actions;
        float _scrollAccumulator;

        /// <summary>HUD の統計表示の切り替え(表示だけの状態なので ECS には載せない)。</summary>
        public bool StatsToggledThisFrame { get; private set; }

        protected override void OnCreate()
        {
            _actions = new GameInputActions();
            _actions.Enable();
            RequireForUpdate<PlayerInput>();
        }

        protected override void OnDestroy() => _actions?.Dispose();

        protected override void OnUpdate()
        {
            var input = new PlayerInput
            {
                Move = _actions.Move.ReadValue<Vector2>(),
                Primary = _actions.Primary.WasPressedThisFrame(),
                Secondary = _actions.Secondary.WasPressedThisFrame(),
                Dodge = _actions.Dodge.WasPressedThisFrame(),
                Cut = _actions.Cut.WasPressedThisFrame(),
                SelectBrick = -1,
                CycleBrick = ReadCycle(),
                AimStick = _actions.AimStick.ReadValue<Vector2>(),
                Retake = _actions.Retake.WasPressedThisFrame(),
                NextWave = _actions.NextWave.WasPressedThisFrame(),
                ToggleCommitCondition = _actions.ToggleCommit.WasPressedThisFrame(),
            };
            for (int i = 0; i < _actions.Bricks.Length; i++)
                if (_actions.Bricks[i].WasPressedThisFrame()) input.SelectBrick = i;

            var cam = PresentationRoot.Current != null ? PresentationRoot.Current.Camera : Camera.main;
            if (cam != null && Mouse.current != null)
            {
                var ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                input.AimRayValid = true;
                input.AimRayOrigin = ray.origin;
                input.AimRayDirection = ray.direction;
            }

            StatsToggledThisFrame = _actions.ToggleStats.WasPressedThisFrame();
            SystemAPI.SetSingleton(PlayerInput.Accumulate(SystemAPI.GetSingleton<PlayerInput>(), input));
        }

        /// <summary>
        /// ブリックの順送り。ホイールは環境で1ノッチの量が違う(正規化なら ±1、旧来の値なら ±120)ので、
        /// 1ノッチの大きさを設定から決めて溜め、1ノッチごとに1段だけ進める。
        /// </summary>
        int ReadCycle()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float notch = InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms ? 1f : 120f;
                _scrollAccumulator += mouse.scroll.y.ReadValue();
                if (math.abs(_scrollAccumulator) >= notch * 0.5f)
                {
                    int step = _scrollAccumulator > 0 ? -1 : 1;
                    _scrollAccumulator = 0f;
                    return step;
                }
            }
            if (!_actions.CycleBrick.WasPressedThisFrame()) return 0;
            return _actions.CycleBrick.ReadValue<float>() > 0 ? 1 : -1;
        }
    }
}
