using Aether.Core.Settings;
using Aether.Gameplay.Settings;
using UnityEngine;

namespace Aether.Gameplay.Levels
{
    /// <summary>
    /// Restrained 2D multi-plane parallax depth for the playable region.
    /// Follows camera motion at fractional rates per depth plane without allocating memory.
    /// Freezes when paused, and respects player preferences for reduced motion and graphics tier.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class ParallaxBackdrop2D : MonoBehaviour
    {
        [SerializeField] private Transform _skyLayer;
        [SerializeField] private Transform _mountainLayer;
        [SerializeField] private Transform _forestLayer;
        [SerializeField] private Transform _foregroundLayer;

        private const float SkyFactor = 0.02f;
        private const float MountainFactor = 0.12f;
        private const float ForestFactor = 0.30f;
        private const float ForegroundFactor = 0.20f;

        private Camera _camera;
        private Vector3 _initialCameraPos;
        private Vector3 _skyInitial;
        private Vector3 _mountainInitial;
        private Vector3 _forestInitial;
        private Vector3 _foregroundInitial;
        private bool _initialized;

        /// <summary>Configures the depth layer transforms.</summary>
        public void Configure(Transform sky, Transform mountains, Transform forest, Transform foreground)
        {
            _skyLayer = sky;
            _mountainLayer = mountains;
            _forestLayer = forest;
            _foregroundLayer = foreground;
            Initialize();
        }

        private void Start()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;
            _camera = Camera.main;
            if (_camera == null) return;

            _initialCameraPos = _camera.transform.position;
            if (_skyLayer != null) _skyInitial = _skyLayer.position;
            if (_mountainLayer != null) _mountainInitial = _mountainLayer.position;
            if (_forestLayer != null) _forestInitial = _forestLayer.position;
            if (_foregroundLayer != null) _foregroundInitial = _foregroundLayer.position;
            _initialized = true;
        }

        private void LateUpdate()
        {
            if (Time.timeScale <= 0f) return;
            if (!_initialized) Initialize();
            if (_camera == null) return;

            bool allowMotion = true;
            try
            {
                GameSettings settings = AetherSettings.Ensure().Values;
                if (settings.Accessibility != null && settings.Accessibility.ReducedMotion)
                    allowMotion = false;
                else if (settings.Graphics != null && settings.Graphics.Tier == GraphicsTier.Low)
                    allowMotion = false;
            }
            catch
            {
                allowMotion = true;
            }

            Vector3 delta = _camera.transform.position - _initialCameraPos;
            if (!allowMotion) delta = Vector3.zero;

            if (_skyLayer != null)
                _skyLayer.position = _skyInitial + new Vector3(delta.x * SkyFactor, delta.y * SkyFactor, 0f);

            if (_mountainLayer != null)
                _mountainLayer.position = _mountainInitial + new Vector3(delta.x * MountainFactor, delta.y * (MountainFactor * 0.5f), 0f);

            if (_forestLayer != null)
                _forestLayer.position = _forestInitial + new Vector3(delta.x * ForestFactor, delta.y * (ForestFactor * 0.5f), 0f);

            if (_foregroundLayer != null)
                _foregroundLayer.position = _foregroundInitial + new Vector3(delta.x * ForegroundFactor, delta.y * (ForegroundFactor * 0.5f), 0f);
        }
    }
}
