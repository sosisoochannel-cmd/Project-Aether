using Aether.Core.Progression;
using Aether.Gameplay.Settings;
using Aether.Gameplay.Storage;
using UnityEngine;

namespace Aether.Gameplay.Progression
{
    /// <summary>
    /// The one place the game decides that there is a session, where progress is stored, and when a
    /// new game begins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GameSession</c> owns the snapshot and its rules; this owns the lifetime. Before it existed
    /// there were two ways to get a session — the level bootstrap made one when it found none — and
    /// nothing installed a store at all, which is why progress could not survive a restart. Both the
    /// main menu and the region now ask for a session here, so "is there a save", "start a new game"
    /// and "write this down" all have one answer.
    /// </para>
    /// <para>
    /// <b>The store is created without a session.</b> The menu has to know whether a stored run
    /// exists <i>before</i> it builds anything, and asking that question must not drag a session into
    /// being — a menu on a device that is about to be closed should cost nothing.
    /// </para>
    /// </remarks>
    public static class SaveHost
    {
        private static FileSaveStore _store;

        /// <summary>The store, created on first ask.</summary>
        public static FileSaveStore Store
        {
            get
            {
                if (_store != null) return _store;

                _store = new FileSaveStore();
                _store.AutomaticWrites = AetherSettings.Ensure().Values.Gameplay.Autosave;
                return _store;
            }
        }

        /// <summary>True when a stored run exists on disk right now.</summary>
        public static bool HasStoredProgress => Store.HasStoredProgress;

        /// <summary>Where progress is kept, for the Data/Save screen and for support.</summary>
        public static string Location => Store.Location;

        /// <summary>
        /// The session for this run, created and loaded from disk if this is the first ask.
        /// </summary>
        /// <remarks>
        /// Loading happens here rather than at every read: a session is created once per scene load
        /// and it is fresh, so it is loaded once. A second ask in the same scene returns the same
        /// session and touches no file.
        /// </remarks>
        public static GameSession Ensure()
        {
            GameSession session = GameSession.Instance;
            if (session != null) return session;

            var host = new GameObject("GameSession");
            session = host.AddComponent<GameSession>();
            session.Store = Store;
            session.TryLoadFromStore();
            return session;
        }

        /// <summary>
        /// Asks for a session without creating one. Null when nothing has booted yet.
        /// </summary>
        /// <remarks>
        /// Used by the Data/Save screen, which must be able to show "no session yet" rather than
        /// quietly creating one to have something to display.
        /// </remarks>
        public static GameSession Existing => GameSession.Instance;

        /// <summary>
        /// Writes progress now, whatever the autosave preference says.
        /// </summary>
        /// <remarks>
        /// The explicit half of the autosave setting. It is what "Save now" calls, and what the menu
        /// calls before it hands the app over to a region — the one moment where a write is worth
        /// spending on a transition that is about to load a scene anyway.
        /// </remarks>
        public static bool SaveNow()
        {
            GameSession session = GameSession.Instance;
            if (session == null) return false;

            Store.Save(session.Save, true);
            return true;
        }

        /// <summary>Re-reads the autosave preference. Called when that setting changes.</summary>
        public static void ApplySettings()
        {
            if (_store == null) return;
            _store.AutomaticWrites = AetherSettings.Ensure().Values.Gameplay.Autosave;
        }

        /// <summary>
        /// Begins a new game: the stored run is deleted and the session is reset.
        /// </summary>
        /// <remarks>
        /// Destructive, and therefore only ever reached from a confirmed action in the interface.
        /// The session is created if there is none, so this is safe to call from the menu before a
        /// region has ever run.
        /// </remarks>
        public static GameSession BeginNewGame()
        {
            GameSession session = Ensure();

            // clearStore: true — the run on disk is the old one, and leaving it would let a reset
            // be undone by closing the app before the next autosave.
            session.StartNewGame(true);
            Store.Save(session.Save, true);
            return session;
        }

        /// <summary>Forgets the store and session. Used by tests, which must not touch real files.</summary>
        public static void ResetForTests(GameSession session)
        {
            if (session != null) Object.DestroyImmediate(session.gameObject);
            _store = null;
        }

        /// <summary>A one-line description of the stored run, for the Data/Save screen.</summary>
        /// <remarks>
        /// Counts, not prose: the interface decides how to say "no run yet", because that is a
        /// translation question and this is not the layer that knows about languages.
        /// </remarks>
        public static ProgressSummary DescribeStoredRun()
        {
            var summary = new ProgressSummary();
            summary.HasRun = Store.TryLoad(out SaveData data) && data != null;
            if (!summary.HasRun) return summary;

            summary.AbilitesOwned = data.Progression != null && data.Progression.HasAbility(AbilityId.Rootbind) ? 1 : 0;
            summary.FlagsEstablished = data.World != null ? data.World.FlagCount : 0;
            summary.HasCheckpoint = data.World != null && !string.IsNullOrEmpty(data.World.ActiveCheckpointId);
            return summary;
        }
    }

    /// <summary>What the Data/Save screen shows about the stored run.</summary>
    public struct ProgressSummary
    {
        /// <summary>True when a stored run exists and could be read.</summary>
        public bool HasRun;

        /// <summary>How many abilities the stored run has.</summary>
        public int AbilitesOwned;

        /// <summary>How many one-shot world facts the stored run has established.</summary>
        public int FlagsEstablished;

        /// <summary>Whether the stored run is standing at a checkpoint rather than the start.</summary>
        public bool HasCheckpoint;
    }
}
