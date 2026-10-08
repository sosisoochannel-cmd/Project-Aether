using Aether.Core.Settings;
using Aether.Gameplay.Menus.Components;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Panels
{
    /// <summary>
    /// The list of settings categories: ten entries, two columns, no scrolling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a category list and not one long list.</b> Ten categories hold thirty options between
    /// them. As one scrolling list that is seven screens of scrolling before the player reaches
    /// About, on a phone held sideways where only three and a half rows fit. As a list of categories
    /// it is ten rows, all of them on screen at once, and a category's own options — between one and
    /// six — fit in a single view. The brief asks for a scalable settings screen; this is what makes
    /// it scale: adding the eleventh category adds one row, and no screen gets longer.
    /// </para>
    /// <para>
    /// The rows are the same <see cref="MenuButton"/> every other screen uses, so a category row and
    /// a setting row behave identically under a thumb: the whole cell is the target, the caret shows
    /// where the navigation is, and the row answers the press in the frame it happens.
    /// </para>
    /// </remarks>
    public sealed class MenuSettingsPanel : MenuPanel
    {
        /// <summary>Every category the settings screen shows, in the order it shows them.</summary>
        public static readonly SettingCategory[] Categories =
        {
            SettingCategory.Gameplay,
            SettingCategory.Controls,
            SettingCategory.Camera,
            SettingCategory.Graphics,
            SettingCategory.Audio,
            SettingCategory.Display,
            SettingCategory.Accessibility,
            SettingCategory.Language,
            SettingCategory.Data,
            SettingCategory.About,
        };

        private readonly MenuButton[] _rows = new MenuButton[Categories.Length];
        private ScrollRect _scroll;
        private RectTransform _content;
        private int _columns = 2;

        /// <summary>Raised when a category is chosen.</summary>
        public System.Action<SettingCategory> Chosen;

        /// <summary>Creates the category list.</summary>
        public static MenuSettingsPanel Create(string name, Transform parent, MenuNav nav, int columns)
        {
            MenuSettingsPanel panel = Create<MenuSettingsPanel>(name, parent, nav);
            panel._columns = Mathf.Max(1, columns);

            panel._scroll = MenuUi.CreateScroll("Scroll", panel.Rect, out panel._content);
            MenuUi.Stretch(panel._scroll.GetComponent<RectTransform>());
            if (nav != null)
            {
                nav.SelectionChanged += selected =>
                {
                    if (selected != null && selected.transform.IsChildOf(panel._content))
                        MenuUi.ScrollIntoView(panel._scroll, selected);
                };
            }

            for (int i = 0; i < Categories.Length; i++)
            {
                SettingCategory category = Categories[i];
                MenuButton row = MenuButton.Create("Category " + CategoryLabelKey(category), panel._content,
                                                   MenuStrings.Get(CategoryLabelKey(category)),
                                                   MenuButton.Weight.Secondary);
                row.Nav = nav;
                row.SetRuleWidth(260f);

                // Caught in a local: the delegate must not allocate per row, and it must not close
                // over the loop variable.
                SettingCategory captured = category;
                row.Activated = () =>
                {
                    MenuAudio.Confirm();
                    System.Action<SettingCategory> handler = panel.Chosen;
                    if (handler != null) handler(captured);
                };

                panel._rows[i] = row;
            }

            return panel;
        }

        /// <summary>The localisation key for a category's name.</summary>
        public static string CategoryLabelKey(SettingCategory category)
        {
            return "category." + CategoryKey(category);
        }

        /// <summary>The localisation key for a category's note.</summary>
        public static string CategoryNoteKey(SettingCategory category)
        {
            return "note." + CategoryKey(category);
        }

        /// <summary>
        /// A category as a lowercase word: the suffix both of its keys share.
        /// </summary>
        /// <remarks>
        /// Keys are identifiers rather than prose, so deriving them from the enum's own name is
        /// exact rather than clever — and it means a category that is added to the catalogue without
        /// a name and a note fails the gate instead of quietly showing an empty header.
        /// </remarks>
        public static string CategoryKey(SettingCategory category)
        {
            return category.ToString().ToLowerInvariant();
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            Remember(width, height);
            PlaceRows(width);
            SetPanelSize(width, height);
        }

        /// <summary>
        /// Puts one row per cell of the grid, and sizes the scroll's content to the grid.
        /// </summary>
        /// <remarks>
        /// Scrolling is not decoration: the categories have to stay reachable when the interface size
        /// is turned up, a translation makes every label longer, or the screen is short enough that
        /// five bands of rows do not fit. A clamped scroll view with nothing to scroll is inert, so
        /// the same layout serves both cases without anything having to detect which one it is in.
        /// </remarks>
        private void PlaceRows(float width)
        {
            float pitch = MenuTheme.Metrics.SecondaryPitch;
            float cell = width / _columns;
            int bands = Mathf.CeilToInt(_rows.Length / (float)_columns);

            for (int i = 0; i < _rows.Length; i++)
            {
                int band = i / _columns;
                int column = i % _columns;

                RectTransform rect = _rows[i].Rect;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(cell, pitch);
                rect.anchoredPosition = new Vector2(column * cell, -(band * pitch));

                _rows[i].ApplyLayout(MenuTheme.Metrics.CaretOutdent, MenuTheme.Metrics.CaretOutdent);
                _rows[i].SetRuleWidth(cell - MenuTheme.Metrics.CaretOutdent);
            }

            MenuUi.SetContentHeight(_content, bands * pitch);
            _content.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Changes the number of columns.
        /// </summary>
        /// <remarks>
        /// A responsive decision, taken from the width the screen actually has: two columns of
        /// categories need each cell to hold the longest name, and a narrow box is better served by
        /// one column than by two clipped ones.
        /// </remarks>
        public void SetColumns(int columns)
        {
            _columns = Mathf.Max(1, columns);
        }

        /// <inheritdoc />
        public override void Register()
        {
            for (int i = 0; i < _rows.Length; i++)
            {
                Nav.Add(_rows[i], i % _columns, i / _columns);
            }
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            // A category row shows nothing about its contents, so there is no value to re-read.
            // What it does have is a drawing, and a change of contrast has to reach it.
            for (int i = 0; i < _rows.Length; i++) _rows[i].RefreshColors();
        }
    }
}
