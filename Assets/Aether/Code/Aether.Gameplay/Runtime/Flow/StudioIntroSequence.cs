using System;
using System.Collections;
using Aether.Gameplay.Sound;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// The render test steps the intro's clock so that the frames it captures are tied to the
// animation rather than to the runner's wall clock. Nothing else outside this assembly has any
// business with its internals.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Aether.Tests.PlayMode")]

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
    /// mid-reveal fades from the current presence, not from full), and the whole sequence runs under
    /// about three and a half seconds because an intro is a signature, not a wait. In order: a short black, the mark
    /// arriving, the wordmark completing the lockup a beat later, two restrained light passes across
    /// it, a still hold, and a fade back to black — <see cref="Timing"/> holds the numbers.
    /// </para>
    /// <para>
    /// <b>The light that crosses the lockup is drawn on the logo's own ink.</b> A sheen is the one
    /// flourish a studio ident is allowed and the easiest thing to get wrong: a bright band laid over
    /// the screen is a grey streak across the black, and a glow around the mark is the lens-flare look
    /// this intro exists not to be. So the band is masked by the artwork's own coverage — the mask the
    /// mark and the wordmark are built from, sampled down because it carries only a soft gradient —
    /// and it is invisible everywhere the logo is not. The lockup is taken down a few percent while
    /// the light crosses it and returns to its exact resting colour afterwards, so the still hold is
    /// the same picture it would have been without the pass.
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
        /// <para>
        /// The sequence, in seconds from the first frame of the scene: pure black 0.00-0.30, the mark
        /// arriving 0.30-0.95, the wordmark completing the lockup 0.55-1.15, the primary light pass
        /// 1.15-1.87, the reverse glint 2.37-2.75, and the fade to black beginning at 2.92. The
        /// second glint sits inside the hold and the hand-over comes <see cref="HandOver"/> after the last of
        /// the logo has gone.
        /// </para>
        /// <para>
        /// The light starts before the wordmark has quite finished, deliberately: that overlap is what
        /// makes the pass read as one continuous arrival instead of a separate beat bolted onto it.
        /// </para>
        /// <para>
        /// That budget is a requirement, not a taste — an intro of this kind is roughly three to four seconds —
        /// and <c>tools/verify/intro.py</c> reads these numbers and fails if the phases stop making
        /// that shape, so buying a longer reveal means shortening something else on purpose.
        /// </para>
        /// </remarks>
        public static class Timing
        {
            /// <summary>Pure black before the mark appears. Covers the first frames of the app.</summary>
            public const float BlackHold = 0.30f;

            /// <summary>The mark's arrival, from nothing to full presence: 0.30s to 0.95s.</summary>
            public const float Reveal = 0.65f;

            /// <summary>How much later than the mark the wordmark starts: the hierarchy.</summary>
            public const float WordmarkDelay = 0.25f;

            /// <summary>The wordmark's arrival, completing the lockup at 1.15s.</summary>
            public const float WordmarkReveal = 0.60f;

            /// <summary>The primary light pass starts only after the complete logo has settled.</summary>
            public const float SheenStartsAt = 1.15f;

            /// <summary>A slower, clearly visible first sweep, from 1.15s to 1.97s.</summary>
            public const float SheenDuration = 0.82f;

            /// <summary>Delay from the end of the first sweep until a shorter reverse glint begins.</summary>
            public const float SecondarySheenDelay = 0.42f;

            /// <summary>The second, reverse-direction glint lasts from 2.39s to 2.87s.</summary>
            public const float SecondarySheenDuration = 0.48f;

            /// <summary>Quiet hold after the first sweep, long enough for the reverse glint and a clean exit.</summary>
            public const float Hold = 1.00f;

            /// <summary>The fade from the complete lockup to pure black.</summary>
            public const float Exit = 0.55f;

            /// <summary>Black held after the lockup has gone, so the hand-over is not a hard cut.</summary>
            public const float HandOver = 0.10f;

            /// <summary>Input before this is ignored: the tap that launched the app is still landing.</summary>
            public const float SkipGrace = 0.45f;

            /// <summary>How long the shortened exit lasts when the player skips.</summary>
            public const float SkipExit = 0.35f;
        }

        /// <summary>
        /// The light that crosses the finished lockup: how wide it is, how bright, and how far the
        /// logo is taken down while it passes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It is a soft band of white, masked by the artwork's own ink, travelling on a diagonal that
        /// is nearly level. Every number here is deliberately small: this is the one moment in the
        /// sequence that could turn into a logo sting, and the brief is the opposite — a light an eye
        /// notices on the second viewing rather than the first. The band is never wider than the
        /// lockup it crosses, the highlight never reaches solid white, and the logo never dips far
        /// enough for the dip itself to read as a change.
        /// </para>
        /// <para>
        /// <c>tools/verify/intro.py</c> holds these numbers in bands — a wider band is a wash, a
        /// brighter one is a flare, a deeper dip is a flicker — so the restraint is checked rather
        /// than remembered.
        /// </para>
        /// </remarks>
        public static class Sheen
        {
            /// <summary>Half the width of the band, in the pass's own 0-1 coordinate.</summary>
            public const float BandHalfWidth = 0.30f;

            /// <summary>How much of the pass is vertical: 0 is level, 1 is fully diagonal.</summary>
            public const float Tilt = 0.16f;

            /// <summary>How bright the band is where it is centred, as alpha of white on the ink.</summary>
            public const float HighlightPeak = 0.94f;

            /// <summary>How far the logo is taken down while the light is crossing it.</summary>
            public const float DimWhilePassing = 0.90f;

            /// <summary>Fraction of the pass at each end over which the light comes and goes.</summary>
            public const float EdgeFade = 0.14f;

            /// <summary>Mask texels per artwork pixel. It carries a soft gradient, nothing finer.</summary>
            public const float Resolution = 0.45f;
        }

        /// <summary>
        /// What the player has chosen about the intro.
        /// </summary>
        /// <remarks>
        /// This persisted setting is retained for compatibility with future settings UI. The current
        /// studio ident deliberately does not consult it: a stale saved value must never make the
        /// opening logo or its glint disappear on a normal launch.
        /// </remarks>
        public static class Preference
        {
            private const string Key = "aether.studioIntro.play";

            /// <summary>Legacy persisted preference; current startup intentionally always plays the ident.</summary>
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
        private Texture2D _sheenTexture;
        private Sprite _sheenSprite;
        private SpriteRenderer _sheenRenderer;
        private Color32[] _sheenPixels;
        private byte[] _sheenMask;
        private float[] _sheenAcross;

        /// <summary>Most texels the light's mask may have on its longer side.</summary>
        private const int MaxSheenTexels = 420;

        /// <summary>
        /// The intro's clock: how much time one drawn frame adds, in seconds. Real time in every
        /// build, so the ident takes about 3.57 seconds whatever the machine's frame rate is.
        /// </summary>
        /// <remarks>
        /// The render test replaces this with a fixed step, because the clock an animation is
        /// measured against has to be the clock the animation itself runs on. On the runner the
        /// editor does not tick the scene once per frame the test sees, so a frame captured against
        /// the wall clock can show the same drawn moment twice — a run that failed this way showed
        /// 0.50s and 0.90s of the test's clock as one picture, which cannot be told apart from an
        /// animation that never drew the wordmark at all. It is internal and it is null in a build:
        /// with it null the sequence reads <see cref="Time.unscaledDeltaTime"/>, exactly as before.
        /// </remarks>
        internal static Func<float> Clock;

        /// <summary>The interval this frame adds to the intro's time.</summary>
        private static float NextDelta()
        {
            return Clock != null ? Clock() : Time.unscaledDeltaTime;
        }

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

            // The studio mark is part of every normal launch, not a randomized or persisted one-shot.
            // Only the explicit scene switch used by tools/tests may suppress the ident.
            if (!_playOnAwake)
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
            // The still hold runs from the moment the light has left the lockup to the moment the fade
            // begins, which is why it is not simply the reveal's end plus a pause: between them are the
            // wordmark's arrival and the pass itself.
            float exitStartsAt = Timing.SheenStartsAt + Timing.SheenDuration + Timing.Hold;
            float exitLength = Timing.Exit;

            HideAll();

            while (true)
            {
                elapsed += NextDelta();

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
                    // The exit is a fade and nothing else: the lockup leaves at the size and in the
                    // place the hold gave it, so the last thing seen is the picture that was held.
                    float exit = EaseInOutCubic(Ramp(elapsed, exitStartsAt, exitLength));
                    Draw(_mark, skipped ? markFadeFrom * (1f - exit) : 1f - exit, 1f, 0f, 1f);
                    Draw(_wordmark, skipped ? wordmarkFadeFrom * (1f - exit) : 1f - exit, 1f, 0f, 1f);
                    HideSheen();
                }
                else
                {
                    RevealAt(elapsed, false, out float markPresence, out float markScale, out float markRise);
                    RevealAt(elapsed, true, out float wordPresence, out float wordScale, out float wordRise);
                    float dim = SheenDim(elapsed);
                    Draw(_mark, markPresence, markScale, markRise, dim);
                    Draw(_wordmark, wordPresence, wordScale, wordRise, dim);
                    DrawSheen(elapsed);
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
        /// <para>
        /// The mark starts at <see cref="Timing.BlackHold"/> and takes <see cref="Timing.Reveal"/>; the
        /// wordmark starts <see cref="Timing.WordmarkDelay"/> later and takes
        /// <see cref="Timing.WordmarkReveal"/>, so the symbol establishes the identity and the wordmark
        /// completes it. Both curves are ease-outs: they arrive promptly enough to feel deliberate and
        /// settle softly enough to feel expensive, and neither overshoots — a bounce is the one thing
        /// this sequence must not do.
        /// </para>
        /// <para>
        /// Presence and movement ride separate curves. The mark's opacity follows a sine ease-out, the
        /// gentlest of them; the wordmark's arrives on a slightly more decisive cubic. The scale and the
        /// small rise behind both settle on a curve that is soft at both ends, and the movement is small
        /// enough to be felt rather than watched.
        /// </para>
        /// </remarks>
        private static void RevealAt(float elapsed, bool wordmark,
                                     out float presence, out float scale, out float rise)
        {
            float startsAt = Timing.BlackHold + (wordmark ? Timing.WordmarkDelay : 0f);
            float duration = wordmark ? Timing.WordmarkReveal : Timing.Reveal;
            float linear = Ramp(elapsed, startsAt, duration);
            float settle = EaseInOutSine(linear);
            presence = wordmark ? EaseOutCubic(linear) : EaseOutSine(linear);
            scale = Mathf.Lerp(Layout.RevealScale, 1f, settle);
            rise = -(wordmark ? Layout.WordmarkRise : Layout.RevealRise) * (1f - settle);
        }

        /// <summary>
/// Waits real seconds, unaffected by time scale: the intro is not gameplay. It reads the same
/// clock the reveal does, so a test stepping that clock steps this with it.
/// </summary>
        private static IEnumerator WaitUnscaled(float seconds)
        {
            float waited = 0f;
            while (waited < seconds)
            {
                waited += NextDelta();
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

            public Part(SpriteRenderer renderer, Vector2 offsetPixels)
            {
                Renderer = renderer;
                OffsetPixels = offsetPixels;
            }

            public bool Present => Renderer != null;
        }

        /// <summary>
        /// Draws one piece for one frame: presence is its opacity, scale its size, rise how far it has
        /// drifted up from its resting place in safe-area heights, and dim how far the light passing
        /// over the lockup has taken it down. The dip belongs to the light, not to the piece: both are
        /// given the same one, so the lockup dims as one picture.
        /// </summary>
        private void Draw(Part part, float presence, float scale, float rise, float dim)
        {
            if (part.Renderer == null) return;
            part.Renderer.color = new Color(dim, dim, dim, Mathf.Clamp01(presence));
            Place(part.Renderer.transform, part.OffsetPixels, scale, rise);
        }

        // -- the light pass ------------------------------------------------------------------------

        /// <summary>
        /// Moves the light across the lockup for this frame, or takes it away when the pass is over.
        /// </summary>
        /// <remarks>
        /// The crest starts off one side of the lockup and ends off the other, so the light arrives
        /// from outside the logo rather than switching on over it, and the mask is rewritten only
        /// while the pass is on screen. What is drawn is the artwork's own ink at its resting place:
        /// the light's sprite is the lockup's box, placed by the same fit as the pieces, which is why
        /// it lines up with them on every shape of screen without a second set of numbers.
        /// </remarks>
        private void DrawSheen(float elapsed)
        {
            if (_sheenRenderer == null) return;

            if (!TryGetSheenPass(elapsed, out float progress, out bool reverse))
            {
                HideSheen();
                return;
            }

            float crest = reverse
                ? Mathf.Lerp(1f + Sheen.BandHalfWidth, -Sheen.BandHalfWidth, EaseInOutSine(progress))
                : Mathf.Lerp(-Sheen.BandHalfWidth, 1f + Sheen.BandHalfWidth, EaseInOutSine(progress));
            float presence = EaseInOutSine(Mathf.Clamp01(progress / Sheen.EdgeFade)) *
                             EaseInOutSine(Mathf.Clamp01((1f - progress) / Sheen.EdgeFade));

            // Keep the phase calculation explicit: both sweeps use the same logo-derived mask, but
            // the second travels back across it. No random timing or one-frame trigger can suppress it.
            for (int i = 0; i < _sheenPixels.Length; i++)
            {
                float strength = _sheenMask[i] / 255f * SheenBand(_sheenAcross[i] - crest);
                _sheenPixels[i].a = (byte)(Mathf.Clamp01(strength * presence * Sheen.HighlightPeak) * 255f);
            }

            _sheenTexture.SetPixels32(_sheenPixels);
            _sheenTexture.Apply(false, false);
            _sheenRenderer.color = new Color(1f, 1f, 1f, 1f);
            Place(_sheenRenderer.transform, Vector2.zero, 1f, 0f);
        }

        private static bool TryGetSheenPass(float elapsed, out float progress, out bool reverse)
        {
            float first = (elapsed - Timing.SheenStartsAt) / Timing.SheenDuration;
            if (first > 0f && first < 1f)
            {
                progress = first;
                reverse = false;
                return true;
            }

            float secondStart = Timing.SheenStartsAt + Timing.SheenDuration + Timing.SecondarySheenDelay;
            float second = (elapsed - secondStart) / Timing.SecondarySheenDuration;
            if (second > 0f && second < 1f)
            {
                progress = second;
                reverse = true;
                return true;
            }

            progress = 0f;
            reverse = false;
            return false;
        }

        /// <summary>
        /// Takes the light off the screen, without rebuilding the mask: the still hold and the fade
        /// must not carry a hidden layer that could show at the edge of a frame.
        /// </summary>
        private void HideSheen()
        {
            if (_sheenRenderer != null) _sheenRenderer.color = new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>The band's own shape: full where it is centred, nothing at its edges.</summary>
        private static float SheenBand(float distance)
        {
            float away = Mathf.Abs(distance) / Sheen.BandHalfWidth;
            if (away >= 1f) return 0f;
            return 0.5f * (1f + Mathf.Cos(Mathf.PI * away));
        }

        /// <summary>
        /// How far the lockup is taken down at this moment, if the light is crossing it.
        /// </summary>
        /// <remarks>
        /// The dip is what lets the pass read as light rather than as a change of colour: it is the
        /// only way a highlight can be brighter than a logo that is already nearly white. It rises and
        /// falls smoothly across the pass and is exactly 1.0 before it and after it, so the still hold
        /// and every frame either side of the pass are the picture the rest of the sequence draws.
        /// </remarks>
        private static float SheenDim(float elapsed)
        {
            if (!TryGetSheenPass(elapsed, out float progress, out _)) return 1f;
            float bell = progress < 0.5f ? progress * 2f : (1f - progress) * 2f;
            return Mathf.Lerp(1f, Sheen.DimWhilePassing, EaseInOutSine(bell));
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

            BuildSheen(coverage, width, lockup);
        }

        /// <summary>
        /// Builds the mask the light travels across: the lockup's own ink, at the resolution a soft
        /// gradient needs and no more.
        /// </summary>
        /// <remarks>
        /// This is what keeps the pass on the logo. The mask is the same coverage the mark and the
        /// wordmark are built from, so the light is bright where there is ink and completely absent
        /// everywhere else — no band of grey crossing the black around the lockup, which is what an
        /// unmasked highlight would be. It is sampled down because a gradient has no fine detail to
        /// lose, and at a few hundred texels it costs a fraction of a megabyte and nothing measurable
        /// per frame.
        /// </remarks>
        private void BuildSheen(byte[] coverage, int width, Bounds lockup)
        {
            Vector2 size = lockup.Size;
            float scale = Mathf.Max(0.02f, Mathf.Min(Sheen.Resolution, MaxSheenTexels / Mathf.Max(size.x, size.y)));
            int maskWidth = Mathf.Max(8, Mathf.RoundToInt(size.x * scale));
            int maskHeight = Mathf.Max(8, Mathf.RoundToInt(size.y * scale));
            int sourceWidth = Mathf.Max(1, Mathf.RoundToInt(size.x));
            int sourceHeight = Mathf.Max(1, Mathf.RoundToInt(size.y));

            _sheenMask = new byte[maskWidth * maskHeight];
            _sheenAcross = new float[maskWidth * maskHeight];
            _sheenPixels = new Color32[maskWidth * maskHeight];

            for (int my = 0; my < maskHeight; my++)
            {
                int yFrom = Mathf.Clamp(my * sourceHeight / maskHeight, 0, sourceHeight - 1);
                int yTo = Mathf.Clamp((my + 1) * sourceHeight / maskHeight, yFrom + 1, sourceHeight);
                for (int mx = 0; mx < maskWidth; mx++)
                {
                    int xFrom = Mathf.Clamp(mx * sourceWidth / maskWidth, 0, sourceWidth - 1);
                    int xTo = Mathf.Clamp((mx + 1) * sourceWidth / maskWidth, xFrom + 1, sourceWidth);

                    int sum = 0;
                    int count = 0;
                    for (int y = yFrom; y < yTo; y++)
                    {
                        int row = (lockup.MinY + y) * width;
                        for (int x = xFrom; x < xTo; x++)
                        {
                            sum += coverage[row + lockup.MinX + x];
                            count++;
                        }
                    }

                    int index = (my * maskWidth) + mx;
                    _sheenMask[index] = (byte)(count == 0 ? 0 : sum / count);

                    // Where the texel sits along the pass, and how far up the lockup it is: the band
                    // crosses nearly level, leaning so its leading edge leads at the top.
                    float across = mx / (float)Mathf.Max(1, maskWidth - 1);
                    float up = my / (float)Mathf.Max(1, maskHeight - 1);
                    _sheenAcross[index] = ((1f - Sheen.Tilt) * across) + (Sheen.Tilt * (1f - up));
                    _sheenPixels[index] = new Color32(255, 255, 255, 0);
                }
            }

            _sheenTexture = new Texture2D(maskWidth, maskHeight, TextureFormat.RGBA32, false)
            {
                name = _markResourcePath + " (studio intro sheen)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _sheenTexture.SetPixels32(_sheenPixels);
            _sheenTexture.Apply(false, false);

            // The sprite is the lockup's own box. Its pixels-per-unit is scaled with the mask, so the
            // same fit that places the mark and the wordmark places the light over them, at the size
            // the lockup is drawn, and it keeps doing so on every shape of screen.
            float texelsPerPixel = maskWidth / size.x;
            _sheenSprite = Sprite.Create(_sheenTexture, new Rect(0f, 0f, maskWidth, maskHeight),
                                         new Vector2(0.5f, 0.5f), _pixelsPerUnit * texelsPerPixel);
            _sheenSprite.name = _markResourcePath + " (studio intro sheen)";
            _sheenSprite.hideFlags = HideFlags.HideAndDontSave;

            var host = new GameObject("StudioSheen");
            host.transform.SetParent(_camera.transform, false);
            _sheenRenderer = host.AddComponent<SpriteRenderer>();
            _sheenRenderer.sprite = _sheenSprite;
            _sheenRenderer.sortingOrder = 1001;   // the mark and the wordmark are at 1000
            _sheenRenderer.color = new Color(1f, 1f, 1f, 0f);
            host.hideFlags = HideFlags.HideAndDontSave;
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
            return new Part(renderer, bounds.Centre - lockupCentre);
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
        private void Place(Transform drawn, Vector2 offsetPixels, float scale, float rise)
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
            // world units, times the reveal's scale.
            float unitsPerPixel = fit * worldPerPixel * scale;
            float pixelsPerUnit = Mathf.Max(1f, _pixelsPerUnit);

            // And what the transform has to carry to draw at that size. A sprite is already as many
            // world units across as its pixels over its pixels-per-unit, so the scale is this number
            // times the pixels-per-unit and nothing else. Multiplying by the piece's pixel size as
            // well would apply the artwork's own resolution twice: the mark, 681 pixels across at 100
            // pixels per unit, was drawn 681 times too large and filled the screen. The arithmetic
            // gates cannot see that - they check the numbers the fit produces, and the fit was right -
            // which is what the render test in CI is for.
            float spriteScale = unitsPerPixel * pixelsPerUnit;

            Vector2 safeCentre = safe.center - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector3 cameraPosition = _camera.transform.position;

            drawn.position = new Vector3(
                cameraPosition.x + (safeCentre.x * worldPerPixel) + (offsetPixels.x * unitsPerPixel),
                cameraPosition.y + (safeCentre.y * worldPerPixel) + (offsetPixels.y * unitsPerPixel)
                    + (rise * safe.height * worldPerPixel),
                cameraPosition.z + 1f);
            drawn.localScale = new Vector3(spriteScale, spriteScale, 1f);
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
            Draw(_mark, 0f, 1f, 0f, 1f);
            Draw(_wordmark, 0f, 1f, 0f, 1f);
            HideSheen();
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
            if (_sheenRenderer != null) Destroy(_sheenRenderer.gameObject);

            if (_markSprite != null) Destroy(_markSprite);
            if (_wordmarkSprite != null) Destroy(_wordmarkSprite);
            if (_generatedTexture != null) Destroy(_generatedTexture);
            if (_sheenSprite != null) Destroy(_sheenSprite);
            if (_sheenTexture != null) Destroy(_sheenTexture);
            _markSprite = null;
            _wordmarkSprite = null;
            _generatedTexture = null;
            _sheenSprite = null;
            _sheenTexture = null;
            _sheenRenderer = null;
            _sheenPixels = null;
            _sheenMask = null;
            _sheenAcross = null;
            _mark = default;
            _wordmark = default;
        }

        // -- easing -----------------------------------------------------------------------------

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        /// <summary>The gentlest ease-out: the one the mark's arrival rides.</summary>
        private static float EaseOutSine(float t)
        {
            return Mathf.Sin(t * Mathf.PI * 0.5f);
        }

        /// <summary>A curve that starts and ends soft: what the settle, the dip and the light ride.</summary>
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

            public int MinX => _minX;

            public int MinY => _minY;

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
