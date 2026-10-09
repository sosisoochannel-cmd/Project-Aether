using System;
using System.Collections.Generic;
using Aether.Data.Config;
using Aether.Data.Levels;
using Aether.Gameplay.Enemies;
using Aether.Gameplay.Player;
using Aether.Gameplay.Support;
using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>Everything the director needs to run a level, built from level data.</summary>
    public sealed class BuiltLevel
    {
        public BuiltLevel(GameObject root, LevelData data)
        {
            Root = root;
            Data = data;
        }

        /// <summary>Root of everything belonging to this level.</summary>
        public GameObject Root { get; }

        /// <summary>The data this was built from.</summary>
        public LevelData Data { get; }

        /// <summary>Where the player starts, as a body centre.</summary>
        public Vector2 PlayerStartCentre { get; set; }

        /// <summary>Where the player starts, as the bottom of their feet.</summary>
        public Vector2 PlayerStartFeet { get; set; }

        /// <summary>Enemies in the level, in file order, paired with where they were placed.</summary>
        public List<EnemySpawn> Enemies { get; } = new List<EnemySpawn>();

        /// <summary>Checkpoints in the level, in file order.</summary>
        public List<CheckpointTrigger> Checkpoints { get; } = new List<CheckpointTrigger>();

        /// <summary>Discoveries in the level, in file order.</summary>
        public List<DiscoveryTrigger> Discoveries { get; } = new List<DiscoveryTrigger>();

        /// <summary>The region exit, if the level has one.</summary>
        public LevelExitTrigger Exit { get; set; }

        /// <summary>An enemy and the tile it is meant to guard.</summary>
        public readonly struct EnemySpawn
        {
            public EnemySpawn(EnemyController controller, LevelEntity source, Vector2 feet)
            {
                Controller = controller;
                Source = source;
                Feet = feet;
            }

            public EnemyController Controller { get; }

            public LevelEntity Source { get; }

            /// <summary>Placement position, as the bottom of the enemy's feet.</summary>
            public Vector2 Feet { get; }
        }
    }

    /// <summary>
    /// Turns parsed level data into colliders, placeholders and gameplay objects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing here is saved.</b> The scene is a view of the data, rebuilt on every load, so a
    /// hand-edit to a baked scene cannot survive and cannot become a second source of truth. The
    /// Editor bake tool produces a Tilemap version of the same grid for artists; this produces the
    /// version the game runs.
    /// </para>
    /// <para>
    /// Solid tiles are merged into maximal rectangles before anything is created, which is why The
    /// Greenway's roughly two thousand solid cells become a few dozen objects instead of a few
    /// thousand. Fewer objects is not a guess about performance: it is a measurement-free choice made
    /// because it is also the simpler thing to reason about, and it is the one thing the mobile
    /// budget actually forbids (one GameObject per tile).
    /// </para>
    /// </remarks>
    public static class LevelRuntimeBuilder
    {
        private const int SortingCanopyBack = -20;
        private const int SortingTerrain = 0;
        private const int SortingEntity = 5;
        private const int SortingCanopyFront = 20;

        /// <summary>
        /// Builds the level under a new root object.
        /// </summary>
        /// <param name="includeGeometry">
        /// False when the scene already contains baked Tilemaps for this level, as happens when
        /// playing a baked scene. Entities are built either way: they are behaviour, not geometry,
        /// and a scene must never be the only place an encounter exists.
        /// </param>
        public static BuiltLevel Build(LevelData level, LevelContent content, Transform parent,
                                       bool includeGeometry = true)
        {
            // Validate before allocating any Unity objects. Invalid content is a boot error,
            // not a partially-created hierarchy that can leak into the next attempt.
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (content.PlayerTuning == null)
                throw new InvalidOperationException("Level content has no player tuning data.");

            var root = new GameObject($"Level_{level.Id}");
            root.transform.SetParent(parent, false);

            if (includeGeometry)
            {
                BuildTerrain(level, root.transform);
                BuildCanopy(level, root.transform);
            }

            var built = new BuiltLevel(root, level);

            LevelEntity start = level.PlayerStart;
            if (start != null)
            {
                built.PlayerStartFeet = level.TileBottomCenter(start.Position.x, start.Position.y);
                built.PlayerStartCentre = built.PlayerStartFeet
                    + new Vector2(0f, content.PlayerTuning != null ? content.PlayerTuning.BodyHeight * 0.5f : 0f);
            }

            BuildEntities(level, content, built, root.transform);
            return built;
        }

        private static void BuildTerrain(LevelData level, Transform parent)
        {
            var terrain = new GameObject("Terrain");
            terrain.transform.SetParent(parent, false);
            GameplayLayers.Assign(terrain, GameplayLayers.GroundName);

            List<LevelRect> rects = level.BuildSolidRects();
            for (int i = 0; i < rects.Count; i++)
            {
                LevelRect rect = rects[i];
                GameObject go = CreateRectObject($"Solid_{rect.Col}_{rect.Row}", rect, level,
                    terrain.transform, SortingTerrain, LevelPalette.For(rect.Kind));

                var collider = go.AddComponent<BoxCollider2D>();
                // CreateRectObject scales the transform to the rectangle's world size. BoxCollider2D
                // dimensions are local-space, so assigning that same size here squares the scale
                // (a 20-unit floor becomes 400 units wide). A unit local box gives the exact intended
                // world-space dimensions for any tile size.
                collider.size = Vector2.one;
                collider.offset = Vector2.zero;

                // The parent was assigned before these rectangles existed. Unity does not inherit
                // a parent's layer when a child is created, so assign each collider explicitly or
                // the player's Ground-layer probe will never see the terrain.
                GameplayLayers.Assign(go, GameplayLayers.GroundName);
            }

            // One collider for the whole terrain would be cheaper still, but a box that spans a gap
            // would be a lie about where the ground is; rectangles are the honest minimum. The
            // wrapper object stays because it is what the layer assignment and the hierarchy use.
        }

        private static void BuildCanopy(LevelData level, Transform parent)
        {
            var canopy = new GameObject("Canopy");
            canopy.transform.SetParent(parent, false);

            CreateKind(level, canopy.transform, LevelTileKind.CanopyBack, LevelPalette.CanopyBack, SortingCanopyBack);
            CreateKind(level, canopy.transform, LevelTileKind.CanopyFront, LevelPalette.CanopyFront, SortingCanopyFront);
        }

        private static void CreateKind(LevelData level, Transform parent, LevelTileKind kind, Color colour, int sorting)
        {
            List<LevelRect> rects = level.BuildRects(kind);
            for (int i = 0; i < rects.Count; i++)
            {
                CreateRectObject($"{kind}_{rects[i].Col}_{rects[i].Row}", rects[i], level, parent, sorting, colour);
            }
        }

        private static GameObject CreateRectObject(string name, LevelRect rect, LevelData level, Transform parent,
                                                  int sortingOrder, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = rect.WorldCenter(level.TileSize, level.Height);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderVisuals.Square;
            renderer.color = colour;
            renderer.sortingOrder = sortingOrder;

            Vector2 size = rect.WorldSize(level.TileSize);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            return go;
        }

        private static void BuildEntities(LevelData level, LevelContent content, BuiltLevel built, Transform parent)
        {
            var entities = new GameObject("Entities");
            entities.transform.SetParent(parent, false);

            for (int i = 0; i < level.Entities.Count; i++)
            {
                LevelEntity entity = level.Entities[i];
                Vector2 feet = level.TileBottomCenter(entity.Position.x, entity.Position.y);

                switch (entity.Kind)
                {
                    case LevelEntityKind.Enemy:
                        BuildEnemy(content, built, entities.transform, entity, feet);
                        break;

                    case LevelEntityKind.Checkpoint:
                        built.Checkpoints.Add(CreateCheckpoint(entities.transform, entity, feet));
                        break;

                    case LevelEntityKind.Discovery:
                        built.Discoveries.Add(CreateDiscovery(entities.transform, entity, feet));
                        break;

                    case LevelEntityKind.Exit:
                        built.Exit = CreateExit(entities.transform, entity, feet);
                        break;

                    case LevelEntityKind.StoryMarker:
                        // Story markers have no behaviour by design: they are things the level says
                        // are there, and the art that shows them comes from the bake. A marker that
                        // popped a message on screen would turn environmental storytelling into a
                        // text box, which is the one thing it must not be.
                        CreateStoryMarker(entities.transform, entity, feet);
                        break;
                }
            }
        }

        private static void BuildEnemy(LevelContent content, BuiltLevel built, Transform parent,
                                       LevelEntity entity, Vector2 feet)
        {
            if (!content.TryGetEnemy(entity.TypeId, out EnemyDefinition definition))
            {
                // A missing archetype is a broken content reference. Silently skipping it
                // makes a level look playable while removing an authored encounter, and can also
                // hide a bad rename in the catalogue. Fail the boot so the error is visible and
                // the partially-created level is torn down by LevelBootstrap.
                throw new InvalidOperationException(
                    $"Level '{built.Data.Id}' places enemy type '{entity.TypeId}' at " +
                    $"({entity.Position.x},{entity.Position.y}), but no matching archetype exists.");
            }

            EnemyController controller = EnemyFactory.Create(definition, feet, parent, entity.PatrolTiles);
            built.Enemies.Add(new BuiltLevel.EnemySpawn(controller, entity, feet));
        }

        private static CheckpointTrigger CreateCheckpoint(Transform parent, LevelEntity entity, Vector2 feet)
        {
            var go = new GameObject($"Checkpoint_{entity.Id}");
            go.transform.SetParent(parent, false);
            go.transform.position = feet + new Vector2(0f, 0.9f);
            GameplayLayers.Assign(go, GameplayLayers.InteractableName);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderVisuals.Ring;
            renderer.color = LevelPalette.CheckpointIdle;
            renderer.sortingOrder = SortingEntity;
            go.transform.localScale = new Vector3(1.4f, 1.4f, 1f);

            var collider = go.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(1.2f / 1.4f, 1.8f / 1.4f);

            var trigger = go.AddComponent<CheckpointTrigger>();
            trigger.Configure(entity, feet);

            // The ring changes colour when the checkpoint takes, so the player can see that dying
            // has stopped being expensive.
            var view = go.AddComponent<CheckpointView>();
            view.Configure(renderer, trigger);
            return trigger;
        }

        private static DiscoveryTrigger CreateDiscovery(Transform parent, LevelEntity entity, Vector2 feet)
        {
            var go = new GameObject($"Discovery_{entity.Id}");
            go.transform.SetParent(parent, false);
            go.transform.position = feet + new Vector2(0f, 0.8f);
            GameplayLayers.Assign(go, GameplayLayers.InteractableName);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderVisuals.Diamond;
            renderer.color = LevelPalette.Secret;
            renderer.sortingOrder = SortingEntity;
            go.transform.localScale = new Vector3(0.8f, 0.8f, 1f);

            var collider = go.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(1.4f / 0.8f, 1.6f / 0.8f);

            var trigger = go.AddComponent<DiscoveryTrigger>();
            trigger.Configure(entity);
            return trigger;
        }

        private static LevelExitTrigger CreateExit(Transform parent, LevelEntity entity, Vector2 feet)
        {
            var go = new GameObject($"Exit_{entity.Id}");
            go.transform.SetParent(parent, false);
            go.transform.position = feet + new Vector2(0f, 1.2f);
            GameplayLayers.Assign(go, GameplayLayers.InteractableName);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderVisuals.Square;
            renderer.color = LevelPalette.Exit;
            renderer.sortingOrder = SortingEntity - 1;
            go.transform.localScale = new Vector3(1.6f, 3.2f, 1f);

            var collider = go.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one;

            var trigger = go.AddComponent<LevelExitTrigger>();
            trigger.Configure(entity);
            return trigger;
        }

        private static void CreateStoryMarker(Transform parent, LevelEntity entity, Vector2 feet)
        {
            var go = new GameObject($"Story_{entity.Id}");
            go.transform.SetParent(parent, false);
            go.transform.position = feet + new Vector2(0f, 0.25f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderVisuals.Square;
            renderer.color = LevelPalette.StoryMarker;
            renderer.sortingOrder = SortingEntity - 2;
            go.transform.localScale = new Vector3(0.9f, 0.14f, 1f);
        }
    }
}
