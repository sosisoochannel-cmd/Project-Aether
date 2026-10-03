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
        // -- layout, in fractions of screen height unless noted ---------------------------------
        private static readonly Rect StickZone = new Rect(0f, 0f, 0.45f, 0.75f);
        private const float StickRadius = 0.15f;
        private const float StickDeadZone = 0.14f;
        private static readonly Vector2 StickRest = new Vector2(0.16f, 0.20f);

        private static readonly Vector2 JumpCentre = new Vector2(0.705f, 0.19f);
        private const float JumpRadius = 0.105f;

        private static readonly Vector2 AttackCentre = new Vector2(0.875f, 0.235f);
        private const float AttackRadius = 0.125f;

        private static readonly Vector2 DodgeCentre = new Vector2(0.775f, 0.47f);
        private const float DodgeRadius = 0.09f;

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
            view.Initialize(targetCamera, source);
            return view;
        }

        private void Initialize(Camera targetCamera, TouchInputSource source)
        {
            _camera = targetCamera;
            _source = source;

            _stickBaseRenderer = CreateCircle("StickBase", PlaceholderVisuals.Ring,
                new Color(1f, 1f, 1f, 0.25f), 900);
            _stickBase = _stickBaseRenderer.transform;

            _stickKnobRenderer = CreateCircle("StickKnob", PlaceholderVisuals.Circle,
                new Color(1f, 1f, 1f, 0.45f), 901);
            _stickKnob = _stickKnobRenderer.transform;

            _jumpRenderer = CreateCircle("JumpButton", PlaceholderVisuals.Circle,
                new Color(0.55f, 0.85f, 1f, 0.35f), 900);
            _attackRenderer = CreateCircle("AttackButton", PlaceholderVisuals.Circle,
                new Color(1f, 0.62f, 0.45f, 0.35f), 901);
            _dodgeRenderer = CreateCircle("DodgeButton", PlaceholderVisuals.Circle,
                new Color(0.8f, 0.8f, 1f, 0.30f), 899);

            _visible = true;
            RefreshLayout(force: true);
        }

        private void OnDestroy()
        {
            if (_source == null) return;
            _source.Reset();
        }

        private void Update()
        {
            RefreshLayout(force: false);
            ReadTouches();
            _source.SetMove(_stickValue);
        }

        private void OnDisable()
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
                    else if (_jumpTouch < 0 && CircleHit(position, JumpCentre, JumpRadius))
                    {
                        _jumpTouch = id;
                        _source.PressJump();
                        sawJump = true;
                    }
                    else if (_attackTouch < 0 && CircleHit(position, AttackCentre, AttackRadius))
                    {
                        _attackTouch = id;
                        _source.PressAttack();
                        sawAttack = true;
                    }
                    else if (_dodgeTouch < 0 && CircleHit(position, DodgeCentre, DodgeRadius))
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
            float radius = StickRadius * Screen.height;
            Vector2 offset = (position - _stickOrigin) / Mathf.Max(1f, radius);
            float magnitude = offset.magnitude;
            if (magnitude < StickDeadZone) offset = Vector2.zero;
            else offset = offset.normalized * Mathf.Min(1f, (magnitude - StickDeadZone) / (1f - StickDeadZone));

            // Only horizontal movement exists in this game, but a full stick vector is produced so a
            // later ability (a crouch, a down-attack) needs no input plumbing.
            _stickValue = new Vector2(offset.x, offset.y);
        }

        private static bool InStickZone(Vector2 position)
        {
            return position.x <= StickZone.width * Screen.width
                && position.y <= StickZone.height * Screen.height;
        }

        private static bool CircleHit(Vector2 position, Vector2 centreFraction, float radiusFraction)
        {
            Vector2 centre = new Vector2(centreFraction.x * Screen.width, centreFraction.y * Screen.height);
            float radius = radiusFraction * Screen.height;
            return (position - centre).sqrMagnitude <= radius * radius;
        }

        // -- presentation ---------------------------------------------------------------------

        private void RefreshLayout(bool force)
        {
            if (!force && _cachedWidth == Screen.width && _cachedHeight == Screen.height) return;
            _cachedWidth = Screen.width;
            _cachedHeight = Screen.height;

            Place(_stickBase, _stickActive ? ToFraction(_stickOrigin) : StickRest, StickRadius * 2f);
            Place(_stickKnob, _stickActive ? ToFraction(_stickOrigin + _stickValue * (StickRadius * Screen.height)) : StickRest, StickRadius * 0.9f);
            Place(_jumpRenderer.transform, JumpCentre, JumpRadius * 2f);
            Place(_attackRenderer.transform, AttackCentre, AttackRadius * 2f);
            Place(_dodgeRenderer.transform, DodgeCentre, DodgeRadius * 2f);
        }

        private Vector2 ToFraction(Vector2 screenPosition)
        {
            return new Vector2(screenPosition.x / Mathf.Max(1f, Screen.width),
                               screenPosition.y / Mathf.Max(1f, Screen.height));
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
