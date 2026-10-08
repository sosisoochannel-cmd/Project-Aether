using Aether.Gameplay.Controls;
using Aether.Gameplay.Flow;
using Aether.Gameplay.Levels;
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
    /// region's own start with the encounters restored. Settings saves first, opens the menu's real
    /// Settings screen, and returns to the main menu on Back; settings stay owned by the menu system.
    /// Save and leave writes the run and goes back to the main menu, where Continue returns the
    /// player to their last checkpoint.
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
        private ScrollRect _scroll;
        private RectTransform _safeRoot;
        private RectTransform _content;
        private RectTransform _column;
        private Vector2 _safeBox;
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
            PlaceColumn();
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
            _saveRow.SetMeta(MenuStrings.Get(written ? "pause.saved" : "pause.saveFailed"));
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

            _overlay = MenuUi.CreateNode("Overlay", _canvas.Root);
            MenuUi.Stretch(_overlay);
            _group = MenuUi.Group(_overlay);

            // The scrim covers the whole display; only readable controls are constrained to the
            // safe area, so notches and gesture bars never cover a pause action.
            Image scrim = MenuUi.Fill("Scrim", _overlay, MenuTheme.Palette.Scrim, true);
            MenuUi.Stretch(scrim.rectTransform);

            _safeRoot = MenuUi.CreateNode("Safe", _overlay);
            MenuUi.Stretch(_safeRoot);
            _safeArea = _safeRoot.gameObject.AddComponent<SafeAreaFitter>();
            _safeArea.Mode = MenuPreferences.SafeArea;

            _scroll = MenuUi.CreateScroll("Pause Scroll", _safeRoot, out _content);
            MenuUi.Stretch(_scroll.GetComponent<RectTransform>());

            _nav = new MenuNav();
            _nav.SelectionChanged += selected =>
            {
                if (selected != null && selected.transform.IsChildOf(_content))
                    MenuUi.ScrollIntoView(_scroll, selected);
            };

            _column = MenuUi.CreateNode("Column", _content);
            _column.anchorMin = new Vector2(0.5f, 1f);
            _column.anchorMax = new Vector2(0.5f, 1f);
            _column.pivot = new Vector2(0.5f, 1f);

            _rows = MenuEntryPanel.Create("Rows", _column, _nav, "pause.title", 1);
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

            _settingsRow.Activated = () =>
            {
                // Settings live in the menu scene. Leaving the region for them is a real trip, so the
                // run is written down first: a trip that costs progress would be a trap, and this is
                // the same promise the leave row makes, kept the same way.
                MenuAudio.Confirm();
                if (!SaveHost.SaveNow())
                {
                    _settingsRow.SetMeta(MenuStrings.Get("pause.saveFailed"));
                    return;
                }

                GameLaunch.RequestOpenSettings();
                GameplayCurtain.Hold("loading.title", "loading.leaving");
                GameplayCurtain.LeaveToMenu();
            };

            _stats = MenuUi.CreateText("Stats", _column, string.Empty, MenuTheme.Metrics.SettingHelpSize,
                                       MenuTheme.Palette.InkMuted, TextAnchor.MiddleCenter);

            _confirm = MenuConfirmPanel.Create("Confirm", _overlay, _nav);
            _confirm.Closed = OnConfirmClosed;

            PlaceColumn();

            _overlay.gameObject.SetActive(false);
            MenuReveal.Attach(_rows.Rect).Settle();
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
            if (_rows == null || _safeRoot == null || _scroll == null) return;

            _safeBox = _safeRoot.rect.size;
            float safeWidth = _safeBox.x;
            float safeHeight = _safeBox.y;
            if (safeWidth <= 0f) safeWidth = Screen.width;
            if (safeHeight <= 0f) safeHeight = Screen.height;

            float width = Mathf.Min(820f, Mathf.Max(280f, safeWidth - 48f));
            _rows.Layout(width, safeHeight);
            _rows.Rect.anchorMin = new Vector2(0f, 1f);
            _rows.Rect.anchorMax = new Vector2(0f, 1f);
            _rows.Rect.pivot = new Vector2(0f, 1f);
            _rows.Rect.anchoredPosition = Vector2.zero;

            if (_stats != null)
                MenuUi.Row(_stats.rectTransform, _rows.Height + 24f, 60f);

            float desiredHeight = _rows.Height + 96f;
            float contentHeight = Mathf.Max(safeHeight, desiredHeight + 32f);
            _column.sizeDelta = new Vector2(width, desiredHeight);
            _column.anchoredPosition = new Vector2(0f, -Mathf.Max(16f, (contentHeight - desiredHeight) * 0.5f));
            MenuUi.SetContentHeight(_content, contentHeight);
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
            if (_session == null || _director == null)
            {
                Debug.LogError("[pause] The region could not be restarted because its session is unavailable.", this);
                _restartRow.SetMeta(MenuStrings.Get("pause.restart.failed"));
                return;
            }

            // Persist the restart intent before moving the player. If the write fails, restore the
            // in-memory checkpoint and leave the region untouched; a crash must not resurrect the
            // checkpoint the confirmed restart meant to clear.
            string checkpoint = _session.World.ActiveCheckpointId;
            _session.World.ActiveCheckpointId = null;
            if (!SaveHost.SaveNow())
            {
                _session.World.ActiveCheckpointId = checkpoint;
                _restartRow.SetMeta(MenuStrings.Get("pause.saveFailed"));
                return;
            }

            _director.RestartRegion();

            Close();
        }

        /// <summary>Writes the run down and leaves for the main menu.</summary>
        private void LeaveRegion()
        {
            if (!SaveHost.SaveNow())
            {
                _leaveRow.SetMeta(MenuStrings.Get("pause.saveFailed"));
                return;
            }

            GameplayCurtain.Hold("loading.title", "loading.leaving");
            GameplayCurtain.LeaveToMenu();
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
            _restartRow.SetMeta(null);
            _settingsRow.SetMeta(MenuStrings.Get("pause.settings.meta"));
            _leaveRow.SetMeta(null);
        }

        private void Update()
        {
            if (_safeRoot != null && _safeRoot.rect.size != _safeBox) PlaceColumn();

            // The only other per-frame work in the overlay: retire the "SAVED" note once it has been
            // up long enough. Nothing runs while there is no note to retire.
            if (_savedNoteUntil <= float.NegativeInfinity) return;
            if (Time.unscaledTime < _savedNoteUntil) return;

            _savedNoteUntil = float.NegativeInfinity;
            _saveRow.SetMeta(null);
        }
    }
}
