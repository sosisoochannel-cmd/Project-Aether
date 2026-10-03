using System;
using System.Collections.Generic;

namespace Aether.Core.States
{
    /// <summary>
    /// Behaviour for a single state of a <see cref="StateMachine{TContext,TKey}"/>.
    /// </summary>
    /// <remarks>
    /// Implementations should be stateless where possible and keep per-run data on the context
    /// object. States are long-lived instances reused across every transition.
    /// </remarks>
    public interface IState<in TContext>
    {
        /// <summary>Called once when the state becomes active. Do not perform heavy allocation here.</summary>
        void Enter(TContext context);

        /// <summary>Called every frame while active.</summary>
        void Tick(TContext context, float deltaTime);

        /// <summary>Called every physics step while active. Use for anything that moves a Rigidbody2D.</summary>
        void FixedTick(TContext context, float deltaTime);

        /// <summary>Called once when the state is left. Cancel timers and clear flags here.</summary>
        void Exit(TContext context);
    }

    /// <summary>
    /// A deterministic, allocation-free-after-setup finite state machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by the player, every enemy archetype and the boss, so transitions are observable in one
    /// place. <see cref="Changed"/> exists so presentation layers (animation, audio, VFX) can react
    /// without the gameplay code knowing about them.
    /// </para>
    /// <para>
    /// <b>Determinism matters here.</b> Enemy behaviour must be predictable and learnable, so this
    /// machine deliberately provides no randomness and no implicit timers — transitions happen only
    /// where gameplay code asks for them.
    /// </para>
    /// </remarks>
    public sealed class StateMachine<TContext, TKey>
        where TKey : struct, Enum
    {
        private readonly Dictionary<TKey, IState<TContext>> _states;

        /// <summary>Raised after a transition completes, with (previous, current).</summary>
        public event Action<TKey, TKey> Changed;

        public StateMachine(int capacity = 8)
        {
            _states = new Dictionary<TKey, IState<TContext>>(capacity);
        }

        /// <summary>The currently active state key.</summary>
        public TKey Current { get; private set; }

        /// <summary>The state that was active before the current one. Equals <see cref="Current"/> before any transition.</summary>
        public TKey Previous { get; private set; }

        /// <summary>Seconds since the last transition. States use this instead of running their own timers.</summary>
        public float TimeInState { get; private set; }

        /// <summary>True once <see cref="Start"/> has been called and the machine is running.</summary>
        public bool IsRunning { get; private set; }

        private IState<TContext> CurrentState { get; set; }

        /// <summary>Registers a state implementation. Re-registering the same key replaces it.</summary>
        public void Add(TKey key, IState<TContext> state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            _states[key] = state;
        }

        /// <summary>
        /// Enters <paramref name="initialState"/>. Call once, after all states are registered.
        /// </summary>
        public void Start(TContext context, TKey initialState)
        {
            if (IsRunning) throw new InvalidOperationException("StateMachine is already running. Call Stop() first.");
            if (!_states.TryGetValue(initialState, out IState<TContext> state))
            {
                throw new KeyNotFoundException(
                    $"State '{initialState}' has not been registered. Register it before calling Start().");
            }

            Current = initialState;
            Previous = initialState;
            CurrentState = state;
            TimeInState = 0f;
            IsRunning = true;
            state.Enter(context);
        }

        /// <summary>
        /// Transitions to <paramref name="key"/>. No-op when the machine is stopped, and
        /// self-transitions are ignored so a state cannot accidentally restart itself.
        /// </summary>
        public void ChangeState(TContext context, TKey key)
        {
            if (!IsRunning) return;
            if (EqualityComparer<TKey>.Default.Equals(Current, key)) return;
            if (!_states.TryGetValue(key, out IState<TContext> next))
            {
                throw new KeyNotFoundException(
                    $"State '{key}' has not been registered. Register it before transitioning to it.");
            }

            TKey from = Current;
            CurrentState.Exit(context);

            Previous = from;
            Current = key;
            CurrentState = next;
            TimeInState = 0f;

            next.Enter(context);
            Changed?.Invoke(from, key);
        }

        /// <summary>Drives the frame update of the active state.</summary>
        public void Tick(TContext context, float deltaTime)
        {
            if (!IsRunning) return;

            TimeInState += deltaTime;
            CurrentState.Tick(context, deltaTime);
        }

        /// <summary>Drives the physics update of the active state.</summary>
        public void FixedTick(TContext context, float deltaTime)
        {
            if (!IsRunning) return;
            CurrentState.FixedTick(context, deltaTime);
        }

        /// <summary>Exits the active state and stops the machine. Safe to call when already stopped.</summary>
        public void Stop(TContext context)
        {
            if (!IsRunning) return;

            CurrentState.Exit(context);
            CurrentState = null;
            IsRunning = false;
        }
    }
}
