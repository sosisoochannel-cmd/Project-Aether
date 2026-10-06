using Aether.Core.Combat;
using Aether.Gameplay.Levels;
using Aether.Gameplay.Settings;
using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>
    /// Shows what happened to the player: a hit taken, a hit landed, a swing thrown, a death.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The combat feedback the player actually needs is narrow. They need to know when they were
    /// hit — because being hit is the thing the game punishes — and they need to see their own
    /// attack connect, because an attack that lands silently reads as broken. Everything else is
    /// decoration and is not here.
    /// </para>
    /// <para>
    /// Like the enemy telegraph, this owns no timing and no rules. It listens to events the combat
    /// systems already publish and colours a square; delete it and the fight is unchanged.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerFeedback : MonoBehaviour
    {
        private const float HitFlashSeconds = 0.18f;
        private const float LandedFlashSeconds = 0.09f;
        private const float AttackPulseSeconds = 0.12f;

        /// <summary>How far the body leans into a swing.</summary>
        private const float AttackPulseScale = 1.14f;

        private SpriteRenderer _renderer;
        private Transform _visual;
        private Vector3 _baseScale;

        private Color _target = Color.white;
        private bool _reducedMotion;
        private float _hitFlash;
        private float _landedFlash;
        private float _attackPulse;

        /// <summary>Supplies the visual. Called by the factory before the object is activated.</summary>
        public void Configure(SpriteRenderer renderer)
        {
            _renderer = renderer;
            _visual = renderer != null ? renderer.transform : null;
            _baseScale = _visual != null ? _visual.localScale : Vector3.one;
            _target = LevelPalette.Player;
        }

        private void OnEnable()
        {
            AetherSettings.Ensure().Changed += OnSettingsChanged;
            ApplySettings();
        }

        private void OnDisable()
        {
            AetherSettings.Current.Changed -= OnSettingsChanged;
        }

        /// <summary>
        /// Reads whether the player has asked for reduced motion.
        /// </summary>
        /// <remarks>
        /// Read into a field rather than looked up in <c>Update</c>: this component runs only while
        /// something is changing, and it should cost nothing while it does.
        /// </remarks>
        private void ApplySettings()
        {
            _reducedMotion = AetherSettings.Ensure().Values.Accessibility.ReducedMotion;
        }

        private void OnSettingsChanged(string id)
        {
            ApplySettings();
        }

        private void Awake()
        {
            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>();
            if (_renderer != null) _renderer.color = LevelPalette.Player;

            PlayerHealth health = GetComponent<PlayerHealth>();
            if (health != null)
            {
                health.Damaged += OnDamaged;
                health.HealthChanged += OnHealthChanged;
                health.Died += OnDied;
            }

            PlayerCombat combat = GetComponent<PlayerCombat>();
            if (combat != null)
            {
                combat.AttackStarted += OnAttackStarted;
                combat.HitLanded += OnHitLanded;
            }
        }

        private void OnDestroy()
        {
            PlayerHealth health = GetComponent<PlayerHealth>();
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.HealthChanged -= OnHealthChanged;
                health.Died -= OnDied;
            }

            PlayerCombat combat = GetComponent<PlayerCombat>();
            if (combat != null)
            {
                combat.AttackStarted -= OnAttackStarted;
                combat.HitLanded -= OnHitLanded;
            }
        }

        private void OnDamaged(DamageInfo info)
        {
            _hitFlash = HitFlashSeconds;
            enabled = true;
        }

        /// <summary>
        /// Being healed back to life — a respawn — is the only signal that the player is alive
        /// again, and it already exists, so no new event and no new coupling is needed.
        /// </summary>
        private void OnHealthChanged(float current, float maximum)
        {
            if (current <= 0f || _target != LevelPalette.PlayerDead) return;
            _target = LevelPalette.Player;
            enabled = true;
        }

        private void OnDied()
        {
            _target = LevelPalette.PlayerDead;
            enabled = true;
        }

        private void OnAttackStarted(Data.Config.AttackDefinition attack)
        {
            _attackPulse = AttackPulseSeconds;
            enabled = true;
        }

        private void OnHitLanded(Collider2D other)
        {
            _landedFlash = LandedFlashSeconds;
            enabled = true;
        }

        private void Update()
        {
            if (_renderer == null) return;

            Color colour = _target;
            if (_hitFlash > 0f)
            {
                _hitFlash -= Time.deltaTime;
                colour = Color.Lerp(_target, LevelPalette.PlayerHit, Mathf.Clamp01(_hitFlash / HitFlashSeconds));
            }
            else if (_landedFlash > 0f)
            {
                _landedFlash -= Time.deltaTime;
                colour = Color.Lerp(_target, Color.white, Mathf.Clamp01(_landedFlash / LandedFlashSeconds));
            }

            float scale = 1f;
            if (_attackPulse > 0f)
            {
                _attackPulse -= Time.deltaTime;

                // The attack pulse is the only movement this component owns, and it is exactly the
                // kind of thing reduced motion is for. The timer keeps running while it is off, so
                // the pulse still ends when it always did and the component still switches itself
                // off afterwards: the setting removes the movement, not the bookkeeping.
                if (!_reducedMotion)
                {
                    float t = Mathf.Clamp01(_attackPulse / AttackPulseSeconds);
                    scale = Mathf.Lerp(1f, AttackPulseScale, t);
                }
            }

            _renderer.color = Color.Lerp(_renderer.color, colour,
                1f - Mathf.Exp(-Time.deltaTime / 0.05f));

            if (_visual != null)
            {
                Vector3 wanted = new Vector3(_baseScale.x * scale, _baseScale.y, _baseScale.z);
                _visual.localScale = Vector3.Lerp(_visual.localScale, wanted,
                    1f - Mathf.Exp(-Time.deltaTime / 0.05f));
            }

            if (_hitFlash <= 0f && _landedFlash <= 0f && _attackPulse <= 0f
                && (_renderer.color - colour).maxColorComponent < 0.02f
                && (_visual == null || (_visual.localScale - _baseScale).sqrMagnitude < 1e-6f))
            {
                _renderer.color = _target;
                if (_visual != null) _visual.localScale = _baseScale;
                enabled = false;
            }
        }
    }
}
