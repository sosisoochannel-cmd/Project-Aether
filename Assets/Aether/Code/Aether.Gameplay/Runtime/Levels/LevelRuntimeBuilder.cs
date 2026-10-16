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
    /// Turns parsed level data into colliders, layered environment art and gameplay objects.
    /// Builds rich visual depth with distinct background, terrain, canopy, foreground and atmospheric motes.
    /// </summary>
    public static class LevelRuntimeBuilder
    {
        private const int SortingCanopyBack = -20;
        private const int SortingTerrain = 0;
        private const int SortingEntity = 5;
        private const int SortingCanopyFront = 20;

        /// <summary>
        /// Builds the level under a new root object.
        /// </summary>
        public static BuiltLevel Build(LevelData level, LevelContent content, Transform parent,
                                       bool includeGeometry = true)
        {
            var root = new GameObject($"Level_{level.Id}");
            root.transform.SetParent(parent, false);

            if (includeGeometry)
            {
                BuildBackdrop(level, root.transform);
                BuildTerrain(level, root.transform);
                BuildCanopy(level, root.transform);
                BuildForeground(level, root.transform);
                BuildAtmosphere(level, root.transform);
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

        private static void BuildBackdrop(LevelData level, Transform parent)
        {
            var backdropHost = new GameObject("EnvironmentBackdrop");
            backdropHost.transform.SetParent(parent, false);

            float totalWidth = level.Width * level.TileSize;
            float totalHeight = level.Height * level.TileSize;
            Vector2 levelCenter = new Vector2(totalWidth * 0.5f, totalHeight * 0.5f);

            // Layer 1: Distant Sky (Sorting -100)
            var skyGo = new GameObject("SkyBackdrop");
            skyGo.transform.SetParent(backdropHost.transform, false);
            skyGo.transform.position = new Vector3(levelCenter.x, levelCenter.y + 4f, 0f);
            var skyRenderer = skyGo.AddComponent<SpriteRenderer>();
            skyRenderer.sprite = EnvironmentSpriteLibrary.SkyBackdrop;
            skyRenderer.sortingOrder = -100;
            skyRenderer.drawMode = SpriteDrawMode.Tiled;
            skyRenderer.size = new Vector2(totalWidth * 1.5f, totalHeight * 1.6f);

            // Layer 2: Distant Mountains (Sorting -70)
            var mountainGo = new GameObject("MountainRidge");
            mountainGo.transform.SetParent(backdropHost.transform, false);
            mountainGo.transform.position = new Vector3(levelCenter.x, 8f, 0f);
            var mountainRenderer = mountainGo.AddComponent<SpriteRenderer>();
            mountainRenderer.sprite = EnvironmentSpriteLibrary.MountainRidge;
            mountainRenderer.sortingOrder = -70;
            mountainRenderer.drawMode = SpriteDrawMode.Tiled;
            mountainRenderer.size = new Vector2(totalWidth * 1.4f, 14f);

            // Layer 3: Forest Midground (Sorting -40)
            var forestGo = new GameObject("ForestMidground");
            forestGo.transform.SetParent(backdropHost.transform, false);
            forestGo.transform.position = new Vector3(levelCenter.x, 6f, 0f);
            var forestRenderer = forestGo.AddComponent<SpriteRenderer>();
            forestRenderer.sprite = EnvironmentSpriteLibrary.ForestMidground;
            forestRenderer.sortingOrder = -40;
            forestRenderer.drawMode = SpriteDrawMode.Tiled;
            forestRenderer.size = new Vector2(totalWidth * 1.3f, 16f);

            var foregroundHost = new GameObject("EnvironmentForeground");
            foregroundHost.transform.SetParent(parent, false);

            var parallax = backdropHost.AddComponent<ParallaxBackdrop2D>();
            parallax.Configure(skyGo.transform, mountainGo.transform, forestGo.transform, foregroundHost.transform);
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

                GameplayLayers.Assign(go, GameplayLayers.GroundName);

                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = rect.WorldSize(level.TileSize);
                collider.offset = Vector2.zero;
            }
        }

        private static void BuildCanopy(LevelData level, Transform parent)
        {
            var canopy = new GameObject("Canopy");
            canopy.transform.SetParent(parent, false);

            CreateKind(level, canopy.transform, LevelTileKind.CanopyBack, LevelPalette.CanopyBack, SortingCanopyBack);
            CreateKind(level, canopy.transform, LevelTileKind.CanopyFront, LevelPalette.CanopyFront, SortingCanopyFront);
        }

        private static void BuildForeground(LevelData level, Transform parent)
        {
            Transform foregroundHost = parent.Find("EnvironmentForeground");
            if (foregroundHost == null)
            {
                var go = new GameObject("EnvironmentForeground");
                go.transform.SetParent(parent, false);
                foregroundHost = go.transform;
            }

            float totalWidth = level.Width * level.TileSize;
            float totalHeight = level.Height * level.TileSize;
            Vector2 levelCenter = new Vector2(totalWidth * 0.5f, totalHeight * 0.5f);

            var foliageGo = new GameObject("ForegroundFoliage");
            foliageGo.transform.SetParent(foregroundHost, false);
            foliageGo.transform.position = new Vector3(levelCenter.x, totalHeight - 2f, 0f);
            var foliageRenderer = foliageGo.AddComponent<SpriteRenderer>();
            foliageRenderer.sprite = EnvironmentSpriteLibrary.ForegroundFoliage;
            foliageRenderer.sortingOrder = 25;
            foliageRenderer.drawMode = SpriteDrawMode.Tiled;
            foliageRenderer.size = new Vector2(totalWidth * 1.3f, 6f);
        }

        private static void BuildAtmosphere(LevelData level, Transform parent)
        {
            GameplayAtmosphere.Attach(parent, level);
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
            renderer.sortingOrder = sortingOrder;

            Vector2 size = rect.WorldSize(level.TileSize);
            Sprite tiledSurface = null;
            switch (rect.Kind)
            {
                case LevelTileKind.Ground:
                    tiledSurface = EnvironmentSpriteLibrary.Ground;
                    break;
                case LevelTileKind.DeepRock:
                    tiledSurface = EnvironmentSpriteLibrary.DeepRock;
                    break;
                case LevelTileKind.Platform:
                    tiledSurface = EnvironmentSpriteLibrary.Platform;
                    break;
                case LevelTileKind.CanopyBack:
                    tiledSurface = EnvironmentSpriteLibrary.CanopyBack;
                    break;
                case LevelTileKind.CanopyFront:
                    tiledSurface = EnvironmentSpriteLibrary.CanopyFront;
                    break;
            }

            if (tiledSurface != null)
            {
                renderer.sprite = tiledSurface;
                renderer.drawMode = SpriteDrawMode.Tiled;
                renderer.size = size;
                renderer.color = Color.white;
                go.transform.localScale = Vector3.one;
            }
            else
            {
                renderer.sprite = PlaceholderVisuals.Square;
                renderer.color = colour;
                go.transform.localScale = new Vector3(size.x, size.y, 1f);
            }
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
                Debug.LogError(
                    $"Level '{built.Data.Id}' places an enemy of type '{entity.TypeId}' at " +
                    $"({entity.Position.x},{entity.Position.y}) but no such archetype exists. " +
                    "The encounter is missing from the level as built.");
                return;
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
            renderer.sprite = EnvironmentSpriteLibrary.Checkpoint;
            renderer.color = LevelPalette.CheckpointIdle;
            renderer.sortingOrder = SortingEntity;
            go.transform.localScale = new Vector3(1.6f, 1.6f, 1f);

            var collider = go.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(1.2f, 1.8f);

            var trigger = go.AddComponent<CheckpointTrigger>();
            trigger.Configure(entity, feet);

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
            renderer.sprite = EnvironmentSpriteLibrary.Discovery;
            renderer.color = LevelPalette.Secret;
            renderer.sortingOrder = SortingEntity;
            go.transform.localScale = new Vector3(1.2f, 1.2f, 1f);

            var collider = go.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(1.4f, 1.6f);

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
            renderer.sprite = EnvironmentSpriteLibrary.Exit;
            renderer.color = Color.white;
            renderer.sortingOrder = SortingEntity - 1;
            go.transform.localScale = new Vector3(2.0f, 3.2f, 1f);

            var collider = go.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(1.6f, 3.2f);

            var trigger = go.AddComponent<LevelExitTrigger>();
            trigger.Configure(entity);
            return trigger;
        }

        private static void CreateStoryMarker(Transform parent, LevelEntity entity, Vector2 feet)
        {
            var go = new GameObject($"Story_{entity.Id}");
            go.transform.SetParent(parent, false);
            go.transform.position = feet + new Vector2(0f, 0.8f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = EnvironmentSpriteLibrary.StoryMarker;
            renderer.color = Color.white;
            renderer.sortingOrder = SortingEntity - 2;
            go.transform.localScale = new Vector3(1.0f, 1.6f, 1f);
        }
    }
}
