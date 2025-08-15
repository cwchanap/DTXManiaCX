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
        
        #region Lane Layout Constants
        
        /// <summary>
        /// Lane layout for GITADORA 9-lane drumming layout
        /// </summary>
        public const int LaneCount = 9;
        public const int LaneWidth = 64;
        public const int LaneSpacing = 8;
        public const int LanesStartX = 400; // Starting X position for leftmost lane
        public const int JudgementLineY = 600; // Y position of judgement line
        
        /// <summary>
        /// Get the left X position of a specific lane (0-indexed)
        /// </summary>
        public static int GetLaneLeftX(int laneIndex)
        {
            return LanesStartX + (laneIndex * (LaneWidth + LaneSpacing));
        }
        
        /// <summary>
        /// Get the right X position of a specific lane (0-indexed)
        /// </summary>
        public static int GetLaneRightX(int laneIndex)
        {
            return GetLaneLeftX(laneIndex) + LaneWidth;
        }
        
        /// <summary>
        /// Get the center X position of a specific lane (0-indexed)
        /// </summary>
        public static int GetLaneCenterX(int laneIndex)
        {
            return GetLaneLeftX(laneIndex) + (LaneWidth / 2);
        }
        
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
    }
}