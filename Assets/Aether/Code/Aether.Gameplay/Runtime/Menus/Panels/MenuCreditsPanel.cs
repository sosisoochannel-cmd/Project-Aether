using Aether.Gameplay.Menus.Widgets;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Panels
{
    /// <summary>
    /// The credits: named blocks, each one contributed by a group of people or a technology.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The blocks come from a table of localisation keys rather than from prose in this file, so a
    /// credit is added by adding a line to the string table. Each block is a tracked label over a
    /// short paragraph — the same two shapes the settings categories use, which is what keeps the
    /// menu looking like one interface rather than four screens that happen to share colours.
    /// </para>
    /// <para>
    /// The music block says the truth: there is no approved track in the build yet. A credits screen
    /// that thanks someone who has not written anything is the smallest possible lie and exactly the
    /// kind this project does not tell.
    /// </para>
    /// </remarks>
    public sealed class MenuCreditsPanel : MenuPanel
    {
        private struct Block
        {
            public string LabelKey;
            public string BodyKey;
        }

        private static readonly Block[] Blocks =
        {
            new Block { LabelKey = "credits.art", BodyKey = "credits.art.body" },
            new Block { LabelKey = "credits.code", BodyKey = "credits.code.body" },
            new Block { LabelKey = "credits.engine", BodyKey = null },
            new Block { LabelKey = "credits.music", BodyKey = "credits.music.body" },
            new Block { LabelKey = "note.about", BodyKey = null },
        };

        private readonly System.Collections.Generic.List<Text> _bodies =
            new System.Collections.Generic.List<Text>(Blocks.Length);

        private ScrollRect _scroll;
        private RectTransform _content;
        private bool _built;

        /// <summary>Creates the credits panel.</summary>
        public static MenuCreditsPanel Create(string name, Transform parent, MenuNav nav)
        {
            MenuCreditsPanel panel = Create<MenuCreditsPanel>(name, parent, nav);
            panel._scroll = MenuUi.CreateScroll("Scroll", panel.Rect, out panel._content);
            MenuUi.Stretch(panel._scroll.GetComponent<RectTransform>());
            return panel;
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            Remember(width, height);
            SetPanelSize(width, height);

            if (!_built) Build(width);
        }

        private void Build(float width)
        {
            _built = true;

            float y = 0f;

            for (int i = 0; i < Blocks.Length; i++)
            {
                Block block = Blocks[i];

                MenuCategoryHeader header = MenuCategoryHeader.Create("Block " + block.LabelKey, _content,
                                                                      MenuStrings.Get(block.LabelKey));
                MenuUi.Corner(header.Rect, new Vector2(0f, 1f), new Vector2(0f, -y),
                              new Vector2(width, 60f), new Vector2(0f, 1f));
                header.SetRuleWidth(width);

                y += 60f;
                y += MenuTheme.Metrics.ParagraphBlockPadding;

                if (string.IsNullOrEmpty(block.BodyKey)) continue;

                Text body = MenuUi.CreateParagraph("Body " + block.BodyKey, _content,
                                                   MenuStrings.Get(block.BodyKey),
                                                   MenuTheme.Metrics.ParagraphSize,
                                                   MenuTheme.Palette.InkMuted,
                                                   MenuTheme.Metrics.ParagraphLineHeight);
                MenuUi.Corner(body.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -y),
                              new Vector2(width, MenuTheme.Metrics.ParagraphLineHeight),
                              new Vector2(0f, 1f));

                float measured = body.preferredHeight;
                float lines = Mathf.Max(1f, Mathf.Ceil(measured / MenuTheme.Metrics.ParagraphLineHeight));
                body.rectTransform.sizeDelta = new Vector2(width,
                                                          lines * MenuTheme.Metrics.ParagraphLineHeight);

                y += (lines * MenuTheme.Metrics.ParagraphLineHeight)
                     + (MenuTheme.Metrics.ParagraphBlockPadding * 2f);

                _bodies.Add(body);
            }

            MenuUi.SetContentHeight(_content, y + MenuTheme.Metrics.ParagraphBlockPadding);
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            // The prose is built once and kept, so a change of contrast is a colour write per
            // paragraph rather than a rebuild of the screen.
            bool highContrast = MenuPreferences.HighContrast;
            Color body = MenuTheme.Palette.WithContrast(MenuTheme.Palette.InkMuted, highContrast);
            for (int i = 0; i < _bodies.Count; i++) _bodies[i].color = body;
        }
    }
}
