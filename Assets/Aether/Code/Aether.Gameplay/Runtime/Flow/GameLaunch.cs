namespace Aether.Gameplay.Flow
{
    /// <summary>
    /// What the player asked for, carried across the scene load that honours it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scene loads destroy the object that asked for them, so a small explicit intent crosses the
    /// boundary instead: Boot consumes a play request, while the menu consumes a request to open
    /// Settings. Each is read-and-clear, so a fulfilled request cannot unexpectedly run again on a
    /// later visit.
    /// </para>
    /// <para>
    /// This is deliberately not a navigation stack. It carries only these two hand-off intents;
    /// ordinary screen history stays with <see cref="Aether.Gameplay.Menus.MenuSystem"/>.
    /// </para>
    /// </remarks>
    public static class GameLaunch
    {
        private static bool _playRequested;
        private static string _requestedLevelPath;
        private static bool _openSettingsRequested;

        /// <summary>True while a request is waiting to be served.</summary>
        public static bool IsPlayRequested => _playRequested;

        /// <summary>True while the menu has been asked to open its Settings screen.</summary>
        public static bool IsOpenSettingsRequested => _openSettingsRequested;

        /// <summary>Asks for the region to be started the next time boot runs.</summary>
        public static void RequestPlay(string levelPath = null)
        {
            _requestedLevelPath = levelPath;
            _playRequested = true;
            _openSettingsRequested = false;
        }

        /// <summary>Opens the menu on Settings after a scene hand-off, such as from the pause menu.</summary>
        public static void RequestOpenSettings()
        {
            _playRequested = false;
            _openSettingsRequested = true;
        }

        /// <summary>
        /// Reads the request and clears it. Return value tells the caller which of the two things
        /// boot should do.
        /// </summary>
        public static bool TakePlayRequest()
        {
            bool requested = _playRequested;
            _playRequested = false;
            return requested;
        }

        public static string TakeRequestedLevelPath()
        {
            string path = _requestedLevelPath;
            _requestedLevelPath = null;
            return path;
        }

        /// <summary>Reads and clears the pending menu-settings destination.</summary>
        public static bool TakeOpenSettingsRequest()
        {
            bool requested = _openSettingsRequested;
            _openSettingsRequested = false;
            return requested;
        }

        /// <summary>Clears only a pending play request after a failed scene load.</summary>
        public static void ClearPlayRequest()
        {
            _playRequested = false;
        }

        /// <summary>Drops any pending request. Used by tests and by a deliberate return to the menu.</summary>
        public static void Clear()
        {
            _playRequested = false;
            _requestedLevelPath = null;
            _openSettingsRequested = false;
        }
    }
}
