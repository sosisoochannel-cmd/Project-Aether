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
        public static readonly Color Ground = new Color(0.30f, 0.27f, 0.20f, 1f);
        public static readonly Color DeepRock = new Color(0.16f, 0.18f, 0.16f, 1f);
        public static readonly Color Platform = new Color(0.33f, 0.46f, 0.28f, 1f);
        public static readonly Color CanopyBack = new Color(0.08f, 0.19f, 0.13f, 0.94f);
        public static readonly Color CanopyFront = new Color(0.13f, 0.30f, 0.17f, 0.70f);

        // -- entities ---------------------------------------------------------------------------
        //
        // One vocabulary, learned in the first minute: warm and bright means danger, dull and cool
        // means it is safe to go there. Shapes carry the rest — a square attacks, a ring saves, a
        // diamond is a secret — so the player never has to read text to know what a thing is.

        public static readonly Color Player = new Color(0.86f, 0.92f, 1f, 1f);
        public static readonly Color PlayerHit = new Color(1f, 0.45f, 0.45f, 1f);
        public static readonly Color PlayerDead = new Color(0.45f, 0.45f, 0.5f, 1f);

        public static readonly Color EnemyIdle = new Color(0.72f, 0.4f, 0.2f, 0.85f);
        public static readonly Color EnemyAlert = new Color(0.95f, 0.6f, 0.25f, 1f);
        public static readonly Color EnemyWindup = new Color(1f, 0.85f, 0.45f, 1f);
        public static readonly Color EnemyActive = new Color(1f, 0.35f, 0.25f, 1f);
        public static readonly Color EnemyRecovering = new Color(0.45f, 0.55f, 0.62f, 1f);
        public static readonly Color EnemyStagger = new Color(0.95f, 0.95f, 0.6f, 1f);
        public static readonly Color EnemyDead = new Color(0.3f, 0.28f, 0.26f, 1f);

        public static readonly Color CheckpointIdle = new Color(0.55f, 0.9f, 1f, 0.85f);
        public static readonly Color CheckpointActive = new Color(0.78f, 1f, 1f, 1f);
        public static readonly Color Secret = new Color(0.98f, 0.86f, 0.42f, 0.95f);
        public static readonly Color Exit = new Color(0.5f, 1f, 0.6f, 0.6f);
        public static readonly Color StoryMarker = new Color(0.86f, 0.80f, 0.62f, 0.5f);

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
