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
}
