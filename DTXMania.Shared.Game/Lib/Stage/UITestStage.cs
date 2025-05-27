using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Stage;
using DTX.UI;
using DTX.UI.Components;
using DTXMania.Shared.Game;
using System;

namespace DTX.Stage
{
    /// <summary>
    /// Test stage to demonstrate the UI architecture
    /// Shows how UI components work together following DTXMania patterns
    /// </summary>
    public class UITestStage : IStage
    {
        #region Private Fields

        private readonly BaseGame _game;
        private UIManager _uiManager;
        private UIContainer _mainContainer;
        private SpriteBatch _spriteBatch;
        private SpriteFont _defaultFont;
        private Texture2D _whitePixel;

        #endregion

        #region Properties

        public StageType Type => StageType.Config; // Using Config as test stage

        #endregion

        #region Constructor

        public UITestStage(BaseGame game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
        }

        #endregion

        #region IStage Implementation

        public void Activate()
        {
            var graphicsDevice = _game.GraphicsDevice;
            
            // Create SpriteBatch
            _spriteBatch = new SpriteBatch(graphicsDevice);

            // Create basic resources
            CreateBasicResources(graphicsDevice);

            // Initialize UI system
            InitializeUI();

            System.Diagnostics.Debug.WriteLine("UITestStage activated");
        }

        public void Deactivate()
        {
            // Dispose UI system
            _uiManager?.Dispose();
            _uiManager = null;

            // Dispose resources
            _whitePixel?.Dispose();
            _spriteBatch?.Dispose();

            System.Diagnostics.Debug.WriteLine("UITestStage deactivated");
        }

        public void Update(double deltaTime)
        {
            // Update UI system
            _uiManager?.Update(deltaTime);

            // Handle stage-specific input (like returning to previous stage)
            if (_uiManager?.InputState.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Escape) == true)
            {
                // Return to startup stage
                _game.StageManager?.ChangeStage(StageType.Startup);
            }
        }

        public void Draw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            // Begin drawing
            _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);

            // Draw background
            DrawBackground();

            // Draw UI
            _uiManager?.Draw(_spriteBatch, deltaTime);

            // End drawing
            _spriteBatch.End();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Create basic rendering resources
        /// </summary>
        /// <param name="graphicsDevice">Graphics device</param>
        private void CreateBasicResources(GraphicsDevice graphicsDevice)
        {
            // Create a 1x1 white pixel texture for UI backgrounds
            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // In a real implementation, we would load fonts from content
            // For now, we'll use the default font if available
            // _defaultFont = _game.Content.Load<SpriteFont>("DefaultFont");
        }

        /// <summary>
        /// Initialize the UI system and create test UI elements
        /// </summary>
        private void InitializeUI()
        {
            // Create UI manager
            _uiManager = new UIManager();

            // Create main container
            _mainContainer = new UIContainer
            {
                Position = Vector2.Zero,
                Size = new Vector2(_game.GraphicsDevice.Viewport.Width, _game.GraphicsDevice.Viewport.Height)
            };

            // Create test UI elements
            CreateTestUIElements();

            // Add main container to UI manager
            _uiManager.AddRootContainer(_mainContainer);
        }

        /// <summary>
        /// Create test UI elements to demonstrate the system
        /// </summary>
        private void CreateTestUIElements()
        {
            if (_mainContainer == null)
                return;

            // Create title label
            var titleLabel = new UILabel("DTXMania UI Architecture Test")
            {
                Position = new Vector2(50, 50),
                Font = _defaultFont,
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Center
            };
            _mainContainer.AddChild(titleLabel);

            // Create instruction label
            var instructionLabel = new UILabel("Press ESC to return to startup")
            {
                Position = new Vector2(50, 100),
                Font = _defaultFont,
                TextColor = Color.LightGray
            };
            _mainContainer.AddChild(instructionLabel);

            // Create test buttons
            CreateTestButtons();

            // Create test container with child elements
            CreateTestContainer();
        }

        /// <summary>
        /// Create test buttons to demonstrate button functionality
        /// </summary>
        private void CreateTestButtons()
        {
            if (_mainContainer == null)
                return;

            // Button 1
            var button1 = new UIButton("Click Me!")
            {
                Position = new Vector2(100, 200),
                Size = new Vector2(150, 50),
                Font = _defaultFont,
                BackgroundColor = Color.DarkBlue,
                HoverColor = Color.Blue,
                PressedColor = Color.Navy
            };
            button1.SetBackgroundTexture(_whitePixel);
            button1.ButtonClicked += (sender, e) => 
            {
                System.Diagnostics.Debug.WriteLine("Button 1 clicked!");
            };
            _mainContainer.AddChild(button1);

            // Button 2
            var button2 = new UIButton("Another Button")
            {
                Position = new Vector2(300, 200),
                Size = new Vector2(150, 50),
                Font = _defaultFont,
                BackgroundColor = Color.DarkGreen,
                HoverColor = Color.Green,
                PressedColor = Color.DarkOliveGreen
            };
            button2.SetBackgroundTexture(_whitePixel);
            button2.ButtonClicked += (sender, e) => 
            {
                System.Diagnostics.Debug.WriteLine("Button 2 clicked!");
            };
            _mainContainer.AddChild(button2);
        }

        /// <summary>
        /// Create a test container with child elements to demonstrate nesting
        /// </summary>
        private void CreateTestContainer()
        {
            if (_mainContainer == null)
                return;

            // Create a sub-container
            var subContainer = new UIContainer
            {
                Position = new Vector2(100, 300),
                Size = new Vector2(400, 200)
            };

            // Add label to sub-container
            var containerLabel = new UILabel("Sub-Container")
            {
                Position = new Vector2(10, 10),
                Font = _defaultFont,
                TextColor = Color.Yellow
            };
            subContainer.AddChild(containerLabel);

            // Add buttons to sub-container
            for (int i = 0; i < 3; i++)
            {
                var button = new UIButton($"Sub Button {i + 1}")
                {
                    Position = new Vector2(10 + i * 120, 50),
                    Size = new Vector2(100, 40),
                    Font = _defaultFont,
                    BackgroundColor = Color.DarkRed,
                    HoverColor = Color.Red,
                    PressedColor = Color.Maroon
                };
                button.SetBackgroundTexture(_whitePixel);
                
                int buttonIndex = i; // Capture for closure
                button.ButtonClicked += (sender, e) => 
                {
                    System.Diagnostics.Debug.WriteLine($"Sub Button {buttonIndex + 1} clicked!");
                };
                
                subContainer.AddChild(button);
            }

            _mainContainer.AddChild(subContainer);
        }

        /// <summary>
        /// Draw the background
        /// </summary>
        private void DrawBackground()
        {
            if (_spriteBatch == null || _whitePixel == null)
                return;

            var viewport = _game.GraphicsDevice.Viewport;
            _spriteBatch.Draw(_whitePixel, 
                new Rectangle(0, 0, viewport.Width, viewport.Height), 
                Color.DarkSlateGray);
        }

        #endregion
    }
}
