using UnityEngine;

namespace Aether.Data.Config
{
    /// <summary>
    /// AI behaviours available to enemies. Each value is one small, self-contained state machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Behaviour is chosen per archetype rather than per instance. An enemy that randomly picks a
    /// behaviour, or that changes its rules mid-fight, cannot be learned — and "the player can read
    /// the enemy" is a hard requirement of this project, not a preference.
    /// </para>
    /// <para>
    /// New behaviours are appended. Values are not persisted yet, but treat them as append-only
    /// anyway so that level data referencing them stays valid.
    /// </para>
    /// </remarks>
    public enum EnemyBehaviour
    {
        /// <summary>
        /// Holds a post and strikes when the player enters range. Teaches <b>timing and dodge</b>:
        /// the wind-up is long and unmistakable, the reach is honest, and stepping out of range
        /// ends the threat.
        /// </summary>
        PatrolStriker = 0,

        /// <summary>
        /// Stays on its surface, turns at edges, and punishes careless ground. Teaches
        /// <b>positioning</b>: the safe answer is a platform, not a longer jump.
        /// </summary>
        SurfaceCrawler = 1,

        /// <summary>
        /// Perches until the player passes beneath, then drops. Teaches <b>environmental
        /// awareness</b>: the cue is overhead and avoidable by not walking under it.
        /// </summary>
        AmbushDropper = 2,
    }

    /// <summary>
    /// Every tunable and behaviour switch for one enemy archetype.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One asset per archetype. Level data references the archetype by <see cref="TypeId"/>; it
    /// never contains a tuning number. A designer can therefore retune an entire region's enemies
    /// from one asset, and the same archetype can appear at different difficulties by instantiating
    /// a variant asset rather than by adding a code branch.
    /// </para>
    /// <para>
    /// <b>Every timing value here is a telegraph the player has to read.</b> When changing one,
    /// ask whether the player still has enough warning to react. If not, the change is a
    /// difficulty increase disguised as a tuning tweak.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "Enemy",
        menuName = "Aether/Config/Enemy",
        order = 20)]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id referenced by level data. Must be unique across the project.")]
        [SerializeField]
        private string _typeId = "enemy.unnamed";

        [Tooltip("Display name, for editor tooling and debug overlays. Never shown to the player.")]
        [SerializeField]
        private string _displayName = "Unnamed";

        [Tooltip("Which AI behaviour drives this archetype.")]
        [SerializeField]
        private EnemyBehaviour _behaviour = EnemyBehaviour.PatrolStriker;

        [Header("Survivability")]
        [Tooltip("Hit points. Keep early archetypes low so combat stays about reading, not grinding.")]
        [SerializeField, Min(1f)]
        private float _maxHealth = 3f;

        [Header("Perception")]
        [Tooltip("Horizontal distance at which the player is noticed.")]
        [SerializeField, Min(0f)]
        private float _detectionRange = 5.5f;

        [Tooltip("Player must be within this vertical distance to be noticed. Keeps enemies from reacting through floors.")]
        [SerializeField, Min(0f)]
        private float _detectionHeight = 1.6f;

        [Tooltip("Distance beyond which the enemy gives up and returns to its post.")]
        [SerializeField, Min(0f)]
        private float _disengageRange = 9f;

        [Header("Movement")]
        [Tooltip("Patrol or crawl speed, in world units per second.")]
        [SerializeField, Min(0f)]
        private float _moveSpeed = 2.1f;

        [Tooltip("Speed of the committed attack lunge. Higher values make spacing matter more.")]
        [SerializeField, Min(0f)]
        private float _attackLungeSpeed = 3.6f;

        [Tooltip("Seconds of minimal control before the attack starts, so the enemy cannot turn mid-swing.")]
        [SerializeField, Range(0f, 0.5f)]
        private float _attackCommitTime = 0.12f;

        [Tooltip("Empties the enemy takes between an attack ending and the next decision. This is the player's turn.")]
        [SerializeField, Range(0f, 3f)]
        private float _recoveryPause = 0.65f;

        [Header("Attack")]
        [Tooltip("Attack used by this archetype. Hitbox, damage and timing all live there.")]
        [SerializeField]
        private AttackDefinition _attack;

        [Tooltip("Horizontal distance at which the attack is worth starting. Should be slightly beyond the hitbox reach so the lunge closes the gap.")]
        [SerializeField, Min(0f)]
        private float _attackRange = 1.5f;

        [Tooltip("Extra delay between the decision to attack and the attack's wind-up beginning.")]
        [SerializeField, Range(0f, 1.5f)]
        private float _attackWindupDelay = 0.2f;

        [Header("Patrol")]
        [Tooltip("Distance patrolled either side of the post when the player is not visible. Zero means the enemy holds its post.")]
        [SerializeField, Min(0f)]
        private float _patrolDistance = 3f;

        [Tooltip("Seconds to stand still at each end of the patrol. Motion catches the eye; stillness hides it.")]
        [SerializeField, Range(0f, 5f)]
        private float _patrolPause = 1.1f;

        [Header("Damage handling")]
        [Tooltip("Seconds of stagger after taking a hit.")]
        [SerializeField, Range(0f, 1.5f)]
        private float _staggerDuration = 0.2f;

        [Tooltip("Seconds of knockback, during which the enemy has no control.")]
        [SerializeField, Range(0f, 1f)]
        private float _knockbackDuration = 0.16f;

        [Tooltip("Seconds of invulnerability after being hit. Above zero makes its stagger readable instead of a stun-lock.")]
        [SerializeField, Range(0f, 1f)]
        private float _invulnerabilityAfterHit = 0.1f;

        [Header("Perception edges")]
        [Tooltip("Distance from a ledge at which a crawler turns around, measured from its centre.")]
        [SerializeField, Min(0f)]
        private float _ledgeProbeDistance = 0.55f;

        [Tooltip("Distance ahead at which a walker treats a wall as impassable.")]
        [SerializeField, Min(0f)]
        private float _wallProbeDistance = 0.45f;

        [Tooltip("Vertical offset of the ground probe from the enemy's centre.")]
        [SerializeField, Min(0f)]
        private float _groundProbeOffset = 0.5f;

        [Header("Ambush")]
        [Tooltip("Delay between the perch being triggered and the drop beginning. This is the player's cue.")]
        [SerializeField, Range(0f, 1.5f)]
        private float _dropDelay = 0.45f;

        [Header("Feedback hooks")]
        [Tooltip("Placeholder cue id fired when the enemy notices the player. Consumed by the audio layer once it exists.")]
        [SerializeField]
        private string _alertCueId = "enemy.alert";

        [Tooltip("Placeholder cue id fired when an attack begins its wind-up.")]
        [SerializeField]
        private string _attackCueId = "enemy.attack";

        [Tooltip("Placeholder cue id fired when the enemy dies.")]
        [SerializeField]
        private string _deathCueId = "enemy.death";

        // ---- Identity -----------------------------------------------------------------------

        /// <summary>Stable id referenced by level data.</summary>
        public string TypeId => _typeId;

        /// <summary>Name for editor tooling.</summary>
        public string DisplayName => _displayName;

        /// <summary>Behaviour driving this archetype.</summary>
        public EnemyBehaviour Behaviour => _behaviour;

        // ---- Survivability ------------------------------------------------------------------

        /// <summary>Hit points.</summary>
        public float MaxHealth => _maxHealth;

        // ---- Perception ---------------------------------------------------------------------

        /// <summary>Horizontal notice distance.</summary>
        public float DetectionRange => _detectionRange;

        /// <summary>Vertical notice tolerance.</summary>
        public float DetectionHeight => _detectionHeight;

        /// <summary>Distance at which the enemy returns to its post.</summary>
        public float DisengageRange => _disengageRange;

        // ---- Movement -----------------------------------------------------------------------

        /// <summary>Patrol or crawl speed.</summary>
        public float MoveSpeed => _moveSpeed;

        /// <summary>Lunge speed during an attack.</summary>
        public float AttackLungeSpeed => _attackLungeSpeed;

        /// <summary>Control lockout at the start of an attack.</summary>
        public float AttackCommitTime => _attackCommitTime;

        /// <summary>Pause after an attack, during which the enemy cannot act.</summary>
        public float RecoveryPause => _recoveryPause;

        // ---- Attack -------------------------------------------------------------------------

        /// <summary>The archetype's attack, or null for a purely passive enemy.</summary>
        public AttackDefinition Attack => _attack;

        /// <summary>Distance at which an attack is worth starting.</summary>
        public float AttackRange => _attackRange;

        /// <summary>Delay between deciding to attack and the wind-up starting.</summary>
        public float AttackWindupDelay => _attackWindupDelay;

        /// <summary>True when this archetype can attack.</summary>
        public bool CanAttack => _attack != null && _attackRange > 0f;

        // ---- Patrol -------------------------------------------------------------------------

        /// <summary>Patrol extent either side of the post.</summary>
        public float PatrolDistance => _patrolDistance;

        /// <summary>Dwell time at each patrol end.</summary>
        public float PatrolPause => _patrolPause;

        /// <summary>True when this archetype patrols rather than holding its post.</summary>
        public bool Patrols => _patrolDistance > 0.05f;

        // ---- Damage handling ----------------------------------------------------------------

        /// <summary>Stagger duration after a hit.</summary>
        public float StaggerDuration => _staggerDuration;

        /// <summary>Knockback window after a hit.</summary>
        public float KnockbackDuration => _knockbackDuration;

        /// <summary>Invulnerability granted after a hit.</summary>
        public float InvulnerabilityAfterHit => _invulnerabilityAfterHit;

        // ---- Probe geometry -----------------------------------------------------------------

        /// <summary>Ledge probe distance for crawlers.</summary>
        public float LedgeProbeDistance => _ledgeProbeDistance;

        /// <summary>Wall probe distance.</summary>
        public float WallProbeDistance => _wallProbeDistance;

        /// <summary>Vertical offset of the ground probe.</summary>
        public float GroundProbeOffset => _groundProbeOffset;

        // ---- Ambush -------------------------------------------------------------------------

        /// <summary>Warning delay before an ambush drop.</summary>
        public float DropDelay => _dropDelay;

        // ---- Feedback hooks ----------------------------------------------------------------

        /// <summary>Placeholder cue id for "noticed the player".</summary>
        public string AlertCueId => _alertCueId;

        /// <summary>Placeholder cue id for "attack incoming".</summary>
        public string AttackCueId => _attackCueId;

        /// <summary>Placeholder cue id for death.</summary>
        public string DeathCueId => _deathCueId;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // A detection range longer than the disengage range would make the enemy oscillate
            // between chasing and giving up at the boundary.
            _disengageRange = Mathf.Max(_disengageRange, _detectionRange + 1f);

            // An attack the enemy can start from inside its own hitbox never shows a wind-up at a
            // readable distance, which removes the player's cue.
            if (_attack != null)
            {
                float reach = _attack.HitboxOffset.x + (_attack.HitboxSize.x * 0.5f);
                _attackRange = Mathf.Max(_attackRange, reach);
            }
        }
#endif
    }
}
