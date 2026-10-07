using Aether.Gameplay.Menus.Panels;
using UnityEngine;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// One screen of the menu: it lays itself out in a content box, and it answers input.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A screen is what the host shows, hides and asks for input. It owns a navigation, some panels,
    /// and its own back behaviour — nothing else. Screens are created once and kept: switching
    /// between them is a matter of hiding one and showing another, which is what makes the menu
    /// instant on a slow phone and what keeps a screen's scroll position and its place in the
    /// navigation when the player comes back to it.
    /// </para>
    /// <para>
    /// <b>Layout takes two numbers.</b> A screen is told the size of the content box and places
    /// everything in reference units from its top-left corner. It never measures its parent, so it
    /// can lay out on the frame it is created, and it lays out again — with no state lost — when the
    /// device rotates, the window resizes, or the player changes the interface size.
    /// </para>
    /// </remarks>
    public abstract class MenuScreen : MonoBehaviour
    {
        private RectTransform _rect;

        /// <summary>The screen's navigation. Its rows are registered by the panels.</summary>
        protected MenuNav Nav { get; private set; }

        /// <summary>The menu this screen belongs to, for going to another screen or leaving.</summary>
        protected MenuSystem Host { get; private set; }

        /// <summary>Which screen this is.</summary>
        public MenuScreenId Id { get; private set; }

        /// <summary>The screen's root rect: the content box.</summary>
        public RectTransform Rect
        {
            get { return _rect; }
        }

        /// <summary>Whether the screen is on screen.</summary>
        public bool Visible
        {
            get { return gameObject.activeSelf; }
        }

        /// <summary>
        /// The navigation input should drive right now. A screen with a modal in front of it returns
        /// the modal's navigation instead, which is how a dialog takes over without a second input path.
        /// </summary>
        public virtual MenuNav ActiveNav
        {
            get { return DialogOpen ? DialogNav : Nav; }
        }

        /// <summary>
        /// The screen's own dialog, built on first ask.
        /// </summary>
        /// <remarks>
        /// Four screens ask a question before doing something that cannot be undone — replace a run,
        /// delete one, leave a region — and every one of them asks it the same way: a
        /// <see cref="MenuConfirmPanel"/> under the menu's dialog root, with its own navigation so a
        /// dialog takes over the input without a second input path, and with Cancel selected first so
        /// a double tap on a destructive row cannot answer its own question. Building it here means
        /// the fifth screen that needs one gets all of that by asking for it.
        /// </remarks>
        protected MenuConfirmPanel Dialog { get; private set; }

        /// <summary>The navigation a dialog drives while it is open.</summary>
        protected MenuNav DialogNav { get; private set; }

        /// <summary>Builds the screen's dialog once and returns it.</summary>
        protected MenuConfirmPanel EnsureDialog()
        {
            if (Dialog != null) return Dialog;

            DialogNav = new MenuNav();
            Dialog = MenuConfirmPanel.Create("Confirm", Host.DialogRoot, DialogNav);
            Dialog.Closed = OnDialogClosed;
            return Dialog;
        }

        /// <summary>Called after the screen's dialog closes. Puts the selection back by default.</summary>
        protected virtual void OnDialogClosed()
        {
            Refocus();
        }

        /// <summary>
        /// Whether the dialog is up, and should be given the input.
        /// </summary>
        public bool DialogOpen
        {
            get { return Dialog != null && Dialog.IsOpen; }
        }

        /// <summary>Creates a screen under the menu's content root.</summary>
        public static T Create<T>(MenuScreenId id, Transform parent, MenuSystem host)
            where T : MenuScreen
        {
            var hostObject = new GameObject(id + " Screen", typeof(RectTransform));
            hostObject.transform.SetParent(parent, false);

            var screen = hostObject.AddComponent<T>();
            screen._rect = (RectTransform)hostObject.transform;
            screen.Id = id;
            screen.Host = host;
            screen.Nav = new MenuNav();

            MenuUi.Stretch(screen._rect);
            screen.Construct();
            return screen;
        }

        /// <summary>Builds the screen's contents. Called once, before any layout.</summary>
        protected virtual void Construct()
        {
        }

        /// <summary>Places everything in a content box, in reference units.</summary>
        public abstract void Layout(float width, float height);

        /// <summary>Re-reads the values the screen displays.</summary>
        public virtual void Refresh()
        {
        }

        /// <summary>
        /// Handles a left or right press before the navigation sees it. Returns true when consumed,
        /// which is what lets a slider take the left and right keys on the row it belongs to.
        /// </summary>
        public virtual bool OnMove(MenuNav.Move move)
        {
            return false;
        }

        /// <summary>
        /// Handles the back request: the Back row, Escape, the gamepad's east button, the Android
        /// back gesture. Returns true when the screen did something with it.
        /// </summary>
        public virtual bool OnBack()
        {
            // A question outranks the screen's own back: closing a dialog is what back means while
            // one is open, and it is the answer that destroys nothing.
            if (DialogOpen)
            {
                Dialog.Cancel();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Puts the selection back where the screen wants it, and re-asserts it to the event system.
        /// </summary>
        /// <remarks>
        /// Used when a screen is asked for while it is already up, and after a dialog it opened has
        /// closed. Both are moments where the row the player is on has not moved, but the event
        /// system's idea of it may have been cleared — and a keyboard or gamepad that cannot press
        /// anything is worse than one that presses the wrong thing.
        /// </remarks>
        public void Refocus()
        {
            MenuNav nav = ActiveNav;
            if (nav != null) nav.SelectFirst();
        }

        /// <summary>Shows the screen, rebuilding its layout for the current box first.</summary>
        /// <param name="width">Content box width, in reference units.</param>
        /// <param name="height">Content box height, in reference units.</param>
        /// <param name="instant">True to skip the entrance.</param>
        public void Show(float width, float height, bool instant)
        {
            gameObject.SetActive(true);
            Layout(width, height);
            OnShown(instant);
        }

        /// <summary>Called after the screen has been laid out and shown.</summary>
        protected virtual void OnShown(bool instant)
        {
        }

        /// <summary>Hides the screen. It stays in memory: screens are built once and reused.</summary>
        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            OnHidden();
            gameObject.SetActive(false);
        }

        /// <summary>Called just before the screen is hidden.</summary>
        protected virtual void OnHidden()
        {
        }
    }
}
