using System.Collections.Generic;
using UnityEngine;

namespace Aether.Core.Progression
{
    /// <summary>
    /// Persistent one-shot world facts: which secrets were found, which shortcuts were opened,
    /// which gates were unsealed, which checkpoints were activated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Facts are keyed by <b>stable string ids authored in level data</b> (for example
    /// <c>secret.greenway.hollow</c>) rather than by enum members. Level content must be authorable
    /// without touching code, and a new secret in a future region must not require a code change.
    /// Stability is enforced by the project verifier, which rejects duplicate or dangling ids.
    /// </para>
    /// <para>
    /// String ids are only compared at load/unlock time, never per frame, so the cost is irrelevant
    /// even on low-end Android hardware.
    /// </para>
    /// </remarks>
    [System.Serializable]
    public sealed class WorldState
    {
        [SerializeField]
        private List<string> _flags = new List<string>();

        [SerializeField]
        private string _activeCheckpointId = string.Empty;

        /// <summary>Id of the checkpoint the player will respawn at. Empty means "region start".</summary>
        public string ActiveCheckpointId
        {
            get => _activeCheckpointId;
            set => _activeCheckpointId = value ?? string.Empty;
        }

        /// <summary>Number of one-shot facts currently satisfied. Useful for completion statistics.</summary>
        public int FlagCount => _flags.Count;

        /// <summary>Returns true when <paramref name="flagId"/> has been established.</summary>
        public bool IsSet(string flagId)
        {
            if (string.IsNullOrEmpty(flagId)) return false;
            return _flags.Contains(flagId);
        }

        /// <summary>
        /// Establishes a fact. Returns true only on the first call for a given id, so callers can
        /// gate one-shot presentation (a secret chime, a camera flourish) on the return value.
        /// </summary>
        public bool Set(string flagId)
        {
            if (string.IsNullOrEmpty(flagId)) return false;
            if (_flags.Contains(flagId)) return false;

            _flags.Add(flagId);
            return true;
        }

        /// <summary>Clears every fact. Used when starting a new game.</summary>
        public void Reset()
        {
            _flags.Clear();
            _activeCheckpointId = string.Empty;
        }

        /// <summary>Copies this state into <paramref name="destination"/> for save serialisation.</summary>
        public void CopyTo(WorldState destination)
        {
            if (destination == null) throw new System.ArgumentNullException(nameof(destination));

            destination._flags = new List<string>(_flags);
            destination._activeCheckpointId = _activeCheckpointId;
        }

        /// <summary>
        /// Every established fact. Exposed for save serialisation and diagnostics; gameplay should
        /// query <see cref="IsSet"/> instead of scanning this.
        /// </summary>
        public IReadOnlyList<string> Flags => _flags;
    }
}
