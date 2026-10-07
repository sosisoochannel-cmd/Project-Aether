using System;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Localization;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Progression.Achievements;
using Aether.Gameplay.Storage;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// The three slots a run can live in: what is in each one, and what can be done with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every state on this screen is read from the file.</b> An empty slot says EMPTY. A slot with
    /// a run shows the chapter it is in, when it was last played, how long it has been played, what it
    /// has found and what it has earned — all of it out of the stored document. A slot whose file
    /// cannot be read says DAMAGED SAVE and offers only the one thing that is honest to offer, which
    /// is deleting it: there is no play button on a run that cannot be loaded.
    /// </para>
    /// <para>
    /// <b>Nothing is overwritten without a question.</b> Starting in an empty slot happens at once —
    /// there is nothing to lose — and starting in a slot that holds a run asks first, naming the slot.
    /// Deleting always asks. Both dialogs start on Cancel, so a second tap in the same place cannot
    /// answer its own question.
    /// </para>
    /// <para>
    /// <b>Two rows per slot, and the reason is thumbs.</b> A play row and a delete row are the same
    /// size, so a finger cannot do the destructive thing by aiming one row too low. The delete row is
    /// secondary weight, unaccented, and says what it deletes.
    /// </para>
    /// </remarks>
    public sealed class SaveSlotScreen : MenuScreen
    {
        /// <summary>What the primary row of a slot does, which depends on what is in it.</summary>
        private enum SlotAction
        {
            /// <summary>Nothing stored: pressing it begins a run here.</summary>
            Start,

            /// <summary>A run is here and can be loaded: pressing it loads this slot.</summary>
            Play,

            /// <summary>The file cannot be read: the row is informational and says so.</summary>
            Damaged,
        }

        private sealed class SlotRows
        {
            public int Slot;
            public SlotAction Action;
            public MenuButton Primary;
            public MenuButton Delete;
        }

        private MenuHeader _header;
        private MenuEntryPanel _slots;
        private MenuEntryPanel _notes;
        private Text _body;
        private MenuButton _backRow;
        private SlotRows[] _rows;
        private MenuButton _note;

        /// <summary>The primary rows, in slot order, for a test to read and press.</summary>
        public MenuButton[] PrimaryRows
        {
            get
            {
                var rows = new MenuButton[_rows.Length];
                for (int i = 0; i < _rows.Length; i++) rows[i] = _rows[i].Primary;
                return rows;
            }
        }

        /// <summary>The delete rows, in slot order.</summary>
        public MenuButton[] DeleteRows
        {
            get
            {
                var rows = new MenuButton[_rows.Length];
                for (int i = 0; i < _rows.Length; i++) rows[i] = _rows[i].Delete;
                return rows;
            }
        }

        /// <inheritdoc />
        protected override void Construct()
        {
            EnsureDialog();

            _header = MenuHeader.Create(Rect, MenuStrings.Get("slots.title"), Nav, true);
            _header.Back.Activated = Leave;

            _rows = new SlotRows[SaveSlots.Count];
            _slots = MenuEntryPanel.Create("Slots", Rect, Nav, "slots.title", 1);
            _notes = MenuEntryPanel.Create("Notes", Rect, Nav, null, 1);
            _note = _notes.AddEntry("SlotsUsed", MenuStrings.Get("slots.body"),
                                    MenuButton.Weight.Secondary);
            _note.SetInformational(true);

            for (int i = 0; i < SaveSlots.Count; i++)
            {
                int slot = i + 1;
                var entry = new SlotRows { Slot = slot };

                entry.Primary = _slots.AddEntry(
                    "Slot " + slot,
                    MenuStrings.Format("slots.slot", slot),
                    MenuButton.Weight.Primary);

                int captured = slot;
                entry.Primary.Activated = () => UseSlot(captured);

                entry.Delete = _slots.AddEntry(
                    "Delete " + slot,
                    MenuStrings.Get("slots.delete"),
                    MenuButton.Weight.Secondary);
                entry.Delete.SetMeta(MenuStrings.Format("slots.slot", slot));

                entry.Delete.Activated = () => AskToDelete(captured);

                _rows[i] = entry;
            }

            _body = MenuUi.CreateText("Body", Rect, MenuStrings.Get("slots.body"),
                                      MenuTheme.Metrics.SettingHelpSize, MenuTheme.Palette.InkMuted,
                                      TextAnchor.UpperLeft);
            _body.rectTransform.anchorMin = new Vector2(0f, 1f);
            _body.rectTransform.anchorMax = new Vector2(0f, 1f);
            _body.rectTransform.pivot = new Vector2(0f, 1f);
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            float top = MenuTheme.Metrics.ScreenHeaderPitch + MenuTheme.Metrics.ScreenHeaderGap;
            float box = Mathf.Max(120f, height - top);
            float column = Mathf.Min(width, width * MenuTheme.Metrics.LeftColumnFraction * 1.7f);

            _body.rectTransform.sizeDelta = new Vector2(column, 70f);
            _body.rectTransform.anchoredPosition = new Vector2(0f, -top);

            float listTop = top + 84f;
            _slots.Rect.anchoredPosition = new Vector2(0f, -listTop);
            _slots.Layout(column, box);

            // The notes cluster is a second, one-row list rather than a caption so that it scrolls and
            // navigates with everything else — and so a translation that makes it long has somewhere
            // to go.
            _notes.Rect.anchoredPosition = new Vector2(0f, -(listTop + _slots.Height + MenuTheme.Metrics.ClusterGap));
            _notes.Layout(column, box);
        }

        /// <inheritdoc />
        protected override void OnShown(bool instant)
        {
            Refresh();

            _slots.Show(instant);
            _notes.Show(instant, MenuTheme.Motion.EntranceStagger * 2f);

            Nav.Clear();
            _slots.Register();
            _notes.Register();
            Nav.Add(_header.Back, 0, 1000);
            Nav.SelectFirst();
        }

        /// <inheritdoc />
        public override bool OnMove(MenuNav.Move move)
        {
            if (DialogOpen) return Dialog.OnMove(move);
            return false;
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

            // Otherwise the menu decides where back goes. Calling Host.Back() from inside OnBack
            // would re-enter this method: the host calls OnBack first, by design.
            return false;
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            SaveSlotInfo[] slots = SaveHost.DescribeSlots();
            int described = SaveHost.DescribedSlot();

            _slots.Refresh();
            _notes.Refresh();

            for (int i = 0; i < _rows.Length && i < slots.Length; i++)
            {
                SlotRows entry = _rows[i];
                SaveSlotInfo info = slots[i];

                if (info.Corrupt)
                {
                    // A file that cannot be read is neither empty nor playable. It is damaged, it says
                    // so, and the only row that does anything is the one that frees the slot.
                    entry.Action = SlotAction.Damaged;
                    entry.Primary.SetLabel(MenuStrings.Get("slots.damaged"));
                    entry.Primary.SetMeta(null);
                    entry.Primary.SetLocked(true, MenuStrings.Get("slots.damaged.help"));
                    entry.Delete.SetMeta(MenuStrings.Get("slots.damaged.help"));
                    continue;
                }

                if (!info.Exists)
                {
                    entry.Action = SlotAction.Start;
                    entry.Primary.SetLabel(MenuStrings.Get("slots.empty"));
                    entry.Primary.SetMeta(MenuStrings.Get("slots.newHere"));
                    entry.Primary.SetLocked(false);
                    entry.Primary.SetAccented(true);
                    entry.Delete.SetLocked(true, MenuStrings.Get("slots.empty.help"));
                    entry.Delete.SetMeta(MenuStrings.Get("slots.empty.help"));
                    continue;
                }

                entry.Action = SlotAction.Play;
                entry.Primary.SetLocked(false);
                entry.Primary.SetAccented(entry.Slot == described);

                // The label is where the run stands — the region it is in, which is what a player
                // recognises — and the meta line is the run's own summary: when it was last played,
                // how long it has been played, what it found and what it earned. Every number here is
                // read from the file.
                ChapterDefinition chapter = ChapterCatalog.Find(info.ChapterId);
                entry.Primary.SetLabel(chapter != null
                    ? MenuStrings.Get(chapter.TitleKey)
                    : MenuStrings.Get("slots.damaged"));

                entry.Primary.SetMeta(MenuStrings.Format("slots.played", ClockOf(info.PlaySeconds))
                                      + " · " + When(info)
                                      + " · " + MenuStrings.Format("slots.findings", info.Discoveries)
                                      + " · " + MenuStrings.Format("slots.achievements", info.Achievements,
                                                                   AchievementCatalog.Count));

                entry.Delete.SetLocked(false);
                entry.Delete.SetMeta(MenuStrings.Format("slots.slot", entry.Slot));
            }

            if (_notes != null && _notes.Rows.Count > 0)
            {
                // One line of context, built from the same numbers as the rows: how many slots are in
                // use, and — when nothing is stored anywhere — which slot a new run would go in.
                int used = SaveSlots.UsedCount();
                _note.SetLabel(MenuStrings.Format("menu.slots.used", used, SaveSlots.Count));
                _note.SetMeta(SaveSlots.HasAnyRun()
                    ? null
                    : MenuStrings.Format("menu.slots.free", SaveSlots.FirstEmpty()));
            }
        }

        private void Leave()
        {
            // The host's own Back: it decides where back goes and it plays the cue, once.
            Host.Back();
        }

        /// <summary>Does what the slot's row says it does.</summary>
        private void UseSlot(int slot)
        {
            SaveSlotInfo info = SaveSlots.Describe(slot);

            if (info.Corrupt)
            {
                // The row is locked and not interactable, so no tap reaches this. It is here so a
                // programmatic caller that presses it anyway gets the screen's own answer — nothing —
                // rather than opening a run that cannot be loaded. Silence is the honest feedback for
                // an action the interface does not offer.
                return;
            }

            if (!info.Exists)
            {
                MenuAudio.Confirm();
                SaveHost.BeginNewGameIn(slot, ChapterCatalog.First.Id);
                Host.PlayRegion();
                return;
            }

            AskToPlay(slot);
        }

        /// <summary>Asks before loading a stored run, and says which one.</summary>
        private void AskToPlay(int slot)
        {
            // No cue on opening: a question is answered, and the answer is what the player hears.
            // This is the convention the quit dialog already set.
            EnsureDialog().Open(MenuStrings.Get("slots.continue.title"),
                                MenuStrings.Format("slots.continue.body", slot),
                                () =>
                                {
                                    if (!SaveHost.LoadSlot(slot))
                                    {
                                        // The file passed the header check and still would not load —
                                        // a race with another process, or damage in a section the
                                        // header does not cover. The screen reloads and says what it
                                        // can; nothing is replaced and nothing is written.
                                        Debug.LogError(
                                            $"[menu] slot {slot} could not be loaded. The screen has " +
                                            "been refreshed rather than opening a run that is not there.");
                                        Refresh();
                                        return;
                                    }

                                    Host.PlayRegion();
                                });
        }

        /// <summary>Asks before deleting, and names the slot in the question.</summary>
        private void AskToDelete(int slot)
        {
            SaveSlotInfo info = SaveSlots.Describe(slot);
            if (!info.Exists) return;

            EnsureDialog().Open(MenuStrings.Get("slots.delete.title"),
                                MenuStrings.Format("slots.delete.body", slot),
                                () =>
                                {
                                    SaveHost.DeleteSlot(slot);
                                    Refresh();
                                });
        }

        /// <summary>When a slot was last played, in the player's own locale, or never.</summary>
        private static string When(SaveSlotInfo info)
        {
            if (info.SavedUtcTicks <= 0) return MenuStrings.Get("slots.never");

            try
            {
                // The date is written in the language the interface is in, not in the machine's:
                // a player reading Spanish should not be shown English month names because the
                // device happens to be set to English.
                DateTime when = new DateTime(info.SavedUtcTicks, DateTimeKind.Utc).ToLocalTime();
                return MenuStrings.Format("slots.lastPlayed",
                                          when.ToString("d MMM HH:mm", LanguageService.Culture));
            }
            catch (ArgumentOutOfRangeException)
            {
                // A timestamp from a file that has been hand-edited, or from a clock set to the year
                // 12000. Saying "never played" about a run that exists would be a lie, so the row
                // says the date cannot be read instead.
                return MenuStrings.Get("slots.never");
            }
        }

        /// <summary>
        /// A played time as hours, minutes and seconds, with no words in it.
        /// </summary>
        /// <remarks>
        /// Digits and colons rather than "12m 34s": this line sits inside a sentence that is
        /// translated, and a unit letter is a word — it would be English in every language, which is
        /// exactly the sort of thing nobody notices until a player does. 12:34 reads the same in all
        /// thirteen languages the interface names.
        /// </remarks>
        private static string ClockOf(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            int hours = total / 3600;
            int minutes = (total % 3600) / 60;

            return hours > 0
                ? hours + ":" + minutes.ToString("00") + ":" + (total % 60).ToString("00")
                : minutes + ":" + (total % 60).ToString("00");
        }
    }
}
