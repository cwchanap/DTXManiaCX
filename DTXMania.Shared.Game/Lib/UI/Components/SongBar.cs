using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.Song;
using DTX.Resources;
using System;

namespace DTX.UI.Components
{
    /// <summary>
    /// Individual song bar component for DTXManiaNX-style song list display
    /// Represents a single song item with textures, effects, and visual indicators
    /// </summary>
    public class SongBar : UIElement
    {
        #region Constants

        private const int BAR_HEIGHT = 30;
        private const int PREVIEW_IMAGE_SIZE = 24;
        private const int CLEAR_LAMP_WIDTH = 8;
        private const int TEXT_PADDING = 10;

        #endregion

        #region Fields

        private SongListNode _songNode;
        private int _currentDifficulty;
        private bool _isSelected;
        private bool _isCenter;

        // Visual resources
        private ITexture _titleTexture;
        private ITexture _previewImageTexture;
        private ITexture _clearLampTexture;
        private SpriteFont _font;
        private Texture2D _whitePixel;

        // Colors for different node types
        private Color _backgroundColor = Color.Black * 0.7f;
        private Color _selectedBackgroundColor = Color.Blue * 0.8f;
        private Color _centerBackgroundColor = Color.Yellow * 0.3f;
        private Color _textColor = Color.White;
        private Color _selectedTextColor = Color.Yellow;

        // Node type specific colors
        private static readonly Color BoxColor = Color.Cyan;
        private static readonly Color BackBoxColor = Color.Orange;
        private static readonly Color RandomColor = Color.Magenta;
        private static readonly Color ScoreColor = Color.White;

        #endregion

        #region Properties

        /// <summary>
        /// The song node this bar represents
        /// </summary>
        public SongListNode SongNode
        {
            get => _songNode;
            set
            {
                if (_songNode != value)
                {
                    _songNode = value;
                    InvalidateTextures();
                }
            }
        }

        /// <summary>
        /// Current difficulty level (0-4)
        /// </summary>
        public int CurrentDifficulty
        {
            get => _currentDifficulty;
            set
            {
                if (_currentDifficulty != value)
                {
                    _currentDifficulty = Math.Max(0, Math.Min(4, value));
                    InvalidateClearLamp();
                }
            }
        }

        /// <summary>
        /// Whether this bar is currently selected
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    UpdateVisualState();
                }
            }
        }

        /// <summary>
        /// Whether this bar is in the center position (index 6)
        /// </summary>
        public bool IsCenter
        {
            get => _isCenter;
            set
            {
                if (_isCenter != value)
                {
                    _isCenter = value;
                    UpdateVisualState();
                }
            }
        }

        /// <summary>
        /// Font for text rendering
        /// </summary>
        public SpriteFont Font
        {
            get => _font;
            set
            {
                if (_font != value)
                {
                    _font = value;
                    InvalidateTextures();
                }
            }
        }

        /// <summary>
        /// White pixel texture for drawing backgrounds
        /// </summary>
        public Texture2D WhitePixel
        {
            get => _whitePixel;
            set => _whitePixel = value;
        }

        #endregion

        #region Constructor

        public SongBar()
        {
            Size = new Vector2(600, BAR_HEIGHT);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Set textures from external renderer
        /// </summary>
        public void SetTextures(ITexture titleTexture, ITexture previewImageTexture, ITexture clearLampTexture)
        {
            _titleTexture = titleTexture;
            _previewImageTexture = previewImageTexture;
            _clearLampTexture = clearLampTexture;
        }

        /// <summary>
        /// Update visual state based on selection and center position
        /// </summary>
        public void UpdateVisualState()
        {
            // Update colors based on state
            if (_isSelected && _isCenter)
            {
                _backgroundColor = _centerBackgroundColor;
                _textColor = _selectedTextColor;
            }
            else if (_isSelected)
            {
                _backgroundColor = _selectedBackgroundColor;
                _textColor = _selectedTextColor;
            }
            else
            {
                _backgroundColor = Color.Black * 0.7f;
                _textColor = GetNodeTypeColor();
            }
        }

        #endregion

        #region Protected Methods

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible || _songNode == null)
                return;

            var bounds = Bounds;

            // Draw background
            DrawBackground(spriteBatch, bounds);

            // Draw clear lamp (left side)
            DrawClearLamp(spriteBatch, bounds);

            // Draw preview image (if available)
            DrawPreviewImage(spriteBatch, bounds);

            // Draw song title
            DrawTitle(spriteBatch, bounds);

            // Draw node type indicator
            DrawNodeTypeIndicator(spriteBatch, bounds);

            base.OnDraw(spriteBatch, deltaTime);
        }

        #endregion

        #region Private Methods

        private void DrawBackground(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_whitePixel != null)
            {
                spriteBatch.Draw(_whitePixel, bounds, _backgroundColor);

                // Draw selection border
                if (_isSelected)
                {
                    var borderColor = _isCenter ? Color.Yellow : Color.White;
                    var borderThickness = _isCenter ? 2 : 1;

                    // Top border
                    spriteBatch.Draw(_whitePixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, borderThickness), borderColor);
                    // Bottom border
                    spriteBatch.Draw(_whitePixel, new Rectangle(bounds.X, bounds.Bottom - borderThickness, bounds.Width, borderThickness), borderColor);
                }
            }
        }

        private void DrawClearLamp(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_clearLampTexture != null && _songNode.Type == NodeType.Score)
            {
                var lampPosition = new Vector2(bounds.X, bounds.Y);
                var lampDestRect = new Rectangle(bounds.X, bounds.Y, CLEAR_LAMP_WIDTH, bounds.Height);

                _clearLampTexture.Draw(spriteBatch, lampDestRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            }
        }

        private void DrawPreviewImage(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_previewImageTexture != null)
            {
                var imageX = bounds.X + CLEAR_LAMP_WIDTH + 5;
                var imageY = bounds.Y + (bounds.Height - PREVIEW_IMAGE_SIZE) / 2;
                var imageDestRect = new Rectangle(imageX, imageY, PREVIEW_IMAGE_SIZE, PREVIEW_IMAGE_SIZE);

                _previewImageTexture.Draw(spriteBatch, imageDestRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            }
        }

        private void DrawTitle(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_titleTexture != null)
            {
                var textX = bounds.X + CLEAR_LAMP_WIDTH + (_previewImageTexture != null ? PREVIEW_IMAGE_SIZE + 10 : 5);
                var textY = bounds.Y + (bounds.Height - _titleTexture.Height) / 2;
                var textPosition = new Vector2(textX, textY);

                _titleTexture.Draw(spriteBatch, textPosition);
            }
            else if (_font != null)
            {
                // Fallback to direct text rendering
                var displayText = GetDisplayText();
                var textX = bounds.X + CLEAR_LAMP_WIDTH + (_previewImageTexture != null ? PREVIEW_IMAGE_SIZE + 10 : 5);
                var textY = bounds.Y + (bounds.Height - _font.LineSpacing) / 2;

                spriteBatch.DrawString(_font, displayText, new Vector2(textX, textY), _textColor);
            }
        }

        private void DrawNodeTypeIndicator(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_whitePixel == null)
                return;

            // Draw a small indicator on the right side for node type
            var indicatorWidth = 4;
            var indicatorBounds = new Rectangle(bounds.Right - indicatorWidth, bounds.Y, indicatorWidth, bounds.Height);
            var indicatorColor = GetNodeTypeColor();

            spriteBatch.Draw(_whitePixel, indicatorBounds, indicatorColor);
        }

        private Color GetNodeTypeColor()
        {
            return _songNode?.Type switch
            {
                NodeType.Box => BoxColor,
                NodeType.BackBox => BackBoxColor,
                NodeType.Random => RandomColor,
                NodeType.Score => ScoreColor,
                _ => ScoreColor
            };
        }

        private string GetDisplayText()
        {
            if (_songNode == null)
                return "Unknown";

            return _songNode.Type switch
            {
                NodeType.BackBox => ".. (Back)",
                NodeType.Box => $"[{_songNode.DisplayTitle}]",
                NodeType.Random => "*** RANDOM SELECT ***",
                NodeType.Score => _songNode.DisplayTitle ?? "Unknown Song",
                _ => _songNode.DisplayTitle ?? "Unknown"
            };
        }

        private void InvalidateTextures()
        {
            _titleTexture = null;
            _previewImageTexture = null;
            InvalidateClearLamp();
        }

        private void InvalidateClearLamp()
        {
            _clearLampTexture = null;
        }

        #endregion
    }
}
