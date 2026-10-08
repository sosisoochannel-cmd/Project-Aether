using System;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Panels
{
    /// <summary>
    /// The dialog that stands between a player and something that cannot be undone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deleting a stored run is destructive, so it is asked about — with the two buttons in the same
    /// order and the same size every time, the cancel on the left and the destructive choice on the
    /// right, and neither of them focused by default. A confirmation that preselects "yes" is a
    /// confirmation that a thumb can answer by accident.
    /// </para>
    /// <para>
    /// <b>It owns its own navigation.</b> While the dialog is open the screen routes every move and
    /// every back press here, so a press cannot reach the rows behind it — and its scrim is a
    /// raycast target, so a finger cannot either. Both halves matter: a modal that blocks one kind of
    /// input is a modal that leaks the other.
    /// </para>
    /// <para>
    /// The panel is created once and reused for every question the menu asks, which is why the
    /// question is passed in when it opens rather than built into it.
    /// </para>
    /// </remarks>
    public sealed class MenuConfirmPanel : MenuPanel
    {
        private Image _scrim;
        private Image _plate;
        private Text _title;
        private Text _body;
        private MenuButton _confirm;
        private MenuButton _cancel;
        private Action _onConfirm;

        /// <summary>Whether the dialog is currently asking something.</summary>
        /// <summary>Whether the question is on screen.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The answer that does not run the action. What a dialog opens on, by rule.</summary>
        public MenuButton CancelRow
        {
            get { return _cancel; }
        }

        /// <summary>The answer that runs the action.</summary>
        public MenuButton ConfirmRow
        {
            get { return _confirm; }
        }

        /// <summary>Raised when the dialog closes, whether or not it was confirmed.</summary>
        public Action Closed;

        /// <summary>Builds the dialog. It is created hidden.</summary>
        /// <param name="name">GameObject name.</param>
        /// <param name="parent">The canvas root: the dialog covers the whole canvas, not a column.</param>
        /// <param name="nav">The dialog's own navigation, not the screen's.</param>
        public static MenuConfirmPanel Create(string name, Transform parent, MenuNav nav)
        {
            MenuConfirmPanel panel = Create<MenuConfirmPanel>(name, parent, nav);

            // A dialog covers the canvas, unlike every other panel, which is a column of a screen.
            MenuUi.Stretch(panel.Rect);

            panel._scrim = MenuUi.Fill("Scrim", panel.Rect, MenuTheme.Palette.Scrim, true);

            panel._plate = MenuUi.CreateImage("Dialog", panel.Rect, MenuArt.Solid,
                                              MenuTheme.Palette.Horizon, true);
            panel._plate.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            panel._plate.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            panel._plate.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel._plate.rectTransform.sizeDelta = new Vector2(MenuTheme.Metrics.DialogWidth,
                                                              MenuTheme.Metrics.DialogMinHeight);

            panel._title = MenuUi.CreateTrackedText("Title", panel._plate.rectTransform, string.Empty,
                                                    MenuTheme.Metrics.ScreenTitleSize,
                                                    MenuTheme.Palette.Ink,
                                                    MenuTheme.Metrics.ScreenTitleTracking);
            MenuUi.Corner(panel._title.rectTransform, new Vector2(LanguageService.IsRightToLeft ? 1f : 0f, 1f),
                          new Vector2(MenuTheme.Metrics.DialogPadding, -MenuTheme.Metrics.DialogPadding),
                          new Vector2(MenuTheme.Metrics.DialogWidth - (MenuTheme.Metrics.DialogPadding * 2f), 64f),
                          new Vector2(LanguageService.IsRightToLeft ? 1f : 0f, 1f));
            panel._title.alignment = LanguageService.IsRightToLeft ? TextAnchor.UpperRight : TextAnchor.UpperLeft;

            panel._body = MenuUi.CreateParagraph("Body", panel._plate.rectTransform, string.Empty,
                                                 MenuTheme.Metrics.ParagraphSize,
                                                 MenuTheme.Palette.InkMuted,
                                                 MenuTheme.Metrics.ParagraphLineHeight);
            MenuUi.Corner(panel._body.rectTransform, new Vector2(LanguageService.IsRightToLeft ? 1f : 0f, 1f),
                          new Vector2(MenuTheme.Metrics.DialogPadding, -150f),
                          new Vector2(MenuTheme.Metrics.DialogWidth - (MenuTheme.Metrics.DialogPadding * 2f), 88f),
                          new Vector2(LanguageService.IsRightToLeft ? 1f : 0f, 1f));
            panel._body.alignment = LanguageService.IsRightToLeft ? TextAnchor.UpperRight : TextAnchor.UpperLeft;

            panel._cancel = Row("Cancel", panel._plate.rectTransform, MenuStrings.Get("common.cancel"));
            bool rtl = LanguageService.IsRightToLeft;
            MenuUi.Corner(panel._cancel.Rect, new Vector2(rtl ? 1f : 0f, 0f),
                          new Vector2(rtl ? -MenuTheme.Metrics.DialogPadding : MenuTheme.Metrics.DialogPadding,
                                      MenuTheme.Metrics.DialogPadding),
                          new Vector2(360f, MenuTheme.Metrics.SettingRowHeight), new Vector2(0f, 0f));
            panel._cancel.Activated = () => panel.Close(false);

            panel._confirm = Row("Confirm", panel._plate.rectTransform, MenuStrings.Get("common.confirm"));
            MenuUi.Corner(panel._confirm.Rect, new Vector2(rtl ? 0f : 1f, 0f),
                          new Vector2(rtl ? MenuTheme.Metrics.DialogPadding : -MenuTheme.Metrics.DialogPadding,
                                      MenuTheme.Metrics.DialogPadding),
                          new Vector2(360f, MenuTheme.Metrics.SettingRowHeight), new Vector2(1f, 0f));
            panel._confirm.Activated = () => panel.Close(true);

            panel._cancel.SetRuleWidth(300f);
            panel._confirm.SetRuleWidth(300f);
            panel._confirm.SetAccented(true);
            panel._cancel.Nav = nav;
            panel._confirm.Nav = nav;

            panel.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>Opens the dialog with a question. The confirm button runs the action.</summary>
        /// <param name="title">The question, already localised.</param>
        /// <param name="body">What will happen, already localised.</param>
        /// <param name="onConfirm">What runs if the player says yes.</param>
        public void Open(string title, string body, Action onConfirm)
        {
            _onConfirm = onConfirm;
            IsOpen = true;

            // The dialog is built once and asked different questions, so its two answers are
            // re-labelled and re-drawn here rather than being left pointing at the last question's
            // wording. Colours come from the palette now, which is what makes a contrast change
            // from the settings screen land on the next question as well as the last one.
            bool highContrast = MenuPreferences.HighContrast;
            if (_title != null)
            {
                _title.text = MenuUi.Track(title, MenuTheme.Metrics.ScreenTitleTracking);
                _title.color = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Ink, highContrast);
            }

            if (_body != null)
            {
                _body.color = MenuTheme.Palette.WithContrast(MenuTheme.Palette.InkMuted, highContrast);
            }

            _cancel.RefreshColors();
            _confirm.RefreshColors();
            if (_body != null)
            {
                _body.text = body;
                float lines = Mathf.Max(1f, Mathf.Ceil(_body.preferredHeight / MenuTheme.Metrics.ParagraphLineHeight));
                float height = lines * MenuTheme.Metrics.ParagraphLineHeight;

                float dialogHeight = Mathf.Max(MenuTheme.Metrics.DialogMinHeight,
                                               150f + height + MenuTheme.Metrics.DialogPadding * 2f
                                               + MenuTheme.Metrics.SettingRowHeight);
                _plate.rectTransform.sizeDelta = new Vector2(MenuTheme.Metrics.DialogWidth, dialogHeight);
                _body.rectTransform.sizeDelta =
                    new Vector2(MenuTheme.Metrics.DialogWidth - (MenuTheme.Metrics.DialogPadding * 2f), height);
            }

            BringToFront();

            // The dialog arrives the same way a screen does, through the one reveal, so nothing about
            // it looks like a second kind of animation. It is never focused on open: the selection is
            // on the cancel, which is the safe answer to a finger that presses without reading.
            Show(false);
            Nav.Clear();
            Nav.Add(_cancel, 0, 0);
            Nav.Add(_confirm, 1, 0);
            Nav.Select(_cancel, false);
        }

        /// <summary>Closes the dialog, without confirming anything.</summary>
        public void Cancel()
        {
            if (!IsOpen) return;
            Close(false);
        }

        private void Close(bool confirmed)
        {
            IsOpen = false;
            Action action = confirmed ? _onConfirm : null;
            _onConfirm = null;

            Nav.Clear();
            Hide();

            if (confirmed) MenuAudio.Confirm();
            else MenuAudio.Back();

            if (action != null) action();

            Action handler = Closed;
            if (handler != null) handler();
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            Remember(width, height);
            SetPanelSize(width, height);

            // The dialog covers the canvas rather than a column of it, so the rect it was created
            // with is stretched by the canvas and nothing here needs the box's numbers.
        }

        /// <inheritdoc />
        public override bool OnMove(MenuNav.Move move)
        {
            // Only left and right do something here: the two answers sit side by side, and the
            // navigation's own left and right would otherwise have nothing to move between.
            if (!IsOpen) return false;
            if (move != MenuNav.Move.Left && move != MenuNav.Move.Right) return false;

            bool rtl = LanguageService.IsRightToLeft;
            Nav.Select(move == (rtl ? MenuNav.Move.Left : MenuNav.Move.Right)
                ? (Selectable)_confirm
                : _cancel);
            return true;
        }

        private static MenuButton Row(string name, Transform parent, string label)
        {
            MenuButton row = MenuButton.Create(name, parent, label, MenuButton.Weight.Secondary);
            row.SetMeta(null);
            return row;
        }
    }
}
