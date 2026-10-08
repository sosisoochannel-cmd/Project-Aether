using UnityEngine;
using Aether.Gameplay.Localization;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Components
{
    /// <summary>
    /// The band at the top of a screen that is not the main menu: a title, a rule, and the way back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every screen after the first one needs the same three things, and they need to be in the same
    /// place on each of them — a title that says where the player is, a rule that separates the title
    /// from the content, and a way back that is always in the same corner of the screen. That
    /// consistency is most of what makes a menu feel finished, and it is the kind of thing that drifts
    /// when each screen builds its own.
    /// </para>
    /// <para>
    /// The back row's hit rect is the full height of the header band, which makes it the largest
    /// target on the screen: the control a player reaches for without looking should be the one they
    /// cannot miss.
    /// </para>
    /// </remarks>
    public sealed class MenuHeader : MonoBehaviour
    {
        private RectTransform _rect;
        private Text _title;
        private Image _rule;
        private MenuButton _back;

        /// <summary>The header's own rect, for the screen that places it.</summary>
        public RectTransform Rect
        {
            get { return _rect; }
        }

        /// <summary>The way back, for the screen to wire up and to reach from the keyboard.</summary>
        public MenuButton Back
        {
            get { return _back; }
        }

        /// <summary>Builds a header across the top of a screen.</summary>
        /// <param name="parent">The screen's root.</param>
        /// <param name="title">Already localised title.</param>
        /// <param name="nav">The screen's navigation; the back row is registered on it.</param>
        /// <param name="showBack">False for a screen with nowhere to go back to.</param>
        public static MenuHeader Create(Transform parent, string title, MenuNav nav, bool showBack = true)
        {
            var host = new GameObject("Header", typeof(RectTransform));
            host.transform.SetParent(parent, false);

            var header = host.AddComponent<MenuHeader>();
            header._rect = (RectTransform)host.transform;
            header._rect.anchorMin = new Vector2(0f, 1f);
            header._rect.anchorMax = new Vector2(1f, 1f);
            header._rect.pivot = new Vector2(0.5f, 1f);
            header._rect.anchoredPosition = Vector2.zero;
            header._rect.sizeDelta = new Vector2(0f, MenuTheme.Metrics.ScreenHeaderPitch);

            header._title = MenuUi.CreateTrackedText("Title", header._rect, title,
                                                     MenuTheme.Metrics.ScreenTitleSize,
                                                     MenuTheme.Palette.Ink,
                                                     MenuTheme.Metrics.ScreenTitleTracking);
            bool rtl = LanguageService.IsRightToLeft;
            header._title.alignment = rtl ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            header._title.rectTransform.anchorMin = new Vector2(rtl ? 1f : 0f, 0f);
            header._title.rectTransform.anchorMax = new Vector2(rtl ? 1f : 0f, 1f);
            header._title.rectTransform.pivot = new Vector2(rtl ? 1f : 0f, 0.5f);
            header._title.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            header._title.rectTransform.sizeDelta = new Vector2(10f, 0f);

            header._rule = MenuUi.CreateHairline("Rule", header._rect, MenuTheme.Palette.Line,
                                                 MenuTheme.Metrics.TitleRuleHeight);
            header._rule.rectTransform.anchorMin = new Vector2(0f, 0f);
            header._rule.rectTransform.anchorMax = new Vector2(1f, 0f);
            header._rule.rectTransform.pivot = new Vector2(0.5f, 0f);
            header._rule.rectTransform.anchoredPosition = Vector2.zero;
            header._rule.rectTransform.sizeDelta = new Vector2(0f, MenuTheme.Metrics.TitleRuleHeight);

            if (!showBack) return header;

            header._back = MenuButton.Create("Back", header._rect, MenuStrings.Get("common.back"),
                                             MenuButton.Weight.Secondary);
            header._back.Nav = nav;
            header._back.Rect.anchorMin = new Vector2(rtl ? 0f : 1f, 1f);
            header._back.Rect.anchorMax = new Vector2(rtl ? 0f : 1f, 1f);
            header._back.Rect.pivot = new Vector2(rtl ? 0f : 1f, 1f);
            header._back.Rect.anchoredPosition = Vector2.zero;
            header._back.Rect.sizeDelta = new Vector2(MenuTheme.Metrics.BackHitWidth,
                                                              MenuTheme.Metrics.ScreenHeaderPitch);
            header._back.ApplyLayout(MenuTheme.Metrics.CaretOutdent + 14f, 14f);
            header._back.SetRuleWidth(MenuTheme.Metrics.BackHitWidth);
            header._back.SetCaretMirrored(!rtl);

            return header;
        }

        /// <summary>Renames the screen, for a header that is reused for a category.</summary>
        public void SetTitle(string title)
        {
            if (_title == null) return;
            _title.text = MenuUi.Track(title, MenuTheme.Metrics.ScreenTitleTracking);
        }

        /// <summary>Dims the title, for a screen that has nothing to show yet.</summary>
        public void SetDimmed(bool dimmed)
        {
            if (_title == null) return;
            _title.color = dimmed ? MenuTheme.Palette.InkMuted : MenuTheme.Palette.Ink;
            if (_rule != null) _rule.color = dimmed ? MenuTheme.Palette.Locked : MenuTheme.Palette.Line;
        }
    }
}
