using Aether.Core.Settings;
using Aether.Gameplay.Settings;
using UnityEngine;

namespace Aether.Gameplay.Support
{
    /// <summary>
    /// The device's vibration motor, on a leash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity's only built-in vibration is <c>Handheld.Vibrate</c>, which on Android buzzes for a
    /// fixed, fairly long time and cannot be tuned. That is a poor building block for feedback under
    /// a thumb, so this wrapper does the two things that make it usable: it honours the player's
    /// setting, and it <b>rate-limits</b>, so a flurry of interface taps cannot turn into one long
    /// buzz. A buzz that keeps going after the player stopped touching the screen reads as a
    /// malfunction, which is worse than no haptics at all.
    /// </para>
    /// <para>
    /// It is used for confirmations and for taking a hit — the two moments where a player's eyes are
    /// on something else. It is deliberately not used for navigation.
    /// </para>
    /// </remarks>
    public static class Haptics
    {
        /// <summary>Shortest gap between two buzzes, in seconds.</summary>
        public const float MinimumInterval = 0.12f;

        private static float _lastAt = -999f;

        /// <summary>Whether the player has haptics on. Read from settings, never cached.</summary>
        public static bool Enabled => AetherSettings.Ensure().Values.Controls.Haptics;

        /// <summary>True when a buzz would actually be felt right now.</summary>
        public static bool Available => Enabled && Application.isMobilePlatform;

        /// <summary>
        /// Buzzes once, if haptics are on, the platform has a motor, and the last buzz was long
        /// enough ago.
        /// </summary>
        public static bool Tap()
        {
            if (!Available) return false;

            float now = Time.unscaledTime;
            if (now - _lastAt < MinimumInterval) return false;

            _lastAt = now;
            Handheld.Vibrate();
            return true;
        }

        /// <summary>Forgets the rate limit. Used by tests so one case cannot silence the next.</summary>
        public static void ResetForTests()
        {
            _lastAt = -999f;
        }

        /// <summary>The bus this feedback belongs to, so a future implementation can mix it.</summary>
        public static AudioBusId Bus => AudioBusId.Ui;
    }
}
