using DTXMania.Game.Lib.Config;
using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Layout
{
    /// <summary>
    /// Centralized layout configuration for Song Selection UI components
    /// Contains all positioning and sizing constants for DTXManiaNX-authentic layout
    /// </summary>
    public static class SongSelectionUILayout
    {
        #region Status Panel Layout
        
        /// <summary>
        /// Status Panel main position and size (DTXManiaNX authentic: X:130, Y:350, W:561, H:342)
        /// </summary>
        public static class StatusPanel
        {
            public const int X = 130; // Restored original DTXManiaNX authentic position
            public const int Y = 350; // Restored original DTXManiaNX authentic position
            public const int Width = 561;
            public const int Height = 342;
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Rectangle Bounds => new Rectangle(X, Y, Width, Height);
        }
        
        #endregion

        #region Folder Hint Overlay Layout

        /// <summary>
        /// Folder-hint overlay position offsets (drawn at top of status panel)
        /// </summary>
        public static class FolderHintOverlay
        {
            public const int OffsetX = 12;
            public const int OffsetY = 6;
        }

        #endregion

        #region BPM and Song Length Section
        
        /// <summary>
        /// BPM and Song Length badge positioning and sizing
        /// </summary>
        public static class BPMSection
        {
            public const int X = 32;  // Left-aligned with SkillPointSection (NX: 90, CX: 32)
            public const int Y = 275; // NX authentic: nBPM位置Y = 275
            public const int Width = 200;
            public const int Height = 50;

            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Rectangle Bounds => new Rectangle(X, Y, Width, Height);

            // Dark box pixel offsets within 5_BPM.png texture (measured from pixel analysis)
            // Dark box X: texture x=63..175 (113px wide); use 8px left padding → textX = X+71
            // Dark box 1 (length): texture y=8..31 (24px tall)
            // Dark box 2 (BPM):    texture y=38..61 (24px tall)
            public const int TextX = 71;            // X+63 (dark box left) + 8px padding
            public const int LengthBoxTop = 8;      // texture y where length box starts
            public const int BPMBoxTop = 38;        // texture y where BPM box starts
            public const int DarkBoxHeight = 24;    // height of each dark box

            // Static fallback positions (centered for 16px font — use DrawBPMSection for dynamic centering)
            public static Vector2 LengthTextPosition => new Vector2(X + TextX, Y + LengthBoxTop + (DarkBoxHeight - 16) / 2);
            public static Vector2 BPMTextPosition    => new Vector2(X + TextX, Y + BPMBoxTop    + (DarkBoxHeight - 16) / 2);
        }
        
        #endregion
        
        #region Difficulty Grid Layout
        
        /// <summary>
        /// 3×5 Difficulty grid layout (DTXManiaNX authentic)
        /// </summary>
        public static class DifficultyGrid
        {
            public const int BaseX = 130;  // NX authentic: nBaseX = 130
            public const int BaseY = 391;  // NX authentic: nBoxY = 391 + (4-i)*60 - 2
            public const int GridX = 140;  // Grid base X position (relative to main panel)
            public const int GridY = 52;   // Grid base Y position (relative to main panel)
            public const int CellWidth = 187;  // Panel width per difficulty cell
            public const int CellHeight = 60;  // Panel height per difficulty cell
            public const int PanelBodyWidth = 561; // Estimated panel body width
            
            // Cell content positioning
            public const int CellTextOffsetX = 10;   // Text offset from cell left edge
            public const int CellTextOffsetY = 5;    // Level text offset from cell top edge
            public const int CellRankOffsetY = 25;   // Rank text offset from cell top edge
            public const int CellScoreOffsetY = 40;  // Score text offset from cell top edge
            public const int CellEmptyOffsetY = 10;  // Empty cell text offset from top edge

            // NX-authentic pixel offsets for bitmap content within a difficulty cell.
            // NX positions content as nBoxX + nPanelW - offset (from the right edge) and
            // nBoxY + nPanelH - offset (from the bottom edge). These mirror the original
            // tDrawDifficulty / tDrawAchievementRate coordinates in CActSelectStatus.
            public const int LevelTextOffsetFromRight = 77;  // tDrawDifficulty X offset
            public const int LevelTextOffsetFromBottom = 35; // tDrawDifficulty Y offset
            public const int AchievementRateOffsetFromRight = 157; // tDrawAchievementRate X offset
            public const int AchievementRateOffsetFromBottom = 27; // tDrawAchievementRate Y offset
            public const int AchievementMaxOffsetFromRight = 142;  // tx達成率MAX X offset
            
            public static Vector2 BasePosition => new Vector2(BaseX, BaseY);
            public static Vector2 GridPosition => new Vector2(GridX, GridY);
            public static Vector2 CellSize => new Vector2(CellWidth, CellHeight);
            
            // Difficulty label position
            public static Vector2 DifficultyLabelPosition => new Vector2(160, 352);
            
            /// <summary>
            /// Calculate difficulty cell position using DTXManiaNX formula (for panel background)
            /// </summary>
            public static Vector2 GetCellPosition(int difficultyLevel, int instrument)
            {
                var nBoxX = BaseX + PanelBodyWidth + (CellWidth * (instrument - 3));
                var nBoxY = BaseY + ((4 - difficultyLevel) * CellHeight) - 2; // Higher difficulties at top
                return new Vector2(nBoxX, nBoxY);
            }
            
            /// <summary>
            /// Calculate difficulty cell content position (text and frame) - 20px down from panel position
            /// </summary>
            public static Vector2 GetCellContentPosition(int difficultyLevel, int instrument)
            {
                var nBoxX = BaseX + PanelBodyWidth + (CellWidth * (instrument - 3));
                var nBoxY = BaseY + ((4 - difficultyLevel) * CellHeight) - 2 + 20; // Higher difficulties at top, +20px offset
                return new Vector2(nBoxX, nBoxY);
            }
        }
        
        #endregion
        
        #region Skill Point Section
        
        /// <summary>
        /// Skill Point section layout
        /// </summary>
        public static class SkillPointSection
        {
            public const int X = 32;   // NX: txSkillPointPanel at (32, 180)
            public const int Y = 180;
            public const int Width = 187;  // Natural texture size (5_skill point panel.png 187x64)
            public const int Height = 64;

            // Dark box pixel offsets within 5_skill point panel.png (measured from pixel analysis)
            // Dark box X: texture x=60..176; use 8px left padding → textX = X+68
            // Dark box Y: texture y=11..56 (46px tall)
            public const int DarkBoxLeft = 60;   // texture x where dark box starts
            public const int DarkBoxTop = 11;    // texture y where dark box starts
            public const int DarkBoxHeight = 46; // height of dark box
            public const int ValueX = 100;       // X + DarkBoxLeft + 8px padding
            public const int ValueY = 206;       // fallback for 16px font; use dynamic centering in draw code
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Rectangle Bounds => new Rectangle(X, Y, Width, Height);
            public static Vector2 ValuePosition => new Vector2(ValueX, ValueY);
        }
        
        #endregion

        #region Play History Panel

        /// <summary>
        /// DTXManiaNX play history panel layout.
        /// </summary>
        public static class PlayHistoryPanel
        {
            public const int X = 700;
            public const int Y = 570;
            public const int Width = 458;
            public const int Height = 151;
            public const int TextOffsetX = 18;
            public const int TextOffsetY = 32;
            public const int RowSpacing = 18;
            // Delegates to the domain constant so the panel, the DB merge, and the
            // projection in SongListNode all share one source of truth.
            public const int MaxRows = GameConstants.PlayHistory.MaxRecentPlays;
            public const float FontScale = 0.8f;

            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Rectangle Bounds => new Rectangle(X, Y, Width, Height);
        }

        #endregion
        
        #region Graph Panel Section
        
        /// <summary>
        /// Graph Panel section layout
        /// </summary>
        public static class GraphPanel
        {
            public const int BaseX = 15;   // Panel base X position
            public const int BaseY = 368;  // Panel base Y position
            public const int Width = 110;  // Natural texture width (5_graph panel drums.png)
            public const int Height = 321; // Natural texture height (5_graph panel drums.png)
            
            // Total notes counter position
            public const int NotesCounterX = 81;  // X position (15 + 66)
            public const int NotesCounterY = 666; // Y position (368 + 298)
            
            // Progress bar position
            public const int ProgressBarX = 33;  // X position (15 + 18)
            public const int ProgressBarY = 389; // Y position (368 + 21)
            public const int ProgressBarWidth = 5; // Progress bar width (reasonable size)
            public const int ProgressBarHeight = 120; // Progress bar height (increased to be more visible)
            
            public static Vector2 BasePosition => new Vector2(BaseX, BaseY);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Rectangle Bounds => new Rectangle(BaseX, BaseY, Width, Height);
            public static Vector2 NotesCounterPosition => new Vector2(NotesCounterX, NotesCounterY);
            public static Vector2 ProgressBarPosition => new Vector2(ProgressBarX, ProgressBarY);
            public static Vector2 ProgressBarSize => new Vector2(ProgressBarWidth, ProgressBarHeight);
        }
        
        #endregion
        
        #region Note Distribution Bars
        
        /// <summary>
        /// Note distribution bar graph layout
        /// </summary>
        public static class NoteDistributionBars
        {
            // Drums configuration (10 lanes)
            public static class Drums
            {
                public const int StartX = 46;  // NX authentic: nGraphBaseX(15) + 31 = 46
                public const int StartY = 389; // Start position Y (368 + 21) - aligns with dark area top in texture
                public const int BarSpacing = 4;  // Space between bars
                public const int BarWidth = 4;    // Width of each bar
                public const int MaxBarHeight = 252; // Maximum bar height
                public const int LaneCount = 10;   // Number of drum lanes
                
                public static Vector2 StartPosition => new Vector2(StartX, StartY);
                
                public static Vector2 GetBarPosition(int lane)
                {
                    var barX = StartX + (lane * (BarWidth + BarSpacing));
                    return new Vector2(barX, StartY);
                }
            }
            
            // Guitar/Bass configuration (6 lanes)
            public static class GuitarBass
            {
                public const int StartX = 53;  // Start position X (15 + 38)
                public const int StartY = 389; // Start position Y (368 + 21)
                public const int BarSpacing = 6;  // NX authentic: interval=10, BarWidth=4, so spacing=6
                public const int BarWidth = 4;    // Width of each bar
                public const int MaxBarHeight = 252; // Maximum bar height
                public const int LaneCount = 6;   // Number of guitar/bass lanes
                
                public static Vector2 StartPosition => new Vector2(StartX, StartY);
                
                public static Vector2 GetBarPosition(int lane)
                {
                    var barX = StartX + (lane * (BarWidth + BarSpacing));
                    return new Vector2(barX, StartY);
                }
            }
        }
        
        #endregion
        
        #region Song Bars Layout
        
        /// <summary>
        /// Song Bars positioning and layout constants (DTXManiaNX authentic)
        /// Controls the 13-item song list display with selected/unselected bar positioning
        /// </summary>
        public static class SongBars
        {
            // DTXManiaNX Current Implementation: Vertical List Layout
            // Selected bar (index 5): X:665, Y:269 (special position, curves out from list)
            // Unselected bars: Fixed X:673 (vertical list formation)
            public const int SelectedBarX = 701;       // X position for selected song bar; gap from difficulty panel (BASS right edge ~691)
            public const int SelectedBarY = 269;       // Y position for selected song bar (center position)
            public const int UnselectedBarX = 709;     // Fixed X position for all unselected bars; 18px gap from difficulty panel
            public const int BarWidth = 510;           // Maximum width for song bars

            // Bookmark star marker offsets, relative to the bar's top-left (itemBounds).
            public const int BookmarkStarOffsetX = 20;
            public const int BookmarkStarOffsetY = 6;

            // Horizontal inset for the Recent/Bookmarks empty-state status text, relative to
            // UnselectedBarX. Shared by both tabs' "No … yet" / "Could not load …" messages.
            public const int EmptyMessageOffsetX = 100;
            
            // Visual constants
            public const int VisibleItems = 13;        // Number of visible song bars
            public const int CenterIndex = 5;          // Center bar index (0-based, DTXManiaNX uses 5)
            public const int ScrollUnit = 100;         // Scroll increment unit
            
            // DTXManiaNX Current Implementation Coordinates (ptバーの基本座標)
            // NOTE: Original curved X coordinates are present but DISABLED in current DTXManiaNX
            // Current implementation uses vertical list layout with fixed X positions
            public static readonly Point[] BarCoordinates = new Point[]
            {
                new Point(708, 5),      // Bar 0 (original curved, X ignored)
                new Point(626, 56),     // Bar 1 (original curved, X ignored)
                new Point(578, 107),    // Bar 2 (original curved, X ignored)
                new Point(546, 158),    // Bar 3 (original curved, X ignored)
                new Point(528, 209),    // Bar 4 (original curved, X ignored)
                new Point(464, 270),    // Bar 5 (original curved, special position)
                new Point(548, 362),    // Bar 6 (original curved, X ignored)
                new Point(578, 413),    // Bar 7 (original curved, X ignored)
                new Point(624, 464),    // Bar 8 (original curved, X ignored)
                new Point(686, 515),    // Bar 9 (original curved, X ignored)
                new Point(788, 566),    // Bar 10 (original curved, X ignored)
                new Point(996, 617),    // Bar 11 (original curved, X ignored)
                new Point(1280, 668)    // Bar 12 (original curved, X ignored)
            };
            
            // Helper properties for easy access
            public static Vector2 SelectedBarPosition => new Vector2(SelectedBarX, SelectedBarY);
            public static Vector2 UnselectedBarPosition => new Vector2(UnselectedBarX, 0); // Y varies per bar
            public static Vector2 BarSize => new Vector2(BarWidth, BarHeight);
            
            // Individual Song Bar Component Layout
            public const int BarHeight = 48;           // Height of each song bar (NX authentic: skin texture height)
            public const int PreviewImageSize = 44;    // Size of preview image square (NX authentic: 44x44)
            public const int ClearLampWidth = 7;       // Width of clear lamp indicator (NX authentic: 7px)
            public const int ClearLampHeight = 41;     // Height of clear lamp indicator (NX authentic: 41px)
            public const int TextPadding = 10;         // General text padding
            public const int NodeTypeIndicatorWidth = 4; // Width of node type indicator

            // Artist name display layout (absolute NX-authentic coordinates)
            public const int ArtistNameAbsoluteRightEdge = 1235; // NX: 1260 - 25 (right-aligned)
            public const int ArtistNameAbsoluteY = 320;           // NX: y = 320 (absolute)

            // Selected bar skin texture vertical offset (NX authentic: skin drawn at Y-30, natural size)
            public const int SelectedBarTextureYOffset = -30;

            // NX-authentic content offsets within bars (skin drawn at natural 1:1 size)
            // Selected bar (640x96 skin): preview at (barX+7, barY-3), lamp at (barX, barY+1), title at (barX+55, barY+centered)
            public const int SelectedBarPreviewImageOffsetX = 7;
            public const int SelectedBarPreviewImageOffsetY = -3;
            public const int SelectedBarClearLampOffsetX = 0;
            public const int SelectedBarClearLampOffsetY = 1;
            public const int SelectedBarTitleOffsetX = 55;
            // Unselected bar (620x48 skin): preview at (barX+31, barY+2), lamp at (barX+24, barY+6), title at (barX+78, barY+5+centered)
            public const int UnselectedBarPreviewImageOffsetX = 31;
            public const int UnselectedBarPreviewImageOffsetY = 2;
            public const int UnselectedBarClearLampOffsetX = 24;
            public const int UnselectedBarClearLampOffsetY = 6;
            public const int UnselectedBarTitleOffsetX = 78;
            public const int UnselectedBarTitleOffsetY = 5;

            // Spacing and positioning within bars
            public const int PreviewImageMargin = 5;   // Margin around preview image
            public const int TextMarginWithImage = 10; // Text margin when preview image present
            public const int TextMarginNoImage = 5;    // Text margin when no preview image
            
            // Selection visual effects
            public const int SelectedBorderThickness = 2; // Border thickness for center/selected bar
            public const int UnselectedBorderThickness = 1; // Border thickness for normal selected bar
            
            // Texture generation constants (NX-authentic 2x render / 0.5x display)
            public const int TitleTextureWidth = 1020;  // Width for generated title textures (510 * 2 for 2x render)
            public const int TitleTextureHeight = 76;   // Height for generated title textures (38 * 2 for 2x render)
            public const int TitleDisplayWidth = 510;    // Max display width (texture drawn at 0.5x)
            public const int TitleDisplayHeight = 38;    // Max display height (texture drawn at 0.5x)
            public const float TitleRenderScale = 2.0f;  // Render at 2x for anti-aliasing
            public const float TitleDisplayScale = 0.5f;  // Display at 0.5x (1/RenderScale)
            public const int TextPositionX = 5;          // X position for text within textures
            
            /// <summary>
            /// Get the Y coordinate for a specific bar index
            /// </summary>
            public static int GetBarY(int barIndex)
            {
                if (barIndex < 0 || barIndex >= BarCoordinates.Length)
                    return 0;
                return BarCoordinates[barIndex].Y;
            }
            
            /// <summary>
            /// Get the full position for a specific bar index
            /// </summary>
            public static Vector2 GetBarPosition(int barIndex)
            {
                if (barIndex == CenterIndex)
                {
                    return SelectedBarPosition;
                }
                else
                {
                    return new Vector2(UnselectedBarX, GetBarY(barIndex));
                }
            }
        }
        
        #endregion
        
        #region Song List Display Layout
        
        /// <summary>
        /// Song List Display layout (full screen for curved layout)
        /// </summary>
        public static class SongListDisplay
        {
            public const int X = 0;
            public const int Y = 0;
            public const int Width = 1280;  // Full screen width
            public const int Height = 720;  // Full screen height
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Rectangle Bounds => new Rectangle(X, Y, Width, Height);
        }
        
        #endregion
        
        #region Item Counter Layout
        
        /// <summary>
        /// Item counter "currentIndex/totalCount" layout (NX-authentic: right-aligned at 1260, 620)
        /// </summary>
        public static class ItemCounter
        {
            public const int BaseX = 1260;      // Right edge position (screen width - 20)
            public const int BaseY = 620;       // Y position
            public const int ShadowOffsetX = 1; // Text shadow X offset
            public const int ShadowOffsetY = 1; // Text shadow Y offset
            
            public static Vector2 BasePosition => new Vector2(BaseX, BaseY);
        }
        
        #endregion
        
        #region Scrollbar Layout
        
        /// <summary>
        /// NX-authentic scrollbar layout (12px wide at right edge)
        /// </summary>
        public static class Scrollbar
        {
            public const int X = 1268;              // Right edge (1280 - 12)
            public const int Y = 5;                 // Top of bar list area
            public const int Height = 492;          // Track height
            public const int IndicatorSize = 12;    // 12x12 indicator
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 IndicatorSizeVector => new Vector2(IndicatorSize, IndicatorSize);
        }
        
        #endregion

        #region Comment Bar Layout

        /// <summary>
        /// Comment Bar layout for song comments (DTXManiaNX authentic)
        /// Note: Artist names are handled by the existing song bar rendering system
        /// </summary>
        public static class CommentBar
        {
            public const int X = 560;
            public const int Y = 257;
            public const int FallbackHeight = 80;
            public static readonly Color FallbackColor = Color.Blue * 0.3f;

            // Font scaling for long text
            public const float FontScale = 0.5f;       // Font scale factor for comment text
            public static readonly Vector2 CommentTextPosition = new Vector2(683, 339);
            public const int CommentTextMaxWidth = 510;

            public static Vector2 Position => new Vector2(X, Y);
        }

        #endregion

        #region Preview Image Panel Layout
        
        /// <summary>
        /// Preview Image Panel layout (position depends on status panel presence)
        /// </summary>
        public static class PreviewImagePanel
        {
            // Without status panel positioning
            public static class WithoutStatusPanel
            {
                public const int X = 18;
                public const int Y = 88;
                public const int Size = 368;
                
                public static Vector2 Position => new Vector2(X, Y);
                public static Vector2 SizeVector => new Vector2(Size, Size);
                public static Rectangle Bounds => new Rectangle(X, Y, Size, Size);
            }
            
            // With status panel positioning
            public static class WithStatusPanel
            {
                public const int X = 250;
                public const int Y = 34;
                public const int Size = 292;
                
                public static Vector2 Position => new Vector2(X, Y);
                public static Vector2 SizeVector => new Vector2(Size, Size);
                public static Rectangle Bounds => new Rectangle(X, Y, Size, Size);
            }
            
            // Content offsets
            public const int ContentOffsetWithoutStatus = 37;     // X+37, Y+24
            public const int ContentOffsetYWithoutStatus = 24;
            public const int ContentOffsetWithStatus = 8;         // X+8, Y+8
        }
        
        #endregion
        
        #region UI Labels Layout
        
        /// <summary>
        /// UI Labels layout (title, breadcrumb, etc.)
        /// </summary>
        public static class UILabels
        {
            // Title label (positioned below header panel)
            public static class Title
            {
                public const int X = 50;
                public const int Y = 100;
                public const int Width = 400;
                public const int Height = 40;
                
                public static Vector2 Position => new Vector2(X, Y);
                public static Vector2 Size => new Vector2(Width, Height);
            }
            
            // Breadcrumb label
            public static class Breadcrumb
            {
                public const int X = 50;
                public const int Y = 150;
                public const int Width = 600;
                public const int Height = 30;
                
                public static Vector2 Position => new Vector2(X, Y);
                public static Vector2 Size => new Vector2(Width, Height);
            }
        }
        
        #endregion
        
        #region Background and Visual Settings
        
        /// <summary>
        /// Background and visual effect settings
        /// </summary>
        public static class Background
        {
            // Gradient drawing settings
            public const int GradientLineSpacing = 4;  // Draw every 4th line for performance
            
            // Font settings
            public const int DefaultFontSize = 16;     // Default UI font size
            public const string DefaultFontName = "Arial"; // Default font name
            
            // UI transparency settings
            public const float MainPanelAlpha = 0.8f;  // Main panel background transparency
        }
        
        #endregion
        
        #region Timing and Animation Settings
        
        /// <summary>
        /// Timing and animation constants
        /// </summary>
        public static class Timing
        {
            // Stage transition timings
            public const double FadeInDuration = 0.5;   // 0.5 second fade in
            public const double FadeOutDuration = 0.5;  // 0.5 second fade out
            public const double TransitionDuration = 0.5; // Stage transition duration
            
            // Input debouncing
            public const double NavigationDebounceSeconds = 0.01; // 10ms debounce for smooth navigation
            
            // Task timeout settings
            public const int TaskTimeoutMilliseconds = 500; // 500ms timeout for task completion
        }
        
        #endregion
        
        #region Audio Settings
        
        /// <summary>
        /// Audio volume and sound settings
        /// </summary>
        public static class Audio
        {
            // Volume levels
            public const float PreviewSoundVolume = 0.8f;  // 80% volume for preview sounds
            public const float NavigationSoundVolume = 0.7f; // 70% volume for navigation sounds
            public const float GameStartSoundVolume = 0.9f;  // 90% volume for game start sound
            
            // BGM fade settings
            public const double BgmFadeOutDuration = 0.5;  // 500ms fade
            public const double BgmFadeInDuration = 1.0;   // 1 second fade
            public const double PreviewPlayDelaySeconds = 1.0; // 1 second delay
            
            // Volume fade ranges
            public const float BgmMinVolume = 0.1f;    // 10% minimum volume during preview
            public const float BgmMaxVolume = 1.0f;    // 100% maximum volume
            public const float BgmFadeRange = 0.9f;    // Volume fade range (90% of full volume)
        }
        
        #endregion
        
        #region Spacing and Offsets

        /// <summary>
        /// Common spacing and offset values
        /// </summary>
        public static class Spacing
        {
            public const int CellPadding = 10;      // General cell padding
            public const int SectionSpacing = 15;   // Space between sections
            public const int BorderThickness = 2;   // Border thickness for panels
            public const int LabelValueSpacing = 5; // Space between label and value text
        }

        #endregion

        // Scroll-speed display (e.g., "Scroll x1.5") on song-select panel
        public const int ScrollSpeedLabelX = 20;
        public const int ScrollSpeedLabelY = 680; // bottom-left

        #region Tab Bar Layout

        /// <summary>
        /// Layout for the song-select tab bar (All Songs / Recent).
        /// Coordinates are in the stage's 1280x720 design space.
        /// </summary>
        public static class Tabs
        {
            // Top-left origin of the tab strip.
            public const int X = 40;
            public const int Y = 8;
            // Horizontal gap between tab labels.
            public const int Spacing = 24;
            // Active vs. inactive label tint.
            public static readonly Color ActiveColor = Color.White;
            public static readonly Color InactiveColor = new Color(150, 150, 150);
        }

        #endregion

        #region Search Filter Modal Layout

        /// <summary>
        /// Layout constants for the search/filter/sort modal overlay.
        /// </summary>
        public static class SearchFilterModal
        {
            // Centered in 1280x720
            public const int X = 340;
            public const int Y = 180;
            public const int Width = 600;
            public const int Height = 360;

            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Rectangle Bounds => new Rectangle(X, Y, Width, Height);

            // Title bar
            public const int TitleY = 12;             // relative to modal top
            public const int TitleHeight = 28;

            // Field rows (relative to modal top-left)
            public const int RowSpacing = 44;
            public const int FirstRowY = 56;
            public const int LabelX = 24;
            public const int FieldX = 130;

            // Search box
            public const int SearchBoxY = FirstRowY;
            public const int SearchBoxWidth = 430;
            public const int SearchBoxHeight = 30;

            // Level row
            public const int LevelRowY = FirstRowY + RowSpacing;
            public const int LevelMinX = FieldX;
            public const int LevelMaxX = FieldX + 180;
            public const int LevelInputWidth = 80;
            public const int LevelInputHeight = 30;

            // Played-status row
            public const int PlayedRowY = FirstRowY + RowSpacing * 2;

            // Sort row
            public const int SortRowY = FirstRowY + RowSpacing * 3;

            // Buttons
            public const int ButtonRowY = FirstRowY + RowSpacing * 4 + 8;
            public const int ResetButtonX = 200;
            public const int ApplyButtonX = 360;
            public const int ButtonWidth = 120;
            public const int ButtonHeight = 36;
        }

        #endregion
    }
}
