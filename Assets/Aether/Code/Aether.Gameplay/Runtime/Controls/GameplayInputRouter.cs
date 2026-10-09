using System.Collections.Generic;
using UnityEngine;

namespace Aether.Gameplay.Controls
{
    /// <summary>
    /// Merges every input source on the player into the one command stream gameplay reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Several sources can be live at once — a phone with a gamepad attached, or the Editor with a
    /// touchscreen being simulated — so this does not pick a winner. It merges: the largest
    /// movement vector wins, and a press from any source counts. Consuming clears the press on
    /// every source, because "the player pressed attack" is true or false for the player, not for a
    /// device.
    /// </para>
    /// <para>
    /// The device source (keyboard and gamepad) is discovered on the same GameObject; the touch
    /// source is registered by whatever builds the on-screen controls. Nothing is polled per frame
    /// here: each source refreshes itself, and this class only combines.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GameplayInputRouter : MonoBehaviour, IGameplayInput
    {
        private const int MaxSources = 4;

        private readonly List<IGameplayInput> _sources = new List<IGameplayInput>(MaxSources);
        private bool _enabled = true;

        /// <summary>Adds a source. Sources are merged, never chosen between.</summary>
        public void AddSource(IGameplayInput source)
        {
            if (source == null || _sources.Contains(source)) return;
            if (_sources.Count >= MaxSources)
            {
                Debug.LogError(
                    $"{nameof(GameplayInputRouter)} already has {MaxSources} sources; " +
                    "increase MaxSources rather than silently dropping input.", this);
                return;
            }

            source.Enabled = _enabled;
            _sources.Add(source);
        }

        /// <summary>Removes a source, for example when a touch overlay is destroyed.</summary>
        public void RemoveSource(IGameplayInput source)
        {
            if (source == null) return;
            _sources.Remove(source);
        }

        /// <summary>The device source on this GameObject, if one has been added.</summary>
        public bool HasDeviceSource
        {
            get
            {
                for (int i = 0; i < _sources.Count; i++)
                {
                    if (_sources[i] is Player.PlayerInputReader) return true;
                }
                return false;
            }
        }

        /// <inheritdoc />
        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                for (int i = 0; i < _sources.Count; i++) _sources[i].Enabled = value;
            }
        }

        /// <inheritdoc />
        public Vector2 Move
        {
            get
            {
                if (!_enabled) return Vector2.zero;

                Vector2 best = Vector2.zero;
                float bestSqr = 0f;
                for (int i = 0; i < _sources.Count; i++)
                {
                    float sqr = _sources[i].Move.sqrMagnitude;
                    if (sqr <= bestSqr) continue;
                    bestSqr = sqr;
                    best = _sources[i].Move;
                }
                return best;
            }
        }

        /// <inheritdoc />
        public bool JumpHeld => _enabled && Any(source => source.JumpHeld);

        /// <inheritdoc />
        public bool JumpPressed => _enabled && Any(source => source.JumpPressed);

        /// <inheritdoc />
        public bool AttackPressed => _enabled && Any(source => source.AttackPressed);

        /// <inheritdoc />
        public bool DodgePressed => _enabled && Any(source => source.DodgePressed);

        /// <inheritdoc />
        public void ConsumeJump() => All(source => source.ConsumeJump());

        /// <inheritdoc />
        public void ConsumeAttack() => All(source => source.ConsumeAttack());

        /// <inheritdoc />
        public void ConsumeDodge() => All(source => source.ConsumeDodge());

        private delegate bool SourcePredicate(IGameplayInput source);

        private delegate void SourceAction(IGameplayInput source);

        private bool Any(SourcePredicate predicate)
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                if (predicate(_sources[i])) return true;
            }
            return false;
        }

        private void All(SourceAction action)
        {
            for (int i = 0; i < _sources.Count; i++) action(_sources[i]);
        }
    }
}
