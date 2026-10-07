using System.Collections;
using Aether.Gameplay.Menus;
using Aether.Gameplay.Sound;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Aether.Gameplay.Presentation
{
    /// <summary>
    /// The one way this game moves from one screen to another, and from a screen into a region.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One system, every transition.</b> Main menu to settings, settings back, credits, the
    /// placeholder screens, a screen to a region: all of them go through <see cref="GoTo"/> or
    /// <see cref="GoToScene"/>, so there is one fade curve, one input lock, one place where a double
    /// press can be refused, and one thing to change when the transition should feel different.
    /// A second implementation for scene loads would drift from the first within a week.
    /// </para>
    /// <para>
    /// <b>No black flash.</b> The veil fades to the menu's own scrim colour and back rather than
    /// snapping to black, and it fades <i>out</i> again after the new scene is loaded, which is what
    /// makes a fast load look continuous instead of like two unrelated frames. The veil object is
    /// marked <c>DontDestroyOnLoad</c>, so a load does not tear down the thing that is covering it —
    /// that was the actual bug this design exists to prevent.
    /// </para>
    /// <para>
    /// <b>Input is locked while it covers.</b> The property is read by the menu host, and the veil
    /// itself is a raycast target, so a finger that lands during a fade does nothing at all: no
    /// second scene load, no duplicated screen.
    /// </para>
    /// </remarks>
    public sealed class MenuTransition : MonoBehaviour
    {
        private const int VeilSortingOrder = 500;
        private const float SceneLoadTimeoutSeconds = 20f;
        private const string UiBusCue = "ui.transition";

        private static MenuTransition _instance;

        private RectTransform _veilRoot;
        private Image _veil;
        private bool _busy;

        /// <summary>The transition system, created on demand and kept across scene loads.</summary>
        public static MenuTransition Instance
        {
            get
            {
                if (_instance != null) return _instance;

                var host = new GameObject("Menu Transition");
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<MenuTransition>();
                _instance.Build();
                return _instance;
            }
        }

        /// <summary>True while a fade or a load is in progress. Everything that moves must check it.</summary>
        public bool Busy
        {
            get { return _busy; }
        }

        /// <summary>Fades the menu out and a region in, once. A second ask while busy is refused.</summary>
        public void GoToScene(string sceneName)
        {
            if (_busy || string.IsNullOrEmpty(sceneName)) return;

            // A region that is not in the build is a scene that would throw on load. Refusing here
            // leaves the menu on screen and says why, which is recoverable; the throw is not.
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError(
                    $"[menu] '{sceneName}' is not in the build settings, so the menu cannot open it. " +
                    "The menu stays where it is rather than throwing on the load.", this);
                return;
            }

            StartCoroutine(LoadRoutine(sceneName));
        }

        /// <summary>
        /// Fades out, runs an action while the screen is covered, and fades back in.
        /// </summary>
        /// <remarks>
        /// Used for the transitions that are not scene loads — a mode change, a settings reset, a
        /// future gameplay pause — so that every covered action in the game looks the same and
        /// leaves the input locked for exactly as long as it should be.
        /// </remarks>
        public void Cover(System.Action whileCovered, bool fadeBack = true)
        {
            if (_busy || whileCovered == null) return;
            StartCoroutine(CoverRoutine(whileCovered, fadeBack));
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        private void Build()
        {
            MenuArt.EnsureBuilt();

            var canvasHost = new GameObject("Veil", typeof(RectTransform));
            canvasHost.transform.SetParent(transform, false);

            Canvas canvas = canvasHost.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = VeilSortingOrder;
            canvasHost.AddComponent<GraphicRaycaster>();

            Image panel = MenuUi.Fill("Scrim", canvasHost.transform, MenuTheme.Palette.Scrim, true);
            _veil = panel;
            _veil.color = new Color(MenuTheme.Palette.Scrim.r, MenuTheme.Palette.Scrim.g,
                                    MenuTheme.Palette.Scrim.b, 0f);
            _veilRoot = (RectTransform)canvasHost.transform;

            // While the veil is up it is a raycast target, so a finger that lands during a fade is
            // absorbed by it rather than reaching a row. When it is idle the object is switched off,
            // which is also what keeps a full-screen transparent quad out of the frame.
            _veil.raycastTarget = true;
            canvasHost.SetActive(false);
        }

        private IEnumerator LoadRoutine(string sceneName)
        {
            _busy = true;
            MenuAudio.Request(UiBusCue, 0.6f);

            try
            {
                yield return Fade(1f, MenuTheme.Motion.TransitionFade(MenuPreferences.ReducedMotion));

                AsyncOperation load;
                try
                {
                    load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"[transition] Could not load '{sceneName}'. {exception.Message}", this);
                    yield return Fade(0f, MenuTheme.Motion.TransitionFade(MenuPreferences.ReducedMotion));
                    yield break;
                }

                if (load == null)
                {
                    Debug.LogError($"[transition] Unity returned no load operation for '{sceneName}'.", this);
                    yield return Fade(0f, MenuTheme.Motion.TransitionFade(MenuPreferences.ReducedMotion));
                    yield break;
                }

                float startedAt = Time.unscaledTime;
                while (!load.isDone)
                {
                    // A failed or stalled asynchronous operation must not leave a persistent veil
                    // and a permanent input lock over the only recoverable screen. Unity cannot
                    // cancel a Single-mode operation, but releasing the cover restores the current
                    // scene and gives the player a usable retry path instead of a black hang.
                    if (Time.unscaledTime - startedAt > SceneLoadTimeoutSeconds)
                    {
                        Debug.LogError($"[transition] Loading '{sceneName}' exceeded " +
                                       $"{SceneLoadTimeoutSeconds:0} seconds. The current screen was " +
                                       "restored so the player can retry.", this);
                        yield return Fade(0f, MenuTheme.Motion.TransitionFade(MenuPreferences.ReducedMotion));
                        yield break;
                    }

                    yield return null;
                }

                // One frame of the new scene before the cover lifts, so the destination's first
                // frames are not the ones the player sees appear through the fade.
                yield return null;
                yield return Fade(0f, MenuTheme.Motion.TransitionFade(MenuPreferences.ReducedMotion));
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[transition] Loading '{sceneName}' failed. {exception.Message}", this);
                if (_veil != null)
                {
                    _veil.color = new Color(_veil.color.r, _veil.color.g, _veil.color.b, 0f);
                }
                if (_veilRoot != null) _veilRoot.gameObject.SetActive(false);
            }
            finally
            {
                // Also covers an exception thrown by a scene callback while activation is finishing:
                // the transition can never strand the app with Busy=true.
                _busy = false;
                if (_veilRoot != null && _veil != null && _veilRoot.gameObject.activeSelf && _veil.color.a <= 0.01f)
                    _veilRoot.gameObject.SetActive(false);
            }
        }

        private IEnumerator CoverRoutine(System.Action whileCovered, bool fadeBack)
        {
            _busy = true;
            try
            {
                yield return Fade(1f, MenuTheme.Motion.TransitionFade(MenuPreferences.ReducedMotion));

                whileCovered();

                if (fadeBack)
                {
                    yield return Fade(0f, MenuTheme.Motion.TransitionFade(MenuPreferences.ReducedMotion));
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[transition] Covered menu action failed. {exception.Message}", this);
                if (_veil != null)
                    _veil.color = new Color(_veil.color.r, _veil.color.g, _veil.color.b, 0f);
                if (_veilRoot != null) _veilRoot.gameObject.SetActive(false);
            }
            finally
            {
                _busy = false;
            }
        }

        private IEnumerator Fade(float target, float duration)
        {
            if (duration > 0f || target > 0f) _veilRoot.gameObject.SetActive(true);
            if (!_veilRoot.gameObject.activeSelf) yield break;

            float from = _veil.color.a;
            if (duration <= 0f)
            {
                _veil.color = new Color(_veil.color.r, _veil.color.g, _veil.color.b, target);
            }
            else
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    _veil.color = new Color(_veil.color.r, _veil.color.g, _veil.color.b,
                                            Mathf.Lerp(from, target, t * t * (3f - (2f * t))));
                    yield return null;
                }

                _veil.color = new Color(_veil.color.r, _veil.color.g, _veil.color.b, target);
            }

            if (target <= 0f) _veilRoot.gameObject.SetActive(false);
        }
    }
}
