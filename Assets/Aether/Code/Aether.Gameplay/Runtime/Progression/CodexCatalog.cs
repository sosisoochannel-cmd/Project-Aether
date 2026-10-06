using System.Collections.Generic;
using Aether.Core.Progression;

namespace Aether.Gameplay.Progression
{
    /// <summary>Where the game knows a character from.</summary>
    public enum CharacterUnlock
    {
        /// <summary>Known from the first frame: the character the player is.</summary>
        Always = 0,

        /// <summary>Met by entering a region where this creature lives.</summary>
        Encounter = 1,

        /// <summary>Met by finding something that proves they were here.</summary>
        Finding = 2,
    }

    /// <summary>One character, and what has to happen before the player knows they exist.</summary>
    public sealed class CharacterDefinition
    {
        /// <summary>Stable id, written into save data. Never reorder or reuse.</summary>
        public readonly string Id;

        /// <summary>Localisation key for the name.</summary>
        public readonly string TitleKey;

        /// <summary>Localisation key for the role line under the name.</summary>
        public readonly string RoleKey;

        /// <summary>Localisation key for the paragraph about them.</summary>
        public readonly string BodyKey;

        /// <summary>How they are met.</summary>
        public readonly CharacterUnlock Unlock;

        /// <summary>
        /// What unlocks them: an enemy type id for <see cref="CharacterUnlock.Encounter"/>, or the
        /// world flag of a find for <see cref="CharacterUnlock.Finding"/>. Null when always known.
        /// </summary>
        public readonly string UnlockId;

        public CharacterDefinition(string id, string titleKey, string roleKey, string bodyKey,
                                   CharacterUnlock unlock, string unlockId)
        {
            Id = id;
            TitleKey = titleKey;
            RoleKey = roleKey;
            BodyKey = bodyKey;
            Unlock = unlock;
            UnlockId = unlockId;
        }
    }

    /// <summary>
    /// Everyone the game knows about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every entry is tied to something that exists.</b> The wayfarer is the character the player
    /// is; the stalker is the creature that is actually placed in the Greenway, named by the content
    /// that defines it; the precursor is known only from the one find the level actually contains.
    /// A character list is easy to fill with names nothing else in the game refers to, and that is
    /// precisely what this list refuses to do.
    /// </para>
    /// <para>
    /// <b>The player's and the precursor's names are working titles.</b> Nothing in the level data
    /// names them: the find is authored as a <c>master_clue</c> and the player is a body without a
    /// story. They are named here so the screen can be built and reviewed, and renaming them is one
    /// string each, because the screens read keys and never prose.
    /// </para>
    /// </remarks>
    public static class CharacterCatalog
    {
        /// <summary>The character the player is.</summary>
        public const string WayfarerId = "wayfarer";

        /// <summary>The creature the Greenway is populated with, by its content-defined type id.</summary>
        public const string StalkerId = "forest_stalker";

        /// <summary>Whoever the Greenway's one find is a trace of.</summary>
        public const string PrecursorId = "precursor";

        /// <summary>Every character, in the order the list shows them.</summary>
        public static readonly CharacterDefinition[] All =
        {
            new CharacterDefinition(
                WayfarerId,
                "character.wayfarer.title", "character.wayfarer.role", "character.wayfarer.body",
                CharacterUnlock.Always, null),

            new CharacterDefinition(
                StalkerId,
                "character.stalker.title", "character.stalker.role", "character.stalker.body",
                CharacterUnlock.Encounter, StalkerId),

            new CharacterDefinition(
                PrecursorId,
                "character.precursor.title", "character.precursor.role", "character.precursor.body",
                CharacterUnlock.Finding, "secret.greenway.overhang"),
        };

        /// <summary>Whether the player has met a character in this run.</summary>
        /// <remarks>
        /// Always-known characters are met the moment there is a run at all, which is why this takes
        /// the snapshot rather than a bool: without a run there is nobody to have met anybody.
        /// </remarks>
        public static bool IsMet(SaveData save, CharacterDefinition character)
        {
            if (save == null || character == null) return false;
            if (character.Unlock == CharacterUnlock.Always) return true;

            if (character.Unlock == CharacterUnlock.Encounter)
            {
                return save.Characters != null && save.Characters.HasMet(character.UnlockId);
            }

            // A find is met through the world flag the level authors, so the two can never disagree
            // about whether the player has been there.
            return save.World != null && !string.IsNullOrEmpty(character.UnlockId)
                   && save.World.IsSet(character.UnlockId);
        }

        /// <summary>How many characters have been met.</summary>
        public static int MetCount(SaveData save)
        {
            int count = 0;
            for (int i = 0; i < All.Length; i++)
            {
                if (IsMet(save, All[i])) count++;
            }

            return count;
        }

        /// <summary>The character with an id, or null.</summary>
        public static CharacterDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id) return All[i];
            }

            return null;
        }

        /// <summary>The character an enemy type is, or null when no character is that creature.</summary>
        public static CharacterDefinition ForEnemyType(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return null;

            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Unlock == CharacterUnlock.Encounter && All[i].UnlockId == typeId) return All[i];
            }

            return null;
        }

        /// <summary>
        /// Records every character a region's contents prove the player has met.
        /// </summary>
        /// <remarks>
        /// Called once when a region has been built, with the enemy type ids it placed. "Met" means
        /// the player has been in a region where the creature lives, which is a fact the level itself
        /// establishes — no proximity test, no chance of a player missing a character that was
        /// standing in front of them.
        /// </remarks>
        public static List<CharacterDefinition> RecordEncounters(SaveData save, IReadOnlyList<string> enemyTypeIds)
        {
            var met = new List<CharacterDefinition>();
            if (save == null || enemyTypeIds == null) return met;
            if (save.Characters == null) save.Characters = new CharacterState();

            for (int i = 0; i < enemyTypeIds.Count; i++)
            {
                CharacterDefinition character = ForEnemyType(enemyTypeIds[i]);
                if (character == null) continue;
                if (!save.Characters.Record(character.Id)) continue;

                met.Add(character);
            }

            return met;
        }
    }

    /// <summary>Where a collection entry comes from, and therefore what makes it count as found.</summary>
    public enum CollectionSource
    {
        /// <summary>A find in a region, recorded as a world flag.</summary>
        Finding = 0,

        /// <summary>An ability the player owns.</summary>
        Ability = 1,

        /// <summary>A region, complete or merely visited.</summary>
        Region = 2,
    }

    /// <summary>One entry in the collection.</summary>
    public sealed class CollectionEntry
    {
        /// <summary>Stable id: a world flag, an ability name, or a chapter id.</summary>
        public readonly string Id;

        /// <summary>Localisation key for its name.</summary>
        public readonly string TitleKey;

        /// <summary>Localisation key for the line about it.</summary>
        public readonly string BodyKey;

        /// <summary>Localisation key for why it is still locked, when it is not findable yet.</summary>
        public readonly string LockedKey;

        /// <summary>Where this entry comes from.</summary>
        public readonly CollectionSource Source;

        /// <summary>
        /// True when the entry cannot be obtained at all yet, whatever the player does.
        /// </summary>
        /// <remarks>
        /// The distinction the whole screen turns on: "not found yet" is a player's job, "not in this
        /// build" is the project's, and a list that said the first about the second would be lying to
        /// somebody who is doing everything right.
        /// </remarks>
        public readonly bool NotInBuild;

        public CollectionEntry(string id, string titleKey, string bodyKey, CollectionSource source,
                               bool notInBuild = false, string lockedKey = null)
        {
            Id = id;
            TitleKey = titleKey;
            BodyKey = bodyKey;
            LockedKey = lockedKey;
            Source = source;
            NotInBuild = notInBuild;
        }
    }

    /// <summary>A group of entries, as the collection screen lists them.</summary>
    public sealed class CollectionCategory
    {
        public readonly string Id;
        public readonly string TitleKey;
        public readonly string NoteKey;
        public readonly CollectionEntry[] Entries;

        public CollectionCategory(string id, string titleKey, string noteKey, CollectionEntry[] entries)
        {
            Id = id;
            TitleKey = titleKey;
            NoteKey = noteKey;
            Entries = entries;
        }
    }

    /// <summary>
    /// Everything that can be collected, as categories with entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The collection is not a second save file: it is a <i>view</i> of what a run already knows.
    /// Findings are the world flags the level authors, abilities are what the progression state
    /// holds, regions are the completion flags the exits record. That is why nothing here needs to
    /// be written down twice, and why a find can never appear in the collection without also being
    /// recorded in the world.
    /// </para>
    /// <para>
    /// Entries that exist in code but cannot be reached in this build are listed, marked as not in
    /// this build, and never counted as missing. That is the honest shape of a collection screen in
    /// a vertical slice: the list is the plan, and the state is the truth.
    /// </para>
    /// </remarks>
    public static class CollectionCatalog
    {
        /// <summary>Category ids, so screens and gates can name them.</summary>
        public const string FindingsId = "findings";
        public const string AbilitiesId = "abilities";
        public const string RegionsId = "regions";

        /// <summary>Every category, in the order the screen lists them.</summary>
        public static readonly CollectionCategory[] All =
        {
            new CollectionCategory(FindingsId, "collection.findings", "collection.findings.note",
                new[]
                {
                    new CollectionEntry("secret.greenway.overhang",
                        "entry.secret.greenway.overhang",
                        "entry.secret.greenway.overhang.body",
                        CollectionSource.Finding,
                        false, "collection.locked.unfound"),
                }),

            new CollectionCategory(AbilitiesId, "collection.abilities", "collection.abilities.note",
                new[]
                {
                    new CollectionEntry("Rootbind",
                        "entry.ability.rootbind",
                        "entry.ability.rootbind.body",
                        CollectionSource.Ability,
                        true, "collection.locked.laterRegion"),
                }),

            new CollectionCategory(RegionsId, "collection.regions", "collection.regions.note",
                new[]
                {
                    new CollectionEntry(ChapterCatalog.GreenwayId,
                        "chapter.greenway.title",
                        "chapter.greenway.subtitle",
                        CollectionSource.Region),

                    new CollectionEntry("hollow",
                        "chapter.hollow.title",
                        "chapter.hollow.subtitle",
                        CollectionSource.Region,
                        true, "collection.locked.notInBuild"),

                    new CollectionEntry("saltmarsh",
                        "chapter.saltmarsh.title",
                        "chapter.saltmarsh.subtitle",
                        CollectionSource.Region,
                        true, "collection.locked.notInBuild"),

                    new CollectionEntry("theSpine",
                        "chapter.spine.title",
                        "chapter.spine.subtitle",
                        CollectionSource.Region,
                        true, "collection.locked.notInBuild"),
                }),
        };

        /// <summary>Whether an entry has been found in this run.</summary>
        public static bool IsFound(SaveData save, CollectionEntry entry)
        {
            if (save == null || entry == null) return false;

            switch (entry.Source)
            {
                case CollectionSource.Finding:
                    return save.World != null && save.World.IsSet(entry.Id);

                case CollectionSource.Ability:
                    return save.Progression != null && OwnsAbility(save, entry.Id);

                case CollectionSource.Region:
                    return save.World != null
                           && save.World.IsSet(ChapterCatalog.CompletionFlagOf(entry.Id));

                default:
                    return false;
            }
        }

        /// <summary>How many entries of a category have been found.</summary>
        public static int FoundCount(SaveData save, CollectionCategory category)
        {
            if (category == null) return 0;

            int count = 0;
            for (int i = 0; i < category.Entries.Length; i++)
            {
                if (IsFound(save, category.Entries[i])) count++;
            }

            return count;
        }

        /// <summary>How many entries of a category could be found in this build.</summary>
        /// <remarks>
        /// The denominator the screen shows. Counting entries that are not in the build would make a
        /// complete collection look unfinished for as long as the game is a vertical slice.
        /// </remarks>
        public static int ObtainableCount(CollectionCategory category)
        {
            if (category == null) return 0;

            int count = 0;
            for (int i = 0; i < category.Entries.Length; i++)
            {
                if (!category.Entries[i].NotInBuild) count++;
            }

            return count;
        }

        /// <summary>How many entries have been found across every category.</summary>
        public static int TotalFound(SaveData save)
        {
            int count = 0;
            for (int i = 0; i < All.Length; i++) count += FoundCount(save, All[i]);

            return count;
        }

        /// <summary>The category with an id, or null.</summary>
        public static CollectionCategory Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id) return All[i];
            }

            return null;
        }

        private static bool OwnsAbility(SaveData save, string id)
        {
            // The catalogue stores the ability by name, which is what the enum's own ToString gives
            // and what a save file holds as a number. Parsing it here keeps one spelling of an
            // ability's identity in the data and one in the enum.
            if (string.Equals(id, "Rootbind", System.StringComparison.Ordinal))
            {
                return save.Progression.HasAbility(AbilityId.Rootbind);
            }

            return false;
        }
    }
}
