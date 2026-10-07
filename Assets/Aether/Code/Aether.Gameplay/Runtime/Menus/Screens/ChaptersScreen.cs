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

            _regions = MenuEntryPanel.Create("Regions", Rect, Nav, "chapters.region", 1);

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

            _body = MenuUi.CreateText("Body", Rect, MenuStrings.Get("chapters.body"),
                                      MenuTheme.Metrics.SettingHelpSize, MenuTheme.Palette.InkMuted,
                                      TextAnchor.UpperLeft);
            _body.rectTransform.anchorMin = new Vector2(0f, 1f);
            _body.rectTransform.anchorMax = new Vector2(0f, 1f);
            _body.rectTransform.pivot = new Vector2(0f, 1f);

            _lockedNote = MenuUi.CreateTrackedText("Note", Rect, MenuStrings.Get("chapters.notInBuild.help"),
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
            float box = Mathf.Max(120f, height - top);
            float column = Mathf.Min(width, width * MenuTheme.Metrics.LeftColumnFraction * 1.6f);

            _regions.Rect.anchoredPosition = new Vector2(0f, -top);
            _regions.Layout(column, box);

            float bodyTop = top + _regions.Height + MenuTheme.Metrics.ClusterGap;
            _body.rectTransform.sizeDelta = new Vector2(column, 90f);
            _body.rectTransform.anchoredPosition = new Vector2(0f, -bodyTop);

            _lockedNote.rectTransform.sizeDelta = new Vector2(column, MenuTheme.Metrics.SectionLabelHeight);
            _lockedNote.rectTransform.anchoredPosition = new Vector2(0f, -(bodyTop + 96f));
        }

        /// <inheritdoc />
        public override bool OnBack()
        {
            if (DialogOpen)
            {
                Dialog.Cancel();
                return true;
            }

            Leave();
            return true;
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
            // Through the host, not straight to the main menu: chapters are reachable from the main
            // menu and from the save screen, and back means back.
            MenuAudio.Back();
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

            if (resume && SaveHost.ContinueMostRecent())
            {
                MenuAudio.Confirm();
                Host.PlayRegion();
                return;
            }

            int free = SaveSlots.FirstEmpty();
            if (free > 0)
            {
                MenuAudio.Confirm();
                SaveHost.BeginNewGameIn(free, chapter.Id);
                Host.PlayRegion();
                return;
            }

            // Every slot is in use, so starting this chapter means replacing a run. That is the main
            // menu's question, and it is asked here in the same words rather than with a new one.
            EnsureDialog().Open(MenuStrings.Get("menu.newGame"), MenuStrings.Get("menu.newGame.confirm"), () =>
            {
                SaveHost.BeginNewGameIn(SaveSlots.LeastRecent(), chapter.Id);
                Host.PlayRegion();
            });
        }
    }
}
