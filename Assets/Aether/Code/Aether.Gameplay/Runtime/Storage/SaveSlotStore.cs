using System.Collections.Generic;
using Aether.Core.Progression;
using UnityEngine;

namespace Aether.Gameplay.Storage
{
    /// <summary>What a save slot looks like from the outside, before anything is loaded.</summary>
    /// <remarks>
    /// The menu has to draw a slot list without creating a session, without parsing a level, and
    /// without touching gameplay. Everything here is read from the file's own header, so a screen
    /// full of slots costs a few small reads and nothing else.
    /// </remarks>
    public struct SaveSlotInfo
    {
        /// <summary>1-based slot number, matching what the interface shows.</summary>
        public int Slot;

        /// <summary>True when a document exists for this slot.</summary>
        public bool Exists;

        /// <summary>
        /// True when a document exists but could not be read.
        /// </summary>
        /// <remarks>
        /// Deliberately separate from <see cref="Exists"/>: "nothing here yet" and "something is
        /// here and it is broken" are different things to a player, and a slot that silently
        /// reported itself empty would be the reason a run looks lost. A corrupt slot can be
        /// deleted; it is never loaded and never overwritten by accident.
        /// </remarks>
        public bool Corrupt;

        /// <summary>When the run was last written, as UTC ticks. 0 when it never has been.</summary>
        public long SavedUtcTicks;

        /// <summary>Seconds played in this run.</summary>
        public float PlaySeconds;

        /// <summary>Stable id of the chapter the run is in.</summary>
        public string ChapterId;

        /// <summary>Stable id of the objective the run is standing on.</summary>
        public string ObjectiveId;

        /// <summary>How many findings the run has recorded.</summary>
        public int Discoveries;

        /// <summary>How many achievements it has unlocked.</summary>
        public int Achievements;

        /// <summary>How many times the player has died in it.</summary>
        public int Deaths;

        /// <summary>True when the run can be loaded: it exists and it read.</summary>
        public bool Playable
        {
            get { return Exists && !Corrupt; }
        }

        /// <summary>True when there is anything at all to show for this slot.</summary>
        public bool HasAnything
        {
            get { return Exists; }
        }
    }

    /// <summary>
    /// One save slot on disk: the <see cref="ISaveStore"/> the session writes a run into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A slot is a file — <c>slot-1.json</c>, <c>slot-2.json</c>, <c>slot-3.json</c> in the game's
    /// own folder under the platform's persistent storage — and this type is the only thing that
    /// knows that. The session still sees <see cref="ISaveStore"/>, so nothing about gameplay had to
    /// learn about slots: it writes to "the store" and a slot is what that store happens to be.
    /// </para>
    /// <para>
    /// <b>Writes are the same atomic write the settings file uses</b>, because a run is worth more
    /// than a preference and deserves the same protection from a phone killing the app mid-write.
    /// </para>
    /// </remarks>
    public sealed class SaveSlotStore : ISaveStore
    {
        private readonly JsonFileStore _file;

        /// <summary>Creates the store for a slot number. Slots are 1-based.</summary>
        public SaveSlotStore(int slot)
        {
            Slot = slot < 1 ? 1 : slot;
            _file = new JsonFileStore("Aether", "slot-" + Slot + ".json");
        }

        /// <summary>The slot number this store belongs to.</summary>
        public int Slot { get; private set; }

        /// <summary>
        /// Whether progress records itself as it happens. The player owns this; the settings service
        /// changes it, and every slot of one player's game obeys the same answer.
        /// </summary>
        public bool AutomaticWrites = true;

        /// <summary>Path this slot's document lives at.</summary>
        public string Location
        {
            get { return _file.Path; }
        }

        /// <summary>True when a document exists right now.</summary>
        public bool HasStoredProgress
        {
            get { return _file.Exists; }
        }

        /// <summary>Reads the slot. False when there is nothing, or the document is unusable.</summary>
        public bool TryLoad(out SaveData data)
        {
            data = null;
            if (!_file.TryRead(out SaveData stored)) return false;

            // Guard the shape rather than trusting the file: a save with a missing section would
            // otherwise null-reference the first system that reads it. A document written before a
            // section existed is repaired here rather than rejected — that is what the version
            // number is for, and losing a run over an added field would be the wrong trade.
            if (stored == null) return false;

            if (stored.Progression == null) stored.Progression = new ProgressionState();
            if (stored.World == null) stored.World = new WorldState();
            if (stored.Meta == null) stored.Meta = new SaveMeta();
            if (stored.Collection == null) stored.Collection = new CollectionState();
            if (stored.Characters == null) stored.Characters = new CharacterState();
            if (stored.Achievements == null) stored.Achievements = new AchievementState();

            stored.Meta.Slot = Slot;
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
        /// The explicit path, used by "Save now", by the pause menu's save, and by the transitions
        /// that must not lose the player's progress — starting a new game, leaving to the menu and
        /// closing the app.
        /// </remarks>
        public void Save(SaveData data, bool explicitWrite)
        {
            if (data == null) return;
            if (!explicitWrite && !AutomaticWrites) return;

            // Stamped on the way out, so the slot list is drawn from what is actually on disk
            // rather than from what the session happened to be holding.
            if (data.Meta == null) data.Meta = new SaveMeta();
            data.Meta.Slot = Slot;
            data.Meta.SavedUtcTicks = System.DateTime.UtcNow.Ticks;
            data.Version = SaveData.CurrentVersion;

            _file.Write(data);
        }

        public void Clear()
        {
            _file.Delete();
        }

        /// <summary>Reads the header of this slot without loading a session.</summary>
        public SaveSlotInfo Describe()
        {
            var info = new SaveSlotInfo { Slot = Slot, Exists = _file.Exists };
            if (!info.Exists) return info;

            if (!TryLoad(out SaveData data))
            {
                // It is on disk and it did not read: the one case the interface must not show as
                // "empty", because there is something there and the player may want it back.
                info.Corrupt = true;
                return info;
            }

            SaveMeta meta = data.Meta;
            info.SavedUtcTicks = meta != null ? meta.SavedUtcTicks : 0L;
            info.PlaySeconds = meta != null ? meta.PlaySeconds : 0f;
            info.ChapterId = meta != null ? meta.ChapterId : string.Empty;
            info.ObjectiveId = meta != null ? meta.ObjectiveId : string.Empty;
            info.Discoveries = data.Collection != null ? data.Collection.Count : 0;
            info.Achievements = data.Achievements != null ? data.Achievements.UnlockedCount : 0;
            info.Deaths = meta != null ? meta.Deaths : 0;
            return info;
        }

        public override string ToString()
        {
            return _file.ToString();
        }
    }

    /// <summary>
    /// The three save slots, and the rules about which one the game is playing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three slots, one file each, named by number.</b> A fixed number rather than a growing list
    /// because a phone interface can show three well and thirty badly, and because the number is
    /// part of the promise the save screen makes: the player always knows where their runs are.
    /// Adding a fourth is one constant.
    /// </para>
    /// <para>
    /// <b>The old single save file is adopted, not abandoned.</b> This project shipped a build with
    /// one <c>save.json</c>; a player who installed it has a run there and would lose it if the slot
    /// system simply looked for slot files. The first ask moves it into slot one, once, and only
    /// when no slot file exists.
    /// </para>
    /// </remarks>
    public static class SaveSlots
    {
        /// <summary>How many slots the game has.</summary>
        public const int Count = 3;

        /// <summary>The file name the single-save build used, before slots existed.</summary>
        public const string LegacyFileName = "save.json";

        private static readonly SaveSlotStore[] Stores = new SaveSlotStore[Count];
        private static bool _migrated;

        /// <summary>The store for a slot. Slots are 1-based; anything else is pulled into range.</summary>
        public static SaveSlotStore For(int slot)
        {
            int index = Clamp(slot) - 1;
            if (Stores[index] == null)
            {
                Stores[index] = new SaveSlotStore(index + 1);
                Stores[index].AutomaticWrites = AutomaticWrites;
            }

            return Stores[index];
        }

        /// <summary>Every slot's header, in order. This is what the save screen paints.</summary>
        public static SaveSlotInfo[] DescribeAll()
        {
            AdoptLegacySave();

            var all = new SaveSlotInfo[Count];
            for (int i = 0; i < Count; i++) all[i] = For(i + 1).Describe();
            return all;
        }

        /// <summary>One slot's header.</summary>
        public static SaveSlotInfo Describe(int slot)
        {
            AdoptLegacySave();
            return For(slot).Describe();
        }

        /// <summary>
        /// The most recently played slot that can actually be loaded, or 0 when there is none.
        /// </summary>
        /// <remarks>
        /// This is what CONTINUE means: the most recent valid run, not "the last slot that happened
        /// to be selected". A corrupt newest slot does not hide an older good one — the player is
        /// offered the run that can be played, and the broken one stays visible in the list.
        /// </remarks>
        /// <summary>
        /// The slot whose run was played longest ago, which is the one a new run may replace.
        /// </summary>
        /// <remarks>
        /// Empty slots are never returned: an empty slot is free, and a caller that has run out of
        /// free slots is asking a different question. Every filled slot ties at "never played" for a
        /// run that was created and never entered, and the lowest slot wins the tie, so the answer is
        /// stable rather than whichever directory the file system happened to list first.
        /// </remarks>
        public static int LeastRecent()
        {
            int found = 0;
            long oldest = long.MaxValue;

            for (int slot = 1; slot <= Count; slot++)
            {
                SaveSlotInfo info = Describe(slot);
                if (!info.Exists) continue;

                long ticks = info.SavedUtcTicks <= 0 ? 1 : info.SavedUtcTicks;
                if (ticks < oldest)
                {
                    oldest = ticks;
                    found = slot;
                }
            }

            return found == 0 ? MostRecent() : found;
        }

        /// <summary>The slot of the most recently written run.</summary>
        public static int MostRecent()
        {
            SaveSlotInfo[] all = DescribeAll();

            int best = 0;
            long bestTicks = 0L;
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].Playable) continue;
                if (best != 0 && all[i].SavedUtcTicks <= bestTicks) continue;

                best = all[i].Slot;
                bestTicks = all[i].SavedUtcTicks;
            }

            return best;
        }

        /// <summary>The lowest-numbered slot with nothing in it, or 0 when all three are used.</summary>
        public static int FirstEmpty()
        {
            SaveSlotInfo[] all = DescribeAll();
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].HasAnything) return all[i].Slot;
            }

            return 0;
        }

        /// <summary>Whether any slot holds a run that can be loaded.</summary>
        public static bool HasAnyRun()
        {
            return MostRecent() != 0;
        }

        /// <summary>Deletes one slot's document.</summary>
        public static void Delete(int slot)
        {
            For(slot).Clear();
        }

        /// <summary>How many slots hold something, corrupt documents included.</summary>
        public static int UsedCount()
        {
            SaveSlotInfo[] all = DescribeAll();
            int used = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].HasAnything) used++;
            }

            return used;
        }

        /// <summary>Whether automatic writes are on, so a newly created store agrees with the rest.</summary>
        public static bool AutomaticWrites { get; set; } = true;

        /// <summary>Applies the autosave preference to every slot that exists.</summary>
        public static void ApplyAutosave(bool automaticWrites)
        {
            AutomaticWrites = automaticWrites;
            for (int i = 0; i < Count; i++)
            {
                if (Stores[i] != null) Stores[i].AutomaticWrites = automaticWrites;
            }
        }

        /// <summary>Forgets every cached slot. Used by tests, which must not touch real files.</summary>
        public static void ResetForTests()
        {
            for (int i = 0; i < Count; i++) Stores[i] = null;
            _migrated = false;
        }

        /// <summary>
        /// Moves a pre-slot save file into slot one, once, if nothing else is there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A build before this one wrote a single <c>save.json</c>. A player with that build has a
        /// run in it, and a slot system that only looks for slot files would show them three empty
        /// slots and a lost region. So the file is moved rather than copied: after the move there is
        /// exactly one document in exactly one place, and no way for the two to drift apart.
        /// </para>
        /// <para>
        /// The move is attempted once per launch and only when slot one is empty, so a player who
        /// deletes slot one on purpose does not get it back.
        /// </para>
        /// </remarks>
        public static void AdoptLegacySave()
        {
            if (_migrated) return;
            _migrated = true;

            var legacy = new JsonFileStore("Aether", LegacyFileName);
            if (!legacy.Exists) return;

            var first = For(1);
            if (first.HasStoredProgress) return;

            // Read it as a save, then write it as a slot: the legacy file has no Meta, so it is
            // stamped by the normal write path, which is also what gives it a slot number.
            if (!legacy.TryRead(out SaveData data) || data == null) return;
            if (data.Progression == null || data.World == null) return;

            if (data.Collection == null) data.Collection = new CollectionState();
            if (data.Characters == null) data.Characters = new CharacterState();
            if (data.Achievements == null) data.Achievements = new AchievementState();
            data.Meta = new SaveMeta { Slot = 1, ChapterId = string.Empty };

            first.Save(data, true);
            if (!first.HasStoredProgress) return;      // the write failed; leave the old file alone

            legacy.Delete();
            Debug.Log("Adopted the single-file save from an earlier build into slot 1.");
        }

        private static int Clamp(int slot)
        {
            if (slot < 1) return 1;
            return slot > Count ? Count : slot;
        }
    }
}
