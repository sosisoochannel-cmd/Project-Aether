using Aether.Core.Progression;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// Characters: the cast, and which of them this run has met.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The list is the catalogue and the state is the save file.</b> Every row is a
    /// <see cref="CharacterDefinition"/>, and whether it reads as met comes from what the run
    /// recorded. The two are deliberately separate: the cast exists whether or not anybody has played,
    /// and what a run knows about them is its own business.
    /// </para>
    /// <para>
    /// <b>A locked entry is not a blank row.</b> It shows the name it will have and says it has not
    /// been met — never the paragraph, which would spoil the meeting — and the count at the top says
    /// how many of the cast the run has found. A row that showed a name and nothing else would be a
    /// row a player cannot tell from a bug.
    /// </para>
    /// </remarks>
    public sealed class CharactersScreen : MenuScreen
    {
        private MenuHeader _header;
        private MenuEntryPanel _cast;
        private MenuEntryPanel _summary;
        private MenuButton[] _rows;
        private MenuButton _countRow;

        /// <summary>The cast rows, in catalogue order, for a test to read.</summary>
        public MenuButton[] Rows
        {
            get { return _rows; }
        }

        /// <inheritdoc />
        protected override void Construct()
        {
            _header = MenuHeader.Create(Rect, MenuStrings.Get("characters.title"), Nav, true);
            _header.Back.Activated = Leave;

            _cast = MenuEntryPanel.Create("Cast", Rect, Nav, "characters.title", 1);
            _summary = MenuEntryPanel.Create("Summary", Rect, Nav, null, 1);
            _countRow = _summary.AddEntry("Met", string.Empty, MenuButton.Weight.Secondary);
            _countRow.SetInformational(true);

            _rows = new MenuButton[CharacterCatalog.All.Length];
            for (int i = 0; i < CharacterCatalog.All.Length; i++)
            {
                CharacterDefinition character = CharacterCatalog.All[i];
                MenuButton row = _cast.AddEntry(character.Id, MenuStrings.Get(character.TitleKey),
                                                i == 0 ? MenuButton.Weight.Primary
                                                       : MenuButton.Weight.Secondary);
                row.SetMeta(MenuStrings.Get(character.RoleKey));
                _rows[i] = row;
            }
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            float top = MenuTheme.Metrics.ScreenHeaderPitch + MenuTheme.Metrics.ScreenHeaderGap;
            float box = Mathf.Max(120f, height - top);
            float column = Mathf.Min(width, width * MenuTheme.Metrics.LeftColumnFraction * 1.7f);

            _cast.Rect.anchoredPosition = new Vector2(0f, -top);
            _cast.Layout(column, box);

            _summary.Rect.anchoredPosition =
                new Vector2(0f, -(top + _cast.Height + MenuTheme.Metrics.ClusterGap));
            _summary.Layout(column, box);
        }

        /// <inheritdoc />
        protected override void OnShown(bool instant)
        {
            Refresh();

            _cast.Show(instant);
            _summary.Show(instant, MenuTheme.Motion.EntranceStagger * 2f);

            Nav.Clear();
            _cast.Register();
            _summary.Register();
            Nav.Add(_header.Back, 0, 1000);
            Nav.SelectFirst();
        }

        /// <inheritdoc />
        public override bool OnBack()
        {
            // Answered here, and answered with a no: the menu knows where this screen was opened
            // from and it is the one that should decide, so back falls through to it. A screen that
            // called the host's own Back from inside OnBack would be re-entering the method that
            // asked it — which is a stack overflow, not navigation.
            return false;
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            SaveData run = SaveHost.Peek();
            _cast.Refresh();
            _summary.Refresh();

            for (int i = 0; i < _rows.Length; i++)
            {
                CharacterDefinition character = CharacterCatalog.All[i];
                MenuButton row = _rows[i];

                // The rule for what counts as having met someone lives in the catalogue, next to
                // the character it is about. Asking it here means the screen and the achievement that
                // reads the same rule can never disagree about who the player has met — and the count
                // below asks the same catalogue, so the rows and the total cannot part company either.
                bool known = CharacterCatalog.IsMet(run, character);

                row.SetLocked(!known, MenuStrings.Get("characters.unknown"));
                row.SetHelp(known ? MenuStrings.Get(character.BodyKey) : null);
                row.SetMeta(known ? MenuStrings.Get("characters.metBadge")
                                  : MenuStrings.Get("characters.unknown"));
            }

            // The count is over the cast that exists, and the line says so: a total that counted
            // characters this build does not have would be a number the player could never reach.
            _countRow.SetLabel(MenuStrings.Format("characters.met", CharacterCatalog.MetCount(run),
                                                 CharacterCatalog.All.Length));
            _countRow.SetMeta(MenuStrings.Get("characters.body"));
        }

        private void Leave()
        {
            // The host's own Back: it decides where back goes and it plays the cue, once.
            Host.Back();
        }
    }
}
