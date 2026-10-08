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
            get { return _activeCheckpointId ?? string.Empty; }
            set { _activeCheckpointId = value ?? string.Empty; }
        }

        private List<string> FlagData
        {
            get
            {
                if (_flags == null) _flags = new List<string>();
                return _flags;
            }
        }

        /// <summary>Number of one-shot facts currently satisfied. Useful for completion statistics.</summary>
        public int FlagCount => FlagData.Count;

        /// <summary>Returns true when <paramref name="flagId"/> has been established.</summary>
        public bool IsSet(string flagId)
        {
            if (string.IsNullOrEmpty(flagId)) return false;
            return FlagData.Contains(flagId);
        }

        /// <summary>
        /// Establishes a fact. Returns true only on the first call for a given id, so callers can
        /// gate one-shot presentation (a secret chime, a camera flourish) on the return value.
        /// </summary>
        public bool Set(string flagId)
        {
            if (string.IsNullOrEmpty(flagId)) return false;
            if (FlagData.Contains(flagId)) return false;

            FlagData.Add(flagId);
            return true;
        }

        /// <summary>Clears every fact. Used when starting a new game.</summary>
        public void Reset()
        {
            FlagData.Clear();
            _activeCheckpointId = string.Empty;
        }

        /// <summary>Repairs persisted fields that were absent or null in a document.</summary>
        public void RepairMissingData()
        {
            if (_flags == null) _flags = new List<string>();
            if (_activeCheckpointId == null) _activeCheckpointId = string.Empty;
        }

        /// <summary>Copies this state into <paramref name="destination"/> for save serialisation.</summary>
        public void CopyTo(WorldState destination)
        {
            if (destination == null) throw new System.ArgumentNullException(nameof(destination));

            if (ReferenceEquals(this, destination)) return;
            destination._flags = new List<string>(FlagData);
            destination._activeCheckpointId = ActiveCheckpointId;
        }

        /// <summary>
        /// Every established fact. Exposed for save serialisation and diagnostics; gameplay should
        /// query <see cref="IsSet"/> instead of scanning this.
        /// </summary>
        public IReadOnlyList<string> Flags => FlagData;
    }
}
