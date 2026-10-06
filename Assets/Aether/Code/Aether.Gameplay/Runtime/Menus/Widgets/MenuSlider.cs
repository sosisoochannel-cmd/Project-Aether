using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Widgets
{
    /// <summary>
    /// A slider that is dragged, and that takes the drag away from the list it sits in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a <c>UnityEngine.UI.Slider</c>.</b> A <c>Slider</c> is a <c>Selectable</c>, and a
    /// <c>Selectable</c> takes the EventSystem's selection when it is pressed — which would pull the
    /// highlight off the row this slider belongs to, and leave the menu's own navigation pointing at
    /// a row that is no longer drawn as current. A row is one target; the slider inside it is a piece
    /// of that target, not a second one.
    /// </para>
    /// <para>
    /// <b>Dragging beats scrolling.</b> A settings list scrolls, and this control is dragged, and the
    /// two use the same finger. Implementing the drag interfaces claims the gesture from the
    /// enclosing <c>ScrollRect</c>, so a finger that starts on the track adjusts the value and a
    /// finger that starts anywhere else scrolls the list — which is the only behaviour that does not
    /// make a player fight the screen.
    /// </para>
    /// <para>
    /// <b>Writes are committed, not streamed.</b> The value changes on every drag frame so the
    /// interface answers the finger, but the settings service is only told when the drag ends: a
    /// slider that rewrites a settings file sixty times a second is a slider that stutters.
    /// </para>
    /// </remarks>
    public sealed class MenuSlider : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
                                     IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private float _minimum;
        private float _maximum;
        private float _step;
        private float _value;
        private bool _dragging;
        private bool _pressed;
        private bool _interactable = true;

        private RectTransform _track;
        private RectTransform _knob;
        private RectTransform _fill;
        private Text _readout;
        private Image _fillImage;

        /// <summary>
        /// Raised as the value changes.
        /// </summary>
        /// <remarks>
        /// The flag is false while the finger is still down and true once the gesture is over. A
        /// screen uses the first to apply the change live — volume is the reason this exists, since
        /// dragging a volume slider should be audible — and the second to persist it.
        /// </remarks>
        public Action<float, bool> ValueChanged;

        /// <summary>The value the slider is showing, snapped to its step.</summary>
        public float Value
        {
            get { return _value; }
        }

        /// <summary>
        /// Builds a slider inside a row's control area.
        /// </summary>
        /// <param name="name">GameObject name.</param>
        /// <param name="parent">The row's control area.</param>
        /// <param name="minimum">Lowest value.</param>
        /// <param name="maximum">Highest value.</param>
        /// <param name="step">Snap interval; 0 means continuous.</param>
        public static MenuSlider Create(string name, Transform parent, float minimum, float maximum,
                                        float step)
        {
            MenuArt.EnsureBuilt();

            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var rect = (RectTransform)host.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(MenuTheme.Metrics.SliderWidth + 120f, MenuTheme.Metrics.SettingRowHeight);

            var slider = host.AddComponent<MenuSlider>();
            slider._minimum = minimum;
            slider._maximum = maximum;
            slider._step = step;

            // The touch area is the full height of the row and a little wider than the visible track:
            // a 6-unit line is the right thing to look at and the wrong thing to aim at.
            var hit = MenuUi.CreateImage("Hit", rect, MenuArt.Solid, new Color(0f, 0f, 0f, 0f), true);
            MenuUi.Stretch(hit.rectTransform);

            slider._track = MenuUi.CreateNode("Track", rect);
            slider._track.anchorMin = new Vector2(0f, 0.5f);
            slider._track.anchorMax = new Vector2(0f, 0.5f);
            slider._track.pivot = new Vector2(0f, 0.5f);
            slider._track.sizeDelta = new Vector2(MenuTheme.Metrics.SliderWidth,
                                                 MenuTheme.Metrics.SliderTrackHeight);
            slider._track.anchoredPosition = Vector2.zero;

            MenuUi.CreateImage("Rail", slider._track, MenuArt.Solid, MenuTheme.Palette.Line);

            slider._fill = MenuUi.CreateNode("Fill", slider._track);
            slider._fill.anchorMin = new Vector2(0f, 0f);
            slider._fill.anchorMax = new Vector2(0f, 1f);
            slider._fill.pivot = new Vector2(0f, 0.5f);
            slider._fill.sizeDelta = new Vector2(0f, 0f);
            slider._fill.anchoredPosition = Vector2.zero;
            slider._fillImage = slider._fill.gameObject.AddComponent<Image>();
            slider._fillImage.sprite = MenuArt.Solid;
            slider._fillImage.raycastTarget = false;
            slider._fillImage.color = MenuTheme.Palette.Accent;

            slider._knob = MenuUi.CreateNode("Knob", slider._track);
            slider._knob.anchorMin = new Vector2(0f, 0.5f);
            slider._knob.anchorMax = new Vector2(0f, 0.5f);
            slider._knob.pivot = new Vector2(0.5f, 0.5f);
            slider._knob.sizeDelta = new Vector2(MenuTheme.Metrics.SliderKnobSize,
                                                MenuTheme.Metrics.SliderKnobSize);
            var knobImage = slider._knob.gameObject.AddComponent<Image>();
            knobImage.sprite = MenuArt.Circle;
            knobImage.raycastTarget = false;
            knobImage.color = MenuTheme.Palette.Ink;

            slider._readout = MenuUi.CreateText("Value", rect, string.Empty,
                                                MenuTheme.Metrics.SettingLabelSize,
                                                MenuTheme.Palette.InkMuted, TextAnchor.MiddleLeft);
            slider._readout.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            slider._readout.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            slider._readout.rectTransform.pivot = new Vector2(0f, 0.5f);
            slider._readout.rectTransform.sizeDelta = new Vector2(120f, MenuTheme.Metrics.SettingRowHeight);
            slider._readout.rectTransform.anchoredPosition = new Vector2(MenuTheme.Metrics.SliderWidth + 28f, 0f);

            slider.SetValue(minimum, false);
            return slider;
        }

        /// <summary>Sets the range, for a row that is reused for a different setting.</summary>
        public void SetRange(float minimum, float maximum, float step)
        {
            _minimum = minimum;
            _maximum = maximum;
            _step = step;
            SetValue(_value, false);
        }

        /// <summary>
        /// Shows a value. Does not raise <see cref="ValueChanged"/> unless asked, because a screen
        /// filling a row in is not the player changing anything.
        /// </summary>
        public void SetValue(float value, bool notify)
        {
            float snapped = Snap(value);
            bool changed = !Mathf.Approximately(snapped, _value);
            _value = snapped;

            Draw();
            if (notify && changed)
            {
                Action<float, bool> handler = ValueChanged;
                if (handler != null) handler(_value, true);
            }
        }

        /// <summary>
        /// Shows a value as text, for the readout beside the track — a volume as a percentage, a
        /// sensitivity as a number.
        /// </summary>
        /// <remarks>
        /// The text is supplied by the screen rather than formatted here, because how a value reads
        /// is a localisation decision and this widget knows nothing about languages.
        /// </remarks>
        public void SetReadout(string text)
        {
            if (_readout != null) _readout.text = text;
        }

        /// <summary>Enables or disables the drag, for a setting that is not available.</summary>
        public void SetInteractable(bool interactable)
        {
            _interactable = interactable;
            if (_fillImage != null)
            {
                _fillImage.color = interactable ? MenuTheme.Palette.Accent : MenuTheme.Palette.Locked;
            }
        }

        /// <summary>
        /// Steps the value, for a left or right press on the row this belongs to.
        /// </summary>
        /// <param name="direction">-1 to lower, +1 to raise.</param>
        /// <returns>True when the value moved.</returns>
        public bool Nudge(float direction)
        {
            if (!_interactable || direction == 0f) return false;

            float increment = _step > 0f ? _step : (_maximum - _minimum) * 0.05f;
            float wanted = Mathf.Clamp(_value + (Mathf.Sign(direction) * increment), _minimum, _maximum);
            float snapped = Snap(wanted);
            if (Mathf.Approximately(snapped, _value)) return false;

            _value = snapped;
            Draw();

            Action<float, bool> handler = ValueChanged;
            if (handler != null) handler(_value, true);
            return true;
        }

        // -- drag ------------------------------------------------------------------------------

        /// <inheritdoc />
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_interactable) return;

            _pressed = true;
            SetFromPointer(eventData);
        }

        /// <inheritdoc />
        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_pressed) return;

            _pressed = false;
            _dragging = false;
            Commit();
        }

        /// <inheritdoc />
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!_interactable) return;

            _dragging = true;
            _pressed = true;
            SetFromPointer(eventData);
        }

        /// <inheritdoc />
        public void OnDrag(PointerEventData eventData)
        {
            if (!_interactable || !_pressed) return;

            SetFromPointer(eventData);
        }

        /// <inheritdoc />
        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;

            _dragging = false;
            _pressed = false;
            Commit();
        }

        private void Commit()
        {
            Action<float, bool> handler = ValueChanged;
            if (handler != null) handler(_value, true);
        }

        private void SetFromPointer(PointerEventData eventData)
        {
            if (_track == null || _track.rect.width <= 0f) return;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _track, eventData.position, eventData.pressEventCamera, out local))
            {
                return;
            }

            float from = _track.pivot.x * -_track.rect.width;
            float t = Mathf.Clamp01((local.x - from) / _track.rect.width);
            float wanted = Mathf.Lerp(_minimum, _maximum, t);
            float snapped = Snap(wanted);

            bool changed = !Mathf.Approximately(snapped, _value);
            _value = snapped;
            Draw();

            if (changed)
            {
                Action<float, bool> handler = ValueChanged;
                if (handler != null) handler(_value, false);
            }
        }

        private float Snap(float value)
        {
            float clamped = Mathf.Clamp(value, _minimum, _maximum);
            if (_step <= 0f) return clamped;

            float steps = Mathf.Round((clamped - _minimum) / _step);
            return Mathf.Clamp(_minimum + (steps * _step), _minimum, _maximum);
        }

        private void Draw()
        {
            if (_track == null) return;

            float t = Mathf.Approximately(_maximum, _minimum)
                ? 0f
                : Mathf.Clamp01((_value - _minimum) / (_maximum - _minimum));

            float width = _track.rect.width;
            if (_fill != null)
            {
                _fill.sizeDelta = new Vector2(width * t, 0f);
            }

            if (_knob != null)
            {
                _knob.anchoredPosition = new Vector2(width * t, 0f);
            }
        }
    }
}
