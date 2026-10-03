using System;
using Aether.Core.Combat;
using Aether.Data.Config;
using UnityEngine;

namespace Aether.Gameplay.Player
{
    /// <summary>
    /// The player's melee attacks: input handling, facing, and the movement cost of swinging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attack timeline itself lives in <see cref="AttackRunner"/>, which enemies and the boss use
    /// too. This component only supplies the things that are genuinely player-specific: polling
    /// input, choosing the attack that starts a chain, and scaling movement during wind-up so a swing
    /// is a commitment.
    /// </para>
    /// <para>
    /// Driven from <c>Update</c>, not <c>FixedUpdate</c>. Wind-up frames are a promise to the player
    /// about when an attack will land, and the render clock is the clock the player is watching. The
    /// hit query is a physics overlap, which is valid outside the fixed step.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(PlayerMotor))]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [Header("Attacks")]
        [Tooltip("First attack of the chain. Follow-up attacks are referenced from this asset.")]
        [SerializeField]
        private AttackDefinition _firstAttack;

        [Tooltip("Layers that attacks can damage. Should exclude the player's own layer.")]
        [SerializeField]
        private LayerMask _targetLayers;

        private readonly AttackRunner _runner = new AttackRunner();

        /// <summary>
        /// Supplies the attack chain and the layers it may damage. Objects built at runtime have no
        /// inspector to assign them in.
        /// </summary>
        public void Configure(AttackDefinition firstAttack, LayerMask targetLayers)
        {
            _firstAttack = firstAttack;
            _targetLayers = targetLayers;
        }

        private PlayerMotor _motor;
        private PlayerController _controller;

        /// <summary>Raised when an attack begins, including chained follow-ups.</summary>
        public event Action<AttackDefinition> AttackStarted;

        /// <summary>Raised when the hitbox becomes live.</summary>
        public event Action<AttackDefinition> HitboxActivated;

        /// <summary>Raised once when an attack finishes, hit or miss.</summary>
        public event Action<AttackDefinition> AttackFinished;

        /// <summary>Raised for every target actually damaged.</summary>
        public event Action<Collider2D> HitLanded;

        /// <summary>True while an attack is in any phase.</summary>
        public bool IsAttacking => _runner.IsRunning;

        /// <summary>Current phase of the attack timeline.</summary>
        public AttackPhase Phase => _runner.Phase;

        /// <summary>The attack currently executing, or null.</summary>
        public AttackDefinition CurrentAttack => _runner.Current;

        /// <summary>How far through the wind-up the attack is, on [0, 1].</summary>
        public float StartupProgress => _runner.StartupProgress;

        /// <summary>
        /// Scale applied to movement while attacking. Wind-up and active frames root the player so
        /// attacks are committal; recovery returns control so the player is not left helpless.
        /// </summary>
        public float MovementSpeedMultiplier
        {
            get
            {
                if (_controller == null || _controller.Tuning == null) return 1f;

                switch (_runner.Phase)
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

            _runner.AttackStarted += attack => AttackStarted?.Invoke(attack);
            _runner.HitboxActivated += attack => HitboxActivated?.Invoke(attack);
            _runner.AttackFinished += attack => AttackFinished?.Invoke(attack);
            _runner.HitLanded += collider => HitLanded?.Invoke(collider);
        }

        /// <summary>Advances the attack timeline. Called once per frame by <see cref="PlayerController"/>.</summary>
        public void Tick(float deltaTime)
        {
            if (!_runner.IsRunning) return;

            Vector2 origin = _motor.Collider != null
                ? (Vector2)_motor.Collider.bounds.center
                : _motor.Position;

            _runner.Tick(deltaTime, origin, _motor.FacingSign, _targetLayers, _motor.Body, gameObject);
        }

        /// <summary>
        /// Attempts to start or continue an attack. Returns true when an attack actually began.
        /// </summary>
        public bool TryStartAttack()
        {
            if (_controller != null && !_controller.Health.IsAlive) return false;
            if (_firstAttack == null) return false;

            bool started = _runner.RequestAttack(_firstAttack);

            // The lunge is a grounded move. In the air the player keeps their momentum, which stops
            // attacks being used as a free dash and keeps air control predictable.
            if (started && _motor.IsGrounded && _runner.Current != null && _runner.Current.LungeSpeed > 0f)
            {
                _motor.VelocityX = _motor.FacingSign * _runner.Current.LungeSpeed;
            }

            return started;
        }

        /// <summary>Aborts any attack in progress and clears buffered input.</summary>
        public void Cancel()
        {
            _runner.Cancel();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_runner.Current == null || _motor == null) return;

            Vector2 origin = _motor.Collider != null
                ? (Vector2)_motor.Collider.bounds.center
                : _motor.Position;

            Gizmos.color = _runner.Phase == AttackPhase.Active
                ? new Color(1f, 0.2f, 0.2f, 0.6f)
                : new Color(1f, 1f, 0.2f, 0.35f);

            Gizmos.DrawWireCube(_runner.GetHitboxCenter(origin, _motor.FacingSign), _runner.Current.HitboxSize);
        }
#endif
    }
}
