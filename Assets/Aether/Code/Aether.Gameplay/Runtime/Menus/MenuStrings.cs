using System.Collections.Generic;
using System.Globalization;
using Aether.Gameplay.Settings;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// The interface's text, looked up by key, ready for a second language.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is not a translation system and does not pretend to be one.</b> There is one language
    /// in the project, English, and shipping invented translations nobody can review would be worse
    /// than shipping none. What exists is the part that is expensive to add later: no screen ever
    /// holds a sentence, every label is a key, missing keys are visible rather than silent, and the
    /// selected language is stored in the settings like any other preference. Adding a language is a
    /// table and one line in the catalog.
    /// </para>
    /// <para>
    /// A missing key returns the key itself. That is deliberate: a screen that shows
    /// <c>setting.haptics</c> is obviously broken to anyone looking at it, which is how it gets
    /// fixed, where a fallback to English would hide the omission until a player reported it.
    /// </para>
    /// </remarks>
    public static class MenuStrings
    {
        /// <summary>Languages the interface can be shown in, by ISO code.</summary>
        public static readonly string[] Languages = { "en" };

        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            // -- the menu itself
            { "menu.title", "PROJECT AETHER" },
            { "menu.subtitle", "THE GREENWAY — REGION ONE" },
            { "menu.playSection", "PLAY" },
            { "menu.exploreSection", "EXPLORE" },
            { "menu.systemSection", "SYSTEM" },
            { "menu.continue", "CONTINUE" },
            { "menu.newGame", "NEW GAME" },
            { "menu.chapters", "CHAPTERS" },
            { "menu.characters", "CHARACTERS" },
            { "menu.collection", "COLLECTION" },
            { "menu.achievements", "ACHIEVEMENTS" },
            { "menu.settings", "SETTINGS" },
            { "menu.credits", "CREDITS" },
            { "menu.quit", "QUIT" },
            { "menu.continue.meta", "RESUME THE GREENWAY" },
            { "menu.newGame.meta", "BEGIN AT THE START" },
            { "menu.continue.none", "NO RUN YET" },
            { "menu.newGame.confirm", "Starting again deletes the stored run. Abilities and findings are lost, and this cannot be undone." },
            { "menu.quit.confirm", "The game closes. Anything the run has not written down is lost." },

            // -- the screens around it
            { "common.back", "BACK" },
            { "common.on", "ON" },
            { "common.off", "OFF" },
            { "common.locked", "NOT YET IN THE BUILD" },
            { "common.reset", "RESTORE DEFAULTS" },
            { "common.confirm", "CONFIRM" },
            { "common.cancel", "CANCEL" },
            { "settings.title", "SETTINGS" },
            { "credits.title", "CREDITS" },
            { "chapters.title", "CHAPTERS" },
            { "confirm.title", "ARE YOU SURE" },
            { "locked.title", "NOT YET" },

            // -- settings categories
            { "category.gameplay", "GAMEPLAY" },
            { "category.controls", "CONTROLS" },
            { "category.camera", "CAMERA" },
            { "category.graphics", "GRAPHICS" },
            { "category.audio", "AUDIO" },
            { "category.display", "DISPLAY" },
            { "category.accessibility", "ACCESSIBILITY" },
            { "category.language", "LANGUAGE" },
            { "category.data", "DATA / SAVE" },
            { "category.about", "ABOUT" },

            // -- settings rows
            { "setting.autosave", "AUTOSAVE" },
            { "setting.autosave.help", "Record progress as it happens" },
            { "setting.deadZone", "STICK DEAD ZONE" },
            { "setting.deadZone.help", "How far the stick moves before it registers" },
            { "setting.buttonSize", "BUTTON SIZE" },
            { "setting.buttonSize.help", "Size of the on-screen controls" },
            { "setting.buttonOpacity", "BUTTON OPACITY" },
            { "setting.buttonOpacity.help", "How solid the on-screen controls are" },
            { "setting.leftHanded", "LEFT-HANDED LAYOUT" },
            { "setting.leftHanded.help", "Movement under the right thumb, actions under the left" },
            { "setting.haptics", "VIBRATION" },
            { "setting.haptics.help", "A short buzz on hits and confirmations" },
            { "setting.cameraSmoothing", "CAMERA SMOOTHING" },
            { "setting.cameraSmoothing.help", "How softly the camera follows; lower is tighter" },
            { "setting.cameraLookAhead", "CAMERA LOOK AHEAD" },
            { "setting.cameraLookAhead.help", "How far ahead of the player the camera leads" },
            { "setting.graphicsTier", "QUALITY" },
            { "setting.graphicsTier.help", "How much detail the game draws" },
            { "setting.tier.low", "LOW" },
            { "setting.tier.medium", "MEDIUM" },
            { "setting.tier.high", "HIGH" },
            { "setting.frameRate", "FRAME RATE" },
            { "setting.frameRate.help", "A lower cap is easier on the battery; MAX means the game asks for as much as the display will give" },
            { "setting.fps.30", "30" },
            { "setting.fps.60", "60" },
            { "setting.fps.max", "MAX" },
            { "setting.atmosphere", "ATMOSPHERE" },
            { "setting.atmosphere.help", "Let the menu's background drift" },
            { "setting.audio.master", "MASTER" },
            { "setting.audio.music", "MUSIC" },
            { "setting.audio.sfx", "SFX" },
            { "setting.audio.ui", "INTERFACE" },
            { "setting.audio.voice", "VOICE" },
            { "setting.audio.ambience", "AMBIENCE" },
            { "setting.uiScale", "INTERFACE SIZE" },
            { "setting.uiScale.help", "Scale of every menu on screen" },
            { "setting.safeArea", "SAFE AREA" },
            { "setting.safeArea.help", "Keep the interface clear of notches and gesture bars" },
            { "setting.safeArea.full", "FULL SCREEN" },
            { "setting.safeArea.respect", "RESPECT CUTOUTS" },
            { "setting.keepAwake", "KEEP SCREEN AWAKE" },
            { "setting.keepAwake.help", "Stop the screen sleeping while the game is open" },
            { "setting.reducedMotion", "REDUCED MOTION" },
            { "setting.reducedMotion.help", "Stop the menu drifting, and shorten transitions" },
            { "setting.highContrast", "HIGH CONTRAST" },
            { "setting.highContrast.help", "Brighten the interface's text" },
            { "setting.language", "LANGUAGE" },
            { "setting.language.help", "Language of the interface" },
            { "language.en", "ENGLISH" },

            // -- what a category has to say for itself
            { "note.gameplay", "Difficulty and assistance are not in the build. The region is tuned as one experience." },
            { "note.controls", "Looking and aiming belong to the camera, not a stick: there is no aim control to invert." },
            { "note.camera", "Camera shake is not in the build; nothing shakes the camera yet." },
            { "note.graphics", "Resolution scale, texture quality and anti-aliasing are still the project's own settings." },
            { "note.audio", "No spoken dialogue is in the build yet. The bus is ready for it." },
            { "note.display", "Brightness arrives with the post-processing pass, which the game does not run yet." },
            { "note.accessibility", "Subtitles need dialogue, and reduced screen shake needs shake. Neither exists yet." },
            { "note.language", "One language ships today. Every label in the menu is already a key, so a second is a table and a line." },
            { "note.data", "Progress is stored on this device, under the app's own data folder." },
            { "note.about", "Legal text follows the studio's brand guidelines and is not authored yet." },

            // -- data and save
            { "data.info.none", "NO RUN STORED" },
            { "data.info.run", "STORED RUN — {0} ABILITY, {1} FINDINGS" },
            { "data.info.checkpoint", "STANDS AT A CHECKPOINT" },
            { "data.info.start", "STANDS AT THE REGION START" },
            { "data.saveNow", "SAVE NOW" },
            { "data.reset", "DELETE STORED RUN" },
            { "data.reset.title", "DELETE THE STORED RUN" },
            { "data.reset.body", "The stored run is deleted. Abilities and findings are lost, and this cannot be undone." },

            // -- about and credits
            { "about.version", "VERSION {0}" },
            { "about.engine", "UNITY {0}" },
            { "about.platform", "PLATFORM {0}" },
            { "about.credits", "CREDITS" },
            { "about.storage", "SAVE DATA" },
            { "credits.art", "ARTWORK — VARELLON STUDIOS" },
            { "credits.art.body", "The studio mark, and everything the menu is built from, is the approved artwork." },
            { "credits.code", "CODE — PROJECT AETHER" },
            { "credits.code.body", "Written for Android, landscape, first on a phone." },
            { "credits.engine", "MADE WITH UNITY" },
            { "credits.music", "MUSIC" },
            { "credits.music.body", "Not scored yet. The menu is silent until the approved track is dropped in." },

            // -- chapters, and the screens that are not built
            { "chapters.region", "REGION ONE" },
            { "chapters.greenway", "THE GREENWAY" },
            { "chapters.greenway.meta", "PLAY" },
            { "chapters.locked", "MORE REGIONS ARE NOT IN THE BUILD" },
            { "locked.characters.body", "The cast is not in the build yet. When it is, this screen lists who the player has met." },
            { "locked.collection.body", "Nothing to collect yet. This screen will list what has been found in the world." },
            { "locked.achievements.body", "No achievements are defined yet. This screen will list them as they are earned." },
        };

        /// <summary>The language code the interface is drawn in.</summary>
        public static string Language
        {
            get
            {
                string code = AetherSettings.Ensure().Values.Language;
                return string.IsNullOrEmpty(code) ? "en" : code;
            }
        }

        /// <summary>The text for a key, or the key itself when there is none.</summary>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return English.TryGetValue(key, out string text) ? text : key;
        }

        /// <summary>The text for a key with numbered placeholders filled in.</summary>
        public static string Format(string key, params object[] arguments)
        {
            string text = Get(key);
            if (arguments == null || arguments.Length == 0) return text;

            try
            {
                return string.Format(CultureInfo.InvariantCulture, text, arguments);
            }
            catch (System.FormatException)
            {
                // A key whose text has the wrong number of placeholders is a content bug, and a menu
                // that throws on it would be a worse one. The raw text is shown instead.
                return text;
            }
        }

        /// <summary>True when the table holds this key. Used by the gate and by tests.</summary>
        public static bool Has(string key)
        {
            return !string.IsNullOrEmpty(key) && English.ContainsKey(key);
        }

        /// <summary>Every key in the shipped table, for the gate.</summary>
        public static IEnumerable<string> Keys => English.Keys;
    }
}
