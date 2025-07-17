using Microsoft.Xna.Framework;

namespace DTX.UI.Layout
{
    /// <summary>
    /// Centralized layout configuration for Song Transition UI components
    /// Contains all positioning and sizing constants for song transition stage
    /// </summary>
    public static class SongTransitionUILayout
    {
        #region Main Panel Layout
        
        /// <summary>
        /// Main panel layout configuration
        /// </summary>
        public static class MainPanel
        {
            public const float BackgroundAlpha = 0.8f;
            public static Color BackgroundColor => Color.DarkBlue * BackgroundAlpha;
        }
        
        #endregion
        
        #region Label Layout
        
        /// <summary>
        /// Song title label configuration
        /// </summary>
        public static class SongTitle
        {
            public const int X = 100;
            public const int Y = 150;
            public const int Width = 600;
            public const int Height = 80;
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Color TextColor => Color.White;
        }
        
        /// <summary>
        /// Artist label configuration
        /// </summary>
        public static class Artist
        {
            public const int X = 100;
            public const int Y = 250;
            public const int Width = 600;
            public const int Height = 50;
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Color TextColor => Color.LightGray;
        }
        
        /// <summary>
        /// Difficulty label configuration
        /// </summary>
        public static class Difficulty
        {
            public const int X = 100;
            public const int Y = 320;
            public const int Width = 600;
            public const int Height = 50;
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Color TextColor => Color.Yellow;
        }
        
        #endregion
        
        #region Preview Image Layout
        
        /// <summary>
        /// Preview image configuration with rotation
        /// </summary>
        public static class PreviewImage
        {
            public const int X = 640;
            public const int Y = 120;
            public const int Width = 384;
            public const int Height = 384;
            public const float RotationRadians = -0.28f; // Counter-clockwise rotation
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Vector2 Origin => new Vector2(Width / 2f, Height / 2f); // Center origin for rotation
            public static Color TintColor => Color.White;
        }
        
        #endregion
        
        #region Timing Configuration
        
        /// <summary>
        /// Timing constants for the transition stage
        /// </summary>
        public static class Timing
        {
            public const double AutoTransitionDelay = 3.0; // Auto transition after 3 seconds
            public const double FadeInDuration = 0.5;
            public const double FadeOutDuration = 0.5;
        }
        
        #endregion
        
        #region Background Configuration
        
        /// <summary>
        /// Background rendering configuration
        /// </summary>
        public static class Background
        {
            public const int GradientLineSpacing = 8; // Line spacing for gradient fallback
            public static Color GradientTopColor => Color.DarkBlue;
            public static Color GradientBottomColor => Color.Black;
            public static string DefaultBackgroundPath => "Graphics/5_background.jpg";
        }
        
        #endregion
    }
}