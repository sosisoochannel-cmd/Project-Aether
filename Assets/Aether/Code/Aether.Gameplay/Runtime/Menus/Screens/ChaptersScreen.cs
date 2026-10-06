using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// Chapters: the regions the game has, and the one of them that can be played.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One region is in the build. It is listed as the thing it is — region one, the greenway, with
    /// the word PLAY beside it — and the screen says plainly that more are not. That is the difference
    /// between a chapter list that is honest about the build and a chapter list with four greyed rows
    /// invented to fill it out.
    /// </para>
    /// <para>
    /// Choosing the region does not load it here: it asks the menu to hand over, which saves, fades
    /// the music and writes the request boot reads. The screen's whole part in starting the game is
    /// one row and one call.
    /// </para>
    /// </remarks>
    public sealed class ChaptersScreen : MenuScreen
    {
        private MenuHeader _header;
        private MenuEntryPanel _regions;
        private Text _locked;

        /// <inheritdoc />
        protected override void Construct()
        {
            _header = MenuHeader.Create(Rect, MenuStrings.Get("chapters.title"), Nav, true);
            _header.Back.Activated = Leave;

            _regions = MenuEntryPanel.Create("Regions", Rect, Nav, "chapters.region", 1);

            MenuButton greenway = _regions.AddEntry("Greenway", MenuStrings.Get("chapters.greenway"),
                                                    MenuButton.Weight.Primary);
            greenway.SetMeta(MenuStrings.Get("chapters.greenway.meta"));
            greenway.SetAccented(true);
            greenway.Activated = Play;

            _locked = MenuUi.CreateTrackedText("Locked", Rect, MenuStrings.Get("chapters.locked"),
                                               MenuTheme.Metrics.SectionLabelSize,
                                               MenuTheme.Palette.InkFaint,
                                               MenuTheme.Metrics.SectionLabelTracking);
            _locked.rectTransform.anchorMin = new Vector2(0f, 1f);
            _locked.rectTransform.anchorMax = new Vector2(0f, 1f);
            _locked.rectTransform.pivot = new Vector2(0f, 1f);
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            float top = MenuTheme.Metrics.ScreenHeaderPitch + MenuTheme.Metrics.ScreenHeaderGap;
            float box = Mathf.Max(120f, height - top);
            float column = Mathf.Min(width, width * MenuTheme.Metrics.LeftColumnFraction * 1.5f);

            _regions.Rect.anchoredPosition = new Vector2(0f, -top);
            _regions.Layout(column, box);

            _locked.rectTransform.sizeDelta = new Vector2(width, MenuTheme.Metrics.SectionLabelHeight);
            _locked.rectTransform.anchoredPosition = new Vector2(
                0f, -(top + _regions.Height + MenuTheme.Metrics.ClusterGap));
        }

        /// <inheritdoc />
        protected override void OnShown(bool instant)
        {
            _regions.Show(instant);

            Nav.Clear();
            _regions.Register();
            Nav.Add(_header.Back, 0, 1000);
            Nav.SelectFirst();
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            _regions.Refresh();
            _locked.color = MenuTheme.Palette.WithContrast(MenuTheme.Palette.InkFaint,
                                                           MenuPreferences.HighContrast);
        }

        /// <inheritdoc />
        public override bool OnBack()
        {
            return false;
        }

        private void Play()
        {
            MenuAudio.Confirm();
            Host.PlayRegion();
        }

        private void Leave()
        {
            MenuAudio.Back();
            Host.Back();
        }
    }
}
