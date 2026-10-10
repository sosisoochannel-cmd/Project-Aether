using System.Collections.Generic;
using System.Text;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// Small, allocation-light RTL preparation layer for the runtime-built legacy UI.
    /// It converts Arabic/Persian letters to contextual presentation forms, then orders RTL
    /// runs visually while keeping numbers, placeholders and Latin words readable.
    /// </summary>
    internal static class RtlText
    {
        private struct Forms
        {
            public char Isolated;
            public char Final;
            public char Initial;
            public char Medial;
            public bool Dual;
            public Forms(char isolated, char final, char initial, char medial, bool dual)
            {
                Isolated = isolated; Final = final; Initial = initial; Medial = medial; Dual = dual;
            }
        }

        private static readonly Dictionary<char, Forms> Map = BuildMap();

        public static string Visualize(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // This is a deterministic fallback for Unity's legacy Text, not a full Unicode
            // Bidirectional Algorithm. Keep contiguous LTR phrases intact, reverse the order of
            // directional runs in an RTL paragraph, and reverse the words inside each RTL run.
            // Numbers and placeholders remain LTR tokens instead of having their characters flipped.
            string[] tokens = input.Split(new[] { ' ' }, System.StringSplitOptions.None);
            var groups = new List<List<string>>();
            var groupIsRtl = new List<bool>();
            bool hasRtl = false;

            for (int i = 0; i < tokens.Length; i++)
            {
                bool rtl = ContainsRtl(tokens[i]);
                hasRtl |= rtl;
                string prepared = rtl ? ShapeRun(tokens[i]) : tokens[i];

                if (groups.Count == 0 || groupIsRtl[groupIsRtl.Count - 1] != rtl)
                {
                    groups.Add(new List<string>());
                    groupIsRtl.Add(rtl);
                }

                groups[groups.Count - 1].Add(prepared);
            }

            // Preserve the original string exactly when it is wholly LTR.
            if (!hasRtl) return input;

            var output = new StringBuilder(input.Length + 8);
            bool first = true;
            for (int g = groups.Count - 1; g >= 0; g--)
            {
                List<string> group = groups[g];
                bool rtl = groupIsRtl[g];
                for (int i = 0; i < group.Count; i++)
                {
                    int word = rtl ? group.Count - 1 - i : i;
                    if (!first) output.Append(' ');
                    output.Append(group[word]);
                    first = false;
                }
            }

            return output.ToString();
        }

        private static bool ContainsRtl(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
                if (IsRtlChar(text[i])) return true;
            return false;
        }

        private static string ShapeRun(string run)
        {
            var shaped = new StringBuilder(run.Length);
            for (int i = 0; i < run.Length; i++)
            {
                char c = run[i];
                Forms f;
                if (!Map.TryGetValue(c, out f))
                {
                    shaped.Append(c);
                    continue;
                }

                int p = PreviousLetter(run, i - 1);
                int n = NextLetter(run, i + 1);

                // A letter can join to the following logical letter when it has an initial form;
                // the following letter can accept that join when it has a final form. This matters
                // for right-joining letters such as alef: they accept a join from the previous
                // letter even though they cannot continue the join to the next one.
                bool previousCanJoinForward = false;
                if (p >= 0)
                {
                    Forms pf;
                    previousCanJoinForward = Map.TryGetValue(run[p], out pf) && pf.Dual;
                }

                bool nextCanAcceptJoin = false;
                if (n >= 0)
                {
                    Forms nf;
                    nextCanAcceptJoin = Map.TryGetValue(run[n], out nf) && nf.Final != '\0';
                }

                bool joinPrev = previousCanJoinForward && f.Final != '\0';
                bool joinNext = f.Dual && nextCanAcceptJoin;

                if (joinPrev && joinNext && f.Medial != '\0') shaped.Append(f.Medial);
                else if (joinPrev && f.Final != '\0') shaped.Append(f.Final);
                else if (joinNext && f.Initial != '\0') shaped.Append(f.Initial);
                else shaped.Append(f.Isolated);
            }

            // Unity's legacy Text is LTR-oriented. Presentation forms are already shaped,
            // so reversing the visual RTL run gives the correct glyph order without touching
            // Latin/number runs outside it. ZWNJ stays in the output but, above, blocks joining.
            return ReverseKeepingMarks(shaped.ToString());
        }

        private static string ReverseKeepingMarks(string text)
        {
            // Combining marks belong to the preceding base character. Reverse grapheme-like
            // base+mark clusters, not UTF-16 code units, or vowel marks move onto the wrong glyph.
            var clusters = new List<string>();
            for (int i = 0; i < text.Length;)
            {
                int start = i++;
                while (i < text.Length && IsRtlMark(text[i])) i++;
                clusters.Add(text.Substring(start, i - start));
            }

            var output = new StringBuilder(text.Length);
            for (int i = clusters.Count - 1; i >= 0; i--)
                output.Append(clusters[i]);
            return output.ToString();
        }

        private static int PreviousLetter(string text, int index)
        {
            // Only combining marks may sit between two letters that join. Never skip spaces,
            // punctuation, Latin text, or U+200C ZERO WIDTH NON-JOINER: doing so joins words
            // across their boundaries and produces visibly incorrect Persian glyph forms.
            for (int i = index; i >= 0; i--)
            {
                if (IsRtlMark(text[i])) continue;
                if (Map.ContainsKey(text[i])) return i;
                return -1;
            }
            return -1;
        }

        private static int NextLetter(string text, int index)
        {
            for (int i = index; i < text.Length; i++)
            {
                if (IsRtlMark(text[i])) continue;
                if (Map.ContainsKey(text[i])) return i;
                return -1;
            }
            return -1;
        }

        private static bool IsRtlChar(char c)
        {
            return Map.ContainsKey(c) || (c >= 'ﭐ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿');
        }

        private static bool IsRtlMark(char c)
        {
            return c == 'ً' || c == 'ٌ' || c == 'ٍ' || c == 'َ'
                || c == 'ُ' || c == 'ِ' || c == 'ّ' || c == 'ْ'
                || c == 'ٓ' || c == 'ٔ' || c == 'ٕ' || c == 'ٰ';
        }

        private static Dictionary<char, Forms> BuildMap()
        {
            var m = new Dictionary<char, Forms>();

            Add(m, 'ا', 0xFE8D, 0xFE8E, 0, 0, false);
            Add(m, 'آ', 0xFE81, 0xFE82, 0, 0, false);
            Add(m, 'أ', 0xFE83, 0xFE84, 0, 0, false);
            Add(m, 'إ', 0xFE87, 0xFE88, 0, 0, false);
            Add(m, 'ب', 0xFE8F, 0xFE90, 0xFE91, 0xFE92, true);
            Add(m, 'پ', 0xFB56, 0xFB57, 0xFB58, 0xFB59, true);
            Add(m, 'ت', 0xFE95, 0xFE96, 0xFE97, 0xFE98, true);
            Add(m, 'ث', 0xFE99, 0xFE9A, 0xFE9B, 0xFE9C, true);
            Add(m, 'ج', 0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0, true);
            Add(m, 'چ', 0xFB7A, 0xFB7B, 0xFB7C, 0xFB7D, true);
            Add(m, 'ح', 0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4, true);
            Add(m, 'خ', 0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8, true);
            Add(m, 'د', 0xFEA9, 0xFEAA, 0, 0, false);
            Add(m, 'ذ', 0xFEAB, 0xFEAC, 0, 0, false);
            Add(m, 'ر', 0xFEAD, 0xFEAE, 0, 0, false);
            Add(m, 'ز', 0xFEAF, 0xFEB0, 0, 0, false);
            Add(m, 'ژ', 0xFB8A, 0xFB8B, 0, 0, false);
            Add(m, 'س', 0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4, true);
            Add(m, 'ش', 0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8, true);
            Add(m, 'ص', 0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC, true);
            Add(m, 'ض', 0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0, true);
            Add(m, 'ط', 0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4, true);
            Add(m, 'ظ', 0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8, true);
            Add(m, 'ع', 0xFEC9, 0xFECA, 0xFECB, 0xFECC, true);
            Add(m, 'غ', 0xFECD, 0xFECE, 0xFECF, 0xFED0, true);
            Add(m, 'ف', 0xFED1, 0xFED2, 0xFED3, 0xFED4, true);
            Add(m, 'ق', 0xFED5, 0xFED6, 0xFED7, 0xFED8, true);
            Add(m, 'ک', 0xFB8E, 0xFB8F, 0xFB90, 0xFB91, true);
            Add(m, 'ك', 0xFED9, 0xFEDA, 0xFEDB, 0xFEDC, true);
            Add(m, 'گ', 0xFB92, 0xFB93, 0xFB94, 0xFB95, true);
            Add(m, 'ل', 0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0, true);
            Add(m, 'م', 0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4, true);
            Add(m, 'ن', 0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8, true);
            Add(m, 'و', 0xFEED, 0xFEEE, 0, 0, false);
            Add(m, 'ه', 0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC, true);
            Add(m, 'ی', 0xFBFC, 0xFBFD, 0xFBFE, 0xFBFF, true);
            Add(m, 'ي', 0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4, true);
            Add(m, 'ء', 0xFE80, 0, 0, 0, false);
            // Arabic-specific letters used by the new Arabic locale.
            Add(m, 'ة', 0xFE93, 0xFE94, 0, 0, false);
            Add(m, 'ى', 0xFEEF, 0xFEF0, 0, 0, false);
            Add(m, 'ؤ', 0xFE85, 0xFE86, 0, 0, false);
            Add(m, 'ئ', 0xFE89, 0xFE8A, 0xFE8B, 0xFE8C, true);

            return m;
        }

        private static void Add(Dictionary<char, Forms> map, char source,
                                 int isolated, int final, int initial, int medial, bool dual)
        {
            map[source] = new Forms((char)isolated, (char)final, (char)initial, (char)medial, dual);
        }
    }
}
