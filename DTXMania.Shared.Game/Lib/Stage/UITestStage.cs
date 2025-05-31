using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Stage;
using DTX.UI;
using DTX.UI.Components;
using DTXMania.Shared.Game;
using DTX.Resources;
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
        private bool _disposed = false;

        // Font system for testing
        private DTX.Resources.IResourceManager _resourceManager;
        private DTX.Resources.IFont _testFont;
        private DTX.Resources.IFont _titleFont;
        private DTX.Resources.IFont _japaneseFont;

        #endregion

        #region Properties

        public StageType Type => StageType.UITest;

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
            try
            {
                System.Diagnostics.Debug.WriteLine("Activating UITest Stage");
                Console.WriteLine("Activating UITest Stage");

                var graphicsDevice = _game.GraphicsDevice;
                System.Diagnostics.Debug.WriteLine($"Graphics device: {graphicsDevice}");
                Console.WriteLine($"Graphics device: {graphicsDevice}");

                // Create SpriteBatch
                _spriteBatch = new SpriteBatch(graphicsDevice);
                System.Diagnostics.Debug.WriteLine("SpriteBatch created");
                Console.WriteLine("SpriteBatch created");

                // Initialize ResourceManager and load fonts
                InitializeResourceManager(graphicsDevice);

                // Create basic resources
                CreateBasicResources(graphicsDevice);

                // Initialize UI system
                InitializeUI();

                System.Diagnostics.Debug.WriteLine("UITestStage activated successfully");
                Console.WriteLine("UITestStage activated successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in UITestStage.Activate: {ex}");
                Console.WriteLine($"Error in UITestStage.Activate: {ex}");
                throw;
            }
        }

        public void Deactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating UITest Stage");

            // Deactivate UI system but don't dispose (handled in Dispose())
            if (_uiManager != null && _uiManager.RootContainers.Count > 0)
            {
                foreach (var container in _uiManager.RootContainers)
                {
                    if (container.IsActive)
                        container.Deactivate();
                }
            }
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

            try
            {
                // Begin drawing
                _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);

                // Draw background
                DrawBackground();

                // Draw font test text
                DrawFontTests();

                // Draw UI
                _uiManager?.Draw(_spriteBatch, deltaTime);

                // End drawing
                _spriteBatch.End();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UITestStage Draw error: {ex.Message}");
            }
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    System.Diagnostics.Debug.WriteLine("Disposing UITest Stage resources");

                    // Dispose font resources
                    _testFont?.RemoveReference();
                    _titleFont?.RemoveReference();
                    _japaneseFont?.RemoveReference();
                    _resourceManager?.Dispose();

                    // Dispose UI system
                    _uiManager?.Dispose();
                    _uiManager = null;
                    _mainContainer = null;

                    // Dispose MonoGame resources
                    _whitePixel?.Dispose();
                    _spriteBatch?.Dispose();

                    _whitePixel = null;
                    _spriteBatch = null;
                    _defaultFont = null;
                    _testFont = null;
                    _titleFont = null;
                    _japaneseFont = null;
                    _resourceManager = null;
                }
                _disposed = true;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Initialize ResourceManager and load test fonts
        /// </summary>
        /// <param name="graphicsDevice">Graphics device</param>
        private void InitializeResourceManager(GraphicsDevice graphicsDevice)
        {
            try
            {
                // Create ResourceManager using factory
                _resourceManager = DTX.Resources.ResourceManagerFactory.CreateResourceManager(graphicsDevice);
                _resourceManager.SetSkinPath("System/");

                System.Diagnostics.Debug.WriteLine("ResourceManager initialized");

                // Subscribe to resource load failed events for debugging
                _resourceManager.ResourceLoadFailed += (sender, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"Resource load failed: {e.Path} - {e.ErrorMessage}");
                    if (e.Exception != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Exception: {e.Exception}");
                    }
                };

                // Try to load different fonts for testing
                LoadTestFonts();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize ResourceManager: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Exception details: {ex}");
            }
        }

        /// <summary>
        /// Load test fonts to demonstrate the font system
        /// </summary>
        private void LoadTestFonts()
        {
            System.Diagnostics.Debug.WriteLine("Starting font loading...");
            Console.WriteLine("Starting font loading...");

            // Try to load system fonts first (should work on Windows)
            // Try to load test font
            try
            {
                System.Diagnostics.Debug.WriteLine("Attempting to load Arial 16pt...");
                Console.WriteLine("Attempting to load Arial 16pt...");
                _testFont = _resourceManager.LoadFont("Arial", 16, FontStyle.Regular);
                if (_testFont != null)
                {
                    System.Diagnostics.Debug.WriteLine("✓ Successfully loaded Arial 16pt font");
                    Console.WriteLine("✓ Successfully loaded Arial 16pt font");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ Arial 16pt font returned null");
                    Console.WriteLine("✗ Arial 16pt font returned null");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ Failed to load Arial 16pt: {ex.Message}");
                Console.WriteLine($"✗ Failed to load Arial 16pt: {ex.Message}");
                Console.WriteLine($"Exception details: {ex}");
            }

            // Try to load title font
            try
            {
                System.Diagnostics.Debug.WriteLine("Attempting to load Arial 24pt Bold...");
                Console.WriteLine("Attempting to load Arial 24pt Bold...");
                _titleFont = _resourceManager.LoadFont("Arial", 24, FontStyle.Bold);
                if (_titleFont != null)
                {
                    System.Diagnostics.Debug.WriteLine("✓ Successfully loaded Arial 24pt Bold font");
                    Console.WriteLine("✓ Successfully loaded Arial 24pt Bold font");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ Arial 24pt Bold font returned null");
                    Console.WriteLine("✗ Arial 24pt Bold font returned null");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ Failed to load Arial 24pt Bold: {ex.Message}");
                Console.WriteLine($"✗ Failed to load Arial 24pt Bold: {ex.Message}");
                Console.WriteLine($"Exception details: {ex}");
            }

            // Try to load Japanese font
            try
            {
                System.Diagnostics.Debug.WriteLine("Attempting to load MS Gothic 14pt...");
                Console.WriteLine("Attempting to load MS Gothic 14pt...");
                _japaneseFont = _resourceManager.LoadFont("MS Gothic", 14, FontStyle.Regular);
                if (_japaneseFont != null)
                {
                    System.Diagnostics.Debug.WriteLine("✓ Successfully loaded MS Gothic 14pt font");
                    Console.WriteLine("✓ Successfully loaded MS Gothic 14pt font");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ MS Gothic 14pt font returned null");
                    Console.WriteLine("✗ MS Gothic 14pt font returned null");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ Failed to load MS Gothic 14pt: {ex.Message}");
                Console.WriteLine($"✗ Failed to load MS Gothic 14pt: {ex.Message}");
                Console.WriteLine($"Exception details: {ex}");
            }

            // Summary
            var loadedCount = 0;
            if (_testFont != null) loadedCount++;
            if (_titleFont != null) loadedCount++;
            if (_japaneseFont != null) loadedCount++;

            System.Diagnostics.Debug.WriteLine($"Font loading complete: {loadedCount}/3 fonts loaded successfully");
            Console.WriteLine($"Font loading complete: {loadedCount}/3 fonts loaded successfully");
        }

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
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Center,
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
        /// Draw font test text to verify the font system
        /// </summary>
        private void DrawFontTests()
        {
            if (_spriteBatch == null || _whitePixel == null)
                return;

            try
            {
                var viewport = _game.GraphicsDevice.Viewport;
                var startY = viewport.Height - 200; // Draw at bottom of screen

                // Always draw a simple test first to make sure drawing works
                DrawFallbackText("FONT SYSTEM TEST - ALWAYS VISIBLE", new Vector2(20, startY - 150), Color.Lime);

                // Always draw font status first
                DrawFontStatus(startY - 100);

                // Test basic text rendering
                if (_testFont != null)
                {
                    try
                    {
                        var testText = "Font System Test - Basic Arial 16pt";
                        _testFont.DrawString(_spriteBatch, testText, new Vector2(20, startY), Color.White);

                        // Test character support
                        var charTest = "Character Support: ABCabc123!@#";
                        _testFont.DrawString(_spriteBatch, charTest, new Vector2(20, startY + 25), Color.LightGreen);

                        // Test outline effect
                        var outlineText = "Outline Effect Test";
                        _testFont.DrawStringWithOutline(_spriteBatch, outlineText, new Vector2(20, startY + 50),
                            Color.Yellow, Color.Black, 2);

                        // Test shadow effect
                        var shadowText = "Shadow Effect Test";
                        _testFont.DrawStringWithShadow(_spriteBatch, shadowText, new Vector2(20, startY + 75),
                            Color.Cyan, Color.DarkBlue, new Vector2(2, 2));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Test font error: {ex.Message}");
                        DrawFallbackText("Test Font Error", new Vector2(20, startY), Color.Red);
                    }
                }
                else
                {
                    DrawFallbackText("TEST FONT NOT LOADED", new Vector2(20, startY), Color.Red);
                }

                // Test title font
                if (_titleFont != null)
                {
                    try
                    {
                        var titleText = "DTXMania Font System - Bold 24pt";
                        _titleFont.DrawString(_spriteBatch, titleText, new Vector2(20, startY - 50), Color.Orange);

                        // Test gradient effect
                        var gradientText = "Gradient Effect";
                        _titleFont.DrawStringWithGradient(_spriteBatch, gradientText, new Vector2(20, startY - 25),
                            Color.Red, Color.Blue);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Title font error: {ex.Message}");
                        DrawFallbackText("Title Font Error", new Vector2(20, startY - 50), Color.Red);
                    }
                }
                else
                {
                    DrawFallbackText("TITLE FONT NOT LOADED", new Vector2(20, startY - 50), Color.Orange);
                }

                // Test Japanese font
                if (_japaneseFont != null)
                {
                    try
                    {
                        // Test basic Japanese characters
                        var japaneseText = "Japanese Test: こんにちは 世界";
                        _japaneseFont.DrawString(_spriteBatch, japaneseText, new Vector2(20, startY + 100), Color.White);

                        // Test mixed text
                        var mixedText = "Mixed: Hello こんにちは World 世界";
                        _japaneseFont.DrawString(_spriteBatch, mixedText, new Vector2(20, startY + 125), Color.LightBlue);

                        // Test character replacement
                        var specialChars = "Special: \u201CHello\u201D \u2014 World\u2026";
                        _japaneseFont.DrawString(_spriteBatch, specialChars, new Vector2(20, startY + 150), Color.Pink);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Japanese font error: {ex.Message}");
                        DrawFallbackText("Japanese Font Error", new Vector2(20, startY + 100), Color.Red);
                    }
                }
                else
                {
                    DrawFallbackText("JAPANESE FONT NOT LOADED", new Vector2(20, startY + 100), Color.Yellow);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawFontTests error: {ex.Message}");
            }
        }

        /// <summary>
        /// Draw font loading status information
        /// </summary>
        private void DrawFontStatus(float y)
        {
            if (_spriteBatch == null)
                return;

            try
            {
                var statusFont = _testFont ?? _titleFont ?? _japaneseFont; // Use any available font
                if (statusFont == null)
                {
                    // If no fonts loaded, use fallback text rendering
                    DrawFallbackText("FONT SYSTEM STATUS - NO FONTS LOADED", new Vector2(20, y), Color.Red);
                    DrawFallbackText("Font loading failed - Check debug output for details", new Vector2(20, y + 20), Color.Orange);
                    DrawFallbackText("This fallback text proves the system is working", new Vector2(20, y + 40), Color.Yellow);
                    return;
                }

                // Draw font loading status
                var status1 = $"Test Font (Arial 16): {(_testFont != null ? "✓ Loaded" : "✗ Failed")}";
                var status2 = $"Title Font (Arial 24 Bold): {(_titleFont != null ? "✓ Loaded" : "✗ Failed")}";
                var status3 = $"Japanese Font (MS Gothic 14): {(_japaneseFont != null ? "✓ Loaded" : "✗ Failed")}";

                statusFont.DrawString(_spriteBatch, status1, new Vector2(20, y),
                    _testFont != null ? Color.Green : Color.Red);
                statusFont.DrawString(_spriteBatch, status2, new Vector2(20, y + 20),
                    _titleFont != null ? Color.Green : Color.Red);
                statusFont.DrawString(_spriteBatch, status3, new Vector2(20, y + 40),
                    _japaneseFont != null ? Color.Green : Color.Red);

                // Draw character support information
                if (_testFont != null)
                {
                    var charInfo = $"Character Support - ASCII: {(_testFont.SupportsCharacter('A') ? "✓" : "✗")} " +
                                  $"Japanese: {(_testFont.SupportsCharacter('あ') ? "✓" : "✗")} " +
                                  $"Kanji: {(_testFont.SupportsCharacter('漢') ? "✓" : "✗")}";
                    statusFont.DrawString(_spriteBatch, charInfo, new Vector2(20, y + 60), Color.Yellow);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DrawFontStatus error: {ex.Message}");
            }
        }

        /// <summary>
        /// Draw fallback text using white pixel rectangles when fonts fail
        /// </summary>
        private void DrawFallbackText(string text, Vector2 position, Color color)
        {
            if (_whitePixel == null || string.IsNullOrEmpty(text))
                return;

            // Draw each character as a small rectangle
            var charWidth = 8;
            var charHeight = 12;
            var spacing = 2;

            for (int i = 0; i < Math.Min(text.Length, 80); i++) // Limit to 80 characters
            {
                var charRect = new Rectangle(
                    (int)position.X + i * (charWidth + spacing),
                    (int)position.Y,
                    charWidth,
                    charHeight);

                // Use different heights for different characters to make it more readable
                if (char.IsUpper(text[i]))
                    charRect.Height = charHeight;
                else if (char.IsLower(text[i]))
                    charRect.Height = charHeight - 2;
                else if (char.IsDigit(text[i]))
                    charRect.Height = charHeight - 1;
                else if (text[i] == ' ')
                    continue; // Skip spaces

                _spriteBatch.Draw(_whitePixel, charRect, color);
            }
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
