using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aether.Core.Pooling
{
    /// <summary>
    /// Optional callbacks invoked as an object enters and leaves the pool.
    /// </summary>
    /// <remarks>
    /// Implement this on pooled components to reset transient state (velocities, timers, trail
    /// renderers) so a recycled object behaves exactly like a fresh one. Forgetting to reset is the
    /// classic pooling bug; the interface makes the requirement visible on the component itself.
    /// </remarks>
    public interface IPooledObject
    {
        /// <summary>Called immediately after the object is handed out by <c>Get</c>.</summary>
        void OnTakenFromPool();

        /// <summary>Called immediately before the object is returned to the pool.</summary>
        void OnReturnedToPool();
    }

    /// <summary>
    /// A reusable pool of components, built to keep steady-state gameplay allocation-free.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Android is the primary target, and repeated <c>Instantiate</c>/<c>Destroy</c> during combat
    /// causes GC spikes that surface as frame hitching on low-end devices. Anything spawned more than
    /// a handful of times per encounter (projectiles, hit effects, breakable debris, and in some
    /// cases enemies) should come from a pool.
    /// </para>
    /// <para>
    /// The pool is <b>not</b> thread-safe and is intended to be owned by a single manager object.
    /// </para>
    /// </remarks>
    public sealed class ComponentPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _parent;
        private readonly Stack<T> _available;
        private readonly HashSet<T> _allInstances = new HashSet<T>();
        private readonly int _maxSize;

        private int _totalCreated;

        /// <summary>
        /// Creates a pool for <paramref name="prefab"/>.
        /// </summary>
        /// <param name="prefab">Prefab to clone. Must not be null.</param>
        /// <param name="parent">Optional parent for pooled instances; keeps the hierarchy readable.</param>
        /// <param name="prewarm">Instances created up front, during load, to avoid first-use hitching.</param>
        /// <param name="maxSize">
        /// Hard ceiling on live instances. When reached, <see cref="Get"/> returns null rather than
        /// growing without bound — a runaway spawn loop then degrades visibly instead of exhausting memory.
        /// </param>
        public ComponentPool(T prefab, Transform parent = null, int prewarm = 0, int maxSize = 128)
        {
            _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
            _parent = parent;
            _maxSize = Mathf.Max(1, maxSize);
            _available = new Stack<T>(Mathf.Max(4, Mathf.Min(prewarm, _maxSize)));

            // Prewarming obeys the same hard ceiling as runtime creation.
            for (int i = 0; i < Mathf.Min(prewarm, _maxSize); i++)
            {
                T instance = CreateInstance();
                if (instance == null) break;
                instance.gameObject.SetActive(false);
                _available.Push(instance);
            }
        }

        /// <summary>Number of instances currently checked in.</summary>
        public int AvailableCount => _available.Count;

        /// <summary>Total instances ever created by this pool. Useful for diagnostics.</summary>
        public int TotalCreated => _totalCreated;

        /// <summary>
        /// Takes an instance from the pool, creating one if the pool is empty and under the cap.
        /// Returns null when the cap has been reached.
        /// </summary>
        public T Get()
        {
            T instance;
            if (_available.Count > 0)
            {
                instance = _available.Pop();
            }
            else
            {
                if (_totalCreated >= _maxSize) return null;
                instance = CreateInstance();
                if (instance == null) return null;
            }

            GameObject go = instance.gameObject;
            if (!go.activeSelf) go.SetActive(true);

            if (instance is IPooledObject pooled) pooled.OnTakenFromPool();
            return instance;
        }

        /// <summary>
        /// Takes an instance and places it in world space.
        /// </summary>
        /// <remarks>
        /// The returned object's rotation is <b>not</b> set, because turning a sprite to face its
        /// travel direction is a presentation decision that belongs to the spawner, not the pool.
        /// </remarks>
        public T Get(Vector3 position)
        {
            T instance = Get();
            if (instance == null) return null;

            instance.transform.position = position;
            return instance;
        }

        /// <summary>Returns an instance to the pool. Null and duplicate returns are ignored.</summary>
        public void Release(T instance)
        {
            if (instance == null) return;

            // Only objects created by this pool may be returned. Accepting an unrelated component
            // would bypass the creation ceiling and could put the same object in two pools.
            if (!_allInstances.Contains(instance)) return;

            // Guard against the same instance being returned twice, which would hand the same
            // object to two callers later. Cheap because the pool is small.
            if (_available.Contains(instance)) return;

            if (instance is IPooledObject pooled) pooled.OnReturnedToPool();

            GameObject go = instance.gameObject;
            if (go.activeSelf) go.SetActive(false);
            if (_parent != null) instance.transform.SetParent(_parent, false);

            _available.Push(instance);
        }

        /// <summary>
        /// Destroys every pooled instance. Call when tearing down the owning scene or region,
        /// so pooled objects cannot leak between scenes during a load transition.
        /// </summary>
        public void Dispose()
        {
            foreach (T instance in _allInstances)
            {
                if (instance == null) continue;

                if (Application.isPlaying) UnityEngine.Object.Destroy(instance.gameObject);
                else UnityEngine.Object.DestroyImmediate(instance.gameObject);
            }

            _available.Clear();
            _allInstances.Clear();
            _totalCreated = 0;
        }

        private T CreateInstance()
        {
            if (_prefab == null) return null;

            T instance = UnityEngine.Object.Instantiate(_prefab, _parent);
            if (instance == null) return null;

            _totalCreated++;
            _allInstances.Add(instance);
            return instance;
        }
    }
}
