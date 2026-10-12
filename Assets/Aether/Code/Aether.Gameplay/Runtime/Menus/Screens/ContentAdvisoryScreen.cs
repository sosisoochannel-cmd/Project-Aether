using System.Collections;
using System.Collections.Generic;
using Aether.Core.Localization;
using Aether.Gameplay.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// A restrained, cinematic content notice inspired by the clarity and contrast of premium
    /// action-adventure interfaces. Informational only; it is not an age-classification rating.
    /// </summary>
    public sealed class ContentAdvisoryScreen : MenuScreen
    {
        private const float ReadingDurationSeconds = 7f;
        private const float EntranceSeconds = 0.65f;

        private static readonly Color Black = new Color(0.012f, 0.012f, 0.011f, 1f);
        private static readonly Color Gold = new Color(0.86f, 0.68f, 0.35f, 1f);
        private static readonly Color GoldMuted = new Color(0.57f, 0.43f, 0.24f, 0.8f);
        private static readonly Color Ivory = new Color(0.94f, 0.93f, 0.89f, 1f);
        private static readonly Color Muted = new Color(0.68f, 0.67f, 0.62f, 1f);

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

        private RectTransform _accentRule;
        private RectTransform _progressFill;
        private CanvasGroup _visuals;
        private Text _brand;
        private Text _eyebrow;
        private Text _title;
        private Text _body;
        private Text _footer;
        private Image _progressTrack;
        private Coroutine _autoAdvance;
        private bool _leaving;

        protected override void Construct()
        {
            // No frame, no dialog box, no ornamental plaque: just confident type, open space,
            // high contrast, and one small AETHER-gold accent.
            MenuUi.Fill("Advisory Clean Black Ground", Rect, Black, true);

            _brand = MenuUi.CreateTrackedText("Advisory Studio Signature", Rect,
                "V A R E L L O N", 15f, Gold, 1f, TextAnchor.MiddleCenter);
            _eyebrow = MenuUi.CreateTrackedText("Advisory Eyebrow", Rect,
                "PLAYER INFORMATION", 13f, Muted, 1f, TextAnchor.MiddleCenter);
            _title = MenuUi.CreateTrackedText("Advisory Title", Rect,
                "", 34f, Ivory, 1f, TextAnchor.MiddleCenter, FontStyle.Bold);

            _accentRule = MenuUi.CreateNode("Advisory Single Gold Accent", Rect);
            Image accent = MenuUi.CreateImage("Advisory Accent", _accentRule, MenuArt.Solid, Gold);
            accent.rectTransform.anchorMin = accent.rectTransform.anchorMax = new Vector2(.5f, .5f);
            accent.rectTransform.sizeDelta = new Vector2(52f, 2f);

            _body = MenuUi.CreateText("Advisory Body", Rect, "", 23f, Ivory, TextAnchor.MiddleCenter);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.lineSpacing = 1.18f;

            _footer = MenuUi.CreateTrackedText("Advisory Footer", Rect,
                "", 11f, Muted, 1f, TextAnchor.MiddleCenter);

            // A thin progress line is the only other visual ornament; it communicates timing
            // without adding a button or interrupting the player's entry into the game.
            RectTransform progress = MenuUi.CreateNode("Advisory Reading Progress", Rect);
            _progressTrack = MenuUi.CreateHairline("Advisory Progress Track", progress, new Color(GoldMuted.r, GoldMuted.g, GoldMuted.b, 0.45f), 2f);
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
            string[] lines = value.Split(new[] { '\n' });
            for (int i = 0; i < lines.Length; i++) lines[i] = RtlText.Visualize(lines[i]);
            return string.Join("\n", lines);
        }

        public override void Layout(float width, float height)
        {
            float safeWidth = Mathf.Max(240f, width - 56f);
            float compact = height < 600f ? 0.82f : 1f;

            Place(_brand.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, height * .255f), new Vector2(Mathf.Min(safeWidth, 760f), 30f));
            Place(_eyebrow.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, height * .175f), new Vector2(safeWidth, 30f));
            Place(_title.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, height * .095f), new Vector2(safeWidth, 64f * compact));
            Place(_accentRule, new Vector2(.5f, .5f), new Vector2(0f, height * .025f), new Vector2(64f, 8f));
            Place(_body.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -height * .075f), new Vector2(Mathf.Min(safeWidth, 900f), Mathf.Min(height * .25f, 190f)));
            Place(_footer.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -height * .265f), new Vector2(safeWidth, 26f));

            Transform progress = Rect.Find("Advisory Reading Progress");
            RectTransform progressRect = progress as RectTransform;
            Place(progressRect, new Vector2(.5f, 0f), new Vector2(0f, 18f), new Vector2(Mathf.Min(safeWidth, 760f), 4f));
            _progressTrack.rectTransform.sizeDelta = Vector2.zero;
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
            return true;
        }
    }
}