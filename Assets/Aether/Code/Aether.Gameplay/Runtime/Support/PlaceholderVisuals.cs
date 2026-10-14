using UnityEngine;

namespace Aether.Gameplay.Support
{
    /// <summary>
    /// Flat-colour sprites generated at runtime, shared by everything that has no art yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Greenway is playable before it is drawn. Rather than committing placeholder PNGs to the
    /// repository, the few shapes the level needs — a square, a disc, a ring — are generated once,
    /// and every Body, platform and control is one of those sprites tinted and scaled. The result is
    /// readable at a glance, weighs nothing in the repository, and cannot drift out of sync with the
    /// level because it is derived from the same data.
    /// </para>
    /// <para>
    /// This is a placeholder, and it is built to be swapped without touching gameplay: the level
    /// builder asks for a sprite per tile kind, and the bake tool replaces whole rectangles with
    /// real Tiles. Nothing in gameplay code refers to a generated sprite.
    /// </para>
    /// <para>
    /// Sprites are created lazily and cached for the lifetime of the process. They are never
    /// destroyed individually, so each exists exactly once.
    /// </para>
    /// </remarks>
    public static class PlaceholderVisuals
    {
        private const int TextureSize = 64;

        private static Sprite _square;
        private static Sprite _circle;
        private static Sprite _ring;
        private static Sprite _diamond;
        private static Sprite _leftArrow;
        private static Sprite _rightArrow;
        private static Sprite _upArrow;
        private static Sprite _slash;

        /// <summary>A one-unit white square. Scale it to the desired size.</summary>
        public static Sprite Square
        {
            get
            {
                if (_square == null) _square = Build("AetherPlaceholderSquare", (x, y) => true);
                return _square;
            }
        }

        /// <summary>A one-unit white disc, for round controls and soft silhouettes.</summary>
        public static Sprite Circle
        {
            get
            {
                if (_circle == null) _circle = Build("AetherPlaceholderCircle", InsideUnitCircle);
                return _circle;
            }
        }

        /// <summary>A one-unit ring, for control bases and checkpoint rings.</summary>
        public static Sprite Ring
        {
            get
            {
                if (_ring == null) _ring = Build("AetherPlaceholderRing", InsideUnitRing);
                return _ring;
            }
        }

        /// <summary>A diamond: the shape reserved for secrets.</summary>
        /// <remarks>
        /// A second round marker would have been cheaper, but the secret is the one thing in the
        /// region the player is meant to spot across a clearing. Round things are checkpoints; a
        /// diamond is a find.
        /// </remarks>
        public static Sprite Diamond
        {
            get
            {
                if (_diamond == null) _diamond = Build("AetherPlaceholderDiamond", InsideUnitDiamond);
                return _diamond;
            }
        }

        public static Sprite LeftArrow
        {
            get { if (_leftArrow == null) _leftArrow = Build("AetherControlLeft", InsideLeftArrow); return _leftArrow; }
        }

        public static Sprite RightArrow
        {
            get { if (_rightArrow == null) _rightArrow = Build("AetherControlRight", InsideRightArrow); return _rightArrow; }
        }

        public static Sprite UpArrow
        {
            get { if (_upArrow == null) _upArrow = Build("AetherControlJump", InsideUpArrow); return _upArrow; }
        }

        public static Sprite Slash
        {
            get { if (_slash == null) _slash = Build("AetherControlAttack", InsideSlash); return _slash; }
        }

        private static bool InsideLeftArrow(float x, float y)
        {
            bool head = x <= 0.48f && Mathf.Abs(y - 0.5f) <= (0.5f - x) * 0.86f;
            bool shaft = x >= 0.30f && x <= 0.90f && Mathf.Abs(y - 0.5f) <= 0.12f;
            return head || shaft;
        }

        private static bool InsideRightArrow(float x, float y) => InsideLeftArrow(1f - x, y);

        private static bool InsideUpArrow(float x, float y)
        {
            bool head = y >= 0.50f && Mathf.Abs(x - 0.5f) <= (1f - y) * 0.86f;
            bool shaft = y >= 0.10f && y <= 0.72f && Mathf.Abs(x - 0.5f) <= 0.12f;
            return head || shaft;
        }

        private static bool InsideSlash(float x, float y)
        {
            return x >= 0.08f && x <= 0.92f && y >= 0.08f && y <= 0.92f
                && Mathf.Abs((x + y) - 1f) <= 0.13f;
        }

        private static bool InsideUnitCircle(float x, float y)
        {
            return ((x - 0.5f) * (x - 0.5f)) + ((y - 0.5f) * (y - 0.5f)) <= 0.25f;
        }

        private static bool InsideUnitDiamond(float x, float y)
        {
            return (Mathf.Abs(x - 0.5f) + Mathf.Abs(y - 0.5f)) <= 0.5f;
        }

        private static bool InsideUnitRing(float x, float y)
        {
            float dx = x - 0.5f;
            float dy = y - 0.5f;
            float radiusSqr = (dx * dx) + (dy * dy);
            return radiusSqr <= 0.25f && radiusSqr >= 0.16f;
        }

        private static Sprite Build(string name, System.Func<float, float, bool> filled)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float u = (x + 0.5f) / TextureSize;
                    float v = (y + 0.5f) / TextureSize;
                    pixels[(y * TextureSize) + x] = filled(u, v)
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize),
                                       new Vector2(0.5f, 0.5f), TextureSize);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
