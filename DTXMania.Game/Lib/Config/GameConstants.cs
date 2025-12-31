namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// Centralized game-wide constants for DTXManiaCX
    /// </summary>
    public static class GameConstants
    {
        /// <summary>
        /// Stage transition timing constants
        /// </summary>
        public static class StageTransition
        {
            /// <summary>
            /// Global debounce delay in seconds to prevent rapid stage transitions
            /// </summary>
            public const double DebounceDelaySeconds = 0.5;
        }

        /// <summary>
        /// JSON-RPC server configuration constants
        /// </summary>
        public static class JsonRpc
        {
            /// <summary>
            /// Maximum request body size in bytes (8 KB)
            /// </summary>
            public const long MaxRequestBodyBytes = 8 * 1024;

            /// <summary>
            /// Default server port
            /// </summary>
            public const int DefaultPort = 8080;

            /// <summary>
            /// Server shutdown timeout in milliseconds
            /// </summary>
            public const int ShutdownTimeoutMs = 5000;
        }

        /// <summary>
        /// Input system configuration constants
        /// </summary>
        public static class Input
        {
            /// <summary>
            /// Interval in milliseconds between device scans for hot-plug detection
            /// </summary>
            public const double DeviceScanIntervalMs = 3000.0;

            /// <summary>
            /// Target update latency in milliseconds for input processing
            /// </summary>
            public const double TargetUpdateLatencyMs = 1.0;
        }

        /// <summary>
        /// Performance stage timing constants
        /// </summary>
        public static class Performance
        {
            /// <summary>
            /// Buffer time in seconds after song ends before stage completion
            /// </summary>
            public const double SongEndBufferSeconds = 3.0;

            /// <summary>
            /// Ready countdown duration in seconds before song starts
            /// </summary>
            public const double ReadyCountdownSeconds = 1.0;
        }

        /// <summary>
        /// Resource management constants
        /// </summary>
        public static class Resources
        {
            /// <summary>
            /// Default skin path
            /// </summary>
            public const string DefaultSkinPath = "System/";

            /// <summary>
            /// Default fallback skin path.
            /// Intentionally the same as DefaultSkinPath - DTXMania uses a single System/ directory
            /// for all default resources. A separate fallback is not needed as the System/ folder
            /// contains all required fallback assets.
            /// </summary>
            public const string FallbackSkinPath = "System/";
        }

        /// <summary>
        /// Task execution constants
        /// </summary>
        public static class Tasks
        {
            /// <summary>
            /// Default timeout in milliseconds for async task completion during cleanup
            /// </summary>
            public const int CleanupTimeoutMs = 1000;
        }
    }
}
