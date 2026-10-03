using System;
using System.Collections.Generic;

namespace Aether.Core.Events
{
    /// <summary>
    /// Lightweight, allocation-free typed event bus for low-frequency cross-system signals
    /// (ability unlocked, checkpoint reached, boss phase changed, secret discovered).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Use this for state transitions, not for per-frame traffic.</b> Publishing does a single
    /// dictionary lookup plus a delegate invocation; that is cheap but not free. Anything that
    /// happens every frame (grounded checks, animation parameters, hit-stun timers) should stay a
    /// direct C# event, an interface call, or a polled property.
    /// </para>
    /// <para>
    /// Handlers are invoked synchronously on the publishing thread, in subscription order.
    /// A handler that throws will propagate to the publisher; do not use this bus for
    /// exception-driven control flow.
    /// </para>
    /// <para>
    /// The bus owns no Unity objects and is safe to unit test in isolation.
    /// </para>
    /// </remarks>
    public sealed class EventBus
    {
        private readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>(32);

        /// <summary>Registers a handler. Subscribing the same delegate twice registers it twice.</summary>
        public void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            Type key = typeof(T);
            if (_handlers.TryGetValue(key, out Delegate existing))
            {
                _handlers[key] = Delegate.Combine(existing, handler);
            }
            else
            {
                _handlers[key] = handler;
            }
        }

        /// <summary>
        /// Unregisters a previously added handler. Always call this from <c>OnDisable</c>/<c>OnDestroy</c>;
        /// a bus that outlives its subscribers will otherwise keep them alive and call into destroyed objects.
        /// </summary>
        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            Type key = typeof(T);
            if (!_handlers.TryGetValue(key, out Delegate existing)) return;

            Delegate remaining = Delegate.Remove(existing, handler);
            if (remaining == null)
            {
                // Remove the key entirely so a destroyed subscriber type does not leave an
                // empty entry behind for the lifetime of the bus.
                _handlers.Remove(key);
            }
            else
            {
                _handlers[key] = remaining;
            }
        }

        /// <summary>
        /// Invokes every handler registered for <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>
        /// The delegate snapshot is taken before invocation, so a handler may safely
        /// subscribe or unsubscribe during dispatch without disturbing the current publish.
        /// </remarks>
        public void Publish<T>(T payload)
        {
            if (!_handlers.TryGetValue(typeof(T), out Delegate existing)) return;

            // ReSharper disable once UsePatternMatching -- cast target type is not known at compile time
            Action<T> handlers = existing as Action<T>;
            if (handlers == null) return;

            Delegate[] snapshot = handlers.GetInvocationList();
            for (int i = 0; i < snapshot.Length; i++)
            {
                ((Action<T>)snapshot[i]).Invoke(payload);
            }
        }

        /// <summary>Removes every subscription. Call when tearing down a scene or on hard restart.</summary>
        public void Clear()
        {
            _handlers.Clear();
        }
    }
}
