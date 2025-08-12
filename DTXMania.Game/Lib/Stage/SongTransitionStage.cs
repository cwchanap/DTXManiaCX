using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Input;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Song transition stage that displays song information before transitioning to performance
    /// Shows song name, artist, difficulty level, and preview image
    /// </summary>
    public class SongTransitionStage : BaseStage
    {
        #region Fields

        private SpriteBatch _spriteBatch;
        private IResourceManager _resourceManager;
        private InputManager _inputManager;
        
        // Song information
        private SongListNode _selectedSong;
        private int _selectedDifficulty;
        private int _songId;
        
        // UI Components
        private UIManager _uiManager;
        private UIPanel _mainPanel;
        
        // Font rendering
        private IFont _titleFont;
        private IFont _artistFont;
        
        // Text content
        private string _songTitle;
        private string _artistName;
        private string _difficultyName;
        
        // Preview image with rotation support
        private ITexture _previewTexture;
        
        // Difficulty sprite
        private ManagedSpriteTexture _difficultySprite;
        
        // Level number bitmap font for difficulty level display
        private BitmapFont _levelNumberFont;
        
        // Background and styling
        private Texture2D _whitePixel;
        private ITexture _backgroundTexture;
        
        // Sound
        private ISound _nowLoadingSound;
        
        // Timing
        private double _elapsedTime;
        
        // Chart data
        private ParsedChart _parsedChart;
        private bool _chartLoaded = false;

        public override StageType Type => StageType.SongTransition;

        #endregion

        #region Constructor

        public SongTransitionStage(BaseGame game) : base(game)
        {
            _inputManager = new InputManager();
        }

        #endregion

        #region Stage Lifecycle

        public override void Activate(Dictionary<string, object> sharedData = null)
        {
            base.Activate(sharedData);
            
            // Get song information from shared data
            if (sharedData != null)
            {
                if (sharedData.TryGetValue("selectedSong", out var songObj) && songObj is SongListNode song)
                {
                    _selectedSong = song;
                }
                
                if (sharedData.TryGetValue("selectedDifficulty", out var difficultyObj) && difficultyObj is int difficulty)
                {
                    _selectedDifficulty = difficulty;
                }
                
                if (sharedData.TryGetValue("songId", out var songIdObj) && songIdObj is int songId)
                {
                    _songId = songId;
                }
            }
            
            // Initialize graphics resources
            _spriteBatch = new SpriteBatch(_game.GraphicsDevice);
            _resourceManager = _game.ResourceManager;
            
            // Ensure InputManager is available (recreate if disposed)
            if (_inputManager == null)
            {
                _inputManager = new InputManager();
            }
            
            // Create white pixel for drawing
            _whitePixel = new Texture2D(_game.GraphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });
            
            // Load background
            LoadBackground();
            
            // Load sound
            LoadSound();
            
            // Initialize UI
            InitializeUI();
            
            // Play the now loading sound
            PlayNowLoadingSound();
            
            // Reset timing and start in normal phase (no fade)
            _elapsedTime = 0;
            _currentPhase = StagePhase.Normal;
        }

        public override void Deactivate()
        {
            // Clean up UI
            _uiManager?.Dispose();
            _uiManager = null;
            
            // Clean up input manager
            _inputManager?.Dispose();
            _inputManager = null;
            
            // Clean up graphics resources
            _whitePixel?.Dispose();
            _whitePixel = null;
            _spriteBatch?.Dispose();
            _spriteBatch = null;
            
            // Clean up background texture
            _backgroundTexture?.Dispose();
            _backgroundTexture = null;
            
            // Clean up preview texture
            _previewTexture?.Dispose();
            _previewTexture = null;
            
            // Clean up difficulty sprite
            _difficultySprite?.Dispose();
            _difficultySprite = null;
            
            // Clean up level number font
            _levelNumberFont?.Dispose();
            _levelNumberFont = null;
            
            // Clean up fonts
            _titleFont?.Dispose();
            _titleFont = null;
            _artistFont?.Dispose();
            _artistFont = null;
            
            // Clean up sounds
            _nowLoadingSound?.Dispose();
            _nowLoadingSound = null;
            
            _currentPhase = StagePhase.Inactive;
            
            base.Deactivate();
        }

        #endregion


        #region Initialization

        private void LoadBackground()
        {
            try
            {
                // Try to load a background texture using layout constants
                _backgroundTexture = _resourceManager.LoadTexture(SongTransitionUILayout.Background.DefaultBackgroundPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load background: {ex.Message}");
                // Continue without background - we'll use fallback
            }
        }

        private void LoadSound()
        {
            try
            {
                // Load now loading sound for song selection
                _nowLoadingSound = _resourceManager.LoadSound("Sounds/Now loading.ogg");
            }
            catch (Exception)
            {
                try
                {
                    // Fallback to decide sound if Now loading.ogg doesn't work
                    _nowLoadingSound = _resourceManager.LoadSound("Sounds/Decide.ogg");
                }
                catch (Exception)
                {
                    _nowLoadingSound = null;
                }
            }
        }

        private void PlayNowLoadingSound()
        {
            try
            {
                if (_nowLoadingSound == null)
                    return;
                
                _nowLoadingSound.Play(0.9f); // Play at 90% volume
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to play now loading sound: {ex.Message}");
            }
        }


        private void InitializeUI()
        {
            _uiManager = new UIManager();
            
            // Create main panel with a visible background
            _mainPanel = new UIPanel
            {
                Position = Vector2.Zero,
                Size = new Vector2(_game.GraphicsDevice.Viewport.Width, _game.GraphicsDevice.Viewport.Height),
                BackgroundColor = Color.DarkBlue * 0.8f, // More visible background
                LayoutMode = PanelLayoutMode.Manual
            };
            
            // Get song information from SongManager if available
            _songTitle = _selectedSong?.DisplayTitle ?? "Unknown Song";
            _artistName = _selectedSong?.DatabaseSong?.Artist ?? "Unknown Artist";
            _difficultyName = GetDifficultyName(_selectedDifficulty);
            
            // Load fonts for text rendering
            LoadFonts();
            
            // Load preview image if available
            LoadPreviewImage();
            
            // Load difficulty sprite
            LoadDifficultySprite();
            
            // Load level number font
            LoadLevelNumberFont();
            
            // Add panel to UI manager
            _uiManager.AddRootContainer(_mainPanel);
            
            // Activate the main panel
            _mainPanel.Activate();
        }

        private void LoadFonts()
        {
            try
            {
                // Load title font using layout configuration
                _titleFont = _resourceManager.LoadFont("NotoSerifJP", SongTransitionUILayout.SongTitle.FontSize);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load title font: {ex.Message}");
            }
            
            try
            {
                // Load artist font using layout configuration
                _artistFont = _resourceManager.LoadFont("NotoSerifJP", SongTransitionUILayout.Artist.FontSize);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load artist font: {ex.Message}");
            }
        }
        
        private void LoadPreviewImage()
        {
            try
            {
                // Try to load preview image from song data - use PreviewImage field for images, not PreviewFile (which is for audio)
                if (_selectedSong?.DatabaseChart?.PreviewImage != null)
                {
                    // Get the chart directory and combine with relative preview image path
                    string chartPath = _selectedSong.DatabaseChart.FilePath;
                    string chartDirectory = System.IO.Path.GetDirectoryName(chartPath);
                    string previewImagePath = System.IO.Path.Combine(chartDirectory, _selectedSong.DatabaseChart.PreviewImage);
                    
                    // Convert to absolute path to avoid ResourceManager's skin-based path resolution
                    string absolutePreviewImagePath = System.IO.Path.GetFullPath(previewImagePath);
                    
                    if (System.IO.File.Exists(absolutePreviewImagePath))
                    {
                        _previewTexture = _resourceManager.LoadTexture(absolutePreviewImagePath);
                    }
                }
                
                // Try fallback preview image if primary not found
                if (_previewTexture == null && _selectedSong != null)
                {
                    // Look for common preview file names in song directory
                    var songDir = _selectedSong.DirectoryPath ?? 
                                 (_selectedSong.DatabaseChart?.FilePath != null ? 
                                  System.IO.Path.GetDirectoryName(_selectedSong.DatabaseChart.FilePath) : null);
                    if (!string.IsNullOrEmpty(songDir))
                    {
                        var fallbackFiles = new[] { "preview.jpg", "preview.png", "jacket.jpg", "jacket.png" };
                        foreach (var fallbackFile in fallbackFiles)
                        {
                            var fallbackPath = System.IO.Path.Combine(songDir, fallbackFile);
                            if (System.IO.File.Exists(fallbackPath))
                            {
                                _previewTexture = _resourceManager.LoadTexture(fallbackPath);
                                break;
                            }
                        }
                    }
                }
                
                // Use default preview image as final fallback
                if (_previewTexture == null)
                {
                    try
                    {
                        _previewTexture = _resourceManager.LoadTexture("Graphics/5_preimage default.png");
                    }
                    catch (Exception fallbackEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load default preview image: {fallbackEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load preview image: {ex.Message}");
            }
        }

        private void LoadDifficultySprite()
        {
            try
            {
                // Load the base texture through ResourceManager first
                var baseTexture = _resourceManager.LoadTexture(TexturePath.DifficultySprite);
                if (baseTexture != null && baseTexture.Texture != null)
                {
                    // Create ManagedSpriteTexture from the loaded texture
                    _difficultySprite = new ManagedSpriteTexture(
                        _game.GraphicsDevice,
                        baseTexture.Texture,
                        TexturePath.DifficultySprite,
                        SongTransitionUILayout.DifficultySprite.SpriteWidth,
                        SongTransitionUILayout.DifficultySprite.SpriteHeight
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load difficulty sprite: {ex.Message}");
            }
        }

        private void LoadLevelNumberFont()
        {
            try
            {
                // Load level number bitmap font using the same configuration as SongStatusPanel
                var levelNumberConfig = BitmapFont.CreateLevelNumberFontConfig();
                _levelNumberFont = new BitmapFont(_game.GraphicsDevice, _resourceManager, levelNumberConfig);
                
                if (_levelNumberFont != null && _levelNumberFont.IsLoaded)
                {
                    System.Diagnostics.Debug.WriteLine("SongTransitionStage: Level number bitmap font loaded successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("SongTransitionStage: Level number bitmap font creation failed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load level number bitmap font: {ex.Message}");
                _levelNumberFont = null;
            }
        }
        

        private string GetDifficultyName(int difficulty)
        {
            return difficulty switch
            {
                0 => "Basic",
                1 => "Advanced",
                2 => "Extreme",
                3 => "Master",
                4 => "Ultimate",
                _ => "Unknown"
            };
        }
        
        private float GetCurrentDifficultyLevel()
        {
            // Get the chart for the current difficulty level
            var chart = _selectedSong.GetCurrentDifficultyChart(_selectedDifficulty);
            if (chart == null)
                return 0;
            
            // Get the appropriate level based on the current instrument
            // For now, assuming drums mode - this could be expanded later
            return chart.DrumLevel > 0 ? chart.DrumLevel : 
                   chart.GuitarLevel > 0 ? chart.GuitarLevel : 
                   chart.BassLevel > 0 ? chart.BassLevel : 0;
        }
        
        

        #endregion

        #region Update and Draw

        protected override void OnUpdate(double deltaTime)
        {
            _elapsedTime += deltaTime;
            
            // Update input manager
            if (_inputManager != null)
            {
                _inputManager.Update(deltaTime);
            }
            
            // Handle input
            HandleInput();
            
            // Update UI
            _uiManager?.Update(deltaTime);
            
            // Auto transition after delay (only if user hasn't pressed anything)
            if (_elapsedTime >= SongTransitionUILayout.Timing.AutoTransitionDelay && _currentPhase == StagePhase.Normal)
            {
                TransitionToPerformance();
            }
            
            // Update phase
            UpdatePhase();
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;
            
            _spriteBatch.Begin();
            
            // Draw background
            DrawBackground();
            
            // Draw UI without fade effects (simpler approach)
            _uiManager?.Draw(_spriteBatch, deltaTime);
            
            // Draw text with ManagedFont
            DrawText();
            
            // Draw difficulty background rectangle
            DrawDifficultyBackground();
            
            // Draw difficulty sprite
            DrawDifficultySprite();
            
            // Draw difficulty level number
            DrawDifficultyLevelNumber();
            
            // Draw preview image with rotation separately
            DrawPreviewImage();
            
            _spriteBatch.End();
        }

        private void DrawBackground()
        {
            var viewport = _game.GraphicsDevice.Viewport;
            
            if (_backgroundTexture != null)
            {
                _backgroundTexture.Draw(_spriteBatch, Vector2.Zero);
            }
            else if (_whitePixel != null)
            {
                // Fallback to a simple gradient background using layout constants
                var topColor = SongTransitionUILayout.Background.GradientTopColor;
                var bottomColor = SongTransitionUILayout.Background.GradientBottomColor;
                
                // Simple vertical gradient using fewer draws for better performance
                for (int y = 0; y < viewport.Height; y += SongTransitionUILayout.Background.GradientLineSpacing)
                {
                    float ratio = (float)y / viewport.Height;
                    var color = Color.Lerp(topColor, bottomColor, ratio);
                    var lineRect = new Rectangle(0, y, viewport.Width, SongTransitionUILayout.Background.GradientLineSpacing);
                    _spriteBatch.Draw(_whitePixel, lineRect, color);
                }
            }
            else
            {
                // Last resort - just clear with a color
                _game.GraphicsDevice.Clear(Color.DarkBlue);
            }
        }

        private void DrawText()
        {
            try
            {
                // Draw song title using layout configuration
                if (_titleFont != null && !string.IsNullOrEmpty(_songTitle))
                {
                    var titlePosition = SongTransitionUILayout.SongTitle.Position;
                    var titleColor = SongTransitionUILayout.SongTitle.TextColor;
                    
                    // Draw with shadow for better visibility
                    _titleFont.DrawStringWithShadow(_spriteBatch, _songTitle, titlePosition, 
                        titleColor, Color.Black * 0.8f, new Vector2(2, 2));
                }
                
                // Draw artist name using layout configuration
                if (_artistFont != null && !string.IsNullOrEmpty(_artistName))
                {
                    var artistPosition = SongTransitionUILayout.Artist.Position;
                    var artistColor = SongTransitionUILayout.Artist.TextColor;
                    var artistText = _artistName;
                    
                    // Draw with shadow for better visibility
                    _artistFont.DrawStringWithShadow(_spriteBatch, artistText, artistPosition,
                        artistColor, Color.Black * 0.8f, new Vector2(1, 1));
                }
                
                // Note: Difficulty is now drawn as sprite in DrawDifficultySprite method
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to draw text: {ex.Message}");
            }
        }
        
        private void DrawDifficultyBackground()
        {
            if (_whitePixel == null)
                return;
            
            try
            {
                // Draw grey background rectangle using layout configuration
                var position = SongTransitionUILayout.DifficultySprite.Position;
                var size = SongTransitionUILayout.DifficultySprite.BackgroundSize;
                var rectangle = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
                var greyColor = Color.Gray;
                
                _spriteBatch.Draw(_whitePixel, rectangle, greyColor);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to draw difficulty background: {ex.Message}");
            }
        }
        
        private void DrawDifficultySprite()
        {
            if (_difficultySprite == null)
                return;
            
            try
            {
                // Get the sprite index for the current difficulty
                var spriteIndex = SongTransitionUILayout.DifficultySprite.GetSpriteIndex(_selectedDifficulty);
                var position = SongTransitionUILayout.DifficultySprite.Position;
                
                // Draw the difficulty sprite at the specified position
                _difficultySprite.DrawSprite(_spriteBatch, spriteIndex, position);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to draw difficulty sprite: {ex.Message}");
            }
        }
        
        private void DrawDifficultyLevelNumber()
        {
            if (_levelNumberFont == null || !_levelNumberFont.IsLoaded)
                return;
            
            try
            {
                // Get the difficulty level from the current song chart
                var difficultyLevel = GetCurrentDifficultyLevel();
                if (difficultyLevel <= 0)
                    return;
                
                // Format the level number similar to SongStatusPanel (divide by 10 for decimal format)
                var levelText = (difficultyLevel / 10.0f).ToString("F2"); // Show 38 as "3.80", 60 as "6.00", etc.
                
                // Use layout configuration for position and color
                var position = SongTransitionUILayout.DifficultyLevelNumber.Position;
                var textColor = SongTransitionUILayout.DifficultyLevelNumber.TextColor;
                
                // Draw the level number using bitmap font
                _levelNumberFont.DrawText(_spriteBatch, levelText, (int)position.X, (int)position.Y, textColor);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to draw difficulty level number: {ex.Message}");
            }
        }
        
        private void DrawPreviewImage()
        {
            if (_previewTexture == null)
                return;
            
            try
            {
                // Use SongTransitionUILayout constants for positioning and rotation
                var position = SongTransitionUILayout.PreviewImage.Position;
                var size = SongTransitionUILayout.PreviewImage.Size;
                var rotation = SongTransitionUILayout.PreviewImage.RotationRadians;
                var origin = SongTransitionUILayout.PreviewImage.Origin;
                var tintColor = SongTransitionUILayout.PreviewImage.TintColor;
                
                // Calculate scale to fit the image within the specified size
                var textureSize = new Vector2(_previewTexture.Width, _previewTexture.Height);
                var targetSize = size;
                var scale = new Vector2(
                    targetSize.X / textureSize.X,
                    targetSize.Y / textureSize.Y
                );
                
                // Use smaller scale to maintain aspect ratio (letterbox/pillarbox)
                var uniformScale = Math.Min(scale.X, scale.Y);
                var finalScale = new Vector2(uniformScale, uniformScale);
                
                // Draw the preview image with absolute positioning
                // Use the layout position directly for consistent placement regardless of image size
                var drawPosition = position;
                
                _previewTexture.Draw(_spriteBatch, drawPosition, finalScale, rotation, Vector2.Zero);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to draw preview image: {ex.Message}");
            }
        }

        private void UpdatePhase()
        {
            switch (_currentPhase)
            {
                case StagePhase.FadeIn:
                    if (_elapsedTime >= SongTransitionUILayout.Timing.FadeInDuration)
                    {
                        _currentPhase = StagePhase.Normal;
                    }
                    break;
                    
                case StagePhase.FadeOut:
                    if (_elapsedTime >= SongTransitionUILayout.Timing.FadeOutDuration)
                    {
                        // Transition complete
                        PerformTransition();
                    }
                    break;
            }
        }

        #endregion

        #region Input Handling

        private void HandleInput()
        {
            if (_inputManager == null)
                return;
                
            var commands = _inputManager.GetInputCommands();
            while (commands.Count > 0)
            {
                var command = commands.Dequeue();
                ExecuteInputCommand(command);
            }
        }

        private void ExecuteInputCommand(InputCommand command)
        {
            switch (command.Type)
            {
                case InputCommandType.Activate:
                    TransitionToPerformance();
                    break;
                    
                case InputCommandType.Back:
                    TransitionBackToSongSelect();
                    break;
            }
        }

        #endregion

        #region Stage Transitions

        private void TransitionToPerformance()
        {
            if (_currentPhase == StagePhase.FadeOut)
                return; // Already transitioning
            
            // Transition immediately without fade
            PerformTransition();
        }

        private void TransitionBackToSongSelect()
        {
            // Go back to song selection immediately
            StageManager?.ChangeStage(StageType.SongSelect, new InstantTransition());
        }

        private void PerformTransition()
        {
            // Create shared data for performance stage
            var sharedData = new Dictionary<string, object>();
            if (_selectedSong != null)
            {
                sharedData["selectedSong"] = _selectedSong;
                sharedData["selectedDifficulty"] = _selectedDifficulty;
                sharedData["songId"] = _songId;
                
                // Pass parsed chart data if available
                if (_chartLoaded && _parsedChart != null)
                {
                    sharedData["parsedChart"] = _parsedChart;
                }
            }
            
            // Transition to performance stage immediately
            StageManager?.ChangeStage(StageType.Performance, new InstantTransition(), sharedData);
        }

        #endregion
    }
}