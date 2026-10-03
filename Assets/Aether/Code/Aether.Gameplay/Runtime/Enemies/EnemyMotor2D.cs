using Aether.Core.Combat;
using Aether.Data.Config;
using UnityEngine;

namespace Aether.Gameplay.Enemies
{
    /// <summary>
    /// Physics body for a ground-based enemy: grounding, gravity, and edge/wall awareness.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> shared with <c>PlayerMotor</c>. The two have genuinely different jobs:
    /// the player needs jump shaping, coyote time, variable gravity and a dodge override, while an
    /// enemy needs ledge and wall probing so it can turn around. Forcing them into one base class
    /// today would produce a class full of capabilities each side mostly ignores — the classic
    /// premature-unification mistake. They already share everything that is genuinely common:
    /// <see cref="Physics2DQuery"/>, the state machine, and the attack timeline.
    /// </para>
    /// <para>
    /// If a third ground character appears, unifying this with <c>PlayerMotor</c> becomes worth
    /// doing. Two is not yet a pattern.
    /// </para>
    /// <para>
    /// Grounding is probe-based rather than callback-based, so an enemy that is standing still still
    /// knows it is grounded and one that is spawned mid-air settles correctly.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public sealed class EnemyMotor2D : MonoBehaviour
    {
        [Header("Layers")]
        [Tooltip("Layers treated as solid ground and walls.")]
        [SerializeField]
        private LayerMask _solidLayers;

        [Header("Gravity")]
        [Tooltip("Downward acceleration applied while airborne.")]
        [SerializeField, Min(0f)]
        private float _gravity = 42f;

        [Tooltip("Terminal fall speed, so a high drop cannot outrun collision detection.")]
        [SerializeField, Min(1f)]
        private float _maxFallSpeed = 26f;

        [Header("Motion")]
        [Tooltip("How quickly the enemy reaches its target speed, in units/second².")]
        [SerializeField, Min(1f)]
        private float _acceleration = 28f;

        [Tooltip("How quickly the enemy stops when it has no target speed.")]
        [SerializeField, Min(1f)]
        private float _deceleration = 40f;

        private Rigidbody2D _body;
        private Collider2D _collider;
        private EnemyDefinition _definition;

        /// <summary>The physics body.</summary>
        public Rigidbody2D Body => _body;

        /// <summary>The enemy's collider, used for probe origins and framing.</summary>
        public Collider2D Collider => _collider;

        /// <summary>True when solid ground is directly beneath the enemy.</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>True when the enemy has walked off an edge and is falling.</summary>
        public bool IsFalling => !IsGrounded && _body.linearVelocity.y < -0.01f;

        /// <summary>+1 facing right, -1 facing left.</summary>
        public int FacingSign { get; set; } = 1;

        /// <summary>World position of the body.</summary>
        public Vector2 Position => _body.position;

        /// <summary>Horizontal speed, signed.</summary>
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

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _collider = GetComponent<Collider2D>();

            // Enemies never use engine gravity: a single gravity value cannot express a heavy,
            // fast-settling fall, and mixer-side gravity would fight the AI's own movement.
            _body.gravityScale = 0f;
            _body.freezeRotation = true;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        /// <summary>
        /// Supplies the archetype definition, whose values size the probes. Call before the first
        /// <c>FixedUpdate</c>; the spawner does this when it instantiates the enemy.
        /// </summary>
        public void Initialize(EnemyDefinition definition)
        {
            _definition = definition;
        }

        /// <summary>
        /// Forces the enemy onto the ground beneath it.
        /// </summary>
        /// <remarks>
        /// Called once at spawn. Level data places enemies at an authored height that may not match
        /// the final geometry, and an enemy that spends its first second falling through the floor
        /// of a patrol route looks broken. This is a one-time settle, not a physics hack.
        /// </remarks>
        public void SnapToGround()
        {
            Bounds bounds = _collider.bounds;
            Vector2 origin = new Vector2(bounds.center.x, bounds.max.y);

            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, 8f, _solidLayers);
            if (hit.collider == null) return;

            float halfHeight = bounds.extents.y;
            Vector2 target = new Vector2(bounds.center.x, hit.point.y + halfHeight);

            _body.position = target;
            transform.position = target;
            _body.linearVelocity = Vector2.zero;
        }

        /// <summary>Recomputes grounding. Call once per physics step, before AI logic.</summary>
        public void RefreshGrounded()
        {
            float offset = _definition != null ? _definition.GroundProbeOffset : 0.5f;

            Bounds bounds = _collider.bounds;
            Vector2 probeCenter = new Vector2(bounds.center.x, bounds.min.y - offset + 0.06f);
            var probeSize = new Vector2(Mathf.Max(0.1f, bounds.size.x * 0.7f), 0.14f);

            IsGrounded = Physics2DQuery.OverlapsBox(probeCenter, probeSize, 0f, _solidLayers);
        }

        /// <summary>Applies gravity for this step, clamped to the terminal speed.</summary>
        public void ApplyGravity(float deltaTime)
        {
            if (IsGrounded && _body.linearVelocity.y <= 0f) return;

            Vector2 v = _body.linearVelocity;
            v.y -= _gravity * deltaTime;
            if (v.y < -_maxFallSpeed) v.y = -_maxFallSpeed;
            _body.linearVelocity = v;
        }

        /// <summary>
        /// Drives horizontal speed towards <paramref name="targetSpeed"/>.
        /// </summary>
        /// <param name="targetSpeed">
        /// Signed target speed in units/second. Zero decelerates to a stop.
        /// </param>
        public void Move(float targetSpeed, float deltaTime)
        {
            float rate = Mathf.Abs(targetSpeed) > 0.01f ? _acceleration : _deceleration;

            Vector2 v = _body.linearVelocity;
            v.x = Mathf.MoveTowards(v.x, targetSpeed, rate * deltaTime);
            _body.linearVelocity = v;

            if (Mathf.Abs(targetSpeed) > 0.01f) FacingSign = targetSpeed > 0f ? 1 : -1;
        }

        /// <summary>Halts horizontal motion immediately. Used on death and stagger.</summary>
        public void StopHorizontal()
        {
            VelocityX = 0f;
        }

        /// <summary>
        /// True when the ground runs out ahead of the enemy, meaning it is standing near a ledge.
        /// </summary>
        /// <remarks>
        /// Probes from the far edge of the body rather than its centre, so a wide crawler does not
        /// begin its turn while half of it is already over the drop.
        /// </remarks>
        public bool IsLedgeAhead()
        {
            float reach = _definition != null ? _definition.LedgeProbeDistance : 0.55f;
            Bounds bounds = _collider.bounds;

            Vector2 origin = new Vector2(
                bounds.center.x + (FacingSign * bounds.extents.x),
                bounds.min.y + 0.05f);

            Vector2 probe = origin + new Vector2(FacingSign * reach, 0f);
            return !Physics2DQuery.OverlapsBox(probe, new Vector2(0.08f, 0.16f), 0f, _solidLayers);
        }

        /// <summary>True when a solid surface blocks the enemy's path ahead.</summary>
        public bool IsWallAhead()
        {
            float reach = _definition != null ? _definition.WallProbeDistance : 0.45f;
            Bounds bounds = _collider.bounds;

            Vector2 origin = new Vector2(
                bounds.center.x,
                bounds.min.y + (bounds.size.y * 0.5f));

            Vector2 probe = origin + new Vector2(FacingSign * (bounds.extents.x + reach), 0f);
            var size = new Vector2(0.1f, Mathf.Max(0.1f, bounds.size.y * 0.6f));

            return Physics2DQuery.OverlapsBox(probe, size, 0f, _solidLayers);
        }

        /// <summary>
        /// True when solid ground exists at an offset from the enemy, used to decide whether a
        /// chased target is actually reachable across a gap.
        /// </summary>
        public bool HasGroundAt(Vector2 position, float radius = 0.4f)
        {
            return Physics2DQuery.OverlapsBox(position, new Vector2(radius, 0.2f), 0f, _solidLayers);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Collider2D col = _collider != null ? _collider : GetComponent<Collider2D>();
            if (col == null) return;

            Bounds bounds = col.bounds;
            float offset = _definition != null ? _definition.GroundProbeOffset : 0.5f;

            Gizmos.color = Application.isPlaying && IsGrounded ? Color.green : Color.yellow;
            Gizmos.DrawWireCube(
                new Vector2(bounds.center.x, bounds.min.y - offset + 0.06f),
                new Vector2(Mathf.Max(0.1f, bounds.size.x * 0.7f), 0.14f));

            float reach = _definition != null ? _definition.LedgeProbeDistance : 0.55f;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(
                new Vector2(bounds.center.x + (FacingSign * (bounds.extents.x + reach)), bounds.min.y + 0.05f),
                new Vector2(0.08f, 0.16f));
        }
#endif
    }
}
