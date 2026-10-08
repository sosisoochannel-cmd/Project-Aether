using Aether.Core.Settings;
using Aether.Gameplay.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aether.Gameplay.Controls
{
    /// <summary>
    /// Premium touch control surface for Aether. No joystick: movement is two dedicated left/right
    /// buttons, while jump, attack and dodge each use their own monochrome texture/icon.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TouchControlsView : MonoBehaviour
    {
        public static class Layout
        {
            public static readonly Vector2 LeftCentre = new Vector2(0.13f, 0.18f);
            public static readonly Vector2 RightCentre = new Vector2(0.27f, 0.18f);
            public static readonly Vector2 JumpCentre = new Vector2(0.70f, 0.16f);
            public static readonly Vector2 AttackCentre = new Vector2(0.87f, 0.25f);
            public static readonly Vector2 DodgeCentre = new Vector2(0.78f, 0.43f);
            public const float MoveRadius = 0.085f;
            public const float JumpRadius = 0.085f;
            public const float AttackRadius = 0.10f;
            public const float DodgeRadius = 0.082f;
            public static float RadiusScale = 1f;
            public static float OpacityScale = 1f;
            public static bool Mirrored;

            public static Vector2 Centre(Vector2 value) =>
                Mirrored ? new Vector2(1f - value.x, value.y) : value;
            public static float Radius(float value) => value * RadiusScale;
        }

        private Camera _camera;
        private TouchInputSource _source;
        private SpriteRenderer _left, _right, _jump, _attack, _dodge;
        private int _leftTouch = -1, _rightTouch = -1, _jumpTouch = -1, _attackTouch = -1, _dodgeTouch = -1;
        private int _cachedWidth, _cachedHeight;
        private Rect _cachedSafeArea;
        private bool _visible;

        public bool IsVisible => _visible;

        public static TouchControlsView Create(Camera targetCamera, TouchInputSource source, Transform parent)
        {
            if (targetCamera == null || source == null || Touchscreen.current == null) return null;
            var host = new GameObject("TouchControls");
            host.transform.SetParent(parent, false);
            var view = host.AddComponent<TouchControlsView>();
            ApplySettings();
            view.Initialize(targetCamera, source);
            return view;
        }

        private void Initialize(Camera camera, TouchInputSource source)
        {
            _camera = camera;
            _source = source;
            _left = CreateButton("MoveLeft", TouchControlTextureKind.Left, 900);
            _right = CreateButton("MoveRight", TouchControlTextureKind.Right, 900);
            _jump = CreateButton("Jump", TouchControlTextureKind.Jump, 900);
            _attack = CreateButton("Attack", TouchControlTextureKind.Attack, 901);
            _dodge = CreateButton("Dodge", TouchControlTextureKind.Dodge, 899);
            _visible = true;
            RefreshLayout(true);
        }

        private SpriteRenderer CreateButton(string name, TouchControlTextureKind kind, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = TouchControlTextures.Get(kind);
            renderer.color = new Color(0.92f, 0.94f, 0.97f, 0.86f * Layout.OpacityScale);
            renderer.sortingOrder = order;
            return renderer;
        }

        public static void ApplySettings()
        {
            ControlSettings wanted = AetherSettings.Ensure().Values.Controls;
            Layout.RadiusScale = wanted.ButtonSize;
            Layout.OpacityScale = wanted.ButtonOpacity;
            Layout.Mirrored = wanted.LeftHanded;
        }

        private void OnEnable() => AetherSettings.Ensure().Changed += OnSettingsChanged;

        private void OnDestroy()
        {
            AetherSettings.Current.Changed -= OnSettingsChanged;
            _source?.Reset();
        }

        private void OnSettingsChanged(string id)
        {
            ApplySettings();
            SetAlpha(_left); SetAlpha(_right); SetAlpha(_jump); SetAlpha(_attack); SetAlpha(_dodge);
            RefreshLayout(true);
        }

        private static void SetAlpha(SpriteRenderer r)
        {
            if (r == null) return;
            Color c = r.color;
            c.a = 0.86f * Layout.OpacityScale;
            r.color = c;
        }

        private void Update()
        {
            ReadTouches();
            RefreshLayout(false);
        }

        private void OnDisable() => ReleaseEverything();
        private void OnApplicationFocus(bool focus) { if (!focus) ReleaseEverything(); }
        private void OnApplicationPause(bool pause) { if (pause) ReleaseEverything(); }

        private void ReleaseEverything()
        {
            _source?.Reset();
            _leftTouch = _rightTouch = _jumpTouch = _attackTouch = _dodgeTouch = -1;
        }

        private void ReadTouches()
        {
            Touchscreen screen = Touchscreen.current;
            if (screen == null) { SetVisible(false); return; }
            SetVisible(true);

            bool left = false, right = false, jump = false, attack = false, dodge = false;
            var touches = screen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                if (!t.press.isPressed) continue;
                int id = t.touchId.ReadValue();
                Vector2 p = t.position.ReadValue();
                bool began = t.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Began;

                if (id == _leftTouch) left = true;
                else if (id == _rightTouch) right = true;
                else if (id == _jumpTouch) jump = true;
                else if (id == _attackTouch) attack = true;
                else if (id == _dodgeTouch) dodge = true;
                else if (began)
                {
                    if (_leftTouch < 0 && Hit(p, Layout.LeftCentre, Layout.MoveRadius)) { _leftTouch = id; _source.PressLeft(); left = true; }
                    else if (_rightTouch < 0 && Hit(p, Layout.RightCentre, Layout.MoveRadius)) { _rightTouch = id; _source.PressRight(); right = true; }
                    else if (_jumpTouch < 0 && Hit(p, Layout.JumpCentre, Layout.JumpRadius)) { _jumpTouch = id; _source.PressJump(); jump = true; }
                    else if (_attackTouch < 0 && Hit(p, Layout.AttackCentre, Layout.AttackRadius)) { _attackTouch = id; _source.PressAttack(); attack = true; }
                    else if (_dodgeTouch < 0 && Hit(p, Layout.DodgeCentre, Layout.DodgeRadius)) { _dodgeTouch = id; _source.PressDodge(); dodge = true; }
                }
            }

            if (!left && _leftTouch >= 0) { _leftTouch = -1; _source.ReleaseLeft(); }
            if (!right && _rightTouch >= 0) { _rightTouch = -1; _source.ReleaseRight(); }
            if (!jump && _jumpTouch >= 0) { _jumpTouch = -1; _source.ReleaseJump(); }
            if (!attack) _attackTouch = -1;
            if (!dodge) _dodgeTouch = -1;
        }

        private static bool Hit(Vector2 position, Vector2 centre, float radius)
        {
            Rect safe = SafeArea();
            Vector2 c = new Vector2(safe.x + centre.x * safe.width, safe.y + centre.y * safe.height);
            float r = radius * Layout.RadiusScale * Screen.height;
            return (position - c).sqrMagnitude <= r * r;
        }

        private void RefreshLayout(bool force)
        {
            bool resized = Screen.width != _cachedWidth || Screen.height != _cachedHeight || Screen.safeArea != _cachedSafeArea;
            if (!force && !resized) return;
            _cachedWidth = Screen.width; _cachedHeight = Screen.height; _cachedSafeArea = Screen.safeArea;
            Place(_left, Layout.Centre(Layout.LeftCentre), Layout.Radius(Layout.MoveRadius));
            Place(_right, Layout.Centre(Layout.RightCentre), Layout.Radius(Layout.MoveRadius));
            Place(_jump, Layout.Centre(Layout.JumpCentre), Layout.Radius(Layout.JumpRadius));
            Place(_attack, Layout.Centre(Layout.AttackCentre), Layout.Radius(Layout.AttackRadius));
            Place(_dodge, Layout.Centre(Layout.DodgeCentre), Layout.Radius(Layout.DodgeRadius));
        }

        private void Place(SpriteRenderer renderer, Vector2 fraction, float radius)
        {
            if (renderer == null || _camera == null) return;
            Rect safe = SafeArea();
            float x = safe.x + fraction.x * safe.width;
            float y = safe.y + fraction.y * safe.height;
            Vector3 cam = _camera.transform.position;
            float h = _camera.orthographicSize;
            float w = h * _camera.aspect;
            renderer.transform.position = new Vector3(
                cam.x + ((x / Mathf.Max(1f, Screen.width) - 0.5f) * 2f * w),
                cam.y + ((y / Mathf.Max(1f, Screen.height) - 0.5f) * 2f * h),
                cam.z + 1f);
            renderer.transform.localScale = Vector3.one * (radius * 2f * h);
        }

        private static Rect SafeArea()
        {
            Rect safe = Screen.safeArea;
            return safe.width > 1f && safe.height > 1f ? safe : new Rect(0, 0, Screen.width, Screen.height);
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible && _left != null) return;
            _visible = visible;
            if (_left != null) _left.enabled = visible;
            if (_right != null) _right.enabled = visible;
            if (_jump != null) _jump.enabled = visible;
            if (_attack != null) _attack.enabled = visible;
            if (_dodge != null) _dodge.enabled = visible;
            if (!visible) _source?.Reset();
        }
    }
}
