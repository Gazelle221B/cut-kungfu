using System;
using UnityEngine.InputSystem;

namespace CutKungfu.Presentation
{
    /// <summary>
    /// 操作の割り当て。コードで定義しておくと、差分が読みやすく、資産の生成手順も要らない。
    /// 割り当てを変えるときはここだけを触る。
    /// </summary>
    public sealed class GameInputActions : IDisposable
    {
        readonly InputActionMap _map = new("CutKungfu");

        public InputAction Move { get; }
        public InputAction Primary { get; }
        public InputAction Secondary { get; }
        public InputAction Dodge { get; }
        public InputAction Cut { get; }
        public InputAction CycleBrick { get; }
        public InputAction AimStick { get; }
        public InputAction[] Bricks { get; } = new InputAction[4];
        public InputAction Retake { get; }
        public InputAction NextWave { get; }
        public InputAction ToggleCommit { get; }
        public InputAction ToggleStats { get; }

        public GameInputActions()
        {
            Move = _map.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Move.AddBinding("<Gamepad>/leftStick");

            // 通常時=攻撃 / スロー中=置く
            Primary = Button("Primary", "<Mouse>/leftButton", "<Keyboard>/j", "<Gamepad>/buttonWest");
            // 通常時=捌き / スロー中=取り消し
            Secondary = Button("Secondary", "<Mouse>/rightButton", "<Keyboard>/k", "<Gamepad>/rightShoulder");
            Dodge = Button("Dodge", "<Keyboard>/leftShift", "<Keyboard>/l", "<Gamepad>/buttonSouth");
            // 止める / 切る
            Cut = Button("Cut", "<Keyboard>/space", "<Gamepad>/buttonNorth", "<Gamepad>/leftTrigger");

            CycleBrick = _map.AddAction("CycleBrick", InputActionType.Value);
            // ホイールは InputBridgeSystem が直接読む(1ノッチの量が環境で違うため)
            CycleBrick.AddCompositeBinding("1DAxis").With("Negative", "<Gamepad>/dpad/left").With("Positive", "<Gamepad>/dpad/right");
            CycleBrick.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/q").With("Positive", "<Keyboard>/e");

            AimStick = _map.AddAction("AimStick", InputActionType.Value);
            AimStick.AddBinding("<Gamepad>/rightStick");

            for (int i = 0; i < Bricks.Length; i++)
                Bricks[i] = Button($"Brick{i + 1}", $"<Keyboard>/{i + 1}", $"<Keyboard>/numpad{i + 1}");

            Retake = Button("Retake", "<Keyboard>/r", "<Gamepad>/select");
            NextWave = Button("NextWave", "<Keyboard>/n");
            ToggleCommit = Button("ToggleCommit", "<Keyboard>/f1");
            ToggleStats = Button("ToggleStats", "<Keyboard>/f2", "<Gamepad>/start");
        }

        InputAction Button(string name, params string[] paths)
        {
            var a = _map.AddAction(name, InputActionType.Button);
            foreach (var p in paths) a.AddBinding(p);
            return a;
        }

        public void Enable() => _map.Enable();

        public void Dispose()
        {
            _map.Disable();
            _map.Dispose();
        }
    }
}
