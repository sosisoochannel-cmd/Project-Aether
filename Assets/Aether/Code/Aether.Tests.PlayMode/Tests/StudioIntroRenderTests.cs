using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Aether.Gameplay.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Aether.Tests.PlayMode
{
    /// <summary>
    /// Plays the studio intro in the real engine and measures the pixels it actually produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>tools/verify/intro.py</c> proves the numbers — the lockup fits inside the safe area, keeps
    /// the artwork's aspect ratio and stays centred — but it proves them about arithmetic, not about
    /// what the renderer draws. This test closes that gap: it loads the shipped scene, lets the
    /// shipped component run, captures the frames the engine put on screen, and asserts the same
    /// properties from the pixels. The bug that made the mark fill the screen was invisible to the
    /// arithmetic and unmissable here.
    /// </para>
    /// <para>
    /// The frames it captures are written to <c>IntroRender/</c> in the project root, which is what
    /// the CI job uploads, so a run leaves behind a real screenshot of the intro beside the proof —
    /// four moments: the reveal in progress, the lockup fully arrived, the hold, and the exit. Each
    /// frame is written twice: once at the size it was rendered, and once at half size, because the
    /// runner's artifacts live on storage the machine driving this cannot reach and a small PNG is
    /// the only thing that fits through the one channel that does work — the run's annotations.
    /// </para>
    /// <para>
    /// <b>It runs on a runner with no monitor.</b> A batch-mode editor has no window, so the frame is
    /// taken with <see cref="ScreenCapture.CaptureScreenshotAsTexture"/> when the engine offers one
    /// and, when it does not, by rendering the scene's own camera into a render texture and reading
    /// that back — the same picture the camera would have put on the screen. Which of the two
    /// produced a frame is recorded in <c>IntroRender/diagnostics.txt</c> and printed in the frame's
    /// own line, so a screenshot is never quietly something else.
    /// </para>
    /// <para>
    /// What it cannot check: how the arrival reads in motion, and how it looks on a real panel. Four
    /// stills and their measurements are what a machine can honestly say about a two-second reveal.
    /// </para>
    /// </remarks>
    public sealed class StudioIntroRenderTests
    {
        private const string OutputFolder = "IntroRender";

        /// <summary>Width over height of the lockup the artwork draws, measured from its ink.</summary>
        private const float LockupAspect = 800f / 814f;

        /// <summary>When to look, in seconds after the intro scene came up.</summary>
        private static readonly float[] CaptureTimes = { 0.75f, 1.35f, 1.95f, 2.45f };

        /// <summary>A pixel at or above this luminance is part of the mark.</summary>
        private const float InkLuminance = 0.35f;

        /// <summary>A pixel at or below this luminance is the black the intro is drawn on.</summary>
        private const float BlackLuminance = 0.03f;

        /// <summary>The key <see cref="StudioIntroSequence.Preference"/> stores its choice under.</summary>
        private const string IntroPreferenceKey = "aether.studioIntro.play";

        /// <summary>The folder the frames go to, as an absolute path.</summary>
        /// <remarks>
        /// Unity's working directory is a runner's business, not this test's: the frames belong in the
        /// project root, which is where the CI job looks for them.
        /// </remarks>
        private static string OutputFolderPath()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputFolder));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string OutputPath(string file)
        {
            return Path.Combine(OutputFolderPath(), file);
        }

        /// <summary>What the run saw, written to disk as it happens so a failure still explains itself.</summary>
        private static readonly StringBuilder Diagnostics = new StringBuilder();

        private static readonly List<string> EngineMessages = new List<string>();

        private static bool _recording;

        [UnityTest]
        public IEnumerator The_intro_draws_the_lockup_where_it_belongs()
        {
            string stage = "start up";
            Directory.CreateDirectory(OutputFolderPath());
            Diagnostics.Clear();
            EngineMessages.Clear();
            Application.logMessageReceived += Remember;
            _recording = true;

            var frames = new List<Frame>();
            try
            {
                // The intro plays unless a player has turned it off; this run wants to watch it, so the
                // preference is set to its documented default rather than left to whatever the machine
                // happened to have saved. Nothing else about the intro is touched.
                PlayerPrefs.SetInt(IntroPreferenceKey, 1);
                PlayerPrefs.Save();

                Note($"screen {Screen.width}x{Screen.height}, batch mode {Application.isBatchMode}, "
                     + $"device {SystemInfo.graphicsDeviceType}, resolution {Screen.currentResolution}");

                // A landscape window, so the portrait half of the layout is not what gets photographed.
                // A batch-mode editor may have no window to resize; the frame's real size is reported
                // either way, and the sizes that matter are asserted from the pixels below.
                stage = "resize the window";
                try
                {
                    Screen.SetResolution(1920, 1080, false);
                }
                catch (Exception error)
                {
                    Note($"SetResolution is not available here: {error.GetType().Name}");
                }

                yield return null;

                stage = "load the intro scene";
                yield return LoadIntroScene();

                Scene scene = SceneManager.GetActiveScene();
                Note($"scene '{scene.name}' at {scene.path}, loaded from build index {scene.buildIndex}");
                Note($"root objects: {DescribeRoots(scene)}");

                stage = "find the intro";
                StudioIntroSequence intro = Object.FindFirstObjectByType<StudioIntroSequence>();
                if (intro == null)
                {
                    // A component on an inactive object is still the intro, and a scene loaded for a
                    // test may have arrived that way; look again including what is switched off.
                    intro = Object.FindAnyObjectByType<StudioIntroSequence>(FindObjectsInactive.Include);
                    Note(intro == null
                        ? "no StudioIntroSequence on an active or inactive object"
                        : "the StudioIntroSequence is on an inactive object");
                }

                Note($"Camera.main: {(Camera.main == null ? "none" : Camera.main.name + " (" + Camera.main.orthographicSize.ToString("0.###", CultureInfo.InvariantCulture) + " half-height)")}");
                Note($"Resources.Load<Sprite>('Brand/VarellonLogo'): "
                     + (Resources.Load<Sprite>("Brand/VarellonLogo") == null ? "returned nothing" : "ok"));

                Assert.That(intro, Is.Not.Null,
                            "the intro scene has no StudioIntroSequence, so it can only show black. "
                            + $"What was in the scene: {DescribeRoots(scene)}");

                stage = "capture four frames";
                float start = Time.unscaledTime;
                for (int i = 0; i < CaptureTimes.Length; i++)
                {
                    while (Time.unscaledTime - start < CaptureTimes[i]) yield return null;
                    yield return new WaitForEndOfFrame();

                    Frame frame;
                    try
                    {
                        frame = Frame.Capture(CaptureTimes[i]);
                    }
                    catch (Exception error)
                    {
                        Note($"capture at {CaptureTimes[i]}s threw: {error}");
                        throw;
                    }

                    frames.Add(frame);
                    Note(frame.Describe());
                    WriteFrame(frame);   // on disk before the next assertion, so a later failure loses nothing
                }

                // This test is about what the intro draws, not about what Boot does afterwards: taking
                // the component out stops the hand-over and leaves the measurement above standing.
                stage = "stop the hand-over";
                Object.Destroy(intro);
                yield return null;

                Frame hold = frames[2];
                stage = "check the frame is landscape";
                Assert.That(hold.Width, Is.GreaterThan(hold.Height),
                            $"the capture is {hold.Width}x{hold.Height}, not landscape; this test needs a "
                            + "landscape window or the frames are not the ones a phone would show");

                // Something is drawn, and it is a mark on a black screen rather than a picture of one.
                stage = "check the mark is drawn on black";
                Assert.Greater(hold.InkFraction, 0.002f,
                               "the mark covers almost nothing of the frame; either it is not drawn or it "
                               + "is far smaller than the layout asks for");
                Assert.Less(hold.InkFraction, 0.25f,
                            $"the mark covers {hold.InkFraction:P1} of the frame; this is the shape of the "
                            + "bug where the sprite's own pixels-per-unit was left out of its scale and "
                            + "the lockup was drawn many times too large");
                Assert.Greater(hold.BlackFraction, 0.90f,
                               $"only {hold.BlackFraction:P1} of the frame is black; the intro is drawn on "
                               + "black, so something else is filling the screen");

                // Centred on the frame it has: a lockup pushed off-centre is what the old placement did
                // as soon as the safe area was not the whole screen.
                stage = "check the mark is centred";
                Assert.That(Mathf.Abs(hold.CentreX - 0.5f), Is.LessThan(0.03f),
                            $"the mark's centre is at {hold.CentreX:P1} across, not 50%");
                Assert.That(Mathf.Abs(hold.CentreY - 0.5f), Is.LessThan(0.03f),
                            $"the mark's centre is at {hold.CentreY:P1} down, not 50%");

                // Inside the frame on every side: a crop is the other half of the old bug.
                stage = "check the mark is inside the frame";
                Assert.That(hold.Left, Is.GreaterThan(0.10f), "the mark runs off the left edge");
                Assert.That(hold.Right, Is.GreaterThan(0.10f), "the mark runs off the right edge");
                Assert.That(hold.Bottom, Is.GreaterThan(0.10f), "the mark runs off the bottom edge");
                Assert.That(hold.Top, Is.GreaterThan(0.10f), "the mark runs off the top edge");

                // The artwork's own proportions: the mark is never stretched to fit.
                stage = "check the aspect ratio is the artwork's";
                Assert.That(hold.InkAspect, Is.EqualTo(LockupAspect).Within(0.08f),
                            $"the drawn lockup is {hold.InkAspect:0.000} wide over tall, the artwork is "
                            + $"{LockupAspect:0.000}; the fit is not preserving its aspect ratio");

                // And it animates: the mark arrives, holds, and leaves, rather than cutting in and out.
                stage = "check the reveal and the exit";
                Assert.Less(frames[0].InkFraction, hold.InkFraction,
                            "the mark is as present at 0.75s as at the hold, so there is no reveal");
                Assert.Less(frames[3].InkFraction, hold.InkFraction,
                            "the mark is as present at 2.45s as at the hold, so there is no exit");

                stage = "done";
            }
            finally
            {
                WriteSummary(frames, stage);
                Application.logMessageReceived -= Remember;
                _recording = false;
            }
        }

        /// <summary>
        /// Brings the shipped intro scene up the way the app does, and says what happened if it does
        /// not arrive.
        /// </summary>
        private static IEnumerator LoadIntroScene()
        {
            const string SceneName = "StudioIntro";
            AsyncOperation load = null;
            try
            {
                load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            }
            catch (Exception error)
            {
                Note($"LoadSceneAsync('{SceneName}') threw: {error.GetType().Name}: {error.Message}");
            }

            if (load != null)
            {
                yield return load;
            }
            else
            {
                yield return null;
            }

            if (SceneManager.GetActiveScene().name == SceneName) yield break;

            // The scene is in the build settings, so the load above is the one the app would do. If it
            // did not take, load it by path instead — the same scene, the same components waking up —
            // rather than ending the run without a picture.
            Note($"the active scene is '{SceneManager.GetActiveScene().name}', not '{SceneName}'; "
                 + "loading it by path instead");
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Aether/Scenes/StudioIntro.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            Note($"active scene after the fallback: '{SceneManager.GetActiveScene().name}'");
        }

        private static string DescribeRoots(Scene scene)
        {
            var text = new StringBuilder();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (i > 0) text.Append(", ");
                GameObject root = roots[i];
                var parts = new List<string>();
                Component[] components = root.GetComponents<Component>();
                for (int c = 0; c < components.Length; c++)
                {
                    parts.Add(components[c] == null ? "missing script" : components[c].GetType().Name);
                }

                text.Append(root.name)
                    .Append(root.activeInHierarchy ? "" : " (inactive)")
                    .Append(" [").Append(string.Join("+", parts)).Append(']');
            }

            return roots.Length == 0 ? "(none)" : text.ToString();
        }

        private static void Remember(string message, string stack, LogType type)
        {
            if (!_recording || EngineMessages.Count >= 60) return;
            EngineMessages.Add($"{type}: {message}");
        }

        private static void Note(string line)
        {
            Diagnostics.AppendLine(line);
            Debug.Log("[intro-render] " + line);
            WriteDiagnostics();
        }

        private static void WriteDiagnostics()
        {
            try
            {
                var text = new StringBuilder(Diagnostics.ToString());
                if (EngineMessages.Count > 0)
                {
                    text.AppendLine("engine messages:");
                    for (int i = 0; i < EngineMessages.Count; i++) text.AppendLine("  " + EngineMessages[i]);
                }

                File.WriteAllText(OutputPath("diagnostics.txt"), text.ToString());
            }
            catch (Exception)
            {
                // Diagnostics are a courtesy to whoever reads the run; never let them fail it.
            }
        }

        private static void WriteFrame(Frame frame)
        {
            File.WriteAllBytes(OutputPath($"intro-{frame.Seconds:0.00}s.png"), frame.Png);
            File.WriteAllBytes(OutputPath($"intro-{frame.Seconds:0.00}s-small.png"), frame.SmallPng);
        }

        private static void WriteSummary(List<Frame> frames, string stage)
        {
            var summary = new StringBuilder();
            summary.AppendLine($"stopped during: {stage}");
            foreach (Frame frame in frames) summary.AppendLine(frame.Describe());
            try
            {
                File.WriteAllText(OutputPath("summary.txt"), summary.ToString());
            }
            catch (Exception)
            {
                // Same as above: the measurement matters, this copy of it does not.
            }

            WriteDiagnostics();
        }

        /// <summary>What one captured frame contained, in fractions of the frame.</summary>
        private readonly struct Frame
        {
            public readonly float Seconds;
            public readonly int Width;
            public readonly int Height;
            public readonly float InkFraction;
            public readonly float BlackFraction;
            public readonly float CentreX;
            public readonly float CentreY;
            public readonly float Left;
            public readonly float Right;
            public readonly float Bottom;
            public readonly float Top;
            public readonly float InkAspect;
            public readonly byte[] Png;

            /// <summary>The same frame at half size: small enough to travel in an annotation.</summary>
            public readonly byte[] SmallPng;

            /// <summary>How the frame was taken: the screen, or the camera drawn into a texture.</summary>
            public readonly string How;

            private Frame(float seconds, int width, int height, float inkFraction, float blackFraction,
                          float centreX, float centreY, float left, float right, float bottom,
                          float top, float inkAspect, byte[] png, byte[] smallPng, string how)
            {
                Seconds = seconds;
                Width = width;
                Height = height;
                InkFraction = inkFraction;
                BlackFraction = blackFraction;
                CentreX = centreX;
                CentreY = centreY;
                Left = left;
                Right = right;
                Bottom = bottom;
                Top = top;
                InkAspect = inkAspect;
                Png = png;
                SmallPng = smallPng;
                How = how;
            }

            public static Frame Capture(float time)
            {
                Shot shot = Take();
                int width = shot.Width;
                int height = shot.Height;
                Color32[] pixels = shot.Pixels;

                int ink = 0;
                int black = 0;
                int minX = width;
                int minY = height;
                int maxX = -1;
                int maxY = -1;
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        Color32 pixel = pixels[row + x];
                        float luminance = (0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b) / 255f;
                        if (luminance >= InkLuminance)
                        {
                            ink++;
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                        else if (luminance <= BlackLuminance)
                        {
                            black++;
                        }
                    }
                }

                float total = width * height;
                bool found = maxX >= minX && maxY >= minY;
                float inkWidth = found ? maxX - minX + 1 : 0f;
                float inkHeight = found ? maxY - minY + 1 : 0f;
                return new Frame(
                    time, width, height,
                    ink / total, black / total,
                    found ? ((minX + maxX) * 0.5f) / width : 0.5f,
                    found ? ((minY + maxY) * 0.5f) / height : 0.5f,
                    found ? minX / (float)width : 0f,
                    found ? (width - 1 - maxX) / (float)width : 0f,
                    found ? minY / (float)height : 0f,
                    found ? (height - 1 - maxY) / (float)height : 0f,
                    found && inkHeight > 0f ? inkWidth / inkHeight : 0f,
                    shot.Png,
                    HalfSize(pixels, width, height, shot.Png),
                    shot.How);
            }

            /// <summary>
            /// The frame the engine just drew.
            /// </summary>
            /// <remarks>
            /// On a runner there is no window, so there is nothing for a screen capture to read: the
            /// frame is taken by rendering the scene's own camera into a texture and reading that
            /// back — the same camera, the same objects, the same picture. On a machine with a real
            /// window the screen capture is the more faithful of the two and is used first. Either
            /// way the frame says which way it was taken, so nobody has to guess what they are
            /// looking at.
            /// </remarks>
            private static Shot Take()
            {
                if (Application.isBatchMode)
                {
                    Note("batch mode: there is no window to capture, so the scene's own camera is "
                         + "rendered into a texture and the frame is read back from it");
                    try
                    {
                        return FromCamera();
                    }
                    catch (Exception error)
                    {
                        Note($"rendering the camera into a texture failed: {error.GetType().Name}: "
                             + $"{error.Message}; trying a screen capture instead");
                    }
                }

                Texture2D screen = null;
                try
                {
                    screen = ScreenCapture.CaptureScreenshotAsTexture();
                }
                catch (Exception error)
                {
                    Note($"ScreenCapture threw {error.GetType().Name}: {error.Message}; "
                         + "rendering the camera into a texture instead");
                }

                if (screen != null && screen.width >= 32 && screen.height >= 32)
                {
                    Color32[] pixels = screen.GetPixels32();
                    int width = screen.width;
                    int height = screen.height;
                    byte[] png = Encode(pixels, width, height);
                    Object.Destroy(screen);
                    return new Shot(pixels, width, height, png, "screen capture");
                }

                if (screen != null)
                {
                    Note($"ScreenCapture returned {screen.width}x{screen.height}, too small to measure; "
                         + "rendering the camera into a texture instead");
                    Object.Destroy(screen);
                }

                return FromCamera();
            }

            private static Shot FromCamera()
            {
                Camera camera = Camera.main;
                if (camera == null)
                {
                    throw new InvalidOperationException(
                        "there is no camera tagged MainCamera to render, so the intro cannot be photographed");
                }

                int width = Screen.width;
                int height = Screen.height;
                if (width < 320 || height < 320)
                {
                    width = 1280;
                    height = 720;
                }

                float scale = Mathf.Min(1f, 1920f / width);
                width = Mathf.Max(2, Mathf.RoundToInt(width * scale));
                height = Mathf.Max(2, Mathf.RoundToInt(height * scale));

                RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                RenderTexture previousTarget = camera.targetTexture;
                RenderTexture previousActive = RenderTexture.active;
                Color32[] pixels;
                try
                {
                    camera.targetTexture = target;
                    camera.Render();
                    RenderTexture.active = target;
                    var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    texture.Apply(false, false);
                    pixels = texture.GetPixels32();
                    Object.Destroy(texture);
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    RenderTexture.active = previousActive;
                    RenderTexture.ReleaseTemporary(target);
                }

                Note($"the frame was taken by rendering {camera.name} into a {width}x{height} texture");
                return new Shot(pixels, width, height, Encode(pixels, width, height), "camera into texture");
            }

            private static byte[] Encode(Color32[] pixels, int width, int height)
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                byte[] png = ImageConversion.EncodeToPNG(texture);
                Object.Destroy(texture);
                return png;
            }

            /// <summary>
            /// A half-size copy of a frame, as PNG. Averaging four pixels into one keeps the mark's
            /// soft edges soft; a nearest-neighbour halving would make them jagged, which is the one
            /// thing a screenshot of a logo must not do.
            /// </summary>
            private static byte[] HalfSize(Color32[] pixels, int width, int height, byte[] full)
            {
                int halfWidth = Mathf.Max(1, width / 2);
                int halfHeight = Mathf.Max(1, height / 2);
                if (halfWidth < 8 || halfHeight < 8) return full;

                var small = new Color32[halfWidth * halfHeight];
                for (int y = 0; y < halfHeight; y++)
                {
                    int top = (y * 2) * width;
                    int bottom = Mathf.Min(height - 1, (y * 2) + 1) * width;
                    for (int x = 0; x < halfWidth; x++)
                    {
                        int left = x * 2;
                        int right = Mathf.Min(width - 1, left + 1);
                        Color32 a = pixels[top + left];
                        Color32 b = pixels[top + right];
                        Color32 c = pixels[bottom + left];
                        Color32 d = pixels[bottom + right];
                        small[(y * halfWidth) + x] = new Color32(
                            (byte)((a.r + b.r + c.r + d.r) / 4),
                            (byte)((a.g + b.g + c.g + d.g) / 4),
                            (byte)((a.b + b.b + c.b + d.b) / 4),
                            (byte)((a.a + b.a + c.a + d.a) / 4));
                    }
                }

                return Encode(small, halfWidth, halfHeight) ?? full;
            }

            public string Describe()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "t={0:0.00}s {1}x{2} via {3} ink={4:P2} black={5:P2} centre=({6:0.000},{7:0.000}) "
                    + "margins L{8:0.000} R{9:0.000} B{10:0.000} T{11:0.000} aspect={12:0.000}",
                    Seconds, Width, Height, How, InkFraction, BlackFraction, CentreX, CentreY,
                    Left, Right, Bottom, Top, InkAspect);
            }
        }

        /// <summary>One captured frame: its pixels, its size, and how it was taken.</summary>
        private readonly struct Shot
        {
            public readonly Color32[] Pixels;
            public readonly int Width;
            public readonly int Height;
            public readonly byte[] Png;
            public readonly string How;

            public Shot(Color32[] pixels, int width, int height, byte[] png, string how)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
                Png = png;
                How = how;
            }
        }
    }
}
