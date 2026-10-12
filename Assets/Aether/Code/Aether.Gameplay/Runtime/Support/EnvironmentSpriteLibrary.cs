using System;
using UnityEngine;

namespace Aether.Gameplay.Support
{
    /// <summary>
    /// Repeatable, low-contrast surface textures for the first region's terrain.
    /// A SpriteRenderer tiles these at world-unit scale, so long merged colliders do not stretch
    /// one tiny detail across an entire screen. Gameplay geometry remains unchanged.
    /// </summary>
    public static class EnvironmentSpriteLibrary
    {
        private const int Size = 64;
        private static Sprite _ground;
        private static Sprite _deepRock;
        private static Sprite _platform;

        public static Sprite Ground
        {
            get { if (_ground == null) _ground = Build("Aether_GroundTile", GroundLuma, 11); return _ground; }
        }

        public static Sprite DeepRock
        {
            get { if (_deepRock == null) _deepRock = Build("Aether_DeepRockTile", RockLuma, 29); return _deepRock; }
        }

        public static Sprite Platform
        {
            get { if (_platform == null) _platform = Build("Aether_PlatformTile", PlatformLuma, 43); return _platform; }
        }

        private static Sprite Build(string name, Func<int, int, int, float> luminance, int seed)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(luminance(x, y, seed)) * 255f);
                    pixels[y * Size + x] = new Color32(value, value, value, 255);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size),
                                       new Vector2(0.5f, 0.5f), Size);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static float GroundLuma(int x, int y, int seed)
        {
            float value = 0.82f + Hash(x / 4, y / 4, seed) * 0.15f;
            // A broken, irregular top lip gives the earth a readable edge without drawing a grid.
            if (y >= 60) value = Mathf.Max(value, 0.96f + Hash(x / 3, y, seed + 3) * 0.04f);
            else if (y >= 58 && Hash(x / 5, y, seed + 5) > 0.38f) value *= 0.79f;
            // Tiny flecks and two hairline fissures break up the broad flat fills.
            if (Hash(x, y, seed + 13) > 0.986f) value *= 0.66f;
            if (SegmentDistance(x, y, 13f, 43f, 19f, 36f) < 0.7f
                || SegmentDistance(x, y, 44f, 23f, 49f, 18f) < 0.7f) value *= 0.68f;
            return value;
        }

        private static float RockLuma(int x, int y, int seed)
        {
            float value = 0.66f + Hash(x / 3, y / 3, seed) * 0.22f;
            if (Hash(x, y, seed + 7) > 0.978f) value *= 0.57f;
            // Angular seams make the underlayer read as fractured stone rather than brown soil.
            float crackA = SegmentDistance(x, y, 10f, 52f, 18f, 43f);
            float crackB = SegmentDistance(x, y, 18f, 43f, 15f, 34f);
            float crackC = SegmentDistance(x, y, 43f, 17f, 50f, 10f);
            if (crackA < 0.85f || crackB < 0.65f || crackC < 0.7f) value *= 0.53f;
            if (y >= 61) value *= 1.08f;
            return value;
        }

        private static float PlatformLuma(int x, int y, int seed)
        {
            float value = 0.78f + Hash(x / 5, y / 4, seed) * 0.12f;
            if (y >= 60) value = 1f;
            else if (y >= 57) value *= 0.75f;
            if (y <= 3) value *= 0.65f;
            if (Hash(x, y, seed + 17) > 0.992f) value *= 0.72f;
            return value;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint n = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                n = (n ^ (n >> 13)) * 1274126177u;
                n ^= n >> 16;
                return (n & 0x00ffffffu) / 16777215f;
            }
        }

        private static float SegmentDistance(float x, float y, float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float lengthSq = dx * dx + dy * dy;
            float t = lengthSq > 0.0001f
                ? Mathf.Clamp01(((x - x1) * dx + (y - y1) * dy) / lengthSq) : 0f;
            float ox = x - (x1 + t * dx);
            float oy = y - (y1 + t * dy);
            return Mathf.Sqrt(ox * ox + oy * oy);
        }
    }
}