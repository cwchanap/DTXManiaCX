using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTX.UI.Components
{
    /// <summary>
    /// Simple text label UI component
    /// Demonstrates basic text rendering in the UI system
    /// </summary>
    public class UILabel : UIElement
    {
        #region Private Fields

        private string _text;
        private SpriteFont? _font;
        private Color _textColor = Color.White;
        private TextAlignment _horizontalAlignment = TextAlignment.Left;
        private TextAlignment _verticalAlignment = TextAlignment.Top;

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

        #endregion

        #region Overridden Methods

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible || _font == null || string.IsNullOrEmpty(_text))
                return;

            var textSize = _font.MeasureString(_text);
            var bounds = Bounds;

            // Calculate text position based on alignment
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

            var textPosition = new Vector2(x, y);
            spriteBatch.DrawString(_font, _text, textPosition, _textColor);

            base.OnDraw(spriteBatch, deltaTime);
        }

        #endregion

        #region Private Methods

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
