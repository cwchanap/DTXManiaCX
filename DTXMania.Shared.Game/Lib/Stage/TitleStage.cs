using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTX.Stage;
using DTXMania.Shared.Game;
using System;
using System.IO;

namespace DTX.Stage
{
    /// <summary>
    /// Title stage implementation based on DTXManiaNX CStageTitle
    /// Shows DTXMania logo, version, and main menu options
    /// </summary>
    public class TitleStage : IStage
    {
        #region Fields

        private readonly BaseGame _game;
        private SpriteBatch _spriteBatch;
        private Texture2D _backgroundTexture;
        private Texture2D _menuTexture;
        private Texture2D _whitePixel;
        private bool _disposed = false;

        // DTXMania pattern: timing and animation
        private double _elapsedTime;
        private bool _isFirstUpdate = true;
        private TitlePhase _currentPhase = TitlePhase.FadeIn;

        // Menu system
        private int _currentMenuIndex = 0;
        private readonly string[] _menuItems = { "GAME START", "CONFIG", "EXIT" };
        private const int MenuItemHeight = 39; // 0x27 from original
        private const int MenuItemWidth = 227; // 0xe3 from original
        private const int MenuX = 506; // 0x1fa from original
        private const int MenuY = 513; // 0x201 from original

        // Input handling
        private KeyboardState _previousKeyboardState;
        private KeyboardState _currentKeyboardState;

        // Animation timers
        private double _cursorFlashTimer = 0;
        private double _menuMoveTimer = 0;
        private bool _isMovingUp = false;
        private bool _isMovingDown = false;

        #endregion

        #region Properties

        public StageType Type => StageType.Title;

        #endregion

        #region Constructor

        public TitleStage(BaseGame game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
        }

        #endregion

        #region IStage Implementation

        public void Activate()
        {
            System.Diagnostics.Debug.WriteLine("Activating Title Stage");

            // Initialize graphics resources
            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            // Create white pixel for drawing rectangles
            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Load background texture (DTXManiaNX uses 2_background.jpg)
            LoadBackgroundTexture();

            // Load menu texture (DTXManiaNX uses 2_menu.png)
            LoadMenuTexture();

            // Initialize state
            _elapsedTime = 0;
            _isFirstUpdate = true;
            _currentPhase = TitlePhase.FadeIn;
            _currentMenuIndex = 0;

            // Initialize input state
            _previousKeyboardState = Keyboard.GetState();
            _currentKeyboardState = Keyboard.GetState();

            System.Diagnostics.Debug.WriteLine("Title Stage activated successfully");
        }

        public void Update(double deltaTime)
        {
            _elapsedTime += deltaTime;

            // Update input state
            _previousKeyboardState = _currentKeyboardState;
            _currentKeyboardState = Keyboard.GetState();

            // Handle first update
            if (_isFirstUpdate)
            {
                _isFirstUpdate = false;
                _currentPhase = TitlePhase.Normal;
                _cursorFlashTimer = 0;
            }

            // Update animations
            UpdateAnimations(deltaTime);

            // Handle input only in normal phase
            if (_currentPhase == TitlePhase.Normal)
            {
                HandleInput();
            }
        }

        public void Draw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);

            // Draw background
            DrawBackground();

            // Draw version information (DTXMania pattern)
            DrawVersionInfo();

            // Draw menu
            DrawMenu();

            _spriteBatch.End();
        }

        public void Deactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating Title Stage");

            // Reset stage state for potential reactivation
            _elapsedTime = 0;
            _isFirstUpdate = true;
            _currentPhase = TitlePhase.FadeIn;
            _currentMenuIndex = 0;

            // Reset input state
            _previousKeyboardState = default;
            _currentKeyboardState = default;
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
                    System.Diagnostics.Debug.WriteLine("Disposing Title Stage resources");

                    // Cleanup MonoGame resources
                    _backgroundTexture?.Dispose();
                    _menuTexture?.Dispose();
                    _whitePixel?.Dispose();
                    _spriteBatch?.Dispose();

                    _backgroundTexture = null;
                    _menuTexture = null;
                    _whitePixel = null;
                    _spriteBatch = null;
                }
                _disposed = true;
            }
        }

        #endregion

        #region Private Methods - Resource Loading

        private void LoadBackgroundTexture()
        {
            try
            {
                // Try to load from DTXManiaNX graphics folder first
                string backgroundPath = Path.Combine("DTXManiaNX", "Runtime", "System", "Graphics", "2_background.jpg");

                if (File.Exists(backgroundPath))
                {
                    using (var fileStream = File.OpenRead(backgroundPath))
                    {
                        _backgroundTexture = Texture2D.FromStream(_game.GraphicsDevice, fileStream);
                        System.Diagnostics.Debug.WriteLine($"Loaded background texture from: {backgroundPath}");
                        return;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Background texture not found at: {backgroundPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load background texture: {ex.Message}");
            }

            // Fallback: create a simple gradient background
            CreateFallbackBackground();
        }

        private void LoadMenuTexture()
        {
            try
            {
                // Try to load from DTXManiaNX graphics folder
                string menuPath = Path.Combine("DTXManiaNX", "Runtime", "System", "Graphics", "2_menu.png");

                if (File.Exists(menuPath))
                {
                    using (var fileStream = File.OpenRead(menuPath))
                    {
                        _menuTexture = Texture2D.FromStream(_game.GraphicsDevice, fileStream);
                        System.Diagnostics.Debug.WriteLine($"Loaded menu texture from: {menuPath}");
                        return;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Menu texture not found at: {menuPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load menu texture: {ex.Message}");
            }

            // Menu texture is optional - we'll draw text-based menu as fallback
        }

        private void CreateFallbackBackground()
        {
            // Create a simple gradient background as fallback
            var viewport = _game.GraphicsDevice.Viewport;
            var width = Math.Max(viewport.Width, 1024);
            var height = Math.Max(viewport.Height, 768);

            _backgroundTexture = new Texture2D(_game.GraphicsDevice, width, height);
            var colorData = new Color[width * height];

            // Create a simple gradient from dark blue to black
            for (int y = 0; y < height; y++)
            {
                float factor = (float)y / height;
                var color = Color.Lerp(new Color(0, 0, 64), Color.Black, factor);

                for (int x = 0; x < width; x++)
                {
                    colorData[y * width + x] = color;
                }
            }

            _backgroundTexture.SetData(colorData);
            System.Diagnostics.Debug.WriteLine("Created fallback gradient background");
        }

        #endregion

        #region Private Methods - Update Logic

        private void UpdateAnimations(double deltaTime)
        {
            // Update cursor flash animation
            _cursorFlashTimer += deltaTime;
            if (_cursorFlashTimer > 0.7) // 700ms cycle like original
            {
                _cursorFlashTimer = 0;
            }

            // Update menu movement animations
            if (_isMovingUp || _isMovingDown)
            {
                _menuMoveTimer += deltaTime;
                if (_menuMoveTimer > 0.1) // 100ms animation like original
                {
                    _menuMoveTimer = 0;
                    _isMovingUp = false;
                    _isMovingDown = false;
                }
            }
        }

        private void HandleInput()
        {
            // Handle ESC key - exit game
            if (IsKeyPressed(Keys.Escape))
            {
                _game.Exit();
                return;
            }

            // Handle menu navigation
            if (IsKeyPressed(Keys.Up))
            {
                MoveCursorUp();
            }
            else if (IsKeyPressed(Keys.Down))
            {
                MoveCursorDown();
            }

            // Handle menu selection
            if (IsKeyPressed(Keys.Enter) || IsKeyPressed(Keys.Space))
            {
                SelectCurrentMenuItem();
            }
        }

        private bool IsKeyPressed(Keys key)
        {
            return _currentKeyboardState.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key);
        }

        private void MoveCursorUp()
        {
            if (_currentMenuIndex > 0)
            {
                _currentMenuIndex--;
                _isMovingUp = true;
                _menuMoveTimer = 0;
                System.Diagnostics.Debug.WriteLine($"Menu cursor moved up to: {_menuItems[_currentMenuIndex]}");
            }
        }

        private void MoveCursorDown()
        {
            if (_currentMenuIndex < _menuItems.Length - 1)
            {
                _currentMenuIndex++;
                _isMovingDown = true;
                _menuMoveTimer = 0;
                System.Diagnostics.Debug.WriteLine($"Menu cursor moved down to: {_menuItems[_currentMenuIndex]}");
            }
        }

        private void SelectCurrentMenuItem()
        {
            System.Diagnostics.Debug.WriteLine($"Selected menu item: {_menuItems[_currentMenuIndex]}");

            switch (_currentMenuIndex)
            {
                case 0: // GAME START
                    // Direct to UI Test Stage as requested
                    System.Diagnostics.Debug.WriteLine("Starting game - transitioning to UI Test Stage");
                    _game.StageManager?.ChangeStage(StageType.UITest);
                    break;

                case 1: // CONFIG
                    System.Diagnostics.Debug.WriteLine("Opening config - transitioning to Config Stage");
                    _game.StageManager?.ChangeStage(StageType.Config);
                    break;

                case 2: // EXIT
                    System.Diagnostics.Debug.WriteLine("Exiting game");
                    _game.Exit();
                    break;
            }
        }

        #endregion

        #region Private Methods - Drawing

        private void DrawBackground()
        {
            if (_backgroundTexture != null)
            {
                var viewport = _game.GraphicsDevice.Viewport;
                _spriteBatch.Draw(_backgroundTexture,
                    new Rectangle(0, 0, viewport.Width, viewport.Height),
                    Color.White);
            }
            else if (_whitePixel != null)
            {
                // Fallback: draw solid dark background
                var viewport = _game.GraphicsDevice.Viewport;
                _spriteBatch.Draw(_whitePixel,
                    new Rectangle(0, 0, viewport.Width, viewport.Height),
                    new Color(0, 0, 32));
            }
        }

        private void DrawVersionInfo()
        {
            // Draw version info in top-left corner (DTXMania pattern)
            const string versionText = "DTXManiaCX v1.0.0 - MonoGame Edition";

            if (_whitePixel != null)
            {
                // Draw text as rectangles (since we don't have fonts loaded yet)
                DrawTextRect(2, 2, versionText.Length * 8, 16, Color.White);
            }
        }

        private void DrawMenu()
        {
            if (_whitePixel == null)
                return;

            // Calculate menu position with animation offset
            int baseY = MenuY;
            int animationOffset = 0;

            if (_isMovingUp && _menuMoveTimer < 0.1)
            {
                float progress = (float)(_menuMoveTimer / 0.1);
                animationOffset = (int)(MenuItemHeight * 0.5 * (Math.Cos(Math.PI * progress) + 1.0));
            }
            else if (_isMovingDown && _menuMoveTimer < 0.1)
            {
                float progress = (float)(_menuMoveTimer / 0.1);
                animationOffset = -(int)(MenuItemHeight * 0.5 * (Math.Cos(Math.PI * progress) + 1.0));
            }

            // Draw menu items
            for (int i = 0; i < _menuItems.Length; i++)
            {
                int y = baseY + (i * MenuItemHeight) + animationOffset;
                Color itemColor = (i == _currentMenuIndex) ? Color.Yellow : Color.Gray;

                // Draw menu item background
                DrawTextRect(MenuX, y, MenuItemWidth, MenuItemHeight, itemColor * 0.3f);

                // Draw menu item text representation
                DrawTextRect(MenuX + 10, y + 8, _menuItems[i].Length * 8, 16, itemColor);
            }

            // Draw cursor with flash animation
            if (_currentMenuIndex >= 0 && _currentMenuIndex < _menuItems.Length)
            {
                int cursorY = baseY + (_currentMenuIndex * MenuItemHeight) + animationOffset;

                // Flash effect based on timer
                float flashIntensity = (float)(0.5 + 0.5 * Math.Sin(_cursorFlashTimer * Math.PI * 2 / 0.7));
                Color cursorColor = Color.White * flashIntensity;

                // Draw cursor border
                DrawTextRect(MenuX - 2, cursorY - 2, MenuItemWidth + 4, MenuItemHeight + 4, cursorColor);
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

        #region Enums

        private enum TitlePhase
        {
            FadeIn,
            Normal,
            FadeOut
        }

        #endregion
    }
}
