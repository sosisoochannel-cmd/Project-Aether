using Aether.Core.Progression;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Storage;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// Chapters: the four regions the story has, and the state of each one in the stored run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Built from the catalogue, not from a layout.</b> Every row here is a
    /// <see cref="ChapterDefinition"/>: its name, its subtitle, its paragraph and whether it is in
    /// the build all come from the one list the gameplay layer also reads. A chapter that is added to
    /// the story appears on this screen the same commit, and a chapter whose level does not exist
    /// cannot be started from it.
    /// </para>
    /// <para>
    /// <b>Four states, all of them read.</b> Completed, in progress, playable and not in this build —
    /// the first two from the stored run's world flags and meta, the last two from the catalogue. A
    /// region that is not in the build says so on its own row and shows its own paragraph, because
    /// the story is written even where the level is not, and hiding that would be pretending the
    /// roadmap does not exist.
    /// </para>
    /// </remarks>
    public sealed class ChaptersScreen : MenuScreen
    {
        private MenuHeader _header;
        private ScrollRect _scroll;
        private RectTransform _content;
        private MenuEntryPanel _regions;
        private MenuButton[] _rows;
        private Text _body;
        private Text _lockedNote;

        /// <summary>The rows, in catalogue order, for a test to read and press.</summary>
        public MenuButton[] Rows
        {
            get { return _rows; }
        }

        /// <inheritdoc />
        protected override void Construct()
        {
            EnsureDialog();

            _header = MenuHeader.Create(Rect, MenuStrings.Get("chapters.title"), Nav, true);
            _header.Back.Activated = Leave;

            _scroll = MenuUi.CreateScroll("Chapter Scroll", Rect, out _content);
            Nav.SelectionChanged += selected =>
            {
                if (selected != null && selected.transform.IsChildOf(_content))
                    MenuUi.ScrollIntoView(_scroll, selected);
            };

            _regions = MenuEntryPanel.Create("Regions", _content, Nav, "chapters.region", 1);

            _rows = new MenuButton[ChapterCatalog.All.Length];
            for (int i = 0; i < ChapterCatalog.All.Length; i++)
            {
                ChapterDefinition chapter = ChapterCatalog.All[i];

                MenuButton row = _regions.AddEntry(chapter.Id, MenuStrings.Get(chapter.TitleKey),
                                                   i == 0 ? MenuButton.Weight.Primary
                                                          : MenuButton.Weight.Secondary);
                row.SetHelp(MenuStrings.Get(chapter.BodyKey));

                if (chapter.Playable)
                {
                    ChapterDefinition captured = chapter;
                    row.Activated = () => PlayChapter(captured);
                }
                else
                {
                    // Locked, and with the reason on the row rather than on a tooltip: the level is
                    // not in the build, which is a fact about this build and not about the player.
                    row.SetLocked(true, MenuStrings.Get("chapters.notInBuild"));
                }

                _rows[i] = row;
            }

            _body = MenuUi.CreateText("Body", _content, MenuStrings.Get("chapters.body"),
                                      MenuTheme.Metrics.SettingHelpSize, MenuTheme.Palette.InkMuted,
                                      TextAnchor.UpperLeft);
            _body.rectTransform.anchorMin = new Vector2(0f, 1f);
            _body.rectTransform.anchorMax = new Vector2(0f, 1f);
            _body.rectTransform.pivot = new Vector2(0f, 1f);

            _lockedNote = MenuUi.CreateTrackedText("Note", _content, MenuStrings.Get("chapters.notInBuild.help"),
                                                   MenuTheme.Metrics.SectionLabelSize,
                                                   MenuTheme.Palette.InkFaint,
                                                   MenuTheme.Metrics.SectionLabelTracking);
            _lockedNote.rectTransform.anchorMin = new Vector2(0f, 1f);
            _lockedNote.rectTransform.anchorMax = new Vector2(0f, 1f);
            _lockedNote.rectTransform.pivot = new Vector2(0f, 1f);
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            float top = MenuTheme.Metrics.ScreenHeaderPitch + MenuTheme.Metrics.ScreenHeaderGap;
            float viewportHeight = Mathf.Max(120f, height - top);
            RectTransform scrollRect = _scroll.GetComponent<RectTransform>();
            MenuUi.Corner(scrollRect, new Vector2(0f, 1f), new Vector2(0f, -top),
                          new Vector2(width, viewportHeight), new Vector2(0f, 1f));

            float column = Mathf.Min(width, width * MenuTheme.Metrics.LeftColumnFraction * 1.6f);
            _regions.Rect.anchoredPosition = Vector2.zero;
            _regions.Layout(column, viewportHeight);

            float bodyTop = _regions.Height + MenuTheme.Metrics.ClusterGap;
            _body.rectTransform.sizeDelta = new Vector2(column, 90f);
            _body.rectTransform.anchoredPosition = new Vector2(0f, -bodyTop);

            _lockedNote.rectTransform.sizeDelta = new Vector2(column, MenuTheme.Metrics.SectionLabelHeight);
            _lockedNote.rectTransform.anchoredPosition = new Vector2(0f, -(bodyTop + 96f));
            MenuUi.SetContentHeight(_content, bodyTop + 96f + MenuTheme.Metrics.SectionLabelHeight
                                              + MenuTheme.Metrics.ParagraphBlockPadding);
        }

        /// <inheritdoc />
        public override bool OnBack()
        {
            if (DialogOpen)
            {
                // A question outranks the screen's own back, and closing it destroys nothing.
                Dialog.Cancel();
                return true;
            }

            // Otherwise the menu decides, and it knows whether this screen was opened from the main
            // menu or from the save list. Calling Host.Back() from here would re-enter this method.
            return false;
        }

        /// <inheritdoc />
        protected override void OnShown(bool instant)
        {
            Refresh();
            _regions.Show(instant);

            Nav.Clear();
            _regions.Register();
            Nav.Add(_header.Back, 0, 1000);
            Nav.SelectFirst();
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            SaveData run = SaveHost.Peek();
            _body.text = MenuStrings.Get("chapters.body");
            _regions.Refresh();

            for (int i = 0; i < _rows.Length; i++)
            {
                ChapterDefinition chapter = ChapterCatalog.All[i];
                MenuButton row = _rows[i];

                if (!chapter.Playable)
                {
                    row.SetMeta(MenuStrings.Get("chapters.notInBuild"));
                    continue;
                }

                bool completed = run != null && run.World.IsSet(ChapterCatalog.CompletionFlagOf(chapter.Id));
                bool current = run != null && run.Meta != null && run.Meta.ChapterId == chapter.Id;

                row.SetMeta(completed
                    ? MenuStrings.Get("chapters.completed")
                    : current ? MenuStrings.Get("chapters.current")
                              : MenuStrings.Get("chapters.play"));
                row.SetAccented(!completed);
            }
        }

        private void Leave()
        {
            // Through the host, and only through it: it knows whether this screen was opened from the
            // main menu or from the save list, and it plays the back cue. A screen that played the
            // cue and then asked the host to go back would play it twice in one frame.
            Host.Back();
        }

        /// <summary>
        /// Opens the chapter: the stored run if it is in this chapter, a new one in a free slot if not.
        /// </summary>
        /// <remarks>
        /// The rule is the main menu's rule, seen from a chapter instead of from a button: CONTINUE
        /// loads the run that is stored, and NEW GAME begins one. A chapter screen that did something
        /// third would be a third way for a run to start, and the first thing to go wrong would be a
        /// player losing a run they did not know was being replaced.
        /// </remarks>
        private void PlayChapter(ChapterDefinition chapter)
        {
            SaveData stored = SaveHost.Peek();
            bool resume = stored != null && stored.Meta != null && stored.Meta.ChapterId == chapter.Id
                          && !stored.World.IsSet(ChapterCatalog.CompletionFlagOf(chapter.Id));

            if (resume)
            {
                int describedSlot = SaveHost.DescribedSlot();
                if (describedSlot > 0 && SaveHost.LoadSlot(describedSlot))
                {
                    MenuAudio.Confirm();
                    if (!MenuFlow.PlayRegion(chapter.LevelPath)) _body.text = MenuStrings.Get("menu.start.failed");
                    return;
                }

                _body.text = MenuStrings.Get("menu.continue.unavailable");
                return;
            }

            int free = SaveSlots.FirstEmpty();
            if (free > 0)
            {
                if (SaveHost.BeginNewGameIn(free, chapter.Id) == null)
                {
                    _body.text = MenuStrings.Get("slots.new.failed");
                    return;
                }

                MenuAudio.Confirm();
                if (!MenuFlow.PlayRegion(chapter.LevelPath)) _body.text = MenuStrings.Get("slots.transition.failed");
                return;
            }

            // All slots are occupied. Name the exact run that would be replaced, including the
            // damaged-save case, and do not start the chapter unless the fresh snapshot was written.
            int replacement = SaveSlots.LeastRecent();
            if (replacement < 1) return;

            SaveSlotInfo replacing = SaveSlots.Describe(replacement);
            string body = replacing.Corrupt
                ? MenuStrings.Format("slots.damaged.replace.body", replacement)
                : replacing.Unavailable
                    ? MenuStrings.Format("slots.unavailable.replace.body", replacement)
                    : MenuStrings.Format("slots.new.body", replacement);

            EnsureDialog().Open(MenuStrings.Get("slots.new.title"), body, () =>
            {
                if (SaveHost.BeginNewGameIn(replacement, chapter.Id, true) == null)
                {
                    _body.text = MenuStrings.Get("slots.new.failed");
                    return;
                }

                if (!MenuFlow.PlayRegion(chapter.LevelPath)) _body.text = MenuStrings.Get("slots.transition.failed");
            });
        }
    }
}
