using System;
using Aether.Core.Settings;
using Aether.Gameplay.Settings;
using UnityEngine;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// The settings the menu itself obeys, read from the one settings service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every screen in this menu asks its questions here rather than reaching for the settings
    /// service directly, which means one file answers "what does the menu do differently for the
    /// player who changed something", and a new accessibility option is added in one place instead
    /// of in each screen that ought to respect it.
    /// </para>
    /// <para>
    /// The values are read on demand rather than cached: <see cref="AetherSettings"/> holds the
    /// loaded service, so a read is a field access and a few <c>if</c>s, and a cache would be one
    /// more thing to invalidate. The subscription helper is here for the same reason as the reads —
    /// one place to attach, one place to detach, and no screen holds a settings event it forgot to
    /// release.
    /// </para>
    /// </remarks>
    public static class MenuPreferences
    {
        /// <summary>Raise the interface's text contrast.</summary>
        public static bool HighContrast
        {
            get { return AetherSettings.Ensure().Values.Accessibility.HighContrast; }
        }

        /// <summary>The player asked for less movement.</summary>
        public static bool ReducedMotion
        {
            get { return AetherSettings.Ensure().Values.Accessibility.ReducedMotion; }
        }

        /// <summary>
        /// Whether the backdrop may drift. Two switches answer this: the graphics one, which is a
        /// taste question, and reduced motion, which is an accessibility one and wins.
        /// </summary>
        public static bool AtmosphereMotion
        {
            get
            {
                GameSettings values = AetherSettings.Ensure().Values;
                return values.Graphics.AtmosphereMotion && !values.Accessibility.ReducedMotion;
            }
        }

        /// <summary>The interface scale the player chose.</summary>
        public static float UiScale
        {
            get { return AetherSettings.Ensure().Values.Display.UiScale; }
        }

        /// <summary>Whether the interface keeps clear of the screen's cutouts.</summary>
        public static SafeAreaMode SafeArea
        {
            get { return AetherSettings.Ensure().Values.Display.SafeArea; }
        }

        /// <summary>The graphics tier, which decides how much of the backdrop is drawn.</summary>
        public static GraphicsTier Tier
        {
            get { return AetherSettings.Ensure().Values.Graphics.Tier; }
        }

        /// <summary>
        /// Attaches a handler to the settings service, creating it if this is the first ask.
        /// </summary>
        public static void Subscribe(Action<string> handler)
        {
            if (handler == null) return;
            AetherSettings.Ensure().Changed += handler;
        }

        /// <summary>Detaches a handler. Safe to call when nothing was attached.</summary>
        public static void Unsubscribe(Action<string> handler)
        {
            if (handler == null || !AetherSettings.IsLoaded) return;
            AetherSettings.Current.Changed -= handler;
        }

        /// <summary>
        /// The interface scale the canvas should use, pulled to the range the service allows.
        /// </summary>
        public static float ClampedUiScale
        {
            get { return Mathf.Clamp(UiScale, 0.8f, 1.4f); }
        }
    }
}
