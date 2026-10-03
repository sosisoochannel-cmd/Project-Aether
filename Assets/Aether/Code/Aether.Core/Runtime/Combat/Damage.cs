using UnityEngine;

namespace Aether.Core.Combat
{
    /// <summary>
    /// Identifies what a piece of damage represents, so receivers can react selectively
    /// (invulnerability windows, resistances, reaction animations) without parsing numbers.
    /// </summary>
    public enum DamageKind
    {
        /// <summary>Standard contact damage from an enemy attack.</summary>
        Melee = 0,

        /// <summary>Damage from an environmental hazard (thorns, falling, crushing roots).</summary>
        Hazard = 1,
    }

    /// <summary>
    /// A single resolved damage event. Passed by <c>in</c> reference; deliberately a struct so
    /// combat produces no garbage on mobile.
    /// </summary>
    public readonly struct DamageInfo
    {
        /// <summary>Raw damage amount before any receiver-side mitigation.</summary>
        public readonly float Amount;

        /// <summary>
        /// World-space origin of the hit. Knockback is applied <i>away</i> from this point,
        /// which keeps direction correct for both melee and environmental sources.
        /// </summary>
        public readonly Vector2 SourcePosition;

        /// <summary>Horizontal knockback impulse in world units per second. Zero means "no push".</summary>
        public readonly float KnockbackSpeed;

        /// <summary>Upward impulse applied alongside the horizontal knockback.</summary>
        public readonly float LiftSpeed;

        /// <summary>What produced this damage.</summary>
        public readonly DamageKind Kind;

        /// <summary>
        /// The object responsible for the damage, or null for environmental sources.
        /// Never used for gameplay logic — only for attribution in logs and analytics.
        /// </summary>
        public readonly GameObject Source;

        public DamageInfo(
            float amount,
            Vector2 sourcePosition,
            float knockbackSpeed = 0f,
            float liftSpeed = 0f,
            DamageKind kind = DamageKind.Melee,
            GameObject source = null)
        {
            Amount = amount;
            SourcePosition = sourcePosition;
            KnockbackSpeed = knockbackSpeed;
            LiftSpeed = liftSpeed;
            Kind = kind;
            Source = source;
        }

        /// <summary>
        /// Horizontal direction (unit vector) the receiver should be pushed along, derived from
        /// the source position. Defaults to +X for a perfectly vertical hit so knockback is never
        /// zero-length.
        /// </summary>
        public Vector2 KnockbackDirection(Vector2 receiverPosition)
        {
            Vector2 delta = receiverPosition - SourcePosition;
            if (delta.sqrMagnitude < 0.000001f) return Vector2.right;
            return new Vector2(delta.x >= 0f ? 1f : -1f, 0f);
        }
    }

    /// <summary>
    /// Anything that can receive damage: the player, every enemy, the boss, and destructible props.
    /// </summary>
    /// <remarks>
    /// Kept intentionally tiny. Attackers only need to know "can I hurt this" and "hurt it" — they
    /// must not know about health bars, AI states or audio, all of which live behind this interface.
    /// </remarks>
    public interface IDamageable
    {
        /// <summary>False once the receiver has died, so attackers can stop targeting it.</summary>
        bool IsAlive { get; }

        /// <summary>Applies damage. Implementations decide whether the hit lands at all.</summary>
        void TakeDamage(in DamageInfo damage);
    }
}
