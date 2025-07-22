using Microsoft.Xna.Framework;
using System;

namespace DTX.Stage
{
    /// <summary>
    /// UI layout configuration for PerformanceStage based on NX specifications
    /// Contains all positioning, sizing, and color constants for the 9-lane GITADORA XG layout
    /// </summary>
    public static class PerformanceUILayout
    {
        #region Screen Configuration
        
        /// <summary>
        /// NX screen resolution - 1280×720 letterboxed 16:9
        /// </summary>
        public static readonly Vector2 ScreenResolution = new Vector2(1280, 720);
        
        /// <summary>
        /// Screen width in pixels
        /// </summary>
        public const int ScreenWidth = 1280;
        
        /// <summary>
        /// Screen height in pixels
        /// </summary>
        public const int ScreenHeight = 720;
        
        #endregion
        
        #region Lane System Configuration
        
        /// <summary>
        /// Number of lanes in GITADORA XG layout
        /// </summary>
        public const int LaneCount = 9;
        
        /// <summary>
        /// Width of each lane in pixels
        /// </summary>
        public const int LaneWidth = 64;
        
        /// <summary>
        /// Gap between lanes in pixels
        /// </summary>
        public const int LaneGap = 8;
        
        /// <summary>
        /// X position of the first lane (centers all 9 lanes)
        /// </summary>
        public const int FirstLaneX = 316;
        
        /// <summary>
        /// Y position of the judgement line
        /// </summary>
        public const int JudgementLineY = 560;
        
        /// <summary>
        /// Height of the lane area (full screen height)
        /// </summary>
        public const int LaneHeight = ScreenHeight;
        
        #endregion
        
        #region Lane Order and Names
        
        /// <summary>
        /// Lane types in left-to-right order for GITADORA XG layout
        /// </summary>
        public enum LaneType
        {
            LC = 0,  // Left Cymbal
            LP = 1,  // Left Pedal
            HH = 2,  // Hi-Hat
            SD = 3,  // Snare Drum
            BD = 4,  // Bass Drum
            HT = 5,  // High Tom
            LT = 6,  // Low Tom
            FT = 7,  // Floor Tom
            CY = 8   // Right Cymbal
        }
        
        /// <summary>
        /// Lane names for display purposes
        /// </summary>
        public static readonly string[] LaneNames = new string[]
        {
            "LC", "LP", "HH", "SD", "BD", "HT", "LT", "FT", "CY"
        };
        
        #endregion
        
        #region Lane Colors (NX Default)
        
        /// <summary>
        /// Default lane colors for placeholder display (NX specification)
        /// </summary>
        public static readonly Color[] DefaultLaneColors = new Color[]
        {
            new Color(0xA0, 0x40, 0xFF), // LC - Purple
            new Color(0xFF, 0xC8, 0x00), // LP - Yellow
            new Color(0xFF, 0xC8, 0x00), // HH - Yellow
            new Color(0xFF, 0x40, 0x40), // SD - Red
            new Color(0xFF, 0x80, 0x00), // BD - Orange
            new Color(0x00, 0xC8, 0xFF), // HT - Light Blue
            new Color(0x00, 0x80, 0xFF), // LT - Blue
            new Color(0x00, 0xFF, 0x80), // FT - Green
            new Color(0xFF, 0x64, 0xC8)  // CY - Pink
        };
        
        #endregion
        
        #region UI Element Positions
        
        /// <summary>
        /// Score display position (top-right)
        /// </summary>
        public static readonly Vector2 ScorePosition = new Vector2(1080, 40);
        
        /// <summary>
        /// Combo display position (center-top of lanes)
        /// </summary>
        public static readonly Vector2 ComboPosition = new Vector2(640, 280);
        
        /// <summary>
        /// Life gauge position (top-left)
        /// </summary>
        public static readonly Vector2 GaugePosition = new Vector2(60, 40);
        
        /// <summary>
        /// Life gauge size
        /// </summary>
        public static readonly Vector2 GaugeSize = new Vector2(260, 18);
        
        #endregion
        
        #region Background Configuration

        /// <summary>
        /// Default background image path (references TexturePath for consistency)
        /// </summary>
        public const string DefaultBackgroundPath = "Graphics/7_background.jpg";

        /// <summary>
        /// Fallback background color when image loading fails
        /// </summary>
        public static readonly Color FallbackBackgroundColor = new Color(0x10, 0x10, 0x10);

        #endregion
        
        #region Lane Position Calculations
        
        /// <summary>
        /// Calculate the X position of a lane by index
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>X position of the lane center</returns>
        public static int GetLaneX(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= LaneCount)
                throw new ArgumentOutOfRangeException(nameof(laneIndex), 
                    $"Lane index must be between 0 and {LaneCount - 1}");
            
            return FirstLaneX + (laneIndex * (LaneWidth + LaneGap)) + (LaneWidth / 2);
        }
        
        /// <summary>
        /// Calculate the left edge X position of a lane by index
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>X position of the lane's left edge</returns>
        public static int GetLaneLeftX(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= LaneCount)
                throw new ArgumentOutOfRangeException(nameof(laneIndex), 
                    $"Lane index must be between 0 and {LaneCount - 1}");
            
            return FirstLaneX + (laneIndex * (LaneWidth + LaneGap));
        }
        
        /// <summary>
        /// Calculate the right edge X position of a lane by index
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>X position of the lane's right edge</returns>
        public static int GetLaneRightX(int laneIndex)
        {
            return GetLaneLeftX(laneIndex) + LaneWidth;
        }
        
        /// <summary>
        /// Get the rectangle bounds for a lane
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>Rectangle representing the lane bounds</returns>
        public static Rectangle GetLaneRectangle(int laneIndex)
        {
            return new Rectangle(GetLaneLeftX(laneIndex), 0, LaneWidth, LaneHeight);
        }
        
        /// <summary>
        /// Get the color for a specific lane
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>Color for the lane</returns>
        public static Color GetLaneColor(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= LaneCount)
                throw new ArgumentOutOfRangeException(nameof(laneIndex), 
                    $"Lane index must be between 0 and {LaneCount - 1}");
            
            return DefaultLaneColors[laneIndex];
        }
        
        #endregion
        
        #region Future Skin Loading (TODO)
        
        // TODO: Add skin loading functionality in future phases
        // TODO: Load lane colors from skin configuration
        // TODO: Load UI element positions from skin configuration
        // TODO: Support custom background images per song
        // TODO: Add lane width/spacing customization
        // TODO: Support different screen resolutions with scaling
        
        #endregion
    }
}
