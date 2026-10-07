using Aether.Core.Progression;
using Aether.Core.Settings;
using Aether.Gameplay.Progression;
using NUnit.Framework;

namespace Aether.Tests
{
    /// <summary>
    /// Unit tests for the persistent progression model.
    /// </summary>
    /// <remarks>
    /// These types are deliberately free of Unity object references and file I/O, which is what
    /// makes them testable without an Editor scene. That was the point of keeping persistence out
    /// of <see cref="ProgressionState"/> and <see cref="WorldState"/>: save data is exactly the
    /// kind of code that must not be verified only by playing the game.
    /// </remarks>
    public sealed class ProgressionStateTests
    {
        [Test]
        public void HasAbility_IsFalseForAnUnownedAbility()
        {
            var progression = new ProgressionState();

            Assert.That(progression.HasAbility(AbilityId.Rootbind), Is.False);
        }

        [Test]
        public void HasAbility_IsTrueForNone()
        {
            var progression = new ProgressionState();

            // None is the "ungated" sentinel, so every gate configured with it must pass.
            Assert.That(progression.HasAbility(AbilityId.None), Is.True);
        }

        [Test]
        public void GrantAbility_ReportsTrueOnlyOnFirstAcquisition()
        {
            var progression = new ProgressionState();

            Assert.That(progression.GrantAbility(AbilityId.Rootbind), Is.True);
            Assert.That(progression.GrantAbility(AbilityId.Rootbind), Is.False);
            Assert.That(progression.HasAbility(AbilityId.Rootbind), Is.True);
        }

        [Test]
        public void GrantAbility_IgnoresNone()
        {
            var progression = new ProgressionState();

            Assert.That(progression.GrantAbility(AbilityId.None), Is.False);
        }

        [Test]
        public void Reset_ClearsAbilities()
        {
            var progression = new ProgressionState();
            progression.GrantAbility(AbilityId.Rootbind);

            progression.Reset();

            Assert.That(progression.HasAbility(AbilityId.Rootbind), Is.False);
        }

        [Test]
        public void CopyTo_ProducesAnIndependentSnapshot()
        {
            var source = new ProgressionState();
            source.GrantAbility(AbilityId.Rootbind);

            var destination = new ProgressionState();
            source.CopyTo(destination);

            // Mutating the copy must not feed back into the object still being played.
            destination.Reset();

            Assert.That(destination.HasAbility(AbilityId.Rootbind), Is.False);
            Assert.That(source.HasAbility(AbilityId.Rootbind), Is.True,
                "CopyTo must deep-copy; a shared backing array would corrupt the live session.");
        }
    }

    /// <summary>Unit tests for the world-fact store.</summary>
    public sealed class WorldStateTests
    {
        [Test]
        public void Set_ReportsTrueOnlyTheFirstTime()
        {
            var world = new WorldState();

            Assert.That(world.Set("secret.greenway.hollow"), Is.True);
            Assert.That(world.Set("secret.greenway.hollow"), Is.False);
            Assert.That(world.IsSet("secret.greenway.hollow"), Is.True);
        }

        [Test]
        public void Set_IgnoresNullOrEmptyIds()
        {
            var world = new WorldState();

            Assert.That(world.Set(null), Is.False);
            Assert.That(world.Set(string.Empty), Is.False);
            Assert.That(world.FlagCount, Is.Zero);
        }

        [Test]
        public void IsSet_IsFalseForAnUnknownFact()
        {
            var world = new WorldState();

            Assert.That(world.IsSet("shortcut.greenway.north"), Is.False);
        }

        [Test]
        public void Reset_ClearsFactsAndCheckpoint()
        {
            var world = new WorldState();
            world.Set("shortcut.greenway.north");
            world.ActiveCheckpointId = "cp.greenway.entry";

            world.Reset();

            Assert.That(world.FlagCount, Is.Zero);
            Assert.That(world.ActiveCheckpointId, Is.Empty);
        }

        [Test]
        public void CopyTo_ProducesAnIndependentSnapshot()
        {
            var source = new WorldState();
            source.Set("secret.greenway.hollow");
            source.ActiveCheckpointId = "cp.greenway.entry";

            var destination = new WorldState();
            source.CopyTo(destination);
            destination.Reset();

            Assert.That(source.IsSet("secret.greenway.hollow"), Is.True);
            Assert.That(source.ActiveCheckpointId, Is.EqualTo("cp.greenway.entry"));
        }
    }

    /// <summary>Unit tests for the save container.</summary>
    public sealed class SaveDataTests
    {
        [Test]
        public void ResetForNewGame_ClearsBothHalves()
        {
            var save = new SaveData();
            save.Progression.GrantAbility(AbilityId.Rootbind);
            save.World.Set("secret.greenway.hollow");

            save.ResetForNewGame();

            Assert.That(save.Progression.HasAbility(AbilityId.Rootbind), Is.False);
            Assert.That(save.World.FlagCount, Is.Zero);
        }

        [Test]
        public void CopyFrom_CopiesEveryPersistentSectionWithoutSharingIt()
        {
            var source = new SaveData();
            source.Meta.Slot = 3;
            source.Meta.ChapterId = "region1.greenway";
            source.Meta.Discoveries = 1;
            source.Progression.GrantAbility(AbilityId.Rootbind);
            source.World.Set("secret.greenway.overhang");
            source.World.ActiveCheckpointId = "checkpoint.greenway.overhang";
            source.Collection.Record("secret.greenway.overhang");
            source.Characters.Record(CharacterCatalog.StalkerId);
            source.Achievements.Ensure("achievement.first.find").Progress = 1;

            var destination = new SaveData();
            destination.Collection.Record("stale.entry");
            destination.CopyFrom(source);

            Assert.That(destination.Meta.ChapterId, Is.EqualTo("region1.greenway"));
            Assert.That(destination.Progression.HasAbility(AbilityId.Rootbind), Is.True);
            Assert.That(destination.World.ActiveCheckpointId, Is.EqualTo("checkpoint.greenway.overhang"));
            Assert.That(destination.Collection.Has("secret.greenway.overhang"), Is.True);
            Assert.That(destination.Collection.Has("stale.entry"), Is.False);
            Assert.That(destination.Characters.HasMet(CharacterCatalog.StalkerId), Is.True);
            Assert.That(destination.Achievements.Find("achievement.first.find").Progress, Is.EqualTo(1));

            destination.Collection.Record("destination.only");
            Assert.That(source.Collection.Has("destination.only"), Is.False,
                        "loaded data must not share collection list storage with the live snapshot");
        }

        [Test]
        public void RecordEncounter_UnlocksOnlyWhenTheEnemyTypeIsKnown()
        {
            var save = new SaveData();

            Assert.That(CharacterCatalog.RecordEncounter(save, "unknown.enemy"), Is.Null);
            Assert.That(save.Characters.HasMet(CharacterCatalog.StalkerId), Is.False);
            Assert.That(CharacterCatalog.RecordEncounter(save, CharacterCatalog.StalkerId), Is.Not.Null);
            Assert.That(CharacterCatalog.RecordEncounter(save, CharacterCatalog.StalkerId), Is.Null,
                        "the same perception event must not repeat the codex notification");
        }
    }

    public sealed class SettingsDataTests
    {
        [Test]
        public void FrameRateClampUsesOnlySupportedRequests()
        {
            var settings = new GameSettings();
            settings.Graphics.FrameRateLimit = 0;
            settings.Clamp();
            Assert.That(settings.Graphics.FrameRateLimit, Is.EqualTo(30f));

            settings.Graphics.FrameRateLimit = 61;
            settings.Clamp();
            Assert.That(settings.Graphics.FrameRateLimit, Is.EqualTo(120f));
        }
    }
}
