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
        private static readonly Color Ivory = new Color(0.91f, 0.88f, 0.79f, 1f);

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
        private int _index;
        private bool _confirming;
        private Coroutine _motion;

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
            top.rectTransform.anchorMin = new Vector2(0f, 1f);\n            top.rectTransform.anchorMax = new Vector2(1f, 1f);\n            top.rectTransform.pivot = new Vector2(.5f, 1f);\n            top.rectTransform.anchoredPosition = Vector2.zero;\n            top.rectTransform.sizeDelta = new Vector2(0f, 2f);
            bottom.rectTransform.anchorMin = new Vector2(0f, 0f);\n            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);\n            bottom.rectTransform.pivot = new Vector2(.5f, 0f);\n            bottom.rectTransform.anchoredPosition = Vector2.zero;\n            bottom.rectTransform.sizeDelta = new Vector2(0f, 2f);
            left.rectTransform.anchorMin = new Vector2(0f, 0f);\n            left.rectTransform.anchorMax = new Vector2(0f, 1f);\n            left.rectTransform.pivot = new Vector2(0f, .5f);\n            left.rectTransform.anchoredPosition = Vector2.zero;\n            left.rectTransform.sizeDelta = new Vector2(2f, 0f);
            right.rectTransform.anchorMin = new Vector2(1f, 0f);\n            right.rectTransform.anchorMax = new Vector2(1f, 1f);\n            right.rectTransform.pivot = new Vector2(1f, .5f);\n            right.rectTransform.anchoredPosition = Vector2.zero;\n            right.rectTransform.sizeDelta = new Vector2(2f, 0f);

            _eyebrow = MenuUi.CreateTrackedText("Studio Signature", Rect, "V A R E L L O N", 19f, Gold, 1f, TextAnchor.MiddleCenter);
            _title = MenuUi.CreateTrackedText("Title", Rect, "CHOOSE YOUR LANGUAGE", 35f, Ivory, 1f, TextAnchor.MiddleCenter);
            _language = MenuUi.CreateText("Selected Language", Rect, "", 54f, BrightGold, TextAnchor.MiddleCenter);
            _counter = MenuUi.CreateText("Language Position", Rect, "", 17f, Gold, TextAnchor.MiddleCenter);
            _hint = MenuUi.CreateText("Navigation Hint", Rect, "SELECT WITH ARROWS  •  CONFIRM TO CONTINUE", 17f, new Color(.68f,.64f,.55f,1f), TextAnchor.MiddleCenter);

            _left = MakeButton("Previous Language", "‹", Rect, () => Step(-1));
            _right = MakeButton("Next Language", "›", Rect, () => Step(1));
            _confirm = MakeButton("Confirm Language", "CONTINUE", Rect, Confirm);
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
        }

        private Button MakeButton(string name, string label, Transform parent, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = MenuArt.Solid;
            image.color = new Color(.12f, .095f, .045f, .55f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            ColorBlock colours = button.colors;
            colours.normalColor = new Color(1f, 1f, 1f, .94f);
            colours.highlightedColor = new Color(1f, .88f, .62f, 1f);
            colours.pressedColor = new Color(.75f, .57f, .27f, 1f);
            colours.selectedColor = colours.highlightedColor;
            button.colors = colours;
            Text text = MenuUi.CreateText(name + " Label", go.transform, label, label == "CONTINUE" ? 21f : 46f, BrightGold, TextAnchor.MiddleCenter);
            MenuUi.Stretch(text.rectTransform, 4f, 4f, 2f, 2f);
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
            RefreshLanguage();
        }

        private void RefreshLanguage()
        {
            LanguageDefinition language = LanguageCatalog.Find(_codes[_index]);
            if (language == null) return;
            if (_motion != null) StopCoroutine(_motion);
            _language.text = MenuUi.PrepareText(language.NativeName);
            _language.color = Gold;
            _counter.text = (_index + 1).ToString("00") + "  /  " + _codes.Count.ToString("00");
            _motion = StartCoroutine(Emphasize());
        }

        private IEnumerator Emphasize()
        {
            _language.rectTransform.localScale = new Vector3(.97f,.97f,1f);
            float elapsed = 0f;
            while (elapsed < .18f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / .18f);
                float eased = 1f - (1f-t)*(1f-t);
                _language.rectTransform.localScale = Vector3.one * Mathf.Lerp(.97f, 1f, eased);
                _language.color = Color.Lerp(Gold, BrightGold, eased);
                yield return null;
            }
            _language.rectTransform.localScale = Vector3.one;
            _language.color = BrightGold;
            _motion = null;
        }

        private void Confirm()
        {
            if (_confirming || _codes.Count == 0) return;
            string chosen = _codes[_index];
            // The service rechecks availability and persists through the existing settings pipeline.
            LanguageService.Set(chosen);
            _confirming = true;
            _left.interactable = false;
            _right.interactable = false;
            _confirm.interactable = false;
            Host.GoTo(MenuScreenId.MainMenu);
        }
    }
}
