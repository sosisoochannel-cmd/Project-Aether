using Aether.Core.Settings;
using Aether.Gameplay.Settings;
using Aether.Gameplay.Support;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aether.Gameplay.Controls
{
    /// <summary>
    /// The on-screen directional and action glyphs. Each button has its own touch ID so movement can
    /// be held while another finger presses jump, attack or dodge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why not uGUI.</b> These controls are four circles and one thumb. Building them from
    /// sprites parented to the camera avoids a Canvas, an EventSystem and an input-UI module that
    /// would each need wiring and would each be a thing to get wrong on device. Touch is read
    /// directly from <see cref="Touchscreen"/>, hit-tested against zones defined in screen-relative
    /// units, so the layout is identical on every resolution and aspect ratio.
    /// </para>
    /// <para>
    /// <b>There is no virtual joystick.</b> Two separate left/right buttons provide predictable
    /// movement. Jump, attack and dodge each have a distinct low-opacity glyph, with touch targets
    /// that share the exact same screen-relative geometry.
    /// </para>
    /// <para>
    /// Layout is in fractions of the screen, so it is tuned by editing numbers, not by dragging in a
    /// scene. The numbers below are a starting point chosen for a 16:9 landscape phone and are
    /// expected to be tuned on device; nothing else depends on them.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TouchControlsView : MonoBehaviour
    {
        /// <summary>
        /// Where everything sits, in fractions of the <b>safe area</b>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Fractions rather than pixels, so the layout is identical on every resolution, and of the
        /// safe area rather than the screen, so a notch or a gesture bar cannot put a control under
        /// the player's thumb-if-they-had-one. On a device with no cutout the safe area is the whole
        /// screen and these numbers mean exactly what they say.
        /// </para>
        /// <para>
        /// Radii are fractions of screen height, which is what a thumb's reach scales with; the
        /// centres are fractions of the safe rect. <c>tools/verify/touchlayout.py</c> reads these
        /// numbers and proves no two controls overlap and none leaves the safe area, across the
        /// aspect ratios phones and tablets actually ship with. Editing a number here is therefore
        /// checked, not just hoped for.
        /// </para>
        /// </remarks>
        public static class Layout
        {
            public static readonly Vector2 LeftCentre = new Vector2(0.10f, 0.18f);
            public static readonly Vector2 RightCentre = new Vector2(0.27f, 0.18f);
            public const float DirectionRadius = 0.075f;

            public static readonly Vector2 JumpCentre = new Vector2(0.63f, 0.15f);
            public const float JumpRadius = 0.080f;

            public static readonly Vector2 AttackCentre = new Vector2(0.91f, 0.19f);
            public const float AttackRadius = 0.082f;

            public static readonly Vector2 DodgeCentre = new Vector2(0.75f, 0.43f);
            public const float DodgeRadius = 0.070f;

            // -- what the player owns --------------------------------------------------------------
            // The four values above are the tuned layout and do not change. Everything below is the
            // player's preference applied to it, written here by ApplySettings() and read everywhere
            // the overlay is placed or hit-tested. Keeping the preference out of the constants is
            // what lets tools/verify/touchlayout.py prove the layout is sound: it reads the numbers
            // above and checks them at the extremes of the values below, rather than trusting that
            // a slider cannot move a button onto another one.

            /// <summary>Multiplier on every control's radius. 1 is the tuned layout.</summary>
            public static float RadiusScale = 1f;

            /// <summary>Multiplier on every control's opacity. 1 is the tuned layout.</summary>
            public static float OpacityScale = 1f;

            /// <summary>Movement under the right thumb, actions under the left.</summary>
            public static bool Mirrored;

            /// <summary>A control's centre after mirroring, in fractions of the safe area.</summary>
            public static Vector2 Centre(Vector2 tuned)
            {
                return Mirrored ? new Vector2(1f - tuned.x, tuned.y) : tuned;
            }

            /// <summary>A control's radius after the player's size preference.</summary>
            public static float Radius(float tuned)
            {
                return tuned * RadiusScale;
            }

        }

        private Camera _camera;
        private TouchInputSource _source;
        private SpriteRenderer _leftRenderer;
        private SpriteRenderer _rightRenderer;
        private SpriteRenderer _jumpRenderer;
        private SpriteRenderer _attackRenderer;
        private SpriteRenderer _dodgeRenderer;

        private int _leftTouch = -1;
        private int _rightTouch = -1;
        private int _jumpTouch = -1;
        private int _attackTouch = -1;
        private int _dodgeTouch = -1;
        private Vector2 _moveValue;

        private int _cachedWidth;
        private int _cachedHeight;
        private Rect _cachedSafeArea;
        private bool _visible;
        private bool _gameplayVisible = true;

        /// <summary>True when this overlay is showing, which only happens when touch exists.</summary>
        public bool IsVisible => _visible && _gameplayVisible;

        /// <summary>
        /// Creates the overlay and returns it, or null when the device has no touchscreen. Nothing is
        /// created on a machine without touch: an invisible overlay reading a null device would be
        /// per-frame cost for no benefit.
        /// </summary>
        public static TouchControlsView Create(Camera targetCamera, TouchInputSource source, Transform parent)
        {
            if (targetCamera == null || source == null) return null;
            if (Touchscreen.current == null) return null;

            var host = new GameObject("TouchControls");
            host.transform.SetParent(parent, false);
            var view = host.AddComponent<TouchControlsView>();

            // Before Initialize, so nothing is ever placed or hit-tested with the previous player's
            // preferences still in the statics.
            ApplySettings();
            view.Initialize(targetCamera, source);
            return view;
        }

        private void Initialize(Camera targetCamera, TouchInputSource source)
        {
            _camera = targetCamera;
            _source = source;

            _leftRenderer = CreateCircle("MoveLeftButton", PlaceholderVisuals.LeftArrow,
                new Color(1f, 1f, 1f, 0.72f * Layout.OpacityScale), 900);
            _rightRenderer = CreateCircle("MoveRightButton", PlaceholderVisuals.RightArrow,
                new Color(1f, 1f, 1f, 0.72f * Layout.OpacityScale), 900);
            _jumpRenderer = CreateCircle("JumpButton", PlaceholderVisuals.UpArrow,
                new Color(1f, 1f, 1f, 0.82f * Layout.OpacityScale), 900);
            _attackRenderer = CreateCircle("AttackButton", PlaceholderVisuals.Slash,
                new Color(1f, 1f, 1f, 0.82f * Layout.OpacityScale), 901);
            _dodgeRenderer = CreateCircle("DodgeButton", PlaceholderVisuals.Diamond,
                new Color(0.72f, 0.94f, 1f, 0.78f * Layout.OpacityScale), 899);

            _visible = true;
            RefreshLayout(force: true);
        }

        private void OnEnable()
        {
            AetherSettings.Ensure().Changed += OnSettingsChanged;
        }

        private void OnDestroy()
        {
            AetherSettings.Current.Changed -= OnSettingsChanged;
            if (_source == null) return;
            _source.Reset();
        }

        /// <summary>
        /// Takes the player's control preferences and puts them where the overlay reads them.
        /// </summary>
        /// <remarks>
        /// Static because the layout is static: there is one overlay in the game, its numbers are
        /// read by untimed hit-testing every frame, and routing them through an instance would put a
        /// field lookup in the touch path for no benefit.
        /// </remarks>
        public static void ApplySettings()
        {
            ControlSettings wanted = AetherSettings.Ensure().Values.Controls;

            Layout.RadiusScale = wanted.ButtonSize;
            Layout.OpacityScale = wanted.ButtonOpacity;
            Layout.Mirrored = wanted.LeftHanded;
        }

        private void OnSettingsChanged(string id)
        {
            ApplySettings();
            Repaint();
            RefreshLayout(true);
        }

        /// <summary>Re-applies the opacity preference to circles that were already created.</summary>
        private void Repaint()
        {
            float opacity = Layout.OpacityScale;
            if (_leftRenderer != null) SetAlpha(_leftRenderer, 0.72f * opacity);
            if (_rightRenderer != null) SetAlpha(_rightRenderer, 0.72f * opacity);
            if (_jumpRenderer != null) SetAlpha(_jumpRenderer, 0.82f * opacity);
            if (_attackRenderer != null) SetAlpha(_attackRenderer, 0.82f * opacity);
            if (_dodgeRenderer != null) SetAlpha(_dodgeRenderer, 0.78f * opacity);
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            Color colour = renderer.color;
            colour.a = alpha;
            renderer.color = colour;
        }

        private void Update()
        {
            // The pause overlay owns the screen while open. Do not claim new touches beneath it:
            // otherwise a finger held through Resume could become a gameplay action.
            if (!_gameplayVisible) return;

            ReadTouches();
            RefreshLayout(force: false);
            _source.SetMove(_moveValue);
        }

        private void OnDisable()
        {
            ReleaseEverything();
        }

        /// <summary>
        /// A phone call, a notification the player taps, or the app going to the background ends the
        /// touch stream without a release event. Without this the stick would stay tilted and a
        /// pending attack would fire the moment the player came back.
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ReleaseEverything();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) ReleaseEverything();
        }

        private void ReleaseEverything()
        {
            _source?.Reset();
            _leftTouch = -1;
            _rightTouch = -1;
            _moveValue = Vector2.zero;
            _jumpTouch = -1;
            _attackTouch = -1;
            _dodgeTouch = -1;
        }

        // -- input ---------------------------------------------------------------------------

        private void ReadTouches()
        {
            Touchscreen screen = Touchscreen.current;
            if (screen == null)
            {
                if (_visible) SetVisible(false);
                return;
            }
            if (!_visible) SetVisible(true);

            bool sawLeft = false;
            bool sawRight = false;
            bool sawJump = false;
            bool sawAttack = false;
            bool sawDodge = false;

            var touches = screen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (!touch.press.isPressed) continue;

                int id = touch.touchId.ReadValue();
                Vector2 position = touch.position.ReadValue();
                bool began = touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Began;

                if (id == _leftTouch) sawLeft = true;
                else if (id == _rightTouch) sawRight = true;
                else if (id == _jumpTouch) sawJump = true;
                else if (id == _attackTouch) sawAttack = true;
                else if (id == _dodgeTouch) sawDodge = true;
                else if (began)
                {
                    // Each new finger is claimed by one control and stays there until release.
                    if (_leftTouch < 0 && CircleHit(position, Layout.Centre(Layout.LeftCentre), Layout.Radius(Layout.DirectionRadius)))
                    {
                        _leftTouch = id;
                        sawLeft = true;
                    }
                    else if (_rightTouch < 0 && CircleHit(position, Layout.Centre(Layout.RightCentre), Layout.Radius(Layout.DirectionRadius)))
                    {
                        _rightTouch = id;
                        sawRight = true;
                    }
                    else if (_jumpTouch < 0 && CircleHit(position, Layout.Centre(Layout.JumpCentre), Layout.Radius(Layout.JumpRadius)))
                    {
                        _jumpTouch = id;
                        _source.PressJump();
                        sawJump = true;
                    }
                    else if (_attackTouch < 0 && CircleHit(position, Layout.Centre(Layout.AttackCentre), Layout.Radius(Layout.AttackRadius)))
                    {
                        _attackTouch = id;
                        _source.PressAttack();
                        sawAttack = true;
                    }
                    else if (_dodgeTouch < 0 && CircleHit(position, Layout.Centre(Layout.DodgeCentre), Layout.Radius(Layout.DodgeRadius)))
                    {
                        _dodgeTouch = id;
                        _source.PressDodge();
                        sawDodge = true;
                    }
                }
            }

            if (!sawLeft) _leftTouch = -1;
            if (!sawRight) _rightTouch = -1;
            _moveValue = new Vector2((_leftTouch >= 0 ? -1f : 0f) + (_rightTouch >= 0 ? 1f : 0f), 0f);

            if (!sawJump && _jumpTouch >= 0)
            {
                _jumpTouch = -1;
                _source.ReleaseJump();
            }
            if (!sawAttack) _attackTouch = -1;
            if (!sawDodge) _dodgeTouch = -1;
        }

        private static bool CircleHit(Vector2 position, Vector2 centreFraction, float radiusFraction)
        {
            Vector2 centre = ToScreen(centreFraction);
            float radius = radiusFraction * Screen.height;
            return (position - centre).sqrMagnitude <= radius * radius;
        }

        // -- presentation ---------------------------------------------------------------------

        private void RefreshLayout(bool force)
        {
            bool resized = Screen.width != _cachedWidth
                || Screen.height != _cachedHeight
                || Screen.safeArea != _cachedSafeArea;
            if (!force && !resized) return;

            _cachedWidth = Screen.width;
            _cachedHeight = Screen.height;
            _cachedSafeArea = Screen.safeArea;

            Place(_leftRenderer.transform, Layout.Centre(Layout.LeftCentre), Layout.Radius(Layout.DirectionRadius) * 2f);
            Place(_rightRenderer.transform, Layout.Centre(Layout.RightCentre), Layout.Radius(Layout.DirectionRadius) * 2f);
            Place(_jumpRenderer.transform, Layout.Centre(Layout.JumpCentre), Layout.Radius(Layout.JumpRadius) * 2f);
            Place(_attackRenderer.transform, Layout.Centre(Layout.AttackCentre), Layout.Radius(Layout.AttackRadius) * 2f);
            Place(_dodgeRenderer.transform, Layout.Centre(Layout.DodgeCentre), Layout.Radius(Layout.DodgeRadius) * 2f);
        }

        /// <summary>Screen pixels to fractions of the safe area.</summary>
        private static Vector2 ToFraction(Vector2 screenPosition)
        {
            Rect safe = SafeArea();
            return new Vector2((screenPosition.x - safe.x) / Mathf.Max(1f, safe.width),
                               (screenPosition.y - safe.y) / Mathf.Max(1f, safe.height));
        }

        /// <summary>Fractions of the safe area back to screen pixels.</summary>
        private static Vector2 ToScreen(Vector2 fraction)
        {
            Rect safe = SafeArea();
            return new Vector2(safe.x + (fraction.x * safe.width), safe.y + (fraction.y * safe.height));
        }

        /// <summary>
        /// The drawable screen. Falls back to the whole screen when Unity reports a degenerate
        /// rect, which happens on some devices before the first orientation change settles.
        /// </summary>
        private static Rect SafeArea()
        {
            Rect safe = Screen.safeArea;
            if (safe.width < 1f || safe.height < 1f) return new Rect(0f, 0f, Screen.width, Screen.height);
            return safe;
        }

        private void Place(Transform target, Vector2 fraction, float diameterFraction)
        {
            if (target == null || _camera == null) return;

            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            Vector3 cameraPosition = _camera.transform.position;

            float worldX = cameraPosition.x + ((fraction.x - 0.5f) * 2f * halfWidth);
            float worldY = cameraPosition.y + ((fraction.y - 0.5f) * 2f * halfHeight);
            target.position = new Vector3(worldX, worldY, cameraPosition.z + 1f);
            target.localScale = new Vector3(diameterFraction * 2f * halfHeight, diameterFraction * 2f * halfHeight, 1f);
        }

        private SpriteRenderer CreateCircle(string name, Sprite sprite, Color colour, int sortingOrder)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            var renderer = host.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = colour;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        /// <summary>
        /// Hides the control glyphs and stops gameplay input while a modal owns the screen.
        /// The component remains alive so its visual children cannot be left onscreen when paused.
        /// </summary>
        public void SetGameplayVisible(bool visible)
        {
            if (_gameplayVisible == visible) return;

            _gameplayVisible = visible;
            if (!visible) ReleaseEverything();
            if (_source != null) _source.Enabled = visible;
            ApplyRendererVisibility();
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            ApplyRendererVisibility();
            if (!visible) ReleaseEverything();
        }

        private void ApplyRendererVisibility()
        {
            bool show = _visible && _gameplayVisible;
            if (_leftRenderer != null) _leftRenderer.enabled = show;
            if (_rightRenderer != null) _rightRenderer.enabled = show;
            if (_jumpRenderer != null) _jumpRenderer.enabled = show;
            if (_attackRenderer != null) _attackRenderer.enabled = show;
            if (_dodgeRenderer != null) _dodgeRenderer.enabled = show;
        }
    }
}
