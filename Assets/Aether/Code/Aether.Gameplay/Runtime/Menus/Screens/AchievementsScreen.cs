using Aether.Core.Progression;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Progression.Achievements;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// Achievements: the five this build has, and how many the stored run has earned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every row here is reachable in the game as it stands.</b> That is the rule the catalogue is
    /// held to: five achievements, each tied to something the implemented gameplay does — entering
    /// the region, lighting a checkpoint, finding everything the region hides, reaching the north
    /// exit, and doing the last of those without falling. There is no counter on this screen that
    /// cannot move, because there is no achievement behind a system that does not exist.
    /// </para>
    /// <para>
    /// <b>A locked row still says what it wants.</b> The body line is the achievement's own
    /// description — "Light a checkpoint", "Reach the north exit without dying once" — because an
    /// achievement a player cannot read is a checklist item rather than a goal. What is hidden is the
    /// earned line, and the count at the top, which says how far the run has got.
    /// </para>
    /// </remarks>
    public sealed class AchievementsScreen : MenuScreen
    {
        private MenuHeader _header;
        private MenuEntryPanel _list;
        private MenuEntryPanel _summary;
        private MenuButton[] _rows;
        private MenuButton _countRow;

        /// <summary>The achievement rows, in catalogue order, for a test to read.</summary>
        public MenuButton[] Rows
        {
            get { return _rows; }
        }

        /// <inheritdoc />
        protected override void Construct()
        {
            _header = MenuHeader.Create(Rect, MenuStrings.Get("achievements.title"), Nav, true);
            _header.Back.Activated = Leave;

            _list = MenuEntryPanel.Create("Achievements", Rect, Nav, "achievements.title", 1);
            _summary = MenuEntryPanel.Create("Summary", Rect, Nav, null, 1);
            _countRow = _summary.AddEntry("Earned", string.Empty, MenuButton.Weight.Secondary);
            _countRow.SetInformational(true);

            _rows = new MenuButton[AchievementCatalog.Count];
            for (int i = 0; i < AchievementCatalog.All.Length; i++)
            {
                AchievementDefinition achievement = AchievementCatalog.All[i];
                MenuButton row = _list.AddEntry(achievement.Id, MenuStrings.Get(achievement.TitleKey),
                                                i == 0 ? MenuButton.Weight.Primary
                                                       : MenuButton.Weight.Secondary);
                row.SetHelp(MenuStrings.Get(achievement.BodyKey));
                _rows[i] = row;
            }
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            float top = MenuTheme.Metrics.ScreenHeaderPitch + MenuTheme.Metrics.ScreenHeaderGap;
            float box = Mathf.Max(120f, height - top);
            float column = Mathf.Min(width, width * MenuTheme.Metrics.LeftColumnFraction * 1.7f);

            _list.Rect.anchoredPosition = new Vector2(0f, -top);
            _list.Layout(column, box);

            _summary.Rect.anchoredPosition =
                new Vector2(0f, -(top + _list.Height + MenuTheme.Metrics.ClusterGap));
            _summary.Layout(column, box);
        }

        /// <inheritdoc />
        protected override void OnShown(bool instant)
        {
            Refresh();

            _list.Show(instant);
            _summary.Show(instant, MenuTheme.Motion.EntranceStagger * 2f);

            Nav.Clear();
            _list.Register();
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
            _list.Refresh();
            _summary.Refresh();

            int earned = 0;
            for (int i = 0; i < _rows.Length; i++)
            {
                AchievementDefinition achievement = AchievementCatalog.All[i];

                // Earned is a record in the run's own ledger, written by the service that awards it
                // during play. A screen that worked it out from the world state would be a second
                // rule for the same thing, and the two would part company eventually.
                bool unlocked = run != null && run.Achievements.IsUnlocked(achievement.Id);
                if (unlocked) earned++;

                _rows[i].SetLocked(!unlocked, MenuStrings.Get("achievements.locked"));
                _rows[i].SetMeta(MenuStrings.Get(unlocked ? "achievements.earned"
                                                          : "achievements.locked"));
                _rows[i].SetAccented(unlocked);
            }

            _countRow.SetLabel(MenuStrings.Format("achievements.count", earned, AchievementCatalog.Count));
            _countRow.SetMeta(MenuStrings.Get("achievements.body"));
        }

        private void Leave()
        {
            // The host's own Back: it decides where back goes and it plays the cue, once.
            Host.Back();
        }
    }
}
