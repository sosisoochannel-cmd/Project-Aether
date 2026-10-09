using System;
using System.IO;
using Aether.Core.Progression;
using Aether.Core.Settings;
using Aether.Gameplay;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Settings;
using Aether.Gameplay.Storage;
using NUnit.Framework;
using UnityEngine;

namespace Aether.Tests.PlayMode
{
    /// <summary>End-to-end checks for slot selection, saving, loading, and safe deletion.</summary>
    public sealed class SaveLifecyclePlayModeTests
    {
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = "Aether-SaveLifecycle-Test-" + Guid.NewGuid().ToString("N");
            SaveHost.ResetForTests(GameSession.Instance, _folder);
            AetherSettings.ResetForTests(new MemorySettingsStore());
        }

        [TearDown]
        public void TearDown()
        {
            SaveHost.ResetForTests(GameSession.Instance, _folder);
            for (int slot = 1; slot <= SaveSlots.Count; slot++) SaveSlots.For(slot).Clear();

            string root = Path.GetDirectoryName(SaveSlots.For(1).Location);
            if (Directory.Exists(root)) Directory.Delete(root, true);

            SaveHost.ResetForTests(null);
            AetherSettings.ResetForTests();
        }

        [Test]
        public void NewGame_SaveAndReloadRestoresProgressInTheChosenSlot()
        {
            GameSession session = SaveHost.BeginNewGameIn(2, ChapterCatalog.First.Id);
            Assert.IsNotNull(session, "New Game did not create a session.");
            Assert.AreEqual(2, SaveHost.ActiveSlot, "New Game did not select the requested slot.");

            session.World.Set("test.lifecycle.persisted-progress");
            Assert.IsTrue(SaveHost.SaveNow(), "The explicit save did not report success.");
            Assert.IsTrue(SaveSlots.Describe(2).Playable, "The new run was not persisted as playable.");

            // Drop the live session but retain the isolated files, as a scene/process transition would.
            SaveHost.ResetForTests(session, _folder);
            Assert.IsTrue(SaveHost.LoadSlot(2), "A valid stored run could not be selected.");

            GameSession restored = SaveHost.Ensure();
            Assert.AreEqual(2, SaveHost.ActiveSlot);
            Assert.IsTrue(restored.World.IsSet("test.lifecycle.persisted-progress"),
                "The loaded session lost progress that had been written to disk.");
        }

        [Test]
        public void SaveNowWithoutLiveSessionRewritesTheSelectedStoredSlot()
        {
            GameSession session = SaveHost.BeginNewGameIn(2, ChapterCatalog.First.Id);
            Assert.IsNotNull(session);
            Assert.IsTrue(session.SetWorldFlag("test.lifecycle.menu-save-keeps-slot"));
            Assert.IsTrue(SaveHost.SaveNow());

            // A menu can outlive the scene's session object while retaining the chosen slot.
            UnityEngine.Object.DestroyImmediate(session.gameObject);
            Assert.IsNull(GameSession.Instance);
            Assert.AreEqual(2, SaveHost.ActiveSlot);

            Assert.IsTrue(SaveHost.SaveNow(),
                "Save Now from the menu did not rewrite the selected stored run.");
            Assert.IsTrue(SaveSlots.For(2).TryLoad(out SaveData stored));
            Assert.IsTrue(stored.World.IsSet("test.lifecycle.menu-save-keeps-slot"),
                "The menu save lost the selected run's progress.");
            Assert.IsFalse(SaveSlots.For(1).HasStoredProgress,
                "The menu save unexpectedly wrote to slot one.");
        }

        [Test]
        public void CorruptSlotCannotReplaceTheCurrentActiveRun()
        {
            GameSession session = SaveHost.BeginNewGameIn(1, ChapterCatalog.First.Id);
            Assert.IsNotNull(session);
            Assert.IsTrue(session.SetWorldFlag("test.lifecycle.keep-active-run"));

            SaveSlotStore corrupt = SaveSlots.For(2);
            Directory.CreateDirectory(Path.GetDirectoryName(corrupt.Location));
            File.WriteAllText(corrupt.Location, "{ interrupted save");

            Assert.IsFalse(SaveHost.LoadSlot(2), "A corrupt slot was accepted as loadable.");
            Assert.AreSame(session, GameSession.Instance, "A rejected load replaced the live session.");
            Assert.AreEqual(1, SaveHost.ActiveSlot, "A rejected load changed the selected slot.");
            Assert.IsTrue(session.World.IsSet("test.lifecycle.keep-active-run"),
                "A rejected load discarded the currently active progress.");
            Assert.AreEqual(1, ((SaveSlotStore)session.Store).Slot,
                "The live session remained attached to the corrupt slot.");
        }

        [Test]
        public void DisablingAutosaveStopsAutomaticWritesButSaveNowStillPersists()
        {
            GameSession session = SaveHost.BeginNewGameIn(1, ChapterCatalog.First.Id);
            Assert.IsNotNull(session);
            Assert.IsTrue(session.SetWorldFlag("test.lifecycle.before-autosave-off"));
            Assert.IsTrue(SaveHost.SaveNow());

            AetherSettings.Ensure().Values.Gameplay.Autosave = false;
            SaveHost.ApplySettings();
            Assert.IsFalse(SaveSlots.For(1).AutomaticWrites);
            Assert.IsFalse(SaveSlots.For(2).AutomaticWrites);
            Assert.IsFalse(SaveSlots.For(3).AutomaticWrites);

            Assert.IsTrue(session.SetWorldFlag("test.lifecycle.automatic-write-disabled"));
            Assert.IsTrue(SaveSlots.For(1).TryLoad(out SaveData beforeExplicitSave));
            Assert.IsFalse(beforeExplicitSave.World.IsSet("test.lifecycle.automatic-write-disabled"),
                "Turning autosave off still wrote a gameplay change to disk.");

            Assert.IsTrue(SaveHost.SaveNow(),
                "The explicit Save Now command should work even when autosave is disabled.");
            Assert.IsTrue(SaveSlots.For(1).TryLoad(out SaveData afterExplicitSave));
            Assert.IsTrue(afterExplicitSave.World.IsSet("test.lifecycle.automatic-write-disabled"),
                "The explicit save did not persist the latest snapshot.");
        }

        [Test]
        public void DeletingActiveSlotThenPausingDoesNotRecreateItsSave()
        {
            GameSession session = SaveHost.BeginNewGameIn(1, ChapterCatalog.First.Id);
            Assert.IsNotNull(session);
            session.World.Set("test.lifecycle.must-not-return-after-delete");

            Assert.IsTrue(SaveHost.DeleteSlot(1), "The selected slot could not be deleted.");
            Assert.IsNull(session.Store, "Deleting the active slot did not detach its store.");
            Assert.AreEqual(0, SaveHost.ActiveSlot, "Deleting the active slot did not clear selection.");

            session.gameObject.SendMessage("OnApplicationPause", true);

            Assert.IsFalse(SaveSlots.For(1).HasStoredProgress,
                "A pause save recreated a slot the player had deleted.");
            Assert.IsFalse(SaveHost.HasStoredProgress);
        }

        private sealed class MemorySettingsStore : ISettingsStore
        {
            public GameSettings Stored;

            public bool TryLoad(out GameSettings settings)
            {
                settings = Stored;
                return settings != null;
            }

            public bool Save(GameSettings settings)
            {
                if (settings == null) return false;
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
            public string Location => "isolated save lifecycle test settings";
        }
    }
}
