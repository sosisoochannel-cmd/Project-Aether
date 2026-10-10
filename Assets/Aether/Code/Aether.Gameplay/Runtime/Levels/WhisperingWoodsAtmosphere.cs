using System.Collections.Generic;
using Aether.Data.Levels;
using Aether.Gameplay.Support;
using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// Lightweight, deterministic atmosphere for Whispering Woods.
    /// It is presentation-only: gameplay, collision and traversal never depend on it.
    /// </summary>
    public sealed class WhisperingWoodsAtmosphere : MonoBehaviour
    {
        private readonly List<Transform> _fireflies = new List<Transform>();
        private readonly List<SpriteRenderer> _fireflyRenderers = new List<SpriteRenderer>();
        private readonly List<float> _phase = new List<float>();
        private readonly List<float> _speed = new List<float>();
        private readonly List<float> _baseY = new List<float>();

        private float _time;
        private float _windTime;
        private bool _hasSilentZone;
        private Vector2 _silentZoneCentre;
        private Texture2D _mistTexture;
        private Sprite _mistSprite;

        public static void Create(LevelData level, Transform parent)
        {
            if (level == null || parent == null || level.Id != "region2.whispering_woods") return;

            var host = new GameObject("WhisperingWoodsAtmosphere");
            host.transform.SetParent(parent, false);
            host.AddComponent<WhisperingWoodsAtmosphere>().Build(level);
        }

        private void Build(LevelData level)
        {
            // Broad, inexpensive layers establish depth without adding per-tile renderers or
            // introducing an art-atlas dependency before the region's traversal is locked.
            CreateBand("DistantMist", new Color(0.08f, 0.16f, 0.14f, 0.72f),
                new Vector2(level.WorldSize.x * 0.5f, level.WorldSize.y * 0.62f),
                new Vector2(level.WorldSize.x + 10f, level.WorldSize.y * 0.55f), -40, transform);

            CreateBand("DeepForest", new Color(0.045f, 0.105f, 0.075f, 0.92f),
                new Vector2(level.WorldSize.x * 0.5f, level.WorldSize.y * 0.48f),
                new Vector2(level.WorldSize.x + 8f, level.WorldSize.y * 0.45f), -35, transform);

            CreateBand("NearCanopy", new Color(0.10f, 0.19f, 0.11f, 0.34f),
                new Vector2(level.WorldSize.x * 0.5f, level.WorldSize.y * 0.78f),
                new Vector2(level.WorldSize.x + 8f, level.WorldSize.y * 0.34f), -25, transform);

            CreateBand("HushVeil", new Color(0.18f, 0.30f, 0.20f, 0.11f),
                new Vector2(level.WorldSize.x * 0.48f, level.WorldSize.y * 0.52f),
                new Vector2(level.WorldSize.x * 0.72f, level.WorldSize.y * 0.24f), -15, transform);

            BuildSilentZone(level);

            const int count = 14;
            for (int i = 0; i < count; i++)
            {
                float normalized = (i + 1f) / (count + 1f);
                float x = Mathf.Lerp(5f, Mathf.Max(6f, level.WorldSize.x - 5f), normalized);
                float y = 3.0f + ((i * 1.73f) % 5.5f);

                var go = new GameObject($"Firefly_{i + 1:00}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(x, y, 0f);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = PlaceholderVisuals.Circle;
                renderer.color = FireflyColour;
                renderer.sortingOrder = -5;
                go.transform.localScale = Vector3.one * (0.045f + ((i % 3) * 0.012f));

                _fireflies.Add(go.transform);
                _fireflyRenderers.Add(renderer);
                _phase.Add(i * 0.71f);
                _speed.Add(0.65f + ((i % 4) * 0.11f));
                _baseY.Add(y);
            }
        }

        private static readonly Color FireflyColour = new Color(0.82f, 0.96f, 0.55f, 0.68f);
        private static readonly Color SilentFireflyColour = new Color(0.56f, 0.76f, 0.78f, 0.20f);

        /// <summary>
        /// Gives the authored silent-zone marker a local, feathered atmosphere. The marker remains
        /// the source of truth: moving it in level data moves the treatment without changing code.
        /// A tiny generated radial texture avoids hard-edged rectangles and is allocated only once.
        /// </summary>
        private void BuildSilentZone(LevelData level)
        {
            LevelEntity marker = null;
            for (int i = 0; i < level.Entities.Count; i++)
            {
                LevelEntity candidate = level.Entities[i];
                if (candidate.Kind != LevelEntityKind.StoryMarker || candidate.MarkerKind != "silent_zone")
                    continue;

                marker = candidate;
                break;
            }

            if (marker == null) return;

            _hasSilentZone = true;
            _silentZoneCentre = level.TileBottomCenter(marker.Position.x, marker.Position.y)
                                + new Vector2(0f, 3.4f);

            const int resolution = 64;
            _mistTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = "WhisperingWoods_SilentZone_RadialMist",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color32[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                float ny = (((y + 0.5f) / resolution) * 2f) - 1f;
                for (int x = 0; x < resolution; x++)
                {
                    float nx = (((x + 0.5f) / resolution) * 2f) - 1f;
                    float radius = Mathf.Sqrt((nx * nx) + (ny * ny));
                    float falloff = Mathf.Clamp01(1f - radius);
                    // The curve is deliberately soft at the edge: no visible circular stamp.
                    byte alpha = (byte)(Mathf.Pow(falloff, 2.2f) * 255f);
                    pixels[(y * resolution) + x] = new Color32(255, 255, 255, alpha);
                }
            }

            _mistTexture.SetPixels32(pixels);
            _mistTexture.Apply(false, true);
            _mistSprite = Sprite.Create(_mistTexture, new Rect(0f, 0f, resolution, resolution),
                                        new Vector2(0.5f, 0.5f), resolution);
            _mistSprite.name = "WhisperingWoods_SilentZone_RadialMist";
            _mistSprite.hideFlags = HideFlags.DontSave;

            CreateMistLayer("SilentZoneMist_Distant",
                new Color(0.30f, 0.55f, 0.53f, 0.22f),
                _silentZoneCentre + new Vector2(0.5f, 0.25f), new Vector2(24f, 13f), -12);
            CreateMistLayer("SilentZoneMist_Near",
                new Color(0.46f, 0.64f, 0.60f, 0.15f),
                _silentZoneCentre + new Vector2(-1.2f, -0.45f), new Vector2(13f, 7f), -11);
        }

        private void CreateMistLayer(string name, Color colour, Vector2 position, Vector2 size, int sorting)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = _mistSprite;
            renderer.color = colour;
            renderer.sortingOrder = sorting;
        }

        private static void CreateBand(string name, Color color, Vector2 position, Vector2 scale,
                                       int sorting, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderVisuals.Square;
            renderer.color = color;
            renderer.sortingOrder = sorting;
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        }

        private void Update()
        {
            _time += Time.deltaTime;
            _windTime += Time.deltaTime * 0.18f;

            for (int i = 0; i < _fireflies.Count; i++)
            {
                Transform firefly = _fireflies[i];
                SpriteRenderer renderer = _fireflyRenderers[i];
                if (firefly == null || renderer == null) continue;

                float phase = _phase[i] + (_time * _speed[i]);
                float drift = Mathf.Sin(phase * 0.73f) * 0.22f;
                float lift = Mathf.Sin(phase) * 0.18f;
                float pulse = 0.75f + (Mathf.Sin(phase * 1.37f) * 0.25f);
                float wind = Mathf.Sin(_windTime + (i * 0.31f)) * 0.025f;

                Vector3 p = firefly.localPosition;
                p.x += (drift + wind) * Time.deltaTime;
                p.y = _baseY[i] + lift;
                firefly.localPosition = p;

                float hush = 0f;
                if (_hasSilentZone)
                {
                    float distance = Vector2.Distance(new Vector2(p.x, p.y), _silentZoneCentre);
                    hush = 1f - Mathf.SmoothStep(0f, 9f, distance);
                }

                // Life does not stop in the silent zone; it becomes quieter and cooler. This is
                // purely visual and never changes collision, enemy logic, or traversal.
                renderer.color = Color.Lerp(FireflyColour, SilentFireflyColour, hush);
                float scale = 0.045f + ((i % 3) * 0.012f);
                float pulseStrength = Mathf.Lerp(0.18f, 0.10f, hush);
                firefly.localScale = Vector3.one * (scale * (1f + ((pulse - 0.75f) * pulseStrength)));
            }
        }

        private void OnDestroy()
        {
            if (_mistSprite != null) Destroy(_mistSprite);
            if (_mistTexture != null) Destroy(_mistTexture);
        }
    }
}
