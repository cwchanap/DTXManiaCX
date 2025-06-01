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

        // Song management
        private SongManager _songManager;
        private List<SongListNode> _currentSongList;
        private SongListNode _selectedSong;
        private int _selectedIndex = 0;
        private int _currentDifficulty = 0;

        // UI Components
        private UIManager _uiManager;
        private UIList _songList;
        private UILabel _titleLabel;
        private UILabel _statusLabel;
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
            _resourceManager = ResourceManagerFactory.CreateResourceManager(_game.GraphicsDevice);

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
                uiFont = _resourceManager.LoadFont("Arial", 16, FontStyle.Regular);
                System.Diagnostics.Debug.WriteLine("SongSelectionStage: UI font loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Failed to load UI font: {ex.Message}");
            }

            // Initialize song manager
            _songManager = new SongManager();

            // Initialize UI
            InitializeUI(uiFont);

            // Start song loading
            _ = InitializeSongListAsync();

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

            // Clean up graphics resources
            _whitePixel?.Dispose();
            _spriteBatch?.Dispose();

            _currentPhase = StagePhase.Inactive;
            _selectionPhase = SongSelectionPhase.Inactive;

            base.Deactivate();

            System.Diagnostics.Debug.WriteLine("SongSelectionStage: Deactivated");
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

            // Create title label
            _titleLabel = new UILabel("Song Selection")
            {
                Position = new Vector2(50, 30),
                Size = new Vector2(400, 40),
                TextColor = Color.White,
                HasShadow = true,
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Left
            };

            // Create breadcrumb label
            _breadcrumbLabel = new UILabel("")
            {
                Position = new Vector2(50, 80),
                Size = new Vector2(600, 30),
                TextColor = Color.Yellow,
                HasShadow = true,
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Left
            };

            // Create song list
            _songList = new UIList
            {
                Position = new Vector2(50, 120),
                Size = new Vector2(700, 400),
                VisibleItemCount = VISIBLE_SONGS,
                BackgroundColor = Color.Black * 0.5f,
                BackgroundTexture = _whitePixel,
                SelectedItemColor = Color.Blue * 0.8f,
                HoverItemColor = Color.LightBlue * 0.6f,
                TextColor = Color.White,
                SelectedTextColor = Color.Yellow,
                ItemHeight = 30,
                Font = uiFont?.SpriteFont
            };

            System.Diagnostics.Debug.WriteLine($"SongSelectionStage: UIList created with font: {(uiFont?.SpriteFont != null ? "Available" : "Null")}");

            // Create status label
            _statusLabel = new UILabel("Loading songs...")
            {
                Position = new Vector2(50, 540),
                Size = new Vector2(600, 30),
                TextColor = Color.Cyan,
                HasShadow = true,
                HorizontalAlignment = DTX.UI.Components.TextAlignment.Left
            };

            // Wire up events
            _songList.SelectionChanged += OnSongSelectionChanged;
            _songList.ItemActivated += OnSongActivated;

            // Add components to panel
            _mainPanel.AddChild(_titleLabel);
            _mainPanel.AddChild(_breadcrumbLabel);
            _mainPanel.AddChild(_songList);
            _mainPanel.AddChild(_statusLabel);

            // Add panel to UI manager
            _uiManager.AddRootContainer(_mainPanel);
        }

        private async Task InitializeSongListAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("SongSelectionStage: Loading song database...");

                // Load cached song database
                await _songManager.LoadSongsDatabaseAsync("songs.db");

                // Check if enumeration is needed
                if (_songManager.DatabaseScoreCount == 0)
                {
                    System.Diagnostics.Debug.WriteLine("SongSelectionStage: No songs in database, starting enumeration...");
                    
                    var progress = new Progress<EnumerationProgress>(OnEnumerationProgress);
                    await _songManager.EnumerateSongsAsync(new[] { "DTXFiles", "Songs" }, progress);
                }

                // Initialize display with song list
                _currentSongList = _songManager.RootSongs.ToList();
                PopulateSongList();

                _statusLabel.Text = $"Loaded {_songManager.DatabaseScoreCount} songs";

                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Loaded {_songManager.DatabaseScoreCount} songs");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Error loading songs: {ex.Message}");
                _statusLabel.Text = $"Error loading songs: {ex.Message}";
            }
        }

        #endregion

        #region Song List Management

        private void PopulateSongList()
        {
            _songList.ClearItems();

            if (_currentSongList == null || _currentSongList.Count == 0)
            {
                _songList.AddItem("No songs found", null);
                return;
            }

            // Add back navigation if we're in a subfolder
            if (_navigationStack.Count > 0)
            {
                _songList.AddItem(".. (Back)", new SongListNode { Type = NodeType.BackBox, Title = ".." });
            }

            // Add all songs and folders
            foreach (var node in _currentSongList)
            {
                string displayText = GetDisplayText(node);
                _songList.AddItem(displayText, node);
            }

            // Select first item
            if (_songList.Items.Count > 0)
            {
                _songList.SelectedIndex = 0;
                UpdateSelectedSong();
            }
        }

        private string GetDisplayText(SongListNode node)
        {
            switch (node.Type)
            {
                case NodeType.Box:
                    return $"[BOX] {node.DisplayTitle}";
                case NodeType.Score:
                    var metadata = node.Metadata;
                    if (metadata != null)
                    {
                        return $"{metadata.DisplayTitle} - {metadata.DisplayArtist}";
                    }
                    return node.DisplayTitle;
                case NodeType.Random:
                    return "[RANDOM]";
                default:
                    return node.DisplayTitle;
            }
        }

        #endregion

        #region Event Handlers

        private void OnSongSelectionChanged(object sender, ListSelectionChangedEventArgs e)
        {
            UpdateSelectedSong();
        }

        private void OnSongActivated(object sender, ListItemActivatedEventArgs e)
        {
            if (e.Item.Data is SongListNode node)
            {
                HandleSongActivation(node);
            }
        }

        private void OnEnumerationProgress(EnumerationProgress progress)
        {
            _statusLabel.Text = $"Enumerating: {progress.CurrentFile} ({progress.ProcessedCount} processed)";
        }

        private void UpdateSelectedSong()
        {
            if (_songList.SelectedIndex >= 0 && _songList.SelectedIndex < _songList.Items.Count)
            {
                var selectedItem = _songList.Items[_songList.SelectedIndex];
                if (selectedItem.Data is SongListNode node)
                {
                    _selectedSong = node;
                    UpdateBreadcrumb();
                }
            }
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
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Navigated into box: {boxNode.DisplayTitle}");
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
                System.Diagnostics.Debug.WriteLine("SongSelectionStage: Navigated back");
            }
        }

        private void SelectSong(SongListNode songNode)
        {
            System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Selected song: {songNode.DisplayTitle}");

            // TODO: Transition to performance stage with selected song
            // For now, just show selection in status
            _statusLabel.Text = $"Selected: {songNode.DisplayTitle}";
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
            DrawBackground();

            // Draw UI
            _uiManager?.Draw(_spriteBatch, deltaTime);

            // Draw additional info
            DrawSongInfo();

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
                if (_songList.SelectedIndex > 0)
                {
                    _songList.SelectedIndex--;
                    UpdateSelectedSong();
                }
            }
            else if (IsKeyPressed(Keys.Down))
            {
                if (_songList.SelectedIndex < _songList.Items.Count - 1)
                {
                    _songList.SelectedIndex++;
                    UpdateSelectedSong();
                }
            }
        }

        private bool IsKeyPressed(Keys key)
        {
            return _currentKeyboardState.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key);
        }

        private void CycleDifficulty(int direction)
        {
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
                        currentIndex = (currentIndex + direction + availableDifficulties.Count) % availableDifficulties.Count;
                        _currentDifficulty = availableDifficulties[currentIndex];

                        System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Changed difficulty to {_currentDifficulty}");
                    }
                }
            }
        }

        private void DrawBackground()
        {
            var viewport = _game.GraphicsDevice.Viewport;

            // Draw gradient background
            _spriteBatch.Draw(_whitePixel, new Rectangle(0, 0, viewport.Width, viewport.Height),
                Color.DarkBlue * 0.3f);
        }

        private void DrawSongInfo()
        {
            if (_selectedSong?.Type == NodeType.Score && _selectedSong.Metadata != null)
            {
                var metadata = _selectedSong.Metadata;
                int x = 800;
                int y = 150;

                // Draw song info panel background
                _spriteBatch.Draw(_whitePixel, new Rectangle(x - 10, y - 10, 300, 200), Color.Black * 0.7f);

                // Draw song information (simplified - would use proper font rendering in full implementation)
                if (_bitmapFont != null)
                {
                    _bitmapFont.DrawText(_spriteBatch, $"BPM: {metadata.BPM:F0}", x, y);
                    _bitmapFont.DrawText(_spriteBatch, $"Genre: {metadata.Genre}", x, y + 30);

                    if (_selectedSong.Scores?[_currentDifficulty] != null)
                    {
                        var score = _selectedSong.Scores[_currentDifficulty];
                        _bitmapFont.DrawText(_spriteBatch, $"Difficulty: {_currentDifficulty}", x, y + 60);
                        _bitmapFont.DrawText(_spriteBatch, $"Best Score: {score.BestScore}", x, y + 90);
                    }
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
