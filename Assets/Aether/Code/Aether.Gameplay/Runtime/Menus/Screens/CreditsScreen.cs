using Aether.Gameplay.Menus.Components;
using Aether.Gameplay.Menus.Panels;
using UnityEngine;

namespace Aether.Gameplay.Menus.Screens
{
    /// <summary>
    /// Credits and legal: who made the game, what it is made of, and what is not in it yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The text is the credits panel's; this screen gives it a header, a way back, and the whole
    /// content box. It is deliberately the least decorated screen in the menu: a credits screen that
    /// performs is a credits screen nobody finishes reading.
    /// </para>
    /// <para>
    /// The way back is the header's row, which is the first thing registered — so the selection starts
    /// somewhere that always does something, and the keyboard can leave without reading anything.
    /// </para>
    /// </remarks>
    public sealed class CreditsScreen : MenuScreen
    {
        private MenuHeader _header;
        private MenuCreditsPanel _credits;

        /// <inheritdoc />
        protected override void Construct()
        {
            _header = MenuHeader.Create(Rect, MenuStrings.Get("credits.title"), Nav, true);
            _header.Back.Activated = Leave;

            _credits = MenuCreditsPanel.Create("Credits", Rect, Nav);
        }

        /// <inheritdoc />
        public override void Layout(float width, float height)
        {
            float top = MenuTheme.Metrics.ScreenHeaderPitch + MenuTheme.Metrics.ScreenHeaderGap;
            float box = Mathf.Max(160f, height - top);

            _credits.Rect.anchoredPosition = new Vector2(0f, -top);
            _credits.Layout(width, box);
        }

        /// <inheritdoc />
        protected override void OnShown(bool instant)
        {
            _credits.Show(instant);

            // The back row is the only entry the credits screen owns, and it is the one to start on.
            Nav.Clear();
            Nav.Add(_header.Back, 0, 1);
            Nav.SelectFirst();
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            _credits.Refresh();
        }

        /// <inheritdoc />
        public override bool OnBack()
        {
            // Returning false lets the menu take the player back to the main menu, which is the
            // screen behind this one; the header's own row does the same thing directly.
            return false;
        }

        private void Leave()
        {
            // Through the host, not straight to the main menu: the credits screen is reachable from
            // the settings screen too, and back means back.
            MenuAudio.Back();
            Host.Back();
        }
    }
}
