using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game;
using DTX.Resources;
using DTX.UI;
using DTX.Song.Components;
using DTX.UI.Components;
using DTX.UI.Layout;
using DTX.Song;
using DTX.Input;
using DTX.Config;

namespace DTX.Stage
{
    /// <summary>
    /// Song selection stage implementation based on DTXManiaNX CStage選曲
    /// Handles song list display, navigation, and selection with BOX folder support
    /// </summary>
    public class SongSelectionStage : BaseStage
    {
        #region Fields

        private SpriteBatch _spriteBatch;
        private IResourceManager _resourceManager;
        private BitmapFont _bitmapFont;
        private Texture2D _whitePixel;

        // DTXManiaNX Background Graphics (Phase 3)
        private ITexture _backgroundTexture;
        private ITexture _headerPanelTexture;
        private ITexture _footerPanelTexture;        // Song management
        private List<SongListNode> _currentSongList;
        private SongListNode _selectedSong;
        private int _selectedIndex = 0;
        private int _currentDifficulty = 0;

        // Async initialization management
        private Task<List<SongListNode>> _songInitializationTask;
        private bool _songInitializationProcessed = false;
        private CancellationTokenSource _cancellationTokenSource;

        // UI Components - Enhanced DTXManiaNX style
        private UIManager _uiManager;
        private SongListDisplay _songListDisplay;
        private SongStatusPanel _statusPanel;
        private PreviewImagePanel _previewImagePanel;
        private UILabel _titleLabel;
        private UILabel _breadcrumbLabel;
        private UIPanel _mainPanel;

        // Input tracking using InputManager
        private InputManager _inputManager;
        private IConfigManager _configManager;

        // Navigation state
        private Stack<SongListNode> _navigationStack;
        private string _currentBreadcrumb = "";

        // Status panel navigation state
        private bool _isInStatusPanel = false;
        
        // DTXMania pattern: timing and animation
        private double _elapsedTime;
        private SongSelectionPhase _selectionPhase = SongSelectionPhase.FadeIn;
        private double _phaseStartTime;        // Performance optimization: Input debouncing
        private double _lastNavigationTime = 0;

        // Constants for DTXMania-style display - using values from SongSelectionUILayout
        private const int VISIBLE_SONGS = SongSelectionUILayout.SongBars.VisibleItems;
        private const int CENTER_INDEX = SongSelectionUILayout.SongBars.CenterIndex;        // RenderTarget management for stage-level resource pooling
        private RenderTarget2D _stageRenderTarget;

        // Preview sound functionality
        private ISound _previewSound;
        private SoundEffectInstance _previewSoundInstance;
        private ISound _backgroundMusic; // Reference to BGM
        private SoundEffectInstance _backgroundMusicInstance;
        
        // Navigation sound functionality (same as TitleStage)
        private ISound _cursorMoveSound;
        private ISound _gameStartSound;
        private double _previewPlayDelay = 0.0;
        private double _bgmFadeOutTimer = 0.0;
        private double _bgmFadeInTimer = 0.0;
        private bool _isPreviewDelayActive = false;
        private bool _isBgmFadingOut = false;
        private bool _isBgmFadingIn = false;
        
        // Preview sound timing constants
        private const double PREVIEW_PLAY_DELAY_SECONDS = SongSelectionUILayout.Audio.PreviewPlayDelaySeconds;
        private const double BGM_FADE_OUT_DURATION = SongSelectionUILayout.Audio.BgmFadeOutDuration;
        private const double BGM_FADE_IN_DURATION = SongSelectionUILayout.Audio.BgmFadeInDuration;

        #endregion

        #region Properties

        public override StageType Type => StageType.SongSelect;

        #endregion

        #region Constructor

        public SongSelectionStage(BaseGame game) : base(game)
        {
            _navigationStack = new Stack<SongListNode>();
            _inputManager = new InputManager();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        #endregion

        #region Stage Lifecycle

        public override void Activate(Dictionary<string, object> sharedData = null)
        {
            base.Activate(sharedData);

            // Recreate disposed resources
            _inputManager = new InputManager();
            _cancellationTokenSource = new CancellationTokenSource();

            // Get config manager from game
            if (_game is BaseGame baseGame)
            {
                _configManager = baseGame.ConfigManager;
            }

            // Initialize graphics resources
            _spriteBatch = new SpriteBatch(_game.GraphicsDevice);
            _resourceManager = ResourceManagerFactory.CreateResourceManager(_game.GraphicsDevice);

            // Create white pixel for drawing
            _whitePixel = new Texture2D(_game.GraphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Load fonts
            try
            {
                var consoleFontConfig = BitmapFont.CreateConsoleFontConfig();
                _bitmapFont = new BitmapFont(_game.GraphicsDevice, _resourceManager, consoleFontConfig);
            }
            catch (Exception ex)
            {
                // Only log critical errors
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load BitmapFont: {ex.Message}");
            }

            // Try to load a SpriteFont for UI components
            IFont uiFont = null;
            try
            {
                uiFont = _resourceManager.LoadFont(SongSelectionUILayout.Background.DefaultFontName, 
                    SongSelectionUILayout.Background.DefaultFontSize, FontStyle.Regular);
            }
            catch (Exception ex)
            {
                // Only log critical errors
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load UI font: {ex.Message}");

                // Create a simple fallback font for testing
                try
                {
                    uiFont = CreateFallbackFont();
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Fallback font creation failed: {fallbackEx.Message}");
                }
            }            // Load DTXManiaNX background graphics (Phase 3)
            LoadBackgroundGraphics();

            // Load navigation sound (same as TitleStage)
            LoadNavigationSound();

            // Initialize UI
            InitializeUI(uiFont);

            // Start song loading
            InitializeSongList();

            // Initialize stage RenderTargets for shared use
            InitializeStageRenderTargets();

            _currentPhase = StagePhase.FadeIn;
            _selectionPhase = SongSelectionPhase.FadeIn;
            _phaseStartTime = 0;
            _elapsedTime = 0;
        }

        public override void Deactivate()
        {
            // Cancel any running song initialization task
            _cancellationTokenSource?.Cancel();

            // Wait for task completion with timeout to avoid hanging
            if (_songInitializationTask != null && !_songInitializationTask.IsCompleted)
            {
                try
                {
                    _songInitializationTask.Wait(TimeSpan.FromMilliseconds(SongSelectionUILayout.Timing.TaskTimeoutMilliseconds));
                }
                catch (AggregateException)
                {
                    // Task was cancelled or failed, which is expected
                }
            }

            // Clean up task resources
            _songInitializationTask?.Dispose();
            _songInitializationTask = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            // Clean up UI
            _uiManager?.Dispose();

            // Clean up input manager
            _inputManager?.Dispose();
            _inputManager = null;

            // Clean up DTXManiaNX background graphics (Phase 3)
            _backgroundTexture?.Dispose();
            _headerPanelTexture?.Dispose();
            _footerPanelTexture?.Dispose();

            // Clean up preview sound resources
            StopCurrentPreview();
            _backgroundMusicInstance?.Dispose();
            _backgroundMusicInstance = null;
            _backgroundMusic = null;

            // Clean up navigation sound (same as TitleStage)
            _cursorMoveSound?.Dispose();
            _cursorMoveSound = null;
            
            // Clean up game start sound
            _gameStartSound?.Dispose();
            _gameStartSound = null;

            // Clean up graphics resources
            _whitePixel?.Dispose();
            _spriteBatch?.Dispose();

            // Clean up stage RenderTargets
            CleanupStageRenderTargets();

            _currentPhase = StagePhase.Inactive;
            _selectionPhase = SongSelectionPhase.Inactive;

            base.Deactivate();
        }

        #endregion

        #region Font Management

        /// <summary>
        /// Create a fallback font when the font factory is not available
        /// </summary>
        private IFont CreateFallbackFont()
        {
            try
            {
                // Try to create a simple fallback using the resource manager's fallback mechanism
                return _resourceManager.LoadFont(SongSelectionUILayout.Background.DefaultFontName, 
                    SongSelectionUILayout.Background.DefaultFontSize, FontStyle.Regular);
            }
            catch
            {
                // If that fails, return null - the UI components will handle this gracefully
                return null;
            }
        }

        #endregion

        #region Background Graphics Loading (Phase 3)

        private void LoadBackgroundGraphics()
        {
            try
            {
                // Load DTXManiaNX song selection background graphics
                _backgroundTexture = _resourceManager.LoadTexture(TexturePath.SongSelectionBackground);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load background: {ex.Message}");
            }

            try
            {
                _headerPanelTexture = _resourceManager.LoadTexture(TexturePath.SongSelectionHeaderPanel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load header panel: {ex.Message}");
            }

            try
            {
                _footerPanelTexture = _resourceManager.LoadTexture(TexturePath.SongSelectionFooterPanel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load footer panel: {ex.Message}");
            }
        }

        #endregion

        #region Initialization

        private void InitializeUI(IFont uiFont)
        {
            _uiManager = new UIManager();

            // Create main panel
            _mainPanel = new UIPanel
            {
                Position = Vector2.Zero,
                Size = new Vector2(_game.GraphicsDevice.Viewport.Width, _game.GraphicsDevice.Viewport.Height),
                BackgroundColor = Color.Black * SongSelectionUILayout.Background.MainPanelAlpha,
                LayoutMode = PanelLayoutMode.Manual
            };

            // Create title label (positioned below header panel)
            _titleLabel = new UILabel("Song Selection")
            {
                Position = SongSelectionUILayout.UILabels.Title.Position,
                Size = SongSelectionUILayout.UILabels.Title.Size,
                TextColor = Color.White,
                HasShadow = true,
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Left
            };

            // Create breadcrumb label
            _breadcrumbLabel = new UILabel("")
            {
                Position = SongSelectionUILayout.UILabels.Breadcrumb.Position,
                Size = SongSelectionUILayout.UILabels.Breadcrumb.Size,
                TextColor = Color.Yellow,
                HasShadow = true,
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Left
            };

            // Create DTXManiaNX-style song list display
            // Use full screen width to accommodate curved layout coordinates
            _songListDisplay = new SongListDisplay
            {
                Position = SongSelectionUILayout.SongListDisplay.Position,
                Size = SongSelectionUILayout.SongListDisplay.Size,
                Font = uiFont?.SpriteFont,
                ManagedFont = uiFont,
                WhitePixel = _whitePixel
            };            // Initialize Phase 2 enhanced rendering
            try
            {
                _songListDisplay.InitializeEnhancedRendering(_game.GraphicsDevice, _resourceManager,
                    _stageRenderTarget);
                _songListDisplay.SetEnhancedRendering(true); // Explicitly enable enhanced rendering
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to initialize enhanced rendering: {ex.Message}");
                _songListDisplay.SetEnhancedRendering(false);
            }

            // Create DTXManiaNX-style status panel
            // Position using centralized layout constants
            _statusPanel = new SongStatusPanel
            {
                Position = SongSelectionUILayout.StatusPanel.Position, // Use centralized layout constants
                Size = SongSelectionUILayout.StatusPanel.Size, // Use centralized layout constants
                Font = uiFont?.SpriteFont,
                SmallFont = uiFont?.SpriteFont, // Use same font for now
                ManagedFont = uiFont,
                ManagedSmallFont = uiFont, // Use same font for now
                WhitePixel = _whitePixel
            };

            // Create DTXManiaNX-style preview image panel
            // Position at authentic DTXManiaNX coordinates (X:250, Y:34 with status panel)
            _previewImagePanel = new PreviewImagePanel
            {
                HasStatusPanel = true, // We have a status panel, so use right-side positioning
                WhitePixel = _whitePixel
            };            // Initialize graphics generator for status panel
            _statusPanel.InitializeGraphicsGenerator(_game.GraphicsDevice, _stageRenderTarget);

            // Initialize DTXManiaNX authentic graphics for status panel (Phase 3)
            _statusPanel.InitializeAuthenticGraphics(_resourceManager);

            // Status panel starts hidden and will be shown when a song is selected
            _statusPanel.Visible = false;

            // Initialize preview image panel
            try
            {
                _previewImagePanel.Initialize(_resourceManager);
                _previewImagePanel.Visible = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to initialize preview image panel: {ex.Message}");
            }

            // Wire up events
            _songListDisplay.SelectionChanged += OnSongSelectionChanged;
            _songListDisplay.SongActivated += OnSongActivated;
            _songListDisplay.DifficultyChanged += OnDifficultyChanged;

            // Add components to panel
            _mainPanel.AddChild(_titleLabel);
            _mainPanel.AddChild(_breadcrumbLabel);
            _mainPanel.AddChild(_songListDisplay);
            _mainPanel.AddChild(_statusPanel);
            _mainPanel.AddChild(_previewImagePanel);

            // Add panel to UI manager
            _uiManager.AddRootContainer(_mainPanel);

            // Activate the main panel and all its children
            _mainPanel.Activate();
        }

        private void InitializeSongList()
        {
            try
            {
                var songManager = SongManager.Instance;

                // Check if SongManager is properly initialized
                if (!songManager.IsInitialized)
                {
                    // Start background task to initialize SongManager and fetch song list
                    // This task only fetches data and doesn't modify shared state or UI
                    _songInitializationTask = Task.Run(async () =>
                    {
                        try
                        {
                            // Check for cancellation before starting
                            _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                            
                            // SongManager should already be initialized from StartupStage
                            // Just check if it's initialized and return the song list
                            if (!songManager.IsInitialized)
                            {
                                Debug.WriteLine("SongSelectionStage: SongManager not initialized from StartupStage - this should not happen");
                            }
                            
                            // Check for cancellation after initialization
                            _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                            
                            // Return the song list without modifying shared state
                            return new List<SongListNode>(songManager.RootSongs);
                        }
                        catch (OperationCanceledException)
                        {
                            return new List<SongListNode>();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to initialize SongManager: {ex.Message}");
                            return new List<SongListNode>();
                        }
                    }, _cancellationTokenSource.Token);

                    // For now, initialize with empty list
                    _currentSongList = new List<SongListNode>();
                }
                else
                {
                    // Initialize display with song list
                    _currentSongList = [.. songManager.RootSongs];
                }

                PopulateSongList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Error loading songs: {ex.Message}");

                // Fallback to empty list
                _currentSongList = new List<SongListNode>();
                PopulateSongList();
            }
        }

        #endregion

        #region Song List Management

        private void PopulateSongList()
        {
            var displayList = new List<SongListNode>();

            if (_currentSongList == null || _currentSongList.Count == 0)
            {
                _songListDisplay.CurrentList = displayList;
                return;
            }

            // Add back navigation if we're in a subfolder
            if (_navigationStack.Count > 0)
            {
                displayList.Add(new SongListNode { Type = NodeType.BackBox, Title = ".." });
            }

            // Add all songs and folders
            displayList.AddRange(_currentSongList);            // Update the song list display
            _songListDisplay.CurrentList = displayList;
        }

        private void CheckSongInitializationCompletion()
        {
            // Check if we have a running task and it's completed
            if (_songInitializationTask != null && _songInitializationTask.IsCompleted && !_songInitializationProcessed)
            {
                _songInitializationProcessed = true;

                try
                {
                    // Get the result from the completed task
                    var songList = _songInitializationTask.Result;

                    // Update the song list on the main thread
                    _currentSongList = songList;
                    PopulateSongList();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Error processing completed song initialization: {ex.Message}");
                    _currentSongList = new List<SongListNode>();
                }
                finally
                {
                    // Clean up the task reference
                    _songInitializationTask?.Dispose();
                    _songInitializationTask = null;
                }
            }
        }

        #endregion

        #region Event Handlers

        private void OnSongSelectionChanged(object sender, SongSelectionChangedEventArgs e)
        {
            
            _selectedSong = e.SelectedSong;
            _currentDifficulty = e.CurrentDifficulty;

            // Handle preview sound on selection change
            StopCurrentPreview();

            // Auto-manage status panel visibility and navigation mode based on selected item type
            if (e.SelectedSong != null && e.SelectedSong.Type == NodeType.Score)
            {
                // Show status panel when on a song (to display song information)
                if (_statusPanel != null)
                {
                    _statusPanel.Visible = true;
                }

                // Update preview image panel immediately for songs so it knows about the new song
                _previewImagePanel?.UpdateSelectedSong(e.SelectedSong);

                // Start preview sound loading - load immediately but delay playback
                bool isScrolling = _songListDisplay?.IsScrolling ?? false;
                
                // Always load the preview sound - the delay timer will handle when to play it
                LoadPreviewSound(e.SelectedSong);
            }
            else
            {
                // Hide status panel and exit navigation mode when not on a song
                if (_isInStatusPanel)
                {
                    _isInStatusPanel = false;
                }
                if (_statusPanel != null)
                {
                    _statusPanel.Visible = false;
                }

                // Update preview image panel immediately for non-songs so it clears the preview
                _previewImagePanel?.UpdateSelectedSong(e.SelectedSong);
            }

            // DTXManiaNX-style navigation debouncing
            if (!e.IsScrollComplete)
            {
                // During scrolling - only update lightweight UI
                UpdateBreadcrumb();
                
                // Always update status panel content even during scrolling (critical for song display)
                if (e.SelectedSong != null && e.SelectedSong.Type == NodeType.Score)
                {
                    _statusPanel?.UpdateSongInfo(e.SelectedSong, e.CurrentDifficulty);
                }
                
                return; // Skip other heavy updates during scrolling
            }

            // After scrolling completes - update everything
            // Process all selection changes immediately (debounce removed per senior engineer feedback)

            // Force immediate visual update
            _songListDisplay?.InvalidateVisuals();

            // Update status panel (now cached for performance)
            _statusPanel?.UpdateSongInfo(e.SelectedSong, e.CurrentDifficulty);

            // Update breadcrumb (lightweight operation)
            UpdateBreadcrumb();
        }

        private void OnSongActivated(object sender, SongActivatedEventArgs e)
        {
            if (e.Song != null)
            {
                HandleSongActivation(e.Song);
            }
        }

        private void OnDifficultyChanged(object sender, DifficultyChangedEventArgs e)
        {
            _currentDifficulty = e.NewDifficulty;

            // Update status panel
            _statusPanel.UpdateSongInfo(e.Song, e.NewDifficulty);
        }

        private void HandleSongActivation(SongListNode node)
        {
            switch (node.Type)
            {
                case NodeType.BackBox:
                    NavigateBack();
                    break;

                case NodeType.Box:
                    NavigateIntoBox(node);
                    break;

                case NodeType.Score:
                    SelectSong(node);
                    break;

                case NodeType.Random:
                    SelectRandomSong();
                    break;
            }
        }

        private void NavigateIntoBox(SongListNode boxNode)
        {
            if (boxNode.Children != null && boxNode.Children.Count > 0)
            {
                // Push current state onto navigation stack
                _navigationStack.Push(new SongListNode
                {
                    Children = _currentSongList,
                    Title = _currentBreadcrumb
                });

                // Navigate into the box
                _currentSongList = boxNode.Children;
                _currentBreadcrumb = string.IsNullOrEmpty(_currentBreadcrumb)
                    ? boxNode.DisplayTitle
                    : $"{_currentBreadcrumb} > {boxNode.DisplayTitle}";

                PopulateSongList();
            }
        }

        private void NavigateBack()
        {
            if (_navigationStack.Count > 0)
            {
                var previousState = _navigationStack.Pop();
                _currentSongList = previousState.Children;
                _currentBreadcrumb = previousState.Title ?? "";

                PopulateSongList();
            }
        }

        private void SelectSong(SongListNode songNode)
        {
            if (songNode == null || songNode.Type != NodeType.Score)
                return;
            
            // Create shared data to pass song information to the transition stage
            var sharedData = new Dictionary<string, object>
            {
                ["selectedSong"] = songNode,
                ["selectedDifficulty"] = _currentDifficulty,
                ["songId"] = songNode.DatabaseSongId ?? 0
            };
            
            // Transition immediately to SongTransitionStage
            StageManager?.ChangeStage(StageType.SongTransition, new InstantTransition(), sharedData);
        }

        private void SelectRandomSong()
        {
            if (_currentSongList != null && _currentSongList.Count > 0)
            {
                var random = new Random();
                var songNodes = _currentSongList.FindAll(n => n.Type == NodeType.Score);

                if (songNodes.Count > 0)
                {
                    var randomSong = songNodes[random.Next(songNodes.Count)];
                    SelectSong(randomSong);
                }
            }
        }

        private void UpdateBreadcrumb()
        {
            _breadcrumbLabel.Text = string.IsNullOrEmpty(_currentBreadcrumb)
                ? "Root"
                : _currentBreadcrumb;
        }

        #endregion

        #region Update and Draw

        protected override void OnUpdate(double deltaTime)
        {
            _elapsedTime += deltaTime;

            // Update preview sound timers
            UpdatePreviewSoundTimers(deltaTime);

            // Check for completed song initialization task
            CheckSongInitializationCompletion();

            // Update input manager
            _inputManager?.Update(deltaTime);

            // Update phase
            UpdatePhase(deltaTime);

            // Handle input
            HandleInput();

            // Update UI (PreviewImagePanel will handle its own timing)
            _uiManager?.Update(deltaTime);
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin();

            // Draw background
            DrawBackground();

            // Draw UI (PreviewImagePanel will handle its own drawing including delay)
            _uiManager?.Draw(_spriteBatch, deltaTime);

            _spriteBatch.End();
        }

        private void UpdatePhase(double deltaTime)
        {
            double phaseElapsed = _elapsedTime - _phaseStartTime;

            switch (_selectionPhase)
            {
                case SongSelectionPhase.FadeIn:
                    if (phaseElapsed >= SongSelectionUILayout.Timing.FadeInDuration)
                    {
                        _selectionPhase = SongSelectionPhase.Normal;
                        _currentPhase = StagePhase.Normal;
                        _phaseStartTime = _elapsedTime;
                    }
                    break;

                case SongSelectionPhase.FadeOut:
                    if (phaseElapsed >= SongSelectionUILayout.Timing.FadeOutDuration)
                    {
                        // Transition complete
                    }
                    break;
            }
        }

        private void HandleInput()
        {
            // Don't process input if input manager is disposed
            if (_inputManager == null)
                return;
                
            // Process queued input commands from InputManager
            ProcessInputCommands();
        }




        /// <summary>
        /// Process queued input commands from InputManager
        /// </summary>
        private void ProcessInputCommands()
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

        /// <summary>
        /// Execute a specific input command
        /// </summary>
        private void ExecuteInputCommand(InputCommand command)
        {
            switch (command.Type)
            {
                case InputCommandType.MoveUp:
                    if (_isInStatusPanel)
                    {
                        // Navigate within status panel (could be between difficulty rows)
                        // For now, keep it as song navigation to allow changing songs while in panel
                        _songListDisplay.MovePrevious();
                        _lastNavigationTime = _elapsedTime;
                        PlayCursorMoveSound(); // Play navigation sound
                    }
                    else
                    {
                        // Normal song list navigation
                        _songListDisplay.MovePrevious();
                        _lastNavigationTime = _elapsedTime;
                        PlayCursorMoveSound(); // Play navigation sound
                    }
                    break;

                case InputCommandType.MoveDown:
                    if (_isInStatusPanel)
                    {
                        // Navigate within status panel (could be between difficulty rows)
                        // For now, keep it as song navigation to allow changing songs while in panel
                        _songListDisplay.MoveNext();
                        _lastNavigationTime = _elapsedTime;
                        PlayCursorMoveSound(); // Play navigation sound
                    }
                    else
                    {
                        // Normal song list navigation
                        _songListDisplay.MoveNext();
                        _lastNavigationTime = _elapsedTime;
                        PlayCursorMoveSound(); // Play navigation sound
                    }
                    break;
                case InputCommandType.MoveLeft:
                    if (_isInStatusPanel)
                    {
                        // Navigate in status panel (e.g., between difficulties)
                        CycleDifficulty(-1);
                    }
                    // Note: Normal left navigation removed - use Activate (Enter) instead
                    break;
                case InputCommandType.MoveRight:
                    if (_isInStatusPanel)
                    {
                        // Navigate in status panel (e.g., between difficulties)
                        CycleDifficulty(1);
                    }
                    // Note: Normal right navigation removed - use Activate (Enter) instead
                    break;

                case InputCommandType.Activate:
                    HandleActivateInput();
                    break;

                case InputCommandType.Back:
                    if (_isInStatusPanel)
                    {
                        // Exit status panel mode
                        _isInStatusPanel = false;
                    }
                    else if (_navigationStack.Count > 0)
                    {
                        NavigateBack();
                    }
                    else
                    {
                        // Return to title stage
                        StageManager?.ChangeStage(StageType.Title, new DTXManiaFadeTransition(SongSelectionUILayout.Timing.TransitionDuration));
                    }
                    break;
            }
        }

        private void CycleDifficulty(int direction)
        {
            if (direction > 0)
            {
                // Use the SongListDisplay's built-in difficulty cycling
                _songListDisplay.CycleDifficulty();
                PlayCursorMoveSound(); // Play navigation sound for difficulty change
            }
            else
            {
                // For backward cycling, implement manually
                if (_selectedSong?.Type == NodeType.Score && _selectedSong.Scores != null)
                {
                    // Find available difficulties
                    var availableDifficulties = new List<int>();
                    for (int i = 0; i < _selectedSong.Scores.Length; i++)
                    {
                        if (_selectedSong.Scores[i] != null)
                        {
                            availableDifficulties.Add(i);
                        }
                    }

                    if (availableDifficulties.Count > 1)
                    {
                        int currentIndex = availableDifficulties.IndexOf(_currentDifficulty);
                        if (currentIndex >= 0)
                        {
                            currentIndex = (currentIndex - 1 + availableDifficulties.Count) % availableDifficulties.Count;
                            _currentDifficulty = availableDifficulties[currentIndex];

                            // Update the display's difficulty
                            _songListDisplay.CurrentDifficulty = _currentDifficulty;

                            // Update status panel
                            _statusPanel.UpdateSongInfo(_selectedSong, _currentDifficulty);

                            // Difficulty changed - play navigation sound
                            PlayCursorMoveSound();
                        }
                    }
                }
            }
        }

        private void DrawBackground()
        {
            var viewport = _game.GraphicsDevice.Viewport;

            // Draw DTXManiaNX authentic background graphics (Phase 3)
            DrawDTXManiaNXBackground(viewport);
        }

        private void DrawDTXManiaNXBackground(Viewport viewport)
        {
            // Draw main background (5_background.jpg)
            if (_backgroundTexture != null)
            {
                _backgroundTexture.Draw(_spriteBatch, Vector2.Zero);
            }
            else
            {
                // Fallback to gradient if background texture failed to load
                DrawGradientBackground(viewport);
            }

            // Draw header panel (5_header panel.png) at top
            if (_headerPanelTexture != null)
            {
                _headerPanelTexture.Draw(_spriteBatch, Vector2.Zero);
            }

            // Draw footer panel (5_footer panel.png) at bottom
            if (_footerPanelTexture != null)
            {
                // Position footer panel at bottom of screen
                var footerY = viewport.Height - _footerPanelTexture.Height;
                _footerPanelTexture.Draw(_spriteBatch, new Vector2(0, footerY));
            }
        }

        private void DrawGradientBackground(Viewport viewport)
        {
            var topColor = DTXManiaVisualTheme.SongSelection.BackgroundGradientTop;
            var bottomColor = DTXManiaVisualTheme.SongSelection.BackgroundGradientBottom;

            // Simple vertical gradient using multiple horizontal lines
            int height = viewport.Height;
            for (int y = 0; y < height; y += SongSelectionUILayout.Background.GradientLineSpacing)
            {
                float ratio = (float)y / height;
                var color = Color.Lerp(topColor, bottomColor, ratio);
                var lineRect = new Rectangle(0, y, viewport.Width, SongSelectionUILayout.Background.GradientLineSpacing);
                _spriteBatch.Draw(_whitePixel, lineRect, color);
            }
        }

        /// <summary>
        #endregion

        #region RenderTarget Management

        /// <summary>
        /// Initialize stage-level RenderTarget for shared use by UI components
        /// </summary>
        private void InitializeStageRenderTargets()
        {
            // Create a single RenderTarget for all stage operations using RenderTargetManager
            // Size should be large enough to accommodate all UI components
            if (_game is BaseGame baseGame)
            {
                _stageRenderTarget = baseGame.GraphicsManager.RenderTargetManager
                    .GetOrCreateRenderTarget("SongSelectionStage_Main", 1024, 1024);
            }
            else
            {
                // Fallback for non-BaseGame instances (shouldn't happen in normal operation)
                _stageRenderTarget = new RenderTarget2D(_game.GraphicsDevice, 1024, 1024);
            }
        }        /// <summary>
                 /// Cleanup stage RenderTarget
                 /// </summary>
        private void CleanupStageRenderTargets()
        {
            if (_game is BaseGame baseGame)
            {
                // Use RenderTargetManager to properly dispose the RenderTarget
                baseGame.GraphicsManager.RenderTargetManager.RemoveRenderTarget("SongSelectionStage_Main");
            }
            else
            {
                // Fallback cleanup for non-BaseGame instances
                _stageRenderTarget?.Dispose();
            }
            _stageRenderTarget = null;
        }

        /// <summary>
        /// Load navigation sound for song list movement (same as TitleStage)
        /// </summary>
        private void LoadNavigationSound()
        {
            try
            {
                // Load DTXMania-style cursor move sound (same as TitleStage)
                _cursorMoveSound = _resourceManager.LoadSound("Sounds/Move.ogg");
            }
            catch (Exception ex)
            {
                _cursorMoveSound = null;
            }
            
            try
            {
                // Load now loading sound for song selection
                _gameStartSound = _resourceManager.LoadSound("Sounds/Now loading.ogg");
            }
            catch (Exception ex)
            {
                try
                {
                    // Fallback to decide sound if Now loading.ogg doesn't work
                    _gameStartSound = _resourceManager.LoadSound("Sounds/Decide.ogg");
                }
                catch (Exception fallbackEx)
                {
                    _gameStartSound = null;
                }
            }
        }

        #endregion

        #region Preview Sound Management

        /// <summary>
        /// Update all preview sound timers and handle sound transitions
        /// </summary>
        private void UpdatePreviewSoundTimers(double deltaTime)
        {
            // Update preview play delay timer
            if (_isPreviewDelayActive)
            {
                _previewPlayDelay += deltaTime;
                
                // Start preview after delay ends
                if (_previewPlayDelay >= PREVIEW_PLAY_DELAY_SECONDS && _previewSound != null)
                {
                    _isPreviewDelayActive = false;
                    
                    try
                    {
                        // Check if preview sound instance is available and not already playing
                        if (_previewSoundInstance == null || _previewSoundInstance.State != SoundState.Playing)
                        {
                            _previewSoundInstance = _previewSound.CreateInstance();
                            if (_previewSoundInstance != null)
                            {
                                _previewSoundInstance.Volume = SongSelectionUILayout.Audio.PreviewSoundVolume;
                                _previewSoundInstance.IsLooped = true; // Enable looping
                                _previewSoundInstance.Play();
                            }
                            
                            if (_previewSoundInstance != null)
                            {
                                StartBGMFade(true); // Fade out BGM
                            }
                        }
                    }
                    catch (Exception)
                    {
                        _previewSoundInstance = null;
                    }
                }
            }

            // Update BGM fade out timer
            if (_isBgmFadingOut)
            {
                _bgmFadeOutTimer += deltaTime;
                if (_bgmFadeOutTimer >= BGM_FADE_OUT_DURATION)
                {
                    _isBgmFadingOut = false;
                    _bgmFadeOutTimer = BGM_FADE_OUT_DURATION; // Clamp to duration
                }
                
                // Apply volume fade
                if (_backgroundMusicInstance != null && _backgroundMusicInstance.State == SoundState.Playing)
                {
                try
                {
                    float progress = (float)(_bgmFadeOutTimer / BGM_FADE_OUT_DURATION);
                    _backgroundMusicInstance.Volume = SongSelectionUILayout.Audio.BgmMaxVolume - (progress * SongSelectionUILayout.Audio.BgmFadeRange); // Fade to 10%
                }
                catch (Exception ex)
                {
                    // BGM fade failed, continue
                }
                }
            }

            // Update BGM fade in timer
            if (_isBgmFadingIn)
            {
                _bgmFadeInTimer += deltaTime;
                if (_bgmFadeInTimer >= BGM_FADE_IN_DURATION)
                {
                    _isBgmFadingIn = false;
                    _bgmFadeInTimer = BGM_FADE_IN_DURATION; // Clamp to duration
                }
                
                // Apply volume fade
                if (_backgroundMusicInstance != null && _backgroundMusicInstance.State == SoundState.Playing)
                {
                try
                {
                    float progress = (float)(_bgmFadeInTimer / BGM_FADE_IN_DURATION);
                    _backgroundMusicInstance.Volume = SongSelectionUILayout.Audio.BgmMinVolume + (progress * SongSelectionUILayout.Audio.BgmFadeRange); // Fade back to 100%
                }
                catch (Exception ex)
                {
                    // BGM fade failed, continue
                }
                }
            }
        }

        /// <summary>
        /// Load preview sound for the selected song
        /// </summary>
        /// <summary>
        /// Load preview sound for the selected song using direct ResourceManager loading
        /// MP3 files are now supported via FFMpegCore integration in ManagedSound
        /// </summary>
        private void LoadPreviewSound(SongListNode selectedNode)
        {
            if (selectedNode?.DatabaseChart?.PreviewFile == null || string.IsNullOrEmpty(selectedNode.DatabaseChart.PreviewFile))
            {
                return;
            }

            try
            {
                string chartPath = selectedNode.DatabaseChart.FilePath;
                string chartDirectory = Path.GetDirectoryName(chartPath);
                string previewPath = Path.Combine(chartDirectory, selectedNode.DatabaseChart.PreviewFile);
                
                // Convert to absolute path to avoid ResourceManager's skin-based path resolution
                string absolutePreviewPath = Path.GetFullPath(previewPath);
                
                if (File.Exists(absolutePreviewPath))
                {
                    TryLoadPreviewSoundFile(absolutePreviewPath);
                }
            }
            catch (Exception)
            {
                // Clear the failed sound reference
                _previewSound = null;
                _isPreviewDelayActive = false;
            }
        }

        private bool TryLoadPreviewSoundFile(string filePath)
        {
            bool loadFailed = false;
            string currentLoadPath = null;

            // Subscribe to ResourceLoadFailed event temporarily to detect load failures
            EventHandler<ResourceLoadFailedEventArgs> loadFailedHandler = (sender, args) =>
            {
                if (args.Path == currentLoadPath)
                {
                    loadFailed = true;
                }
            };

            try
            {
                currentLoadPath = filePath;
                _resourceManager.ResourceLoadFailed += loadFailedHandler;
                
                _previewSound = _resourceManager.LoadSound(filePath);
                
                // Check if load failed (detected by event) or if sound is null
                if (loadFailed)
                {
                    _previewSound = null;
                    return false;
                }
                
                if (_previewSound == null)
                {
                    return false;
                }
                
                // Successfully loaded
                _previewPlayDelay = 0.0;
                _isPreviewDelayActive = true;
                return true;
            }
            catch (Exception)
            {
                _previewSound = null;
                return false;
            }
            finally
            {
                // Always unsubscribe from the event
                _resourceManager.ResourceLoadFailed -= loadFailedHandler;
            }
        }

        /// <summary>
        /// Stop current preview sound and restore BGM
        /// </summary>
        private void StopCurrentPreview()
        {
            // Stop preview sound instance
            if (_previewSoundInstance != null)
            {
                try
                {
                    if (_previewSoundInstance.State == SoundState.Playing)
                    {
                        _previewSoundInstance.Stop();
                    }
                    _previewSoundInstance.Dispose();
                }
                catch (Exception)
                {
                    // Ignore disposal errors
                }
                finally
                {
                    _previewSoundInstance = null;
                }
            }

            // Clear preview sound (ManagedSound handles disposal)
            _previewSound = null;
            
            // Reset timers
            _previewPlayDelay = 0.0;
            _isPreviewDelayActive = false;
            
            // Start BGM fade in
            StartBGMFade(false);
        }

        /// <summary>
        /// Start BGM fade transition
        /// </summary>
        private void StartBGMFade(bool fadeOut)
        {
            if (fadeOut)
            {
                _bgmFadeOutTimer = 0.0;
                _isBgmFadingOut = true;
                _isBgmFadingIn = false;
            }
            else
            {
                _bgmFadeInTimer = 0.0;
                _isBgmFadingIn = true;
                _isBgmFadingOut = false;
            }
        }

        /// <summary>
        /// Play cursor move sound for navigation (same as TitleStage)
        /// </summary>
        private void PlayCursorMoveSound()
        {
            try
            {
                _cursorMoveSound?.Play(SongSelectionUILayout.Audio.NavigationSoundVolume);
            }
            catch (Exception ex)
            {
                // Cursor sound failed, continue
            }
        }
        
        /// <summary>
        /// Play game start sound when selecting a song (same as TitleStage)
        /// </summary>
        private void PlayGameStartSound()
        {
            try
            {
                _gameStartSound?.Play(0.9f); // Play at 90% volume (same as TitleStage)
            }
            catch (Exception ex)
            {
                // Game start sound failed, continue
            }
        }

        #endregion

        #region Handle Activate Input

        /// <summary>
        /// Handle the Activate input (Enter key) with context-sensitive functionality:
        /// - Navigate into folders/back boxes when in song list mode
        /// - Enter status panel when on a song and not in status panel mode
        /// - Select chart when in status panel mode (placeholder - stays in status panel)
        /// </summary>
        private void HandleActivateInput()
        {
            if (_isInStatusPanel)
            {
                // When in status panel, Enter should select the chart and transition to song transition stage
                if (_selectedSong != null && _selectedSong.Type == NodeType.Score)
                {
                    SelectSong(_selectedSong);
                }
            }
            else if (_selectedSong != null)
            {                // Check if this is a song (Score type) - if so, enter status panel
                if (_selectedSong.Type == NodeType.Score)
                {
                    // Enter status panel mode (no difficulty cycling)
                    _isInStatusPanel = true;
                    if (_statusPanel != null)
                    {
                        _statusPanel.Visible = true;
                    }
                }
                else
                {
                    // For non-songs (folders, back boxes, etc.), handle normal activation
                    HandleSongActivation(_selectedSong);
                }
            }
        }

        #endregion

        #region Set Background Music Reference

        /// <summary>
        /// Set the background music instance for volume control during preview
        /// </summary>
        public void SetBackgroundMusic(ISound backgroundMusic, SoundEffectInstance backgroundMusicInstance)
        {
            _backgroundMusic = backgroundMusic;
            _backgroundMusicInstance = backgroundMusicInstance;
        }

        #endregion

    }

    #region Enums

    /// <summary>
    /// Song selection stage phases
    /// </summary>
    public enum SongSelectionPhase
    {
        Inactive,
        FadeIn,
        Normal,
        FadeOut
    }

    #endregion

}
