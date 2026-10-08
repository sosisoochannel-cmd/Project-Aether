using System;
using Aether.Core.States;
using Aether.Data.Config;
using Aether.Gameplay.Controls;
using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>
    /// The player's high-level movement states.
    /// </summary>
    /// <remarks>
    /// Deliberately a short list. Idle and running are <b>not</b> separate states: nothing in gameplay
    /// behaves differently between standing still and moving at full speed, so splitting them would
    /// duplicate the same movement code twice and create a transition to keep in sync. Presentation
    /// derives "idle versus running" from horizontal speed, which is also how it blends between the
    /// two animations.
    /// </remarks>
    public enum PlayerStateId
    {
        /// <summary>Standing on solid ground.</summary>
        Grounded = 0,

        /// <summary>In the air, whether rising or falling. Gravity shaping handles the difference.</summary>
        Airborne = 1,

        /// <summary>Executing a dodge. Gravity is suppressed and velocity is locked for predictability.</summary>
        Dodge = 2,

        /// <summary>Recovering from a hit; control is withheld for a short, fixed window.</summary>
        Hurt = 3,

        /// <summary>Dead. Input is ignored until the respawn flow resets the controller.</summary>
        Dead = 4,
    }

    /// <summary>
    /// Owns the player's state machine and the shared traversal timers (coyote time, jump buffering,
    /// dodge cooldown).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Timing is expressed as absolute timestamps, not countdowns.</b> Coyote time, jump buffering
    /// and cooldowns are all "did this happen within N seconds of now", compared against
    /// <c>Time.time</c>. A countdown decremented in <c>FixedUpdate</c> measurably loses time when the
    /// render frame rate and the physics rate disagree — which is exactly the situation on a phone
    /// that is thermally throttling — and that loss shows up as jumps that occasionally do not come
    /// out. Timestamps make responsiveness independent of frame rate.
    /// </para>
    /// <para>
    /// Movement runs in <c>FixedUpdate</c> so it is stable regardless of render rate; attack timing
    /// runs in <c>Update</c> through <see cref="PlayerCombat"/> so it stays visually sharp.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(PlayerMotor))]
    [RequireComponent(typeof(PlayerHealth))]
    [RequireComponent(typeof(PlayerCombat))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Tooltip("Movement and combat feel. Required.")]
        [SerializeField]
        private PlayerTuningData _tuning;

        private PlayerMotor _motor;
        private IGameplayInput _input;
        private PlayerHealth _health;
        private PlayerCombat _combat;
        private StateMachine<PlayerController, PlayerStateId> _machine;

        private float _lastGroundedAt = float.NegativeInfinity;
        private bool _wasGroundedLastPhysicsStep;
        private float _jumpBufferedAt = float.NegativeInfinity;
        private float _nextDodgeAt = float.NegativeInfinity;
        private float _dodgeEndsAt;
        private float _hurtEndsAt;
        private int _airJumpsUsed;

        /// <summary>Raised after the player enters a new state, with (previous, current).</summary>
        public event Action<PlayerStateId, PlayerStateId> StateChanged;

        /// <summary>Raised when the player leaves the ground, after coyote time has expired.</summary>
        public event Action LeftGround;

        /// <summary>Raised when the player lands after being airborne.</summary>
        public event Action Landed;

        /// <summary>Current state. Read-only; transitions happen through gameplay code only.</summary>
        public PlayerStateId State => _machine.Current;

        /// <summary>Movement and combat tuning asset.</summary>
        public PlayerTuningData Tuning => _tuning;

        /// <summary>The physics body wrapper.</summary>
        public PlayerMotor Motor => _motor;

        /// <summary>Attack execution and hit resolution.</summary>
        public PlayerCombat Combat => _combat;

        /// <summary>Health and invulnerability.</summary>
        public PlayerHealth Health => _health;

        /// <summary>When false, no input is read and the player keeps its current velocity.</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>Legal facing direction, +1 or -1.</summary>
        public int FacingSign => _motor.FacingSign;

        /// <summary>The input stream this controller reads. Null when nothing is wired up.</summary>
        public IGameplayInput Input => _input;

        /// <summary>
        /// Supplies the tuning asset. Objects built at runtime have no inspector to assign it in, so
        /// the player factory calls this before the first frame.
        /// </summary>
        public void Configure(PlayerTuningData tuning)
        {
            _tuning = tuning;
        }

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _health = GetComponent<PlayerHealth>();
            _combat = GetComponent<PlayerCombat>();
            // The router merges every input source on this object; falling back to the device
            // reader keeps a hand-built player working without one.
            _input = GetComponent<GameplayInputRouter>();
            if (_input == null) _input = GetComponent<PlayerInputReader>();

            if (_tuning == null)
            {
                Debug.LogError(
                    $"{nameof(PlayerController)} on '{name}' has no PlayerTuningData assigned. " +
                    "The player will not move. Assign a tuning asset in the inspector.",
                    this);
            }

            if (_input == null)
            {
                Debug.LogWarning(
                    $"{nameof(PlayerController)} on '{name}' has no {nameof(PlayerInputReader)}. " +
                    "The player will be inert until one is added.",
                    this);
            }

            BuildStateMachine();

            _health.Died += OnDied;
        }

        private void OnDestroy()
        {
            if (_health != null) _health.Died -= OnDied;
        }

        private void OnEnable()
        {
            _machine.Start(this, PlayerStateId.Grounded);
        }

        private void OnDisable()
        {
            _machine.Stop(this);
        }

        private void Update()
        {
            // Attack timing lives on the render clock so wind-up frames land where the animator
            // expects them even when the physics rate and frame rate disagree.
            _combat.Tick(Time.deltaTime);

            if (_input == null || !InputEnabled || !_health.IsAlive) return;

            if (_input.JumpPressed)
            {
                _jumpBufferedAt = Time.time;
                _input.ConsumeJump();
            }

            if (_input.DodgePressed)
            {
                _input.ConsumeDodge();
                TryStartDodge();
            }

            if (_input.AttackPressed)
            {
                _input.ConsumeAttack();
                _combat.TryStartAttack();
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            _motor.RefreshGrounded();

            bool groundedNow = _motor.IsGrounded;
            if (groundedNow)
            {
                _lastGroundedAt = Time.time;
                _airJumpsUsed = 0;

                if (!_wasGroundedLastPhysicsStep && _machine.Current == PlayerStateId.Airborne)
                    Landed?.Invoke();
            }
            else if (_wasGroundedLastPhysicsStep)
            {
                // Fire exactly once when the player actually leaves a grounded surface. The old
                // timestamp-based check could invoke this every physics frame during coyote time.
                LeftGround?.Invoke();
            }

            _wasGroundedLastPhysicsStep = groundedNow;

            _machine.FixedTick(this, dt);
        }

        private void BuildStateMachine()
        {
            _machine = new StateMachine<PlayerController, PlayerStateId>(5);
            _machine.Changed += (from, to) => StateChanged?.Invoke(from, to);

            _machine.Add(PlayerStateId.Grounded, new GroundedState());
            _machine.Add(PlayerStateId.Airborne, new AirborneState());
            _machine.Add(PlayerStateId.Dodge, new DodgeState());
            _machine.Add(PlayerStateId.Hurt, new HurtState());
            _machine.Add(PlayerStateId.Dead, new DeadState());
        }

        // ---- Shared conditions --------------------------------------------------------------

        /// <summary>True while the jump input arrived recently enough to still be honoured.</summary>
        private bool HasBufferedJump =>
            _tuning != null && Time.time - _jumpBufferedAt <= _tuning.JumpBufferTime;

        /// <summary>True while the player has recently been on solid ground.</summary>
        private bool InCoyoteWindow =>
            _tuning != null && Time.time - _lastGroundedAt <= _tuning.CoyoteTime;

        private bool CanAirJump =>
            _tuning != null && _airJumpsUsed < _tuning.AirJumpCount;

        private bool DodgeReady =>
            _tuning != null && Time.time >= _nextDodgeAt;

        private bool CanDodge =>
            _tuning != null && DodgeReady && (_motor.IsGrounded || _tuning.AllowAirDodge);

        /// <summary>Consumes the buffered jump so one press produces exactly one jump.</summary>
        private void ConsumeBufferedJump()
        {
            _jumpBufferedAt = float.NegativeInfinity;
        }

        private void TryStartDodge()
        {
            if (_tuning == null) return;
            if (!_health.IsAlive) return;
            if (!CanDodge) return;
            if (_machine.Current == PlayerStateId.Dodge) return;

            _dodgeEndsAt = Time.time + _tuning.DodgeDuration;
            _nextDodgeAt = _dodgeEndsAt + _tuning.DodgeCooldown;
            _machine.ChangeState(this, PlayerStateId.Dodge);
        }

        /// <summary>Puts the player into hit recovery. Called by <see cref="PlayerHealth"/>.</summary>
        internal void EnterHurt(float duration)
        {
            if (!_health.IsAlive) return;

            _hurtEndsAt = Time.time + duration;
            _machine.ChangeState(this, PlayerStateId.Hurt);
        }

        private void OnDied()
        {
            _machine.ChangeState(this, PlayerStateId.Dead);
        }

        /// <summary>
        /// Returns the player to a controllable state at <paramref name="position"/>.
        /// Used by the respawn flow; leaves no residual velocity or buffered input behind.
        /// </summary>
        public void ResetForRespawn(Vector2 position)
        {
            _motor.TeleportTo(position);
            _lastGroundedAt = float.NegativeInfinity;
            _wasGroundedLastPhysicsStep = false;
            _jumpBufferedAt = float.NegativeInfinity;
            _nextDodgeAt = float.NegativeInfinity;
            _airJumpsUsed = 0;

            _combat.Cancel();
            _health.ResetToFull();
            InputEnabled = true;

            _machine.ChangeState(this, PlayerStateId.Airborne);
        }

        /// <summary>Applies an external impulse, used by knockback and by Rootbind momentum.</summary>
        public void ApplyImpulse(Vector2 velocity)
        {
            _motor.SetVelocity(velocity.x, velocity.y);
        }

        /// <summary>Suspends control, for example while a dialogue or a region transition is on screen.</summary>
        public void EnterCutscene(Vector2 velocity)
        {
            InputEnabled = false;
            _motor.SetVelocity(velocity.x, velocity.y);
        }

        // ---- States -------------------------------------------------------------------------

        /// <summary>Applies horizontal input and gravity in the standard way.</summary>
        private void ApplyStandardMotion()
        {
            float inputX = _input != null && InputEnabled ? _input.Move.x : 0f;
            float speedMultiplier = _combat != null ? _combat.MovementSpeedMultiplier : 1f;

            _motor.ApplyHorizontal(inputX, _tuning, Time.fixedDeltaTime, speedMultiplier);
            _motor.ApplyGravity(_tuning, Time.fixedDeltaTime);
        }

        private sealed class GroundedState : IState<PlayerController>
        {
            public void Enter(PlayerController c) { }

            public void Tick(PlayerController c, float dt) { }

            public void FixedTick(PlayerController c, float dt)
            {
                c.ApplyStandardMotion();

                if (c.CanDodge && c._input != null && c._input.DodgePressed) return;

                if (c.TryJump()) return;

                if (c._motor.IsGrounded) return;

                if (c.InCoyoteWindow) return;

                c._machine.ChangeState(c, PlayerStateId.Airborne);
            }

            public void Exit(PlayerController c) { }
        }

        private sealed class AirborneState : IState<PlayerController>
        {
            public void Enter(PlayerController c) { }

            public void Tick(PlayerController c, float dt) { }

            public void FixedTick(PlayerController c, float dt)
            {
                c.ApplyStandardMotion();
                c.HandleJumpCut();

                if (c.TryJump()) return;

                if (c._motor.IsGrounded)
                {
                    c._machine.ChangeState(c, PlayerStateId.Grounded);
                }
            }

            public void Exit(PlayerController c) { }
        }

        private sealed class DodgeState : IState<PlayerController>
        {
            private float _direction;

            public void Enter(PlayerController c)
            {
                float inputX = c._input != null && c.InputEnabled ? c._input.Move.x : 0f;

                // Dodge along the stick if the player is steering, otherwise along facing. Reading
                // input at entry rather than continuously is what makes the dodge a committed move.
                _direction = Mathf.Abs(inputX) > 0.01f
                    ? (inputX > 0f ? 1f : -1f)
                    : c._motor.FacingSign;

                c._motor.FacingSign = _direction > 0f ? 1 : -1;
                c._motor.SetVelocity(_direction * c._tuning.DodgeSpeed, 0f);
                c._health.BeginInvulnerability(c._tuning.DodgeInvulnerability);
                c._combat.Cancel();
            }

            public void Tick(PlayerController c, float dt) { }

            public void FixedTick(PlayerController c, float dt)
            {
                // Velocity is held flat for the duration; gravity and player input are both ignored so
                // the dash distance is identical every time the player performs it.
                c._motor.SetVelocity(_direction * c._tuning.DodgeSpeed, 0f);

                if (Time.time < c._dodgeEndsAt) return;

                c._machine.ChangeState(
                    c,
                    c._motor.IsGrounded ? PlayerStateId.Grounded : PlayerStateId.Airborne);
            }

            public void Exit(PlayerController c)
            {
                // Bleed off most of the dash speed so the dodge does not launch the player.
                c._motor.VelocityX *= 0.35f;
            }
        }

        private sealed class HurtState : IState<PlayerController>
        {
            public void Enter(PlayerController c)
            {
                c._combat.Cancel();
            }

            public void Tick(PlayerController c, float dt) { }

            public void FixedTick(PlayerController c, float dt)
            {
                // Knockback velocity is preserved; only gravity is applied, so being hit reads as a
                // push the player has to recover from rather than a teleport.
                c._motor.ApplyGravity(c._tuning, dt);

                if (Time.time < c._hurtEndsAt) return;

                c._machine.ChangeState(
                    c,
                    c._motor.IsGrounded ? PlayerStateId.Grounded : PlayerStateId.Airborne);
            }

            public void Exit(PlayerController c) { }
        }

        private sealed class DeadState : IState<PlayerController>
        {
            public void Enter(PlayerController c)
            {
                c.InputEnabled = false;
                c._combat.Cancel();
                c._motor.Stop();
            }

            public void Tick(PlayerController c, float dt) { }

            public void FixedTick(PlayerController c, float dt)
            {
                // Gravity keeps acting so the body settles onto the ground instead of hovering.
                c._motor.ApplyGravity(c._tuning, dt);
            }

            public void Exit(PlayerController c) { }
        }

        // ---- Jump ---------------------------------------------------------------------------

        /// <summary>
        /// Attempts a jump from the current state. Returns true when a jump was started.
        /// </summary>
        /// <remarks>
        /// Written once and shared by the grounded and airborne states, so ground jumps, coyote jumps
        /// and air jumps can never diverge in behaviour.
        /// </remarks>
        private bool TryJump()
        {
            if (_tuning == null) return false;
            if (!HasBufferedJump) return false;

            bool grounded = _motor.IsGrounded || InCoyoteWindow;

            if (grounded)
            {
                _motor.StartJump(_tuning);
                ConsumeBufferedJump();
                _machine.ChangeState(this, PlayerStateId.Airborne);
                return true;
            }

            if (!CanAirJump) return false;

            _airJumpsUsed++;
            _motor.StartJump(_tuning);
            ConsumeBufferedJump();
            _machine.ChangeState(this, PlayerStateId.Airborne);
            return true;
        }

        /// <summary>Cuts the jump short when the button is released before the apex.</summary>
        private void HandleJumpCut()
        {
            if (_input == null) return;
            if (_input.JumpHeld) return;
            if (!_motor.IsRising) return;

            _motor.CutJump(_tuning);
        }
    }
}
