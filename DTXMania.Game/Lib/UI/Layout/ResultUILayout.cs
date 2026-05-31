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

        #region NX Result Layout

        public static class NXViewport
        {
            public const int Width = 1280;
            public const int Height = 720;
        }

        public static class Fonts
        {
            public const int Small = 16;
            public const int Normal = 20;
            public const int Large = 32;
        }

        public static class Rank
        {
            public static readonly Vector2 BadgePosition = new(480, 0);
        }

        public static class ResultPlate
        {
            public static readonly Vector2 Position = new(315, 100);
            public static readonly Vector2 FailedTextPosition = new(420, 156);
        }

        public static class Jacket
        {
            public static readonly Vector2 PanelPosition = new(467, 287);
            public static readonly Rectangle PreviewDestination = new(519, 338, 245, 245);
        }

        public static class SkillPanel
        {
            public static readonly Vector2 PanelPosition = new(180, 260);
            public static readonly Vector2 LevelPosition = new(198, 550);
            public static readonly Vector2 PlayingSkillPosition = new(238, 537);
            public static readonly Vector2 GameSkillPosition = new(268, 623);
            public static readonly Vector2 PerfectCountPosition = new(260, 332);
            public static readonly Vector2 GreatCountPosition = new(260, 362);
            public static readonly Vector2 GoodCountPosition = new(260, 392);
            public static readonly Vector2 PoorCountPosition = new(260, 422);
            public static readonly Vector2 MissCountPosition = new(260, 452);
            public static readonly Vector2 MaxComboCountPosition = new(260, 482);
            public static readonly Vector2 PerfectPercentPosition = new(347, 332);
            public static readonly Vector2 GreatPercentPosition = new(347, 362);
            public static readonly Vector2 GoodPercentPosition = new(347, 392);
            public static readonly Vector2 PoorPercentPosition = new(347, 422);
            public static readonly Vector2 MissPercentPosition = new(347, 452);
            public static readonly Vector2 MaxComboPercentPosition = new(347, 482);
        }

        public static class Score
        {
            public static readonly Vector2 Position = new(30, 58);
        }

        public static class SongInfo
        {
            public static readonly Vector2 TitlePosition = new(500, 630);
            public static readonly Vector2 ArtistPosition = new(500, 665);
            public const int MaxWidth = 320;
        }

        public static class NewRecord
        {
            public static readonly Vector2 BadgePosition = new(298, 582);
        }

        #endregion
        
        #region Fallback Text Rendering
        
        /// <summary>
        /// Fallback text rendering settings when the font fails to load
        /// </summary>
        public static class FallbackText
        {
            public const int CharacterWidth = 8;
            public const int RectHeight = 20;
        }
        
        #endregion
    }
}
