using Aether.Data.Levels;
using Aether.Gameplay.Cameras;
using Aether.Gameplay.Controls;
using Aether.Gameplay.Player;
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

        /// <summary>The level currently running, or null before boot.</summary>
        public BuiltLevel Level { get; private set; }

        /// <summary>The director running it.</summary>
        public LevelDirector Director { get; private set; }

        /// <summary>The on-screen controls, or null when the device has none.</summary>
        public TouchControlsView TouchControls { get; private set; }

        private void Start()
        {
            if (_bootOnStart) Boot();
        }

        /// <summary>Runs the whole boot flow. Safe to call by hand from a test or a debug key.</summary>
        public void Boot()
        {
            _session = GameSession.Instance;
            if (_session == null)
            {
                _session = new GameObject("GameSession").AddComponent<GameSession>();
            }

            LevelContent content = LevelContent.Load();
            LevelData data = LoadLevelData();
            if (data == null || content.PlayerTuning == null) return;

            Level = LevelRuntimeBuilder.Build(data, content, transform, _buildGeometry);
            if (Level.PlayerStartFeet == Vector2.zero && data.PlayerStart == null)
            {
                Debug.LogError($"Level '{data.Id}' has no player start; nothing can be spawned.", this);
                return;
            }

            Camera camera = ResolveCamera();
            CameraFollow2D follow = camera.gameObject.GetComponent<CameraFollow2D>();
            if (follow == null) follow = camera.gameObject.AddComponent<CameraFollow2D>();

            Vector2 respawnFeet = ResolveRespawnFeet(Level);
            float halfHeight = content.PlayerTuning.BodyHeight * 0.5f;
            PlayerController player = PlayerFactory.Create(respawnFeet + new Vector2(0f, halfHeight),
                content.PlayerTuning, content.PlayerFirstAttack, transform);

            follow.Configure(player.transform, new Vector2(0f, 0f), data.WorldSize);

            AttachTouchControls(camera, player);

            var directorHost = new GameObject("LevelDirector");
            directorHost.transform.SetParent(transform, false);
            Director = directorHost.AddComponent<LevelDirector>();
            Director.Initialize(_session, Level, player);
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
