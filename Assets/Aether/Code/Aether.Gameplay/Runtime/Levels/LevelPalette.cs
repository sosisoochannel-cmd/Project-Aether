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
        public static readonly Color Ground = new Color(0.16f, 0.25f, 0.29f, 1f);
        public static readonly Color DeepRock = new Color(0.075f, 0.12f, 0.16f, 1f);
        public static readonly Color Platform = new Color(0.32f, 0.47f, 0.52f, 1f);
        public static readonly Color CanopyBack = new Color(0.08f, 0.21f, 0.24f, 0.9f);
        public static readonly Color CanopyFront = new Color(0.14f, 0.32f, 0.38f, 0.62f);

        // -- entities ---------------------------------------------------------------------------
        //
        // One vocabulary, learned in the first minute: warm and bright means danger, dull and cool
        // means it is safe to go there. The illustrated combatants carry identity, while a ring marks a checkpoint or enemy attack and a
        // diamond marks a secret — so the player never has to read text to understand key feedback.

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
        public static readonly Color Secret = new Color(0.82f, 0.92f, 1f, 0.95f);
        public static readonly Color Exit = new Color(0.5f, 1f, 0.6f, 0.6f);
        public static readonly Color StoryMarker = new Color(0.82f, 0.90f, 0.94f, 0.5f);

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
