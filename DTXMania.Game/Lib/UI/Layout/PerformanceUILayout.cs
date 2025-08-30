using Microsoft.Xna.Framework;
using System;

namespace DTXMania.Game.Lib.UI.Layout
{
    /// <summary>
    /// UI layout configuration for PerformanceStage based on DTXManiaNX specifications
    /// Contains all positioning, sizing, and color constants for the drum gameplay UI
    /// Resolution: 1280×720 fixed UI space with pixel-perfect rendering
    /// </summary>
    public static class PerformanceUILayout
    {
        #region Screen Configuration
        
        /// <summary>
        /// DTXManiaNX screen resolution - 1280×720 letterboxed 16:9
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
        
        public static Vector2 ScreenCenter => new Vector2(ScreenWidth / 2, ScreenHeight / 2);
        public static Vector2 ScreenSize => new Vector2(ScreenWidth, ScreenHeight);
        
        /// <summary>
        /// Judgeline Y position (non-reverse layout)
        /// </summary>
        public const int JudgelineY = 600;
        
        /// <summary>
        /// Legacy judgement line Y position (compatibility)
        /// </summary>
        public const int JudgementLineY = JudgelineY;
        
        /// <summary>
        /// Ready state pulse frequency for text animation
        /// </summary>
        public const double ReadyPulseFrequency = 2.0;
        
        #endregion
        
        #region Drum Lane System Configuration
        
        /// <summary>
        /// Number of drum lanes (classic DTXMania drum layout)
        /// </summary>
        public const int LaneCount = 10;
        
        /// <summary>
        /// Lane types in corrected DTXMania order: LC, HH, LP, SN, HT, DB, LT, FT, CY
        /// </summary>
        public enum LaneType
        {
            LC = 0,  // Left Cymbal
            HH = 1,  // Hi-Hat
            LP = 2,  // Left Pedal
            SN = 3,  // Snare Drum
            HT = 4,  // High Tom
            DB = 5,  // Bass Drum (Drum Bass)
            LT = 6,  // Low Tom
            FT = 7,  // Floor Tom
            CY = 8,  // Cymbal (Right Cymbal)
            RD = 9   // Right Pedal (Ride)
        }
        
        /// <summary>
        /// Lane names for display purposes - corrected order: LC, HH, LP, SN, HT, DB, LT, FT, CY
        /// </summary>
        public static readonly string[] LaneNames = new string[]
        {
            "LC", "HH", "LP", "SN", "HT", "DB", "LT", "FT", "CY", "RD"
        };
        
        /// <summary>
        /// Lane center X positions based on DTXManiaNX layout specifications
        /// Reordered for gameplay order: LC, HH, LP, SN, HT, DB, LT, FT, CY
        /// These positions are pixel-perfect from 7_Paret.png sprite sheet layout
        /// </summary>
        public static readonly int[] LaneCenterX = new int[]
        {
            295 + 36,  // Lane 0: LC - X=295, width=72, center=295+36=331
            367 + 24,  // Lane 1: HH - X=367, width=49, center=367+24.5≈391
            416 + 25,  // Lane 2: Missing - X=416, width=51, center=416+25.5≈441
            467 + 28,  // Lane 3: SD - X=467, width=57, center=467+28.5≈495
            524 + 24,  // Lane 4: HT - X=524, width=49, center=524+24.5≈548
            573 + 34,  // Lane 5: LP - X=573, width=69, center=573+34.5≈607
            642 + 24,  // Lane 6: LT - X=642, width=49, center=642+24.5≈666
            691 + 27,  // Lane 7: FT - X=691, width=54, center=691+27=718
            745 + 35,  // Lane 8: RC - X=745, width=70, center=745+35=780
            815 + 19   // Lane 9: RD - X=815, width=38, center=815+19=834
        };
        
        /// <summary>
        /// Lane widths based on 7_Paret.png sprite sheet
        /// Reordered for gameplay order: LC, HH, LP, SN, HT, DB, LT, FT, CY
        /// </summary>
        public static readonly int[] LaneWidths = new int[]
        {
            72, // Lane 0: LC - Width matches source rectangle (72)
            49, // Lane 1: HH - Width matches source rectangle (49)
            51, // Lane 2: Missing lane - Width calculated (121+51=172, so width=51)
            57, // Lane 3: SD - Width matches source rectangle (57)
            49, // Lane 4: HT - Width matches source rectangle (49)
            69, // Lane 5: LP - Width matches source rectangle (69)
            49, // Lane 6: LT - Width matches source rectangle (49)
            54, // Lane 7: FT - Width matches source rectangle (54)
            70, // Lane 8: RC - Width matches source rectangle (70)
            38  // Lane 9: RD - Width matches source rectangle (38)
        };
        
        /// <summary>
        /// Lane left X positions based on 7_Paret.png sprite sheet  
        /// Corrected order: LC, HH, SD, LP, HT, LT, FT, RC, RD
        /// </summary>
        public static readonly int[] LaneLeftX = new int[]
        {
            295, // Lane 0: LC - Left Cymbal  
            367, // Lane 1: HH - Hi-Hat
            416, // Lane 2: Missing lane between HH and SD (seamless after HH)
            467, // Lane 3: SD - Snare Drum
            524, // Lane 4: HT - High Tom
            573, // Lane 5: LP - Bass Drum (Low Pedal)  
            642, // Lane 6: LT - Low Tom  
            691, // Lane 7: FT - Floor Tom
            745, // Lane 8: RC - Ride Cymbal
            815  // Lane 9: RD - Ride (alternate position)
        };
        
        /// <summary>
        /// Lane height - stops before gauge area to prevent overlap
        /// Gauge starts at Y=626, so lanes stop at Y=620 with padding
        /// </summary>
        public const int LaneHeight = 620;
        
        #endregion
        
        #region Lane Position Calculations
        
        /// <summary>
        /// Get the center X position of a lane by index
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>X position of the lane center</returns>
        public static int GetLaneX(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= LaneCount)
                throw new ArgumentOutOfRangeException(nameof(laneIndex), 
                    $"Lane index must be between 0 and {LaneCount - 1}");
            
            return LaneCenterX[laneIndex];
        }
        
        /// <summary>
        /// Get the left edge X position of a lane by index
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>X position of the lane's left edge</returns>
        public static int GetLaneLeftX(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= LaneCount)
                throw new ArgumentOutOfRangeException(nameof(laneIndex), 
                    $"Lane index must be between 0 and {LaneCount - 1}");
            
            return LaneLeftX[laneIndex];
        }
        
        /// <summary>
        /// Get the width of a lane by index
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>Width of the lane in pixels</returns>
        public static int GetLaneWidth(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= LaneCount)
                throw new ArgumentOutOfRangeException(nameof(laneIndex), 
                    $"Lane index must be between 0 and {LaneCount - 1}");
            
            return LaneWidths[laneIndex];
        }
        
        /// <summary>
        /// Calculate the right edge X position of a lane by index
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>X position of the lane's right edge</returns>
        public static int GetLaneRightX(int laneIndex)
        {
            return GetLaneLeftX(laneIndex) + GetLaneWidth(laneIndex);
        }
        
        /// <summary>
        /// Get the center X position of a specific lane (0-indexed)
        /// </summary>
        public static int GetLaneCenterX(int laneIndex)
        {
            return GetLaneX(laneIndex);
        }
        
        /// <summary>
        /// Get the rectangle bounds for a lane
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>Rectangle representing the lane bounds</returns>
        public static Rectangle GetLaneRectangle(int laneIndex)
        {
            return new Rectangle(GetLaneLeftX(laneIndex), 0, GetLaneWidth(laneIndex), LaneHeight);
        }
        
        /// <summary>
        /// Get lane color for compatibility (using default drum colors)
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>Color for the lane</returns>
        public static Color GetLaneColor(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= LaneCount)
                throw new ArgumentOutOfRangeException(nameof(laneIndex), 
                    $"Lane index must be between 0 and {LaneCount - 1}");
            
            // Default drum colors for visual feedback - all 10 lanes
            var drumColors = new Color[]
            {
                new Color(0xA0, 0x40, 0xFF), // Lane 0: LC - Purple
                new Color(0xFF, 0xC8, 0x00), // Lane 1: HH - Yellow
                new Color(0xFF, 0x60, 0xFF), // Lane 2: Missing lane - Magenta
                new Color(0xFF, 0x40, 0x40), // Lane 3: SD - Red
                new Color(0x00, 0xC8, 0xFF), // Lane 4: HT - Light Blue
                new Color(0xFF, 0x80, 0x00), // Lane 5: LP (Bass) - Orange
                new Color(0x00, 0x80, 0xFF), // Lane 6: LT - Blue
                new Color(0x00, 0xFF, 0x80), // Lane 7: FT - Green
                new Color(0xFF, 0x64, 0xC8), // Lane 8: RC - Pink
                new Color(0xFF, 0x64, 0xC8)  // Lane 9: RD - Pink
            };
            
            return drumColors[laneIndex];
        }
        
        #endregion
        
        #region Score Display (7_score numbersGD.png)
        
        /// <summary>
        /// Score display configuration (top-left)
        /// </summary>
        public static class Score
        {
            public static readonly Vector2 LabelPosition = new Vector2(40, 13);
            public static readonly Vector2 LabelSize = new Vector2(86, 28);
            public static readonly Vector2 FirstDigitPosition = new Vector2(40, 41);
            public static readonly Vector2 DigitSize = new Vector2(36, 50);
            public const int DigitSpacing = 34; // with -2px overlap
            public const int MaxDigits = 7;
        }
        
        #endregion
        
        #region Combo Display (ScreenPlayDrums combo.png)
        
        /// <summary>
        /// Combo display configuration (top-right)
        /// </summary>
        public static class Combo
        {
            public static readonly Vector2 BasePosition = new Vector2(1245, 60); // anchor top-right
            public static readonly Vector2 CombobombSize = new Vector2(360, 340);
        }
        
        #endregion
        
        #region Gauge Display (7_Gauge.png, 7_gauge_bar.png)
        
        /// <summary>
        /// Life gauge configuration
        /// </summary>
        public static class Gauge
        {
            public static readonly Vector2 FramePosition = new Vector2(294, 626);
            public static readonly Vector2 FillOrigin = new Vector2(314, 635); // FramePos + (20,9)
            public const int FillHeight = 31;
            
            /// <summary>
            /// Hi-Speed badge position (at gauge right end)
            /// </summary>
            public static class HiSpeedBadge
            {
                public static readonly Vector2 Position = new Vector2(294 + 200 - 37, 634); // estimated frame width
                public static readonly Vector2 Size = new Vector2(42, 48);
                public const int CellHeight = 48;
            }
        }
        
        #endregion
        
        #region Song Progress Bar (7_Drum_Progress_bg.png)
        
        /// <summary>
        /// Song progress bar configuration (right side)
        /// </summary>
        public static class Progress
        {
            public static readonly Rectangle FrameBounds = new Rectangle(853, 0, 60, 540);
            public static readonly Rectangle BarBounds = new Rectangle(855, 15, 20, 540);
            public static readonly Rectangle MarkerBounds = new Rectangle(877, 15, 8, 540);
        }
        
        #endregion
        
        #region Skill Panel (7_SkillPanel.png)
        
        /// <summary>
        /// Skill/Status panel configuration (left side)
        /// </summary>
        public static class SkillPanel
        {
            public static readonly Vector2 PanelPosition = new Vector2(22, 250);
            
            /// <summary>
            /// Difficulty icon configuration
            /// </summary>
            public static class DifficultyIcon
            {
                public static readonly Rectangle Bounds = new Rectangle(36, 516, 60, 60);
                public static readonly Vector2 CellSize = new Vector2(60, 60);
            }
            
            /// <summary>
            /// Difficulty level number configuration
            /// </summary>
            public static class LevelNumber
            {
                public static readonly Vector2 StartPosition = new Vector2(40, 540); // classic: (48, 540)
                public static readonly Vector2 DigitSize = new Vector2(16, 19); // estimated
                public const int DigitSpacing = 16;
            }
            
            /// <summary>
            /// Large skill percentage display
            /// </summary>
            public static class SkillPercent
            {
                public static readonly Vector2 NumbersPosition = new Vector2(80, 527);
                public static readonly Vector2 PercentPosition = new Vector2(239, 537);
                public static readonly Vector2 MaxBadgePosition = new Vector2(149, 527);
                public const int DigitWidth = 28;
            }
            
            /// <summary>
            /// Small judgement counts and percentages
            /// </summary>
            public static class JudgementCounts
            {
                public static readonly Vector2 PerfectCountPos = new Vector2(102, 322);
                public static readonly Vector2 GreatCountPos = new Vector2(102, 352);
                public static readonly Vector2 GoodCountPos = new Vector2(102, 382);
                public static readonly Vector2 PoorCountPos = new Vector2(102, 412);
                public static readonly Vector2 MissCountPos = new Vector2(102, 442);
                public static readonly Vector2 MaxComboCountPos = new Vector2(102, 472);
                
                public static readonly Vector2 PerfectPercentPos = new Vector2(189, 322);
                public static readonly Vector2 GreatPercentPos = new Vector2(189, 352);
                public static readonly Vector2 GoodPercentPos = new Vector2(189, 382);
                public static readonly Vector2 PoorPercentPos = new Vector2(189, 412);
                public static readonly Vector2 MissPercentPos = new Vector2(189, 442);
                public static readonly Vector2 MaxComboPercentPos = new Vector2(189, 472);
                
                public static readonly Vector2 DigitSize = new Vector2(20, 19);
                public const int MaxDigits = 4;
            }
            
            /// <summary>
            /// Timing offset displays (Early/Late)
            /// </summary>
            public static class TimingOffsets
            {
                public static readonly Vector2 EarlyPosition = new Vector2(192, 585);
                public static readonly Vector2 LatePosition = new Vector2(267, 585);
                public static readonly Vector2 DigitSize = new Vector2(15, 19);
                public const int MaxDigits = 4;
            }
        }
        
        #endregion
        
        #region Background Configuration

        /// <summary>
        /// Fallback background color when image loading fails
        /// </summary>
        public static readonly Color FallbackBackgroundColor = new Color(0x10, 0x10, 0x10);

        #endregion
        
        #region Legacy Compatibility Properties
        
        /// <summary>
        /// Legacy score position (compatibility)
        /// </summary>
        public static readonly Vector2 ScorePosition = Score.FirstDigitPosition;
        
        /// <summary>
        /// Legacy combo position (compatibility)
        /// </summary>
        public static readonly Vector2 ComboPosition = Combo.BasePosition;
        
        /// <summary>
        /// Legacy gauge position (compatibility)
        /// </summary>
        public static readonly Vector2 GaugePosition = Gauge.FramePosition;
        
        /// <summary>
        /// Legacy gauge size (compatibility)
        /// </summary>
        public static readonly Vector2 GaugeSize = new Vector2(200, Gauge.FillHeight);
        
        #endregion
        
        #region Ready Display Constants
        
        /// <summary>
        /// Ready countdown display settings
        /// </summary>
        public const double ReadyCountdownSeconds = 1.0;
        
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
        
        #region Performance Component Constants
        
        /// <summary>
        /// Animation timing constants
        /// </summary>
        public static class Animation
        {
            public const double StandardFrameDuration = 1.0 / 60.0; // 60 fps
            public const float FlashDecayRate = 8.0f;
            public const float SpringAnimationForce = 8.0f;
            public const float TargetScaleLerpRate = 5.0f;
            public const float ScaleDamping = 0.2f;
        }
        
        /// <summary>
        /// Visual effects constants
        /// </summary>
        public static class Effects
        {
            public const int FrameWidth = 8;
            public const int FrameHeight = 32;
            public const float DefaultAlpha = 0.3f;
            public const float ComboHitScale = 1.5f;
            public const float FlashCleanupThreshold = 0.01f;
        }
        
        /// <summary>
        /// Typography constants
        /// </summary>
        public static class Typography
        {
            public const int ComboFontSize = 48;
            public const int ComboLabelFontSize = 20;
            public const int ScoreFontSize = 32;
        }
        
        /// <summary>
        /// Visual styling constants
        /// </summary>
        public static class Visual
        {
            public static readonly Color StandardShadowColor = new Color(0, 0, 0, 128);
            public static readonly Vector2 StandardShadowOffset = new Vector2(2, 2);
            public const int StandardFrameThickness = 2;
        }
        
        /// <summary>
        /// Timing constants
        /// </summary>
        public static class Timing
        {
            public const double BaseLookAheadMs = 1500.0;
            public const double BufferTimeMs = 200.0;
            public const float JudgementTextFadeDuration = 0.6f;
            public const float JudgementTextRiseDistance = 30f;
            public const int JudgementTextYOffset = 50;
        }
        
        /// <summary>
        /// Note constants
        /// </summary>
        public static class Notes
        {
            public static readonly Vector2 DefaultSize = new Vector2(32, 16);
            public const int DropGracePeriod = 20;
        }
        
        /// <summary>
        /// Combo display constants
        /// </summary>
        public static class ComboDisplay
        {
            public static readonly Color ShadowColor = new Color(0, 0, 0, 128);
            public static readonly Vector2 ShadowOffset = new Vector2(2, 2);
        }
        
        /// <summary>
        /// Gauge display constants
        /// </summary>
        public static class GaugeDisplay
        {
            public static readonly Color BackgroundColor = new Color(0, 0, 0, 128);
            public const float DefaultValue = 0.5f;
            public const float HighLifeThreshold = 0.8f;
            public const float MediumLifeThreshold = 0.5f;
            public const float LowLifeThreshold = 0.2f;
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
        /// Judgement line constants
        /// </summary>
        public static class JudgementLine
        {
            public const int DefaultThickness = 2;
        }
        
        /// <summary>
        /// Score display constants
        /// </summary>
        public static class ScoreDisplay
        {
            public static readonly Vector2 ShadowOffset = new Vector2(2, 2);
            public const int MaxScore = 9999999;
        }
        
        /// <summary>
        /// Pooled effects constants
        /// </summary>
        public static class PooledEffects
        {
            public const int InitialPoolSize = 100;
            public const int MaxPoolSize = 500;
        }
        
        #endregion
        
        #region DTXManiaNX Performance Assets Layout
        
        /// <summary>
        /// Background configuration - fullscreen
        /// </summary>
        public static class Background
        {
            public static readonly Rectangle Bounds = new Rectangle(0, 0, 1280, 720);
        }
        
        /// <summary>
        /// Stage Failed overlay
        /// </summary>
        public static class StageFailed
        {
            public static readonly Rectangle Bounds = new Rectangle(0, 0, 1280, 720);
        }
        
        /// <summary>
        /// Full Combo overlay
        /// </summary>
        public static class FullCombo
        {
            // Centered, baseline calculation: (1280-w)/2, (720-h)/2
            public static Vector2 GetCenteredPosition(int textureWidth, int textureHeight)
            {
                return new Vector2((1280 - textureWidth) / 2, (720 - textureHeight) / 2);
            }
        }
        
        /// <summary>
        /// Danger tint overlay (tiling)
        /// </summary>
        public static class Danger
        {
            public static readonly Vector2 TileSize = new Vector2(32, 64); // estimated tile size
        }
        
        /// <summary>
        /// Lane strips configuration based on 7_Paret.png sprite sheet
        /// </summary>
        public static class LaneStrips
        {            
            public static readonly Rectangle[] SourceRects = new Rectangle[]
            {
                // All 10 lanes from 7_Paret.png texture in sequential order:
                new Rectangle(0, 0, 72, 720),    // Lane 0: LC -> Src: (0,0,72,720)
                new Rectangle(72, 0, 49, 720),   // Lane 1: HH -> Src: (72,0,49,720)
                new Rectangle(121, 0, 51, 720),  // Lane 2: Missing lane -> Src: (121,0,51,720)
                new Rectangle(172, 0, 57, 720),  // Lane 3: SD -> Src: (172,0,57,720)  
                new Rectangle(229, 0, 49, 720),  // Lane 4: HT -> Src: (229,0,49,720)
                new Rectangle(278, 0, 69, 720),  // Lane 5: LP -> Src: (278,0,69,720)
                new Rectangle(347, 0, 49, 720),  // Lane 6: LT -> Src: (347,0,49,720)
                new Rectangle(396, 0, 54, 720),  // Lane 7: FT -> Src: (396,0,54,720)
                new Rectangle(450, 0, 70, 720),  // Lane 8: RC -> Src: (450,0,70,720) 
                new Rectangle(520, 0, 38, 720),  // Lane 9: RD -> Src: (520,0,38,720)
            };
            
            public static Rectangle GetDestinationRect(int laneIndex)
            {
                return new Rectangle(LaneLeftX[laneIndex], 0, LaneWidths[laneIndex], LaneHeight);
            }
        }
        
        /// <summary>
        /// Lane covers configuration based on 7_lanes_Cover_cls.png
        /// </summary>
        public static class LaneCovers
        {
            // Example cover source rectangles (adjust based on actual sprite sheet)
            public static readonly Rectangle[] CoverSourceRects = new Rectangle[]
            {
                new Rectangle(0, 0, 70, 720),    // LC cover
                new Rectangle(70, 0, 47, 720),   // HH cover
                new Rectangle(117, 0, 54, 720),  // SD cover
                new Rectangle(124, 0, 54, 720),  // BD cover - position varies by layout
                new Rectangle(71, 0, 47, 720),   // HT cover
                new Rectangle(71, 0, 47, 720),   // LT cover
                new Rectangle(71, 0, 52, 720),   // FT cover
                new Rectangle(178, 0, 68, 720),  // RC cover
                new Rectangle(178, 0, 38, 720)   // RD cover
            };
        }
        
        /// <summary>
        /// Shutter animation configuration
        /// </summary>
        public static class Shutter
        {
            public static readonly Vector2 StartPosition = new Vector2(295, 0); // aligned with lane area left
        }
        
        /// <summary>
        /// Hit-bar configuration
        /// </summary>
        public static class HitBar
        {
            public static readonly Vector2 Position = new Vector2(295, JudgelineY); // spans lane region
        }
        
        /// <summary>
        /// Drum notes configuration based on 7_chips_drums.png
        /// </summary>
        public static class DrumNotes
        {
            // Sprite sheet with one cell per pad type
        }
        
        /// <summary>
        /// Hit spark effects - colored per pad
        /// </summary>
        public static class HitSparks
        {
            public static Vector2 GetSparkPosition(int laneIndex)
            {
                return new Vector2(LaneCenterX[laneIndex], JudgelineY);
            }
        }
        
        /// <summary>
        /// Lane flush effects configuration
        /// </summary>
        public static class LaneFlush
        {
            // Frame size for animation
            public static readonly Vector2 FrameSize = new Vector2(42, 128);
            public const int FrameAdvance = 42; // pixels per frame
            public const string FlushReverseAssetPathSuffix = " reverse.png";
        }
        
        /// <summary>
        /// Wailing and bonus effects
        /// </summary>
        public static class WailingBonus
        {
            // Asset paths are handled by TexturePath constants
        }
        
        #endregion
        
        #region Legacy Asset Layouts (for backwards compatibility)
        
        /// <summary>
        /// Legacy compatibility - keeping some existing structures for backwards compatibility
        /// </summary>
        public static class JudgementLineAssets
        {
            public static readonly Rectangle Bounds = new Rectangle(295, JudgelineY - 5, 520, 10); // estimated span
        }
        
        public static class LifeGaugeAssets
        {
            public static readonly Rectangle Background = Gauge.FramePosition.ToRectangle();
            public static readonly Rectangle Fill = new Rectangle((int)Gauge.FillOrigin.X, (int)Gauge.FillOrigin.Y, 200, Gauge.FillHeight); // estimated width
        }
        
        public static class SongProgressAssets
        {
            public static readonly Rectangle Background = Progress.FrameBounds;
            public static readonly Rectangle Fill = Progress.BarBounds;
        }
        
        public static class ComboDigitsAssets
        {
            public static readonly Rectangle Bounds = new Rectangle((int)Combo.BasePosition.X - 100, (int)Combo.BasePosition.Y, 200, 60); // estimated
            public const int AtlasRows = 1;
            public const int AtlasCols = 11;
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2);
        }
        
        public static class ScoreDigitsAssets
        {
            public static readonly Rectangle Bounds = new Rectangle((int)Score.FirstDigitPosition.X, (int)Score.FirstDigitPosition.Y, (int)Score.DigitSize.X * Score.MaxDigits, (int)Score.DigitSize.Y);
            public const int AtlasRows = 1;
            public const int AtlasCols = 12;
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2);
        }
        
        public static class JudgementTextAssets
        {
            public static readonly Rectangle Bounds = new Rectangle((int)(0.39f * 1280), (int)(0.77f * 720), (int)(0.22f * 1280), (int)(0.07f * 720));
            public const int AtlasRows = 1;
            public const int AtlasCols = 5;
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2);
            public const int PerfectFrame = 0;
            public const int GreatFrame = 1;
            public const int GoodFrame = 2;
            public const int PoorFrame = 3;
            public const int MissFrame = 4;
        }
        
        public static class TimingIndicatorAssets
        {
            public static readonly Rectangle EarlyBounds = new Rectangle((int)(0.335f * 1280), (int)(0.77f * 720), (int)(0.05f * 1280), (int)(0.05f * 720));
            public static readonly Rectangle LateBounds = new Rectangle((int)(0.615f * 1280), (int)(0.77f * 720), (int)(0.05f * 1280), (int)(0.05f * 720));
            public const int AtlasRows = 1;
            public const int AtlasCols = 2;
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2);
            public const int EarlyFrame = 0;
            public const int LateFrame = 1;
        }
        
        public static class OverlayAssets
        {
            public static readonly Rectangle PauseOverlay = new Rectangle(0, 0, 1280, 720);
            public static readonly Rectangle DangerOverlay = new Rectangle(0, 0, 1280, 720);
        }
        
        public static class NotesAssets
        {
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2);
        }
        
        public static class LongNotesAssets
        {
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2);
        }
        
        public static class ExplosionAssets
        {
            public const int AtlasRows = 2;
            public const int AtlasCols = 4;
            public static readonly Rectangle Padding = new Rectangle(2, 2, 2, 2);
            public const int AnimationFps = 30;
            public const bool AnimationLoop = false;
            public const int TotalFrames = 8;
        }
        
        #endregion
        
        #region Utility Extensions
        
        /// <summary>
        /// Extension method to convert Vector2 to Rectangle with zero size
        /// </summary>
        public static Rectangle ToRectangle(this Vector2 position)
        {
            return new Rectangle((int)position.X, (int)position.Y, 0, 0);
        }
        
        /// <summary>
        /// Extension method to convert Vector2 to Rectangle with specified size
        /// </summary>
        public static Rectangle ToRectangle(this Vector2 position, Vector2 size)
        {
            return new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
        }
        
        #endregion
    }
}