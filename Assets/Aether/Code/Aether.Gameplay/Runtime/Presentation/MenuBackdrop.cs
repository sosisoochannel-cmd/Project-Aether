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
    /// <b>The layers are named, and each has one job.</b> <i>Background</i> is the approved forest
    /// image, sized to cover the canvas without stretching. <i>Midground</i> is the horizon — three
    /// faint ridges and the wash under them. <i>Atmosphere</i> is a pair of muted antique-gold washes
    /// that drift slowly. <i>Foreground</i> is the vignette and the darkening along the bottom edge
    /// that the entry rows sit on. Keeping them apart lets the artwork support the interface without
    /// changing the approved mark, typography or composition.
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
    /// asked for less is not paying for a sine it does not draw. The artwork stays as one imported
    /// texture; the remaining atmosphere uses small procedural sprites, with no added materials or
    /// shaders and only restrained translucent overlays for readability.
    /// </para>
    /// </remarks>
    public sealed class MenuBackdrop : MonoBehaviour
    {
        private const float DriftLayerOnePeriod = 26f;
        private const float DriftLayerTwoPeriod = 31f;
        private const float DriftLayerThreePeriod = 43f;
        private const float BackgroundDriftPeriod = 37f;
        private const float BackgroundZoomAmplitude = 0.0065f;

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
        private Vector2 _backgroundRestPosition;
        private Vector3 _backgroundRestScale = Vector3.one;
        private RectTransform[] _fireflies;
        private Vector2[] _fireflyOrigins;
        private float[] _fireflyPhases;

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
            backdrop._backgroundSprite = MenuArt.Forest;
            if (backdrop._backgroundSprite != null)
            {
                ground.sprite = backdrop._backgroundSprite;
                ground.color = Color.white;
            }
            ground.transform.SetAsFirstSibling();

            // -- midground: the horizon. A wash, and three soft bands of decreasing width and strength.
            //    The shared band sprite keeps the impression atmospheric, not like a set of hard lines.
            RectTransform midground = MenuUi.CreateNode("Midground", root);
            MenuUi.Stretch(midground);
            backdrop._midground = midground;

            Image horizon = MenuUi.CreateImage("Horizon Wash", midground, MenuArt.Band,
                                               MenuTheme.Palette.Horizon);
            backdrop._wash = horizon.rectTransform;

            Ridge(midground, "Ridge Far", 0.318f, 1.00f, 18f, 0.055f);
            Ridge(midground, "Ridge Mid", 0.286f, 0.82f, 12f, 0.040f);
            Ridge(midground, "Ridge Near", 0.252f, 0.62f, 8f, 0.030f);

            // -- atmosphere: two blooms, far apart, moving at different speeds.
            backdrop._bloomFar = Bloom(root, "Atmosphere Far", new Vector2(0.30f, 0.86f), 1500f);
            backdrop._bloomNear = Bloom(root, "Atmosphere Near", new Vector2(0.86f, 0.20f), 1900f);
            backdrop.BuildFireflies(root);

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

            // The corner metalwork sits above the vignette but outside the safe content box.
            backdrop.BuildFrameOrnaments(root);
            backdrop.ApplySettings();
            return backdrop;
        }

        /// <summary>Sizes the layers against the canvas, in reference units.</summary>
        public void Layout(float canvasWidth, float canvasHeight)
        {
            _width = Mathf.Max(1f, canvasWidth);
            _height = Mathf.Max(1f, canvasHeight);
            CoverBackground();

            // The horizon wash settles the artwork through the entry band without adding a panel.
            BandWash(0.30f, _height * 0.42f);
            _midgroundRest = Vector2.zero;
            Band(_foregroundShade, 0f, _height * 0.44f);

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
            _backgroundRestPosition = Vector2.zero;
            _backgroundRestScale = Vector3.one;
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
            if (_fireflies != null)
            {
                for (int i = 0; i < _fireflies.Length; i++)
                    if (_fireflies[i] != null) _fireflies[i].gameObject.SetActive(MotionEnabled);
            }

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
            float background = Mathf.Sin((_phase / BackgroundDriftPeriod) * Mathf.PI * 2f);

            // A nearly invisible camera-like breathing pass keeps the supplied forest from reading
            // as a flat wallpaper. The scale is tiny enough that the cover crop always remains safe.
            _background.anchoredPosition = _backgroundRestPosition +
                                           new Vector2(background * 1.5f, background * 0.8f);
            float zoom = 1f + (background * BackgroundZoomAmplitude);
            _background.localScale = _backgroundRestScale * zoom;

            _bloomNear.anchoredPosition = _bloomNearRest
                                          + new Vector2(near * amplitude, near * amplitude * 0.35f);
            _bloomFar.anchoredPosition = _bloomFarRest
                                         + new Vector2(far * amplitude * 0.6f, far * amplitude * 0.25f);
            _midground.anchoredPosition = _midgroundRest
                                          + new Vector2(horizon * amplitude * 0.18f, 0f);

            if (_fireflies != null)
            {
                for (int i = 0; i < _fireflies.Length; i++)
                {
                    RectTransform mote = _fireflies[i];
                    if (mote == null) continue;
                    float phase = _phase * (0.22f + (i * 0.031f)) + _fireflyPhases[i];
                    Vector2 origin = _fireflyOrigins[i];
                    mote.anchoredPosition = origin + new Vector2(Mathf.Sin(phase) * 18f,
                                                                 Mathf.Cos(phase * 0.73f) * 10f);
                    float pulse = 0.5f + (0.5f * Mathf.Sin(phase * 1.7f));
                    Image image = mote.GetComponent<Image>();
                    if (image != null)
                    {
                        Color gold = MenuTheme.Palette.Accent;
                        image.color = new Color(gold.r, gold.g, gold.b, Mathf.Lerp(0.10f, 0.42f, pulse));
                    }
                }
            }
        }

        private Vector2 _bloomNearRest;
        private Vector2 _bloomFarRest;
        private Vector2 _midgroundRest;

        private void Rest()
        {
            if (_background != null)
            {
                _background.anchoredPosition = _backgroundRestPosition;
                _background.localScale = _backgroundRestScale;
            }
            if (_bloomNear != null) _bloomNear.anchoredPosition = _bloomNearRest;
            if (_bloomFar != null) _bloomFar.anchoredPosition = _bloomFarRest;
            if (_midground != null) _midground.anchoredPosition = _midgroundRest;
            if (_fireflies != null)
            {
                for (int i = 0; i < _fireflies.Length; i++)
                    if (_fireflies[i] != null) _fireflies[i].anchoredPosition = _fireflyOrigins[i];
            }
        }

        private void BuildFireflies(RectTransform parent)
        {
            // A few more distant motes add a living, gilded atmosphere without becoming particles
            // over the text. They remain static on low quality and under Reduced Motion.
            const int count = 11;
            _fireflies = new RectTransform[count];
            _fireflyOrigins = new Vector2[count];
            _fireflyPhases = new float[count];
            Vector2[] normalized =
            {
                new Vector2(0.13f, 0.68f), new Vector2(0.22f, 0.35f), new Vector2(0.31f, 0.81f),
                new Vector2(0.39f, 0.23f), new Vector2(0.48f, 0.73f), new Vector2(0.57f, 0.39f),
                new Vector2(0.66f, 0.84f), new Vector2(0.73f, 0.26f), new Vector2(0.81f, 0.65f),
                new Vector2(0.88f, 0.46f), new Vector2(0.93f, 0.22f)
            };
            Color gold = MenuTheme.Palette.Accent;
            for (int i = 0; i < count; i++)
            {
                Image mote = MenuUi.CreateImage("Gold Mote " + i, parent, MenuArt.Circle,
                                                new Color(gold.r, gold.g, gold.b, 0.18f));
                RectTransform rect = mote.rectTransform;
                rect.anchorMin = normalized[i];
                rect.anchorMax = normalized[i];
                rect.pivot = new Vector2(0.5f, 0.5f);
                float size = 4f + ((i % 3) * 2f);
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = Vector2.zero;
                _fireflies[i] = rect;
                _fireflyOrigins[i] = Vector2.zero;
                _fireflyPhases[i] = i * 1.83f;
            }
        }

        /// <summary>
        /// Adds a delicate corner frame, inspired by carved metalwork rather than a full UI panel.
        /// It stays at the canvas edge so it never competes with menu choices or touch targets.
        /// </summary>
        private static void BuildFrameOrnaments(RectTransform parent)
        {
            Color accent = MenuTheme.Palette.Accent;
            Color colour = new Color(accent.r, accent.g, accent.b, 0.24f);
            const float insetX = 0.035f;
            const float insetY = 0.055f;
            const float armX = 0.055f;
            const float armY = 0.055f;
            const float thickness = 2f;

            FrameLine(parent, "Frame TL horizontal", colour,
                      new Vector2(insetX, 1f - insetY), new Vector2(insetX + armX, 1f - insetY), thickness);
            FrameLine(parent, "Frame TL vertical", colour,
                      new Vector2(insetX, 1f - insetY - armY), new Vector2(insetX, 1f - insetY), thickness);
            FrameLine(parent, "Frame TR horizontal", colour,
                      new Vector2(1f - insetX - armX, 1f - insetY), new Vector2(1f - insetX, 1f - insetY), thickness);
            FrameLine(parent, "Frame TR vertical", colour,
                      new Vector2(1f - insetX, 1f - insetY - armY), new Vector2(1f - insetX, 1f - insetY), thickness);
            FrameLine(parent, "Frame BL horizontal", colour,
                      new Vector2(insetX, insetY), new Vector2(insetX + armX, insetY), thickness);
            FrameLine(parent, "Frame BL vertical", colour,
                      new Vector2(insetX, insetY), new Vector2(insetX, insetY + armY), thickness);
            FrameLine(parent, "Frame BR horizontal", colour,
                      new Vector2(1f - insetX - armX, insetY), new Vector2(1f - insetX, insetY), thickness);
            FrameLine(parent, "Frame BR vertical", colour,
                      new Vector2(1f - insetX, insetY), new Vector2(1f - insetX, insetY + armY), thickness);
        }

        private static void FrameLine(RectTransform parent, string name, Color colour,
                                      Vector2 from, Vector2 to, float thickness)
        {
            Image line = MenuUi.CreateImage(name, parent, MenuArt.Solid, colour);
            RectTransform rect = line.rectTransform;
            rect.anchorMin = Vector2.Min(from, to);
            rect.anchorMax = Vector2.Max(from, to);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(from.x == to.x ? thickness : 0f,
                                         from.y == to.y ? thickness : 0f);
            rect.anchoredPosition = Vector2.zero;
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
