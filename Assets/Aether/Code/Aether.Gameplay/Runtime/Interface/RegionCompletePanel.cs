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

            _safeArea = _canvas.SafeRoot.gameObject.AddComponent<SafeAreaFitter>();
            _safeArea.Mode = MenuPreferences.SafeArea;

            _overlay = MenuUi.CreateNode("Overlay", _canvas.Root);
            MenuUi.Stretch(_overlay);
            _group = MenuUi.Group(_overlay);

            Image scrim = MenuUi.Fill("Scrim", _overlay, MenuTheme.Palette.Scrim, true);
            MenuUi.Stretch(scrim.rectTransform);

            _nav = new MenuNav();

            RectTransform column = MenuUi.CreateNode("Column", _overlay);
            column.anchorMin = new Vector2(0.5f, 0.5f);
            column.anchorMax = new Vector2(0.5f, 0.5f);
            column.pivot = new Vector2(0.5f, 0.5f);
            column.sizeDelta = new Vector2(900f, 780f);

            _rows = MenuEntryPanel.Create("Rows", column, _nav, "complete.title", 1);
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

            _body = MenuUi.CreateText("Body", column, MenuStrings.Get("complete.body"),
                                      MenuTheme.Metrics.ParagraphSize, MenuTheme.Palette.InkMuted,
                                      TextAnchor.UpperCenter);
            _stats = MenuUi.CreateText("Stats", column, string.Empty, MenuTheme.Metrics.SettingHelpSize,
                                       MenuTheme.Palette.Accent, TextAnchor.UpperCenter);

            _confirm = MenuConfirmPanel.Create("Confirm", _overlay, _nav);
            _confirm.Closed = OnConfirmClosed;

            Layout();
            _overlay.gameObject.SetActive(false);
            MenuReveal.Attach(_rows.Rect).Settle();
        }

        private void Layout()
        {
            RectTransform column = (RectTransform)_rows.transform.parent;
            float width = column.sizeDelta.x;

            _rows.Layout(width, 0f);
            _rows.Rect.anchorMin = new Vector2(0f, 1f);
            _rows.Rect.anchorMax = new Vector2(0f, 1f);
            _rows.Rect.pivot = new Vector2(0f, 1f);
            _rows.Rect.anchoredPosition = Vector2.zero;

            MenuUi.Row(_body.rectTransform, _rows.Height + 28f, 90f);
            MenuUi.Row(_stats.rectTransform, _rows.Height + 126f, 60f);

            column.sizeDelta = new Vector2(width, _rows.Height + 200f);
            column.anchoredPosition = Vector2.zero;
        }

        private void OnConfirmClosed()
        {
            if (IsOpen) _nav.SelectFirst();
        }

        /// <summary>Reads the run's own numbers, which is the only place they can come from.</summary>
        private void Refresh()
        {
            if (_session == null || _session.Save.Meta == null) return;

            _stats.text = MenuStrings.Format("complete.stats",
                                             RunSummary.Clock(RunSummary.PlaySeconds(_session)),
                                             RunSummary.Deaths(_session),
                                             RunSummary.Findings(_session),
                                             RunSummary.FindingsTotal());
        }

    }
}
