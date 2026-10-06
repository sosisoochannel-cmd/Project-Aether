using Aether.Gameplay.Menus.Components;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// A screen for a system that does not exist yet: an entry point, and an honest sentence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Characters, the collection and achievements are real entries in the main menu and real screens
    /// in the build, and there is nothing behind them yet. The alternative to a screen like this is a
    /// row that does nothing when it is pressed, which teaches a player that the menu is broken; this
    /// teaches them that the feature is coming, in their own language, without pretending.
    /// </para>
    /// <para>
    /// <b>It is one screen, not three.</b> The three differ only by which title and which sentence
    /// they show, and those come from the string table by way of the screen's own id, so making one of
    /// them real means replacing a case in <see cref="MenuScreens.Create"/> — the entry point, the
    /// navigation and the layout do not change.
    /// </para>
    /// </remarks>
    public sealed class PlaceholderScreen : MenuScreen
    {
        /// <summary>Width of the prose column, in reference units. Long lines are hard to read.</summary>
        private const float ProseWidth = 900f;

        private MenuHeader _header;
        private Text _kicker;
        private Text _body;

        /// <inheritdoc />
        protected override void Construct()
        {
            string titleKey = MenuScreens.TitleKey(Id);

            _header = MenuHeader.Create(Rect, MenuStrings.Get(titleKey), Nav, true);
            _header.SetDimmed(true);
            _header.Back.Activated = Leave;

            _kicker = MenuUi.CreateTrackedText("Kicker", Rect, MenuStrings.Get("locked.title"),
                                               MenuTheme.Metrics.SectionLabelSize,
                                               MenuTheme.Palette.Accent,
                                               MenuTheme.Metrics.SectionLabelTracking);
            _kicker.rectTransform.anchorMin = new Vector2(0f, 1f);
            _kicker.rectTransform.anchorMax = new Vector2(0f, 1f);
            _kicker.rectTransform.pivot = new Vector2(0f, 1f);

            string bodyKey = MenuScreens.BodyKey(Id);
            _body = MenuUi.CreateParagraph("Body", Rect, MenuStrings.Get(bodyKey),
                                           MenuTheme.Metrics.ParagraphSize,
                                           MenuTheme.Palette.InkMuted,
                                           MenuTheme.Metrics.ParagraphLineHeight);
            _body.rectTransform.anchorMin = new Vector2(0f, 1f);
            _body.rectTransform.anchorMax = new Vector2(0f, 1f);
            _body.rectTransform.pivot = new Vector2(0f, 1f);
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            float top = MenuTheme.Metrics.ScreenHeaderPitch + MenuTheme.Metrics.ScreenHeaderGap;
            float prose = Mathf.Min(width, ProseWidth);

            _kicker.rectTransform.sizeDelta = new Vector2(width, MenuTheme.Metrics.SectionLabelHeight);
            _kicker.rectTransform.anchoredPosition = new Vector2(0f, -top);

            // Measured rather than guessed: the sentence is prose, so its height depends on the width
            // this device gives it and on nothing the theme can know.
            _body.rectTransform.sizeDelta = new Vector2(prose, MenuTheme.Metrics.ParagraphLineHeight);
            _body.rectTransform.anchoredPosition = new Vector2(
                0f, -(top + MenuTheme.Metrics.SectionLabelHeight + MenuTheme.Metrics.SectionLabelGap));

            float needed = _body.preferredHeight;
            _body.rectTransform.sizeDelta = new Vector2(prose, Mathf.Max(MenuTheme.Metrics.ParagraphLineHeight,
                                                                        needed));
        }

        /// <inheritdoc />
        protected override void OnShown(bool instant)
        {
            // One row, and it is the way out: the screen has nothing else to offer.
            Nav.Clear();
            Nav.Add(_header.Back, 0, 1);
            Nav.SelectFirst();
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            bool highContrast = MenuPreferences.HighContrast;
            _header.SetDimmed(true);
            _kicker.color = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Accent, highContrast);
            _body.color = MenuTheme.Palette.WithContrast(MenuTheme.Palette.InkMuted, highContrast);
        }

        /// <inheritdoc />
        public override bool OnBack()
        {
            return false;
        }

        private void Leave()
        {
            MenuAudio.Back();
            Host.Back();
        }
    }
}
