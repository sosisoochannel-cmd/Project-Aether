using Aether.Gameplay.Presentation;
using UnityEngine;

namespace Aether.Gameplay.Menus.Panels
{
    /// <summary>
    /// A block of a screen: it lays itself out, registers its rows, and reveals itself in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Screens are made of a few of these — a title block, one cluster of entries, another, a list of
    /// settings — and each one is a plain object that owns its rects and nothing else. Splitting a
    /// screen this way is what keeps a screen file short enough to read, and it is also what makes
    /// the entrance orderly: the brief asks for the background, then the title, then the primary
    /// navigation, then the rest, and a delay per panel is exactly that sequence.
    /// </para>
    /// <para>
    /// <b>Layout is a pure function of two numbers.</b> A panel is told the content box it lives in
    /// and places everything itself, in reference units, against the top-left corner. Nothing is
    /// measured from a parent's rect, so a panel can lay out on the frame it is created — before the
    /// canvas has been through a layout pass — and laying out twice is harmless. That matters on a
    /// phone, where a rotation changes the box under a screen that is already on screen.
    /// </para>
    /// <para>
    /// Nothing here is per-frame. Values are refreshed when a setting changes, rows are built once
    /// and relabelled rather than rebuilt, and a hidden panel is a deactivated object.
    /// </para>
    /// </remarks>
    public abstract class MenuPanel : MonoBehaviour
    {
        private RectTransform _rect;
        private CanvasGroup _group;
        private MenuReveal _reveal;
        private bool _visible = true;

        /// <summary>The navigation this panel's rows belong to. Owned by the screen.</summary>
        protected MenuNav Nav { get; private set; }

        /// <summary>The width of the content box, in reference units.</summary>
        protected float ContentWidth { get; private set; }

        /// <summary>The height of the content box, in reference units.</summary>
        protected float ContentHeight { get; private set; }

        /// <summary>The height this panel wants, in reference units. Valid after a layout.</summary>
        public float Height { get; protected set; }

        /// <summary>The panel's own rect, for the screen that stacks panels.</summary>
        public RectTransform Rect
        {
            get { return _rect; }
        }

        /// <summary>Whether the panel has been laid out at least once.</summary>
        public bool Built { get; private set; }

        /// <summary>Whether the panel is currently shown.</summary>
        public bool Visible
        {
            get { return _visible; }
        }

        /// <summary>
        /// Creates a panel of the given type, wired to a navigation.
        /// </summary>
        protected static T Create<T>(string name, Transform parent, MenuNav nav) where T : MenuPanel
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);

            var panel = host.AddComponent<T>();
            panel._rect = (RectTransform)host.transform;
            panel._group = MenuUi.Group(panel._rect);
            panel.Nav = nav;

            panel._rect.anchorMin = new Vector2(0f, 1f);
            panel._rect.anchorMax = new Vector2(0f, 1f);
            panel._rect.pivot = new Vector2(0f, 1f);
            panel._rect.anchoredPosition = Vector2.zero;
            return panel;
        }

        /// <summary>Places the panel's contents in a content box, in reference units.</summary>
        public abstract void Layout(float width, float height);

        /// <summary>
        /// Offers the panel's rows to the screen's navigation, in reading order.
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="Layout"/> on purpose: geometry is decided once per box, and what
        /// can be selected is decided by the screen, which knows which panel is in front. A screen
        /// rebuilds the order whenever it shows something else — one call, no bookkeeping here.
        /// </remarks>
        public virtual void Register()
        {
        }

        /// <summary>Re-reads whatever the panel displays. Called when a setting changes.</summary>
        public virtual void Refresh()
        {
        }

        /// <summary>
        /// Handles a left or right press. Returns true when the panel consumed it.
        /// </summary>
        public virtual bool OnMove(MenuNav.Move move)
        {
            return false;
        }

        /// <summary>Shows the panel, immediately or with its entrance.</summary>
        public void Show(bool instant, float delay = 0f)
        {
            gameObject.SetActive(true);
            _group.blocksRaycasts = true;
            _group.interactable = true;
            _visible = true;

            _reveal = MenuReveal.Attach(_rect);
            _reveal.Play(delay, instant);
        }

        /// <summary>
        /// Hides the panel. It stays in memory and is switched off: a menu that destroys and rebuilds
        /// its screens churns the canvas and loses the reader's place for no gain.
        /// </summary>
        public void Hide()
        {
            if (!gameObject.activeSelf) return;

            _visible = false;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            gameObject.SetActive(false);
        }

        /// <summary>Brings the panel to the front of its parent, for overlapping panels.</summary>
        public void BringToFront()
        {
            if (_rect != null) _rect.SetAsLastSibling();
        }

        /// <summary>Anchors a child to the top-left of the content box.</summary>
        protected RectTransform Place(string name, float x, float y, float width, float height)
        {
            RectTransform rect = MenuUi.CreateNode(name, _rect);
            MenuUi.Corner(rect, new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(width, height),
                          new Vector2(0f, 1f));
            return rect;
        }

        /// <summary>Records the box being laid out in, so subclasses can use it.</summary>
        protected void Remember(float width, float height)
        {
            ContentWidth = width;
            ContentHeight = height;
            Built = true;
        }

        /// <summary>
        /// Sizes the panel's own rect. A panel is anchored to its content box's top-left corner, so
        /// both numbers are its own: a cluster is a column inside the box rather than the whole of it.
        /// </summary>
        protected void SetPanelSize(float width, float height)
        {
            Height = height;
            _rect.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
        }
    }
}
