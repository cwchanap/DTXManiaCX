using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.UI.Layout;
using DTX.Song;
using DTX.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Entities;

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
            catch
            {
                _bpmBackgroundTexture = null;
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
            lock (this)
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
            else if (_font != null || _managedFont != null)
            {
                DrawNoSongMessage(spriteBatch, bounds);
            }            else
            {
                // Fallback rendering when no fonts are available
                DrawNoSongMessage(spriteBatch, bounds);
            }            base.OnDraw(spriteBatch, deltaTime);
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
            
            lock (this)
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
                // Temporarily update the fields for the drawing methods (they expect _currentSong to be set)
                var originalSong = _currentSong;
                var originalDifficulty = _currentDifficulty;
                _currentSong = currentSong;
                _currentDifficulty = currentDifficulty;
                
                try
                {
                    // Draw DTXManiaNX authentic layout
                    DrawDTXManiaNXLayout(spriteBatch, bounds);
                }
                finally
                {
                    // Restore original state
                    _currentSong = originalSong;
                    _currentDifficulty = originalDifficulty;
                }
            }
            else
            {
                // Temporarily update the fields for the drawing methods
                var originalSong = _currentSong;
                var originalDifficulty = _currentDifficulty;
                _currentSong = currentSong;
                _currentDifficulty = currentDifficulty;
                
                try
                {
                    // Draw simplified info for non-score items
                    DrawSimplifiedInfo(spriteBatch, bounds);
                }
                finally
                {
                    // Restore original state
                    _currentSong = originalSong;
                    _currentDifficulty = originalDifficulty;
                }
            }
        }

        private void DrawDTXManiaNXLayout(SpriteBatch spriteBatch, Rectangle bounds)
        {
            // Draw BPM and song duration section (top area)
            DrawBPMSection(spriteBatch, bounds);

            // Draw skill point section (above BPM)
            DrawSkillPointSection(spriteBatch, bounds);

            // Draw 3×5 difficulty grid (main area)
            DrawDifficultyGrid(spriteBatch, bounds);

            // Draw graph panel area (bottom area)
            DrawGraphPanel(spriteBatch, bounds);
        }

        private void DrawSimplifiedInfo(SpriteBatch spriteBatch, Rectangle bounds)
        {
            var padding = SongSelectionUILayout.Spacing.CellPadding;
            float y = bounds.Y + padding;
            float x = bounds.X + padding;

            // Draw song type indicator
            DrawSongTypeInfo(spriteBatch, ref x, ref y, bounds.Width - (padding * 2));
        }

        private void DrawSongTypeInfo(SpriteBatch spriteBatch, ref float x, ref float y, float maxWidth)
        {
            string typeText = _currentSong.Type switch
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
            var title = _currentSong.DisplayTitle ?? "Unknown";
            if (title.Length > 30)
                title = title.Substring(0, 27) + "...";

            DrawTextWithShadow(spriteBatch, _font, title, new Vector2(x, y), DTXManiaVisualTheme.SongSelection.StatusValueText);
            y += LINE_HEIGHT + SECTION_SPACING;
        }

        private void DrawSongMetadata(SpriteBatch spriteBatch, ref float x, ref float y, float maxWidth)
        {
            // Use EF Core entities instead of legacy metadata
            var song = _currentSong?.DatabaseSong;
            var chart = GetCurrentDifficultyChart();
            
            if (song == null && chart == null)
            {
                return;
            }

            // Artist
            if (!string.IsNullOrEmpty(song?.Artist))
            {
                DrawLabelValue(spriteBatch, "Artist:", song.Artist, x, y);
                y += LINE_HEIGHT;
            }

            // Genre
            if (!string.IsNullOrEmpty(song?.Genre))
            {
                DrawLabelValue(spriteBatch, "Genre:", song.Genre, x, y);
                y += LINE_HEIGHT;
            }

            // BPM
            if (chart?.Bpm > 0)
            {
                DrawLabelValue(spriteBatch, "BPM:", chart.Bpm.ToString("F0"), x, y);
                y += LINE_HEIGHT;
            }

            // Duration
            if (chart?.Duration > 0)
            {
                var formattedDuration = FormatDuration(chart.Duration);
                DrawLabelValue(spriteBatch, "Duration:", formattedDuration, x, y);
                y += LINE_HEIGHT;
            }

            // Total notes using centralized SongChart methods
            if (chart != null && chart.HasAnyNotes())
            {
                var formattedNotes = chart.GetFormattedNoteCount(true); // Include breakdown
                DrawLabelValue(spriteBatch, "Total Notes:", formattedNotes, x, y);
                y += LINE_HEIGHT;
            }

            y += SECTION_SPACING;
        }

        private void DrawDifficultyInfo(SpriteBatch spriteBatch, ref float x, ref float y, float maxWidth)
        {
            DrawTextWithShadow(spriteBatch, _font, "Difficulties:", new Vector2(x, y), DTXManiaVisualTheme.SongSelection.StatusLabelText);
            y += LINE_HEIGHT;

            // Use EF Core entities instead of legacy metadata
            var chart = GetCurrentDifficultyChart();
            if (chart != null)
            {
                // Show available instruments with their difficulty levels and note counts
                var instruments = new[] { ("DRUMS", chart.DrumLevel, chart.DrumNoteCount, chart.HasDrumChart), 
                                        ("GUITAR", chart.GuitarLevel, chart.GuitarNoteCount, chart.HasGuitarChart), 
                                        ("BASS", chart.BassLevel, chart.BassNoteCount, chart.HasBassChart) };

                foreach (var (instrument, level, noteCount, hasChart) in instruments)
                {
                    if (hasChart && level > 0)
                    {
                        var instrumentName = GetInstrumentDisplayName(instrument);
                        var isCurrentInstrument = GetInstrumentFromDifficulty(_currentDifficulty) == instrument;

                        var color = isCurrentInstrument ? DTXManiaVisualTheme.SongSelection.CurrentDifficultyIndicator : DTXManiaVisualTheme.SongSelection.StatusValueText;
                        var prefix = isCurrentInstrument ? "► " : "  ";

                        var text = $"{prefix}{instrumentName}: Lv.{level}";
                        if (noteCount > 0)
                        {
                            // Use consistent formatting from SongChart
                            text += $" ({noteCount:N0} notes)";
                        }

                        DrawTextWithShadow(spriteBatch, _smallFont ?? _font, text, new Vector2(x + INDENT, y), color);
                        y += LINE_HEIGHT * 0.8f;
                    }
                }
            }
            else
            {
                // Fallback to old difficulty display for compatibility
                for (int i = 0; i < 5; i++)
                {
                    if (_currentSong.Scores?.Length > i && _currentSong.Scores[i] != null)
                    {
                        var scoreItem = _currentSong.Scores[i];
                        var difficultyName = GetDifficultyName(i);
                        var level = GetDifficultyLevel(scoreItem, i);
                        var isSelected = i == _currentDifficulty;

                        var color = isSelected ? DTXManiaVisualTheme.SongSelection.CurrentDifficultyIndicator : DTXManiaVisualTheme.SongSelection.StatusValueText;
                        var prefix = isSelected ? "► " : "  ";
                        var text = $"{prefix}{difficultyName}: {level}";

                        spriteBatch.DrawString(_smallFont ?? _font, text, new Vector2(x + INDENT, y), color);
                        y += LINE_HEIGHT * 0.8f;
                    }
                }
            }

            y += SECTION_SPACING;
        }

        private void DrawPerformanceStats(SpriteBatch spriteBatch, ref float x, ref float y, float maxWidth)
        {
            var score = GetCurrentScore();
            if (score == null)
                return;

            DrawTextWithShadow(spriteBatch, _font, "Performance:", new Vector2(x, y), DTXManiaVisualTheme.SongSelection.StatusLabelText);
            y += LINE_HEIGHT;

            // Best score
            if (score.BestScore > 0)
            {
                DrawLabelValue(spriteBatch, "Best Score:", score.BestScore.ToString("N0"), x + INDENT, y);
                y += LINE_HEIGHT * 0.8f;
            }

            // Best rank
            if (score.BestRank > 0)
            {
                var rankText = GetRankText(score.BestRank);
                DrawLabelValue(spriteBatch, "Best Rank:", rankText, x + INDENT, y);
                y += LINE_HEIGHT * 0.8f;
            }

            // Full combo status
            if (score.FullCombo)
            {
                spriteBatch.DrawString(_smallFont ?? _font, "  ★ FULL COMBO", new Vector2(x + INDENT, y), Color.Gold);
                y += LINE_HEIGHT * 0.8f;
            }

            // Play count
            if (score.PlayCount > 0)
            {
                DrawLabelValue(spriteBatch, "Play Count:", score.PlayCount.ToString(), x + INDENT, y);
                y += LINE_HEIGHT * 0.8f;
            }

            // Skill values
            if (score.HighSkill > 0)
            {
                DrawLabelValue(spriteBatch, "High Skill:", score.HighSkill.ToString("F2"), x + INDENT, y);
                y += LINE_HEIGHT * 0.8f;
            }
        }

        #region DTXManiaNX Authentic Layout Methods

        private void DrawBPMSection(SpriteBatch spriteBatch, Rectangle bounds)
        {
            // Get the chart for the current difficulty, not just the primary chart
            var chart = GetCurrentDifficultyChart();
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
                        fallbackTexture.Dispose(); // Dispose immediately as this is a one-time use texture
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

        private void DrawSkillPointSection(SpriteBatch spriteBatch, Rectangle bounds)
        {
            var score = GetCurrentScore();
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

        private void DrawDifficultyGrid(SpriteBatch spriteBatch, Rectangle bounds)
        {
            // Always draw all 5 difficulty levels regardless of available charts

            // Use positioning from SongSelectionUILayout
            var difficultyLabelPosition = SongSelectionUILayout.DifficultyGrid.DifficultyLabelPosition;

            // Draw "Difficulty" text
            DrawTextWithShadow(spriteBatch, _font, "Difficulty", difficultyLabelPosition, DTXManiaVisualTheme.SongSelection.StatusLabelText);

            // DTXManiaNX Column Structure (from documentation)
            // Column 0: Drums (D), Column 1: Guitar (G), Column 2: Bass (B)
            var nPart = new[] { 0, 1, 2 }; // Simplified: Drums, Guitar, Bass (no swapping for now)

            // Draw 3×5 difficulty grid using DTXManiaNX formula
            for (int i = 0; i < 5; i++) // 5 difficulty levels (0=Novice to 4=Ultimate)
            {
                for (int j = 0; j < 3; j++) // 3 instruments
                {
                    // Use SongSelectionUILayout method to get cell position
                    var cellPosition = SongSelectionUILayout.DifficultyGrid.GetCellPosition(i, j);
                    DrawDifficultyCell(spriteBatch, (int)cellPosition.X, (int)cellPosition.Y, i, j);
                }
            }
        }

        private void DrawDifficultyCell(SpriteBatch spriteBatch, int x, int y, int difficultyLevel, int instrument)
        {
            // Use DTXManiaNX authentic cell dimensions from SongSelectionUILayout
            var cellSize = SongSelectionUILayout.DifficultyGrid.CellSize;
            var cellWidth = (int)cellSize.X;
            var cellHeight = (int)cellSize.Y;

            // Draw cell background
            if (_whitePixel != null)
            {
                var isSelected = difficultyLevel == _currentDifficulty;
                var cellColor = isSelected ? Color.Yellow * 0.3f : Color.Gray * 0.2f;
                spriteBatch.Draw(_whitePixel, new Rectangle(x, y, cellWidth, cellHeight), cellColor);

                // Draw cell border
                var borderColor = isSelected ? Color.Yellow : Color.Gray;
                var borderThickness = SongSelectionUILayout.Spacing.BorderThickness;
                // Top border
                spriteBatch.Draw(_whitePixel, new Rectangle(x, y, cellWidth, borderThickness), borderColor);
                // Bottom border
                spriteBatch.Draw(_whitePixel, new Rectangle(x, y + cellHeight - borderThickness, cellWidth, borderThickness), borderColor);
                // Left border
                spriteBatch.Draw(_whitePixel, new Rectangle(x, y, borderThickness, cellHeight), borderColor);
                // Right border
                spriteBatch.Draw(_whitePixel, new Rectangle(x + cellWidth - borderThickness, y, borderThickness, cellHeight), borderColor);
            }

            // Draw cell content: difficulty level for the specific combination
            DrawDifficultyCellContent(spriteBatch, x, y, cellWidth, cellHeight, difficultyLevel, instrument);
        }

        private void DrawDifficultyCellContent(SpriteBatch spriteBatch, int x, int y, int cellWidth, int cellHeight, int difficultyLevel, int instrument)
        {
            var textOffsetX = SongSelectionUILayout.DifficultyGrid.CellTextOffsetX;
            var emptyTextOffsetY = SongSelectionUILayout.DifficultyGrid.CellEmptyOffsetY;
            var levelTextOffsetY = SongSelectionUILayout.DifficultyGrid.CellTextOffsetY;
            var rankTextOffsetY = SongSelectionUILayout.DifficultyGrid.CellRankOffsetY;
            var scoreTextOffsetY = SongSelectionUILayout.DifficultyGrid.CellScoreOffsetY;

            // Use the Scores array which contains the actual difficulty levels we want to display
            if (_currentSong?.Scores == null || difficultyLevel >= _currentSong.Scores.Length)
            {
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, "--", new Vector2(x + textOffsetX, y + emptyTextOffsetY), Color.Gray);
                return;
            }
            
            var score = _currentSong.Scores[difficultyLevel];
            if (score == null)
            {
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, "--", new Vector2(x + textOffsetX, y + emptyTextOffsetY), Color.Gray);
                return;
            }
            
            // Check if this instrument column matches the score's instrument type
            var expectedInstrument = instrument switch
            {
                0 => DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS,
                1 => DTXMania.Game.Lib.Song.Entities.EInstrumentPart.GUITAR,
                2 => DTXMania.Game.Lib.Song.Entities.EInstrumentPart.BASS,
                _ => DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS
            };
            
            // Only show data if the instrument matches, otherwise show "--"
            if (score.Instrument != expectedInstrument)
            {
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, "--", new Vector2(x + textOffsetX, y + emptyTextOffsetY), Color.Gray);
                return;
            }
            
            var level = score.DifficultyLevel;
            if (level <= 0)
            {
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, "--", new Vector2(x + textOffsetX, y + emptyTextOffsetY), Color.Gray);
                return;
            }

            // Fix the formatting: divide by 10 and use "F2" for proper decimal display with trailing zeros
            // Level 34 should display as "3.40", level 58 should display as "5.80"
            var levelText = (level / 10.0).ToString("F2");
            var textColor = (difficultyLevel == _currentDifficulty) ? Color.Yellow : Color.White;
            
            DrawTextWithShadow(spriteBatch, _smallFont ?? _font, levelText, new Vector2(x + textOffsetX, y + levelTextOffsetY), textColor);

            // Draw rank icon (if available)
            if (score.BestRank > 0)
            {
                var rankText = GetRankText(score.BestRank);
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, rankText, new Vector2(x + textOffsetX, y + rankTextOffsetY), GetRankColor(score.BestRank));
            }

            // Draw achievement rate (if available)
            if (score.BestScore > 0)
            {
                var achievementText = score.BestScore >= 1000000 ? "MAX" : $"{score.BestScore / 10000.0:F1}%";
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, achievementText, new Vector2(x + textOffsetX, y + scoreTextOffsetY), Color.Cyan);
            }
        }

        private void DrawGraphPanel(SpriteBatch spriteBatch, Rectangle bounds)
        {
            // Get the chart for the current difficulty level
            var chart = GetCurrentDifficultyChart();
            var score = GetCurrentScore();
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
        private void DrawLabelValue(SpriteBatch spriteBatch, string label, string value, float x, float y)
        {
            var font = _smallFont ?? _font;
            var managedFont = _managedSmallFont ?? _managedFont;

            DrawTextWithShadow(spriteBatch, font, label, new Vector2(x, y), DTXManiaVisualTheme.SongSelection.StatusLabelText);

            // Calculate label width for positioning the value
            Vector2 labelSize;
            if (font != null)
            {
                labelSize = font.MeasureString(label);
            }
            else if (managedFont != null)
            {
                labelSize = managedFont.MeasureString(label);
            }
            else
            {
                labelSize = new Vector2(label.Length * 8, 16); // Rough estimate
            }

            DrawTextWithShadow(spriteBatch, font, value, new Vector2(x + labelSize.X + SongSelectionUILayout.Spacing.LabelValueSpacing, y), DTXManiaVisualTheme.SongSelection.StatusValueText);
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

        private SongScore GetCurrentScore()
        {
            if (_currentSong?.Scores == null || _currentDifficulty < 0 || _currentDifficulty >= _currentSong.Scores.Length)
                return null;

            return _currentSong.Scores[_currentDifficulty];
        }

        private string GetDifficultyName(int difficulty)
        {
            return difficulty switch
            {
                0 => "Basic",
                1 => "Advanced",
                2 => "Extreme",
                3 => "Master",
                4 => "Ultimate",
                _ => $"Diff {difficulty + 1}"
            };
        }

        private string GetDifficultyLevel(SongScore score, int difficulty)
        {
            // First, try to use the score's instrument type property if available
            if (score != null)
            {
                var chart = score.Chart ?? GetCurrentDifficultyChart();
                if (chart != null)
                {
                    return score.Instrument switch
                    {
                        EInstrumentPart.DRUMS => chart.DrumLevel.ToString(),
                        EInstrumentPart.GUITAR => chart.GuitarLevel.ToString(),
                        EInstrumentPart.BASS => chart.BassLevel.ToString(),
                        _ => "??"
                    };
                }
            }
            
            // Fallback to chart-based lookup using difficulty index if score instrument is not available
            var fallbackChart = GetCurrentDifficultyChart();
            if (fallbackChart != null)
            {
                return difficulty switch
                {
                    0 => fallbackChart.DrumLevel.ToString(),
                    1 => fallbackChart.GuitarLevel.ToString(),
                    2 => fallbackChart.BassLevel.ToString(),
                    3 => "??", // Master level not implemented yet
                    4 => "??", // Ultimate level not implemented yet
                    _ => "??"
                };
            }
            
            return "??";
        }

        private string GetRankText(int rank)
        {
            return rank switch
            {
                >= 95 => "SS",
                >= 90 => "S",
                >= 80 => "A",
                >= 70 => "B",
                >= 60 => "C",
                >= 50 => "D",
                _ => "E"
            };
        }

        private string GetInstrumentDisplayName(string instrument)
        {
            return instrument.ToUpperInvariant() switch
            {
                "DRUMS" => "Drums",
                "GUITAR" => "Guitar",
                "BASS" => "Bass",
                _ => instrument
            };
        }

        private string GetInstrumentFromDifficulty(int difficulty)
        {
            // For DTXMania, all difficulties (0-4) typically represent different difficulty levels for the same instrument
            // Default to DRUMS as the primary instrument, but this could be made configurable
            return "DRUMS";
        }

        private SongScore GetScoreForDifficultyAndInstrument(int difficultyLevel, int instrument)
        {
            // For now, use the current score system
            // In a full implementation, this would map to the specific instrument/difficulty combination
            if (_currentSong?.Scores != null && difficultyLevel >= 0 && difficultyLevel < _currentSong.Scores.Length)
            {
                return _currentSong.Scores[difficultyLevel];
            }
            return null;
        }

        private string GetDifficultyLevelForInstrument(SongScore score, int instrument)
        {
            // Get level from EF Core chart
            var chart = GetCurrentDifficultyChart();
            if (chart != null)
            {
                var level = instrument switch
                {
                    0 => chart.DrumLevel, // Drums
                    1 => chart.GuitarLevel, // Guitar
                    2 => chart.BassLevel, // Bass
                    _ => 0
                };
                
                if (level > 0)
                {
                    // Convert from integer format (e.g. 560) to decimal format (e.g. 5.60)
                    // Fix: 56 should display as 5.60, and use F1 for trailing zero
                    return (level / 10.0).ToString("F1");
                }
            }
            
            return "--";
        }

        private Color GetRankColor(int rank)
        {
            return rank switch
            {
                >= 95 => Color.Gold, // SS
                >= 90 => Color.Silver, // S
                >= 80 => Color.LightBlue, // A
                >= 70 => Color.Green, // B
                >= 60 => Color.Yellow, // C
                >= 50 => Color.Orange, // D
                _ => Color.Red // E
            };
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
        private DTXMania.Game.Lib.Song.Entities.SongChart GetCurrentDifficultyChart()
        {
            // If no song is selected, return null
            if (_currentSong?.DatabaseSong == null)
            {
                return null;
            }
            
            // Get all charts for this song
            var allCharts = _currentSong.DatabaseSong.Charts?.ToList();
            
            if (allCharts == null || allCharts.Count == 0)
            {
                var fallbackChart = _currentSong.DatabaseChart;
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
            int chartIndex = Math.Clamp(_currentDifficulty, 0, sortedCharts.Count - 1);

            var selectedChart = sortedCharts[chartIndex];

            return selectedChart;
        }

        #endregion
    }
}
