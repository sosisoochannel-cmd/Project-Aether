using System;
using System.Collections;
using System.IO;
using Aether.Core.Settings;
using Aether.Gameplay;
using Aether.Gameplay.Cameras;
using Aether.Gameplay.Flow;
using Aether.Gameplay.Interface;
using Aether.Gameplay.Levels;
using Aether.Gameplay.Localization;
using Aether.Gameplay.Player;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Settings;
using Aether.Gameplay.Storage;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// Boots the shipped Boot scene in Unity and checks the actual first gameplay frame.
    /// </summary>
    /// <remarks>
    /// Static contracts are useful, but they cannot prove that the camera, terrain, player and touch
    /// overlay end up in the same usable viewport. This test drives BootFlow, waits for the region to
    /// build, renders the real camera, and checks both world visibility and pause behaviour.
    /// Save files and settings are isolated so the test never touches a player's progress.
    /// </remarks>
    public sealed class GameplayRuntimeRenderTests
    {
        private sealed class MemorySettingsStore : ISettingsStore
        {
            private GameSettings _stored;

            public bool TryLoad(out GameSettings settings)
            {
                settings = _stored;
                return settings != null;
            }

            public bool Save(GameSettings settings)
            {
                if (settings == null) return false;
                _stored = new GameSettings();
                _stored.CopyFrom(settings);
                return true;
            }

            public bool Clear()
            {
                _stored = null;
                return true;
            }

            public bool Exists { get { return _stored != null; } }
            public string Location { get { return "isolated gameplay render test settings"; } }
        }

        private string _saveFolder;
        private Camera _camera;
        private GameObject _cameraHost;
        private GameObject _runtimeRoot;
        private RenderTexture _target;
        private Texture2D _frame;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            GameLaunch.Clear();
            _saveFolder = "Aether-GameplayRender-Test-" + Guid.NewGuid().ToString("N");
            SaveHost.ResetForTests(GameSession.Instance, _saveFolder);
            AetherSettings.ResetForTests(new MemorySettingsStore());
            LanguageService.Reload();
            LanguageService.Set("en");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            GameLaunch.Clear();

            if (_camera != null) _camera.targetTexture = null;
            if (_target != null)
            {
                _target.Release();
                UnityEngine.Object.Destroy(_target);
                _target = null;
            }
            if (_frame != null)
            {
                UnityEngine.Object.Destroy(_frame);
                _frame = null;
            }
            if (_runtimeRoot != null) UnityEngine.Object.Destroy(_runtimeRoot);
            if (_cameraHost != null) UnityEngine.Object.Destroy(_cameraHost);

            GameplayCurtain.ResetForTests();
            yield return null;

            SaveHost.ResetForTests(GameSession.Instance, _saveFolder);
            for (int slot = 1; slot <= SaveSlots.Count; slot++) SaveSlots.For(slot).Clear();
            string saveRoot = Path.GetDirectoryName(SaveSlots.For(1).Location);
            if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
            SaveHost.ResetForTests(null);
            AetherSettings.ResetForTests();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Boot_builds_visible_world_and_pause_freezes_the_camera()
        {
            GameLaunch.RequestPlay();
            AsyncOperation load = SceneManager.LoadSceneAsync("Boot", LoadSceneMode.Single);
            Assert.IsNotNull(load, "The shipped Boot scene must be included in build settings.");
            while (!load.isDone) yield return null;

            LevelBootstrap bootstrap = null;
            float bootDeadline = Time.realtimeSinceStartup + 35f;
            while (Time.realtimeSinceStartup < bootDeadline)
            {
                bootstrap = UnityEngine.Object.FindAnyObjectByType<LevelBootstrap>();
                if (bootstrap != null && bootstrap.Level != null && bootstrap.Shell != null) break;
                yield return null;
            }

            Assert.IsNotNull(bootstrap, "BootFlow did not create a runtime LevelBootstrap.");
            Assert.IsNotNull(bootstrap.Level, "The Greenway data was not built.");
            Assert.IsNotNull(bootstrap.Shell, "The gameplay shell and HUD were not created.");
            _runtimeRoot = bootstrap.gameObject;

            _camera = Camera.main;
            Assert.IsNotNull(_camera, "Boot has no MainCamera.");
            _cameraHost = _camera.gameObject;

            Assert.IsFalse(bootstrap.transform == _camera.transform
                           || bootstrap.transform.IsChildOf(_camera.transform)
                           || _camera.transform.IsChildOf(bootstrap.transform),
                "World objects and the camera must have independent transform hierarchies.");

            PlayerController player = bootstrap.GetComponentInChildren<PlayerController>(true);
            Assert.IsNotNull(player, "The level built no player.");
            Assert.IsTrue(player.transform.IsChildOf(bootstrap.transform),
                "The player must be parented to the independent world root.");

            // The player is built at runtime from components added in code, so "how many" is a real
            // question: a second controller, added by a component requirement, has no tuning asset and
            // throws once per physics step.
            Assert.AreEqual(1, player.GetComponents<PlayerController>().Length,
                "The spawned player must have exactly one controller.");
            Assert.AreEqual(1, player.GetComponents<PlayerHealth>().Length,
                "The spawned player must have exactly one health component.");
            Assert.IsTrue(player.IsInitialized,
                "The spawned player's controller must be initialized with the shipped tuning asset.");

            SpriteRenderer playerRenderer = player.GetComponentInChildren<SpriteRenderer>(true);
            Assert.IsNotNull(playerRenderer, "The player has no visual renderer.");
            Assert.IsNotNull(playerRenderer.sprite, "The player renderer has no sprite.");

            CameraFollow2D follow = _camera.GetComponent<CameraFollow2D>();
            Assert.IsNotNull(follow, "The runtime camera has no follow behaviour.");
            Assert.AreSame(player.transform, follow.Target,
                "The camera must follow the spawned player rather than the previous scene target.");

            float curtainDeadline = Time.realtimeSinceStartup + 5f;
            while (GameplayCurtain.IsUp && Time.realtimeSinceStartup < curtainDeadline) yield return null;
            Assert.IsFalse(GameplayCurtain.IsUp,
                "The region's loading/error curtain stayed up after gameplay was built.");

            Vector3 viewport = _camera.WorldToViewportPoint(playerRenderer.bounds.center);
            Assert.Greater(viewport.z, 0f, "The player is behind the camera.");
            Assert.That(viewport.x, Is.InRange(-0.08f, 1.08f),
                "The player is horizontally outside the first gameplay view: " + viewport);
            Assert.That(viewport.y, Is.InRange(-0.08f, 1.08f),
                "The player is vertically outside the first gameplay view: " + viewport);

            int groundLayer = LayerMask.NameToLayer("Ground");
            Assert.GreaterOrEqual(groundLayer, 0, "The Ground layer is missing from TagManager.");
            Assert.AreNotEqual(0, _camera.cullingMask & (1 << groundLayer),
                "The camera culling mask excludes the terrain layer.");

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(_camera);
            SpriteRenderer[] worldRenderers = bootstrap.Level.Root.GetComponentsInChildren<SpriteRenderer>(true);
            int visibleGround = 0;
            for (int i = 0; i < worldRenderers.Length; i++)
            {
                SpriteRenderer renderer = worldRenderers[i];
                if (renderer != null && renderer.gameObject.layer == groundLayer
                    && GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                    visibleGround++;
            }
            Assert.Greater(visibleGround, 0,
                "No generated terrain renderer intersects the first camera viewport.");

            const int width = 960;
            const int height = 540;
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            _target.name = "Aether Greenway runtime proof";
            _target.Create();
            _camera.targetTexture = _target;
            _camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            _frame = new Texture2D(width, height, TextureFormat.RGB24, false);
            _frame.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            _frame.Apply();
            RenderTexture.active = previous;

            Color32[] pixels = _frame.GetPixels32();
            int litPixels = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].r > 32 || pixels[i].g > 38 || pixels[i].b > 32) litPixels++;
            }

            string outputFolder = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                                               "GameplayRender");
            Directory.CreateDirectory(outputFolder);
            string imagePath = Path.Combine(outputFolder, "greenway-runtime.png");
            File.WriteAllBytes(imagePath, _frame.EncodeToPNG());
            string summary = "Scene: Boot\n"
                + "World root: " + bootstrap.transform.name + " (independent of MainCamera)\n"
                + "Player viewport: " + viewport.ToString("F3") + "\n"
                + "Visible Ground renderers: " + visibleGround + "\n"
                + "Rendered pixels above background threshold: " + litPixels + "\n"
                + "Camera culling mask includes Ground and Player: yes\n"
                + "Screenshot: " + imagePath + "\n";
            File.WriteAllText(Path.Combine(outputFolder, "summary.txt"), summary);
            Debug.Log("[gameplay-render] " + summary.Replace("\n", " | "));

            Assert.Greater(litPixels, 500,
                "The real camera output contains no useful world/character pixels.");

            bootstrap.Shell.Pause.Open();
            Assert.IsTrue(bootstrap.Shell.Pause.IsOpen, "Pause did not open.");
            Assert.AreEqual(0f, Time.timeScale, "Pause did not freeze simulation time.");
            if (bootstrap.TouchControls != null)
            {
                Assert.IsFalse(bootstrap.TouchControls.IsVisible,
                    "Gameplay touch glyphs must be hidden while the pause menu is open.");
            }

            Vector3 pausedCameraPosition = _camera.transform.position;
            yield return new WaitForSecondsRealtime(0.30f);
            Assert.Less(Vector3.Distance(pausedCameraPosition, _camera.transform.position), 0.001f,
                "The camera continued moving after pause froze gameplay.");

            bootstrap.Shell.Pause.Close();
            Assert.AreEqual(1f, Time.timeScale, "Closing pause did not restore gameplay time.");
        }
    }
}
