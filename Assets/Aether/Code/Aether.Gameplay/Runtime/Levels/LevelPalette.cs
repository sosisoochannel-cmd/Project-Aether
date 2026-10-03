using Aether.Data.Levels;
using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// Placeholder colours for tile kinds, in one place.
    /// </summary>
    /// <remarks>
    /// The runtime builder and the bake tool both draw the level before there is art. Sharing the
    /// palette means the scene an artist opens looks like the game, so "the level" means the same
    /// thing to both of them. Every value here is replaced by real art later; nothing in gameplay
    /// reads it.
    /// </remarks>
    public static class LevelPalette
    {
        public static readonly Color Ground = new Color(0.44f, 0.36f, 0.26f, 1f);
        public static readonly Color DeepRock = new Color(0.27f, 0.24f, 0.22f, 1f);
        public static readonly Color Platform = new Color(0.46f, 0.53f, 0.34f, 1f);
        public static readonly Color CanopyBack = new Color(0.14f, 0.27f, 0.17f, 0.9f);
        public static readonly Color CanopyFront = new Color(0.20f, 0.40f, 0.20f, 0.65f);

        /// <summary>Colour used for a tile kind.</summary>
        public static Color For(LevelTileKind kind)
        {
            switch (kind)
            {
                case LevelTileKind.DeepRock: return DeepRock;
                case LevelTileKind.Platform: return Platform;
                case LevelTileKind.CanopyBack: return CanopyBack;
                case LevelTileKind.CanopyFront: return CanopyFront;
                default: return Ground;
            }
        }
    }
}
