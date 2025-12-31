#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.UI.Components
{
    /// <summary>
    /// Enhanced button UI component with full state management
    /// Supports Idle, Hover, Pressed, and Disabled states with text and/or image content
    /// </summary>
    public class UIButton : UIElement
    {
        #region Private Fields

        private string _text;
        private UIImage? _imageComponent;
        private readonly IResourceManager _resourceManager;

        // State-based appearance
        private ButtonStateAppearance _idleAppearance;
        private ButtonStateAppearance _hoverAppearance;
        private ButtonStateAppearance _pressedAppearance;
        private ButtonStateAppearance _disabledAppearance;

        // Current state
        private ButtonState _currentState = ButtonState.Idle;
        private bool _isHovered = false;
        private bool _isPressed = false;

        #endregion

        #region Constructor

        public UIButton(IResourceManager resourceManager, string text = "Button")
        {
            _resourceManager = resourceManager;
            _text = text;
            Size = new Vector2(120, 40); // Default size

            // Initialize default appearances
            _idleAppearance = new ButtonStateAppearance
            {
                BackgroundColor = Color.Gray,
                TextColor = Color.White,
                BorderColor = Color.DarkGray,
                BorderThickness = 1
            };

            _hoverAppearance = new ButtonStateAppearance
            {
                BackgroundColor = Color.LightGray,
                TextColor = Color.White,
                BorderColor = Color.Gray,
                BorderThickness = 1
            };

            _pressedAppearance = new ButtonStateAppearance
            {
                BackgroundColor = Color.DarkGray,
                TextColor = Color.White,
                BorderColor = Color.Black,
                BorderThickness = 2
            };

            _disabledAppearance = new ButtonStateAppearance
            {
                BackgroundColor = Color.DimGray,
                TextColor = Color.DarkGray,
                BorderColor = Color.Gray,
                BorderThickness = 1
            };
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
        /// Font used for button text. May be null if not set.
        /// </summary>
        public SpriteFont? Font { get; set; }

        /// <summary>
        /// Image component for the button (optional). May be null.
        /// </summary>
        public UIImage? ImageComponent
        {
            get => _imageComponent;
            set
            {
                if (_imageComponent != value)
                {
                    _imageComponent = value;
                    if (_imageComponent != null)
                        _imageComponent.Parent = this;
                }
            }
        }

        /// <summary>
        /// Current button state
        /// </summary>
        public ButtonState CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    _currentState = value;
                    OnStateChanged();
                }
            }
        }

        /// <summary>
        /// Appearance settings for idle state
        /// </summary>
        public ButtonStateAppearance IdleAppearance
        {
            get => _idleAppearance;
            set => _idleAppearance = value ?? new ButtonStateAppearance();
        }

        /// <summary>
        /// Appearance settings for hover state
        /// </summary>
        public ButtonStateAppearance HoverAppearance
        {
            get => _hoverAppearance;
            set => _hoverAppearance = value ?? new ButtonStateAppearance();
        }

        /// <summary>
        /// Appearance settings for pressed state
        /// </summary>
        public ButtonStateAppearance PressedAppearance
        {
            get => _pressedAppearance;
            set => _pressedAppearance = value ?? new ButtonStateAppearance();
        }

        /// <summary>
        /// Appearance settings for disabled state
        /// </summary>
        public ButtonStateAppearance DisabledAppearance
        {
            get => _disabledAppearance;
            set => _disabledAppearance = value ?? new ButtonStateAppearance();
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
        /// Fired when the button is clicked. May be null if no handler is attached.
        /// </summary>
        public event EventHandler? ButtonClicked;

        #endregion

        #region Overridden Methods

        protected override void OnCreateResources()
        {
            base.OnCreateResources();

            // Load a default font if not already set
            if (Font == null)
            {
                Font = _resourceManager.LoadFont("DefaultFont", 20).SpriteFont; // Assuming "DefaultFont" is a registered font
            }
        }

        protected override void OnUpdate(double deltaTime)
        {
            base.OnUpdate(deltaTime);

            // Update current state based on enabled status
            if (!Enabled)
            {
                CurrentState = ButtonState.Disabled;
            }
            else if (_isPressed)
            {
                CurrentState = ButtonState.Pressed;
            }
            else if (_isHovered)
            {
                CurrentState = ButtonState.Hover;
            }
            else
            {
                CurrentState = ButtonState.Idle;
            }

            // Update image component if present
            _imageComponent?.Update(deltaTime);
        }

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible)
                return;

            var bounds = Bounds;
            var appearance = GetCurrentAppearance();
            var drawBounds = new Rectangle(
                bounds.X + (int)appearance.Offset.X,
                bounds.Y + (int)appearance.Offset.Y,
                bounds.Width,
                bounds.Height
            );

            // Draw background
            DrawBackground(spriteBatch, drawBounds, appearance);

            // Draw border
            DrawBorder(spriteBatch, drawBounds, appearance);

            // Draw image if present
            if (_imageComponent != null)
            {
                _imageComponent.Position = new Vector2(drawBounds.X, drawBounds.Y);
                _imageComponent.Size = new Vector2(drawBounds.Width, drawBounds.Height);
                _imageComponent.Draw(spriteBatch, deltaTime);
            }

            // Draw text
            DrawText(spriteBatch, drawBounds, appearance);

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

        #region Private Methods

        /// <summary>
        /// Get the current appearance based on button state
        /// </summary>
        /// <returns>Current appearance settings</returns>
        private ButtonStateAppearance GetCurrentAppearance()
        {
            return _currentState switch
            {
                ButtonState.Hover => _hoverAppearance,
                ButtonState.Pressed => _pressedAppearance,
                ButtonState.Disabled => _disabledAppearance,
                _ => _idleAppearance
            };
        }

        /// <summary>
        /// Draw the button background
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="bounds">Button bounds</param>
        /// <param name="appearance">Current appearance</param>
        private void DrawBackground(SpriteBatch spriteBatch, Rectangle bounds, ButtonStateAppearance appearance)
        {
            if (appearance.BackgroundTexture != null)
            {
                spriteBatch.Draw(appearance.BackgroundTexture, bounds,
                    appearance.BackgroundSourceRectangle, appearance.BackgroundColor);
            }
            // Note: For solid color backgrounds, we would need a white pixel texture
            // This would typically be provided by the graphics manager
        }

        /// <summary>
        /// Draw the button border
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="bounds">Button bounds</param>
        /// <param name="appearance">Current appearance</param>
        private void DrawBorder(SpriteBatch spriteBatch, Rectangle bounds, ButtonStateAppearance appearance)
        {
            if (appearance.BorderThickness <= 0)
                return;

            // Note: Border drawing would require a white pixel texture or line drawing utilities
            // This would typically be provided by the graphics manager
        }

        /// <summary>
        /// Draw the button text
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="bounds">Button bounds</param>
        /// <param name="appearance">Current appearance</param>
        private void DrawText(SpriteBatch spriteBatch, Rectangle bounds, ButtonStateAppearance appearance)
        {
            if (Font == null || string.IsNullOrEmpty(_text))
                return;

            var textSize = Font.MeasureString(_text);
            var textPosition = new Vector2(
                bounds.X + (bounds.Width - textSize.X) / 2,
                bounds.Y + (bounds.Height - textSize.Y) / 2
            );

            spriteBatch.DrawString(Font, _text, textPosition, appearance.TextColor);
        }

        /// <summary>
        /// Called when button state changes
        /// </summary>
        private void OnStateChanged()
        {
            // Override in derived classes for custom state change handling
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Set the background texture for a specific state
        /// </summary>
        /// <param name="state">Button state</param>
        /// <param name="texture">Background texture</param>
        /// <param name="sourceRectangle">Source rectangle (optional)</param>
        public void SetBackgroundTexture(ButtonState state, Texture2D texture, Rectangle? sourceRectangle = null)
        {
            var appearance = state switch
            {
                ButtonState.Hover => _hoverAppearance,
                ButtonState.Pressed => _pressedAppearance,
                ButtonState.Disabled => _disabledAppearance,
                _ => _idleAppearance
            };

            appearance.BackgroundTexture = texture;
            appearance.BackgroundSourceRectangle = sourceRectangle;
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

    /// <summary>
    /// Button state enumeration
    /// </summary>
    public enum ButtonState
    {
        /// <summary>
        /// Button is in normal idle state
        /// </summary>
        Idle,

        /// <summary>
        /// Mouse is hovering over the button
        /// </summary>
        Hover,

        /// <summary>
        /// Button is being pressed
        /// </summary>
        Pressed,

        /// <summary>
        /// Button is disabled and cannot be interacted with
        /// </summary>
        Disabled
    }

    /// <summary>
    /// Appearance settings for a button state
    /// </summary>
    public class ButtonStateAppearance
    {
        /// <summary>
        /// Background color for this state
        /// </summary>
        public Color BackgroundColor { get; set; } = Color.Gray;

        /// <summary>
        /// Text color for this state
        /// </summary>
        public Color TextColor { get; set; } = Color.White;

        /// <summary>
        /// Border color for this state
        /// </summary>
        public Color BorderColor { get; set; } = Color.DarkGray;

        /// <summary>
        /// Border thickness in pixels
        /// </summary>
        public int BorderThickness { get; set; } = 1;

        /// <summary>
        /// Background texture for this state (optional). May be null if no texture is used.
        /// </summary>
        public Texture2D? BackgroundTexture { get; set; }

        /// <summary>
        /// Source rectangle for background texture (null for full texture). 
        /// Use HasValue to check if set.
        /// </summary>
        public Rectangle? BackgroundSourceRectangle { get; set; }

        /// <summary>
        /// Scale factor for this state
        /// </summary>
        public Vector2 Scale { get; set; } = Vector2.One;

        /// <summary>
        /// Offset for this state (useful for pressed effect)
        /// </summary>
        public Vector2 Offset { get; set; } = Vector2.Zero;
    }
}
