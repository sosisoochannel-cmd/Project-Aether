using Aether.Gameplay.Flow;
using Aether.Gameplay.Interface;
using Aether.Gameplay.Localization;
using Aether.Gameplay.Presentation;
using Aether.Gameplay.Settings;
using UnityEngine;

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
    /// <para>
    /// <b>Build gate:</b> this root intentionally keeps all Unity-engine references explicit and
    /// confined to this assembly, so Android player compilation can be validated independently from
    /// the editor-only test assemblies.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MenuRoot : SceneFlowOwner
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
            AetherSettings.Ensure();
            MenuArt.EnsureBuilt();
            GameplayCurtain.Drop();

            _canvas = MenuCanvas.Create("Menu Canvas", MenuSortingOrder);
            _canvas.GameObject.transform.SetParent(transform, false);
            _canvas.SetScale(MenuPreferences.ClampedUiScale);

            _safeArea = _canvas.SafeRoot.gameObject.AddComponent<SafeAreaFitter>();
            _safeArea.Mode = MenuPreferences.SafeArea;

            _backdrop = MenuBackdrop.Create(_canvas.Root);
            UiEventSystem.Ensure();

            _input = MenuInput.Create(transform);
            _system = MenuSystem.Create(_canvas, _input);
            _audio = MenuAudio.Create(transform);
            _audio.PlayMenuMusic();

            MenuPreferences.Subscribe(OnSettingChanged);
            LanguageService.Changed += OnLanguageChanged;
            _listening = true;

            Canvas.ForceUpdateCanvases();
            LayoutNow();

            MenuScreenId initialScreen = GameLaunch.TakeOpenSettingsRequest()
                ? MenuScreenId.Settings
                : MenuScreenId.MainMenu;
            _system.Show(initialScreen, true);
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
            if (_canvas.ContentRoot.rect.size != _box) LayoutNow();
            if (_input != null) _input.Locked = MenuTransition.Instance.Busy;
            if (_system != null) _system.ApplyPendingRebuild();
        }

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

        private void OnSettingChanged(string id)
        {
            if (_canvas != null) _canvas.SetScale(MenuPreferences.ClampedUiScale);
            if (_safeArea != null) _safeArea.Mode = MenuPreferences.SafeArea;
            if (_backdrop != null) _backdrop.ApplySettings();
            if (_audio != null) _audio.ApplySettings();
            if (_system != null) _system.Refresh();

            Canvas.ForceUpdateCanvases();
            LayoutNow();
        }

        private void OnLanguageChanged()
        {
            if (_system != null) _system.RequestRebuild();
        }
    }
}
