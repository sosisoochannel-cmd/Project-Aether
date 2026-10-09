using System.Collections.Generic;
using UnityEngine;

namespace Aether.Data.Levels
{
    /// <summary>
    /// Kinds of landmark a level can place. Kept deliberately small: everything here is used by
    /// The Greenway today, and a kind that nothing reads should not exist.
    /// </summary>
    public enum LevelEntityKind
    {
        PlayerStart = 0,

        /// <summary>
        /// A respawn point. Its <see cref="LevelEntity.RespawnPosition"/> is the exact spot the
        /// player is returned to, which may differ from where the landmark is drawn.
        /// </summary>
        Checkpoint = 1,

        /// <summary>A hostile placed in the level. <see cref="LevelEntity.TypeId"/> names its archetype.</summary>
        Enemy = 2,

        /// <summary>A find. Carries the flag that records it in the save file.</summary>
        Discovery = 3,

        /// <summary>A non-interactive storytelling detail. Purely environmental.</summary>
        StoryMarker = 4,

        /// <summary>Where the region ends.</summary>
        Exit = 5,

        /// <summary>
        /// A named point in space with no behaviour of its own. Traversal connections are written
        /// against anchors so that a level can make a claim about a piece of ground ("this stretch
        /// is an uninterrupted walk") without inventing a gameplay object to hang it on.
        /// </summary>
        Anchor = 6,
    }

    /// <summary>
    /// One thing placed in the level. Positions are in tile coordinates with row 0 at the top of
    /// the map, matching the textual format. Tile-to-world conversion lives on
    /// <see cref="LevelData"/> so that the vertical flip is defined in exactly one place.
    /// </summary>
    public sealed class LevelEntity
    {
        public LevelEntity(LevelEntityKind kind, string id, Vector2Int position)
        {
            Kind = kind;
            Id = id;
            Position = position;
        }

        /// <summary>What this entity is.</summary>
        public LevelEntityKind Kind { get; }

        /// <summary>
        /// Stable identity, unique inside the level. The bake tool and the validation pass both
        /// rely on it, and it is what a future save-file migration would key off.
        /// </summary>
        public string Id { get; }

        /// <summary>Tile the entity occupies. For characters this is the tile they stand on.</summary>
        public Vector2Int Position { get; }

        /// <summary>Enemy archetype id, or null for other kinds.</summary>
        public string TypeId { get; internal set; }

        /// <summary>Half-width in tiles; -1 inherits the archetype default, zero holds position.</summary>
        public int PatrolTiles { get; internal set; } = -1;

        /// <summary>Exact respawn tile for a checkpoint, so the respawn can hug the ground.</summary>
        public Vector2Int RespawnPosition { get; internal set; }

        /// <summary>Descriptive kind for a discovery or story marker ("master_clue", "cut_trail", ...).</summary>
        public string MarkerKind { get; internal set; }

        /// <summary>
        /// Progression flag raised when this entity is used. Empty means the entity carries no
        /// persistent state, which is the case for story markers and the exit.
        /// </summary>
        public string Flag { get; internal set; }

        /// <summary>
        /// Flavour text carried in the data for the Editor and for documentation. It is never
        /// shown to the player as a UI element — environmental storytelling stays environmental.
        /// </summary>
        public string Note { get; internal set; }
    }
}
