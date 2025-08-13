using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI;
using DTXMania.Game;
using DTXMania.Game.Lib.Input;
using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Title stage implementation based on DTXManiaNX CStageTitle
    /// Shows DTXMania logo, version, and main menu options
    /// </summary>
    public class TitleStage : BaseStage
    {
        #region Fields

        private SpriteBatch _spriteBatch;
        private ITexture _backgroundTexture;
        private ITexture _menuTexture;
        private Texture2D _whitePixel;
        private IResourceManager _resourceManager;

        // Sound effects
        private ISound _cursorMoveSound;
        private ISound _selectSound;
        private ISound _gameStartSound;

        // DTXMania pattern: timing and animation
        private double _elapsedTime;
        private TitlePhase _titlePhase = TitlePhase.FadeIn;



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
        private MouseState _previousMouseState;
        private MouseState _currentMouseState;
        private int _hoveredMenuIndex = -1;

        // Animation timers
        private double _cursorFlashTimer = 0;
        private double _menuMoveTimer = 0;
        private bool _isMovingUp = false;
        private bool _isMovingDown = false;

        #endregion

        #region Properties

        public override StageType Type => StageType.Title;

        #endregion

        #region Constructor

        public TitleStage(BaseGame game) : base(game)
        {
        }

        #endregion

        #region BaseStage Implementation

        protected override void OnActivate()
        {
            System.Diagnostics.Debug.WriteLine("Activating Title Stage");

            // Initialize graphics resources
            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            // Create white pixel for drawing rectangles
            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Initialize ResourceManager using factory
            _resourceManager = _game.ResourceManager;

            // Load background texture (DTXManiaNX uses 2_background.jpg)
            LoadBackgroundTexture();

            // Load menu texture (DTXManiaNX uses 2_menu.png)
            LoadMenuTexture();

            // Load sound effects
            LoadSoundEffects();

            // Initialize state
            _elapsedTime = 0;
            _titlePhase = TitlePhase.FadeIn;
            _currentMenuIndex = 0;

            // Initialize input state
            _previousKeyboardState = Keyboard.GetState();
            _currentKeyboardState = Keyboard.GetState();
            _previousMouseState = Mouse.GetState();
            _currentMouseState = Mouse.GetState();

            System.Diagnostics.Debug.WriteLine("Title Stage activated successfully");
        }

        protected override void OnFirstUpdate(double deltaTime)
        {
            _titlePhase = TitlePhase.Normal; // Go directly to normal menu
            _cursorFlashTimer = 0;
        }

        protected override void OnUpdate(double deltaTime)
        {
            _elapsedTime += deltaTime;

            // Update input state
            _previousKeyboardState = _currentKeyboardState;
            _currentKeyboardState = Keyboard.GetState();
            _previousMouseState = _currentMouseState;
            _currentMouseState = Mouse.GetState();

            // Update animations
            UpdateAnimations(deltaTime);

            // Handle input
            if (_titlePhase == TitlePhase.Normal && _currentPhase == StagePhase.Normal)
            {
                HandleInput();
                HandleMouseInput();
            }
        }

        protected override void OnDraw(double deltaTime)
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

        protected override void OnDeactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating Title Stage");

            // Reset stage state for potential reactivation
            _elapsedTime = 0;
            _titlePhase = TitlePhase.FadeIn;
            _currentMenuIndex = 0;

            // Reset input state
            _previousKeyboardState = default;
            _currentKeyboardState = default;
            _previousMouseState = default;
            _currentMouseState = default;
            _hoveredMenuIndex = -1;
        }

        protected override void OnTransitionInStarted(IStageTransition transition)
        {
            System.Diagnostics.Debug.WriteLine($"Title Stage: Transition in started with {transition.GetType().Name}");

            // Handle special transition from startup
            if (transition is StartupToTitleTransition)
            {
                _titlePhase = TitlePhase.FadeInFromStartup;
            }
            else
            {
                _titlePhase = TitlePhase.FadeIn;
            }
        }

        protected override void OnTransitionCompleted()
        {
            System.Diagnostics.Debug.WriteLine("Title Stage: Transition completed");
            _titlePhase = TitlePhase.Normal;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                System.Diagnostics.Debug.WriteLine("Disposing Title Stage resources");

                // Cleanup MonoGame resources - using reference counting for managed textures
                _backgroundTexture?.RemoveReference();
                _menuTexture?.RemoveReference();
                _whitePixel?.Dispose();
                _spriteBatch?.Dispose();
                _resourceManager?.Dispose();

                // Cleanup sound resources
                _cursorMoveSound?.Dispose();
                _selectSound?.Dispose();
                _gameStartSound?.Dispose();

                _backgroundTexture = null;
                _menuTexture = null;
                _whitePixel = null;
                _spriteBatch = null;
                _resourceManager = null;
                _cursorMoveSound = null;
                _selectSound = null;
                _gameStartSound = null;
            }

            base.Dispose(disposing);
        }

        #region Private Methods - Resource Loading

        private void LoadBackgroundTexture()
        {
            try
            {
                // Use ResourceManager to load background texture with proper skin path resolution
                _backgroundTexture = _resourceManager.LoadTexture(TexturePath.TitleBackground);
                System.Diagnostics.Debug.WriteLine("Loaded title background using ResourceManager");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load title background: {ex.Message}");
                // ResourceManager will handle fallback automatically, so _backgroundTexture should still be valid
            }
        }

        private void LoadMenuTexture()
        {
            try
            {
                // Use ResourceManager to load menu texture with proper skin path resolution
                _menuTexture = _resourceManager.LoadTexture(TexturePath.TitleMenu);
                System.Diagnostics.Debug.WriteLine("Loaded menu texture using ResourceManager");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load menu texture: {ex.Message}");
                // Menu texture is optional - we'll draw text-based menu as fallback
            }
        }

        private void LoadSoundEffects()
        {
            try
            {
                // Load DTXMania-style sound effects (OGG format)
                _cursorMoveSound = _resourceManager.LoadSound("Sounds/Move.ogg");
                System.Diagnostics.Debug.WriteLine("Loaded cursor move sound");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load cursor move sound: {ex.Message}");
            }

            try
            {
                _selectSound = _resourceManager.LoadSound("Sounds/Decide.ogg");
                System.Diagnostics.Debug.WriteLine("Loaded select sound");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load select sound: {ex.Message}");
            }

            try
            {
                // Load game start sound (note: file name has space)
                _gameStartSound = _resourceManager.LoadSound("Sounds/Game start.ogg");
                System.Diagnostics.Debug.WriteLine("Loaded game start sound");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load game start sound, trying fallback: {ex.Message}");
                try
                {
                    // Fallback to decide sound if Game start.ogg doesn't exist
                    _gameStartSound = _resourceManager.LoadSound("Sounds/Decide.ogg");
                    System.Diagnostics.Debug.WriteLine("Loaded game start sound (fallback to Decide.ogg)");
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load game start fallback sound: {fallbackEx.Message}");
                }
            }
        }





        #endregion

        #region Private Methods - Animation and Input



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
            // Check for back action (ESC key or controller Back button) using consolidated method
            if (_game.InputManager?.IsBackActionTriggered() == true)
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
            var previousIndex = _currentMenuIndex;

            if (_currentMenuIndex > 0)
            {
                _currentMenuIndex--;
            }
            else
            {
                // Menu wrapping: go to last item
                _currentMenuIndex = _menuItems.Length - 1;
            }

            if (_currentMenuIndex != previousIndex)
            {
                _isMovingUp = true;
                _menuMoveTimer = 0;
                PlayCursorMoveSound();
                System.Diagnostics.Debug.WriteLine($"Menu cursor moved up to: {_menuItems[_currentMenuIndex]}");
            }
        }

        private void MoveCursorDown()
        {
            var previousIndex = _currentMenuIndex;

            if (_currentMenuIndex < _menuItems.Length - 1)
            {
                _currentMenuIndex++;
            }
            else
            {
                // Menu wrapping: go to first item
                _currentMenuIndex = 0;
            }

            if (_currentMenuIndex != previousIndex)
            {
                _isMovingDown = true;
                _menuMoveTimer = 0;
                PlayCursorMoveSound();
                System.Diagnostics.Debug.WriteLine($"Menu cursor moved down to: {_menuItems[_currentMenuIndex]}");
            }
        }

        private void SelectCurrentMenuItem()
        {
            System.Diagnostics.Debug.WriteLine($"Selected menu item: {_menuItems[_currentMenuIndex]}");

            switch (_currentMenuIndex)
            {
                case 0: // GAME START
                    PlayGameStartSound();
                    System.Diagnostics.Debug.WriteLine("Starting game - transitioning to Song Selection Stage");
                    // Use DTXMania-style fade transition for game start
                    ChangeStage(StageType.SongSelect, new DTXManiaFadeTransition(0.7));
                    break;

                case 1: // CONFIG
                    PlaySelectSound();
                    System.Diagnostics.Debug.WriteLine("Opening config - transitioning to Config Stage");
                    // Use crossfade transition for config
                    ChangeStage(StageType.Config, new CrossfadeTransition(0.5));
                    break;

                case 2: // EXIT
                    PlaySelectSound();
                    System.Diagnostics.Debug.WriteLine("Exiting game");
                    _game.Exit();
                    break;
            }
        }

        private void HandleMouseInput()
        {
            var mousePos = _currentMouseState.Position;
            var previousHoveredIndex = _hoveredMenuIndex;
            _hoveredMenuIndex = -1;

            // Check if mouse is over any menu item
            for (int i = 0; i < _menuItems.Length; i++)
            {
                var menuItemRect = new Rectangle(
                    MenuX,
                    MenuY + (i * MenuItemHeight),
                    MenuItemWidth,
                    MenuItemHeight
                );

                if (menuItemRect.Contains(mousePos))
                {
                    _hoveredMenuIndex = i;

                    // If hover changed, update cursor position and play sound
                    if (_hoveredMenuIndex != previousHoveredIndex && _hoveredMenuIndex != _currentMenuIndex)
                    {
                        _currentMenuIndex = _hoveredMenuIndex;
                        PlayCursorMoveSound();
                        System.Diagnostics.Debug.WriteLine($"Mouse hover changed cursor to: {_menuItems[_currentMenuIndex]}");
                    }
                    break;
                }
            }

            // Handle mouse clicks
            if (IsMouseButtonPressed(MouseButton.Left) && _hoveredMenuIndex >= 0)
            {
                _currentMenuIndex = _hoveredMenuIndex;
                SelectCurrentMenuItem();
            }
        }

        private bool IsMouseButtonPressed(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => _currentMouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                                   _previousMouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Released,
                MouseButton.Right => _currentMouseState.RightButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                                    _previousMouseState.RightButton == Microsoft.Xna.Framework.Input.ButtonState.Released,
                MouseButton.Middle => _currentMouseState.MiddleButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                                     _previousMouseState.MiddleButton == Microsoft.Xna.Framework.Input.ButtonState.Released,
                _ => false
            };
        }

        private void PlayCursorMoveSound()
        {
            try
            {
                _cursorMoveSound?.Play(0.7f); // Play at 70% volume
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to play cursor move sound: {ex.Message}");
            }
        }

        private void PlaySelectSound()
        {
            try
            {
                _selectSound?.Play(0.8f); // Play at 80% volume
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to play select sound: {ex.Message}");
            }
        }

        private void PlayGameStartSound()
        {
            try
            {
                _gameStartSound?.Play(0.9f); // Play at 90% volume
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to play game start sound: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods - Drawing

        private void DrawBackground()
        {
            if (_backgroundTexture != null)
            {
                var viewport = _game.GraphicsDevice.Viewport;
                _spriteBatch.Draw(_backgroundTexture.Texture,
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
            // Use texture-based menu rendering if available, otherwise fallback to rectangles
            if (_menuTexture != null)
            {
                DrawMenuWithTexture();
            }
            else if (_whitePixel != null)
            {
                DrawMenuWithRectangles();
            }
        }

        private void DrawMenuWithTexture()
        {
            if (_menuTexture == null)
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

            // Draw menu items from texture (following DTXMania pattern)
            // Menu texture layout: GAME START (row 0), OPTION (row 1, skipped), CONFIG (row 2), EXIT (row 3)
            DrawMenuItemFromTexture(0, MenuX, baseY + animationOffset, 0); // GAME START
            DrawMenuItemFromTexture(1, MenuX, baseY + MenuItemHeight + animationOffset, 2); // CONFIG (skip OPTION row)
            DrawMenuItemFromTexture(2, MenuX, baseY + (2 * MenuItemHeight) + animationOffset, 3); // EXIT

            // Draw cursor with DTXMania-style effects
            DrawMenuCursor(baseY, animationOffset);
        }

        private void DrawMenuItemFromTexture(int menuIndex, int x, int y, int textureRow)
        {
            if (_menuTexture == null)
                return;

            // Create source rectangle for the menu item from texture
            var sourceRect = new Rectangle(0, textureRow * MenuItemHeight, MenuItemWidth, MenuItemHeight);
            var destRect = new Rectangle(x, y, MenuItemWidth, MenuItemHeight);

            // Draw the menu item
            _spriteBatch.Draw(_menuTexture.Texture, destRect, sourceRect, Color.White);
        }

        private void DrawMenuCursor(int baseY, int animationOffset)
        {
            if (_currentMenuIndex < 0 || _currentMenuIndex >= _menuItems.Length || _menuTexture == null)
                return;

            int cursorY = baseY + (_currentMenuIndex * MenuItemHeight) + animationOffset;

            // DTXMania cursor effect: scaling and transparency animation
            float flashProgress = (float)(_cursorFlashTimer / 0.7); // 0.7 second cycle
            float scaleMagnification = 1.0f + (flashProgress * 0.5f); // Scale up to 1.5x
            int transparency = (int)(255.0f * (1.0f - flashProgress)); // Fade out during flash

            // Calculate scaled position to center the scaled cursor
            int scaledWidth = (int)(MenuItemWidth * scaleMagnification);
            int scaledHeight = (int)(MenuItemHeight * scaleMagnification);
            int scaledX = MenuX + (int)((MenuItemWidth * (1.0f - scaleMagnification)) / 2.0f);
            int scaledY = cursorY + (int)((MenuItemHeight * (1.0f - scaleMagnification)) / 2.0f);

            // Draw cursor background (row 4 in texture) with scaling effect
            var cursorSourceRect = new Rectangle(0, 4 * MenuItemHeight, MenuItemWidth, MenuItemHeight);
            var cursorDestRect = new Rectangle(scaledX, scaledY, scaledWidth, scaledHeight);
            var cursorColor = Color.White * (transparency / 255.0f);

            _spriteBatch.Draw(_menuTexture.Texture, cursorDestRect, cursorSourceRect, cursorColor);

            // Draw normal cursor (row 5 in texture) without scaling
            var normalCursorSourceRect = new Rectangle(0, 5 * MenuItemHeight, MenuItemWidth, MenuItemHeight);
            var normalCursorDestRect = new Rectangle(MenuX, cursorY, MenuItemWidth, MenuItemHeight);

            _spriteBatch.Draw(_menuTexture.Texture, normalCursorDestRect, normalCursorSourceRect, Color.White);
        }

        private void DrawMenuWithRectangles()
        {
            // Fallback rectangle-based rendering (existing implementation)
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
            FadeOut,
            FadeInFromStartup
        }

        private enum MouseButton
        {
            Left,
            Right,
            Middle
        }

        #endregion
    }
}
