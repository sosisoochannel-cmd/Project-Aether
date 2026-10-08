using UnityEngine;
using UnityEngine.UI;
using Aether.Gameplay.Localization;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// Builds the interface's parts. One place that knows how a text or a hairline is made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The menu is assembled in code rather than authored in a scene, for the same reason the level
    /// is a text file: a scene cannot be reviewed, diffed or checked by a tool, and nobody in this
    /// project has an Editor to open one in. Every builder here is deliberately small and generic —
    /// the composition lives in the panels, and the look lives in <see cref="MenuTheme"/>.
    /// </para>
    /// <para>
    /// <b>No layout groups.</b> Rows are placed by arithmetic, not by <c>VerticalLayoutGroup</c>,
    /// <c>ContentSizeFitter</c> or <c>LayoutElement</c>. Layout components re-solve the whole
    /// hierarchy whenever anything in it changes, which is exactly the wrong behaviour for a list
    /// that animates as it appears and a screen that scrolls; placing a known number of known rows
    /// is both cheaper and easier to reason about. It is also what lets the gate compute the layout
    /// from the same numbers the code uses.
    /// </para>
    /// </remarks>
    public static class MenuUi
    {
        /// <summary>A hair space, used to open up capitals without a font that has tracking.</summary>
        private const char HairSpace = '\u200A';

        /// <summary>
        /// An empty node, for grouping.
        /// </summary>
        public static RectTransform CreateNode(string name, Transform parent)
        {
            var host = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>An image. Unraycastable unless asked, because most of them are decoration.</summary>
        public static Image CreateImage(string name, Transform parent, Sprite sprite, Color colour,
                                        bool raycastTarget = false)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var image = host.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = raycastTarget;
            image.type = Image.Type.Simple;
            return image;
        }

        /// <summary>A one-unit-tall rule.</summary>
        public static Image CreateHairline(string name, Transform parent, Color colour, float height = 2f)
        {
            Image line = CreateImage(name, parent, MenuArt.Solid, colour);
            line.rectTransform.sizeDelta = new Vector2(0f, height);
            return line;
        }

        /// <summary>A label.</summary>
        public static Text CreateText(string name, Transform parent, string content, float size,
                                     Color colour, TextAnchor alignment = TextAnchor.MiddleLeft,
                                     FontStyle style = FontStyle.Normal)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var text = host.AddComponent<Text>();
            text.font = MenuArt.Font;
            bool rtl = LanguageService.IsRightToLeft;
            text.text = PrepareText(content);
            if (rtl) alignment = TextAnchor.MiddleRight;
            text.fontSize = Mathf.RoundToInt(size);
            text.fontStyle = style;
            text.color = colour;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            return text;
        }

        /// <summary>
        /// A label with its capitals opened up.
        /// </summary>
        /// <remarks>
        /// The engine's built-in font has no letter-spacing setting, and tracking is most of what
        /// makes a title look designed rather than typed. Hair spaces between the letters give the
        /// same effect with no font asset: they are three characters wide in the font's own metrics,
        /// so the spacing scales with the text instead of being a constant pixel offset that looks
        /// wrong at another size.
        /// </remarks>
        public static Text CreateTrackedText(string name, Transform parent, string content, float size,
                                            Color colour, float tracking = 1f,
                                            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            return CreateText(name, parent, Track(content, tracking), size, colour, alignment);
        }

        /// <summary>Tracked label overload that also preserves an explicit font style.</summary>
        public static Text CreateTrackedText(string name, Transform parent, string content, float size,
                                            Color colour, float tracking, TextAnchor alignment,
                                            FontStyle style)
        {
            return CreateText(name, parent, Track(content, tracking), size, colour, alignment, style);
        }

        /// <summary>Opens a string up with hair spaces, leaving punctuation and spaces alone.</summary>
        public static string Track(string content, float spacing)
        {
            if (string.IsNullOrEmpty(content) || spacing <= 0 || LanguageService.IsRightToLeft) return content;

            int gap = Mathf.RoundToInt(spacing);
            if (gap <= 0) return content;

            var builder = new System.Text.StringBuilder(content.Length + (content.Length * gap));
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                builder.Append(c);
                if (i == content.Length - 1) break;
                if (c == ' ' || content[i + 1] == ' ') continue;
                for (int s = 0; s < gap; s++) builder.Append(HairSpace);
            }

            return builder.ToString();
        }

        /// <summary>Prepares a string for the current script, including Persian/Arabic shaping.</summary>
        public static string PrepareText(string content)
        {
            return LanguageService.IsRightToLeft ? RtlText.Visualize(content) : content;
        }

        /// <summary>Stretches a node to its parent, with insets in reference units.</summary>
        public static void Stretch(RectTransform rect, float left = 0f, float right = 0f,
                                   float top = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>
        /// Places a node against an anchor, with an explicit size.
        /// </summary>
        /// <param name="anchor">Where on the parent the node hangs, as fractions.</param>
        /// <param name="offset">Position from that anchor, in reference units.</param>
        /// <param name="size">Width and height, in reference units. Zero means "stretch that axis".</param>
        public static void Place(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size,
                                 Vector2? pivot = null)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot ?? new Vector2(0f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        /// <summary>Stretches a node across the parent's width at a fixed height.</summary>
        public static void Row(RectTransform rect, float top, float height, float left = 0f,
                               float right = 0f, Vector2? pivot = null)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = pivot ?? new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(0f, height);
        }

        /// <summary>
        /// A scrollable list: a viewport with a rectangular mask and a content column.
        /// </summary>
        /// <remarks>
        /// <c>RectMask2D</c> rather than <c>Mask</c>: it clips without a stencil pass and without
        /// needing a material, which on the low end is the difference between a settings list that
        /// scrolls and one that costs a draw call per row.
        /// </remarks>
        public static ScrollRect CreateScroll(string name, Transform parent, out RectTransform content)
        {
            RectTransform root = CreateNode(name, parent);

            var viewport = CreateNode("Viewport", root);
            Stretch(viewport);
            var mask = viewport.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;

            // The viewport needs a graphic of its own to be a drag target; it is invisible, and an
            // image with zero alpha still receives pointer events, which is what makes a list
            // draggable from any point on it, including over its rows.
            Image catcher = CreateImage("Drag", viewport, MenuArt.Solid, new Color(0f, 0f, 0f, 0f), true);
            Stretch(catcher.rectTransform);

            content = CreateNode("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.elasticity = 0.08f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 32f;
            return scroll;
        }

        /// <summary>Sizes a scroll's content to the rows that were placed in it.</summary>
        public static void SetContentHeight(RectTransform content, float height)
        {
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(0f, height));
        }

        /// <summary>Scrolls a selected row into the viewport, for keyboard and gamepad users.</summary>
        public static void ScrollIntoView(ScrollRect scroll, Selectable selectable)
        {
            if (scroll == null || scroll.viewport == null || scroll.content == null || selectable == null)
                return;

            RectTransform item = selectable.transform as RectTransform;
            if (item == null || scroll.content.rect.height <= scroll.viewport.rect.height + 0.1f) return;

            Canvas.ForceUpdateCanvases();
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, item);
            Rect viewport = scroll.viewport.rect;
            float delta = 0f;

            if (bounds.min.y < viewport.yMin) delta = viewport.yMin - bounds.min.y;
            else if (bounds.max.y > viewport.yMax) delta = viewport.yMax - bounds.max.y;

            if (Mathf.Abs(delta) < 0.1f) return;

            scroll.StopMovement();
            scroll.content.anchoredPosition += new Vector2(0f, delta);
        }

        /// <summary>A canvas group, for fading a whole subtree at once.</summary>
        public static CanvasGroup Group(RectTransform rect)
        {
            CanvasGroup group = rect.GetComponent<CanvasGroup>();
            if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
            return group;
        }

        /// <summary>Sets a rect's size, ignoring its anchors, for a fixed-size element.</summary>
        public static void Size(RectTransform rect, float width, float height)
        {
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>An image that fills its parent: the background of a panel or a dialog.</summary>
        public static Image Fill(string name, Transform parent, Color colour, bool raycastTarget = false)
        {
            Image image = CreateImage(name, parent, MenuArt.Solid, colour, raycastTarget);
            Stretch(image.rectTransform);
            return image;
        }

        /// <summary>
        /// A block of prose: wrapped, top-aligned, with the paragraph's leading built in.
        /// </summary>
        /// <remarks>
        /// Wrapping is the one place the interface genuinely has to measure text rather than place
        /// it, because a note's number of lines depends on the screen's width. The rect is given a
        /// height by the caller from <see cref="MenuTheme.Metrics.ParagraphLineHeight"/> and the
        /// block is allowed to overflow rather than shrink its type, so a longer translation grows
        /// downward into space the caller has already left for it.
        /// </remarks>
        public static Text CreateParagraph(string name, Transform parent, string content, float size,
                                           Color colour, float lineHeight)
        {
            Text text = CreateText(name, parent, content, size, colour, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = Mathf.Max(1f, lineHeight / Mathf.Max(1f, size));
            return text;
        }

        /// <summary>
        /// Places a node against a corner of its parent with two anchors, for a block that should
        /// keep its size but follow the edge it belongs to.
        /// </summary>
        public static void Corner(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size,
                                  Vector2 pivot)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        /// <summary>Anchors a node to a column of the parent, as fractions of its width.</summary>
        public static void Column(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(left, 1f);
            rect.anchorMax = new Vector2(right, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, 0f);
            rect.offsetMax = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(0f, height);
        }
    }
}
