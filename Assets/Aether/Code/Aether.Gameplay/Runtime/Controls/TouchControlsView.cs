using Aether.Core.Settings;
using Aether.Gameplay.Settings;
using Aether.Gameplay.Support;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aether.Gameplay.Controls
{
    /// <summary>
    /// The on-screen stick and buttons: a virtual joystick on the left, jump, attack and dodge on the
    /// right, sized and placed for thumbs in landscape.
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
    /// <b>The stick has no fixed origin.</b> The zone covers the lower-left of the screen, and the
    /// base moves to wherever the thumb lands. A fixed joystick forces the player to look at it;
    /// a floating one lets them keep their eyes on the level, which is the whole point of a
    /// two-thumb action game.
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
            /// <summary>Region of the lower left that may claim a touch for the stick.</summary>
            public static readonly Rect StickZone = new Rect(0f, 0f, 0.45f, 0.75f);

            /// <summary>Distance from the touch origin that means full tilt.</summary>
            public const float StickRadius = 0.15f;

            /// <summary>Movement below this fraction of the radius is ignored.</summary>
            public const float StickDeadZone = 0.14f;

            /// <summary>Where the stick rests before it is touched.</summary>
            public static readonly Vector2 StickRest = new Vector2(0.16f, 0.20f);

            public static readonly Vector2 JumpCentre = new Vector2(0.62f, 0.15f);
            public const float JumpRadius = 0.10f;

            public static readonly Vector2 AttackCentre = new Vector2(0.88f, 0.24f);
            public const float AttackRadius = 0.115f;

            public static readonly Vector2 DodgeCentre = new Vector2(0.73f, 0.46f);
            public const float DodgeRadius = 0.09f;

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

            /// <summary>Movement below this fraction of the stick's radius is ignored.</summary>
            public static float DeadZonePreference = StickDeadZone;

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

            /// <summary>The stick's claim on the lower third, mirrored with everything else.</summary>
            public static Rect StickZoneAt
            {
                get
                {
                    if (!Mirrored) return StickZone;
                    return new Rect(1f - StickZone.x - StickZone.width, StickZone.y,
                                    StickZone.width, StickZone.height);
                }
            }

            /// <summary>The dead zone the stick actually applies.</summary>
            public static float DeadZoneAt => Mathf.Clamp(DeadZonePreference, 0f, 0.45f);
        }

        private Camera _camera;
        private TouchInputSource _source;
        private Transform _stickBase;
        private Transform _stickKnob;
        private SpriteRenderer _stickBaseRenderer;
        private SpriteRenderer _stickKnobRenderer;
        private SpriteRenderer _jumpRenderer;
        private SpriteRenderer _attackRenderer;
        private SpriteRenderer _dodgeRenderer;

        private int _stickTouch = -1;
        private int _jumpTouch = -1;
        private int _attackTouch = -1;
        private int _dodgeTouch = -1;
        private Vector2 _stickOrigin;
        private Vector2 _stickValue;
        private bool _stickActive;

        private int _cachedWidth;
        private int _cachedHeight;
        private Rect _cachedSafeArea;
        private Vector2 _placedStickOrigin;
        private Vector2 _placedStickValue;
        private bool _placedStickActive;
        private bool _visible;

        /// <summary>True when this overlay is showing, which only happens when touch exists.</summary>
        public bool IsVisible => _visible;

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

            _stickBaseRenderer = CreateCircle("StickBase", PlaceholderVisuals.Ring,
                new Color(1f, 1f, 1f, 0.25f * Layout.OpacityScale), 900);
            _stickBase = _stickBaseRenderer.transform;

            _stickKnobRenderer = CreateCircle("StickKnob", PlaceholderVisuals.Circle,
                new Color(1f, 1f, 1f, 0.45f * Layout.OpacityScale), 901);
            _stickKnob = _stickKnobRenderer.transform;

            _jumpRenderer = CreateCircle("JumpButton", PlaceholderVisuals.Circle,
                new Color(0.55f, 0.85f, 1f, 0.35f * Layout.OpacityScale), 900);
            _attackRenderer = CreateCircle("AttackButton", PlaceholderVisuals.Circle,
                new Color(1f, 0.62f, 0.45f, 0.35f * Layout.OpacityScale), 901);
            _dodgeRenderer = CreateCircle("DodgeButton", PlaceholderVisuals.Circle,
                new Color(0.8f, 0.8f, 1f, 0.30f * Layout.OpacityScale), 899);

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
            Layout.DeadZonePreference = wanted.StickDeadZone;
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
            if (_stickBaseRenderer != null) SetAlpha(_stickBaseRenderer, 0.25f * opacity);
            if (_stickKnobRenderer != null) SetAlpha(_stickKnobRenderer, 0.45f * opacity);
            if (_jumpRenderer != null) SetAlpha(_jumpRenderer, 0.35f * opacity);
            if (_attackRenderer != null) SetAlpha(_attackRenderer, 0.35f * opacity);
            if (_dodgeRenderer != null) SetAlpha(_dodgeRenderer, 0.30f * opacity);
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            Color colour = renderer.color;
            colour.a = alpha;
            renderer.color = colour;
        }

        private void Update()
        {
            ReadTouches();
            RefreshLayout(force: false);
            _source.SetMove(_stickValue);
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
            _stickTouch = -1;
            _stickActive = false;
            _stickValue = Vector2.zero;
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

            bool sawStick = false;
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

                if (id == _stickTouch)
                {
                    ApplyStick(position);
                    sawStick = true;
                }
                else if (id == _jumpTouch)
                {
                    sawJump = true;
                }
                else if (id == _attackTouch)
                {
                    sawAttack = true;
                }
                else if (id == _dodgeTouch)
                {
                    sawDodge = true;
                }
                else if (began)
                {
                    // An unclaimed new touch: give it to whatever zone it landed in, if any.
                    if (_stickTouch < 0 && InStickZone(position))
                    {
                        _stickTouch = id;
                        _stickOrigin = position;
                        _stickActive = true;
                        ApplyStick(position);
                        sawStick = true;
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

            if (!sawStick)
            {
                _stickTouch = -1;
                _stickActive = false;
                _stickValue = Vector2.zero;
            }
            if (!sawJump && _jumpTouch >= 0)
            {
                // Releasing the jump control matters as much as pressing it: it is what ends the
                // variable-height part of a jump.
                _jumpTouch = -1;
                _source.ReleaseJump();
            }
            if (!sawAttack) _attackTouch = -1;
            if (!sawDodge) _dodgeTouch = -1;
        }

        private void ApplyStick(Vector2 position)
        {
            float radius = Layout.Radius(Layout.StickRadius) * Screen.height;
            Vector2 offset = (position - _stickOrigin) / Mathf.Max(1f, radius);
            float deadZone = Layout.DeadZoneAt;
            float magnitude = offset.magnitude;
            if (magnitude < deadZone) offset = Vector2.zero;
            else offset = offset.normalized * Mathf.Min(1f, (magnitude - deadZone) / (1f - deadZone));

            // Only horizontal movement exists in this game, but a full stick vector is produced so a
            // later ability (a crouch, a down-attack) needs no input plumbing.
            _stickValue = new Vector2(offset.x, offset.y);
        }

        private static bool InStickZone(Vector2 position)
        {
            Vector2 fraction = ToFraction(position);
            Rect zone = Layout.StickZoneAt;
            return fraction.x >= zone.xMin && fraction.x <= zone.xMax
                && fraction.y >= zone.yMin && fraction.y <= zone.yMax;
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
            bool stickMoved = _stickActive != _placedStickActive
                || _stickOrigin != _placedStickOrigin
                || _stickValue != _placedStickValue;

            if (!force && !resized && !stickMoved) return;

            if (force || resized)
            {
                _cachedWidth = Screen.width;
                _cachedHeight = Screen.height;
                _cachedSafeArea = Screen.safeArea;

                Place(_jumpRenderer.transform, Layout.Centre(Layout.JumpCentre), Layout.Radius(Layout.JumpRadius) * 2f);
                Place(_attackRenderer.transform, Layout.Centre(Layout.AttackCentre), Layout.Radius(Layout.AttackRadius) * 2f);
                Place(_dodgeRenderer.transform, Layout.Centre(Layout.DodgeCentre), Layout.Radius(Layout.DodgeRadius) * 2f);
            }

            // The stick is placed whenever it moves, not only when the screen changes: a floating
            // stick that never follows the thumb is a stick the player cannot aim with, and the
            // player's own input would be the only thing working.
            _placedStickActive = _stickActive;
            _placedStickOrigin = _stickOrigin;
            _placedStickValue = _stickValue;

            float stickRadius = Layout.Radius(Layout.StickRadius);
            Vector2 baseFraction = _stickActive
                ? ToFraction(_stickOrigin)
                : Layout.Centre(Layout.StickRest);
            Place(_stickBase, baseFraction, stickRadius * 2f);

            Vector2 knobFraction = _stickActive
                ? ToFraction(_stickOrigin + _stickValue * (stickRadius * Screen.height))
                : Layout.Centre(Layout.StickRest);
            Place(_stickKnob, knobFraction, stickRadius * 0.9f);
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

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_stickBaseRenderer != null) _stickBaseRenderer.enabled = visible;
            if (_stickKnobRenderer != null) _stickKnobRenderer.enabled = visible;
            if (_jumpRenderer != null) _jumpRenderer.enabled = visible;
            if (_attackRenderer != null) _attackRenderer.enabled = visible;
            if (_dodgeRenderer != null) _dodgeRenderer.enabled = visible;
            if (!visible) _source?.Reset();
        }
    }
}
