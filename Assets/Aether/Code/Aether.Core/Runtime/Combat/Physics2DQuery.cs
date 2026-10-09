using System.Collections.Generic;
using UnityEngine;

namespace Aether.Core.Combat
{
    /// <summary>
    /// The single place in the project where 2D physics overlap queries are issued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every gameplay query funnels through here for three reasons:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>No per-call allocation.</b> Queries write into caller-owned buffers that are reused across
    /// frames, which is what keeps combat free of GC spikes on Android.
    /// </description></item>
    /// <item><description>
    /// <b>One place to change.</b> Layer filtering, trigger handling and query flags are decided here
    /// rather than being re-derived at twenty call sites with subtly different settings.
    /// </description></item>
    /// <item><description>
    /// <b>Testability.</b> Gameplay code depends on a named operation ("what am I standing on")
    /// rather than on a particular engine call signature.
    /// </description></item>
    /// </list>
    /// <para>
    /// All buffers used by gameplay should be created once (typically in <c>Awake</c>) and passed in
    /// on every call. Creating a <see cref="List{T}"/> per query defeats the point of this class.
    /// </para>
    /// </remarks>
    public static class Physics2DQuery
    {
        // Shared because Unity runs these gameplay queries on its main thread. A one-result buffer
        // avoids allocations while letting ContactFilter2D explicitly ignore triggers regardless
        // of the project-wide Physics2D.queriesHitTriggers setting.
        private static readonly Collider2D[] SingleHitBuffer = new Collider2D[1];

        /// <summary>
        /// Builds a reusable filter that ignores trigger colliders — the correct default for
        /// movement, grounding and hit detection, since triggers are the physics engine's mechanism
        /// for "something happened here", not "something is solid".
        /// </summary>
        public static ContactFilter2D CreateSolidFilter(LayerMask layers)
        {
            ContactFilter2D filter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = true,
                layerMask = layers,
                useDepth = false,
            };
            return filter;
        }

        /// <summary>
        /// Returns true when any solid collider on <paramref name="layers"/> overlaps the box.
        /// </summary>
        /// <remarks>
        /// This is the grounding probe. It answers a yes/no question, so a single-result query is
        /// enough and an enumeration buffer would be wasted work — it runs every physics step for
        /// every character in the scene.
        /// </remarks>
        public static bool OverlapsBox(Vector2 center, Vector2 size, float angle, LayerMask layers)
        {
            ContactFilter2D filter = CreateSolidFilter(layers);
            return Physics2D.OverlapBox(center, size, angle, filter, SingleHitBuffer) > 0;
        }

        /// <summary>
        /// Collects solid colliders on <paramref name="layers"/> that intersect the box into
        /// <paramref name="results"/>, which is cleared first. Returns the number of hits.
        /// </summary>
        /// <remarks>
        /// Used for attack hit detection. The caller owns <paramref name="results"/> and reuses it,
        /// so this performs no allocation once the buffer has grown to its high-water mark.
        /// </remarks>
        public static int OverlapBox(Vector2 center, Vector2 size, float angle, in ContactFilter2D filter, List<Collider2D> results)
        {
            if (results == null) return 0;

            results.Clear();
            return Physics2D.OverlapBox(center, size, angle, filter, results);
        }
    }
}
