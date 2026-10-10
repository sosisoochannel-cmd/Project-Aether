namespace Aether.Core.Localization
{
    /// <summary>Which way a language reads.</summary>
    public enum TextDirection
    {
        LeftToRight = 0,
        RightToLeft = 1,
    }

    /// <summary>
    /// One language the interface can be offered in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The codes and the facts about them live here, in the assembly that owns settings, because the
    /// settings file stores the choice and the settings service has to be able to read it back into
    /// a language without knowing anything about the interface. The text itself lives in
    /// <c>Aether.Gameplay.Localization</c>, where the screens are.
    /// </para>
    /// <para>
    /// <b>The font flag is a fact, not a preference.</b> The project bundles one font — Unity's
    /// Liberation Sans fallback — and it has no Arabic, Persian, Chinese, Japanese or Korean glyphs.
    /// Offering one of those languages would show a screen of empty boxes, which is worse than not
    /// offering it; so a language whose script the font cannot draw says so here, and the row that
    /// offers it is disabled with the reason on it. When a font pack arrives, that is one flag.
    /// </para>
    /// </remarks>
    public sealed class LanguageDefinition
    {
        /// <summary>ISO 639-1 code, which is also what a save or settings file stores.</summary>
        public readonly string Code;

        /// <summary>
        /// The language's name in its own language.
        /// </summary>
        /// <remarks>
        /// Never translated. A player looking for their language is looking for the word they know,
        /// which is why every language picker ever built lists "Deutsch" and not "German". This is
        /// what the row that offers the language is labelled with.
        /// </remarks>
        public readonly string NativeName;

        /// <summary>
        /// The language's name in Latin script, for the sentences that talk about it.
        /// </summary>
        /// <remarks>
        /// The screen that explains which languages are not offered yet has to name them, and it has
        /// to do it in text the bundled font can draw — naming 日本語 on a screen whose whole point
        /// is that the font cannot draw 日本語 would be an irony the player cannot read.
        /// </remarks>
        public readonly string LatinName;

        /// <summary>Which way the language reads.</summary>
        public readonly TextDirection Direction;

        /// <summary>Whether the font this build bundles can draw the script.</summary>
        public readonly bool BundledFontCovers;

        public LanguageDefinition(string code, string nativeName, string latinName,
                                  TextDirection direction, bool bundledFontCovers)
        {
            Code = code;
            NativeName = nativeName;
            LatinName = latinName;
            Direction = direction;
            BundledFontCovers = bundledFontCovers;
        }

        /// <summary>The localisation key for the row that offers this language.</summary>
        public string LabelKey
        {
            get { return "language." + Code; }
        }
    }

    /// <summary>
    /// Every language the project knows, in the order a picker lists them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two kinds of entry live here.</b> A language whose interface text is written and whose
    /// script the font can draw is selectable. A language that is named but has no table yet is
    /// listed as not available — visible, explained, and not offered. The alternative, hiding them,
    /// makes a picker that looks complete and is not.
    /// </para>
    /// <para>
    /// <c>tools/verify/localization.py</c> holds <see cref="Offered"/> to the translation sheets:
    /// every offered language must have a sheet with every English key in it, and no sheet may have
    /// a key English does not.
    /// </para>
    /// </remarks>
    public static class LanguageCatalog
    {
        /// <summary>The language a fresh install is shown in, and the fallback for anything missing.</summary>
        public const string DefaultCode = "en";

        /// <summary>Every language the project knows about, in picker order.</summary>
        public static readonly LanguageDefinition[] All =
        {
            new LanguageDefinition("en", "English", "English", TextDirection.LeftToRight, true),
            new LanguageDefinition("es", "Español", "Spanish", TextDirection.LeftToRight, true),
            new LanguageDefinition("fa", "فارسی", "Persian", TextDirection.RightToLeft, true),
            new LanguageDefinition("fr", "Français", "French", TextDirection.LeftToRight, true),
            new LanguageDefinition("de", "Deutsch", "German", TextDirection.LeftToRight, true),
            new LanguageDefinition("it", "Italiano", "Italian", TextDirection.LeftToRight, true),
            new LanguageDefinition("pt", "Português", "European Portuguese", TextDirection.LeftToRight, true),
            new LanguageDefinition("pt-BR", "Português (Brasil)", "Brazilian Portuguese", TextDirection.LeftToRight, true),
            new LanguageDefinition("ru", "Русский", "Russian", TextDirection.LeftToRight, true),
            new LanguageDefinition("tr", "Türkçe", "Turkish", TextDirection.LeftToRight, true),
        };

        /// <summary>
        /// The languages this build <b>offers</b>: every one of these must have a complete table.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This list is the promise the language row keeps. <c>tools/verify/localization.py</c> holds
        /// it to the translation sheets: a code named here without a complete sheet — every English
        /// key, no key English does not have, placeholders intact — fails the gate, and a complete
        /// sheet whose code is not named here fails it too, so a finished translation cannot sit in
        /// the repository unshipped.
        /// </para>
        /// <para>
        /// <b>The supported product scope is deliberate.</b> The catalogue contains the ten agreed
        /// language choices: English, Spanish, Persian, French, German, Italian, European Portuguese,
        /// Brazilian Portuguese, Russian and Turkish. A language is only selectable when its complete
        /// translation sheet is generated into a table and its code is listed in <see cref="Offered"/>.
        /// Regional Portuguese has its own code so its vocabulary and culture never get conflated with
        /// European Portuguese.
        /// </para>
        /// </remarks>
        public static readonly string[] Offered = { "en", "es", "fa", "fr", "de" };

        /// <summary>Whether the build offers a language right now.</summary>
        public static bool IsOffered(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;

            for (int i = 0; i < Offered.Length; i++)
            {
                if (string.Equals(Offered[i], code, System.StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>The definition for a code, or the default when the code is unknown.</summary>
        public static LanguageDefinition Resolve(string code)
        {
            LanguageDefinition found = Find(code);
            return found ?? All[0];
        }

        /// <summary>The definition for a code, or null.</summary>
        public static LanguageDefinition Find(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;

            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Code, code, System.StringComparison.OrdinalIgnoreCase)) return All[i];
            }

            return null;
        }

        /// <summary>
        /// Where a code sits in the list, which is what a choice row stores.
        /// </summary>
        /// <remarks>
        /// The stored value is an index rather than a string because the settings file is a table of
        /// numbers: every other row is a float, and giving one row a different shape would be a
        /// second persistence mechanism for a single setting.
        /// </remarks>
        public static int IndexOf(string code)
        {
            if (string.IsNullOrEmpty(code)) return 0;

            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Code, code, System.StringComparison.OrdinalIgnoreCase)) return i;
            }

            return 0;
        }

        /// <summary>The code at an index, clamped into range.</summary>
        public static string CodeAt(int index)
        {
            if (index < 0) index = 0;
            if (index >= All.Length) index = All.Length - 1;
            return All[index].Code;
        }

        /// <summary>How many languages are known.</summary>
        public static int Count
        {
            get { return All.Length; }
        }
    }
}
