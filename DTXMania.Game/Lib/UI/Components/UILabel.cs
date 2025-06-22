using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DTX.UI.Components
{
    /// <summary>
    /// Enhanced text label UI component with DTXMania-style effects
    /// Supports shadow/outline effects common in DTXMania
    /// </summary>
    public class UILabel : UIElement
    {
        #region Private Fields

        private string _text;
        private SpriteFont? _font;
        private Color _textColor = Color.White;
        private TextAlignment _horizontalAlignment = TextAlignment.Left;
        private TextAlignment _verticalAlignment = TextAlignment.Top;

        // Shadow/Outline effects (DTXMania style)
        private bool _hasShadow = false;
        private Vector2 _shadowOffset = new Vector2(2, 2);
        private Color _shadowColor = Color.Black;
        private bool _hasOutline = false;
        private Color _outlineColor = Color.Black;
        private int _outlineThickness = 1;

        #endregion

        #region Constructor

        public UILabel(string text = "")
        {
            _text = text;
            // Auto-size based on text when font is available
        }

        #endregion

        #region Properties

        /// <summary>
        /// Text to display
        /// </summary>
        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value ?? string.Empty;
                    UpdateSize();
                }
            }
        }

        /// <summary>
        /// Font used for text rendering
        /// </summary>
        public SpriteFont? Font
        {
            get => _font;
            set
            {
                if (_font != value)
                {
                    _font = value;
                    UpdateSize();
                }
            }
        }

        /// <summary>
        /// Color of the text
        /// </summary>
        public Color TextColor
        {
            get => _textColor;
            set => _textColor = value;
        }

        /// <summary>
        /// Horizontal text alignment within the label bounds
        /// </summary>
        public TextAlignment HorizontalAlignment
        {
            get => _horizontalAlignment;
            set => _horizontalAlignment = value;
        }

        /// <summary>
        /// Vertical text alignment within the label bounds
        /// </summary>
        public TextAlignment VerticalAlignment
        {
            get => _verticalAlignment;
            set => _verticalAlignment = value;
        }

        /// <summary>
        /// Whether the text has a shadow effect
        /// </summary>
        public bool HasShadow
        {
            get => _hasShadow;
            set => _hasShadow = value;
        }

        /// <summary>
        /// Shadow offset from the main text
        /// </summary>
        public Vector2 ShadowOffset
        {
            get => _shadowOffset;
            set => _shadowOffset = value;
        }

        /// <summary>
        /// Color of the shadow
        /// </summary>
        public Color ShadowColor
        {
            get => _shadowColor;
            set => _shadowColor = value;
        }

        /// <summary>
        /// Whether the text has an outline effect
        /// </summary>
        public bool HasOutline
        {
            get => _hasOutline;
            set => _hasOutline = value;
        }

        /// <summary>
        /// Color of the outline
        /// </summary>
        public Color OutlineColor
        {
            get => _outlineColor;
            set => _outlineColor = value;
        }

        /// <summary>
        /// Thickness of the outline in pixels
        /// </summary>
        public int OutlineThickness
        {
            get => _outlineThickness;
            set => _outlineThickness = Math.Max(0, value);
        }

        #endregion

        #region Overridden Methods

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible || _font == null || string.IsNullOrEmpty(_text))
                return;

            var textSize = _font.MeasureString(_text);
            var bounds = Bounds;

            // Calculate text position based on alignment
            var textPosition = CalculateTextPosition(bounds, textSize);

            // Draw outline effect (DTXMania style)
            if (_hasOutline && _outlineThickness > 0)
            {
                DrawOutline(spriteBatch, textPosition);
            }

            // Draw shadow effect (DTXMania style)
            if (_hasShadow)
            {
                var shadowPosition = textPosition + _shadowOffset;
                spriteBatch.DrawString(_font, _text, shadowPosition, _shadowColor);
            }

            // Draw main text
            spriteBatch.DrawString(_font, _text, textPosition, _textColor);

            base.OnDraw(spriteBatch, deltaTime);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Calculate text position based on alignment
        /// </summary>
        /// <param name="bounds">Label bounds</param>
        /// <param name="textSize">Size of the text</param>
        /// <returns>Position to draw text</returns>
        private Vector2 CalculateTextPosition(Rectangle bounds, Vector2 textSize)
        {
            float x = bounds.X;
            float y = bounds.Y;

            // Horizontal alignment
            switch (_horizontalAlignment)
            {
                case TextAlignment.Center:
                    x += (bounds.Width - textSize.X) / 2;
                    break;
                case TextAlignment.Right:
                    x += bounds.Width - textSize.X;
                    break;
                case TextAlignment.Left:
                default:
                    // x is already set to bounds.X
                    break;
            }

            // Vertical alignment
            switch (_verticalAlignment)
            {
                case TextAlignment.Center:
                    y += (bounds.Height - textSize.Y) / 2;
                    break;
                case TextAlignment.Bottom:
                    y += bounds.Height - textSize.Y;
                    break;
                case TextAlignment.Top:
                default:
                    // y is already set to bounds.Y
                    break;
            }

            return new Vector2(x, y);
        }

        /// <summary>
        /// Draw outline effect around text (DTXMania style)
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="textPosition">Base text position</param>
        private void DrawOutline(SpriteBatch spriteBatch, Vector2 textPosition)
        {
            if (_font == null || string.IsNullOrEmpty(_text))
                return;

            // Draw outline by drawing text in 8 directions around the main position
            for (int dx = -_outlineThickness; dx <= _outlineThickness; dx++)
            {
                for (int dy = -_outlineThickness; dy <= _outlineThickness; dy++)
                {
                    if (dx == 0 && dy == 0)
                        continue; // Skip center position (main text)

                    var outlinePosition = textPosition + new Vector2(dx, dy);
                    spriteBatch.DrawString(_font, _text, outlinePosition, _outlineColor);
                }
            }
        }

        /// <summary>
        /// Update the size of the label based on text and font
        /// </summary>
        private void UpdateSize()
        {
            if (_font != null && !string.IsNullOrEmpty(_text))
            {
                var textSize = _font.MeasureString(_text);
                Size = textSize;
            }
        }

        #endregion
    }

    /// <summary>
    /// Text alignment enumeration
    /// </summary>
    public enum TextAlignment
    {
        Left,
        Center,
        Right,
        Top,
        Bottom
    }
}
