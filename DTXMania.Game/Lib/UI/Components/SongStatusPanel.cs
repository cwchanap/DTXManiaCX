using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.Song;
using DTX.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Entities;

// Type alias for SongScore to use the EF Core entity
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTX.UI.Components
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

        // Performance optimization: Cache generated background texture
        private ITexture _cachedBackgroundTexture;
        private Rectangle _cachedBackgroundSize;

        // DTXManiaNX layout constants (authentic positioning from documentation)
        private const int DIFFICULTY_GRID_X = 140; // Grid base X position (relative to main panel at X:130)
        private const int DIFFICULTY_GRID_Y = 52; // Grid base Y position (relative to main panel)
        private const int DIFFICULTY_CELL_WIDTH = 187; // Panel width per difficulty cell
        private const int DIFFICULTY_CELL_HEIGHT = 60; // Panel height per difficulty cell
        private const int BPM_SECTION_X = 90; // BPM section X position (relative to panel, when panel exists)
        private const int BPM_SECTION_Y = 275; // BPM section Y position (absolute Y:275 from doc)
        private const int SKILL_POINT_X = 32; // Skill point section X position (absolute X:32 from doc)
        private const int SKILL_POINT_Y = 180; // Skill point section Y position (absolute Y:180 from doc)
        private const int GRAPH_PANEL_X = 15; // Graph panel base X position (absolute X:15 from doc)
        private const int GRAPH_PANEL_Y = 368; // Graph panel base Y position (absolute Y:368 from doc)

        // Layout constants from DTXManiaNX theme
        private float LINE_HEIGHT => DTXManiaVisualTheme.Layout.StatusLineHeight;
        private float SECTION_SPACING => DTXManiaVisualTheme.Layout.StatusSectionSpacing;
        private float INDENT => DTXManiaVisualTheme.Layout.StatusPanelPadding;

        #endregion

        #region Properties

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
        }        private void LoadStatusPanelGraphics()
        {
            try
            {
                // Load DTXManiaNX status panel background
                _statusPanelTexture = _resourceManager.LoadTexture("Graphics/5_status panel.png");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongStatusPanel: Failed to load status panel background: {ex.Message}");
            }
        }

        #endregion

        #region Constructor

        public SongStatusPanel()
        {
            Size = new Vector2(DTXManiaVisualTheme.Layout.StatusPanelWidth, DTXManiaVisualTheme.Layout.StatusPanelHeight);
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
            // DEBUG: Log update parameters
            System.Diagnostics.Debug.WriteLine($"UpdateSongInfo: song='{song?.Title}', difficulty={difficulty}");
            if (song?.DatabaseChart != null)
            {
                var chart = song.DatabaseChart;
                System.Diagnostics.Debug.WriteLine($"  Song chart: DrumLevel={chart.DrumLevel}, GuitarLevel={chart.GuitarLevel}, BassLevel={chart.BassLevel}");
            }
            if (song?.Scores != null)
            {
                System.Diagnostics.Debug.WriteLine($"  Song scores array length: {song.Scores.Length}");
                for (int i = 0; i < song.Scores.Length; i++)
                {
                    var score = song.Scores[i];
                    if (score != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"    Score[{i}]: Instrument={score.Instrument}, DifficultyLevel={score.DifficultyLevel}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"    Score[{i}]: null");
                    }
                }
            }
            
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

                // Draw border
                var borderColor = DTXManiaVisualTheme.SongSelection.StatusBorder;
                var borderThickness = 2;

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
            float y = bounds.Y + 10;
            float x = bounds.X + 10;

            // Draw song type indicator
            DrawSongTypeInfo(spriteBatch, ref x, ref y, bounds.Width - 20);
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
            var chart = _currentSong?.DatabaseChart;
            
            if (song == null && chart == null)
                return;

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

            // Total notes
            var totalNotes = (chart?.DrumNoteCount ?? 0) + (chart?.GuitarNoteCount ?? 0) + (chart?.BassNoteCount ?? 0);
            if (totalNotes > 0)
            {
                DrawLabelValue(spriteBatch, "Total Notes:", totalNotes.ToString("N0"), x, y);
                y += LINE_HEIGHT;
            }

            y += SECTION_SPACING;
        }

        private void DrawDifficultyInfo(SpriteBatch spriteBatch, ref float x, ref float y, float maxWidth)
        {
            DrawTextWithShadow(spriteBatch, _font, "Difficulties:", new Vector2(x, y), DTXManiaVisualTheme.SongSelection.StatusLabelText);
            y += LINE_HEIGHT;

            // Use EF Core entities instead of legacy metadata
            var chart = _currentSong?.DatabaseChart;
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

            // Use absolute positioning from DTXManiaNX documentation
            // BPM section: X:90, Y:275 (when status panel exists)
            var x = BPM_SECTION_X;
            var y = BPM_SECTION_Y;

            // Draw song duration first (DTXManiaNX format: "Length: 2:34")
            // Duration display at X:132, Y:268
            var formattedDuration = FormatDuration(chart.Duration);
            var durationText = $"Length: {formattedDuration}";
            DrawTextWithShadow(spriteBatch, _smallFont ?? _font, durationText, new Vector2(132, 268), DTXManiaVisualTheme.SongSelection.StatusValueText);

            // Draw BPM value (DTXManiaNX format: "BPM: 145")
            // BPM value at X:135, Y:298
            if (chart.Bpm > 0)
            {
                var bpmText = $"BPM: {chart.Bpm:F0}";
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, bpmText, new Vector2(135, 298), DTXManiaVisualTheme.SongSelection.StatusValueText);
            }
        }

        private void DrawSkillPointSection(SpriteBatch spriteBatch, Rectangle bounds)
        {
            var score = GetCurrentScore();
            if (score == null)
                return;

            // Use absolute positioning from DTXManiaNX documentation
            // Skill Point Panel at X:32, Y:180
            var x = SKILL_POINT_X;
            var y = SKILL_POINT_Y;

            // Draw skill point background panel (if available)
            if (_whitePixel != null)
            {
                var skillPanelRect = new Rectangle(x, y, 120, 20);
                spriteBatch.Draw(_whitePixel, skillPanelRect, Color.DarkBlue * 0.7f);
            }

            // Draw highest skill point value (DTXManiaNX format: "##0.00")
            // Skill Point Value at X:92, Y:200 (32 + 60)
            var skillValue = score.HighSkill > 0 ? score.HighSkill.ToString("F2") : "0.00";
            DrawTextWithShadow(spriteBatch, _smallFont ?? _font, skillValue, new Vector2(92, 200), Color.Yellow);
        }

        private void DrawDifficultyGrid(SpriteBatch spriteBatch, Rectangle bounds)
        {
            // Always draw all 5 difficulty levels regardless of available charts

            // Use DTXManiaNX authentic positioning formula
            // Main Panel at X:130, Y:350, difficulty text at X:140, Y:352
            var nBaseX = 130; // Main panel X position (from documentation)
            var nBaseY = 350; // Main panel Y position (from documentation)

            // Draw "Difficulty" text at X:140, Y:352 (relative to main panel)
            DrawTextWithShadow(spriteBatch, _font, "Difficulty", new Vector2(140, 352), DTXManiaVisualTheme.SongSelection.StatusLabelText);

            // DTXManiaNX Column Structure (from documentation)
            // int[] nPart = { 0, CDTXMania.ConfigIni.bIsSwappedGuitarBass ? 2 : 1, CDTXMania.ConfigIni.bIsSwappedGuitarBass ? 1 : 2 };
            // Column 0: Drums (D), Column 1: Guitar (G), Column 2: Bass (B)
            var nPart = new[] { 0, 1, 2 }; // Simplified: Drums, Guitar, Bass (no swapping for now)

            // Panel dimensions from documentation
            var nPanelW = DIFFICULTY_CELL_WIDTH; // 187 pixels
            var nPanelH = DIFFICULTY_CELL_HEIGHT; // 60 pixels

            // Assume panel body width (this.txパネル本体.szImageSize.Width) - typical value from DTXManiaNX
            var panelBodyWidth = 561; // Estimated panel body width

            // Draw 3×5 difficulty grid using DTXManiaNX formula
            for (int i = 0; i < 5; i++) // 5 difficulty levels (0=Novice to 4=Ultimate)
            {
                for (int j = 0; j < 3; j++) // 3 instruments
                {
                    // DTXManiaNX authentic positioning formula:
                    // int nBoxX = nBaseX + this.txパネル本体.szImageSize.Width + (nPanelW * (nPart[j] - 3));
                    // int nBoxY = (391 + ((4 - i) * 60)) - 2;  // Higher difficulties at top

                    var nBoxX = nBaseX + panelBodyWidth + (nPanelW * (nPart[j] - 3));
                    var nBoxY = nBaseY + ((4 - i) * nPanelH) - 2; // Higher difficulties at top

                    DrawDifficultyCell(spriteBatch, nBoxX, nBoxY, i, j);
                }
            }
        }

        private void DrawDifficultyCell(SpriteBatch spriteBatch, int x, int y, int difficultyLevel, int instrument)
        {
            // Use DTXManiaNX authentic cell dimensions: 187×60 pixels
            var cellWidth = DIFFICULTY_CELL_WIDTH;
            var cellHeight = DIFFICULTY_CELL_HEIGHT;

            // Draw cell background
            if (_whitePixel != null)
            {
                var isSelected = difficultyLevel == _currentDifficulty;
                var cellColor = isSelected ? Color.Yellow * 0.3f : Color.Gray * 0.2f;
                spriteBatch.Draw(_whitePixel, new Rectangle(x, y, cellWidth, cellHeight), cellColor);

                // Draw cell border
                var borderColor = isSelected ? Color.Yellow : Color.Gray;
                var borderThickness = 2;
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
            // Use the Scores array which contains the actual difficulty levels we want to display
            if (_currentSong?.Scores == null || difficultyLevel >= _currentSong.Scores.Length)
            {
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, "--", new Vector2(x + 10, y + 10), Color.Gray);
                return;
            }
            
            var score = _currentSong.Scores[difficultyLevel];
            if (score == null)
            {
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, "--", new Vector2(x + 10, y + 10), Color.Gray);
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
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, "--", new Vector2(x + 10, y + 10), Color.Gray);
                return;
            }
            
            var level = score.DifficultyLevel;
            if (level <= 0)
            {
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, "--", new Vector2(x + 10, y + 10), Color.Gray);
                return;
            }

            // Fix the formatting: divide by 10 and use "F2" for proper decimal display with trailing zeros
            // Level 34 should display as "3.40", level 58 should display as "5.80"
            var levelText = (level / 10.0).ToString("F2");
            var textColor = (difficultyLevel == _currentDifficulty) ? Color.Yellow : Color.White;
            
            DrawTextWithShadow(spriteBatch, _smallFont ?? _font, levelText, new Vector2(x + 10, y + 5), textColor);

            // Draw rank icon (if available)
            if (score.BestRank > 0)
            {
                var rankText = GetRankText(score.BestRank);
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, rankText, new Vector2(x + 10, y + 25), GetRankColor(score.BestRank));
            }

            // Draw achievement rate (if available)
            if (score.BestScore > 0)
            {
                var achievementText = score.BestScore >= 1000000 ? "MAX" : $"{score.BestScore / 10000.0:F1}%";
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, achievementText, new Vector2(x + 10, y + 40), Color.Cyan);
            }
        }

        private void DrawGraphPanel(SpriteBatch spriteBatch, Rectangle bounds)
        {
            // Use EF Core entities instead of legacy metadata
            var chart = _currentSong?.DatabaseChart;
            var score = GetCurrentScore();
            if (chart == null)
                return;

            // Use absolute positioning from DTXManiaNX documentation
            // Graph Panel Base at X:15, Y:368
            var baseX = GRAPH_PANEL_X;
            var baseY = GRAPH_PANEL_Y;

            // Draw graph panel background (use authentic texture if available)
            if (_whitePixel != null)
            {
                var graphRect = new Rectangle(baseX, baseY, 350, 300); // Approximate size from documentation
                spriteBatch.Draw(_whitePixel, graphRect, Color.Black * 0.5f);
            }

            // Draw total notes counter at X:81 (15 + 66), Y:666 (368 + 298)
            var totalNotes = chart.DrumNoteCount + chart.GuitarNoteCount + chart.BassNoteCount;
            if (totalNotes > 0)
            {
                var notesText = totalNotes.ToString("N0");
                DrawTextWithShadow(spriteBatch, _smallFont ?? _font, notesText, new Vector2(81, 666), Color.Cyan);
            }

            // Draw note distribution bar graph
            DrawNoteDistributionBars(spriteBatch, baseX, baseY, score);

            // Draw progress bar at X:33 (15 + 18), Y:389 (368 + 21)
            DrawProgressBar(spriteBatch, 33, 389, score);
        }

        private void DrawNoteDistributionBars(SpriteBatch spriteBatch, int baseX, int baseY, SongScore score)
        {
            if (_whitePixel == null || score == null)
                return;

            // Determine if this is drums or guitar/bass mode
            var isDrumsMode = GetInstrumentFromDifficulty(_currentDifficulty) == "DRUMS";

            if (isDrumsMode)
            {
                // Drums configuration (9 lanes): LC, HH, LP, SD, HT, BD, LT, FT, CY
                // Start position: X:46 (15 + 31), Y:389 (368 + 21)
                // Spacing: 8 pixels between bars
                var startX = 46;
                var startY = 389;
                var barSpacing = 8;
                var barWidth = 4;
                var maxBarHeight = 252;

                for (int i = 0; i < 9; i++)
                {
                    var barX = startX + (i * (barWidth + barSpacing));
                    var barHeight = (int)(maxBarHeight * (0.2f + (i % 4) * 0.2f)); // Simulated distribution
                    var barColor = GetDrumLaneColor(i);

                    spriteBatch.Draw(_whitePixel, new Rectangle(barX, startY + maxBarHeight - barHeight, barWidth, barHeight), barColor);
                }
            }
            else
            {
                // Guitar/Bass configuration (6 lanes): R, G, B, Y, P, Pick
                // Start position: X:53 (15 + 38), Y:389 (368 + 21)
                // Spacing: 10 pixels between bars
                var startX = 53;
                var startY = 389;
                var barSpacing = 10;
                var barWidth = 4;
                var maxBarHeight = 252;

                for (int i = 0; i < 6; i++)
                {
                    var barX = startX + (i * (barWidth + barSpacing));
                    var barHeight = (int)(maxBarHeight * (0.3f + (i % 3) * 0.2f)); // Simulated distribution
                    var barColor = GetGuitarBassLaneColor(i);

                    spriteBatch.Draw(_whitePixel, new Rectangle(barX, startY + maxBarHeight - barHeight, barWidth, barHeight), barColor);
                }
            }
        }

        private void DrawProgressBar(SpriteBatch spriteBatch, int x, int y, SongScore score)
        {
            if (_whitePixel == null || score == null)
                return;

            // Draw a simple progress bar representing chart completion
            var progressWidth = 200;
            var progressHeight = 8;
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

            DrawTextWithShadow(spriteBatch, font, value, new Vector2(x + labelSize.X + 5, y), DTXManiaVisualTheme.SongSelection.StatusValueText);
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
                var chart = score.Chart ?? _currentSong?.DatabaseChart;
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
            var fallbackChart = _currentSong?.DatabaseChart;
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
            return difficulty switch
            {
                0 => "DRUMS",
                1 => "GUITAR",
                2 => "BASS",
                _ => "DRUMS" // Default to drums
            };
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
            var chart = _currentSong?.DatabaseChart;
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
                return null;

            // Get all charts for this song
            var allCharts = _currentSong.DatabaseSong.Charts?.ToList();
            if (allCharts == null || allCharts.Count == 0)
                return _currentSong.DatabaseChart; // Fallback to primary chart

            // If we only have one chart, return it
            if (allCharts.Count == 1)
                return allCharts[0];

            // Get the current score to determine which chart to use
            var currentScore = GetCurrentScore();
            if (currentScore == null)
                return allCharts[0]; // Default to first chart

            // Try to find a chart that matches the current instrument/difficulty
            // First, try to match by instrument
            var instrumentCharts = allCharts.Where(chart =>
            {
                return currentScore.Instrument switch
                {
                    DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS => chart.HasDrumChart && chart.DrumLevel > 0,
                    DTXMania.Game.Lib.Song.Entities.EInstrumentPart.GUITAR => chart.HasGuitarChart && chart.GuitarLevel > 0,
                    DTXMania.Game.Lib.Song.Entities.EInstrumentPart.BASS => chart.HasBassChart && chart.BassLevel > 0,
                    _ => true
                };
            }).ToList();

            if (instrumentCharts.Count == 0)
                return allCharts[0]; // Fallback if no matching instrument

            // If we have multiple charts for the same instrument, try to match by difficulty level
            if (instrumentCharts.Count == 1)
                return instrumentCharts[0];

            // Sort by difficulty level and select based on current difficulty index
            var sortedCharts = instrumentCharts.OrderBy(chart =>
            {
                return currentScore.Instrument switch
                {
                    DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS => chart.DrumLevel,
                    DTXMania.Game.Lib.Song.Entities.EInstrumentPart.GUITAR => chart.GuitarLevel,
                    DTXMania.Game.Lib.Song.Entities.EInstrumentPart.BASS => chart.BassLevel,
                    _ => 50
                };
            }).ToList();

            // Clamp difficulty index to available charts
            int chartIndex = Math.Clamp(_currentDifficulty, 0, sortedCharts.Count - 1);
            return sortedCharts[chartIndex];
        }

        #endregion
    }
}
