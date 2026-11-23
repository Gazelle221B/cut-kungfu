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
            SystemAPI.SetSingleton(input);
        }

        /// <summary>ホイールは1ノッチ=120 などデバイス差が大きいので、符号だけを1段ずつ取り出す。</summary>
        int ReadCycle()
        {
            float v = _actions.CycleBrick.ReadValue<float>();
            if (_actions.CycleBrick.activeControl?.device is Mouse)
            {
                _scrollAccumulator += v;
                if (math.abs(_scrollAccumulator) < 60f) return 0;
                int step = _scrollAccumulator > 0 ? -1 : 1;
                _scrollAccumulator = 0f;
                return step;
            }
            _scrollAccumulator = 0f;
            return _actions.CycleBrick.WasPressedThisFrame() ? (v > 0 ? 1 : -1) : 0;
        }
    }
}
