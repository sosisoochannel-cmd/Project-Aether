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
        /// document says what it is. Older versions are repaired on load; a newer version is kept on
        /// disk but not opened by a build that cannot guarantee its shape.
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

        /// <summary>Repairs sections that were absent in an older or partially written document.</summary>
        /// <remarks>
        /// A newly added section is a migration, not a reason to discard a run. Nested collections
        /// are repaired too, because a JSON <c>null</c> inside an otherwise readable document must
        /// not turn the first lookup into a null-reference exception.
        /// </remarks>
        public void RepairMissingSections()
        {
            if (Progression == null) Progression = new ProgressionState();
            if (World == null) World = new WorldState();
            if (Meta == null) Meta = new SaveMeta();
            if (Collection == null) Collection = new CollectionState();
            if (Characters == null) Characters = new CharacterState();
            if (Achievements == null) Achievements = new AchievementState();

            Progression.RepairMissingData();
            World.RepairMissingData();
            Collection.RepairMissingData();
            Characters.RepairMissingData();
            Achievements.RepairMissingData();

            if (Meta.ChapterId == null) Meta.ChapterId = string.Empty;
            if (Meta.ObjectiveId == null) Meta.ObjectiveId = string.Empty;
        }

        /// <summary>Copies the complete snapshot in, without sharing mutable state.</summary>
        public void CopyFrom(SaveData other)
        {
            if (other == null) throw new System.ArgumentNullException(nameof(other));
            if (ReferenceEquals(this, other)) return;

            RepairMissingSections();
            other.RepairMissingSections();

            other.Progression.CopyTo(Progression);
            other.World.CopyTo(World);
            Collection.CopyFrom(other.Collection);
            Characters.CopyFrom(other.Characters);
            Achievements.CopyFrom(other.Achievements);

            Meta = new SaveMeta
            {
                Slot = other.Meta.Slot,
                ChapterId = other.Meta.ChapterId,
                ObjectiveId = other.Meta.ObjectiveId,
                SavedUtcTicks = other.Meta.SavedUtcTicks,
                PlaySeconds = other.Meta.PlaySeconds,
                Deaths = other.Meta.Deaths,
                Discoveries = other.Meta.Discoveries,
            };
            Version = other.Version;
        }

        /// <summary>Resets this snapshot to a brand-new game.</summary>
        /// <remarks>
        /// The meta is reset too, and then re-stamped: a new game in a slot keeps the slot's number,
        /// because the number is where the file lives rather than something the run owns.
        /// </remarks>
        public void ResetForNewGame()
        {
            RepairMissingSections();
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

        private List<string> FoundData
        {
            get
            {
                if (_found == null) _found = new List<string>();
                return _found;
            }
        }

        /// <summary>Ids of everything found, in the order it was found.</summary>
        public IReadOnlyList<string> Found
        {
            get { return FoundData; }
        }

        /// <summary>How many things have been found.</summary>
        public int Count
        {
            get { return FoundData.Count; }
        }

        /// <summary>Whether an id has been recorded.</summary>
        public bool Has(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return FoundData.Contains(id);
        }

        /// <summary>Records an id once. Returns false when it was already there.</summary>
        public bool Record(string id)
        {
            if (string.IsNullOrEmpty(id) || FoundData.Contains(id)) return false;

            FoundData.Add(id);
            return true;
        }

        /// <summary>Back to nothing found.</summary>
        public void Reset()
        {
            FoundData.Clear();
        }

        /// <summary>Repairs the collection list after deserializing an incomplete document.</summary>
        public void RepairMissingData()
        {
            if (_found == null) _found = new List<string>();
        }

        /// <summary>Copies another snapshot in, field by field.</summary>
        public void CopyFrom(CollectionState other)
        {
            if (ReferenceEquals(this, other)) return;

            FoundData.Clear();
            if (other != null && other._found != null) FoundData.AddRange(other._found);
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

        private List<string> MetData
        {
            get
            {
                if (_met == null) _met = new List<string>();
                return _met;
            }
        }

        /// <summary>Ids of the characters met, in the order they were met.</summary>
        public IReadOnlyList<string> MetIds
        {
            get { return MetData; }
        }

        /// <summary>Whether a character has been met.</summary>
        public bool HasMet(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return MetData.Contains(id);
        }

        /// <summary>Records a meeting once. Returns false when it was already recorded.</summary>
        public bool Record(string id)
        {
            if (string.IsNullOrEmpty(id) || MetData.Contains(id)) return false;

            MetData.Add(id);
            return true;
        }

        /// <summary>Back to having met nobody.</summary>
        public void Reset()
        {
            MetData.Clear();
            SelectedId = string.Empty;
        }

        /// <summary>Repairs character data after deserializing an incomplete document.</summary>
        public void RepairMissingData()
        {
            if (_met == null) _met = new List<string>();
            if (SelectedId == null) SelectedId = string.Empty;
        }

        /// <summary>Copies another snapshot in, field by field.</summary>
        public void CopyFrom(CharacterState other)
        {
            if (ReferenceEquals(this, other)) return;

            MetData.Clear();
            if (other != null && other._met != null) MetData.AddRange(other._met);
            SelectedId = other != null ? other.SelectedId ?? string.Empty : string.Empty;
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

        private List<AchievementRecord> RecordData
        {
            get
            {
                if (_records == null) _records = new List<AchievementRecord>();
                return _records;
            }
        }

        /// <summary>One record per achievement that has been touched, locked or not.</summary>
        public IReadOnlyList<AchievementRecord> Records
        {
            get { return RecordData; }
        }

        /// <summary>The record for an id, or null when nothing has been recorded for it.</summary>
        public AchievementRecord Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            List<AchievementRecord> records = RecordData;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null && records[i].Id == id) return records[i];
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
                List<AchievementRecord> records = RecordData;
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i] != null && records[i].Unlocked) count++;
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
            RecordData.Add(record);
            return record;
        }

        /// <summary>Back to nothing achieved.</summary>
        public void Reset()
        {
            RecordData.Clear();
        }

        /// <summary>Repairs achievement data after deserializing an incomplete document.</summary>
        public void RepairMissingData()
        {
            if (_records == null) _records = new List<AchievementRecord>();
        }

        /// <summary>Copies another snapshot in, field by field and record by record.</summary>
        public void CopyFrom(AchievementState other)
        {
            if (ReferenceEquals(this, other)) return;

            RecordData.Clear();
            if (other == null || other._records == null) return;

            for (int i = 0; i < other._records.Count; i++)
            {
                AchievementRecord source = other._records[i];
                if (source == null) continue;

                RecordData.Add(new AchievementRecord
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

        /// <summary>
        /// Requests an automatic write. True means the request succeeded or was intentionally
        /// suppressed by the player's autosave preference; false means storage failed.
        /// </summary>
        bool Save(SaveData data);

        /// <summary>Writes even when autosave is off. Returns false when the write failed.</summary>
        bool Save(SaveData data, bool explicitWrite);

        /// <summary>Deletes any stored snapshot. Returns false when storage refused the deletion.</summary>
        bool Clear();
    }
}
