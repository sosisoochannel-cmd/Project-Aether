using UnityEngine;
using Aether.Gameplay.Localization;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// Procedural interface shapes and the approved imported artwork used by the runtime-built menu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the interface shapes are generated.</b> A menu needs hairlines, a soft wash behind
    /// the title, a vignette, a caret, a slider knob and a small padlock. Those small details are
    /// arithmetic rather than extra image files, so they need no atlas and cannot be re-exported by
    /// accident. The approved forest background is a real Unity resource and is returned here
    /// unchanged; a 1-pixel white sprite is not art, it is a rectangle.
    /// </para>
    /// <para>
    /// Procedural shapes are built lazily, once, and kept with <c>HideAndDontSave</c> so they are
    /// not written into a scene or leaked between play sessions. Their small textures use bilinear
    /// filtering and no mipmaps; the imported forest keeps its asset lifetime and platform settings.
    /// </para>
    /// </remarks>
    public static class MenuArt
    {
        private const string ForestResourcePath = "Menu/MainMenuForest";

        private static Sprite _forest;
        private static Sprite _solid;
        private static Sprite _circle;
        private static Sprite _glow;
        private static Sprite _band;
        private static Sprite _vignette;
        private static Sprite _triangle;
        private static Sprite _padlock;
        private static Font _font;
        private static Font _rtlFont;
        private static bool _built;

        /// <summary>The approved forest background loaded from Resources, without modifying the asset.</summary>
        public static Sprite Forest
        {
            get
            {
                if (_forest == null) _forest = Resources.Load<Sprite>(ForestResourcePath);
                return _forest;
            }
        }

        /// <summary>A one-unit white square. The basis of every hairline, rule and fill.</summary>
        public static Sprite Solid
        {
            get
            {
                EnsureBuilt();
                return _solid;
            }
        }

        /// <summary>A soft disc: the slider knob, the switch's thumb, and the atmosphere's bloom.</summary>
        public static Sprite Circle
        {
            get
            {
                EnsureBuilt();
                return _circle;
            }
        }

        /// <summary>A soft round wash with no edge at all, for the atmosphere.</summary>
        public static Sprite Glow
        {
            get
            {
                EnsureBuilt();
                return _glow;
            }
        }

        /// <summary>
        /// A soft horizontal band: opaque along its middle line, fading to nothing at both edges.
        /// The horizon, a highlight, and the base the bottom of the screen sits on.
        /// </summary>
        public static Sprite Band
        {
            get
            {
                EnsureBuilt();
                return _band;
            }
        }

        /// <summary>Dark at the edges, clear in the middle: what keeps the frame from looking flat.</summary>
        public static Sprite Vignette
        {
            get
            {
                EnsureBuilt();
                return _vignette;
            }
        }

        /// <summary>A right-pointing triangle: the caret beside a row, and the locked-row marker.</summary>
        public static Sprite Triangle
        {
            get
            {
                EnsureBuilt();
                return _triangle;
            }
        }

        /// <summary>A small padlock, drawn for the rows that are not in the build yet.</summary>
        public static Sprite Padlock
        {
            get
            {
                EnsureBuilt();
                return _padlock;
            }
        }

        /// <summary>
        /// The font every label is drawn in.
        /// </summary>
        /// <remarks>
        /// Unity ships one font in the engine rather than in the project: <c>LegacyRuntime.ttf</c>.
        /// Using it means a clone of this repository has text on its first play with nothing
        /// imported, which is the same reason the level is a text file. A project font asset — and
        /// with it signed distance field text, which is crisper at large sizes — is an Editor job for
        /// whoever has Unity open, and it replaces this one method when it happens.
        /// </remarks>
        public static Font Font
        {
            get
            {
                EnsureBuilt();
                return LanguageService.IsRightToLeft ? (_rtlFont ?? _font) : _font;
            }
        }

        /// <summary>Builds everything, once. Safe to call from anywhere; repeated calls do nothing.</summary>
        public static void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            _solid = MakeSprite(Texture("solid", 2, 2), "solid", new Rect(0f, 0f, 2f, 2f));

            _circle = MakeSprite(Radial("circle", 64, 0.5f, 1f, 1f), "circle",
                                 new Rect(0f, 0f, 64f, 64f));
            _glow = MakeSprite(Radial("glow", 128, 0f, 1f, 1.9f), "glow", new Rect(0f, 0f, 128f, 128f));
            _band = MakeSprite(BandTexture(8, 128), "band", new Rect(0f, 0f, 8f, 128f));
            _vignette = MakeSprite(VignetteTexture(160), "vignette", new Rect(0f, 0f, 160f, 160f));
            _triangle = MakeSprite(TriangleTexture(32), "triangle", new Rect(0f, 0f, 32f, 32f));
            _padlock = MakeSprite(PadlockTexture(48), "padlock", new Rect(0f, 0f, 48f, 48f));

            _font = ResolveFont(false);
            _rtlFont = ResolveFont(true);
        }

        /// <summary>Releases everything. Used by tests so a run does not depend on the previous one.</summary>
        public static void ResetForTests()
        {
            Destroy(_solid);
            Destroy(_circle);
            Destroy(_glow);
            Destroy(_band);
            Destroy(_vignette);
            Destroy(_triangle);
            Destroy(_padlock);
            _solid = _circle = _glow = _band = _vignette = _triangle = _padlock = null;
            _forest = null;
            _font = null;
            _rtlFont = null;
            _built = false;
        }

        // -- construction ----------------------------------------------------------------------

        private static Font ResolveFont(bool rtl)
        {
            if (!rtl)
            {
                Font builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (builtin != null) return builtin;
            }

            string[] names = rtl
                ? new[]
                {
                    "Noto Sans Arabic",
                    "Noto Sans Arabic UI",
                    "Noto Naskh Arabic",
                    "Vazirmatn",
                    "Tahoma",
                    "Noto Sans",
                    "Droid Sans",
                    "DejaVu Sans",
                    "Arial"
                }
                : new[]
                {
                    "Roboto",
                    "Noto Sans",
                    "Droid Sans",
                    "DejaVu Sans",
                    "Arial"
                };

            return Font.CreateDynamicFontFromOSFont(names, 48);
        }

        private static Texture2D Texture(string name, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Aether menu " + name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            return texture;
        }

        /// <summary>A disc or a soft wash: opaque in the middle, transparent at the rim.</summary>
        private static Texture2D Radial(string name, int size, float innerFraction, float power,
                                        float aspect)
        {
            Texture2D texture = Texture(name, size, size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float scaleX = size / aspect;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - (size * 0.5f)) / Mathf.Max(1f, scaleX);
                    float dy = (y + 0.5f - half) / half;
                    float distance = Mathf.Sqrt((dx * dx) + (dy * dy));

                    float alpha;
                    if (innerFraction > 0f)
                    {
                        // A disc with a defined edge, softened by a couple of pixels.
                        float edge = Mathf.InverseLerp(innerFraction, 1f, distance);
                        alpha = 1f - Mathf.SmoothStep(0.9f, 1f, edge);
                    }
                    else
                    {
                        // A wash: no edge anywhere, which is what a bloom has to look like.
                        alpha = Mathf.Clamp01(1f - distance);
                        alpha = Mathf.Pow(alpha, power);
                    }

                    pixels[(y * size) + x] = ToColor32(alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>A soft band: alpha follows a raised cosine across the texture's height.</summary>
        private static Texture2D BandTexture(int width, int height)
        {
            Texture2D texture = Texture("band", width, height);
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;

                // A raised cosine, not a triangle: a linear ramp is visible as a hard line where it
                // meets its own edge, and this is the one shape in the backdrop that has to be
                // invisible in the places it is not wanted.
                float alpha = 0.5f - (0.5f * Mathf.Cos(v * Mathf.PI * 2f));
                alpha = Mathf.Pow(alpha, 1.4f);

                var colour = new Color32(255, 255, 255,
                                        (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                for (int x = 0; x < width; x++) pixels[(y * width) + x] = colour;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Clear in the middle, black at the edges.</summary>
        private static Texture2D VignetteTexture(int size)
        {
            Texture2D texture = Texture("vignette", size, size);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = ((x + 0.5f) / size * 2f) - 1f;
                    float ny = ((y + 0.5f) / size * 2f) - 1f;
                    float distance = Mathf.Sqrt((nx * nx) + (ny * ny)) / 1.4142f;
                    float alpha = Mathf.SmoothStep(0.42f, 1f, distance) * 0.55f;
                    pixels[(y * size) + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>A right-pointing triangle, antialiased along its two slanted edges.</summary>
        private static Texture2D TriangleTexture(int size)
        {
            Texture2D texture = Texture("triangle", size, size);
            var pixels = new Color32[size * size];
            float width = size * 0.62f;

            for (int y = 0; y < size; y++)
            {
                float down = (y + 0.5f) / size;
                float up = 1f - down;
                float edge = Mathf.Min(down, up) * 2f * width;

                for (int x = 0; x < size; x++)
                {
                    float across = (x + 0.5f) / size * size;
                    float alpha = Mathf.Clamp01((edge - across) / 1.5f);
                    pixels[(y * size) + x] = ToColor32(alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>A padlock: a shackle over a body, at the size a 20-unit icon needs.</summary>
        private static Texture2D PadlockTexture(int size)
        {
            Texture2D texture = Texture("padlock", size, size);
            var pixels = new Color32[size * size];

            float bodyTop = 0.62f;
            float bodyBottom = 0.12f;
            float bodyLeft = 0.16f;
            float bodyRight = 0.84f;
            float shackleOuter = 0.30f;
            float shackleInner = 0.20f;

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size;
                    float alpha = 0f;

                    if (ny >= bodyBottom && ny <= bodyTop && nx >= bodyLeft && nx <= bodyRight)
                    {
                        alpha = 1f;
                    }
                    else if (ny > bodyTop)
                    {
                        // The shackle: a half ring standing on the body, with a gap in the middle.
                        float cx = 0.5f;
                        float cy = bodyTop;
                        float dx = nx - cx;
                        float dy = ny - cy;
                        float radius = Mathf.Sqrt((dx * dx) + (dy * dy));
                        if (dy > 0f && radius <= shackleOuter && radius >= shackleInner) alpha = 1f;
                    }

                    pixels[(y * size) + x] = ToColor32(alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Color32 ToColor32(float alpha)
        {
            return new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
        }

        private static Sprite MakeSprite(Texture2D texture, string name, Rect rect)
        {
            var sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "Aether menu " + name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static void Destroy(Sprite sprite)
        {
            if (sprite == null) return;
            if (sprite.texture != null) Object.Destroy(sprite.texture);
            Object.Destroy(sprite);
        }
    }
}
