using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game;
using DTX.Resources;
using DTX.UI;
using DTX.UI.Components;
using DTX.Song;
using DTX.Input;

namespace DTX.Stage
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
        private UILabel _songTitleLabel;
        private UILabel _artistLabel;
        private UILabel _difficultyLabel;
        private UIImage _previewImage;
        
        // Background and styling
        private Texture2D _whitePixel;
        private ITexture _backgroundTexture;
        
        // Sound
        private ISound _nowLoadingSound;
        
        // Timing
        private double _elapsedTime;
        private const double AUTO_TRANSITION_DELAY = 3.0; // Auto transition after 3 seconds
        private const double FADE_IN_DURATION = 0.5;
        private const double FADE_OUT_DURATION = 0.5;
        
        #endregion

        #region Properties

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
            _resourceManager = ResourceManagerFactory.CreateResourceManager(_game.GraphicsDevice);
            
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
            
            // Clean up sound
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
                // Try to load a background texture, but don't fail if it doesn't exist
                _backgroundTexture = _resourceManager.LoadTexture("Graphics/5_background.jpg");
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
                System.Diagnostics.Debug.WriteLine("SongTransitionStage: Successfully loaded now loading sound");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load now loading sound, trying fallback: {ex.Message}");
                try
                {
                    // Fallback to decide sound if Now loading.ogg doesn't work
                    _nowLoadingSound = _resourceManager.LoadSound("Sounds/Decide.ogg");
                    System.Diagnostics.Debug.WriteLine("SongTransitionStage: Loaded fallback sound (Decide.ogg)");
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load fallback sound: {fallbackEx.Message}");
                    _nowLoadingSound = null;
                }
            }
        }

        private void PlayNowLoadingSound()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("SongTransitionStage: PlayNowLoadingSound called");
                if (_nowLoadingSound == null)
                {
                    System.Diagnostics.Debug.WriteLine("SongTransitionStage: WARNING - nowLoadingSound is null, cannot play");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine("SongTransitionStage: Playing now loading sound at 90% volume");
                _nowLoadingSound.Play(0.9f); // Play at 90% volume
                System.Diagnostics.Debug.WriteLine("SongTransitionStage: Now loading sound play command executed");
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
                LayoutMode = DTX.UI.Components.PanelLayoutMode.Manual
            };
            
            // Get song information from SongManager if available
            var songTitle = _selectedSong?.DisplayTitle ?? "Unknown Song";
            var songArtist = _selectedSong?.DatabaseSong?.Artist ?? "Unknown Artist";
            var difficultyName = GetDifficultyName(_selectedDifficulty);
            
            // Create song title label - larger and more visible
            _songTitleLabel = new UILabel(songTitle)
            {
                Position = new Vector2(100, 150),
                Size = new Vector2(600, 80),
                TextColor = Color.White,
                HasShadow = true,
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Center
            };
            
            // Create artist label
            _artistLabel = new UILabel($"by {songArtist}")
            {
                Position = new Vector2(100, 250),
                Size = new Vector2(600, 50),
                TextColor = Color.LightGray,
                HasShadow = true,
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Center
            };
            
            // Create difficulty label
            _difficultyLabel = new UILabel($"Difficulty: {difficultyName}")
            {
                Position = new Vector2(100, 320),
                Size = new Vector2(600, 50),
                TextColor = Color.Yellow,
                HasShadow = true,
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Center
            };
            
            // Create preview image
            _previewImage = new UIImage
            {
                Position = new Vector2(300, 400),
                Size = new Vector2(200, 200),
                TintColor = Color.White // Use white tint for visibility
            };
            
            // Load preview image if available
            LoadPreviewImage();
            
            // Add components to panel
            _mainPanel.AddChild(_songTitleLabel);
            _mainPanel.AddChild(_artistLabel);
            _mainPanel.AddChild(_difficultyLabel);
            _mainPanel.AddChild(_previewImage);
            
            // Add panel to UI manager
            _uiManager.AddRootContainer(_mainPanel);
            
            // Activate the main panel
            _mainPanel.Activate();
            
            System.Diagnostics.Debug.WriteLine($"SongTransitionStage: UI initialized with song: {songTitle}");
        }

        private void LoadPreviewImage()
        {
            try
            {
                // Try to load preview image from song data
                if (_selectedSong?.DatabaseChart?.PreviewFile != null)
                {
                    var previewPath = _selectedSong.DatabaseChart.PreviewFile;
                    if (System.IO.File.Exists(previewPath))
                    {
                        var previewTexture = _resourceManager.LoadTexture(previewPath);
                        _previewImage.Texture = previewTexture.Texture;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongTransitionStage: Failed to load preview image: {ex.Message}");
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
            if (_elapsedTime >= AUTO_TRANSITION_DELAY && _currentPhase == StagePhase.Normal)
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
                // Fallback to a simple gradient background
                var topColor = Color.DarkBlue;
                var bottomColor = Color.Black;
                
                // Simple vertical gradient using fewer draws for better performance
                for (int y = 0; y < viewport.Height; y += 8)
                {
                    float ratio = (float)y / viewport.Height;
                    var color = Color.Lerp(topColor, bottomColor, ratio);
                    var lineRect = new Rectangle(0, y, viewport.Width, 8);
                    _spriteBatch.Draw(_whitePixel, lineRect, color);
                }
            }
            else
            {
                // Last resort - just clear with a color
                _game.GraphicsDevice.Clear(Color.DarkBlue);
            }
        }

        private float GetFadeAlpha()
        {
            return _currentPhase switch
            {
                StagePhase.FadeIn => Math.Min(1.0f, (float)(_elapsedTime / FADE_IN_DURATION)),
                StagePhase.FadeOut => Math.Max(0.0f, 1.0f - (float)(_elapsedTime / FADE_OUT_DURATION)),
                _ => 1.0f
            };
        }

        private void UpdatePhase()
        {
            switch (_currentPhase)
            {
                case StagePhase.FadeIn:
                    if (_elapsedTime >= FADE_IN_DURATION)
                    {
                        _currentPhase = StagePhase.Normal;
                    }
                    break;
                    
                case StagePhase.FadeOut:
                    if (_elapsedTime >= FADE_OUT_DURATION)
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
            }
            
            // Transition to performance stage immediately
            StageManager?.ChangeStage(StageType.Performance, new InstantTransition(), sharedData);
        }

        #endregion
    }
}