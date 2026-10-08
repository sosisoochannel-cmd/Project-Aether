using Aether.Core.Settings;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Presentation;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Settings;
using UnityEngine;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// Settings: the ten categories, and the rows of whichever one is open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two levels, one screen.</b> The category list and a category's rows are two panels and never
    /// both visible: the header changes from SETTINGS to the category's name, the back row steps out
    /// of the category rather than out of the screen, and the navigation is rebuilt for whichever of
    /// the two is in front. That is what makes a ten-category settings screen cost two presses to
    /// reach any row without a stack of screens to maintain or a second place to look for one.
    /// </para>
    /// <para>
    /// <b>The rows belong to the catalogue.</b> Nothing here decides what a setting is called, what it
    /// does, or where its value goes; this screen places the category buttons, asks the right panel
    /// for the right category, and answers the three requests a category can make that are not
    /// settings: delete the stored run, open the credits, close the game.
    /// </para>
    /// <para>
    /// <b>A destructive row asks first.</b> Deleting a run is confirmed through the same dialog the
    /// main menu uses, and the dialog never starts on the destructive answer.
    /// </para>
    /// </remarks>
    public sealed class SettingsScreen : MenuScreen
    {
        /// <summary>Where the second column of the category list starts, below this width.</summary>
        private const float MinimumCategoryCell = 300f;

        private MenuHeader _header;
        private MenuSettingsPanel _categories;
        private MenuCategoryPanel _rows;
        private MenuConfirmPanel _confirm;
        private MenuNav _dialogNav;
        private bool _inCategory;
        private float _width;
        private float _height;

        /// <summary>The category list, for a test that drives the screen.</summary>
        public MenuSettingsPanel Categories
        {
            get { return _categories; }
        }

        /// <summary>The rows of whichever category is open.</summary>
        public MenuCategoryPanel Rows
        {
            get { return _rows; }
        }

        /// <inheritdoc />
        public override MenuNav ActiveNav
        {
            get { return _confirm != null && _confirm.IsOpen ? _dialogNav : Nav; }
        }

        /// <summary>How many categories are listed before a category is opened. For the gate and tests.</summary>
        public static int CategoryCount
        {
            get { return MenuSettingsPanel.Categories.Length; }
        }

        /// <inheritdoc />
        protected override void Construct()
        {
            _header = MenuHeader.Create(Rect, MenuStrings.Get("settings.title"), Nav, true);
            _header.Back.Activated = StepOut;

            _categories = MenuSettingsPanel.Create("Categories", Rect, Nav, 1);
            _categories.Chosen = OpenCategory;

            _rows = MenuCategoryPanel.Create("Category", Rect, Nav);
            _rows.Requested = OnRequested;

            _dialogNav = new MenuNav();
            _confirm = MenuConfirmPanel.Create("Confirm", Host.DialogRoot, _dialogNav);
            _confirm.Closed = OnDialogClosed;
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            _width = width;
            _height = height;

            // The header is placed by its own anchors: full width, pinned to the top of the screen.
            float top = MenuTheme.Metrics.ScreenHeaderPitch + MenuTheme.Metrics.ScreenHeaderGap;
            float box = Mathf.Max(120f, height - top);

            _categories.SetColumns(width / 2f >= MinimumCategoryCell ? 2 : 1);
            _categories.Rect.anchoredPosition = new Vector2(0f, -top);
            _categories.Layout(width, box);

            // A category's rows are a list of labels against controls, so they take the full width
            // whatever the category list does.
            _rows.Rect.anchoredPosition = new Vector2(0f, -top);
            _rows.Layout(width, box);
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            _categories.Refresh();
            if (_inCategory) _rows.Refresh();
        }

        /// <inheritdoc />
        protected override void OnShown(bool instant)
        {
            if (!_inCategory) ShowCategories(instant);
            else ShowRows(instant);
        }

        /// <inheritdoc />
        public override bool OnMove(MenuNav.Move move)
        {
            if (_confirm != null && _confirm.IsOpen) return _confirm.OnMove(move);

            return _inCategory && _rows != null && _rows.OnMove(move);
        }

        /// <inheritdoc />
        public override bool OnBack()
        {
            if (_confirm != null && _confirm.IsOpen)
            {
                _confirm.Cancel();
                return true;
            }

            if (!_inCategory) return false;   // the main menu is the screen behind this one

            StepOut();
            return true;
        }

        /// <summary>Opens a category's rows.</summary>
        public void OpenCategory(SettingCategory category)
        {
            _inCategory = true;
            _rows.ShowCategory(category);
            _header.SetTitle(MenuStrings.Get(MenuSettingsPanel.CategoryLabelKey(category)));
            _header.SetDimmed(false);

            _categories.Hide();
            _rows.Show(false);
            RegisterNav();
        }

        /// <summary>Goes back to the list of categories, or out of the screen if already there.</summary>
        private void StepOut()
        {
            if (!_inCategory)
            {
                // The screen's own back row and the system back gesture end up in the same place:
                // the host decides, and the host remembers where the player came from.
                Host.Back();
                return;
            }

            MenuAudio.Back();
            _inCategory = false;
            ShowCategories(false);
        }

        private void ShowCategories(bool instant)
        {
            _rows.Hide();
            _header.SetTitle(MenuStrings.Get("settings.title"));
            _header.SetDimmed(false);
            _categories.Show(instant);
            RegisterNav();
        }

        private void ShowRows(bool instant)
        {
            _categories.Hide();
            _rows.Show(instant);
            RegisterNav();
        }

        /// <summary>
        /// Rebuilds what is selectable.
        /// </summary>
        /// <remarks>
        /// The back row is registered last and numbered past the content, so a downward walk ends on
        /// the way out of the screen instead of somewhere in the middle of it.
        /// </remarks>
        private void RegisterNav()
        {
            Nav.Clear();

            if (_inCategory) _rows.Register();
            else _categories.Register();

            Nav.Add(_header.Back, 0, 1000);
            Nav.SelectFirst();
        }

        /// <summary>
        /// Answers what a category's rows ask for when they are not settings.
        /// </summary>
        private void OnRequested(string request)
        {
            switch (request)
            {
                case "settings.reset":
                    int slot = SaveHost.DescribedSlot();
                    if (slot < 1) break;

                    _confirm.Open(MenuStrings.Get("slots.delete.title"),
                                  MenuStrings.Format("slots.delete.body", slot),
                                  () => DeleteRun(slot));
                    break;

                case "settings.credits":
                    MenuAudio.Confirm();
                    Host.GoTo(MenuScreenId.Credits);
                    break;

                case "settings.quit":
                    MenuAudio.Confirm();
                    if (!MenuFlow.Quit()) _rows.ShowQuitFailure();
                    break;
            }
        }

        private void DeleteRun(int slot)
        {
            // The slot is captured when the question opens, not looked up after it closes: the thing
            // the player confirmed must be the thing that is deleted, and an unset slot is never
            // clamped into slot one by a destructive helper.
            if (!SaveHost.DeleteSlot(slot))
            {
                Debug.LogError($"[settings] The stored run in slot {slot} could not be deleted.");
                _rows.Refresh();
                _rows.ShowDeleteFailure();
                return;
            }

            _rows.Refresh();
        }

        private void OnDialogClosed()
        {
            if (_inCategory) Nav.Select(_header.Back, true);
            else Nav.SelectFirst();
        }
    }
}
