using ArabicSupport;
using Aether.Gameplay.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// Applies Arabic/Persian contextual shaping to legacy uGUI text when an RTL language is active.
    /// Source text is retained separately so language switches never shape an already-shaped string.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PersianTextAdapter : MonoBehaviour
    {
        private Text _text;
        private string _source;
        private string _rendered;
        private TextAnchor _leftToRightAlignment;
        private bool _lastDirectionWasRtl;
        private bool _dirty = true;

        private void Awake()
        {
            _text = GetComponent<Text>();
            if (_text == null) { enabled = false; return; }

            _source = _text.text ?? string.Empty;
            _leftToRightAlignment = _text.alignment;
            _lastDirectionWasRtl = false;
        }

        private void OnEnable()
        {
            LanguageService.Changed += OnLanguageChanged;
            _dirty = true;
        }

        private void OnDisable()
        {
            LanguageService.Changed -= OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (_text == null) return;

            string current = _text.text ?? string.Empty;
            bool textChanged = _rendered == null || !string.Equals(current, _rendered, System.StringComparison.Ordinal);
            if (!textChanged && !_dirty) return;
            if (textChanged) _source = current;
            bool rtl = LanguageService.IsRightToLeft;
            if (rtl != _lastDirectionWasRtl)
            {
                _text.alignment = rtl ? Mirror(_leftToRightAlignment) : _leftToRightAlignment;
                _lastDirectionWasRtl = rtl;
            }

            string output = _source;
            if (rtl && !string.IsNullOrEmpty(_source))
            {
                output = ShapeRightToLeft(_source);
            }
            if (!string.Equals(_text.text, output, System.StringComparison.Ordinal))
                _text.text = output;

            _rendered = output;
            _dirty = false;
        }

        private static string ShapeRightToLeft(string source)
        {
            if (string.IsNullOrEmpty(source) || !ContainsRtl(source)) return source;

            // Legacy uGUI has no bidi engine. Shape each Persian word independently, keep Latin
            // identifiers/numbers in their original order, then reverse word order for the visual
            // left-to-right renderer. Work per line so wrapping cannot reverse the order of lines.
            string[] lines = System.Text.RegularExpressions.Regex.Split(source, @"(\r\n|\r|\n)");
            var output = new System.Text.StringBuilder(source.Length + 8);

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                if (line == "\r\n" || line == "\r" || line == "\n")
                {
                    output.Append(line);
                    continue;
                }

                string safe = System.Text.RegularExpressions.Regex.Replace(line, @"\s+", " ").Trim();
                if (!ContainsRtl(safe))
                {
                    output.Append(safe);
                    continue;
                }

                string[] words = safe.Split(' ');
                for (int wordIndex = words.Length - 1; wordIndex >= 0; wordIndex--)
                {
                    string word = words[wordIndex];
                    if (ContainsRtl(word))
                        output.Append(ArabicFixer.Fix(word, false, true));
                    else
                        output.Append(word);

                    if (wordIndex > 0) output.Append(' ');
                }
            }

            return output.ToString();
        }

        private static bool ContainsRtl(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= '\u0600' && c <= '\u06FF') ||
                    (c >= '\u0750' && c <= '\u077F') ||
                    (c >= '\u08A0' && c <= '\u08FF') ||
                    (c >= '\uFB50' && c <= '\uFDFF') ||
                    (c >= '\uFE70' && c <= '\uFEFF'))
                    return true;
            }

            return false;
        }

        private static TextAnchor Mirror(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAnchor.UpperRight;
                case TextAnchor.MiddleLeft: return TextAnchor.MiddleRight;
                case TextAnchor.LowerLeft: return TextAnchor.LowerRight;
                case TextAnchor.UpperRight: return TextAnchor.UpperLeft;
                case TextAnchor.MiddleRight: return TextAnchor.MiddleLeft;
                case TextAnchor.LowerRight: return TextAnchor.LowerLeft;
                default: return anchor;
            }
        }
    }
}
