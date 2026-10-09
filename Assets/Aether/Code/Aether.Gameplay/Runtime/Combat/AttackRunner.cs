using System;
using System.Collections.Generic;
using Aether.Core.Combat;
using Aether.Data.Config;
using UnityEngine;

namespace Aether.Gameplay.Combat
{
    /// <summary>Phase of an attack timeline.</summary>
    public enum AttackPhase
    {
        /// <summary>No attack in progress.</summary>
        None = 0,

        /// <summary>Wind-up. The hitbox is not yet live; this is the opponent's warning.</summary>
        Startup = 1,

        /// <summary>The hitbox is live.</summary>
        Active = 2,

        /// <summary>Lockout. A chained attack may be accepted here.</summary>
        Recovery = 3,
    }

    /// <summary>
    /// Executes an <see cref="AttackDefinition"/> timeline: wind-up, hitbox, recovery,
    /// hit resolution and combo chaining.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shared by the player and by every enemy archetype. Two divergent attack implementations is
    /// how a project ends up with enemies whose telegraphs do not match the player's, or with a
    /// combo rule that only works for one of them; there is one timeline and every attacker uses it.
    /// </para>
    /// <para>
    /// <b>Each target is hit at most once per attack.</b> A wide hitbox that stays active for several
    /// frames would otherwise deal its damage repeatedly, which is both unfair and the classic source
    /// of "one swing took half my health".
    /// </para>
    /// <para>
    /// Plain C# and Unity-free apart from <see cref="Vector2"/> and collider types, so it is unit
    /// testable without a scene.
    /// </para>
    /// </remarks>
    public sealed class AttackRunner
    {
        private readonly List<Collider2D> _overlapBuffer = new List<Collider2D>(8);
        private readonly HashSet<IDamageable> _targetsHit = new HashSet<IDamageable>();

        private ContactFilter2D _filter;
        private bool _filterBuilt;

        private AttackDefinition _current;
        private AttackPhase _phase = AttackPhase.None;
        private float _phaseElapsed;
        private bool _comboQueued;

        /// <summary>Raised when an attack begins, including a chained follow-up.</summary>
        public event Action<AttackDefinition> AttackStarted;

        /// <summary>
        /// Raised when the hitbox becomes live. This is the precise moment a telegraph ends, so it is
        /// what a presentation layer should hang its "strike" feedback on.
        /// </summary>
        public event Action<AttackDefinition> HitboxActivated;

        /// <summary>Raised once per attack when it finishes, whether or not it connected.</summary>
        public event Action<AttackDefinition> AttackFinished;

        /// <summary>Raised for every collider actually damaged by this attack.</summary>
        public event Action<Collider2D> HitLanded;

        /// <summary>The attack currently executing, or null.</summary>
        public AttackDefinition Current => _current;

        /// <summary>Current phase.</summary>
        public AttackPhase Phase => _phase;

        /// <summary>True while any phase is running.</summary>
        public bool IsRunning => _phase != AttackPhase.None;

        /// <summary>
        /// How far through the wind-up the attack is, on [0, 1]. Exposed so a telegraph can be
        /// animated in step with the timeline rather than guessed at with a separate timer.
        /// </summary>
        public float StartupProgress
        {
            get
            {
                if (_phase != AttackPhase.Startup || _current == null) return 0f;
                float startup = _current.Startup;
                return startup <= 0f ? 1f : Mathf.Clamp01(_phaseElapsed / startup);
            }
        }

        /// <summary>True while a follow-up is buffered and waiting for the combo window.</summary>
        public bool ComboQueued => _comboQueued;

        /// <summary>True when the current attack can still chain and a follow-up is buffered.</summary>
        public bool ComboWillFire =>
            _comboQueued && _current != null && _current.HasFollowUp && _phase == AttackPhase.Recovery;

        /// <summary>
        /// Starts a new attack, or queues a follow-up if one is already running.
        /// Returns true when an attack actually began.
        /// </summary>
        /// <remarks>
        /// A press during wind-up or active frames is queued rather than discarded, so a player
        /// mashing for the second hit during the first hit's animation gets it. That input handling
        /// is identical for enemies, which is why it lives here rather than in the caller.
        /// </remarks>
        public bool RequestAttack(AttackDefinition attack)
        {
            if (attack == null) return false;

            if (_phase == AttackPhase.None)
            {
                Begin(attack);
                return true;
            }

            // Recovery is the only phase where a queued input is still meaningful; queueing during
            // recovery is handled in Tick so that the chain fires as early as it legally can.
            if (_phase != AttackPhase.Recovery) _comboQueued = true;
            else _comboQueued = true;
            return false;
        }

        /// <summary>Advances the timeline. Call once per frame.</summary>
        /// <param name="deltaTime">Frame delta.</param>
        /// <param name="origin">World-space centre the hitbox is positioned from.</param>
        /// <param name="facingSign">+1 or -1; mirrors the hitbox offset.</param>
        /// <param name="targetLayers">Layers this attack can damage.</param>
        /// <param name="selfBody">The attacker's body, so it never damages itself. May be null.</param>
        /// <param name="source">Object credited with the damage. May be null.</param>
        public void Tick(
            float deltaTime,
            Vector2 origin,
            int facingSign,
            LayerMask targetLayers,
            Rigidbody2D selfBody,
            GameObject source)
        {
            if (_phase == AttackPhase.None) return;
            if (_current == null)
            {
                Cancel();
                return;
            }

            _phaseElapsed += deltaTime;

            switch (_phase)
            {
                case AttackPhase.Startup:
                    if (_phaseElapsed >= _current.Startup) EnterActive();
                    break;

                case AttackPhase.Active:
                    ResolveHits(origin, facingSign, targetLayers, selfBody, source);
                    if (_phaseElapsed >= _current.Active) EnterRecovery();
                    break;

                case AttackPhase.Recovery:
                    // The chain fires the moment the queued input can be honoured rather than at the
                    // end of recovery, which is what makes a combo feel like it has no dead frames.
                    if (_comboQueued && _current.HasFollowUp && _phaseElapsed <= _current.ComboInputWindow)
                    {
                        Begin(_current.NextInCombo);
                        break;
                    }

                    if (_phaseElapsed >= _current.Recovery) Cancel();
                    break;
            }
        }

        /// <summary>
        /// Aborts the attack and clears the buffered input. Safe to call when idle.
        /// </summary>
        public void Cancel()
        {
            if (_phase == AttackPhase.None && _current == null) return;

            AttackDefinition finished = _current;
            _phase = AttackPhase.None;
            _phaseElapsed = 0f;
            _comboQueued = false;
            _current = null;
            _targetsHit.Clear();

            if (finished != null) AttackFinished?.Invoke(finished);
        }

        /// <summary>
        /// World-space centre of the current hitbox.
        /// </summary>
        public Vector2 GetHitboxCenter(Vector2 origin, int facingSign)
        {
            if (_current == null) return origin;
            return origin + _current.HitboxOffsetFor(facingSign);
        }

        private void Begin(AttackDefinition attack)
        {
            _current = attack;
            _phase = AttackPhase.Startup;
            _phaseElapsed = 0f;
            _comboQueued = false;
            _targetsHit.Clear();

            AttackStarted?.Invoke(attack);
        }

        private void EnterActive()
        {
            _phase = AttackPhase.Active;
            _phaseElapsed = 0f;

            HitboxActivated?.Invoke(_current);
        }

        private void EnterRecovery()
        {
            _phase = AttackPhase.Recovery;
            _phaseElapsed = 0f;
        }

        private void BuildFilterIfNeeded(LayerMask layers)
        {
            if (_filterBuilt && _filter.layerMask == layers) return;

            _filter = Physics2DQuery.CreateSolidFilter(layers);
            _filterBuilt = true;
        }

        private void ResolveHits(
            Vector2 origin,
            int facingSign,
            LayerMask targetLayers,
            Rigidbody2D selfBody,
            GameObject source)
        {
            BuildFilterIfNeeded(targetLayers);

            Vector2 center = GetHitboxCenter(origin, facingSign);

            int count = Physics2DQuery.OverlapBox(center, _current.HitboxSize, 0f, _filter, _overlapBuffer);
            if (count == 0) return;

            var damage = new DamageInfo(
                _current.Damage,
                origin,
                _current.KnockbackSpeed,
                _current.LiftSpeed,
                DamageKind.Melee,
                source);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _overlapBuffer[i];
                if (hit == null) continue;

                if (selfBody != null && hit.attachedRigidbody == selfBody) continue;

                // A character may have several colliders (body, hurtbox and child hitboxes), but it
                // is one damage receiver. Track the receiver, not the collider, so one swing cannot
                // deal repeated damage just because the overlap touched two of its shapes.
                if (!DamageResolver.TryResolve(hit, out IDamageable target)) continue;
                if (!_targetsHit.Add(target)) continue;

                if (DamageResolver.TryDamage(hit, damage)) HitLanded?.Invoke(hit);
            }
        }
    }
}
