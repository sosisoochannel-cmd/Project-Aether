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
    /// <summary>Regression coverage for Continue and the non-destructive New Game guard.</summary>
    public sealed class SaveSlotSelectionPlayModeTests
    {
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = "Aether-SlotSelection-Test-" + Guid.NewGuid().ToString("N");
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
        public void ContinueLoadsTheMostRecentPlayableSlotAndIgnoresACorruptNewerSlot()
        {
            GameSession first = SaveHost.BeginNewGameIn(1, ChapterCatalog.First.Id);
            Assert.IsNotNull(first);
            Assert.IsTrue(first.SetWorldFlag("test.selection.slot-one"));
            Assert.IsTrue(SaveHost.SaveNow());

            GameSession second = SaveHost.BeginNewGameIn(2, ChapterCatalog.First.Id);
            Assert.IsNotNull(second);
            Assert.IsTrue(second.SetWorldFlag("test.selection.slot-two"));
            Assert.IsTrue(SaveHost.SaveNow());

            // A damaged slot must not win Continue just because a file exists.
            SaveSlotStore damaged = SaveSlots.For(3);
            Directory.CreateDirectory(Path.GetDirectoryName(damaged.Location));
            File.WriteAllText(damaged.Location, "{ interrupted save");

            SaveHost.ResetForTests(GameSession.Instance, _folder);
            Assert.IsTrue(SaveHost.ContinueMostRecent(), "Continue did not find a playable run.");
            Assert.AreEqual(2, SaveHost.ActiveSlot, "Continue did not choose the most recently saved playable slot.");

            GameSession restored = SaveHost.Ensure();
            Assert.IsTrue(restored.World.IsSet("test.selection.slot-two"),
                "Continue did not restore the chosen slot's progress.");
            Assert.IsFalse(restored.World.IsSet("test.selection.slot-one"),
                "Continue mixed progress from two different slots.");
            Assert.IsTrue(SaveSlots.Describe(3).Corrupt,
                "The damaged slot was not reported distinctly from an empty slot.");
        }

        [Test]
        public void NewGameRefusesToReplaceAnExistingRunWithoutExplicitConfirmation()
        {
            GameSession original = SaveHost.BeginNewGameIn(1, ChapterCatalog.First.Id);
            Assert.IsNotNull(original);
            Assert.IsTrue(original.SetWorldFlag("test.selection.preserve-existing"));
            Assert.IsTrue(SaveHost.SaveNow());

            GameSession rejected = SaveHost.BeginNewGameIn(1, ChapterCatalog.First.Id);
            Assert.IsNull(rejected, "New Game replaced a populated slot without explicit confirmation.");
            Assert.AreSame(original, GameSession.Instance, "A rejected New Game replaced the live session.");
            Assert.AreEqual(1, SaveHost.ActiveSlot);
            Assert.IsTrue(original.World.IsSet("test.selection.preserve-existing"),
                "The original in-memory run was changed by a rejected New Game.");
            Assert.IsTrue(SaveHost.Peek(1).World.IsSet("test.selection.preserve-existing"),
                "The original stored run was overwritten by a rejected New Game.");
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
            public string Location => "isolated slot selection test settings";
        }
    }
}
