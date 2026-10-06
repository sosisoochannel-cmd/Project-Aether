using Aether.Gameplay.Flow;
using Aether.Gameplay.Menus;
using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Interface
{
    /// <summary>
    /// The cover the region puts over itself while it is being built.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why gameplay owns a veil when the menu already has one.</b> The menu's veil covers the
    /// scene load and lifts as soon as the new scene has drawn a frame — which is correct for a
    /// screen that is ready the moment it exists. A region is not: the level file is parsed, geometry,
    /// enemies, checkpoints and the player are built, and the camera is placed, all of which happens
    /// after the scene is live. Without a cover of its own the player would watch that happen: a
    /// frame of empty sky, then terrain appearing, then a player dropping in. So this veil is taken
    /// <i>before</i> the build and lifted <i>after</i> the first frame that has a player in it.
    /// </para>
    /// <para>
    /// <b>It is also the error screen, and that is the point.</b> If the level data cannot be read
    /// there is nothing to play, and the honest answer is a screen that says so and offers the way
    /// back to the menu — not a black viewport, which is what a failed boot looks like if nobody
    /// plans for it. <see cref="Fail"/> is that screen.
    /// </para>
    /// <para>
    /// It lives across the scene load (<c>DontDestroyOnLoad</c>) and sorts above the menu's veil, so
    /// the hand-over is covered from the menu's last frame to the region's first: there is no instant
    /// where the screen is empty, which is the one rule this whole class exists to keep.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GameplayCurtain : MonoBehaviour
    {
        /// <summary>Above the menu's veil (500): the region's cover is the one on top.</summary>
        private const int SortingOrder = 600;

        private static GameplayCurtain _instance;

        private CanvasGroup _group;
        private Text _title;
        private Text _note;
        private RectTransform _errorBlock;
        private MenuNav _nav;

        /// <summary>The curtain, created on demand and kept across the load it covers.</summary>
        public static GameplayCurtain Instance
        {
            get
            {
                if (_instance != null) return _instance;

                var host = new GameObject("Gameplay Curtain");
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<GameplayCurtain>();
                _instance.Build();
                return _instance;
            }
        }

        /// <summary>Whether the curtain is covering the screen right now.</summary>
        public static bool IsUp
        {
            get { return _instance != null && _instance._group != null && _instance._group.alpha > 0.01f; }
        }

        /// <summary>
        /// Covers the screen before a region is built.
        /// </summary>
        /// <remarks>
        /// Called at the very start of the boot, before the level is parsed, so the cover is up in
        /// the same frame the previous one lifts. It is idempotent: a second call re-labels the
        /// curtain rather than building a second one, which is what lets the boot be re-run.
        /// </remarks>
        public static void Hold(string titleKey, string noteKey)
        {
            GameplayCurtain curtain = Instance;
            curtain.SetError(false);
            curtain.SetText(titleKey, noteKey);
            curtain.Show();
        }

        /// <summary>Lifts the curtain, one frame after the region is ready to be looked at.</summary>
        public static void Reveal()
        {
            if (_instance == null) return;
            _instance.StartCoroutine(_instance.RevealRoutine());
        }

        /// <summary>Shows the honest failure screen, with the way back to the menu on it.</summary>
        public static void Fail(string detail)
        {
            GameplayCurtain curtain = Instance;
            curtain.SetText("error.title", "error.body");
            curtain.SetError(true);
            curtain.Show();

            if (!string.IsNullOrEmpty(detail)) Debug.LogError("[region] " + detail, curtain);
        }

        /// <summary>Lifts the curtain immediately. Used by a test, and by a scene that owns its own cover.</summary>
        public static void Drop()
        {
            if (_instance == null) return;
            _instance.StopAllCoroutines();
            _instance.SetAlpha(0f, false);
        }

        /// <summary>Forgets the curtain. Tests build and destroy real objects; this is how they detach.</summary>
        public static void ResetForTests()
        {
            if (_instance == null) return;
            Destroy(_instance.gameObject);
            _instance = null;
        }

        private void Build()
        {
            MenuArt.EnsureBuilt();

            var canvasHost = new GameObject("Curtain Canvas", typeof(RectTransform));
            canvasHost.transform.SetParent(transform, false);

            Canvas canvas = canvasHost.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvasHost.AddComponent<GraphicRaycaster>();

            // Opaque, not the menu's scrim: a cover that lets a half-built level show through is not
            // a cover, and this one is over the menu's veil rather than under it.
            Image veil = MenuUi.Fill("Veil", canvasHost.transform, MenuTheme.Palette.Ground, true);
            veil.raycastTarget = true;

            _group = canvasHost.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = true;
            _group.interactable = true;
            canvasHost.SetActive(false);

            RectTransform safe = MenuUi.CreateNode("Safe", canvasHost.transform);
            MenuUi.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>().Mode = MenuPreferences.SafeArea;

            RectTransform block = MenuUi.CreateNode("Centre", safe);
            block.anchorMin = new Vector2(0.5f, 0.5f);
            block.anchorMax = new Vector2(0.5f, 0.5f);
            block.pivot = new Vector2(0.5f, 0.5f);
            block.sizeDelta = new Vector2(1100f, 260f);

            _title = MenuUi.CreateTrackedText("Title", block, string.Empty,
                                               MenuTheme.Metrics.ScreenTitleSize,
                                               MenuTheme.Palette.Ink, TextAnchor.MiddleCenter,
                                               MenuTheme.Metrics.ScreenTitleTracking);
            MenuUi.Row(_title.rectTransform, 0f, 90f);

            _note = MenuUi.CreateText("Note", block, string.Empty, MenuTheme.Metrics.ParagraphSize,
                                      MenuTheme.Palette.InkMuted, TextAnchor.UpperCenter);
            MenuUi.Row(_note.rectTransform, 96f, 120f);

            BuildErrorBlock(block);

            _nav = new MenuNav();
        }

        /// <summary>
        /// The block that only exists when the region could not start: a sentence and a way out.
        /// </summary>
        /// <remarks>
        /// Built with the curtain rather than on demand, so a failure never has to allocate anything
        /// to explain itself, and switched off until it is needed. The row is a real button on a real
        /// event system — it leaves for the menu through the one transition system the game has — so
        /// a failed boot is a screen the player can act on rather than a dead end.
        /// </remarks>
        private void BuildErrorBlock(RectTransform parent)
        {
            _errorBlock = MenuUi.CreateNode("Error", parent);
            MenuUi.Row(_errorBlock, 220f, 200f);

            MenuButton row = MenuButton.Create("ReturnToMenu", _errorBlock,
                                               MenuStrings.Get("error.menu"), MenuButton.Weight.Primary);
            row.Nav = _nav;
            row.Rect.anchorMin = new Vector2(0.5f, 1f);
            row.Rect.anchorMax = new Vector2(0.5f, 1f);
            row.Rect.pivot = new Vector2(0.5f, 1f);
            row.Rect.sizeDelta = new Vector2(720f, MenuTheme.Metrics.PrimaryPitch);
            row.Rect.anchoredPosition = Vector2.zero;
            row.ApplyLayout(0f, 0f);
            row.SetRuleWidth(720f);
            row.Activated = () =>
            {
                MenuAudio.Confirm();
                _nav.Select(row, true);
                LeaveToMenu();
            };

            _errorBlock.gameObject.SetActive(false);
        }

        /// <summary>Leaves the region for the menu, through the one transition system.</summary>
        /// <remarks>
        /// Also the path the pause menu's "save and leave" ends on, which is why it is public: there
        /// is one way out of a region, and every screen that offers one calls this.
        /// </remarks>
        public static void LeaveToMenu()
        {
            if (!Application.CanStreamedLevelBeLoaded(Scenes.MainMenu))
            {
                Debug.LogError(
                    "[region] the main menu is not in the build settings, so there is nowhere to go. " +
                    "This is a build configuration problem: check EditorBuildSettings.", Instance);
                return;
            }

            // The menu's transition owns the fade, the load and the input lock, and it survives the
            // load. Reusing it is what keeps the way out and the way in the same transition rather
            // than two that look almost the same.
            MenuTransition.Instance.GoToScene(Scenes.MainMenu);
        }

        private void SetText(string titleKey, string noteKey)
        {
            if (_title != null) _title.text = MenuUi.Track(MenuStrings.Get(titleKey),
                                                           MenuTheme.Metrics.ScreenTitleTracking);
            if (_note != null) _note.text = MenuStrings.Get(noteKey);
        }

        private void SetError(bool failed)
        {
            if (_errorBlock != null) _errorBlock.gameObject.SetActive(failed);

            // The loading note sits under the title while the region builds and is replaced by the
            // error sentence when it cannot: two different things, never both at once.
            if (_note != null) _note.gameObject.SetActive(!failed);

            if (failed && _nav != null && _errorBlock != null)
            {
                MenuButton row = _errorBlock.GetComponentInChildren<MenuButton>(true);
                if (row != null) _nav.Select(row, true);
            }
        }

        private void Show()
        {
            if (_group == null) return;

            _group.gameObject.SetActive(true);
            SetAlpha(1f, false);
            _group.blocksRaycasts = true;
        }

        private System.Collections.IEnumerator RevealRoutine()
        {
            // One frame with the region live and drawn, then the fade: the alternative is a fade
            // that starts while the first frame is still being assembled, which shows the seam it
            // was meant to hide.
            yield return null;

            float duration = MenuTheme.Motion.ScreenFade(MenuPreferences.ReducedMotion);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / duration)), true);
                yield return null;
            }

            SetAlpha(0f, false);
        }

        private void SetAlpha(float alpha, bool interactive)
        {
            if (_group == null) return;

            _group.alpha = alpha;

            // While the cover is up, its veil absorbs every touch. When it is down, the object is
            // switched off entirely: a full-screen quad at zero alpha that still raycasts is the
            // classic way to ship a game whose buttons do not work.
            _group.blocksRaycasts = interactive || alpha > 0.01f;
            if (alpha <= 0.01f) _group.gameObject.SetActive(false);
        }
    }
}
