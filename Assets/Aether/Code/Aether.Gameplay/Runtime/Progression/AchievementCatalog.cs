using System.Collections.Generic;

namespace Aether.Gameplay.Progression.Achievements
{
    /// <summary>What a player has to do to unlock an achievement.</summary>
    /// <remarks>
    /// The trigger is a stable id rather than a delegate, so the catalogue can be read — by the
    /// interface, by the gate, by a person — without running anything, and so that the code which
    /// raises a trigger is the only code that decides when one fires.
    /// </remarks>
    public enum AchievementTrigger
    {
        /// <summary>No trigger. Used as a "nothing here" sentinel so a row can be unconfigured.</summary>
        None = 0,

        /// <summary>The player entered a region in this run for the first time.</summary>
        FirstRegionEntered = 1,

        /// <summary>The player lit a checkpoint.</summary>
        CheckpointLit = 2,

        /// <summary>Every secret of the region the player is in has been found.</summary>
        AllFindingsInRegion = 3,

        /// <summary>The region's exit was reached.</summary>
        RegionExitReached = 4,

        /// <summary>The region's exit was reached without dying in this run.</summary>
        RegionExitWithoutDying = 5,
    }

    /// <summary>One achievement: what it is called, how it is earned, and whether it is honest to show.</summary>
    public sealed class AchievementDefinition
    {
        /// <summary>Stable id, written into save data. Never reorder or reuse.</summary>
        public readonly string Id;

        /// <summary>Localisation key for the name.</summary>
        public readonly string TitleKey;

        /// <summary>Localisation key for the line describing how it is earned.</summary>
        public readonly string BodyKey;

        /// <summary>What raises it.</summary>
        public readonly AchievementTrigger Trigger;

        /// <summary>How many the trigger counts before it unlocks. 1 for a one-shot achievement.</summary>
        public readonly int Target;

        public AchievementDefinition(string id, string titleKey, string bodyKey,
                                     AchievementTrigger trigger, int target)
        {
            Id = id;
            TitleKey = titleKey;
            BodyKey = bodyKey;
            Trigger = trigger;
            Target = target < 1 ? 1 : target;
        }
    }

    /// <summary>
    /// The achievements that exist, and the rule that none of them is a decoration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every entry here can be earned in the build that ships it.</b> That is a stronger rule than
    /// it sounds: an achievement list is the easiest place in a game to write five rows that never
    /// fire, and a player who cannot tell the difference between a hard achievement and a broken one
    /// will assume the worst. Each of the five below is raised by an event that a player can
    /// actually reach in the Greenway, and <c>tools/verify/gameflow.py</c> checks that every
    /// definition's trigger is one the runtime service handles, so a row added later cannot silently
    /// do nothing.
    /// </para>
    /// <para>
    /// The fifth is deliberately hard rather than impossible: reaching the exit without dying is
    /// exactly the run a good player has on their second or third attempt, and the level's two
    /// checkpoints mean a fall costs progress rather than the attempt.
    /// </para>
    /// </remarks>
    public static class AchievementCatalog
    {
        /// <summary>Every achievement, in the order the list shows them.</summary>
        public static readonly AchievementDefinition[] All =
        {
            new AchievementDefinition(
                "ach.greenway.enter",
                "ach.enter.title", "ach.enter.body",
                AchievementTrigger.FirstRegionEntered, 1),

            new AchievementDefinition(
                "ach.greenway.checkpoint",
                "ach.checkpoint.title", "ach.checkpoint.body",
                AchievementTrigger.CheckpointLit, 1),

            new AchievementDefinition(
                "ach.greenway.findings",
                "ach.findings.title", "ach.findings.body",
                AchievementTrigger.AllFindingsInRegion, 1),

            new AchievementDefinition(
                "ach.greenway.exit",
                "ach.exit.title", "ach.exit.body",
                AchievementTrigger.RegionExitReached, 1),

            new AchievementDefinition(
                "ach.greenway.unbroken",
                "ach.unbroken.title", "ach.unbroken.body",
                AchievementTrigger.RegionExitWithoutDying, 1),
        };

        /// <summary>How many achievements exist.</summary>
        public static int Count
        {
            get { return All.Length; }
        }

        /// <summary>The achievement with an id, or null.</summary>
        public static AchievementDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id) return All[i];
            }

            return null;
        }

        /// <summary>The ids of every achievement that uses a trigger. For the gate and for tests.</summary>
        public static List<string> IdsFor(AchievementTrigger trigger)
        {
            var ids = new List<string>();
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Trigger == trigger) ids.Add(All[i].Id);
            }

            return ids;
        }
    }
}
