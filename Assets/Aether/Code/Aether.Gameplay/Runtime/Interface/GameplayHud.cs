using Aether.Core.Events;
using Aether.Gameplay.Menus;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Progression;
using Aether.Gameplay.Progression.Achievements;
using Aether.Gameplay.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Interface
{
    /// <summary>
    /// What the player can read while they are playing: where they are going, what they have found,
    /// how many times they have fallen, and the way to the pause menu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It shows state that exists, and nothing else.</b> The objective line is the chapter
    /// catalogue's own objective for the region that is running; the counters are the save file's own
    /// numbers, read from the session; the findings counter's total is the level's real number of
    /// discoveries, counted from the built level rather than promised by a designer's spreadsheet.
    /// There is no ammo, no score and no timer, because the game has none of those.
    /// </para>
    /// <para>
    /// <b>Nothing here runs per frame except a clock that has to.</b> The strip is written when
    /// something changes: a finding recorded, a death, an achievement unlocked, an objective met —
    /// all of which arrive as events the session already publishes. The one exception is the toast's
    /// own fade, which is a countdown and has to be counted; and even that stops when there is no
    /// toast to show.
    /// </para>
    /// <para>
    /// <b>It is drawn on the menu's own canvas rig and with the menu's own rows.</b> Same reference
    /// resolution, same interface scale, same safe-area rule, same button, same palette: the pause
    /// button in the corner of the region is the same object as a row in Settings, so it presses,
    /// highlights and reads the same way.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GameplayHud : MonoBehaviour
    {
        private const int SortingOrder = 540;
        private const float ToastSeconds = 2.8f;
        private const float ToastFadeSeconds = 0.25f;

        private MenuCanvas _canvas;
        private SafeAreaFitter _safeArea;
        private RectTransform _strip;
        private Text _objective;
        private Text _objectiveHelp;
        private Text _findings;
        private Text _falls;
        private MenuButton _pauseButton;
        private MenuNav _nav;
        private RectTransform _toast;
        private Text _toastTitle;
        private Text _toastBody;
        private CanvasGroup _toastGroup;

        private GameSession _session;
        private int _found;
        private int _foundTotal;
        private int _deaths;
        private float _toastUntil = float.NegativeInfinity;
        private Vector2 _box;

        /// <summary>Raised when the player asks to pause: the button, or the pause key.</summary>
        public System.Action PauseRequested;

        /// <summary>The pause button, for a test to press the way a thumb would.</summary>
        public MenuButton PauseButton
        {
            get { return _pauseButton; }
        }

        /// <summary>The objective line, for a test to read.</summary>
        public Text ObjectiveLine
        {
            get { return _objective; }
        }

        /// <summary>The findings counter, for a test to read.</summary>
        public Text FindingsLine
        {
            get { return _findings; }
        }

        /// <summary>Builds the strip and subscribes it to the session's events.</summary>
        public static GameplayHud Create(GameSession session, int foundTotal)
        {
            var host = new GameObject("Gameplay HUD", typeof(RectTransform));
            GameplayHud hud = host.AddComponent<GameplayHud>();
            hud.Build(session, foundTotal);
            return hud;
        }

        /// <summary>Reads the counters again. Called whenever the session changes.</summary>
        public void Refresh()
        {
            if (_session == null) return;

            _found = RunSummary.Findings(_session);
            _deaths = RunSummary.Deaths(_session);

            if (_findings != null)
            {
                _findings.text = MenuStrings.Format("hud.findings", _found, _foundTotal);
            }

            if (_falls != null)
            {
                _falls.text = MenuStrings.Format("hud.deaths", _deaths);
            }
        }

        /// <summary>Shows the objective for a chapter, or clears the line when there is none.</summary>
        public void SetObjective(ChapterDefinition chapter)
        {
            if (_objective == null) return;

            bool has = chapter != null && !string.IsNullOrEmpty(chapter.ObjectiveId);
            _objective.gameObject.SetActive(has);
            if (_objectiveHelp != null) _objectiveHelp.gameObject.SetActive(has);
            if (!has) return;

            _objective.text = MenuUi.Track(MenuStrings.Get(chapter.ObjectiveId),
                                           MenuTheme.Metrics.SubtitleTracking);
            if (_objectiveHelp != null) _objectiveHelp.text = MenuStrings.Get(chapter.ObjectiveId + ".body");
        }

        /// <summary>
        /// Announces an achievement in the strip: a line in, a hold, a line out.
        /// </summary>
        /// <remarks>
        /// Deliberately not a modal. An achievement that interrupts play is a worse achievement; this
        /// one slides in under the objective, stays long enough to read, and leaves. The fade is the
        /// only thing in the HUD that needs a clock, and the clock is unscaled so a paused game does
        /// not hold a toast on screen for ever.
        /// </remarks>
        public void Announce(AchievementDefinition achievement)
        {
            if (_toast == null || achievement == null) return;

            _toastTitle.text = MenuUi.Track(MenuStrings.Get("achievements.unlocked"),
                                            MenuTheme.Metrics.SectionLabelTracking);
            _toastBody.text = MenuStrings.Get(achievement.TitleKey);
            _toastUntil = Time.unscaledTime + ToastSeconds;
            _toast.gameObject.SetActive(true);
            _toastGroup.alpha = 0f;

            MenuAudio.Request("ui.confirm", 0.5f);
        }

        /// <summary>
        /// Announces something that is not an achievement: a codex entry, a region's first arrival.
        /// </summary>
        /// <remarks>
        /// The same strip and the same timing, with the caller's own two lines. It exists because the
        /// characters the player meets are recorded during play and nothing said so; a record that
        /// changes without a word is a record nobody learns about, which is the difference between a
        /// codex and a database.
        /// </remarks>
        /// <param name="titleKey">Localisation key for the small line above.</param>
        /// <param name="body">The line itself, already in the player's language.</param>
        public void Announce(string titleKey, string body)
        {
            if (_toast == null || string.IsNullOrEmpty(titleKey)) return;
            if (_toastTitle == null || _toastBody == null) return;

            _toastTitle.text = MenuUi.Track(MenuStrings.Get(titleKey),
                                            MenuTheme.Metrics.SectionLabelTracking);
            _toastBody.text = body ?? string.Empty;
            _toastUntil = Time.unscaledTime + ToastSeconds;
            _toast.gameObject.SetActive(true);
            _toastGroup.alpha = 0f;

            MenuAudio.Request("ui.confirm", 0.5f);
        }

        /// <summary>Puts the selection back on the pause button, after an overlay closes.</summary>
        /// <remarks>
        /// The button stays selected while an overlay is up, and an overlay that deactivates what
        /// the event system has selected leaves a gamepad with nothing to submit. This is the one
        /// line that puts it back.
        /// </remarks>
        public void Focus()
        {
            if (_pauseButton == null || _nav == null) return;
            _nav.Select(_pauseButton, true);
        }

        /// <summary>Hides the strip: used while the pause menu is up.</summary>
        public void SetVisible(bool visible)
        {
            if (_strip != null) _strip.gameObject.SetActive(visible);
        }

        private void Build(GameSession session, int foundTotal)
        {
            _session = session;
            _foundTotal = foundTotal;

            MenuArt.EnsureBuilt();

            _canvas = MenuCanvas.Create("Gameplay Canvas", SortingOrder);
            _canvas.GameObject.transform.SetParent(transform, false);
            _canvas.SetScale(MenuPreferences.ClampedUiScale);

            _safeArea = _canvas.SafeRoot.gameObject.AddComponent<SafeAreaFitter>();
            _safeArea.Mode = MenuPreferences.SafeArea;

            // A region is not a menu: its interface has to be pressable, and it needs an event
            // system to be. Both the strip and the curtain ask for the same one.
            UiEventSystem.Ensure();

            RectTransform content = _canvas.ContentRoot;
            MenuNav nav = new MenuNav();
            _nav = nav;

            _strip = MenuUi.CreateNode("Strip", content);
            MenuUi.Stretch(_strip);

            BuildObjective(_strip);
            BuildCounters(_strip);
            BuildPauseButton(_strip, nav);
            BuildToast(_strip);

            _box = content.rect.size;

            Subscribe();
            Refresh();
        }

        private void BuildObjective(RectTransform parent)
        {
            RectTransform block = MenuUi.CreateNode("Objective", parent);
            block.anchorMin = new Vector2(0f, 1f);
            block.anchorMax = new Vector2(0f, 1f);
            block.pivot = new Vector2(0f, 1f);
            block.sizeDelta = new Vector2(900f, 120f);
            block.anchoredPosition = new Vector2(MenuTheme.Metrics.ScreenMarginLeft * 0.5f,
                                                 -MenuTheme.Metrics.ScreenMarginTop * 0.4f);

            _objective = MenuUi.CreateTrackedText("Title", block, string.Empty,
                                                  MenuTheme.Metrics.ParagraphSize,
                                                  MenuTheme.Palette.Ink,
                                                  MenuTheme.Metrics.SubtitleTracking, TextAnchor.UpperLeft);
            MenuUi.Row(_objective.rectTransform, 0f, 46f);

            _objectiveHelp = MenuUi.CreateText("Help", block, string.Empty,
                                               MenuTheme.Metrics.SettingHelpSize,
                                               MenuTheme.Palette.InkMuted, TextAnchor.UpperLeft);
            MenuUi.Row(_objectiveHelp.rectTransform, 50f, 60f);
        }

        private void BuildCounters(RectTransform parent)
        {
            RectTransform block = MenuUi.CreateNode("Counters", parent);
            block.anchorMin = new Vector2(0f, 0f);
            block.anchorMax = new Vector2(0f, 0f);
            block.pivot = new Vector2(0f, 0f);
            block.sizeDelta = new Vector2(700f, 90f);
            block.anchoredPosition = new Vector2(MenuTheme.Metrics.ScreenMarginLeft * 0.5f,
                                                 MenuTheme.Metrics.ScreenMarginBottom * 0.5f);

            _findings = MenuUi.CreateTrackedText("Findings", block, string.Empty,
                                                 MenuTheme.Metrics.SettingHelpSize,
                                                 MenuTheme.Palette.Accent,
                                                 MenuTheme.Metrics.SubtitleTracking, TextAnchor.LowerLeft);
            MenuUi.Row(_findings.rectTransform, 0f, 40f);

            _falls = MenuUi.CreateText("Falls", block, string.Empty, MenuTheme.Metrics.SettingHelpSize,
                                       MenuTheme.Palette.InkFaint, TextAnchor.LowerLeft);
            MenuUi.Row(_falls.rectTransform, 44f, 40f);
        }

        private void BuildPauseButton(RectTransform parent, MenuNav nav)
        {
            _pauseButton = MenuButton.Create("Pause", parent, MenuStrings.Get("hud.pause"),
                                             MenuButton.Weight.Secondary);
            _pauseButton.Nav = nav;

            RectTransform rect = _pauseButton.Rect;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(300f, MenuTheme.Metrics.InfoRowHeight);
            rect.anchoredPosition = new Vector2(-MenuTheme.Metrics.ScreenMarginRight * 0.5f,
                                               -MenuTheme.Metrics.ScreenMarginTop * 0.4f);

            _pauseButton.ApplyLayout(MenuTheme.Metrics.CaretOutdent, MenuTheme.Metrics.CaretOutdent);
            _pauseButton.SetRuleWidth(260f);
            _pauseButton.Activated = OnPausePressed;

            nav.Add(_pauseButton, 0, 0);
            nav.Select(_pauseButton, false);
        }

        private void BuildToast(RectTransform parent)
        {
            _toast = MenuUi.CreateNode("Toast", parent);
            _toast.anchorMin = new Vector2(0.5f, 1f);
            _toast.anchorMax = new Vector2(0.5f, 1f);
            _toast.pivot = new Vector2(0.5f, 1f);
            _toast.sizeDelta = new Vector2(760f, 130f);
            _toast.anchoredPosition = new Vector2(0f, -(MenuTheme.Metrics.ScreenMarginTop * 0.4f + 40f));

            _toastGroup = MenuUi.Group(_toast);
            _toastGroup.alpha = 0f;
            _toastGroup.blocksRaycasts = false;
            _toastGroup.interactable = false;

            Image rule = MenuUi.CreateHairline("Rule", _toast, MenuTheme.Palette.Accent, 2f);
            rule.rectTransform.anchorMin = new Vector2(0f, 1f);
            rule.rectTransform.anchorMax = new Vector2(1f, 1f);
            rule.rectTransform.pivot = new Vector2(0.5f, 1f);
            rule.rectTransform.sizeDelta = new Vector2(0f, 2f);
            rule.rectTransform.anchoredPosition = Vector2.zero;

            _toastTitle = MenuUi.CreateTrackedText("Label", _toast, string.Empty,
                                                   MenuTheme.Metrics.SectionLabelSize,
                                                   MenuTheme.Palette.Accent,
                                                   MenuTheme.Metrics.SectionLabelTracking, TextAnchor.UpperCenter);
            MenuUi.Row(_toastTitle.rectTransform, 12f, 34f);

            _toastBody = MenuUi.CreateText("Title", _toast, string.Empty, MenuTheme.Metrics.ParagraphSize,
                                           MenuTheme.Palette.Ink, TextAnchor.UpperCenter);
            MenuUi.Row(_toastBody.rectTransform, 50f, 50f);

            _toast.gameObject.SetActive(false);
        }

        private void Subscribe()
        {
            if (_session == null) return;

            _session.Events.Subscribe<WorldFlagSetEvent>(OnWorldFlag);
            _session.Events.Subscribe<PlayerDiedEvent>(OnPlayerDied);
            AchievementService.Unlocked += OnAchievementUnlocked;
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Events.Unsubscribe<WorldFlagSetEvent>(OnWorldFlag);
                _session.Events.Unsubscribe<PlayerDiedEvent>(OnPlayerDied);
            }

            AchievementService.Unlocked -= OnAchievementUnlocked;
        }

        private void OnWorldFlag(WorldFlagSetEvent _)
        {
            Refresh();
        }

        private void OnPlayerDied(PlayerDiedEvent _)
        {
            Refresh();
        }

        private void OnAchievementUnlocked(AchievementDefinition achievement)
        {
            Announce(achievement);
        }

        private void OnPausePressed()
        {
            MenuAudio.Confirm();
            System.Action handler = PauseRequested;
            if (handler != null) handler();
        }

        private void Update()
        {
            // Keep the interface size and the safe area honest when the window changes shape: this
            // is one rect comparison, and it is what makes the strip land in the cutout-free box on
            // a phone held the other way up.
            if (_canvas != null && _canvas.ContentRoot.rect.size != _box)
            {
                _box = _canvas.ContentRoot.rect.size;
                _canvas.SetScale(MenuPreferences.ClampedUiScale);
                if (_safeArea != null) _safeArea.Mode = MenuPreferences.SafeArea;
            }

            if (_toast == null || !_toast.gameObject.activeSelf) return;

            float remaining = _toastUntil - Time.unscaledTime;
            if (remaining <= 0f)
            {
                _toast.gameObject.SetActive(false);
                return;
            }

            float elapsed = ToastSeconds - remaining;
            float alpha = elapsed < ToastFadeSeconds
                ? Mathf.Clamp01(elapsed / ToastFadeSeconds)
                : Mathf.Clamp01(remaining / ToastFadeSeconds);
            _toastGroup.alpha = alpha;
        }

        /// <summary>
        /// How many of the region's secrets this run has found.
        /// </summary>
        /// <remarks>
        /// Counted from the session's own record of which flags are raised, not from a counter that
        /// could drift: the discoveries are world flags, and the total comes from the level that is
        /// running. A region whose level data changes changes both numbers together.
        /// </remarks>
    }
}
