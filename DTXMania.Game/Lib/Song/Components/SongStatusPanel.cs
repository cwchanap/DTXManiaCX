using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.UI.Layout;
using DTX.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

// Type alias for SongScore to use the EF Core entity
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTX.Song.Components
{
    /// <summary>
    /// DTXManiaNX-compatible status panel for displaying detailed song information
    /// Equivalent to CActSelectStatusPanel from DTXManiaNX
    /// </summary>
    public class SongStatusPanel : UIElement
    {
        #region Fields

        private readonly object _updateLock = new object();
        private SongListNode _currentSong;
        private int _currentDifficulty;
        private SpriteFont _font;
        private SpriteFont _smallFont;
        private IFont _managedFont;
        private IFont _managedSmallFont;
        private Texture2D _whitePixel;

        // Visual properties using DTXManiaNX theme
        private DefaultGraphicsGenerator _graphicsGenerator;

        // DTXManiaNX authentic graphics (Phase 3)
        private ITexture _statusPanelTexture;
        private IResourceManager _resourceManager;

        // BPM background texture for 5_BPM.png support
        private ITexture _bpmBackgroundTexture;

        // Difficulty panel texture for 5_difficulty panel.png support
        private ITexture _difficultyPanelTexture;

        // Difficulty frame texture for 5_difficulty frame.png support (selection highlight)
        private ITexture _difficultyFrameTexture;

        // Performance optimization: Cache generated background texture
        private ITexture _cachedBackgroundTexture;
        private Rectangle _cachedBackgroundSize;

        // Layout constants from DTXManiaNX theme
        private float LINE_HEIGHT => DTXManiaVisualTheme.Layout.StatusLineHeight;
        private float SECTION_SPACING => DTXManiaVisualTheme.Layout.StatusSectionSpacing;
        private float INDENT => DTXManiaVisualTheme.Layout.StatusPanelPadding;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the BPM background should use standalone positioning
        /// </summary>
        public bool UseStandaloneBPMBackground { get; set; } = false;

        /// <summary>
        /// Font for main text
        /// </summary>
        public SpriteFont Font
        {
            get => _font;
            set => _font = value;
        }

        /// <summary>
        /// Font for smaller text
        /// </summary>
        public SpriteFont SmallFont
        {
            get => _smallFont;
            set => _smallFont = value;
        }

        /// <summary>
        /// Managed font for advanced text rendering
        /// </summary>
        public IFont ManagedFont
        {
            get => _managedFont;
            set
            {
                _managedFont = value;
                _font = value?.SpriteFont; // Update SpriteFont reference
            }
        }

        /// <summary>
        /// Managed font for smaller text
        /// </summary>
        public IFont ManagedSmallFont
        {
            get => _managedSmallFont;
            set
            {
                _managedSmallFont = value;
                _smallFont = value?.SpriteFont; // Update SpriteFont reference
            }
        }

        /// <summary>
        /// White pixel texture for backgrounds
        /// </summary>
        public Texture2D WhitePixel
        {
            get => _whitePixel;
            set => _whitePixel = value;
        }        /// <summary>
        /// Initialize graphics generator for enhanced rendering
        /// </summary>
        public void InitializeGraphicsGenerator(GraphicsDevice graphicsDevice, RenderTarget2D renderTarget)
        {
            _graphicsGenerator?.Dispose();
            
            if (renderTarget != null)
            {
                _graphicsGenerator = new DefaultGraphicsGenerator(graphicsDevice, renderTarget);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("SongStatusPanel: Cannot initialize DefaultGraphicsGenerator without RenderTarget. Default graphics disabled.");
                _graphicsGenerator = null;
            }
        }

        /// <summary>
        /// Initialize DTXManiaNX authentic graphics (Phase 3)
        /// </summary>
        public void InitializeAuthenticGraphics(IResourceManager resourceManager)
        {
            _resourceManager = resourceManager;
            
            LoadStatusPanelGraphics();
            LoadBPMBackgroundTexture();
            LoadDifficultyPanelTexture();
            LoadDifficultyFrameTexture();
        }

        private void LoadStatusPanelGraphics()
        {
            try
            {
                // Load DTXManiaNX status panel background
                _statusPanelTexture = _resourceManager.LoadTexture(TexturePath.SongStatusPanel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongStatusPanel: Failed to load status panel background: {ex.Message}");
            }
        }

        /// <summary>
        /// Load the authentic 5_BPM.png background texture
        /// </summary>
        private void LoadBPMBackgroundTexture()
        {
            if (_resourceManager == null)
                return;

            try
            {
                _bpmBackgroundTexture = _resourceManager.LoadTexture(TexturePath.BpmBackground);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongStatusPanel: Failed to load BPM background texture: {ex.Message}");
                _bpmBackgroundTexture = null;
            }
        }

        /// <summary>
        /// Load the authentic 5_difficulty panel.png texture
        /// </summary>
        private void LoadDifficultyPanelTexture()
        {
            if (_resourceManager == null)
                return;

            try
            {
                _difficultyPanelTexture = _resourceManager.LoadTexture(TexturePath.DifficultyPanel);
            }
            catch
            {
                _difficultyPanelTexture = null;
            }
        }

        private void LoadDifficultyFrameTexture()
        {
            if (_resourceManager == null)
                return;

            try
            {
                _difficultyFrameTexture = _resourceManager.LoadTexture(TexturePath.DifficultyFrame);
            }
            catch
            {
                _difficultyFrameTexture = null;
            }
        }

        #endregion

        #region Constructor

        public SongStatusPanel()
        {
            Size = SongSelectionUILayout.StatusPanel.Size;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Update the displayed song information
        /// Thread-safe method to prevent state corruption during rapid updates
        /// </summary>
        /// <param name="song">Song to display</param>
        /// <param name="difficulty">Current difficulty level</param>
        public void UpdateSongInfo(SongListNode song, int difficulty)
        {
            // Force immediate state update - no lazy updates
            lock (_updateLock)
            {
                _currentSong = song;
                _currentDifficulty = difficulty;
            }
        }

        /// <summary>
        /// Dispose of resources
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cachedBackgroundTexture?.Dispose();
                _cachedBackgroundTexture = null;
                _graphicsGenerator?.Dispose();
                _graphicsGenerator = null;
                _statusPanelTexture?.Dispose();
                _statusPanelTexture = null;
                // Note: Don't dispose _bpmBackgroundTexture as it's managed by ResourceManager
                _bpmBackgroundTexture = null;
                // Note: Don't dispose _difficultyPanelTexture as it's managed by ResourceManager
                _difficultyPanelTexture = null;
            }
            base.Dispose(disposing);
        }

        #endregion

        #region Protected Methods

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible)
            {
                return;
            }

            var bounds = Bounds;            // Debug logging removed for cleaner code

            // Draw background using DTXManiaNX styling
            DrawBackground(spriteBatch, bounds);

            // Draw content - try fonts in order of preference
            if (_currentSong != null && (_font != null || _managedFont != null))
            {
                DrawSongInfo(spriteBatch, bounds);
            }
            else
            {
                DrawNoSongMessage(spriteBatch, bounds);
            }
            
            base.OnDraw(spriteBatch, deltaTime);
        }

        #endregion

        #region Private Methods

        private void DrawBackground(SpriteBatch spriteBatch, Rectangle bounds)
        {
            // Use DTXManiaNX authentic status panel background (Phase 3)
            if (_statusPanelTexture != null)
            {
                // Scale the authentic background to fit the panel bounds
                var sourceRect = new Rectangle(0, 0, _statusPanelTexture.Width, _statusPanelTexture.Height);
                var destRect = bounds;

                spriteBatch.Draw(_statusPanelTexture.Texture, destRect, sourceRect, Color.White);
                return;
            }

            // Performance optimization: Use cached generated background
            if (_graphicsGenerator != null)
            {
                // Check if we need to regenerate the cached texture
                if (_cachedBackgroundTexture == null || _cachedBackgroundSize != bounds)
                {
                    _cachedBackgroundTexture?.Dispose();
                    _cachedBackgroundTexture = _graphicsGenerator.GeneratePanelBackground(bounds.Width, bounds.Height, true);
                    _cachedBackgroundSize = bounds;
                }

                if (_cachedBackgroundTexture != null)
                {
                    _cachedBackgroundTexture.Draw(spriteBatch, new Vector2(bounds.X, bounds.Y));
                    return;
                }
            }

            // Final fallback to simple background
            if (_whitePixel != null)
            {
                spriteBatch.Draw(_whitePixel, bounds, DTXManiaVisualTheme.SongSelection.StatusBackground);

                // Draw border using layout constants
                var borderColor = DTXManiaVisualTheme.SongSelection.StatusBorder;
                var borderThickness = SongSelectionUILayout.Spacing.BorderThickness;

                // Top border
                spriteBatch.Draw(_whitePixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, borderThickness), borderColor);
                // Bottom border
                spriteBatch.Draw(_whitePixel, new Rectangle(bounds.X, bounds.Bottom - borderThickness, bounds.Width, borderThickness), borderColor);
                // Left border
                spriteBatch.Draw(_whitePixel, new Rectangle(bounds.X, bounds.Y, borderThickness, bounds.Height), borderColor);
                // Right border
                spriteBatch.Draw(_whitePixel, new Rectangle(bounds.Right - borderThickness, bounds.Y, borderThickness, bounds.Height), borderColor);
            }
        }

        private void DrawSongInfo(SpriteBatch spriteBatch, Rectangle bounds)
        {
            // Thread-safe capture of current state
            SongListNode currentSong;
            int currentDifficulty;
            
            lock (_updateLock)
            {
                currentSong = _currentSong;
                currentDifficulty = _currentDifficulty;
            }
            
            // Check for songs (including those with database metadata) 
            var songType = currentSong?.Type ?? NodeType.BackBox;
            var scoresLength = currentSong?.Scores?.Length ?? 0;
            var hasDatabase = currentSong?.DatabaseSong != null;
            
            if (songType == NodeType.Score && (scoresLength > 0 || hasDatabase))
            {
                // Draw DTXManiaNX authentic layout
                DrawDTXManiaNXLayout(spriteBatch, bounds, currentSong, currentDifficulty);
            }
            else
            {
                // Draw simplified info for non-score items
                DrawSimplifiedInfo(spriteBatch, bounds, currentSong, currentDifficulty);
            }
        }

        private void DrawDTXManiaNXLayout(SpriteBatch spriteBatch, Rectangle bounds, SongListNode currentSong, int currentDifficulty)
        {
            // Draw BPM and song duration section (top area)
            DrawBPMSection(spriteBatch, bounds, currentSong, currentDifficulty);

            // Draw skill point section (above BPM)
            DrawSkillPointSection(spriteBatch, bounds, currentSong, currentDifficulty);

            // Draw 3×5 difficulty grid (main area)
            DrawDifficultyGrid(spriteBatch, bounds, currentSong, currentDifficulty);

            // Draw graph panel area (bottom area)
            DrawGraphPanel(spriteBatch, bounds);
        }

        private void DrawSimplifiedInfo(SpriteBatch spriteBatch, Rectangle bounds, SongListNode currentSong, int currentDifficulty)
        {
            var padding = SongSelectionUILayout.Spacing.CellPadding;
            float y = bounds.Y + padding;
            float x = bounds.X + padding;

            // Draw song type indicator
            DrawSongTypeInfo(spriteBatch, ref x, ref y, bounds.Width - (padding * 2), currentSong, currentDifficulty);
        }

        private void DrawSongTypeInfo(SpriteBatch spriteBatch, ref float x, ref float y, float maxWidth, SongListNode currentSong, int currentDifficulty)
        {
            string typeText = currentSong.Type switch
            {
                NodeType.Score => "♪ SONG",
                NodeType.Box => "📁 FOLDER",
                NodeType.BackBox => "⬅ BACK",
                NodeType.Random => "🎲 RANDOM",
                _ => "UNKNOWN"
            };

            DrawTextWithShadow(spriteBatch, _font, typeText, new Vector2(x, y), DTXManiaVisualTheme.SongSelection.StatusValueText);
            y += LINE_HEIGHT + SECTION_SPACING;

            // Draw title
            var title = currentSong.DisplayTitle ?? "Unknown";
            if (title.Length > 30)
                title = title.Substring(0, 27) + "...";

            DrawTextWithShadow(spriteBatch, _font, title, new Vector2(x, y), DTXManiaVisualTheme.SongSelection.StatusValueText);
            y += LINE_HEIGHT + SECTION_SPACING;
        }

        #region DTXManiaNX Authentic Layout Methods

        private void DrawBPMSection(SpriteBatch spriteBatch, Rectangle bounds, SongListNode currentSong, int currentDifficulty)
        {
            // Get the chart for the current difficulty, not just the primary chart
            var chart = GetCurrentDifficultyChart(currentSong, currentDifficulty);
            if (chart == null)
                return;

            // Draw BMP background texture first (5_BPM.png or fallback)
            DrawBPMBackground(spriteBatch);

            // Use positioning from SongSelectionUILayout
            var lengthPosition = SongSelectionUILayout.BPMSection.LengthTextPosition;
            var bpmPosition = SongSelectionUILayout.BPMSection.BPMTextPosition;

            // When using authentic 5_BPM.png, don't draw redundant labels
            bool useAuthenticTexture = _bpmBackgroundTexture != null && !_bpmBackgroundTexture.IsDisposed;

            // Draw song duration (DTXManiaNX format: "Length: 2:34" or just "2:34")
            var formattedDuration = FormatDuration(chart.Duration);
            var durationText = useAuthenticTexture ? formattedDuration : $"Length: {formattedDuration}";
            DrawTextWithShadow(spriteBatch, _smallFont ?? _font, durationText, lengthPosition, DTXManiaVisualTheme.SongSelection.StatusValueText);

            // Draw BPM value (DTXManiaNX format: "BPM: 145" or just "145")
            if (chart.Bpm > 0)
            {
                var bpmText = useAuthenticTexture ? $"{chart.Bpm:F0}" : $"BPM: {chart.Bpm:F0}";
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, bpmText, bpmPosition, DTXManiaVisualTheme.SongSelection.StatusValueText);
            }
        }

        /// <summary>
        /// Draw BPM background using authentic texture or fallback generation
        /// </summary>
        private void DrawBPMBackground(SpriteBatch spriteBatch)
        {
            // Determine position and size based on standalone mode
            Vector2 position;
            Vector2 size;
            
            if (UseStandaloneBPMBackground)
            {
                // X:490, Y:385 (standalone mode from DTXManiaNX)
                position = new Vector2(490, 385);
                size = SongSelectionUILayout.BPMSection.Size;
            }
            else
            {
                // X:90, Y:275 (with panel mode from DTXManiaNX)
                position = SongSelectionUILayout.BPMSection.Position;
                size = SongSelectionUILayout.BPMSection.Size;
            }

            // Try to use the authentic 5_BPM.png texture first
            if (_bpmBackgroundTexture != null && !_bpmBackgroundTexture.IsDisposed)
            {
                try
                {
                    // Scale the texture to fit the panel bounds
                    var sourceRect = new Rectangle(0, 0, _bpmBackgroundTexture.Width, _bpmBackgroundTexture.Height);
                    var destinationRect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
                    _bpmBackgroundTexture.Draw(spriteBatch, destinationRect, sourceRect, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
                }
                catch (ObjectDisposedException)
                {
                    // Texture was disposed, clear reference
                    _bpmBackgroundTexture = null;
                }
            }
            else if (_graphicsGenerator != null)
            {
                // Generate fallback background texture when 5_BPM.png is unavailable
                try
                {
                    var fallbackTexture = _graphicsGenerator.GenerateBPMBackground((int)size.X, (int)size.Y, true);
                    if (fallbackTexture != null && !fallbackTexture.IsDisposed)
                    {
                        fallbackTexture.Draw(spriteBatch, position);
                        // Note: Don't dispose here - texture is cached and managed by DefaultGraphicsGenerator
                    }
                }
                catch
                {
                    // Fallback failed, draw simple background
                    DrawSimpleBPMBackground(spriteBatch, position, size);
                }
            }
            else
            {
                // Draw simple background when no graphics generator is available
                DrawSimpleBPMBackground(spriteBatch, position, size);
            }
        }

        /// <summary>
        /// Draw simple BPM background fallback
        /// </summary>
        private void DrawSimpleBPMBackground(SpriteBatch spriteBatch, Vector2 position, Vector2 size)
        {
            if (_whitePixel != null)
            {
                var rect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
                spriteBatch.Draw(_whitePixel, rect, Color.DarkBlue * 0.7f);
                
                // Draw border
                var borderThickness = SongSelectionUILayout.Spacing.BorderThickness;
                var borderColor = Color.Blue;
                
                // Top border
                spriteBatch.Draw(_whitePixel, new Rectangle(rect.X, rect.Y, rect.Width, borderThickness), borderColor);
                // Bottom border
                spriteBatch.Draw(_whitePixel, new Rectangle(rect.X, rect.Bottom - borderThickness, rect.Width, borderThickness), borderColor);
                // Left border
                spriteBatch.Draw(_whitePixel, new Rectangle(rect.X, rect.Y, borderThickness, rect.Height), borderColor);
                // Right border
                spriteBatch.Draw(_whitePixel, new Rectangle(rect.Right - borderThickness, rect.Y, borderThickness, rect.Height), borderColor);
            }
        }

        private void DrawSkillPointSection(SpriteBatch spriteBatch, Rectangle bounds, SongListNode currentSong, int currentDifficulty)
        {
            var score = GetCurrentScore(currentSong, currentDifficulty);
            if (score == null)
                return;

            // Use positioning from SongSelectionUILayout
            var skillPanelPosition = SongSelectionUILayout.SkillPointSection.Position;
            var skillPanelSize = SongSelectionUILayout.SkillPointSection.Size;
            var skillValuePosition = SongSelectionUILayout.SkillPointSection.ValuePosition;

            // Draw skill point background panel (if available)
            if (_whitePixel != null)
            {
                var skillPanelRect = new Rectangle((int)skillPanelPosition.X, (int)skillPanelPosition.Y, (int)skillPanelSize.X, (int)skillPanelSize.Y);
                spriteBatch.Draw(_whitePixel, skillPanelRect, Color.DarkBlue * 0.7f);
            }

            // Draw highest skill point value (DTXManiaNX format: "##0.00")
            var skillValue = score.HighSkill > 0 ? score.HighSkill.ToString("F2") : "0.00";
            DrawTextWithShadow(spriteBatch, _smallFont ?? _font, skillValue, skillValuePosition, Color.Yellow);
        }

        private void DrawDifficultyGrid(SpriteBatch spriteBatch, Rectangle bounds, SongListNode currentSong, int currentDifficulty)
        {
            // Draw difficulty panel background texture if available
            if (_difficultyPanelTexture != null)
            {
                // Position the difficulty panel texture to match the top-left corner of the grid
                // In DTXManiaNX, difficulty 4 (hardest) appears at the top, difficulty 0 (easiest) at bottom
                var topLeftCellPosition = SongSelectionUILayout.DifficultyGrid.GetCellPosition(4, 0);
                
                // Draw texture aligned with the actual top-left cell of the grid
                _difficultyPanelTexture.Draw(spriteBatch, topLeftCellPosition);
            }

            // Draw difficulty grid based on actual chart levels from SET.def
            // Grid layout: Row 0 = Level 1, Row 1 = Level 2, Row 2 = Level 3, Row 3 = Level 4, Row 4 = Level 5+
            // Higher levels appear at higher row indices (closer to top visually due to inverted Y calculation)

            // DTXManiaNX Column Structure (from documentation)
            // Column 0: Drums (D), Column 1: Guitar (G), Column 2: Bass (B)
            var instruments = new[] { 0, 1, 2 }; // Drums, Guitar, Bass

            // Revert to original working logic for now
            // Draw 3×5 difficulty grid using DTXManiaNX formula
            for (int i = 0; i < 5; i++) // 5 difficulty levels (0=Novice to 4=Ultimate)
            {
                for (int j = 0; j < 3; j++) // 3 instruments
                {
                    // Use SongSelectionUILayout method to get cell position
                    var cellPosition = SongSelectionUILayout.DifficultyGrid.GetCellPosition(i, j);
                    DrawDifficultyCell(spriteBatch, (int)cellPosition.X, (int)cellPosition.Y, i, j, null);
                }
            }

            // Draw difficulty frame texture over the currently selected cell
            DrawDifficultyFrame(spriteBatch);
        }

        private void DrawDifficultyCell(SpriteBatch spriteBatch, int x, int y, int gridRow, int instrument, ChartLevelInfo chartInfo = null)
        {
            // Use DTXManiaNX authentic cell dimensions from SongSelectionUILayout
            var cellSize = SongSelectionUILayout.DifficultyGrid.CellSize;
            var cellWidth = (int)cellSize.X;
            var cellHeight = (int)cellSize.Y;

            // Only draw cell content (no background or borders) - the panel texture provides the background
            DrawDifficultyCellContent(spriteBatch, x, y, cellWidth, cellHeight, gridRow, instrument, chartInfo);
        }

        private void DrawDifficultyFrame(SpriteBatch spriteBatch)
        {
            // Only draw frame if texture is available
            if (_difficultyFrameTexture == null)
                return;

            // For now, use the current difficulty and default to drums column
            int selectedColumn = 0; // Default to drums column
            var selectedCellPosition = SongSelectionUILayout.DifficultyGrid.GetCellPosition(_currentDifficulty, selectedColumn);
            
            // Draw the frame texture aligned exactly with the selected cell
            _difficultyFrameTexture.Draw(spriteBatch, selectedCellPosition);
        }

        private void DrawDifficultyCellContent(SpriteBatch spriteBatch, int x, int y, int cellWidth, int cellHeight, int gridRow, int instrument, ChartLevelInfo chartInfo = null)
        {
            // Position text at bottom-right corner of the cell with small padding
            var rightPadding = 4; // Small right padding  
            var textOffsetY = cellHeight - 16; // Bottom position with small padding (assuming 16px font height)

            // Get available charts and find one that matches this grid position
            var availableCharts = GetAvailableChartsWithLevels();
            
            // Get charts for this instrument only and sort by level (DESCENDING - hardest first)
            var instrumentCharts = availableCharts
                .Where(chart => chart.InstrumentColumn == instrument)
                .OrderByDescending(chart => chart.Level)  // Sort HIGH to LOW: [L5, L3, L2]
                .ToList();
            
            // IMPORTANT: DTXManiaNX coordinate system is INVERTED!
            // In the rendering loop: i=0 → BOTTOM visual position, i=4 → TOP visual position
            // GetCellPosition formula: nBoxY = BaseY + ((4 - difficultyLevel) * CellHeight)
            // So gridRow=0 renders at BOTTOM, gridRow=4 renders at TOP
            
            ChartLevelInfo matchingChart = null;
            if (instrumentCharts.Count > 0)
            {
                // User requirement: "L5 chart at top row" = gridRow=4 (due to inverted coordinates)
                // User requirement: "L2 and L3 chart at 2nd and 3rd row (count from bottom)" = gridRow=1 and gridRow=2
                
                if (instrumentCharts.Count == 1)
                {
                    // Single chart goes to top row (gridRow=4 due to inverted Y)
                    if (gridRow == 4)
                    {
                        matchingChart = instrumentCharts[0]; // The only chart
                    }
                }
                else if (instrumentCharts.Count == 2)
                {
                    // Two charts: hardest at top (gridRow=4), easiest at bottom (gridRow=0)
                    if (gridRow == 4)
                    {
                        matchingChart = instrumentCharts[0]; // Hardest chart (first in desc order)
                    }
                    else if (gridRow == 0)
                    {
                        matchingChart = instrumentCharts[1]; // Easiest chart (last in desc order)
                    }
                }
                else
                {
                    // 3+ charts: distribute across rows, with hardest at top (gridRow=4)
                    // gridRow=4 gets index 0 (hardest), gridRow=3 gets index 1, etc.
                    int chartIndex = 4 - gridRow; // Invert: gridRow=4→index=0, gridRow=3→index=1, etc.
                    if (chartIndex >= 0 && chartIndex < instrumentCharts.Count)
                    {
                        matchingChart = instrumentCharts[chartIndex];
                    }
                }
            }

            if (matchingChart == null)
            {
                // No chart exists for this position - show empty
                var emptyText = "--";
                var emptyTextWidth = (_smallFont ?? _font)?.MeasureString(emptyText).X ?? 0;
                var emptyTextX = x + cellWidth - emptyTextWidth - rightPadding;
                
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, emptyText, new Vector2(emptyTextX, y + textOffsetY), Color.Gray);
                return;
            }

            // Display the actual chart level from SET.def
            var levelText = matchingChart.Level.ToString("F2"); // Show as "5.00", "3.80", etc.
            
            // Determine if this chart is currently selected
            var isSelected = IsChartSelected(matchingChart);
            var textColor = isSelected ? Color.Yellow : Color.White;
            
            // Calculate right-aligned position and draw the level at bottom-right corner
            var levelTextWidth = (_smallFont ?? _font)?.MeasureString(levelText).X ?? 0;
            var levelTextX = x + cellWidth - levelTextWidth - rightPadding;
            
            DrawTextWithShadow(spriteBatch, _smallFont ?? _font, levelText, new Vector2(levelTextX, y + textOffsetY), textColor);
        }

        private void DrawGraphPanel(SpriteBatch spriteBatch, Rectangle bounds)
        {
            // Get the chart for the current difficulty level
            var chart = GetCurrentDifficultyChart(_currentSong, _currentDifficulty);
            var score = GetCurrentScore(_currentSong, _currentDifficulty);
            if (chart == null)
                return;

            // Use positioning from SongSelectionUILayout
            var graphPanelPosition = SongSelectionUILayout.GraphPanel.BasePosition;
            var graphPanelSize = SongSelectionUILayout.GraphPanel.Size;
            var notesCounterPosition = SongSelectionUILayout.GraphPanel.NotesCounterPosition;
            var progressBarPosition = SongSelectionUILayout.GraphPanel.ProgressBarPosition;

            // Draw graph panel background (use authentic texture if available)
            if (_whitePixel != null)
            {
                var graphRect = new Rectangle((int)graphPanelPosition.X, (int)graphPanelPosition.Y, (int)graphPanelSize.X, (int)graphPanelSize.Y);
                spriteBatch.Draw(_whitePixel, graphRect, Color.Black * 0.5f);
            }

            // Draw total notes counter using centralized SongChart methods
            DrawNotesCounter(spriteBatch, chart, notesCounterPosition);

            // Draw note distribution bar graph
            DrawNoteDistributionBars(spriteBatch, (int)graphPanelPosition.X, (int)graphPanelPosition.Y, score);

            // Draw progress bar
            DrawProgressBar(spriteBatch, (int)progressBarPosition.X, (int)progressBarPosition.Y, score);
        }

        private void DrawNoteDistributionBars(SpriteBatch spriteBatch, int baseX, int baseY, SongScore score)
        {
            if (_whitePixel == null || score == null)
                return;

            // Determine if this is drums or guitar/bass mode
            var isDrumsMode = GetInstrumentFromDifficulty(_currentDifficulty) == "DRUMS";

            if (isDrumsMode)
            {
                // Use drums configuration from SongSelectionUILayout
                for (int i = 0; i < SongSelectionUILayout.NoteDistributionBars.Drums.LaneCount; i++)
                {
                    var barPosition = SongSelectionUILayout.NoteDistributionBars.Drums.GetBarPosition(i);
                    var barHeight = (int)(SongSelectionUILayout.NoteDistributionBars.Drums.MaxBarHeight * (0.2f + (i % 4) * 0.2f)); // Simulated distribution
                    var barColor = GetDrumLaneColor(i);

                    spriteBatch.Draw(_whitePixel, new Rectangle((int)barPosition.X, (int)barPosition.Y + SongSelectionUILayout.NoteDistributionBars.Drums.MaxBarHeight - barHeight, SongSelectionUILayout.NoteDistributionBars.Drums.BarWidth, barHeight), barColor);
                }
            }
            else
            {
                // Use guitar/bass configuration from SongSelectionUILayout
                for (int i = 0; i < SongSelectionUILayout.NoteDistributionBars.GuitarBass.LaneCount; i++)
                {
                    var barPosition = SongSelectionUILayout.NoteDistributionBars.GuitarBass.GetBarPosition(i);
                    var barHeight = (int)(SongSelectionUILayout.NoteDistributionBars.GuitarBass.MaxBarHeight * (0.3f + (i % 3) * 0.2f)); // Simulated distribution
                    var barColor = GetGuitarBassLaneColor(i);

                    spriteBatch.Draw(_whitePixel, new Rectangle((int)barPosition.X, (int)barPosition.Y + SongSelectionUILayout.NoteDistributionBars.GuitarBass.MaxBarHeight - barHeight, SongSelectionUILayout.NoteDistributionBars.GuitarBass.BarWidth, barHeight), barColor);
                }
            }
        }

        private void DrawProgressBar(SpriteBatch spriteBatch, int x, int y, SongScore score)
        {
            if (_whitePixel == null || score == null)
                return;

            // Use progress bar dimensions from SongSelectionUILayout
            var progressBarSize = SongSelectionUILayout.GraphPanel.ProgressBarSize;
            var progressWidth = (int)progressBarSize.X;
            var progressHeight = (int)progressBarSize.Y;
            var progress = Math.Min(1.0f, score.PlayCount / 10.0f); // Simple progress calculation

            // Background
            spriteBatch.Draw(_whitePixel, new Rectangle(x, y, progressWidth, progressHeight), Color.Gray * 0.5f);

            // Progress fill
            var fillWidth = (int)(progressWidth * progress);
            if (fillWidth > 0)
            {
                spriteBatch.Draw(_whitePixel, new Rectangle(x, y, fillWidth, progressHeight), Color.Green);
            }
        }

        /// <summary>
        /// Draw the notes counter using centralized SongChart methods
        /// </summary>
        private void DrawNotesCounter(SpriteBatch spriteBatch, DTXMania.Game.Lib.Song.Entities.SongChart chart, Vector2 position)
        {
            if (chart == null)
            {
                return;
            }

            // Use the new centralized note count methods
            if (!chart.HasAnyNotes())
            {
                // Draw "No notes" indicator if chart has no notes
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, "No notes", position, Color.Gray);
                return;
            }

            // Get note count statistics
            var (total, drums, guitar, bass) = chart.GetNoteCountStats();
            
            // Determine which instrument is currently selected to highlight relevant count
            var currentInstrument = GetInstrumentFromDifficulty(_currentDifficulty);
            var currentInstrumentCount = chart.GetInstrumentNoteCount(currentInstrument);
            
            // Create formatted text based on current context
            string notesText;
            Color textColor;
            
            if (currentInstrumentCount > 0)
            {
                // Show only current instrument count (remove duplicate total text)
                notesText = $"{currentInstrumentCount:N0}";
                textColor = Color.Yellow; // Highlight current instrument
            }
            else
            {
                // No notes for current instrument, show total only
                notesText = chart.GetFormattedNoteCount(false);
                textColor = Color.Cyan;
            }

            // Draw main notes text
            DrawTextWithShadow(spriteBatch, _smallFont ?? _font, notesText, position, textColor);
        }

        #endregion

        private void DrawNoSongMessage(SpriteBatch spriteBatch, Rectangle bounds)
        {
            var message = "No song selected";

            // Calculate message size and position
            Vector2 messageSize;
            if (_font != null)
            {
                messageSize = _font.MeasureString(message);
            }
            else if (_managedFont != null)
            {
                messageSize = _managedFont.MeasureString(message);
            }
            else
            {
                messageSize = new Vector2(message.Length * 8, 16); // Rough estimate
            }

            var messagePos = new Vector2(
                bounds.X + (bounds.Width - messageSize.X) / 2,
                bounds.Y + (bounds.Height - messageSize.Y) / 2
            );
            DrawTextWithShadow(spriteBatch, _font, message, messagePos, DTXManiaVisualTheme.SongSelection.StatusValueText);
        }

        private void DrawTextWithShadow(SpriteBatch spriteBatch, SpriteFont font, string text, Vector2 position, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Try SpriteFont first
            if (font != null)
            {
                // Draw shadow first
                var shadowPosition = position + DTXManiaVisualTheme.FontEffects.DefaultShadowOffset;
                spriteBatch.DrawString(font, text, shadowPosition, DTXManiaVisualTheme.FontEffects.DefaultShadowColor);

                // Draw main text
                spriteBatch.DrawString(font, text, position, color);
            }
            // Fallback to managed font
            else if (_managedFont != null)
            {
                // Draw shadow first
                var shadowPosition = position + DTXManiaVisualTheme.FontEffects.DefaultShadowOffset;
                _managedFont.DrawString(spriteBatch, text, shadowPosition, DTXManiaVisualTheme.FontEffects.DefaultShadowColor);

                // Draw main text
                _managedFont.DrawString(spriteBatch, text, position, color);
            }
        }

        private SongScore GetCurrentScore(SongListNode currentSong, int currentDifficulty)
        {
            if (currentSong?.Scores == null || currentDifficulty < 0 || currentDifficulty >= currentSong.Scores.Length)
                return null;

            return currentSong.Scores[currentDifficulty];
        }

        private string GetInstrumentFromDifficulty(int difficulty)
        {
            // For DTXMania, all difficulties (0-4) typically represent different difficulty levels for the same instrument
            // Default to DRUMS as the primary instrument, but this could be made configurable
            return "DRUMS";
        }

        private Color GetDrumLaneColor(int lane)
        {
            // Colors for drum lanes: LC, HH, LP, SD, HT, BD, LT, FT, CY
            return lane switch
            {
                0 => Color.Purple,    // LC
                1 => Color.Yellow,    // HH
                2 => Color.Purple,    // LP
                3 => Color.Red,       // SD
                4 => Color.Blue,      // HT
                5 => Color.Orange,    // BD
                6 => Color.Blue,      // LT
                7 => Color.Green,     // FT
                8 => Color.Cyan,      // CY
                _ => Color.White
            };
        }

        private Color GetGuitarBassLaneColor(int lane)
        {
            // Colors for guitar/bass lanes: R, G, B, Y, P, Pick
            return lane switch
            {
                0 => Color.Red,       // R
                1 => Color.Green,     // G
                2 => Color.Blue,      // B
                3 => Color.Yellow,    // Y
                4 => Color.Purple,    // P
                5 => Color.Orange,    // Pick
                _ => Color.White
            };
        }

        /// <summary>
        /// Formats a duration in seconds to a human-readable string format.
        /// Returns format "M:SS" for durations under 1 hour, "H:MM:SS" for durations 1 hour or longer.
        /// </summary>
        /// <param name="durationInSeconds">Duration in seconds to format</param>
        /// <returns>Formatted duration string</returns>
        private string FormatDuration(double durationInSeconds)
        {
            var duration = TimeSpan.FromSeconds(durationInSeconds);
            return duration.TotalHours >= 1 
                ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
                : $"{duration.Minutes}:{duration.Seconds:D2}";
        }

        /// <summary>
        /// Gets the chart for the current difficulty level
        /// </summary>
        private DTXMania.Game.Lib.Song.Entities.SongChart GetCurrentDifficultyChart(SongListNode currentSong, int currentDifficulty)
        {
            // If no song is selected, return null
            if (currentSong?.DatabaseSong == null)
            {
                return null;
            }
            
            // Get all charts for this song
            var allCharts = currentSong.DatabaseSong.Charts?.ToList();
            
            if (allCharts == null || allCharts.Count == 0)
            {
                var fallbackChart = currentSong.DatabaseChart;
                return fallbackChart; // Fallback to primary chart
            }

            // If we only have one chart, return it
            if (allCharts.Count == 1)
                return allCharts[0];

            // Determine the current instrument based on difficulty index
            // For now, assume difficulty 0-2 are for drums (basic, advanced, extreme)
            // Later difficulties (3-4) could be for other instruments
            var currentInstrument = GetInstrumentFromDifficulty(_currentDifficulty);
            
            // Get charts for the current instrument
            var instrumentCharts = allCharts.Where(chart =>
            {
                return currentInstrument.ToUpperInvariant() switch
                {
                    "DRUMS" => chart.HasDrumChart && chart.DrumLevel > 0,
                    "GUITAR" => chart.HasGuitarChart && chart.GuitarLevel > 0,
                    "BASS" => chart.HasBassChart && chart.BassLevel > 0,
                    _ => chart.HasDrumChart && chart.DrumLevel > 0 // Default to drums
                };
            }).ToList();

            if (instrumentCharts.Count == 0)
                return allCharts[0]; // Fallback if no matching instrument

            // If we only have one chart for this instrument, return it
            if (instrumentCharts.Count == 1)
                return instrumentCharts[0];

            // Sort charts by difficulty level (ASCENDING - easiest first) 
            var sortedCharts = instrumentCharts.OrderBy(chart =>
            {
                return currentInstrument.ToUpperInvariant() switch
                {
                    "DRUMS" => chart.DrumLevel,
                    "GUITAR" => chart.GuitarLevel,
                    "BASS" => chart.BassLevel,
                    _ => chart.DrumLevel
                };
            }).ToList();

            // Map difficulty index to chart index
            // For all instruments: 0=basic (lowest), 1=advanced, 2=extreme, 3=master, 4=ultimate (highest)
            // Map difficulty 0-4 to available charts sorted by level
            int chartIndex = Math.Clamp(currentDifficulty, 0, sortedCharts.Count - 1);

            var selectedChart = sortedCharts[chartIndex];

            return selectedChart;
        }

        /// <summary>
        /// Represents chart level information for grid positioning
        /// </summary>
        public class ChartLevelInfo
        {
            public float Level { get; set; }
            public int InstrumentColumn { get; set; }  // 0=Drums, 1=Guitar, 2=Bass
            public string InstrumentName { get; set; }
            public DTXMania.Game.Lib.Song.Entities.SongChart Chart { get; set; }
        }

        /// <summary>
        /// Get available charts with their levels for grid positioning
        /// </summary>
        private List<ChartLevelInfo> GetAvailableChartsWithLevels()
        {
            var chartLevels = new List<ChartLevelInfo>();

            if (_currentSong?.DatabaseSong?.Charts == null)
                return chartLevels;

            var allCharts = _currentSong.DatabaseSong.Charts.ToList();

            foreach (var chart in allCharts)
            {
                // Add drum chart if available
                if (chart.HasDrumChart && chart.DrumLevel > 0)
                {
                    // Convert integer level to decimal (e.g., 38 -> 3.8)
                    float drumLevel = chart.DrumLevel / 10.0f + (chart.DrumLevelDec / 100.0f);
                    chartLevels.Add(new ChartLevelInfo
                    {
                        Level = drumLevel,
                        InstrumentColumn = 0, // Drums column
                        InstrumentName = "DRUMS",
                        Chart = chart
                    });
                }

                // Add guitar chart if available
                if (chart.HasGuitarChart && chart.GuitarLevel > 0)
                {
                    // Convert integer level to decimal (e.g., 60 -> 6.0)
                    float guitarLevel = chart.GuitarLevel / 10.0f + (chart.GuitarLevelDec / 100.0f);
                    chartLevels.Add(new ChartLevelInfo
                    {
                        Level = guitarLevel,
                        InstrumentColumn = 1, // Guitar column
                        InstrumentName = "GUITAR",
                        Chart = chart
                    });
                }

                // Add bass chart if available
                if (chart.HasBassChart && chart.BassLevel > 0)
                {
                    // Convert integer level to decimal (e.g., 77 -> 7.7)
                    float bassLevel = chart.BassLevel / 10.0f + (chart.BassLevelDec / 100.0f);
                    chartLevels.Add(new ChartLevelInfo
                    {
                        Level = bassLevel,
                        InstrumentColumn = 2, // Bass column
                        InstrumentName = "BASS",
                        Chart = chart
                    });
                }
            }

            return chartLevels;
        }

        /// <summary>
        /// Check if a chart is currently selected
        /// </summary>
        private bool IsChartSelected(ChartLevelInfo chartInfo)
        {
            if (chartInfo == null)
                return false;

            // Get current instrument and chart
            var currentInstrument = GetInstrumentFromDifficulty(_currentDifficulty);
            var currentChart = GetCurrentDifficultyChart(_currentSong, _currentDifficulty);

            // Check if this is the current instrument and chart
            return currentInstrument.Equals(chartInfo.InstrumentName, StringComparison.OrdinalIgnoreCase) &&
                   currentChart != null && currentChart == chartInfo.Chart;
        }

        #endregion
    }
}
