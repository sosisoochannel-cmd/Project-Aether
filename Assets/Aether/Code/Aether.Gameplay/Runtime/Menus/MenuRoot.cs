using Aether.Gameplay.Localization;
using Aether.Gameplay.Presentation;
using Aether.Gameplay.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// The menu scene's root: it builds the canvas, the backdrop and the screens, and keeps them in
    /// step with the settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only component a menu scene needs. It is the one place that knows the order things
    /// have to be built in — the canvas before the screens that lay out against it, the backdrop
    /// before the screens that are drawn over it, the event system before anything that can be
    /// clicked — and the one place that listens to the settings service on the menu's behalf.
    /// </para>
    /// <para>
    /// <b>Who owns what.</b> <see cref="MenuSystem"/> owns the screens and the rules for moving
    /// between them; <see cref="MenuCanvas"/> owns the scaler and the content box; this owns the
    /// lifetime of both, and does nothing that is per frame except compare a size and copy one bool.
    /// </para>
    /// <para>
    /// <b>The event system is created here, not authored in the scene.</b> The project's input
    /// handler is the Input System package alone, so the module the event system carries has to be
    /// <see cref="InputSystemUIInputModule"/>: the legacy module reads a backend that is switched
    /// off. Building it in code means a new scene cannot get it wrong, and a scene that already has
    /// one is left alone.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MenuRoot : MonoBehaviour
    {
        /// <summary>Where the menu's canvas sits, above anything the scene draws itself.</summary>
        private const int MenuSortingOrder = 100;

        private MenuCanvas _canvas;
        private MenuSystem _system;
        private MenuInput _input;
        private MenuBackdrop _backdrop;
        private SafeAreaFitter _safeArea;
        private MenuAudio _audio;
        private Vector2 _box;
        private bool _listening;

        /// <summary>The screens and their navigation, for a test or another system to drive.</summary>
        public MenuSystem System
        {
            get { return _system; }
        }

        /// <summary>
        /// The canvas, for anything that needs its scale or its content box.
        /// </summary>
        /// <remarks>
        /// Named for what it holds rather than for the Canvas it wraps: a member called
        /// <c>Canvas</c> shadows <c>UnityEngine.Canvas</c> inside this class, which is how a call to
        /// the engine's static canvas methods ends up being read as a call on this property.
        /// </remarks>
        public MenuCanvas Ui
        {
            get { return _canvas; }
        }

        private void Awake()
        {
            // Settings first: the interface scale, the safe area rule and the quality tier all decide
            // what is built below, and asking for them here means no other system has to.
            AetherSettings.Ensure();
            MenuArt.EnsureBuilt();

            _canvas = MenuCanvas.Create("Menu Canvas", MenuSortingOrder);
            _canvas.GameObject.transform.SetParent(transform, false);
            _canvas.SetScale(MenuPreferences.ClampedUiScale);

            _safeArea = _canvas.SafeRoot.gameObject.AddComponent<SafeAreaFitter>();
            _safeArea.Mode = MenuPreferences.SafeArea;

            _backdrop = MenuBackdrop.Create(_canvas.Root);

            EnsureEventSystem();

            _input = MenuInput.Create(transform);
            _system = MenuSystem.Create(_canvas, _input);

            _audio = MenuAudio.Create(transform);

            // The music is asked for once, here, and says so plainly when there is no approved track
            // to play: the menu then runs silently on the music bus, which is the integration point
            // the brief asks for rather than a placeholder standing in for a real score.
            _audio.PlayMenuMusic();

            MenuPreferences.Subscribe(OnSettingChanged);
            LanguageService.Changed += OnLanguageChanged;
            _listening = true;

            // The rects are only meaningful once the canvas has laid out, and the menu should be
            // laid out before its first frame is drawn rather than jumping on the second.
            Canvas.ForceUpdateCanvases();
            LayoutNow();

            _system.Show(MenuScreenId.MainMenu, true);
            _box = _canvas.ContentRoot.rect.size;
        }

        private void OnDestroy()
        {
            if (_listening)
            {
                MenuPreferences.Unsubscribe(OnSettingChanged);
                LanguageService.Changed -= OnLanguageChanged;
                _listening = false;
            }
        }

        private void LateUpdate()
        {
            // Two reads, no allocation, no search: the content box changes when the device rotates,
            // when the window is resized, when the interface size setting moves, and when the system
            // bars appear or hide — and the input lock has to be right in the frame a fade starts in.
            if (_canvas.ContentRoot.rect.size != _box) LayoutNow();

            if (_input != null) _input.Locked = MenuTransition.Instance.Busy;

            // A language change asked for while a transition was running waits here, and is built the
            // frame the fade finishes. One boolean read per frame, and no work in the usual case.
            if (_system != null) _system.ApplyPendingRebuild();
        }

        /// <summary>Re-measures the box and lays the menu out in it. Safe to call at any time.</summary>
        public void LayoutNow()
        {
            if (_canvas == null) return;

            Rect box = _canvas.ContentRoot.rect;
            _box = box.size;

            if (_system != null) _system.Layout(box.width, box.height);

            if (_backdrop != null)
            {
                Rect canvasRect = _canvas.Root.rect;
                _backdrop.Layout(canvasRect.width, canvasRect.height);
            }
        }

        /// <summary>
        /// Re-reads everything a setting can change.
        /// </summary>
        /// <remarks>
        /// One handler for the whole menu rather than one per screen: the canvas scale, the safe area,
        /// the backdrop's motion and the screen that is up all have a value that the player can move,
        /// and they all have to move together.
        /// </remarks>
        private void OnSettingChanged(string id)
        {
            if (_canvas != null) _canvas.SetScale(MenuPreferences.ClampedUiScale);

            if (_safeArea != null) _safeArea.Mode = MenuPreferences.SafeArea;
            if (_backdrop != null) _backdrop.ApplySettings();
            if (_audio != null) _audio.ApplySettings();
            if (_system != null) _system.Refresh();

            // The scale change only exists in the canvas after it has laid out again, and this is a
            // settled moment — a settings row is committed, not dragged frame by frame — so the
            // forced update is paid once here instead of the menu laying out twice.
            Canvas.ForceUpdateCanvases();
            LayoutNow();
        }

        /// <summary>
        /// Builds the screens again in the language that was just chosen.
        /// </summary>
        /// <remarks>
        /// The setting is stored, applied and flushed by the localization service; what is left for
        /// the menu is to stop showing the old language. It does that by building its screens again
        /// rather than by updating labels row by row — there is no per-label update pass in this menu
        /// and there is not meant to be one, because every string is written when its screen is built.
        /// </remarks>
        private void OnLanguageChanged()
        {
            if (_system != null) _system.RequestRebuild();
        }

        /// <summary>
        /// Finds the scene's event system, or makes one, and makes sure it speaks the Input System.
        /// </summary>
        private static EventSystem EnsureEventSystem()
        {
            EventSystem events = EventSystem.current;
            if (events == null)
            {
                var host = new GameObject("Event System");
                events = host.AddComponent<EventSystem>();
            }

            // A legacy module in the same scene would fight this one for every event, and on an
            // Input System Only project it also throws the first time it reads a key.
            StandaloneInputModule legacy = events.GetComponent<StandaloneInputModule>();
            if (legacy != null) Destroy(legacy);

            if (events.GetComponent<InputSystemUIInputModule>() == null)
            {
                events.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            return events;
        }
    }
}
