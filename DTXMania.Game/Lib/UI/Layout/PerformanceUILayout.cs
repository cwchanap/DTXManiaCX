using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Layout
{
    /// <summary>
    /// Centralized layout configuration for Performance Stage UI components
    /// Contains all positioning, sizing, and timing constants for gameplay display
    /// </summary>
    public static class PerformanceUILayout
    {
        #region Screen Dimensions
        
        /// <summary>
        /// Standard screen dimensions for performance stage
        /// </summary>
        public const int ScreenWidth = 1280;
        public const int ScreenHeight = 720;
        
        public static Vector2 ScreenCenter => new Vector2(ScreenWidth / 2, ScreenHeight / 2);
        public static Vector2 ScreenSize => new Vector2(ScreenWidth, ScreenHeight);
        
        #endregion
        
        #region Ready Display Constants
        
        /// <summary>
        /// Ready countdown display settings
        /// </summary>
        public const double ReadyCountdownSeconds = 1.0;
        public const double ReadyPulseFrequency = 2.0; // pulses per second
        
        // Fallback text rendering settings
        public const int ReadyFallbackCharacterWidth = 12;
        public const int ReadyFallbackTextHeight = 20;
        
        #endregion
        
        #region BGM Events Constants
        
        /// <summary>
        /// BGM event timing settings
        /// </summary>
        public const double BGMTimingToleranceMs = 50.0; // 50ms tolerance for BGM triggering
        
        #endregion
        
        #region Note Rendering Constants
        
        /// <summary>
        /// Note rendering settings
        /// </summary>
        public const double NoteDefaultLookAheadMs = 1500.0;
        public const int NoteDefaultScrollSpeed = 100; // Default scroll speed percentage
        
        #endregion
    }
}