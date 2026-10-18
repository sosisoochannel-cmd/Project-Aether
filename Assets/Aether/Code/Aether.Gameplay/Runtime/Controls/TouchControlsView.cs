using Aether.Core.Settings;
using Aether.Gameplay.Settings;
using Aether.Gameplay.Support;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aether.Gameplay.Controls
{
    /// <summary>
    /// The on-screen controls: a static virtual joystick for analogue/directional movement
    /// on one thumb, and action glyphs for jump, attack, and dodge on the other.
    /// Each interactive zone tracks its own touch ID so simultaneous multi-finger gameplay is robust.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TouchControlsView : MonoBehaviour
    {
        /// <summary>
        /// Where everything sits, in fractions of the <b>safe area</b>.
        /// </summary>
        public static class Layout
        {
            // Static virtual joystick geometry (anchor position and base radius)
            public static readonly Vector2 DefaultJoystickCentre = new Vector2(0.18f, 0.20f);
            public static Vector2 JoystickCentre = DefaultJoystickCentre;
            public const float JoystickBaseRadius = 0.115f;
            public const float JoystickHandleRadius = 0.050f;

            // Backward compatibility aliases for verification tools & legacy queries
            public static readonly Vector2 LeftCentre = new Vector2(0.10f, 0.18f);
            public static readonly Vector2 RightCentre = new Vector2(0.27f, 0.18f);
            public const float DirectionRadius = 0.075f;

            public static readonly Vector2 JumpCentre = new Vector2(0.63f, 0.15f);
            public const float JumpRadius = 0.080f;

            public static readonly Vector2 AttackCentre = new Vector2(0.91f, 0.19f);
            public const float AttackRadius = 0.082f;

            public static readonly Vector2 DodgeCentre = new Vector2(0.75f, 0.43f);
            public const float DodgeRadius = 0.070f;

            // Player preferences
            public static float RadiusScale = 1f;
            public static float OpacityScale = 1f;
            public static float StickDeadZone = 0.14f;
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

        // Joystick renderers
        private SpriteRenderer _joystickBaseRenderer;
        private SpriteRenderer _joystickHandleRenderer;

        // Action renderers
        private SpriteRenderer _jumpRenderer;
        private SpriteRenderer _attackRenderer;
        private SpriteRenderer _dodgeRenderer;

        // Touch tracking
        private int _stickTouch = -1;
        private int _jumpTouch = -1;
        private int _attackTouch = -1;
        private int _dodgeTouch = -1;

        private Vector2 _moveValue;
        private Vector2 _stickHandleOffsetPx;

        private int _cachedWidth;
        private int _cachedHeight;
        private Rect _cachedSafeArea;
        private bool _visible;
        private bool _gameplayVisible = true;

        /// <summary>True when this overlay is showing, which only happens when touch exists.</summary>
        public bool IsVisible => _visible && _gameplayVisible;

        /// <summary>
        /// Creates the overlay and returns it, or null when the device has no touchscreen.
        /// </summary>
        public static TouchControlsView Create(Camera targetCamera, TouchInputSource source, Transform parent)
        {
            if (targetCamera == null || source == null) return null;
            if (Touchscreen.current == null) return null;

            var host = new GameObject("TouchControls");
            host.transform.SetParent(parent, false);
            var view = host.AddComponent<TouchControlsView>();

            ApplySettings();
            view.Initialize(targetCamera, source);
            return view;
        }

        private void Initialize(Camera targetCamera, TouchInputSource source)
        {
            _camera = targetCamera;
            _source = source;

            // Static Joystick Base and Handle
            _joystickBaseRenderer = CreateCircle("JoystickBase", PlaceholderVisuals.Ring,
                new Color(1f, 1f, 1f, 0.50f * Layout.OpacityScale), 895);
            _joystickHandleRenderer = CreateCircle("JoystickHandle", PlaceholderVisuals.Circle,
                new Color(0.72f, 0.94f, 1f, 0.60f * Layout.OpacityScale), 898);

            // Action Buttons
            _jumpRenderer = CreateCircle("JumpButton", PlaceholderVisuals.UpArrow,
                new Color(1f, 1f, 1f, 0.58f * Layout.OpacityScale), 900);
            _attackRenderer = CreateCircle("AttackButton", PlaceholderVisuals.Slash,
                new Color(1f, 1f, 1f, 0.58f * Layout.OpacityScale), 901);
            _dodgeRenderer = CreateCircle("DodgeButton", PlaceholderVisuals.Diamond,
                new Color(0.72f, 0.94f, 1f, 0.52f * Layout.OpacityScale), 899);

            _visible = true;
            RefreshLayout(force: true);
        }

        private void OnEnable()
        {
            var settings = AetherSettings.Ensure();
            settings.Changed -= OnSettingsChanged;
            settings.Changed += OnSettingsChanged;
        }

        private void OnDestroy()
        {
            AetherSettings.Current.Changed -= OnSettingsChanged;
            ReleaseEverything();
        }

        public static void ApplySettings()
        {
            ControlSettings wanted = AetherSettings.Ensure().Values.Controls;

            Layout.RadiusScale = wanted.ButtonSize;
            Layout.OpacityScale = wanted.ButtonOpacity;
            Layout.StickDeadZone = wanted.StickDeadZone;
            Layout.Mirrored = wanted.LeftHanded;
        }

        private void OnSettingsChanged(string id)
        {
            ApplySettings();
            Repaint();
            RefreshLayout(true);
        }

        private void Repaint()
        {
            float opacity = Layout.OpacityScale;
            if (_joystickBaseRenderer != null) SetAlpha(_joystickBaseRenderer, 0.50f * opacity);
            if (_joystickHandleRenderer != null) SetAlpha(_joystickHandleRenderer, 0.60f * opacity);
            if (_jumpRenderer != null) SetAlpha(_jumpRenderer, 0.58f * opacity);
            if (_attackRenderer != null) SetAlpha(_attackRenderer, 0.58f * opacity);
            if (_dodgeRenderer != null) SetAlpha(_dodgeRenderer, 0.52f * opacity);
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            Color colour = renderer.color;
            colour.a = alpha;
            renderer.color = colour;
        }

        private void Update()
        {
            if (!_gameplayVisible) return;

            ReadTouches();
            RefreshLayout(force: false);
            _source.SetMove(_moveValue);
        }

        private void OnDisable()
        {
            AetherSettings.Current.Changed -= OnSettingsChanged;
            ReleaseEverything();
        }

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
            _stickHandleOffsetPx = Vector2.zero;
            _moveValue = Vector2.zero;
            _jumpTouch = -1;
            _attackTouch = -1;
            _dodgeTouch = -1;
        }

        // -- Input Reading --

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

            Vector2 stickCenterPx = ToScreen(Layout.Centre(Layout.JoystickCentre));
            float stickBaseRadiusPx = Layout.Radius(Layout.JoystickBaseRadius) * Screen.height;

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
                    sawStick = true;
                    UpdateJoystickMovement(position, stickCenterPx, stickBaseRadiusPx);
                }
                else if (id == _jumpTouch) sawJump = true;
                else if (id == _attackTouch) sawAttack = true;
                else if (id == _dodgeTouch) sawDodge = true;
                else if (began)
                {
                    // Hit-test Joystick area
                    if (_stickTouch < 0 && (position - stickCenterPx).sqrMagnitude <= stickBaseRadiusPx * stickBaseRadiusPx * 1.5f)
                    {
                        _stickTouch = id;
                        sawStick = true;
                        UpdateJoystickMovement(position, stickCenterPx, stickBaseRadiusPx);
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
                _stickHandleOffsetPx = Vector2.zero;
                _moveValue = Vector2.zero;
            }

            if (!sawJump && _jumpTouch >= 0)
            {
                _jumpTouch = -1;
                _source.ReleaseJump();
            }
            if (!sawAttack) _attackTouch = -1;
            if (!sawDodge) _dodgeTouch = -1;
        }

        private void UpdateJoystickMovement(Vector2 touchPos, Vector2 centerPx, float maxRadiusPx)
        {
            Vector2 offset = touchPos - centerPx;
            float dist = offset.magnitude;
            if (dist > maxRadiusPx && maxRadiusPx > 0.001f)
            {
                offset = (offset / dist) * maxRadiusPx;
                dist = maxRadiusPx;
            }

            _stickHandleOffsetPx = offset;

            float normDist = maxRadiusPx > 0.001f ? dist / maxRadiusPx : 0f;
            float deadZone = Mathf.Clamp01(Layout.StickDeadZone);

            if (normDist <= deadZone)
            {
                _moveValue = Vector2.zero;
            }
            else
            {
                float remapped = (normDist - deadZone) / Mathf.Max(0.001f, 1f - deadZone);
                Vector2 dir = offset.normalized;
                _moveValue = dir * Mathf.Clamp01(remapped);
            }
        }

        private static bool CircleHit(Vector2 position, Vector2 centreFraction, float radiusFraction)
        {
            Vector2 centre = ToScreen(centreFraction);
            float radius = radiusFraction * Screen.height;
            return (position - centre).sqrMagnitude <= radius * radius;
        }

        // -- Presentation & Layout --

        private void RefreshLayout(bool force)
        {
            bool resized = Screen.width != _cachedWidth
                || Screen.height != _cachedHeight
                || Screen.safeArea != _cachedSafeArea;
            if (!force && !resized)
            {
                // Reposition handle dynamically when touched
                if (_joystickHandleRenderer != null && _camera != null)
                {
                    PlaceScreenPixel(_joystickHandleRenderer.transform,
                        ToScreen(Layout.Centre(Layout.JoystickCentre)) + _stickHandleOffsetPx,
                        Layout.Radius(Layout.JoystickHandleRadius) * 2f);
                }
                return;
            }

            _cachedWidth = Screen.width;
            _cachedHeight = Screen.height;
            _cachedSafeArea = Screen.safeArea;

            // Place static joystick base
            if (_joystickBaseRenderer != null)
            {
                Place(_joystickBaseRenderer.transform, Layout.Centre(Layout.JoystickCentre),
                    Layout.Radius(Layout.JoystickBaseRadius) * 2f);
            }

            // Place joystick handle
            if (_joystickHandleRenderer != null)
            {
                PlaceScreenPixel(_joystickHandleRenderer.transform,
                    ToScreen(Layout.Centre(Layout.JoystickCentre)) + _stickHandleOffsetPx,
                    Layout.Radius(Layout.JoystickHandleRadius) * 2f);
            }

            // Place action buttons
            if (_jumpRenderer != null) Place(_jumpRenderer.transform, Layout.Centre(Layout.JumpCentre), Layout.Radius(Layout.JumpRadius) * 2f);
            if (_attackRenderer != null) Place(_attackRenderer.transform, Layout.Centre(Layout.AttackCentre), Layout.Radius(Layout.AttackRadius) * 2f);
            if (_dodgeRenderer != null) Place(_dodgeRenderer.transform, Layout.Centre(Layout.DodgeCentre), Layout.Radius(Layout.DodgeRadius) * 2f);
        }

        private static Vector2 ToScreen(Vector2 fraction)
        {
            Rect safe = SafeArea();
            return new Vector2(safe.x + (fraction.x * safe.width), safe.y + (fraction.y * safe.height));
        }

        private static Rect SafeArea()
        {
            Rect safe = Screen.safeArea;
            if (safe.width < 1f || safe.height < 1f) return new Rect(0f, 0f, Screen.width, Screen.height);
            return safe;
        }

        private void Place(Transform target, Vector2 fraction, float diameterFraction)
        {
            if (target == null || _camera == null) return;
            PlaceScreenPixel(target, ToScreen(fraction), diameterFraction);
        }

        private void PlaceScreenPixel(Transform target, Vector2 screenPx, float diameterFraction)
        {
            if (target == null || _camera == null) return;

            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            Vector3 cameraPosition = _camera.transform.position;

            float viewportX = screenPx.x / Mathf.Max(1f, Screen.width);
            float viewportY = screenPx.y / Mathf.Max(1f, Screen.height);
            float worldX = cameraPosition.x + ((viewportX - 0.5f) * 2f * halfWidth);
            float worldY = cameraPosition.y + ((viewportY - 0.5f) * 2f * halfHeight);
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

        public void SetGameplayVisible(bool visible)
        {
            if (_gameplayVisible == visible) return;

            _gameplayVisible = visible;
            if (!visible) ReleaseEverything();
            if (_source != null) _source.Enabled = visible;
            RefreshRendererVisibility();
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            RefreshRendererVisibility();
            if (!visible) ReleaseEverything();
        }

        private void RefreshRendererVisibility()
        {
            bool show = _visible && _gameplayVisible;
            if (_joystickBaseRenderer != null) _joystickBaseRenderer.enabled = show;
            if (_joystickHandleRenderer != null) _joystickHandleRenderer.enabled = show;
            if (_jumpRenderer != null) _jumpRenderer.enabled = show;
            if (_attackRenderer != null) _attackRenderer.enabled = show;
            if (_dodgeRenderer != null) _dodgeRenderer.enabled = show;
        }
    }
}
