using System;
using System.Collections.Generic;
using System.Linq;
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

        // UI Components - Enhanced DTXManiaNX style
        private UIManager _uiManager;
        private SongListDisplay _songListDisplay;
        private SongStatusPanel _statusPanel;
        private PreviewImagePanel _previewImagePanel;
        private UILabel _titleLabel;
        private UILabel _breadcrumbLabel;
        private UIPanel _mainPanel;

        // Input tracking (DTXMania pattern)
        private KeyboardState _currentKeyboardState;
        private KeyboardState _previousKeyboardState;

        // Keyboard repeat detection system
        private readonly Dictionary<Keys, KeyRepeatState> _keyRepeatStates;
        private readonly Queue<InputCommand> _inputCommandQueue;
        private IConfigManager _configManager;

        // Navigation state
        private Stack<SongListNode> _navigationStack;
        private string _currentBreadcrumb = "";

        // DTXMania pattern: timing and animation
        private double _elapsedTime;
        private SongSelectionPhase _selectionPhase = SongSelectionPhase.FadeIn;
        private double _phaseStartTime;

        // Performance optimization: Input debouncing
        private double _lastNavigationTime = 0;
        private const double NAVIGATION_DEBOUNCE_SECONDS = 0.01; // 10ms debounce for smooth navigation

        // Performance optimization: Selection change debouncing
        private double _lastSelectionUpdateTime = 0;
        private const double SELECTION_UPDATE_DEBOUNCE_SECONDS = 0.016; // ~60fps update rate        // Constants for DTXMania-style display
        private const int VISIBLE_SONGS = 13;
        private const int CENTER_INDEX = 6;        // RenderTarget management for stage-level resource pooling
        private RenderTarget2D _stageRenderTarget;

        #endregion

        #region Properties

        public override StageType Type => StageType.SongSelect;

        #endregion

        #region Constructor

        public SongSelectionStage(BaseGame game) : base(game)
        {
            _navigationStack = new Stack<SongListNode>();
            _keyRepeatStates = new Dictionary<Keys, KeyRepeatState>();
            _inputCommandQueue = new Queue<InputCommand>();
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
                if (ResourceManagerFactory.HasFontFactory)
                {
                    uiFont = _resourceManager.LoadFont("Arial", 16, FontStyle.Regular);
                }
                else
                {
                    // Create a minimal fallback font using MonoGame's built-in capabilities
                    uiFont = CreateFallbackFont();
                }
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
            LoadBackgroundGraphics();            // Initialize UI
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
            // Clean up UI
            _uiManager?.Dispose();

            // Clean up DTXManiaNX background graphics (Phase 3)
            _backgroundTexture?.Dispose();
            _headerPanelTexture?.Dispose();
            _footerPanelTexture?.Dispose();

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

            // Ensure status panel is visible and properly configured
            _statusPanel.Visible = true;

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

                // Initialize display with song list
                _currentSongList = [.. songManager.RootSongs];
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Loaded {_currentSongList.Count} songs");
                PopulateSongList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Error loading songs: {ex.Message}");
            }
        }

        #endregion

        #region Song List Management

        private void PopulateSongList()
        {
            var displayList = new List<SongListNode>();

            if (_currentSongList == null || _currentSongList.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("SongSelectionStage: No songs to display");
                _songListDisplay.CurrentList = displayList;
                return;
            }

            // Add back navigation if we're in a subfolder
            if (_navigationStack.Count > 0)
            {
                displayList.Add(new SongListNode { Type = NodeType.BackBox, Title = ".." });
            }

            // Add all songs and folders
            displayList.AddRange(_currentSongList);

            // Update the song list display
            System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Populating display with {displayList.Count} items");
            _songListDisplay.CurrentList = displayList;        }

        #endregion

        #region Event Handlers

        private void OnSongSelectionChanged(object sender, SongSelectionChangedEventArgs e)
        {
            _selectedSong = e.SelectedSong;
            _currentDifficulty = e.CurrentDifficulty;

            // DTXManiaNX-style navigation debouncing
            if (!e.IsScrollComplete)
            {
                // During scrolling - only update lightweight UI
                UpdateBreadcrumb();
                return; // Skip heavy updates
            }

            // After scrolling completes - update everything
            // Performance optimization: Debounce rapid selection changes
            var currentTime = _elapsedTime;
            if (currentTime - _lastSelectionUpdateTime < SELECTION_UPDATE_DEBOUNCE_SECONDS)
            {
                return; // Skip update if too soon after last update
            }
            _lastSelectionUpdateTime = currentTime;

            // Update status panel (now cached for performance)
            _statusPanel?.UpdateSongInfo(e.SelectedSong, e.CurrentDifficulty);

            // Update preview image panel asynchronously (already optimized)
            _previewImagePanel?.UpdateSelectedSong(e.SelectedSong);

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

        private void OnEnumerationProgress(EnumerationProgress progress)
        {
            // Progress tracking for song enumeration
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

            // Update input state
            _previousKeyboardState = _currentKeyboardState;
            _currentKeyboardState = Keyboard.GetState();

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
            DrawBackground();            // Draw UI
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
            // Update key repeat states and generate input commands
            UpdateKeyRepeatStates();

            // Process queued input commands
            ProcessInputCommands();
        }        /// <summary>
        /// Update key repeat states for continuous input detection
        /// </summary>
        private void UpdateKeyRepeatStates()
        {
            // Define keys we want to track for repeat
            var trackedKeys = new[]
            {
                Keys.Up, Keys.Down, Keys.Left, Keys.Right,
                Keys.Enter, Keys.Escape
            };

            foreach (var key in trackedKeys)
            {
                if (!_keyRepeatStates.ContainsKey(key))
                {
                    _keyRepeatStates[key] = new KeyRepeatState();
                }

                var state = _keyRepeatStates[key];
                bool isCurrentlyPressed = _currentKeyboardState.IsKeyDown(key);
                bool wasPressed = _previousKeyboardState.IsKeyDown(key);

                if (isCurrentlyPressed && !wasPressed)
                {
                    // Key just pressed - initial press
                    state.IsPressed = true;
                    state.InitialPressTime = _elapsedTime;
                    state.LastRepeatTime = _elapsedTime;
                    state.CurrentRepeatInterval = 200 / 1000.0; // Default: 200ms initial delay
                    state.HasStartedRepeating = false;

                    // Queue initial command
                    QueueInputCommand(GetCommandTypeForKey(key), _elapsedTime, false);
                }
                else if (isCurrentlyPressed && wasPressed)
                {
                    // Key held down - check for repeat
                    double timeSinceInitialPress = _elapsedTime - state.InitialPressTime;
                    double timeSinceLastRepeat = _elapsedTime - state.LastRepeatTime;

                    if (timeSinceLastRepeat >= state.CurrentRepeatInterval)
                    {
                        // Time for a repeat
                        state.LastRepeatTime = _elapsedTime;
                        state.HasStartedRepeating = true;

                        // Calculate accelerated repeat interval
                        double accelerationProgress = Math.Min(1.0, timeSinceInitialPress / (1000 / 1000.0)); // Default: 1000ms acceleration
                        state.CurrentRepeatInterval = MathHelper.Lerp(
                            200 / 1000.0f, // Default: 200ms initial delay
                            50 / 1000.0f,  // Default: 50ms final delay
                            (float)accelerationProgress);

                        // Queue repeat command
                        QueueInputCommand(GetCommandTypeForKey(key), _elapsedTime, true);
                    }
                }
                else if (!isCurrentlyPressed && wasPressed)
                {
                    // Key released
                    state.Reset();
                }
            }
        }

        /// <summary>
        /// Get the input command type for a given key
        /// </summary>
        private InputCommandType GetCommandTypeForKey(Keys key)
        {
            return key switch
            {
                Keys.Up => InputCommandType.MoveUp,
                Keys.Down => InputCommandType.MoveDown,
                Keys.Left => InputCommandType.MoveLeft,
                Keys.Right => InputCommandType.MoveRight,
                Keys.Enter => InputCommandType.Activate,
                Keys.Escape => InputCommandType.Back,
                _ => InputCommandType.Activate
            };
        }

        /// <summary>
        /// Queue an input command for processing
        /// </summary>
        private void QueueInputCommand(InputCommandType commandType, double timestamp, bool isRepeat)
        {
            _inputCommandQueue.Enqueue(new InputCommand(commandType, timestamp, isRepeat));
        }

        /// <summary>
        /// Process queued input commands
        /// </summary>
        private void ProcessInputCommands()
        {
            while (_inputCommandQueue.Count > 0)
            {
                var command = _inputCommandQueue.Dequeue();
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
                    _songListDisplay.MovePrevious();
                    _lastNavigationTime = _elapsedTime;
                    break;

                case InputCommandType.MoveDown:
                    _songListDisplay.MoveNext();
                    _lastNavigationTime = _elapsedTime;
                    break;

                case InputCommandType.MoveLeft:
                    CycleDifficulty(-1);
                    break;

                case InputCommandType.MoveRight:
                    CycleDifficulty(1);
                    break;

                case InputCommandType.Activate:
                    if (_selectedSong != null)
                    {
                        HandleSongActivation(_selectedSong);
                    }
                    break;

                case InputCommandType.Back:
                    if (_navigationStack.Count > 0)
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
            }        }

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

    #region Input Command System

    /// <summary>
    /// Represents an input command that can be queued and processed
    /// </summary>
    public enum InputCommandType
    {
        MoveUp,
        MoveDown,
        MoveLeft,
        MoveRight,
        Activate,
        Back
    }

    /// <summary>
    /// Input command with timestamp for processing
    /// </summary>
    public struct InputCommand
    {
        public InputCommandType Type { get; set; }
        public double Timestamp { get; set; }
        public bool IsRepeat { get; set; }

        public InputCommand(InputCommandType type, double timestamp, bool isRepeat = false)
        {
            Type = type;
            Timestamp = timestamp;
            IsRepeat = isRepeat;
        }
    }

    /// <summary>
    /// Tracks the repeat state of a key for continuous input
    /// </summary>
    public class KeyRepeatState
    {
        public bool IsPressed { get; set; }
        public double InitialPressTime { get; set; }
        public double LastRepeatTime { get; set; }
        public double CurrentRepeatInterval { get; set; }
        public bool HasStartedRepeating { get; set; }

        public KeyRepeatState()
        {
            Reset();
        }

        public void Reset()
        {
            IsPressed = false;
            InitialPressTime = 0;
            LastRepeatTime = 0;
            CurrentRepeatInterval = 0;
            HasStartedRepeating = false;
        }
    }

    #endregion
}
