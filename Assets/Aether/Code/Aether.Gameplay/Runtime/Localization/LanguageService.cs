using System.Collections.Generic;
using Aether.Core.Localization;
using Aether.Gameplay.Localization.Tables;
using Aether.Gameplay.Settings;

namespace Aether.Gameplay.Localization
{
    /// <summary>
    /// The interface's text, in the player's language.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is one place a string comes from, and this is it. Screens ask for a key; the key is
    /// looked up in the selected language's table; a key that language does not hold falls back to
    /// English; a key no language holds returns itself, so a missing string is visible on the screen
    /// that asked for it rather than silently blank.
    /// </para>
    /// <para>
    /// <b>The choice is a setting, stored like every other setting.</b> The code lives in the
    /// settings file, the settings service owns the file, and this class reads it. Nothing here
    /// writes a preference itself — <see cref="Set"/> goes through the service — and nothing anywhere
    /// else in the project writes the language at all.
    /// </para>
    /// <para>
    /// <b>Changing the language is announced, not polled.</b> <see cref="Changed"/> fires once, and
    /// the menu rebuilds the screens it has already built. Screens are built once and cached, so a
    /// language change has to reach text that was written at construction time — rebuilding is the
    /// only way to be sure every label moved, and it happens once per change rather than per frame.
    /// </para>
    /// <para>
    /// <b>Right-to-left is reported, not fabricated.</b> Arabic and Persian are catalogued, and
    /// <see cref="IsRightToLeft"/> answers true for them; but the font this build bundles cannot draw
    /// those scripts at all, and a Unity <c>Text</c> would draw them as disconnected, unshaped
    /// letters even with a font that could. Their rows are therefore not selectable, the screen that
    /// offers languages says why, and no shaping pass is faked here.
    /// </para>
    /// </remarks>
    public static class LanguageService
    {
        private static readonly Dictionary<string, Strings> Tables = BuildTables();

        private static string _code;
        private static bool _loaded;

        /// <summary>Raised once when the language changes. The menu rebuilds what it has built.</summary>
        public static event System.Action Changed;

        /// <summary>The language the interface is being drawn in.</summary>
        public static string Code
        {
            get
            {
                EnsureLoaded();
                return _code;
            }
        }

        /// <summary>The definition of the current language.</summary>
        public static LanguageDefinition Current
        {
            get { return LanguageCatalog.Resolve(Code); }
        }

        /// <summary>Which way the current language reads.</summary>
        public static TextDirection Direction
        {
            get { return Current.Direction; }
        }

        /// <summary>Whether the current language reads right to left.</summary>
        public static bool IsRightToLeft
        {
            get { return Direction == TextDirection.RightToLeft; }
        }

        /// <summary>
        /// Changes the language and stores it.
        /// </summary>
        /// <remarks>
        /// Refuses a language that cannot be shown: one the catalogue does not offer, one whose
        /// script the bundled font cannot draw, or one whose table is not in the build. Refusing is
        /// the honest answer — accepting any of those would leave the player looking at boxes, or at
        /// a screen in a language the row said it had changed away from.
        /// </remarks>
        public static bool Set(string code)
        {
            LanguageDefinition wanted = LanguageCatalog.Find(code);
            if (wanted == null) return false;
            if (!CanSelect(wanted.Code)) return false;

            EnsureLoaded();
            if (_code == wanted.Code) return false;

            _code = wanted.Code;

            // Through the settings service, which owns the file and the flush. The language is one
            // more value it holds; it is not a second persistence mechanism.
            AetherSettings.Ensure().Values.Language = wanted.Code;
            AetherSettings.Flush();

            System.Action handler = Changed;
            if (handler != null) handler();

            return true;
        }

        /// <summary>Whether a language can be chosen right now.</summary>
        /// <remarks>
        /// Three facts have to agree before the row may be offered: the catalogue promises the
        /// language, the font can draw its script, and its table is in the build. The first is a
        /// decision, the second is a limit of the bundled font, the third is what the sheet pipeline
        /// guarantees — and <c>tools/verify/localization.py</c> keeps the third honest.
        /// </remarks>
        public static bool CanSelect(string code)
        {
            LanguageDefinition wanted = LanguageCatalog.Find(code);
            if (wanted == null) return false;

            return LanguageCatalog.IsOffered(wanted.Code)
                && wanted.BundledFontCovers
                && Tables.ContainsKey(wanted.Code);
        }

        /// <summary>How complete a language's table is, as strings carried out of strings needed.</summary>
        public static void Coverage(string code, out int present, out int total)
        {
            total = Count;

            Strings table;
            present = Tables.TryGetValue(code ?? string.Empty, out table) ? table.Count : 0;
        }

        /// <summary>
        /// A one-line, font-safe account of a language's state: <c>ES · 268 OF 268 STRINGS</c>.
        /// </summary>
        public static string Describe(string code)
        {
            LanguageDefinition language = LanguageCatalog.Resolve(code);
            int present;
            int total;
            Coverage(language.Code, out present, out total);

            return language.Code.ToUpperInvariant() + " · " + present + " / " + total;
        }

        /// <summary>Every language the build offers, by code, in catalogue order.</summary>
        public static List<string> OfferedCodes()
        {
            var codes = new List<string>();
            for (int i = 0; i < LanguageCatalog.Count; i++)
            {
                string code = LanguageCatalog.All[i].Code;
                if (CanSelect(code)) codes.Add(code);
            }

            return codes;
        }

        /// <summary>
        /// The Latin names of the languages the build never offers, joined for the screen that says so.
        /// </summary>
        /// <remarks>
        /// Two groups, because they are two different problems: languages whose script this build's
        /// font cannot draw, and languages whose translation is simply not written yet. Grouping them
        /// apart is the whole point of the row — one of them needs a font, and the other needs six
        /// sheets and no code at all.
        /// </remarks>
        public static string DescribeNotOffered(bool fontBlocked)
        {
            var names = new List<string>();
            for (int i = 0; i < LanguageCatalog.Count; i++)
            {
                LanguageDefinition language = LanguageCatalog.All[i];
                if (CanSelect(language.Code)) continue;
                if (language.BundledFontCovers == fontBlocked) continue;

                names.Add(language.LatinName.ToUpperInvariant());
            }

            return string.Join(" · ", names.ToArray());
        }

        /// <summary>How many of the catalogued languages the build offers.</summary>
        public static int OfferedCount
        {
            get { return OfferedCodes().Count; }
        }

        /// <summary>How many strings the reference language has: what coverage is measured against.</summary>
        public static int Count
        {
            get
            {
                Strings english = English;
                return english == null ? 0 : english.Count;
            }
        }

        /// <summary>Whether a translation table exists for a code, drawn or not.</summary>
        public static bool IsTranslated(string code)
        {
            LanguageDefinition wanted = LanguageCatalog.Find(code);
            return wanted != null && Tables.ContainsKey(wanted.Code);
        }

        /// <summary>
        /// The text for a key in the current language, or the key itself when nobody has it.
        /// </summary>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            EnsureLoaded();

            Strings table;
            if (Tables.TryGetValue(_code, out table))
            {
                string text = table.Lookup(key);
                if (!string.IsNullOrEmpty(text)) return text;
            }

            // Fallback is English, and it is a real fallback rather than a silent one: a language
            // that is missing a key shows the English sentence, which a player can still use, and
            // the gate reports the gap so it gets translated.
            Strings english;
            if (Tables.TryGetValue(LanguageCatalog.DefaultCode, out english))
            {
                string text = english.Lookup(key);
                if (!string.IsNullOrEmpty(text)) return text;
            }

            return key;
        }

        /// <summary>The text for a key with numbered placeholders filled in.</summary>
        public static string Format(string key, params object[] arguments)
        {
            string text = Get(key);
            if (arguments == null || arguments.Length == 0) return text;

            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, text, arguments);
            }
            catch (System.FormatException)
            {
                // A key whose text has the wrong number of placeholders is a translation bug, and a
                // menu that throws on one would be a worse bug. The raw text is shown instead.
                return text;
            }
        }

        /// <summary>True when the current language holds this key.</summary>
        public static bool Has(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            EnsureLoaded();
            Strings table;
            return Tables.TryGetValue(_code, out table) && table.Has(key);
        }

        /// <summary>True when English holds this key. What the gate checks every table against.</summary>
        public static bool EnglishHas(string key)
        {
            return !string.IsNullOrEmpty(key) && English.Has(key);
        }

        /// <summary>The English table, as the reference every other table is measured against.</summary>
        public static Strings English
        {
            get
            {
                Strings english;
                return Tables.TryGetValue(LanguageCatalog.DefaultCode, out english) ? english : null;
            }
        }

        /// <summary>Every table in this build, by code, for the gate and for diagnostics.</summary>
        public static IEnumerable<KeyValuePair<string, Strings>> AllTables
        {
            get { return Tables; }
        }

        /// <summary>How many languages have a table in this build.</summary>
        public static int TranslatedCount
        {
            get { return Tables.Count; }
        }

        /// <summary>
        /// Re-reads the stored language. Called once per screen build and by the settings screen.
        /// </summary>
        /// <remarks>
        /// A stored code that names no language, or one this build cannot draw, resolves to English
        /// rather than to a blank interface: a hand-edited settings file, or a save carried from a
        /// build that offered a language this one does not, must not be able to make the game
        /// unreadable.
        /// </remarks>
        public static void Reload()
        {
            string stored = AetherSettings.Ensure().Values.Language;
            LanguageDefinition resolved = LanguageCatalog.Resolve(stored);
            if (!CanSelectQuietly(resolved.Code)) resolved = LanguageCatalog.All[0];

            bool changed = _loaded && _code != resolved.Code;
            _code = resolved.Code;
            _loaded = true;

            if (!changed) return;

            System.Action handler = Changed;
            if (handler != null) handler();
        }

        /// <summary>
        /// Whether a language is offered and drawable, without touching the settings file.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="CanSelect"/> because <see cref="Reload"/> is the call that reads
        /// the settings file, and asking it here would ask the file for a value while reading it.
        /// </remarks>
        private static bool CanSelectQuietly(string code)
        {
            LanguageDefinition wanted = LanguageCatalog.Find(code);
            if (wanted == null) return false;

            return LanguageCatalog.IsOffered(wanted.Code)
                && wanted.BundledFontCovers
                && Tables.ContainsKey(wanted.Code);
        }

        /// <summary>
        /// The tables this build carries, keyed by language code.
        /// </summary>
        /// <remarks>
        /// Generated rather than written here: a language is added by translating its sheet in
        /// <c>tools/localization</c> and naming the code in the catalogue, and the sheet pipeline
        /// writes both this registry and the tables it names. There is no step where a table exists
        /// but is not registered, or is registered but is not complete, because that is exactly the
        /// state a "translated" claim would otherwise be able to hide behind.
        /// </remarks>
        private static Dictionary<string, Strings> BuildTables()
        {
            return StringsTables.Build();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            Reload();
        }
    }
}
