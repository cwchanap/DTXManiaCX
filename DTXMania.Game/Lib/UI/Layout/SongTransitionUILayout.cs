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
            public const int X = 190;
            public const int Y = 285;
            public const int Width = 600;
            public const int Height = 80;
            public const int FontSize = 50;
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Color TextColor => Color.White;
        }
        
        /// <summary>
        /// Artist label configuration
        /// </summary>
        public static class Artist
        {
            public const int X = 190;
            public const int Y = 450;
            public const int Width = 600;
            public const int Height = 50;
            public const int FontSize = 30;
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Color TextColor => Color.LightGray;
        }
        
        /// <summary>
        /// Difficulty label configuration
        /// </summary>
        public static class Difficulty
        {
            public const int X = 191;
            public const int Y = 80;
            public const int Width = 262;
            public const int Height = 50;
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Color TextColor => Color.Yellow;
        }
        
        /// <summary>
        /// Difficulty sprite configuration for 6_Difficulty.png spritesheet
        /// Image is 262x600 with 50px height per sprite
        /// </summary>
        public static class DifficultySprite
        {
            public const int X = 191;
            public const int Y = 80;
            public const int SpriteWidth = 262;
            public const int SpriteHeight = 50;
            public const int TotalHeight = 600;
            public const int SpritesPerColumn = TotalHeight / SpriteHeight; // 12 sprites
            
            // Background rectangle configuration
            public const int BackgroundWidth = 262;
            public const int BackgroundHeight = 120;
            
            // Sprite indices for difficulty levels
            public const int MasterIndex = 5;     // 6th entry (0-based index 5)
            public const int BasicIndex = 6;      // 7th entry (0-based index 6)
            public const int AdvancedIndex = 7;   // 8th entry (0-based index 7)
            public const int ExtremeIndex = 8;    // 9th entry (0-based index 8)
            public const int RealIndex = 11;      // 12th entry (0-based index 11)
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 SpriteSize => new Vector2(SpriteWidth, SpriteHeight);
            public static Vector2 BackgroundSize => new Vector2(BackgroundWidth, BackgroundHeight);
            
            /// <summary>
            /// Gets the sprite index for a given difficulty level
            /// </summary>
            public static int GetSpriteIndex(int difficultyLevel)
            {
                return difficultyLevel switch
                {
                    0 => BasicIndex,      // Basic
                    1 => AdvancedIndex,   // Advanced
                    2 => ExtremeIndex,    // Extreme
                    3 => MasterIndex,     // Master
                    4 => RealIndex,       // Ultimate/Real
                    _ => BasicIndex       // Default to Basic
                };
            }
        }
        
        /// <summary>
        /// Difficulty level number configuration for 6_LevelNumber.png bitmap font
        /// </summary>
        public static class DifficultyLevelNumber
        {
            public const int X = 191;
            public const int Y = 130;
            
            public static Vector2 Position => new Vector2(X, Y);
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