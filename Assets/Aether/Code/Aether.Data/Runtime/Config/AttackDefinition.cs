using UnityEngine;

namespace Aether.Data.Config
{
    /// <summary>
    /// One attack: its hitbox geometry, its damage, and the three timing windows that give the
    /// player something to read and learn (startup, active, recovery).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attacks are data, not code. A melee combo is expressed as a linked list of these assets
    /// via <see cref="NextInCombo"/>, so adding a third or fourth hit is an authoring task rather
    /// than a code change.
    /// </para>
    /// <para>
    /// The hitbox is a box cast in front of the attacker rather than a child collider, so an attack
    /// asset fully describes itself and no prefab wiring is required. This keeps enemies and the
    /// player on the same code path.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "Attack",
        menuName = "Aether/Config/Attack",
        order = 10)]
    public sealed class AttackDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used in logs and analytics. Must be unique across the project.")]
        [SerializeField]
        private string _id = "attack.unnamed";

        [Header("Damage")]
        [Tooltip("Damage applied on connect.")]
        [SerializeField, Min(0f)]
        private float _damage = 1f;

        [Tooltip("Horizontal knockback impulse applied to the target, away from the attacker.")]
        [SerializeField, Min(0f)]
        private float _knockbackSpeed = 6f;

        [Tooltip("Upward impulse applied to the target. Small values read as a satisfying 'pop'.")]
        [SerializeField, Min(0f)]
        private float _liftSpeed = 1.5f;

        [Header("Hitbox")]
        [Tooltip("Hitbox centre, relative to the attacker's origin. X is mirrored by facing direction.")]
        [SerializeField]
        private Vector2 _hitboxOffset = new Vector2(0.9f, 0.1f);

        [Tooltip("Hitbox size in world units.")]
        [SerializeField]
        private Vector2 _hitboxSize = new Vector2(1.2f, 0.9f);

        [Header("Timing (seconds)")]
        [Tooltip("Wind-up before the hitbox becomes active. This is the player's dodge cue.")]
        [SerializeField, Min(0f)]
        private float _startup = 0.09f;

        [Tooltip("How long the hitbox stays active. Short windows reward spacing.")]
        [SerializeField, Min(0.01f)]
        private float _active = 0.07f;

        [Tooltip("Lockout after the hitbox ends, before the player regains full control.")]
        [SerializeField, Min(0f)]
        private float _recovery = 0.17f;

        [Tooltip("Forward impulse applied at the start of the attack, in world units per second.")]
        [SerializeField, Min(0f)]
        private float _lungeSpeed = 3.5f;

        [Header("Combo")]
        [Tooltip("Attack to chain into when the player presses attack again during the combo window. Leave empty to end the chain.")]
        [SerializeField]
        private AttackDefinition _nextInCombo;

        [Tooltip("Window, measured from the start of recovery, during which a chained attack is accepted.")]
        [SerializeField, Min(0f)]
        private float _comboInputWindow = 0.22f;

        // ---- Identity -----------------------------------------------------------------------

        /// <summary>Stable identifier for this attack.</summary>
        public string Id => _id;

        // ---- Damage -------------------------------------------------------------------------

        /// <summary>Damage applied on connect.</summary>
        public float Damage => _damage;

        /// <summary>Horizontal knockback applied to the target.</summary>
        public float KnockbackSpeed => _knockbackSpeed;

        /// <summary>Upward impulse applied to the target.</summary>
        public float LiftSpeed => _liftSpeed;

        // ---- Hitbox -------------------------------------------------------------------------

        /// <summary>Hitbox centre relative to the attacker's origin, unmirrored.</summary>
        public Vector2 HitboxOffset => _hitboxOffset;

        /// <summary>Hitbox size in world units.</summary>
        public Vector2 HitboxSize => _hitboxSize;

        /// <summary>
        /// Hitbox centre mirrored for the given facing, so one asset serves both directions.
        /// </summary>
        public Vector2 HitboxOffsetFor(int facingSign)
        {
            return new Vector2(_hitboxOffset.x * (facingSign >= 0 ? 1f : -1f), _hitboxOffset.y);
        }

        // ---- Timing -------------------------------------------------------------------------

        /// <summary>Wind-up duration.</summary>
        public float Startup => _startup;

        /// <summary>Hitbox active duration.</summary>
        public float Active => _active;

        /// <summary>Lockout after the hitbox ends.</summary>
        public float Recovery => _recovery;

        /// <summary>Total duration of the attack.</summary>
        public float TotalDuration => _startup + _active + _recovery;

        /// <summary>Forward impulse applied at the start of the attack.</summary>
        public float LungeSpeed => _lungeSpeed;

        // ---- Combo --------------------------------------------------------------------------

        /// <summary>Next attack in the chain, or null when the chain ends here.</summary>
        public AttackDefinition NextInCombo => _nextInCombo;

        /// <summary>Window during recovery in which a chained attack is accepted.</summary>
        public float ComboInputWindow => _comboInputWindow;

        /// <summary>True when this attack continues into another.</summary>
        public bool HasFollowUp => _nextInCombo != null;
    }
}
