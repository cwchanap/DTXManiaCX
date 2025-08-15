using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Layout
{
    /// <summary>
    /// Centralized layout configuration for Result Stage UI components
    /// Contains all positioning, sizing, and color constants for result display
    /// </summary>
    public static class ResultUILayout
    {
        #region Background
        
        /// <summary>
        /// Background display settings
        /// </summary>
        public static class Background
        {
            public static readonly Color BackgroundColor = Color.DarkBlue * 0.8f;
        }
        
        #endregion
        
        #region Result Display Layout
        
        /// <summary>
        /// Main result display positioning
        /// </summary>
        public static class ResultDisplay
        {
            public const int StartY = 100;
            public const int LineHeight = 40;
            public const int ExtraSpacing = 20; // LineHeight / 2
            
            // Colors for different result elements
            public static readonly Color TitleColor = Color.Yellow;
            public static readonly Color ClearedColor = Color.Green;
            public static readonly Color FailedColor = Color.Red;
            public static readonly Color NormalTextColor = Color.White;
            public static readonly Color SectionHeaderColor = Color.Cyan;
            public static readonly Color InstructionTextColor = Color.Gray;
        }
        
        #endregion
        
        #region Fallback Text Rendering
        
        /// <summary>
        /// Fallback text rendering settings when bitmap font is unavailable
        /// </summary>
        public static class FallbackText
        {
            public const int CharacterWidth = 8;
            public const int RectHeight = 20;
        }
        
        #endregion
    }
}