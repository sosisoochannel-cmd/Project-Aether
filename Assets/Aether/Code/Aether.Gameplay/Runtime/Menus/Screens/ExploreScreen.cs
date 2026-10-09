using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using UnityEngine;
using UnityEngine.UI;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// A quiet second level for destinations that do not need to compete with starting or resuming a run.
    /// </summary>
    public sealed class ExploreScreen : MenuScreen
    {
        private MenuHeader _header;
        private ScrollRect _scroll;
        private RectTransform _content;
        private MenuEntryPanel _destinations;

        protected override void Construct()
        {
            _header = MenuHeader.Create(Rect, MenuStrings.Get("menu.exploreSection"), Nav, true);
            _header.Back.Activated = Leave;

            _scroll = MenuUi.CreateScroll("Explore Scroll", Rect, out _content);
            Nav.SelectionChanged += selected =>
            {
                if (selected != null && selected.transform.IsChildOf(_content))
                    MenuUi.ScrollIntoView(_scroll, selected);
            };

            _destinations = MenuEntryPanel.Create("Destinations", _content, Nav, null, 1);
            AddDestination(MenuScreenId.Characters, "menu.characters");
            AddDestination(MenuScreenId.Collection, "menu.collection");
            AddDestination(MenuScreenId.Achievements, "menu.achievements");
            AddDestination(MenuScreenId.Credits, "menu.credits");
        }

        private void AddDestination(MenuScreenId destination, string labelKey)
        {
            MenuButton row = _destinations.AddEntry(destination.ToString(), MenuStrings.Get(labelKey),
                                                   MenuButton.Weight.Secondary);
            MenuScreenId target = destination;
            row.Activated = () =>
            {
                MenuAudio.Confirm();
                Host.GoTo(target);
            };
        }

        public override void Layout(float width, float height)
        {
            float top = MenuTheme.Metrics.ScreenHeaderPitch + MenuTheme.Metrics.ScreenHeaderGap;
            float viewportHeight = Mathf.Max(120f, height - top);
            RectTransform scrollRect = _scroll.GetComponent<RectTransform>();
            MenuUi.Corner(scrollRect, new Vector2(0f, 1f), new Vector2(0f, -top),
                          new Vector2(width, viewportHeight), new Vector2(0f, 1f));

            _destinations.Rect.anchoredPosition = Vector2.zero;
            _destinations.SetColumns(1);
            _destinations.Layout(width, viewportHeight);
            MenuUi.SetContentHeight(_content, _destinations.Height + MenuTheme.Metrics.ParagraphBlockPadding);
        }

        protected override void OnShown(bool instant)
        {
            _destinations.Show(instant);
            Nav.Clear();
            _destinations.Register();
            Nav.Add(_header.Back, 0, 1000);
            Nav.SelectFirst();
        }

        public override void Refresh()
        {
            if (_destinations != null) _destinations.Refresh();
        }

        public override bool OnBack()
        {
            return false;
        }

        private void Leave()
        {
            Host.Back();
        }
    }
}
