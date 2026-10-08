using Aether.Gameplay.Menus;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using Aether.Gameplay.Presentation;
using Aether.Gameplay.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Interface
{
    /// <summary>
    /// What the player is shown when the region is finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is a summary and a choice, not a fanfare.</b> The Greenway's exit records a world flag
    /// and the region is over; the next region is not in this build. So this panel says exactly that:
    /// what the run did — time, deaths, findings out of the region's real total — and the two things
    /// that still exist, which are walking back into the region and returning to the menu with the
    /// run saved.
    /// </para>
    /// <para>
    /// <b>The region keeps running behind it.</b> Nobody is teleported, nothing is reloaded and the
    /// enemies are not reset: the panel is a layer over the world, and "keep exploring" simply lifts
    /// it. That is why it is a panel and not a scene.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RegionCompletePanel : MonoBehaviour
    {
        private const int SortingOrder = 570;

        private MenuCanvas _canvas;
        private SafeAreaFitter _safeArea;
        private ScrollRect _scroll;
        private RectTransform _safeRoot;
        private RectTransform _content;
        private RectTransform _column;
        private Vector2 _safeBox;
        private CanvasGroup _group;
        private RectTransform _overlay;
        private MenuEntryPanel _rows;
        private MenuConfirmPanel _confirm;
        private Text _body;
        private Text _stats;
        private MenuNav _nav;

        private GameSession _session;
        private System.Action _onKeepExploring;
        private System.Action _onLeave;

        /// <summary>The body paragraph, for a test to read.</summary>
        public Text Body
        {
            get { return _body; }
        }

        /// <summary>The numbers line, for a test to read.</summary>
        public Text Stats
        {
            get { return _stats; }
        }

        /// <summary>The rows, for a test to press.</summary>
        public MenuEntryPanel Rows
        {
            get { return _rows; }
        }

        /// <summary>Reports a save failure without hiding the completed run's summary or choices.</summary>
        public void ShowSaveFailure()
        {
            if (_body != null) _body.text = MenuStrings.Get("complete.saveFailed");
        }

        /// <summary>Whether the panel is on screen.</summary>
        public bool IsOpen
        {
            get { return _overlay != null && _overlay.gameObject.activeSelf; }
        }

        /// <summary>Builds the panel. It starts closed.</summary>
        public static RegionCompletePanel Create(GameSession session, System.Action onKeepExploring,
                                                 System.Action onLeave)
        {
            var host = new GameObject("Region Complete", typeof(RectTransform));
            RegionCompletePanel panel = host.AddComponent<RegionCompletePanel>();
            panel.Build(session, onKeepExploring, onLeave);
            return panel;
        }

        /// <summary>Shows the summary, with the run's numbers read now.</summary>
        public void Open()
        {
            if (_overlay == null) return;

            Refresh();
            _overlay.gameObject.SetActive(true);
            Layout();
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            MenuReveal.Attach(_rows.Rect).Play(MenuTheme.Motion.Entrance(MenuPreferences.ReducedMotion),
                                               MenuPreferences.ReducedMotion);
            _nav.SelectFirst();
        }

        /// <summary>Hides the panel without leaving the region.</summary>
        public void Close()
        {
            if (_overlay == null) return;

            _confirm.Cancel();
            _overlay.gameObject.SetActive(false);
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
        }

        private void Build(GameSession session, System.Action onKeepExploring, System.Action onLeave)
        {
            _session = session;
            _onKeepExploring = onKeepExploring;
            _onLeave = onLeave;

            MenuArt.EnsureBuilt();
            UiEventSystem.Ensure();

            _canvas = MenuCanvas.Create("Complete Canvas", SortingOrder);
            _canvas.GameObject.transform.SetParent(transform, false);
            _canvas.SetScale(MenuPreferences.ClampedUiScale);

            _overlay = MenuUi.CreateNode("Overlay", _canvas.Root);
            MenuUi.Stretch(_overlay);
            _group = MenuUi.Group(_overlay);

            Image scrim = MenuUi.Fill("Scrim", _overlay, MenuTheme.Palette.Scrim, true);
            MenuUi.Stretch(scrim.rectTransform);

            _safeRoot = MenuUi.CreateNode("Safe", _overlay);
            MenuUi.Stretch(_safeRoot);
            _safeArea = _safeRoot.gameObject.AddComponent<SafeAreaFitter>();
            _safeArea.Mode = MenuPreferences.SafeArea;
            _safeArea.DimensionsChanged += OnSafeAreaDimensionsChanged;

            _scroll = MenuUi.CreateScroll("Complete Scroll", _safeRoot, out _content);
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

            _rows = MenuEntryPanel.Create("Rows", _column, _nav, "complete.title", 1);
            MenuButton keep = _rows.AddEntry("Keep", MenuStrings.Get("complete.continue"),
                                             MenuButton.Weight.Primary);
            MenuButton leave = _rows.AddEntry("Leave", MenuStrings.Get("complete.menu"),
                                              MenuButton.Weight.Secondary);

            keep.Activated = () =>
            {
                MenuAudio.Back();
                Close();
                if (_onKeepExploring != null) _onKeepExploring();
            };

            leave.Activated = () =>
            {
                MenuAudio.Confirm();
                _confirm.Open(MenuStrings.Get("complete.menu.title"),
                              MenuStrings.Get("complete.menu.body"),
                              () =>
                              {
                                  if (_onLeave != null) _onLeave();
                              });
            };

            _body = MenuUi.CreateText("Body", _column, MenuStrings.Get("complete.body"),
                                      MenuTheme.Metrics.ParagraphSize, MenuTheme.Palette.InkMuted,
                                      TextAnchor.UpperCenter);
            _stats = MenuUi.CreateText("Stats", _column, string.Empty, MenuTheme.Metrics.SettingHelpSize,
                                       MenuTheme.Palette.Accent, TextAnchor.UpperCenter);

            _confirm = MenuConfirmPanel.Create("Confirm", _overlay, _nav);
            _confirm.Closed = OnConfirmClosed;

            Layout();
            _overlay.gameObject.SetActive(false);
            MenuReveal.Attach(_rows.Rect).Settle();
        }

        private void Layout()
        {
            if (_rows == null || _safeRoot == null || _scroll == null) return;

            _safeBox = _safeRoot.rect.size;
            float safeWidth = _safeBox.x;
            float safeHeight = _safeBox.y;
            if (safeWidth <= 0f) safeWidth = Screen.width;
            if (safeHeight <= 0f) safeHeight = Screen.height;

            float width = Mathf.Min(900f, Mathf.Max(280f, safeWidth - 48f));
            _rows.Layout(width, safeHeight);
            _rows.Rect.anchorMin = new Vector2(0f, 1f);
            _rows.Rect.anchorMax = new Vector2(0f, 1f);
            _rows.Rect.pivot = new Vector2(0f, 1f);
            _rows.Rect.anchoredPosition = Vector2.zero;

            MenuUi.Row(_body.rectTransform, _rows.Height + 28f, 90f);
            MenuUi.Row(_stats.rectTransform, _rows.Height + 126f, 60f);

            float desiredHeight = _rows.Height + 200f;
            float contentHeight = Mathf.Max(safeHeight, desiredHeight + 32f);
            _column.sizeDelta = new Vector2(width, desiredHeight);
            _column.anchoredPosition = new Vector2(0f, -Mathf.Max(16f, (contentHeight - desiredHeight) * 0.5f));
            MenuUi.SetContentHeight(_content, contentHeight);
        }

        private void OnSafeAreaDimensionsChanged()
        {
            if (_overlay != null && _overlay.gameObject.activeSelf) Layout();
        }

        private void OnDestroy()
        {
            if (_safeArea != null) _safeArea.DimensionsChanged -= OnSafeAreaDimensionsChanged;
        }

        private void OnConfirmClosed()
        {
            if (IsOpen) _nav.SelectFirst();
        }

        /// <summary>Reads the run's own numbers, which is the only place they can come from.</summary>
        private void Refresh()
        {
            if (_body != null) _body.text = MenuStrings.Get("complete.body");
            if (_session == null || _session.Save.Meta == null) return;

            _stats.text = MenuStrings.Format("complete.stats",
                                             RunSummary.Clock(RunSummary.PlaySeconds(_session)),
                                             RunSummary.Deaths(_session),
                                             RunSummary.Findings(_session),
                                             RunSummary.FindingsTotal());
        }

    }
}
