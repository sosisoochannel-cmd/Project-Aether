using Aether.Core.Progression;
using UnityEngine;

namespace Aether.Core.Events
{
    /// <summary>
    /// Raised when the player gains a traversal or combat ability for the first time.
    /// </summary>
    /// <remarks>
    /// Presentation listens for this to play the unlock sequence; the systems that open gated routes
    /// listen for it to become interactable. Neither needs a reference to the other.
    /// </remarks>
    public readonly struct AbilityUnlockedEvent
    {
        /// <summary>Which ability was granted.</summary>
        public readonly AbilityId Ability;

        /// <summary>World position where the unlock happened, for camera framing and effects.</summary>
        public readonly Vector2 Position;

        public AbilityUnlockedEvent(AbilityId ability, Vector2 position)
        {
            Ability = ability;
            Position = position;
        }
    }

    /// <summary>
    /// Raised when the player activates a checkpoint, including re-activation of one already used.
    /// </summary>
    /// <remarks>
    /// A checkpoint is the moment the player commits progress, so this is the natural autosave
    /// trigger and the natural place to refresh health. It is deliberately not raised on respawn.
    /// </remarks>
    public readonly struct CheckpointActivatedEvent
    {
        /// <summary>Stable id of the checkpoint, matching the id authored in level data.</summary>
        public readonly string CheckpointId;

        /// <summary>Respawn position recorded by this checkpoint.</summary>
        public readonly Vector2 RespawnPosition;

        public CheckpointActivatedEvent(string checkpointId, Vector2 respawnPosition)
        {
            CheckpointId = checkpointId;
            RespawnPosition = respawnPosition;
        }
    }

    /// <summary>
    /// Raised when a one-shot world fact is recorded for the first time — a secret found, a region
    /// completed.
    /// </summary>
    /// <remarks>
    /// The save file is a list of these facts, so anything that wants to react to "the world
    /// changed" needs exactly this and nothing more. Achivements and the collection screen are the
    /// two listeners today; both would otherwise have to poll the world state every frame for
    /// something that happens a handful of times in a run.
    /// </remarks>
    public readonly struct WorldFlagSetEvent
    {
        /// <summary>The flag that was set, exactly as authored in level data.</summary>
        public readonly string FlagId;

        public WorldFlagSetEvent(string flagId)
        {
            FlagId = flagId;
        }
    }

    /// <summary>Raised when the player dies, before the respawn beat starts.</summary>
    /// <remarks>
    /// A run's death count is part of its record — it is what a "no deaths" achievement is measured
    /// against — so it is counted where the death happens rather than derived later.
    /// </remarks>
    public readonly struct PlayerDiedEvent
    {
        /// <summary>How many times the player has died in this run, this death included.</summary>
        public readonly int Deaths;

        public PlayerDiedEvent(int deaths)
        {
            Deaths = deaths;
        }
    }

    /// <summary>Raised the first time an enemy genuinely notices the player in this run.</summary>
    public readonly struct CharacterMetEvent
    {
        /// <summary>Stable id of the character entry recorded in the codex.</summary>
        public readonly string CharacterId;

        public CharacterMetEvent(string characterId)
        {
            CharacterId = characterId;
        }
    }

    /// <summary>Raised when a region has been built and the player is standing in it.</summary>
    /// <remarks>
    /// Deliberately after the level exists rather than before the load: a listener that wants to
    /// look at the region — the collection counting its secrets — has to be able to, and "we are
    /// about to load" would not allow that.
    /// </remarks>
    public readonly struct RegionEnteredEvent
    {
        /// <summary>Stable id of the chapter, as the chapter catalogue knows it.</summary>
        public readonly string ChapterId;

        public RegionEnteredEvent(string chapterId)
        {
            ChapterId = chapterId;
        }
    }
}
