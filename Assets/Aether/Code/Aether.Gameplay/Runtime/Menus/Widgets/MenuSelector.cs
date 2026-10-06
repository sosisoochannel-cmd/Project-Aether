using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Widgets
{
    /// <summary>
    /// A value with a caret on either side: how a row shows a choice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Display only, like the switch and the slider. The arrows are drawn to say "this can be
    /// stepped" — a finger on the row changes it, and so do the left and right keys — but they are
    /// not separate touch targets. Two 18-unit arrows beside a 168-unit row would be the classic
    /// mobile settings mistake: a control that is easy to see and hard to hit.
    /// </para>
    /// </remarks>
    public sealed class MenuSelector : MonoBehaviour
    {
        private Image _left;
        private Image _right;
        private Text _readout;

        /// <summary>Builds a selector inside a row's control area.</summary>
        public static MenuSelector Create(string name, Transform parent)
        {
            MenuArt.EnsureBuilt();

            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var rect = (RectTransform)host.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);

            float span = MenuTheme.Metrics.SelectorArrowGap;
            rect.sizeDelta = new Vector2(span + 320f, MenuTheme.Metrics.SettingRowHeight);

            var widget = host.AddComponent<MenuSelector>();

            widget._left = Arrow("Previous", rect, true, 0f);
            widget._right = Arrow("Next", rect, false, span);

            widget._readout = MenuUi.CreateText("Value", rect, string.Empty,
                                                MenuTheme.Metrics.SettingLabelSize,
                                                MenuTheme.Palette.Ink, TextAnchor.MiddleLeft);
            widget._readout.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            widget._readout.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            widget._readout.rectTransform.pivot = new Vector2(0f, 0.5f);
            widget._readout.rectTransform.sizeDelta = new Vector2(300f, MenuTheme.Metrics.SettingRowHeight);
            widget._readout.rectTransform.anchoredPosition = new Vector2(46f, 0f);

            return widget;
        }

        /// <summary>Shows the value's name.</summary>
        public void SetValue(string text)
        {
            if (_readout != null) _readout.text = text;
        }

        /// <summary>
        /// Lights the arrows. A selector on the first or last option greys the arrow that would do
        /// nothing, so a row that cannot be stepped further says so instead of silently ignoring a
        /// press.
        /// </summary>
        public void SetBounds(bool canStepBack, bool canStepForward)
        {
            Color on = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Accent, MenuPreferences.HighContrast);
            Color off = MenuTheme.Palette.Locked;

            if (_left != null) _left.color = canStepBack ? on : off;
            if (_right != null) _right.color = canStepForward ? on : off;
        }

        /// <summary>Greys both arrows, for a row that is not available.</summary>
        public void SetDimmed(bool dimmed)
        {
            if (!dimmed) return;

            if (_left != null) _left.color = MenuTheme.Palette.Locked;
            if (_right != null) _right.color = MenuTheme.Palette.Locked;
            if (_readout != null) _readout.color = MenuTheme.Palette.Locked;
        }

        private static Image Arrow(string name, RectTransform parent, bool pointsLeft, float x)
        {
            Image arrow = MenuUi.CreateImage(name, parent, MenuArt.Triangle, MenuTheme.Palette.Accent);
            arrow.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            arrow.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            arrow.rectTransform.pivot = new Vector2(0f, 0.5f);
            arrow.rectTransform.sizeDelta = new Vector2(MenuTheme.Metrics.SelectorArrowSize,
                                                       MenuTheme.Metrics.SelectorArrowSize);
            arrow.rectTransform.anchoredPosition = new Vector2(x, 0f);
            arrow.rectTransform.localScale = new Vector3(pointsLeft ? -1f : 1f, 1f, 1f);
            return arrow;
        }
    }
}
