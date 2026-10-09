using System;
using System.IO;
using System.Text.RegularExpressions;
using Aether.Core.Events;
using Aether.Core.Progression;
using Aether.Core.Settings;
using Aether.Gameplay;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Settings;
using Aether.Gameplay.Progression.Achievements;
using Aether.Gameplay.Storage;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Aether.Tests
{
    /// <summary>Persistence checks for settings and the complete save snapshot.</summary>
    public sealed class PersistenceTests
    {
        private string _saveFile;
        private FileSaveStore _saveStore;
        private string _slotFolder;
        private SaveSlotStore _slotStore;

        [SetUp]
        public void SetUp()
        {
            _saveFile = "test-" + Guid.NewGuid().ToString("N") + ".json";
            _saveStore = new FileSaveStore(_saveFile);
            _slotFolder = "Aether-Test-" + Guid.NewGuid().ToString("N");
            _slotStore = new SaveSlotStore(1, _slotFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (_saveStore != null) _saveStore.Clear();
            if (_slotStore != null) _slotStore.Clear();

            string root = System.IO.Path.GetDirectoryName(_slotStore.Location);
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }

        [Test]
        public void SaveStore_RoundTripsProgressWorldCollectionCharactersAndAchievements()
        {
            var original = new SaveData();
            original.Version = SaveData.CurrentVersion;
            original.Meta.Slot = 2;
            original.Meta.ChapterId = "greenway";
            original.Meta.ObjectiveId = "objective.greenway.exit";
            original.Meta.PlaySeconds = 91.5f;
            original.Meta.Deaths = 1;
            original.Progression.GrantAbility(AbilityId.Rootbind);
            original.World.Set("secret.greenway.overhang");
            original.World.ActiveCheckpointId = "checkpoint.greenway.north";
            original.Collection.Record("secret.greenway.overhang");
            original.Characters.Record("forest_stalker");
            original.Characters.SelectedId = "forest_stalker";
            AchievementRecord achievement = original.Achievements.Ensure("ach.greenway.enter");
            achievement.Progress = 1;
            achievement.UnlockedUtcTicks = DateTime.UtcNow.Ticks;

            Assert.That(_saveStore.Save(original, true), Is.True,
                "A successful explicit write must report that the document reached storage.");

            var reader = new FileSaveStore(_saveFile);
            Assert.That(reader.TryLoad(out SaveData loaded), Is.True);
            Assert.That(loaded.Progression.HasAbility(AbilityId.Rootbind), Is.True);
            Assert.That(loaded.World.IsSet("secret.greenway.overhang"), Is.True);
            Assert.That(loaded.World.ActiveCheckpointId, Is.EqualTo("checkpoint.greenway.north"));
            Assert.That(loaded.Collection.Has("secret.greenway.overhang"), Is.True);
            Assert.That(loaded.Characters.HasMet("forest_stalker"), Is.True);
            Assert.That(loaded.Characters.SelectedId, Is.EqualTo("forest_stalker"));
            Assert.That(loaded.Achievements.IsUnlocked("ach.greenway.enter"), Is.True);
            Assert.That(loaded.Achievements.Find("ach.greenway.enter").Progress, Is.EqualTo(1));
            Assert.That(loaded.Meta.Slot, Is.EqualTo(2));
            Assert.That(loaded.Meta.ChapterId, Is.EqualTo("greenway"));
            Assert.That(loaded.Meta.ObjectiveId, Is.EqualTo("objective.greenway.exit"));
            Assert.That(loaded.Meta.Deaths, Is.EqualTo(1));
        }

        [Test]
        public void SaveStore_RepairsMissingSectionsWithoutDiscardingTheRun()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_saveStore.Location));
            File.WriteAllText(_saveStore.Location,
                "{\"Version\":1,\"Progression\":null,\"World\":null,\"Meta\":null,"
                + "\"Collection\":null,\"Characters\":null,\"Achievements\":null}");

            Assert.That(_saveStore.TryLoad(out SaveData loaded), Is.True);
            Assert.That(loaded.Progression, Is.Not.Null);
            Assert.That(loaded.World, Is.Not.Null);
            Assert.That(loaded.Meta, Is.Not.Null);
            Assert.That(loaded.Collection, Is.Not.Null);
            Assert.That(loaded.Characters, Is.Not.Null);
            Assert.That(loaded.Achievements, Is.Not.Null);
            Assert.That(loaded.Version, Is.EqualTo(1), "Reading an older shape should not pretend it was already rewritten.");
        }

        [Test]
        public void SettingsService_FlushWithoutStoreIsSessionOnlyAndCanPersistLater()
        {
            var settings = new SettingsService();
            settings.Load();
            settings.Set("audio.master", 0.35f);

            Assert.That(settings.Flush(), Is.True,
                "A session-only settings service must not report a persistence failure when no store exists.");
            Assert.That(settings.IsDirty, Is.True,
                "Keep the change dirty so a store attached later can persist it.");

            var store = new MemorySettingsStore();
            settings.Store = store;
            Assert.That(settings.Flush(), Is.True);
            Assert.That(settings.IsDirty, Is.False);
            Assert.That(SettingsCatalog.Read(store.Stored, "audio.master"), Is.EqualTo(0.35f).Within(0.001f));
        }

        [Test]
        public void SettingsService_RetriesAFailedWriteAndClearsDirtyOnlyAfterSuccess()
        {
            var store = new MemorySettingsStore();
            SettingsService settings = new SettingsService();
            settings.Store = store;
            settings.Load();
            settings.Set("audio.master", 0.35f);

            store.FailWrites = true;
            Assert.That(settings.Flush(), Is.False);
            Assert.That(settings.IsDirty, Is.True);

            store.FailWrites = false;
            Assert.That(settings.Flush(), Is.True);
            Assert.That(settings.IsDirty, Is.False);
            Assert.That(SettingsCatalog.Read(store.Stored, "audio.master"), Is.EqualTo(0.35f).Within(0.001f));
        }

        [Test]
        public void FrameRateSetting_PreservesAndAppliesThe120FpsRequest()
        {
            SettingDefinition definition = SettingsCatalog.Find("graphics.frameRate");
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Minimum, Is.EqualTo(30f));
            Assert.That(definition.Maximum, Is.EqualTo(120f));
            Assert.That(definition.OptionValues, Is.EqualTo(new[] { 30f, 60f, 120f }));

            var settings = new SettingsService();
            settings.Set("graphics.frameRate", 120f);
            Assert.That(settings.Values.Graphics.FrameRateLimit, Is.EqualTo(120));

            int previousTarget = Application.targetFrameRate;
            int previousVsync = QualitySettings.vSyncCount;
            try
            {
                AetherSettings.ApplyFrameRate(settings.Values.Graphics.FrameRateLimit);
                Assert.That(Application.targetFrameRate, Is.EqualTo(120));
                Assert.That(QualitySettings.vSyncCount, Is.Zero);
            }
            finally
            {
                QualitySettings.vSyncCount = previousVsync;
                Application.targetFrameRate = previousTarget;
            }
        }

        [Test]
        public void CharacterMeeting_IsRecordedPublishedAndPersistedOnlyOnce()
        {
            var host = new GameObject("GameSession persistence test");
            try
            {
                GameSession session = host.AddComponent<GameSession>();
                var store = new MemorySaveStore();
                session.Store = store;

                int events = 0;
                string eventCharacter = null;
                session.Events.Subscribe<CharacterMetEvent>(raised =>
                {
                    events++;
                    eventCharacter = raised.CharacterId;
                });

                Assert.That(session.RecordCharacterMet("forest_stalker"), Is.True);
                Assert.That(session.RecordCharacterMet("forest_stalker"), Is.False);
                Assert.That(events, Is.EqualTo(1));
                Assert.That(eventCharacter, Is.EqualTo("forest_stalker"));
                Assert.That(store.WriteCount, Is.EqualTo(1));
                Assert.That(store.Stored.Characters.HasMet("forest_stalker"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AutomaticSaveFailureRaisesARecoverableSessionSignal()
        {
            var host = new GameObject("GameSession save failure test");
            try
            {
                GameSession session = host.AddComponent<GameSession>();
                var store = new MemorySaveStore();
                session.Store = store;

                int failures = 0;
                session.SaveFailed += _ => failures++;
                store.FailWrites = true;

                LogAssert.Expect(LogType.Exception, new Regex("The automatic save could not be written."));
                Assert.That(session.RequestSave(), Is.False);
                Assert.That(failures, Is.EqualTo(1));

                store.FailWrites = false;
                Assert.That(session.RequestSave(), Is.True);
                Assert.That(failures, Is.EqualTo(1));
                Assert.That(store.WriteCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SlotStore_AutomaticWritesRespectPreferenceButExplicitWritesDoNot()
        {
            var save = new SaveData();
            save.Meta.ChapterId = ChapterCatalog.First.Id;
            _slotStore.AutomaticWrites = false;

            Assert.That(_slotStore.Save(save), Is.True);
            Assert.That(_slotStore.HasStoredProgress, Is.False,
                "A declined automatic write must not create or replace a document.");

            Assert.That(_slotStore.Save(save, true), Is.True);
            Assert.That(_slotStore.HasStoredProgress, Is.True);
            Assert.That(_slotStore.Describe().Playable, Is.True);
        }

        [Test]
        public void SlotStore_UnsupportedChapterIsVisibleButNeverPlayable()
        {
            var save = new SaveData();
            save.Meta.ChapterId = "chapter.from-a-newer-build";
            Assert.That(_slotStore.Save(save, true), Is.True);

            SaveSlotInfo info = _slotStore.Describe();
            Assert.That(info.Exists, Is.True);
            Assert.That(info.Corrupt, Is.False);
            Assert.That(info.Unavailable, Is.True);
            Assert.That(info.Playable, Is.False);
        }

        [Test]
        public void SlotStore_NewSaveFormatIsUnavailableRatherThanCorrupt()
        {
            var newer = new SaveData { Version = SaveData.CurrentVersion + 1 };
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_slotStore.Location));
            File.WriteAllText(_slotStore.Location, JsonUtility.ToJson(newer, true));

            Assert.That(_slotStore.TryLoad(out SaveData ignored), Is.False);
            SaveSlotInfo info = _slotStore.Describe();
            Assert.That(info.Unavailable, Is.True);
            Assert.That(info.Corrupt, Is.False);
            Assert.That(info.Playable, Is.False);
        }

        [Test]
        public void SlotStore_MalformedSaveIsCorruptAndNotEmpty()
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_slotStore.Location));
            File.WriteAllText(_slotStore.Location, "{ not valid json");

            SaveSlotInfo info = _slotStore.Describe();
            Assert.That(info.Exists, Is.True);
            Assert.That(info.Corrupt, Is.True);
            Assert.That(info.Playable, Is.False);
        }

        [Test]
        public void SaveHost_RefusesInvalidSlotNumbersInsteadOfClampingToSlotOne()
        {
            Assert.That(SaveHost.LoadSlot(0), Is.False);
            Assert.That(SaveHost.LoadSlot(SaveSlots.Count + 1), Is.False);
            Assert.That(SaveHost.DeleteSlot(0), Is.False);
            Assert.That(SaveHost.BeginNewGameIn(0, ChapterCatalog.First.Id), Is.Null);
            Assert.That(SaveHost.Peek(0), Is.Null);
            Assert.That(SaveSlots.Describe(0).HasAnything, Is.False);
        }

        private sealed class MemorySaveStore : ISaveStore
        {
            public SaveData Stored;
            public int WriteCount;
            public bool FailWrites;

            public bool TryLoad(out SaveData data)
            {
                data = Stored;
                return data != null;
            }

            public bool Save(SaveData data)
            {
                return Save(data, false);
            }

            public bool Save(SaveData data, bool explicitWrite)
            {
                if (FailWrites || data == null) return false;
                Stored = new SaveData();
                Stored.CopyFrom(data);
                WriteCount++;
                return true;
            }

            public bool Clear()
            {
                Stored = null;
                return true;
            }
        }

        private sealed class MemorySettingsStore : ISettingsStore
        {
            public GameSettings Stored;
            public bool FailWrites;

            public bool TryLoad(out GameSettings settings)
            {
                settings = Stored;
                return settings != null;
            }

            public bool Save(GameSettings settings)
            {
                if (FailWrites || settings == null) return false;
                Stored = new GameSettings();
                Stored.CopyFrom(settings);
                return true;
            }

            public bool Clear()
            {
                Stored = null;
                return true;
            }

            public bool Exists => Stored != null;
            public string Location => "test memory store";
        }
    }
}
