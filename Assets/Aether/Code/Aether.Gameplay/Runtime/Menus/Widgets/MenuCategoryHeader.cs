using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Widgets
{
    /// <summary>
    /// The line that opens a group of settings: a tracked label and a rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A settings list of thirty rows in one column is unreadable, and a list of thirty rows inside
    /// ten rounded boxes is worse. A label with a hairline running out of it does the job with two
    /// thin shapes: the eye finds the group, the rule says where it starts and ends, and there is no
    /// fill to look at when the group is not the one being read.
    /// </para>
    /// <para>
    /// The label is tracked, because the engine's built-in font has no letter-spacing and small
    /// capitals are most of what separates a designed interface from a form.
    /// </para>
    /// </remarks>
    public sealed class MenuCategoryHeader : MonoBehaviour
    {
        private Text _label;
        private Image _rule;
        private RectTransform _rect;

        /// <summary>Builds a header. The rule is sized by the panel that places it.</summary>
        public static MenuCategoryHeader Create(string name, Transform parent, string text)
        {
            MenuArt.EnsureBuilt();

            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);

            var header = host.AddComponent<MenuCategoryHeader>();
            header._rect = (RectTransform)host.transform;

            header._label = MenuUi.CreateTrackedText("Label", host.transform, text,
                                                     MenuTheme.Metrics.CategoryLabelSize,
                                                     MenuTheme.Palette.Accent,
                                                     MenuTheme.Metrics.CategoryLabelTracking);
            header._label.rectTransform.anchorMin = new Vector2(0f, 1f);
            header._label.rectTransform.anchorMax = new Vector2(0f, 1f);
            header._label.rectTransform.pivot = new Vector2(0f, 1f);
            header._label.rectTransform.sizeDelta = new Vector2(600f, 34f);
            header._label.rectTransform.anchoredPosition = Vector2.zero;

            header._rule = MenuUi.CreateHairline("Rule", host.transform, MenuTheme.Palette.Line,
                                                 MenuTheme.Metrics.TitleRuleHeight);
            header._rule.rectTransform.anchorMin = new Vector2(0f, 1f);
            header._rule.rectTransform.anchorMax = new Vector2(0f, 1f);
            header._rule.rectTransform.pivot = new Vector2(0f, 1f);
            header._rule.rectTransform.anchoredPosition = new Vector2(0f, -40f);

            return header;
        }

        /// <summary>The header's own rect, for the panel that places it.</summary>
        public RectTransform Rect
        {
            get { return _rect; }
        }

        /// <summary>Sets the group's name.</summary>
        public void SetLabel(string text)
        {
            if (_label != null) _label.text = MenuUi.Track(text, MenuTheme.Metrics.CategoryLabelTracking);
        }

        /// <summary>Gives the rule its width, in units: how far out of the label it runs.</summary>
        public void SetRuleWidth(float width)
        {
            if (_rule == null) return;
            _rule.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, width),
                                                       MenuTheme.Metrics.TitleRuleHeight);
        }

        /// <summary>Dims the whole header, for a category that has nothing in it yet.</summary>
        public void SetDimmed(bool dimmed)
        {
            if (_label == null) return;

            _label.color = dimmed ? MenuTheme.Palette.InkFaint : MenuTheme.Palette.Accent;
            if (_rule != null) _rule.color = dimmed ? MenuTheme.Palette.Locked : MenuTheme.Palette.Line;
        }
    }
}
