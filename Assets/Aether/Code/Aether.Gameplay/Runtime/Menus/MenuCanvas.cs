using UnityEngine;
using Aether.Gameplay.Presentation;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// The canvas every menu screen is drawn on: one scaler, one safe-area root, one content box.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Height, not width, is what is matched.</b> A landscape menu is read across, so a device
    /// that is taller in aspect should gain horizontal room rather than taller rows; matching height
    /// also means the canvas is <i>always</i> 1080 reference units tall, which is what makes the
    /// numbers in <see cref="MenuTheme.Metrics"/> mean the same thing on a phone, a tablet and a
    /// desktop window, and what lets the gate convert them into dp for a device list. Matching
    /// width instead would give a 20:9 phone a 864-unit-tall canvas and squeeze every row on the
    /// tallest phones — the exact opposite of what a menu wants.
    /// </para>
    /// <para>
    /// <b>The interface size setting moves the reference resolution, not the scale factor.</b>
    /// <c>CanvasScaler</c> owns <c>scaleFactor</c> and overwrites it whenever the canvas updates, so
    /// assigning it is a change that lasts until the next frame. Dividing the reference resolution by
    /// the player's scale is the supported way to ask for the same thing: the scaler then computes a
    /// scale factor that puts more (or fewer) reference units on the screen. The height in units
    /// becomes 1080 / scale, which is why every screen in this menu is scrollable once its content
    /// no longer fits — the alternative would be a layout that breaks at the top of the range.
    /// </para>
    /// </remarks>
    public sealed class MenuCanvas
    {
        private const float ReferenceHeight = 1080f;
        private const float ReferenceWidth = 1920f;

        private float _scale = 1f;

        /// <summary>Invoked after the scale changed and the reference resolution was reapplied.</summary>
        public System.Action<float> ScaleChanged;

        private MenuCanvas(Canvas canvas, CanvasScaler scaler, RectTransform root, RectTransform safeRoot,
                           RectTransform contentRoot)
        {
            Canvas = canvas;
            Scaler = scaler;
            Root = root;
            SafeRoot = safeRoot;
            ContentRoot = contentRoot;
        }

        /// <summary>The canvas itself.</summary>
        public Canvas Canvas { get; private set; }

        /// <summary>The scaler, for anything that needs to read the computed scale factor.</summary>
        public CanvasScaler Scaler { get; private set; }

        /// <summary>Full-canvas rect: what the backdrop fills, under the safe area.</summary>
        public RectTransform Root { get; private set; }

        /// <summary>The safe area, as fractions of the canvas. Everything readable goes in here.</summary>
        public RectTransform SafeRoot { get; private set; }

        /// <summary>The safe area minus the screen margins: the box a screen lays its content out in.</summary>
        public RectTransform ContentRoot { get; private set; }

        /// <summary>The interface scale the player chose.</summary>
        public float Scale
        {
            get { return _scale; }
        }

        /// <summary>
        /// The height of the canvas in reference units at the current scale, which is what a screen
        /// has to fit its content into.
        /// </summary>
        public float HeightInUnits
        {
            get { return ReferenceHeight / Mathf.Clamp(_scale, 0.5f, 2f); }
        }

        /// <summary>Builds a canvas, its layers and its content box.</summary>
        /// <param name="name">GameObject name, so the hierarchy says what it is.</param>
        /// <param name="sortingOrder">Where the canvas sits against any other canvas.</param>
        public static MenuCanvas Create(string name, int sortingOrder)
        {
            var host = new GameObject(name);
            var canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = false;

            var scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            scaler.referencePixelsPerUnit = 100f;

            host.AddComponent<GraphicRaycaster>();

            var root = (RectTransform)host.transform;

            RectTransform safe = MenuUi.CreateNode("Safe Area", root);
            MenuUi.Stretch(safe);
            // Respect physical cutouts/gesture areas instead of merely naming this object "Safe Area".
            // The fitter listens to canvas resize events, so rotation and window resizing remain correct
            // without an Update loop.
            safe.gameObject.AddComponent<SafeAreaFitter>();

            RectTransform content = MenuUi.CreateNode("Content", safe);
            MenuUi.Stretch(content,
                           MenuTheme.Metrics.ScreenMarginLeft,
                           MenuTheme.Metrics.ScreenMarginRight,
                           MenuTheme.Metrics.ScreenMarginTop,
                           MenuTheme.Metrics.ScreenMarginBottom);

            return new MenuCanvas(canvas, scaler, root, safe, content);
        }

        /// <summary>Applies the player's interface scale. Cheap and idempotent.</summary>
        public void SetScale(float scale)
        {
            float clamped = Mathf.Clamp(scale, 0.5f, 2f);
            if (Mathf.Approximately(clamped, _scale)) return;

            _scale = clamped;
            Scaler.referenceResolution = new Vector2(ReferenceWidth / clamped, ReferenceHeight / clamped);

            System.Action<float> handler = ScaleChanged;
            if (handler != null) handler(clamped);
        }

        /// <summary>The object the canvas lives on, for anything that needs to hand it to a screen.</summary>
        public GameObject GameObject
        {
            get { return Canvas.gameObject; }
        }
    }
}
