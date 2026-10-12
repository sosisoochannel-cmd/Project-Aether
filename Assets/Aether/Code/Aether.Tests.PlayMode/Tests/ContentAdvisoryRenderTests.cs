using System.Collections;
using System.IO;
using Aether.Core.Settings;
using Aether.Gameplay.Flow;
using Aether.Gameplay.Localization;
using Aether.Gameplay.Menus;
using Aether.Gameplay.Menus.Screens;
using Aether.Gameplay.Settings;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// Captures the actual warning UI built by MenuRoot in Unity, for a short preview video.
    /// </summary>
    public sealed class ContentAdvisoryRenderTests
    {
        private sealed class MemoryStore : ISettingsStore
        {
            private GameSettings _stored;
            public bool TryLoad(out GameSettings settings) { settings = _stored; return settings != null; }
            public bool Save(GameSettings settings)
            {
                if (settings == null) return false;
                _stored = new GameSettings();
                _stored.CopyFrom(settings);
                return true;
            }
            public bool Clear() { _stored = null; return true; }
            public bool Exists { get { return _stored != null; } }
            public string Location { get { return "isolated warning render test store"; } }
        }

        private int _width = 1920;
        private int _height = 1080;
        private const string OutputFolder = "WarningRender";
        private GameObject _menuHost;
        private GameObject _cameraHost;
        private RenderTexture _target;
        private MenuRoot _menu;

        private string OutputPath(string name)
        {
            return Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputFolder)), name);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Directory.CreateDirectory(OutputPath("."));
            AetherSettings.ResetForTests(new MemoryStore());
            GameLaunch.Clear();
            Screen.SetResolution(Width, Height, false);
            yield return null;
            yield return null;
            // A headless runner may refuse the requested resolution. Capture at the dimensions Unity
            // actually renders so the canvas and its pixel target describe the same viewport.
            _width = Mathf.Max(640, Screen.width);
            _height = Mathf.Max(360, Screen.height);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_menuHost != null) Object.Destroy(_menuHost);
            if (_cameraHost != null) Object.Destroy(_cameraHost);
            if (_target != null)
            {
                _target.Release();
                Object.Destroy(_target);
            }
            GameLaunch.Clear();
            AetherSettings.ResetForTests();
            yield return null;
        }

        [UnityTest]
        public IEnumerator The_real_warning_screen_renders_and_stays_until_input()
        {
            GameLaunch.RequestShowAdvisory();
            _menuHost = new GameObject("AETHER warning render test");
            _menu = _menuHost.AddComponent<MenuRoot>();
            yield return null;
            yield return null;

            Assert.IsNotNull(_menu.System, "MenuRoot did not create its menu system");
            Assert.AreEqual(MenuScreenId.ContentAdvisory, _menu.System.CurrentId,
                            "startup did not open the content warning");
            Assert.AreEqual("fa", LanguageService.Code,
                            "a fresh settings store should use Persian as the default language");

            _cameraHost = new GameObject("Warning preview capture camera");
            Camera camera = _cameraHost.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = _height * 0.5f;
            camera.aspect = (float)_width / _height;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = ~0;
            camera.enabled = false;

            _target = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32);
            _target.name = "AETHER warning preview frame";
            _target.Create();
            camera.targetTexture = _target;

            Canvas canvas = _menu.Ui.Canvas;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();

            string[] files =
            {
                "warning-00.png", "warning-01.png", "warning-02.png", "warning-03.png",
                "warning-04.png", "warning-05.png", "warning-06.png", "warning-07.png"
            };
            float[] captureTimes = { 0.05f, 0.15f, 0.30f, 0.50f, 0.80f, 1.20f, 2.00f, 3.50f };
            float elapsed = 0f;
            string folder = OutputPath(".");
            string manifest = "ffconcat version 1.0\n";
            float[] durations = { 0.10f, 0.15f, 0.20f, 0.30f, 0.40f, 0.80f, 1.50f, 2.00f };

            for (int i = 0; i < captureTimes.Length; i++)
            {
                while (elapsed < captureTimes[i])
                {
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }

                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _target;
                var frame = new Texture2D(_width, _height, TextureFormat.RGB24, false);
                frame.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
                frame.Apply();
                File.WriteAllBytes(Path.Combine(folder, files[i]), frame.EncodeToPNG());
                Object.Destroy(frame);
                RenderTexture.active = previous;

                manifest += "file '" + files[i] + "'\n";
                manifest += "duration " + durations[i].ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
            }

            manifest += "file '" + files[files.Length - 1] + "'\n";
            File.WriteAllText(OutputPath("frames.txt"), manifest);

            // The warning has no countdown: it must remain up after the captured preview period.
            yield return new WaitForSecondsRealtime(2f);
            Assert.AreEqual(MenuScreenId.ContentAdvisory, _menu.System.CurrentId,
                            "the warning automatically disappeared without player input");
            Assert.IsTrue(_menu.System.Current.Visible, "the warning screen is not visible");

            NoteOutput(folder, files.Length);
        }

        private static void NoteOutput(string folder, int count)
        {
            Debug.Log("[warning-render] captured " + count + " Unity-rendered warning frames in " + folder);
        }
    }
}