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

            // Work a sentence as whitespace-separated visual runs. Consecutive RTL words are
            // emitted from right to left, while Latin/number tokens remain in their original
            // order. This is deliberately small and deterministic for the menu's known strings.
            string[] tokens = input.Split(new[] { ' ' }, System.StringSplitOptions.None);
            var prepared = new string[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
                prepared[i] = ContainsRtl(tokens[i]) ? ShapeRun(tokens[i]) : tokens[i];

            var output = new StringBuilder(input.Length + 8);
            int index = 0;
            while (index < prepared.Length)
            {
                if (!ContainsRtl(tokens[index]))
                {
                    output.Append(prepared[index]);
                    if (index + 1 < prepared.Length) output.Append(' ');
                    index++;
                    continue;
                }

                int end = index;
                while (end + 1 < prepared.Length && ContainsRtl(tokens[end + 1])) end++;

                for (int i = end; i >= index; i--)
                {
                    output.Append(prepared[i]);
                    if (i != index || end != prepared.Length - 1) output.Append(' ');
                }

                index = end + 1;
                if (index < prepared.Length && output.Length > 0 && output[output.Length - 1] != ' ')
                    output.Append(' ');
            }

            return output.ToString().TrimEnd();
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

                // Joining is directional. A dual-joining previous letter may connect forward
                // to a current letter that accepts a previous connection (has a final form),
                // even when the current letter itself cannot connect to the following letter.
                // This distinction is essential for alef, dal, reh and other right-joining letters.
                bool joinPrev = false;
                bool joinNext = false;

                if (p >= 0)
                {
                    Forms pf;
                    joinPrev = Map.TryGetValue(run[p], out pf)
                        && pf.Dual && f.Final != '\0';
                }

                if (n >= 0)
                {
                    Forms nf;
                    joinNext = f.Dual && Map.TryGetValue(run[n], out nf)
                        && nf.Final != '\0';
                }

                if (joinPrev && joinNext && f.Medial != '\0') shaped.Append(f.Medial);
                else if (joinPrev && f.Final != '\0') shaped.Append(f.Final);
                else if (joinNext && f.Initial != '\0') shaped.Append(f.Initial);
                else shaped.Append(f.Isolated);
            }

            // Unity's legacy Text is LTR-oriented. Presentation forms are already shaped,
            // so reversing the visual RTL run gives the correct glyph order without touching
            // Latin/number runs outside it.
            char[] chars = shaped.ToString().ToCharArray();
            System.Array.Reverse(chars);
            return new string(chars);
        }

        private static int PreviousLetter(string text, int index)
        {
            for (int i = index; i >= 0; i--)
                if (Map.ContainsKey(text[i])) return i;
            return -1;
        }

        private static int NextLetter(string text, int index)
        {
            for (int i = index; i < text.Length; i++)
                if (Map.ContainsKey(text[i])) return i;
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

            return m;
        }

        private static void Add(Dictionary<char, Forms> map, char source,
                                 int isolated, int final, int initial, int medial, bool dual)
        {
            map[source] = new Forms((char)isolated, (char)final, (char)initial, (char)medial, dual);
        }
    }
}
