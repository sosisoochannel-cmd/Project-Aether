using System.Collections;
using Aether.Gameplay.Flow;
using Aether.Gameplay.Menus;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Presentation
{
    /// <summary>
    /// The studio's existing mark, keyed to its ink, trimmed and placed in the menu's brand block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source PNG is the approved Varellon lockup, but it is an RGB export with a pale
    /// checkerboard rather than transparency. Drawing that file as-is puts the checkerboard on the
    /// menu; multiplying its black ink by a white UI tint does not turn the ink white. The studio
    /// intro already solves both parts of this: it keys the canvas by luminance, then colours the
    /// resulting coverage for its dark screen. The menu uses those same luminance limits and keeps
    /// the full lockup, cropped to its actual ink, so only the existing logo is shown here too.
    /// </para>
    /// <para>
    /// Processing happens once when the menu is built. The source remains untouched, the generated
    /// texture is the cropped logo only, and it is released with this component. A separate UI shader
    /// draws one finite metallic glint over the same alpha mask after the entrance; its runtime
    /// material is released with the component, and Reduced Motion skips the effect.
    /// </para>
    /// </remarks>
    public sealed class MenuLogo : MonoBehaviour
    {
        private const string MarkResourcePath = "Brand/VarellonLogo";
        private const string GlintShaderPath = "Brand/VarellonLogoSheen";

        private static bool _missingReported;

        private RectTransform _rect;
        private Image _mark;
        private Image _shadow;
        private Image _glint;
        private Material _glintMaterial;
        private Texture2D _generatedTexture;
        private Sprite _generatedSprite;
        private float _height = MenuTheme.Metrics.LogoHeight;
        private Vector2 _restPosition;
        private Coroutine _presentation;

        /// <summary>Whether a mark is actually being drawn.</summary>
        public bool HasMark
        {
            get { return _mark != null && _mark.sprite != null; }
        }

        /// <summary>The block's rect, for the screen that places it.</summary>
        public RectTransform Rect
        {
            get { return _rect; }
        }

        /// <summary>Builds the mark inside a screen's title block.</summary>
        public static MenuLogo Create(Transform parent, float height)
        {
            MenuArt.EnsureBuilt();

            var host = new GameObject("Brand Mark", typeof(RectTransform));
            host.transform.SetParent(parent, false);

            var logo = host.AddComponent<MenuLogo>();
            logo._rect = (RectTransform)host.transform;
            logo._height = height;
            logo._rect.anchorMin = new Vector2(0f, 1f);
            logo._rect.anchorMax = new Vector2(0f, 1f);
            logo._rect.pivot = new Vector2(0f, 1f);
            logo._rect.anchoredPosition = Vector2.zero;
            logo._rect.sizeDelta = new Vector2(MenuTheme.Metrics.LogoMaxWidth, height);

            logo._shadow = MenuUi.CreateImage("Shadow", logo._rect, null, new Color(0f, 0f, 0f, 0.18f));
            logo._mark = MenuUi.CreateImage("Mark", logo._rect, null, MenuTheme.Palette.Ink);
            logo._glint = MenuUi.CreateImage("One-time Glint", logo._rect, null, Color.white);
            logo._glint.raycastTarget = false;
            logo._glint.gameObject.SetActive(false);

            Sprite source = Resources.Load<Sprite>(MarkResourcePath);
            if (source == null)
            {
                logo.ReportUnavailable("the resource could not be loaded");
                logo.Remove();
                return logo;
            }

            if (!logo.BuildKeyedMark(source))
            {
                logo.ReportUnavailable("the image was unreadable or contained no usable ink");
                logo.Remove();
                return logo;
            }

            logo._mark.sprite = logo._generatedSprite;
            logo._shadow.sprite = logo._generatedSprite;
            logo._glint.sprite = logo._generatedSprite;
            logo.BuildGlintMaterial();
            logo.Apply();
            // AddComponent invokes OnEnable before the generated sprite exists on an active host.
            // Start explicitly after the keyed mark is ready so the entrance animation is not lost.
            logo.BeginPresentation();
            return logo;
        }

        /// <summary>Sizes the trimmed mark to its slot, preserving the artwork's aspect ratio.</summary>
        public void Apply()
        {
            if (_mark == null || _mark.sprite == null) return;

            Rect source = _mark.sprite.rect;
            if (source.height <= 0f) return;

            float aspect = source.width / source.height;
            float width = Mathf.Min(_height * aspect, MenuTheme.Metrics.LogoMaxWidth);
            float height = width / aspect;

            _rect.sizeDelta = new Vector2(MenuTheme.Metrics.LogoMaxWidth, height);
            _restPosition = _rect.anchoredPosition;

            Place(_mark, width, height, Vector2.zero);
            Place(_shadow, width, height, new Vector2(1f, -1f));
            Place(_glint, width, height, Vector2.zero);
            _shadow.transform.SetAsFirstSibling();
            _mark.transform.SetAsLastSibling();
            if (_glint != null) _glint.transform.SetAsLastSibling();
            _shadow.color = new Color(0f, 0f, 0f, 0.18f);
            _mark.color = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Ink,
                                                         MenuPreferences.HighContrast);
            if (_glint != null) _glint.color = Color.white;
        }

        private bool BuildKeyedMark(Sprite source)
        {
            Texture2D texture = source.texture;
            if (texture == null || !texture.isReadable) return false;

            Rect rect = source.textureRect;
            int sourceX = Mathf.Clamp(Mathf.RoundToInt(rect.x), 0, texture.width);
            int sourceY = Mathf.Clamp(Mathf.RoundToInt(rect.y), 0, texture.height);
            int sourceWidth = Mathf.Clamp(Mathf.RoundToInt(rect.width), 0, texture.width - sourceX);
            int sourceHeight = Mathf.Clamp(Mathf.RoundToInt(rect.height), 0, texture.height - sourceY);
            if (sourceWidth < 2 || sourceHeight < 2) return false;

            Color32[] sourcePixels;
            try
            {
                sourcePixels = texture.GetPixels32();
            }
            catch (UnityException)
            {
                return false;
            }

            bool hasTransparency = false;
            for (int y = 0; y < sourceHeight && !hasTransparency; y++)
            {
                int row = ((sourceY + y) * texture.width) + sourceX;
                for (int x = 0; x < sourceWidth; x++)
                {
                    if (sourcePixels[row + x].a >= 250) continue;
                    hasTransparency = true;
                    break;
                }
            }

            int minX = sourceWidth;
            int minY = sourceHeight;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < sourceHeight; y++)
            {
                int row = ((sourceY + y) * texture.width) + sourceX;
                for (int x = 0; x < sourceWidth; x++)
                {
                    if (Coverage(sourcePixels[row + x], hasTransparency) == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY) return false;

            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                int row = ((sourceY + minY + y) * texture.width) + sourceX + minX;
                for (int x = 0; x < width; x++)
                {
                    byte coverage = Coverage(sourcePixels[row + x], hasTransparency);
                    pixels[(y * width) + x] = new Color32(255, 255, 255, coverage);
                }
            }

            _generatedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = source.name + " (menu cutout)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _generatedTexture.SetPixels32(pixels);
            _generatedTexture.Apply(false, true);

            float pixelsPerUnit = source.pixelsPerUnit > 0f ? source.pixelsPerUnit : 100f;
            _generatedSprite = Sprite.Create(_generatedTexture, new Rect(0f, 0f, width, height),
                                             new Vector2(0.5f, 0.5f), pixelsPerUnit);
            if (_generatedSprite == null)
            {
                Destroy(_generatedTexture);
                _generatedTexture = null;
                return false;
            }

            _generatedSprite.name = source.name + " (menu cutout)";
            _generatedSprite.hideFlags = HideFlags.HideAndDontSave;
            return true;
        }

        private static byte Coverage(Color32 pixel, bool fromAlpha)
        {
            if (fromAlpha) return pixel.a;

            float luminance = (0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b) / 255f;
            float ink = (StudioIntroSequence.Artwork.BackgroundLuminance - luminance)
                       / (StudioIntroSequence.Artwork.BackgroundLuminance
                          - StudioIntroSequence.Artwork.InkLuminance);
            return (byte)(Mathf.Clamp01(ink) * 255f);
        }

        private void BuildGlintMaterial()
        {
            if (_glint == null) return;

            Shader shader = Resources.Load<Shader>(GlintShaderPath);
            if (shader == null)
            {
                Debug.LogWarning($"[menu] Resources/{GlintShaderPath} was not found; the logo will appear without its one-time glint.", this);
                return;
            }

            _glintMaterial = new Material(shader)
            {
                name = "Varellon menu one-time glint",
                hideFlags = HideFlags.HideAndDontSave,
            };
            _glintMaterial.SetFloat("_SweepProgress", 0f);
            _glintMaterial.SetFloat("_SweepOpacity", 0f);
            _glintMaterial.SetFloat("_BandHalfWidth", 0.095f);
            _glint.material = _glintMaterial;
        }

        private void OnEnable()
        {
            BeginPresentation();
        }

        private void BeginPresentation()
        {
            if (!isActiveAndEnabled || _mark == null || _mark.sprite == null) return;

            if (_presentation != null)
            {
                StopCoroutine(_presentation);
                _presentation = null;
            }
            _presentation = StartCoroutine(PresentRoutine());
        }

        private void OnDisable()
        {
            if (_presentation != null)
            {
                StopCoroutine(_presentation);
                _presentation = null;
            }

            if (_glintMaterial != null) _glintMaterial.SetFloat("_SweepOpacity", 0f);
            if (_glint != null) _glint.gameObject.SetActive(false);
        }

        /// <summary>
        /// A short fade-and-rise entrance followed by one finite metallic glint. The logo itself
        /// stays still; there is no looping glow, pulse, breathing, or idle motion.
        /// </summary>
        private IEnumerator PresentRoutine()
        {
            if (_glintMaterial != null) _glintMaterial.SetFloat("_SweepOpacity", 0f);
            if (_glint != null) _glint.gameObject.SetActive(false);

            Color markTarget = _mark.color;
            Color shadowTarget = _shadow != null ? _shadow.color : new Color(0f, 0f, 0f, 0f);

            if (MenuPreferences.ReducedMotion)
            {
                _rect.anchoredPosition = _restPosition;
                _rect.localScale = Vector3.one;
                _mark.color = markTarget;
                if (_shadow != null) _shadow.color = shadowTarget;
                _presentation = null;
                yield break;
            }

            const float duration = 0.42f;
            const float rise = 6f;
            const float startScale = 0.985f;
            _rect.anchoredPosition = _restPosition + new Vector2(0f, rise);
            _rect.localScale = Vector3.one * startScale;
            _mark.color = new Color(markTarget.r, markTarget.g, markTarget.b, 0f);
            if (_shadow != null)
                _shadow.color = new Color(shadowTarget.r, shadowTarget.g, shadowTarget.b, 0f);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = 1f - Mathf.Pow(1f - t, 3f);

                _rect.anchoredPosition = Vector2.LerpUnclamped(
                    _restPosition + new Vector2(0f, rise), _restPosition, ease);
                _rect.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, ease);
                _mark.color = new Color(markTarget.r, markTarget.g, markTarget.b,
                                        Mathf.Lerp(0f, markTarget.a, ease));
                if (_shadow != null)
                    _shadow.color = new Color(shadowTarget.r, shadowTarget.g, shadowTarget.b,
                                              Mathf.Lerp(0f, shadowTarget.a, ease));
                yield return null;
            }

            _rect.anchoredPosition = _restPosition;
            _rect.localScale = Vector3.one;
            _mark.color = markTarget;
            if (_shadow != null) _shadow.color = shadowTarget;

            if (_glintMaterial != null && _glint != null && !MenuPreferences.ReducedMotion)
            {
                // One deliberate highlight: a short anticipation, a longer diagonal pass,
                // a tiny metallic linger, then a clean return to the untouched logo.
                _glint.gameObject.SetActive(true);
                _glintMaterial.SetFloat("_SweepProgress", 0f);
                _glintMaterial.SetFloat("_SweepOpacity", 0f);

                const float anticipation = 0.14f;
                const float sweepDuration = 0.92f;
                const float lingerDuration = 0.16f;
                const float fadeDuration = 0.34f;
                float wait = 0f;
                while (wait < anticipation)
                {
                    wait += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(wait / anticipation);
                    _glintMaterial.SetFloat("_SweepOpacity", 0.12f * t);
                    yield return null;
                }

                float elapsed = 0f;
                while (elapsed < sweepDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / sweepDuration);
                    float eased = 1f - Mathf.Pow(1f - t, 2.2f);
                    _glintMaterial.SetFloat("_SweepProgress", eased);
                    float envelope = Mathf.Sin(t * Mathf.PI);
                    _glintMaterial.SetFloat("_SweepOpacity", Mathf.Lerp(0.30f, 0.98f, envelope));
                    yield return null;
                }

                _glintMaterial.SetFloat("_SweepProgress", 1f);
                _glintMaterial.SetFloat("_SweepOpacity", 0.34f);
                elapsed = 0f;
                while (elapsed < lingerDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / lingerDuration);
                    _glintMaterial.SetFloat("_SweepOpacity", Mathf.Lerp(0.34f, 0.24f, t));
                    yield return null;
                }

                elapsed = 0f;
                while (elapsed < fadeDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / fadeDuration);
                    _glintMaterial.SetFloat("_SweepOpacity", Mathf.Lerp(0.24f, 0f, t));
                    yield return null;
                }

                _glintMaterial.SetFloat("_SweepOpacity", 0f);
                _glint.gameObject.SetActive(false);
            }

            _presentation = null;
        }

        private void ReportUnavailable(string reason)
        {
            if (_missingReported) return;
            _missingReported = true;
            Debug.LogWarning(
                $"[menu] Resources/{MarkResourcePath} could not be drawn because {reason}; " +
                "the title remains in place without a substitute mark.", this);
        }

        private static void Place(Image image, float width, float height, Vector2 offset)
        {
            if (image == null) return;

            image.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            image.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            image.rectTransform.pivot = new Vector2(0f, 0.5f);
            image.rectTransform.sizeDelta = new Vector2(width, height);
            image.rectTransform.anchoredPosition = offset;
        }

        private void Remove()
        {
            if (_shadow != null) Destroy(_shadow.gameObject);
            if (_mark != null) Destroy(_mark.gameObject);
            _shadow = null;
            _mark = null;
            if (_glint != null) Destroy(_glint.gameObject);
            _glint = null;
        }

        private void OnDestroy()
        {
            if (_glintMaterial != null) Destroy(_glintMaterial);
            if (_generatedSprite != null) Destroy(_generatedSprite);
            if (_generatedTexture != null) Destroy(_generatedTexture);
            _generatedSprite = null;
            _generatedTexture = null;
        }
    }
}
