using Aether.Core.Combat;
using Aether.Gameplay.Combat;
using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>
    /// Drives the protagonist's sprite animation playback based on player state, velocity, and combat lifecycle.
    /// Operates with strict zero per-frame allocations and preserves exact physics and collision bounds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAnimator : MonoBehaviour
    {
        private const float IdleAltInterval = 7.0f;
        private const float LandDuration = 0.12f;
        private const float StopDuration = 0.12f;

        private PlayerController _controller;
        private PlayerMotor _motor;
        private PlayerCombat _combat;
        private PlayerHealth _health;
        private SpriteRenderer _renderer;

        private PlayerAnimationClip _currentClip;
        private PlayerAnimationId _currentId = PlayerAnimationId.Idle;
        private float _frameTimer;
        private int _frameIndex;
        private AttackPhase _lastAttackPhase = AttackPhase.None;
        private float _attackPhaseElapsed;

        private float _landTimer;
        private float _stopTimer;
        private bool _wasRunning;
        private float _idleTime;
        private bool _isIdleAlt;
        private bool _isInitialized;

        /// <summary>Active animation ID.</summary>
        public PlayerAnimationId CurrentAnimation => _currentId;

        /// <summary>Current frame index in the active animation clip.</summary>
        public int CurrentFrameIndex => _frameIndex;

        /// <summary>The currently active animation clip.</summary>
        public PlayerAnimationClip CurrentClip => _currentClip;

        /// <summary>True when all required components have been wired.</summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Configures the animator before or upon player activation.
        /// </summary>
        public void Configure(PlayerController controller, PlayerMotor motor, PlayerCombat combat,
                              PlayerHealth health, SpriteRenderer renderer)
        {
            _controller = controller;
            _motor = motor;
            _combat = combat;
            _health = health;
            _renderer = renderer;

            _isInitialized = _renderer != null && _motor != null;
            PlayClip(PlayerAnimationId.Idle);
        }

        private void Awake()
        {
            if (_controller == null) _controller = GetComponent<PlayerController>();
            if (_motor == null) _motor = GetComponent<PlayerMotor>();
            if (_combat == null) _combat = GetComponent<PlayerCombat>();
            if (_health == null) _health = GetComponent<PlayerHealth>();
            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>();

            if (!_isInitialized && _renderer != null && _motor != null)
            {
                _isInitialized = true;
                PlayClip(PlayerAnimationId.Idle);
            }
        }

        private void OnEnable()
        {
            if (_controller != null)
            {
                _controller.Landed += OnLanded;
            }
            if (_combat != null)
            {
                _combat.AttackStarted += OnAttackStarted;
            }
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.Landed -= OnLanded;
            }
            if (_combat != null)
            {
                _combat.AttackStarted -= OnAttackStarted;
            }
        }

        private void OnLanded()
        {
            _landTimer = LandDuration;
        }

        private void OnAttackStarted(Data.Config.AttackDefinition attack)
        {
            // Reset idle timers on attack input
            _idleTime = 0f;
            _isIdleAlt = false;
        }

        private void Update()
        {
            if (!_isInitialized || _renderer == null || _motor == null) return;

            float dt = Time.deltaTime;

            if (_landTimer > 0f) _landTimer -= dt;
            if (_stopTimer > 0f) _stopTimer -= dt;

            PlayerAnimationId desired = EvaluateDesiredAnimation();

            if (desired != _currentId)
            {
                PlayClip(desired);
            }

            UpdateActiveFrame(dt);

            // Sprite facing matches physics FacingSign directly
            _renderer.flipX = _motor.FacingSign < 0;
        }

        private PlayerAnimationId EvaluateDesiredAnimation()
        {
            // 1. Defeat / Dead state
            if ((_health != null && !_health.IsAlive) || (_controller != null && _controller.State == PlayerStateId.Dead))
            {
                return PlayerAnimationId.Dead;
            }

            // 2. Hurt stagger
            if (_controller != null && _controller.State == PlayerStateId.Hurt)
            {
                return PlayerAnimationId.Hurt;
            }

            // 3. Dodge phase dash
            if (_controller != null && _controller.State == PlayerStateId.Dodge)
            {
                return PlayerAnimationId.Dodge;
            }

            // 4. Melee Combat Attacking
            if (_combat != null && _combat.IsAttacking)
            {
                // Chain / combo detection: StrikeFollowUp has no next attack in chain
                bool isCombo = _combat.CurrentAttack != null && _combat.CurrentAttack.NextInCombo == null;
                return isCombo ? PlayerAnimationId.Attack2 : PlayerAnimationId.Attack1;
            }

            // 5. Airborne State
            bool isAirborne = _controller != null ? _controller.State == PlayerStateId.Airborne : !_motor.IsGrounded;
            if (isAirborne)
            {
                _idleTime = 0f;
                _isIdleAlt = false;

                if (_motor.VelocityY > 2.0f)
                {
                    return PlayerAnimationId.JumpRise;
                }
                if (Mathf.Abs(_motor.VelocityY) <= 2.0f)
                {
                    return PlayerAnimationId.JumpApex;
                }
                return PlayerAnimationId.JumpFall;
            }

            // 6. Landing compression
            if (_landTimer > 0f)
            {
                return PlayerAnimationId.JumpLand;
            }

            // 7. Grounded Locomotion
            float speed = Mathf.Abs(_motor.VelocityX);
            if (speed > 2.5f)
            {
                _wasRunning = true;
                _idleTime = 0f;
                _isIdleAlt = false;
                return PlayerAnimationId.Run;
            }

            if (speed > 0.2f)
            {
                _wasRunning = false;
                _idleTime = 0f;
                _isIdleAlt = false;
                return PlayerAnimationId.Walk;
            }

            // 8. Braking / Run stop transition
            if (_wasRunning && _stopTimer > 0f)
            {
                return PlayerAnimationId.RunStop;
            }

            if (_wasRunning && speed <= 0.2f && _stopTimer <= 0f)
            {
                _wasRunning = false;
                _stopTimer = StopDuration;
                return PlayerAnimationId.RunStop;
            }

            // 9. Idle & Periodic Idle Variation
            if (_isIdleAlt)
            {
                return PlayerAnimationId.IdleAlt;
            }

            _idleTime += Time.deltaTime;
            if (_idleTime >= IdleAltInterval)
            {
                _idleTime = 0f;
                _isIdleAlt = true;
                return PlayerAnimationId.IdleAlt;
            }

            return PlayerAnimationId.Idle;
        }

        private void PlayClip(PlayerAnimationId id)
        {
            _currentId = id;
            _currentClip = PlayerAnimationCatalog.GetClip(id);
            _frameIndex = 0;
            _frameTimer = 0f;
            _attackPhaseElapsed = 0f;
            _lastAttackPhase = AttackPhase.None;

            if (_currentClip != null)
            {
                _renderer.sprite = _currentClip.GetFrame(0);
            }
        }

        private void UpdateActiveFrame(float dt)
        {
            if (_currentClip == null || _currentClip.FrameCount == 0) return;

            // Attack animation timing locks to combat timeline with full interpolation
            if ((_currentId == PlayerAnimationId.Attack1 || _currentId == PlayerAnimationId.Attack2) && _combat != null)
            {
                AttackPhase currentPhase = _combat.Phase;
                if (currentPhase != _lastAttackPhase)
                {
                    _lastAttackPhase = currentPhase;
                    _attackPhaseElapsed = 0f;
                }
                else
                {
                    _attackPhaseElapsed += dt;
                }

                var def = _combat.CurrentAttack;
                switch (currentPhase)
                {
                    case AttackPhase.Startup:
                    {
                        // Anticipation / windup: Frames 0..4
                        float p = _combat.StartupProgress;
                        _frameIndex = Mathf.Clamp((int)(p * 5f), 0, 4);
                        break;
                    }
                    case AttackPhase.Active:
                    {
                        // Explosive strike: Frames 5..8 smoothly advancing across active window
                        float activeDuration = def != null ? def.Active : 0.07f;
                        float p = activeDuration > 0f ? Mathf.Clamp01(_attackPhaseElapsed / activeDuration) : 1f;
                        _frameIndex = Mathf.Clamp(5 + (int)(p * 4f), 5, 8);
                        break;
                    }
                    case AttackPhase.Recovery:
                    {
                        // Follow-through & return to guard: Frames 9..15 across recovery duration
                        float recoveryDuration = def != null ? def.Recovery : 0.17f;
                        float p = recoveryDuration > 0f ? Mathf.Clamp01(_attackPhaseElapsed / recoveryDuration) : 1f;
                        _frameIndex = Mathf.Clamp(9 + (int)(p * 7f), 9, 15);
                        break;
                    }
                }
            }
            else
            {
                // Standard time-based frame advancement
                _frameTimer += dt;
                float frameDuration = 1f / _currentClip.Fps;

                if (_frameTimer >= frameDuration)
                {
                    int framesToAdvance = (int)(_frameTimer / frameDuration);
                    _frameTimer %= frameDuration;
                    _frameIndex += framesToAdvance;

                    if (_frameIndex >= _currentClip.FrameCount)
                    {
                        if (_currentClip.Loops)
                        {
                            _frameIndex %= _currentClip.FrameCount;
                        }
                        else
                        {
                            _frameIndex = _currentClip.FrameCount - 1;
                            // Reset one-shot idle alt when complete
                            if (_currentId == PlayerAnimationId.IdleAlt)
                            {
                                _isIdleAlt = false;
                                PlayClip(PlayerAnimationId.Idle);
                                return;
                            }
                        }
                    }
                }
            }

            _renderer.sprite = _currentClip.GetFrame(_frameIndex);
        }
    }
}
