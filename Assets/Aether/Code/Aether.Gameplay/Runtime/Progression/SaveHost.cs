using Aether.Core.Progression;
using Aether.Gameplay.Settings;
using Aether.Gameplay.Storage;
using UnityEngine;

namespace Aether.Gameplay.Progression
{
    /// <summary>
    /// The one place the game decides which run is being played, where it is stored, and when a new
    /// one begins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GameSession</c> owns the snapshot and its rules; this owns the lifetime and the slot. Both
    /// the menu and the region ask for a session here, so "which run", "is there one", "start a new
    /// game" and "write this down" all have one answer, and the answer is the same in the menu, in
    /// gameplay and in the pause menu.
    /// </para>
    /// <para>
    /// <b>The store is created without a session.</b> The menu has to know what is in the slots
    /// <i>before</i> it builds anything, and asking that question must not drag a session into
    /// being — a menu on a device that is about to be closed should cost nothing.
    /// </para>
    /// <para>
    /// <b>Continue and New Game are both slot operations.</b> Continue is
    /// <see cref="ContinueMostRecent"/>: the most recent slot that can actually be loaded. New Game
    /// is <see cref="BeginNewGameIn"/>: one named slot, cleared and restamped, never "whichever slot
    /// happened to be current", because that is how a player loses a run they did not mean to
    /// touch.
    /// </para>
    /// </remarks>
    public static class SaveHost
    {
        private static SaveSlotStore _store;
        private static int _activeSlot;

        /// <summary>The store for the run being played, created on first ask.</summary>
        /// <remarks>
        /// Before a slot is chosen this is slot one's store, which is what the Data/Save screen and
        /// the settings layer have always read. Choosing a slot replaces it.
        /// </remarks>
        public static SaveSlotStore Store
        {
            get
            {
                if (_store != null) return _store;

                _store = SaveSlots.For(_activeSlot < 1 ? 1 : _activeSlot);
                _store.AutomaticWrites = AetherSettings.Ensure().Values.Gameplay.Autosave;
                return _store;
            }
        }

        /// <summary>Which slot the game is playing, or 0 when none has been chosen yet.</summary>
        public static int ActiveSlot
        {
            get { return _activeSlot; }
        }

        /// <summary>True when a run exists that could be continued, in any slot.</summary>
        public static bool HasStoredProgress
        {
            get { return SaveSlots.HasAnyRun(); }
        }

        /// <summary>Whether a specific slot holds a loadable run.</summary>
        public static bool HasRunIn(int slot)
        {
            return slot >= 1 && slot <= SaveSlots.Count && SaveSlots.Describe(slot).Playable;
        }

        /// <summary>Every slot's header, for the save screen.</summary>
        public static SaveSlotInfo[] DescribeSlots()
        {
            return SaveSlots.DescribeAll();
        }

        /// <summary>
        /// Reads a stored run without playing it, or null when there is nothing readable there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The screens that report on a run — chapters, characters, collection, achievements — must
        /// show the run that is <i>stored</i> when the player is still in the menu, and they must not
        /// start one to do it. This reads the file and hands back the data; the active session is not
        /// touched, no run is loaded, and nothing is written.
        /// </para>
        /// <para>
        /// A damaged file returns null rather than throwing, which is the same answer as an empty
        /// slot: a screen that cannot read a run has nothing to report about it, and the slot screen
        /// is where the damage itself is named.
        /// </para>
        /// </remarks>
        public static SaveData Peek(int slot)
        {
            if (slot < 1 || slot > SaveSlots.Count || !SaveSlots.Describe(slot).Playable) return null;

            SaveSlotStore store = SaveSlots.For(slot);
            if (store == null || !store.HasStoredProgress) return null;

            return store.TryLoad(out SaveData data) ? data : null;
        }

        /// <summary>
        /// The run the menu should describe: the active slot's if it is standing on one, otherwise
        /// the most recent loadable one.
        /// </summary>
        /// <remarks>
        /// Which run a menu screen describes is a decision, not a lookup, so it is made once here:
        /// a session that is already playing a slot reports that slot, and a menu opened fresh
        /// reports the run CONTINUE would load. Anything else would have the main menu offer to
        /// continue one run while the achievements screen described another.
        /// </remarks>
        public static SaveData Peek()
        {
            GameSession session = GameSession.Instance;
            if (_activeSlot > 0 && _activeSlot <= SaveSlots.Count)
            {
                // In gameplay the live snapshot is authoritative. Back in the menu the session may
                // have been destroyed with its scene, so read that selected slot rather than showing
                // a different run merely because it was written more recently.
                if (session != null && session.Save != null) return session.Save;
                return Peek(_activeSlot);
            }

            int slot = SaveSlots.MostRecent();
            return slot > 0 ? Peek(slot) : null;
        }

        /// <summary>The slot <see cref="Peek()"/> reads, or 0 when there is nothing stored.</summary>
        public static int DescribedSlot()
        {
            // LoadSlot selects its destination before Boot creates a session. Preserve that choice
            // across the hand-off instead of silently switching Save Now to the newest other slot.
            if (_activeSlot > 0 && _activeSlot <= SaveSlots.Count) return _activeSlot;

            return SaveSlots.MostRecent();
        }

        /// <summary>The most recent slot that can be loaded, or 0. This is what CONTINUE uses.</summary>
        public static int MostRecentSlot()
        {
            return SaveSlots.MostRecent();
        }

        /// <summary>Where progress is kept, for the Data/Save screen and for support.</summary>
        public static string Location
        {
            get { return Store.Location; }
        }

        /// <summary>
        /// The session for this run, created and loaded from the active slot if this is the first ask.
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
        /// Points the game at a slot and loads it. This is both CONTINUE and "load this slot".
        /// </summary>
        /// <remarks>
        /// Returns false when the slot cannot be loaded — it is empty, or its document is damaged —
        /// and in that case nothing is changed: the game keeps whatever run it had rather than
        /// silently starting from nothing. The caller decides what to tell the player.
        /// </remarks>
        public static bool LoadSlot(int slot)
        {
            if (slot < 1 || slot > SaveSlots.Count) return false;

            SaveSlotInfo info = SaveSlots.Describe(slot);
            if (!info.Playable) return false;

            SaveSlotStore wanted = SaveSlots.For(info.Slot);
            wanted.AutomaticWrites = AetherSettings.Ensure().Values.Gameplay.Autosave;

            GameSession existing = GameSession.Instance;
            if (existing != null)
            {
                ISaveStore previous = existing.Store;
                existing.Store = wanted;
                if (!existing.TryLoadFromStore())
                {
                    existing.Store = previous;
                    return false;
                }
            }

            _store = wanted;
            _activeSlot = info.Slot;
            return true;
        }

        /// <summary>Loads the most recent playable slot. False when there is nothing to continue.</summary>
        public static bool ContinueMostRecent()
        {
            int slot = SaveSlots.MostRecent();
            return slot != 0 && LoadSlot(slot);
        }

        /// <summary>
        /// Starts a new run in one slot, erasing whatever was there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Destructive, and therefore only ever reached from a confirmed action in the interface —
        /// or from a slot the player explicitly chose as the place to start. The erase is a real
        /// erase: the slot's document is written over with a fresh snapshot, so the old run is gone
        /// the moment this returns rather than whenever the next autosave happens.
        /// </para>
        /// <para>
        /// The session is created if there is none, so this is safe to call from the menu before a
        /// region has ever run.
        /// </para>
        /// </remarks>
        public static GameSession BeginNewGameIn(int slot, string chapterId, bool replaceExisting = false)
        {
            if (slot < 1 || slot > SaveSlots.Count) return null;

            // This low-level guard is deliberate: even a new caller cannot replace a stored run
            // without naming that decision. Empty slots are safe to initialize immediately.
            SaveSlotInfo existingInfo = SaveSlots.Describe(slot);
            if (existingInfo.Exists && !replaceExisting) return null;

            SaveSlotStore target = SaveSlots.For(slot);
            target.AutomaticWrites = AetherSettings.Ensure().Values.Gameplay.Autosave;

            var fresh = new SaveData();
            fresh.Meta.Slot = target.Slot;
            fresh.Meta.ChapterId = string.IsNullOrEmpty(chapterId) ? string.Empty : chapterId;

            // The old run is replaced atomically by the store. If the write fails, neither the
            // active session nor the selected slot changes, and the old document remains readable.
            if (!target.Save(fresh, true)) return null;

            GameSession session = GameSession.Instance;
            if (session == null)
            {
                var host = new GameObject("GameSession");
                session = host.AddComponent<GameSession>();
            }

            session.Store = target;
            session.Save.CopyFrom(fresh);
            _store = target;
            _activeSlot = target.Slot;
            return session;
        }

        /// <summary>Deletes one slot's run. Destructive; the interface confirms first.</summary>
        public static bool DeleteSlot(int slot)
        {
            if (slot < 1 || slot > SaveSlots.Count) return false;
            if (!SaveSlots.Delete(slot)) return false;

            // A session standing on the slot that was just deleted must not write it back. Detach
            // it and clear the active selection only after the filesystem confirms the deletion.
            if (_activeSlot == slot)
            {
                GameSession session = GameSession.Instance;
                if (session != null)
                {
                    session.Save.ResetForNewGame();
                    session.Store = null;
                }

                _store = null;
                _activeSlot = 0;
            }

            return true;
        }

        /// <summary>
        /// Writes progress now, whatever the autosave preference says.
        /// </summary>
        /// <remarks>
        /// The explicit half of the autosave setting. It is what "Save now" calls, what the pause
        /// menu calls, and what the menu calls before it hands the app over to a region.
        /// </remarks>
        public static bool SaveNow()
        {
            GameSession session = GameSession.Instance;
            if (session != null && session.Store != null)
            {
                try
                {
                    bool written = session.Store.Save(session.Save, true);
                    if (!written) Debug.LogError("[save] The explicit save could not be written.", session);
                    return written;
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex, session);
                    return false;
                }
            }

            // The Data screen can ask for a save while the game is in the menu and no live session
            // exists. In that case, explicitly re-write the same selected stored snapshot; never
            // manufacture a blank session or route an unset slot through the slot-one clamp.
            int slot = DescribedSlot();
            if (slot < 1 || slot > SaveSlots.Count) return false;

            SaveSlotStore store = SaveSlots.For(slot);
            if (!store.TryLoad(out SaveData stored) || stored == null) return false;

            bool saved = store.Save(stored, true);
            if (!saved) Debug.LogError($"[save] The explicit save for slot {slot} could not be written.");
            return saved;
        }

        /// <summary>Re-reads the autosave preference. Called when that setting changes.</summary>
        public static void ApplySettings()
        {
            SaveSlots.ApplyAutosave(AetherSettings.Ensure().Values.Gameplay.Autosave);
        }

        /// <summary>Forgets the store and session, restoring the normal storage folder.</summary>
        public static void ResetForTests(GameSession session)
        {
            ResetForTests(session, null);
        }

        /// <summary>Forgets the store and session, optionally selecting an isolated storage folder.</summary>
        public static void ResetForTests(GameSession session, string folder)
        {
            if (session != null) Object.DestroyImmediate(session.gameObject);
            _store = null;
            _activeSlot = 0;
            SaveSlots.ResetForTests(folder);
        }

        /// <summary>A one-line description of the stored run, for the Data/Save screen.</summary>
        /// <remarks>
        /// Counts, not prose: the interface decides how to say "no run yet", because that is a
        /// translation question and this is not the layer that knows about languages.
        /// </remarks>
        public static ProgressSummary DescribeStoredRun()
        {
            return DescribeStoredRun(_activeSlot < 1 ? SaveSlots.MostRecent() : _activeSlot);
        }

        /// <summary>Counts for one slot, for the Data/Save screen and the slot list.</summary>
        public static ProgressSummary DescribeStoredRun(int slot)
        {
            var summary = new ProgressSummary();
            if (slot < 1) return summary;

            SaveSlotInfo info = SaveSlots.Describe(slot);
            summary.Slot = info.Slot;
            summary.HasRun = info.Playable;
            summary.Corrupt = info.Corrupt;
            summary.PlaySeconds = info.PlaySeconds;
            summary.SavedUtcTicks = info.SavedUtcTicks;
            summary.Discoveries = info.Discoveries;
            summary.Achievements = info.Achievements;
            summary.Deaths = info.Deaths;
            summary.ChapterId = info.ChapterId;

            if (!summary.HasRun) return summary;

            if (SaveSlots.For(slot).TryLoad(out SaveData data) && data != null)
            {
                summary.AbilitesOwned =
                    data.Progression != null && data.Progression.HasAbility(AbilityId.Rootbind) ? 1 : 0;
                summary.FlagsEstablished = data.World != null ? data.World.FlagCount : 0;
                summary.HasCheckpoint = data.World != null && !string.IsNullOrEmpty(data.World.ActiveCheckpointId);
            }

            return summary;
        }
    }

    /// <summary>What the Data/Save screen and the slot list show about a stored run.</summary>
    public struct ProgressSummary
    {
        /// <summary>Which slot this describes. 0 when there is none.</summary>
        public int Slot;

        /// <summary>True when a stored run exists and could be read.</summary>
        public bool HasRun;

        /// <summary>True when something is stored and could not be read.</summary>
        public bool Corrupt;

        /// <summary>How many abilities the stored run has.</summary>
        public int AbilitesOwned;

        /// <summary>How many one-shot world facts the stored run has established.</summary>
        public int FlagsEstablished;

        /// <summary>Whether the stored run is standing at a checkpoint rather than the start.</summary>
        public bool HasCheckpoint;

        /// <summary>Seconds played in the stored run.</summary>
        public float PlaySeconds;

        /// <summary>When it was last written, as UTC ticks.</summary>
        public long SavedUtcTicks;

        /// <summary>How many findings the stored run has recorded.</summary>
        public int Discoveries;

        /// <summary>How many achievements it has unlocked.</summary>
        public int Achievements;

        /// <summary>How many times the player died in it.</summary>
        public int Deaths;

        /// <summary>Stable id of the chapter the run is in.</summary>
        public string ChapterId;
    }
}
