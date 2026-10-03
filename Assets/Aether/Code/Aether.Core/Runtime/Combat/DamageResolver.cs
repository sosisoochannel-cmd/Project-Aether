using UnityEngine;

namespace Aether.Core.Combat
{
    /// <summary>
    /// Turns a physics query result into the <see cref="IDamageable"/> behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attack code should never call <c>GetComponent</c> on a hit result directly. Keeping the
    /// lookup here means the project has exactly one place that knows how damage receivers are
    /// attached to colliders, and it can be changed once rather than at every attack site.
    /// </para>
    /// <para>
    /// Resolution walks to the attached rigidbody first. Characters put their damage receiver on the
    /// body root, while hitboxes, hurtboxes and visual children are separate colliders — so resolving
    /// from the child collider would otherwise be the common case and the slow one.
    /// </para>
    /// </remarks>
    public static class DamageResolver
    {
        /// <summary>
        /// Resolves the damage receiver behind <paramref name="collider"/>.
        /// Returns false when the collider does not belong to something damageable, which is the
        /// normal case for terrain.
        /// </summary>
        public static bool TryResolve(Collider2D collider, out IDamageable damageable)
        {
            damageable = null;
            if (collider == null) return false;

            Rigidbody2D body = collider.attachedRigidbody;
            if (body != null && body.TryGetComponent(out damageable)) return true;

            return collider.TryGetComponent(out damageable);
        }

        /// <summary>
        /// Applies damage to whatever <paramref name="collider"/> belongs to.
        /// Returns true when the damage was delivered to a live receiver.
        /// </summary>
        public static bool TryDamage(Collider2D collider, in DamageInfo damage)
        {
            if (!TryResolve(collider, out IDamageable damageable)) return false;
            if (!damageable.IsAlive) return false;

            damageable.TakeDamage(damage);
            return true;
        }
    }
}
