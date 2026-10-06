using Aether.Core.Settings;
using Aether.Gameplay.Storage;
using UnityEngine;

namespace Aether.Gameplay.Settings
{
    /// <summary>
    /// The one settings service this running game has, and the three settings that are applied by the
    /// engine rather than by a system of ours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Aether.Core</c> owns what a setting is and how it is stored; this is where the game
    /// decides that there is one of them, where the file lives, and which values Unity itself has to
    /// be told about. Anything that reads a setting reads it from here, so there is exactly one
    /// answer to "what is the music volume" in a running game.
    /// </para>
    /// <para>
    /// <b>Lazy, and safe to ask for at any time.</b> <see cref="Ensure"/> creates the service, reads
    /// the file once, and applies the engine-side values. A system that is built before the menu can
    /// simply ask; a test can call <see cref="ResetForTests"/> and get a clean slate without a file.
    /// </para>
    /// </remarks>
    public static class AetherSettings
    {
        private static SettingsService _current;

        /// <summary>The live service, created and loaded on the first ask.</summary>
        public static SettingsService Current => Ensure();

        /// <summary>True once the settings have been read. False means nothing has asked yet.</summary>
        public static bool IsLoaded => _current != null;

        /// <summary>Creates the service if it does not exist, loads it, and returns it.</summary>
        public static SettingsService Ensure()
        {
            if (_current != null) return _current;

            _current = new SettingsService { Store = new FileSettingsStore() };
            _current.Changed += OnChanged;
            _current.Load();
            ApplyEngineValues(_current.Values);
            return _current;
        }

        /// <summary>
        /// Writes any pending change. Called at the moments a write is cheap and meaningful: the end
        /// of a drag, the close of a screen, and before the app loses focus.
        /// </summary>
        public static void Flush()
        {
            if (_current != null) _current.Flush();
        }

        /// <summary>
        /// Applies the values Unity holds: the frame cap, the quality level and the screen timeout.
        /// </summary>
        /// <remarks>
        /// Idempotent, and called both on load and on every change, so there is no path that can
        /// leave one of the three out of step with the settings object.
        /// </remarks>
        public static void ApplyEngineValues(GameSettings values)
        {
            if (values == null) return;

            ApplyFrameRate(values.Graphics.FrameRateLimit);
            ApplyQualityTier(values.Graphics.Tier);
            ApplyScreenTimeout(values.Display.KeepScreenAwake);
        }

        /// <summary>
        /// Caps the frame rate.
        /// </summary>
        /// <remarks>
        /// vSync is turned off as part of setting a cap. On a device where vSync is on, the platform
        /// silently ignores <c>targetFrameRate</c>, and a settings row that does nothing is exactly
        /// the kind of thing this project refuses to ship: the two are set together, here, so that
        /// cannot happen. Zero means "no cap of ours" and leaves the platform's own default alone.
        /// </remarks>
        public static void ApplyFrameRate(int framesPerSecond)
        {
            if (framesPerSecond <= 0)
            {
                Application.targetFrameRate = -1;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = framesPerSecond;
        }

        /// <summary>
        /// Selects Unity's own quality level to match the player's tier.
        /// </summary>
        /// <remarks>
        /// The levels are found by name ("Low", "Medium", "High") rather than by index, because
        /// index order is a project setting someone will reorder. A project without a level of that
        /// name falls back to the nearest one it does have, so this cannot throw or select nothing.
        /// </remarks>
        public static void ApplyQualityTier(GraphicsTier tier)
        {
            string[] names = QualitySettings.names;
            if (names == null || names.Length == 0) return;

            string wanted = tier == GraphicsTier.Low ? "Low"
                          : tier == GraphicsTier.High ? "High"
                          : "Medium";

            int index = IndexOfLevel(names, wanted);
            if (index < 0)
            {
                // No level by that name: spread the three tiers across whatever exists.
                float fraction = tier == GraphicsTier.Low ? 0f : (tier == GraphicsTier.High ? 1f : 0.5f);
                index = Mathf.Clamp(Mathf.RoundToInt(fraction * (names.Length - 1)), 0, names.Length - 1);
            }

            if (QualitySettings.GetQualityLevel() != index) QualitySettings.SetQualityLevel(index, true);
        }

        /// <summary>Lets the screen sleep, or keeps it awake while the game is open.</summary>
        public static void ApplyScreenTimeout(bool keepAwake)
        {
            Screen.sleepTimeout = keepAwake ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
        }

        private static int IndexOfLevel(string[] names, string wanted)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], wanted, System.StringComparison.OrdinalIgnoreCase)) return i;
            }

            return -1;
        }

        private static void OnChanged(string id)
        {
            if (_current == null) return;
            ApplyEngineValues(_current.Values);
        }

        /// <summary>Forgets the service. Used by tests, which must not inherit a real file.</summary>
        public static void ResetForTests()
        {
            if (_current != null) _current.Changed -= OnChanged;
            _current = null;
        }
    }
}
