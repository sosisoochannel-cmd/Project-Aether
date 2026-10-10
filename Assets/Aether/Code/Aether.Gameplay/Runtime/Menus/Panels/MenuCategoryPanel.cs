using System;
using System.Collections.Generic;
using System.Globalization;
using Aether.Core.Localization;
using Aether.Core.Settings;
using Aether.Gameplay.Localization;
using Aether.Gameplay.Menus;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Widgets;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Panels
{
    /// <summary>
    /// One settings category, with the rows for every option in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Built from the catalogue, not from a layout.</b> Every row that adjusts a setting is created
    /// from its <see cref="SettingDefinition"/>: the label and help keys, the range, the step, the
    /// options and the toggle-ness all come from the one list that also records where each value
    /// ends up. A row that does not exist in the catalogue cannot be drawn, and a definition that
    /// nothing draws is a failure the gate reports — which is what stops this screen from becoming a
    /// convincing collection of sliders that nothing reads.
    /// </para>
    /// <para>
    /// <b>The rows that are not settings live here.</b> Save data, the version, the platform and the
    /// credits link are not preferences; they are facts and actions, and they belong to the screen
    /// that shows them rather than to the catalogue. They are the <see cref="Extra"/> table below,
    /// each with a real destination — a store that writes, a scene that opens, or a read of the
    /// running application.
    /// </para>
    /// <para>
    /// <b>Built once, shown selectively.</b> Every row for every category is created when the panel
    /// is built, and opening a category only places the ones that belong to it and switches the rest
    /// off. Nothing is instantiated or destroyed while the player is looking at the screen, and a
    /// category that is opened twice looks the same the second time.
    /// </para>
    /// </remarks>
    public sealed class MenuCategoryPanel : MenuPanel
    {
        /// <summary>What a row that is not a catalogue setting does when it is pressed.</summary>
        private enum ExtraKind
        {
            /// <summary>The state of the stored run, read from the save layer.</summary>
            RunState = 0,

            /// <summary>Where the stored run is kept, as a file name.</summary>
            StorageName = 1,

            /// <summary>Write the current run out now.</summary>
            SaveNow = 2,

            /// <summary>Delete the stored run. Destructive, so it asks first.</summary>
            DeleteRun = 3,

            /// <summary>The application's version.</summary>
            Version = 4,

            /// <summary>The engine's version.</summary>
            Engine = 5,

            /// <summary>The platform the game is running on.</summary>
            Platform = 6,

            /// <summary>Open the credits screen.</summary>
            Credits = 7,

            /// <summary>Close the game. Desktop only, where closing is what a menu row means.</summary>
            Quit = 8,

            /// <summary>How complete the language on screen is, and how many are offered.</summary>
            LanguageState = 9,

            /// <summary>Languages that are named, catalogued and not translated yet.</summary>
            LanguageMissing = 10,

            /// <summary>Languages whose script has no usable runtime font on this device.</summary>
            LanguageFont = 11,

            /// <summary>A single language in the visual language hub.</summary>
            LanguageOption = 12,
        }

        /// <summary>One row that is not a catalogue setting.</summary>
        private struct Extra
        {
            public SettingCategory Category;
            public string LabelKey;
            public ExtraKind Kind;
        }

        /// <summary>A row: the button, and whatever it needs to be shown and changed.</summary>
        private sealed class Row
        {
            public SettingCategory Category;
            public MenuButton Button;
            public SettingDefinition Definition;
            public ExtraKind Extra;
            public bool IsExtra;
            public MenuSwitch Switch;
            public MenuSlider Slider;
            public MenuSelector Selector;
            public string LanguageCode;
        }

        // -- the rows that are not settings ------------------------------------------------
        //
        // Order within a category is this table's order, appended after the catalogue's own rows.
        private static readonly Extra[] Extras =
        {
            new Extra { Category = SettingCategory.Language, LabelKey = "language.coverage.label", Kind = ExtraKind.LanguageState },
            new Extra { Category = SettingCategory.Language, LabelKey = "language.notWritten", Kind = ExtraKind.LanguageMissing },
            new Extra { Category = SettingCategory.Language, LabelKey = "language.needsFont", Kind = ExtraKind.LanguageFont },

            new Extra { Category = SettingCategory.Data, LabelKey = "data.info.none", Kind = ExtraKind.RunState },
            new Extra { Category = SettingCategory.Data, LabelKey = "about.storage", Kind = ExtraKind.StorageName },
            new Extra { Category = SettingCategory.Data, LabelKey = "data.saveNow", Kind = ExtraKind.SaveNow },
            new Extra { Category = SettingCategory.Data, LabelKey = "data.reset", Kind = ExtraKind.DeleteRun },

            new Extra { Category = SettingCategory.About, LabelKey = "about.version", Kind = ExtraKind.Version },
            new Extra { Category = SettingCategory.About, LabelKey = "about.engine", Kind = ExtraKind.Engine },
            new Extra { Category = SettingCategory.About, LabelKey = "about.platform", Kind = ExtraKind.Platform },
            new Extra { Category = SettingCategory.About, LabelKey = "about.credits", Kind = ExtraKind.Credits },
            new Extra { Category = SettingCategory.About, LabelKey = "menu.quit", Kind = ExtraKind.Quit },
        };

        /// <summary>The one settings row whose effect reaches beyond the row itself.</summary>
        private const string LanguageSettingId = "language.primary";

        private readonly List<Row> _rows = new List<Row>(40);
        private readonly List<Row> _visible = new List<Row>(12);

        private ScrollRect _scroll;
        private RectTransform _content;
        private Text _note;
        private SettingCategory _category = SettingCategory.Gameplay;
        private float _columnWidth;
        private bool _scrollHooked;

        /// <summary>Raised when a row needs the screen to do something: confirm, navigate, quit.</summary>
        public Action<string> Requested;

        /// <summary>Shows a failed delete on the row that owns the action.</summary>
        public void ShowDeleteFailure()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Extra != ExtraKind.DeleteRun) continue;
                _rows[i].Button.SetMeta(MenuStrings.Get("slots.delete.failed"));
                return;
            }
        }

        /// <summary>Shows why a confirmed quit did not close the game.</summary>
        public void ShowQuitFailure()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Extra != ExtraKind.Quit) continue;
                _rows[i].Button.SetMeta(MenuStrings.Get("menu.quit.saveFailed"));
                return;
            }
        }

        /// <summary>Creates the panel and builds every row.</summary>
        public static MenuCategoryPanel Create(string name, Transform parent, MenuNav nav)
        {
            MenuCategoryPanel panel = Create<MenuCategoryPanel>(name, parent, nav);
            panel.EnsureScroll();
            panel.BuildRows();
            return panel;
        }

        /// <summary>Shows one category's rows and hides everything else.</summary>
        public void ShowCategory(SettingCategory category)
        {
            _category = category;

            if (_note != null)
            {
                _note.text = MenuStrings.Get(MenuSettingsPanel.CategoryNoteKey(category));
            }

            Refresh();
            PlaceRows();
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            Remember(width, height);

            EnsureScroll();

            _columnWidth = width;
            SetPanelSize(width, height);
            PlaceRows();
        }

        /// <inheritdoc />
        public override void Register()
        {
            for (int i = 0; i < _visible.Count; i++)
            {
                Row row = _visible[i];
                Nav.Add(row.Button, 0, i);
            }
        }

        /// <summary>
        /// Steps the row the navigation is standing on.
        /// </summary>
        /// <remarks>
        /// Left and right mean "change the thing I am on": a slider moves, a choice cycles, a switch
        /// flips. Up and down belong to the navigation, and the row that a press lands on is the row
        /// the change lands on, so the two input methods never disagree.
        /// </remarks>
        public override bool OnMove(MenuNav.Move move)
        {
            if (move != MenuNav.Move.Left && move != MenuNav.Move.Right) return false;

            MenuButton current = Nav == null ? null : Nav.Current as MenuButton;
            if (current == null) return false;

            Row row = Find(current);
            if (row == null) return false;

            float direction = move == MenuNav.Move.Right ? 1f : -1f;

            if (row.Slider != null) return row.Slider.Nudge(direction);
            if (row.Selector != null) return StepChoice(row, direction);

            return false;
        }

        // -- construction --------------------------------------------------------------------

        private void EnsureScroll()
        {
            if (_scroll != null) return;

            _scroll = MenuUi.CreateScroll("Scroll", Rect, out _content);
            MenuUi.Stretch(_scroll.GetComponent<RectTransform>());

            if (!_scrollHooked && Nav != null)
            {
                Nav.SelectionChanged += selected =>
                {
                    if (selected != null && selected.transform.IsChildOf(_content))
                        MenuUi.ScrollIntoView(_scroll, selected);
                };
                _scrollHooked = true;
            }

            _note = MenuUi.CreateParagraph("Note", _content, string.Empty,
                                           MenuTheme.Metrics.ParagraphSize,
                                           MenuTheme.Palette.InkMuted,
                                           MenuTheme.Metrics.ParagraphLineHeight);
        }

        private void BuildRows()
        {
            SettingDefinition[] all = SettingsCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                // Language gets a dedicated visual hub below. Keeping the raw Choice row hidden avoids
                // a tiny cycling control when the player is choosing among a real list of languages.
                if (all[i].Id == LanguageSettingId) continue;

                Row row = NewRow(all[i].Category, all[i].LabelKey, all[i].HelpKey ?? string.Empty);
                row.Definition = all[i];
                BuildWidget(row);
                _rows.Add(row);
            }

            for (int i = 0; i < Extras.Length; i++)
            {
                Extra extra = Extras[i];
                if (extra.Kind == ExtraKind.Quit && Application.isMobilePlatform) continue;

                Row row = NewRow(extra.Category, extra.LabelKey, string.Empty);
                row.IsExtra = true;
                row.Extra = extra.Kind;
                row.Button.SetInformational(!IsAction(extra.Kind));

                if (IsAction(extra.Kind))
                {
                    ExtraKind captured = extra.Kind;
                    row.Button.Activated = () => RunExtra(row, captured);
                }

                _rows.Add(row);
            }

            // The language category is a proper hub: every known language gets a row, and the right
            // side tells the player whether it is ready, has no usable script font on this device,
            // or is still awaiting its translation.
            for (int i = 0; i < LanguageCatalog.Count; i++)
            {
                LanguageDefinition language = LanguageCatalog.All[i];
                if (language.Code == LanguageCatalog.DefaultCode) { /* English is still a real row. */ }

                bool fontAvailable = language.FontResolverConfigured
                    && MenuArt.CanRenderLanguage(language.Code);
                bool nativeLabelReadable = fontAvailable
                    && (language.Direction != TextDirection.RightToLeft
                        || string.Equals(language.Code, LanguageService.Code, StringComparison.OrdinalIgnoreCase));

                Row languageRow;
                if (nativeLabelReadable)
                {
                    languageRow = NewRow(SettingCategory.Language, language.LabelKey, string.Empty);
                    languageRow.Button.Label.font = MenuArt.FontForLanguage(language.Code);
                }
                else
                {
                    // Use a Latin name when the script font is absent or an RTL shaping pass is not
                    // active for that row. This keeps every language discoverable without displaying
                    // missing-glyph boxes in the picker.
                    languageRow = new Row { Category = SettingCategory.Language };
                    languageRow.Button = MenuButton.Create("Language " + language.Code, _content,
                                                            language.LatinName.ToUpperInvariant(),
                                                            MenuButton.Weight.Secondary);
                    languageRow.Button.Nav = Nav;
                }
                languageRow.IsExtra = true;
                languageRow.Extra = ExtraKind.LanguageOption;
                languageRow.LanguageCode = language.Code;
                languageRow.Button.SetRuleWidth(420f);

                string captured = language.Code;
                if (LanguageService.CanSelect(captured))
                {
                    languageRow.Button.Activated = () => LanguageService.Set(captured);
                }

                _rows.Add(languageRow);
            }
        }

        private Row NewRow(SettingCategory category, string labelKey, string helpKey)
        {
            var row = new Row { Category = category };

            MenuButton button = MenuButton.Create("Row " + labelKey, _content, MenuStrings.Get(labelKey),
                                                  MenuButton.Weight.Secondary);
            button.Nav = Nav;
            button.SetHelp(string.IsNullOrEmpty(helpKey) ? null : MenuStrings.Get(helpKey));

            row.Button = button;
            return row;
        }

        private void BuildWidget(Row row)
        {
            SettingDefinition definition = row.Definition;
            if (definition == null) return;

            switch (definition.Kind)
            {
                case SettingKind.Toggle:
                    row.Switch = MenuSwitch.Create("Switch", row.Button.ControlArea);
                    row.Button.Activated = () => Flip(row);
                    break;

                case SettingKind.Slider:
                    row.Slider = MenuSlider.Create("Slider", row.Button.ControlArea,
                                                   definition.Minimum, definition.Maximum, definition.Step);
                    MenuSlider slider = row.Slider;
                    slider.ValueChanged = (value, committed) => OnSlider(row, value, committed);

                    // A row that owns a slider does nothing when it is pressed on its label: the
                    // value is changed by dragging the slider or by stepping it with left and right.
                    row.Button.Activated = null;
                    break;

                case SettingKind.Choice:
                    row.Selector = MenuSelector.Create("Choice", row.Button.ControlArea);
                    row.Button.Activated = () => StepChoice(row, 1f);
                    break;
            }
        }

        // -- values --------------------------------------------------------------------------

        /// <inheritdoc />
        public override void Refresh()
        {
            SettingsService settings = AetherSettings.Ensure();

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                if (row.Category != _category) continue;

                row.Button.RefreshColors();

                if (row.Definition != null)
                {
                    float value = SettingsCatalog.Read(settings.Values, row.Definition.Id);

                    if (row.Switch != null) row.Switch.Set(value >= 0.5f, true);
                    if (row.Slider != null)
                    {
                        row.Slider.SetValue(value, false);
                        row.Slider.SetReadout(Describe(row.Definition, value));
                    }

                    if (row.Selector != null)
                    {
                        int index = IndexOfOption(row.Definition, value);
                        row.Selector.SetValue(MenuStrings.Get(row.Definition.OptionKeys[index]));
                        row.Selector.SetBounds(index > 0, index < row.Definition.OptionKeys.Length - 1);
                    }

                    row.Button.SetMeta(null);
                }
                else
                {
                    RefreshExtra(row);
                }
            }
        }

        private void RefreshExtra(Row row)
        {
            ProgressSummary summary = SaveHost.DescribeStoredRun();

            switch (row.Extra)
            {
                case ExtraKind.RunState:
                    row.Button.SetLabel(summary.HasRun
                        ? MenuStrings.Format("data.info.run", summary.AbilitesOwned, summary.FlagsEstablished)
                        : MenuStrings.Get("data.info.none"));
                    row.Button.SetMeta(summary.HasRun
                        ? MenuStrings.Get(summary.HasCheckpoint ? "data.info.checkpoint" : "data.info.start")
                        : null);
                    break;

                case ExtraKind.SaveNow:
                    bool canSave = SaveHost.DescribedSlot() > 0;
                    row.Button.SetLocked(!canSave, MenuStrings.Get("data.info.none"));
                    break;

                case ExtraKind.DeleteRun:
                    int described = SaveHost.DescribedSlot();
                    row.Button.SetLocked(described < 1, MenuStrings.Get("data.info.none"));
                    row.Button.SetMeta(described > 0
                        ? MenuStrings.Format("slots.slot", described)
                        : MenuStrings.Get("data.info.none"));
                    break;

                case ExtraKind.StorageName:
                    row.Button.SetMeta(SaveHost.Location == null
                        ? null
                        : System.IO.Path.GetFileName(SaveHost.Location));
                    break;

                case ExtraKind.Version:
                    row.Button.SetLabel(MenuStrings.Format("about.version", Application.version));
                    break;

                case ExtraKind.Engine:
                    row.Button.SetLabel(MenuStrings.Format("about.engine", Application.unityVersion));
                    break;

                case ExtraKind.Platform:
                    row.Button.SetLabel(MenuStrings.Format("about.platform", Application.platform));
                    break;

                case ExtraKind.LanguageState:
                    // Real numbers, read from the tables that are actually in the build: how much of
                    // the interface this language carries, and how many languages are offered.
                    int present;
                    int total;
                    LanguageService.Coverage(LanguageService.Code, out present, out total);
                    row.Button.SetLabel(MenuStrings.Format("language.coverage",
                                                           LanguageService.Code.ToUpperInvariant(),
                                                           present, total));
                    row.Button.SetMeta(MenuStrings.Format("language.offered.count",
                                                         LanguageService.OfferedCount, LanguageCatalog.Count));
                    break;

                case ExtraKind.LanguageMissing:
                    row.Button.SetMeta(LanguageService.DescribeNotOffered(false));
                    break;

                case ExtraKind.LanguageFont:
                    row.Button.SetMeta(LanguageService.DescribeNotOffered(true));
                    break;

                case ExtraKind.LanguageOption:
                    RefreshLanguageOption(row);
                    break;
            }
        }

        private void RefreshLanguageOption(Row row)
        {
            if (string.IsNullOrEmpty(row.LanguageCode)) return;

            LanguageDefinition language = LanguageCatalog.Resolve(row.LanguageCode);
            bool selectable = LanguageService.CanSelect(language.Code);
            bool translated = LanguageService.IsTranslated(language.Code);
            bool selected = string.Equals(LanguageService.Code, language.Code,
                                          StringComparison.OrdinalIgnoreCase);

            int present;
            int total;
            LanguageService.Coverage(language.Code, out present, out total);

            row.Button.SetAccented(selected);
            row.Button.SetLocked(!selectable && !selected,
                                 language.FontResolverConfigured && MenuArt.CanRenderLanguage(language.Code)
                                     ? MenuStrings.Get("language.notWritten")
                                     : MenuStrings.Get("language.needsFont"));

            if (selected)
            {
                row.Button.SetMeta(MenuStrings.Format("language.coverage", language.Code.ToUpperInvariant(), present, total));
            }
            else if (selectable)
            {
                row.Button.SetMeta(MenuStrings.Format("language.coverage", language.Code.ToUpperInvariant(), present, total));
            }
            else if (!language.FontResolverConfigured || !MenuArt.CanRenderLanguage(language.Code))
            {
                row.Button.SetMeta(MenuStrings.Get("language.needsFont"));
            }
            else if (translated)
            {
                row.Button.SetMeta(MenuStrings.Get("language.unavailable"));
            }
            else
            {
                row.Button.SetMeta(MenuStrings.Get("language.notWritten"));
            }
        }

        private void OnSlider(Row row, float value, bool committed)
        {
            if (row.Definition == null) return;

            SettingsService settings = AetherSettings.Ensure();
            settings.Set(row.Definition.Id, value);

            if (row.Slider != null) row.Slider.SetReadout(Describe(row.Definition, value));

            if (committed)
            {
                MenuAudio.Adjust();
                FlushSettings(settings, row);
            }
        }

        private void Flip(Row row)
        {
            if (row.Definition == null) return;

            SettingsService settings = AetherSettings.Ensure();
            float current = SettingsCatalog.Read(settings.Values, row.Definition.Id);
            float wanted = current >= 0.5f ? 0f : 1f;

            settings.Set(row.Definition.Id, wanted);
            FlushSettings(settings, row);

            MenuAudio.Adjust();
            if (row.Switch != null) row.Switch.Set(wanted >= 0.5f);

            if (row.Definition.Id == "gameplay.autosave") SaveHost.ApplySettings();
        }

        private bool StepChoice(Row row, float direction)
        {
            if (row.Definition == null || row.Definition.OptionValues == null) return false;

            SettingsService settings = AetherSettings.Ensure();
            float current = SettingsCatalog.Read(settings.Values, row.Definition.Id);
            int index = IndexOfOption(row.Definition, current);

            int wanted = index + (direction > 0f ? 1 : -1);
            if (wanted < 0 || wanted >= row.Definition.OptionValues.Length) return false;

            settings.Set(row.Definition.Id, row.Definition.OptionValues[wanted]);
            bool persisted = FlushSettings(settings, row);

            MenuAudio.Adjust();

            // Every other option on this screen shows its new value on the row that changed. The
            // language does not: it changes the words on every row, on every screen, including the
            // row being stepped. So the change is applied through the one service that owns it, and
            // the menu answers by building its screens again — see MenuSystem.RequestRebuild.
            if (row.Definition.Id == LanguageSettingId)
            {
                LanguageService.Set(LanguageCatalog.CodeAt((int)Math.Round(row.Definition.OptionValues[wanted])));
                return true;
            }

            Refresh();
            if (!persisted) row.Button.SetMeta(MenuStrings.Get("settings.save.failed"));
            return true;
        }

        private bool FlushSettings(SettingsService settings, Row row)
        {
            if (settings.Flush())
            {
                if (row != null && row.Button != null) row.Button.SetMeta(null);
                return true;
            }

            Debug.LogError($"[settings] The change to '{row?.Definition?.Id ?? "menu"}' could not be saved.");
            if (row != null && row.Button != null)
                row.Button.SetMeta(MenuStrings.Get("settings.save.failed"));
            return false;
        }

        private void RunExtra(Row row, ExtraKind kind)
        {
            switch (kind)
            {
                case ExtraKind.SaveNow:
                    bool saved = SaveHost.SaveNow();
                    MenuAudio.Confirm();
                    Refresh();
                    row.Button.SetMeta(MenuStrings.Get(saved ? "data.save.success" : "data.save.failed"));
                    break;

                case ExtraKind.DeleteRun:
                    Raise("settings.reset");
                    break;

                case ExtraKind.Credits:
                    Raise("settings.credits");
                    break;

                case ExtraKind.Quit:
                    Raise("settings.quit");
                    break;
            }
        }

        private void Raise(string request)
        {
            Action<string> handler = Requested;
            if (handler != null) handler(request);
        }

        // -- geometry ------------------------------------------------------------------------

        private void PlaceRows()
        {
            _visible.Clear();

            float y = 0f;

            if (_note != null && !string.IsNullOrEmpty(_note.text))
            {
                MenuUi.Corner(_note.rectTransform, new Vector2(0f, 1f), Vector2.zero,
                              new Vector2(_columnWidth, 0f), new Vector2(0f, 1f));
                float noteHeight = NoteHeight(_note);
                _note.rectTransform.sizeDelta = new Vector2(_columnWidth, noteHeight);
                y += noteHeight + MenuTheme.Metrics.ParagraphBlockPadding * 2f;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                bool belongs = row.Category == _category;

                if (row.Button.gameObject.activeSelf != belongs)
                    row.Button.gameObject.SetActive(belongs);

                if (!belongs) continue;

                bool informational = row.Button.Informational;
                float height = informational
                    ? MenuTheme.Metrics.InfoRowHeight
                    : MenuTheme.Metrics.SettingRowHeight;

                RectTransform rect = row.Button.Rect;
                MenuUi.Corner(rect, new Vector2(0f, 1f), new Vector2(0f, -y),
                              new Vector2(_columnWidth, height), new Vector2(0f, 1f));

                row.Button.ApplyLayout(MenuTheme.Metrics.CaretOutdent, MenuTheme.Metrics.CaretOutdent);
                row.Button.SetRuleWidth(_columnWidth - MenuTheme.Metrics.CaretOutdent);
                row.Button.SetControlWidth(_columnWidth * (1f - MenuTheme.Metrics.SettingLabelFraction)
                                           - (MenuTheme.Metrics.CaretOutdent * 2f));

                y += height;
                _visible.Add(row);
            }

            if (_content != null)
            {
                MenuUi.SetContentHeight(_content, y + MenuTheme.Metrics.ParagraphBlockPadding);

                // Opening a category starts at its first row. Writing the anchored position is exact
                // here — the content's pivot is its own top edge — while the normalised position
                // depends on bounds the scroll view has not recomputed yet.
                _content.anchoredPosition = Vector2.zero;
            }
        }

        /// <summary>
        /// How tall a wrapped note is, measured from the font rather than guessed.
        /// </summary>
        /// <remarks>
        /// The rect's width is set before this is asked, and the rect is anchored to a corner rather
        /// than stretched, so the measurement does not depend on the parent having been laid out
        /// yet — which is what lets a screen build itself on the frame it is created.
        /// </remarks>
        private static float NoteHeight(Text text)
        {
            float measured = text.preferredHeight;
            float minimum = MenuTheme.Metrics.ParagraphLineHeight;
            return Mathf.Max(minimum, Mathf.Ceil(measured / MenuTheme.Metrics.ParagraphLineHeight)
                                      * MenuTheme.Metrics.ParagraphLineHeight);
        }

        private Row Find(MenuButton button)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Button == button) return _rows[i];
            }

            return null;
        }

        private static bool IsAction(ExtraKind kind)
        {
            return kind == ExtraKind.SaveNow || kind == ExtraKind.DeleteRun
                   || kind == ExtraKind.Credits || kind == ExtraKind.Quit;
        }

        private static int IndexOfOption(SettingDefinition definition, float value)
        {
            if (definition.OptionValues == null || definition.OptionValues.Length == 0) return 0;

            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < definition.OptionValues.Length; i++)
            {
                float distance = Mathf.Abs(definition.OptionValues[i] - value);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// How a value reads: a percentage for anything on a 0-to-1 scale, two decimals otherwise.
        /// </summary>
        /// <remarks>
        /// Volumes and interface scalings are read as percentages because that is how people say
        /// them. Decimals are written with the invariant culture, so a device set to a comma decimal
        /// separator does not change how a value looks between two phones. A choice shows the name of
        /// the option it is on, which is the value the player actually cares about.
        /// </remarks>
        private static string Describe(SettingDefinition definition, float value)
        {
            if (definition.OptionValues != null && definition.OptionValues.Length > 0)
            {
                return MenuStrings.Get(definition.OptionKeys[IndexOfOption(definition, value)]);
            }

            if (Mathf.Approximately(definition.Minimum, 0f) && definition.Maximum > 0.999f)
            {
                return Mathf.RoundToInt(value * 100f) + "%";
            }

            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
