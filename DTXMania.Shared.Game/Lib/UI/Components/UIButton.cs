using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DTX.UI.Components
{
    /// <summary>
    /// Basic button UI component
    /// Demonstrates the UI architecture with a simple interactive element
    /// </summary>
    public class UIButton : UIElement
    {
        #region Private Fields

        private string _text;
        private SpriteFont? _font;
        private Texture2D? _backgroundTexture;
        private Color _backgroundColor = Color.Gray;
        private Color _textColor = Color.White;
        private Color _hoverColor = Color.LightGray;
        private Color _pressedColor = Color.DarkGray;
        private bool _isHovered = false;
        private bool _isPressed = false;

        #endregion

        #region Constructor

        public UIButton(string text = "Button")
        {
            _text = text;
            Size = new Vector2(120, 40); // Default size
        }

        #endregion

        #region Properties

        /// <summary>
        /// Text displayed on the button
        /// </summary>
        public string Text
        {
            get => _text;
            set => _text = value ?? string.Empty;
        }

        /// <summary>
        /// Font used for button text
        /// </summary>
        public SpriteFont? Font
        {
            get => _font;
            set => _font = value;
        }

        /// <summary>
        /// Background color when button is in normal state
        /// </summary>
        public Color BackgroundColor
        {
            get => _backgroundColor;
            set => _backgroundColor = value;
        }

        /// <summary>
        /// Text color
        /// </summary>
        public Color TextColor
        {
            get => _textColor;
            set => _textColor = value;
        }

        /// <summary>
        /// Background color when button is hovered
        /// </summary>
        public Color HoverColor
        {
            get => _hoverColor;
            set => _hoverColor = value;
        }

        /// <summary>
        /// Background color when button is pressed
        /// </summary>
        public Color PressedColor
        {
            get => _pressedColor;
            set => _pressedColor = value;
        }

        /// <summary>
        /// Whether the button is currently being hovered
        /// </summary>
        public bool IsHovered => _isHovered;

        /// <summary>
        /// Whether the button is currently being pressed
        /// </summary>
        public bool IsPressed => _isPressed;

        #endregion

        #region Events

        /// <summary>
        /// Fired when the button is clicked
        /// </summary>
        public event EventHandler? ButtonClicked;

        #endregion

        #region Overridden Methods

        protected override void OnCreateResources()
        {
            base.OnCreateResources();

            // Create a simple white pixel texture for background if none provided
            // In a real implementation, this would be loaded from the graphics manager
            // For now, we'll create it when we have access to GraphicsDevice
        }

        protected override void OnUpdate(double deltaTime)
        {
            base.OnUpdate(deltaTime);

            // Update button state based on interaction
            // This would typically be handled in input processing
        }

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible)
                return;

            var bounds = Bounds;

            // Determine current background color based on state
            Color currentBackgroundColor = _backgroundColor;
            if (_isPressed)
                currentBackgroundColor = _pressedColor;
            else if (_isHovered)
                currentBackgroundColor = _hoverColor;

            // Draw background
            if (_backgroundTexture != null)
            {
                spriteBatch.Draw(_backgroundTexture, bounds, currentBackgroundColor);
            }
            else
            {
                // If no background texture, we'll need to create a simple rectangle
                // This would typically use a 1x1 white pixel texture
                // For now, we'll skip the background drawing
            }

            // Draw text
            if (_font != null && !string.IsNullOrEmpty(_text))
            {
                var textSize = _font.MeasureString(_text);
                var textPosition = new Vector2(
                    bounds.X + (bounds.Width - textSize.X) / 2,
                    bounds.Y + (bounds.Height - textSize.Y) / 2
                );

                spriteBatch.DrawString(_font, _text, textPosition, _textColor);
            }

            base.OnDraw(spriteBatch, deltaTime);
        }

        protected override bool OnHandleInput(IInputState inputState)
        {
            if (!Enabled)
                return false;

            var mousePos = inputState.MousePosition;
            var wasHovered = _isHovered;
            var wasPressed = _isPressed;

            // Update hover state
            _isHovered = HitTest(mousePos);

            // Update pressed state
            if (_isHovered && inputState.IsMouseButtonDown(MouseButton.Left))
            {
                _isPressed = true;
            }
            else
            {
                _isPressed = false;
            }

            // Check for click (mouse released while over button)
            if (_isHovered && inputState.IsMouseButtonReleased(MouseButton.Left))
            {
                ButtonClicked?.Invoke(this, EventArgs.Empty);
                return true;
            }

            // Return true if we're interacting with the button
            return _isHovered || wasHovered || _isPressed || wasPressed;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Set the background texture for the button
        /// </summary>
        /// <param name="texture">Background texture</param>
        public void SetBackgroundTexture(Texture2D texture)
        {
            _backgroundTexture = texture;
        }

        /// <summary>
        /// Programmatically trigger a button click
        /// </summary>
        public void Click()
        {
            if (Enabled)
            {
                ButtonClicked?.Invoke(this, EventArgs.Empty);
            }
        }

        #endregion
    }
}
