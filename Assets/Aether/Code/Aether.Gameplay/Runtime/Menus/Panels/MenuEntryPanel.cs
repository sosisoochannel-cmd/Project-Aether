using System.Collections.Generic;
using Aether.Gameplay.Menus.Components;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Panels
{
    /// <summary>
    /// A cluster of entries under a section label: the menu's basic unit of composition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The main menu is three of these — PLAY, EXPLORE, SYSTEM — and the placeholder screens are one
    /// each. A cluster is a section label and a grid of rows with a fixed number of columns, which is
    /// all the main menu needs and all it should need: a grid of identical rectangles is exactly the
    /// look the brief rules out, and the way to avoid it is to make the clusters genuinely different
    /// — two large rows in one, a four-cell grid in another, a pair in the third — rather than to add
    /// layout options nobody will use.
    /// </para>
    /// <para>
    /// Rows are created once and placed by arithmetic: no layout groups, nothing measured at runtime,
    /// and the same numbers <c>tools/verify/mainmenu.py</c> reads to check the result in dp.
    /// </para>
    /// </remarks>
    public sealed class MenuEntryPanel : MenuPanel
    {
        private readonly List<MenuButton> _rows = new List<MenuButton>(8);

        private RectTransform _label;
        private Text _labelText;
        private Image _sectionRule;
        private int _columns = 1;
        private float _pitch = MenuTheme.Metrics.SecondaryPitch;

        /// <summary>The rows this cluster owns, in reading order.</summary>
        public IList<MenuButton> Rows
        {
            get { return _rows; }
        }

        /// <summary>Creates a cluster with a section label and a column count.</summary>
        /// <param name="name">GameObject name.</param>
        /// <param name="parent">The screen's content root.</param>
        /// <param name="nav">The screen's navigation.</param>
        /// <param name="sectionKey">Localisation key for the section label, or null for none.</param>
        /// <param name="columns">How many rows sit side by side.</param>
        public static MenuEntryPanel Create(string name, Transform parent, MenuNav nav,
                                           string sectionKey, int columns)
        {
            MenuEntryPanel panel = Create<MenuEntryPanel>(name, parent, nav);
            panel._columns = Mathf.Max(1, columns);

            if (!string.IsNullOrEmpty(sectionKey))
            {
                panel._label = panel.Place("Section", 0f, 0f, 600f, MenuTheme.Metrics.SectionLabelHeight);
                panel._labelText = MenuUi.CreateTrackedText("Label", panel._label,
                                                            MenuStrings.Get(sectionKey),
                                                            MenuTheme.Metrics.SectionLabelSize,
                                                            MenuTheme.Palette.Accent,
                                                            MenuTheme.Metrics.SectionLabelTracking);
                MenuUi.Stretch(panel._labelText.rectTransform);
                if (Aether.Gameplay.Localization.LanguageService.IsRightToLeft)
                {
                    panel._label.anchorMin = panel._label.anchorMax = new Vector2(1f, 1f);
                    panel._label.pivot = new Vector2(1f, 1f);
                    panel._labelText.alignment = TextAnchor.MiddleRight;
                }

                panel._sectionRule = MenuUi.CreateHairline("SectionRule", panel.Rect,
                                                           MenuTheme.Palette.Line,
                                                           MenuTheme.Metrics.TitleRuleHeight);
                panel._sectionRule.rectTransform.anchorMin = new Vector2(0f, 1f);
                panel._sectionRule.rectTransform.anchorMax = new Vector2(0f, 1f);
                panel._sectionRule.rectTransform.pivot = new Vector2(0f, 1f);
                panel._sectionRule.rectTransform.anchoredPosition = new Vector2(
                    0f, -(MenuTheme.Metrics.SectionLabelHeight + 4f));
            }

            return panel;
        }

        /// <summary>Adds an entry and returns its row.</summary>
        public MenuButton AddEntry(string name, string label, MenuButton.Weight weight)
        {
            MenuButton row = MenuButton.Create(name, Rect, label, weight);
            row.Nav = Nav;

            if (_rows.Count == 0)
            {
                _pitch = weight == MenuButton.Weight.Primary
                    ? MenuTheme.Metrics.PrimaryPitch
                    : MenuTheme.Metrics.SecondaryPitch;
            }

            _rows.Add(row);
            return row;
        }

        /// <summary>
        /// Changes how many rows sit side by side.
        /// </summary>
        /// <remarks>
        /// A responsive decision rather than a preference: a two-column grid needs each cell to be
        /// wide enough for the longest label, and on a narrow canvas — a tablet in landscape, or any
        /// screen with the interface scaled up — one column is the honest layout. It is set from the
        /// layout's own numbers, so the same screen can be one column on one device and two on another.
        /// </remarks>
        public void SetColumns(int columns)
        {
            _columns = Mathf.Max(1, columns);
        }

        /// <summary>Renames the cluster's section label, for a panel that is reused.</summary>
        public void SetSection(string text)
        {
            if (_labelText == null) return;
            _labelText.text = MenuUi.PrepareText(
                MenuUi.Track(text, MenuTheme.Metrics.SectionLabelTracking));
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            Remember(width, height);

            float top = _label != null ? MenuTheme.Metrics.SectionLabelHeight + MenuTheme.Metrics.SectionLabelGap : 0f;
            bool rtl = Aether.Gameplay.Localization.LanguageService.IsRightToLeft;
            if (_label != null)
            {
                _label.sizeDelta = new Vector2(width, MenuTheme.Metrics.SectionLabelHeight);
                _label.anchoredPosition = Vector2.zero;
            }

            if (_sectionRule != null)
            {
                RectTransform rule = _sectionRule.rectTransform;
                rule.anchorMin = rule.anchorMax = new Vector2(rtl ? 1f : 0f, 1f);
                rule.pivot = new Vector2(rtl ? 1f : 0f, 1f);
                rule.anchoredPosition = new Vector2(0f,
                    -(MenuTheme.Metrics.SectionLabelHeight + 4f));
                rule.sizeDelta = new Vector2(width, MenuTheme.Metrics.TitleRuleHeight);
            }
            float cell = width / _columns;
            int bands = Mathf.CeilToInt(_rows.Count / (float)_columns);

            for (int i = 0; i < _rows.Count; i++)
            {
                int band = i / _columns;
                int column = i % _columns;

                RectTransform rect = _rows[i].Rect;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(cell, _pitch);
                float columnX = rtl ? width - ((column + 1) * cell) : column * cell;
                rect.anchoredPosition = new Vector2(columnX, -(top + (band * _pitch)));

                _rows[i].ApplyLayout(MenuTheme.Metrics.CaretOutdent, MenuTheme.Metrics.CaretOutdent);
                _rows[i].SetRuleWidth(cell - MenuTheme.Metrics.CaretOutdent);
            }

            SetPanelSize(width, top + (bands * _pitch));
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            // Rows redraw themselves in their current state, which is what makes a change of
            // contrast reach the rows of every cluster rather than only the one on screen.
            if (_labelText != null)
            {
                _labelText.color = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Accent,
                                                                  MenuPreferences.HighContrast);
            }
            if (_sectionRule != null) _sectionRule.color = MenuTheme.Palette.Line;

            for (int i = 0; i < _rows.Count; i++) _rows[i].RefreshColors();
        }

        /// <inheritdoc />
        public override void Register()
        {
            bool rtl = Aether.Gameplay.Localization.LanguageService.IsRightToLeft;
            for (int i = 0; i < _rows.Count; i++)
            {
                // Navigation coordinates describe the cells as placed, not just the translation's
                // source order. In RTL the first entry is physically rightmost, so left/right input
                // must follow that mirrored geometry rather than jumping to the opposite cell.
                int column = i % _columns;
                int navigationColumn = rtl ? _columns - 1 - column : column;
                Nav.Add(_rows[i], navigationColumn, i / _columns);
            }
        }
    }
}
