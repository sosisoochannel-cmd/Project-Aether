using System.Collections;
using System.Collections.Generic;
using Aether.Core.Localization;
using Aether.Gameplay.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// A cinematic, localized content notice between language selection and the main menu.
    /// It is informational, not an age-classification rating, and advances automatically.
    /// </summary>
    public sealed class ContentAdvisoryScreen : MenuScreen
    {
        private const float ReadingDurationSeconds = 7f;
        private const float EntranceSeconds = 0.65f;

        private static readonly Color Black = new Color(0.009f, 0.010f, 0.008f, 1f);
        private static readonly Color Panel = new Color(0.035f, 0.030f, 0.020f, 0.96f);
        private static readonly Color Gold = new Color(0.88f, 0.72f, 0.42f, 1f);
        private static readonly Color GoldSoft = new Color(0.55f, 0.39f, 0.18f, 0.72f);
        private static readonly Color GoldDim = new Color(0.32f, 0.24f, 0.12f, 0.72f);
        private static readonly Color Ivory = new Color(0.94f, 0.91f, 0.83f, 1f);
        private static readonly Color Muted = new Color(0.68f, 0.64f, 0.55f, 1f);

        private static readonly Dictionary<string, string[]> Copy = new Dictionary<string, string[]>
        {
            { "en", new[] { "PLAYER INFORMATION", "This game contains fantasy combat and scenes of peril.\nSome content may not suit every player. Player discretion is advised.", "AETHER  /  PLAYER INFORMATION" } },
            { "es", new[] { "INFORMACIÓN PARA EL JUGADOR", "Este juego incluye combates fantásticos y situaciones de peligro.\nAlgunos contenidos pueden no ser adecuados para todos. Se recomienda discreción.", "AETHER  /  INFORMACIÓN PARA EL JUGADOR" } },
            { "fa", new[] { "اطلاعات بازیکن", "این بازی شامل نبردهای فانتزی و صحنه‌هایی از خطر است.\nممکن است بخشی از محتوا برای همه مناسب نباشد؛ با آگاهی از این موضوع بازی کنید.", "AETHER  /  اطلاعات بازیکن" } },
            { "fr", new[] { "INFORMATIONS POUR LES JOUEURS", "Ce jeu comporte des combats fantastiques et des situations périlleuses.\nCertains éléments peuvent ne pas convenir à tout le monde. Faites preuve de discernement.", "AETHER  /  INFORMATIONS AUX JOUEURS" } },
            { "de", new[] { "SPIELERINFORMATION", "Dieses Spiel enthält Fantasy-Kämpfe und Gefahrensituationen.\nEinige Inhalte sind möglicherweise nicht für alle geeignet. Bitte entscheide selbst.", "AETHER  /  SPIELERINFORMATION" } },
            { "it", new[] { "INFORMAZIONI PER I GIOCATORI", "Il gioco include combattimenti fantasy e situazioni di pericolo.\nAlcuni contenuti potrebbero non essere adatti a tutti. Si consiglia discrezione.", "AETHER  /  INFORMAZIONI PER I GIOCATORI" } },
            { "pt", new[] { "INFORMAÇÃO AO JOGADOR", "Este jogo inclui combates de fantasia e situações de perigo.\nAlguns conteúdos podem não ser adequados a todos. Recomenda-se discrição.", "AETHER  /  INFORMAÇÃO AO JOGADOR" } },
            { "pt-BR", new[] { "INFORMAÇÕES AO JOGADOR", "Este jogo inclui combates de fantasia e situações de perigo.\nAlguns conteúdos podem não ser adequados para todos. Jogue com isso em mente.", "AETHER  /  INFORMAÇÕES AO JOGADOR" } },
            { "ru", new[] { "ИНФОРМАЦИЯ ДЛЯ ИГРОКА", "В игре есть фэнтезийные сражения и опасные ситуации.\nНекоторые материалы могут подойти не всем. Пожалуйста, учитывайте это.", "AETHER  /  ИНФОРМАЦИЯ ДЛЯ ИГРОКА" } },
            { "tr", new[] { "OYUNCU BİLGİLENDİRMESİ", "Bu oyunda fantastik çatışmalar ve tehlike içeren sahneler bulunur.\nBazı içerikler herkese uygun olmayabilir. Lütfen bunu göz önünde bulundurun.", "AETHER  /  OYUNCU BİLGİLENDİRMESİ" } },
        };

        private RectTransform _frame;
        private RectTransform _panel;
        private RectTransform _content;
        private RectTransform _crest;
        private RectTransform _progressFill;
        private CanvasGroup _visuals;
        private Text _brand;
        private Text _eyebrow;
        private Text _title;
        private Text _body;
        private Text _footer;
        private Image _panelFill;
        private Image _progressTrack;
        private Coroutine _autoAdvance;
        private bool _leaving;

        protected override void Construct()
        {
            MenuUi.Fill("Advisory Obsidian Ground", Rect, Black, true);

            // Quiet, architectural frame: double keylines, clipped-looking corner brackets,
            // and tiny gold registration marks rather than a generic rectangular dialog.
            _frame = MenuUi.CreateNode("Advisory Outer Frame", Rect);
            MenuUi.Fill("Advisory Frame Tint", _frame, new Color(0.10f, 0.075f, 0.035f, 0.08f));
            AddFrameEdges(_frame);

            // The central plaque creates depth without a texture dependency or expensive shader.
            _panel = MenuUi.CreateNode("Advisory Plaque", Rect);
            _panelFill = MenuUi.Fill("Advisory Plaque Shadow", _panel, Panel);
            AddFrameEdges(_panel);
            Image inset = MenuUi.CreateHairline("Advisory Plaque Inset", _panel, GoldDim, 1f);
            inset.rectTransform.anchorMin = new Vector2(0f, 0f);
            inset.rectTransform.anchorMax = new Vector2(1f, 1f);
            inset.rectTransform.offsetMin = new Vector2(12f, 12f);
            inset.rectTransform.offsetMax = new Vector2(-12f, -12f);
            inset.rectTransform.sizeDelta = Vector2.zero;

            _content = MenuUi.CreateNode("Advisory Content", _panel);
            _crest = MenuUi.CreateNode("Advisory Crest", _content);
            Image diamondOuter = MenuUi.CreateImage("Crest Outer Diamond", _crest, MenuArt.Solid, GoldSoft);
            diamondOuter.rectTransform.sizeDelta = new Vector2(38f, 38f);
            diamondOuter.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image diamondInner = MenuUi.CreateImage("Crest Inner Diamond", _crest, MenuArt.Solid, Black);
            diamondInner.rectTransform.sizeDelta = new Vector2(25f, 25f);
            diamondInner.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image core = MenuUi.CreateImage("Crest Core", _crest, MenuArt.Solid, Gold);
            core.rectTransform.sizeDelta = new Vector2(7f, 7f);
            core.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            AddCrestRays(_crest);

            _brand = MenuUi.CreateTrackedText("Advisory Studio Signature", _content,
                "V A R E L L O N", 16f, Gold, 1f, TextAnchor.MiddleCenter);
            _eyebrow = MenuUi.CreateTrackedText("Advisory Eyebrow", _content,
                "PLAYER INFORMATION", 14f, Muted, 1f, TextAnchor.MiddleCenter);
            _title = MenuUi.CreateTrackedText("Advisory Title", _content,
                "", 34f, Ivory, 1f, TextAnchor.MiddleCenter, FontStyle.Bold);

            RectTransform titleRule = MenuUi.CreateNode("Advisory Title Rule Group", _content);
            Image ruleLeft = MenuUi.CreateHairline("Advisory Rule Left", titleRule, GoldSoft, 1.5f);
            Image ruleRight = MenuUi.CreateHairline("Advisory Rule Right", titleRule, GoldSoft, 1.5f);
            Image ruleGem = MenuUi.CreateImage("Advisory Rule Gem", titleRule, MenuArt.Solid, Gold);
            ruleLeft.rectTransform.anchorMin = new Vector2(0f, .5f);
            ruleLeft.rectTransform.anchorMax = new Vector2(.42f, .5f);
            ruleLeft.rectTransform.sizeDelta = new Vector2(0f, 1.5f);
            ruleRight.rectTransform.anchorMin = new Vector2(.58f, .5f);
            ruleRight.rectTransform.anchorMax = new Vector2(1f, .5f);
            ruleRight.rectTransform.sizeDelta = new Vector2(0f, 1.5f);
            ruleGem.rectTransform.anchorMin = ruleGem.rectTransform.anchorMax = new Vector2(.5f, .5f);
            ruleGem.rectTransform.sizeDelta = new Vector2(7f, 7f);
            ruleGem.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            RectTransform copyCard = MenuUi.CreateNode("Advisory Copy Card", _content);
            MenuUi.Fill("Advisory Copy Wash", copyCard, new Color(0.0f, 0.0f, 0.0f, 0.22f));
            Image cardTop = MenuUi.CreateHairline("Advisory Copy Top Edge", copyCard, GoldDim, 1f);
            Image cardBottom = MenuUi.CreateHairline("Advisory Copy Bottom Edge", copyCard, GoldDim, 1f);
            cardTop.rectTransform.anchorMin = new Vector2(.12f, 1f);
            cardTop.rectTransform.anchorMax = new Vector2(.88f, 1f);
            cardTop.rectTransform.pivot = new Vector2(.5f, 1f);
            cardTop.rectTransform.anchoredPosition = Vector2.zero;
            cardBottom.rectTransform.anchorMin = new Vector2(.12f, 0f);
            cardBottom.rectTransform.anchorMax = new Vector2(.88f, 0f);
            cardBottom.rectTransform.pivot = new Vector2(.5f, 0f);
            cardBottom.rectTransform.anchoredPosition = Vector2.zero;

            _body = MenuUi.CreateText("Advisory Body", copyCard, "", 23f, Ivory, TextAnchor.MiddleCenter);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.lineSpacing = 1.15f;

            _footer = MenuUi.CreateTrackedText("Advisory Footer", _content,
                "", 12f, Muted, 1f, TextAnchor.MiddleCenter);

            // The countdown is a quiet progress signature, not a button or an instruction.
            RectTransform progress = MenuUi.CreateNode("Advisory Reading Progress", Rect);
            _progressTrack = MenuUi.CreateHairline("Advisory Progress Track", progress, GoldDim, 2f);
            _progressTrack.rectTransform.anchorMin = new Vector2(0f, .5f);
            _progressTrack.rectTransform.anchorMax = new Vector2(1f, .5f);
            _progressTrack.rectTransform.sizeDelta = Vector2.zero;
            Image fill = MenuUi.CreateImage("Advisory Progress Fill", progress, MenuArt.Solid, Gold);
            _progressFill = fill.rectTransform;
            _progressFill.anchorMin = new Vector2(0f, .5f);
            _progressFill.anchorMax = new Vector2(0f, .5f);
            _progressFill.pivot = new Vector2(0f, .5f);
            _progressFill.sizeDelta = new Vector2(0f, 2f);

            _visuals = MenuUi.Group(Rect);
            ApplyLanguageCopy();
        }

        private static void AddFrameEdges(RectTransform parent)
        {
            // Eight restrained corner strokes make the border feel engraved, not boxed in.
            float length = parent == null ? 0f : 34f;
            float thickness = 2f;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            {
                RectTransform corner = MenuUi.CreateNode("Engraved Corner", parent);
                Vector2 anchor = new Vector2(x == 0 ? 0f : 1f, y == 0 ? 0f : 1f);
                Vector2 inward = new Vector2(x == 0 ? 1f : -1f, y == 0 ? 1f : -1f);
                corner.anchorMin = corner.anchorMax = anchor;
                corner.pivot = new Vector2(x == 0 ? 0f : 1f, y == 0 ? 0f : 1f);
                corner.anchoredPosition = new Vector2(x == 0 ? 14f : -14f, y == 0 ? 14f : -14f);
                corner.sizeDelta = new Vector2(length, length);

                Image horizontal = MenuUi.CreateHairline("Corner Horizontal Engraving", corner, GoldSoft, thickness);
                horizontal.rectTransform.anchorMin = new Vector2(x == 0 ? 0f : 1f, y == 0 ? 0f : 1f);
                horizontal.rectTransform.anchorMax = horizontal.rectTransform.anchorMin;
                horizontal.rectTransform.pivot = horizontal.rectTransform.anchorMin;
                horizontal.rectTransform.anchoredPosition = Vector2.zero;
                horizontal.rectTransform.sizeDelta = new Vector2(length, thickness);

                Image vertical = MenuUi.CreateHairline("Corner Vertical Engraving", corner, GoldSoft, thickness);
                vertical.rectTransform.anchorMin = horizontal.rectTransform.anchorMin;
                vertical.rectTransform.anchorMax = horizontal.rectTransform.anchorMin;
                vertical.rectTransform.pivot = horizontal.rectTransform.anchorMin;
                vertical.rectTransform.anchoredPosition = Vector2.zero;
                vertical.rectTransform.sizeDelta = new Vector2(thickness, length);
            }
        }

        private static void AddCrestRays(RectTransform parent)
        {
            for (int i = 0; i < 4; i++)
            {
                Image ray = MenuUi.CreateHairline("Crest Compass Ray", parent,
                    new Color(Gold.r, Gold.g, Gold.b, 0.62f), 1f);
                ray.rectTransform.anchorMin = ray.rectTransform.anchorMax = new Vector2(.5f, .5f);
                ray.rectTransform.sizeDelta = new Vector2(54f, 1f);
                ray.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 45f);
            }
        }

        private void ApplyLanguageCopy()
        {
            string code = LanguageService.Code;
            string[] copy;
            if (!Copy.TryGetValue(code, out copy)) copy = Copy["en"];
            LanguageDefinition language = LanguageCatalog.Find(code);
            bool rtl = language != null && language.Direction == TextDirection.RightToLeft;

            _eyebrow.text = Prepare(copy[0], rtl);
            _title.text = Prepare(LanguageService.Get("content.advisory.title"), rtl);
            _body.text = Prepare(copy[1], rtl);
            _footer.text = Prepare(copy[2], rtl);
            _eyebrow.alignment = TextAnchor.MiddleCenter;
            _title.alignment = TextAnchor.MiddleCenter;
            _body.alignment = TextAnchor.MiddleCenter;
            _footer.alignment = TextAnchor.MiddleCenter;
        }

        private static string Prepare(string value, bool rtl)
        {
            if (!rtl || string.IsNullOrEmpty(value)) return value;
            string[] lines = value.Split(new[] { '\\n' });
            for (int i = 0; i < lines.Length; i++) lines[i] = RtlText.Visualize(lines[i]);
            return string.Join("\\n", lines);
        }

        public override void Layout(float width, float height)
        {
            _frame.anchorMin = Vector2.zero;
            _frame.anchorMax = Vector2.one;
            _frame.offsetMin = new Vector2(26f, 24f);
            _frame.offsetMax = new Vector2(-26f, -24f);

            _panel.anchorMin = _panel.anchorMax = new Vector2(.5f, .5f);
            _panel.pivot = new Vector2(.5f, .5f);
            _panel.anchoredPosition = Vector2.zero;
            _panel.sizeDelta = new Vector2(Mathf.Min(width - 112f, 1120f), Mathf.Min(height - 128f, 720f));

            MenuUi.Stretch(_content, 36f, 36f, 30f, 30f);
            Place(_crest, new Vector2(.5f, 1f), new Vector2(0f, -40f), new Vector2(70f, 70f));
            Place(_brand.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -94f), new Vector2(Mathf.Min(width - 180f, 800f), 28f));
            Place(_eyebrow.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -128f), new Vector2(Mathf.Min(width - 180f, 900f), 28f));
            Place(_title.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -180f), new Vector2(Mathf.Min(width - 180f, 1000f), 64f));

            RectTransform ruleGroup = _content.Find("Advisory Title Rule Group") as RectTransform;
            Place(ruleGroup, new Vector2(.5f, 1f), new Vector2(0f, -224f), new Vector2(Mathf.Min(width - 260f, 560f), 12f));

            RectTransform copyCard = _content.Find("Advisory Copy Card") as RectTransform;
            Place(copyCard, new Vector2(.5f, .5f), new Vector2(0f, -12f), new Vector2(Mathf.Min(width - 240f, 840f), Mathf.Min(height * .22f, 160f)));
            Place(_body.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(Mathf.Min(width - 300f, 760f), Mathf.Min(height * .18f, 132f)));

            Place(_footer.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, 48f), new Vector2(Mathf.Min(width - 180f, 900f), 26f));

            Transform progress = Rect.Find("Advisory Reading Progress");
            RectTransform progressRect = progress as RectTransform;
            Place(progressRect, new Vector2(.5f, 0f), new Vector2(0f, 20f), new Vector2(Mathf.Min(width - 120f, 760f), 4f));
            _progressTrack.rectTransform.sizeDelta = new Vector2(0f, 2f);
            _progressFill.sizeDelta = new Vector2(0f, 2f);
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        private IEnumerator AutoAdvanceAfterReadingPeriod()
        {
            float elapsed = 0f;
            _visuals.alpha = 0f;
            while (elapsed < ReadingDurationSeconds && !_leaving)
            {
                elapsed += Time.unscaledDeltaTime;
                float entrance = Mathf.Clamp01(elapsed / EntranceSeconds);
                _visuals.alpha = entrance * entrance * (3f - 2f * entrance);

                RectTransform trackRect = _progressTrack.rectTransform;
                float progressWidth = trackRect.rect.width > 0f ? trackRect.rect.width : trackRect.sizeDelta.x;
                _progressFill.sizeDelta = new Vector2(progressWidth * Mathf.Clamp01(elapsed / ReadingDurationSeconds), 2f);
                yield return null;
            }

            _autoAdvance = null;
            if (_leaving || Host == null) yield break;
            _leaving = true;
            Host.GoTo(MenuScreenId.MainMenu);
        }

        protected override void OnShown(bool instant)
        {
            _leaving = false;
            if (_autoAdvance != null) StopCoroutine(_autoAdvance);
            _autoAdvance = StartCoroutine(AutoAdvanceAfterReadingPeriod());
            if (instant && _visuals != null) _visuals.alpha = 1f;
        }

        protected override void OnHidden()
        {
            if (_autoAdvance != null)
            {
                StopCoroutine(_autoAdvance);
                _autoAdvance = null;
            }
        }

        public override bool OnBack()
        {
            // This is a notice, not a dialog; Back cannot accidentally skip its reading period.
            return true;
        }
    }
}