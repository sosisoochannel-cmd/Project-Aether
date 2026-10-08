using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Components
{
    /// <summary>
    /// One selectable line of the menu: a label with a caret, and whatever the row's control is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the project's own <c>Selectable</c> rather than a <c>Button</c>, for three reasons the
    /// brief's states require and a <c>Button</c> cannot express: there are more than five visual
    /// states (a locked row is not the same as a disabled one), the feedback has to be split between
    /// what happens on press and what happens on activation, and the highlight is driven by the
    /// menu's own navigation rather than by an editor-authored <c>Navigation</c> graph.
    /// </para>
    /// <para>
    /// <b>Instant geometry, faded colour.</b> Press feedback moves the row's own text and shows its
    /// caret the frame the finger lands, because a press that waits for an animation reads as a
    /// missed tap. The colour change fades through <c>Graphic.CrossFadeColor</c>, which the engine
    /// animates without this class needing an <c>Update</c>. Nothing here is per-frame, and nothing
    /// allocates after construction.
    /// </para>
    /// <para>
    /// <b>The whole row is the target.</b> A row's rect is its pitch — 208 units for a primary entry,
    /// 168 for the others, 176 for a setting — while the text inside it is a fraction of that and the
    /// caret is 14 units wide. That gap is the point: the thing a thumb aims at is the row, not the
    /// word.
    /// </para>
    /// </remarks>
    public class MenuButton : Selectable, IPointerClickHandler, ISubmitHandler
    {
        /// <summary>How much weight a row carries in the composition.</summary>
        public enum Weight
        {
            /// <summary>CONTINUE, NEW GAME: the largest type, the strongest accent.</summary>
            Primary = 0,

            /// <summary>The explore grid and the system pair: smaller, quieter.</summary>
            Secondary = 1,
        }

        /// <summary>Raised on activation: a tap, a click, or Submit from a keyboard or gamepad.</summary>
        public System.Action Activated;

        /// <summary>Raised when the pointer moves onto this row, for pointer-driven highlighting.</summary>
        public System.Action<MenuButton> Hovered;

        /// <summary>The navigation this row belongs to, when it has one.</summary>
        public MenuNav Nav;

        private Image _plate;
        private Image _selectionGlow;
        private Text _label;
        private Text _help;
        private Text _meta;
        private Image _caret;
        private Image _rule;
        private Image _accent;
        private Image _icon;
        private RectTransform _control;
        private RectTransform _textBlock;
        private Weight _weight = Weight.Primary;
        private bool _locked;
        private bool _accented;
        private bool _informational;
        private string _metaText;
        private string _lockNote;
        private float _textBaseX;
        private float _labelY;
        private int _lastActivationFrame = -1;

        /// <summary>
        /// The row's own rect. A Selectable hands out a Transform, and every caller wants the rect,
        /// so the cast lives here once and the call sites read like the rest of the menu.
        /// </summary>
        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        /// <summary>Where a row's control (a switch, a slider, a value) is placed.</summary>
        public RectTransform ControlArea
        {
            get { return _control; }
        }

        /// <summary>The row's label, for a screen that relabels a pooled row.</summary>
        public Text Label
        {
            get { return _label; }
        }

        /// <summary>The small line beside the label: a value, a note, a state.</summary>
        public Text Meta
        {
            get { return _meta; }
        }

        /// <summary>Whether the row is one of the ones the player cannot use yet.</summary>
        public bool Locked
        {
            get { return _locked; }
        }

        /// <summary>Whether the row can be used right now, for the navigation's own checks.</summary>
        public bool Usable
        {
            get { return isActiveAndEnabled && IsActive() && IsInteractable(); }
        }

        /// <summary>Whether the row is a read-only fact rather than a destination.</summary>
        public bool Informational
        {
            get { return _informational; }
        }

        /// <summary>Builds a row and returns it.</summary>
        /// <param name="name">GameObject name.</param>
        /// <param name="parent">The panel the row belongs to.</param>
        /// <param name="text">Initial label text; may be empty and set later.</param>
        /// <param name="weight">How much weight the row carries.</param>
        public static MenuButton Create(string name, Transform parent, string text, Weight weight)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);

            var plate = host.AddComponent<Image>();
            plate.sprite = MenuArt.Solid;
            plate.color = new Color(1f, 1f, 1f, 0f);
            plate.type = Image.Type.Simple;

            var button = host.AddComponent<MenuButton>();
            button._weight = weight;
            button.Build(text);
            return button;
        }

        /// <summary>
        /// How far a row's own type is opened up.
        /// </summary>
        /// <remarks>
        /// A primary row is a headline and a secondary row is a list entry, so they are tracked
        /// differently. Keeping it here rather than at the call sites is what stops one screen from
        /// having airy type and another from having none.
        /// </remarks>
        private float Tracking
        {
            get
            {
                return _weight == Weight.Primary
                    ? MenuTheme.Metrics.PrimaryTracking
                    : MenuTheme.Metrics.SecondaryTracking;
            }
        }

        /// <summary>
        /// Redraws the row in its current state, re-reading the palette.
        /// </summary>
        /// <remarks>
        /// Used when a setting that changes what the interface looks like — high contrast — moves.
        /// Every colour in this class is read from <see cref="MenuTheme.Palette"/> at the moment a
        /// state is drawn, so nothing has to be tracked: asking the row to draw again is the whole
        /// of it.
        /// </remarks>
        public void RefreshColors()
        {
            ApplyState(currentSelectionState, true);
        }

        /// <summary>Sets the label. Safe to call every time a screen refreshes a pooled row.</summary>
        /// <remarks>
        /// The tracking is applied here, so callers pass the string the table holds and never a
        /// spaced one: a label set through this can be re-set any number of times and looks the same.
        /// </remarks>
        public void SetLabel(string text)
        {
            if (_label == null) return;

            string wanted = MenuUi.Track(text, Tracking);
            if (_label.text == wanted) return;
            _label.text = wanted;
        }

        /// <summary>Sets the small line beside the label, or clears it with null or empty.</summary>
        public void SetMeta(string text)
        {
            _metaText = text;
            RefreshMeta();
        }

        /// <summary>
        /// Marks the row as not in the build: dimmed, marked, and not reachable by pointer or
        /// keyboard. Used for destinations that exist in the interface before the system behind them
        /// does, which is the alternative to pretending they work.
        /// </summary>
        public void SetLocked(bool locked, string note = null)
        {
            _locked = locked;
            _lockNote = note;
            interactable = !locked;

            RefreshMeta();
            ApplyState(currentSelectionState, true);
        }

        /// <summary>
        /// Marks the row as a read-only fact rather than a destination: a version, a platform, the
        /// state of the stored run.
        /// </summary>
        /// <remarks>
        /// Drawn like a row, because a settings category of two labelled facts and four buttons
        /// should look like one list; but not selectable, because a row the navigation can land on
        /// and nothing happens is the most confusing thing an interface can contain.
        /// </remarks>
        public void SetInformational(bool informational)
        {
            _informational = informational;
            if (informational) interactable = false;

            ApplyState(currentSelectionState, true);
        }

        /// <summary>
        /// Gives the row the accent treatment: a brighter label, an accent bar, and its small line
        /// shown. Used for the row the player most likely wants — CONTINUE with a run stored — and
        /// for the row a settings screen is standing on.
        /// </summary>
        public void SetAccented(bool accented)
        {
            _accented = accented;
            if (_accent != null) _accent.gameObject.SetActive(accented);

            ApplyState(currentSelectionState, true);
        }

        /// <summary>
        /// Points the row's caret the other way, for a Back row.
        /// </summary>
        /// <remarks>
        /// The caret is drawn from its own left edge, so mirroring about that edge puts the arrow to
        /// the left of the text without anything else moving. It is the only shape in the row that is
        /// ever flipped, and flipping it is the smallest possible difference between "go on" and
        /// "go back".
        /// </remarks>
        public void SetCaretMirrored(bool mirrored)
        {
            if (_caret == null) return;
            _caret.rectTransform.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
        }

        /// <summary>
        /// Adds the one-line explanation under the label, or removes it with null.
        /// </summary>
        /// <remarks>
        /// A setting row carries a label and, when it has one, a sentence saying what the setting
        /// does. The help sits under the label inside the same row rather than in a tooltip or a
        /// second screen: on a phone, an explanation nobody can find is an explanation nobody reads.
        /// </remarks>
        public void SetHelp(string text)
        {
            if (_help == null) return;

            bool wanted = !string.IsNullOrEmpty(text);
            if (_help.gameObject.activeSelf != wanted) _help.gameObject.SetActive(wanted);
            if (wanted && _help.text != text) _help.text = text;

            // With a second line the label moves up to make room for it; without one it is centred.
            _labelY = wanted ? 15f : 0f;
            _label.rectTransform.anchoredPosition = new Vector2(_label.rectTransform.anchoredPosition.x, _labelY);
        }

        /// <summary>
        /// Gives the row's control column a width. Callers place their widget against its left edge,
        /// so a switch or a slider lines up down a list without any of them knowing the row's width.
        /// </summary>
        public void SetControlWidth(float width)
        {
            if (_control == null) return;
            _control.sizeDelta = new Vector2(Mathf.Max(0f, width), 0f);
        }

        /// <summary>Lays the row out for the track it was given. Called by the panel that owns it.</summary>
        public void ApplyLayout(float caretOutdent, float textInset)
        {
            if (_label == null) return;

            if (_caret != null) _caret.rectTransform.anchoredPosition = new Vector2(-caretOutdent, 0f);
            if (_icon != null) _icon.rectTransform.anchoredPosition = new Vector2(-caretOutdent, 0f);

            _textBaseX = textInset;
            _label.rectTransform.anchoredPosition = new Vector2(_textBaseX, _labelY);
            if (_help != null) _help.rectTransform.anchoredPosition = new Vector2(textInset, -18f);
            if (_meta != null) _meta.rectTransform.anchoredPosition = new Vector2(-textInset, 0f);
        }

        // -- construction ----------------------------------------------------------------------

        private void Build(string text)
        {
            MenuArt.EnsureBuilt();

            _plate = GetComponent<Image>();

            _selectionGlow = MenuUi.CreateImage("SelectionGlow", transform, MenuArt.Glow,
                                                new Color(MenuTheme.Palette.Accent.r, MenuTheme.Palette.Accent.g,
                                                          MenuTheme.Palette.Accent.b, 0f));
            _selectionGlow.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _selectionGlow.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _selectionGlow.rectTransform.pivot = new Vector2(0.08f, 0.5f);
            _selectionGlow.rectTransform.anchoredPosition = new Vector2(42f, 0f);
            _selectionGlow.rectTransform.sizeDelta = new Vector2(520f, 150f);
            _selectionGlow.raycastTarget = false;
            _selectionGlow.gameObject.SetActive(false);
            _selectionGlow.transform.SetAsFirstSibling();

            // This component owns every pixel of its own transition — the plate, the label, the caret
            // and the rule each answer the state on their own schedule — so Selectable's built-in
            // tinting is switched off rather than fought with. Navigation is solved by
            // MenuNav, so no Navigation links are wanted either.
            transition = Transition.None;
            navigation = new Navigation { mode = Navigation.Mode.None };

            _textBlock = MenuUi.CreateNode("Text", transform);
            MenuUi.Stretch(_textBlock);

            _rule = MenuUi.CreateHairline("Rule", transform, new Color(1f, 1f, 1f, 0f),
                                          MenuTheme.Metrics.TitleRuleHeight);
            _rule.rectTransform.anchorMin = new Vector2(0f, 0f);
            _rule.rectTransform.anchorMax = new Vector2(0f, 0f);
            _rule.rectTransform.pivot = new Vector2(0f, 0f);
            _rule.rectTransform.anchoredPosition = Vector2.zero;
            _rule.rectTransform.sizeDelta = new Vector2(0f, MenuTheme.Metrics.TitleRuleHeight);

            _accent = MenuUi.CreateImage("Accent", transform, MenuArt.Solid, MenuTheme.Palette.Accent);
            _accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            _accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            _accent.rectTransform.pivot = new Vector2(1f, 0.5f);
            _accent.rectTransform.sizeDelta = new Vector2(4f, -14f);
            _accent.rectTransform.anchoredPosition = new Vector2(-8f, 0f);
            _accent.gameObject.SetActive(false);

            _caret = MenuUi.CreateImage("Caret", transform, MenuArt.Triangle,
                                        MenuTheme.Palette.InkFaint);
            _caret.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _caret.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _caret.rectTransform.pivot = new Vector2(0f, 0.5f);
            _caret.rectTransform.sizeDelta = new Vector2(MenuTheme.Metrics.CaretWidth,
                                                        MenuTheme.Metrics.CaretHeight);

            _icon = MenuUi.CreateImage("Locked", transform, MenuArt.Padlock, MenuTheme.Palette.Locked);
            _icon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _icon.rectTransform.pivot = new Vector2(0f, 0.5f);
            _icon.rectTransform.sizeDelta = new Vector2(MenuTheme.Metrics.CaretHeight,
                                                       MenuTheme.Metrics.CaretHeight);
            _icon.gameObject.SetActive(false);

            _control = MenuUi.CreateNode("Control", transform);
            _control.anchorMin = new Vector2(1f, 0.5f);
            _control.anchorMax = new Vector2(1f, 0.5f);
            _control.pivot = new Vector2(1f, 0.5f);
            _control.anchoredPosition = Vector2.zero;
            _control.sizeDelta = Vector2.zero;
            _control.SetAsLastSibling();

            float size = _weight == Weight.Primary
                ? MenuTheme.Metrics.PrimarySize
                : MenuTheme.Metrics.SecondarySize;

            _label = MenuUi.CreateTrackedText("Label", _textBlock, text, size, MenuTheme.Palette.Ink,
                                             Tracking, TextAnchor.MiddleLeft,
                                             _weight == Weight.Primary ? FontStyle.Bold : FontStyle.Normal);
            _label.rectTransform.anchorMin = new Vector2(0f, 0f);
            _label.rectTransform.anchorMax = new Vector2(0f, 1f);
            _label.rectTransform.pivot = new Vector2(0f, 0.5f);
            _label.rectTransform.anchoredPosition = Vector2.zero;
            _label.rectTransform.sizeDelta = new Vector2(10f, 0f);

            _meta = MenuUi.CreateText("Meta", _textBlock, string.Empty,
                                      MenuTheme.Metrics.SettingHelpSize, MenuTheme.Palette.InkFaint,
                                      TextAnchor.MiddleRight);
            _meta.rectTransform.anchorMin = new Vector2(1f, 0f);
            _meta.rectTransform.anchorMax = new Vector2(1f, 1f);
            _meta.rectTransform.pivot = new Vector2(1f, 0.5f);
            _meta.rectTransform.anchoredPosition = Vector2.zero;
            _meta.rectTransform.sizeDelta = new Vector2(10f, 0f);
            _meta.gameObject.SetActive(false);

            _help = MenuUi.CreateText("Help", _textBlock, string.Empty,
                                      MenuTheme.Metrics.SettingHelpSize, MenuTheme.Palette.InkFaint,
                                      TextAnchor.MiddleLeft);
            _help.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _help.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _help.rectTransform.pivot = new Vector2(0f, 0.5f);
            _help.rectTransform.sizeDelta = new Vector2(10f, MenuTheme.Metrics.SettingHelpSize * 2f);
            _help.rectTransform.anchoredPosition = new Vector2(0f, -18f);
            _help.gameObject.SetActive(false);

            ApplyState(currentSelectionState, true);
        }

        private void RefreshMeta()
        {
            if (_meta == null) return;

            string wanted = _locked ? _lockNote ?? _metaText : _metaText;
            bool show = !string.IsNullOrEmpty(wanted);

            if (_meta.gameObject.activeSelf != show) _meta.gameObject.SetActive(show);
            if (show && _meta.text != wanted) _meta.text = wanted;
        }

        // -- state -----------------------------------------------------------------------------

        /// <summary>
        /// Draws one state. Colours fade; positions do not. A press has to be visible in the frame it
        /// happens in.
        /// </summary>
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            ApplyState(state, instant);
        }

        private void ApplyState(SelectionState state, bool instant)
        {
            if (_label == null) return;   // the base class asks for a state before Build has run

            bool highContrast = MenuPreferences.HighContrast;
            float fade = instant ? 0f : MenuTheme.Motion.HighlightSeconds;
            Color ink = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Ink, highContrast);
            Color muted = MenuTheme.Palette.WithContrast(MenuTheme.Palette.InkMuted, highContrast);
            Color faint = MenuTheme.Palette.WithContrast(MenuTheme.Palette.InkFaint, highContrast);

            var plate = new Color(1f, 1f, 1f, 0f);
            Color label = _weight == Weight.Primary ? ink : muted;
            Color caret = faint;
            float nudge = 0f;
            bool ruleVisible = false;
            bool accentVisible = _accented && !_locked && !_informational;

            if (_locked)
            {
                label = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Locked, highContrast);
                caret = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Locked, highContrast);
            }
            else
            {
                switch (state)
                {
                    case SelectionState.Pressed:
                        plate = MenuTheme.Palette.AccentWash;
                        label = ink;
                        caret = MenuTheme.Palette.Accent;
                        ruleVisible = true;
                        accentVisible = true;
                        nudge = MenuTheme.Metrics.HighlightNudge * 0.5f;
                        break;

                    case SelectionState.Highlighted:
                    case SelectionState.Selected:
                        plate = MenuTheme.Palette.AccentWash;
                        label = ink;
                        caret = MenuTheme.Palette.Accent;
                        ruleVisible = true;
                        accentVisible = true;
                        nudge = MenuTheme.Metrics.HighlightNudge;
                        break;

                    case SelectionState.Disabled:
                        label = faint;
                        caret = faint;
                        break;
                }
            }

            if (_informational)
            {
                // A fact has no state of its own: whatever the row would draw for a highlight is
                // dropped, so a reader cannot mistake it for something they can press.
                plate = new Color(1f, 1f, 1f, 0f);
                ruleVisible = false;
                nudge = 0f;
                label = muted;
                caret = faint;
            }
            else if (_accented && !_locked && state != SelectionState.Highlighted
                     && state != SelectionState.Pressed)
            {
                label = ink;
                caret = MenuTheme.Palette.Accent;
            }

            // A press also compresses the row a little. It is set instantly rather than
            // interpolated: the finger is already on the row, and an animation that starts after
            // the touch reads as a delay. The compression is under three per cent — enough to be
            // felt as feedback, not enough to move the text the eye is reading.
            float scale = state == SelectionState.Pressed && !_locked ? MenuTheme.Motion.PressedScale : 1f;
            if (_textBlock.localScale.x != scale)
            {
                _textBlock.localScale = new Vector3(scale, scale, 1f);
            }

            CrossFade(_plate, plate, fade);
            if (_selectionGlow != null)
            {
                bool glowVisible = !_locked && !_informational &&
                                   (state == SelectionState.Highlighted || state == SelectionState.Selected ||
                                    state == SelectionState.Pressed);
                Color glow = new Color(MenuTheme.Palette.Accent.r, MenuTheme.Palette.Accent.g,
                                       MenuTheme.Palette.Accent.b, glowVisible ? 0.10f : 0f);
                CrossFade(_selectionGlow, glow, fade);
                _selectionGlow.gameObject.SetActive(glowVisible || !instant);
            }
            CrossFade(_caret, caret, fade);
            CrossFade(_rule, ruleVisible ? new Color(MenuTheme.Palette.Accent.r, MenuTheme.Palette.Accent.g, MenuTheme.Palette.Accent.b, 0.72f) : new Color(1f, 1f, 1f, 0f), fade);
            if (_accent != null)
            {
                _accent.color = MenuTheme.Palette.Accent;
                _accent.rectTransform.sizeDelta = new Vector2(accentVisible ? 6f : 4f, -14f);
                _accent.gameObject.SetActive(accentVisible);
            }

            if (_meta != null) _meta.color = faint;
            if (_help != null) _help.color = faint;
            _label.color = label;

            // Geometry is instant: the caret appears and the text slides the frame the row is
            // selected, which is what makes the menu feel like it heard the finger.
            if (_caret != null) _caret.gameObject.SetActive(!_locked && !_informational);
            if (_icon != null)
            {
                _icon.color = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Locked, highContrast);
                _icon.gameObject.SetActive(_locked);
            }

            float x = _textBaseX + nudge;
            _label.rectTransform.anchoredPosition = new Vector2(x, _labelY);
        }

        private static void CrossFade(Graphic graphic, Color colour, float duration)
        {
            if (graphic == null || !graphic.isActiveAndEnabled) return;

            if (duration <= 0f) graphic.color = colour;
            else graphic.CrossFadeColor(colour, duration, true, true);
        }

        private static void Fade(Graphic graphic, bool visible, float duration)
        {
            if (graphic == null || !graphic.isActiveAndEnabled) return;

            var target = new Color(1f, 1f, 1f, visible ? 1f : 0f);
            if (duration <= 0f) graphic.color = target;
            else graphic.CrossFadeColor(target, duration, true, true);
        }

        /// <summary>
        /// Sets how wide the row's hairline is drawn. A screen sets this once to the width of its
        /// column, so the rule appears to grow out of the row rather than out of the text.
        /// </summary>
        public void SetRuleWidth(float width)
        {
            if (_rule == null) return;
            _rule.rectTransform.sizeDelta = new Vector2(width, MenuTheme.Metrics.TitleRuleHeight);
        }

        // -- pointer and keyboard --------------------------------------------------------------

        /// <inheritdoc />
        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);

            // Touching a row with a mouse or a finger moves the menu's selection to it, so the two
            // ways of driving the menu never disagree about which row is current.
            if (!interactable) return;
            if (Nav != null && Nav.Current != this) Nav.Select(this);

            System.Action<MenuButton> handler = Hovered;
            if (handler != null) handler(this);
        }

        /// <summary>
        /// A tap or a click on the row.
        /// </summary>
        /// <remarks>
        /// The interface rather than an override, and that is not a detail: <c>Selectable</c> handles
        /// the pointer's down, up, enter, exit and selection events, but it does not implement
        /// <see cref="IPointerClickHandler"/> or <see cref="ISubmitHandler"/> — only <c>Button</c>
        /// does. A row that wants the click and the submit implements them itself, which is what
        /// this class does and what the editor's compiler insisted on.
        /// </remarks>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;

            Activate();
        }

        /// <inheritdoc />
        public void OnSubmit(BaseEventData eventData)
        {
            Activate();
        }

        /// <summary>
        /// Runs the row's action, once. Every route in — tap, click, Submit, or a screen driving a
        /// row itself — arrives here, which is what keeps one touch from running a row twice.
        /// </summary>
        /// <remarks>
        /// Two activations can arrive in the same frame: a tap and a Submit when a finger and a
        /// gamepad are used together, or a fast double tap on a slow device. One frame is the
        /// smallest interval in which a second activation can only be an accident, so it is the
        /// interval this refuses. A frame counter is a comparison, not a timer — nothing is polled
        /// and nothing is queued.
        /// </remarks>
        public void Activate()
        {
            if (!Usable) return;
            if (Time.frameCount == _lastActivationFrame) return;

            _lastActivationFrame = Time.frameCount;

            // One buzz, in the one place every route into a row passes through, and only when the
            // player asked for it and the device can: the gate is inside Haptics itself.
            Support.Haptics.Tap();

            System.Action handler = Activated;
            if (handler != null) handler();
        }
    }
}
