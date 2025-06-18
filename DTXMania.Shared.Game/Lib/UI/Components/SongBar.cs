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

        // Visual state
        private Color _backgroundColor;
        private Color _textColor;
        private DefaultGraphicsGenerator _graphicsGenerator;

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
            Size = new Vector2(600, DTXManiaVisualTheme.Layout.SongBarHeight);
            UpdateVisualState();
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
            // Update colors using DTXManiaNX theme
            var baseColor = DTXManiaVisualTheme.SongSelection.SongBarBackground;
            _backgroundColor = DTXManiaVisualTheme.ApplySelectionHighlight(baseColor, _isSelected, _isCenter);

            // Update text color based on selection and node type
            if (_isSelected)
            {
                _textColor = DTXManiaVisualTheme.SongSelection.SongSelectedText;
            }
            else
            {
                _textColor = DTXManiaVisualTheme.GetNodeTypeColor(_songNode?.Type ?? NodeType.Score);
            }
        }

        /// <summary>
        /// Initialize graphics generator for fallback rendering
        /// </summary>
        public void InitializeGraphicsGenerator(GraphicsDevice graphicsDevice)
        {
            _graphicsGenerator?.Dispose();
            _graphicsGenerator = new DefaultGraphicsGenerator(graphicsDevice);
        }

        /// <summary>
        /// Set draw phase state to prevent texture generation during draw
        /// CRITICAL: Must be called before Draw() to prevent render target issues
        /// </summary>
        public void SetDrawPhase(bool isInDrawPhase)
        {
            if (_graphicsGenerator != null)
            {
                _graphicsGenerator.IsInDrawPhase = isInDrawPhase;
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
            // Try to use generated background texture first
            if (_graphicsGenerator != null)
            {
                var backgroundTexture = _graphicsGenerator.GenerateSongBarBackground(bounds.Width, bounds.Height, _isSelected, _isCenter);
                if (backgroundTexture != null)
                {
                    backgroundTexture.Draw(spriteBatch, new Vector2(bounds.X, bounds.Y));
                    return;
                }
            }

            // Fallback to simple rectangle rendering
            if (_whitePixel != null)
            {
                spriteBatch.Draw(_whitePixel, bounds, _backgroundColor);

                // Draw selection border with DTXManiaNX styling
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
            if (_songNode?.Type != NodeType.Score)
                return;

            // Use existing clear lamp texture if available
            if (_clearLampTexture != null)
            {
                var lampDestRect = new Rectangle(bounds.X, bounds.Y, DTXManiaVisualTheme.Layout.ClearLampWidth, bounds.Height);
                _clearLampTexture.Draw(spriteBatch, lampDestRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
                return;
            }

            // Generate default clear lamp if graphics generator is available
            if (_graphicsGenerator != null)
            {
                var hasCleared = _songNode.Scores?[_currentDifficulty]?.BestScore > 0;
                var lampTexture = _graphicsGenerator.GenerateClearLamp(_currentDifficulty, hasCleared);
                if (lampTexture != null)
                {
                    var lampDestRect = new Rectangle(bounds.X, bounds.Y, DTXManiaVisualTheme.Layout.ClearLampWidth, bounds.Height);
                    lampTexture.Draw(spriteBatch, new Vector2(lampDestRect.X, lampDestRect.Y));
                }
            }
        }

        private void DrawPreviewImage(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_previewImageTexture != null)
            {
                var imageX = bounds.X + DTXManiaVisualTheme.Layout.ClearLampWidth + 5;
                var imageY = bounds.Y + (bounds.Height - DTXManiaVisualTheme.Layout.PreviewImageSize) / 2;
                var imageDestRect = new Rectangle(imageX, imageY, DTXManiaVisualTheme.Layout.PreviewImageSize, DTXManiaVisualTheme.Layout.PreviewImageSize);

                _previewImageTexture.Draw(spriteBatch, imageDestRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            }
        }

        private void DrawTitle(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_titleTexture != null)
            {
                var textX = bounds.X + DTXManiaVisualTheme.Layout.ClearLampWidth + (_previewImageTexture != null ? DTXManiaVisualTheme.Layout.PreviewImageSize + 10 : 5);
                var textY = bounds.Y + (bounds.Height - _titleTexture.Height) / 2;
                var textPosition = new Vector2(textX, textY);

                _titleTexture.Draw(spriteBatch, textPosition);
            }
            else if (_font != null)
            {
                // Fallback to direct text rendering with DTXManiaNX-style shadow
                var displayText = GetDisplayText();
                var textX = bounds.X + DTXManiaVisualTheme.Layout.ClearLampWidth + (_previewImageTexture != null ? DTXManiaVisualTheme.Layout.PreviewImageSize + 10 : 5);
                var textY = bounds.Y + (bounds.Height - _font.LineSpacing) / 2;
                var textPosition = new Vector2(textX, textY);

                // Draw shadow first
                var shadowPosition = textPosition + DTXManiaVisualTheme.FontEffects.SongTextShadowOffset;
                spriteBatch.DrawString(_font, displayText, shadowPosition, DTXManiaVisualTheme.FontEffects.SongTextShadowColor);

                // Draw main text
                spriteBatch.DrawString(_font, displayText, textPosition, _textColor);
            }
        }

        private void DrawNodeTypeIndicator(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_whitePixel == null)
                return;

            // Draw a small indicator on the right side for node type using DTXManiaNX colors
            var indicatorWidth = 4;
            var indicatorBounds = new Rectangle(bounds.Right - indicatorWidth, bounds.Y, indicatorWidth, bounds.Height);
            var indicatorColor = DTXManiaVisualTheme.GetNodeTypeColor(_songNode?.Type ?? NodeType.Score);

            spriteBatch.Draw(_whitePixel, indicatorBounds, indicatorColor);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _graphicsGenerator?.Dispose();
                _graphicsGenerator = null;
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
