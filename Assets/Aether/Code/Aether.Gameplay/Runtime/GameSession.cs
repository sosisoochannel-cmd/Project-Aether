using System;
using System.IO;
using Aether.Core.Events;
using Aether.Core.Progression;
using UnityEngine;

namespace Aether.Gameplay
{
    /// <summary>
    /// Owns the live player snapshot for a play session and is the single entry point for changing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Progression is mutated in exactly one place. Scattered call sites writing directly to
    /// <see cref="ProgressionState"/> would make autosave triggers, "first time" presentation and
    /// debugging impossible to reason about; funnelling them through named operations means the
    /// definition of "the player just unlocked something" lives in one method.
    /// </para>
    /// <para>
    /// The session owns the complete snapshot; its selected <see cref="ISaveStore"/> loads and writes
    /// that snapshot. Automatic requests obey the autosave preference, while an explicit write is a
    /// separate operation owned by the pause and data screens.
    /// </para>
    /// <para>
    /// There is exactly one session per running game. It is created by the boot flow and is not
    /// destroyed on region loads.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    public sealed class GameSession : MonoBehaviour
    {
        private static GameSession _instance;

        /// <summary>The active session, or null before boot has run.</summary>
        public static GameSession Instance => _instance;

        /// <summary>Raised when <see cref="Store"/> saves, so failure can be surfaced to the player.</summary>
        public event Action<Exception> SaveFailed;

        /// <summary>Low-frequency cross-system signals. See <see cref="EventBus"/> for usage rules.</summary>
        public EventBus Events { get; } = new EventBus();

        /// <summary>The complete persistent snapshot for this session.</summary>
        public SaveData Save { get; } = new SaveData();

        /// <summary>Shorthand for <c>Save.Progression</c>.</summary>
        public ProgressionState Progression => Save.Progression;

        /// <summary>Shorthand for <c>Save.World</c>.</summary>
        public WorldState World => Save.World;

        /// <summary>
        /// Storage backend hook. Null until a save layer is installed; callers must tolerate that,
        /// which is why <see cref="RequestSave"/> is a no-op rather than an error.
        /// </summary>
        /// <summary>The whole snapshot, including the run's own record.</summary>
        /// <remarks>
        /// <see cref="Save"/> is the same object; this name exists because half of gameplay reads it
        /// for the collection and the achievements rather than for progression, and two names for
        /// the same field used consistently is clearer than one used for two jobs.
        /// </remarks>
        public SaveData Snapshot
        {
            get { return Save; }
        }

        /// <summary>What the run has found, met and achieved.</summary>
        public CollectionState Collection
        {
            get { return Save.Collection; }
        }

        /// <summary>Who the run has met.</summary>
        public CharacterState Characters
        {
            get { return Save.Characters; }
        }

        /// <summary>What the run has achieved.</summary>
        public AchievementState Achievements
        {
            get { return Save.Achievements; }
        }

        public ISaveStore Store { get; set; }

        /// <summary>True when a storage backend has been installed.</summary>
        public bool HasPersistence => Store != null;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogError(
                    $"A second {nameof(GameSession)} was created on '{name}'. Only one session may " +
                    "exist per running game; the duplicate has been disabled.",
                    this);
                enabled = false;
                return;
            }

            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            Events.Clear();
        }

        /// <summary>
        /// Grants an ability. Returns true only on first acquisition, so callers can gate the unlock
        /// presentation without tracking that themselves.
        /// </summary>
        public bool GrantAbility(AbilityId ability, Vector2 position)
        {
            if (!Progression.GrantAbility(ability)) return false;

            Events.Publish(new AbilityUnlockedEvent(ability, position));
            RequestSave();
            return true;
        }

        /// <summary>Records a one-shot world fact. Returns true only the first time.</summary>
        public bool SetWorldFlag(string flagId)
        {
            if (!World.Set(flagId)) return false;

            Events.Publish(new WorldFlagSetEvent(flagId));
            RequestSave();
            return true;
        }

        /// <summary>
        /// Records a find: the world fact, the collection entry, and the event, once.
        /// </summary>
        /// <remarks>
        /// The two records are written in one place on purpose. The world flag is what makes the
        /// find stay found; the collection entry is what the collection screen lists; and a find
        /// that existed in one and not the other would be a screen that lies about a level. Public
        /// so a discovery trigger — and a test — can record a find without knowing either rule.
        /// </remarks>
        public bool RecordFinding(string flagId, string collectionId)
        {
            if (string.IsNullOrEmpty(flagId)) return false;

            bool recorded = Collection.Record(string.IsNullOrEmpty(collectionId) ? flagId : collectionId);
            bool set = SetWorldFlag(flagId);

            // SetWorldFlag already saves when the flag is new. If repairing an older snapshot whose
            // flag exists but whose collection entry does not, the collection still needs its own
            // write or the menu would keep losing the recovered finding.
            if (recorded && !set) RequestSave();

            return recorded || set;
        }

        /// <summary>Records a newly met character, announces it and asks the save layer to persist it.</summary>
        /// <returns>True only the first time this character is met in the stored run.</returns>
        public bool RecordCharacterMet(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return false;
            if (Save.Characters == null) Save.Characters = new CharacterState();
            if (!Save.Characters.Record(characterId)) return false;

            Events.Publish(new CharacterMetEvent(characterId));
            RequestSave();
            return true;
        }

        /// <summary>Counts a death in this run and announces it.</summary>
        /// <remarks>
        /// The count lives in the run's own record rather than in a counter somewhere in the scene,
        /// because a run that ends and is loaded again must still remember how it went.
        /// </remarks>
        public void RecordDeath()
        {
            if (Save.Meta == null) Save.Meta = new SaveMeta();

            Save.Meta.Deaths++;
            Events.Publish(new PlayerDiedEvent(Save.Meta.Deaths));
            RequestSave();
        }

        /// <summary>Notes that the player is standing in a region, and which chapter it belongs to.</summary>
        /// <param name="chapterId">Stable chapter id from <c>ChapterCatalog</c>.</param>
        /// <param name="objectiveId">Stable objective id the region starts on, or empty.</param>
        public void NoteRegionEntered(string chapterId, string objectiveId)
        {
            if (Save.Meta == null) Save.Meta = new SaveMeta();

            bool changed = Save.Meta.ChapterId != chapterId;
            Save.Meta.ChapterId = chapterId ?? string.Empty;
            if (!string.IsNullOrEmpty(objectiveId)) Save.Meta.ObjectiveId = objectiveId;

            Events.Publish(new RegionEnteredEvent(Save.Meta.ChapterId));
            RequestSave();
            if (changed) Events.Publish(new WorldFlagSetEvent("chapter.entered." + Save.Meta.ChapterId));
        }

        /// <summary>Updates how long this run has been played, in seconds.</summary>
        /// <remarks>
        /// Called by the gameplay clock rather than by a timer here: gameplay pauses, and a run's
        /// time should stop while it is paused.
        /// </remarks>
        public void AddPlayTime(float seconds)
        {
            if (seconds <= 0f) return;
            if (Save.Meta == null) Save.Meta = new SaveMeta();

            Save.Meta.PlaySeconds += seconds;
        }

        /// <summary>
        /// Records a checkpoint as the player's respawn point and refreshes it.
        /// </summary>
        public void ActivateCheckpoint(string checkpointId, Vector2 respawnPosition)
        {
            World.ActiveCheckpointId = checkpointId;
            Events.Publish(new CheckpointActivatedEvent(checkpointId, respawnPosition));
            RequestSave();
        }

        /// <summary>
        /// Asks the storage backend to persist. Silently does nothing when no backend is installed,
        /// so gameplay code can call this unconditionally.
        /// </summary>
        public bool RequestSave()
        {
            if (Store == null) return false;

            try
            {
                if (Store.Save(Save)) return true;

                var failure = new IOException("The automatic save could not be written.");
                Debug.LogException(failure, this);
                SaveFailed?.Invoke(failure);
                return false;
            }
            catch (Exception ex)
            {
                // A failed save must never take the game down mid-play. Surface it and carry on.
                Debug.LogException(ex, this);
                SaveFailed?.Invoke(ex);
                return false;
            }
        }

        /// <summary>
        /// Replaces the current snapshot with one loaded from <see cref="Store"/>.
        /// Returns false when there was nothing to load.
        /// </summary>
        public bool TryLoadFromStore()
        {
            if (Store == null) return false;

            try
            {
                if (!Store.TryLoad(out SaveData loaded) || loaded == null) return false;
                loaded.RepairMissingSections();
                Save.CopyFrom(loaded);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
                return false;
            }
        }

        /// <summary>Discards all progress and starts a fresh session.</summary>
        /// <returns>False when clearing the current stored document failed.</returns>
        public bool StartNewGame(bool clearStore)
        {
            if (clearStore && Store != null)
            {
                try
                {
                    if (!Store.Clear()) return false;
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                    return false;
                }
            }

            Save.ResetForNewGame();
            return true;
        }
    }
}
