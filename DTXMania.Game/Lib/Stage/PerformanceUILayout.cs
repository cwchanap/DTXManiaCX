using Microsoft.Xna.Framework;
using System;

namespace DTXMania.Game.Lib.Stage
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
        
        #region Performance Assets Layout (7_* files)
        
        /// <summary>
        /// Background configuration - place: {0,0,1,1} fullscreen
        /// </summary>
        public static class Background
        {
            /// <summary>
            /// Background bounds - place: {0,0,1,1} for 1280×720
            /// </summary>
            public static readonly Rectangle Bounds = new Rectangle(0, 0, 1280, 720);
            public const string AssetPath = "Graphics/7_background.jpg";
            public const string VideoAssetPath = "Graphics/7_background.mp4";
        }
        
        /// <summary>
        /// Shutter elements - using normalized coordinates
        /// </summary>
        public static class Shutters
        {
            /// <summary>
            /// Top shutter - place: {0,0,1,0.10} for 1280×720
            /// </summary>
            public static readonly Rectangle TopShutter = new Rectangle(0, 0, 1280, (int)(0.10f * 720));
            public const string TopShutterAssetPath = "Graphics/7_shutter_up.png";
            public static readonly Rectangle TopShutterPadding = new Rectangle(2, 2, 2, 2); // padding: 2,2,2,2
            
            /// <summary>
            /// Bottom shutter - place: {0,0.90,1,0.10} for 1280×720
            /// </summary>
            public static readonly Rectangle BottomShutter = new Rectangle(0, (int)(0.90f * 720), 1280, (int)(0.10f * 720));
            public const string BottomShutterAssetPath = "Graphics/7_shutter_down.png";
            public static readonly Rectangle BottomShutterPadding = new Rectangle(2, 2, 2, 2); // padding: 2,2,2,2
        }
        
        /// <summary>
        /// Lane area configuration - using normalized coordinates {0.10,0.08,0.58,0.78}
        /// Parent "lane block" for all lane-related assets
        /// </summary>
        public static class LaneArea
        {
            /// <summary>
            /// Main lane background - place: {0.10,0.08,0.58,0.78} for 1280×720
            /// </summary>
            public static readonly Rectangle LaneBackground = new Rectangle((int)(0.10f * 1280), (int)(0.08f * 720), (int)(0.58f * 1280), (int)(0.78f * 720));
            public const string LaneBackgroundAssetPath = "Graphics/7_lane_bg.png";
            
            /// <summary>
            /// Lane divider/line overlay - same place as lane_bg
            /// </summary>
            public static readonly Rectangle LaneDivider = new Rectangle((int)(0.10f * 1280), (int)(0.08f * 720), (int)(0.58f * 1280), (int)(0.78f * 720));
            public const string LaneDividerAssetPath = "Graphics/7_lane_divider.png";
            
            /// <summary>
            /// Lane flash overlay - same place as lane_bg, additive blend
            /// </summary>
            public static readonly Rectangle LaneFlash = new Rectangle((int)(0.10f * 1280), (int)(0.08f * 720), (int)(0.58f * 1280), (int)(0.78f * 720));
            public const string LaneFlashAssetPath = "Graphics/7_lane_flush.png";
            
            /// <summary>
            /// Individual column properties - calculated from lane block
            /// For 12-lane example: each column width = 0.58/12 of screen width
            /// </summary>
            public const int ColumnCount = 9; // DTXMania uses 9 lanes
            public static float ColumnWidthNormalized => 0.58f / ColumnCount; // Each column as fraction of screen width
            
            /// <summary>
            /// Get normalized column X position by index (0-8)
            /// </summary>
            public static float GetColumnXNormalized(int columnIndex)
            {
                return 0.10f + (columnIndex * ColumnWidthNormalized);
            }
            
            /// <summary>
            /// Get column rectangle by index (0-8) in screen coordinates
            /// </summary>
            public static Rectangle GetColumnRectangle(int columnIndex)
            {
                var x = (int)(GetColumnXNormalized(columnIndex) * 1280);
                var width = (int)(ColumnWidthNormalized * 1280);
                return new Rectangle(x, (int)(0.08f * 720), width, (int)(0.78f * 720));
            }
        }
        
        /// <summary>
        /// Judgement line (hit line where notes are judged) - using normalized coordinates {0.10,0.84,0.58,0.01}
        /// </summary>
        public static class JudgementLineAssets
        {
            /// <summary>
            /// Judgement line bounds - place: {0.10,0.84,0.58,0.01} for 1280×720
            /// </summary>
            public static readonly Rectangle Bounds = new Rectangle((int)(0.10f * 1280), (int)(0.84f * 720), (int)(0.58f * 1280), (int)(0.01f * 720));
            public const string AssetPath = "Graphics/7_judge_line.png";
            public static readonly Rectangle Padding = new Rectangle(1, 1, 1, 1); // padding: 1,1,1,1
        }
        
        /// <summary>
        /// Life gauge configuration - using normalized coordinates {0.20,0.90,0.60,0.05}
        /// </summary>
        public static class LifeGaugeAssets
        {
            /// <summary>
            /// Gauge background (7_gauge_base.png) - place: {0.20,0.90,0.60,0.05} for 1280×720
            /// </summary>
            public static readonly Rectangle Background = new Rectangle((int)(0.20f * 1280), (int)(0.90f * 720), (int)(0.60f * 1280), (int)(0.05f * 720));
            public const string BackgroundAssetPath = "Graphics/7_gauge_base.png";
            
            /// <summary>
            /// Gauge fill (7_gauge_fill.png) - same place, width clipped by life %
            /// </summary>
            public static readonly Rectangle Fill = new Rectangle((int)(0.20f * 1280), (int)(0.90f * 720), (int)(0.60f * 1280), (int)(0.05f * 720));
            public const string FillAssetPath = "Graphics/7_gauge_fill.png";
        }
        
        /// <summary>
        /// Song progress bar configuration - using normalized coordinates {0.10,0.95,0.58,0.02}
        /// </summary>
        public static class SongProgressAssets
        {
            /// <summary>
            /// Progress background (7_progress_base.png) - place: {0.10,0.95,0.58,0.02} for 1280×720
            /// </summary>
            public static readonly Rectangle Background = new Rectangle((int)(0.10f * 1280), (int)(0.95f * 720), (int)(0.58f * 1280), (int)(0.02f * 720));
            public const string BackgroundAssetPath = "Graphics/7_progress_base.png";
            
            /// <summary>
            /// Progress fill (7_progress_fill.png) - same place, clipped by song position
            /// </summary>
            public static readonly Rectangle Fill = new Rectangle((int)(0.10f * 1280), (int)(0.95f * 720), (int)(0.58f * 1280), (int)(0.02f * 720));
            public const string FillAssetPath = "Graphics/7_progress_fill.png";
        }
        
        /// <summary>
        /// Combo display configuration - using normalized coordinates {0.46,0.23,0.16,0.09}
        /// Atlas-grid: rows:1 cols:11, includes 0–9 plus separator (x or %)
        /// </summary>
        public static class ComboDigitsAssets
        {
            /// <summary>
            /// Combo digits bounds - place: {0.46,0.23,0.16,0.09} for 1280×720
            /// </summary>
            public static readonly Rectangle Bounds = new Rectangle((int)(0.46f * 1280), (int)(0.23f * 720), (int)(0.16f * 1280), (int)(0.09f * 720));
            public const string AssetPath = "Graphics/7_combo_digits.png";
            
            // Atlas-grid properties
            public const int AtlasRows = 1;
            public const int AtlasCols = 11;
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2); // padding: 2,2,2,2
        }
        
        /// <summary>
        /// Score display configuration - using normalized coordinates {0.82,0.06,0.16,0.08}
        /// Atlas-grid: rows:1 cols:12, includes 0–9 + separators/signs
        /// </summary>
        public static class ScoreDigitsAssets
        {
            /// <summary>
            /// Score digits bounds - place: {0.82,0.06,0.16,0.08} for 1280×720
            /// </summary>
            public static readonly Rectangle Bounds = new Rectangle((int)(0.82f * 1280), (int)(0.06f * 720), (int)(0.16f * 1280), (int)(0.08f * 720));
            public const string AssetPath = "Graphics/7_score_digits.png";
            
            // Atlas-grid properties
            public const int AtlasRows = 1;
            public const int AtlasCols = 12;
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2); // padding: 2,2,2,2
        }
        
        /// <summary>
        /// Judgement text display - using normalized coordinates {0.39,0.77,0.22,0.07}
        /// Atlas-grid: rows:1 cols:5 (PERFECT/GREAT/GOOD/POOR/MISS)
        /// </summary>
        public static class JudgementTextAssets
        {
            /// <summary>
            /// Judgement text bounds - place: {0.39,0.77,0.22,0.07} for 1280×720
            /// </summary>
            public static readonly Rectangle Bounds = new Rectangle((int)(0.39f * 1280), (int)(0.77f * 720), (int)(0.22f * 1280), (int)(0.07f * 720));
            
            public const string JudgeStringsAssetPath = "Graphics/7_judge.png";
            
            // Atlas-grid properties
            public const int AtlasRows = 1;
            public const int AtlasCols = 5;
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2); // padding: 2,2,2,2
            
            // Frame indices for judgement types
            public const int PerfectFrame = 0;
            public const int GreatFrame = 1;
            public const int GoodFrame = 2;
            public const int PoorFrame = 3;
            public const int MissFrame = 4;
        }
        
        /// <summary>
        /// Early/Late indicator configuration - using normalized coordinates
        /// Atlas-grid: rows:1 cols:2 (EARLY/LATE)
        /// </summary>
        public static class TimingIndicatorAssets
        {
            /// <summary>
            /// Early indicator bounds - L:{0.335,0.77,0.05,0.05} for 1280×720
            /// </summary>
            public static readonly Rectangle EarlyBounds = new Rectangle((int)(0.335f * 1280), (int)(0.77f * 720), (int)(0.05f * 1280), (int)(0.05f * 720));
            
            /// <summary>
            /// Late indicator bounds - R:{0.615,0.77,0.05,0.05} for 1280×720
            /// </summary>
            public static readonly Rectangle LateBounds = new Rectangle((int)(0.615f * 1280), (int)(0.77f * 720), (int)(0.05f * 1280), (int)(0.05f * 720));
            
            public const string LagNumbersAssetPath = "Graphics/7_lag.png";
            
            // Atlas-grid properties
            public const int AtlasRows = 1;
            public const int AtlasCols = 2;
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2); // padding: 2,2,2,2
            
            // Frame indices
            public const int EarlyFrame = 0;
            public const int LateFrame = 1;
        }
        
        /// <summary>
        /// Overlay screens (pause and danger) - fullscreen {0,0,1,1}
        /// </summary>
        public static class OverlayAssets
        {
            /// <summary>
            /// Pause overlay - place: {0,0,1,1} for 1280×720
            /// </summary>
            public static readonly Rectangle PauseOverlay = new Rectangle(0, 0, 1280, 720);
            public const string PauseOverlayAssetPath = "Graphics/7_pause_overlay.png";
            
            /// <summary>
            /// Danger overlay - place: {0,0,1,1} for 1280×720, fullscreen tint
            /// </summary>
            public static readonly Rectangle DangerOverlay = new Rectangle(0, 0, 1280, 720);
            public const string DangerOverlayAssetPath = "Graphics/7_danger_overlay.png";
        }
        
        /// <summary>
        /// Gauge system constants
        /// </summary>
        public static class GaugeSettings
        {
            public const float FailureThreshold = 2.0f;
            public const float StartingLife = 50.0f;
            public const float DangerThreshold = 20.0f;
            
            public static class LifeAdjustment
            {
                public const float Just = 2.0f;
                public const float Great = 1.5f;
                public const float Good = 1.0f;
                public const float Poor = -1.5f;
                public const float Miss = -3.0f;
            }
        }
        
        /// <summary>
        /// Notes atlas configuration - atlas-grid for different lane chips
        /// </summary>
        public static class NotesAssets
        {
            /// <summary>
            /// Notes atlas - one cell per lane-chip (LC/HH/SN/…)
            /// Atlas-grid with spacing:2 margin:2, pivot: {0.5,0.5}
            /// </summary>
            public const string NotesAtlasAssetPath = "Graphics/7_notes.png";
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2); // padding: 2,2,2,2
            // Grid properties will be auto-detected by sprite manifest builder
        }
        
        /// <summary>
        /// Long notes atlas configuration - atlas-grid for head/body/tail frames
        /// </summary>
        public static class LongNotesAssets
        {
            /// <summary>
            /// Long notes atlas - head/body/tail frames
            /// Atlas-grid with spacing:2 margin:2, pivot: {0.5,0}
            /// </summary>
            public const string LongNotesAtlasAssetPath = "Graphics/7_longnotes.png";
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2); // padding: 2,2,2,2
            // Grid properties will be auto-detected by sprite manifest builder
        }
        
        /// <summary>
        /// Explosion animation configuration - atlas-grid for hit effects
        /// </summary>
        public static class ExplosionAssets
        {
            /// <summary>
            /// Explosion animation - place: near judge line
            /// Atlas-grid: rows:2 cols:4 (8 frames @ ~30fps), pivot: {0.5,0.5}, additive blend
            /// </summary>
            public const string ExplosionAssetPath = "Graphics/7_explosion.png";
            public const int AtlasRows = 2;
            public const int AtlasCols = 4;
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2); // padding: 2,2,2,2
            
            // Animation properties
            public const int AnimationFps = 30;
            public const bool AnimationLoop = false;
            public const int TotalFrames = 8;
        }
        
        #endregion
    }
}
