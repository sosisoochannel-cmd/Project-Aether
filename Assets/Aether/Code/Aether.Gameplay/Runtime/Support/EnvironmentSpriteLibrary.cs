using System;
using UnityEngine;

namespace Aether.Gameplay.Support
{
    /// <summary>
    /// Rich, production-quality hand-authored procedural textures and sprites for Aether's world.
    /// Provides cohesive material variation for terrain, canopy layers, monuments, interactive relics,
    /// and multi-plane parallax depth. All sprites are cached once and shared at runtime.
    /// </summary>
    public static class EnvironmentSpriteLibrary
    {
        private const int TileSize = 64;

        private static Sprite _ground;
        private static Sprite _deepRock;
        private static Sprite _platform;
        private static Sprite _canopyBack;
        private static Sprite _canopyFront;
        private static Sprite _storyMarker;
        private static Sprite _discovery;
        private static Sprite _checkpoint;
        private static Sprite _exit;
        private static Sprite _skyBackdrop;
        private static Sprite _mountainRidge;
        private static Sprite _forestMidground;
        private static Sprite _foregroundFoliage;
        private static Sprite _moteParticle;

        public static Sprite Ground
        {
            get { if (_ground == null) _ground = BuildTile("Aether_GroundTile", DrawGround); return _ground; }
        }

        public static Sprite DeepRock
        {
            get { if (_deepRock == null) _deepRock = BuildTile("Aether_DeepRockTile", DrawDeepRock); return _deepRock; }
        }

        public static Sprite Platform
        {
            get { if (_platform == null) _platform = BuildTile("Aether_PlatformTile", DrawPlatform); return _platform; }
        }

        public static Sprite CanopyBack
        {
            get { if (_canopyBack == null) _canopyBack = BuildTile("Aether_CanopyBackTile", DrawCanopyBack); return _canopyBack; }
        }

        public static Sprite CanopyFront
        {
            get { if (_canopyFront == null) _canopyFront = BuildTile("Aether_CanopyFrontTile", DrawCanopyFront); return _canopyFront; }
        }

        public static Sprite StoryMarker
        {
            get { if (_storyMarker == null) _storyMarker = Build("Aether_StoryMarker", 64, 64, DrawStoryMarker, TextureWrapMode.Clamp, 64f); return _storyMarker; }
        }

        public static Sprite Discovery
        {
            get { if (_discovery == null) _discovery = Build("Aether_DiscoveryRelic", 64, 64, DrawDiscovery, TextureWrapMode.Clamp, 64f); return _discovery; }
        }

        public static Sprite Checkpoint
        {
            get { if (_checkpoint == null) _checkpoint = Build("Aether_CheckpointDais", 96, 96, DrawCheckpoint, TextureWrapMode.Clamp, 96f); return _checkpoint; }
        }

        public static Sprite Exit
        {
            get { if (_exit == null) _exit = Build("Aether_ExitPortal", 96, 128, DrawExit, TextureWrapMode.Clamp, 64f); return _exit; }
        }

        public static Sprite SkyBackdrop
        {
            get { if (_skyBackdrop == null) _skyBackdrop = Build("Aether_SkyBackdrop", 128, 256, DrawSkyBackdrop, TextureWrapMode.Clamp, 64f); return _skyBackdrop; }
        }

        public static Sprite MountainRidge
        {
            get { if (_mountainRidge == null) _mountainRidge = Build("Aether_MountainRidge", 256, 128, DrawMountainRidge, TextureWrapMode.Repeat, 64f); return _mountainRidge; }
        }

        public static Sprite ForestMidground
        {
            get { if (_forestMidground == null) _forestMidground = Build("Aether_ForestMidground", 256, 128, DrawForestMidground, TextureWrapMode.Repeat, 64f); return _forestMidground; }
        }

        public static Sprite ForegroundFoliage
        {
            get { if (_foregroundFoliage == null) _foregroundFoliage = Build("Aether_ForegroundFoliage", 256, 64, DrawForegroundFoliage, TextureWrapMode.Repeat, 64f); return _foregroundFoliage; }
        }

        public static Sprite MoteParticle
        {
            get { if (_moteParticle == null) _moteParticle = Build("Aether_MoteParticle", 32, 32, DrawMote, TextureWrapMode.Clamp, 32f); return _moteParticle; }
        }

        private static Sprite BuildTile(string name, Action<Color32[], int, int> draw)
        {
            return Build(name, TileSize, TileSize, draw, TextureWrapMode.Repeat, TileSize);
        }

        private static Sprite Build(string name, int width, int height, Action<Color32[], int, int> draw,
                                   TextureWrapMode wrapMode, float pixelsPerUnit)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = wrapMode,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[width * height];
            draw(pixels, width, height);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height),
                                       new Vector2(0.5f, 0.5f), pixelsPerUnit);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // ---- Terrain: Ground, DeepRock, Platform ------------------------------------------------

        private static void DrawGround(Color32[] p, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float noise = Hash(x / 4, y / 4, 11);
                    byte r = (byte)Mathf.Clamp(38 + Mathf.RoundToInt(noise * 14f), 0, 255);
                    byte g = (byte)Mathf.Clamp(54 + Mathf.RoundToInt(noise * 16f), 0, 255);
                    byte b = (byte)Mathf.Clamp(62 + Mathf.RoundToInt(noise * 18f), 0, 255);

                    // Organic moss top turf
                    int mossThreshold = 55 + Mathf.RoundToInt(Hash(x / 3, y, 7) * 5f);
                    if (y >= mossThreshold)
                    {
                        r = (byte)Mathf.Clamp(64 + Mathf.RoundToInt(noise * 20f), 0, 255);
                        g = (byte)Mathf.Clamp(142 + Mathf.RoundToInt(noise * 26f), 0, 255);
                        b = (byte)Mathf.Clamp(76 + Mathf.RoundToInt(noise * 20f), 0, 255);
                    }
                    else if (y >= 53 && Hash(x / 5, y, 5) > 0.38f)
                    {
                        // Moss undertone and root tendrils
                        r = (byte)Mathf.Clamp(46 + Mathf.RoundToInt(noise * 15f), 0, 255);
                        g = (byte)Mathf.Clamp(98 + Mathf.RoundToInt(noise * 20f), 0, 255);
                        b = (byte)Mathf.Clamp(58 + Mathf.RoundToInt(noise * 15f), 0, 255);
                    }

                    // Pebble flecks
                    if (Hash(x, y, 17) > 0.982f)
                    {
                        r = 78; g = 110; b = 120;
                    }

                    // Fissures
                    if (SegmentDistance(x, y, 13f, 43f, 19f, 36f) < 0.8f
                        || SegmentDistance(x, y, 44f, 23f, 49f, 18f) < 0.8f)
                    {
                        r = (byte)(r * 0.55f);
                        g = (byte)(g * 0.55f);
                        b = (byte)(b * 0.55f);
                    }

                    p[y * w + x] = new Color32(r, g, b, 255);
                }
            }
        }

        private static void DrawDeepRock(Color32[] p, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float noise = Hash(x / 3, y / 3, 29);
                    byte r = (byte)Mathf.Clamp(22 + Mathf.RoundToInt(noise * 12f), 0, 255);
                    byte g = (byte)Mathf.Clamp(34 + Mathf.RoundToInt(noise * 15f), 0, 255);
                    byte b = (byte)Mathf.Clamp(45 + Mathf.RoundToInt(noise * 18f), 0, 255);

                    // Fractures
                    float crackA = SegmentDistance(x, y, 10f, 52f, 18f, 43f);
                    float crackB = SegmentDistance(x, y, 18f, 43f, 15f, 34f);
                    float crackC = SegmentDistance(x, y, 43f, 17f, 50f, 10f);
                    if (crackA < 0.85f || crackB < 0.65f || crackC < 0.7f)
                    {
                        r = 10; g = 16; b = 22;
                    }
                    else if (Hash(x, y, 77) > 0.965f)
                    {
                        // Turquoise mineral veins
                        r = 55; g = 175; b = 190;
                    }

                    if (y >= 61)
                    {
                        r = (byte)Mathf.Min(255, r + 15);
                        g = (byte)Mathf.Min(255, g + 20);
                        b = (byte)Mathf.Min(255, b + 25);
                    }

                    p[y * w + x] = new Color32(r, g, b, 255);
                }
            }
        }

        private static void DrawPlatform(Color32[] p, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float noise = Hash(x / 5, y / 4, 43);
                    byte r = (byte)Mathf.Clamp(75 + Mathf.RoundToInt(noise * 18f), 0, 255);
                    byte g = (byte)Mathf.Clamp(105 + Mathf.RoundToInt(noise * 22f), 0, 255);
                    byte b = (byte)Mathf.Clamp(118 + Mathf.RoundToInt(noise * 24f), 0, 255);

                    if (y >= 60)
                    {
                        // Chiseled upper rim
                        r = 115; g = 150; b = 162;
                    }
                    else if (y >= 57)
                    {
                        // Mossy edge
                        r = 68; g = 135; b = 78;
                    }
                    else if (y <= 4)
                    {
                        // Underside shadow
                        r = 28; g = 42; b = 50;
                    }
                    else if (y >= 51 && y <= 53)
                    {
                        // Carved groove
                        r = 42; g = 62; b = 72;
                    }

                    p[y * w + x] = new Color32(r, g, b, 255);
                }
            }
        }

        // ---- Canopy: Back & Front -------------------------------------------------------------

        private static void DrawCanopyBack(Color32[] p, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float noise = Hash(x / 4, y / 4, 53);
                    byte r = (byte)Mathf.Clamp(16 + Mathf.RoundToInt(noise * 16f), 0, 255);
                    byte g = (byte)Mathf.Clamp(42 + Mathf.RoundToInt(noise * 26f), 0, 255);
                    byte b = (byte)Mathf.Clamp(38 + Mathf.RoundToInt(noise * 22f), 0, 255);

                    // Clustered pine needles / foliage dappling
                    if (Hash(x / 2, y / 2, 89) > 0.65f)
                    {
                        r = (byte)Mathf.Min(255, r + 16);
                        g = (byte)Mathf.Min(255, g + 32);
                        b = (byte)Mathf.Min(255, b + 24);
                    }

                    p[y * w + x] = new Color32(r, g, b, 235);
                }
            }
        }

        private static void DrawCanopyFront(Color32[] p, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Hanging vine tendrils draping from above
                    float vineCurve = Mathf.Sin((x / (float)w) * Mathf.PI * 4f) * 12f;
                    float tendrilReach = 28f + vineCurve + Hash(x / 3, 0, 97) * 16f;

                    if (y >= h - tendrilReach)
                    {
                        float noise = Hash(x / 3, y / 3, 61);
                        byte r = (byte)Mathf.Clamp(32 + Mathf.RoundToInt(noise * 30f), 0, 255);
                        byte g = (byte)Mathf.Clamp(88 + Mathf.RoundToInt(noise * 55f), 0, 255);
                        byte b = (byte)Mathf.Clamp(46 + Mathf.RoundToInt(noise * 35f), 0, 255);
                        byte a = (byte)Mathf.Clamp(190 + Mathf.RoundToInt(noise * 50f), 0, 240);
                        p[y * w + x] = new Color32(r, g, b, a);
                    }
                    else
                    {
                        p[y * w + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }
        }

        // ---- Entities: StoryMarker, Discovery, Checkpoint, Exit --------------------------------

        private static void DrawStoryMarker(Color32[] p, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Upright carved stone stele shape
                    float centerX = w * 0.5f;
                    float halfWidth = 12f - (y / (float)h) * 3f;
                    float dx = Mathf.Abs(x - centerX);

                    if (dx <= halfWidth && y >= 6 && y <= h - 8)
                    {
                        float noise = Hash(x, y, 71);
                        byte r = (byte)(58 + Mathf.RoundToInt(noise * 14f));
                        byte g = (byte)(80 + Mathf.RoundToInt(noise * 16f));
                        byte b = (byte)(90 + Mathf.RoundToInt(noise * 18f));

                        // Base moss
                        if (y <= 14)
                        {
                            r = 45; g = 110; b = 55;
                        }

                        // Carved glowing rune down center
                        if (dx <= 1.5f && (y % 8 >= 2 && y % 8 <= 6) && y > 16 && y < h - 14)
                        {
                            r = 85; g = 240; b = 235;
                        }
                        else if (dx <= 3.5f && (y % 8 >= 2 && y % 8 <= 6) && y > 16 && y < h - 14)
                        {
                            r = 45; g = 145; b = 160; // rune glow
                        }

                        p[y * w + x] = new Color32(r, g, b, 255);
                    }
                    else
                    {
                        p[y * w + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }
        }

        private static void DrawDiscovery(Color32[] p, int w, int h)
        {
            float cx = w * 0.5f;
            float cy = h * 0.5f;
            float radius = 22f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Diamond crystal geometry
                    float diamondDist = (Mathf.Abs(dx) + Mathf.Abs(dy)) / radius;

                    if (diamondDist <= 1.0f)
                    {
                        // Inner facet lighting
                        float facet = 1f - diamondDist;
                        byte r = (byte)Mathf.Clamp(60 + Mathf.RoundToInt(facet * 185f), 0, 255);
                        byte g = (byte)Mathf.Clamp(210 + Mathf.RoundToInt(facet * 45f), 0, 255);
                        byte b = (byte)Mathf.Clamp(235 + Mathf.RoundToInt(facet * 20f), 0, 255);
                        p[y * w + x] = new Color32(r, g, b, 255);
                    }
                    else if (dist <= radius + 5f)
                    {
                        // Soft ethereal glow halo
                        float aura = 1f - ((dist - radius) / 5f);
                        byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(aura * 110f), 0, 255);
                        p[y * w + x] = new Color32(80, 230, 245, a);
                    }
                    else
                    {
                        p[y * w + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }
        }

        private static void DrawCheckpoint(Color32[] p, int w, int h)
        {
            float cx = w * 0.5f;
            float cy = h * 0.5f;
            float outerR = 38f;
            float innerR = 24f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist >= innerR - 2f && dist <= outerR)
                    {
                        // Ancient stone runic dial
                        float angle = Mathf.Atan2(dy, dx);
                        bool isRuneMark = Mathf.Abs(Mathf.Sin(angle * 6f)) > 0.75f;

                        byte r = 68; byte g = 92; byte b = 104;
                        if (dist >= innerR && dist <= innerR + 5f)
                        {
                            // Inlaid cyan energy channel
                            r = 75; g = 225; b = 238;
                        }
                        else if (isRuneMark && dist > innerR + 5f && dist < outerR - 4f)
                        {
                            r = 110; g = 240; b = 245; // glowing rune glyph
                        }

                        p[y * w + x] = new Color32(r, g, b, 255);
                    }
                    else if (dist < innerR - 2f)
                    {
                        // Glowing sanctuary focal core
                        float core = 1f - (dist / (innerR - 2f));
                        byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(core * 135f), 0, 255);
                        p[y * w + x] = new Color32(60, 215, 235, a);
                    }
                    else
                    {
                        p[y * w + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }
        }

        private static void DrawExit(Color32[] p, int w, int h)
        {
            float cx = w * 0.5f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x - cx);
                    bool isPillar = (dx >= 26f && dx <= 42f && y <= h - 22);
                    bool isLintel = (dx <= 44f && y > h - 26 && y <= h - 4);
                    bool isArchThreshold = (dx < 26f && y <= h - 26);

                    if (isPillar || isLintel)
                    {
                        // Megalithic weathered stone arch
                        float noise = Hash(x / 2, y / 2, 83);
                        byte r = (byte)(62 + Mathf.RoundToInt(noise * 16f));
                        byte g = (byte)(85 + Mathf.RoundToInt(noise * 18f));
                        byte b = (byte)(95 + Mathf.RoundToInt(noise * 20f));

                        // Creeping moss on stone
                        if (y < 24 || (x % 7 == 0))
                        {
                            r = 45; g = 115; b = 60;
                        }

                        p[y * w + x] = new Color32(r, g, b, 255);
                    }
                    else if (isArchThreshold)
                    {
                        // Swirling ethereal portal threshold
                        float u = dx / 26f;
                        float v = y / (float)(h - 26);
                        float vortex = Mathf.Sin(v * Mathf.PI * 3f + u * 2f);
                        byte r = (byte)Mathf.Clamp(40 + Mathf.RoundToInt(vortex * 30f), 0, 255);
                        byte g = (byte)Mathf.Clamp(180 + Mathf.RoundToInt(vortex * 50f), 0, 255);
                        byte b = (byte)Mathf.Clamp(225 + Mathf.RoundToInt(vortex * 30f), 0, 255);
                        byte a = (byte)Mathf.Clamp(140 + Mathf.RoundToInt((1f - u) * 90f), 0, 240);

                        p[y * w + x] = new Color32(r, g, b, a);
                    }
                    else
                    {
                        p[y * w + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }
        }

        // ---- Parallax Environment Sprites ------------------------------------------------------

        private static void DrawSkyBackdrop(Color32[] p, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);
                // Deep celestial midnight blue down to pale twilight forest horizon
                byte r = (byte)Mathf.RoundToInt(Mathf.Lerp(22f, 8f, t));
                byte g = (byte)Mathf.RoundToInt(Mathf.Lerp(58f, 18f, t));
                byte b = (byte)Mathf.RoundToInt(Mathf.Lerp(72f, 32f, t));

                for (int x = 0; x < w; x++)
                {
                    p[y * w + x] = new Color32(r, g, b, 255);
                }
            }
        }

        private static void DrawMountainRidge(Color32[] p, int w, int h)
        {
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w;
                // Jagged peaks profile
                float peak = Mathf.Sin(u * Mathf.PI * 4f) * 24f
                           + Mathf.Sin(u * Mathf.PI * 9f) * 14f
                           + 48f;

                for (int y = 0; y < h; y++)
                {
                    if (y <= peak)
                    {
                        float heightRatio = y / peak;
                        byte r = (byte)Mathf.RoundToInt(Mathf.Lerp(14f, 24f, heightRatio));
                        byte g = (byte)Mathf.RoundToInt(Mathf.Lerp(32f, 52f, heightRatio));
                        byte b = (byte)Mathf.RoundToInt(Mathf.Lerp(42f, 66f, heightRatio));
                        p[y * w + x] = new Color32(r, g, b, 255);
                    }
                    else
                    {
                        p[y * w + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }
        }

        private static void DrawForestMidground(Color32[] p, int w, int h)
        {
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w;
                float canopyLine = 36f + Mathf.Sin(u * Mathf.PI * 6f) * 16f
                                  + Mathf.Cos(u * Mathf.PI * 12f) * 8f;

                for (int y = 0; y < h; y++)
                {
                    if (y <= canopyLine)
                    {
                        float ratio = y / canopyLine;
                        byte r = (byte)Mathf.RoundToInt(Mathf.Lerp(12f, 22f, ratio));
                        byte g = (byte)Mathf.RoundToInt(Mathf.Lerp(28f, 50f, ratio));
                        byte b = (byte)Mathf.RoundToInt(Mathf.Lerp(32f, 56f, ratio));
                        byte a = (byte)Mathf.RoundToInt(Mathf.Lerp(160f, 235f, ratio));
                        p[y * w + x] = new Color32(r, g, b, a);
                    }
                    else
                    {
                        p[y * w + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }
        }

        private static void DrawForegroundFoliage(Color32[] p, int w, int h)
        {
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w;
                float vineReach = 18f + Mathf.Sin(u * Mathf.PI * 8f) * 12f;

                for (int y = 0; y < h; y++)
                {
                    if (y >= h - vineReach || y <= 6f + Mathf.Sin(u * Mathf.PI * 6f) * 4f)
                    {
                        // Dark mossy foreground silhouettes
                        p[y * w + x] = new Color32(8, 18, 16, 230);
                    }
                    else
                    {
                        p[y * w + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }
        }

        private static void DrawMote(Color32[] p, int w, int h)
        {
            float cx = w * 0.5f;
            float cy = h * 0.5f;
            float maxR = w * 0.48f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist <= maxR)
                    {
                        float falloff = Mathf.Pow(1f - (dist / maxR), 1.8f);
                        byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(falloff * 255f), 0, 255);
                        p[y * w + x] = new Color32(255, 255, 255, a);
                    }
                    else
                    {
                        p[y * w + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }
        }

        // ---- Utilities ------------------------------------------------------------------------

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
