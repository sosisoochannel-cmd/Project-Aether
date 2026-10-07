using Aether.Core.Progression;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// Collection: everything the run has found, met or finished, category by category.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three categories, and each row knows exactly what it is waiting for.</b> A finding is
    /// recorded when the player reaches it in the world, an ability when a region grants it, and a
    /// region is marked when its exit is reached. The catalogue says which of those a row is, so the
    /// screen can say "not found yet" about a secret that exists and "not in this build" about one
    /// that does not — two different sentences, because they are two different facts.
    /// </para>
    /// <para>
    /// <b>The count under each category is over what this build can give.</b> An entry that is not in
    /// the build is excluded from the total, so the player is never shown "1 of 4" for a category
    /// where three of the four are behind a region that does not exist.
    /// </para>
    /// </remarks>
    public sealed class CollectionScreen : MenuScreen
    {
        private MenuHeader _header;
        private Text _body;
        private MenuEntryPanel[] _categories;
        private MenuButton[][] _rows;
        private Text[] _notes;
        private MenuButton[] _counts;

        /// <summary>The rows of one category, for a test to read.</summary>
        public MenuButton[] RowsOf(int category)
        {
            if (_rows == null || category < 0 || category >= _rows.Length) return new MenuButton[0];
            return _rows[category];
        }

        /// <summary>How many categories the screen lists.</summary>
        public int CategoryCount
        {
            get { return _categories == null ? 0 : _categories.Length; }
        }

        /// <inheritdoc />
        protected override void Construct()
        {
            _header = MenuHeader.Create(Rect, MenuStrings.Get("collection.title"), Nav, true);
            _header.Back.Activated = Leave;

            _body = MenuUi.CreateText("Body", Rect, MenuStrings.Get("collection.body"),
                                      MenuTheme.Metrics.SettingHelpSize, MenuTheme.Palette.InkMuted,
                                      TextAnchor.UpperLeft);
            _body.rectTransform.anchorMin = new Vector2(0f, 1f);
            _body.rectTransform.anchorMax = new Vector2(0f, 1f);
            _body.rectTransform.pivot = new Vector2(0f, 1f);

            CollectionCategory[] categories = CollectionCatalog.All;
            _categories = new MenuEntryPanel[categories.Length];
            _rows = new MenuButton[categories.Length][];
            _notes = new Text[categories.Length];
            _counts = new MenuButton[categories.Length];

            for (int c = 0; c < categories.Length; c++)
            {
                CollectionCategory category = categories[c];
                _categories[c] = MenuEntryPanel.Create(category.Id, Rect, Nav, category.TitleKey, 1);

                _rows[c] = new MenuButton[category.Entries.Length];
                for (int e = 0; e < category.Entries.Length; e++)
                {
                    CollectionEntry entry = category.Entries[e];
                    MenuButton row = _categories[c].AddEntry(entry.Id, MenuStrings.Get(entry.TitleKey),
                                                             MenuButton.Weight.Secondary);
                    _rows[c][e] = row;
                }

                // The category's own note is a real row rather than a caption: it explains what the
                // category counts, and in a translation it can be longer than the entries are.
                _counts[c] = _categories[c].AddEntry(category.Id + "Count", string.Empty,
                                                     MenuButton.Weight.Secondary);
                _counts[c].SetInformational(true);

                _notes[c] = MenuUi.CreateText(category.Id + "Note", Rect, MenuStrings.Get(category.NoteKey),
                                              MenuTheme.Metrics.SettingHelpSize,
                                              MenuTheme.Palette.InkFaint, TextAnchor.UpperLeft);
                _notes[c].rectTransform.anchorMin = new Vector2(0f, 1f);
                _notes[c].rectTransform.anchorMax = new Vector2(0f, 1f);
                _notes[c].rectTransform.pivot = new Vector2(0f, 1f);
            }
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            float top = MenuTheme.Metrics.ScreenHeaderPitch + MenuTheme.Metrics.ScreenHeaderGap;
            float column = Mathf.Min(width, width * MenuTheme.Metrics.LeftColumnFraction * 1.7f);

            _body.rectTransform.sizeDelta = new Vector2(column, 70f);
            _body.rectTransform.anchoredPosition = new Vector2(0f, -top);

            float cursor = top + 84f;
            for (int c = 0; c < _categories.Length; c++)
            {
                _categories[c].Rect.anchoredPosition = new Vector2(0f, -cursor);
                _categories[c].Layout(column, Mathf.Max(80f, height - cursor));
                cursor += _categories[c].Height + 8f;

                _notes[c].rectTransform.sizeDelta = new Vector2(column, 40f);
                _notes[c].rectTransform.anchoredPosition = new Vector2(0f, -(cursor + 16f));
                cursor += 24f + MenuTheme.Metrics.ClusterGap;
            }
        }

        /// <inheritdoc />
        protected override void OnShown(bool instant)
        {
            Refresh();

            Nav.Clear();
            for (int c = 0; c < _categories.Length; c++)
            {
                _categories[c].Show(instant, MenuTheme.Motion.EntranceStagger * c);
                _categories[c].Register();
            }

            Nav.Add(_header.Back, 0, 1000);
            Nav.SelectFirst();
        }

        /// <inheritdoc />
        public override bool OnBack()
        {
            Leave();
            return true;
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            SaveData run = SaveHost.Peek();

            for (int c = 0; c < _categories.Length; c++)
            {
                CollectionCategory category = CollectionCatalog.All[c];
                _categories[c].Refresh();

                int obtained = 0;
                int obtainable = 0;

                for (int e = 0; e < category.Entries.Length; e++)
                {
                    CollectionEntry entry = category.Entries[e];
                    MenuButton row = _rows[c][e];

                    if (entry.NotInBuild)
                    {
                        // Named, explained and excluded from the count: the entry is part of the
                        // collection's design and not of this build, and the row says which.
                        row.SetLocked(true, MenuStrings.Get(entry.LockedKey ?? "collection.locked.notInBuild"));
                        row.SetMeta(MenuStrings.Get("collection.locked.notInBuild"));
                        continue;
                    }

                    obtainable++;
                    bool has = Found(run, entry);
                    if (has) obtained++;

                    row.SetLocked(!has, MenuStrings.Get("collection.locked.unfound"));
                    row.SetMeta(MenuStrings.Get(has ? "achievements.earned"
                                                    : "collection.locked.unfound"));
                    row.SetHelp(has ? MenuStrings.Get(entry.BodyKey) : null);
                }

                _counts[c].SetLabel(MenuStrings.Format("collection.progress", obtained, obtainable));
                _counts[c].SetMeta(MenuStrings.Get(category.NoteKey));
            }
        }

        /// <summary>
        /// Whether a run has an entry.
        /// </summary>
        /// <remarks>
        /// Which record an entry lives in follows from what kind of thing it is: a finding is a
        /// collection record, an ability is owned by the progression state, and a region is complete
        /// when its exit flag is set. The catalogue says which kind, so this is a lookup rather than a
        /// guess — and a fourth kind added later cannot silently be counted as found.
        /// </remarks>
        private static bool Found(SaveData run, CollectionEntry entry)
        {
            if (run == null) return false;

            switch (entry.Source)
            {
                case CollectionSource.Finding:
                    return run.Collection.Has(entry.Id) || run.World.IsSet(entry.Id);

                case CollectionSource.Ability:
                    // The entry's id is the ability's own name in the catalogue, parsed back to the
                    // enum it stands for: "Rootbind" is AbilityId.Rootbind. A name that does not
                    // parse is nothing owned rather than an exception on a menu screen.
                    return run.Progression.HasAbility(ParseAbility(entry.Id));

                case CollectionSource.Region:
                    return run.World.IsSet(ChapterCatalog.CompletionFlagOf(entry.Id));

                default:
                    return false;
            }
        }

        /// <summary>The ability an entry id names, or <c>None</c> when it names nothing.</summary>
        private static Aether.Core.Progression.AbilityId ParseAbility(string id)
        {
            if (string.IsNullOrEmpty(id)) return Aether.Core.Progression.AbilityId.None;

            try
            {
                return (Aether.Core.Progression.AbilityId)System.Enum.Parse(
                    typeof(Aether.Core.Progression.AbilityId), id, true);
            }
            catch (System.ArgumentException)
            {
                Debug.LogWarning(
                    $"The collection catalogue names the ability '{id}', which is not an ability " +
                    "this build knows. The row reads as not owned until the catalogue is fixed.");
                return Aether.Core.Progression.AbilityId.None;
            }
        }

        private void Leave()
        {
            MenuAudio.Back();
            Host.Back();
        }
    }
}
