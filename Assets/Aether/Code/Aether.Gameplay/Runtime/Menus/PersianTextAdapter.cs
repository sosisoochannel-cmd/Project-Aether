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

        private void Awake()
        {
            _text = GetComponent<Text>();
            if (_text == null) { enabled = false; return; }

            _source = _text.text ?? string.Empty;
            _leftToRightAlignment = _text.alignment;
            _lastDirectionWasRtl = false;
        }

        private void LateUpdate()
        {
            if (_text == null) return;

            string current = _text.text ?? string.Empty;
            if (_rendered == null || !string.Equals(current, _rendered, System.StringComparison.Ordinal))
                _source = current;

            bool rtl = LanguageService.IsRightToLeft;
            if (rtl != _lastDirectionWasRtl)
            {
                _text.alignment = rtl ? Mirror(_leftToRightAlignment) : _leftToRightAlignment;
                _lastDirectionWasRtl = rtl;
            }

            string output = rtl ? ArabicFixer.Fix(_source, false, true) : _source;
            if (!string.Equals(_text.text, output, System.StringComparison.Ordinal))
                _text.text = output;

            _rendered = output;
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
