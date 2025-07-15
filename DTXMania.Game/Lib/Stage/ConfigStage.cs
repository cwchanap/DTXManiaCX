using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTX.Stage;
using DTX.Config;
using DTX.Resources;
using DTXMania.Game;
using System;
using System.Collections.Generic;


namespace DTX.Stage
{
    /// <summary>
    /// Configuration stage with basic settings management
    /// Follows DTXMania patterns for config item handling
    /// </summary>
    public class ConfigStage : BaseStage
    {
        #region Private Fields

        private IConfigManager _configManager;
        private List<IConfigItem> _configItems;
        private ConfigData _workingConfig;
        private bool _hasUnsavedChanges;
        private int _selectedIndex = 0;

        // Input handling
        private KeyboardState _previousKeyboardState;
        private KeyboardState _currentKeyboardState;

        // Graphics resources
        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private BitmapFont _bitmapFont;
        private IResourceManager _resourceManager;

        // DTXMania-style constants
        private const int MenuX = 100;
        private const int MenuY = 100;
        private const int MenuItemHeight = 40;
        private const int MenuItemWidth = 600;

        #endregion

        #region Constructor

        public override StageType Type => StageType.Config;

        public ConfigStage(BaseGame game) : base(game)
        {
            _configManager = game.ConfigManager ?? throw new InvalidOperationException("ConfigManager not found");
        }

        #endregion

        #region Stage Lifecycle

        protected override void OnActivate()
        {
            System.Diagnostics.Debug.WriteLine("Activating Config Stage");

            InitializeGraphics();
            LoadConfiguration();
            SetupConfigItems();

            _previousKeyboardState = Keyboard.GetState();
            _currentKeyboardState = Keyboard.GetState();
        }

        protected override void OnUpdate(double deltaTime)
        {
            _previousKeyboardState = _currentKeyboardState;
            _currentKeyboardState = Keyboard.GetState();

            // Handle input
            HandleInput();
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin();

            // Draw background
            DrawBackground();

            // Draw title
            DrawTitle();

            // Draw config items
            DrawConfigItems();

            // Draw buttons
            DrawButtons();

            // Draw instructions
            DrawInstructions();

            _spriteBatch.End();
        }

        protected override void OnDeactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating Config Stage");

            // Check for unsaved changes
            if (_hasUnsavedChanges)
            {
                // In a full implementation, you might want to show a confirmation dialog
                System.Diagnostics.Debug.WriteLine("Warning: Unsaved configuration changes will be lost");
            }

            // Reset input state
            _previousKeyboardState = default;
            _currentKeyboardState = default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                System.Diagnostics.Debug.WriteLine("Disposing Config Stage resources");

                // Cleanup MonoGame resources
                _whitePixel?.Dispose();
                _spriteBatch?.Dispose();
                _bitmapFont?.Dispose();
                _resourceManager?.Dispose();

                _whitePixel = null;
                _spriteBatch = null;
                _bitmapFont = null;
                _resourceManager = null;
            }

            base.Dispose(disposing);
        }

        #endregion

        #region Initialization

        private void InitializeGraphics()
        {
            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Initialize ResourceManager
            _resourceManager = ResourceManagerFactory.CreateResourceManager(graphicsDevice);

            // Initialize bitmap font for DTXMania-style text rendering
            try
            {
                var consoleFontConfig = BitmapFont.CreateConsoleFontConfig();
                _bitmapFont = new BitmapFont(graphicsDevice, _resourceManager, consoleFontConfig);
                System.Diagnostics.Debug.WriteLine("Bitmap font loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load bitmap font: {ex.Message}");
                // Font is optional - we'll use rectangles as fallback
            }
        }


        private void LoadConfiguration()
        {
            // Create a working copy of the configuration
            var originalConfig = _configManager.Config;
            _workingConfig = new ConfigData
            {
                ScreenWidth = originalConfig.ScreenWidth,
                ScreenHeight = originalConfig.ScreenHeight,
                FullScreen = originalConfig.FullScreen,
                VSyncWait = originalConfig.VSyncWait,
                DTXManiaVersion = originalConfig.DTXManiaVersion,
                SkinPath = originalConfig.SkinPath,
                DTXPath = originalConfig.DTXPath,
                UseBoxDefSkin = originalConfig.UseBoxDefSkin,
                SystemSkinRoot = originalConfig.SystemSkinRoot,
                LastUsedSkin = originalConfig.LastUsedSkin,
                MasterVolume = originalConfig.MasterVolume,
                BGMVolume = originalConfig.BGMVolume,
                SEVolume = originalConfig.SEVolume,
                BufferSizeMs = originalConfig.BufferSizeMs,                ScrollSpeed = originalConfig.ScrollSpeed,
                AutoPlay = originalConfig.AutoPlay
            };

            _hasUnsavedChanges = false;
        }

        private void SetupConfigItems()
        {
            _configItems = new List<IConfigItem>();

            // Screen Resolution dropdown
            var resolutionItem = new DropdownConfigItem(
                "Screen Resolution",
                () => $"{_workingConfig.ScreenWidth}x{_workingConfig.ScreenHeight}",
                new[] { "1280x720", "1920x1080", "2560x1440", "3840x2160" },
                value =>
                {
                    var parts = value.Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
                    {
                        _workingConfig.ScreenWidth = width;
                        _workingConfig.ScreenHeight = height;
                        _hasUnsavedChanges = true;
                        System.Diagnostics.Debug.WriteLine($"Resolution changed to {width}x{height}");
                    }
                }
            );

            // Fullscreen checkbox
            var fullscreenItem = new ToggleConfigItem(
                "Fullscreen",
                () => _workingConfig.FullScreen,
                value =>
                {
                    _workingConfig.FullScreen = value;
                    _hasUnsavedChanges = true;
                    System.Diagnostics.Debug.WriteLine($"Fullscreen changed to {value}");
                }
            );

            // VSync toggle
            var vsyncItem = new ToggleConfigItem(
                "VSync Wait",
                () => _workingConfig.VSyncWait,
                value =>
                {
                    _workingConfig.VSyncWait = value;
                    _hasUnsavedChanges = true;
                    System.Diagnostics.Debug.WriteLine($"VSync changed to {value}");
                }            );

            _configItems.Add(resolutionItem);
            _configItems.Add(fullscreenItem);
            _configItems.Add(vsyncItem);

            // Select first item
            if (_configItems.Count > 0)
            {
                _selectedIndex = 0;
            }
        }

        #endregion

        #region Input Handling

        private void HandleInput()
        {
            // Handle ESC key - return to title stage
            if (IsKeyPressed(Keys.Escape))
            {
                if (_hasUnsavedChanges)
                {
                    System.Diagnostics.Debug.WriteLine("Config: ESC pressed with unsaved changes - discarding changes");
                }
                System.Diagnostics.Debug.WriteLine("Config: Returning to Title stage");
                ChangeStage(StageType.Title, new CrossfadeTransition(0.3));
                return;
            }

            // Handle navigation
            if (IsKeyPressed(Keys.Up))
            {
                _selectedIndex = (_selectedIndex - 1 + _configItems.Count + 2) % (_configItems.Count + 2); // +2 for buttons
            }
            else if (IsKeyPressed(Keys.Down))
            {
                _selectedIndex = (_selectedIndex + 1) % (_configItems.Count + 2); // +2 for buttons
            }

            // Handle config item value editing (only for config items, not buttons)
            if (_selectedIndex < _configItems.Count)
            {
                var selectedItem = _configItems[_selectedIndex];

                // Left/Right arrows for value editing
                if (IsKeyPressed(Keys.Left))
                {
                    selectedItem.PreviousValue();
                }
                else if (IsKeyPressed(Keys.Right))
                {
                    selectedItem.NextValue();
                }
                else if (IsKeyPressed(Keys.Enter))
                {
                    selectedItem.ToggleValue();
                }
            }
            else
            {
                // Handle button selection
                if (IsKeyPressed(Keys.Enter))
                {
                    int buttonIndex = _selectedIndex - _configItems.Count;
                    if (buttonIndex == 0) // Back button
                    {
                        OnBackButtonClicked(null, EventArgs.Empty);
                    }
                    else if (buttonIndex == 1) // Save button
                    {
                        OnSaveButtonClicked(null, EventArgs.Empty);
                    }
                }
            }
        }

        private bool IsKeyPressed(Keys key)
        {
            return _currentKeyboardState.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key);
        }

        #endregion

        #region Event Handlers

        private void OnBackButtonClicked(object sender, EventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                System.Diagnostics.Debug.WriteLine("Back button clicked with unsaved changes - discarding changes");
            }
            System.Diagnostics.Debug.WriteLine("Back button clicked - returning to Title stage");
            ChangeStage(StageType.Title, new CrossfadeTransition(0.3));
        }

        private void OnSaveButtonClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Save button clicked - applying configuration");
            ApplyConfiguration();
            ChangeStage(StageType.Title, new CrossfadeTransition(0.3));
        }

        #endregion

        #region Configuration Management

        private void ApplyConfiguration()
        {            // Copy working config back to the main config
            var config = _configManager.Config;
            config.ScreenWidth = _workingConfig.ScreenWidth;
            config.ScreenHeight = _workingConfig.ScreenHeight;
            config.FullScreen = _workingConfig.FullScreen;
            config.VSyncWait = _workingConfig.VSyncWait;

            // Save to file
            try
            {
                _configManager.SaveConfig("Config.ini");
                _hasUnsavedChanges = false;
                System.Diagnostics.Debug.WriteLine("Configuration saved successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save configuration: {ex.Message}");
            }
        }

        #endregion

        #region Drawing (DTXMania Style)

        private void DrawBackground()
        {
            var viewport = _game.GraphicsDevice.Viewport;
            _spriteBatch.Draw(_whitePixel,
                new Rectangle(0, 0, viewport.Width, viewport.Height),
                new Color(16, 16, 32));
        }

        private void DrawTitle()
        {
            const string titleText = "CONFIGURATION";
            int x = MenuX;
            int y = 50;

            if (_bitmapFont?.IsLoaded == true)
            {
                // Center the title
                var textSize = _bitmapFont.MeasureText(titleText);
                x = (1280 - (int)textSize.X) / 2; // Assume 1280 width for centering
                _bitmapFont.DrawText(_spriteBatch, titleText, x, y, Color.White, BitmapFont.FontType.Normal);
            }
            else
            {
                // Fallback to rectangle
                DrawTextRect(x, y, titleText.Length * 12, 20, Color.White);
            }
        }

        private void DrawConfigItems()
        {
            int x = MenuX;
            int y = MenuY;

            for (int i = 0; i < _configItems.Count; i++)
            {
                var item = _configItems[i];
                var displayText = item.GetDisplayText();
                bool isSelected = (i == _selectedIndex);

                // Draw selection background
                if (isSelected)
                {
                    DrawTextRect(x - 5, y - 2, MenuItemWidth + 10, MenuItemHeight, new Color(64, 64, 128, 150));
                }

                // Draw text
                if (_bitmapFont?.IsLoaded == true)
                {
                    var textColor = isSelected ? Color.Yellow : Color.White;
                    var fontType = isSelected ? BitmapFont.FontType.Thin : BitmapFont.FontType.Normal;
                    _bitmapFont.DrawText(_spriteBatch, displayText, x, y + 10, textColor, fontType);
                }
                else
                {
                    // Fallback to rectangle
                    var color = isSelected ? Color.Yellow : Color.White;
                    DrawTextRect(x, y + 10, displayText.Length * 8, 16, color);
                }

                y += MenuItemHeight;
            }
        }

        private void DrawButtons()
        {
            int x = MenuX;
            int y = MenuY + (_configItems.Count * MenuItemHeight) + 20;

            // Back button
            bool backSelected = (_selectedIndex == _configItems.Count);
            if (backSelected)
            {
                DrawTextRect(x - 5, y - 2, 100, 30, new Color(64, 64, 64, 150));
            }

            if (_bitmapFont?.IsLoaded == true)
            {
                var textColor = backSelected ? Color.Yellow : Color.White;
                var fontType = backSelected ? BitmapFont.FontType.Thin : BitmapFont.FontType.Normal;
                _bitmapFont.DrawText(_spriteBatch, "BACK", x, y + 5, textColor, fontType);
            }
            else
            {
                var color = backSelected ? Color.Yellow : Color.Gray;
                DrawTextRect(x, y + 5, 32, 16, color);
            }

            // Save button
            x += 150;
            bool saveSelected = (_selectedIndex == _configItems.Count + 1);
            if (saveSelected)
            {
                DrawTextRect(x - 5, y - 2, 120, 30, new Color(64, 96, 64, 150));
            }

            if (_bitmapFont?.IsLoaded == true)
            {
                var textColor = saveSelected ? Color.Yellow : Color.White;
                var fontType = saveSelected ? BitmapFont.FontType.Thin : BitmapFont.FontType.Normal;
                _bitmapFont.DrawText(_spriteBatch, "SAVE & EXIT", x, y + 5, textColor, fontType);
            }
            else
            {
                var color = saveSelected ? Color.Yellow : Color.Green;
                DrawTextRect(x, y + 5, 88, 16, color);
            }
        }

        private void DrawInstructions()
        {
            const string instructions = "Use UP/DOWN to navigate, LEFT/RIGHT to change values, ENTER to select, ESC to cancel";
            int x = 10;
            int y = 720 - 30; // Bottom of screen

            if (_bitmapFont?.IsLoaded == true)
            {
                _bitmapFont.DrawText(_spriteBatch, instructions, x, y, Color.White, BitmapFont.FontType.Normal);
            }
            else
            {
                // Fallback to rectangle
                DrawTextRect(x, y, instructions.Length * 6, 12, Color.Gray);
            }
        }

        private void DrawTextRect(int x, int y, int width, int height, Color color)
        {
            if (_whitePixel != null)
            {
                _spriteBatch.Draw(_whitePixel, new Rectangle(x, y, width, height), color);
            }
        }

        #endregion
    }
}
