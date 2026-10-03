using System.Collections.Generic;
using System.Text;
using Aether.Data.Config;
using Aether.Data.Levels;
using Aether.Gameplay.Levels;
using Aether.Gameplay.Support;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Aether.Editor
{
    /// <summary>
    /// Bakes level data into a scene: the Tilemap representation of the region, plus the objects the
    /// level declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The data stays the source of truth.</b> The scene this produces is an output. Editing it by
    /// hand is pointless because the next bake overwrites it, and that is the point: there is one
    /// description of the level and everything else is derived.
    /// </para>
    /// <para>
    /// <b>The bake refuses to run on a level that does not solve.</b> Before anything is written, the
    /// traversal solver proves every connection the level claims — with the real jump arc, the real
    /// body size, the real run speed — and any claim it cannot prove stops the bake with the reason.
    /// A bake that produced a pretty scene containing an impossible jump would be worse than no bake
    /// at all.
    /// </para>
    /// <para>
    /// Nothing here is verified by running it: see the report accompanying this milestone. It uses
    /// only long-established editor APIs and creates assets rather than modifying them, so the worst
    /// outcome of a mistake is a failed menu command, not a corrupted project.
    /// </para>
    /// </remarks>
    public static class LevelBaker
    {
        private const string LevelResourcePath = "Assets/Aether/Resources/Levels/region1.greenway.level.txt";
        private const string TuningAssetPath = "Assets/Aether/Resources/Content/PlayerTuning.asset";
        private const string ScenePath = "Assets/Aether/Scenes/Greenway.unity";
        private const string TileFolder = "Assets/Aether/Baked/Tiles";
        private const string BakedFolder = "Assets/Aether/Baked";

        private static readonly Dictionary<LevelTileKind, string> TileNames = new Dictionary<LevelTileKind, string>
        {
            { LevelTileKind.Ground, "Tile_Ground" },
            { LevelTileKind.DeepRock, "Tile_DeepRock" },
            { LevelTileKind.Platform, "Tile_Platform" },
            { LevelTileKind.CanopyBack, "Tile_CanopyBack" },
            { LevelTileKind.CanopyFront, "Tile_CanopyFront" },
        };

        [MenuItem("Aether/Bake Region 1 (Greenway)")]
        public static void BakeGreenway()
        {
            LevelData level = LoadLevel();
            if (level == null) return;

            PlayerTuningData tuning = AssetDatabase.LoadAssetAtPath<PlayerTuningData>(TuningAssetPath);
            if (tuning == null)
            {
                Debug.LogError(
                    $"Bake stopped: '{TuningAssetPath}' is missing, so the level cannot be proven " +
                    "against the player's real movement.");
                return;
            }

            if (!ProveTraversal(level, tuning)) return;

            EnsureFolder(BakedFolder);
            EnsureFolder(TileFolder);

            var tiles = new Dictionary<LevelTileKind, Tile>();
            foreach (KeyValuePair<LevelTileKind, string> entry in TileNames)
            {
                tiles[entry.Key] = EnsureTile(entry.Value, entry.Key);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildScene(level, tiles);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"Baked '{level.DisplayName}' ({level.Width}x{level.Height}) to {ScenePath}. " +
                "The scene is generated: edit the level data, then bake again.");
        }

        [MenuItem("Aether/Verify Region 1 (Greenway)")]
        public static void VerifyGreenway()
        {
            LevelData level = LoadLevel();
            if (level == null) return;

            PlayerTuningData tuning = AssetDatabase.LoadAssetAtPath<PlayerTuningData>(TuningAssetPath);
            if (tuning == null)
            {
                Debug.LogError($"Verify stopped: '{TuningAssetPath}' is missing.");
                return;
            }

            var solver = new PlayerTraversalSolver(level, tuning);
            List<TraversalReport> reports = solver.CheckConnections();
            List<string> problems = solver.StructuralProblems();

            var summary = new StringBuilder();
            summary.AppendLine($"Level: {level.DisplayName} ({level.Width}x{level.Height})");
            int usable = 0;
            for (int i = 0; i < reports.Count; i++)
            {
                if (reports[i].IsUsable) usable++;
                summary.AppendLine($"  {reports[i].Describe()}");
            }
            for (int i = 0; i < problems.Count; i++) summary.AppendLine($"  structural: {problems[i]}");
            summary.AppendLine($"{usable}/{reports.Count} traversal claims proven.");

            if (usable == reports.Count && problems.Count == 0) Debug.Log(summary.ToString());
            else Debug.LogError(summary.ToString());
        }

        private static LevelData LoadLevel()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(LevelResourcePath);
            if (text == null)
            {
                Debug.LogError($"Level data '{LevelResourcePath}' was not found.");
                return null;
            }

            try
            {
                return LevelParser.Parse(text.text, text.name);
            }
            catch (LevelParseException ex)
            {
                Debug.LogError($"Bake stopped: {ex.Message}");
                return null;
            }
        }

        /// <summary>Runs the traversal solver and reports every claim it cannot prove.</summary>
        private static bool ProveTraversal(LevelData level, PlayerTuningData tuning)
        {
            var solver = new PlayerTraversalSolver(level, tuning);
            List<TraversalReport> reports = solver.CheckConnections();
            List<string> problems = solver.StructuralProblems();

            var failures = new StringBuilder();
            int failuresFound = 0;
            for (int i = 0; i < reports.Count; i++)
            {
                if (reports[i].IsUsable) continue;
                failuresFound++;
                failures.AppendLine($"  {reports[i].Describe()}");
            }
            for (int i = 0; i < problems.Count; i++)
            {
                failuresFound++;
                failures.AppendLine($"  structural: {problems[i]}");
            }

            if (failuresFound > 0)
            {
                Debug.LogError(
                    $"Bake stopped: {failuresFound} problem(s) in '{level.Id}'. The level is not " +
                    "playable as written, and baking it would only make the problem look finished.\n" +
                    failures);
                return false;
            }

            return true;
        }

        private static void BuildScene(LevelData level, Dictionary<LevelTileKind, Tile> tiles)
        {
            var gridHost = new GameObject("Grid");
            Grid grid = gridHost.AddComponent<Grid>();
            grid.cellSize = new Vector3(level.TileSize, level.TileSize, 0f);

            Tilemap ground = CreateTilemap(gridHost.transform, "Ground", 0);
            Tilemap platforms = CreateTilemap(gridHost.transform, "Platforms", 1);
            Tilemap background = CreateTilemap(gridHost.transform, "CanopyBack", -20);
            Tilemap foreground = CreateTilemap(gridHost.transform, "CanopyFront", 20);

            for (int row = 0; row < level.Height; row++)
            {
                int y = level.Height - 1 - row;   // row 0 is the top of the map
                for (int col = 0; col < level.Width; col++)
                {
                    LevelTileKind kind = level.KindAt(col, row);
                    if (kind == LevelTileKind.Empty) continue;

                    Tilemap target = kind == LevelTileKind.CanopyBack ? background
                        : kind == LevelTileKind.CanopyFront ? foreground
                        : kind == LevelTileKind.Platform ? platforms
                        : ground;

                    target.SetTile(new Vector3Int(col, y, 0), tiles[kind]);
                }
            }

            // Colliders come from the tilemaps so the baked scene is physically inspectable. The
            // running game builds its own from the same grid; these are for looking at.
            ground.gameObject.AddComponent<TilemapCollider2D>();
            platforms.gameObject.AddComponent<TilemapCollider2D>();
            GameplayLayers.Assign(ground.gameObject, GameplayLayers.GroundName);
            GameplayLayers.Assign(platforms.gameObject, GameplayLayers.GroundName);

            var cameraHost = new GameObject("MainCamera");
            cameraHost.tag = "MainCamera";
            Camera camera = cameraHost.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.09f, 0.07f, 1f);
            cameraHost.transform.position = new Vector3(0f, level.Height * 0.5f, -10f);

            var bootHost = new GameObject("LevelBootstrap");
            LevelBootstrap bootstrap = bootHost.AddComponent<LevelBootstrap>();
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("_levelPath")?.SetValue("Levels/region1.greenway.level");
            // The tilemaps above are this scene's geometry, so the runtime builder must not add a
            // second copy of it; entities are still built from data every time.
            serialized.FindProperty("_buildGeometry")?.SetValue(false);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var markerHost = new GameObject("LevelMarkers");
            for (int i = 0; i < level.Entities.Count; i++)
            {
                LevelEntity entity = level.Entities[i];
                var marker = new GameObject($"{entity.Kind}_{entity.Id}");
                marker.transform.SetParent(markerHost.transform, false);
                marker.transform.position = level.TileBottomCenter(entity.Position.x, entity.Position.y);
            }
        }

        private static Tilemap CreateTilemap(Transform parent, string name, int sortingOrder)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            Tilemap tilemap = host.AddComponent<Tilemap>();
            var renderer = host.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = sortingOrder;
            return tilemap;
        }

        private static Tile EnsureTile(string name, LevelTileKind kind)
        {
            string path = $"{TileFolder}/{name}.asset";
            Tile existing = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (existing != null) return existing;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = name;
            tile.color = LevelPalette.For(kind);
            tile.colliderType = LevelTileTraits.IsSolid(kind) ? Tile.ColliderType.Sprite : Tile.ColliderType.None;
            AssetDatabase.CreateAsset(tile, path);
            return tile;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/');
            string parent = path.Substring(0, split);
            string leaf = path.Substring(split + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
