using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Shared.Game;
using DTX.Resources;
using DTX.UI;
using DTX.UI.Components;
using DTX.Song;
using DTX.Input;

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

        // Navigation state
        private Stack<SongListNode> _navigationStack;
        private string _currentBreadcrumb = "";

        // DTXMania pattern: timing and animation
        private double _elapsedTime;
        private SongSelectionPhase _selectionPhase = SongSelectionPhase.FadeIn;
        private double _phaseStartTime;

        // Constants for DTXMania-style display
        private const int VISIBLE_SONGS = 13;
        private const int CENTER_INDEX = 6;

        #endregion

        #region Properties

        public override StageType Type => StageType.SongSelect;

        #endregion

        #region Constructor

        public SongSelectionStage(BaseGame game) : base(game)
        {
            _navigationStack = new Stack<SongListNode>();
        }

        #endregion

        #region Stage Lifecycle

        public override void Activate(Dictionary<string, object> sharedData = null)
        {
            base.Activate(sharedData);

            System.Diagnostics.Debug.WriteLine("SongSelectionStage: Activating...");

            // Initialize graphics resources
            _spriteBatch = new SpriteBatch(_game.GraphicsDevice);
            System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Creating ResourceManager, HasFontFactory: {ResourceManagerFactory.HasFontFactory}");
            _resourceManager = ResourceManagerFactory.CreateResourceManager(_game.GraphicsDevice);
            System.Diagnostics.Debug.WriteLine("SongSelectionStage: ResourceManager created successfully");

            // Create white pixel for drawing
            _whitePixel = new Texture2D(_game.GraphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Load fonts
            try
            {
                _bitmapFont = new BitmapFont(_game.GraphicsDevice, _resourceManager);
                System.Diagnostics.Debug.WriteLine("SongSelectionStage: BitmapFont loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load BitmapFont: {ex.Message}");
            }

            // Try to load a SpriteFont for UI components
            IFont uiFont = null;
            try
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: ResourceManagerFactory has font factory: {ResourceManagerFactory.HasFontFactory}");

                if (ResourceManagerFactory.HasFontFactory)
                {
                    uiFont = _resourceManager.LoadFont("Arial", 16, FontStyle.Regular);
                    System.Diagnostics.Debug.WriteLine("SongSelectionStage: UI font loaded successfully");
                    System.Diagnostics.Debug.WriteLine($"SongSelectionStage: UI font SpriteFont: {(uiFont?.SpriteFont != null ? "Available" : "Null")}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("SongSelectionStage: No font factory available - creating fallback font");
                    // Create a minimal fallback font using MonoGame's built-in capabilities
                    uiFont = CreateFallbackFont();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load UI font: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Exception details: {ex}");

                // Create a simple fallback font for testing
                try
                {
                    System.Diagnostics.Debug.WriteLine("SongSelectionStage: Attempting to create fallback font...");
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

            _currentPhase = StagePhase.FadeIn;
            _selectionPhase = SongSelectionPhase.FadeIn;
            _phaseStartTime = 0;
            _elapsedTime = 0;

            System.Diagnostics.Debug.WriteLine("SongSelectionStage: Activated");
        }

        public override void Deactivate()
        {
            System.Diagnostics.Debug.WriteLine("SongSelectionStage: Deactivating...");

            // Clean up UI
            _uiManager?.Dispose();

            // Clean up DTXManiaNX background graphics (Phase 3)
            _backgroundTexture?.Dispose();
            _headerPanelTexture?.Dispose();
            _footerPanelTexture?.Dispose();

            // Clean up graphics resources
            _whitePixel?.Dispose();
            _spriteBatch?.Dispose();

            _currentPhase = StagePhase.Inactive;
            _selectionPhase = SongSelectionPhase.Inactive;

            base.Deactivate();

            System.Diagnostics.Debug.WriteLine("SongSelectionStage: Deactivated");
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
                System.Diagnostics.Debug.WriteLine("SongSelectionStage: All fallback font creation attempts failed");
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
                System.Diagnostics.Debug.WriteLine("SongSelectionStage: Loaded 5_background.jpg");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load background: {ex.Message}");
            }

            try
            {
                _headerPanelTexture = _resourceManager.LoadTexture("Graphics/5_header panel.png");
                System.Diagnostics.Debug.WriteLine("SongSelectionStage: Loaded 5_header panel.png");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load header panel: {ex.Message}");
            }

            try
            {
                _footerPanelTexture = _resourceManager.LoadTexture("Graphics/5_footer panel.png");
                System.Diagnostics.Debug.WriteLine("SongSelectionStage: Loaded 5_footer panel.png");
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
            };

            System.Diagnostics.Debug.WriteLine($"SongSelectionStage: SongListDisplay created with SpriteFont: {(uiFont?.SpriteFont != null ? "Available" : "Null")}, ManagedFont: {(uiFont != null ? "Available" : "Null")}");

            // Initialize Phase 2 enhanced rendering
            try
            {
                _songListDisplay.InitializeEnhancedRendering(_game.GraphicsDevice, _resourceManager);
                _songListDisplay.SetEnhancedRendering(true); // Explicitly enable enhanced rendering
                System.Diagnostics.Debug.WriteLine("SongSelectionStage: Enhanced rendering initialized and enabled successfully");

                // Ensure font is set on the renderer after initialization
                if (uiFont?.SpriteFont != null)
                {
                    System.Diagnostics.Debug.WriteLine("SongSelectionStage: Setting font on enhanced renderer");
                }
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
            };

            System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Status panel configured with Font: {(uiFont?.SpriteFont != null ? "Available" : "Null")}, ManagedFont: {(uiFont != null ? "Available" : "Null")}");

            // Initialize graphics generator for status panel
            _statusPanel.InitializeGraphicsGenerator(_game.GraphicsDevice);

            // Initialize DTXManiaNX authentic graphics for status panel (Phase 3)
            _statusPanel.InitializeAuthenticGraphics(_resourceManager);

            // Ensure status panel is visible and properly configured
            _statusPanel.Visible = true;
            System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Status panel configured - Visible: {_statusPanel.Visible}, Position: {_statusPanel.Position}, Size: {_statusPanel.Size}");

            // Initialize preview image panel
            try
            {
                _previewImagePanel.Initialize(_resourceManager);
                _previewImagePanel.Visible = true;
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Preview image panel configured - Position: {_previewImagePanel.Position}, Size: {_previewImagePanel.Size}");
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

            System.Diagnostics.Debug.WriteLine($"SongSelectionStage: UI initialization complete. Status panel in main panel: {_mainPanel.Children.Contains(_statusPanel)}");
        }        private void InitializeSongList()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("SongSelectionStage: Accessing SongManager singleton...");

                var songManager = SongManager.Instance;

                // Check if already initialized
                if (songManager.IsInitialized)
                {
                    System.Diagnostics.Debug.WriteLine("SongSelectionStage: SongManager already initialized, using existing data");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("SongSelectionStage: SongManager not initialized - this should have been done in StartupStage");
                    // Note: In the singleton pattern, initialization should be done in StartupStage
                    // If we reach here, it means StartupStage didn't properly initialize
                }

                // Initialize display with song list
                _currentSongList = songManager.RootSongs.ToList();
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
            _songListDisplay.CurrentList = displayList;
        }

        #endregion

        #region Event Handlers

        private void OnSongSelectionChanged(object sender, SongSelectionChangedEventArgs e)
        {
            _selectedSong = e.SelectedSong;
            _currentDifficulty = e.CurrentDifficulty;
            UpdateBreadcrumb();

            // Update status panel
            _statusPanel.UpdateSongInfo(e.SelectedSong, e.CurrentDifficulty);

            // Update preview image panel (DTXManiaNX t選択曲が変更された equivalent)
            _previewImagePanel.UpdateSelectedSong(e.SelectedSong);
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
            // Handle escape key - return to title
            if (IsKeyPressed(Keys.Escape))
            {
                if (_navigationStack.Count > 0)
                {
                    NavigateBack();
                }
                else
                {
                    // Return to title stage
                    StageManager?.ChangeStage(StageType.Title, new DTXManiaFadeTransition(0.5));
                }
            }

            // Handle enter key - activate selected item
            if (IsKeyPressed(Keys.Enter))
            {
                if (_selectedSong != null)
                {
                    HandleSongActivation(_selectedSong);
                }
            }

            // Handle difficulty change (left/right arrows)
            if (IsKeyPressed(Keys.Left))
            {
                CycleDifficulty(-1);
            }
            else if (IsKeyPressed(Keys.Right))
            {
                CycleDifficulty(1);
            }

            // Handle song list navigation (up/down arrows)
            if (IsKeyPressed(Keys.Up))
            {
                _songListDisplay.MovePrevious();
            }
            else if (IsKeyPressed(Keys.Down))
            {
                _songListDisplay.MoveNext();
            }
        }

        private bool IsKeyPressed(Keys key)
        {
            return _currentKeyboardState.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key);
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
