using System.Collections.Generic;
using Aether.Core.Combat;
using Aether.Data.Config;
using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>Phase of the attack timeline.</summary>
    public enum AttackPhase
    {
        /// <summary>No attack in progress.</summary>
        None = 0,

        /// <summary>Wind-up. The hitbox is not yet live; this is the opponent's warning.</summary>
        Startup = 1,

        /// <summary>The hitbox is live.</summary>
        Active = 2,

        /// <summary>Lockout. Chained attacks may be accepted here.</summary>
        Recovery = 3,
    }

    /// <summary>
    /// Executes the player's melee attacks: timeline, hit queries, damage delivery and combo chaining.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attacks are driven from <c>Update</c> rather than <c>FixedUpdate</c>. Wind-up frames are a
    /// promise made to the player about when an attack will land, and the render clock is the clock
    /// the player is watching. The hit query itself is a physics overlap, which is perfectly valid
    /// outside the fixed step and does not need the physics clock to have ticked.
    /// </para>
    /// <para>
    /// Each target can be hit <b>at most once per attack</b>. Without that rule a wide hitbox staying
    /// active for several frames would deal its damage repeatedly, which is both unfair and the
    /// classic source of "why did I lose half my health to one swing".
    /// </para>
    /// <para>
    /// The hitbox is a box query positioned relative to the player's collider rather than a child
    /// trigger, so an attack asset fully describes itself and the level builder does not have to
    /// create and wire colliders per attack.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [Header("Attacks")]
        [Tooltip("First attack of the chain. Follow-up attacks are referenced from this asset.")]
        [SerializeField]
        private AttackDefinition _firstAttack;

        [Tooltip("Layers that attacks can damage. Should exclude the player's own layer.")]
        [SerializeField]
        private LayerMask _targetLayers;

        private readonly List<Collider2D> _overlapBuffer = new List<Collider2D>(8);
        private readonly HashSet<Collider2D> _targetsHitThisAttack = new HashSet<Collider2D>();

        private PlayerMotor _motor;
        private PlayerController _controller;
        private ContactFilter2D _filter;
        private bool _filterBuilt;

        private AttackDefinition _currentAttack;
        private AttackPhase _phase = AttackPhase.None;
        private float _phaseElapsed;
        private bool _comboQueued;

        /// <summary>True while an attack is in any phase.</summary>
        public bool IsAttacking => _phase != AttackPhase.None;

        /// <summary>Current attack phase.</summary>
        public AttackPhase Phase => _phase;

        /// <summary>The attack currently executing, or null.</summary>
        public AttackDefinition CurrentAttack => _currentAttack;

        /// <summary>
        /// Scale applied to the player's movement while attacking. Wind-up and active frames root the
        /// player so attacks are committal; recovery returns control so the player is not left helpless.
        /// </summary>
        public float MovementSpeedMultiplier
        {
            get
            {
                if (_controller == null || _controller.Tuning == null) return 1f;

                switch (_phase)
                {
                    case AttackPhase.Startup:
                    case AttackPhase.Active:
                        return _controller.Tuning.AttackMoveSpeedMultiplier;
                    default:
                        return 1f;
                }
            }
        }

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _controller = GetComponent<PlayerController>();
        }

        private void Start()
        {
            BuildFilterIfNeeded();
        }

        /// <summary>
        /// Advances the attack timeline. Called once per frame by <see cref="PlayerController"/>.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_phase == AttackPhase.None) return;
            if (_currentAttack == null)
            {
                Cancel();
                return;
            }

            _phaseElapsed += deltaTime;

            switch (_phase)
            {
                case AttackPhase.Startup:
                    if (_phaseElapsed >= _currentAttack.Startup) EnterPhase(AttackPhase.Active);
                    break;

                case AttackPhase.Active:
                    ResolveHits();
                    if (_phaseElapsed >= _currentAttack.Active) EnterPhase(AttackPhase.Recovery);
                    break;

                case AttackPhase.Recovery:
                    // The chain fires the moment the queued input can be honoured rather than at the
                    // end of recovery, which is what makes a combo feel like it has no dead frames.
                    if (_comboQueued && _currentAttack.HasFollowUp && _phaseElapsed <= _currentAttack.ComboInputWindow)
                    {
                        BeginAttack(_currentAttack.NextInCombo);
                        break;
                    }

                    if (_phaseElapsed >= _currentAttack.Recovery) Cancel();
                    break;
            }
        }

        /// <summary>
        /// Attempts to start or continue an attack. Returns true when an attack actually began.
        /// </summary>
        /// <remarks>
        /// Presses during wind-up and active frames are queued rather than discarded, so a player
        /// mashing for the second hit during the first hit's animation gets it.
        /// </remarks>
        public bool TryStartAttack()
        {
            if (_controller != null && !_controller.Health.IsAlive) return false;
            if (_firstAttack == null) return false;

            if (_phase == AttackPhase.None)
            {
                BeginAttack(_firstAttack);
                return true;
            }

            if (_phase == AttackPhase.Recovery) return false;

            _comboQueued = true;
            return false;
        }

        /// <summary>Aborts any attack in progress and clears buffered input.</summary>
        public void Cancel()
        {
            _phase = AttackPhase.None;
            _phaseElapsed = 0f;
            _comboQueued = false;
            _currentAttack = null;
            _targetsHitThisAttack.Clear();
        }

        private void BeginAttack(AttackDefinition attack)
        {
            if (attack == null)
            {
                Cancel();
                return;
            }

            _currentAttack = attack;
            _phase = AttackPhase.Startup;
            _phaseElapsed = 0f;
            _comboQueued = false;
            _targetsHitThisAttack.Clear();

            // The lunge is a grounded move. In the air the player keeps their momentum, which keeps
            // air control predictable and stops attacks from being used as a free dash.
            if (_motor.IsGrounded && attack.LungeSpeed > 0f)
            {
                _motor.VelocityX = _motor.FacingSign * attack.LungeSpeed;
            }
        }

        private void EnterPhase(AttackPhase phase)
        {
            _phase = phase;
            _phaseElapsed = 0f;
        }

        private void BuildFilterIfNeeded()
        {
            if (_filterBuilt) return;

            _filter = Physics2DQuery.CreateSolidFilter(_targetLayers);
            _filterBuilt = true;
        }

        /// <summary>Centre of the hitbox in world space for the current attack and facing.</summary>
        public Vector2 GetHitboxCenter()
        {
            if (_currentAttack == null) return _motor.Position;

            // Anchored to the collider rather than the transform so a prefab whose pivot sits at the
            // feet, the centre or an offset all produce the same hitbox.
            Vector2 origin = _motor.Collider != null
                ? (Vector2)_motor.Collider.bounds.center
                : _motor.Position;

            return origin + _currentAttack.HitboxOffsetFor(_motor.FacingSign);
        }

        private void ResolveHits()
        {
            BuildFilterIfNeeded();

            int count = Physics2DQuery.OverlapBox(
                GetHitboxCenter(),
                _currentAttack.HitboxSize,
                0f,
                _filter,
                _overlapBuffer);

            if (count == 0) return;

            Vector2 source = _motor.Collider != null ? (Vector2)_motor.Collider.bounds.center : _motor.Position;
            var damage = new DamageInfo(
                _currentAttack.Damage,
                source,
                _currentAttack.KnockbackSpeed,
                _currentAttack.LiftSpeed,
                DamageKind.Melee,
                gameObject);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _overlapBuffer[i];
                if (hit == null) continue;

                // Never damage the attacker's own body.
                if (_motor.Body != null && hit.attachedRigidbody == _motor.Body) continue;
                if (!_targetsHitThisAttack.Add(hit)) continue;

                DamageResolver.TryDamage(hit, damage);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_currentAttack == null) return;

            Gizmos.color = _phase == AttackPhase.Active
                ? new Color(1f, 0.2f, 0.2f, 0.6f)
                : new Color(1f, 1f, 0.2f, 0.35f);

            Gizmos.DrawWireCube(GetHitboxCenter(), _currentAttack.HitboxSize);
        }
#endif
    }
}
