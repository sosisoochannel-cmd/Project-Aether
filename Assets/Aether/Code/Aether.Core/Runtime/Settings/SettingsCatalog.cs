using System;
using System.Collections.Generic;

namespace Aether.Core.Settings
{
    /// <summary>The screen a setting lives on.</summary>
    /// <remarks>
    /// The order of these members is the order of the settings rail, so the most-used categories
    /// are the ones a thumb reaches first. It is also the order the gate checks, which is what
    /// keeps the rail from quietly losing a category.
    /// </remarks>
    public enum SettingCategory
    {
        Gameplay = 0,
        Controls = 1,
        Camera = 2,
        Graphics = 3,
        Audio = 4,
        Display = 5,
        Accessibility = 6,
        Language = 7,
        Data = 8,
        About = 9,
    }

    /// <summary>What kind of control a setting is drawn as.</summary>
    public enum SettingKind
    {
        /// <summary>On or off. The value is 0 or 1.</summary>
        Toggle = 0,

        /// <summary>A continuous range, dragged.</summary>
        Slider = 1,

        /// <summary>One of a fixed list of named values.</summary>
        Choice = 2,
    }

    /// <summary>
    /// One setting: what it is called, where it lives, what it may hold, and what reads it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A definition carries three things that must agree: the <b>range</b> the interface is allowed
    /// to offer, the <b>field</b> the value is stored in, and the <b>destination</b> — the code that
    /// actually reads it. The third is written down as documentation and checked by
    /// <c>tools/verify/mainmenu.py</c>, because a settings screen full of sliders that nothing reads
    /// is the most convincing kind of fake feature there is: it looks finished, it persists, and it
    /// does nothing.
    /// </para>
    /// <para>
    /// Labels are keys rather than text, so the catalog is the same list in every language.
    /// </para>
    /// </remarks>
    public sealed class SettingDefinition
    {
        /// <summary>Stable id, also the key the value is filed under in a saved file.</summary>
        public readonly string Id;

        public readonly SettingCategory Category;
        public readonly SettingKind Kind;

        /// <summary>Localisation key for the row's label.</summary>
        public readonly string LabelKey;

        /// <summary>Localisation key for the one-line explanation under the label, or null.</summary>
        public readonly string HelpKey;

        public readonly float Minimum;
        public readonly float Maximum;

        /// <summary>Slider step. 0 means continuous.</summary>
        public readonly float Step;

        /// <summary>Localisation keys for a <see cref="SettingKind.Choice"/> row, in value order.</summary>
        public readonly string[] OptionKeys;

        /// <summary>The values those options stand for, in the same order.</summary>
        public readonly float[] OptionValues;

        /// <summary>Where the value ends up: the code that reads it, named for a human and the gate.</summary>
        public readonly string Destination;

        public SettingDefinition(string id, SettingCategory category, SettingKind kind, string labelKey,
                                 string helpKey, float minimum, float maximum, float step,
                                 string[] optionKeys, float[] optionValues, string destination)
        {
            Id = id;
            Category = category;
            Kind = kind;
            LabelKey = labelKey;
            HelpKey = helpKey;
            Minimum = minimum;
            Maximum = maximum;
            Step = step;
            OptionKeys = optionKeys;
            OptionValues = optionValues;
            Destination = destination;
        }

        /// <summary>True when a value is outside what this setting may hold.</summary>
        public bool OutOfRange(float value)
        {
            if (Kind == SettingKind.Toggle) return value != 0f && value != 1f;
            return value < Minimum - 0.0001f || value > Maximum + 0.0001f;
        }

        /// <summary>The value pulled into range and onto its step.</summary>
        public float Sanitise(float value)
        {
            if (Kind == SettingKind.Toggle) return value >= 0.5f ? 1f : 0f;

            float clamped = value < Minimum ? Minimum : (value > Maximum ? Maximum : value);
            if (Step > 0f)
            {
                float steps = (float)Math.Round((clamped - Minimum) / Step);
                clamped = Minimum + (steps * Step);
            }

            bool known = false;
            if (OptionValues != null && OptionValues.Length > 0)
            {
                for (int i = 0; i < OptionValues.Length; i++)
                {
                    if (Math.Abs(OptionValues[i] - clamped) < 0.0001f)
                    {
                        clamped = OptionValues[i];
                        known = true;
                        break;
                    }
                }
            }

            // A choice that is not one of its options is a corrupt file, not a preference: snap it
            // to the first option rather than leaving the row showing nothing selected.
            if (Kind == SettingKind.Choice && !known && OptionValues != null && OptionValues.Length > 0)
                clamped = OptionValues[0];

            return clamped;
        }

        /// <summary>How this row reads in a log or a test failure.</summary>
        public override string ToString()
        {
            return $"{Id} [{Category}/{Kind}] -> {Destination}";
        }
    }

    /// <summary>
    /// Every setting in the game, and the two directions of getting at its value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The binding is a pair of switches rather than a pair of delegates on purpose. A delegate
    /// table reads well but hides the field name from anything that greps the source, and the whole
    /// point of <see cref="SettingDefinition.Destination"/> is that a tool can check the claim. A
    /// switch statement is also the fastest thing the compiler can build for this, and it fails
    /// loudly at compile time when a field is renamed.
    /// </para>
    /// <para>
    /// Adding a setting means adding it in three places — the field, the definition, and the two
    /// switches — and the gate fails the build if only two of them happened.
    /// </para>
    /// </remarks>
    public static class SettingsCatalog
    {
        /// <summary>Every setting, in the order the panels show them.</summary>
        public static readonly SettingDefinition[] All =
        {
            // -- Gameplay. Two rows, and the honesty is in what is not here: there is one region,
            //    it has one tuned difficulty, and no assistance system exists to configure.
            new SettingDefinition("gameplay.autosave", SettingCategory.Gameplay, SettingKind.Toggle,
                "setting.autosave", "setting.autosave.help", 0f, 1f, 0f, null, null,
                "GameSessionHost -> FileSaveStore.AutomaticWrites"),

            // -- Controls.
            new SettingDefinition("controls.deadZone", SettingCategory.Controls, SettingKind.Slider,
                "setting.deadZone", "setting.deadZone.help", 0f, 0.45f, 0f, null, null,
                "TouchControlsView.Layout.StickDeadZone"),

            new SettingDefinition("controls.buttonSize", SettingCategory.Controls, SettingKind.Slider,
                "setting.buttonSize", "setting.buttonSize.help", 0.85f, 1.15f, 0.05f, null, null,
                "TouchControlsView.Layout.RadiusScale"),

            new SettingDefinition("controls.buttonOpacity", SettingCategory.Controls, SettingKind.Slider,
                "setting.buttonOpacity", "setting.buttonOpacity.help", 0.3f, 1f, 0.05f, null, null,
                "TouchControlsView.Layout.OpacityScale"),

            new SettingDefinition("controls.leftHanded", SettingCategory.Controls, SettingKind.Toggle,
                "setting.leftHanded", "setting.leftHanded.help", 0f, 1f, 0f, null, null,
                "TouchControlsView.Layout.Mirrored"),

            new SettingDefinition("controls.haptics", SettingCategory.Controls, SettingKind.Toggle,
                "setting.haptics", "setting.haptics.help", 0f, 1f, 0f, null, null,
                "Haptics.Enabled -> Handheld.Vibrate"),

            // -- Camera.
            new SettingDefinition("camera.smoothing", SettingCategory.Camera, SettingKind.Slider,
                "setting.cameraSmoothing", "setting.cameraSmoothing.help", 0.04f, 0.6f, 0.02f, null, null,
                "CameraFollow2D._smoothTime -> ApplySettings"),

            new SettingDefinition("camera.lookAhead", SettingCategory.Camera, SettingKind.Slider,
                "setting.cameraLookAhead", "setting.cameraLookAhead.help", 0f, 3.5f, 0.25f, null, null,
                "CameraFollow2D._lookAhead.x -> ApplySettings"),

            // -- Graphics.
            new SettingDefinition("graphics.tier", SettingCategory.Graphics, SettingKind.Choice,
                "setting.graphicsTier", "setting.graphicsTier.help", 0f, 2f, 1f,
                new[] { "setting.tier.low", "setting.tier.medium", "setting.tier.high" },
                new[] { 0f, 1f, 2f },
                "MenuBackdrop detail + QualitySettings.SetQualityLevel"),

            new SettingDefinition("graphics.frameRate", SettingCategory.Graphics, SettingKind.Choice,
                "setting.frameRate", "setting.frameRate.help", 30f, 120f, 0f,
                new[] { "setting.fps.30", "setting.fps.60", "setting.fps.max" },
                new[] { 30f, 60f, 120f },
                "Application.targetFrameRate + QualitySettings.vSyncCount"),

            new SettingDefinition("graphics.atmosphere", SettingCategory.Graphics, SettingKind.Toggle,
                "setting.atmosphere", "setting.atmosphere.help", 0f, 1f, 0f, null, null,
                "MenuBackdrop.AtmosphereMotion"),

            // -- Audio, one row per bus.
            new SettingDefinition("audio.master", SettingCategory.Audio, SettingKind.Slider,
                "setting.audio.master", null, 0f, 1f, 0.05f, null, null,
                "AudioBuses.Gain(Master) -> every AudioSource"),

            new SettingDefinition("audio.music", SettingCategory.Audio, SettingKind.Slider,
                "setting.audio.music", null, 0f, 1f, 0.05f, null, null,
                "AudioBuses.Gain(Music) -> MenuAudio._music"),

            new SettingDefinition("audio.sfx", SettingCategory.Audio, SettingKind.Slider,
                "setting.audio.sfx", null, 0f, 1f, 0.05f, null, null,
                "AudioBuses.Gain(Sfx) -> SoundDirector playback"),

            new SettingDefinition("audio.ui", SettingCategory.Audio, SettingKind.Slider,
                "setting.audio.ui", null, 0f, 1f, 0.05f, null, null,
                "AudioBuses.Gain(Ui) -> MenuAudio one-shots"),

            new SettingDefinition("audio.voice", SettingCategory.Audio, SettingKind.Slider,
                "setting.audio.voice", null, 0f, 1f, 0.05f, null, null,
                "AudioBuses.Gain(Voice) -> SoundDirector playback"),

            new SettingDefinition("audio.ambience", SettingCategory.Audio, SettingKind.Slider,
                "setting.audio.ambience", null, 0f, 1f, 0.05f, null, null,
                "AudioBuses.Gain(Ambience) -> MenuAudio._ambience"),

            // -- Display.
            new SettingDefinition("display.uiScale", SettingCategory.Display, SettingKind.Slider,
                "setting.uiScale", "setting.uiScale.help", 0.8f, 1.4f, 0.05f, null, null,
                "MenuCanvas.Scale -> CanvasScaler.scaleFactor"),

            new SettingDefinition("display.safeArea", SettingCategory.Display, SettingKind.Choice,
                "setting.safeArea", "setting.safeArea.help", 0f, 1f, 1f,
                new[] { "setting.safeArea.full", "setting.safeArea.respect" },
                new[] { 0f, 1f },
                "SafeAreaFitter.Mode"),

            new SettingDefinition("display.keepAwake", SettingCategory.Display, SettingKind.Toggle,
                "setting.keepAwake", "setting.keepAwake.help", 0f, 1f, 0f, null, null,
                "Screen.sleepTimeout"),

            // -- Accessibility.
            new SettingDefinition("accessibility.reducedMotion", SettingCategory.Accessibility,
                SettingKind.Toggle, "setting.reducedMotion", "setting.reducedMotion.help", 0f, 1f, 0f,
                null, null, "MenuBackdrop.MotionEnabled + MenuTransition durations"),

            new SettingDefinition("accessibility.highContrast", SettingCategory.Accessibility,
                SettingKind.Toggle, "setting.highContrast", "setting.highContrast.help", 0f, 1f, 0f,
                null, null, "MenuTheme.Palette contrast"),

            // -- Language. The options are the languages this build can actually draw; the ones it
            //    cannot are named on the screen by a note row rather than offered and then failed.
            new SettingDefinition("language.primary", SettingCategory.Language, SettingKind.Choice,
                "setting.language", "setting.language.help", LanguageOptionMinimum(),
                LanguageOptionMaximum(), 1f, LanguageOptionKeys(), LanguageOptionValues(),
                "LanguageService.Set -> GameSettings.Language -> Aether/settings.json"),

        };

        private static float LanguageOptionMinimum()
        {
            float[] values = LanguageOptionValues();
            return values.Length == 0 ? 0f : values[0];
        }

        private static float LanguageOptionMaximum()
        {
            float[] values = LanguageOptionValues();
            return values.Length == 0 ? 0f : values[values.Length - 1];
        }

        /// <summary>
        /// Localisation keys for the language row's options, in catalogue order.
        /// </summary>
        /// <remarks>
        /// Built from the language catalogue rather than written out, so a language that gains a font
        /// and a table appears in the row without anybody remembering to edit a second list — and,
        /// more to the point, a language that <i>cannot</i> be drawn cannot appear in it by accident.
        /// </remarks>
        public static string[] LanguageOptionKeys()
        {
            var keys = new List<string>();
            for (int i = 0; i < Aether.Core.Localization.LanguageCatalog.All.Length; i++)
            {
                Aether.Core.Localization.LanguageDefinition language =
                    Aether.Core.Localization.LanguageCatalog.All[i];

                // Offered means drawable *and* written: a language with no sheet of its own is not in
                // the row, because choosing it would do nothing. The catalogue holds that list, and
                // tools/verify/localization.py holds the catalogue to the sheets.
                if (language.FontResolverConfigured &&
                    Aether.Core.Localization.LanguageCatalog.IsOffered(language.Code))
                {
                    keys.Add(language.LabelKey);
                }
            }

            return keys.ToArray();
        }

        /// <summary>
        /// The values those options stand for: the language's index in the catalogue.
        /// </summary>
        /// <remarks>
        /// An index rather than a code because a setting's value is a float and every other row on
        /// the screen is a number. The index is looked up by code on read, so a catalogue that is
        /// reordered between versions still resolves to the language the player chose — a stored
        /// index is only ever a position, never an identity.
        /// </remarks>
        public static float[] LanguageOptionValues()
        {
            var values = new List<float>();
            for (int i = 0; i < Aether.Core.Localization.LanguageCatalog.All.Length; i++)
            {
                Aether.Core.Localization.LanguageDefinition option =
                    Aether.Core.Localization.LanguageCatalog.All[i];
                if (option.FontResolverConfigured &&
                    Aether.Core.Localization.LanguageCatalog.IsOffered(option.Code))
                {
                    values.Add(i);
                }
            }

            return values.ToArray();
        }

        /// <summary>Every setting on one screen, in catalog order.</summary>
        public static List<SettingDefinition> InCategory(SettingCategory category)
        {
            var found = new List<SettingDefinition>();
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Category == category) found.Add(All[i]);
            }

            return found;
        }

        /// <summary>The definition with this id, or null.</summary>
        public static SettingDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id) return All[i];
            }

            return null;
        }

        /// <summary>The value this setting currently holds.</summary>
        /// <remarks>
        /// One id, one field. A setting that is reachable from two screens is one row in one place
        /// with a link from the other (<c>SettingKind</c> has no such row today), never two rows
        /// writing the same field and drifting apart the first time one of them is refreshed.
        /// </remarks>
        public static float Read(GameSettings settings, string id)
        {
            if (settings == null) return 0f;

            switch (id)
            {
                case "gameplay.autosave":
                    return settings.Gameplay.Autosave ? 1f : 0f;
                case "controls.deadZone":
                    return settings.Controls.StickDeadZone;
                case "controls.buttonSize":
                    return settings.Controls.ButtonSize;
                case "controls.buttonOpacity":
                    return settings.Controls.ButtonOpacity;
                case "controls.leftHanded":
                    return settings.Controls.LeftHanded ? 1f : 0f;
                case "controls.haptics":
                    return settings.Controls.Haptics ? 1f : 0f;
                case "camera.smoothing":
                    return settings.Camera.FollowSmoothing;
                case "camera.lookAhead":
                    return settings.Camera.LookAhead;
                case "graphics.tier":
                    return (float)settings.Graphics.Tier;
                case "graphics.frameRate":
                    return settings.Graphics.FrameRateLimit;
                case "graphics.atmosphere":
                    return settings.Graphics.AtmosphereMotion ? 1f : 0f;
                case "audio.master":
                    return settings.Audio.Master;
                case "audio.music":
                    return settings.Audio.Music;
                case "audio.sfx":
                    return settings.Audio.Sfx;
                case "audio.ui":
                    return settings.Audio.Ui;
                case "audio.voice":
                    return settings.Audio.Voice;
                case "audio.ambience":
                    return settings.Audio.Ambience;
                case "display.uiScale":
                    return settings.Display.UiScale;
                case "display.safeArea":
                    return (float)settings.Display.SafeArea;
                case "display.keepAwake":
                    return settings.Display.KeepScreenAwake ? 1f : 0f;
                case "accessibility.reducedMotion":
                    return settings.Accessibility.ReducedMotion ? 1f : 0f;
                case "accessibility.highContrast":
                    return settings.Accessibility.HighContrast ? 1f : 0f;
                case "language.primary":
                    return Aether.Core.Localization.LanguageCatalog.IndexOf(settings.Language);
                default:
                    return 0f;
            }
        }

        /// <summary>Stores a value, sanitised for the setting it belongs to.</summary>
        public static void Write(GameSettings settings, string id, float value)
        {
            if (settings == null) return;

            SettingDefinition definition = Find(id);
            float clean = definition != null ? definition.Sanitise(value) : value;

            switch (id)
            {
                case "gameplay.autosave":
                    settings.Gameplay.Autosave = clean >= 0.5f;
                    break;
                case "controls.deadZone":
                    settings.Controls.StickDeadZone = clean;
                    break;
                case "controls.buttonSize":
                    settings.Controls.ButtonSize = clean;
                    break;
                case "controls.buttonOpacity":
                    settings.Controls.ButtonOpacity = clean;
                    break;
                case "controls.leftHanded":
                    settings.Controls.LeftHanded = clean >= 0.5f;
                    break;
                case "controls.haptics":
                    settings.Controls.Haptics = clean >= 0.5f;
                    break;
                case "camera.smoothing":
                    settings.Camera.FollowSmoothing = clean;
                    break;
                case "camera.lookAhead":
                    settings.Camera.LookAhead = clean;
                    break;
                case "graphics.tier":
                    settings.Graphics.Tier = (GraphicsTier)(int)clean;
                    break;
                case "graphics.frameRate":
                    settings.Graphics.FrameRateLimit = (int)clean;
                    break;
                case "graphics.atmosphere":
                    settings.Graphics.AtmosphereMotion = clean >= 0.5f;
                    break;
                case "audio.master":
                    settings.Audio.Master = clean;
                    break;
                case "audio.music":
                    settings.Audio.Music = clean;
                    break;
                case "audio.sfx":
                    settings.Audio.Sfx = clean;
                    break;
                case "audio.ui":
                    settings.Audio.Ui = clean;
                    break;
                case "audio.voice":
                    settings.Audio.Voice = clean;
                    break;
                case "audio.ambience":
                    settings.Audio.Ambience = clean;
                    break;
                case "display.uiScale":
                    settings.Display.UiScale = clean;
                    break;
                case "display.safeArea":
                    settings.Display.SafeArea = (SafeAreaMode)(int)clean;
                    break;
                case "display.keepAwake":
                    settings.Display.KeepScreenAwake = clean >= 0.5f;
                    break;
                case "accessibility.reducedMotion":
                    settings.Accessibility.ReducedMotion = clean >= 0.5f;
                    break;
                case "accessibility.highContrast":
                    settings.Accessibility.HighContrast = clean >= 0.5f;
                    break;
                case "language.primary":
                    // The stored value is a position in the catalogue; the code is what is filed.
                    settings.Language = Aether.Core.Localization.LanguageCatalog.CodeAt((int)Math.Round(clean));
                    break;
                default:
                    break;
            }
        }

        /// <summary>The category's own bounds, for a row that has none of its own.</summary>
        public static float DefaultOf(string id)
        {
            var fresh = new GameSettings();
            fresh.Reset();
            return Read(fresh, id);
        }
    }
}
