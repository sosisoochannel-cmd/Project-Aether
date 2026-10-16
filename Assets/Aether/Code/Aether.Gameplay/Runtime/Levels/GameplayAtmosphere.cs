using Aether.Core.Settings;
using Aether.Data.Levels;
using Aether.Gameplay.Settings;
using Aether.Gameplay.Support;
using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// Restrained floating luminous aether motes and forest spores in the playable area.
    /// Mobile-optimized: fixed sprite instances, zero per-frame allocation, gentle trigonometric drift.
    /// Disables when reduced motion or low graphics tier is selected.
    /// </summary>
    public sealed class GameplayAtmosphere : MonoBehaviour
    {
        private const int MoteCount = 18;
        private Transform[] _moteTransforms;
        private SpriteRenderer[] _moteRenderers;
        private Vector3[] _baseOffsets;
        private float[] _phases;
        private float[] _speeds;
        private bool _active = true;

        /// <summary>Creates the atmospheric motes across the level.</summary>
        public static GameplayAtmosphere Attach(Transform parent, LevelData level)
        {
            var host = new GameObject("GameplayAtmosphere");
            host.transform.SetParent(parent, false);
            var atmosphere = host.AddComponent<GameplayAtmosphere>();
            atmosphere.Build(level);
            return atmosphere;
        }

        private void Build(LevelData level)
        {
            _moteTransforms = new Transform[MoteCount];
            _moteRenderers = new SpriteRenderer[MoteCount];
            _baseOffsets = new Vector3[MoteCount];
            _phases = new float[MoteCount];
            _speeds = new float[MoteCount];

            Sprite moteSprite = EnvironmentSpriteLibrary.MoteParticle;
            float worldWidth = level.Width * level.TileSize;
            float worldHeight = level.Height * level.TileSize;

            for (int i = 0; i < MoteCount; i++)
            {
                var go = new GameObject("AtmosphereMote_" + i.ToString("00"));
                go.transform.SetParent(transform, false);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = moteSprite;
                renderer.sortingOrder = 28;

                // Restrained palette: mystical turquoise cyan, pale emerald, and warm moonlight gold
                Color col;
                int variant = i % 3;
                if (variant == 0) col = new Color(0.35f, 0.95f, 0.95f, 0.45f);
                else if (variant == 1) col = new Color(0.40f, 0.90f, 0.50f, 0.40f);
                else col = new Color(0.95f, 0.92f, 0.70f, 0.35f);

                renderer.color = col;
                float scale = 0.35f + (i % 4) * 0.12f;
                go.transform.localScale = new Vector3(scale, scale, 1f);

                float x = (i + 0.5f) / MoteCount * worldWidth;
                float y = 5f + ((i * 7) % (int)Mathf.Max(6f, worldHeight - 6f));
                _baseOffsets[i] = new Vector3(x, y, 0f);
                go.transform.position = _baseOffsets[i];

                _moteTransforms[i] = go.transform;
                _moteRenderers[i] = renderer;
                _phases[i] = i * 1.37f;
                _speeds[i] = 0.4f + (i % 5) * 0.15f;
            }
        }

        private void Start()
        {
            ApplySettings();
        }

        /// <summary>Applies graphics and accessibility preferences.</summary>
        public void ApplySettings()
        {
            try
            {
                GameSettings settings = AetherSettings.Ensure().Values;
                if (settings.Accessibility != null && settings.Accessibility.ReducedMotion)
                    _active = false;
                else if (settings.Graphics != null && settings.Graphics.Tier == GraphicsTier.Low)
                    _active = false;
                else
                    _active = true;
            }
            catch
            {
                _active = true;
            }

            for (int i = 0; i < _moteRenderers.Length; i++)
            {
                if (_moteRenderers[i] != null)
                    _moteRenderers[i].enabled = _active;
            }
            enabled = _active;
        }

        private void Update()
        {
            if (!_active || Time.timeScale <= 0f) return;

            float time = Time.time;
            for (int i = 0; i < MoteCount; i++)
            {
                Transform t = _moteTransforms[i];
                if (t == null) continue;

                float p = _phases[i] + time * _speeds[i];
                float dx = Mathf.Sin(p) * 0.8f + Mathf.Cos(p * 0.5f) * 0.4f;
                float dy = Mathf.Cos(p * 0.7f) * 0.6f + Mathf.Sin(p * 0.3f) * 0.3f;

                t.position = _baseOffsets[i] + new Vector3(dx, dy, 0f);
            }
        }
    }
}
