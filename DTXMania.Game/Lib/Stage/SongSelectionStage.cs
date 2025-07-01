using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game;
using DTX.Resources;
using DTX.UI;
using DTX.UI.Components;
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
        private const double NAVIGATION_DEBOUNCE_SECONDS = 0.01; // 10ms debounce for smooth navigation

        // Constants for DTXMania-style display
        private const int VISIBLE_SONGS = 13;
        private const int CENTER_INDEX = 6;        // RenderTarget management for stage-level resource pooling
        private RenderTarget2D _stageRenderTarget;

        // Preview image functionality
        private ITexture _previewTexture;
        private ITexture _defaultPreviewTexture;
        private double _previewDisplayDelay = 0.0;
        private string _currentPreviewPath = "";
        private const double PREVIEW_DELAY_SECONDS = 0.5; // 500ms delay before showing preview

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
                _bitmapFont = new BitmapFont(_game.GraphicsDevice, _resourceManager);
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
                uiFont = _resourceManager.LoadFont("Arial", 16, FontStyle.Regular);
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

            // Load default preview texture
            LoadDefaultPreviewTexture();

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
                    _songInitializationTask.Wait(TimeSpan.FromMilliseconds(500));
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

            // Clean up preview image textures
            // Don't dispose textures loaded from ResourceManager - it handles disposal
            // Only dispose default texture if we manually created it
            _previewTexture = null;
            _defaultPreviewTexture = null;

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
                return _resourceManager.LoadFont("Arial", 16, FontStyle.Regular);
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
                _backgroundTexture = _resourceManager.LoadTexture("Graphics/5_background.jpg");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load background: {ex.Message}");
            }

            try
            {
                _headerPanelTexture = _resourceManager.LoadTexture("Graphics/5_header panel.png");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load header panel: {ex.Message}");
            }

            try
            {
                _footerPanelTexture = _resourceManager.LoadTexture("Graphics/5_footer panel.png");
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
                BackgroundColor = Color.Black * 0.8f,
                LayoutMode = PanelLayoutMode.Manual
            };

            // Create title label (positioned below header panel)
            _titleLabel = new UILabel("Song Selection")
            {
                Position = new Vector2(50, 100), // Moved down to account for header panel
                Size = new Vector2(400, 40),
                TextColor = Color.White,
                HasShadow = true,
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Left
            };

            // Create breadcrumb label
            _breadcrumbLabel = new UILabel("")
            {
                Position = new Vector2(50, 150), // Moved down to account for header panel
                Size = new Vector2(600, 30),
                TextColor = Color.Yellow,
                HasShadow = true,
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Left
            };

            // Create DTXManiaNX-style song list display
            // Use full screen width to accommodate curved layout coordinates
            _songListDisplay = new SongListDisplay
            {
                Position = new Vector2(0, 0),
                Size = new Vector2(1280, 720), // Full screen for curved layout
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
            // Position at authentic DTXManiaNX coordinates (X:130, Y:350)
            _statusPanel = new SongStatusPanel
            {
                Position = new Vector2(130, 350), // DTXManiaNX authentic position
                Size = new Vector2(580, 320), // DTXManiaNX authentic size for 3×5 difficulty grid
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
                            
                            var songPaths = new[] { "DTXFiles" };
                            await songManager.InitializeAsync(songPaths);
                            
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

            // Always reset preview delay when selection changes
            _previewDisplayDelay = 0.0;

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

                // Load preview image for songs (but only after scroll completes to avoid unnecessary loading)
                if (e.IsScrollComplete)
                {

                    LoadPreviewImage(e.SelectedSong);
                }
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

                // Clear preview immediately when not on a song

                _previewTexture = null;
                _currentPreviewPath = "";

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
            }            // After scrolling completes - update everything
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
            // TODO: Transition to performance stage with selected song
            // For now, just show selection in debug output
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

            // Update preview display delay counter
            if (_previewDisplayDelay < PREVIEW_DELAY_SECONDS)
            {
                _previewDisplayDelay += deltaTime;
            }

            // Check for completed song initialization task
            CheckSongInitializationCompletion();

            // Update input manager
            _inputManager.Update(deltaTime);

            // Update phase
            UpdatePhase(deltaTime);

            // Handle input
            HandleInput();

            // Update UI
            _uiManager?.Update(deltaTime);
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin();

            // Draw background
            DrawBackground();

            // Draw preview image if delay has ended
            DrawPreviewImage();

            // Draw UI
            _uiManager?.Draw(_spriteBatch, deltaTime);

            _spriteBatch.End();
        }

        private void UpdatePhase(double deltaTime)
        {
            double phaseElapsed = _elapsedTime - _phaseStartTime;

            switch (_selectionPhase)
            {
                case SongSelectionPhase.FadeIn:
                    if (phaseElapsed >= 0.5) // 0.5 second fade in
                    {
                        _selectionPhase = SongSelectionPhase.Normal;
                        _currentPhase = StagePhase.Normal;
                        _phaseStartTime = _elapsedTime;
                    }
                    break;

                case SongSelectionPhase.FadeOut:
                    if (phaseElapsed >= 0.5) // 0.5 second fade out
                    {
                        // Transition complete
                    }
                    break;
            }
        }

        private void HandleInput()
        {
            // Process queued input commands from InputManager
            ProcessInputCommands();
        }




        /// <summary>
        /// Process queued input commands from InputManager
        /// </summary>
        private void ProcessInputCommands()
        {
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
                    }
                    else
                    {
                        // Normal song list navigation
                        _songListDisplay.MovePrevious();
                        _lastNavigationTime = _elapsedTime;
                    }
                    break;

                case InputCommandType.MoveDown:
                    if (_isInStatusPanel)
                    {
                        // Navigate within status panel (could be between difficulty rows)
                        // For now, keep it as song navigation to allow changing songs while in panel
                        _songListDisplay.MoveNext();
                        _lastNavigationTime = _elapsedTime;
                    }
                    else
                    {
                        // Normal song list navigation
                        _songListDisplay.MoveNext();
                        _lastNavigationTime = _elapsedTime;
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
                        StageManager?.ChangeStage(StageType.Title, new DTXManiaFadeTransition(0.5));
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

                            // Difficulty changed
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
            for (int y = 0; y < height; y += 4) // Draw every 4th line for performance
            {
                float ratio = (float)y / height;
                var color = Color.Lerp(topColor, bottomColor, ratio);
                var lineRect = new Rectangle(0, y, viewport.Width, 4);
                _spriteBatch.Draw(_whitePixel, lineRect, color);
            }
        }

        /// <summary>
        /// Draw preview image at correct position based on status panel visibility
        /// </summary>
        private void DrawPreviewImage()
        {
            // Don't draw if we don't have a selected song or it's not a song type
            if (_selectedSong?.Type != NodeType.Score)
            {
                return;
            }

            // Only draw if delay has ended and we have a valid preview texture
            if (_previewDisplayDelay < PREVIEW_DELAY_SECONDS || _previewTexture == null || _previewTexture.IsDisposed)
                return;

            try
            {
                // Determine position based on status panel visibility
                Vector2 position;
                Vector2 size;

                if (_statusPanel?.Visible == true)
                {
                    // Position with status panel: (250, 34) size 292x292
                    position = new Vector2(250, 34);
                    size = new Vector2(292, 292);
                }
                else
                {
                    // Position without status panel: (18, 88) size 368x368
                    position = new Vector2(18, 88);
                    size = new Vector2(368, 368);
                }

                // Draw preview image
                var destinationRect = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
                _previewTexture.Draw(_spriteBatch, destinationRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            }
            catch (ObjectDisposedException)
            {
                // Texture was disposed during draw, clear the reference

                _previewTexture = null;
            }
            catch (Exception)
            {

            }
        }

        #endregion

        #region RenderTarget Management        /// <summary>
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

        #endregion

        #region Preview Image Management

        /// <summary>
        /// Load preview image for the selected song
        /// </summary>
        private void LoadPreviewImage(SongListNode songNode)
        {
            // Check if resource manager is still valid
            if (_resourceManager == null)
            {

                return;
            }

            // Clear current preview reference (don't dispose - ResourceManager handles that)
            _previewTexture = null;
            _currentPreviewPath = "";

            // If not a song (folder, back button, etc.), don't load any preview
            if (songNode?.Type != NodeType.Score)
            {

                return;
            }

            try
            {
                // Look for preview image in common locations
                string[] previewExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };
                string[] previewNames = { "preview", "jacket", "banner" };
                
                string songDirectory = null;
                
                // Debug: Log song node details

                
                // Try to get song directory from DatabaseSong
                if (songNode.DatabaseSong?.Charts?.Count > 0)
                {
                    var chartPath = songNode.DatabaseSong.Charts.FirstOrDefault()?.FilePath;
                    if (!string.IsNullOrEmpty(chartPath))
                    {
                        songDirectory = Path.GetDirectoryName(chartPath);

                    }
                    else
                    {

                    }
                }
                else
                {

                }

                // If we couldn't get directory from chart, try the DirectoryPath property
                if (string.IsNullOrEmpty(songDirectory) && !string.IsNullOrEmpty(songNode.DirectoryPath))
                {
                    songDirectory = songNode.DirectoryPath;

                }

                if (string.IsNullOrEmpty(songDirectory))
                {

                    // Use default texture if available and not disposed
                    if (_defaultPreviewTexture != null && !_defaultPreviewTexture.IsDisposed)
                    {
                        _previewTexture = _defaultPreviewTexture;
                    }
                    return;
                }

                // Convert to absolute path if needed - handle both relative and already absolute paths
                if (!Path.IsPathRooted(songDirectory))
                {

                    
                    // Try to resolve relative path from current working directory or known song directories
                    try
                    {
                        var workingDir = Environment.CurrentDirectory;

                        
                        var possiblePaths = new[]
                        {
                            Path.GetFullPath(songDirectory), // From current directory
                            Path.GetFullPath(Path.Combine(workingDir, songDirectory)), // From app directory
                            Path.GetFullPath(Path.Combine(workingDir, "DTXFiles", songDirectory)), // From DTXFiles folder
                            Path.GetFullPath(Path.Combine(workingDir, "Songs", songDirectory)), // From Songs folder
                            Path.GetFullPath(Path.Combine(workingDir, "..", "Songs", songDirectory)), // Parent Songs folder
                            Path.GetFullPath(Path.Combine(workingDir, "..", "DTXFiles", songDirectory)) // Parent DTXFiles folder
                        };

                        foreach (var testPath in possiblePaths)
                        {

                            if (Directory.Exists(testPath))
                            {
                                songDirectory = testPath;

                                break;
                            }
                        }
                    }
                    catch (Exception)
                    {

                        songDirectory = Path.GetFullPath(songDirectory); // Fallback to simple resolution
                    }
                }

                // Verify directory exists before trying to find preview files
                if (!Directory.Exists(songDirectory))
                {

                    if (_defaultPreviewTexture != null && !_defaultPreviewTexture.IsDisposed)
                    {
                        _previewTexture = _defaultPreviewTexture;
                    }
                    return;
                }



                // Try to find preview image
                string previewPath = null;
                foreach (var name in previewNames)
                {
                    foreach (var ext in previewExtensions)
                    {
                        var testPath = Path.Combine(songDirectory, name + ext);

                        if (File.Exists(testPath))
                        {
                            previewPath = testPath;

                            break;
                        }
                    }
                    if (previewPath != null) break;
                }

                if (previewPath != null)
                {
                    // Try to load the texture, with additional error handling
                    try
                    {
                        _previewTexture = _resourceManager.LoadTexture(previewPath);
                        _currentPreviewPath = previewPath;
                        
                        // Verify the loaded texture is valid
                        if (_previewTexture != null && _previewTexture.IsDisposed)
                        {

                            _previewTexture = _defaultPreviewTexture;
                        }
                        else
                        {

                        }
                    }
                    catch (ObjectDisposedException)
                    {

                        _previewTexture = _defaultPreviewTexture;
                    }
                }
                else
                {

                    // Use default texture if available and not disposed
                    if (_defaultPreviewTexture != null && !_defaultPreviewTexture.IsDisposed)
                    {
                        _previewTexture = _defaultPreviewTexture;
                    }
                    else
                    {
                        _previewTexture = null;
                    }
                }
            }
            catch (Exception)
            {

                
                // Try to use default texture as fallback, but check if it's valid first
                if (_defaultPreviewTexture != null && !_defaultPreviewTexture.IsDisposed)
                {
                    _previewTexture = _defaultPreviewTexture;
                }
                else
                {
                    _previewTexture = null;
                }
            }
        }

        /// <summary>
        /// Load default preview texture
        /// </summary>
        private void LoadDefaultPreviewTexture()
        {
            try
            {
                if (_resourceManager != null)
                {
                    _defaultPreviewTexture = _resourceManager.LoadTexture("Graphics/5_default_preview.png");
                    
                    // Verify the loaded texture is valid
                    if (_defaultPreviewTexture != null && _defaultPreviewTexture.IsDisposed)
                    {

                        _defaultPreviewTexture = null;
                    }
                }
                else
                {

                    _defaultPreviewTexture = null;
                }
            }
            catch (Exception)
            {
                // Default texture is optional

                _defaultPreviewTexture = null;
            }
        }

        #endregion

        #region Handle Activate Input        /// <summary>
        /// Handle the Activate input (Enter key) with context-sensitive functionality:
        /// - Navigate into folders/back boxes when in song list mode
        /// - Enter status panel when on a song and not in status panel mode
        /// - Select chart when in status panel mode (placeholder - stays in status panel)
        /// </summary>
        private void HandleActivateInput()
        {
            if (_isInStatusPanel)
            {
                // When in status panel, Enter should select the chart (placeholder for now)
                // Stay in status panel mode - only Escape should exit
                
                // TODO: Implement chart selection and transition to performance stage
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
