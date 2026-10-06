using Aether.Gameplay.Menus;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Presentation
{
    /// <summary>
    /// The studio's approved mark, at the top of the menu, with depth and nothing invented.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the existing artwork or it is nothing.</b> The mark is loaded from
    /// <c>Resources/Brand/VarellonLogo</c> — the same file the studio intro draws — and the only
    /// things done to it are scale, placement and a soft shadow behind it. It is never redrawn,
    /// recoloured outside its own alpha or replaced with type: the brief for this menu says the
    /// approved logo is the logo, and a menu that invents one is worse than a menu with none.
    /// </para>
    /// <para>
    /// If the file is missing, the mark removes itself and says so once. That is the honest
    /// behaviour for a build without its artwork: the layout closes up around the gap instead of
    /// showing a placeholder rectangle.
    /// </para>
    /// <para>
    /// <b>Depth without a shader.</b> One soft glow behind the mark and one offset shadow at very
    /// low opacity — both drawn from sprites generated in memory — lift it off the backdrop. Anything
    /// more (bloom, an outline, a second copy in the accent colour) would be the cheap-glow look the
    /// brief rules out.
    /// </para>
    /// </remarks>
    public sealed class MenuLogo : MonoBehaviour
    {
        private const string MarkResourcePath = "Brand/VarellonLogo";

        private static bool _missingReported;

        private RectTransform _rect;
        private Image _mark;
        private Image _shadow;
        private Image _glow;
        private float _height = MenuTheme.Metrics.LogoHeight;

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

            // Behind the mark: a cold bloom, then a shadow just off its lower right. The order
            // matters — the glow is furthest back, the mark is last and therefore drawn on top.
            logo._glow = MenuUi.CreateImage("Depth", logo._rect, MenuArt.Glow,
                                           new Color(MenuTheme.Palette.Atmosphere.r,
                                                     MenuTheme.Palette.Atmosphere.g,
                                                     MenuTheme.Palette.Atmosphere.b, 0.5f));
            logo._glow.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            logo._glow.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            logo._glow.rectTransform.pivot = new Vector2(0f, 0.5f);
            logo._glow.rectTransform.sizeDelta = new Vector2(height * 2.4f, height * 2.4f);
            logo._glow.rectTransform.anchoredPosition = new Vector2(0f, 0f);

            logo._shadow = MenuUi.CreateImage("Shadow", logo._rect, null, new Color(0f, 0f, 0f, 0.35f));
            logo._mark = MenuUi.CreateImage("Mark", logo._rect, null, Color.white);

            Sprite mark = Resources.Load<Sprite>(MarkResourcePath);
            if (mark == null)
            {
                logo.Remove();
                if (!_missingReported)
                {
                    _missingReported = true;
                    Debug.LogWarning(
                        $"[menu] Resources/{MarkResourcePath} could not be loaded, so the menu shows " +
                        "its title without the studio mark. The approved mark is required artwork; " +
                        "the menu does not substitute one for it.", logo);
                }

                return logo;
            }

            logo._mark.sprite = mark;
            logo._shadow.sprite = mark;
            logo.Apply();
            return logo;
        }

        /// <summary>Sizes the mark to its slot, preserving the artwork's aspect ratio.</summary>
        public void Apply()
        {
            if (_mark == null || _mark.sprite == null) return;

            Rect source = _mark.sprite.rect;
            if (source.height <= 0f) return;

            float aspect = source.width / source.height;
            float width = Mathf.Min(_height * aspect, MenuTheme.Metrics.LogoMaxWidth);
            float height = width / aspect;

            _rect.sizeDelta = new Vector2(MenuTheme.Metrics.LogoMaxWidth, height);

            Place(_mark, width, height, Vector2.zero);
            Place(_shadow, width, height, new Vector2(3f, -3f));

            // The shadow is the same mark, three units down and to the right, at low opacity.
            // Enough to separate it from the backdrop; not enough to be seen as a second copy.
            _shadow.color = new Color(0f, 0f, 0f, 0.4f);

            _mark.color = MenuTheme.Palette.WithContrast(Color.white, MenuPreferences.HighContrast);
            if (_glow != null)
            {
                _glow.rectTransform.sizeDelta = new Vector2(width * 1.6f, height * 1.6f);
                _glow.enabled = !MenuPreferences.ReducedMotion;
            }
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
            if (_glow != null) Destroy(_glow.gameObject);
            if (_shadow != null) Destroy(_shadow.gameObject);
            if (_mark != null) Destroy(_mark.gameObject);
            _glow = null;
            _shadow = null;
            _mark = null;
        }
    }
}
