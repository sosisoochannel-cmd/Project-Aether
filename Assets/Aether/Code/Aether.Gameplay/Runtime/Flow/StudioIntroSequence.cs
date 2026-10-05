using System.Collections;
using Aether.Gameplay.Sound;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Aether.Gameplay.Flow
{
    /// <summary>
    /// The studio's intro: black, the Varellon mark, a short hold, and a soft exit into the next
    /// scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It plays in its own scene, in front of everything else.</b> The scene is first in the build
    /// order, so it is the first thing the app shows, and it hands over to <see cref="_nextScene"/>
    /// when it ends. Repointing that one field at a main menu is the whole change the next stage
    /// needs; nothing here knows what the next scene contains.
    /// </para>
    /// <para>
    /// <b>Sprites parented to the camera, not uGUI.</b> The same choice the on-screen controls make,
    /// for the same reasons: a Canvas, an EventSystem and an input-UI module are three things that
    /// need wiring and can be got wrong in a build nobody can open in an Editor, and the intro is one
    /// image that fades. Layout is arithmetic in fractions of the safe area, which is why it is
    /// checkable without a device — <c>tools/verify/intro.py</c> proves the mark stays inside the
    /// safe area, keeps its aspect ratio (no stretching) and stays centred on every shape of screen
    /// the game is expected to meet, using the numbers in <see cref="Layout"/> in this file.
    /// </para>
    /// <para>
    /// <b>The mark is drawn from the mark's own alpha channel.</b> The supplied artwork is black on
    /// transparency, and this intro starts from a black screen, so the pixels are recoloured
    /// (<see cref="KnockOut"/>) and the artwork's coverage — the alpha channel, which is the mark
    /// itself — is what is shown. The colour is a serialized field, so a light mark on a dark
    /// backdrop, or the reverse, is a data change rather than a code change.
    /// </para>
    /// <para>
    /// <b>Everything is eased and nothing snaps.</b> Presence, scale and a small vertical rise are
    /// curves over elapsed time, the exit starts from wherever the mark currently is (skipping
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
        /// Where the mark sits, in fractions of the <b>safe area</b>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The mark is fitted — one scale factor from both limits, never one per axis — so it can
        /// never be stretched, and it is centred on the safe area rather than the screen, so a notch
        /// on one side does not push the mark off-centre.
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
            /// <summary>Largest fraction of the safe area's width the mark may occupy.</summary>
            public const float MaxWidthFraction = 0.62f;

            /// <summary>Largest fraction of the safe area's height the mark may occupy.</summary>
            public const float MaxHeightFraction = 0.34f;

            /// <summary>Scale the mark starts at, as a fraction of its final size.</summary>
            public const float RevealScale = 0.94f;

            /// <summary>How far below its resting place the mark starts, in safe-area heights.</summary>
            public const float RevealRise = 0.022f;

            /// <summary>Scale the mark grows to as it leaves.</summary>
            public const float ExitScale = 1.025f;

            /// <summary>How far the mark drifts upwards as it leaves, in safe-area heights.</summary>
            public const float ExitRise = 0.012f;
        }

        /// <summary>
        /// How long each part of the intro lasts, in seconds.
        /// </summary>
        /// <remarks>
        /// Black hold, reveal, hold, exit: 2.85 seconds including the beat of black before the next
        /// scene loads. That budget is a requirement, not a taste — an intro of this kind is two to
        /// three seconds — and <c>tools/verify/intro.py</c> fails the build if the parts stop adding
        /// up to it, so buying a longer reveal means shortening something else on purpose.
        /// </remarks>
        public static class Timing
        {
            /// <summary>Black before the mark appears. Covers the first frames of the app.</summary>
            public const float BlackHold = 0.25f;

            /// <summary>Fade-in and settle of the mark.</summary>
            public const float Reveal = 1.00f;

            /// <summary>The mark at full presence. The pause that makes it read as a signature.</summary>
            public const float Hold = 0.90f;

            /// <summary>Fade-out of the mark.</summary>
            public const float Exit = 0.55f;

            /// <summary>Black held after the mark has gone, so the hand-over is not a hard cut.</summary>
            public const float HandOver = 0.15f;

            /// <summary>Input before this is ignored: the tap that launched the app is still landing.</summary>
            public const float SkipGrace = 0.40f;

            /// <summary>How long the shortened exit lasts when the player skips.</summary>
            public const float SkipExit = 0.30f;
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

        [Tooltip("Resource path of the studio mark, without extension.")]
        [SerializeField]
        private string _markResourcePath = "Brand/VarellonLogo";

        [Tooltip("Scene to load when the intro ends. The main menu's future home; 'Boot' stands in for it.")]
        [SerializeField]
        private string _nextScene = "Boot";

        [Tooltip("Colour the mark is drawn in. Its shape comes from the artwork's alpha channel.")]
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
        private SpriteRenderer _mark;
        private Sprite _generatedSprite;
        private Texture2D _generatedTexture;
        private Vector2 _markPixels;
        private bool _skipRequested;

        /// <summary>
        /// Ends the intro early, exactly as a tap would. For a future "skip" button of the intro's
        /// own, or a test that does not want to wait three seconds.
        /// </summary>
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

            BuildMark();
        }

        private void Start()
        {
            if (_mark == null || !_playOnAwake || !Preference.Enabled)
            {
                // Nothing to show, or nothing the player wants to see: the app still has to reach the
                // next scene, and it must not sit on a black screen to do it.
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
            float skipPresence = 1f;
            float exitStartsAt = Timing.BlackHold + Timing.Reveal + Timing.Hold;
            float exitLength = Timing.Exit;

            PlaceMark(0f, 1f, 0f);

            while (true)
            {
                elapsed += Time.unscaledDeltaTime;

                if (!skipped && (_skipRequested || (elapsed >= Timing.SkipGrace && SkipRequested())))
                {
                    // Skipping mid-reveal must not jump: the exit fades from wherever the mark is.
                    skipPresence = EaseOutCubic(Ramp(elapsed, Timing.BlackHold, Timing.Reveal));
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
                    PlaceMark(skipPresence * (1f - exit),
                              Mathf.Lerp(1f, Layout.ExitScale, exit),
                              Layout.ExitRise * exit);
                }
                else
                {
                    float reveal = EaseOutCubic(Ramp(elapsed, Timing.BlackHold, Timing.Reveal));
                    PlaceMark(reveal,
                              Mathf.Lerp(Layout.RevealScale, 1f, reveal),
                              -Layout.RevealRise * (1f - reveal));
                }

                if (elapsed >= exitStartsAt + exitLength) break;
                yield return null;
            }

            // The mark is gone and the screen is black. A beat of black before the next scene keeps
            // the hand-over from reading as a cut.
            PlaceMark(0f, 1f, 0f);
            yield return WaitUnscaled(Timing.HandOver);
            HandOver();
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

        // -- the mark ---------------------------------------------------------------------------

        private void BuildMark()
        {
            Sprite source = Resources.Load<Sprite>(_markResourcePath);
            if (source == null)
            {
                Debug.LogError(
                    $"The studio mark '{_markResourcePath}' was not found under Assets/Aether/Resources. " +
                    "The intro has nothing to show and hands over immediately.", this);
                return;
            }

            _markPixels = new Vector2(Mathf.Round(source.textureRect.width),
                                     Mathf.Round(source.textureRect.height));
            if (_markPixels.x < 1f || _markPixels.y < 1f)
            {
                Debug.LogError($"The studio mark '{_markResourcePath}' has no pixels to draw.", this);
                return;
            }

            var host = new GameObject("StudioMark");
            host.transform.SetParent(_camera.transform, false);
            _mark = host.AddComponent<SpriteRenderer>();
            _mark.sprite = KnockOut(source, _markColour);
            _mark.sortingOrder = 1000;
            _mark.color = new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>
        /// The artwork's coverage, drawn in one colour.
        /// </summary>
        /// <remarks>
        /// The mark is the shape in the alpha channel; the pixels themselves are the ink colour the
        /// artwork happens to be exported in. Recolouring from alpha is what lets a black-on-clear
        /// logo appear on a black screen without touching the file, and it is why the brand texture
        /// is imported readable. A texture that is not readable is used as it came, with a warning:
        /// that is correct for a light mark and invisible for a dark one, which is worth saying out
        /// loud rather than rendering nothing.
        /// </remarks>
        private Sprite KnockOut(Sprite source, Color colour)
        {
            Texture2D texture = source.texture;
            if (!texture.isReadable)
            {
                Debug.LogWarning(
                    $"The studio mark '{texture.name}' is not readable, so it cannot be recoloured for " +
                    "the black backdrop. Import it with Read/Write enabled.", this);
                return source;
            }

            int width = (int)_markPixels.x;
            int height = (int)_markPixels.y;
            var pixels = texture.GetPixels32(Mathf.RoundToInt(source.textureRect.x),
                                            Mathf.RoundToInt(source.textureRect.y), width, height);

            var tint = (Color32)colour;
            for (int i = 0; i < pixels.Length; i++)
            {
                // Alpha carries the shape; the ink colour is replaced outright.
                pixels[i] = new Color32(tint.r, tint.g, tint.b, (byte)((pixels[i].a * tint.a) / 255));
            }

            _generatedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = source.name + " (studio intro)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _generatedTexture.SetPixels32(pixels);
            // No mipmaps, and the CPU copy is not needed again: the intro runs once.
            _generatedTexture.Apply(false, true);

            _generatedSprite = Sprite.Create(_generatedTexture, new Rect(0f, 0f, width, height),
                                            new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
            _generatedSprite.name = source.name + " (studio intro)";
            _generatedSprite.hideFlags = HideFlags.HideAndDontSave;
            return _generatedSprite;
        }

        /// <summary>
        /// Places the mark for this frame: centred on the safe area, fitted without stretching, and
        /// drawn at the given presence, scale and rise.
        /// </summary>
        private void PlaceMark(float presence, float scale, float rise)
        {
            if (_mark == null || _camera == null) return;

            Rect safe = DrawableArea();

            // Pixels to world units. The camera is full screen and orthographic, so its vertical
            // extent is twice the orthographic size over the whole screen, and pixels are square.
            float worldPerPixel = (2f * _camera.orthographicSize) / Mathf.Max(1f, Screen.height);

            // One scale factor from both limits: the mark keeps its own aspect ratio exactly, which
            // is what "no stretching" means. Two factors would be the bug this avoids.
            float fit = Mathf.Min((safe.width * Layout.MaxWidthFraction) / _markPixels.x,
                                  (safe.height * Layout.MaxHeightFraction) / _markPixels.y);

            Vector2 offset = safe.center - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector3 cameraPosition = _camera.transform.position;
            float riseWorld = rise * safe.height * worldPerPixel;

            _mark.transform.position = new Vector3(
                cameraPosition.x + (offset.x * worldPerPixel),
                cameraPosition.y + (offset.y * worldPerPixel) + riseWorld,
                cameraPosition.z + 1f);
            _mark.transform.localScale = new Vector3(_markPixels.x * fit * scale * worldPerPixel,
                                                     _markPixels.y * fit * scale * worldPerPixel,
                                                     1f);
            _mark.color = new Color(1f, 1f, 1f, Mathf.Clamp01(presence));
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
            ReleaseMark();

            if (string.IsNullOrEmpty(_nextScene))
            {
                Debug.LogError(
                    "The studio intro has no scene to hand over to, so the app has nowhere to go. " +
                    "Set the next scene on the StudioIntroSequence component.", this);
                return;
            }

            SceneManager.LoadScene(_nextScene);
        }

        /// <summary>
        /// Frees what this intro generated. The scene unload would take the renderer with it, but the
        /// texture and sprite were created with <c>HideAndDontSave</c> and would outlive the scene;
        /// the mark is a one-off, and nothing else should be left holding a copy of it.
        /// </summary>
        private void ReleaseMark()
        {
            if (_mark != null)
            {
                _mark.sprite = null;
                _mark.enabled = false;
            }

            // Only what was generated is destroyed: on the not-readable path the sprite is the
            // imported asset itself, and destroying that would remove it from the project.
            if (_generatedSprite != null) Destroy(_generatedSprite);
            if (_generatedTexture != null) Destroy(_generatedTexture);
            _generatedSprite = null;
            _generatedTexture = null;
            _mark = null;
        }

        // -- easing -----------------------------------------------------------------------------

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        private static float EaseInOutCubic(float t)
        {
            if (t < 0.5f) return 4f * t * t * t;
            float inverse = 1f - t;
            return 1f - (4f * inverse * inverse * inverse);
        }
    }
}
