using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.Song;
using DTX.Resources;
using System;
using System.Linq;

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
        }

        /// <summary>
        /// Initialize graphics generator for enhanced rendering
        /// </summary>
        public void InitializeGraphicsGenerator(GraphicsDevice graphicsDevice)
        {
            _graphicsGenerator?.Dispose();
            _graphicsGenerator = new DefaultGraphicsGenerator(graphicsDevice);
        }

        /// <summary>
        /// Initialize DTXManiaNX authentic graphics (Phase 3)
        /// </summary>
        public void InitializeAuthenticGraphics(IResourceManager resourceManager)
        {
            _resourceManager = resourceManager;
            LoadStatusPanelGraphics();
        }

        private void LoadStatusPanelGraphics()
        {
            try
            {
                // Load DTXManiaNX status panel background
                _statusPanelTexture = _resourceManager.LoadTexture("Graphics/5_status panel.png");
                System.Diagnostics.Debug.WriteLine("SongStatusPanel: Loaded 5_status panel.png");
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
            Size = new Vector2(DTXManiaVisualTheme.Layout.StatusPanelWidth, 400);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Update the displayed song information
        /// </summary>
        /// <param name="song">Song to display</param>
        /// <param name="difficulty">Current difficulty level</param>
        public void UpdateSongInfo(SongListNode song, int difficulty)
        {
            _currentSong = song;
            _currentDifficulty = difficulty;
        }

        #endregion

        #region Protected Methods

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible)
            {
                return;
            }

            var bounds = Bounds;

            // Only log occasionally to reduce noise
            if (System.Environment.TickCount % 1000 < 50) // Log roughly once per second
            {
                System.Diagnostics.Debug.WriteLine($"SongStatusPanel: Drawing at bounds {bounds}, Font: {(_font != null ? "Available" : "Null")}, Song: {(_currentSong != null ? _currentSong.DisplayTitle : "None")}");
            }

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
            }
            else
            {
                // Fallback rendering when no fonts are available - this should always show something
                DrawFallbackContent(spriteBatch, bounds);
            }

            // Debug: Always show fallback content for now to test visibility
            if (_currentSong != null)
            {
                // Draw a small indicator in the corner to show the panel is working
                if (_whitePixel != null)
                {
                    var indicator = new Rectangle(bounds.Right - 20, bounds.Y + 5, 15, 15);
                    spriteBatch.Draw(_whitePixel, indicator, Color.Lime);
                }
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

            // Try to use generated panel background as fallback
            if (_graphicsGenerator != null)
            {
                var panelTexture = _graphicsGenerator.GeneratePanelBackground(bounds.Width, bounds.Height, true);
                if (panelTexture != null)
                {
                    panelTexture.Draw(spriteBatch, new Vector2(bounds.X, bounds.Y));
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
            float y = bounds.Y + 10;
            float x = bounds.X + 10;

            // Draw song type indicator
            DrawSongTypeInfo(spriteBatch, ref x, ref y, bounds.Width - 20);

            if (_currentSong.Type == NodeType.Score && _currentSong.Scores?.Length > 0)
            {
                // Draw song metadata
                DrawSongMetadata(spriteBatch, ref x, ref y, bounds.Width - 20);

                // Draw difficulty information
                DrawDifficultyInfo(spriteBatch, ref x, ref y, bounds.Width - 20);

                // Draw performance statistics
                DrawPerformanceStats(spriteBatch, ref x, ref y, bounds.Width - 20);
            }
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
            var score = GetCurrentScore();
            if (score?.Metadata == null)
                return;

            var metadata = score.Metadata;

            // Artist
            if (!string.IsNullOrEmpty(metadata.Artist))
            {
                DrawLabelValue(spriteBatch, "Artist:", metadata.Artist, x, y);
                y += LINE_HEIGHT;
            }

            // Genre
            if (!string.IsNullOrEmpty(metadata.Genre))
            {
                DrawLabelValue(spriteBatch, "Genre:", metadata.Genre, x, y);
                y += LINE_HEIGHT;
            }

            // BPM
            if (metadata.BPM.HasValue && metadata.BPM.Value > 0)
            {
                DrawLabelValue(spriteBatch, "BPM:", metadata.BPM.Value.ToString("F0"), x, y);
                y += LINE_HEIGHT;
            }

            // Duration
            if (metadata.Duration.HasValue && metadata.Duration.Value > 0)
            {
                DrawLabelValue(spriteBatch, "Duration:", metadata.FormattedDuration, x, y);
                y += LINE_HEIGHT;
            }

            // Total notes
            if (metadata.TotalNoteCount > 0)
            {
                DrawLabelValue(spriteBatch, "Total Notes:", metadata.TotalNoteCount.ToString("N0"), x, y);
                y += LINE_HEIGHT;
            }

            y += SECTION_SPACING;
        }

        private void DrawDifficultyInfo(SpriteBatch spriteBatch, ref float x, ref float y, float maxWidth)
        {
            DrawTextWithShadow(spriteBatch, _font, "Difficulties:", new Vector2(x, y), DTXManiaVisualTheme.SongSelection.StatusLabelText);
            y += LINE_HEIGHT;

            var score = GetCurrentScore();
            if (score?.Metadata != null)
            {
                // Show available instruments with their difficulty levels and note counts
                var instruments = new[] { "DRUMS", "GUITAR", "BASS" };

                foreach (var instrument in instruments)
                {
                    var level = score.Metadata.GetDifficultyLevel(instrument);
                    var noteCount = score.Metadata.GetNoteCount(instrument);

                    if (level.HasValue && level.Value > 0)
                    {
                        var instrumentName = GetInstrumentDisplayName(instrument);
                        var isCurrentInstrument = GetInstrumentFromDifficulty(_currentDifficulty) == instrument;

                        var color = isCurrentInstrument ? DTXManiaVisualTheme.SongSelection.CurrentDifficultyIndicator : DTXManiaVisualTheme.SongSelection.StatusValueText;
                        var prefix = isCurrentInstrument ? "► " : "  ";

                        var text = $"{prefix}{instrumentName}: Lv.{level}";
                        if (noteCount.HasValue && noteCount.Value > 0)
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

        private void DrawFallbackContent(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_whitePixel == null)
                return;

            // Draw status indicators using colored rectangles when font is unavailable
            var y = bounds.Y + 10;
            var x = bounds.X + 10;
            var lineHeight = 25;

            // Draw panel title indicator with bright border to make it visible
            spriteBatch.Draw(_whitePixel, new Rectangle(x - 2, y - 2, bounds.Width - 16, 24), Color.White);
            spriteBatch.Draw(_whitePixel, new Rectangle(x, y, bounds.Width - 20, 20), Color.DarkBlue * 0.8f);
            y += lineHeight;

            // Song type indicator
            if (_currentSong != null)
            {
                var typeColor = _currentSong.Type switch
                {
                    NodeType.Score => Color.LightBlue,
                    NodeType.Box => Color.Yellow,
                    NodeType.BackBox => Color.Orange,
                    NodeType.Random => Color.Magenta,
                    _ => Color.Gray
                };

                // Song type bar
                spriteBatch.Draw(_whitePixel, new Rectangle(x, y, 120, 18), typeColor);
                y += lineHeight;

                // Title indicator (wider and more visible)
                spriteBatch.Draw(_whitePixel, new Rectangle(x, y, 200, 15), Color.White);
                y += lineHeight;

                // Difficulty indicators (if it's a score)
                if (_currentSong.Type == NodeType.Score && _currentSong.Scores != null)
                {
                    // Draw difficulty label area
                    spriteBatch.Draw(_whitePixel, new Rectangle(x, y, 60, 12), Color.Gray);

                    // Draw individual difficulty indicators
                    for (int i = 0; i < Math.Min(5, _currentSong.Scores.Length); i++)
                    {
                        if (_currentSong.Scores[i] != null)
                        {
                            var diffColor = i == _currentDifficulty ? Color.Gold : Color.LightGray;
                            spriteBatch.Draw(_whitePixel, new Rectangle(x + 70 + i * 35, y, 30, 12), diffColor);
                        }
                    }
                    y += lineHeight;

                    // BPM indicator (if available)
                    if (_currentSong.Metadata?.BPM > 0)
                    {
                        var bpmValue = _currentSong.Metadata.BPM.GetValueOrDefault(0);
                        var bpmWidth = (int)Math.Min(150, bpmValue * 0.8);
                        spriteBatch.Draw(_whitePixel, new Rectangle(x, y, bpmWidth, 8), Color.Green);
                    }
                    y += 15;

                    // Score indicator (if available)
                    var currentScore = GetCurrentScore();
                    if (currentScore?.BestScore > 0)
                    {
                        var scoreWidth = (int)Math.Min(180, currentScore.BestScore / 1000);
                        spriteBatch.Draw(_whitePixel, new Rectangle(x, y, scoreWidth, 8), Color.Cyan);
                    }
                }
            }
            else
            {
                // No song selected indicator
                spriteBatch.Draw(_whitePixel, new Rectangle(x, y, 120, 18), Color.DarkGray);
                y += lineHeight;
                spriteBatch.Draw(_whitePixel, new Rectangle(x, y, 80, 12), Color.Red * 0.5f);
            }
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
            if (score?.Metadata == null)
                return "??";

            return difficulty switch
            {
                0 => score.Metadata.DrumLevel?.ToString() ?? "??",
                1 => score.Metadata.GuitarLevel?.ToString() ?? "??",
                2 => score.Metadata.BassLevel?.ToString() ?? "??",
                3 => "??", // Master level not in metadata
                4 => "??", // Ultimate level not in metadata
                _ => "??"
            };
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _graphicsGenerator?.Dispose();
                _graphicsGenerator = null;

                _statusPanelTexture?.Dispose();
                _statusPanelTexture = null;
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
