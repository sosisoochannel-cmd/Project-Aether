using UnityEngine;
using UnityEngine.InputSystem;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// Menu navigation from a keyboard, a gamepad or a remote — as events, never as polling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pointing and pressing are the EventSystem's job.</b> The scene's <c>EventSystem</c> carries
    /// an <c>InputSystemUIInputModule</c>, which is what the project's Input System Only setting
    /// requires: taps, clicks and the Submit event that activates a row all come through it, with no
    /// code here involved. This component adds only what the module deliberately does not do —
    /// spatial navigation, because that needs to know the shape of a screen.
    /// </para>
    /// <para>
    /// <b>Two actions, created in code.</b> Move and cancel are bound here rather than authored in an
    /// action asset: an asset would be a new file whose bindings nothing in this repository can
    /// review, and the bindings that matter are three lines long. Arrows, WASD, the d-pad and the
    /// left stick move; Escape and the east button go back.
    /// </para>
    /// <para>
    /// <b>The stick is latched.</b> An analogue axis reports a change every frame it is held, which
    /// would make the selection fly through a list at sixty rows a second. A step is taken when the
    /// axis crosses the outer threshold and nothing more happens until it falls back inside the
    /// inner one — a hysteresis band, which is the standard remedy and costs one <c>bool</c> and no
    /// timer.
    /// </para>
    /// </remarks>
    public sealed class MenuInput : MonoBehaviour
    {
        /// <summary>How far a stick must move before it steps the selection.</summary>
        private const float StepThreshold = 0.55f;

        /// <summary>How far it must return before it can step again.</summary>
        private const float ReleaseThreshold = 0.35f;

        private InputAction _move;
        private InputAction _cancel;
        private bool _latched;

        /// <summary>Raised for each navigation step. Subscribed by the menu host.</summary>
        public System.Action<MenuNav.Move> Moved;

        /// <summary>Raised for a back request: the east button, Escape, or the Android back gesture.</summary>
        public System.Action Cancelled;

        /// <summary>
        /// When true, movement and cancel are ignored. The transition system raises this while a
        /// fade is running, so a press during a load cannot reach a hidden screen.
        /// </summary>
        public bool Locked { get; set; }

        /// <summary>Builds the action set on an object under the menu.</summary>
        public static MenuInput Create(Transform parent)
        {
            var host = new GameObject("Menu Input");
            host.transform.SetParent(parent, false);

            var input = host.AddComponent<MenuInput>();
            input.Build();
            return input;
        }

        private void Build()
        {
            _move = new InputAction("Menu Move", InputActionType.Value);

            // Three ways to point, all of them into the same 2D vector: the arrow keys, WASD, and
            // the gamepad's d-pad or left stick. A composite per device keeps the bindings honest —
            // a keyboard and a stick are not the same kind of input and are not merged into one
            // composite by the Input System.
            _move.AddCompositeBinding("2DVector")
                 .With("Up", "<Keyboard>/upArrow")
                 .With("Down", "<Keyboard>/downArrow")
                 .With("Left", "<Keyboard>/leftArrow")
                 .With("Right", "<Keyboard>/rightArrow");

            _move.AddCompositeBinding("2DVector")
                 .With("Up", "<Keyboard>/w")
                 .With("Down", "<Keyboard>/s")
                 .With("Left", "<Keyboard>/a")
                 .With("Right", "<Keyboard>/d");

            _move.AddBinding("<Gamepad>/dpad");
            _move.AddBinding("<Gamepad>/leftStick");

            _cancel = new InputAction("Menu Cancel", InputActionType.Button);
            _cancel.AddBinding("<Keyboard>/escape");
            _cancel.AddBinding("<Gamepad>/buttonEast");

            _move.performed += OnMove;
            _cancel.performed += OnCancel;

            _move.Enable();
            _cancel.Enable();
        }

        private void OnMove(InputAction.CallbackContext context)
        {
            if (Locked) return;

            Vector2 axis = context.ReadValue<Vector2>();

            // Vertical wins a tie. A stick pushed diagonally should move the selection once, not
            // twice, and a list is read downward: that is the tie's resolution.
            bool vertical = Mathf.Abs(axis.y) >= Mathf.Abs(axis.x);
            float primary = vertical ? axis.y : axis.x;

            if (Mathf.Abs(primary) < ReleaseThreshold)
            {
                _latched = false;
                return;
            }

            if (_latched || Mathf.Abs(primary) < StepThreshold) return;

            _latched = true;
            System.Action<MenuNav.Move> handler = Moved;
            if (handler == null) return;

            if (vertical) handler(primary > 0f ? MenuNav.Move.Up : MenuNav.Move.Down);
            else handler(primary > 0f ? MenuNav.Move.Right : MenuNav.Move.Left);
        }

        private void OnCancel(InputAction.CallbackContext context)
        {
            if (Locked) return;

            System.Action handler = Cancelled;
            if (handler != null) handler();
        }

        private void OnDestroy()
        {
            if (_move != null)
            {
                _move.performed -= OnMove;
                _move.Disable();
                _move.Dispose();
                _move = null;
            }

            if (_cancel != null)
            {
                _cancel.performed -= OnCancel;
                _cancel.Disable();
                _cancel.Dispose();
                _cancel = null;
            }
        }
    }
}
