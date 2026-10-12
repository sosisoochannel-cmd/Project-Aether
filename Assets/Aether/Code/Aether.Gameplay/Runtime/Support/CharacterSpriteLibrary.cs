using System;
using UnityEngine;

namespace Aether.Gameplay.Support
{
    /// <summary>
    /// Small, hand-authored-at-runtime character illustrations for the first playable slice.
    /// These are deliberately composed silhouettes, not the old full-rectangle debug sprites.
    /// They are cached once and shared; gameplay colliders remain independent of their artwork.
    /// </summary>
    public static class CharacterSpriteLibrary
    {
        private const int Size = 96;
        private static Sprite _player;
        private static Sprite _forestStalker;

        public static Sprite Player
        {
            get
            {
                if (_player == null) _player = Build("Aether_Ranger", DrawPlayer);
                return _player;
            }
        }

        public static Sprite ForestStalker
        {
            get
            {
                if (_forestStalker == null) _forestStalker = Build("Aether_ForestStalker", DrawForestStalker);
                return _forestStalker;
            }
        }

        private static Sprite Build(string name, Action<Color32[], int> draw)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[Size * Size];
            draw(pixels, Size);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size),
                                       new Vector2(0.5f, 0.5f), Size);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static void DrawPlayer(Color32[] p, int s)
        {
            Color ink = new Color(0.055f, 0.075f, 0.12f, 1f);
            Color navy = new Color(0.10f, 0.17f, 0.28f, 1f);
            Color steel = new Color(0.52f, 0.70f, 0.82f, 1f);
            Color ice = new Color(0.82f, 0.94f, 1f, 1f);
            Color gold = new Color(1f, 0.70f, 0.30f, 1f);
            Color cyan = new Color(0.28f, 0.92f, 1f, 1f);

            // Trailing half-cloak and boots are behind the armour.
            Line(p, s, 43, 57, 25, 43, 7.0f, ink, ink, 1.5f);
            Line(p, s, 42, 55, 27, 45, 4.3f, navy, steel, 0f);
            Line(p, s, 42, 33, 39, 18, 4.7f, ink, ink, 1.5f);
            Line(p, s, 54, 33, 58, 18, 4.7f, ink, ink, 1.5f);
            RoundRect(p, s, 33, 12, 44, 8, 3, ink, ink, 0f);
            RoundRect(p, s, 52, 12, 63, 8, 3, ink, ink, 0f);
            Line(p, s, 42, 31, 41, 19, 2.8f, steel, navy, 0f);
            Line(p, s, 55, 31, 58, 19, 2.8f, steel, navy, 0f);

            // Arms, with small shoulder plates.
            Line(p, s, 39, 54, 33, 41, 5.0f, ink, ink, 1.4f);
            Line(p, s, 39, 53, 34, 42, 3.0f, steel, navy, 0f);
            Line(p, s, 57, 54, 64, 43, 5.0f, ink, ink, 1.4f);
            Line(p, s, 57, 53, 63, 44, 3.0f, steel, navy, 0f);
            Ellipse(p, s, 38, 55, 5.4f, 5.0f, ink, ink, 0f);
            Ellipse(p, s, 58, 55, 5.4f, 5.0f, gold, gold, 0f);

            // Body armour, belt and a bright centre crest.
            RoundRect(p, s, 39, 37, 58, 59, 6, ink, ink, 0f);
            RoundRect(p, s, 41, 39, 56, 57, 5, steel, navy, 0f);
            Line(p, s, 42, 51, 55, 51, 1.6f, gold, gold, 0f);
            Ellipse(p, s, 49, 47, 3.2f, 4.0f, gold, gold, 0f);
            Ellipse(p, s, 49, 47, 1.5f, 2.1f, ice, cyan, 0f);
            Line(p, s, 42, 40, 46, 38, 1.1f, ink, ink, 0f);
            Line(p, s, 52, 38, 56, 40, 1.1f, ink, ink, 0f);

            // Hood, faceplate and visor. The silhouette reads even when the sprite is small.
            Ellipse(p, s, 49, 70, 13.2f, 14.5f, ink, ink, 0f);
            Ellipse(p, s, 49, 70, 10.8f, 12.0f, navy, steel, 0f);
            Ellipse(p, s, 49, 68, 8.0f, 6.0f, ink, ink, 0f);
            Line(p, s, 42, 69, 56, 69, 2.0f, cyan, steel, 0f);
            Ellipse(p, s, 43, 69, 1.1f, 1.1f, ice, ice, 0f);
            Ellipse(p, s, 55, 69, 1.1f, 1.1f, ice, ice, 0f);
            Line(p, s, 44, 79, 49, 83, 1.4f, ice, steel, 0f);
            Line(p, s, 49, 83, 55, 79, 1.4f, ice, steel, 0f);
            Line(p, s, 44, 60, 39, 56, 2.0f, gold, gold, 0f);
        }

        private static void DrawForestStalker(Color32[] p, int s)
        {
            Color ink = new Color(0.035f, 0.075f, 0.055f, 1f);
            Color bark = new Color(0.12f, 0.23f, 0.15f, 1f);
            Color moss = new Color(0.30f, 0.43f, 0.23f, 1f);
            Color leaf = new Color(0.52f, 0.64f, 0.29f, 1f);
            Color amber = new Color(1f, 0.60f, 0.20f, 1f);

            // Thorned limbs and root-like feet establish a non-human silhouette.
            Line(p, s, 39, 51, 25, 42, 5.0f, ink, ink, 0f);
            Line(p, s, 25, 42, 21, 48, 2.0f, moss, leaf, 0f);
            Line(p, s, 57, 51, 70, 41, 5.0f, ink, ink, 0f);
            Line(p, s, 70, 41, 76, 47, 2.0f, moss, leaf, 0f);
            Line(p, s, 42, 32, 36, 19, 4.7f, ink, ink, 0f);
            Line(p, s, 36, 19, 28, 17, 2.0f, bark, moss, 0f);
            Line(p, s, 54, 32, 61, 20, 4.7f, ink, ink, 0f);
            Line(p, s, 61, 20, 70, 18, 2.0f, bark, moss, 0f);

            // Branch antlers.
            Line(p, s, 42, 73, 35, 84, 3.4f, ink, ink, 0f);
            Line(p, s, 35, 84, 27, 86, 1.6f, moss, leaf, 0f);
            Line(p, s, 37, 81, 36, 88, 1.4f, moss, leaf, 0f);
            Line(p, s, 56, 73, 64, 84, 3.4f, ink, ink, 0f);
            Line(p, s, 64, 84, 73, 86, 1.6f, moss, leaf, 0f);
            Line(p, s, 62, 81, 62, 88, 1.4f, moss, leaf, 0f);

            // Ribbed trunk, dark mask and layered moss plates.
            Ellipse(p, s, 49, 49, 17.0f, 22.0f, ink, ink, 0f);
            Ellipse(p, s, 49, 49, 14.0f, 19.0f, bark, moss, 0f);
            Line(p, s, 43, 61, 40, 45, 2.0f, moss, bark, 0f);
            Line(p, s, 49, 64, 48, 43, 2.1f, leaf, bark, 0f);
            Line(p, s, 55, 60, 58, 46, 2.0f, moss, bark, 0f);
            Ellipse(p, s, 38, 55, 5.2f, 6.0f, ink, ink, 0f);
            Ellipse(p, s, 60, 55, 5.2f, 6.0f, ink, ink, 0f);
            Ellipse(p, s, 38, 55, 2.1f, 2.3f, leaf, moss, 0f);
            Ellipse(p, s, 60, 55, 2.1f, 2.3f, leaf, moss, 0f);

            // Narrow head with unmistakable amber eyes and a jagged jaw.
            Ellipse(p, s, 49, 70, 12.0f, 12.5f, ink, ink, 0f);
            Ellipse(p, s, 49, 70, 9.2f, 9.8f, bark, moss, 0f);
            Ellipse(p, s, 44, 71, 2.6f, 2.0f, amber, amber, 0f);
            Ellipse(p, s, 54, 71, 2.6f, 2.0f, amber, amber, 0f);
            Ellipse(p, s, 44, 71, 0.8f, 1.2f, ink, ink, 0f);
            Ellipse(p, s, 54, 71, 0.8f, 1.2f, ink, ink, 0f);
            Line(p, s, 42, 62, 46, 59, 1.7f, leaf, moss, 0f);
            Line(p, s, 46, 59, 49, 62, 1.7f, leaf, moss, 0f);
            Line(p, s, 49, 62, 53, 59, 1.7f, leaf, moss, 0f);
            Line(p, s, 53, 59, 57, 62, 1.7f, leaf, moss, 0f);
        }

        private static void Ellipse(Color32[] p, int s, float cx, float cy, float rx, float ry,
                                    Color top, Color bottom, float outlineWidth)
        {
            if (outlineWidth > 0f)
                Paint(p, s, (x, y) => EllipseDistance(x, y, cx, cy, rx + outlineWidth, ry + outlineWidth), top * 0f + new Color(0.035f, 0.045f, 0.065f, 1f), top * 0f + new Color(0.035f, 0.045f, 0.065f, 1f));
            Paint(p, s, (x, y) => EllipseDistance(x, y, cx, cy, rx, ry), top, bottom);
        }

        private static void RoundRect(Color32[] p, int s, float x, float y, float w, float h, float radius,
                                      Color top, Color bottom, float outlineWidth)
        {
            if (outlineWidth > 0f)
                Paint(p, s, (px, py) => RoundRectDistance(px, py, x + w * 0.5f, y + h * 0.5f,
                    w * 0.5f + outlineWidth, h * 0.5f + outlineWidth, radius + outlineWidth),
                    new Color(0.035f, 0.045f, 0.065f, 1f), new Color(0.035f, 0.045f, 0.065f, 1f));
            Paint(p, s, (px, py) => RoundRectDistance(px, py, x + w * 0.5f, y + h * 0.5f,
                w * 0.5f, h * 0.5f, radius), top, bottom);
        }

        private static void Line(Color32[] p, int s, float x1, float y1, float x2, float y2,
                                 float radius, Color top, Color bottom, float outlineWidth)
        {
            if (outlineWidth > 0f)
                Paint(p, s, (x, y) => SegmentDistance(x, y, x1, y1, x2, y2) - radius - outlineWidth,
                    new Color(0.035f, 0.045f, 0.065f, 1f), new Color(0.035f, 0.045f, 0.065f, 1f));
            Paint(p, s, (x, y) => SegmentDistance(x, y, x1, y1, x2, y2) - radius, top, bottom);
        }

        private static float EllipseDistance(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx;
            float dy = (y - cy) / ry;
            return (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(rx, ry);
        }

        private static float RoundRectDistance(float x, float y, float cx, float cy,
                                               float hx, float hy, float radius)
        {
            float qx = Mathf.Abs(x - cx) - hx + radius;
            float qy = Mathf.Abs(y - cy) - hy + radius;
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f)
                                     + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static float SegmentDistance(float x, float y, float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float lengthSq = dx * dx + dy * dy;
            float t = lengthSq > 0.0001f
                ? Mathf.Clamp01(((x - x1) * dx + (y - y1) * dy) / lengthSq) : 0f;
            float px = x1 + t * dx;
            float py = y1 + t * dy;
            float ox = x - px;
            float oy = y - py;
            return Mathf.Sqrt(ox * ox + oy * oy);
        }

        private static void Paint(Color32[] pixels, int size, Func<float, float, float> distance,
                                  Color top, Color bottom)
        {
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)(size - 1);
                Color shade = Color.Lerp(bottom, top, v);
                for (int x = 0; x < size; x++)
                {
                    float coverage = Mathf.Clamp01(0.5f - distance(x + 0.5f, y + 0.5f));
                    if (coverage <= 0f) continue;
                    Color source = shade;
                    source.a *= coverage;
                    int i = y * size + x;
                    Color destination = pixels[i];
                    float outA = source.a + destination.a / 255f * (1f - source.a);
                    if (outA <= 0f) continue;
                    Color result = (source * source.a + destination * (destination.a / 255f) * (1f - source.a)) / outA;
                    result.a = outA;
                    pixels[i] = result;
                }
            }
        }
    }
}