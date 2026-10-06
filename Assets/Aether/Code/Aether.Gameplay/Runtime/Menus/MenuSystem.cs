using System.Collections.Generic;
using Aether.Gameplay.Menus.Screens;
using Aether.Gameplay.Presentation;
using UnityEngine;

namespace Aether.Gameplay.Menus
{
    /// <summary>
    /// The menu's screens, and the rules for moving between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One screen at a time, built once.</b> A screen is created the first time it is asked for and
    /// then kept, hidden rather than destroyed: coming back to the settings screen is instant, its
    /// scroll position is where it was left, and the menu never pays for a rebuild while the player
    /// is watching. Nothing here allocates per frame, and nothing here looks for anything — the host
    /// hands it the canvas and the input it should use.
    /// </para>
    /// <para>
    /// <b>Every move goes through the transition system.</b> A change of screen is the same fade as a
    /// change of scene, which is what makes a settings screen and a region load feel like parts of one
    /// product. The transition also refuses a second request while it is busy and covers the screen
    /// with something that absorbs pointers, so a fast double tap cannot open two screens or start two
    /// loads — the protection is in the system, not in each row.
    /// </para>
    /// <para>
    /// <b>Who receives input.</b> Movement is offered to the screen first (a row may own the left and
    /// right directions, as a slider does), then to whatever navigation the screen says is in front —
    /// which is how an open dialog takes over the keyboard and the gamepad without a second input
    /// path existing.
    /// </para>
    /// </remarks>
    public sealed class MenuSystem
    {
        /// <summary>How far back the menu remembers. Deeper than any screen in it, and bounded.</summary>
        private const int MaxHistory = 4;

        private readonly List<MenuScreen> _screens = new List<MenuScreen>(8);
        private readonly List<MenuScreenId> _history = new List<MenuScreenId>(MaxHistory);
        private readonly MenuCanvas _canvas;
        private readonly MenuInput _input;
        private RectTransform _screenRoot;
        private RectTransform _dialogRoot;
        private MenuScreen _current;
        private float _width;
        private float _height;
        private bool _rebuildPending;

        private MenuSystem(MenuCanvas canvas, MenuInput input)
        {
            _canvas = canvas;
            _input = input;
        }

        /// <summary>The canvas the screens are drawn on.</summary>
        public MenuCanvas Canvas
        {
            get { return _canvas; }
        }

        /// <summary>Where dialogs are built: over the whole screen, under the transition's veil.</summary>
        public RectTransform DialogRoot
        {
            get { return _dialogRoot; }
        }

        /// <summary>The screen that is up, or null before the first one has been shown.</summary>
        public MenuScreen Current
        {
            get { return _current; }
        }

        /// <summary>Which screen is up. Defaults to the main menu before anything is shown.</summary>
        public MenuScreenId CurrentId
        {
            get { return _current == null ? MenuScreenId.MainMenu : _current.Id; }
        }

        /// <summary>Whether a transition is running. Input is locked while it is.</summary>
        public bool Transitioning
        {
            get { return MenuTransition.Instance.Busy; }
        }

        /// <summary>
        /// Builds the screens' root and the dialogs' root, and connects the navigation input.
        /// </summary>
        /// <param name="canvas">The canvas the menu is drawn on.</param>
        /// <param name="input">The navigation input, or null in a test that drives the system itself.</param>
        public static MenuSystem Create(MenuCanvas canvas, MenuInput input)
        {
            var system = new MenuSystem(canvas, input);

            system._screenRoot = MenuUi.CreateNode("Screens", canvas.ContentRoot);
            MenuUi.Stretch(system._screenRoot);

            // Dialogs hang off the canvas root rather than the content box: their scrim is meant to
            // cover the screen edge to edge, cutouts included, so that nothing behind it can be
            // touched or read while a question is being asked.
            system._dialogRoot = MenuUi.CreateNode("Dialogs", canvas.Root);
            MenuUi.Stretch(system._dialogRoot);

            if (input != null)
            {
                input.Moved += system.OnMoveRequested;
                input.Cancelled += system.OnCancelRequested;
            }

            return system;
        }

        /// <summary>True when a rebuild is waiting for a transition to finish.</summary>
        public bool RebuildPending
        {
            get { return _rebuildPending; }
        }

        /// <summary>
        /// Builds every screen again from scratch, keeping the player where they are.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A screen is built once and cached, and its labels are written at that moment rather than
        /// every frame — that is what keeps a menu of this size cheap. So the one thing that cannot be
        /// a per-row refresh is a change of language: every word on every screen, including the words
        /// on the screen that made the change, is a different word afterwards.
        /// </para>
        /// <para>
        /// Rebuilding is the honest way to do that: throw the built screens away and build them again
        /// in the new language. It happens once per change and never per frame, and any dialog that is
        /// up is thrown away with them, because a confirmation in the old language over a screen in
        /// the new one is exactly the halfway state this menu does not have.
        /// </para>
        /// </remarks>
        public void RequestRebuild()
        {
            // Not while a transition is running: the screen on its way out is still visible through
            // the veil, and pulling it apart mid-fade would show the player a half-built menu.
            if (MenuTransition.Instance.Busy)
            {
                _rebuildPending = true;
                return;
            }

            Rebuild();
        }

        /// <summary>Performs a deferred rebuild once the transition that deferred it has finished.</summary>
        public void ApplyPendingRebuild()
        {
            if (!_rebuildPending) return;
            if (MenuTransition.Instance.Busy) return;

            _rebuildPending = false;
            Rebuild();
        }

        private void Rebuild()
        {
            MenuScreenId id = CurrentId;
            _rebuildPending = false;

            for (int i = 0; i < _screens.Count; i++)
            {
                MenuScreen screen = _screens[i];
                if (screen == null) continue;

                screen.Hide();

                // Qualified: this is a plain class, not a component, so a bare Destroy would not
                // resolve — and the objects go at the end of the frame, which is why the new screen
                // is built straight away and drawn over the empty shells of the old ones.
                UnityEngine.Object.Destroy(screen.gameObject);
            }

            _screens.Clear();
            _current = null;

            for (int i = _dialogRoot.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_dialogRoot.GetChild(i).gameObject);
            }

            Show(id, true);
        }

        /// <summary>Lays the screen that is up out in a content box.</summary>
        public void Layout(float width, float height)
        {
            // A box smaller than this is not a screen this menu is designed for; clamping keeps a
            // bad editor window or a portrait device from producing negative rects.
            _width = Mathf.Max(320f, width);
            _height = Mathf.Max(240f, height);

            if (_current != null) _current.Layout(_width, _height);
        }

        /// <summary>Shows a screen now, without a transition. Used for the first screen and for tests.</summary>
        public void Show(MenuScreenId id, bool instant)
        {
            MenuScreen next = Ensure(id);
            if (next == null) return;

            if (_current == next)
            {
                next.Refresh();
                next.Refocus();
                return;
            }

            if (_current != null) _current.Hide();

            _current = next;
            _current.Show(_width, _height, instant);
        }

        /// <summary>
        /// Fades to a screen. Refused while a transition is running, and refused when it would go
        /// where the menu already is: one press, one outcome.
        /// </summary>
        public void GoTo(MenuScreenId id)
        {
            if (_current != null && _current.Id == id) return;

            MenuTransition transition = MenuTransition.Instance;
            if (transition.Busy) return;

            Remember();
            transition.Cover(() => Show(id, false));
        }

        /// <summary>Re-reads the values every screen displays. Called when a setting changes.</summary>
        public void Refresh()
        {
            if (_current != null) _current.Refresh();
        }

        /// <summary>
        /// Goes back: the screen's own answer first, then the screen the player came from, then the
        /// main menu.
        /// </summary>
        /// <remarks>
        /// One method, called by the system back gesture and by every screen's own back row. Two
        /// implementations would mean the hardware button and the on-screen one could disagree about
        /// where "back" is, which is exactly the sort of thing a player never reports and always
        /// notices.
        /// </remarks>
        /// <returns>True when something was done with it, so the caller need not look for another meaning.</returns>
        public bool Back()
        {
            MenuScreen screen = _current;
            if (screen == null) return false;

            if (screen.OnBack()) return true;

            // Where the player came from, not a fixed parent: the credits screen is reachable from
            // both the main menu and the settings screen, and going back to the wrong one is the kind
            // of small betrayal that makes a menu feel like it is not paying attention.
            if (_history.Count > 0)
            {
                MenuScreenId previous = Pop();
                MenuAudio.Back();
                GoTo(previous);
                return true;
            }

            if (screen.Id == MenuScreenId.MainMenu) return false;

            GoTo(MenuScreenId.MainMenu);
            return true;
        }

        /// <summary>Notes where the player was before this move.</summary>
        private void Remember()
        {
            if (_current == null) return;

            _history.Add(_current.Id);
            if (_history.Count > MaxHistory) _history.RemoveAt(0);
        }

        private MenuScreenId Pop()
        {
            int last = _history.Count - 1;
            MenuScreenId id = _history[last];
            _history.RemoveAt(last);
            return id;
        }

        /// <summary>Sends a direction to the screen that is up.</summary>
        public void Move(MenuNav.Move move)
        {
            MenuScreen screen = _current;
            if (screen == null) return;

            if (screen.OnMove(move)) return;

            MenuNav nav = screen.ActiveNav;
            if (nav != null) nav.Step(move);
        }

        /// <summary>Runs the row the selection is on, as the Submit button would.</summary>
        public void Submit()
        {
            MenuScreen screen = _current;
            if (screen == null) return;

            MenuNav nav = screen.ActiveNav;
            if (nav != null) nav.Submit();
        }

        /// <summary>Hands over to the region. One line, because the order of operations lives in one place.</summary>
        public void PlayRegion()
        {
            MenuFlow.PlayRegion();
        }

        private void OnMoveRequested(MenuNav.Move move)
        {
            if (MenuTransition.Instance.Busy) return;
            Move(move);
        }

        private void OnCancelRequested()
        {
            if (MenuTransition.Instance.Busy) return;
            Back();
        }

        /// <summary>
        /// The screen for an id, built on first ask and kept afterwards.
        /// </summary>
        /// <remarks>
        /// Building is deferred until a screen is actually wanted, which is the difference between a
        /// menu that costs a few hundred objects to show its first screen and one that costs them all
        /// to show its title. It happens under the transition's cover, so the work is not visible.
        /// </remarks>
        private MenuScreen Ensure(MenuScreenId id)
        {
            for (int i = 0; i < _screens.Count; i++)
            {
                if (_screens[i] != null && _screens[i].Id == id) return _screens[i];
            }

            MenuScreen screen = MenuScreens.Create(id, _screenRoot, this);
            if (screen == null) return null;

            // It arrives active, because a component cannot build itself on a switched-off object.
            // Hiding it before the first layout is what stops it from appearing for a frame.
            screen.Hide();
            screen.Layout(_width, _height);
            _screens.Add(screen);
            return screen;
        }
    }
}
