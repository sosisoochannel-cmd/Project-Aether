using System.Collections;
using System.Collections.Generic;
using Aether.Core.Localization;
using Aether.Gameplay.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// The first playable interface after the studio ident: a single language, framed in gold on black.
    /// The catalogue and LanguageService remain the only authority on what can actually be selected.
    /// </summary>
    public sealed class LanguageSelectionScreen : MenuScreen
    {
        private static readonly Color Black = new Color(0.012f, 0.011f, 0.009f, 1f);
        private static readonly Color Gold = new Color(0.85f, 0.71f, 0.43f, 1f);
        private static readonly Color BrightGold = new Color(0.97f, 0.87f, 0.63f, 1f);
        private static readonly Color DimGold = new Color(0.43f, 0.32f, 0.15f, 0.85f);
        private static readonly Color GoldGlow = new Color(1f, 0.78f, 0.36f, 1f);
        private static readonly Color PressedGold = new Color(1f, 0.91f, 0.67f, 1f);
        private static readonly Color Ivory = new Color(0.91f, 0.88f, 0.79f, 1f);

        // Preview the interface copy in the language currently highlighted, before committing it.
        // Keep these three short strings aligned with the canonical localization sheets.
        private static readonly Dictionary<string, string[]> SelectorCopy = new Dictionary<string, string[]>
        {
            { "en", new[] { "CHOOSE YOUR LANGUAGE", "USE ARROWS TO SELECT • CONFIRM TO CONTINUE", "CONTINUE" } },
            { "es", new[] { "ELIGE TU IDIOMA", "USA LAS FLECHAS PARA ELEGIR • CONFIRMA PARA CONTINUAR", "CONTINUAR" } },
            { "fa", new[] { "زبان خود را انتخاب کنید", "با پیکان‌ها انتخاب کنید • برای ادامه تأیید کنید", "ادامه" } },
            { "fr", new[] { "CHOISISSEZ VOTRE LANGUE", "UTILISEZ LES FLÈCHES POUR CHOISIR • CONFIRMEZ POUR CONTINUER", "CONTINUER" } },
            { "de", new[] { "SPRACHE WÄHLEN", "MIT DEN PFEILEN WÄHLEN • ZUM FORTFAHREN BESTÄTIGEN", "WEITER" } },
            { "it", new[] { "SCEGLI LA LINGUA", "USA LE FRECCE PER SCEGLIERE • CONFERMA PER CONTINUARE", "CONTINUA" } },
            { "pt", new[] { "ESCOLHA O SEU IDIOMA", "USE AS SETAS PARA ESCOLHER • CONFIRME PARA CONTINUAR", "CONTINUAR" } },
            { "pt-BR", new[] { "ESCOLHA SEU IDIOMA", "USE AS SETAS PARA ESCOLHER • CONFIRME PARA CONTINUAR", "CONTINUAR" } },
            { "ru", new[] { "ВЫБЕРИТЕ ЯЗЫК", "ВЫБЕРИТЕ С ПОМОЩЬЮ СТРЕЛОК • ПОДТВЕРДИТЕ ДЛЯ ПРОДОЛЖЕНИЯ", "ПРОДОЛЖИТЬ" } },
            { "tr", new[] { "DİLİNİZİ SEÇİN", "SEÇMEK İÇİN OKLARI KULLANIN • DEVAM ETMEK İÇİN ONAYLAYIN", "DEVAM ET" } },
        };

        private readonly List<string> _codes = new List<string>(16);
        private RectTransform _frame;
        private Text _eyebrow;
        private Text _title;
        private Text _language;
        private Text _counter;
        private Text _hint;
        private Button _left;
        private Button _right;
        private Button _confirm;
        private Image _underline;
        private Image _crest;
        private Image _blackout;
        private Coroutine _blackoutRoutine;
        private readonly List<RectTransform> _scratchMarks = new List<RectTransform>(6);
        private int _index;
        private bool _confirming;
        private Coroutine _motion;
        private static AudioClip _switchTone;
        private static AudioClip _confirmTone;

        protected override void Construct()
        {
            // The opaque screen intentionally covers the forest backdrop. The next screen reveals it.
            Image ground = MenuUi.Fill("Pure Black Ground", Rect, Black);
            ground.raycastTarget = true;

            _frame = MenuUi.CreateNode("Gold Architectural Frame", Rect);
            Image top = MenuUi.CreateHairline("Frame Top", _frame, DimGold, 2f);
            Image bottom = MenuUi.CreateHairline("Frame Bottom", _frame, DimGold, 2f);
            Image left = MenuUi.CreateImage("Frame Left", _frame, MenuArt.Solid, DimGold);
            Image right = MenuUi.CreateImage("Frame Right", _frame, MenuArt.Solid, DimGold);
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.anchorMax = new Vector2(1f, 1f);
            top.rectTransform.pivot = new Vector2(.5f, 1f);
            top.rectTransform.anchoredPosition = Vector2.zero;
            top.rectTransform.sizeDelta = new Vector2(0f, 2f);
            bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.pivot = new Vector2(.5f, 0f);
            bottom.rectTransform.anchoredPosition = Vector2.zero;
            bottom.rectTransform.sizeDelta = new Vector2(0f, 2f);
            left.rectTransform.anchorMin = new Vector2(0f, 0f);
            left.rectTransform.anchorMax = new Vector2(0f, 1f);
            left.rectTransform.pivot = new Vector2(0f, .5f);
            left.rectTransform.anchoredPosition = Vector2.zero;
            left.rectTransform.sizeDelta = new Vector2(2f, 0f);
            right.rectTransform.anchorMin = new Vector2(1f, 0f);
            right.rectTransform.anchorMax = new Vector2(1f, 1f);
            right.rectTransform.pivot = new Vector2(1f, .5f);
            right.rectTransform.anchoredPosition = Vector2.zero;
            right.rectTransform.sizeDelta = new Vector2(2f, 0f);

            // Fine corner cuts and a centered crest give the frame a crafted, non-template silhouette.
            CreateScratch("Upper Left Etch A", new Vector2(.06f, .88f), new Vector2(0f, 0f), 54f, -28f);
            CreateScratch("Upper Left Etch B", new Vector2(.06f, .88f), new Vector2(14f, -10f), 31f, -28f);
            CreateScratch("Upper Right Etch A", new Vector2(.94f, .88f), new Vector2(0f, 0f), 54f, 28f);
            CreateScratch("Lower Left Etch", new Vector2(.06f, .12f), new Vector2(0f, 0f), 44f, 28f);
            CreateScratch("Lower Right Etch A", new Vector2(.94f, .12f), new Vector2(0f, 0f), 44f, -28f);
            CreateScratch("Lower Right Etch B", new Vector2(.94f, .12f), new Vector2(-12f, 9f), 27f, -28f);

            _crest = MenuUi.CreateNode("Central Gold Crest", Rect);
            _crest.sizeDelta = new Vector2(13f, 13f);
            _crest.anchorMin = _crest.anchorMax = new Vector2(.5f, .5f);
            _crest.anchoredPosition = Vector2.zero;
            Image crestImage = MenuUi.CreateImage("Crest Diamond", _crest, MenuArt.Solid, Gold);
            crestImage.rectTransform.anchorMin = Vector2.zero;
            crestImage.rectTransform.anchorMax = Vector2.one;
            crestImage.rectTransform.offsetMin = Vector2.zero;
            crestImage.rectTransform.offsetMax = Vector2.zero;
            crestImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _underline = MenuUi.CreateHairline("Selected Language Gold Underline", Rect, GoldGlow, 2f);

            _eyebrow = MenuUi.CreateTrackedText("Studio Signature", Rect, "V A R E L L O N", 19f, Gold, 1f, TextAnchor.MiddleCenter);
            _title = MenuUi.CreateTrackedText("Title", Rect, LanguageService.Get("language.select.title"), 35f, Ivory, 1f, TextAnchor.MiddleCenter);
            _language = MenuUi.CreateText("Selected Language", Rect, "", 54f, BrightGold, TextAnchor.MiddleCenter);
            _counter = MenuUi.CreateText("Language Position", Rect, "", 17f, Gold, TextAnchor.MiddleCenter);
            _hint = MenuUi.CreateText("Navigation Hint", Rect, LanguageService.Get("language.select.hint"), 17f, new Color(.68f,.64f,.55f,1f), TextAnchor.MiddleCenter);

            _left = MakeButton("Previous Language", "‹", Rect, () => Step(-1));
            _right = MakeButton("Next Language", "›", Rect, () => Step(1));
            _confirm = MakeButton("Confirm Language", LanguageService.Get("language.select.continue"), Rect, Confirm);
            _confirming = false;

            foreach (string code in LanguageService.OfferedCodes()) _codes.Add(code);
            if (_codes.Count == 0)
            {
                Debug.LogError("[language selector] No selectable language passed LanguageService.CanSelect.");
                _codes.Add(LanguageCatalog.DefaultCode);
            }

            string current = LanguageService.Code;
            _index = Mathf.Max(0, _codes.IndexOf(current));
            RefreshLanguage();

            // This topmost veil is transparent until confirmation. It then fades the entire selector
            // away and deliberately remains opaque: the next screen is a separate milestone.
            _blackout = MenuUi.CreateImage("Post-Confirmation Blackout", Rect, MenuArt.Solid, new Color(0f, 0f, 0f, 0f));
            _blackout.rectTransform.anchorMin = Vector2.zero;
            _blackout.rectTransform.anchorMax = Vector2.one;
            _blackout.rectTransform.offsetMin = Vector2.zero;
            _blackout.rectTransform.offsetMax = Vector2.zero;
            _blackout.raycastTarget = false;
        }

        private void CreateScratch(string name, Vector2 anchor, Vector2 offset, float length, float angle)
        {
            Image mark = MenuUi.CreateHairline(name, Rect, new Color(.43f, .32f, .15f, .38f), 1f);
            RectTransform rt = mark.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(length, 1f);
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            mark.raycastTarget = false;
            _scratchMarks.Add(rt);
        }

        private Button MakeButton(string name, string label, Transform parent, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            // A restrained slant gives the controls a bespoke adventure-game silhouette.
            go.transform.localRotation = Quaternion.Euler(0f, 0f, -5f);
            Image image = go.GetComponent<Image>();
            image.sprite = MenuArt.Solid;
            image.color = Color.white;
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            ColorBlock colours = button.colors;
            colours.normalColor = new Color(.22f, .16f, .065f, .88f);
            colours.highlightedColor = GoldGlow;
            colours.pressedColor = PressedGold;
            colours.selectedColor = colours.highlightedColor;
            button.colors = colours;
            float labelSize = label.Length <= 2 ? 46f : (label.Length > 8 ? 17f : 21f);
            Text text = MenuUi.CreateText(name + " Label", go.transform, label, labelSize, BrightGold, TextAnchor.MiddleCenter);
            MenuUi.Stretch(text.rectTransform, 4f, 4f, 2f, 2f);
            text.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 5f);
            return button;
        }

        public override void Layout(float width, float height)
        {
            float frameWidth = Mathf.Min(width - 48f, 1500f);
            float frameHeight = Mathf.Min(height - 44f, 860f);
            _frame.sizeDelta = new Vector2(frameWidth, frameHeight);
            _frame.anchorMin = _frame.anchorMax = new Vector2(.5f, .5f);
            _frame.anchoredPosition = Vector2.zero;

            PlaceCentered(_eyebrow.rectTransform, height * .28f, new Vector2(Mathf.Min(width - 60f, 900f), 42f));
            PlaceCentered(_title.rectTransform, height * .13f, new Vector2(Mathf.Min(width - 64f, 1200f), 70f));
            PlaceCentered(_language.rectTransform, -height * .015f, new Vector2(Mathf.Min(width - 200f, 1050f), 120f));
            PlaceCentered(_counter.rectTransform, -height * .15f, new Vector2(300f, 34f));
            PlaceCentered(_hint.rectTransform, -height * .30f, new Vector2(Mathf.Min(width - 70f, 1000f), 42f));
            PlaceCentered((RectTransform)_left.transform, -height * .02f, new Vector2(76f, 76f), new Vector2(.5f,.5f), new Vector2(-Mathf.Min(width * .27f, 430f), 0f));
            PlaceCentered((RectTransform)_right.transform, -height * .02f, new Vector2(76f, 76f), new Vector2(.5f,.5f), new Vector2(Mathf.Min(width * .27f, 430f), 0f));
            PlaceCentered((RectTransform)_confirm.transform, -height * .23f, new Vector2(300f, 66f));

            // Keep the focus underline tied to the language name's actual center, not a fixed screen pixel.
            _underline.rectTransform.anchorMin = _underline.rectTransform.anchorMax = new Vector2(.5f, .5f);
            _underline.rectTransform.pivot = new Vector2(.5f, .5f);
            _underline.rectTransform.sizeDelta = new Vector2(Mathf.Min(width * .32f, 420f), 2f);
            _underline.rectTransform.anchoredPosition = new Vector2(0f, -height * .11f);
            _crest.anchoredPosition = new Vector2(0f, height * .205f);
            _crest.localScale = Vector3.one;
            _underline.raycastTarget = false;
        }

        private static void PlaceCentered(RectTransform rect, float y, Vector2 size,
                                          Vector2? anchor = null, Vector2? offset = null)
        {
            Vector2 a = anchor ?? new Vector2(.5f,.5f);
            rect.anchorMin = rect.anchorMax = a;
            rect.pivot = new Vector2(.5f,.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = offset.HasValue ? offset.Value + new Vector2(0f,y) : new Vector2(0f,y);
        }

        public override bool OnMove(MenuNav.Move move)
        {
            if (_confirming) return true;
            if (move == MenuNav.Move.Left) { Step(-1); return true; }
            if (move == MenuNav.Move.Right) { Step(1); return true; }
            return true; // This screen is a single horizontal selector, not a vertical menu.
        }

        public override bool OnBack() { return true; }

        private void Step(int delta)
        {
            if (_confirming || _codes.Count == 0) return;
            _index = (_index + delta + _codes.Count) % _codes.Count;
            PlayUiTone(false);
            RefreshLanguage();
        }

        private void RefreshLanguage()
        {
            LanguageDefinition language = LanguageCatalog.Find(_codes[_index]);
            if (language == null) return;
            if (_motion != null) StopCoroutine(_motion);
            // Preview shaping and alignment must follow the highlighted language, not the
            // currently committed language. Otherwise Persian is unshaped before confirmation,
            // or Latin copy gets incorrectly reordered while the saved language is Persian.
            bool previewRtl = language.Direction == TextDirection.RightToLeft;
            _language.text = PreparePreviewText(language.NativeName, previewRtl);
            string[] copy;
            if (!SelectorCopy.TryGetValue(language.Code, out copy)) copy = SelectorCopy["en"];
            _title.text = PreparePreviewText(copy[0], previewRtl);
            _hint.text = PreparePreviewText(copy[1], previewRtl);
            _eyebrow.alignment = TextAnchor.MiddleCenter;
            _title.alignment = TextAnchor.MiddleCenter;
            _language.alignment = TextAnchor.MiddleCenter;
            _counter.alignment = TextAnchor.MiddleCenter;
            _hint.alignment = TextAnchor.MiddleCenter;
            Text confirmLabel = _confirm.GetComponentInChildren<Text>();
            if (confirmLabel != null)
            {
                confirmLabel.text = PreparePreviewText(copy[2], previewRtl);
                confirmLabel.alignment = TextAnchor.MiddleCenter;
            }
            _language.color = GoldGlow;
            _underline.color = DimGold;
            _underline.rectTransform.localScale = new Vector3(.72f, 1f, 1f);
            _counter.text = (_index + 1).ToString("00") + "  /  " + _codes.Count.ToString("00");
            _motion = StartCoroutine(Emphasize());
        }

        private static string PreparePreviewText(string content, bool rightToLeft)
        {
            return rightToLeft ? RtlText.Visualize(content) : content;
        }

        private IEnumerator Emphasize()
        {
            _language.rectTransform.localScale = new Vector3(.97f,.97f,1f);
            _underline.rectTransform.localScale = new Vector3(.72f, 1f, 1f);
            _crest.localRotation = Quaternion.Euler(0f, 0f, 45f);
            float elapsed = 0f;
            while (elapsed < .18f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / .18f);
                float eased = 1f - (1f-t)*(1f-t);
                _language.rectTransform.localScale = Vector3.one * Mathf.Lerp(.97f, 1f, eased);
                _language.color = Color.Lerp(GoldGlow, PressedGold, eased);
                _underline.color = Color.Lerp(DimGold, GoldGlow, eased);
                _underline.rectTransform.localScale = new Vector3(Mathf.Lerp(.72f, 1f, eased), 1f, 1f);
                _crest.localRotation = Quaternion.Euler(0f, 0f, 45f + 12f * (1f - eased));
                yield return null;
            }
            _language.rectTransform.localScale = Vector3.one;
            _language.color = PressedGold;
            _underline.color = Gold;
            _underline.rectTransform.localScale = Vector3.one;
            _crest.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _motion = null;
        }

        /// <summary>
        /// Original procedural UI tones: a short, warm metallic two-part chime.
        /// This intentionally does not sample or reproduce another game's audio.
        /// </summary>
        private static void PlayUiTone(bool confirm)
        {
            AudioClip clip = confirm ? _confirmTone : _switchTone;
            if (clip == null)
            {
                clip = BuildUiTone(confirm);
                if (confirm) _confirmTone = clip;
                else _switchTone = clip;
            }

            // Use the menu's UI bus so mute and UI-volume settings affect this generated sound too.
            MenuAudio.PlayGeneratedUiClip(clip, confirm ? 0.24f : 0.16f);
        }

        private static AudioClip BuildUiTone(bool confirm)
        {
            const int sampleRate = 44100;
            int sampleCount = Mathf.RoundToInt(sampleRate * (confirm ? 0.24f : 0.16f));
            float[] samples = new float[sampleCount];
            float fundamental = confirm ? 1046.5f : 880f;
            float second = confirm ? 1568f : 1320f;
            float third = confirm ? 2093f : 1760f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Exp(-t * (confirm ? 14f : 20f));
                float attack = Mathf.Min(1f, t * 90f);
                float body = Mathf.Sin(2f * Mathf.PI * fundamental * t) * 0.58f
                           + Mathf.Sin(2f * Mathf.PI * second * t) * 0.28f
                           + Mathf.Sin(2f * Mathf.PI * third * t) * 0.14f;
                float sparkle = Mathf.Sin(2f * Mathf.PI * (third * 1.5f) * t)
                              * Mathf.Exp(-t * 48f) * 0.10f;
                samples[i] = (body + sparkle) * envelope * attack * 0.42f;
            }

            AudioClip clip = AudioClip.Create(
                confirm ? "AETHER_GoldConfirm" : "AETHER_GoldLanguageSwitch",
                sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void Confirm()
        {
            if (_confirming || _codes.Count == 0) return;
            string chosen = _codes[_index];
            PlayUiTone(true);
            // The service rechecks availability and persists through the existing settings pipeline.
            // A false return also means "already selected", so verify the resulting code rather than
            // treating every false return as a failure or blindly advancing after a rejected change.
            if (LanguageService.Code != chosen)
            {
                bool accepted = LanguageService.Set(chosen);
                if (!accepted && LanguageService.Code != chosen)
                {
                    Debug.LogWarning("[language selector] Selection was rejected; remaining on the language screen.");
                    return;
                }
            }
            if (LanguageService.Code != chosen)
            {
                Debug.LogWarning("[language selector] Selected language did not become active; refusing menu handoff.");
                return;
            }
            _confirming = true;
            _left.interactable = false;
            _right.interactable = false;
            _confirm.interactable = false;
            if (_blackoutRoutine != null) StopCoroutine(_blackoutRoutine);
            _blackoutRoutine = StartCoroutine(FadeToBlack());
        }

        private IEnumerator FadeToBlack()
        {
            const float duration = 0.72f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Ease-in/out for a deliberate cinematic close, independent of game time scale.
                float eased = t * t * (3f - 2f * t);
                _blackout.color = new Color(0f, 0f, 0f, eased);
                yield return null;
            }

            _blackout.color = Color.black;
            _blackoutRoutine = null;

            // The language choice is now committed. Keep the selector hidden behind black while
            // the shared menu transition hands control to the localized content advisory.
            Host.GoTo(MenuScreenId.ContentAdvisory);
        }
    }
}
