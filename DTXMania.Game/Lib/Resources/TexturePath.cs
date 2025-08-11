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
        /// Performance stage background texture
        /// </summary>
        public const string PerformanceBackground = "Graphics/7_background.jpg";

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
                PerformanceBackground,
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
                DefaultPreview
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
                PerformanceBackground
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