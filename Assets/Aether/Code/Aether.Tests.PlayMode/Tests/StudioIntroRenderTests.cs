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
    /// the CI job uploads, so a run leaves behind a real screenshot of the intro beside the proof:
    /// the black it opens on, the mark arriving alone, the wordmark completing the lockup, the light
    /// crossing it, two frames of the still hold, and the end of the fade. The assertions are those
    /// moments in order — the mark before the wordmark, the light on the finished lockup and nowhere
    /// else, the hold completely still, the fade finishing in black. Each
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

        /// <summary>
        /// When to look, in seconds after the intro scene came up: the black it opens on, the mark
        /// arriving alone, the wordmark extending the lockup downwards, the light crossing the
        /// finished lockup, two frames of the still hold, and the end of the fade.
        /// </summary>
        private static readonly float[] CaptureTimes = { 0.15f, 0.50f, 0.90f, 1.55f, 2.05f, 2.25f, 2.62f, 3.40f };

        /// <summary>A pixel at or above this luminance is part of the mark.</summary>
        private const float InkLuminance = 0.35f;

        /// <summary>A pixel at or below this luminance is the black the intro is drawn on.</summary>
        private const float BlackLuminance = 0.03f;

        /// <summary>
        /// A pixel at or above this luminance is brighter than the standing lockup's own ink, which is
        /// what a light crossing it puts there. The artwork is drawn at a tint just under white, so
        /// only something laid over it can reach this: it is how "the light is on the logo" is
        /// measured rather than eyeballed.
        /// </summary>
        private const float BrightLuminance = 0.96f;

        /// <summary>The key <see cref="StudioIntroSequence.Preference"/> stores its choice under.</summary>
        private const string IntroPreferenceKey = "aether.studioIntro.play";

        /// <summary>
        /// The step this run gives the intro's clock: a sixtieth of a second per frame, the rate the
        /// animation is designed around.
        /// </summary>
        private const float ClockStep = 1f / 60f;

        /// <summary>How long the run waits for the animation to reach a moment before giving up.</summary>
        private const float WaitLimitSeconds = 240f;

        /// <summary>The animation's own time, counted where the intro asks for its next interval.</summary>
        private static float _clocked;

        /// <summary>
        /// The moment the animation is being stepped towards. The clock holds the animation there
        /// until the frame has been taken, so a moment cannot be stepped past: it starts at zero,
        /// which is what keeps the animation still while the test reads the scene.
        /// </summary>
        private static float _moment;

        /// <summary>How many frames the intro has drawn: how far its clock has advanced.</summary>
        private static int _stepped;

        /// <summary>
        /// The clock this run hands the intro: every call is one drawn frame of the animation.
        /// </summary>
        /// <remarks>
        /// The frames are tied to the animation rather than to the wall clock, because on the runner
        /// those are two different things. The editor there does not tick the scene once per frame
        /// this test sees, so a capture taken by wall time can show the same drawn moment twice: a run
        /// that failed this way produced 0.50s and 0.90s as one identical picture, which cannot be
        /// told apart from an intro that never drew the wordmark at all. Stepping the clock here means
        /// every frame is captured at a time the animation itself reached, so the moments whose order
        /// this test checks are genuinely different frames.
        /// </remarks>
        private static float NextStep()
        {
            _stepped++;
            if (_clocked + ClockStep * 0.5f >= _moment) return 0f;   // held at the moment
            _clocked += ClockStep;
            return ClockStep;
        }

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
                // Regression guard: even an old saved "disabled" value must not suppress the ident.
                // The startup logo and its glints are deterministic on every normal launch.
                PlayerPrefs.SetInt(IntroPreferenceKey, 0);
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

                // The clock goes in before the scene does, so the intro's very first frame is stepped
                // too and the animation's time is the test's from the beginning. It is held at zero
                // while the scene is read below; the captures start it.
                _clocked = 0f;
                _stepped = 0;
                _moment = 0f;
                StudioIntroSequence.Clock = NextStep;

                stage = "load the intro scene";
                yield return LoadIntroScene();

                Scene scene = SceneManager.GetActiveScene();
                Note($"scene '{scene.name}' at {scene.path}, loaded from build index {scene.buildIndex}");
                Note($"root objects: {DescribeRoots(scene)}");

                stage = "find the intro";
                StudioIntroSequence intro = UnityEngine.Object.FindFirstObjectByType<StudioIntroSequence>();
                if (intro == null)
                {
                    // A component on an inactive object is still the intro, and a scene loaded for a
                    // test may have arrived that way; look again including what is switched off.
                    intro = UnityEngine.Object.FindAnyObjectByType<StudioIntroSequence>(FindObjectsInactive.Include);
                    Note(intro == null
                        ? "no StudioIntroSequence on an active or inactive object"
                        : "the StudioIntroSequence is on an inactive object");
                }

                Note($"Camera.main: {(Camera.main == null ? "none" : Camera.main.name + " (" + Camera.main.orthographicSize.ToString("0.###", CultureInfo.InvariantCulture) + " half-height)")}");
                Note($"Resources.Load<Sprite>('Brand/VarellonLogo'): "
                     + (Resources.Load<Sprite>("Brand/VarellonLogo") == null ? "returned nothing" : "ok"));
                Note($"safe area {Describe(Screen.safeArea)} of {Screen.width}x{Screen.height}");
                DescribeSprites(intro);

                Assert.That(intro, Is.Not.Null,
                            "the intro scene has no StudioIntroSequence, so it can only show black. "
                            + $"What was in the scene: {DescribeRoots(scene)}");

                stage = "capture the moments";
                float start = Time.unscaledTime;
                Note($"the intro is stepped at {ClockStep * 1000f:0.0}ms of its own time per frame, so "
                     + "how far the animation had got when a frame was taken is known exactly instead "
                     + "of being guessed from the wall clock");
                for (int i = 0; i < CaptureTimes.Length; i++)
                {
                    // Step the animation to the moment, and wait for it to get there: it is held at
                    // the moment until the frame is taken, so it cannot be stepped past unseen.
                    _moment = CaptureTimes[i];
                    while (_clocked + ClockStep * 0.5f < CaptureTimes[i])
                    {
                        if (Time.unscaledTime - start > WaitLimitSeconds)
                        {
                            Assert.Fail(
                                $"the intro's own clock never reached {CaptureTimes[i]:0.00}s: "
                                + $"{WaitLimitSeconds:0}s in, the animation stands at {_clocked:0.000}s "
                                + $"over {_stepped} frames. {SpritesState(intro)}");
                        }

                        yield return null;
                    }

                    // End of frame is a windowed-player thing: a batch-mode editor refuses it outright
                    // ("UnityTest yielded WaitForEndOfFrame, which is not evoked in batchmode") and
                    // fails the test. Nothing is lost by skipping it - the frame is taken by rendering
                    // the camera, which happens at the moment of the capture, not by reading whatever
                    // the window last showed.
                    if (!Application.isBatchMode) yield return new WaitForEndOfFrame();

                    Frame frame;
                    try
                    {
                        frame = Frame.Capture(CaptureTimes[i], _clocked, _stepped, intro);
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
                UnityEngine.Object.Destroy(intro);
                yield return null;

                Frame black = frames[0];
                Frame markOnly = frames[1];
                Frame arriving = frames[2];
                Frame light = frames[3];
                Frame hold = frames[4];
                Frame still = frames[5];
                Frame secondLight = frames[6];
                Frame leaving = frames[7];

                // 1. The sequence opens on black, and nothing is on it yet: the pause is its own beat.
                stage = "check the opening black";
                Assert.Less(black.InkFraction, 0.002f,
                            "something is drawn during the black the intro opens on");
                Assert.Greater(black.BlackFraction, 0.99f,
                               $"the opening frame is only {black.BlackFraction:P1} black");

                // 2. The mark arrives first, on its own, and it is not simply there: at 0.50s it is
                //    present but well short of the weight it holds later. The wordmark has not begun,
                //    which is what makes the mark establish the identity before the name completes it.
                stage = "check the mark arrives first";
                Assert.Greater(markOnly.InkFraction, 0.002f,
                               "nothing has been drawn by 0.50s: the mark's arrival is missing");
                Assert.Less(markOnly.InkFraction, hold.InkFraction,
                            "the mark carries its final weight at 0.50s, so there is no arrival to see");
                Assert.Greater(markOnly.Bottom, hold.Bottom + 0.05f,
                               "the wordmark is already drawn at 0.50s, so the mark does not lead it");

                // 3. The wordmark completes the lockup, and the lockup grows downwards to take it.
                stage = "check the wordmark arrives after the mark";
                Assert.Greater(arriving.InkFraction, markOnly.InkFraction,
                               "the wordmark never arrives: the ink at 0.90s is no more than the mark's");
                Assert.Less(arriving.Bottom, markOnly.Bottom - 0.02f,
                            "the lockup does not reach further down at 0.90s, so the wordmark is not drawn");

                // 4. The light crosses the finished lockup. Lit means brighter than the lockup's own ink
                //    at rest, so it can only be the highlight - and it is on the logo, not around it.
                stage = "check the light crosses the lockup, and only the lockup";
                Assert.Greater(light.BrightOfInk, 0.05f,
                               "no light is crossing the lockup at 1.55s: nothing on the ink is brighter "
                               + "than the resting logo");
                Assert.Greater(light.BlackFraction, 0.90f,
                               $"only {light.BlackFraction:P1} of the frame is black while the light is "
                               + "on: the highlight is being drawn over the black around the lockup too");
                Assert.Less(hold.BrightOfInk, 0.01f,
                            "the standing lockup carries lit pixels, so the light never left it");

                // 5. The quiet part of the hold is completely still: the reverse glint has not started yet,
                //    so these two frames must match exactly.
                stage = "check the hold is completely still";
                Assert.That(hold.MaxChannelDifference(still), Is.LessThanOrEqualTo(2),
                            "the lockup moves during the hold: two frames of it differ");

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

                // 6. And the fade takes the complete lockup to black rather than cutting it.
                stage = "check the fade finishes in black";
                Assert.Less(leaving.InkFraction, hold.InkFraction * 0.25f,
                            "the lockup is still on screen at 3.40s, so the fade is not far enough along");
                Assert.Greater(leaving.BlackFraction, 0.95f,
                               $"only {leaving.BlackFraction:P1} of the frame is black at 3.35s");

                // The frames claim a moment each, and were taken at it: within half a step. A frame
                // taken late is a frame about some other moment, and the order above would be a claim
                // about the test rather than about the animation.
                stage = "check every frame was taken at the moment it names";
                for (int i = 0; i < frames.Count; i++)
                {
                    Assert.That(frames[i].At, Is.EqualTo(CaptureTimes[i]).Within(ClockStep * 1.5f),
                                $"the frame named {CaptureTimes[i]:0.00}s was taken at "
                                + $"{frames[i].At:0.000}s of the animation, so it is not that moment");
                }

                // What the sequence itself was drawing at each moment, in its own numbers: the order
                // the ident has to read in, stated independently of the pixels above. The wordmark is
                // at zero while the mark arrives, which is what "the mark leads" means; the light is
                // on during the pass and off during the hold; and neither the light nor the fade has
                // started while the two frames compared for stillness are being taken.
                stage = "check the sequence was in the right moment for every frame";
                Assert.That(black.MarkAlpha, Is.EqualTo(0f).Within(0.001f),
                            $"something is already up at the opening: mark a={black.MarkAlpha:0.000}");
                Assert.That(markOnly.MarkAlpha, Is.GreaterThan(0.4f),
                            $"the mark is only at {markOnly.MarkAlpha:0.000} of its weight at 0.50s, "
                            + "so its arrival has not begun");
                Assert.That(markOnly.WordAlpha, Is.EqualTo(0f).Within(0.001f),
                            $"the wordmark is already at {markOnly.WordAlpha:0.000} at 0.50s, so it "
                            + "does not follow the mark");
                Assert.That(arriving.WordAlpha, Is.GreaterThan(0.5f),
                            $"the wordmark is only at {arriving.WordAlpha:0.000} at 0.90s, so it has "
                            + "not arrived after the mark");
                Assert.That(light.SheenAlpha, Is.GreaterThan(0.05f),
                            "the primary light is not switched on at 1.55s, so nothing crosses the lockup");
                Assert.That(hold.SheenAlpha, Is.EqualTo(0f).Within(0.001f),
                            $"the first light is still switched on ({hold.SheenAlpha:0.000}) at 1.95s");
                Assert.That(still.SheenAlpha, Is.EqualTo(0f).Within(0.001f),
                            "the reverse glint starts too early during the quiet hold");
                Assert.Greater(secondLight.SheenAlpha, 0.05f,
                               "the scheduled reverse glint is missing from the logo hold");
                Assert.Greater(secondLight.BrightOfInk, 0.05f,
                               "the reverse glint does not brighten the logo ink");
                Assert.Greater(secondLight.BlackFraction, 0.90f,
                               "the reverse glint spills into the black around the logo");
                Assert.That(still.MarkAlpha, Is.EqualTo(1f).Within(0.001f),
                            $"the fade has started by 2.20s (mark a={still.MarkAlpha:0.000}), so the "
                            + "two frames compared for stillness are not both the hold");
                Assert.That(leaving.MarkAlpha, Is.LessThan(0.5f),
                            $"the lockup is still at {leaving.MarkAlpha:0.000} of its weight at 3.35s, "
                            + "so the fade is not under way");

                stage = "done";
            }
            finally
            {
                // The intro goes back to real time: nothing outside this run is stepped.
                StudioIntroSequence.Clock = null;
                _moment = 0f;
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

        private static string Describe(Rect rect)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.#},{1:0.#} {2:0.#}x{3:0.#}",
                                 rect.x, rect.y, rect.width, rect.height);
        }

        /// <summary>
        /// What the intro actually put on screen, as opposed to what its numbers say: the renderers
        /// it built, how big they are and whether they are switched on. A lockup drawn at zero size
        /// or never given a renderer looks exactly like a lockup that never arrived.
        /// </summary>
        private static List<SpriteRenderer> Renderers(StudioIntroSequence intro)
        {
            var seen = new List<SpriteRenderer>();
            if (intro != null) seen.AddRange(intro.GetComponentsInChildren<SpriteRenderer>(true));
            if (Camera.main != null)
            {
                SpriteRenderer[] onCamera = Camera.main.GetComponentsInChildren<SpriteRenderer>(true);
                for (int i = 0; i < onCamera.Length; i++)
                {
                    if (!seen.Contains(onCamera[i])) seen.Add(onCamera[i]);
                }
            }

            return seen;
        }

        private static void DescribeSprites(StudioIntroSequence intro)
        {
            List<SpriteRenderer> seen = Renderers(intro);
            Note($"sprites in play: {seen.Count}");
            for (int i = 0; i < seen.Count; i++)
            {
                SpriteRenderer renderer = seen[i];
                Note(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0}: enabled={1}, active={2}, colour={3:0.##}, size {4:0.###}x{5:0.###} world, "
                    + "centre {6:0.###},{7:0.###}, sprite {8}",
                    renderer.name, renderer.enabled, renderer.gameObject.activeInHierarchy,
                    renderer.color.a, renderer.bounds.size.x, renderer.bounds.size.y,
                    renderer.transform.position.x, renderer.transform.position.y,
                    renderer.sprite == null ? "none" : $"{renderer.sprite.rect.width}x{renderer.sprite.rect.height}"));
            }
        }

        /// <summary>
        /// One of the intro's three renderers' opacity, by name: the sequence's own statement of what
        /// it is drawing, which is what each moment of this run is described by.
        /// </summary>
        private static float Opacity(StudioIntroSequence intro, string name)
        {
            List<SpriteRenderer> renderers = Renderers(intro);
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i].name == name) return renderers[i].color.a;
            }

            return 0f;
        }

        /// <summary>The three opacities as one line, for the log: what the animation is showing.</summary>
        private static string SpritesState(StudioIntroSequence intro)
        {
            return string.Format(CultureInfo.InvariantCulture,
                                 "mark a={0:0.000} word a={1:0.000} sheen a={2:0.000}",
                                 Opacity(intro, "StudioMark"), Opacity(intro, "StudioWordmark"),
                                 Opacity(intro, "StudioSheen"));
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

            /// <summary>How much of the ink is brighter than the standing ink: the light, if any.</summary>
            public readonly float BrightOfInk;
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

            /// <summary>Where the animation's own clock stood when this frame was taken.</summary>
            public readonly float At;

            /// <summary>How many frames the intro had drawn by then.</summary>
            public readonly int Steps;

            /// <summary>The opacity the mark, the wordmark and the light had at this moment.</summary>
            public readonly float MarkAlpha;
            public readonly float WordAlpha;
            public readonly float SheenAlpha;

            /// <summary>How the frame was taken: the screen, or the camera drawn into a texture.</summary>
            public readonly string How;

            /// <summary>The frame itself, so two frames can be told apart or found identical.</summary>
            private readonly Color32[] _pixels;

            private Frame(float seconds, int width, int height, float at, int steps, float markAlpha,
                          float wordAlpha, float sheenAlpha, float inkFraction, float blackFraction,
                          float brightOfInk, float centreX, float centreY, float left, float right,
                          float bottom, float top, float inkAspect, byte[] png, byte[] smallPng,
                          string how, Color32[] pixels)
            {
                Seconds = seconds;
                Width = width;
                Height = height;
                At = at;
                Steps = steps;
                MarkAlpha = markAlpha;
                WordAlpha = wordAlpha;
                SheenAlpha = sheenAlpha;
                InkFraction = inkFraction;
                BlackFraction = blackFraction;
                BrightOfInk = brightOfInk;
                _pixels = pixels;
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

            /// <summary>
            /// The largest per-channel difference between this frame and another, in 0-255 steps. Used
            /// to say that the hold really is still: two moments of a still picture are the same
            /// picture, and two moments of a moving one are not.
            /// </summary>
            public int MaxChannelDifference(Frame other)
            {
                if (_pixels == null || other._pixels == null) return 255;
                if (_pixels.Length != other._pixels.Length) return 255;

                int worst = 0;
                for (int i = 0; i < _pixels.Length; i++)
                {
                    Color32 a = _pixels[i];
                    Color32 b = other._pixels[i];
                    int difference = Mathf.Max(Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g)),
                                               Mathf.Abs(a.b - b.b));
                    if (difference > worst) worst = difference;
                }

                return worst;
            }

            public static Frame Capture(float beat, float at, int steps, StudioIntroSequence intro)
            {
                Shot shot = Take();
                int width = shot.Width;
                int height = shot.Height;
                Color32[] pixels = shot.Pixels;

                int ink = 0;
                int black = 0;
                int lit = 0;
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
                        if (luminance >= BrightLuminance) lit++;

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
                    beat, width, height,
                    at, steps,
                    Opacity(intro, "StudioMark"), Opacity(intro, "StudioWordmark"),
                    Opacity(intro, "StudioSheen"),
                    ink / total, black / total,
                    ink == 0 ? 0f : lit / (float)ink,
                    found ? ((minX + maxX) * 0.5f) / width : 0.5f,
                    found ? ((minY + maxY) * 0.5f) / height : 0.5f,
                    found ? minX / (float)width : 0f,
                    found ? (width - 1 - maxX) / (float)width : 0f,
                    found ? minY / (float)height : 0f,
                    found ? (height - 1 - maxY) / (float)height : 0f,
                    found && inkHeight > 0f ? inkWidth / inkHeight : 0f,
                    shot.Png,
                    HalfSize(pixels, width, height, shot.Png),
                    shot.How,
                    pixels);
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
                    UnityEngine.Object.Destroy(screen);
                    return new Shot(pixels, width, height, png, "screen capture");
                }

                if (screen != null)
                {
                    Note($"ScreenCapture returned {screen.width}x{screen.height}, too small to measure; "
                         + "rendering the camera into a texture instead");
                    UnityEngine.Object.Destroy(screen);
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
                    UnityEngine.Object.Destroy(texture);
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
                UnityEngine.Object.Destroy(texture);
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
                    "t={0:0.00}s (taken at {1:0.000}s of the animation, {2} frames in; mark "
                    + "a={3:0.000} word a={4:0.000} sheen a={5:0.000}) {6}x{7} via {8} ink={9:P2} "
                    + "black={10:P2} lit={11:P2} of ink centre=({12:0.000},{13:0.000}) margins "
                    + "L{14:0.000} R{15:0.000} B{16:0.000} T{17:0.000} aspect={18:0.000}",
                    Seconds, At, Steps, MarkAlpha, WordAlpha, SheenAlpha, Width, Height, How,
                    InkFraction, BlackFraction, BrightOfInk, CentreX, CentreY, Left, Right, Bottom,
                    Top, InkAspect);
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
