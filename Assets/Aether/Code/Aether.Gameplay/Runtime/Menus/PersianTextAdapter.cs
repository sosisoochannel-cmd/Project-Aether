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
                // The mixed-run overload keeps Latin words from being reversed with Persian text.
                // Its legacy tokenizer cannot accept empty tokens, so preserve unusual repeated-space
                // strings rather than risk throwing during a UI frame.
                if (_source.Contains("  "))
                    output = ArabicFixer.Fix(_source, false, true);
                else
                    output = ArabicFixer.Fix(_source, false);
            }
            if (!string.Equals(_text.text, output, System.StringComparison.Ordinal))
                _text.text = output;

            _rendered = output;
            _dirty = false;
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
