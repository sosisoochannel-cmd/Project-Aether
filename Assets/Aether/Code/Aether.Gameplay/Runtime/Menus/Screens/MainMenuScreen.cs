using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Storage;
using Aether.Gameplay.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// The main menu: the brand, two primary choices, three secondary destinations, and the version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The composition is deliberately restrained.</b> CONTINUE and NEW GAME are the two large
    /// actions. CHAPTERS, EXPLORE, and SETTINGS sit in one quiet secondary list. Characters,
    /// collection, achievements, and credits live one level deeper, so the first screen offers the
    /// next meaningful action instead of every destination at once.
    /// </para>
    /// <para>
    /// <b>The content scrolls only when it has to.</b> At the standard interface size the whole menu
    /// fits a 1080-unit box — the gate checks that arithmetic — so the scroll view is inert because
    /// its content is shorter than its viewport. If the player scales the interface up, or a
    /// translation makes a label wrap, the same layout scrolls instead of overlapping. Nothing has to
    /// detect which case it is in: a clamped scroll view with nothing to scroll does nothing.
    /// </para>
    /// <para>
    /// <b>What is not here.</b> No invented entries. CONTINUE is only usable when a run is stored,
    /// QUIT is reached through Back or Settings. The secondary Explore screen keeps less frequent
    /// destinations available without making the first screen compete with them.
    /// </para>
    /// </remarks>
    public sealed class MainMenuScreen : MenuScreen
    {
        /// <summary>Height reserved at the bottom of the screen for the version line.</summary>
        private const float FooterHeight = 40f;

        /// <summary>The narrowest a grid cell may be before two columns become one.</summary>
        private const float MinimumSecondaryCell = 300f;

        private ScrollRect _scroll;
        private RectTransform _content;
        private MenuTitlePanel _title;
        private MenuEntryPanel _play;
        private MenuEntryPanel _explore;

        private MenuButton _continue;
        private Text _version;
        private MenuConfirmPanel _confirm;
        private MenuNav _dialogNav;

        /// <summary>The question this screen asks before it destroys something, or closes the game.</summary>
        public MenuConfirmPanel Confirm
        {
            get { return _confirm; }
        }

        /// <summary>Which rows the player can actually reach. Exposed for the gate.</summary>
        public MenuEntryPanel Play
        {
            get { return _play; }
        }

        /// <inheritdoc />
        public override MenuNav ActiveNav
        {
            get { return _confirm != null && _confirm.IsOpen ? _dialogNav : Nav; }
        }

        /// <inheritdoc />
        protected override void Construct()
        {
            _scroll = MenuUi.CreateScroll("Scroll", Rect, out _content);
            Nav.SelectionChanged += selected =>
            {
                if (selected != null && selected.transform.IsChildOf(_content))
                    MenuUi.ScrollIntoView(_scroll, selected);
            };

            // The scroll stops above the footer: the version line is a fact about the build, not part
            // of the list, and it should not scroll away when the list is long.
            MenuUi.Stretch(_scroll.GetComponent<RectTransform>(), 0f, 0f, 0f, FooterHeight);

            _title = MenuTitlePanel.Create("Brand", _content, Nav, "menu.title", "menu.subtitle");

            _play = MenuEntryPanel.Create("Play", _content, Nav, "menu.playSection", 1);
            _continue = _play.AddEntry("Continue", MenuStrings.Get("menu.continue"),
                                       MenuButton.Weight.Primary);
            MenuButton newGame = _play.AddEntry("New Game", MenuStrings.Get("menu.newGame"),
                                                MenuButton.Weight.Primary);
            _continue.SetMeta(MenuStrings.Get("menu.continue.meta"));
            newGame.SetMeta(MenuStrings.Get("menu.newGame.meta"));
            _continue.Activated = Continue;
            newGame.Activated = NewGame;

            // One short, unheaded list gives the secondary choices room to breathe.
            // Less-frequent destinations are grouped on Explore rather than competing here.
            _explore = MenuEntryPanel.Create("Destinations", _content, Nav, null, 1);
            AddDestination(_explore, MenuScreenId.Chapters, "menu.chapters");
            AddDestination(_explore, MenuScreenId.Explore, "menu.exploreSection");
            AddDestination(_explore, MenuScreenId.Settings, "menu.settings");

            _version = MenuUi.CreateText("Version", Rect, string.Empty, MenuTheme.Metrics.VersionSize,
                                         MenuTheme.Palette.InkFaint, TextAnchor.LowerRight);
            _version.text = MenuUi.Track(MenuUi.PrepareText(MenuStrings.Format("about.version", Application.version)),
                                         MenuTheme.Metrics.VersionTracking);
            bool rtl = LanguageService.IsRightToLeft;
            _version.rectTransform.anchorMin = new Vector2(rtl ? 0f : 1f, 0f);
            _version.rectTransform.anchorMax = new Vector2(rtl ? 0f : 1f, 0f);
            _version.rectTransform.pivot = new Vector2(rtl ? 0f : 1f, 0f);
            _version.alignment = rtl ? TextAnchor.LowerLeft : TextAnchor.LowerRight;
            _version.rectTransform.anchoredPosition = new Vector2(0f, MenuTheme.Metrics.ScreenMarginBottom * 0.25f);
            _version.rectTransform.sizeDelta = new Vector2(600f, FooterHeight);

            _dialogNav = new MenuNav();
            _confirm = MenuConfirmPanel.Create("Confirm", Host.DialogRoot, _dialogNav);
            _confirm.Closed = OnDialogClosed;
        }

        private void AddDestination(MenuEntryPanel panel, MenuScreenId destination, string labelKey)
        {
            MenuButton row = panel.AddEntry(destination.ToString(), MenuStrings.Get(labelKey),
                                            MenuButton.Weight.Secondary);

            // Caught in a local: the delegate must not close over anything that moves.
            MenuScreenId target = destination;
            row.Activated = () => Request(target);
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            float box = Mathf.Max(160f, height - FooterHeight);
            float titleHeight;

            _title.Layout(width, box);
            _title.Rect.anchoredPosition = Vector2.zero;
            titleHeight = _title.Height;

            float top = titleHeight + MenuTheme.Metrics.TitleGapBody;
            bool compact = width < 1200f;
            bool rtl = LanguageService.IsRightToLeft;
            float leftWidth = compact ? width : width * MenuTheme.Metrics.LeftColumnFraction;
            float rightWidth = compact ? width : width * MenuTheme.Metrics.RightBlockFraction;

            // The grid keeps two columns while a cell can hold a comfortable translated label.
            // On narrower landscape windows the whole secondary block becomes one column; below the
            // compact breakpoint the primary and secondary clusters stack instead of competing for
            // horizontal space. This is especially important for long RTL and German/Russian strings.
            // Keep the secondary list vertical. A short list reads faster than a grid, and each row
            // remains a comfortable touch target across translations and interface scales.
            _explore.SetColumns(1);

            _play.Layout(leftWidth, box);
            _explore.Layout(rightWidth, box);

            float primaryX = compact ? 0f : (rtl ? width - leftWidth : 0f);
            float secondaryX = compact ? 0f : (rtl ? 0f : width - rightWidth);
            _play.Rect.anchoredPosition = new Vector2(primaryX, -top);

            float secondaryTop = compact
                ? top + _play.Height + MenuTheme.Metrics.ClusterGap
                : top;
            _explore.Rect.anchoredPosition = new Vector2(secondaryX, -secondaryTop);

            float bottom = compact
                ? secondaryTop + _explore.Height
                : Mathf.Max(top + _play.Height, secondaryTop + _explore.Height);
            MenuUi.SetContentHeight(_content, bottom + MenuTheme.Metrics.ParagraphBlockPadding);
            _content.anchoredPosition = Vector2.zero;
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            SaveSlotInfo[] slots = SaveHost.DescribeSlots();
            bool stored = false;
            bool anythingStored = false;
            for (int i = 0; i < slots.Length; i++)
            {
                stored |= slots[i].Playable;
                anythingStored |= slots[i].HasAnything;
            }

            if (_title != null) _title.Refresh();
            if (_version != null)
                _version.text = MenuUi.Track(MenuUi.PrepareText(
                    MenuStrings.Format("about.version", Application.version)),
                    MenuTheme.Metrics.VersionTracking);

            // CONTINUE is a real row only when there is something this build can load. A corrupt or
            // newer-build file is not called an empty slot: it stays visible in the slot manager.
            _continue.SetMeta(MenuStrings.Get("menu.continue.meta"));
            _continue.SetLocked(!stored, MenuStrings.Get(anythingStored
                ? "menu.continue.unavailable"
                : "menu.continue.none"));
            _continue.SetAccented(stored);

            // The clusters redraw themselves in place: a change of contrast has to reach the rows
            // of every cluster, not only the one under the player's finger.
            _play.Refresh();
            _explore.Refresh();
        }

        /// <inheritdoc />
        protected override void OnShown(bool instant)
        {
            Refresh();

            _title.Show(instant, 0f);
            _play.Show(instant, MenuTheme.Motion.EntranceStagger);
            _explore.Show(instant, MenuTheme.Motion.EntranceStagger * 2f);

            // The order mirrors the visual hierarchy: primary actions first, then destinations.
            // Rebuilt on every show, never on a refresh.
            Nav.Clear();
            _play.Register();
            _explore.Register();
            Nav.SelectFirst();
        }

        /// <inheritdoc />
        public override bool OnMove(MenuNav.Move move)
        {
            if (_confirm != null && _confirm.IsOpen) return _confirm.OnMove(move);
            return false;
        }

        /// <inheritdoc />
        public override bool OnBack()
        {
            if (_confirm != null && _confirm.IsOpen)
            {
                _confirm.Cancel();
                return true;
            }

            AskToQuit();
            return true;
        }

        // -- actions -------------------------------------------------------------------------

        private void Continue()
        {
            if (!SaveHost.ContinueMostRecent())
            {
                Refresh();
                _continue.SetMeta(MenuStrings.Get("menu.continue.unavailable"));
                return;
            }

            MenuAudio.Confirm();
            if (!Host.PlayRegion())
                _continue.SetMeta(MenuStrings.Get("menu.start.failed"));
        }

        private void NewGame()
        {
            // A new run needs somewhere to live, and there are three slots. NEW GAME opens the slot
            // list instead of picking one: which slot a run goes in is the player's decision, and the
            // question "replace this one?" only makes sense once a slot has been named — the reason
            // the confirm dialog lives on that screen and names the slot it is about.
            MenuAudio.Confirm();
            Request(MenuScreenId.SaveSlots);
        }

        /// <summary>Asks before closing the game: back is a thumb's width from the rows on a phone.</summary>
        private void AskToQuit()
        {
            if (_confirm == null || _confirm.IsOpen) return;

            _confirm.Open(MenuStrings.Get("menu.quit"), MenuStrings.Get("menu.quit.confirm"), Quit);
        }

        private void Quit()
        {
            if (MenuFlow.Quit()) return;

            _version.text = MenuUi.Track(MenuUi.PrepareText(MenuStrings.Get("menu.quit.saveFailed")),
                                         MenuTheme.Metrics.VersionTracking);
        }

        private void Request(MenuScreenId destination)
        {
            MenuAudio.Confirm();
            Host.GoTo(destination);
        }

        private void OnDialogClosed()
        {
            Nav.SelectFirst();
        }
    }
}
