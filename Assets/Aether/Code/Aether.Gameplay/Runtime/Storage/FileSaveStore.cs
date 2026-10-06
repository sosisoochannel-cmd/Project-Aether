using Aether.Core.Progression;

namespace Aether.Gameplay.Storage
{
    /// <summary>
    /// Player progress on disk: the abilities owned and the world facts established.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the backend <c>ISaveStore</c> was written for. It implements the interface and
    /// nothing more, so <c>GameSession</c> did not have to change to gain persistence: it already
    /// called <c>Store.Save</c> at every point where progress changed.
    /// </para>
    /// <para>
    /// <b>The autosave preference is honoured here, not in gameplay.</b> Turning it off must not
    /// change when the game decides progress happened — it changes whether that decision reaches the
    /// disk. So the calls keep arriving, and this declines the ones that are not explicit.
    /// </para>
    /// </remarks>
    public sealed class FileSaveStore : ISaveStore
    {
        private readonly JsonFileStore _file;

        public FileSaveStore() : this("save.json")
        {
        }

        public FileSaveStore(string fileName)
        {
            _file = new JsonFileStore("Aether", fileName);
        }

        /// <summary>
        /// Whether progress records itself as it happens. The player owns this; the settings service
        /// is what changes it.
        /// </summary>
        public bool AutomaticWrites = true;

        /// <summary>Path progress is kept at.</summary>
        public string Location => _file.Path;

        /// <summary>True when a stored run exists right now.</summary>
        public bool HasStoredProgress => _file.Exists;

        public bool TryLoad(out SaveData data)
        {
            data = null;
            if (!_file.TryRead(out SaveData stored)) return false;

            // Guard the shape rather than trusting the file: a save with a missing section would
            // otherwise null-reference the first system that reads it.
            if (stored.Progression == null || stored.World == null) return false;

            data = stored;
            return true;
        }

        /// <summary>Writes progress, unless the player has turned automatic writes off.</summary>
        /// <remarks>This is the signature <c>ISaveStore</c> declares, and every gameplay call arrives here.</remarks>
        public void Save(SaveData data)
        {
            if (!AutomaticWrites) return;
            Save(data, false);
        }

        /// <summary>Writes progress regardless of the autosave preference.</summary>
        /// <remarks>
        /// The explicit path, used by "Save now" and by the two transitions that must not lose the
        /// player's progress — starting a new game and closing the menu.
        /// </remarks>
        public void Save(SaveData data, bool explicitWrite)
        {
            if (data == null) return;
            if (!explicitWrite && !AutomaticWrites) return;

            _file.Write(data);
        }

        public void Clear()
        {
            _file.Delete();
        }

        public override string ToString()
        {
            return _file.ToString();
        }
    }
}
