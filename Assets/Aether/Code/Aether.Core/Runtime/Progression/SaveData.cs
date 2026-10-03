namespace Aether.Core.Progression
{
    /// <summary>
    /// The complete persistent player snapshot: what the player can do (abilities) and what has
    /// happened in the world (flags, checkpoints).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This type is intentionally free of Unity object references and file I/O so that the storage
    /// backend remains an implementation detail. A future save layer writes this object to JSON,
    /// binary, or a platform cloud-save API without gameplay code changing.
    /// </para>
    /// <para>
    /// Scene objects must never hold a long-lived reference to a loaded <see cref="SaveData"/> and
    /// mutate it directly; they should go through the game session so that change notification and
    /// autosave triggers have a single choke point.
    /// </para>
    /// </remarks>
    [System.Serializable]
    public sealed class SaveData
    {
        public ProgressionState Progression = new ProgressionState();
        public WorldState World = new WorldState();

        /// <summary>Resets this snapshot to a brand-new game.</summary>
        public void ResetForNewGame()
        {
            Progression.Reset();
            World.Reset();
        }
    }

    /// <summary>
    /// Storage hook for player progress.
    /// </summary>
    /// <remarks>
    /// This interface exists so gameplay never talks to <c>PlayerPrefs</c>, file paths, or a cloud
    /// SDK directly. The concrete backend (and any encryption, versioning or cloud sync) is a
    /// separate deliverable; nothing in this milestone implements persistence to disk.
    /// </remarks>
    public interface ISaveStore
    {
        /// <summary>Loads a snapshot. Returns false when no save exists or it could not be read.</summary>
        bool TryLoad(out SaveData data);

        /// <summary>Writes a snapshot. Implementations must not mutate the supplied object.</summary>
        void Save(SaveData data);

        /// <summary>Deletes any stored snapshot.</summary>
        void Clear();
    }
}
