using Microsoft.Xna.Framework;
using System;

namespace DTX.UI
{
    /// <summary>
    /// DTXManiaNX-style visual theme configuration
    /// Provides colors, effects, and styling constants for authentic DTXManiaNX appearance
    /// </summary>
    public static class DTXManiaVisualTheme
    {
        #region Color Schemes

        /// <summary>
        /// Song selection UI colors
        /// </summary>
        public static class SongSelection
        {
            // Background colors
            public static readonly Color BackgroundGradientTop = new Color(20, 30, 60);
            public static readonly Color BackgroundGradientBottom = new Color(10, 15, 30);
            public static readonly Color PanelBackground = Color.Black * 0.8f;
            public static readonly Color PanelBorder = new Color(100, 150, 255);

            // Song list colors
            public static readonly Color SongBarBackground = new Color(40, 40, 60);
            public static readonly Color SongBarSelected = new Color(80, 120, 200);
            public static readonly Color SongBarCenter = new Color(120, 160, 255);
            public static readonly Color SongBarHover = new Color(60, 80, 140);

            // Bar type specific backgrounds (Phase 2)
            public static readonly Color FolderBackground = new Color(40, 60, 40);
            public static readonly Color SpecialBackground = new Color(60, 40, 60);

            // Text colors
            public static readonly Color SongTitleText = Color.White;
            public static readonly Color SongArtistText = new Color(200, 200, 255);
            public static readonly Color SongSelectedText = Color.Yellow;
            public static readonly Color FolderText = new Color(150, 255, 150);
            public static readonly Color BackFolderText = new Color(255, 200, 150);

            // Difficulty colors (DTXMania standard)
            public static readonly Color[] DifficultyColors = new Color[]
            {
                new Color(100, 255, 100),  // Basic - Green
                new Color(255, 255, 100),  // Advanced - Yellow
                new Color(255, 150, 100),  // Extreme - Orange
                new Color(255, 100, 100),  // Master - Red
                new Color(200, 100, 255)   // Special - Purple
            };

            // Status panel colors
            public static readonly Color StatusBackground = new Color(20, 20, 40);
            public static readonly Color StatusBorder = new Color(80, 120, 200);
            public static readonly Color StatusLabelText = new Color(150, 200, 255);
            public static readonly Color StatusValueText = Color.White;
            public static readonly Color CurrentDifficultyIndicator = Color.Yellow;
        }

        /// <summary>
        /// Font effect settings
        /// </summary>
        public static class FontEffects
        {
            // Shadow settings
            public static readonly Vector2 DefaultShadowOffset = new Vector2(2, 2);
            public static readonly Color DefaultShadowColor = Color.Black * 0.8f;

            // Outline settings
            public static readonly Color DefaultOutlineColor = Color.Black;
            public static readonly int DefaultOutlineThickness = 1;

            // Title font effects
            public static readonly Vector2 TitleShadowOffset = new Vector2(3, 3);
            public static readonly Color TitleShadowColor = Color.Black;
            public static readonly Color TitleOutlineColor = Color.Black;
            public static readonly int TitleOutlineThickness = 2;

            // Song text effects
            public static readonly Vector2 SongTextShadowOffset = new Vector2(1, 1);
            public static readonly Color SongTextShadowColor = Color.Black * 0.6f;
        }

        #endregion

        #region Visual Effects

        /// <summary>
        /// Animation and transition settings
        /// </summary>
        public static class Animations
        {
            public static readonly TimeSpan SelectionFadeTime = TimeSpan.FromMilliseconds(200);
            public static readonly TimeSpan ScrollAnimationTime = TimeSpan.FromMilliseconds(300);
            public static readonly TimeSpan DifficultyChangeTime = TimeSpan.FromMilliseconds(150);
            
            // Easing functions
            public static float EaseOutQuad(float t) => 1 - (1 - t) * (1 - t);
            public static float EaseInOutQuad(float t) => t < 0.5f ? 2 * t * t : 1 - (float)Math.Pow(-2 * t + 2, 2) / 2;
        }

        /// <summary>
        /// UI element dimensions and spacing
        /// </summary>
        public static class Layout
        {
            // Song list dimensions
            public static readonly int SongBarHeight = 32;
            public static readonly int SongBarSpacing = 2;
            public static readonly int SongListPadding = 10;
            public static readonly int VisibleSongCount = 13;
            public static readonly int CenterSongIndex = 6;

            // Status panel dimensions
            public static readonly int StatusPanelWidth = 320;
            public static readonly int StatusPanelPadding = 15;
            public static readonly int StatusLineHeight = 20;
            public static readonly int StatusSectionSpacing = 10;

            // Clear lamp dimensions
            public static readonly int ClearLampWidth = 8;
            public static readonly int ClearLampHeight = 24;

            // Preview image dimensions
            public static readonly int PreviewImageSize = 24;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get difficulty color by index
        /// </summary>
        public static Color GetDifficultyColor(int difficulty)
        {
            if (difficulty >= 0 && difficulty < SongSelection.DifficultyColors.Length)
                return SongSelection.DifficultyColors[difficulty];
            return Color.White;
        }

        /// <summary>
        /// Get node type color
        /// </summary>
        public static Color GetNodeTypeColor(Song.NodeType nodeType)
        {
            return nodeType switch
            {
                Song.NodeType.Score => SongSelection.SongTitleText,
                Song.NodeType.Box => SongSelection.FolderText,
                Song.NodeType.BackBox => SongSelection.BackFolderText,
                Song.NodeType.Random => Color.Magenta,
                _ => Color.White
            };
        }

        /// <summary>
        /// Create gradient color between two colors
        /// </summary>
        public static Color LerpColor(Color color1, Color color2, float amount)
        {
            return Color.Lerp(color1, color2, MathHelper.Clamp(amount, 0f, 1f));
        }

        /// <summary>
        /// Apply selection highlighting to a color
        /// </summary>
        public static Color ApplySelectionHighlight(Color baseColor, bool isSelected, bool isCenter = false)
        {
            if (isCenter)
                return LerpColor(baseColor, SongSelection.SongBarCenter, 0.7f);
            else if (isSelected)
                return LerpColor(baseColor, SongSelection.SongBarSelected, 0.5f);
            else
                return baseColor;
        }

        #endregion
    }
}
