using System.Collections.Generic;
using Aether.Gameplay.Localization;

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
        /// <summary>
        /// Languages the interface knows about, by ISO code.
        /// </summary>
        /// <remarks>
        /// Read from the catalogue rather than written here: the code list is what the settings file
        /// stores, so it belongs with the settings layer, and the translations belong in
        /// <c>Localization/</c>. This property remains because the settings screen and the gate both
        /// ask for "the languages" and neither should have to know where the list is kept.
        /// </remarks>
        public static IReadOnlyList<Aether.Core.Localization.LanguageDefinition> Languages
        {
            get { return Aether.Core.Localization.LanguageCatalog.All; }
        }

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
            { "note.language", "What the interface is written in. A language is offered once its table is complete; the languages that are named but not offered say why on the row." },
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
            { "locked.characters.body", "The screen that lists the cast is not in this build yet. Who the player has met is recorded in the save, by the same layer the collection reads." },
            { "locked.collection.body", "The screen that lists findings is not in this build yet. Each finding is recorded as a world flag the moment it is reached." },
            { "locked.achievements.body", "The screen that lists achievements is not in this build yet. The achievements themselves are defined, and are awarded as they are earned." },

            // -- the chapter list
            { "chapters.body", "One region is playable in this build. The rest are named so the road ahead is honest." },
            { "chapters.play", "PLAY THIS CHAPTER" },
            { "chapters.resume", "RESUME THIS CHAPTER" },
            { "chapters.completed", "COMPLETED" },
            { "chapters.current", "IN PROGRESS" },
            { "chapters.notInBuild", "NOT IN THIS BUILD" },
            { "chapters.notInBuild.help", "This region is not part of the current build. Its place in the story is written; its level is not." },
            { "chapters.playable.help", "Pick this chapter to start or resume a run in it." },
            { "chapter.greenway.title", "THE GREENWAY" },
            { "chapter.greenway.subtitle", "REGION ONE" },
            { "chapter.greenway.body", "A cut trail through old canopy. Somebody came through here before you, and the region has been holding its breath since." },
            { "chapter.hollow.title", "THE HOLLOW" },
            { "chapter.hollow.subtitle", "REGION TWO" },
            { "chapter.hollow.body", "Beneath the greenway, a worked-out seam that remembers who filled it in." },
            { "chapter.saltmarsh.title", "SALTMARSH" },
            { "chapter.saltmarsh.subtitle", "REGION THREE" },
            { "chapter.saltmarsh.body", "Tidal flats where the water has been let back in, and something has learned to use the channels." },
            { "chapter.spine.title", "THE SPINE" },
            { "chapter.spine.subtitle", "REGION FOUR" },
            { "chapter.spine.body", "The ridge above everything, and the reason the greenway had to be cut where it was." },

            // -- save slots
            { "slots.title", "SAVE SLOTS" },
            { "slots.body", "Three runs, kept apart. Continuing loads the most recent one that can be played." },
            { "slots.slot", "SLOT {0}" },
            { "slots.empty", "EMPTY" },
            { "slots.empty.help", "Nothing has been stored here yet." },
            { "slots.newHere", "START HERE" },
            { "slots.play", "PLAY" },
            { "slots.delete", "DELETE" },
            { "slots.damaged", "DAMAGED SAVE" },
            { "slots.damaged.help", "This file could not be read. It is never loaded and never overwritten by accident; delete it to free the slot." },
            { "slots.lastPlayed", "LAST PLAYED {0}" },
            { "slots.never", "NEVER PLAYED" },
            { "slots.played", "{0} PLAYED" },
            { "slots.findings", "{0} FINDING(S)" },
            { "slots.achievements", "{0} OF {1} ACHIEVEMENTS" },
            { "slots.deaths", "{0} DEATH(S)" },
            { "slots.new.title", "START A NEW RUN?" },
            { "slots.new.body", "Slot {0} already holds a run. Starting here erases it, along with everything it found." },
            { "slots.delete.title", "DELETE THIS RUN?" },
            { "slots.delete.body", "The run in slot {0} is deleted. Everything it found is lost, and this cannot be undone." },
            { "slots.continue.title", "CONTINUE?" },
            { "slots.continue.body", "Load slot {0} and pick the region up where it was left." },

            // -- the main menu's account of the save state
            { "menu.slots.used", "{0} OF {1} SLOTS USED" },
            { "menu.slots.free", "NEW RUNS GO IN SLOT {0}" },
            { "menu.slots.damaged", "A SAVE COULD NOT BE READ" },

            // -- characters
            { "characters.title", "CHARACTERS" },
            { "characters.body", "Who the road has introduced so far. A locked entry has not been met in this run." },
            { "characters.met", "{0} OF {1} MET" },
            { "characters.unknown", "NOT MET YET" },
            { "characters.metBadge", "MET" },
            { "character.wayfarer.title", "THE WAYFARER" },
            { "character.wayfarer.role", "YOU" },
            { "character.wayfarer.body", "A cutter's build, a walker's habit. Carries nothing the greenway does not already have in it." },
            { "character.stalker.title", "FOREST STALKER" },
            { "character.stalker.role", "TERRITORIAL" },
            { "character.stalker.body", "Patrols a short stretch and fights for it. Commits, misses, recovers — and does not give up the ground." },
            { "character.precursor.title", "THE PRECURSOR" },
            { "character.precursor.role", "UNKNOWN" },
            { "character.precursor.body", "Known only from what they left behind on the overhang: a marker cut into the trunk, and no body anywhere." },

            // -- collection
            { "collection.title", "COLLECTION" },
            { "collection.body", "Everything the run has found, met or finished. An entry that is not in this build says so." },
            { "collection.findings", "FINDINGS" },
            { "collection.findings.note", "Things found in a region. Each one is a world flag in the save file." },
            { "collection.abilities", "ABILITIES" },
            { "collection.abilities.note", "What the player can do. Rootbind is granted by a region that is not in this build." },
            { "collection.regions", "REGIONS" },
            { "collection.regions.note", "Regions finished. The Greenway records its completion at the north exit." },
            { "collection.progress", "{0} OF {1}" },
            { "collection.locked.unfound", "NOT FOUND YET" },
            { "collection.locked.laterRegion", "NOT IN THIS BUILD" },
            { "collection.locked.notInBuild", "NOT IN THIS BUILD" },
            { "entry.secret.greenway.overhang", "CUT MARKER" },
            { "entry.secret.greenway.overhang.body", "A sign cut into the trunk above the trail. Whoever made it expected to come back for it." },
            { "entry.ability.rootbind", "ROOTBIND" },
            { "entry.ability.rootbind.body", "Anchors the player to a root anchor, opening vertical routes. Held by the guardian of a region not in this build." },

            // -- achievements
            { "achievements.title", "ACHIEVEMENTS" },
            { "achievements.body", "Every achievement here can be earned in this build. None of them is a placeholder." },
            { "achievements.count", "{0} OF {1} EARNED" },
            { "achievements.locked", "LOCKED" },
            { "achievements.earned", "EARNED" },
            { "achievements.unlocked", "ACHIEVEMENT UNLOCKED" },
            { "ach.enter.title", "INTO THE GREENWAY" },
            { "ach.enter.body", "Enter region one." },
            { "ach.checkpoint.title", "A PLACE TO COME BACK TO" },
            { "ach.checkpoint.body", "Light a checkpoint." },
            { "ach.findings.title", "NOTHING LEFT ON THE OVERHANG" },
            { "ach.findings.body", "Find every secret the Greenway holds." },
            { "ach.exit.title", "NORTH EXIT" },
            { "ach.exit.body", "Reach the end of the Greenway." },
            { "ach.unbroken.title", "WITHOUT FALLING" },
            { "ach.unbroken.body", "Reach the north exit without dying once." },

            // -- in the region: the objective strip, the pause menu, the load, the end
            { "objective.greenway.exit", "REACH THE NORTH EXIT" },
            { "objective.greenway.exit.body", "Follow the cut trail north. Checkpoints keep your place." },
            { "hud.findings", "{0} OF {1} FOUND" },
            { "hud.deaths", "{0} FALLS" },
            { "hud.pause", "PAUSE" },
            { "pause.title", "PAUSED" },
            { "pause.body", "The region is exactly where you left it." },
            { "pause.stats", "{0} PLAYED · {1} FOUND · {2} FALLS" },
            { "pause.resume", "RESUME" },
            { "pause.save", "SAVE NOW" },
            { "pause.saved", "SAVED" },
            { "pause.restart", "RESTART THE REGION" },
            { "pause.settings", "SETTINGS" },
            { "pause.menu", "SAVE AND LEAVE" },
            { "pause.leave.title", "LEAVE THE REGION?" },
            { "pause.leave.body", "Progress is written down, and the region keeps your place. Continuing brings you back here." },
            { "pause.restart.title", "RESTART THE REGION?" },
            { "pause.restart.body", "You return to the last checkpoint with full health, and every encounter is restored. Nothing you have found is lost." },
            { "loading.title", "LOADING" },
            { "loading.region", "BUILDING THE GREENWAY" },
            { "loading.starting", "STARTING A NEW RUN" },
            { "loading.leaving", "WRITING PROGRESS" },
            { "error.title", "THE REGION COULD NOT START" },
            { "error.body", "The level data did not load, so there is nothing to play. This is a build problem, not something you did." },
            { "error.menu", "RETURN TO THE MAIN MENU" },
            { "complete.title", "GREENWAY COMPLETE" },
            { "complete.body", "The north exit is behind you. The next region is not in this build — what is here is finished." },
            { "complete.stats", "{0} PLAYED · {1} DEATH(S) · {2} OF {3} FOUND" },
            { "complete.continue", "KEEP EXPLORING" },
            { "complete.menu", "RETURN TO THE MENU" },
            { "complete.menu.title", "BACK TO THE MENU?" },
            { "complete.menu.body", "The run is saved. Continuing it later returns to the last checkpoint you lit." },

            // -- language
            { "language.es", "ESPAÑOL" },
            { "language.fr", "FRANÇAIS" },
            { "language.de", "DEUTSCH" },
            { "language.ru", "РУССКИЙ" },
            { "language.tr", "TÜRKÇE" },
            { "language.ar", "العربية" },
            { "language.fa", "فارسی" },
            { "language.zh", "中文" },
            { "language.ja", "日本語" },
            { "language.ko", "한국어" },
            { "language.it", "ITALIANO" },
            { "language.pt", "PORTUGUÊS" },
            { "language.needsFont", "NEEDS A SCRIPT FONT" },
            { "language.fontNote", "This build bundles one font, and it cannot draw this script. The language is not offered until a font that carries it is added." },
            { "language.notWritten", "NOT WRITTEN YET" },
            { "language.notWritten.note", "This language is named, and its sheet is not written. Adding it is a translation and one line in the catalogue, with nothing else to change." },
            { "language.unavailable", "NOT OFFERED" },
            { "language.coverage", "{0} · {1} OF {2} STRINGS" },
            { "language.coverage.label", "TRANSLATION" },
            { "language.coverage.note", "How much of the interface this language carries. A missing string falls back to English rather than showing a blank." },
            { "language.offered.count", "{0} OF {1} LANGUAGES OFFERED" },
        };

        /// <summary>The language code the interface is drawn in.</summary>
        public static string Language
        {
            get
            {
                return LanguageService.Code;
            }
        }

        /// <summary>
        /// The English table itself.
        /// </summary>
        /// <remarks>
        /// English is the reference every other language is measured against, so it is written here
        /// — one place, one sheet, read by people and by the gate — and the other languages live
        /// beside it in <c>Localization/</c>. <see cref="Get"/> does not read this directly; the
        /// localization service does, as the fallback for a key a translation does not have.
        /// </remarks>
        public static IEnumerable<KeyValuePair<string, string>> Raw
        {
            get { return English; }
        }

        /// <summary>The text for a key in the player's language, or the key itself when nobody has it.</summary>
        public static string Get(string key)
        {
            return LanguageService.Get(key);
        }

        /// <summary>The text for a key with numbered placeholders filled in.</summary>
        public static string Format(string key, params object[] arguments)
        {
            return LanguageService.Format(key, arguments);
        }

        /// <summary>True when English holds this key. Used by the gate and by tests.</summary>
        public static bool Has(string key)
        {
            return !string.IsNullOrEmpty(key) && English.ContainsKey(key);
        }

        /// <summary>Every key in the English table, for the gate.</summary>
        public static IEnumerable<string> Keys => English.Keys;
    }
}
