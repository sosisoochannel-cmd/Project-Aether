using Aether.Gameplay.Controls;
using Aether.Gameplay.Levels;
using Aether.Gameplay.Localization;
using Aether.Gameplay.Menus;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Player;
using Aether.Gameplay.Presentation;
using Aether.Gameplay.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Interface
{
    /// <summary>
    /// The pause menu: the region stops, and the player gets five real choices.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every row does something, and the list says what.</b> Resume returns to the region. Save now
    /// writes the run and says so on the row that asked. Restart region returns the player to the
    /// region's own start with the encounters restored. Settings saves first and then leaves for the
    /// the pause overlay, so changing a value does not abandon the region or its context. Save and
    /// leave writes the run and goes back to the main menu, where Continue returns the player to their
    /// last checkpoint.
    /// </para>
    /// <para>
    /// <b>Freezing is <c>Time.timeScale = 0</c>, and that is the whole of it.</b> Physics stops,
    /// fixed updates stop, the game's own touch controls are switched off with their view — so a
    /// thumb resting on the stick while the overlay opens cannot queue a jump — and the overlay's own
    /// fades run on unscaled time, which is why they still animate while the world is frozen.
    /// </para>
    /// <para>
    /// <b>It reuses the menu's panels rather than growing a second menu.</b> The rows are
    /// <see cref="MenuEntryPanel"/> and the questions are <see cref="MenuConfirmPanel"/> — the same
    /// classes the settings and credits screens use — so a pause row and a settings row are the same
    /// object, with the same press behaviour, the same caret, the same contrast handling and the same
    /// keyboard and gamepad navigation.
    /// </para>
    /// <para>
    /// <b>The pause key is not read here.</b> One reader, in <see cref="GameplayShell"/>, decides
    /// whether a press opens or closes this overlay; two readers of the same key would open and close
    /// it in the same frame.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GameplayPause : MonoBehaviour
    {
        private const int SortingOrder = 560;

        /// <summary>How long "SAVED" stays on the row after an explicit save.</summary>
        private const float SavedNoteSeconds = 1.6f;

        private MenuCanvas _canvas;
        private SafeAreaFitter _safeArea;
        private MenuEntryPanel _rows;
        private MenuConfirmPanel _confirm;
        private MenuButton _resumeRow;
        private MenuButton _saveRow;
        private MenuButton _restartRow;
        private MenuButton _settingsRow;
        private MenuButton _leaveRow;
        private Text _stats;
        private MenuNav _nav;
        private CanvasGroup _group;
        private RectTransform _overlay;

        // Settings is a contextual overlay, not a trip to the title screen. It reuses the same
        // catalogue-driven SettingsScreen on a second canvas and returns to this pause overlay.
        private MenuCanvas _settingsCanvas;
        private MenuSystem _settingsSystem;
        private MenuInput _settingsInput;
        private bool _settingsOpen;
        private GameplayHud _hud;
        private float _savedNoteUntil = float.NegativeInfinity;
        private bool _open;

        private GameSession _session;
        private LevelDirector _director;
        private TouchControlsView _touchControls;

        /// <summary>Raised when the overlay closes and play resumes.</summary>
        public System.Action Resumed;

        /// <summary>Whether this overlay is up.</summary>
        public bool IsOpen
        {
            get { return _open; }
        }

        /// <summary>True while the contextual settings overlay is in front of pause.</summary>
        public bool IsSettingsOpen
        {
            get { return _settingsOpen; }
        }

        /// <summary>The rows, in order, for a test to read and press.</summary>
        public MenuEntryPanel Rows
        {
            get { return _rows; }
        }

        /// <summary>The questions panel, for a test to drive a confirmation.</summary>
        public MenuConfirmPanel Confirm
        {
            get { return _confirm; }
        }

        /// <summary>Builds the overlay. It starts closed, and the region keeps running.</summary>
        public static GameplayPause Create(GameSession session, LevelDirector director,
                                           TouchControlsView touchControls, GameplayHud hud)
        {
            var host = new GameObject("Gameplay Pause", typeof(RectTransform));
            GameplayPause pause = host.AddComponent<GameplayPause>();
            pause.Build(session, director, touchControls, hud);
            return pause;
        }

        /// <summary>Opens the pause menu.</summary>
        public void Open()
        {
            if (_open || _overlay == null) return;
            _open = true;

            // The region stops. The overlay's own timing is unscaled, so this freezes the world
            // without freezing the interface that is on top of it.
            Time.timeScale = 0f;

            if (_touchControls != null) _touchControls.enabled = false;
            if (_hud != null) _hud.SetVisible(false);

            Refresh();
            _overlay.gameObject.SetActive(true);
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            MenuReveal.Attach(_rows.Rect).Play(MenuTheme.Motion.Entrance(MenuPreferences.ReducedMotion),
                                               MenuPreferences.ReducedMotion);
            _nav.SelectFirst();
        }

        /// <summary>Closes the overlay and gives the region back.</summary>
        public void Close()
        {
            if (!_open) return;
            if (_settingsOpen)
            {
                _settingsOpen = false;
                if (_settingsInput != null) _settingsInput.gameObject.SetActive(false);
                if (_settingsCanvas != null) _settingsCanvas.GameObject.SetActive(false);
            }

            _open = false;
            _confirm.Cancel();

            Time.timeScale = 1f;

            if (_touchControls != null) _touchControls.enabled = true;
            if (_hud != null) _hud.SetVisible(true);

            _overlay.gameObject.SetActive(false);
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            System.Action handler = Resumed;
            if (handler != null) handler();
        }

        /// <summary>Writes the run out now, and says so on the row that asked.</summary>
        public bool SaveNow()
        {
            bool written = SaveHost.SaveNow();
            _savedNoteUntil = Time.unscaledTime + SavedNoteSeconds;
            _saveRow.SetMeta(MenuStrings.Get(written ? "pause.saved" : "data.info.none"));
            return written;
        }

        private void Build(GameSession session, LevelDirector director, TouchControlsView touchControls,
                           GameplayHud hud)
        {
            _session = session;
            _director = director;
            _touchControls = touchControls;
            _hud = hud;

            MenuArt.EnsureBuilt();
            UiEventSystem.Ensure();

            _canvas = MenuCanvas.Create("Pause Canvas", SortingOrder);
            _canvas.GameObject.transform.SetParent(transform, false);
            _canvas.SetScale(MenuPreferences.ClampedUiScale);

            _safeArea = _canvas.SafeRoot.gameObject.AddComponent<SafeAreaFitter>();
            _safeArea.Mode = MenuPreferences.SafeArea;

            _overlay = MenuUi.CreateNode("Overlay", _canvas.Root);
            MenuUi.Stretch(_overlay);
            _group = MenuUi.Group(_overlay);

            // Opaque enough that the region does not read through the rows, and not a black screen:
            // the strip behind it is switched off while this is up, so there is one thing on screen.
            Image scrim = MenuUi.Fill("Scrim", _overlay, MenuTheme.Palette.Scrim, true);
            MenuUi.Stretch(scrim.rectTransform);

            _nav = new MenuNav();

            RectTransform column = MenuUi.CreateNode("Column", _overlay);
            column.anchorMin = new Vector2(0.5f, 0.5f);
            column.anchorMax = new Vector2(0.5f, 0.5f);
            column.pivot = new Vector2(0.5f, 0.5f);
            column.sizeDelta = new Vector2(820f, 760f);

            _rows = MenuEntryPanel.Create("Rows", column, _nav, "pause.title", 1);
            _resumeRow = _rows.AddEntry("Resume", MenuStrings.Get("pause.resume"), MenuButton.Weight.Primary);
            _saveRow = _rows.AddEntry("Save", MenuStrings.Get("pause.save"), MenuButton.Weight.Secondary);
            _restartRow = _rows.AddEntry("Restart", MenuStrings.Get("pause.restart"), MenuButton.Weight.Secondary);
            _settingsRow = _rows.AddEntry("Settings", MenuStrings.Get("pause.settings"), MenuButton.Weight.Secondary);
            _leaveRow = _rows.AddEntry("Leave", MenuStrings.Get("pause.menu"), MenuButton.Weight.Secondary);

            _resumeRow.Activated = () =>
            {
                MenuAudio.Back();
                Close();
            };

            _saveRow.Activated = () =>
            {
                MenuAudio.Adjust();
                SaveNow();
            };

            _restartRow.Activated = () => Ask("pause.restart.title", "pause.restart.body", RestartRegion);
            _leaveRow.Activated = () => Ask("pause.leave.title", "pause.leave.body", LeaveRegion);

            _settingsRow.Activated = OpenSettings;

            _stats = MenuUi.CreateText("Stats", column, string.Empty, MenuTheme.Metrics.SettingHelpSize,
                                       MenuTheme.Palette.InkMuted, TextAnchor.MiddleCenter);

            _confirm = MenuConfirmPanel.Create("Confirm", _overlay, _nav);
            _confirm.Closed = OnConfirmClosed;

            PlaceColumn();
            BuildSettingsOverlay();

            _overlay.gameObject.SetActive(false);
            MenuReveal.Attach(_rows.Rect).Settle();
            MenuPreferences.Subscribe(OnSettingChanged);
            LanguageService.Changed += OnLanguageChanged;
        }

        /// <summary>
        /// Builds the same settings screen used by the main menu, but keeps it in the paused region
        /// so Back returns to the pause choices instead of loading MainMenu.
        /// </summary>
        private void BuildSettingsOverlay()
        {
            // Keep it below the persistent transition veil so credits/settings navigation still uses
            // the same cover without leaving the gameplay pause canvas above the fade.
            _settingsCanvas = MenuCanvas.Create("Pause Settings Canvas", 480);
            _settingsCanvas.GameObject.transform.SetParent(transform, false);
            _settingsCanvas.SetScale(MenuPreferences.ClampedUiScale);

            SafeAreaFitter safe = _settingsCanvas.SafeRoot.gameObject.AddComponent<SafeAreaFitter>();
            safe.Mode = MenuPreferences.SafeArea;

            _settingsInput = MenuInput.Create(transform);
            _settingsSystem = MenuSystem.Create(_settingsCanvas, _settingsInput);
            _settingsSystem.BackFallback = CloseSettings;

            _settingsInput.gameObject.SetActive(false);
            _settingsCanvas.GameObject.SetActive(false);
        }

        private void OpenSettings()
        {
            if (!_open || _settingsOpen || _settingsSystem == null) return;

            MenuAudio.Confirm();
            SaveHost.SaveNow();
            _settingsOpen = true;
            _overlay.gameObject.SetActive(false);
            _settingsInput.gameObject.SetActive(true);
            _settingsCanvas.GameObject.SetActive(true);

            Canvas.ForceUpdateCanvases();
            Rect box = _settingsCanvas.ContentRoot.rect;
            _settingsSystem.Layout(box.width, box.height);
            _settingsSystem.Show(MenuScreenId.Settings, true);
        }

        private void CloseSettings()
        {
            if (!_settingsOpen) return;

            MenuAudio.Back();
            _settingsOpen = false;
            _settingsInput.gameObject.SetActive(false);
            _settingsCanvas.GameObject.SetActive(false);
            _overlay.gameObject.SetActive(true);
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            _nav.SelectFirst();
        }

        private void OnSettingChanged(string id)
        {
            if (_canvas != null) _canvas.SetScale(MenuPreferences.ClampedUiScale);
            if (_safeArea != null) _safeArea.Mode = MenuPreferences.SafeArea;
            if (_settingsCanvas != null) _settingsCanvas.SetScale(MenuPreferences.ClampedUiScale);

            if (_settingsSystem != null)
            {
                Rect box = _settingsCanvas.ContentRoot.rect;
                _settingsSystem.Layout(box.width, box.height);
                _settingsSystem.Refresh();
            }
        }

        /// <summary>
        /// Stacks the section label, the rows and the stats line inside the column.
        /// </summary>
        /// <remarks>
        /// The rows panel is laid out by its own class, so this only has to say where the three
        /// blocks sit: label and rows from the top of the column, stats under them. It runs again on
        /// every open, which is what makes the overlay right the second time the window is a
        /// different shape.
        /// </remarks>
        private void PlaceColumn()
        {
            RectTransform column = (RectTransform)_rows.transform.parent;
            float width = column.sizeDelta.x;

            _rows.Layout(width, 0f);
            _rows.Rect.anchorMin = new Vector2(0f, 1f);
            _rows.Rect.anchorMax = new Vector2(0f, 1f);
            _rows.Rect.pivot = new Vector2(0f, 1f);
            _rows.Rect.anchoredPosition = Vector2.zero;

            if (_stats != null)
            {
                MenuUi.Row(_stats.rectTransform, _rows.Height + 24f, 60f);
            }

            column.sizeDelta = new Vector2(width, _rows.Height + 96f);
        }

        /// <summary>Asks a question before doing something that cannot be undone.</summary>
        private void Ask(string titleKey, string bodyKey, System.Action onConfirm)
        {
            MenuAudio.Confirm();
            _confirm.Open(MenuStrings.Get(titleKey), MenuStrings.Get(bodyKey), onConfirm);
        }

        private void OnConfirmClosed()
        {
            if (_open) _nav.SelectFirst();
        }

        /// <summary>
        /// Returns the player to the region's own start, with the encounters restored.
        /// </summary>
        /// <remarks>
        /// Not the checkpoint: the checkpoint is where the player already stands, and a restart that
        /// puts them back where they were is a button that does nothing. The session's active
        /// checkpoint is cleared at the same time, so a death after a restart also returns to the
        /// start — the run really has begun again from the region's first frame.
        /// </remarks>
        private void RestartRegion()
        {
            if (_session != null) _session.World.ActiveCheckpointId = string.Empty;
            if (_director != null) _director.RestartRegion();

            // Restart is a persistent state change, not only a teleport. Write the cleared
            // checkpoint before returning control so a crash, force-close or Android suspend cannot
            // resurrect the old checkpoint on the next boot.
            SaveHost.SaveNow();

            MenuAudio.Confirm();
            Close();
        }

        /// <summary>Writes the run down and leaves for the main menu.</summary>
        private void LeaveRegion()
        {
            SaveHost.SaveNow();
            GameplayCurtain.Hold("loading.title", "loading.leaving");
            GameplayCurtain.LeaveToMenu();
        }

        private void OnDestroy()
        {
            MenuPreferences.Unsubscribe(OnSettingChanged);
            LanguageService.Changed -= OnLanguageChanged;
            if (_open && Time.timeScale == 0f) Time.timeScale = 1f;
        }

        private void OnLanguageChanged()
        {
            if (_settingsSystem != null) _settingsSystem.RequestRebuild();
        }

        private void Refresh()
        {
            if (_stats != null)
            {
                _stats.text = MenuStrings.Format("pause.stats",
                                                 RunSummary.Clock(RunSummary.PlaySeconds(_session)),
                                                 RunSummary.Findings(_session),
                                                 RunSummary.Deaths(_session));
            }

            _saveRow.SetMeta(null);
        }

        private void Update()
        {
            // The only per-frame work in the overlay: retire the "SAVED" note once it has been up
            // long enough. Nothing runs while there is no note to retire.
            if (_savedNoteUntil <= float.NegativeInfinity) return;
            if (Time.unscaledTime < _savedNoteUntil) return;

            _savedNoteUntil = float.NegativeInfinity;
            _saveRow.SetMeta(null);
        }
    }
}
