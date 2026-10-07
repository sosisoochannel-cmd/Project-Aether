using Aether.Data.Levels;
using Aether.Gameplay.Cameras;
using Aether.Gameplay.Controls;
using Aether.Gameplay.Interface;
using Aether.Gameplay.Player;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Localization;
using Aether.Gameplay.Menus;
using System.Collections.Generic;
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
            _session = SaveHost.Ensure();

            LevelContent content = LevelContent.Load();
            LevelData data = LoadLevelData();
            if (data == null || content.PlayerTuning == null)
            {
                // Nothing to play, and saying so is the whole job now. The curtain becomes the
                // failure screen with a real way back to the menu on it.
                GameplayCurtain.Fail(
                    "The region could not start: the level data or the content catalogue did not " +
                    "load. There is no playable level in this build configuration.");
                return;
            }

            Level = LevelRuntimeBuilder.Build(data, content, transform, _buildGeometry);
            if (Level.PlayerStartFeet == Vector2.zero && data.PlayerStart == null)
            {
                Debug.LogError($"Level '{data.Id}' has no player start; nothing can be spawned.", this);
                GameplayCurtain.Fail(
                    $"Level '{data.Id}' has no player start, so there is nowhere to put the player. " +
                    "This is a level data problem, not something the player did.");
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

            // Who the player has now met. The region's own contents are the evidence: the creatures
            // it placed are creatures the player has been in a region with, and the catalogue turns
            // that into characters. Called here, once, because a region is built once — and without
            // the call the characters screen would report a cast nobody can ever meet.
            RecordEncounters();

            var directorHost = new GameObject("LevelDirector");
            directorHost.transform.SetParent(transform, false);
            Director = directorHost.AddComponent<LevelDirector>();
            Director.Initialize(_session, Level, player, follow);

            // The region's own interface, and the thing that makes this a session rather than a
            // level: objective, counters, pause, the clock that the run's playtime comes from, and
            // the summary at the end of it.
            Shell = GameplayShell.Attach(_session, Level, Director, player, TouchControls);
            Shell.transform.SetParent(transform, false);
            Shell.Reveal();
        }

        /// <summary>Records every character this region's enemies prove the player has met.</summary>
        private void RecordEncounters()
        {
            if (_session == null || Level == null || Level.Enemies == null || Level.Enemies.Count == 0)
            {
                // A region with nothing in it introduces nobody, and that is not a failure: the
                // protagonist's own entry is the catalogue's business, not this loop's.
                return;
            }

            var types = new List<string>(Level.Enemies.Count);
            for (int i = 0; i < Level.Enemies.Count; i++)
            {
                LevelEntity entity = Level.Enemies[i].Source;
                if (entity == null || string.IsNullOrEmpty(entity.TypeId)) continue;
                if (!types.Contains(entity.TypeId)) types.Add(entity.TypeId);
            }

            List<CharacterDefinition> met = CharacterCatalog.RecordEncounters(_session.Save, types);
            if (met.Count == 0) return;

            // Said out loud, the way an achievement is: a record that changes and says nothing is a
            // record the player never learns about.
            if (Shell != null && Shell.Hud != null)
            {
                Shell.Hud.Announce("characters.met.title",
                                   MenuStrings.Format("characters.met.banner", met.Count));
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
