using Aether.Core.Progression;
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
    }
}
