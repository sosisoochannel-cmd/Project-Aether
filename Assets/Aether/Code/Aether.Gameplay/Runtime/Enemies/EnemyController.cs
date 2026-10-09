using System;
using Aether.Core.Combat;
using Aether.Core.States;
using Aether.Gameplay.Combat;
using Aether.Data.Config;
using Aether.Gameplay.Sound;
using UnityEngine;

namespace Aether.Gameplay.Enemies
{
    /// <summary>High-level states shared by every ground enemy archetype.</summary>
    /// <remarks>
    /// Archetypes differ by which transitions they allow and what they do inside a state, not by
    /// owning separate state machines. One machine means one place to reason about "can this enemy
    /// attack from where it is standing", which is the question that decides whether a fight is fair.
    /// </remarks>
    public enum EnemyStateId
    {
        /// <summary>Holding post, unaware.</summary>
        Idle = 0,

        /// <summary>Walking a patrol route, unaware.</summary>
        Patrol = 1,

        /// <summary>Has noticed the player; playing the alert telegraph before committing.</summary>
        Alert = 2,

        /// <summary>Moving to attack range.</summary>
        Chase = 3,

        /// <summary>Attack timeline is running.</summary>
        Attacking = 4,

        /// <summary>Pause after an attack. This is the player's turn.</summary>
        Recovering = 5,

        /// <summary>Knocked back by a hit; no control.</summary>
        Stagger = 6,

        /// <summary>Perched, waiting to drop on a passing player.</summary>
        Ambush = 7,

        /// <summary>Dead.</summary>
        Dead = 8,
    }

    /// <summary>
    /// Drives one enemy: perception, approach, attack commitment and reactions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deterministic by construction.</b> There is no random selection anywhere in this class, and
    /// every delay comes from the archetype's definition. An enemy that randomly chooses an attack,
    /// or that starts one after a random pause, cannot be learned; the whole point of the first three
    /// archetypes is that each one teaches a specific skill, and a player can only learn a rule that
    /// holds every time.
    /// </para>
    /// <para>
    /// <b>Every attack is preceded by a visible cue.</b> Entering <see cref="EnemyStateId.Alert"/>
    /// fires the alert cue and starts the wind-up delay, which is the player's warning that a strike
    /// is coming. Nothing in the attack path happens without it.
    /// </para>
    /// <para>
    /// Perception is a box in front of the enemy rather than a distance check, so an enemy never
    /// notices the player through a floor or a wall. Being noticed through geometry is one of the few
    /// genuinely unfair things an enemy can do.
    /// </para>
    /// <para>
    /// Movement integrates in <c>FixedUpdate</c>; the attack timeline runs in <c>Update</c>, matching
    /// the player's combat. Both go through the shared <see cref="AttackRunner"/>.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(EnemyMotor2D))]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemyController : MonoBehaviour
    {
        [Tooltip("Archetype definition. Overrides the one on EnemyHealth if set here.")]
        [SerializeField]
        private EnemyDefinition _definition;

        [Tooltip("Layers that block sight. Should include the terrain the enemy stands on.")]
        [SerializeField]
        private LayerMask _sightBlockingLayers;

        [Tooltip("Layers this enemy can damage. Should exclude its own layer.")]
        [SerializeField]
        private LayerMask _targetLayers;

        private readonly AttackRunner _runner = new AttackRunner();

        private EnemyMotor2D _motor;
        private EnemyHealth _health;
        private StateMachine<EnemyController, EnemyStateId> _machine;

        private Transform _player;
        private Collider2D _playerCollider;

        /// <summary>
        /// Patrol half-width for this instance, or a negative value to use the archetype's. Level
        /// data gives each placement its own patrol so an encounter's size is authored where the
        /// enemy is placed, not only in the shared archetype.
        /// </summary>
        private float _patrolDistanceOverride = -1f;

        private Vector2 _postPosition;
        private float _patrolTargetX;
        private float _stateTimer;
        private float _staggerRemaining;
        private bool _hasAlerted;

        /// <summary>Raised after a state transition, with (previous, current).</summary>
        public event Action<EnemyStateId, EnemyStateId> StateChanged;

        /// <summary>Raised when this enemy first notices the player, with this controller as the source.</summary>
        public event Action<EnemyController> PlayerSpotted;

        /// <summary>The archetype definition driving this enemy.</summary>
        public EnemyDefinition Definition => _definition;

        /// <summary>Current AI state.</summary>
        public EnemyStateId State => _machine.Current;

        /// <summary>The physics body wrapper.</summary>
        public EnemyMotor2D Motor => _motor;

        /// <summary>Health and damage reactions.</summary>
        public EnemyHealth Health => _health;

        /// <summary>True while the attack timeline is in any phase.</summary>
        public bool IsAttacking => _runner.IsRunning;

        /// <summary>How far through its wind-up the current attack is, on [0, 1].</summary>
        public float AttackStartupProgress => _runner.StartupProgress;

        /// <summary>Raised when an attack's hitbox becomes live.</summary>
        public event Action<AttackDefinition> HitboxActivated;

        /// <summary>Supplies the layers that block sight and that attacks may damage.</summary>
        public void ConfigureLayers(LayerMask sightBlockingLayers, LayerMask targetLayers)
        {
            _sightBlockingLayers = sightBlockingLayers;
            _targetLayers = targetLayers;
        }

        /// <summary>Narrows or widens this instance's patrol. Zero or less keeps the archetype's.</summary>
        public void ConfigurePatrol(int patrolTiles)
        {
            if (patrolTiles > 0) _patrolDistanceOverride = patrolTiles;
        }

        /// <summary>Half-width of this enemy's patrol, in world units.</summary>
        private float PatrolDistance =>
            _patrolDistanceOverride > 0f
                ? _patrolDistanceOverride
                : (_definition != null ? _definition.PatrolDistance : 0f);

        private void Awake()
        {
            _motor = GetComponent<EnemyMotor2D>();
            _health = GetComponent<EnemyHealth>();

            if (_definition == null) _definition = _health.Definition;

            _machine = new StateMachine<EnemyController, EnemyStateId>(9);
            _machine.Add(EnemyStateId.Idle, new IdleState());
            _machine.Add(EnemyStateId.Patrol, new PatrolState());
            _machine.Add(EnemyStateId.Alert, new AlertState());
            _machine.Add(EnemyStateId.Chase, new ChaseState());
            _machine.Add(EnemyStateId.Attacking, new AttackingState());
            _machine.Add(EnemyStateId.Recovering, new RecoveringState());
            _machine.Add(EnemyStateId.Stagger, new StaggerState());
            _machine.Add(EnemyStateId.Ambush, new AmbushState());
            _machine.Add(EnemyStateId.Dead, new DeadState());
            _machine.Changed += (from, to) => StateChanged?.Invoke(from, to);

            _runner.HitboxActivated += attack => HitboxActivated?.Invoke(attack);
            _runner.AttackFinished += _ => { };

            _health.Staggered += OnStaggered;
            _health.Died += OnDied;
        }

        private void OnDestroy()
        {
            if (_health == null) return;

            _health.Staggered -= OnStaggered;
            _health.Died -= OnDied;
        }

        private void Start()
        {
            _motor.Initialize(_definition);
            _motor.SnapToGround();

            _postPosition = _motor.Position;
            _patrolTargetX = _postPosition.x;
            _hasAlerted = false;

            EnemyStateId initial = _definition != null && _definition.Behaviour == EnemyBehaviour.AmbushDropper
                ? EnemyStateId.Ambush
                : (_definition != null && _definition.Patrols ? EnemyStateId.Patrol : EnemyStateId.Idle);

            _machine.Start(this, initial);
        }

        private void OnDisable()
        {
            _machine.Stop(this);
            _runner.Cancel();
        }

        /// <summary>
        /// Supplies the archetype and target. Called by the spawner, because a pooled enemy comes
        /// from a shared prefab and neither its archetype nor the player reference lives there.
        /// </summary>
        public void Initialize(EnemyDefinition definition, Transform player)
        {
            _definition = definition;
            _player = player;

            if (player != null) _playerCollider = player.GetComponent<Collider2D>();

            if (_health != null) _health.Initialize(definition);
            if (_motor != null) _motor.Initialize(definition);
        }

        /// <summary>Returns the enemy to its spawn state. Used when a pooled instance is recycled.</summary>
        public void ResetForSpawn(Vector2 position)
        {
            _runner.Cancel();
            _health.ResetToFull();

            transform.position = position;
            if (_motor.Body != null) _motor.Body.linearVelocity = Vector2.zero;
            _motor.SnapToGround();

            _postPosition = _motor.Position;
            _patrolTargetX = _postPosition.x;
            _hasAlerted = false;
            _staggerRemaining = 0f;

            if (_machine.IsRunning)
            {
                // Restore the archetype's authored opening behaviour. Resetting every enemy to
                // Idle silently turns ambush enemies into ordinary enemies after the first death
                // and also erases patrol routes until they happen to transition again.
                EnemyStateId spawnState =
                    _definition != null && _definition.Behaviour == EnemyBehaviour.AmbushDropper
                        ? EnemyStateId.Ambush
                        : (_definition != null && _definition.Patrols
                            ? EnemyStateId.Patrol
                            : EnemyStateId.Idle);
                _machine.ChangeState(this, spawnState);
            }
        }

        private void Update()
        {
            if (!_health.IsAlive) return;

            if (_runner.IsRunning)
            {
                Vector2 origin = _motor.Collider != null
                    ? (Vector2)_motor.Collider.bounds.center
                    : _motor.Position;

                _runner.Tick(Time.deltaTime, origin, _motor.FacingSign, _targetLayers, _motor.Body, gameObject);
            }

            _stateTimer += Time.deltaTime;
        }

        private void FixedUpdate()
        {
            _motor.RefreshGrounded();
            _machine.FixedTick(this, Time.fixedDeltaTime);
        }

        // ---- Perception ---------------------------------------------------------------------

        /// <summary>
        /// True when the player is inside the archetype's notice box in front of this enemy.
        /// </summary>
        /// <remarks>
        /// The box is centred ahead of the enemy rather than on it, so an enemy facing away genuinely
        /// does not see the player. That asymmetry is what lets a player sneak past a patrolling
        /// enemy, which is a real traversal option rather than a dice roll.
        /// </remarks>
        private bool CanSeePlayer()
        {
            if (_player == null) return false;
            if (_definition == null) return false;

            Vector2 self = _motor.Position;
            Vector2 target = _player.position;

            if (Mathf.Abs(target.y - self.y) > _definition.DetectionHeight) return false;

            float dx = target.x - self.x;
            if (Mathf.Abs(dx) > _definition.DetectionRange) return false;

            // Only look forwards. Distance is measured to the box, not the centre.
            if (Mathf.Sign(dx) != _motor.FacingSign && Mathf.Abs(dx) > 0.35f) return false;

            Vector2 center = self + new Vector2(_motor.FacingSign * (_definition.DetectionRange * 0.5f), 0f);
            var size = new Vector2(_definition.DetectionRange, _definition.DetectionHeight * 2f);

            if (_sightBlockingLayers.value != 0)
            {
                // A wall between the two means no sight, even if the box overlaps.
                RaycastHit2D blocked = Physics2D.Linecast(self, target, _sightBlockingLayers);
                if (blocked.collider != null) return false;
            }

            return Physics2D.OverlapBox(center, size, 0f, _targetLayers) != null;
        }

        /// <summary>True when the player is close enough to be worth attacking.</summary>
        private bool InAttackRange()
        {
            if (_player == null || _definition == null) return false;

            Vector2 delta = (Vector2)_player.position - _motor.Position;
            if (Mathf.Abs(delta.y) > _definition.DetectionHeight) return false;

            return Mathf.Abs(delta.x) <= _definition.AttackRange;
        }

        /// <summary>True when the player has moved beyond the give-up distance.</summary>
        private bool ShouldDisengage()
        {
            if (_player == null || _definition == null) return true;

            Vector2 delta = (Vector2)_player.position - _motor.Position;
            return delta.magnitude > _definition.DisengageRange;
        }

        /// <summary>Horizontal direction towards the player, or 0 when aligned.</summary>
        private float DirectionToPlayer()
        {
            if (_player == null) return 0f;

            float dx = _player.position.x - _motor.Position.x;
            return Mathf.Abs(dx) < 0.05f ? 0f : Mathf.Sign(dx);
        }

        private void FacePlayerOrDirection(float direction)
        {
            if (Mathf.Abs(direction) > 0.01f) _motor.FacingSign = direction > 0f ? 1 : -1;
        }

        // ---- Movement helpers ---------------------------------------------------------------

        /// <summary>
        /// Walks at patrol speed, turning at ledges and walls.
        /// </summary>
        /// <remarks>
        /// Ledge and wall checks run every step rather than only at waypoints, so an enemy whose
        /// patrol route crosses terrain added later still turns instead of walking into a pit. Level
        /// geometry changes should never be able to break an enemy's footing.
        /// </remarks>
        private void MoveWithTerrainChecks(float direction, float deltaTime)
        {
            if (Mathf.Abs(direction) < 0.01f)
            {
                _motor.Move(0f, deltaTime);
                return;
            }

            FacePlayerOrDirection(direction);

            if (_motor.IsGrounded && (_motor.IsLedgeAhead() || _motor.IsWallAhead()))
            {
                _motor.Move(0f, deltaTime);
                _motor.FacingSign = -_motor.FacingSign;
                return;
            }

            _motor.Move(direction * _definition.MoveSpeed, deltaTime);
        }

        private void FireCue(string cueId)
        {
            SoundDirector.Request(cueId, _motor.Position);
        }

        private void OnStaggered(float duration)
        {
            if (!_health.IsAlive) return;

            _staggerRemaining = duration;
            _runner.Cancel();
            _machine.ChangeState(this, EnemyStateId.Stagger);
        }

        private void OnDied()
        {
            _runner.Cancel();
            _machine.ChangeState(this, EnemyStateId.Dead);
        }

        private bool TryBeginAttack()
        {
            if (_definition == null || !_definition.CanAttack) return false;

            FireCue(_definition.AttackCueId);
            _machine.ChangeState(this, EnemyStateId.Attacking);
            return true;
        }

        // ---- States -------------------------------------------------------------------------

        /// <summary>Holds post, scanning for the player.</summary>
        private sealed class IdleState : IState<EnemyController>
        {
            public void Enter(EnemyController c)
            {
                c._motor.StopHorizontal();
            }

            public void Tick(EnemyController c, float dt) { }

            public void FixedTick(EnemyController c, float dt)
            {
                c._motor.Move(0f, dt);
                c._motor.ApplyGravity(dt);

                if (c.CanSeePlayer()) c.BeginAlert();
            }

            public void Exit(EnemyController c) { }
        }

        /// <summary>Walks between patrol bounds, pausing at each end.</summary>
        private sealed class PatrolState : IState<EnemyController>
        {
            public void Enter(EnemyController c)
            {
                c._patrolTargetX = c._postPosition.x + (c._motor.FacingSign * c.PatrolDistance);
                c._stateTimer = 0f;
            }

            public void Tick(EnemyController c, float dt) { }

            public void FixedTick(EnemyController c, float dt)
            {
                c._motor.ApplyGravity(dt);

                if (c.CanSeePlayer())
                {
                    c.BeginAlert();
                    return;
                }

                float toTarget = c._patrolTargetX - c._motor.Position.x;

                // Pause at each end so the patrol reads as deliberation rather than a moving target,
                // and so a player watching can time their crossing.
                if (c._stateTimer < c._definition.PatrolPause && Mathf.Abs(toTarget) < 0.2f)
                {
                    c._motor.Move(0f, dt);
                    return;
                }

                if (Mathf.Abs(toTarget) < 0.15f)
                {
                    c._motor.FacingSign = -c._motor.FacingSign;
                    c._patrolTargetX = c._postPosition.x + (c._motor.FacingSign * c.PatrolDistance);
                    c._stateTimer = 0f;
                    return;
                }

                c.MoveWithTerrainChecks(Mathf.Sign(toTarget), dt);
            }

            public void Exit(EnemyController c) { }
        }

        /// <summary>Notices the player and delays before committing. This is the player's warning.</summary>
        private sealed class AlertState : IState<EnemyController>
        {
            public void Enter(EnemyController c)
            {
                c._stateTimer = 0f;
                c._motor.StopHorizontal();

                if (!c._hasAlerted)
                {
                    c._hasAlerted = true;
                    c.FireCue(c._definition.AlertCueId);
                    c.PlayerSpotted?.Invoke(c);
                }
            }

            public void Tick(EnemyController c, float dt) { }

            public void FixedTick(EnemyController c, float dt)
            {
                c._motor.Move(0f, dt);
                c._motor.ApplyGravity(dt);

                if (c.ShouldDisengage())
                {
                    c._machine.ChangeState(c, EnemyStateId.Idle);
                    return;
                }

                if (c._stateTimer < c._definition.AttackWindupDelay) return;

                if (c.InAttackRange())
                {
                    c.TryBeginAttack();
                    return;
                }

                c._machine.ChangeState(c, EnemyStateId.Chase);
            }

            public void Exit(EnemyController c) { }
        }

        /// <summary>Closes to attack range.</summary>
        private sealed class ChaseState : IState<EnemyController>
        {
            public void Enter(EnemyController c)
            {
                c._stateTimer = 0f;
            }

            public void Tick(EnemyController c, float dt) { }

            public void FixedTick(EnemyController c, float dt)
            {
                c._motor.ApplyGravity(dt);

                if (c.ShouldDisengage())
                {
                    c._machine.ChangeState(c, EnemyStateId.Idle);
                    return;
                }

                float direction = c.DirectionToPlayer();
                c.FacePlayerOrDirection(direction);

                if (c.InAttackRange())
                {
                    // Only commit once the approach has settled, so an enemy does not attack from
                    // mid-stride with its telegraph half-hidden by its own movement.
                    if (Mathf.Abs(c._motor.VelocityX) < c._definition.MoveSpeed * 0.5f)
                    {
                        c.TryBeginAttack();
                        return;
                    }
                }

                c.MoveWithTerrainChecks(direction, dt);
            }

            public void Exit(EnemyController c) { }
        }

        /// <summary>The attack timeline owns this state's lifetime.</summary>
        private sealed class AttackingState : IState<EnemyController>
        {
            public void Enter(EnemyController c)
            {
                c._stateTimer = 0f;

                if (c._definition == null || c._definition.Attack == null)
                {
                    c._machine.ChangeState(c, EnemyStateId.Idle);
                    return;
                }

                c._runner.RequestAttack(c._definition.Attack);
            }

            public void Tick(EnemyController c, float dt) { }

            public void FixedTick(EnemyController c, float dt)
            {
                c._motor.ApplyGravity(dt);

                if (!c._runner.IsRunning)
                {
                    c._machine.ChangeState(c, EnemyStateId.Recovering);
                    return;
                }

                float lunge = c._definition.AttackLungeSpeed;

                // The lunge only happens while the hitbox is live. Committing to it during wind-up
                // would slide the telegraph out of the player's reading position.
                if (c._runner.Phase == AttackPhase.Active && c._motor.IsGrounded)
                {
                    c._motor.Move(c._motor.FacingSign * lunge, dt);
                }
                else if (c._runner.Phase == AttackPhase.Startup)
                {
                    c._motor.Move(0f, dt);
                }
                else
                {
                    c._motor.Move(0f, dt);
                }
            }

            public void Exit(EnemyController c) { }
        }

        /// <summary>
        /// Stands still after attacking. This pause is load-bearing: it is the window in which the
        /// player is meant to answer, and removing it turns every exchange into a trade.
        /// </summary>
        private sealed class RecoveringState : IState<EnemyController>
        {
            public void Enter(EnemyController c)
            {
                c._stateTimer = 0f;
                c._motor.StopHorizontal();
            }

            public void Tick(EnemyController c, float dt) { }

            public void FixedTick(EnemyController c, float dt)
            {
                c._motor.Move(0f, dt);
                c._motor.ApplyGravity(dt);

                if (c._stateTimer < c._definition.RecoveryPause) return;

                if (c.CanSeePlayer()) c.BeginAlert();
                else c._machine.ChangeState(c, EnemyStateId.Idle);
            }

            public void Exit(EnemyController c) { }
        }

        /// <summary>Knocked back; no decisions are made.</summary>
        private sealed class StaggerState : IState<EnemyController>
        {
            public void Enter(EnemyController c)
            {
                c._staggerRemaining = c._definition != null ? c._definition.KnockbackDuration : 0.15f;
            }

            public void Tick(EnemyController c, float dt) { }

            public void FixedTick(EnemyController c, float dt)
            {
                c._motor.ApplyGravity(dt);

                // Knockback velocity is preserved for its brief window so the hit reads as a shove,
                // then control returns. The enemy is never left stunned.
                if (c._staggerRemaining > 0f)
                {
                    c._staggerRemaining -= dt;
                    return;
                }

                c._machine.ChangeState(c, EnemyStateId.Idle);
            }

            public void Exit(EnemyController c)
            {
                c._motor.StopHorizontal();
            }
        }

        /// <summary>Perched above a route, waiting to drop on a passing player.</summary>
        private sealed class AmbushState : IState<EnemyController>
        {
            public void Enter(EnemyController c)
            {
                c._stateTimer = 0f;
                c._motor.StopHorizontal();

                // Perched enemies ignore gravity so they hold their position until triggered.
                if (c._motor.Body != null) c._motor.Body.gravityScale = 0f;
                c._motor.Body.linearVelocity = Vector2.zero;
            }

            public void Tick(EnemyController c, float dt) { }

            public void FixedTick(EnemyController c, float dt)
            {
                if (c._motor.Body.gravityScale != 0f) c._motor.Body.gravityScale = 0f;

                // Triggered from directly below, which is the direction the player is travelling and
                // the only one where a drop is a fair surprise rather than an arbitrary hit.
                bool below = c._player != null &&
                             Mathf.Abs(c._player.position.x - c._motor.Position.x) < 1.2f &&
                             c._player.position.y < c._motor.Position.y;

                if (!below)
                {
                    c._stateTimer = 0f;
                    return;
                }

                // The delay is the cue. Without it the drop is unavoidable, which is the difference
                // between teaching environmental awareness and punishing it.
                if (c._stateTimer < c._definition.DropDelay) return;

                c.FireCue(c._definition.AlertCueId);
                c._motor.Body.gravityScale = 1f;
                c._machine.ChangeState(c, EnemyStateId.Chase);
            }

            public void Exit(EnemyController c)
            {
                // EnemyMotor2D applies gravity manually for every state. Restoring Unity gravity
                // here would apply gravity twice after an ambush drop and make its fall speed
                // diverge from the authored telegraph and terminal-speed clamp.
                if (c._motor.Body != null) c._motor.Body.gravityScale = 0f;
            }
        }

        /// <summary>Dead. All behaviour stops; the spawner handles removal and pooling.</summary>
        private sealed class DeadState : IState<EnemyController>
        {
            public void Enter(EnemyController c)
            {
                c._motor.StopHorizontal();
                c._runner.Cancel();
            }

            public void Tick(EnemyController c, float dt) { }

            public void FixedTick(EnemyController c, float dt)
            {
                // Gravity still applies so the body settles rather than hovering mid-air.
                c._motor.ApplyGravity(dt);
                c._motor.Move(0f, dt);
            }

            public void Exit(EnemyController c) { }
        }

        /// <summary>Moves into the alert state and records the transition.</summary>
        private void BeginAlert()
        {
            // AlertState.Enter owns the first-alert latch, sound and PlayerSpotted event.
            // Setting _hasAlerted here would make Enter believe the cue already fired, so no
            // enemy would ever announce its first sighting to the discovery/achievement systems.
            _machine.ChangeState(this, EnemyStateId.Alert);
        }
    }
}
