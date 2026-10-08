using UnityEngine;

namespace Aether.Core.Progression
{
    /// <summary>
    /// Stable identifiers for traversal / combat abilities the player can acquire.
    /// </summary>
    /// <remarks>
    /// These values are persisted in save data, so <b>never reorder or reuse a value</b>.
    /// Append new abilities at the end. Renaming a member is safe; changing its numeric
    /// value is a save-breaking change.
    /// </remarks>
    public enum AbilityId
    {
        /// <summary>No ability. Used as a "nothing here" sentinel so gates can be unconfigured.</summary>
        None = 0,

        /// <summary>
        /// Rootbind — anchors the player to a designated root anchor point, enabling vertical
        /// traversal, access to root-sealed areas, and limited combat repositioning.
        /// Awarded on defeating the Root Guardian.
        /// </summary>
        Rootbind = 1,
    }

    /// <summary>
    /// Persistent, serialisable progression snapshot: which abilities the player owns and which
    /// one-shot world facts have been established (secrets found, shortcuts opened, boss defeated).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately a plain serialisable class with no Unity object references, so it can be
    /// written to disk by whichever save backend the project adopts (JSON, binary, platform cloud save)
    /// without touching gameplay code. Gameplay only ever reads and mutates this object.
    /// </para>
    /// <para>
    /// <b>Do not</b> add scene/level data here. This holds player progress, not world definition.
    /// </para>
    /// </remarks>
    [System.Serializable]
    public sealed class ProgressionState
    {
        [SerializeField]
        private int[] _abilities = new int[0];

        /// <summary>Returns true when the player owns <paramref name="ability"/>.</summary>
        public bool HasAbility(AbilityId ability)
        {
            if (ability == AbilityId.None) return true;

            int raw = (int)ability;
            int[] owned = AbilityData;
            for (int i = 0; i < owned.Length; i++)
            {
                if (owned[i] == raw) return true;
            }

            return false;
        }

        /// <summary>
        /// Grants an ability. Returns true when this call actually changed state, so callers can
        /// drive the "ability unlocked" presentation only on the first acquisition.
        /// </summary>
        public bool GrantAbility(AbilityId ability)
        {
            if (ability == AbilityId.None) return false;
            if (HasAbility(ability)) return false;

            int[] owned = AbilityData;
            int[] grown = new int[owned.Length + 1];
            System.Array.Copy(owned, grown, owned.Length);
            grown[owned.Length] = (int)ability;
            _abilities = grown;
            return true;
        }

        private int[] AbilityData
        {
            get
            {
                if (_abilities == null) _abilities = new int[0];
                return _abilities;
            }
        }

        /// <summary>Repairs the owned-ability list after deserializing an incomplete document.</summary>
        public void RepairMissingData()
        {
            if (_abilities == null) _abilities = new int[0];
        }

        /// <summary>Clears all progression. Used when starting a new game.</summary>
        public void Reset()
        {
            _abilities = new int[0];
        }

        /// <summary>
        /// Copies progression out of this instance into <paramref name="destination"/>.
        /// Used by the save layer to snapshot state without handing out a live reference.
        /// </summary>
        public void CopyTo(ProgressionState destination)
        {
            if (destination == null) throw new System.ArgumentNullException(nameof(destination));

            if (ReferenceEquals(this, destination)) return;
            destination._abilities = (int[])AbilityData.Clone();
        }
    }
}
