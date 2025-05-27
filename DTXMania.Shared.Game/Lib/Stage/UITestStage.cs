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
                // Return to title stage (previous stage)
                _game.StageManager?.ChangeStage(StageType.Title);
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

            // Create title label with enhanced effects
            var titleLabel = new UILabel("DTXMania UI Architecture - Enhanced Components")
            {
                Position = new Vector2(50, 20),
                Font = _defaultFont,
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Center,
                HasShadow = true,
                ShadowOffset = new Vector2(2, 2),
                ShadowColor = Color.Black,
                HasOutline = true,
                OutlineColor = Color.DarkBlue,
                OutlineThickness = 1
            };
            _mainContainer.AddChild(titleLabel);

            // Create instruction label
            var instructionLabel = new UILabel("Press ESC to return to title | Use arrow keys in list")
            {
                Position = new Vector2(50, 60),
                Font = _defaultFont,
                TextColor = Color.LightGray
            };
            _mainContainer.AddChild(instructionLabel);

            // Create font warning label
            var fontWarningLabel = new UILabel("NOTE: No font loaded - text will not display, but UI structure is functional")
            {
                Position = new Vector2(50, 80),
                Font = _defaultFont,
                TextColor = Color.Orange
            };
            _mainContainer.AddChild(fontWarningLabel);

            // Create enhanced test components
            CreateEnhancedButtons();
            CreateTestImages();
            CreateTestPanels();
            CreateTestList();
        }

        /// <summary>
        /// Create enhanced buttons to demonstrate new button functionality
        /// </summary>
        private void CreateEnhancedButtons()
        {
            if (_mainContainer == null)
                return;

            // Enhanced Button 1 - State-based styling
            var button1 = new UIButton("Enhanced Button")
            {
                Position = new Vector2(50, 100),
                Size = new Vector2(150, 50),
                Font = _defaultFont
            };

            // Configure state-based appearance
            button1.IdleAppearance.BackgroundColor = Color.DarkBlue;
            button1.IdleAppearance.TextColor = Color.White;
            button1.HoverAppearance.BackgroundColor = Color.Blue;
            button1.HoverAppearance.TextColor = Color.Yellow;
            button1.PressedAppearance.BackgroundColor = Color.Navy;
            button1.PressedAppearance.Offset = new Vector2(1, 1); // Pressed effect
            button1.DisabledAppearance.BackgroundColor = Color.Gray;
            button1.DisabledAppearance.TextColor = Color.DarkGray;

            if (_whitePixel != null)
            {
                button1.SetBackgroundTexture(ButtonState.Idle, _whitePixel);
                button1.SetBackgroundTexture(ButtonState.Hover, _whitePixel);
                button1.SetBackgroundTexture(ButtonState.Pressed, _whitePixel);
                button1.SetBackgroundTexture(ButtonState.Disabled, _whitePixel);
            }

            button1.ButtonClicked += (sender, e) =>
            {
                System.Diagnostics.Debug.WriteLine("Enhanced Button clicked!");
            };
            _mainContainer.AddChild(button1);

            // Button with Image
            var imageButton = new UIButton("Image Button")
            {
                Position = new Vector2(220, 100),
                Size = new Vector2(150, 50),
                Font = _defaultFont
            };

            // Add image component (would use actual texture in real implementation)
            imageButton.ImageComponent = new UIImage(_whitePixel)
            {
                TintColor = Color.Green,
                ScaleMode = ImageScaleMode.Uniform
            };

            imageButton.ButtonClicked += (sender, e) =>
            {
                System.Diagnostics.Debug.WriteLine("Image Button clicked!");
            };
            _mainContainer.AddChild(imageButton);
        }



        /// <summary>
        /// Create test images to demonstrate UIImage component
        /// </summary>
        private void CreateTestImages()
        {
            if (_mainContainer == null || _whitePixel == null)
                return;

            // Simple image
            var image1 = new UIImage(_whitePixel)
            {
                Position = new Vector2(400, 100),
                Size = new Vector2(64, 64),
                TintColor = Color.Red,
                ScaleMode = ImageScaleMode.Stretch
            };
            _mainContainer.AddChild(image1);

            // Scaled image with rotation
            var image2 = new UIImage(_whitePixel)
            {
                Position = new Vector2(480, 100),
                Size = new Vector2(64, 64),
                TintColor = Color.Green,
                Scale = new Vector2(1.5f, 1.5f),
                Rotation = 0.785f, // 45 degrees
                Origin = new Vector2(32, 32)
            };
            _mainContainer.AddChild(image2);
        }

        /// <summary>
        /// Create test panels to demonstrate UIPanel component
        /// </summary>
        private void CreateTestPanels()
        {
            if (_mainContainer == null || _whitePixel == null)
                return;

            // Panel with vertical layout
            var verticalPanel = new UIPanel
            {
                Position = new Vector2(50, 180),
                Size = new Vector2(200, 150),
                BackgroundColor = Color.DarkBlue,
                BackgroundTexture = _whitePixel,
                BorderColor = Color.Blue,
                BorderThickness = 2,
                BorderTexture = _whitePixel,
                LayoutMode = PanelLayoutMode.Vertical,
                Padding = new Vector2(10, 10),
                Spacing = 5
            };

            // Add items to vertical panel
            for (int i = 0; i < 3; i++)
            {
                var label = new UILabel($"Item {i + 1}")
                {
                    Font = _defaultFont,
                    TextColor = Color.White,
                    Size = new Vector2(180, 25)
                };
                verticalPanel.AddChild(label);
            }

            _mainContainer.AddChild(verticalPanel);

            // Panel with horizontal layout
            var horizontalPanel = new UIPanel
            {
                Position = new Vector2(270, 180),
                Size = new Vector2(300, 80),
                BackgroundColor = Color.DarkGreen,
                BackgroundTexture = _whitePixel,
                LayoutMode = PanelLayoutMode.Horizontal,
                Padding = new Vector2(5, 5),
                Spacing = 10
            };

            // Add buttons to horizontal panel
            for (int i = 0; i < 3; i++)
            {
                var button = new UIButton($"H{i + 1}")
                {
                    Size = new Vector2(60, 30),
                    Font = _defaultFont
                };
                button.IdleAppearance.BackgroundColor = Color.Orange;
                if (_whitePixel != null)
                    button.SetBackgroundTexture(ButtonState.Idle, _whitePixel);

                horizontalPanel.AddChild(button);
            }

            _mainContainer.AddChild(horizontalPanel);
        }

        /// <summary>
        /// Create test list to demonstrate UIList component
        /// </summary>
        private void CreateTestList()
        {
            if (_mainContainer == null || _whitePixel == null)
                return;

            var testList = new UIList
            {
                Position = new Vector2(600, 100),
                Size = new Vector2(200, 200),
                Font = _defaultFont,
                BackgroundColor = Color.DarkSlateGray,
                BackgroundTexture = _whitePixel,
                SelectedItemColor = Color.Blue,
                HoverItemColor = Color.LightBlue,
                TextColor = Color.White,
                SelectedTextColor = Color.Yellow,
                VisibleItemCount = 8,
                ItemHeight = 25
            };

            System.Diagnostics.Debug.WriteLine($"Creating UIList at {testList.Position} with size {testList.Size}");

            // Add items to the list
            testList.AddItem("Song 1", "song1.dtx");
            testList.AddItem("Song 2", "song2.dtx");
            testList.AddItem("Song 3", "song3.dtx");
            testList.AddItem("Song 4", "song4.dtx");
            testList.AddItem("Song 5", "song5.dtx");
            testList.AddItem("Song 6", "song6.dtx");
            testList.AddItem("Song 7", "song7.dtx");
            testList.AddItem("Song 8", "song8.dtx");
            testList.AddItem("Song 9", "song9.dtx");
            testList.AddItem("Song 10", "song10.dtx");

            // Handle selection events
            testList.SelectionChanged += (sender, e) =>
            {
                if (e.NewIndex >= 0)
                {
                    var item = testList.Items[e.NewIndex];
                    System.Diagnostics.Debug.WriteLine($"Selected: {item.Text} (Data: {item.Data})");
                }
            };

            testList.ItemActivated += (sender, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"Activated: {e.Item.Text}");
            };

            // Set initial selection
            testList.SelectedIndex = 0;

            _mainContainer.AddChild(testList);
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
