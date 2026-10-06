using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Widgets
{
    /// <summary>
    /// A switch: a track, a knob, and nothing to press.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The switch does not handle input. The row it sits in does — the whole row is the target, so a
    /// thumb on the label and a thumb on the switch do the same thing, and there is only one
    /// selectable per row for the navigation to walk. That is the opposite of how a desktop settings
    /// screen is usually built, and it is the right way round for a phone.
    /// </para>
    /// <para>
    /// The knob moves in one frame rather than sliding, because a control that animates its own
    /// state needs an <c>Update</c> while it does, and a switch that is still moving when the finger
    /// has gone is a switch the player is unsure about. The colour changes fade; the position does
    /// not.
    /// </para>
    /// </remarks>
    public sealed class MenuSwitch : MonoBehaviour
    {
        private Image _track;
        private Image _knob;
        private Text _state;
        private RectTransform _knobRect;
        private bool _on;

        /// <summary>Builds a switch inside a row's control area.</summary>
        public static MenuSwitch Create(string name, Transform parent)
        {
            MenuArt.EnsureBuilt();

            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var rect = (RectTransform)host.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(MenuTheme.Metrics.SwitchWidth + 120f,
                                        MenuTheme.Metrics.SwitchHeight);

            var widget = host.AddComponent<MenuSwitch>();

            widget._track = MenuUi.CreateImage("Track", rect, MenuArt.Solid, MenuTheme.Palette.Line);
            widget._track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            widget._track.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            widget._track.rectTransform.pivot = new Vector2(0f, 0.5f);
            widget._track.rectTransform.sizeDelta = new Vector2(MenuTheme.Metrics.SwitchWidth,
                                                               MenuTheme.Metrics.SwitchHeight);
            widget._track.rectTransform.anchoredPosition = Vector2.zero;

            widget._knobRect = MenuUi.CreateNode("Knob", rect);
            widget._knobRect.anchorMin = new Vector2(0f, 0.5f);
            widget._knobRect.anchorMax = new Vector2(0f, 0.5f);
            widget._knobRect.pivot = new Vector2(0.5f, 0.5f);
            widget._knobRect.sizeDelta = new Vector2(MenuTheme.Metrics.SwitchHeight, MenuTheme.Metrics.SwitchHeight);
            widget._knob = widget._knobRect.gameObject.AddComponent<Image>();
            widget._knob.sprite = MenuArt.Solid;
            widget._knob.raycastTarget = false;
            widget._knob.color = MenuTheme.Palette.Ink;

            widget._state = MenuUi.CreateText("State", rect, string.Empty,
                                              MenuTheme.Metrics.SettingHelpSize,
                                              MenuTheme.Palette.InkFaint, TextAnchor.MiddleLeft);
            widget._state.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            widget._state.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            widget._state.rectTransform.pivot = new Vector2(0f, 0.5f);
            widget._state.rectTransform.sizeDelta = new Vector2(110f, MenuTheme.Metrics.SwitchHeight);
            widget._state.rectTransform.anchoredPosition = new Vector2(MenuTheme.Metrics.SwitchWidth + 18f, 0f);

            widget.Set(false, true);
            return widget;
        }

        /// <summary>Whether the switch is on.</summary>
        public bool IsOn
        {
            get { return _on; }
        }

        /// <summary>
        /// Draws the switch on or off.
        /// </summary>
        /// <param name="on">The state to show.</param>
        /// <param name="instant">True while a screen is being built, so nothing fades in.</param>
        public void Set(bool on, bool instant = false)
        {
            _on = on;
            if (_track == null) return;

            float fade = instant ? 0f : MenuTheme.Motion.HighlightSeconds;

            CrossFade(_track, on ? MenuTheme.Palette.Accent : MenuTheme.Palette.Line, fade);
            CrossFade(_knob, on ? MenuTheme.Palette.Ground : MenuTheme.Palette.InkMuted, fade);

            float inset = MenuTheme.Metrics.SwitchKnobInset;
            float travel = MenuTheme.Metrics.SwitchWidth - MenuTheme.Metrics.SwitchHeight;
            _knobRect.anchoredPosition = new Vector2(
                on ? inset + travel : inset,
                0f);

            _state.text = MenuStrings.Get(on ? "common.on" : "common.off");
            _state.color = on
                ? MenuTheme.Palette.WithContrast(MenuTheme.Palette.InkMuted, MenuPreferences.HighContrast)
                : MenuTheme.Palette.InkFaint;
        }

        private static void CrossFade(Graphic graphic, Color colour, float duration)
        {
            if (graphic == null || !graphic.isActiveAndEnabled) return;
            if (duration <= 0f) graphic.color = colour;
            else graphic.CrossFadeColor(colour, duration, true, true);
        }
    }
}
