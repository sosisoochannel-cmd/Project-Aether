using System;
using System.Collections;
using Aether.Gameplay.Sound;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Aether.Gameplay.Flow
{
    /// <summary>
    /// The studio's intro: black, the Varellon mark, the wordmark settling under it, a short hold,
    /// and a soft exit into the next scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It plays in its own scene, in front of everything else.</b> The scene is first in the build
    /// order, so it is the first thing the app shows, and it hands over to <see cref="_nextScene"/>
    /// when it ends. Repointing that one field at a main menu is the whole change the next stage
    /// needs; nothing here knows what the next scene contains.
    /// </para>
    /// <para>
    /// <b>The screen behind it is black, and nothing else is drawn on it.</b> The scene's camera clears
    /// to solid black, and the intro draws nothing else — no backdrop, no frame, no caption. The Unity
    /// splash that used to come first is switched off in the project's own player settings, so the
    /// first thing the app shows is this.
    /// </para>
    /// <para>
    /// <b>Sprites parented to the camera, not uGUI.</b> The same choice the on-screen controls make,
    /// for the same reasons: a Canvas, an EventSystem and an input-UI module are three things that
    /// need wiring and can be got wrong in a build nobody can open in an Editor, and the intro is two
    /// images that fade. Layout is arithmetic in fractions of the safe area, which is why it is
    /// checkable without a device — <c>tools/verify/intro.py</c> proves the lockup stays inside the
    /// safe area, keeps its aspect ratio (no stretching) and stays centred on every shape of screen
    /// the game is expected to meet, using the numbers in <see cref="Layout"/> in this file.
    /// </para>
    /// <para>
    /// <b>One PNG, two parts.</b> The supplied artwork is a single image: the Varellon mark with the
    /// VAR-ELLON STUDIOS wordmark under it. Rather than cut the file in two, the studio mark and the
    /// wordmark are found in the pixels at load time — the artwork is split by
    /// <see cref="Layout.WordmarkSplit"/>, and each half is trimmed to the ink inside it. That is
    /// also why the two can be animated separately: the mark leads the reveal and the wordmark
    /// settles a beat later into the slot the artwork already left for it, which is the difference
    /// between a logo reveal and a picture fading in.
    /// </para>
    /// <para>
    /// <b>The artwork's coverage becomes the alpha channel.</b> A PNG with an alpha channel is used
    /// as it is imported: its alpha is the shape, and the pixels are recoloured to
    /// <see cref="_markColour"/> so the mark is legible on the black backdrop whatever ink colour
    /// the artwork happens to use. An export that has <b>no</b> alpha — a light canvas with the logo
    /// drawn on it — is keyed instead: the canvas is subtracted by luminance
    /// (<see cref="Artwork"/>) and the logo becomes the shape. Both paths produce the same thing: ink
    /// with soft edges and no background, which is what the black screen behind it requires. The
    /// brand gate checks the committed file's pixels against these numbers before a build.
    /// </para>
    /// <para>
    /// <b>Everything is eased and nothing snaps.</b> Presence, scale and a small vertical rise are
    /// curves over elapsed time, the exit starts from wherever either part currently is (skipping
    /// mid-reveal fades from the current presence, not from full), and the whole sequence is under
    /// three seconds because an intro is a signature, not a wait.
    /// </para>
    /// <para>
    /// <b>Skipping and switching it off are both seams that already exist.</b> A tap, a key or a
    /// button ends the intro early after a short grace period — the grace is there because the tap
    /// that launched the app is still on the screen — and <see cref="Preference"/> is the setting a
    /// future settings screen writes when a player would rather never see it. The intro is silent
    /// today: it requests its cues through <see cref="SoundDirector"/> like the rest of the game, so
    /// it becomes audible the moment an audio backend exists, and no audio asset is invented here.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class StudioIntroSequence : SceneFlowOwner
    {
        /// <summary>
        /// Where the lockup sits, in fractions of the <b>safe area</b>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The lockup — mark and wordmark together — is fitted with one scale factor from both
        /// limits, never one per axis, so it can never be stretched, and it is centred on the safe
        /// area rather than the screen, so a notch on one side does not push it off-centre. Both
        /// parts share that one factor, so their relative sizes and the gap between them stay as the
        /// artwork drew them.
        /// </para>
        /// <para>
        /// The limits are deliberately generous in neither direction: a mark that fills the screen is
        /// a banner, and a mark at ten percent is a watermark. <c>tools/verify/intro.py</c> reads
        /// these numbers and proves the result is inside the safe area, aspect-correct and centred on
        /// phones and tablets, in portrait and in landscape, with and without a cutout.
        /// </para>
        /// </remarks>
        public static class Layout
        {
            /// <summary>Largest fraction of the safe area's width the lockup may occupy.</summary>
            public const float MaxWidthFraction = 0.60f;

            /// <summary>
            /// Largest fraction of the safe area's height the lockup may occupy.
            /// </summary>
            /// <remarks>
            /// The lockup is taller than it is wide, so on a phone held sideways this is the limit that
            /// decides its size. It is large enough to be read as a studio card and small enough to
            /// leave the screen around it empty, which is what "a mark on black" means.
            /// </remarks>
            public const float MaxHeightFraction = 0.40f;

            /// <summary>Scale the lockup starts at, as a fraction of its final size.</summary>
            public const float RevealScale = 0.965f;

            /// <summary>How far below its resting place the mark starts, in safe-area heights.</summary>
            public const float RevealRise = 0.018f;

            /// <summary>Scale the lockup grows to as it leaves.</summary>
            public const float ExitScale = 1.015f;

            /// <summary>How far the lockup drifts upwards as it leaves, in safe-area heights.</summary>
            public const float ExitRise = 0.010f;

            /// <summary>
            /// Where the artwork splits into mark and wordmark, as a fraction of its height
            /// <b>from the top</b>: above this line is the mark, below it is the wordmark.
            /// </summary>
            /// <remarks>
            /// The supplied artwork has a clear band between the two, so this is a line drawn through
            /// empty pixels; both halves are trimmed to their own ink afterwards, which is what makes
            /// a slightly wrong split harmless. The brand gate re-measures the committed file with
            /// this number and fails if either half would come out empty.
            /// </remarks>
            public const float WordmarkSplit = 0.605f;

            /// <summary>Fraction of the reveal before the wordmark starts: the mark leads.</summary>
            public const float RevealOverlap = 0.17f;

            /// <summary>How far the wordmark rises into its place, in safe-area heights.</summary>
            public const float WordmarkRise = 0.008f;
        }

        /// <summary>
        /// How an artwork with no alpha channel is keyed.
        /// </summary>
        /// <remarks>
        /// A pixel at or above <see cref="BackgroundLuminance"/> is canvas and becomes fully
        /// transparent; one at or below <see cref="InkLuminance"/> is ink and becomes fully opaque;
        /// between the two the coverage ramps, which keeps the anti-aliased edges of a letter or a
        /// feather smooth instead of jagged. The gap between the two numbers is what stops a
        /// slightly warm or slightly grey canvas from turning into a haze of half-transparent ink.
        /// <c>tools/verify/intro.py</c> measures the committed artwork against these numbers.
        /// </remarks>
        public static class Artwork
        {
            /// <summary>Luminance (0-1) at or above which a pixel is background.</summary>
            public const float BackgroundLuminance = 0.78f;

            /// <summary>Luminance (0-1) at or below which a pixel is ink.</summary>
            public const float InkLuminance = 0.20f;
        }

        /// <summary>
        /// How long each part of the intro lasts, in seconds.
        /// </summary>
        /// <remarks>
        /// Black hold, reveal, hold, exit: 2.80 seconds including the beat of black before the next
        /// scene loads. That budget is a requirement, not a taste — an intro of this kind is two to
        /// three seconds — and <c>tools/verify/intro.py</c> fails the build if the parts stop adding
        /// up to it, so buying a longer reveal means shortening something else on purpose.
        /// </remarks>
        public static class Timing
        {
            /// <summary>Black before the mark appears. Covers the first frames of the app.</summary>
            public const float BlackHold = 0.35f;

            /// <summary>Fade-in and settle of the mark, and then of the wordmark behind it.</summary>
            public const float Reveal = 1.05f;

            /// <summary>The lockup at full presence. The pause that makes it read as a signature.</summary>
            public const float Hold = 0.70f;

            /// <summary>Fade-out of the lockup.</summary>
            public const float Exit = 0.55f;

            /// <summary>Black held after the lockup has gone, so the hand-over is not a hard cut.</summary>
            public const float HandOver = 0.15f;

            /// <summary>Input before this is ignored: the tap that launched the app is still landing.</summary>
            public const float SkipGrace = 0.45f;

            /// <summary>How long the shortened exit lasts when the player skips.</summary>
            public const float SkipExit = 0.35f;
        }

        /// <summary>
        /// What the player has chosen about the intro.
        /// </summary>
        /// <remarks>
        /// Nothing writes this yet — there is no settings screen — and it defaults to playing the
        /// intro, because a studio mark that has never been seen is not yet a signature. It exists
        /// now so that the day a settings screen arrives, "skip the intro" is a switch over state
        /// that already persists, not a redesign of this class.
        /// </remarks>
        public static class Preference
        {
            private const string Key = "aether.studioIntro.play";

            /// <summary>Whether the intro plays at all. True unless a player has turned it off.</summary>
            public static bool Enabled
            {
                get => PlayerPrefs.GetInt(Key, 1) != 0;
                set
                {
                    PlayerPrefs.SetInt(Key, value ? 1 : 0);
                    PlayerPrefs.Save();
                }
            }
        }

        [Tooltip("Resource path of the studio artwork: the mark, or a lockup with its wordmark.")]
        [SerializeField]
        private string _markResourcePath = "Brand/VarellonLogo";

        [Tooltip("Scene to load when the intro ends. The main menu's future home; 'Boot' stands in for it.")]
        [SerializeField]
        private string _nextScene = "Boot";

        [Tooltip("Colour the artwork is drawn in. Its shape comes from the alpha channel, or its ink.")]
        [SerializeField]
        private Color _markColour = new Color(0.949f, 0.937f, 0.918f, 1f);

        [Tooltip("Sound cue requested as the mark appears. Silent until an audio backend exists.")]
        [SerializeField]
        private string _revealCueId = "studio.intro.reveal";

        [Tooltip("Sound cue requested as the mark leaves.")]
        [SerializeField]
        private string _exitCueId = "studio.intro.exit";

        [Tooltip("Play as soon as the scene starts. Off to drive the sequence by hand in a test.")]
        [SerializeField]
        private bool _playOnAwake = true;

        private Camera _camera;
        private float _pixelsPerUnit = 100f;
        private Part _mark;
        private Part _wordmark;
        private Vector2 _lockupPixels;
        private Texture2D _generatedTexture;
        private Sprite _markSprite;
        private Sprite _wordmarkSprite;
        private bool _skipRequested;

        /// <summary>
        /// Ends the intro early, starting the exit from wherever the sequence has reached. For a
        /// future "skip" button of the intro's own, or a test that does not want to wait three
        /// seconds.
        /// </summary>
        /// <remarks>
        /// Unlike a tap it is not held back by <see cref="Timing.SkipGrace"/>: a tap may be the one
        /// that launched the app, but a call to this method is deliberate.
        /// </remarks>
        public void Skip()
        {
            _skipRequested = true;
        }

        private void Awake()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                Debug.LogError(
                    "The studio intro scene has no camera tagged MainCamera, so nothing can be drawn. " +
                    "The intro will end immediately; the next scene still loads.", this);
                return;
            }

            BuildLockup();
        }

        private void Start()
        {
            if (!_mark.Present && !_wordmark.Present)
            {
                // Nothing to show, or nothing the player wants to see: the app still has to reach the
                // next scene, and it must not sit on a black screen to do it.
                HandOver();
                return;
            }

            if (!_playOnAwake || !Preference.Enabled)
            {
                HandOver();
                return;
            }

            StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            float elapsed = 0f;
            bool skipped = false;
            bool revealCued = false;
            bool exitCued = false;
            float markFadeFrom = 1f;
            float wordmarkFadeFrom = 1f;
            float exitStartsAt = Timing.BlackHold + Timing.Reveal + Timing.Hold;
            float exitLength = Timing.Exit;

            HideAll();

            while (true)
            {
                elapsed += Time.unscaledDeltaTime;

                if (!skipped && (_skipRequested || (elapsed >= Timing.SkipGrace && SkipRequested())))
                {
                    // Skipping mid-reveal must not jump: each part exits from wherever it currently is.
                    RevealAt(elapsed, false, out markFadeFrom, out _, out _);
                    RevealAt(elapsed, true, out wordmarkFadeFrom, out _, out _);
                    skipped = true;
                    exitStartsAt = elapsed;
                    exitLength = Timing.SkipExit;
                }

                if (!revealCued && elapsed >= Timing.BlackHold)
                {
                    revealCued = true;
                    SoundDirector.Request(_revealCueId, Vector2.zero);
                }

                if (!exitCued && elapsed >= exitStartsAt)
                {
                    exitCued = true;
                    SoundDirector.Request(_exitCueId, Vector2.zero);
                }

                if (elapsed >= exitStartsAt)
                {
                    float exit = EaseInOutCubic(Ramp(elapsed, exitStartsAt, exitLength));
                    float scale = Mathf.Lerp(1f, Layout.ExitScale, exit);
                    float rise = Layout.ExitRise * exit;
                    Draw(_mark, skipped ? markFadeFrom * (1f - exit) : 1f - exit, scale, rise);
                    Draw(_wordmark, skipped ? wordmarkFadeFrom * (1f - exit) : 1f - exit, scale, rise);
                }
                else
                {
                    RevealAt(elapsed, false, out float markPresence, out float markScale, out float markRise);
                    RevealAt(elapsed, true, out float wordPresence, out float wordScale, out float wordRise);
                    Draw(_mark, markPresence, markScale, markRise);
                    Draw(_wordmark, wordPresence, wordScale, wordRise);
                }

                if (elapsed >= exitStartsAt + exitLength) break;
                yield return null;
            }

            // The lockup is gone and the screen is black. A beat of black before the next scene keeps
            // the hand-over from reading as a cut.
            HideAll();
            yield return WaitUnscaled(Timing.HandOver);
            HandOver();
        }

        /// <summary>
        /// How present, how large and how high the given part is during the reveal.
        /// </summary>
        /// <remarks>
        /// The mark starts at <see cref="Timing.BlackHold"/>; the wordmark starts a fraction of the
        /// reveal later, so the mark arrives first and the wordmark resolves under it. Both end at the
        /// same moment, which is the moment the hold begins.
        /// <para>
        /// Presence and movement ride separate curves. The mark's opacity follows a curve that is soft
        /// at both ends, which is what makes it bloom rather than appear; the wordmark's arrives
        /// sooner and settles later, so it reads as resolving. The scale and the small rise behind both
        /// are gentler still, and never overshoot: nothing here bounces.
        /// </para>
        /// </remarks>
        private static void RevealAt(float elapsed, bool wordmark,
                                     out float presence, out float scale, out float rise)
        {
            float delay = wordmark ? Layout.RevealOverlap * Timing.Reveal : 0f;
            float linear = Ramp(elapsed, Timing.BlackHold + delay, Timing.Reveal - delay);
            float settle = EaseInOutSine(linear);
            presence = wordmark ? EaseOutCubic(linear) : settle;
            scale = Mathf.Lerp(Layout.RevealScale, 1f, settle);
            rise = -(wordmark ? Layout.WordmarkRise : Layout.RevealRise) * (1f - settle);
        }

        /// <summary>Waits real seconds, unaffected by time scale: the intro is not gameplay.</summary>
        private static IEnumerator WaitUnscaled(float seconds)
        {
            float waited = 0f;
            while (waited < seconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>Progress through a phase, clamped to [0, 1].</summary>
        private static float Ramp(float elapsed, float startsAt, float duration)
        {
            if (duration <= 0f) return 1f;
            return Mathf.Clamp01((elapsed - startsAt) / duration);
        }

        // -- the lockup -------------------------------------------------------------------------

        /// <summary>
        /// One drawn piece of the artwork: the mark, or the wordmark, or neither when the artwork
        /// does not have one.
        /// </summary>
        private readonly struct Part
        {
            public readonly SpriteRenderer Renderer;

            /// <summary>This piece's centre relative to the lockup's centre, in pixels, y up.</summary>
            public readonly Vector2 OffsetPixels;

            /// <summary>This piece's size in pixels: what the fit multiplies.</summary>
            public readonly Vector2 SizePixels;

            public Part(SpriteRenderer renderer, Vector2 offsetPixels, Vector2 sizePixels)
            {
                Renderer = renderer;
                OffsetPixels = offsetPixels;
                SizePixels = sizePixels;
            }

            public bool Present => Renderer != null;
        }

        /// <summary>
        /// Draws one piece for one frame: presence is its opacity, scale its size, and rise how far
        /// it has drifted up from its resting place, in safe-area heights.
        /// </summary>
        private void Draw(Part part, float presence, float scale, float rise)
        {
            if (part.Renderer == null) return;
            part.Renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(presence));
            Place(part.Renderer.transform, part.SizePixels, part.OffsetPixels, scale, rise);
        }

        /// <summary>
        /// Reads the artwork, keys it, splits it, and puts the two pieces on screen at zero opacity.
        /// </summary>
        private void BuildLockup()
        {
            Sprite source = Resources.Load<Sprite>(_markResourcePath);
            if (source == null)
            {
                Debug.LogError(
                    $"The studio artwork '{_markResourcePath}' was not found under " +
                    "Assets/Aether/Resources. The intro has nothing to show and hands over " +
                    "immediately.", this);
                return;
            }

            Texture2D texture = source.texture;
            if (texture == null || !texture.isReadable)
            {
                Debug.LogError(
                    $"The studio artwork '{_markResourcePath}' cannot be read from script, so the " +
                    "intro cannot build the mark from it. Import it with Read/Write enabled.", this);
                return;
            }

            Rect rect = source.textureRect;
            int x0 = Mathf.RoundToInt(rect.x);
            int y0 = Mathf.RoundToInt(rect.y);
            int width = Mathf.RoundToInt(rect.width);
            int height = Mathf.RoundToInt(rect.height);
            if (width < 2 || height < 2)
            {
                Debug.LogError($"The studio artwork '{_markResourcePath}' has no pixels to draw.", this);
                return;
            }

            // GetPixels32 has no rect overload, so the whole texture is fetched and the sprite's
            // patch is copied out of it. Rows run bottom-up and the copy keeps that order, which is
            // what the split below and the ink bounds assume.
            Color32[] whole = texture.GetPixels32();
            var pixels = new Color32[width * height];
            for (int row = 0; row < height; row++)
            {
                Array.Copy(whole, ((y0 + row) * texture.width) + x0, pixels, row * width, width);
            }

            bool hasAlpha = HasTransparency(pixels);
            byte[] coverage = Coverage(pixels, hasAlpha);
            if (!hasAlpha)
            {
                Debug.Log(
                    $"The studio artwork '{_markResourcePath}' has no alpha channel, so its light " +
                    $"background is being keyed out by luminance ({Artwork.BackgroundLuminance}.." +
                    $"{Artwork.InkLuminance}).", this);
            }

            // Pixel rows run bottom-up in Unity, so the band above the split line is the upper one.
            int bandTop = Mathf.Clamp(Mathf.RoundToInt(height * (1f - Layout.WordmarkSplit)), 1,
                                      height - 1);
            Bounds markBounds = InkBounds(coverage, width, bandTop, height);
            Bounds wordmarkBounds = InkBounds(coverage, width, 0, bandTop);

            if (!markBounds.Found)
            {
                Debug.LogError(
                    $"The studio artwork '{_markResourcePath}' has no ink above " +
                    $"{Layout.WordmarkSplit:P0} of its height, where the mark is expected. Check " +
                    "Layout.WordmarkSplit against the file.", this);
                return;
            }

            if (!wordmarkBounds.Found)
            {
                Debug.Log($"The studio artwork '{_markResourcePath}' has no wordmark below " +
                          $"{Layout.WordmarkSplit:P0} of its height; the mark is drawn alone.", this);
            }

            // The lockup is the box around everything that will be drawn: what the fit measures.
            Bounds lockup = markBounds.Union(wordmarkBounds);
            _lockupPixels = lockup.Size;
            Vector2 lockupCentre = lockup.Centre;

            _generatedTexture = BuildKeyedTexture(coverage, width, height);
            _pixelsPerUnit = source.pixelsPerUnit > 0f ? source.pixelsPerUnit : 100f;
            _markSprite = BuildSprite(_generatedTexture, markBounds, _pixelsPerUnit);
            _mark = BuildPart("StudioMark", _markSprite, markBounds, lockupCentre);

            if (wordmarkBounds.Found)
            {
                _wordmarkSprite = BuildSprite(_generatedTexture, wordmarkBounds, source.pixelsPerUnit);
                _wordmark = BuildPart("StudioWordmark", _wordmarkSprite, wordmarkBounds, lockupCentre);
            }
        }

        /// <summary>True when the artwork carries its own transparency, rather than needing a key.</summary>
        private static bool HasTransparency(Color32[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 250) return true;
            }

            return false;
        }

        /// <summary>
        /// The artwork's shape as coverage, one byte per pixel: the alpha channel when the file has
        /// one, its darkness when it does not.
        /// </summary>
        private static byte[] Coverage(Color32[] pixels, bool fromAlpha)
        {
            var coverage = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (fromAlpha)
                {
                    coverage[i] = pixel.a;
                    continue;
                }

                float luminance = (0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b) / 255f;
                float ink = (Artwork.BackgroundLuminance - luminance)
                           / (Artwork.BackgroundLuminance - Artwork.InkLuminance);
                coverage[i] = (byte)(Mathf.Clamp01(ink) * 255f);
            }

            return coverage;
        }

        /// <summary>
        /// The keyed texture: the coverage in <see cref="_markColour"/>, with no background at all.
        /// </summary>
        /// <remarks>
        /// This is the one place the artwork's own colours are dropped, and it is deliberate: the
        /// intro is black, so what has to survive the file is the shape, not the ink someone exported
        /// it in. A CPU copy is not kept — the intro runs once.
        /// </remarks>
        private Texture2D BuildKeyedTexture(byte[] coverage, int width, int height)
        {
            var tint = (Color32)_markColour;
            var pixels = new Color32[coverage.Length];
            for (int i = 0; i < coverage.Length; i++)
            {
                pixels[i] = new Color32(tint.r, tint.g, tint.b, (byte)((coverage[i] * tint.a) / 255));
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = _markResourcePath + " (studio intro)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private Sprite BuildSprite(Texture2D keyed, Bounds bounds, float pixelsPerUnit)
        {
            // The pivot is the middle of the ink, not of the canvas: nothing here draws the margins
            // the artwork happened to be exported with, so the mark lands where it is placed.
            var sprite = Sprite.Create(keyed, bounds.ToRect(), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            sprite.name = _markResourcePath + " (studio intro)";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private Part BuildPart(string name, Sprite sprite, Bounds bounds, Vector2 lockupCentre)
        {
            var host = new GameObject(name);
            host.transform.SetParent(_camera.transform, false);
            var renderer = host.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 1000;
            renderer.color = new Color(1f, 1f, 1f, 0f);
            host.hideFlags = HideFlags.HideAndDontSave;
            return new Part(renderer, bounds.Centre - lockupCentre, bounds.Size);
        }

        /// <summary>
        /// Places one piece: centred on the safe area, fitted without stretching, drawn at the given
        /// presence, scale and rise.
        /// </summary>
        /// <remarks>
        /// The fit is computed from the <b>lockup's</b> size for both pieces, so they keep the
        /// proportions and the spacing the artwork drew, and it is one scale factor from both limits,
        /// which is what "no stretching" means. Two factors would be the bug this avoids.
        /// <para>
        /// The artwork's import resolution is divided out here. A sprite is sized in world units as
        /// its pixels over its pixels-per-unit, so scaling its transform by the fit directly would
        /// draw it at the artwork's own pixel size rather than at the size the fit chose — a mark
        /// filling the screen however carefully the limits were set. <c>tools/verify/intro.py</c>
        /// checks the numbers, and the render test in CI checks the pixels that come out.
        /// </para>
        /// </remarks>
        private void Place(Transform drawn, Vector2 sizePixels, Vector2 offsetPixels, float scale,
                           float rise)
        {
            Rect safe = DrawableArea();

            // Pixels to world units. The camera is full screen and orthographic, so its vertical
            // extent is twice the orthographic size over the whole screen, and pixels are square.
            float worldPerPixel = (2f * _camera.orthographicSize) / Mathf.Max(1f, Screen.height);

            // One factor from both limits — the lockup's size, for both pieces — so the mark and the
            // wordmark keep the relationship the artwork drew at every size.
            float fit = Mathf.Min((safe.width * Layout.MaxWidthFraction) / _lockupPixels.x,
                                  (safe.height * Layout.MaxHeightFraction) / _lockupPixels.y);

            // World units per artwork pixel at this point in the animation: the fit, converted to
            // world units, times the reveal's scale. The sprite's transform then carries its own size
            // in pixels times this, times its pixels-per-unit — the conversion the sprite needs because
            // its size is already expressed in units.
            float unitsPerPixel = fit * worldPerPixel * scale;
            float pixelsPerUnit = Mathf.Max(1f, _pixelsPerUnit);

            Vector2 safeCentre = safe.center - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector3 cameraPosition = _camera.transform.position;

            drawn.position = new Vector3(
                cameraPosition.x + (safeCentre.x * worldPerPixel) + (offsetPixels.x * unitsPerPixel),
                cameraPosition.y + (safeCentre.y * worldPerPixel) + (offsetPixels.y * unitsPerPixel)
                    + (rise * safe.height * worldPerPixel),
                cameraPosition.z + 1f);
            drawn.localScale = new Vector3(sizePixels.x * unitsPerPixel * pixelsPerUnit,
                                           sizePixels.y * unitsPerPixel * pixelsPerUnit,
                                           1f);
        }

        /// <summary>
        /// The rectangle the intro may draw in: the safe area, or the whole screen when Unity reports
        /// a degenerate one, which happens on some devices before the first orientation settles.
        /// </summary>
        private static Rect DrawableArea()
        {
            Rect safe = Screen.safeArea;
            if (safe.width < 1f || safe.height < 1f) return new Rect(0f, 0f, Screen.width, Screen.height);
            return safe;
        }

        /// <summary>
        /// The ink in a horizontal band of the artwork: the box that has to be drawn, so the
        /// transparent margins of the file never become part of the layout.
        /// </summary>
        private static Bounds InkBounds(byte[] coverage, int width, int yFrom, int yTo)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = -1;
            int maxY = -1;

            for (int y = yFrom; y < yTo; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (coverage[row + x] == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            return new Bounds(minX, minY, maxX, maxY);
        }

        // -- input and hand-over ----------------------------------------------------------------

        /// <summary>
        /// True on the frame the player asks to skip: a touch, a click, any key, or a face button.
        /// </summary>
        /// <remarks>
        /// Read from the devices directly rather than through <c>GameplayInputRouter</c>. Skipping an
        /// intro is not a gameplay command — the router's vocabulary is jump, attack, dodge and
        /// interact, and none of them means "I have seen this". The devices are only read while the
        /// intro is on screen, and only for a press.
        /// </remarks>
        private static bool SkipRequested()
        {
            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null
                && (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame))
            {
                return true;
            }

            return false;
        }

        private void HandOver()
        {
            ReleaseLockup();

            if (string.IsNullOrEmpty(_nextScene))
            {
                Debug.LogError(
                    "The studio intro has no scene to hand over to, so the app has nowhere to go. " +
                    "Set the next scene on the StudioIntroSequence component.", this);
                return;
            }

            SceneManager.LoadScene(_nextScene);
        }

        /// <summary>Draws nothing, which is how the intro starts and how it ends.</summary>
        private void HideAll()
        {
            Draw(_mark, 0f, 1f, 0f);
            Draw(_wordmark, 0f, 1f, 0f);
        }

        /// <summary>
        /// Frees what this intro generated. The scene unload would take the renderers with them, but
        /// the texture and the sprites were created with <c>HideAndDontSave</c> and would outlive the
        /// scene; the lockup is a one-off, and nothing else should be left holding a copy of it.
        /// </summary>
        private void ReleaseLockup()
        {
            // The hosts carry HideAndDontSave, so the scene unload would not take them along; they
            // are destroyed here, renderer and all, rather than travelling into the next scene
            // disabled and invisible.
            if (_mark.Renderer != null) Destroy(_mark.Renderer.gameObject);
            if (_wordmark.Renderer != null) Destroy(_wordmark.Renderer.gameObject);

            if (_markSprite != null) Destroy(_markSprite);
            if (_wordmarkSprite != null) Destroy(_wordmarkSprite);
            if (_generatedTexture != null) Destroy(_generatedTexture);
            _markSprite = null;
            _wordmarkSprite = null;
            _generatedTexture = null;
            _mark = default;
            _wordmark = default;
        }

        // -- easing -----------------------------------------------------------------------------

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        /// <summary>A curve that starts and ends soft: the one the mark's arrival rides.</summary>
        private static float EaseInOutSine(float t)
        {
            return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
        }

        private static float EaseInOutCubic(float t)
        {
            if (t < 0.5f) return 4f * t * t * t;
            float inverse = 1f - t;
            return 1f - (4f * inverse * inverse * inverse);
        }

        /// <summary>
        /// What was and was not drawn in a band of artwork, in the artwork's own pixels.
        /// </summary>
        private readonly struct Bounds
        {
            private readonly int _minX;
            private readonly int _minY;
            private readonly int _maxX;
            private readonly int _maxY;

            public Bounds(int minX, int minY, int maxX, int maxY)
            {
                _minX = minX;
                _minY = minY;
                _maxX = maxX;
                _maxY = maxY;
            }

            public bool Found => _maxX >= _minX && _maxY >= _minY;

            public Vector2 Size => new Vector2(_maxX - _minX + 1, _maxY - _minY + 1);

            public Vector2 Centre => new Vector2((_minX + _maxX) * 0.5f, (_minY + _maxY) * 0.5f);

            public Rect ToRect() => new Rect(_minX, _minY, _maxX - _minX + 1, _maxY - _minY + 1);

            /// <summary>The box around both, or just this one when the other is empty.</summary>
            public Bounds Union(Bounds other)
            {
                if (!other.Found) return this;
                if (!Found) return other;
                return new Bounds(Mathf.Min(_minX, other._minX), Mathf.Min(_minY, other._minY),
                                  Mathf.Max(_maxX, other._maxX), Mathf.Max(_maxY, other._maxY));
            }
        }
    }
}
