using Aether.Core.Settings;
using Aether.Gameplay.Menus;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Presentation
{
    /// <summary>
    /// The menu's forest artwork and restrained atmosphere, laid out as a small set of clear layers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The layers are named, and each has one job.</b> <i>Background</i> is the master hand-painted
    /// environment artwork, cover-fitted to the canvas without stretching or distortion. <i>Midground</i>
    /// provides subtle atmospheric horizon mist. <i>Atmosphere</i> layers provide gentle crepuscular warmth
    /// over the ancient sanctuary and cool valley mist with floating golden motes. <i>Foreground</i> provides
    /// restrained grounding along the bottom edge and cinematic vignette framing.
    /// </para>
    /// <para>
    /// <b>Rich, but never moving much.</b> Three coprime oscillation periods drive slow ambient drift so
    /// the environment feels living and deep without drawing the eye away from interface interaction.
    /// </para>
    /// <para>
    /// <b>It costs nothing when it is off.</b> Reduced motion, or the low graphics tier, switches the
    /// motion off and this component switches its own <c>Update</c> off with it, so a phone that
    /// asked for less is not paying for a sine it does not draw.
    /// </para>
    /// </remarks>
    public sealed class MenuBackdrop : MonoBehaviour
    {
        private const float DriftLayerOnePeriod = 26f;
        private const float DriftLayerTwoPeriod = 31f;
        private const float DriftLayerThreePeriod = 43f;

        private RectTransform _root;
        private RectTransform _background;
        private Sprite _backgroundSprite;
        private RectTransform _midground;
        private RectTransform _wash;
        private RectTransform _bloomNear;
        private RectTransform _bloomFar;
        private RectTransform _foregroundShade;
        private float _width = 1920f;
        private float _height = 1080f;
        private float _phase;
        private RectTransform[] _motes = new RectTransform[0];
        private float[] _motePhaseOffsets = new float[0];
        private float[] _moteSpeeds = new float[0];

        /// <summary>Whether the backdrop is allowed to move, as the player set it.</summary>
        public bool AtmosphereMotion { get; private set; } = true;

        /// <summary>Whether it is moving right now. False at the low tier, and under reduced motion.</summary>
        public bool MotionEnabled { get; private set; } = true;

        /// <summary>Builds the artwork and atmosphere under a canvas-filling rect.</summary>
        public static MenuBackdrop Create(Transform parent)
        {
            MenuArt.EnsureBuilt();

            var host = new GameObject("Backdrop", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            // Keep this canvas-level visual layer beneath the Safe Area and its readable controls.
            host.transform.SetAsFirstSibling();
            var root = (RectTransform)host.transform;
            MenuUi.Stretch(root);

            var backdrop = host.AddComponent<MenuBackdrop>();
            backdrop._root = root;

            // -- background: the supplied forest artwork, with a solid fallback if it is unavailable.
            Image ground = MenuUi.Fill("Background", root, MenuTheme.Palette.Ground);
            backdrop._background = ground.rectTransform;
            // Let the approved forest artwork carry the scene. If missing, keep a dark-teal fallback.
            backdrop._backgroundSprite = MenuArt.Forest;
            ground.sprite = backdrop._backgroundSprite;
            ground.color = backdrop._backgroundSprite != null ? Color.white : MenuTheme.Palette.Ground;
            ground.transform.SetAsFirstSibling();

            // -- midground: the horizon. A wash, and three soft bands of decreasing width and strength.
            //    The shared band sprite keeps the impression atmospheric, not like a set of hard lines.
            RectTransform midground = MenuUi.CreateNode("Midground", root);
            MenuUi.Stretch(midground);
            backdrop._midground = midground;
            midground.gameObject.SetActive(true);

            Image horizon = MenuUi.CreateImage("Horizon Wash", midground, MenuArt.Band,
                                               new Color(MenuTheme.Palette.Horizon.r,
                                                         MenuTheme.Palette.Horizon.g,
                                                         MenuTheme.Palette.Horizon.b, 0.14f));
            backdrop._wash = horizon.rectTransform;

            Ridge(midground, "Ridge Far", 0.318f, 1.00f, 16f, 0.035f);
            Ridge(midground, "Ridge Mid", 0.286f, 0.82f, 10f, 0.025f);
            Ridge(midground, "Ridge Near", 0.252f, 0.62f, 6f, 0.015f);

            // -- atmosphere: two blooms, far apart, moving at different speeds.
            backdrop._bloomFar = Bloom(root, "Atmosphere Far", new Vector2(0.30f, 0.86f), 1500f);
            backdrop._bloomNear = Bloom(root, "Atmosphere Near", new Vector2(0.86f, 0.20f), 1900f);
            backdrop._bloomFar.gameObject.SetActive(true);
            backdrop._bloomNear.gameObject.SetActive(true);
            backdrop.CreateMotes(root);

            // -- foreground: subtle grounding along the bottom edge, and cinematic vignette framing.
            Image shade = MenuUi.CreateImage("Foreground Shade", root, MenuArt.Band,
                                             new Color(MenuTheme.Palette.Ground.r,
                                                       MenuTheme.Palette.Ground.g,
                                                       MenuTheme.Palette.Ground.b, 0.65f));
            shade.rectTransform.anchorMin = new Vector2(0f, 0f);
            shade.rectTransform.anchorMax = new Vector2(1f, 0f);
            shade.rectTransform.pivot = new Vector2(0.5f, 0f);
            backdrop._foregroundShade = shade.rectTransform;

            Image vignette = MenuUi.Fill("Vignette", root, MenuTheme.Palette.Scrim);
            vignette.sprite = MenuArt.Vignette;
            vignette.color = new Color(0.02f, 0.055f, 0.07f, 0.72f);

            // The art remains present when motion is reduced; only the drift is removed.
            backdrop.ApplySettings();
            return backdrop;
        }

        /// <summary>Creates subtle screen-relative ambient dust. All motes are tiny and translucent.</summary>
        private void CreateMotes(Transform parent)
        {
            float[] xs = { 0.06f, 0.15f, 0.24f, 0.34f, 0.42f, 0.59f, 0.68f, 0.78f, 0.89f, 0.96f, 0.28f, 0.82f };
            float[] ys = { 0.16f, 0.34f, 0.82f, 0.56f, 0.22f, 0.78f, 0.44f, 0.13f, 0.66f, 0.35f, 0.92f, 0.88f };
            float[] sizes = { 4f, 5f, 3f, 6f, 4f, 5f, 3f, 7f, 4f, 3f, 5f, 4f };
            const int count = 12;
            _motes = new RectTransform[count];
            _motePhaseOffsets = new float[count];
            _moteSpeeds = new float[count];

            for (int i = 0; i < count; i++)
            {
                float opacity = 0.085f + ((i % 4) * 0.018f);
                Image mote = MenuUi.CreateImage(
                    "Forest Dust " + (i + 1).ToString("00"), parent, MenuArt.Circle,
                    new Color(0.96f, 0.94f, 0.82f, opacity));
                RectTransform rect = mote.rectTransform;
                Vector2 point = new Vector2(xs[i], ys[i]);
                rect.anchorMin = point;
                rect.anchorMax = point;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(sizes[i], sizes[i]);
                rect.anchoredPosition = Vector2.zero;
                _motes[i] = rect;
                _motePhaseOffsets[i] = i * 1.618f;
                _moteSpeeds[i] = 0.075f + ((i % 4) * 0.018f);
            }
        }

        /// <summary>Sizes the layers against the canvas, in reference units.</summary>
        public void Layout(float canvasWidth, float canvasHeight)
        {
            _width = Mathf.Max(1f, canvasWidth);
            _height = Mathf.Max(1f, canvasHeight);
            CoverBackground();

            // The horizon wash settles the artwork through the entry band without adding a panel.
            BandWash(0.30f, _height * 0.36f);
            _midgroundRest = Vector2.zero;
            Band(_foregroundShade, 0f, _height * 0.26f);

            // The blooms are large on purpose: a small bright blob reads as a lens flare, a large
            // dim one reads as air.
            BloomSize(_bloomFar, _width * 0.62f, _height * 0.95f);
            BloomSize(_bloomNear, _width * 0.78f, _height * 0.92f);
        }

        /// <summary>Cover-fits the artwork and crops only the centered overhang, never stretching it.</summary>
        private void CoverBackground()
        {
            if (_background == null || _backgroundSprite == null) return;

            Rect spriteRect = _backgroundSprite.rect;
            if (spriteRect.width <= 0f || spriteRect.height <= 0f) return;

            float aspect = spriteRect.width / spriteRect.height;
            float width;
            float height;
            if (_width / _height > aspect)
            {
                width = _width;
                height = width / aspect;
            }
            else
            {
                height = _height;
                width = height * aspect;
            }

            _background.anchorMin = new Vector2(0.5f, 0.5f);
            _background.anchorMax = new Vector2(0.5f, 0.5f);
            _background.pivot = new Vector2(0.5f, 0.5f);
            _background.sizeDelta = new Vector2(width, height);
            _background.anchoredPosition = Vector2.zero;
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

            // Motes drift independently at low amplitude for parallax-like depth.
            for (int i = 0; i < _motes.Length; i++)
            {
                if (_motes[i] == null) continue;
                float motePhase = (_phase * _moteSpeeds[i]) + _motePhaseOffsets[i];
                _motes[i].anchoredPosition = new Vector2(
                    Mathf.Sin(motePhase) * 10f,
                    Mathf.Cos(motePhase * 0.71f) * 6f);
            }
        }

        private Vector2 _bloomNearRest;
        private Vector2 _bloomFarRest;
        private Vector2 _midgroundRest;

        private void Rest()
        {
            if (_bloomNear != null) _bloomNear.anchoredPosition = _bloomNearRest;
            if (_bloomFar != null) _bloomFar.anchoredPosition = _bloomFarRest;
            if (_midground != null) _midground.anchoredPosition = _midgroundRest;
            for (int i = 0; i < _motes.Length; i++)
                if (_motes[i] != null) _motes[i].anchoredPosition = Vector2.zero;
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
