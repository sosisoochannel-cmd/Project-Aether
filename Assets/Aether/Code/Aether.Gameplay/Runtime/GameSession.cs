using System;
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
    /// <b>Persistence is not implemented.</b> <see cref="Store"/> is the hook a later save layer plugs
    /// into. Nothing in this region writes to disk, which is deliberate: shipping a save format before
    /// the region's progression is final would guarantee a migration.
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

            RequestSave();
            return true;
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
        public void RequestSave()
        {
            if (Store == null) return;

            try
            {
                Store.Save(Save);
            }
            catch (Exception ex)
            {
                // A failed save must never take the game down mid-play. Surface it and carry on.
                Debug.LogException(ex, this);
                SaveFailed?.Invoke(ex);
            }
        }

        /// <summary>
        /// Replaces the current snapshot with one loaded from <see cref="Store"/>.
        /// Returns false when there was nothing to load.
        /// </summary>
        public bool TryLoadFromStore()
        {
            if (Store == null) return false;
            if (!Store.TryLoad(out SaveData loaded) || loaded == null) return false;

            loaded.Progression.CopyTo(Save.Progression);
            loaded.World.CopyTo(Save.World);
            return true;
        }

        /// <summary>Discards all progress and starts a fresh session.</summary>
        public void StartNewGame(bool clearStore)
        {
            Save.ResetForNewGame();

            if (clearStore && Store != null)
            {
                try
                {
                    Store.Clear();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }
            }
        }
    }
}
