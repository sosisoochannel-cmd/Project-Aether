using System.Collections;
using System.Collections.Generic;
using Aether.Core.Localization;
using Aether.Gameplay.Localization;
using UnityEngine;
using UnityEngine.UI;

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
        private const float WarningDisplaySeconds = 7f;

        private static readonly Color Black = Color.black;
        private static readonly Color White = Color.white;

        private static readonly Dictionary<string, string[]> Copy = new Dictionary<string, string[]>
        {
            { "en", new[] { "WARNING", "Some people may experience epileptic seizures when exposed to flashing lights or visual patterns in video games, even if they have never had a seizure before. If you or anyone in your family has a history of epilepsy or seizures, consult a doctor before playing. Stop playing and seek medical advice if you experience dizziness, altered vision, muscle twitching, disorientation, or any unusual discomfort. Play in a well-lit room and take regular breaks." } },
            { "es", new[] { "ADVERTENCIA", "Algunas personas pueden sufrir crisis epilépticas al exponerse a luces intermitentes o patrones visuales de los videojuegos, incluso si nunca han tenido una crisis. Si tú o alguien de tu familia tiene antecedentes de epilepsia o convulsiones, consulta con un médico antes de jugar. Deja de jugar y busca consejo médico si notas mareos, alteraciones de la visión, espasmos musculares, desorientación o cualquier molestia inusual. Juega en una habitación bien iluminada y haz descansos con regularidad." } },
            { "fa", new[] { "هشدار", "برخی افراد ممکن است در اثر قرار گرفتن در معرض نورهای چشمک‌زن یا الگوهای تصویری بازی‌های ویدیویی دچار حملهٔ تشنج شوند؛ حتی اگر پیش از این هرگز تشنج نکرده باشند. اگر شما یا یکی از اعضای خانواده‌تان سابقهٔ صرع یا تشنج دارید، پیش از بازی با پزشک مشورت کنید. اگر هنگام بازی دچار سرگیجه، اختلال در بینایی، پرش عضلات، گیجی یا هرگونه ناراحتی غیرعادی شدید، بازی را متوقف کنید و از پزشک راهنمایی بگیرید. در محیطی با نور مناسب بازی کنید و مرتب استراحت کنید." } },
            { "fr", new[] { "AVERTISSEMENT", "Certaines personnes peuvent subir une crise d’épilepsie lorsqu’elles sont exposées aux lumières clignotantes ou aux motifs visuels des jeux vidéo, même si elles n’ont jamais eu de crise auparavant. Si vous ou un membre de votre famille avez des antécédents d’épilepsie ou de convulsions, consultez un médecin avant de jouer. Arrêtez de jouer et demandez un avis médical en cas de vertiges, de troubles de la vision, de contractions musculaires, de désorientation ou de malaise inhabituel. Jouez dans une pièce bien éclairée et faites régulièrement des pauses." } },
            { "de", new[] { "WARNUNG", "Bei manchen Menschen können blinkende Lichter oder visuelle Muster in Videospielen epileptische Anfälle auslösen, auch wenn zuvor noch nie ein Anfall aufgetreten ist. Wenn du oder jemand in deiner Familie an Epilepsie leidet oder schon Krampfanfälle hatte, sprich vor dem Spielen mit einem Arzt. Beende das Spiel und hole ärztlichen Rat ein, wenn Schwindel, Sehstörungen, Muskelzuckungen, Orientierungslosigkeit oder ungewöhnliches Unwohlsein auftreten. Spiele in einem gut beleuchteten Raum und lege regelmäßig Pausen ein." } },
            { "it", new[] { "AVVERTENZA", "Alcune persone possono avere crisi epilettiche se esposte a luci lampeggianti o schemi visivi nei videogiochi, anche se non hanno mai avuto crisi in precedenza. Se tu o un familiare avete una storia di epilessia o convulsioni, consulta un medico prima di giocare. Interrompi il gioco e chiedi consiglio medico se avverti capogiri, alterazioni della vista, contrazioni muscolari, disorientamento o qualsiasi malessere insolito. Gioca in una stanza ben illuminata e fai pause regolari." } },
            { "pt", new[] { "AVISO", "Algumas pessoas podem sofrer crises epilépticas quando expostas a luzes intermitentes ou padrões visuais em videojogos, mesmo que nunca tenham tido uma crise. Se você ou alguém da sua família tiver histórico de epilepsia ou convulsões, consulte um médico antes de jogar. Pare de jogar e procure orientação médica se sentir tonturas, alterações na visão, espasmos musculares, desorientação ou qualquer desconforto incomum. Jogue num espaço bem iluminado e faça pausas regularmente." } },
            { "pt-BR", new[] { "AVISO", "Algumas pessoas podem ter crises epilépticas quando expostas a luzes piscantes ou padrões visuais em videogames, mesmo que nunca tenham tido uma crise antes. Se você ou alguém da sua família tem histórico de epilepsia ou convulsões, consulte um médico antes de jogar. Pare de jogar e procure orientação médica se sentir tontura, alterações na visão, espasmos musculares, desorientação ou qualquer desconforto incomum. Jogue em um ambiente bem iluminado e faça pausas regularmente." } },
            { "ru", new[] { "ПРЕДУПРЕЖДЕНИЕ", "У некоторых людей мигающий свет или визуальные узоры в видеоиграх могут вызвать эпилептический приступ, даже если раньше приступов не было. Если у вас или членов вашей семьи есть эпилепсия или судорожные приступы в анамнезе, проконсультируйтесь с врачом перед игрой. Прекратите игру и обратитесь за медицинской помощью при головокружении, нарушении зрения, подёргивании мышц, дезориентации или необычном недомогании. Играйте в хорошо освещённой комнате и регулярно делайте перерывы." } },
            { "tr", new[] { "UYARI", "Bazı kişiler, daha önce hiç nöbet geçirmemiş olsalar bile video oyunlarındaki yanıp sönen ışıklara veya görsel desenlere maruz kaldıklarında epileptik nöbet geçirebilir. Sizde veya ailenizde epilepsi ya da nöbet öyküsü varsa oynamadan önce doktora danışın. Baş dönmesi, görme değişiklikleri, kas seğirmesi, yönelim bozukluğu veya olağan dışı bir rahatsızlık hissederseniz oyunu bırakın ve tıbbi yardım alın. İyi aydınlatılmış bir odada oynayın ve düzenli aralar verin." } },
        };;

        private Text _title;
        private Text _body;
        private CanvasGroup _visuals;
        private Coroutine _entrance;
        private Coroutine _autoContinue;
        private bool _leaving;

        protected override void Construct()
        {
            // Cover the complete canvas, including the safe-area margins and forest backdrop.
            Canvas parentCanvas = Rect.GetComponentInParent<Canvas>();
            Transform canvasRoot = parentCanvas != null ? parentCanvas.transform : Rect;
            Image background = MenuUi.Fill("Advisory Pure Black Background", canvasRoot, Black, false);
            background.transform.SetSiblingIndex(1);

            // No input surface: the warning advances automatically after seven seconds.

            _title = MenuUi.CreateText("Advisory Warning Heading", Rect, "هشدار",
                70f, White, TextAnchor.MiddleCenter, FontStyle.Bold);
            _body = MenuUi.CreateText("Advisory Warning Copy", Rect, "", 36f,
                White, TextAnchor.MiddleCenter, FontStyle.Bold);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.lineSpacing = 1.28f;
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
            float titleSize = width >= 1000f ? 72f : width >= 600f ? 62f : 50f;
            _title.fontSize = Mathf.RoundToInt(titleSize);
            // Legacy UI Text looks noticeably cleaner when the warning uses a generous, responsive
            // size rather than the small default menu label size. Keep a comfortable measure on phones.
            _body.fontSize = width >= 1000f ? 36 : width >= 600f ? 31 : 26;

            Place(_title.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, height * 0.36f), new Vector2(safeWidth, compact ? 66f : 78f));
            Place(_body.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -height * 0.12f),
                new Vector2(Mathf.Min(safeWidth, 1200f), height * (compact ? 0.66f : 0.64f)));
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

        private IEnumerator AutoContinueAfterDelay()
        {
            yield return new WaitForSecondsRealtime(WarningDisplaySeconds);
            _autoContinue = null;
            ContinueToMenu();
        }

        protected override void OnShown(bool instant)
        {
            _leaving = false;
            if (_entrance != null) StopCoroutine(_entrance);
            if (_autoContinue != null) StopCoroutine(_autoContinue);
            _autoContinue = StartCoroutine(AutoContinueAfterDelay());
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
            if (_entrance != null)
            {
                StopCoroutine(_entrance);
                _entrance = null;
            }

            if (_autoContinue != null)
            {
                StopCoroutine(_autoContinue);
                _autoContinue = null;
            }
        }
    }
}