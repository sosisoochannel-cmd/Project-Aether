using System;
using Aether.Core.Combat;
using Aether.Core.Pooling;
using Aether.Data.Config;
using Aether.Gameplay.Sound;
using UnityEngine;

namespace Aether.Gameplay.Enemies
{
    /// <summary>
    /// An enemy's health, reaction to being hit, and death.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every reaction here is a <b>fixed, readable duration</b> — stagger, knockback, brief
    /// invulnerability. That combination is what makes a hit feel weighty while keeping fights
    /// legible, and it is also what prevents stun-locking an enemy into a corner, which looks like a
    /// bug and removes any need to read its behaviour.
    /// </para>
    /// <para>
    /// Invulnerability is an absolute timestamp, consistent with the rest of the project, so it
    /// cannot drift when the render rate and physics rate disagree.
    /// </para>
    /// <para>
    /// Implements <see cref="IPooledObject"/> so a recycled enemy never wakes up staggered, damaged
    /// or invulnerable from its previous life — the failure mode that makes pooled enemies remember
    /// how they died.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(EnemyMotor2D))]
    public sealed class EnemyHealth : MonoBehaviour, IDamageable, IPooledObject
    {
        [Tooltip("Archetype definition. Supplies health and reaction timings.")]
        [SerializeField]
        private EnemyDefinition _definition;

        private EnemyMotor2D _motor;
        private float _currentHealth;
        private float _invulnerableUntil = float.NegativeInfinity;

        /// <summary>Raised when a hit actually lands, after invulnerability was considered.</summary>
        public event Action<DamageInfo> Damaged;

        /// <summary>Raised once when health reaches zero.</summary>
        public event Action Died;

        /// <summary>Raised whenever health changes, with (current, max).</summary>
        public event Action<float, float> HealthChanged;

        /// <summary>
        /// Raised when a hit should interrupt the enemy's current behaviour, carrying how long the
        /// interruption lasts. The controller decides what a stagger means for its own state machine,
        /// which keeps damage handling and AI behaviour independent of each other.
        /// </summary>
        public event Action<float> Staggered;

        /// <summary>The archetype definition, or null when unassigned.</summary>
        public EnemyDefinition Definition => _definition;

        /// <summary>Current hit points.</summary>
        public float Current => _currentHealth;

        /// <summary>Maximum hit points.</summary>
        public float Max =>
            _definition != null && IsFinite(_definition.MaxHealth)
                ? Mathf.Max(1f, _definition.MaxHealth)
                : 1f;

        /// <summary>Normalised health, for damage flashes and health bars.</summary>
        public float Normalized => Max <= 0f ? 0f : _currentHealth / Max;

        /// <inheritdoc />
        public bool IsAlive => _currentHealth > 0f;

        /// <summary>True while further hits are refused.</summary>
        public bool IsInvulnerable => Time.time < _invulnerableUntil;

        private void Awake()
        {
            _motor = GetComponent<EnemyMotor2D>();
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private void Start()
        {
            if (_definition == null)
            {
                Debug.LogError(
                    $"{nameof(EnemyHealth)} on '{name}' has no {nameof(EnemyDefinition)}. " +
                    "The enemy will die in one hit and will not react. Assign a definition.",
                    this);
                _currentHealth = 1f;
                return;
            }

            _currentHealth = Max;
            HealthChanged?.Invoke(_currentHealth, Max);
        }

        /// <summary>
        /// Supplies the archetype definition. Called by the spawner immediately after instantiation,
        /// because a pooled enemy is created from a shared prefab and its archetype is not part of
        /// that prefab.
        /// </summary>
        public void Initialize(EnemyDefinition definition)
        {
            _definition = definition;
            _currentHealth = definition != null ? definition.MaxHealth : 1f;
            _invulnerableUntil = float.NegativeInfinity;
            HealthChanged?.Invoke(_currentHealth, Max);
        }

        /// <inheritdoc />
        public void TakeDamage(in DamageInfo damage)
        {
            if (!IsAlive) return;
            if (IsInvulnerable) return;
            if (!damage.IsValid) return;

            _currentHealth = Mathf.Max(0f, _currentHealth - damage.Amount);
            Damaged?.Invoke(damage);
            HealthChanged?.Invoke(_currentHealth, Max);

            if (_currentHealth <= 0f)
            {
                Die();
                return;
            }

            if (_definition != null)
            {
                if (IsFinite(_definition.InvulnerabilityAfterHit) &&
                    _definition.InvulnerabilityAfterHit > 0f)
                {
                    _invulnerableUntil = Time.time + _definition.InvulnerabilityAfterHit;
                }

                // Knockback is direction-correct even for a hit from above or below, because the
                // direction is derived from the damage source rather than assumed to be horizontal.
                Vector2 direction = damage.KnockbackDirection(_motor.Position);
                if (damage.KnockbackSpeed > 0f)
                {
                    _motor.VelocityX = direction.x * damage.KnockbackSpeed;
                }
            }

            float staggerDuration = _definition != null && IsFinite(_definition.StaggerDuration)
                ? Mathf.Clamp(_definition.StaggerDuration, 0f, 1.5f)
                : 0.2f;
            Staggered?.Invoke(staggerDuration);
        }

        private void Die()
        {
            // Death is announced before any presentation so listeners can react to the fact itself
            // rather than to whatever plays afterwards.
            Died?.Invoke();

            if (_definition != null) SoundDirector.Request(_definition.DeathCueId, _motor.Position);
        }

        /// <summary>
        /// Restores the enemy to full health and clears all reaction timers.
        /// Used by the spawner, including when a pooled instance is recycled.
        /// </summary>
        public void ResetToFull()
        {
            _currentHealth = Max;
            _invulnerableUntil = float.NegativeInfinity;
            HealthChanged?.Invoke(_currentHealth, Max);
        }

        /// <inheritdoc />
        public void OnTakenFromPool()
        {
            // A pooled enemy must not remember how it died. Publish the reset as well, otherwise
            // a reused health bar or any other listener can keep displaying the previous life.
            _invulnerableUntil = float.NegativeInfinity;
            _currentHealth = Max;
            HealthChanged?.Invoke(_currentHealth, Max);
        }

        /// <inheritdoc />
        public void OnReturnedToPool()
        {
            _invulnerableUntil = float.NegativeInfinity;
        }
    }
}
