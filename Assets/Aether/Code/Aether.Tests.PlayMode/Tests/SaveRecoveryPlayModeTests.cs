using System;
using System.IO;
using Aether.Core.Progression;
using Aether.Gameplay;
using Aether.Gameplay.Storage;
using UnityEngine;
using NUnit.Framework;

namespace Aether.Tests.PlayMode
{
    /// <summary>Regression tests for the recovery path used by real on-device save files.</summary>
    public sealed class SaveRecoveryPlayModeTests
    {
        private string _folder;
        private string _root;
        private SaveSlotStore _store;

        [SetUp]
        public void SetUp()
        {
            _folder = "Aether-Recovery-Test-" + Guid.NewGuid().ToString("N");
            _store = new SaveSlotStore(1, _folder);
            _root = Path.GetDirectoryName(_store.Location);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void GameSession_SavesLatestSnapshotWhenApplicationPauses()
        {
            var host = new GameObject("GameSession pause persistence test");
            var store = new SaveSlotStore(1, _folder);
            try
            {
                GameSession session = host.AddComponent<GameSession>();
                session.Store = store;
                session.World.Set("test.lifecycle.pause-progress");

                host.SendMessage("OnApplicationPause", true);

                Assert.That(store.TryLoad(out SaveData loaded), Is.True,
                    "A backgrounded mobile session must persist its current snapshot.");
                Assert.That(loaded.World.IsSet("test.lifecycle.pause-progress"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GameSession_PauseSaveStillRespectsAutomaticWritePreference()
        {
            var host = new GameObject("GameSession pause autosave preference test");
            var store = new SaveSlotStore(1, _folder) { AutomaticWrites = false };
            try
            {
                GameSession session = host.AddComponent<GameSession>();
                session.Store = store;
                session.World.Set("test.lifecycle.must-not-autosave");

                host.SendMessage("OnApplicationPause", true);

                Assert.That(store.HasStoredProgress, Is.False,
                    "Pausing must not bypass the player's automatic-save preference.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SaveSlotStore_RecoversReadableBackupWhenPrimaryIsCorrupt()
        {
            var snapshot = new SaveData();
            snapshot.World.Set("test.recovery.backup-progress");
            Assert.That(_store.Save(snapshot, true), Is.True);

            string primary = File.ReadAllText(_store.Location);
            File.WriteAllText(_store.Location + ".bak", primary);
            File.WriteAllText(_store.Location, "{ interrupted write");

            Assert.That(_store.TryLoad(out SaveData recovered), Is.True,
                "A damaged primary must not hide a readable recovery copy.");
            Assert.That(recovered.World.IsSet("test.recovery.backup-progress"), Is.True);
            Assert.That(_store.Describe().Playable, Is.True,
                "The save-slot menu must see a recoverable backup as playable progress.");
        }

        [Test]
        public void SaveSlotStore_PrefersValidPrimaryOverOlderBackup()
        {
            var snapshot = new SaveData();
            snapshot.World.Set("test.recovery.old-progress");
            Assert.That(_store.Save(snapshot, true), Is.True);
            string oldDocument = File.ReadAllText(_store.Location);

            // A distinct snapshot models a later save. Keeping the old flag in the same
            // in-memory object would make both flags valid data, not prove which document won.
            var newerSnapshot = new SaveData();
            newerSnapshot.World.Set("test.recovery.new-progress");
            Assert.That(_store.Save(newerSnapshot, true), Is.True);
            File.WriteAllText(_store.Location + ".bak", oldDocument);

            Assert.That(_store.TryLoad(out SaveData loaded), Is.True);
            Assert.That(loaded.World.IsSet("test.recovery.new-progress"), Is.True,
                "A stale recovery copy must never override a valid primary save.");
            Assert.That(loaded.World.IsSet("test.recovery.old-progress"), Is.False);
        }

        [Test]
        public void SaveSlotStore_OrphanTemporaryFileIsNotMistakenForACompletedSave()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_store.Location));
            File.WriteAllText(_store.Location + ".tmp", "{ partially written save");

            Assert.That(_store.HasStoredProgress, Is.False,
                "An unfinished temporary document must not appear as a completed run.");
            SaveSlotInfo info = _store.Describe();
            Assert.That(info.Exists, Is.False);
            Assert.That(info.Playable, Is.False);
        }

        [Test]
        public void SaveSlotStore_FutureVersionIsUnavailableNotCorrupt()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_store.Location));
            var future = new SaveData { Version = SaveData.CurrentVersion + 100 };
            File.WriteAllText(_store.Location, JsonUtility.ToJson(future));

            SaveSlotInfo info = _store.Describe();
            Assert.That(info.Exists, Is.True);
            Assert.That(info.Unavailable, Is.True,
                "A save from a newer build should be preserved and reported as unavailable.");
            Assert.That(info.Corrupt, Is.False,
                "An unsupported format is not the same as a damaged save.");
            Assert.That(info.Playable, Is.False);
            Assert.That(_store.TryLoad(out SaveData ignored), Is.False);
        }

        [Test]
        public void SaveSlotStore_ClearRemovesPrimaryBackupAndInterruptedTemporaryFile()
        {
            var snapshot = new SaveData();
            Assert.That(_store.Save(snapshot, true), Is.True);
            File.WriteAllText(_store.Location + ".bak", "stale backup");
            File.WriteAllText(_store.Location + ".tmp", "interrupted write");

            Assert.That(_store.Clear(), Is.True);
            Assert.That(File.Exists(_store.Location), Is.False);
            Assert.That(File.Exists(_store.Location + ".bak"), Is.False);
            Assert.That(File.Exists(_store.Location + ".tmp"), Is.False);
            Assert.That(_store.HasStoredProgress, Is.False);
        }
    }
}
