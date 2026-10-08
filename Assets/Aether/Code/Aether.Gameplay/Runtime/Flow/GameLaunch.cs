namespace Aether.Gameplay.Flow
{
    /// <summary>
    /// Carries the exact gameplay hand-off across the Boot scene load.
    /// </summary>
    public static class GameLaunch
    {
        public const string DefaultLevelPath = "Levels/region1.greenway.level";

        private static bool _playRequested;
        private static bool _openSettingsRequested;
        private static string _requestedLevelPath;

        public static bool IsPlayRequested => _playRequested;
        public static bool IsOpenSettingsRequested => _openSettingsRequested;
        public static string RequestedLevelPath =>
            string.IsNullOrEmpty(_requestedLevelPath) ? DefaultLevelPath : _requestedLevelPath;

        /// <summary>Starts the default first region.</summary>
        public static void RequestPlay()
        {
            RequestPlay(DefaultLevelPath);
        }

        /// <summary>
        /// Starts a specific source-of-truth region on the next Boot.
        /// The path is a Resources path without the .txt extension.
        /// </summary>
        public static void RequestPlay(string levelPath)
        {
            if (string.IsNullOrWhiteSpace(levelPath)) levelPath = DefaultLevelPath;

            _playRequested = true;
            _openSettingsRequested = false;
            _requestedLevelPath = levelPath.Trim();
        }

        public static void RequestOpenSettings()
        {
            _playRequested = false;
            _openSettingsRequested = true;
            _requestedLevelPath = null;
        }

        public static bool TakePlayRequest()
        {
            bool requested = _playRequested;
            _playRequested = false;
            return requested;
        }

        public static string TakeRequestedLevelPath()
        {
            string path = RequestedLevelPath;
            _requestedLevelPath = null;
            return path;
        }

        public static bool TakeOpenSettingsRequest()
        {
            bool requested = _openSettingsRequested;
            _openSettingsRequested = false;
            return requested;
        }

        public static void ClearPlayRequest()
        {
            _playRequested = false;
            _requestedLevelPath = null;
        }

        public static void Clear()
        {
            _playRequested = false;
            _openSettingsRequested = false;
            _requestedLevelPath = null;
        }
    }
}
