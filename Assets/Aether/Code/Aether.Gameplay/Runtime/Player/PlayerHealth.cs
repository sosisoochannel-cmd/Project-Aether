using System;
using Aether.Core.Combat;
using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>
    /// The player's health, invulnerability windows and death.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Invulnerability is expressed as an <b>absolute expiry timestamp</b> rather than a countdown,
    /// for the same reason the traversal timers are: a countdown ticks down on one clock and gets
    /// read on another, and on a throttling phone that mismatch becomes "I got hit twice by one
    /// attack". <see cref="BeginInvulnerability"/> extends an existing window instead of replacing
    /// it, so a shorter window can never cut short a longer one already in progress.
    /// </para>
    /// <para>
    /// Damage is refused while invulnerable. Callers do not need to pre-check
    /// <see cref="IsInvulnerable"/>, which removes the most common source of double-hit bugs.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(PlayerMotor))]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerHealth : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        [Tooltip("Maximum hit points. The opening region should be survivable on this value without upgrades.")]
        [SerializeField, Min(1f)]
        private float _maxHealth = 5f;

        [Header("Reactions")]
        [Tooltip("Seconds of withheld control after a hit lands.")]
        [SerializeField, Range(0f, 1f)]
        private float _hurtLockout = 0.22f;

        [Tooltip("Seconds of invulnerability granted after a hit lands, so a contact enemy cannot chain-hit.")]
        [SerializeField, Range(0f, 3f)]
        private float _invulnerabilityAfterHit = 0.75f;

        private PlayerMotor _motor;
        private PlayerController _controller;
        private float _currentHealth;
        private float _invulnerableUntil = float.NegativeInfinity;

        /// <summary>Raised whenever current or maximum health changes, with (current, max).</summary>
        public event Action<float, float> HealthChanged;

        /// <summary>Raised when a hit is actually taken, after invulnerability has been considered.</summary>
        public event Action<DamageInfo> Damaged;

        /// <summary>Raised once when health reaches zero.</summary>
        public event Action Died;

        /// <summary>Current hit points.</summary>
        public float Current => _currentHealth;

        /// <summary>Maximum hit points.</summary>
        public float Max => _maxHealth;

        /// <summary>Normalised health, for HUD fills.</summary>
        public float Normalized => _maxHealth <= 0f ? 0f : _currentHealth / _maxHealth;

        /// <inheritdoc />
        public bool IsAlive => _currentHealth > 0f;

        /// <summary>True while damage is being refused.</summary>
        public bool IsInvulnerable => Time.time < _invulnerableUntil;

        /// <summary>Seconds of invulnerability remaining. Zero when vulnerable.</summary>
        public float InvulnerabilityRemaining => Mathf.Max(0f, _invulnerableUntil - Time.time);

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _controller = GetComponent<PlayerController>();
            _currentHealth = _maxHealth;
        }

        private void Start()
        {
            // Announced in Start so listeners that subscribe in their own Awake/OnEnable still get it.
            HealthChanged?.Invoke(_currentHealth, _maxHealth);
        }

        /// <summary>
        /// Opens an invulnerability window, extending any window already running.
        /// </summary>
        public void BeginInvulnerability(float duration)
        {
            if (duration <= 0f) return;
            _invulnerableUntil = Mathf.Max(_invulnerableUntil, Time.time + duration);
        }

        /// <summary>Ends any invulnerability immediately. Used by tests and debug tooling.</summary>
        public void ClearInvulnerability()
        {
            _invulnerableUntil = float.NegativeInfinity;
        }

        /// <inheritdoc />
        public void TakeDamage(in DamageInfo damage)
        {
            if (!IsAlive) return;
            if (IsInvulnerable) return;
            if (damage.Amount <= 0f) return;

            _currentHealth = Mathf.Max(0f, _currentHealth - damage.Amount);
            Damaged?.Invoke(damage);
            HealthChanged?.Invoke(_currentHealth, _maxHealth);

            if (_currentHealth <= 0f)
            {
                Died?.Invoke();
                return;
            }

            BeginInvulnerability(_invulnerabilityAfterHit);

            Vector2 direction = damage.KnockbackDirection(_motor.Position);
            _motor.SetVelocity(
                direction.x * damage.KnockbackSpeed,
                Mathf.Max(_motor.VelocityY, damage.LiftSpeed));

            if (_controller != null) _controller.EnterHurt(_hurtLockout);
        }

        /// <summary>
        /// Heals the player, never exceeding <see cref="Max"/>. Used by checkpoints and pickups.
        /// Returns the amount actually restored so callers can avoid playing feedback for a no-op.
        /// </summary>
        public float Heal(float amount)
        {
            if (amount <= 0f) return 0f;
            if (!IsAlive) return 0f;

            float before = _currentHealth;
            _currentHealth = Mathf.Min(_maxHealth, _currentHealth + amount);

            float restored = _currentHealth - before;
            if (restored > 0f) HealthChanged?.Invoke(_currentHealth, _maxHealth);
            return restored;
        }

        /// <summary>Returns the player to full health and clears invulnerability. Used on respawn.</summary>
        public void ResetToFull()
        {
            _currentHealth = _maxHealth;
            _invulnerableUntil = float.NegativeInfinity;
            HealthChanged?.Invoke(_currentHealth, _maxHealth);
        }
    }
}
