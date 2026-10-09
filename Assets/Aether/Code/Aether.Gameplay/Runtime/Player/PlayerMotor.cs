using Aether.Core.Combat;
using Aether.Data.Config;
using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>
    /// The player's physics body: grounding, velocity, gravity shaping and jump impulses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The motor owns <b>how</b> the player moves; state machine states decide <b>when</b>. Keeping
    /// that split sharp is what lets new abilities (a wall cling, a grapple, Rootbind) reuse the same
    /// body handling instead of each re-implementing gravity.
    /// </para>
    /// <para>
    /// Gravity is applied by this component rather than by the physics engine
    /// (<c>Rigidbody2D.gravityScale</c> is forced to zero). Asymmetric rise/fall gravity and precise
    /// jump shaping are impossible with a single engine gravity value, and a platformer that cannot
    /// shape its arc does not feel good. The trade-off is that every code path which moves the player
    /// must go through this motor.
    /// </para>
    /// <para>
    /// The body is <b>dynamic</b>: collisions, slopes and one-way platforms are handled by the
    /// physics engine, while all velocity decisions stay in game code.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Grounding")]
        [Tooltip("Layers the player can stand on. Must not include the player's own layer.")]
        [SerializeField]
        private LayerMask _groundLayers;

        [Tooltip("Size of the box probed at the player's feet to detect ground.")]
        [SerializeField]
        private Vector2 _groundProbeSize = new Vector2(0.58f, 0.12f);

        [Tooltip("How far the probe is raised into the body, so a resting character still registers as grounded.")]
        [SerializeField, Range(0f, 0.2f)]
        private float _groundProbeInset = 0.03f;

        private Rigidbody2D _body;
        private Collider2D _collider;

        /// <summary>
        /// Supplies the layers that count as ground. Objects built at runtime have no inspector, and
        /// a grounding probe pointed at the wrong mask produces the worst kind of bug: a player who
        /// looks fine and is never grounded.
        /// </summary>
        public void ConfigureGrounding(LayerMask groundLayers)
        {
            _groundLayers = groundLayers;
        }

        /// <summary>The physics body. Exposed for systems that need to read position or mass.</summary>
        public Rigidbody2D Body => _body;

        /// <summary>The player's collider. Exposed for camera framing and damage bounds.</summary>
        public Collider2D Collider => _collider;

        /// <summary>True when a solid surface is directly under the player and they are not rising.</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>True while upward velocity is positive.</summary>
        public bool IsRising => _body.linearVelocity.y > 0.01f;

        /// <summary>+1 when facing right, -1 when facing left. Drives hitbox mirroring and sprite flipping.</summary>
        public int FacingSign { get; set; } = 1;

        /// <summary>World position of the body, as a shorthand for the common case.</summary>
        public Vector2 Position => _body.position;

        /// <summary>Current velocity. Setting this replaces the whole vector.</summary>
        public Vector2 Velocity
        {
            get => _body.linearVelocity;
            set => _body.linearVelocity = value;
        }

        /// <summary>Horizontal velocity component.</summary>
        public float VelocityX
        {
            get => _body.linearVelocity.x;
            set
            {
                Vector2 v = _body.linearVelocity;
                v.x = value;
                _body.linearVelocity = v;
            }
        }

        /// <summary>Vertical velocity component.</summary>
        public float VelocityY
        {
            get => _body.linearVelocity.y;
            set
            {
                Vector2 v = _body.linearVelocity;
                v.y = value;
                _body.linearVelocity = v;
            }
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _collider = GetComponent<Collider2D>();

            // These are invariants of manual velocity control, not tunables. Setting them here means
            // a mis-configured prefab cannot silently produce a player that falls at the wrong rate
            // or tunnels through a thin floor.
            _body.gravityScale = 0f;
            _body.freezeRotation = true;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;

            // At high fall speeds a discrete body can pass through a one-unit-thick platform.
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        /// <summary>
        /// Recomputes <see cref="IsGrounded"/>. Call once per physics step, before state logic runs,
        /// so every state in that step sees the same answer.
        /// </summary>
        public void RefreshGrounded()
        {
            Vector2 probeCenter = GetGroundProbeCenter();
            bool touching = Physics2DQuery.OverlapsBox(probeCenter, _groundProbeSize, 0f, _groundLayers);

            // A character on the way up must not re-attach to the ledge they just left, otherwise the
            // jump is cut short on the very first frame.
            IsGrounded = touching && _body.linearVelocity.y <= 0.01f;
        }

        /// <summary>World-space centre of the grounding probe. Used for debug drawing.</summary>
        public Vector2 GetGroundProbeCenter()
        {
            Bounds bounds = _collider.bounds;
            return new Vector2(
                bounds.center.x,
                bounds.min.y + (_groundProbeSize.y * 0.5f) - _groundProbeInset);
        }

        /// <summary>Size of the grounding probe, for debug drawing.</summary>
        public Vector2 GroundProbeSize => _groundProbeSize;

        /// <summary>
        /// Drives horizontal velocity towards <c>inputX * tuning.RunSpeed</c>.
        /// </summary>
        /// <param name="inputX">Raw input on [-1, 1].</param>
        /// <param name="tuning">Tuning asset supplying acceleration values.</param>
        /// <param name="deltaTime">Physics step length.</param>
        /// <param name="speedMultiplier">
        /// Scales the target speed. Attacks pass a value below 1 to root the player without
        /// introducing a second movement code path.
        /// </param>
        public void ApplyHorizontal(float inputX, PlayerTuningData tuning, float deltaTime, float speedMultiplier = 1f)
        {
            if (tuning == null) return;

            float targetSpeed = Mathf.Clamp(inputX, -1f, 1f) * tuning.RunSpeed * Mathf.Clamp01(speedMultiplier);
            bool hasInput = Mathf.Abs(inputX) > 0.01f;

            float acceleration = IsGrounded
                ? (hasInput ? tuning.GroundAcceleration : tuning.GroundDeceleration)
                : (hasInput ? tuning.AirAcceleration : tuning.AirDeceleration);

            Vector2 v = _body.linearVelocity;
            v.x = Mathf.MoveTowards(v.x, targetSpeed, acceleration * deltaTime);
            _body.linearVelocity = v;

            // Facing follows input rather than velocity, so it flips the moment the stick does and
            // an attack started during a turnaround points the way the player intended.
            if (hasInput) FacingSign = inputX >= 0f ? 1 : -1;
        }

        /// <summary>
        /// Applies asymmetric gravity for this step.
        /// </summary>
        /// <param name="tuning">Tuning asset supplying gravity values.</param>
        /// <param name="deltaTime">Physics step length.</param>
        /// <param name="suppressGravity">
        /// When true, no gravity is applied this step. Used by the dodge so its arc stays flat and
        /// predictable, which is what makes dodge distances learnable.
        /// </param>
        public void ApplyGravity(PlayerTuningData tuning, float deltaTime, bool suppressGravity = false)
        {
            if (tuning == null) return;
            if (suppressGravity && !IsGrounded) return;

            float gravity = _body.linearVelocity.y > 0f ? tuning.RiseGravity : tuning.FallGravity;

            Vector2 v = _body.linearVelocity;
            v.y -= gravity * deltaTime;

            if (v.y < -tuning.MaxFallSpeed) v.y = -tuning.MaxFallSpeed;
            _body.linearVelocity = v;
        }

        /// <summary>Applies the take-off impulse for a jump.</summary>
        public void StartJump(PlayerTuningData tuning)
        {
            if (tuning == null) return;

            Vector2 v = _body.linearVelocity;
            v.y = tuning.JumpVelocity;

            // Cancel the horizontal component that the run-up contributed, so a jump from a full
            // sprint is the same height as a standing jump. Players read this as consistency.
            _body.linearVelocity = v;
            IsGrounded = false;
        }

        /// <summary>
        /// Shortens an in-progress jump when the button is released, producing variable jump height.
        /// Does nothing once the player is already falling, so releasing late has no effect.
        /// </summary>
        public void CutJump(PlayerTuningData tuning)
        {
            if (tuning == null) return;
            if (_body.linearVelocity.y <= 0f) return;

            Vector2 v = _body.linearVelocity;
            v.y *= tuning.JumpCutMultiplier;
            _body.linearVelocity = v;
        }

        /// <summary>
        /// Overrides velocity for a dodge or knockback, ignoring gravity shaping for this step.
        /// </summary>
        public void SetVelocity(float x, float y)
        {
            _body.linearVelocity = new Vector2(x, y);
        }

        /// <summary>Immediately halts all movement. Used on death and on respawn.</summary>
        public void Stop()
        {
            _body.linearVelocity = Vector2.zero;
        }

        /// <summary>
        /// Teleports the body without leaving a physics-swept trace behind. Required for respawns and
        /// for Rootbind anchors, because assigning <c>transform.position</c> on a dynamic body fights
        /// the physics step and can drop the player through geometry.
        /// </summary>
        public void TeleportTo(Vector2 position)
        {
            _body.position = position;
            _body.linearVelocity = Vector2.zero;
            transform.position = position;
            IsGrounded = false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Collider2D col = _collider != null ? _collider : GetComponent<Collider2D>();
            if (col == null) return;

            Bounds bounds = col.bounds;
            Vector2 center = new Vector2(
                bounds.center.x,
                bounds.min.y + (_groundProbeSize.y * 0.5f) - _groundProbeInset);

            Gizmos.color = Application.isPlaying && IsGrounded ? Color.green : Color.yellow;
            Gizmos.DrawWireCube(center, _groundProbeSize);
        }
#endif
    }
}
