using Aether.Core.Settings;
using Aether.Gameplay.Menus;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Presentation
{
    /// <summary>
    /// What the menu stands on: four layers of clearly separated depth, and nothing expensive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The layers are named, and each has one job.</b> <i>Background</i> is the ground: one flat
    /// colour, and the only thing that must be replaced for the menu to stand somewhere else.
    /// <i>Midground</i> is the horizon — three faint ridges and the wash under them, which is the
    /// only part of the frame that suggests a place rather than a screen. <i>Atmosphere</i> is a pair
    /// of cold blooms that drift. <i>Foreground</i> is the vignette and the darkening along the
    /// bottom edge that the entry rows sit on. Keeping them apart is what makes replacing the
    /// artwork later a matter of one image per layer: the composition does not change, and the
    /// approved mark and the typography are unaffected.
    /// </para>
    /// <para>
    /// <b>Rich, but never moving much.</b> Three sines at 26, 31 and 43 seconds, each moving its own
    /// layer by a different fraction of 26 units. The periods are coprime, so the drift has no
    /// perceptible loop, and the amplitudes are small enough that the movement is only noticed when
    /// it is taken away — which is the whole point of an atmospheric background.
    /// </para>
    /// <para>
    /// <b>It costs nothing when it is off.</b> Reduced motion, or the low graphics tier, switches the
    /// motion off and this component switches its own <c>Update</c> off with it, so a phone that
    /// asked for less is not paying for a sine it does not draw. The layers themselves are four
    /// sprites generated in memory, no materials, no shaders, and no transparency over the whole
    /// screen beyond the corner fades that a menu needs anyway.
    /// </para>
    /// </remarks>
    public sealed class MenuBackdrop : MonoBehaviour
    {
        private const float DriftLayerOnePeriod = 26f;
        private const float DriftLayerTwoPeriod = 31f;
        private const float DriftLayerThreePeriod = 43f;

        private RectTransform _root;
        private RectTransform _midground;
        private RectTransform _wash;
        private RectTransform _bloomNear;
        private RectTransform _bloomFar;
        private RectTransform _foregroundShade;
        private float _width = 1920f;
        private float _height = 1080f;
        private float _phase;

        /// <summary>Whether the backdrop is allowed to move, as the player set it.</summary>
        public bool AtmosphereMotion { get; private set; } = true;

        /// <summary>Whether it is moving right now. False at the low tier, and under reduced motion.</summary>
        public bool MotionEnabled { get; private set; } = true;

        /// <summary>Builds the four layers under a canvas-filling rect.</summary>
        public static MenuBackdrop Create(Transform parent)
        {
            MenuArt.EnsureBuilt();

            var host = new GameObject("Backdrop", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var root = (RectTransform)host.transform;
            MenuUi.Stretch(root);

            var backdrop = host.AddComponent<MenuBackdrop>();
            backdrop._root = root;

            // -- background: one flat colour, the thing a real piece of artwork replaces.
            Image ground = MenuUi.Fill("Background", root, MenuTheme.Palette.Ground);
            ground.transform.SetAsFirstSibling();

            // -- midground: the horizon. A wash, and three ridges of decreasing width and strength.
            //    They are geometry rather than a photograph: two units tall, drawn from the same
            //    generated band sprite, so the layer costs four quads and no texture of its own.
            RectTransform midground = MenuUi.CreateNode("Midground", root);
            MenuUi.Stretch(midground);
            backdrop._midground = midground;

            Image horizon = MenuUi.CreateImage("Horizon Wash", midground, MenuArt.Band,
                                               MenuTheme.Palette.Horizon);
            backdrop._wash = horizon.rectTransform;

            Ridge(midground, "Ridge Far", 0.318f, 1.00f, 3f, 0.16f);
            Ridge(midground, "Ridge Mid", 0.286f, 0.82f, 2f, 0.12f);
            Ridge(midground, "Ridge Near", 0.252f, 0.62f, 2f, 0.09f);

            // -- atmosphere: two blooms, far apart, moving at different speeds.
            backdrop._bloomFar = Bloom(root, "Atmosphere Far", new Vector2(0.30f, 0.86f), 1500f);
            backdrop._bloomNear = Bloom(root, "Atmosphere Near", new Vector2(0.86f, 0.20f), 1900f);

            // -- foreground: the shade along the bottom edge, and the vignette over everything.
            Image shade = MenuUi.CreateImage("Foreground Shade", root, MenuArt.Band,
                                             new Color(MenuTheme.Palette.Ground.r,
                                                       MenuTheme.Palette.Ground.g,
                                                       MenuTheme.Palette.Ground.b, 0.85f));
            shade.rectTransform.anchorMin = new Vector2(0f, 0f);
            shade.rectTransform.anchorMax = new Vector2(1f, 0f);
            shade.rectTransform.pivot = new Vector2(0.5f, 0f);
            backdrop._foregroundShade = shade.rectTransform;

            Image vignette = MenuUi.Fill("Vignette", root, MenuTheme.Palette.Scrim);
            vignette.sprite = MenuArt.Vignette;
            vignette.color = new Color(0f, 0f, 0f, 0.75f);

            backdrop.ApplySettings();
            return backdrop;
        }

        /// <summary>Sizes the layers against the canvas, in reference units.</summary>
        public void Layout(float canvasWidth, float canvasHeight)
        {
            _width = Mathf.Max(1f, canvasWidth);
            _height = Mathf.Max(1f, canvasHeight);

            // The horizon wash sits over the lower third, where the entry rows are, so the
            // background is slightly lighter exactly where the interface is.
            BandWash(0.30f, _height * 0.42f);
            _midgroundRest = Vector2.zero;
            Band(_foregroundShade, 0f, _height * 0.44f);

            // The blooms are large on purpose: a small bright blob reads as a lens flare, a large
            // dim one reads as air.
            BloomSize(_bloomFar, _width * 0.62f, _height * 0.95f);
            BloomSize(_bloomNear, _width * 0.78f, _height * 0.92f);
        }

        /// <summary>Reads the settings that decide whether anything moves here.</summary>
        public void ApplySettings()
        {
            AtmosphereMotion = MenuPreferences.AtmosphereMotion;
            MotionEnabled = AtmosphereMotion
                            && MenuPreferences.Tier != GraphicsTier.Low
                            && !MenuPreferences.ReducedMotion;

            // Switched off means switched off: no per-frame callback is left behind.
            enabled = MotionEnabled;

            if (!MotionEnabled) Rest();
        }

        private void Update()
        {
            if (!MotionEnabled) return;

            _phase += Time.unscaledDeltaTime;
            float amplitude = MenuTheme.Motion.ParallaxAmplitude;

            // Three periods that never line up, so the drift has no perceptible loop. The nearer
            // a layer is, the further it moves: that difference is what reads as depth.
            float near = Mathf.Sin((_phase / DriftLayerOnePeriod) * Mathf.PI * 2f);
            float far = Mathf.Sin((_phase / DriftLayerTwoPeriod) * Mathf.PI * 2f);
            float horizon = Mathf.Sin((_phase / DriftLayerThreePeriod) * Mathf.PI * 2f);

            _bloomNear.anchoredPosition = _bloomNearRest
                                          + new Vector2(near * amplitude, near * amplitude * 0.35f);
            _bloomFar.anchoredPosition = _bloomFarRest
                                         + new Vector2(far * amplitude * 0.6f, far * amplitude * 0.25f);
            _midground.anchoredPosition = _midgroundRest
                                          + new Vector2(horizon * amplitude * 0.18f, 0f);
        }

        private Vector2 _bloomNearRest;
        private Vector2 _bloomFarRest;
        private Vector2 _midgroundRest;

        private void Rest()
        {
            if (_bloomNear != null) _bloomNear.anchoredPosition = _bloomNearRest;
            if (_bloomFar != null) _bloomFar.anchoredPosition = _bloomFarRest;
            if (_midground != null) _midground.anchoredPosition = _midgroundRest;
        }

        /// <summary>
        /// One ridge of the horizon: a band of fixed width along an anchor line.
        /// </summary>
        /// <remarks>
        /// A ridge is not a band stretched across the canvas: the whole point of three of them is
        /// that they are different widths, which is what the eye reads as distance. They are
        /// anchored rather than offset, so they follow any screen shape without being resized.
        /// </remarks>
        private static void Ridge(RectTransform parent, string name, float anchorY, float width,
                                  float height, float alpha)
        {
            Color horizon = MenuTheme.Palette.Horizon;
            var colour = new Color(Mathf.Min(1f, horizon.r * 2.1f), Mathf.Min(1f, horizon.g * 2.1f),
                                   Mathf.Min(1f, horizon.b * 2.1f), alpha);

            Image ridge = MenuUi.CreateImage(name, parent, MenuArt.Band, colour);
            RectTransform rect = ridge.rectTransform;
            rect.anchorMin = new Vector2(0.5f - (width * 0.5f), anchorY);
            rect.anchorMax = new Vector2(0.5f + (width * 0.5f), anchorY);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Stretches a band across the canvas at an anchor line, with a height.</summary>
        private static void Band(RectTransform rect, float anchorY, float height)
        {
            if (rect == null) return;

            rect.anchorMin = new Vector2(0f, anchorY);
            rect.anchorMax = new Vector2(1f, anchorY);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Sizes the horizon wash, which hangs from the bottom of the frame.
        /// </summary>
        /// <remarks>
        /// The wash is anchored to the bottom edge with a pivot there, so its height is what places
        /// its top; a plain band would be centred and would move off the frame as the canvas grows
        /// taller. The ridges inside the midground are anchored by fraction, so only this needs a
        /// number that depends on the canvas height.
        /// </remarks>
        private void BandWash(float anchorY, float height)
        {
            RectTransform wash = _wash;
            if (wash == null) return;

            wash.anchorMin = new Vector2(0f, anchorY);
            wash.anchorMax = new Vector2(1f, anchorY);
            wash.pivot = new Vector2(0.5f, 0f);
            wash.sizeDelta = new Vector2(0f, height);
            wash.anchoredPosition = Vector2.zero;
        }

        /// <summary>Gives a bloom its size and remembers where it rests.</summary>
        private void BloomSize(RectTransform bloom, float width, float height)
        {
            if (bloom == null) return;

            bloom.sizeDelta = new Vector2(width, height);
            bloom.anchoredPosition = Vector2.zero;

            if (bloom == _bloomNear) _bloomNearRest = Vector2.zero;
            else if (bloom == _bloomFar) _bloomFarRest = Vector2.zero;
        }

        private static RectTransform Bloom(RectTransform parent, string name, Vector2 at, float size)
        {
            Image bloom = MenuUi.CreateImage(name, parent, MenuArt.Glow, MenuTheme.Palette.Atmosphere);
            RectTransform rect = bloom.rectTransform;
            rect.anchorMin = at;
            rect.anchorMax = at;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size * 0.62f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }
    }
}
