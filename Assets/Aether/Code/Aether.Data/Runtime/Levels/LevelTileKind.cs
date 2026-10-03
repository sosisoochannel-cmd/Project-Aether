namespace Aether.Data.Levels
{
    /// <summary>
    /// Every tile the level format knows about.
    /// </summary>
    /// <remarks>
    /// The kind is what the game *means*; it says nothing about which renderer draws it. The
    /// builder maps kinds onto layers, and the bake tool maps kinds onto Tiles. Adding a visual
    /// variant that behaves exactly like an existing kind must reuse the existing kind rather
    /// than inventing a new one — see <see cref="LevelTileTraits"/> for the behaviour flags.
    /// </remarks>
    public enum LevelTileKind
    {
        /// <summary>Nothing here. The player falls through it.</summary>
        Empty = 0,

        /// <summary>Ordinary solid ground.</summary>
        Ground = 1,

        /// <summary>
        /// Solid, but reads as untouched rock rather than soil. Behaves exactly like
        /// <see cref="Ground"/>; the distinction exists purely so the data can describe the
        /// look of a place without the solver needing to know about art.
        /// </summary>
        DeepRock = 2,

        /// <summary>
        /// Solid, built as a platform. Used where the level asks the player to look at a
        /// surface and think "I can stand on that" — it is never used for terrain that
        /// exists only to block movement.
        /// </summary>
        Platform = 3,

        /// <summary>Background canopy. Non-solid; drawn behind everything.</summary>
        CanopyBack = 4,

        /// <summary>Foreground leaf litter. Non-solid; drawn over the player for depth.</summary>
        CanopyFront = 5,
    }

    /// <summary>
    /// Behaviour of a tile kind, derived rather than authored. Keeping this derived means a
    /// tile can never end up described one way in the data and behave another way at runtime.
    /// </summary>
    public static class LevelTileTraits
    {
        /// <summary>True when the player, enemies and the traversal solver treat this tile as a wall or floor.</summary>
        public static bool IsSolid(LevelTileKind kind)
        {
            switch (kind)
            {
                case LevelTileKind.Ground:
                case LevelTileKind.DeepRock:
                case LevelTileKind.Platform:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Layer band the tile is drawn in.</summary>
        public static LevelDrawLayer Layer(LevelTileKind kind)
        {
            switch (kind)
            {
                case LevelTileKind.CanopyBack:
                    return LevelDrawLayer.Background;
                case LevelTileKind.CanopyFront:
                    return LevelDrawLayer.Foreground;
                default:
                    return LevelDrawLayer.Physics;
            }
        }

        /// <summary>
        /// True when a body standing on this tile is supported. The solver uses this to decide
        /// where the player can come to rest, so it is the single definition of "standing".
        /// </summary>
        public static bool SupportsStanding(LevelTileKind kind)
        {
            return IsSolid(kind);
        }
    }

    /// <summary>Draw order band for a tile.</summary>
    public enum LevelDrawLayer
    {
        Background = 0,
        Physics = 1,
        Foreground = 2,
    }
}
