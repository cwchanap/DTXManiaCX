namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Centralized constants for DTXMania application
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Default song directories for DTX file scanning
        /// </summary>
        public static class SongPaths
        {
            /// <summary>
            /// Primary directory containing DTX song files
            /// </summary>
            public const string DTXFiles = "DTXFiles";
            
            /// <summary>
            /// Default song paths array for initialization
            /// </summary>
            public static readonly string[] Default = { DTXFiles };
        }
    }
}