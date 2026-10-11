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

        private CanvasGroup _group;
        private Button _hitTarget;
        private bool _leaving;

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

            screen.BuildVisuals(root);
            return screen;
        }

        private void BuildVisuals(RectTransform root)
        {
            bool rtl = LanguageService.IsRightToLeft;
            MenuLogo logo = MenuLogo.Create(root, 118f);
            if (logo != null && logo.HasMark)
            {
                logo.SetRightToLeft(rtl);
                RectTransform rect = logo.Rect;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, 190f);
            }

            Text title = MenuUi.CreateTrackedText(
                "Game Title", root, MenuStrings.Get("menu.title"), 86f,
                MenuTheme.Palette.Ink, 18f, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            title.alignment = TextAnchor.MiddleCenter;
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            title.rectTransform.sizeDelta = new Vector2(1120f, 124f);
            title.rectTransform.anchoredPosition = new Vector2(0f, 42f);

            Text subtitle = MenuUi.CreateText(
                "Region Subtitle", root, MenuStrings.Get("menu.subtitle"), 23f,
                MenuTheme.Palette.InkMuted, TextAnchor.MiddleCenter);
            subtitle.alignment = TextAnchor.MiddleCenter;
            subtitle.rectTransform.anchorMin = subtitle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            subtitle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            subtitle.rectTransform.sizeDelta = new Vector2(1180f, 52f);
            subtitle.rectTransform.anchoredPosition = new Vector2(0f, -32f);

            Image rule = MenuUi.CreateImage("Gold Rule", root, MenuArt.Solid, MenuTheme.Palette.Line);
            rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rule.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rule.rectTransform.sizeDelta = new Vector2(224f, 2f);
            rule.rectTransform.anchoredPosition = new Vector2(0f, -83f);

            Image gem = MenuUi.CreateImage("Rule Gem", root, MenuArt.Solid, MenuTheme.Palette.Accent);
            gem.rectTransform.anchorMin = gem.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            gem.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            gem.rectTransform.sizeDelta = new Vector2(9f, 9f);
            gem.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            gem.rectTransform.anchoredPosition = new Vector2(0f, -83f);

            Text prompt = MenuUi.CreateTrackedText(
                "Touch To Start", root, MenuStrings.Get("title.touchToStart"), 25f,
                MenuTheme.Palette.Accent, 2.5f, TextAnchor.MiddleCenter);
            prompt.alignment = TextAnchor.MiddleCenter;
            prompt.rectTransform.anchorMin = prompt.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            prompt.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            prompt.rectTransform.sizeDelta = new Vector2(1500f, 74f);
            prompt.rectTransform.anchoredPosition = new Vector2(0f, -322f);

            // Visuals never intercept the full-screen button; every decorative image and label is
            // non-raycastable, so any point on the display starts the game.
            rule.raycastTarget = false;
            gem.raycastTarget = false;
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
