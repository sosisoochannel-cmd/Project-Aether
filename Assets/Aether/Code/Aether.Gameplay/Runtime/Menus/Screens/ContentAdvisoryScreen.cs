using System.Collections.Generic;
using Aether.Core.Localization;
using Aether.Gameplay.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// A calm, localized content notice between language selection and the main menu.
    /// This is an informational advisory, not a claimed age-classification rating.
    /// </summary>
    public sealed class ContentAdvisoryScreen : MenuScreen
    {
        private static readonly Color Black = new Color(0.012f, 0.011f, 0.009f, 1f);
        private static readonly Color Gold = new Color(0.85f, 0.71f, 0.43f, 1f);
        private static readonly Color BrightGold = new Color(0.97f, 0.87f, 0.63f, 1f);
        private static readonly Color Ivory = new Color(0.91f, 0.88f, 0.79f, 1f);
        private static readonly Color Muted = new Color(0.70f, 0.66f, 0.57f, 1f);

        // Copy is intentionally available in every language offered by the selector, so players
        // can read the notice before entering the game. Keep it factual and measured, not alarmist.
        private static readonly Dictionary<string, string[]> Copy = new Dictionary<string, string[]>
        {
            { "en", new[] { "This game contains fantasy combat and scenes of peril.\nSome content may not suit every player. Player discretion is advised.", "AETHER • PLAYER INFORMATION", "CONTINUE" } },
            { "es", new[] { "Este juego incluye combates fantásticos y situaciones de peligro.\nAlgunos contenidos pueden no ser adecuados para todos. Se recomienda discreción.", "AETHER • INFORMACIÓN PARA EL JUGADOR", "CONTINUAR" } },
            { "fa", new[] { "این بازی شامل نبردهای فانتزی و صحنه‌هایی از خطر است.\nممکن است بخشی از محتوا برای همه مناسب نباشد؛ با آگاهی از این موضوع بازی کنید.", "AETHER • اطلاعات بازیکن", "ادامه" } },
            { "fr", new[] { "Ce jeu comporte des combats fantastiques et des situations périlleuses.\nCertains éléments peuvent ne pas convenir à tout le monde. Faites preuve de discernement.", "AETHER • INFORMATIONS AUX JOUEURS", "CONTINUER" } },
            { "de", new[] { "Dieses Spiel enthält Fantasy-Kämpfe und Gefahrensituationen.\nEinige Inhalte sind möglicherweise nicht für alle geeignet. Bitte entscheide selbst.", "AETHER • SPIELERINFORMATION", "WEITER" } },
            { "it", new[] { "Il gioco include combattimenti fantasy e situazioni di pericolo.\nAlcuni contenuti potrebbero non essere adatti a tutti. Si consiglia discrezione.", "AETHER • INFORMAZIONI PER I GIOCATORI", "CONTINUA" } },
            { "pt", new[] { "Este jogo inclui combates de fantasia e situações de perigo.\nAlguns conteúdos podem não ser adequados a todos. Recomenda-se discrição.", "AETHER • INFORMAÇÃO AO JOGADOR", "CONTINUAR" } },
            { "pt-BR", new[] { "Este jogo inclui combates de fantasia e situações de perigo.\nAlguns conteúdos podem não ser adequados para todos. Jogue com isso em mente.", "AETHER • INFORMAÇÕES AO JOGADOR", "CONTINUAR" } },
            { "ru", new[] { "В игре есть фэнтезийные сражения и опасные ситуации.\nНекоторые материалы могут подойти не всем. Пожалуйста, учитывайте это.", "AETHER • ИНФОРМАЦИЯ ДЛЯ ИГРОКА", "ПРОДОЛЖИТЬ" } },
            { "tr", new[] { "Bu oyunda fantastik çatışmalar ve tehlike içeren sahneler bulunur.\nBazı içerikler herkese uygun olmayabilir. Lütfen bunu göz önünde bulundurun.", "AETHER • OYUNCU BİLGİLENDİRMESİ", "DEVAM ET" } },
        };

        private RectTransform _frame;
        private Text _brand;
        private Text _eyebrow;
        private Text _title;
        private Text _body;
        private Text _footer;
        private Button _continue;
        private Image _rule;
        private bool _leaving;

        protected override void Construct()
        {
            Image ground = MenuUi.Fill("Advisory Black Ground", Rect, Black);
            ground.raycastTarget = true;

            _frame = MenuUi.CreateNode("Advisory Gold Frame", Rect);
            Image top = MenuUi.CreateHairline("Advisory Frame Top", _frame, new Color(.43f, .32f, .15f, .85f), 2f);
            Image bottom = MenuUi.CreateHairline("Advisory Frame Bottom", _frame, new Color(.43f, .32f, .15f, .85f), 2f);
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

            _brand = MenuUi.CreateTrackedText("Advisory Brand", Rect, "V A R E L L O N", 17f, Gold, 1f, TextAnchor.MiddleCenter);
            _eyebrow = MenuUi.CreateTrackedText("Advisory Eyebrow", Rect, "PLAYER INFORMATION", 14f, Muted, 1f, TextAnchor.MiddleCenter);
            _title = MenuUi.CreateTrackedText("Advisory Title", Rect, "", 32f, Ivory, 1f, TextAnchor.MiddleCenter);
            _rule = MenuUi.CreateHairline("Advisory Gold Rule", Rect, Gold, 2f);
            _body = MenuUi.CreateText("Advisory Body", Rect, "", 24f, Ivory, TextAnchor.MiddleCenter);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _footer = MenuUi.CreateTrackedText("Advisory Footer", Rect, "", 13f, Muted, 1f, TextAnchor.MiddleCenter);
            _continue = MakeContinueButton();

            ApplyLanguageCopy();
        }

        private Button MakeContinueButton()
        {
            var go = new GameObject("Advisory Continue", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(Rect, false);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, -3f);
            Image image = go.GetComponent<Image>();
            image.sprite = MenuArt.Solid;
            image.color = new Color(.22f, .16f, .065f, .94f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(Continue);
            ColorBlock colours = button.colors;
            colours.normalColor = new Color(.22f, .16f, .065f, .94f);
            colours.highlightedColor = new Color(1f, .78f, .36f, 1f);
            colours.pressedColor = new Color(1f, .91f, .67f, 1f);
            colours.selectedColor = colours.highlightedColor;
            button.colors = colours;

            Text label = MenuUi.CreateText("Advisory Continue Label", go.transform, "", 20f, BrightGold, TextAnchor.MiddleCenter);
            MenuUi.Stretch(label.rectTransform, 8f, 8f, 4f, 4f);
            label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 3f);
            return button;
        }

        private void ApplyLanguageCopy()
        {
            string code = LanguageService.Code;
            string[] copy;
            if (!Copy.TryGetValue(code, out copy)) copy = Copy["en"];
            LanguageDefinition language = LanguageCatalog.Find(code);
            bool rtl = language != null && language.Direction == TextDirection.RightToLeft;

            _title.text = Prepare(LanguageService.Get("content.advisory.title"), rtl);
            _body.text = Prepare(copy[0], rtl);
            _footer.text = Prepare(copy[1], rtl);
            Text label = _continue.GetComponentInChildren<Text>();
            if (label != null) label.text = Prepare(copy[2], rtl);
            _title.alignment = TextAnchor.MiddleCenter;
            _body.alignment = TextAnchor.MiddleCenter;
            _footer.alignment = TextAnchor.MiddleCenter;
            if (label != null) label.alignment = TextAnchor.MiddleCenter;
        }

        private static string Prepare(string value, bool rtl)
        {
            if (!rtl || string.IsNullOrEmpty(value)) return value;

            // RtlText handles one visual line at a time. Shaping the entire paragraph would
            // reverse its newline along with the glyphs and could move a line break to the front.
            string[] lines = value.Split(new[] { '\\n' });
            for (int i = 0; i < lines.Length; i++)
                lines[i] = RtlText.Visualize(lines[i]);
            return string.Join("\\n", lines);
        }

        public override void Layout(float width, float height)
        {
            _frame.anchorMin = _frame.anchorMax = new Vector2(.5f, .5f);
            _frame.anchoredPosition = Vector2.zero;
            _frame.sizeDelta = new Vector2(Mathf.Min(width - 48f, 1500f), Mathf.Min(height - 44f, 860f));

            Place(_brand.rectTransform, height * .29f, new Vector2(Mathf.Min(width - 64f, 900f), 36f));
            Place(_eyebrow.rectTransform, height * .205f, new Vector2(Mathf.Min(width - 64f, 1000f), 30f));
            Place(_title.rectTransform, height * .115f, new Vector2(Mathf.Min(width - 64f, 1300f), 66f));
            Place(_body.rectTransform, -height * .015f, new Vector2(Mathf.Min(width - 96f, 1120f), Mathf.Min(height * .30f, 190f)));
            Place(_rule.rectTransform, -height * .155f, new Vector2(Mathf.Min(width * .42f, 440f), 2f));
            Place(_continue.transform as RectTransform, -height * .255f, new Vector2(Mathf.Min(width - 100f, 320f), 64f));
            Place(_footer.rectTransform, -height * .365f, new Vector2(Mathf.Min(width - 64f, 1000f), 28f));
        }

        private static void Place(RectTransform rect, float y, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, y);
        }

        private void Continue()
        {
            if (_leaving) return;
            _leaving = true;
            _continue.interactable = false;
            Host.GoTo(MenuScreenId.MainMenu);
        }

        public override bool OnBack()
        {
            // This is the first information gate after language choice; the player must explicitly
            // acknowledge it instead of skipping it accidentally with Back/Escape.
            return true;
        }
    }
}