using System.Collections;
using Aether.Gameplay.Localization;
using Aether.Gameplay.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// The game's first interactive screen: the world is already waiting underneath it, but the
    /// player chooses when to enter the main menu.
    /// </summary>
    /// <remarks>
    /// This is an overlay over the real menu, not another scene or a duplicate boot path. It reuses
    /// the menu's forest, studio mark, type and gold rules; one full-screen hit target means the
    /// whole screen is a comfortable touch target, including on landscape phones. The overlay stays
    /// in front while it fades so a start tap can never fall through and activate a menu row.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TitleStartScreen : MonoBehaviour
    {
        private const float FadeDuration = 0.24f;
        private const float EnterDuration = 0.68f;

        private CanvasGroup _group;
        private Button _hitTarget;
        private bool _leaving;
        private RectTransform _content;
        private RectTransform _rootRect;
        private CanvasGroup _contentGroup;
        private MenuLogo _logo;
        private Text _title;
        private Text _subtitle;
        private Image _rule;
        private Image _gem;
        private Text _prompt;
        private Vector2 _lastLayoutSize;
        private Vector3 _contentRestScale = Vector3.one;

        /// <summary>Builds the title gate over the menu, using the currently selected language.</summary>
        public static TitleStartScreen Create(Transform parent)
        {
            MenuArt.EnsureBuilt();

            RectTransform root = MenuUi.CreateNode("Title Start Screen", parent);
            MenuUi.Stretch(root);
            root.SetAsLastSibling();

            Image scrim = root.gameObject.AddComponent<Image>();
            scrim.sprite = MenuArt.Solid;
            scrim.color = new Color(0.010f, 0.014f, 0.025f, 0.54f);
            scrim.raycastTarget = true;

            var screen = root.gameObject.AddComponent<TitleStartScreen>();
            screen._group = root.gameObject.AddComponent<CanvasGroup>();

            screen._hitTarget = root.gameObject.AddComponent<Button>();
            screen._hitTarget.targetGraphic = scrim;
            screen._hitTarget.transition = Selectable.Transition.None;
            screen._hitTarget.navigation = new Navigation { mode = Navigation.Mode.None };
            screen._hitTarget.onClick.AddListener(screen.BeginStart);

            screen._rootRect = root;
            screen.BuildVisuals(root);
            screen.LayoutComposition(true);
            screen.StartCoroutine(screen.EnterCinematic());
            return screen;
        }

        private void BuildVisuals(RectTransform root)
        {
            bool rtl = LanguageService.IsRightToLeft;
            _content = MenuUi.CreateNode("Title Composition", root);
            MenuUi.Stretch(_content);
            _content.SetAsLastSibling();
            _contentGroup = _content.gameObject.AddComponent<CanvasGroup>();
            _contentRestScale = _content.localScale;

            _logo = MenuLogo.Create(_content, 118f);
            if (_logo != null && _logo.HasMark)
            {
                _logo.SetRightToLeft(rtl);
                RectTransform rect = _logo.Rect;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
            }

            _title = MenuUi.CreateTrackedText(
                "Game Title", _content, MenuStrings.Get("menu.title"), 86f,
                MenuTheme.Palette.Ink, 18f, TextAnchor.MiddleCenter);
            _title.fontStyle = FontStyle.Bold;
            _title.alignment = TextAnchor.MiddleCenter;
            _title.rectTransform.anchorMin = _title.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _title.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            _subtitle = MenuUi.CreateText(
                "Region Subtitle", _content, MenuStrings.Get("menu.subtitle"), 23f,
                MenuTheme.Palette.InkMuted, TextAnchor.MiddleCenter);
            _subtitle.alignment = TextAnchor.MiddleCenter;
            _subtitle.rectTransform.anchorMin = _subtitle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _subtitle.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            _rule = MenuUi.CreateImage("Gold Rule", _content, MenuArt.Solid, MenuTheme.Palette.Line);
            _rule.rectTransform.anchorMin = _rule.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rule.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            _gem = MenuUi.CreateImage("Rule Gem", _content, MenuArt.Solid, MenuTheme.Palette.Accent);
            _gem.rectTransform.anchorMin = _gem.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _gem.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _gem.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            _prompt = MenuUi.CreateTrackedText(
                "Touch To Start", _content, MenuStrings.Get("title.touchToStart"), 25f,
                MenuTheme.Palette.Accent, 2.5f, TextAnchor.MiddleCenter);
            _prompt.alignment = TextAnchor.MiddleCenter;
            _prompt.rectTransform.anchorMin = _prompt.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _prompt.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _prompt.horizontalOverflow = HorizontalWrapMode.Wrap;
            _prompt.verticalOverflow = VerticalWrapMode.Truncate;

            // Visuals never intercept the full-screen button; every decorative image and label is
            // non-raycastable, so any point on the display starts the game.
            _rule.raycastTarget = false;
            _gem.raycastTarget = false;

            // Reveal the title composition once; the full-screen root remains touchable throughout.
            _contentGroup.alpha = 0f;
            _content.localScale = _contentRestScale * 0.985f;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_rootRect != null && _rootRect.rect.size != _lastLayoutSize)
                LayoutComposition(false);
        }

        /// <summary>
        /// Keeps the title hierarchy inside the visible canvas when interface scale or device
        /// dimensions change. The layout is measured only when the rect changes, not every frame.
        /// </summary>
        private void LayoutComposition(bool force)
        {
            if (_rootRect == null || _title == null || _prompt == null) return;
            Rect bounds = _rootRect.rect;
            Vector2 size = bounds.size;
            if (!force && size == _lastLayoutSize) return;
            _lastLayoutSize = size;

            float height = Mathf.Max(1f, size.y);
            float width = Mathf.Max(1f, size.x);
            float fit = Mathf.Clamp(Mathf.Min((height - 72f) / 1008f, width / 1280f), 0.38f, 1f);
            float halfHeight = height * 0.5f;
            float sideInset = Mathf.Min(44f, width * 0.055f);

            if (_logo != null && _logo.HasMark)
            {
                _logo.SetHeight(118f * fit);
                _logo.Rect.anchoredPosition = new Vector2(0f, halfHeight * 0.35f);
            }

            _title.fontSize = Mathf.Max(30, Mathf.RoundToInt(86f * fit));
            _title.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, width - sideInset * 2f), 124f * fit);
            _title.rectTransform.anchoredPosition = new Vector2(0f, height * 0.039f);

            _subtitle.fontSize = Mathf.Max(14, Mathf.RoundToInt(23f * fit));
            _subtitle.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, width - sideInset * 2f), 52f * fit);
            _subtitle.rectTransform.anchoredPosition = new Vector2(0f, -height * 0.030f);

            _rule.rectTransform.sizeDelta = new Vector2(224f * fit, Mathf.Max(1f, 2f * fit));
            _rule.rectTransform.anchoredPosition = new Vector2(0f, -height * 0.077f);
            _gem.rectTransform.sizeDelta = new Vector2(9f * fit, 9f * fit);
            _gem.rectTransform.anchoredPosition = _rule.rectTransform.anchoredPosition;

            _prompt.fontSize = Mathf.Max(13, Mathf.RoundToInt(25f * fit));
            _prompt.rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, width - sideInset * 2f), 74f * fit);
            _prompt.rectTransform.anchoredPosition = new Vector2(0f, -halfHeight * 0.60f);
        }

        private IEnumerator EnterCinematic()
        {
            float elapsed = 0f;
            while (elapsed < EnterDuration && !_leaving)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / EnterDuration);
                float eased = t * t * (3f - 2f * t);
                if (_contentGroup != null) _contentGroup.alpha = eased;
                if (_content != null) _content.localScale = Vector3.Lerp(
                    _contentRestScale * 0.985f, _contentRestScale, eased);
                yield return null;
            }
            if (!_leaving)
            {
                if (_contentGroup != null) _contentGroup.alpha = 1f;
                if (_content != null) _content.localScale = _contentRestScale;
            }
        }

        private void BeginStart()
        {
            if (_leaving) return;
            _leaving = true;
            if (_hitTarget != null) _hitTarget.interactable = false;
            StartCoroutine(FadeAway());
        }

        private IEnumerator FadeAway()
        {
            float elapsed = 0f;
            while (elapsed < FadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _group.alpha = 1f - Mathf.Clamp01(elapsed / FadeDuration);
                yield return null;
            }

            _group.alpha = 0f;
            gameObject.SetActive(false);
        }
    }
}
