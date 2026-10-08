using Aether.Core.Settings;
using Aether.Gameplay.Menus;
using UnityEngine;

namespace Aether.Gameplay.Presentation
{
    /// <summary>
    /// Keeps the readable interface inside the part of the screen a phone actually shows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A landscape phone with a camera cutout loses real estate on one end, and a gesture bar keeps
    /// a strip along the bottom. Both are reported by the platform in <c>Screen.safeArea</c>, in
    /// pixels, and the interface has to be told about them in its own coordinates. This component
    /// does that conversion once per change and nothing else: <c>Screen.safeArea</c> is a fraction of
    /// the screen, so it is turned into anchors rather than offsets, which keeps the result correct
    /// when the canvas is resized, when the interface scale changes, and on a device with a notch on
    /// either side.
    /// </para>
    /// <para>
    /// <b>No per-frame work and no polling.</b> The safe area is applied when the component is
    /// enabled and when its own rect changes size — which is what the canvas does when the screen
    /// rotates, the window resizes or the reference resolution changes. That callback arrives from
    /// the layout system rather than from an <c>Update</c>, so a menu that is standing still costs
    /// nothing at all.
    /// </para>
    /// <para>
    /// The player can turn it off (<see cref="SafeAreaMode.FullScreen"/>), which is a real choice:
    /// some people would rather have the artwork reach the corners and accept a row near the
    /// cutout.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rect;
        private Canvas _canvas;
        private SafeAreaMode _mode = SafeAreaMode.Respect;
        private Rect _applied = new Rect(-1f, -1f, -1f, -1f);

        /// <summary>Raised after the safe-area rect is reapplied because its mode or dimensions changed.</summary>
        public event System.Action DimensionsChanged;

        /// <summary>Which rule is in force. Setting it reapplies immediately.</summary>
        public SafeAreaMode Mode
        {
            get { return _mode; }
            set
            {
                if (_mode == value && _applied.width >= 0f) return;
                _mode = value;
                _applied = new Rect(-1f, -1f, -1f, -1f);
                Apply();
                NotifyDimensionsChanged();
            }
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();
        }

        private void OnEnable()
        {
            Apply();
            NotifyDimensionsChanged();
        }

        /// <summary>
        /// Called by Unity when this rect's size changes, which is the canvas resizing, rotating or
        /// being given a new reference resolution. Reapplying here is the whole mechanism.
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled || _rect == null) return;
            Apply();
            NotifyDimensionsChanged();
        }

        private void NotifyDimensionsChanged()
        {
            System.Action changed = DimensionsChanged;
            if (changed != null) changed();
        }

        /// <summary>Anchors this rect to the device's safe area, or to the whole canvas when off.</summary>
        public void Apply()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null) return;

            // The canvas's own pixel rect, not the screen's: a canvas is not always the whole
            // screen once another camera or a display is involved, and the anchors below are
            // fractions of the canvas.
            Rect canvasRect = _canvas.pixelRect;
            if (canvasRect.width <= 0f || canvasRect.height <= 0f) return;

            Rect safe = _mode == SafeAreaMode.Respect ? Screen.safeArea : new Rect(0f, 0f, Screen.width, Screen.height);
            if (safe.width <= 0f || safe.height <= 0f) safe = new Rect(0f, 0f, canvasRect.width, canvasRect.height);

            var min = new Vector2(
                Mathf.Clamp01((safe.xMin - canvasRect.xMin) / canvasRect.width),
                Mathf.Clamp01((safe.yMin - canvasRect.yMin) / canvasRect.height));
            var max = new Vector2(
                Mathf.Clamp01((safe.xMax - canvasRect.xMin) / canvasRect.width),
                Mathf.Clamp01((safe.yMax - canvasRect.yMin) / canvasRect.height));

            if (min.x == _applied.xMin && min.y == _applied.yMin
                && max.x == _applied.xMax && max.y == _applied.yMax)
            {
                return;
            }

            _applied = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
