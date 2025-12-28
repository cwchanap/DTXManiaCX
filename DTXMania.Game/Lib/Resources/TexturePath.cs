namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Centralized constants for all texture file paths used in DTXManiaCX
    /// Follows DTXManiaNX skin system conventions
    /// </summary>
    public static class TexturePath
    {
        #region Background Textures
        
        /// <summary>
        /// Startup stage background texture
        /// </summary>
        public const string StartupBackground = "Graphics/1_background.jpg";
        
        /// <summary>
        /// Title stage background texture
        /// </summary>
        public const string TitleBackground = "Graphics/2_background.jpg";
        
        /// <summary>
        /// Song selection stage background texture
        /// </summary>
        public const string SongSelectionBackground = "Graphics/5_background.jpg";

        /// <summary>
        /// Song transition stage background texture
        /// </summary>
        public const string SongTransitionBackground = "Graphics/6_background.jpg";

        /// <summary>
        /// Performance stage background texture
        /// </summary>
        public const string PerformanceBackground = "Graphics/7_background.jpg";

        /// <summary>
        /// Result stage background texture
        /// </summary>
        public const string ResultBackground = "Graphics/8_background.jpg";

        #endregion
        
        #region UI Panel Textures
        
        /// <summary>
        /// Title stage menu texture
        /// </summary>
        public const string TitleMenu = "Graphics/2_menu.png";
        
        /// <summary>
        /// Song selection header panel texture
        /// </summary>
        public const string SongSelectionHeaderPanel = "Graphics/5_header panel.png";
        
        /// <summary>
        /// Song selection footer panel texture
        /// </summary>
        public const string SongSelectionFooterPanel = "Graphics/5_footer panel.png";
        
        /// <summary>
        /// Song status panel texture
        /// </summary>
        public const string SongStatusPanel = "Graphics/5_status panel.png";
        
        /// <summary>
        /// BPM background texture for status panel
        /// </summary>
        public const string BpmBackground = "Graphics/5_BPM.png";
        
        /// <summary>
        /// Difficulty panel texture for song status panel
        /// </summary>
        public const string DifficultyPanel = "Graphics/5_difficulty panel.png";
        
        /// <summary>
        /// Difficulty frame texture for highlighting selected grid cell
        /// </summary>
        public const string DifficultyFrame = "Graphics/5_difficulty frame.png";
        
        /// <summary>
        /// Graph panel texture for drums mode
        /// </summary>
        public const string GraphPanelDrums = "Graphics/5_graph panel drums.png";
        
        /// <summary>
        /// Graph panel texture for guitar/bass mode
        /// </summary>
        public const string GraphPanelGuitarBass = "Graphics/5_graph panel guitar bass.png";

        /// <summary>
        /// Comment bar background texture for artist name and song comments
        /// </summary>
        public const string CommentBar = "Graphics/5_comment bar.png";

        #endregion
        
        #region Font Textures
        
        /// <summary>
        /// Primary console font texture
        /// </summary>
        public const string ConsoleFont = "Graphics/Console font 8x16.png";
        
        /// <summary>
        /// Secondary console font texture
        /// </summary>
        public const string ConsoleFontSecondary = "Graphics/Console font 2 8x16.png";
        
        /// <summary>
        /// Level number bitmap font texture for difficulty level display
        /// </summary>
        public const string LevelNumberFont = "Graphics/6_LevelNumber.png";
        
        /// <summary>
        /// Difficulty sprite texture (262x600, 50px per sprite)
        /// Contains difficulty labels: Master(6th), Basic(7th), Advanced(8th), Extreme(9th), Real(12th)
        /// </summary>
        public const string DifficultySprite = "Graphics/6_Difficulty.png";
        
        #endregion
        
        #region Preview and Default Textures
        
        /// <summary>
        /// Default preview image for songs without preview images
        /// </summary>
        public const string DefaultPreview = "Graphics/5_default_preview.png";
        
        #endregion
        
        #region Performance Stage Textures
        
        /// <summary>
        /// Performance stage background texture
        /// </summary>
        public const string PerformanceBackgroundTexture = PerformanceBackground;
        
        /// <summary>
        /// Performance stage background video
        /// </summary>
        public const string PerformanceBackgroundVideo = "Graphics/7_background.mp4";
        
        /// <summary>
        /// Stage failed overlay
        /// </summary>
        public const string StageFailed = "Graphics/7_stage_failed.jpg";
        
        /// <summary>
        /// Full combo celebration overlay
        /// </summary>
        public const string FullCombo = "Graphics/7_FullCombo.png";
        
        /// <summary>
        /// Danger overlay texture (tiling)
        /// </summary>
        public const string Danger = "Graphics/7_Danger.png";
        
        /// <summary>
        /// Lane background strips sprite sheet (7_Paret.png)
        /// Contains individual lane background strips for the drum lanes
        /// </summary>
        public const string LaneStrips = "Graphics/7_Paret.png";
        
        /// <summary>
        /// Lane covers sprite sheet for hidden lanes
        /// </summary>
        public const string LaneCovers = "Graphics/7_lanes_Cover_cls.png";
        
        /// <summary>
        /// Shutter animation texture
        /// </summary>
        public const string Shutter = "Graphics/7_shutter.png";
        
        /// <summary>
        /// Hit-bar (judgement line) texture
        /// </summary>
        public const string HitBar = "Graphics/ScreenPlayDrums hit-bar.png";
        
        /// <summary>
        /// Drum notes sprite sheet with chip graphics for all lanes
        /// </summary>
        public const string DrumChips = "Graphics/7_chips_drums.png";
        
        /// <summary>
        /// Long notes texture atlas
        /// </summary>
        public const string LongNotes = "Graphics/7_longnotes.png";
        
        /// <summary>
        /// Explosion effects sprite sheet
        /// </summary>
        public const string Explosion = "Graphics/7_explosion.png";
        
        /// <summary>
        /// Score display numbers texture
        /// </summary>
        public const string ScoreNumbers = "Graphics/7_score numbersGD.png";
        
        /// <summary>
        /// Combo display texture
        /// </summary>
        public const string ComboDisplay = "Graphics/ScreenPlayDrums combo.png";
        
        /// <summary>
        /// Alternate combo display texture (for 1000+)
        /// </summary>
        public const string ComboDisplayAlt = "Graphics/ScreenPlayDrums combo_2.png";
        
        /// <summary>
        /// Combo bomb effect texture
        /// </summary>
        public const string ComboBomb = "Graphics/7_combobomb.png";
        
        /// <summary>
        /// Gauge frame texture
        /// </summary>
        public const string GaugeFrame = "Graphics/7_Gauge.png";
        
        /// <summary>
        /// Gauge fill texture
        /// </summary>
        public const string GaugeFill = "Graphics/7_gauge_bar.png";
        
        /// <summary>
        /// Gauge full overlay texture
        /// </summary>
        public const string GaugeFullOverlay = "Graphics/7_gauge_bar.jpg";
        
        /// <summary>
        /// Progress bar frame texture
        /// </summary>
        public const string ProgressFrame = "Graphics/7_Drum_Progress_bg.png";
        
        /// <summary>
        /// Progress bar fill texture
        /// </summary>
        public const string ProgressFill = "Graphics/7_progress_fill.png";
        
        /// <summary>
        /// Skill panel texture
        /// </summary>
        public const string SkillPanel = "Graphics/7_SkillPanel.png";
        
        /// <summary>
        /// Panel icons texture sheet
        /// </summary>
        public const string PanelIcons = "Graphics/7_panel_icons.jpg";
        
        /// <summary>
        /// Level number bitmap font texture
        /// </summary>
        public const string LevelNumbers = "Graphics/7_LevelNumber.png";
        
        /// <summary>
        /// Large rate numbers texture
        /// </summary>
        public const string RateNumbersLarge = "Graphics/7_Ratenumber_l.png";
        
        /// <summary>
        /// Rate percent symbol texture
        /// </summary>
        public const string RatePercent = "Graphics/7_RatePercent_l.png";
        
        /// <summary>
        /// Skill max badge texture
        /// </summary>
        public const string SkillMax = "Graphics/7_skill max.png";
        
        /// <summary>
        /// Small rate numbers texture
        /// </summary>
        public const string RateNumbersSmall = "Graphics/7_Ratenumber_s.png";
        
        /// <summary>
        /// Lag numbers texture for timing display
        /// </summary>
        public const string LagNumbers = "Graphics/7_lag numbers.png";
        
        /// <summary>
        /// Judgement text sprite sheet
        /// </summary>
        public const string JudgeStrings = "Graphics/7_judge.png";
        
        /// <summary>
        /// Timing lag indicator texture
        /// </summary>
        public const string LagIndicator = "Graphics/7_lag.png";
        
        /// <summary>
        /// Pause overlay texture
        /// </summary>
        public const string PauseOverlay = "Graphics/7_pause_overlay.png";
        
        /// <summary>
        /// Hit spark effects - Red
        /// </summary>
        public const string HitSparkRed = "Graphics/ScreenPlayDrums chip fire_red.png";
        
        /// <summary>
        /// Hit spark effects - Blue
        /// </summary>
        public const string HitSparkBlue = "Graphics/ScreenPlayDrums chip fire_blue.png";
        
        /// <summary>
        /// Hit spark effects - Green
        /// </summary>
        public const string HitSparkGreen = "Graphics/ScreenPlayDrums chip fire_green.png";
        
        /// <summary>
        /// Hit spark effects - Purple
        /// </summary>
        public const string HitSparkPurple = "Graphics/ScreenPlayDrums chip fire_purple.png";
        
        /// <summary>
        /// Hit spark effects - Yellow
        /// </summary>
        public const string HitSparkYellow = "Graphics/ScreenPlayDrums chip fire_yellow.png";
        
        /// <summary>
        /// Lane flush effects path prefix
        /// </summary>
        public const string LaneFlushPrefix = "Graphics/ScreenPlayDrums lane flush ";
        
        /// <summary>
        /// Wailing fire effect texture
        /// </summary>
        public const string WailingFire = "Graphics/7_WailingFire.png";
        
        /// <summary>
        /// Wailing flush effect texture
        /// </summary>
        public const string WailingFlush = "Graphics/7_WailingFlush.png";
        
        /// <summary>
        /// Chip wave effect texture
        /// </summary>
        public const string ChipWave = "Graphics/ScreenPlayDrums chip wave.png";
        
        /// <summary>
        /// Bonus effect texture
        /// </summary>
        public const string Bonus = "Graphics/7_Bonus.png";
        
        /// <summary>
        /// Bonus 100 effect texture
        /// </summary>
        public const string Bonus100 = "Graphics/7_Bonus_100.png";

        /// <summary>
        /// Pad visual indicators sprite sheet (2 rows: idle/pressed states)
        /// Standard path for pad caps texture - can be customized per skin
        /// </summary>
        public const string PadCaps = "Graphics/7_pads.png";
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Gets all texture paths used in the application
        /// Useful for preloading or validation
        /// </summary>
        public static string[] GetAllTexturePaths()
        {
            return new[]
            {
                StartupBackground,
                TitleBackground,
                SongSelectionBackground,
                SongTransitionBackground,
                PerformanceBackground,
                ResultBackground,
                TitleMenu,
                SongSelectionHeaderPanel,
                SongSelectionFooterPanel,
                SongStatusPanel,
                BpmBackground,
                DifficultyPanel,
                DifficultyFrame,
                GraphPanelDrums,
                GraphPanelGuitarBass,
                CommentBar,
                ConsoleFont,
                ConsoleFontSecondary,
                LevelNumberFont,
                DifficultySprite,
                DefaultPreview,
                PerformanceBackgroundVideo,
                StageFailed,
                FullCombo,
                Danger,
                LaneStrips,
                LaneCovers,
                Shutter,
                HitBar,
                DrumChips,
                LongNotes,
                Explosion,
                ScoreNumbers,
                ComboDisplay,
                ComboDisplayAlt,
                ComboBomb,
                GaugeFrame,
                GaugeFill,
                GaugeFullOverlay,
                ProgressFrame,
                ProgressFill,
                SkillPanel,
                PanelIcons,
                LevelNumbers,
                RateNumbersLarge,
                RatePercent,
                SkillMax,
                RateNumbersSmall,
                LagNumbers,
                JudgeStrings,
                LagIndicator,
                PauseOverlay,
                HitSparkRed,
                HitSparkBlue,
                HitSparkGreen,
                HitSparkPurple,
                HitSparkYellow,
                PadCaps,
                WailingFire,
                WailingFlush,
                ChipWave,
                Bonus,
                Bonus100
            };
        }
        
        /// <summary>
        /// Gets background textures for all stages
        /// </summary>
        public static string[] GetBackgroundTextures()
        {
            return new[]
            {
                StartupBackground,
                TitleBackground,
                SongSelectionBackground,
                SongTransitionBackground,
                PerformanceBackground,
                ResultBackground
            };
        }
        
        /// <summary>
        /// Gets UI panel textures
        /// </summary>
        public static string[] GetPanelTextures()
        {
            return new[]
            {
                TitleMenu,
                SongSelectionHeaderPanel,
                SongSelectionFooterPanel,
                SongStatusPanel,
                BpmBackground,
                DifficultyPanel,
                DifficultyFrame,
                GraphPanelDrums,
                GraphPanelGuitarBass,
                CommentBar
            };
        }
        
        /// <summary>
        /// Gets font textures
        /// </summary>
        public static string[] GetFontTextures()
        {
            return new[]
            {
                ConsoleFont,
                ConsoleFontSecondary,
                LevelNumberFont,
                DifficultySprite
            };
        }
        
        #endregion
    }
}