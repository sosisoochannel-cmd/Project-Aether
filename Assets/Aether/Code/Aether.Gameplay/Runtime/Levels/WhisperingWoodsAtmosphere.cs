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
        private readonly List<float> _phase = new List<float>();
        private readonly List<float> _speed = new List<float>();
        private readonly List<float> _baseY = new List<float>();
        private float _time;

        public static void Create(LevelData level, Transform parent)
        {
            if (level == null || parent == null || level.Id != "region2.whispering_woods") return;

            var host = new GameObject("WhisperingWoodsAtmosphere");
            host.transform.SetParent(parent, false);
            host.AddComponent<WhisperingWoodsAtmosphere>().Build(level);
        }

        private void Build(LevelData level)
        {
            // The atmosphere is deliberately made from a few large, cheap sprites. It gives the
            // procedural level depth on a phone without introducing a texture/atlas dependency.
            CreateBand("DistantMist", new Color(0.08f, 0.16f, 0.14f, 0.72f),
                new Vector2(level.WorldSize.x * 0.5f, level.WorldSize.y * 0.62f),
                new Vector2(level.WorldSize.x + 10f, level.WorldSize.y * 0.55f), -40, transform);

            CreateBand("DeepForest", new Color(0.045f, 0.105f, 0.075f, 0.92f),
                new Vector2(level.WorldSize.x * 0.5f, level.WorldSize.y * 0.48f),
                new Vector2(level.WorldSize.x + 8f, level.WorldSize.y * 0.45f), -35, transform);

            CreateBand("NearCanopy", new Color(0.10f, 0.19f, 0.11f, 0.34f),
                new Vector2(level.WorldSize.x * 0.5f, level.WorldSize.y * 0.78f),
                new Vector2(level.WorldSize.x + 8f, level.WorldSize.y * 0.34f), -25, transform);

            const int count = 14;
            for (int i = 0; i < count; i++)
            {
                float normalized = (i + 1f) / (count + 1f);
                float x = Mathf.Lerp(5f, Mathf.Max(6f, level.WorldSize.x - 5f), normalized);
                float y = 3.0f + ((i * 1.73f) % 5.5f);

                var go = new GameObject($"Firefly_{i + 1:00}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(x, y, 0f);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = PlaceholderVisuals.Circle;
                renderer.color = new Color(0.82f, 0.96f, 0.55f, 0.68f);
                renderer.sortingOrder = -5;
                go.transform.localScale = Vector3.one * (0.045f + ((i % 3) * 0.012f));

                _fireflies.Add(go.transform);
                _phase.Add(i * 0.71f);
                _speed.Add(0.65f + ((i % 4) * 0.11f));
                _baseY.Add(y);
            }
        }

        private static void CreateBand(string name, Color color, Vector2 position, Vector2 scale, int sorting, Transform parent)
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

            for (int i = 0; i < _fireflies.Count; i++)
            {
                Transform firefly = _fireflies[i];
                if (firefly == null) continue;

                float phase = _phase[i] + (_time * _speed[i]);
                float drift = Mathf.Sin(phase * 0.73f) * 0.22f;
                float lift = Mathf.Sin(phase) * 0.18f;
                float pulse = 0.75f + (Mathf.Sin(phase * 1.37f) * 0.25f);

                Vector3 p = firefly.localPosition;
                p.x += drift * Time.deltaTime;
                p.y = _baseY[i] + lift;
                firefly.localPosition = p;

                float scale = 0.045f + ((i % 3) * 0.012f);
                firefly.localScale = Vector3.one * (scale * (0.82f + (pulse * 0.18f)));
            }
        }
    }
}
