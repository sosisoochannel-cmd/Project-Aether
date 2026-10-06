namespace Aether.Gameplay.Flow
{
    /// <summary>
    /// What the player asked for, carried across the scene load that honours it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The menu cannot start the region itself: the region is built by a scene, and a scene load
    /// destroys the object that asked for it. So the request is written down here, the boot scene
    /// reads it once and clears it, and what happens next is decided by one piece of code rather
    /// than by whichever scene happens to be loaded first.
    /// </para>
    /// <para>
    /// It is read-and-clear on purpose. A request that survives being served is a request that will
    /// be served again — the next time the player opens the menu and presses nothing at all — and a
    /// game that starts a region by itself is a bug that is nearly impossible to reproduce.
    /// </para>
    /// <para>
    /// This is deliberately not a navigation stack. There are two destinations and one question
    /// ("did the player ask to play?"); a stack would be a system with its own bugs and nothing to
    /// do.
    /// </para>
    /// </remarks>
    public static class GameLaunch
    {
        private static bool _playRequested;

        /// <summary>True while a request is waiting to be served.</summary>
        public static bool IsPlayRequested => _playRequested;

        /// <summary>Asks for the region to be started the next time boot runs.</summary>
        public static void RequestPlay()
        {
            _playRequested = true;
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

        /// <summary>Drops any pending request. Used by tests and by a deliberate return to the menu.</summary>
        public static void Clear()
        {
            _playRequested = false;
        }
    }
}
