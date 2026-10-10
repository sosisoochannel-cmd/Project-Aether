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
    /// texture is the cropped logo only, and it is released with this component. No new artwork,
    /// shader, font or per-frame work is introduced.
    /// </para>
    /// </remarks>
    public sealed class MenuLogo : MonoBehaviour
    {
        private const string MarkResourcePath = "Brand/VarellonLogo";

        private static bool _missingReported;

        private RectTransform _rect;
        private Image _mark;
        private Image _shadow;
        private Image _halo;
        private Image _coreGlow;
        private Image _scan;
        private Texture2D _generatedTexture;
        private Sprite _generatedSprite;
        private Material _sweepMaterial;
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

            logo._shadow = MenuUi.CreateImage("Shadow", logo._rect, null, new Color(0f, 0f, 0f, 0.24f));
            logo._halo = MenuUi.CreateImage("Halo", logo._rect, MenuArt.Circle, new Color(1f, 1f, 1f, 0f));
            logo._coreGlow = MenuUi.CreateImage("Core Glow", logo._rect, MenuArt.Circle, new Color(1f, 1f, 1f, 0f));
            logo._scan = MenuUi.CreateImage("Light Sweep", logo._rect, MenuArt.Square, new Color(1f, 1f, 1f, 0f));
            logo._mark = MenuUi.CreateImage("Mark", logo._rect, null, MenuTheme.Palette.Ink);

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
            logo._scan.sprite = logo._generatedSprite;
            if (logo._sweepMaterial != null)
                logo._scan.material = logo._sweepMaterial;
            else
                logo._scan.enabled = false;
            logo.Apply();
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
            Place(_shadow, width, height, new Vector2(2f, -2f));
            Place(_halo, width * 1.18f, height * 1.18f, Vector2.zero);
            Place(_coreGlow, width * 1.04f, height * 1.04f, Vector2.zero);
            // The sweep is the same cropped ink mask as the mark, not a free-standing bar.
            // That keeps every highlight pixel inside the actual artwork at every logo aspect ratio.
            Place(_scan, width, height, Vector2.zero);
            _halo.transform.SetAsFirstSibling();
            _coreGlow.transform.SetSiblingIndex(1);
            _shadow.transform.SetSiblingIndex(2);
            _mark.transform.SetSiblingIndex(3);
            _scan.transform.SetAsLastSibling();
            _shadow.color = new Color(0f, 0f, 0f, 0.24f);
            _mark.color = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Ink,
                                                         MenuPreferences.HighContrast);
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
            BuildSweepMaterial();
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

        private void OnEnable()
        {
            if (_mark == null) return;

            if (_presentation != null) StopCoroutine(_presentation);
            _presentation = StartCoroutine(PresentRoutine());
        }

        private void OnDisable()
        {
            if (_presentation != null)
            {
                StopCoroutine(_presentation);
                _presentation = null;
            }
        }

        private IEnumerator PresentRoutine()
        {
            _rect.anchoredPosition = _restPosition + new Vector2(0f, 14f);
            _rect.localScale = Vector3.one * 0.94f;

            Color markTarget = _mark.color;
            Color shadowTarget = _shadow != null ? _shadow.color : new Color(0f, 0f, 0f, 0f);
            Color accent = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Accent, MenuPreferences.HighContrast);

            if (MenuPreferences.ReducedMotion)
            {
                _rect.anchoredPosition = _restPosition;
                _rect.localScale = Vector3.one;
                SetLogoEffectColors(markTarget, shadowTarget, 0f, 0f, 0f);
                yield break;
            }

            SetLogoEffectColors(
                new Color(markTarget.r, markTarget.g, markTarget.b, 0f),
                new Color(shadowTarget.r, shadowTarget.g, shadowTarget.b, 0f), 0f, 0f, 0f);

            float elapsed = 0f;
            const float duration = 0.58f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                _rect.anchoredPosition = Vector2.LerpUnclamped(
                    _restPosition + new Vector2(0f, 14f), _restPosition, ease);
                _rect.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, ease);
                float glow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.72f));
                SetLogoEffectColors(
                    Color.Lerp(new Color(markTarget.r, markTarget.g, markTarget.b, 0f), markTarget, ease),
                    Color.Lerp(new Color(shadowTarget.r, shadowTarget.g, shadowTarget.b, 0f), shadowTarget, ease),
                    glow * 0.11f, glow * 0.16f, 0f);
                yield return null;
            }

            // A single restrained, ink-masked specular pass: it travels through the logo's own
            // alpha instead of drawing a rectangular streak across the surrounding UI.
            const float sweepDuration = 0.86f;
            elapsed = 0f;
            if (_sweepMaterial != null) _sweepMaterial.SetFloat("_SweepOpacity", 0f);
            while (elapsed < sweepDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / sweepDuration);
                if (_sweepMaterial != null)
                {
                    _sweepMaterial.SetFloat("_SweepProgress", t);
                    _sweepMaterial.SetFloat("_SweepOpacity", Mathf.Sin(t * Mathf.PI) * 0.78f);
                }
                if (_scan != null)
                    _scan.color = new Color(accent.r, accent.g, accent.b, 0.42f);
                yield return null;
            }

            if (_sweepMaterial != null) _sweepMaterial.SetFloat("_SweepOpacity", 0f);
            if (_scan != null) _scan.color = new Color(accent.r, accent.g, accent.b, 0f);

            elapsed = 0f;
            while (isActiveAndEnabled && !MenuPreferences.ReducedMotion)
            {
                elapsed += Time.unscaledDeltaTime;
                float breathe = Mathf.Sin(elapsed * Mathf.PI * 2f / 11f);
                _rect.anchoredPosition = _restPosition + new Vector2(0f, breathe * 1.15f);
                _rect.localScale = Vector3.one * (1f + breathe * 0.0015f);

                float pulse = 0.5f + 0.5f * breathe;
                if (_halo != null)
                    _halo.color = new Color(accent.r, accent.g, accent.b, 0.025f + pulse * 0.025f);
                if (_coreGlow != null)
                    _coreGlow.color = new Color(accent.r, accent.g, accent.b, 0.045f + pulse * 0.025f);
                yield return null;
            }

            _rect.anchoredPosition = _restPosition;
            _rect.localScale = Vector3.one;
            SetLogoEffectColors(markTarget, shadowTarget, 0.03f, 0.06f, 0f);
            _presentation = null;
        }

        /// <summary>
        /// Creates one private instance of the UI shader. The logo's coverage stays in the original
        /// sprite alpha; the GPU moves a single soft highlight across that mask, without uploading
        /// a new texture or recalculating every pixel on the CPU each frame.
        /// </summary>
        private void BuildSweepMaterial()
        {
            Shader shader = Resources.Load<Shader>("Brand/VarellonLogoSheen");
            if (shader == null)
            {
                Debug.LogWarning(
                    "[menu] VarellonLogoSheen shader is unavailable; the logo will render without its sweep.",
                    this);
                return;
            }

            _sweepMaterial = new Material(shader)
            {
                name = "Varellon Logo Sheen (runtime)",
                hideFlags = HideFlags.HideAndDontSave,
            };
            _sweepMaterial.SetFloat("_SweepProgress", 0f);
            _sweepMaterial.SetFloat("_SweepOpacity", 0f);
            _sweepMaterial.SetFloat("_BandHalfWidth", 0.12f);
        }

        private void SetLogoEffectColors(Color mark, Color shadow, float haloAlpha, float coreAlpha, float scanAlpha)
        {
            _mark.color = mark;
            if (_shadow != null) _shadow.color = shadow;

            Color accent = MenuTheme.Palette.WithContrast(MenuTheme.Palette.Accent, MenuPreferences.HighContrast);
            if (_halo != null) _halo.color = new Color(accent.r, accent.g, accent.b, haloAlpha);
            if (_coreGlow != null) _coreGlow.color = new Color(accent.r, accent.g, accent.b, coreAlpha);
            if (_scan != null) _scan.color = new Color(accent.r, accent.g, accent.b, scanAlpha);
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
            if (_halo != null) Destroy(_halo.gameObject);
            if (_coreGlow != null) Destroy(_coreGlow.gameObject);
            if (_scan != null) Destroy(_scan.gameObject);
            if (_mark != null) Destroy(_mark.gameObject);
            _shadow = null;
            _halo = null;
            _coreGlow = null;
            _scan = null;
            _mark = null;
        }

        private void OnDestroy()
        {
            if (_generatedSprite != null) Destroy(_generatedSprite);
            if (_generatedTexture != null) Destroy(_generatedTexture);
            if (_sweepMaterial != null) Destroy(_sweepMaterial);
            _generatedSprite = null;
            _generatedTexture = null;
            _sweepMaterial = null;
        }
    }
}
