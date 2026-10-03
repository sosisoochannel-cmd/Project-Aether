using UnityEngine;
using UnityEngine.InputSystem;

namespace Aether.Gameplay.Player
{
    /// <summary>
    /// Translates raw device input into the small vocabulary the player controller understands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Actions are <b>declared in code</b> rather than in an <c>.inputactions</c> asset. The bindings a
    /// 2D action-platformer needs are fixed, known at authoring time, and small; keeping them in code
    /// means the input map is reviewable in a diff, cannot drift out of sync with the controller, and
    /// requires no asset wiring in an automated build. If rebindable controls become a requirement
    /// (a settings screen), this class is the single place that changes — the rest of gameplay only
    /// ever sees the properties below.
    /// </para>
    /// <para>
    /// <b>Edge flags are latched, not polled.</b> The controller reads <see cref="JumpPressed"/> and
    /// then calls <see cref="ConsumeJump"/>. Without that handshake a press observed in <c>Update</c>
    /// could be acted on by two consecutive physics steps, producing a double jump on a frame where
    /// the fixed step ran twice.
    /// </para>
    /// <para>
    /// Touch controls are a separate deliverable and are not implemented here. They will feed the same
    /// five properties, so the controller will not change when they land.
    /// </para>
    /// </remarks>
    public sealed class PlayerInputReader : MonoBehaviour
    {
        private InputAction _move;
        private InputAction _jump;
        private InputAction _attack;
        private InputAction _dodge;
        private InputAction _interact;

        private bool _jumpLatch;
        private bool _attackLatch;
        private bool _dodgeLatch;
        private bool _interactLatch;

        /// <summary>Movement axis, already normalised to at most unit length.</summary>
        public Vector2 Move { get; private set; }

        /// <summary>True while jump is held. Drives variable jump height.</summary>
        public bool JumpHeld { get; private set; }

        /// <summary>True on the first frame a jump press was seen and not yet consumed.</summary>
        public bool JumpPressed => _jumpLatch;

        /// <summary>True on the first frame an attack press was seen and not yet consumed.</summary>
        public bool AttackPressed => _attackLatch;

        /// <summary>True on the first frame a dodge press was seen and not yet consumed.</summary>
        public bool DodgePressed => _dodgeLatch;

        /// <summary>True on the first frame an interact press was seen and not yet consumed.</summary>
        public bool InteractPressed => _interactLatch;

        /// <summary>When false, all output is zeroed. Used by cutscenes, pause and death.</summary>
        public bool Enabled { get; set; } = true;

        private void Awake()
        {
            BuildActions();
        }

        private void OnEnable()
        {
            _move.Enable();
            _jump.Enable();
            _attack.Enable();
            _dodge.Enable();
            _interact.Enable();
        }

        private void OnDisable()
        {
            _move.Disable();
            _jump.Disable();
            _attack.Disable();
            _dodge.Disable();
            _interact.Disable();

            ClearLatches();
            Move = Vector2.zero;
            JumpHeld = false;
        }

        private void OnDestroy()
        {
            _move?.Dispose();
            _jump?.Dispose();
            _attack?.Dispose();
            _dodge?.Dispose();
            _interact?.Dispose();
        }

        private void Update()
        {
            if (!Enabled)
            {
                ClearLatches();
                Move = Vector2.zero;
                JumpHeld = false;
                return;
            }

            Move = _move.ReadValue<Vector2>();
            JumpHeld = _jump.IsPressed();

            // Latch the press so the next physics step cannot miss it, and so two physics steps
            // in one frame cannot both act on it.
            if (_jump.WasPressedThisFrame()) _jumpLatch = true;
            if (_attack.WasPressedThisFrame()) _attackLatch = true;
            if (_dodge.WasPressedThisFrame()) _dodgeLatch = true;
            if (_interact.WasPressedThisFrame()) _interactLatch = true;
        }

        /// <summary>Clears the jump press flag. Call once the jump has been acted on.</summary>
        public void ConsumeJump() => _jumpLatch = false;

        /// <summary>Clears the attack press flag.</summary>
        public void ConsumeAttack() => _attackLatch = false;

        /// <summary>Clears the dodge press flag.</summary>
        public void ConsumeDodge() => _dodgeLatch = false;

        /// <summary>Clears the interact press flag.</summary>
        public void ConsumeInteract() => _interactLatch = false;

        private void ClearLatches()
        {
            _jumpLatch = false;
            _attackLatch = false;
            _dodgeLatch = false;
            _interactLatch = false;
        }

        private void BuildActions()
        {
            _move = new InputAction("Move", InputActionType.Value);

            // Composite for discrete keys, plus the analogue sources. The stick and d-pad are added
            // as separate bindings rather than through a composite so they keep their analogue range.
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            _move.AddBinding("<Gamepad>/leftStick");
            _move.AddBinding("<Gamepad>/dpad");

            _jump = new InputAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space");
            _jump.AddBinding("<Keyboard>/j");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _attack = new InputAction("Attack", InputActionType.Button);
            _attack.AddBinding("<Keyboard>/k");
            _attack.AddBinding("<Mouse>/leftButton");
            _attack.AddBinding("<Gamepad>/buttonWest");

            _dodge = new InputAction("Dodge", InputActionType.Button);
            _dodge.AddBinding("<Keyboard>/l");
            _dodge.AddBinding("<Keyboard>/leftShift");
            _dodge.AddBinding("<Gamepad>/buttonEast");

            _interact = new InputAction("Interact", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonNorth");
        }
    }
}
