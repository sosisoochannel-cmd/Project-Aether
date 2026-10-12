using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Panels
{
    /// <summary>
    /// The game-title block: AETHER, the region it stands in, and a rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The main menu begins with the game title itself, followed by a short regional subtitle and a
    /// single hairline. Studio branding is kept out of this screen; the Varellon mark belongs to the
    /// separate intro. Nothing else — no box, no gradient panel, no glow behind the type.
    /// </para>
    /// <para>
    /// It is a panel like any other so that it takes part in the entrance: the background arrives
    /// first, then this, then the primary entries, then the rest. That order is the brief's order and
    /// it is also the order the eye reads in.
    /// </para>
    /// </remarks>
    public sealed class MenuTitlePanel : MenuPanel
    {
        private Text _title;
        private Text _subtitle;
        private Image _rule;

        /// <summary>Builds the title, subtitle and closing rule.</summary>
        public static MenuTitlePanel Create(string name, Transform parent, MenuNav nav,
                                            string titleKey, string subtitleKey)
        {
            MenuTitlePanel panel = Create<MenuTitlePanel>(name, parent, nav);

            panel._title = MenuUi.CreateTrackedText("Title", panel.Rect, MenuStrings.Get(titleKey),
                                                    MenuTheme.Metrics.TitleSize, MenuTheme.Palette.Ink,
                                                    MenuTheme.Metrics.TitleTracking);
            panel._title.fontStyle = FontStyle.Bold;
            panel._title.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            panel._title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            panel._title.rectTransform.pivot = new Vector2(0.5f, 1f);
            panel._title.alignment = TextAnchor.UpperCenter;

            if (!string.IsNullOrEmpty(subtitleKey))
            {
                panel._subtitle = MenuUi.CreateTrackedText("Subtitle", panel.Rect,
                                                           MenuStrings.Get(subtitleKey),
                                                           MenuTheme.Metrics.SubtitleSize,
                                                           MenuTheme.Palette.InkMuted,
                                                           MenuTheme.Metrics.SubtitleTracking);
                panel._subtitle.rectTransform.anchorMin = new Vector2(0f, 1f);
                panel._subtitle.rectTransform.anchorMax = new Vector2(0f, 1f);
                panel._subtitle.rectTransform.pivot = new Vector2(0f, 1f);
            }

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

            float titleHeight = MenuTheme.Metrics.TitleSize * 1.2f;
            const float horizontalInset = 24f;
            _title.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, width - horizontalInset * 2f), titleHeight);
            _title.rectTransform.anchoredPosition = new Vector2(0f, -y);
            _title.alignment = TextAnchor.UpperCenter;
            _title.resizeTextForBestFit = true;
            _title.resizeTextMinSize = 28;
            _title.resizeTextMaxSize = _title.fontSize;
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.verticalOverflow = VerticalWrapMode.Truncate;
            y += titleHeight;

            if (_subtitle != null)
            {
                _subtitle.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, width - horizontalInset * 2f),
                                                                 MenuTheme.Metrics.SubtitleSize * 1.4f);
                _subtitle.rectTransform.anchoredPosition = new Vector2(horizontalInset, -y);
                _subtitle.resizeTextForBestFit = true;
                _subtitle.resizeTextMinSize = 14;
                _subtitle.resizeTextMaxSize = _subtitle.fontSize;
                _subtitle.horizontalOverflow = HorizontalWrapMode.Wrap;
                _subtitle.verticalOverflow = VerticalWrapMode.Truncate;
                y += (MenuTheme.Metrics.SubtitleSize * 1.4f) + MenuTheme.Metrics.TitleGapRule;
            }
            else
            {
                y += MenuTheme.Metrics.TitleGapRule;
            }

            _rule.rectTransform.sizeDelta = new Vector2(MenuTheme.Metrics.TitleRuleWidth,
                                                       MenuTheme.Metrics.TitleRuleHeight);
            _rule.rectTransform.anchoredPosition = new Vector2(
                Mathf.Max(0f, (width - MenuTheme.Metrics.TitleRuleWidth) * 0.5f), -y);
            y += MenuTheme.Metrics.TitleRuleHeight;

            SetPanelSize(width, y);
        }

        /// <inheritdoc />
        public override void Refresh()
        {
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
