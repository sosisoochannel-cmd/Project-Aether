using Aether.Core.Combat;
using Aether.Gameplay.Levels;
using UnityEngine;

namespace Aether.Gameplay.Enemies
{
    /// <summary>
    /// Shows what the enemy is about to do, using only the states the enemy already has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first encounter teaches the Forest Stalker by letting the player watch it. That only
    /// works if its intentions are visible: a wind-up the player can see coming, an active frame
    /// that is unmistakably dangerous, and — the part most games get wrong — a recovery that reads
    /// as <i>safe to approach</i>. This component is where that is drawn.
    /// </para>
    /// <para>
    /// <b>It owns no timing.</b> Nothing here decides how long a wind-up lasts or when damage
    /// lands; it reads <see cref="EnemyController.State"/> and
    /// <see cref="EnemyController.AttackStartupProgress"/> and colours a square. Delete it and the
    /// enemy fights exactly the same, which is the test for whether presentation has stayed
    /// presentation. There is deliberately no second attack timeline.
    /// </para>
    /// <para>
    /// It animates only while an attack is actually in progress, so a level full of idle enemies
    /// costs nothing per frame.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class EnemyTelegraph : MonoBehaviour
    {
        /// <summary>How far the body swells at the peak of a wind-up.</summary>
        private const float WindupSwell = 1.22f;

        /// <summary>How fast the "I have seen you" pulse fades.</summary>
        private const float AlertPulseSeconds = 0.35f;

        /// <summary>How long a hit reads as a white flash.</summary>
        private const float HitFlashSeconds = 0.12f;

        /// <summary>How quickly colours settle, in seconds. Snappy: this is information, not mood.</summary>
        private const float ColourSnap = 0.06f;

        private SpriteRenderer _renderer;
        private Transform _visual;
        private Vector3 _baseScale;
        private EnemyController _controller;

        private Color _target = Color.white;
        private bool _animating;
        private float _alertPulseRemaining;
        private float _hitFlashRemaining;
        private float _impactPulseRemaining;
        private bool _deadPresentation;

        /// <summary>Supplies the visual. Called by the factory before the object is activated.</summary>
        public void Configure(SpriteRenderer renderer, EnemyController controller)
        {
            _renderer = renderer;
            _controller = controller;
            _visual = renderer != null ? renderer.transform : null;
            _baseScale = _visual != null ? _visual.localScale : Vector3.one;
        }

        private void Awake()
        {
            if (_controller != null)
            {
                _controller.StateChanged += OnStateChanged;
                _controller.PlayerSpotted += OnSpotted;
            }

            EnemyHealth health = GetComponent<EnemyHealth>();
            if (health != null)
            {
                health.Damaged += OnDamaged;
                health.Died += OnDied;
            }

            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>();

            // The enemy may already be in a state when this runs, so the first colour comes from
            // the state itself rather than waiting for the next transition.
            ApplyState(_controller != null ? _controller.State : EnemyStateId.Idle);
            _target = _renderer != null ? _renderer.color : Color.white;
        }

        private void OnDestroy()
        {
            if (_controller != null)
            {
                _controller.StateChanged -= OnStateChanged;
                _controller.PlayerSpotted -= OnSpotted;
            }

            EnemyHealth health = GetComponent<EnemyHealth>();
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
            }
        }

        private void OnStateChanged(EnemyStateId previous, EnemyStateId current)
        {
            // The wind-up ramp needs a value that changes during the state, not the state itself.
            _animating = current == EnemyStateId.Attacking;
            ApplyState(current);
            enabled = true;
        }

        private void OnSpotted(EnemyController _)
        {
            _alertPulseRemaining = AlertPulseSeconds;
            enabled = true;
        }

        private void OnDamaged(DamageInfo info)
        {
            _hitFlashRemaining = HitFlashSeconds;
            _impactPulseRemaining = 0.16f;
            enabled = true;
        }

        private void OnDied()
        {
            _deadPresentation = true;
            ApplyState(EnemyStateId.Dead);
            enabled = true;
        }

        private void ApplyState(EnemyStateId state)
        {
            switch (state)
            {
                case EnemyStateId.Idle:
                case EnemyStateId.Patrol:
                case EnemyStateId.Ambush:
                    _target = LevelPalette.EnemyIdle;
                    break;
                case EnemyStateId.Alert:
                    _target = LevelPalette.EnemyAlert;
                    break;
                case EnemyStateId.Chase:
                    _target = LevelPalette.EnemyAlert;
                    break;
                case EnemyStateId.Attacking:
                    _target = LevelPalette.EnemyWindup;
                    break;
                case EnemyStateId.Recovering:
                    // Dull and cool on purpose: this is the window the player is meant to use.
                    _target = LevelPalette.EnemyRecovering;
                    break;
                case EnemyStateId.Stagger:
                    _target = LevelPalette.EnemyStagger;
                    break;
                case EnemyStateId.Dead:
                    _target = LevelPalette.EnemyDead;
                    _animating = false;
                    break;
            }

            if (_controller != null && _controller.Health != null && !_controller.Health.IsAlive)
            {
                _target = LevelPalette.EnemyDead;
                _animating = false;
            }
        }

        private void Update()
        {
            if (_renderer == null) return;

            float scale = 1f;
            if (_deadPresentation)
            {
                // A short, readable defeat collapse; the controller/spawner still owns removal.
                float deathProgress = Mathf.Clamp01(1f - (_hitFlashRemaining / HitFlashSeconds));
                scale = Mathf.Lerp(1f, 0.72f, deathProgress);
            }
            if (_animating && _controller != null)
            {
                // The wind-up ramp: the body swells as the attack approaches, so the last moment
                // before the hit frame is visibly the peak. This is the telegraph the first
                // encounter is built to teach.
                float progress = Mathf.Clamp01(_controller.AttackStartupProgress);
                scale = Mathf.Lerp(1f, WindupSwell, progress);
                if (_controller.State == EnemyStateId.Attacking && progress >= 1f)
                {
                    // Past the wind-up the attack is live: the colour says danger, not "get ready".
                    _target = LevelPalette.EnemyActive;
                }
            }

            if (_impactPulseRemaining > 0f)
            {
                _impactPulseRemaining -= Time.deltaTime;
                float pulse = Mathf.Sin(Mathf.Clamp01(_impactPulseRemaining / 0.16f) * Mathf.PI);
                scale *= 1f + 0.14f * pulse;
            }

            if (_alertPulseRemaining > 0f)
            {
                _alertPulseRemaining -= Time.deltaTime;
                scale = Mathf.Max(scale, 1f + (0.15f * (_alertPulseRemaining / AlertPulseSeconds)));
            }

            Color colour = _target;
            if (_hitFlashRemaining > 0f)
            {
                _hitFlashRemaining -= Time.deltaTime;
                colour = Color.Lerp(_target, Color.white,
                    Mathf.Clamp01(_hitFlashRemaining / HitFlashSeconds));
            }

            _renderer.color = Color.Lerp(_renderer.color, colour,
                1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, ColourSnap)));

            if (_visual != null)
            {
                _visual.localScale = Vector3.Lerp(_visual.localScale,
                    new Vector3(_baseScale.x * scale, _baseScale.y * scale, _baseScale.z),
                    1f - Mathf.Exp(-Time.deltaTime / 0.05f));
            }

            // Nothing left to show: stop being called at all until something happens. An idle
            // level of three enemies costs three disabled components rather than three updates.
            if (!_animating && _alertPulseRemaining <= 0f && _hitFlashRemaining <= 0f && _impactPulseRemaining <= 0f
                && (_renderer.color - colour).maxColorComponent < 0.02f
                && (_visual == null
                    || (_visual.localScale - new Vector3(_baseScale.x * scale, _baseScale.y * scale,
                        _baseScale.z)).sqrMagnitude < 1e-6f))
            {
                _renderer.color = colour;
                if (_visual != null) _visual.localScale = new Vector3(_baseScale.x, _baseScale.y, _baseScale.z);
                enabled = false;
            }
        }
    }
}
