using System.Collections.Generic;

namespace Aether.Gameplay.Progression
{
    /// <summary>What can be said about a chapter before anything is loaded.</summary>
    public enum ChapterAvailability
    {
        /// <summary>In the build, and playable now.</summary>
        Playable = 0,

        /// <summary>
        /// Not in the build.
        /// </summary>
        /// <remarks>
        /// The honest state for content that does not exist yet: the chapter is named, its place in
        /// the story is described, and the row says plainly that it is not part of this build. What
        /// the interface must never do is offer it and then load nothing.
        /// </remarks>
        NotInBuild = 1,
    }

    /// <summary>One chapter of the game: where it is, what it is called, and whether it exists.</summary>
    /// <remarks>
    /// <para>
    /// Chapters are code rather than an asset because there are four of them and they are bound to
    /// level resources by path — a data file that named a resource path is a file whose mistakes are
    /// found on a device. <c>tools/verify/gameflow.py</c> checks every playable chapter's path exists
    /// on disk, which is the check that matters.
    /// </para>
    /// <para>
    /// <b>The order here is the order of the game.</b> The chapters screen lists them in this order,
    /// the completion rules read them in this order, and the "next chapter" of the last playable one
    /// is the first <see cref="ChapterAvailability.NotInBuild"/> entry — which is how the interface
    /// can say "the next region is not in this build" without a special case.
    /// </para>
    /// </remarks>
    public sealed class ChapterDefinition
    {
        /// <summary>Stable id, written into save data. Never reorder or reuse.</summary>
        public readonly string Id;

        /// <summary>Localisation key for the chapter's name.</summary>
        public readonly string TitleKey;

        /// <summary>Localisation key for the one-line subtitle under the name.</summary>
        public readonly string SubtitleKey;

        /// <summary>Localisation key for the paragraph describing it.</summary>
        public readonly string BodyKey;

        /// <summary>Resource path of the level, without extension, or null when not in the build.</summary>
        public readonly string LevelPath;

        /// <summary>Where the chapter stands.</summary>
        public readonly ChapterAvailability Availability;

        public ChapterDefinition(string id, string titleKey, string subtitleKey, string bodyKey,
                                 string levelPath, ChapterAvailability availability)
        {
            Id = id;
            TitleKey = titleKey;
            SubtitleKey = subtitleKey;
            BodyKey = bodyKey;
            LevelPath = levelPath;
            Availability = availability;
        }

        /// <summary>True when this chapter can be started.</summary>
        public bool Playable
        {
            get { return Availability == ChapterAvailability.Playable && !string.IsNullOrEmpty(LevelPath); }
        }
    }

    /// <summary>
    /// Every chapter, in story order.
    /// </summary>
    /// <remarks>
    /// <b>One chapter is real and the rest say so.</b> The Greenway is a complete, solvable region:
    /// it has a start, two checkpoints, three encounters, a secret, an exit and a traversal proof.
    /// The three chapters after it are named, placed in the story and marked as not in this build —
    /// which is a different thing from being promised, and it is what the brief asks for: no ten
    /// fake missions that do nothing.
    /// </remarks>
    public static class ChapterCatalog
    {
        /// <summary>Stable id of the one playable chapter.</summary>
        public const string GreenwayId = "greenway";

        /// <summary>The world fact the level's exit records when the region is finished.</summary>
        public const string GreenwayCompletionFlag = "region.completed.greenway.north";

        /// <summary>Every chapter, in order.</summary>
        public static readonly ChapterDefinition[] All =
        {
            new ChapterDefinition(
                GreenwayId,
                "chapter.greenway.title",
                "chapter.greenway.subtitle",
                "chapter.greenway.body",
                "Levels/region1.greenway.level",
                ChapterAvailability.Playable),

            new ChapterDefinition(
                "hollow",
                "chapter.hollow.title",
                "chapter.hollow.subtitle",
                "chapter.hollow.body",
                null,
                ChapterAvailability.NotInBuild),

            new ChapterDefinition(
                "saltmarsh",
                "chapter.saltmarsh.title",
                "chapter.saltmarsh.subtitle",
                "chapter.saltmarsh.body",
                null,
                ChapterAvailability.NotInBuild),

            new ChapterDefinition(
                "theSpine",
                "chapter.spine.title",
                "chapter.spine.subtitle",
                "chapter.spine.body",
                null,
                ChapterAvailability.NotInBuild),
        };

        /// <summary>The chapter a new run begins in.</summary>
        public static ChapterDefinition First
        {
            get
            {
                for (int i = 0; i < All.Length; i++)
                {
                    if (All[i].Playable) return All[i];
                }

                return All[0];
            }
        }

        /// <summary>The chapter with an id, or null.</summary>
        public static ChapterDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id) return All[i];
            }

            return null;
        }

        /// <summary>How many chapters are playable.</summary>
        public static int PlayableCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < All.Length; i++)
                {
                    if (All[i].Playable) count++;
                }

                return count;
            }
        }

        /// <summary>
        /// The earliest playable chapter that has not been finished, or the first playable one.
        /// </summary>
        /// <remarks>
        /// "Where does CONTINUE put me" and "which chapter does the story stand on" are the same
        /// question, and this is the answer: the first chapter that is not yet complete.
        /// </remarks>
        public static ChapterDefinition Current(IReadOnlyList<string> completedFlags)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (!All[i].Playable) continue;
                if (completedFlags != null && completedFlags.Contains(CompletionFlagOf(All[i].Id))) continue;

                return All[i];
            }

            return First;
        }

        /// <summary>The world flag that marks a chapter complete.</summary>
        public static string CompletionFlagOf(string chapterId)
        {
            if (chapterId == GreenwayId) return GreenwayCompletionFlag;
            return "region.completed." + chapterId;
        }
    }
}
