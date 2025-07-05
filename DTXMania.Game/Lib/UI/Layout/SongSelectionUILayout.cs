using Microsoft.Xna.Framework;

namespace DTX.UI.Layout
{
    /// <summary>
    /// Centralized layout configuration for Song Selection UI components
    /// Contains all positioning and sizing constants for DTXManiaNX-authentic layout
    /// </summary>
    public static class SongSelectionUILayout
    {
        #region Status Panel Layout
        
        /// <summary>
        /// Status Panel main position and size (DTXManiaNX authentic: X:130, Y:350, W:580, H:320)
        /// </summary>
        public static class StatusPanel
        {
            public const int X = 130; // Restored original DTXManiaNX authentic position
            public const int Y = 350; // Restored original DTXManiaNX authentic position
            public const int Width = 580;
            public const int Height = 320;
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Rectangle Bounds => new Rectangle(X, Y, Width, Height);
        }
        
        #endregion
        
        #region BPM and Song Length Section
        
        /// <summary>
        /// BPM and Song Length badge positioning and sizing
        /// </summary>
        public static class BPMSection
        {
            public const int X = 32;  // Relative to status panel
            public const int Y = 258; // Absolute Y position
            public const int Width = 200;
            public const int Height = 50;
            public const int LineSpacing = 20; // Space between Length and BPM text
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Rectangle Bounds => new Rectangle(X, Y, Width, Height);
            
            // Individual text positions
            public static Vector2 LengthTextPosition => new Vector2(X, Y);
            public static Vector2 BPMTextPosition => new Vector2(X, Y + LineSpacing);
        }
        
        #endregion
        
        #region Difficulty Grid Layout
        
        /// <summary>
        /// 3×5 Difficulty grid layout (DTXManiaNX authentic)
        /// </summary>
        public static class DifficultyGrid
        {
            public const int BaseX = 150;  // Main panel X position
            public const int BaseY = 400;  // Main panel Y position
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
            
            public static Vector2 BasePosition => new Vector2(BaseX, BaseY);
            public static Vector2 GridPosition => new Vector2(GridX, GridY);
            public static Vector2 CellSize => new Vector2(CellWidth, CellHeight);
            
            // Difficulty label position
            public static Vector2 DifficultyLabelPosition => new Vector2(160, 352);
            
            /// <summary>
            /// Calculate difficulty cell position using DTXManiaNX formula
            /// </summary>
            public static Vector2 GetCellPosition(int difficultyLevel, int instrument)
            {
                var nPart = new[] { 0, 1, 2 }; // Drums, Guitar, Bass
                var nBoxX = BaseX + PanelBodyWidth + (CellWidth * (nPart[instrument] - 3));
                var nBoxY = BaseY + ((4 - difficultyLevel) * CellHeight) - 2; // Higher difficulties at top
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
            public const int X = 32;   // Panel X position
            public const int Y = 180;  // Panel Y position
            public const int Width = 120;
            public const int Height = 20;
            
            // Skill value text position
            public const int ValueX = 92;  // X position for skill value text
            public const int ValueY = 200; // Y position for skill value text
            
            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Rectangle Bounds => new Rectangle(X, Y, Width, Height);
            public static Vector2 ValuePosition => new Vector2(ValueX, ValueY);
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
            public const int Width = 110;  // Panel width (reduced to realistic size for bar graph area)
            public const int Height = 300; // Panel height (covers bars + progress area)
            
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
            // Drums configuration (9 lanes)
            public static class Drums
            {
                public const int StartX = 46;  // Start position X (15 + 31)
                public const int StartY = 389; // Start position Y (368 + 21)
                public const int BarSpacing = 4;  // Space between bars
                public const int BarWidth = 4;    // Width of each bar
                public const int MaxBarHeight = 252; // Maximum bar height
                public const int LaneCount = 9;   // Number of drum lanes
                
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
                public const int BarSpacing = 10; // Space between bars
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
            public const int SelectedBarX = 715;       // X position for selected song bar (center position)
            public const int SelectedBarY = 269;       // Y position for selected song bar (center position)
            public const int UnselectedBarX = 723;     // Fixed X position for all unselected bars
            public const int BarWidth = 510;           // Maximum width for song bars
            
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
            public static Vector2 BarSize => new Vector2(BarWidth, 30); // Height will be dynamic
            
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
    }
}
