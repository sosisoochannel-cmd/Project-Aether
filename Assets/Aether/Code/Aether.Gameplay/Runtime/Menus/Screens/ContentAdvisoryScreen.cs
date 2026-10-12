using System.Collections;
using System.Collections.Generic;
using Aether.Core.Localization;
using Aether.Gameplay.Localization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// A plain-language content warning shown after the Varellon studio ident.
    /// The presentation intentionally contains only a pure-black background, a large heading,
    /// and the warning copy.
    /// </summary>
    public sealed class ContentAdvisoryScreen : MenuScreen
    {
        private const float EntranceSeconds = 0.45f;

        private static readonly Color Black = Color.black;
        private static readonly Color White = new Color(0.96f, 0.96f, 0.94f, 1f);

        private static readonly Dictionary<string, string[]> Copy = new Dictionary<string, string[]>
        {
            { "en", new[] { "WARNING", "AETHER is a fantasy adventure that includes combat, hostile encounters, dangerous environments, and moments of tension. Some scenes may be intense for certain players. Take breaks whenever you need them, and play at a pace that feels comfortable for you. If you are playing on a shared device, make sure the experience is suitable for everyone who may be watching. This notice is provided to help players make an informed choice before continuing." } },
            { "es", new[] { "ADVERTENCIA", "AETHER es una aventura fantástica que incluye combates, encuentros hostiles, entornos peligrosos y momentos de tensión. Algunas escenas pueden resultar intensas para ciertos jugadores. Descansa cuando lo necesites y juega a un ritmo que te resulte cómodo. Si compartes el dispositivo, asegúrate de que la experiencia sea adecuada para quienes puedan estar mirando. Este aviso ayuda a tomar una decisión informada antes de continuar." } },
            { "fa", new[] { "هشدار", "بازی AETHER یک ماجراجویی فانتزی است و شامل نبرد، رویارویی با دشمنان، محیط‌های خطرناک و لحظات پرتنش می‌شود. ممکن است بعضی از صحنه‌ها برای برخی بازیکنان سنگین یا ناراحت‌کننده باشند. هر زمان که نیاز داشتید استراحت کنید و با سرعتی بازی کنید که برایتان راحت است. اگر بازی را در کنار دیگران یا روی دستگاهی مشترک انجام می‌دهید، در نظر بگیرید که این تجربه برای همهٔ افراد حاضر مناسب باشد. این هشدار برای آگاهی شما از محتوای بازی ارائه شده است تا پیش از ادامه، با اطلاع تصمیم بگیرید." } },
            { "fr", new[] { "AVERTISSEMENT", "AETHER est une aventure fantastique qui comprend des combats, des rencontres hostiles, des environnements dangereux et des moments de tension. Certaines scènes peuvent être intenses pour certains joueurs. Faites une pause dès que vous en ressentez le besoin et jouez à un rythme qui vous convient. Si vous jouez sur un appareil partagé, veillez à ce que l’expérience convienne également aux personnes présentes. Cet avertissement vous aide à prendre une décision éclairée avant de continuer." } },
            { "de", new[] { "WARNUNG", "AETHER ist ein Fantasy-Abenteuer mit Kämpfen, feindseligen Begegnungen, gefährlichen Umgebungen und angespannten Momenten. Einige Szenen können für manche Spieler belastend sein. Lege Pausen ein, wann immer du sie brauchst, und spiele in einem Tempo, das sich für dich gut anfühlt. Wenn du auf einem gemeinsam genutzten Gerät spielst, achte darauf, dass das Erlebnis auch für anwesende Personen geeignet ist. Dieser Hinweis soll dir helfen, vor dem Fortfahren eine informierte Entscheidung zu treffen." } },
            { "it", new[] { "AVVERTENZA", "AETHER è un’avventura fantasy che include combattimenti, incontri ostili, ambienti pericolosi e momenti di tensione. Alcune scene potrebbero risultare intense per alcuni giocatori. Fai una pausa quando ne senti il bisogno e gioca a un ritmo che ti faccia sentire a tuo agio. Se giochi su un dispositivo condiviso, considera anche le persone presenti e assicurati che l’esperienza sia adatta a loro. Questo avviso ti aiuta a scegliere con consapevolezza prima di continuare." } },
            { "pt", new[] { "AVISO", "AETHER é uma aventura de fantasia que inclui combates, encontros hostis, ambientes perigosos e momentos de tensão. Algumas cenas podem ser intensas para certos jogadores. Faça pausas sempre que precisar e jogue a um ritmo confortável para si. Se estiver a jogar num dispositivo partilhado, tenha em conta se a experiência é adequada para as pessoas presentes. Este aviso pretende ajudar a tomar uma decisão informada antes de continuar." } },
            { "pt-BR", new[] { "AVISO", "AETHER é uma aventura de fantasia que inclui combates, encontros hostis, ambientes perigosos e momentos de tensão. Algumas cenas podem ser intensas para certos jogadores. Faça pausas sempre que precisar e jogue em um ritmo confortável para você. Se estiver jogando em um dispositivo compartilhado, considere também as pessoas presentes e se a experiência é adequada para elas. Este aviso ajuda você a tomar uma decisão informada antes de continuar." } },
            { "ru", new[] { "ПРЕДУПРЕЖДЕНИЕ", "AETHER — это фэнтезийное приключение, в котором есть сражения, враждебные встречи, опасные места и напряжённые моменты. Некоторые сцены могут оказаться тяжёлыми для отдельных игроков. Делайте перерывы, когда они вам нужны, и играйте в комфортном для себя темпе. Если устройство общее, учитывайте, подходит ли игровой опыт всем присутствующим. Это предупреждение помогает принять осознанное решение перед продолжением." } },
            { "tr", new[] { "UYARI", "AETHER; çatışmalar, düşmanca karşılaşmalar, tehlikeli ortamlar ve gergin anlar içeren fantastik bir maceradır. Bazı sahneler kimi oyuncular için yoğun gelebilir. İhtiyaç duyduğunuzda ara verin ve kendinizi rahat hissettiğiniz bir tempoda oynayın. Paylaşılan bir cihazda oynuyorsanız deneyimin çevrenizdeki kişiler için uygun olup olmadığını da göz önünde bulundurun. Bu uyarı, devam etmeden önce bilinçli bir karar vermenize yardımcı olmak için gösterilir." } },
        };

        private Text _title;
        private Text _body;
        private CanvasGroup _visuals;
        private Button _invisibleContinueSurface;
        private Coroutine _entrance;
        private bool _leaving;

        protected override void Construct()
        {
            MenuUi.Fill("Advisory Pure Black Background", Rect, Black, true);

            // The full-screen hit area is deliberately invisible: the warning has no visible button,
            // timer, bar, or other decoration, but a tap anywhere lets the player continue.
            var hitArea = new GameObject("Invisible Continue Hit Area", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            hitArea.transform.SetParent(Rect, false);
            var hitRect = (RectTransform)hitArea.transform;
            hitRect.anchorMin = Vector2.zero;
            hitRect.anchorMax = Vector2.one;
            hitRect.offsetMin = Vector2.zero;
            hitRect.offsetMax = Vector2.zero;
            Image hitImage = hitArea.GetComponent<Image>();
            hitImage.color = new Color(1f, 1f, 1f, 0f);
            hitImage.raycastTarget = true;
            _invisibleContinueSurface = hitArea.GetComponent<Button>();
            _invisibleContinueSurface.transition = Selectable.Transition.None;
            _invisibleContinueSurface.onClick.AddListener(ContinueToMenu);

            _title = MenuUi.CreateText("Advisory Warning Heading", Rect, "هشدار",
                46f, White, TextAnchor.MiddleCenter, FontStyle.Bold);
            _body = MenuUi.CreateText("Advisory Warning Copy", Rect, "", 23f,
                White, TextAnchor.MiddleCenter);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.lineSpacing = 1.22f;
            _title.raycastTarget = false;
            _body.raycastTarget = false;

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
            _title.text = Prepare(copy[0], rtl);
            _body.text = Prepare(copy[1], rtl);
            _title.alignment = TextAnchor.MiddleCenter;
            _body.alignment = TextAnchor.MiddleCenter;
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
            float safeWidth = Mathf.Max(240f, width - 72f);
            bool compact = height < 600f;
            float titleSize = compact ? 40f : 46f;
            _title.fontSize = Mathf.RoundToInt(titleSize);
            _body.fontSize = compact ? 20 : 23;

            Place(_title.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, height * 0.27f), new Vector2(safeWidth, compact ? 66f : 78f));
            Place(_body.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -height * 0.045f),
                new Vector2(Mathf.Min(safeWidth, 1100f), height * (compact ? 0.62f : 0.60f)));
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        private IEnumerator FadeIn()
        {
            float elapsed = 0f;
            _visuals.alpha = 0f;
            while (elapsed < EntranceSeconds && !_leaving)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / EntranceSeconds);
                _visuals.alpha = t * t * (3f - 2f * t);
                yield return null;
            }

            _entrance = null;
            if (!_leaving && _visuals != null) _visuals.alpha = 1f;
        }

        private void ContinueToMenu()
        {
            if (_leaving || Host == null) return;
            _leaving = true;
            Host.GoTo(MenuScreenId.MainMenu);
        }

        private void Update()
        {
            // Keyboard users can continue with any key; touch users tap anywhere on the invisible
            // full-screen hit area. Neither path adds a visible control to the warning.
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                ContinueToMenu();
        }

        protected override void OnShown(bool instant)
        {
            _leaving = false;
            if (_entrance != null) StopCoroutine(_entrance);
            if (instant && _visuals != null)
            {
                _visuals.alpha = 1f;
                _entrance = null;
            }
            else
            {
                _entrance = StartCoroutine(FadeIn());
            }
        }

        protected override void OnHidden()
        {
            if (_entrance == null) return;
            StopCoroutine(_entrance);
            _entrance = null;
        }

        public override bool OnMove(MenuNav.Move move)
        {
            ContinueToMenu();
            return true;
        }

        public override bool OnBack()
        {
            ContinueToMenu();
            return true;
        }
    }
}