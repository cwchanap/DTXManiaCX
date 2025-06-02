using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.Song;
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
        private Texture2D _whitePixel;

        // Visual properties
        private Color _backgroundColor = Color.Black * 0.8f;
        private Color _titleColor = Color.White;
        private Color _labelColor = Color.Cyan;
        private Color _valueColor = Color.Yellow;
        private Color _difficultyColor = Color.Orange;

        // Layout constants
        private const float LINE_HEIGHT = 20f;
        private const float SECTION_SPACING = 10f;
        private const float INDENT = 20f;

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
        /// White pixel texture for backgrounds
        /// </summary>
        public Texture2D WhitePixel
        {
            get => _whitePixel;
            set => _whitePixel = value;
        }

        /// <summary>
        /// Background color
        /// </summary>
        public Color BackgroundColor
        {
            get => _backgroundColor;
            set => _backgroundColor = value;
        }

        #endregion

        #region Constructor

        public SongStatusPanel()
        {
            Size = new Vector2(300, 400);
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
                return;

            var bounds = Bounds;

            // Draw background
            if (_whitePixel != null)
            {
                spriteBatch.Draw(_whitePixel, bounds, _backgroundColor);
            }

            // Draw content
            if (_currentSong != null && _font != null)
            {
                DrawSongInfo(spriteBatch, bounds);
            }
            else if (_font != null)
            {
                DrawNoSongMessage(spriteBatch, bounds);
            }

            base.OnDraw(spriteBatch, deltaTime);
        }

        #endregion

        #region Private Methods

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

            spriteBatch.DrawString(_font, typeText, new Vector2(x, y), _titleColor);
            y += LINE_HEIGHT + SECTION_SPACING;

            // Draw title
            var title = _currentSong.DisplayTitle ?? "Unknown";
            if (title.Length > 30)
                title = title.Substring(0, 27) + "...";
            
            spriteBatch.DrawString(_font, title, new Vector2(x, y), _titleColor);
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

            y += SECTION_SPACING;
        }

        private void DrawDifficultyInfo(SpriteBatch spriteBatch, ref float x, ref float y, float maxWidth)
        {
            spriteBatch.DrawString(_font, "Difficulties:", new Vector2(x, y), _labelColor);
            y += LINE_HEIGHT;

            // Show all available difficulties
            for (int i = 0; i < 5; i++)
            {
                if (_currentSong.Scores?.Length > i && _currentSong.Scores[i] != null)
                {
                    var score = _currentSong.Scores[i];
                    var difficultyName = GetDifficultyName(i);
                    var level = GetDifficultyLevel(score, i);
                    var isSelected = i == _currentDifficulty;

                    var color = isSelected ? _difficultyColor : _valueColor;
                    var prefix = isSelected ? "► " : "  ";
                    var text = $"{prefix}{difficultyName}: {level}";

                    spriteBatch.DrawString(_smallFont ?? _font, text, new Vector2(x + INDENT, y), color);
                    y += LINE_HEIGHT * 0.8f;
                }
            }

            y += SECTION_SPACING;
        }

        private void DrawPerformanceStats(SpriteBatch spriteBatch, ref float x, ref float y, float maxWidth)
        {
            var score = GetCurrentScore();
            if (score == null)
                return;

            spriteBatch.DrawString(_font, "Performance:", new Vector2(x, y), _labelColor);
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
            var messageSize = _font.MeasureString(message);
            var messagePos = new Vector2(
                bounds.X + (bounds.Width - messageSize.X) / 2,
                bounds.Y + (bounds.Height - messageSize.Y) / 2
            );
            spriteBatch.DrawString(_font, message, messagePos, _valueColor);
        }

        private void DrawLabelValue(SpriteBatch spriteBatch, string label, string value, float x, float y)
        {
            var font = _smallFont ?? _font;
            spriteBatch.DrawString(font, label, new Vector2(x, y), _labelColor);
            
            var labelSize = font.MeasureString(label);
            spriteBatch.DrawString(font, value, new Vector2(x + labelSize.X + 5, y), _valueColor);
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

        #endregion
    }
}
