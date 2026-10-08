using System.Collections.Generic;
using Aether.Core.Progression;
using Aether.Gameplay.Progression;
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

        /// <summary>True when it parses but this build cannot open its format or chapter.</summary>
        public bool Unavailable;

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
            get { return Exists && !Corrupt && !Unavailable; }
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

        /// <summary>Creates the store for a slot number under the game's persistent-data folder.</summary>
        public SaveSlotStore(int slot) : this(slot, "Aether")
        {
        }

        /// <summary>Creates a slot store under a chosen persistent-data subfolder.</summary>
        /// <remarks>The folder overload also lets storage tests use an isolated directory.</remarks>
        public SaveSlotStore(int slot, string folder)
        {
            Slot = slot < 1 ? 1 : slot;
            _file = new JsonFileStore(folder ?? "Aether", "slot-" + Slot + ".json");
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

            // Guard the shape rather than trusting the file: older documents are repaired instead
            // of being rejected over a section added after the run was written. A newer format is
            // preserved but not loaded by an older build that may not understand its fields.
            if (stored == null || stored.Version > SaveData.CurrentVersion) return false;

            stored.RepairMissingSections();
            stored.Meta.Slot = Slot;
            if (string.IsNullOrEmpty(stored.Meta.ChapterId))
                stored.Meta.ChapterId = ChapterCatalog.First.Id;
            data = stored;
            return true;
        }

        /// <summary>Writes progress, unless the player has turned automatic writes off.</summary>
        /// <remarks>This is the signature <c>ISaveStore</c> declares, and every gameplay call arrives here.</remarks>
        public bool Save(SaveData data)
        {
            if (!AutomaticWrites) return true;
            return Save(data, false);
        }

        /// <summary>Writes progress regardless of the autosave preference when explicitly requested.</summary>
        public bool Save(SaveData data, bool explicitWrite)
        {
            if (data == null) return false;
            if (!explicitWrite && !AutomaticWrites) return true;

            // Stamp an independent document, so the live session is not mutated by storage. Slot
            // summaries are read from the on-disk copy and therefore report the real write time.
            var snapshot = new SaveData();
            snapshot.CopyFrom(data);
            snapshot.Meta.Slot = Slot;
            snapshot.Meta.SavedUtcTicks = System.DateTime.UtcNow.Ticks;
            snapshot.Version = SaveData.CurrentVersion;

            return _file.Write(snapshot);
        }

        public bool Clear()
        {
            return _file.Delete();
        }

        /// <summary>Reads the header of this slot without loading a session.</summary>
        public SaveSlotInfo Describe()
        {
            var info = new SaveSlotInfo { Slot = Slot, Exists = _file.Exists };
            if (!info.Exists) return info;

            if (!TryLoad(out SaveData data))
            {
                // A newer format is not corrupt: this build simply cannot safely interpret it.
                // Keep it distinct so the menu can explain why it is locked and require confirmation
                // before replacing it.
                if (_file.TryRead(out SaveData unsupported) && unsupported != null
                    && unsupported.Version > SaveData.CurrentVersion)
                    info.Unavailable = true;
                else
                    info.Corrupt = true;
                return info;
            }

            SaveMeta meta = data.Meta;
            info.SavedUtcTicks = meta != null ? meta.SavedUtcTicks : 0L;
            info.PlaySeconds = meta != null ? meta.PlaySeconds : 0f;
            info.ChapterId = meta != null ? meta.ChapterId : string.Empty;
            ChapterDefinition chapter = ChapterCatalog.Find(info.ChapterId);
            info.Unavailable = chapter == null || !chapter.Playable;
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

        private const string DefaultFolder = "Aether";
        private static readonly SaveSlotStore[] Stores = new SaveSlotStore[Count];
        private static bool _migrated;
        private static string _folder = DefaultFolder;

        /// <summary>The store for a slot. Slots are 1-based; anything else is pulled into range.</summary>
        public static SaveSlotStore For(int slot)
        {
            int index = Clamp(slot) - 1;
            if (Stores[index] == null)
            {
                Stores[index] = new SaveSlotStore(index + 1, _folder);
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
            if (slot < 1 || slot > Count) return new SaveSlotInfo { Slot = slot };

            AdoptLegacySave();
            return For(slot).Describe();
        }

        /// <summary>The slot whose run was played longest ago, which is the one a new run may replace.</summary>
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

        /// <summary>Deletes one slot's document. Invalid slot numbers are refused, not clamped.</summary>
        public static bool Delete(int slot)
        {
            if (slot < 1 || slot > Count) return false;
            return For(slot).Clear();
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

        /// <summary>Forgets every cached slot and restores the real storage folder.</summary>
        public static void ResetForTests()
        {
            ResetForTests(DefaultFolder);
        }

        /// <summary>Uses an isolated persistent-data subfolder so menu tests cannot touch player saves.</summary>
        public static void ResetForTests(string folder)
        {
            for (int i = 0; i < Count; i++) Stores[i] = null;
            _migrated = false;
            _folder = string.IsNullOrEmpty(folder) ? DefaultFolder : folder;
            AutomaticWrites = true;
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

            var legacy = new JsonFileStore(_folder, LegacyFileName);
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

            if (!first.Save(data, true)) return;      // leave the old file alone when the write fails
            if (!first.HasStoredProgress) return;

            if (!legacy.Delete())
            {
                Debug.LogWarning("The legacy save was copied into slot 1 but its old file could not be removed.");
                return;
            }

            Debug.Log("Adopted the single-file save from an earlier build into slot 1.");
        }

        private static int Clamp(int slot)
        {
            if (slot < 1) return 1;
            return slot > Count ? Count : slot;
        }
    }
}
