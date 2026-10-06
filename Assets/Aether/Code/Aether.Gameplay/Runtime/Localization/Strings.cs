using System.Collections.Generic;

namespace Aether.Gameplay.Localization
{
    /// <summary>
    /// One language's strings: a key and its text, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately a class rather than a raw dictionary, for one reason that matters: a translation
    /// that cannot be indexed by key is caught at construction rather than on a screen. The keys are
    /// checked against English by <c>tools/verify/localization.py</c>, in the editor and at build
    /// time, but a build can also be run from an editor that was open across a merge — so the type
    /// refuses to be quietly wrong about its own contents.
    /// </para>
    /// <para>
    /// The Arabic and Persian tables carry one extra duty: their text has to be reshaped and
    /// reordered before a Unity <c>Text</c> will draw it correctly, so their constructor marks the
    /// table as right-to-left and the renderer asks. See <c>Localization.Direction</c>.
    /// </para>
    /// </remarks>
    public sealed class Strings
    {
        private readonly Dictionary<string, string> _text;

        public Strings(Dictionary<string, string> text)
        {
            _text = text ?? new Dictionary<string, string>();
        }

        /// <summary>Builds a table from a flat list of key, text pairs.</summary>
        /// <remarks>
        /// The shape every language file uses: <c>Strings.Of("menu.continue", "CONTINUAR", ...)</c>.
        /// A flat list rather than a dictionary initialiser because it reads as a translation sheet —
        /// one key, one line — which is what it is, and because a mis-set brace in a large nested
        /// initialiser is a compile error thirty lines away from its cause.
        /// </remarks>
        public static Strings Of(params string[] pairs)
        {
            var text = new Dictionary<string, string>(pairs.Length / 2);
            for (int i = 0; i + 1 < pairs.Length; i += 2) text[pairs[i]] = pairs[i + 1];

            return new Strings(text);
        }

        /// <summary>
        /// Wraps a table that is already built, without copying it.
        /// </summary>
        /// <remarks>
        /// Used for English, whose table lives in <c>MenuStrings</c> because that is the file people
        /// edit and the gate reads. Wrapping rather than duplicating means there is one English table
        /// and no way for a second copy of it to drift.
        /// </remarks>
        public static Strings From(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            var text = new Dictionary<string, string>();
            if (pairs != null)
            {
                foreach (KeyValuePair<string, string> pair in pairs) text[pair.Key] = pair.Value;
            }

            return new Strings(text);
        }

        /// <summary>How many strings the table holds.</summary>
        public int Count
        {
            get { return _text.Count; }
        }

        /// <summary>Every key in this table.</summary>
        public IEnumerable<string> Keys
        {
            get { return _text.Keys; }
        }

        /// <summary>The string for a key, or null when this language has none.</summary>
        public string Lookup(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _text.TryGetValue(key, out string text) ? text : null;
        }

        /// <summary>Whether the table holds a key.</summary>
        public bool Has(string key)
        {
            return !string.IsNullOrEmpty(key) && _text.ContainsKey(key);
        }

        /// <summary>
        /// Keys this language is missing, computed once against a reference table.
        /// </summary>
        /// <remarks>
        /// Used by the gate and by the diagnostics screen. Kept as a method rather than a field so a
        /// table can be built and thrown away by a test without carrying state around.
        /// </remarks>
        public List<string> MissingAgainst(Strings reference)
        {
            var missing = new List<string>();
            if (reference == null) return missing;

            foreach (string key in reference.Keys)
            {
                if (!_text.ContainsKey(key)) missing.Add(key);
            }

            return missing;
        }

        /// <summary>Keys this table has that the reference does not. A typo shows up here.</summary>
        public List<string> ExtraAgainst(Strings reference)
        {
            var extra = new List<string>();
            if (reference == null) return extra;

            foreach (string key in _text.Keys)
            {
                if (!reference.Has(key)) extra.Add(key);
            }

            return extra;
        }
    }
}
