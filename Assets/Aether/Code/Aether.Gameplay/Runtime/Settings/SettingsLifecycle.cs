using UnityEngine;

namespace Aether.Gameplay.Settings
{
    /// <summary>Flushes pending settings when the mobile app is suspended or the process exits.</summary>
    [DisallowMultipleComponent]
    public sealed class SettingsLifecycle : MonoBehaviour
    {
        private static SettingsLifecycle _instance;

        /// <summary>Creates one process-wide lifecycle hook while playing.</summary>
        public static void Ensure()
        {
            if (!Application.isPlaying || _instance != null) return;

            var host = new GameObject("Aether Settings Lifecycle");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<SettingsLifecycle>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) Flush();
        }

        private void OnApplicationQuit()
        {
            Flush();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private static void Flush()
        {
            if (!AetherSettings.Flush())
                Debug.LogError("[settings] Pending settings could not be saved before suspension or exit.");
        }
    }
}
