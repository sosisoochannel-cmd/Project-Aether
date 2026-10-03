using UnityEngine;

namespace Aether.Data.Config
{
    /// <summary>
    /// Every tunable that defines how the player moves: ground and air handling, jump shaping,
    /// and the dodge. One asset per player form; a future ability may swap in a different asset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Jump feel is expressed in <b>designer units</b> — "how high, and how long to the top" —
    /// rather than raw gravity and impulse numbers. Gravity and initial velocity are derived in
    /// <see cref="Gravity"/> and <see cref="JumpVelocity"/>, so changing jump height does not
    /// silently change the character's weight.
    /// </para>
    /// <para>
    /// These are the constants a designer will tune constantly. Nothing in gameplay code should
    /// hard-code a movement number; it belongs here.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "PlayerTuning",
        menuName = "Aether/Config/Player Tuning",
        order = 0)]
    public sealed class PlayerTuningData : ScriptableObject
    {
        [Header("Body")]
        [Tooltip("Width of the player's collision box, in world units. " +
                 "Level geometry is designed against this value, so changing it invalidates every " +
                 "gap and ledge in the game — run the traversal solver after adjusting it.")]
        [SerializeField, Min(0.1f)]
        private float _bodyWidth = 0.7f;

        [Tooltip("Height of the player's collision box, in world units. Governs headroom under " +
                 "overhangs and clearance in low corridors, and is also read by the traversal solver.")]
        [SerializeField, Min(0.1f)]
        private float _bodyHeight = 1.4f;

        [Header("Horizontal movement")]
        [Tooltip("Ground speed cap in world units per second.")]
        [SerializeField, Min(0.1f)]
        private float _runSpeed = 7f;

        [Tooltip("How quickly the player reaches run speed on the ground, in units/second².")]
        [SerializeField, Min(1f)]
        private float _groundAcceleration = 90f;

        [Tooltip("How quickly the player stops on the ground when there is no input.")]
        [SerializeField, Min(1f)]
        private float _groundDeceleration = 110f;

        [Tooltip("Acceleration multiplier applied while airborne. Below 1 gives floatier air control.")]
        [SerializeField, Range(0.1f, 2f)]
        private float _airControlMultiplier = 0.65f;

        [Tooltip("Deceleration multiplier applied while airborne. Keep close to air control for predictable arcs.")]
        [SerializeField, Range(0.1f, 2f)]
        private float _airDecelerationMultiplier = 0.5f;

        [Header("Jump")]
        [Tooltip("Peak height of a full jump, in world units.")]
        [SerializeField, Min(0.1f)]
        private float _jumpHeight = 3.1f;

        [Tooltip("Seconds from leaving the ground to reaching the apex of the jump.")]
        [SerializeField, Min(0.05f)]
        private float _jumpTimeToApex = 0.36f;

        [Tooltip("Gravity multiplier while rising. Anything above 1 makes the ascent snappier.")]
        [SerializeField, Range(0.5f, 4f)]
        private float _riseGravityMultiplier = 1f;

        [Tooltip("Gravity multiplier while falling. Above 1 removes 'floaty' descent and improves landing feedback.")]
        [SerializeField, Range(0.5f, 5f)]
        private float _fallGravityMultiplier = 1.9f;

        [Tooltip("Terminal downward speed. Prevents tunnelling through thin floors on low frame rates.")]
        [SerializeField, Min(1f)]
        private float _maxFallSpeed = 26f;

        [Tooltip("Multiplier applied to upward velocity when the jump button is released early. 0.5 = half height.")]
        [SerializeField, Range(0.1f, 1f)]
        private float _jumpCutMultiplier = 0.45f;

        [Tooltip("Grace period after walking off a ledge during which a jump still counts as grounded.")]
        [SerializeField, Range(0f, 0.4f)]
        private float _coyoteTime = 0.12f;

        [Tooltip("Window before landing in which a jump press is remembered and fires on touchdown.")]
        [SerializeField, Range(0f, 0.4f)]
        private float _jumpBufferTime = 0.14f;

        [Tooltip("Air jumps available. Zero for the opening region; abilities may raise this later.")]
        [SerializeField, Range(0, 3)]
        private int _airJumpCount = 0;

        [Header("Dodge")]
        [Tooltip("Speed of the dodge dash, in world units per second. Should read clearly faster than a run.")]
        [SerializeField, Min(1f)]
        private float _dodgeSpeed = 16f;

        [Tooltip("Total duration of the dodge in seconds.")]
        [SerializeField, Min(0.05f)]
        private float _dodgeDuration = 0.22f;

        [Tooltip("Seconds before another dodge may start.")]
        [SerializeField, Min(0f)]
        private float _dodgeCooldown = 0.42f;

        [Tooltip("Seconds of invulnerability from the start of the dodge. Keeping this below the dodge duration makes timing matter.")]
        [SerializeField, Min(0f)]
        private float _dodgeInvulnerability = 0.18f;

        [Tooltip("When false the player must be grounded to dodge. Rootbind-era upgrades may lift this.")]
        [SerializeField]
        private bool _allowAirDodge = false;

        [Header("Combat")]
        [Tooltip("Fraction of run speed retained while attacking on the ground. 0 roots the player in place.")]
        [SerializeField, Range(0f, 1f)]
        private float _attackMoveSpeedMultiplier = 0.25f;

        [Tooltip("Minimum seconds between attacks when the action is not part of a defined combo.")]
        [SerializeField, Min(0f)]
        private float _attackRecoveryFloor = 0.08f;

        // ---- Body ---------------------------------------------------------------------------

        /// <summary>
        /// Width of the player's collision box. Read by the runtime player factory and by the
        /// traversal solver, so the level is always measured against the real collider.
        /// </summary>
        public float BodyWidth => _bodyWidth;

        /// <summary>Height of the player's collision box.</summary>
        public float BodyHeight => _bodyHeight;

        // ---- Movement -----------------------------------------------------------------------

        /// <summary>Ground speed cap in world units per second.</summary>
        public float RunSpeed => _runSpeed;

        /// <summary>Ground acceleration in units/second².</summary>
        public float GroundAcceleration => _groundAcceleration;

        /// <summary>Ground deceleration in units/second².</summary>
        public float GroundDeceleration => _groundDeceleration;

        /// <summary>Airborne acceleration.</summary>
        public float AirAcceleration => _groundAcceleration * _airControlMultiplier;

        /// <summary>Airborne deceleration.</summary>
        public float AirDeceleration => _groundDeceleration * _airDecelerationMultiplier;

        // ---- Jump ---------------------------------------------------------------------------

        /// <summary>Peak jump height in world units.</summary>
        public float JumpHeight => _jumpHeight;

        /// <summary>
        /// Upward velocity applied at take-off, derived from <see cref="JumpHeight"/> and
        /// the time to apex via <c>v = 2h / t</c>.
        /// </summary>
        public float JumpVelocity => (2f * _jumpHeight) / Mathf.Max(0.05f, _jumpTimeToApex);

        /// <summary>
        /// Downward acceleration while rising, derived from height and time to apex via
        /// <c>g = 2h / t²</c>, then scaled by <see cref="_riseGravityMultiplier"/>.
        /// </summary>
        public float RiseGravity => ((2f * _jumpHeight) / (_jumpTimeToApex * _jumpTimeToApex)) * _riseGravityMultiplier;

        /// <summary>Downward acceleration while falling.</summary>
        public float FallGravity => RiseGravity * _fallGravityMultiplier;

        /// <summary>Terminal fall speed.</summary>
        public float MaxFallSpeed => _maxFallSpeed;

        /// <summary>Multiplier applied to upward velocity when the jump is released early.</summary>
        public float JumpCutMultiplier => _jumpCutMultiplier;

        /// <summary>Ledge forgiveness window in seconds.</summary>
        public float CoyoteTime => _coyoteTime;

        /// <summary>Pre-landing jump input buffer in seconds.</summary>
        public float JumpBufferTime => _jumpBufferTime;

        /// <summary>Available air jumps.</summary>
        public int AirJumpCount => _airJumpCount;

        // ---- Dodge --------------------------------------------------------------------------

        /// <summary>Dodge dash speed in world units per second.</summary>
        public float DodgeSpeed => _dodgeSpeed;

        /// <summary>Dodge duration in seconds.</summary>
        public float DodgeDuration => _dodgeDuration;

        /// <summary>Seconds between dodges.</summary>
        public float DodgeCooldown => _dodgeCooldown;

        /// <summary>Invulnerability window measured from the start of the dodge.</summary>
        public float DodgeInvulnerability => _dodgeInvulnerability;

        /// <summary>Whether the dodge may be used in mid-air.</summary>
        public bool AllowAirDodge => _allowAirDodge;

        // ---- Combat -------------------------------------------------------------------------

        /// <summary>Movement scale applied while executing an attack.</summary>
        public float AttackMoveSpeedMultiplier => _attackMoveSpeedMultiplier;

        /// <summary>Fallback spacing between attacks, used when an attack defines no follow-up.</summary>
        public float AttackRecoveryFloor => _attackRecoveryFloor;

#if UNITY_EDITOR
        /// <summary>
        /// Keeps designer input inside sane bounds while editing. Runtime code must never rely on
        /// these clamps, because in a build the values arrive exactly as serialised.
        /// </summary>
        private void OnValidate()
        {
            // Invulnerability longer than the dash would make the dodge strictly better than
            // any other defensive option, so keep it inside the dash itself.
            _dodgeInvulnerability = Mathf.Min(_dodgeInvulnerability, _dodgeDuration);
            _groundDeceleration = Mathf.Max(_groundDeceleration, 1f);
            _jumpTimeToApex = Mathf.Max(_jumpTimeToApex, 0.05f);
        }
#endif
    }
}
