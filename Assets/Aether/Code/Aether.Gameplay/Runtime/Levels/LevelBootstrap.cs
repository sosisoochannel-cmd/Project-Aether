using Aether.Data.Levels;
using Aether.Gameplay.Cameras;
using Aether.Gameplay.Controls;
using Aether.Gameplay.Enemies;
using Aether.Gameplay.Interface;
using Aether.Gameplay.Player;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Progression.Achievements;
using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// Boots a region: load the data, build it, spawn the player, start the director.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole runtime flow, and it is deliberately one file. Boot, load, spawn, play —
    /// there is no scene loading pipeline, no addressable group and no service locator, because one
    /// level does not need them and inventing them now would decide questions the game has not asked
    /// yet. When a second region exists, this is the seam that grows.
    /// </para>
    /// <para>
    /// The level is loaded from <c>Resources/Levels</c> by name with no scene wiring, so a fresh
    /// clone is playable by pressing play: the text file is the level, and everything else is derived
    /// from it.
    /// </para>
    /// <para>
    /// <b>The solver is not run here.</b> It runs in the gate and in the Editor bake, where a failure
    /// can stop a build. Running a few million simulated steps at boot would be a visible hitch on a
    /// phone to re-prove something that cannot have changed since the build.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class LevelBootstrap : MonoBehaviour
    {
        [Tooltip("Resource path of the level to boot, without extension.")]
        [SerializeField]
        private string _levelPath = "Levels/region1.greenway.level";

        [Tooltip("Build the level as soon as this component starts. Turn off to drive the boot by hand.")]
        [SerializeField]
        private bool _bootOnStart = true;

        [Tooltip("Build terrain from level data. Off when the scene already contains a baked Tilemap.")]
        [SerializeField]
        private bool _buildGeometry = true;

        [SerializeField]
        private Color _cameraBackground = new Color(0.05f, 0.09f, 0.07f, 1f);

        [SerializeField]
        private float _cameraSize = 5.6f;

        private GameSession _session;
        private PlayerController _player;

        /// <summary>The level currently running, or null before boot.</summary>
        public BuiltLevel Level { get; private set; }

        /// <summary>The director running it.</summary>
        public LevelDirector Director { get; private set; }

        /// <summary>The on-screen controls, or null when the device has none.</summary>
        public TouchControlsView TouchControls { get; private set; }

        /// <summary>The region's interface and run state, or null before boot.</summary>
        public GameplayShell Shell { get; private set; }

        private void Start()
        {
            if (_bootOnStart) Boot();
        }

        /// <summary>
        /// Runs the whole boot flow. Safe to call by hand from a test or a debug key, and safe to
        /// call twice: a scene that owns its own boot and the automatic fallback can both ask, and
        /// the second ask must do nothing rather than build a second level on top of the first.
        /// </summary>
        public void Boot()
        {
            if (Level != null) return;

            // The cover goes up before a single byte of level data is read. Everything below this
            // line is work the player should not watch happen — parsing, geometry, enemies, the
            // player, the camera — and the rule the product is held to is that the screen is never
            // empty: not on the way in, not on the way out, and not when something fails.
            GameplayCurtain.Hold("loading.title", "loading.region");

            // One way to get a session, which is also the thing that installs the store progress
            // is written to. This used to build one here and never load anything into it, so a
            // restart began from scratch however much the player had done.
            try
            {
                _session = SaveHost.Ensure();

                LevelContent content = LevelContent.Load();
                LevelData data = LoadLevelData();
                if (data == null || content.PlayerTuning == null)
                {
                    // Nothing to play, and saying so is the whole job now. The curtain becomes the
                    // failure screen with a real way back to the menu on it.
                    FailBoot(
                        "The region could not start: the level data or the content catalogue did not " +
                        "load. There is no playable level in this build configuration.");
                    return;
                }

                Level = LevelRuntimeBuilder.Build(data, content, transform, _buildGeometry);
                if (Level.PlayerStartFeet == Vector2.zero && data.PlayerStart == null)
                {
                    Debug.LogError($"Level '{data.Id}' has no player start; nothing can be spawned.", this);
                    FailBoot(
                        $"Level '{data.Id}' has no player start, so there is nowhere to put the player. " +
                        "This is a level data problem, not something the player did.");
                    return;
                }

                Camera camera = ResolveCamera();
                CameraFollow2D follow = camera.gameObject.GetComponent<CameraFollow2D>();
                if (follow == null) follow = camera.gameObject.AddComponent<CameraFollow2D>();

                Vector2 respawnFeet = ResolveRespawnFeet(Level);
                float halfHeight = content.PlayerTuning.BodyHeight * 0.5f;
                _player = PlayerFactory.Create(respawnFeet + new Vector2(0f, halfHeight),
                    content.PlayerTuning, content.PlayerFirstAttack, transform);

                follow.Configure(_player.transform, new Vector2(0f, 0f), data.WorldSize);

                AttachTouchControls(camera, _player);

                // A codex entry is earned when an enemy actually sees the player, not merely because
                // its prefab was built. Subscriptions are removed with this bootstrap below.
                AttachCharacterDiscovery();

                var directorHost = new GameObject("LevelDirector");
                directorHost.transform.SetParent(transform, false);
                Director = directorHost.AddComponent<LevelDirector>();
                Director.Initialize(_session, Level, _player, follow);

                // The region's own interface, and the thing that makes this a session rather than a
                // level: objective, counters, pause, the clock that the run's playtime comes from, and
                // the summary at the end of it.
                Shell = GameplayShell.Attach(_session, Level, Director, _player, TouchControls);
                Shell.transform.SetParent(transform, false);
                Shell.Reveal();
            }
            catch (System.Exception ex)
            {
                // Runtime content/build errors must become the same actionable curtain as parser
                // failures. Leaving the cover up without a message would be an empty-screen dead end.
                Debug.LogException(ex, this);
                FailBoot(
                    "The region could not be assembled by this build. Return to the menu and try again.");
            }
        }

        /// <summary>
        /// Tears down anything created before boot failed. A failure curtain must not leave a live
        /// player or enemies running invisibly behind it.
        /// </summary>
        private void FailBoot(string message)
        {
            if (Shell != null)
            {
                Shell.gameObject.SetActive(false);
                Destroy(Shell.gameObject);
                Shell = null;
            }

            if (Director != null)
            {
                Director.gameObject.SetActive(false);
                Destroy(Director.gameObject);
                Director = null;
            }

            if (TouchControls != null)
            {
                TouchControls.gameObject.SetActive(false);
                Destroy(TouchControls.gameObject);
                TouchControls = null;
            }

            if (_player == null)
            {
                PlayerController[] partialPlayers = GetComponentsInChildren<PlayerController>(true);
                if (partialPlayers.Length > 0) _player = partialPlayers[0];
            }

            if (_player != null)
            {
                _player.gameObject.SetActive(false);
                Destroy(_player.gameObject);
                _player = null;
            }

            if (Level != null && Level.Root != null)
            {
                Level.Root.SetActive(false);
                Destroy(Level.Root);
            }
            else
            {
                // Build can throw before returning its BuiltLevel handle. Remove a partially
                // assembled runtime root by its reserved name so those enemies cannot keep ticking.
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = transform.GetChild(i);
                    if (!child.name.StartsWith("Level_")) continue;
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
            }

            Level = null;
            AchievementService.Detach();
            GameplayCurtain.Fail(message);
        }

        /// <summary>Listens for the first real notice event from each enemy in the built region.</summary>
        private void AttachCharacterDiscovery()
        {
            if (Level == null || Level.Enemies == null) return;

            for (int i = 0; i < Level.Enemies.Count; i++)
            {
                EnemyController enemy = Level.Enemies[i].Controller;
                if (enemy != null) enemy.PlayerSpotted += OnPlayerSpotted;
            }
        }

        private void OnPlayerSpotted(EnemyController enemy)
        {
            if (_session == null || enemy == null || enemy.Definition == null) return;

            CharacterDefinition character = CharacterCatalog.ForEnemyType(enemy.Definition.TypeId);
            if (character != null) _session.RecordCharacterMet(character.Id);
        }

        private void OnDestroy()
        {
            if (Level == null || Level.Enemies == null) return;

            for (int i = 0; i < Level.Enemies.Count; i++)
            {
                EnemyController enemy = Level.Enemies[i].Controller;
                if (enemy != null) enemy.PlayerSpotted -= OnPlayerSpotted;
            }
        }

        private LevelData LoadLevelData()
        {
            var asset = Resources.Load<TextAsset>(_levelPath);
            if (asset == null)
            {
                Debug.LogError(
                    $"Level '{_levelPath}' was not found under Assets/Aether/Resources. " +
                    "The level data is the source of truth; without it there is nothing to play.", this);
                return null;
            }

            try
            {
                return LevelParser.Parse(asset.text, asset.name);
            }
            catch (LevelParseException ex)
            {
                // A level that does not parse is a build-time mistake that reached runtime. Say so
                // loudly with the exact line, and do not attempt to play a partial level.
                Debug.LogError($"The Greenway level data could not be read. {ex.Message}", this);
                return null;
            }
        }

        private void AttachTouchControls(Camera camera, PlayerController player)
        {
            var source = new TouchInputSource();
            TouchControls = TouchControlsView.Create(camera, source, transform);
            if (TouchControls == null) return;   // no touchscreen: keyboard and gamepad only

            if (player.Input is GameplayInputRouter router) router.AddSource(source);
            else Debug.LogWarning(
                "The player has no input router, so on-screen controls are shown but will not reach " +
                "gameplay. Something built the player without one.", this);
        }

        private Vector2 ResolveRespawnFeet(BuiltLevel level)
        {
            string activeId = _session != null ? _session.World.ActiveCheckpointId : null;
            if (string.IsNullOrEmpty(activeId)) return level.PlayerStartFeet;

            for (int i = 0; i < level.Checkpoints.Count; i++)
            {
                CheckpointTrigger checkpoint = level.Checkpoints[i];
                if (checkpoint != null && checkpoint.CheckpointId == activeId) return checkpoint.RespawnFeet;
            }
            return level.PlayerStartFeet;
        }

        private Camera ResolveCamera()
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                if (camera.orthographicSize <= 0f) camera.orthographicSize = _cameraSize;
                return camera;
            }

            var host = new GameObject("MainCamera");
            host.tag = "MainCamera";
            camera = host.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = _cameraSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = _cameraBackground;
            host.transform.position = new Vector3(0f, 0f, -10f);
            return camera;
        }
    }
}
