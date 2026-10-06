using Aether.Core.Settings;

namespace Aether.Gameplay.Storage
{
    /// <summary>
    /// The player's settings, as one JSON document under the platform's persistent data path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A file rather than <c>PlayerPrefs</c>, for three reasons that matter more than the extra
    /// thirty lines: the whole settings object arrives and leaves in one document, so it can be
    /// copied out of a phone in one piece when a player reports a bug; a version number travels with
    /// it, so a future format can migrate instead of guessing; and nothing anywhere else in the
    /// project can write a settings value, because there is no global key space to write into.
    /// </para>
    /// <para>
    /// The version is checked here rather than in the service because it is a property of the
    /// document, not of the game: a file from a newer build is not migrated, it is declined, and the
    /// player starts on defaults rather than on a half-understood mixture. The file is left where it
    /// is so a later build can still upgrade it.
    /// </para>
    /// </remarks>
    public sealed class FileSettingsStore : ISettingsStore
    {
        private readonly JsonFileStore _file;

        public FileSettingsStore() : this("settings.json")
        {
        }

        public FileSettingsStore(string fileName)
        {
            _file = new JsonFileStore("Aether", fileName);
        }

        /// <summary>Path the values are kept at, for a diagnostics or support line.</summary>
        public string Location => _file.Path;

        public bool Exists => _file.Exists;

        public bool TryLoad(out GameSettings settings)
        {
            settings = null;
            if (!_file.TryRead(out GameSettings stored)) return false;

            // A newer format is not read: its fields mean things this build does not know, and a
            // partial understanding of them is worse than defaults.
            if (stored.Version != GameSettings.CurrentVersion) return false;

            // An older format has no fields this build lacks, so JsonUtility's defaults for the new
            // ones are already the right answer; the version is simply brought forward.
            stored.Version = GameSettings.CurrentVersion;
            stored.Clamp();
            settings = stored;
            return true;
        }

        public void Save(GameSettings settings)
        {
            if (settings == null) return;
            settings.Version = GameSettings.CurrentVersion;
            _file.Write(settings);
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
