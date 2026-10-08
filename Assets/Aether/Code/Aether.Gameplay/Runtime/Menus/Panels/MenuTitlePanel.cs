using Aether.Gameplay.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Panels
{
    /// <summary>
    /// The brand block: the approved mark, the game's name, the region it stands in, and a rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the composition the brief asks for at the top of the main menu: the approved logo, at
    /// a scale that reads at arm's length, with the title under it in tracked capitals, a short line
    /// saying where the player is, and a single hairline closing the block. Nothing else — no box, no
    /// gradient panel, no glow behind the type.
    /// </para>
    /// <para>
    /// It is a panel like any other so that it takes part in the entrance: the background arrives
    /// first, then this, then the primary entries, then the rest. That order is the brief's order and
    /// it is also the order the eye reads in.
    /// </para>
    /// </remarks>
    public sealed class MenuTitlePanel : MenuPanel
    {
        private MenuLogo _logo;
        private Text _title;
        private Text _subtitle;
        private Image _rule;

        /// <summary>Builds the block with the mark, the game's title and its subtitle.</summary>
        public static MenuTitlePanel Create(string name, Transform parent, MenuNav nav,
                                            string titleKey, string subtitleKey)
        {
            MenuTitlePanel panel = Create<MenuTitlePanel>(name, parent, nav);

            panel._logo = MenuLogo.Create(panel.Rect, MenuTheme.Metrics.LogoHeight);

            panel._title = MenuUi.CreateTrackedText("Title", panel.Rect, MenuStrings.Get(titleKey),
                                                    MenuTheme.Metrics.TitleSize, MenuTheme.Palette.Ink,
                                                    MenuTheme.Metrics.TitleTracking);
            panel._title.fontStyle = FontStyle.Bold;
            panel._title.rectTransform.anchorMin = new Vector2(0f, 1f);
            panel._title.rectTransform.anchorMax = new Vector2(0f, 1f);
            panel._title.rectTransform.pivot = new Vector2(0f, 1f);

            panel._subtitle = MenuUi.CreateTrackedText("Subtitle", panel.Rect,
                                                       MenuStrings.Get(subtitleKey),
                                                       MenuTheme.Metrics.SubtitleSize,
                                                       MenuTheme.Palette.InkMuted,
                                                       MenuTheme.Metrics.SubtitleTracking);
            panel._subtitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            panel._subtitle.rectTransform.anchorMax = new Vector2(0f, 1f);
            panel._subtitle.rectTransform.pivot = new Vector2(0f, 1f);

            panel._rule = MenuUi.CreateImage("Rule", panel.Rect, MenuArt.Solid, MenuTheme.Palette.Line);
            panel._rule.rectTransform.anchorMin = new Vector2(0f, 1f);
            panel._rule.rectTransform.anchorMax = new Vector2(0f, 1f);
            panel._rule.rectTransform.pivot = new Vector2(0f, 1f);

            return panel;
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            Remember(width, height);

            float y = 0f;

            if (_logo != null && _logo.HasMark)
            {
                _logo.Rect.anchoredPosition = new Vector2(0f, -y);
                y += _logo.Rect.sizeDelta.y + MenuTheme.Metrics.LogoGap;
            }

            float titleHeight = MenuTheme.Metrics.TitleSize * 1.2f;
            _title.rectTransform.sizeDelta = new Vector2(width, titleHeight);
            _title.rectTransform.anchoredPosition = new Vector2(0f, -y);
            y += titleHeight;

            _subtitle.rectTransform.sizeDelta = new Vector2(width, MenuTheme.Metrics.SubtitleSize * 1.4f);
            _subtitle.rectTransform.anchoredPosition = new Vector2(0f, -y);
            y += (MenuTheme.Metrics.SubtitleSize * 1.4f) + MenuTheme.Metrics.TitleGapRule;

            _rule.rectTransform.sizeDelta = new Vector2(MenuTheme.Metrics.TitleRuleWidth,
                                                       MenuTheme.Metrics.TitleRuleHeight);
            _rule.rectTransform.anchoredPosition = new Vector2(0f, -y);
            y += MenuTheme.Metrics.TitleRuleHeight;

            SetPanelSize(width, y);
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            if (_logo != null) _logo.Apply();

            bool highContrast = MenuPreferences.HighContrast;
            if (_title != null)
            {
                _title.color = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Ink, highContrast);
            }

            if (_subtitle != null)
            {
                _subtitle.color = MenuTheme.Palette.WithContrast(MenuTheme.Palette.InkMuted,
                                                                 highContrast);
            }

            if (_rule != null) _rule.color = MenuTheme.Palette.Line;
        }

        /// <summary>Replaces the title and the line under it, for a screen that reuses the block.</summary>
        public void SetText(string title, string subtitle)
        {
            if (_title != null) _title.text = MenuUi.Track(title, MenuTheme.Metrics.TitleTracking);
            if (_subtitle != null)
            {
                _subtitle.text = MenuUi.Track(subtitle, MenuTheme.Metrics.SubtitleTracking);
            }
        }
    }
}
