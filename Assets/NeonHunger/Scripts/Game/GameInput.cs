using NeonHunger.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = NeonHunger.Core.PlayerInput;

namespace NeonHunger
{
    public enum InputKind { Keyboard, Gamepad, Touch }

    /// <summary>
    /// Keyboard (WASD / arrows), gamepad and touch (swipe to turn, on-screen DASH button), built
    /// with the Input System. Holding a direction keeps re-buffering it, so turns land reliably.
    /// </summary>
    public class GameInput
    {
        public readonly InputActionAsset Asset;
        public readonly InputAction Move, Dash, Pause, Restart;
        public InputKind Last { get; private set; } = InputKind.Keyboard;

        // touch swipes
        Vector2 touchStart;
        bool touching;
        Dir swipe = Dir.None;
        public bool TouchDash;
        public string TouchDebug = "none";

        public GameInput()
        {
            Asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = Asset.AddActionMap("Play");
            Move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Move.AddBinding("<Gamepad>/dpad");
            Move.AddBinding("<Gamepad>/leftStick");
            Dash = map.AddAction("Dash", InputActionType.Button);
            Dash.AddBinding("<Keyboard>/space"); Dash.AddBinding("<Keyboard>/leftShift"); Dash.AddBinding("<Keyboard>/rightShift");
            Dash.AddBinding("<Gamepad>/buttonSouth"); Dash.AddBinding("<Gamepad>/rightTrigger");
            Pause = map.AddAction("Pause", InputActionType.Button);
            Pause.AddBinding("<Keyboard>/escape"); Pause.AddBinding("<Keyboard>/p"); Pause.AddBinding("<Gamepad>/start");
            Restart = map.AddAction("Restart", InputActionType.Button);
            Restart.AddBinding("<Keyboard>/r"); Restart.AddBinding("<Gamepad>/select");
            Asset.Enable();
        }

        /// <summary>The direction wanted this frame (held keys/stick, or the latest swipe).</summary>
        public PlayerInput Read()
        {
            var input = new PlayerInput();
            var v = Move.ReadValue<Vector2>();
            if (v.sqrMagnitude > 0.25f)
            {
                input.Want = Mathf.Abs(v.x) > Mathf.Abs(v.y) ? (v.x > 0 ? Dir.Right : Dir.Left) : (v.y > 0 ? Dir.Up : Dir.Down);
                Last = Move.activeControl != null && Move.activeControl.device is Gamepad ? InputKind.Gamepad : InputKind.Keyboard;
            }
            input.Dash = Dash.WasPressedThisFrame() || TouchDash;
            if (Dash.WasPressedThisFrame()) Last = Dash.activeControl != null && Dash.activeControl.device is Gamepad ? InputKind.Gamepad : InputKind.Keyboard;
            TouchDash = false;

            var ts = Touchscreen.current;
            if (ts != null)
            {
                var t = ts.primaryTouch;
                // measure from where the finger first landed, and also on release: a quick flick
                // can start, move and lift between two frames on a slow device
                if (t.press.wasPressedThisFrame) { touching = true; touchStart = t.startPosition.ReadValue(); Last = InputKind.Touch; }
                if (touching && (t.press.isPressed || t.press.wasReleasedThisFrame))
                {
                    var d = t.position.ReadValue() - touchStart;
                    float min = Mathf.Max(24f, Screen.dpi > 0 ? Screen.dpi * 0.12f : Screen.height * 0.03f);
                    TouchDebug = $"start={touchStart} pos={t.position.ReadValue()} d={d.magnitude:0} min={min:0} pressed={t.press.isPressed} released={t.press.wasReleasedThisFrame}";
                    if (d.magnitude >= min)
                    {
                        swipe = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? (d.x > 0 ? Dir.Right : Dir.Left) : (d.y > 0 ? Dir.Up : Dir.Down);
                        touchStart = t.position.ReadValue();   // allow chained swipes without lifting
                    }
                }
                if (t.press.wasReleasedThisFrame) touching = false;
            }
            // a swipe keeps steering until the next one, like holding a key
            if (input.Want == Dir.None && swipe != Dir.None && Last == InputKind.Touch) input.Want = swipe;
            return input;
        }

        public void ClearSwipe() => swipe = Dir.None;
    }
}
