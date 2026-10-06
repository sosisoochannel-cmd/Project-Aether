using System.Collections.Generic;

namespace Aether.Core.Progression
{
    /// <summary>
    /// The complete persistent player snapshot: what the player can do (abilities), what has
    /// happened in the world (flags, checkpoints), what they have found (collection), who they have
    /// met (characters) and what they have achieved.
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
        /// <summary>
        /// The shape of this document as it was written.
        /// </summary>
        /// <remarks>
        /// A save file outlives the build that wrote it — that is the whole point of one — so every
        /// document says what it is. Nothing yet reads this, and that is honest: there is one version,
        /// and a version number that is written but never needed is cheaper than discovering later
        /// that no way to tell the shapes apart was ever recorded.
        /// </remarks>
        public const int CurrentVersion = 2;

        /// <summary>What the file was written with. Compared on load, never trusted afterwards.</summary>
        public int Version = CurrentVersion;

        public ProgressionState Progression = new ProgressionState();
        public WorldState World = new WorldState();

        /// <summary>Where this run is and how long it has been played, for the save-slot list.</summary>
        public SaveMeta Meta = new SaveMeta();

        /// <summary>What the player has found, met and achieved.</summary>
        public CollectionState Collection = new CollectionState();
        public CharacterState Characters = new CharacterState();
        public AchievementState Achievements = new AchievementState();

        /// <summary>Resets this snapshot to a brand-new game.</summary>
        /// <remarks>
        /// The meta is reset too, and then re-stamped: a new game in a slot keeps the slot's number,
        /// because the number is where the file lives rather than something the run owns.
        /// </remarks>
        public void ResetForNewGame()
        {
            Progression.Reset();
            World.Reset();
            Collection.Reset();
            Characters.Reset();
            Achievements.Reset();

            int slot = Meta.Slot;
            string chapter = Meta.ChapterId;
            Meta = new SaveMeta { Slot = slot, ChapterId = chapter };
            Version = CurrentVersion;
        }
    }

    /// <summary>
    /// Where a run stands: which chapter, how far along, and how long it has been played.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything here is description rather than progress: the save-slot list has to be able to say
    /// "region one, 42 minutes, 2 of 3 findings, last played on Tuesday" before anything has been
    /// loaded into a session, and that means the numbers have to be in the document itself rather
    /// than derived from a level that is not running.
    /// </para>
    /// <para>
    /// <b>Times are stored as UTC ticks.</b> A player who travels, or whose phone changes time zone
    /// while the app is closed, must not see "last played yesterday" about a run they played an hour
    /// ago, and a file must still sort correctly against another file written in another zone.
    /// </para>
    /// </remarks>
    [System.Serializable]
    public sealed class SaveMeta
    {
        /// <summary>Which save slot this document belongs to. 1-based, matching the interface.</summary>
        public int Slot = 1;

        /// <summary>Stable id of the chapter the run is in, or empty before one has begun.</summary>
        public string ChapterId = string.Empty;

        /// <summary>Stable id of the objective the run is standing on, or empty.</summary>
        public string ObjectiveId = string.Empty;

        /// <summary>When the run was last written, as UTC ticks. 0 when it has never been written.</summary>
        public long SavedUtcTicks;

        /// <summary>How long the run has been played, in seconds.</summary>
        public float PlaySeconds;

        /// <summary>How many times the player has died in this run.</summary>
        public int Deaths;

        /// <summary>How many findings the run has recorded, for the slot's progress line.</summary>
        public int Discoveries;

        /// <summary>True once the run has been written at least once.</summary>
        public bool HasBeenWritten
        {
            get { return SavedUtcTicks > 0L; }
        }
    }

    /// <summary>
    /// What the player has found: the finds recorded in each region, kept for the collection screen.
    /// </summary>
    /// <remarks>
    /// <b>The flags in <see cref="WorldState"/> remain the truth.</b> This is a second view of the
    /// same facts, kept here because the collection screen has to be able to list what exists before
    /// a level is running — and because a list of ids is a much cheaper thing to read on a menu than
    /// a level's worth of world state. Nothing is written here that is not also a world flag, so the
    /// two can never disagree: <see cref="Record"/> is called by the same code that sets the flag.
    /// </remarks>
    [System.Serializable]
    public sealed class CollectionState
    {
        [UnityEngine.SerializeField]
        private List<string> _found = new List<string>();

        /// <summary>Ids of everything found, in the order it was found.</summary>
        public IReadOnlyList<string> Found
        {
            get { return _found; }
        }

        /// <summary>How many things have been found.</summary>
        public int Count
        {
            get { return _found.Count; }
        }

        /// <summary>Whether an id has been recorded.</summary>
        public bool Has(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return _found.Contains(id);
        }

        /// <summary>Records an id once. Returns false when it was already there.</summary>
        public bool Record(string id)
        {
            if (string.IsNullOrEmpty(id) || _found.Contains(id)) return false;

            _found.Add(id);
            return true;
        }

        /// <summary>Back to nothing found.</summary>
        public void Reset()
        {
            _found.Clear();
        }

        /// <summary>Copies another snapshot in, field by field.</summary>
        public void CopyFrom(CollectionState other)
        {
            if (other == null) return;

            _found.Clear();
            if (other._found != null) _found.AddRange(other._found);
        }
    }

    /// <summary>Who the player has met, and who they are playing as.</summary>
    [System.Serializable]
    public sealed class CharacterState
    {
        /// <summary>Characters met in play. The player's own character is not stored: it is always known.</summary>
        [UnityEngine.SerializeField]
        private List<string> _met = new List<string>();

        /// <summary>Id of the selected character, or empty for the default.</summary>
        public string SelectedId = string.Empty;

        /// <summary>Ids of the characters met, in the order they were met.</summary>
        public IReadOnlyList<string> MetIds
        {
            get { return _met; }
        }

        /// <summary>Whether a character has been met.</summary>
        public bool HasMet(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return _met.Contains(id);
        }

        /// <summary>Records a meeting once. Returns false when it was already recorded.</summary>
        public bool Record(string id)
        {
            if (string.IsNullOrEmpty(id) || _met.Contains(id)) return false;

            _met.Add(id);
            return true;
        }

        /// <summary>Back to having met nobody.</summary>
        public void Reset()
        {
            _met.Clear();
            SelectedId = string.Empty;
        }

        /// <summary>Copies another snapshot in, field by field.</summary>
        public void CopyFrom(CharacterState other)
        {
            if (other == null) return;

            _met.Clear();
            if (other._met != null) _met.AddRange(other._met);
            SelectedId = other.SelectedId;
        }
    }

    /// <summary>One achievement as it stands in a run.</summary>
    [System.Serializable]
    public sealed class AchievementRecord
    {
        /// <summary>Stable id of the achievement.</summary>
        public string Id = string.Empty;

        /// <summary>When it was unlocked, as UTC ticks. 0 while it is still locked.</summary>
        public long UnlockedUtcTicks;

        /// <summary>How far along it is, for achievements that count something.</summary>
        public int Progress;

        /// <summary>Whether it has been unlocked.</summary>
        public bool Unlocked
        {
            get { return UnlockedUtcTicks > 0L; }
        }
    }

    /// <summary>What the player has achieved in this run.</summary>
    [System.Serializable]
    public sealed class AchievementState
    {
        [UnityEngine.SerializeField]
        private List<AchievementRecord> _records = new List<AchievementRecord>();

        /// <summary>One record per achievement that has been touched, locked or not.</summary>
        public IReadOnlyList<AchievementRecord> Records
        {
            get { return _records; }
        }

        /// <summary>The record for an id, or null when nothing has been recorded for it.</summary>
        public AchievementRecord Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i] != null && _records[i].Id == id) return _records[i];
            }

            return null;
        }

        /// <summary>Whether an achievement has been unlocked.</summary>
        public bool IsUnlocked(string id)
        {
            AchievementRecord record = Find(id);
            return record != null && record.Unlocked;
        }

        /// <summary>How many achievements have been unlocked.</summary>
        public int UnlockedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _records.Count; i++)
                {
                    if (_records[i] != null && _records[i].Unlocked) count++;
                }

                return count;
            }
        }

        /// <summary>The record for an id, creating it when it does not exist yet.</summary>
        public AchievementRecord Ensure(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            AchievementRecord record = Find(id);
            if (record != null) return record;

            record = new AchievementRecord { Id = id };
            _records.Add(record);
            return record;
        }

        /// <summary>Back to nothing achieved.</summary>
        public void Reset()
        {
            _records.Clear();
        }

        /// <summary>Copies another snapshot in, field by field and record by record.</summary>
        public void CopyFrom(AchievementState other)
        {
            if (other == null) return;

            _records.Clear();
            if (other._records == null) return;

            for (int i = 0; i < other._records.Count; i++)
            {
                AchievementRecord source = other._records[i];
                if (source == null) continue;

                _records.Add(new AchievementRecord
                {
                    Id = source.Id,
                    UnlockedUtcTicks = source.UnlockedUtcTicks,
                    Progress = source.Progress,
                });
            }
        }
    }

    /// <summary>
    /// Storage hook for player progress.
    /// </summary>
    /// <remarks>
    /// This interface exists so gameplay never talks to <c>PlayerPrefs</c>, file paths, or a cloud
    /// SDK directly. <c>JsonFileStore</c> and <c>SaveSlotStore</c> are the backend the project ships;
    /// encryption, versioning or cloud sync would be another implementation of this interface.
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
