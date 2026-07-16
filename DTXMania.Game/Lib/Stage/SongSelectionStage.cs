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
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Song selection stage implementation based on DTXManiaNX CStage選曲
    /// Handles song list display, navigation, and selection with BOX folder support
    /// </summary>
    public class SongSelectionStage : BaseStage, IStageTelemetryProvider
    {
        #region Fields

        private SpriteBatch _spriteBatch;
        private IResourceManager _resourceManager;
        private IFont _font;
        private Texture2D _whitePixel;

        // DTXManiaNX Background Graphics (Phase 3) - Main background now handled by BaseStage
        private ITexture _headerPanelTexture;
        private ITexture _footerPanelTexture;        // Song management
        private List<SongListNode> _currentSongList;
        private SongListNode _selectedSong;
        private int _currentDifficulty = 0;

        // Instance-scoped search/filter/sort state.
        // StageManager caches one SongSelectionStage per game instance in its
        // _stages dictionary, so this field survives Title→SongSelect re-entry
        // without leaking across separate game/test instances.
        private SongFilterCriteria _filterCriteria = SongFilterCriteria.Default;

        private System.Collections.Generic.IReadOnlyList<FilteredSongResult>? _filteredView;
        private readonly ISongListFilterService _filterService = new SongListFilterService();
        private bool _showEmptyFilterMessage;

        // Tab state. The stage instance is cached per game; reset to AllSongs on Activate.
        private SongSelectionTab _activeTab = SongSelectionTab.AllSongs;
        private static readonly SongSelectionTab[] AllTabs = System.Enum.GetValues<SongSelectionTab>();
        // Cached recent-plays nodes (flat Score nodes), refreshed on activation and on
        // switching into the Recent tab. Null until first load. volatile: published from a
        // background load continuation and read on the update thread (release-acquire pair
        // with _tabListNeedsRefresh).
        private volatile List<SongListNode>? _recentPlayNodes;
        private bool _showEmptyRecentMessage;
        // True when the most recent BeginRecentPlaysLoad continuation failed (DB error,
        // IO error, etc.). Distinct from _showEmptyRecentMessage so a corrupted/locked
        // score DB doesn't look identical to "no plays yet."
        private bool _recentPlaysLoadFailed;
        // Cached bookmark nodes (flat Score nodes), refreshed on activation and on switching
        // into the Bookmarks tab. Null until first load. volatile: published from a background
        // load continuation and read on the update thread (release-acquire pair with
        // _tabListNeedsRefresh).
        private volatile List<SongListNode>? _bookmarkNodes;
        private bool _showEmptyBookmarksMessage;
        // True when the most recent BeginBookmarksLoad continuation failed (DB/IO error).
        private bool _bookmarksLoadFailed;
        // Per-load sequence guard for BeginBookmarksLoad. Incremented on every call so a
        // completion from an older same-activation load (e.g., the activation-time warm load
        // still in flight when the user bookmarks a song and switches to the Bookmarks tab)
        // can detect it has been superseded by a newer load and discard its stale result
        // instead of overwriting _bookmarkNodes with a pre-toggle snapshot that omits the
        // just-bookmarked song. Interlocked because BeginBookmarksLoad is called from both
        // the update thread (Activate, ProcessPendingBookmarkReverts) and thread-pool
        // continuations (SwitchToNextTab's Task.WhenAll chain). volatile so the plain read
        // in the continuation observes the latest increment.
        private volatile int _bookmarksLoadVersion;
        private Func<Task<List<SongListNode>>> _loadBookmarkedNodesAsync =
            () => SongManager.Instance.GetBookmarkedNodesAsync();
        // Set when the active tab's list must be repopulated on the next OnUpdate.
        // Used so background recent-plays loads and lane-hit/Tab switches never mutate
        // SongListDisplay off the update thread. volatile so the update thread reliably
        // observes the flag set by the background continuation.
        private volatile bool _tabListNeedsRefresh;
        // Activation token bumped on every Activate(). Captured by BeginRecentPlaysLoad so
        // its background continuation can detect that a later Deactivate/Activate cycle has
        // occurred and discard stale results instead of overwriting fresh state.
        // volatile: written on the update thread (Activate) and read on a background
        // continuation thread; ensures the continuation sees the latest bump.
        private volatile int _activationVersion;

        // Serializes bookmark persistence writes per song. Each toggle chains after the
        // in-flight write for the same song so rapid double-toggles apply in order and the
        // last user intent wins in the database, instead of a fire-and-forget race that can
        // leave the DB divergent from the in-memory flag. ToggleBookmarkForSelectedSong runs
        // on the update thread, so dictionary access here is single-threaded.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task> _pendingBookmarkWrites = new();

        // Monotonic per-song toggle generation. Bumped on every toggle so a persist fault from
        // an older toggle can be detected and its rollback discarded when a newer toggle has
        // since changed the in-memory flag (otherwise a stale fault would override a successful
        // later toggle). Accessed only on the update thread; the value is captured immutably by
        // the background write continuation.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _bookmarkToggleVersion = new();

        // Pending bookmark reverts enqueued by a faulted persist continuation (which runs on a
        // thread-pool thread). OnUpdate drains this queue on the update thread so the in-memory
        // flag and the reconciled lists roll back safely without racing the draw/input path.
        // Each entry carries the toggle generation that triggered it so a superseded revert is
        // skipped.
        private readonly System.Collections.Concurrent.ConcurrentQueue<(int SongId, bool RevertTo, int ToggleVersion)> _pendingBookmarkReverts = new();

        // Async initialization management
        private Task<List<SongListNode>> _songInitializationTask;
        private bool _songInitializationProcessed = false;
        private CancellationTokenSource _cancellationTokenSource;

        // UI Components - Enhanced DTXManiaNX style
        private UIManager _uiManager;
        private SongListDisplay _songListDisplay;
        private SongStatusPanel _statusPanel;
        private PlayHistoryPanel _playHistoryPanel;
        private PreviewImagePanel _previewImagePanel;
        private UILabel _titleLabel;
        private UILabel _breadcrumbLabel;
        private UIPanel _mainPanel;

        // Input tracking using InputManager
        private InputManager _inputManager;
        private bool _ownsInputManager;
        private IConfigManager _configManager;

        // Navigation state
        private Stack<SongListNode> _navigationStack;
        private string _currentBreadcrumb = "";

        // Status panel navigation state
        private bool _isInStatusPanel = false;

        // Search/filter modal
        private SongSearchFilterModal? _searchFilterModal = null;
        private ITextInputSource? _textInputSource = null;
        private Microsoft.Xna.Framework.Input.MouseState? _previousMouseState = null;
        
        // DTXMania pattern: timing and animation
        private double _elapsedTime;
        private SongSelectionPhase _selectionPhase = SongSelectionPhase.FadeIn;
        private double _phaseStartTime;        // Performance optimization: Input debouncing
        private double _lastNavigationTime = 0;
        
        // Note: Using global stage transition debouncing from BaseGame

        // Constants for DTXMania-style display - using values from SongSelectionUILayout
        private const int CENTER_INDEX = SongSelectionUILayout.SongBars.CenterIndex;
        private const int RecentPlaysLimit = 20;        // Max rows shown on the Recent tab.

        // Low Tom in the lane-hit (KeyBindings) numbering; shared with Right Cymbal.
        // NOTE: distinct from the visual PerformanceUILayout.LaneType.LT (=6).
        private const int LowTomLaneIndex = 8;

        // Floor Tom in the lane-hit (KeyBindings) numbering; toggles a bookmark on the
        // highlighted song. Lane 1 is shared by Floor Tom and Left Cymbal (channels 18 & 11),
        // so either pad triggers the toggle. Distinct from the tab-switch pad (Low Tom = lane 8,
        // itself shared with Right Cymbal).
        private const int FloorTomLaneIndex = 1;

        // RenderTarget management for stage-level resource pooling
        private RenderTarget2D _stageRenderTarget;

        // Preview sound functionality
        private ISound _previewSound;
        private ISoundInstance _previewSoundInstance;
        private ISound _backgroundMusic; // Reference to BGM
        private ISoundInstance _backgroundMusicInstance;
        
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

        public SongSelectionStage(IStageGame game) : base(game)
        {
            _navigationStack = new Stack<SongListNode>();
            AssignInputManager(game.InputManager);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        #endregion

        #region Stage Lifecycle

        public override void Activate(Dictionary<string, object> sharedData = null)
        {
            base.Activate(sharedData);

            // Use game's shared InputManager (supports MCP key injection)
            AssignInputManager(_game.InputManager);
            _inputManager?.ClearPendingCommands();
            _cancellationTokenSource = new CancellationTokenSource();

            // Get config manager from game
            _configManager = _game.ConfigManager;

            if (_configManager != null)
            {
                _configManager.ScrollSpeedChanged += OnScrollSpeedChanged;
            }

            // Initialize graphics resources
            InitializeGraphicsResources();

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
            LoadUIGraphics();

            // Load navigation sound (same as TitleStage)
            LoadNavigationSound();

            // Initialize stage RenderTargets for shared use
            try
            {
                InitializeStageRenderTargets();
            }
            catch (Exception ex)
            {
                // RT init failed (device-lost, OOM) — release all acquired resources
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: RT init failed: {ex.Message}");
                uiFont?.RemoveReference();
                uiFont = null;
                _headerPanelTexture?.RemoveReference();
                _headerPanelTexture = null;
                _footerPanelTexture?.RemoveReference();
                _footerPanelTexture = null;
                _whitePixel?.Dispose();
                _whitePixel = null;
                _spriteBatch?.Dispose();
                _spriteBatch = null;
                ReleaseManagedSound(ref _cursorMoveSound);
                ReleaseManagedSound(ref _gameStartSound);
                throw;
            }

            // Retain the loaded font for direct draw paths (scroll-speed label, empty-filter message)
            _font = uiFont;

            // Initialize UI
            InitializeUI(uiFont);

            // Start song loading
            InitializeSongList();

            // Always start on All Songs for predictability; warm the recent-plays cache so
            // the Recent tab is populated the moment the user switches to it.
            _activeTab = SongSelectionTab.AllSongs;
            _recentPlayNodes = null;
            _showEmptyRecentMessage = false;
            _recentPlaysLoadFailed = false;
            _bookmarkNodes = null;
            _showEmptyBookmarksMessage = false;
            _bookmarksLoadFailed = false;
            // Clear any stale refresh flag from the previous activation (e.g., a tab-switch
            // that set the flag just before Deactivate). Without this, the first OnUpdate
            // would repopulate the All Songs list and reset selection/scroll to index 0.
            _tabListNeedsRefresh = false;
            // Bump the activation token so any in-flight BeginRecentPlaysLoad continuation
            // from a prior activation discards its stale result.
            _activationVersion++;
            BeginRecentPlaysLoad();
            _ = BeginBookmarksLoad();

            SubscribeTabSwitchLaneHits();

            _currentPhase = StagePhase.FadeIn;
            _selectionPhase = SongSelectionPhase.FadeIn;
            _phaseStartTime = 0;
            _elapsedTime = 0;
        }

        public override void Deactivate()
        {
            if (_configManager != null)
            {
                _configManager.ScrollSpeedChanged -= OnScrollSpeedChanged;
                _configManager.FlushPendingSave();
            }

            // Cancel any running song initialization task
            _cancellationTokenSource?.Cancel();

            // Use non-blocking observation to avoid hanging the UI
            // Attach a continuation to handle exceptions without waiting synchronously
            if (_songInitializationTask != null)
            {
                // Capture the token source so we can dispose it after the task completes
                var cts = _cancellationTokenSource;
                _cancellationTokenSource = null;

                // Observe the task result asynchronously via continuation to prevent unobserved exceptions
                _songInitializationTask.ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted)
                        {
                            // Log the exception for debugging but don't throw
                            System.Diagnostics.Debug.WriteLine(
                                $"SongSelectionStage.Deactivate: Task faulted during deactivation: {task.Exception?.GetBaseException().Message}");
                        }
                        // Dispose the token source after the task has completed
                        cts?.Dispose();
                    },
                    TaskScheduler.Default);
            }
            else
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }

            // Clean up task references
            _songInitializationTask = null;

            // Clean up UI
            _uiManager?.Dispose();

            // Unsubscribe from GameWindow.TextInput so the modal's adapter doesn't
            // keep firing into a deactivated stage.
            _textInputSource?.Dispose();
            _textInputSource = null;
            _searchFilterModal = null;

            UnsubscribeTabSwitchLaneHits();

            if (_ownsInputManager)
            {
                _inputManager?.ClearPendingCommands();
                _inputManager?.Dispose();
            }

            _inputManager = null;
            _ownsInputManager = false;

            // Release font reference loaded in Activate for direct-draw paths
            _font?.RemoveReference();
            _font = null;

            // Clean up DTXManiaNX background graphics (Phase 3) - using reference counting
            _headerPanelTexture?.RemoveReference();
            _footerPanelTexture?.RemoveReference();

            // Clean up preview sound resources
            StopCurrentPreview();
            _backgroundMusicInstance?.Dispose();
            _backgroundMusicInstance = null;
            _backgroundMusic?.RemoveReference();
            _backgroundMusic = null;

            // Clean up navigation sound (same as TitleStage)
            ReleaseManagedSound(ref _cursorMoveSound);
            
            // Clean up game start sound
            ReleaseManagedSound(ref _gameStartSound);

            // Clean up graphics resources
            _whitePixel?.Dispose();
            _spriteBatch?.Dispose();

            // Clean up stage RenderTargets
            CleanupStageRenderTargets();

            _selectionPhase = SongSelectionPhase.Inactive;

            // base.Deactivate() sets _currentPhase = Inactive after running
            // CleanupStageBackground(). Setting it here first would short-circuit
            // the base guard (if (_currentPhase == Inactive) return) and skip
            // background cleanup, leaving a disposed texture reference that breaks
            // SongSelect after a skin switch from Config.
            base.Deactivate();
        }

        #endregion

        #region Font Management

        private void AssignInputManager(InputManager? inputManager)
        {
            if (_ownsInputManager)
            {
                _inputManager?.ClearPendingCommands();
                _inputManager?.Dispose();
            }

            _inputManager = inputManager ?? new InputManager();
            _ownsInputManager = inputManager == null;
            if (_ownsInputManager)
            {
                Trace.TraceWarning(
                    "SongSelectionStage: No shared InputManager available; MCP key injection will not work. " +
                    "This should not occur in production — IStageGame.InputManager should be provided by the host.");
            }
        }

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Fallback font unavailable: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Background Graphics Loading (Phase 3)

        private void LoadUIGraphics()
        {
            try
            {
                // Main background is now loaded by BaseStage
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

            // Create main panel sized to the fixed virtual resolution. InitializeUI runs during
            // OnActivate (not during draw), so GraphicsDevice.Viewport is the back buffer (window
            // size), NOT the 1280x720 render target. Sizing from the viewport would make the panel
            // window-dependent and break the fixed-virtual-resolution model.
            _mainPanel = new UIPanel
            {
                Position = Vector2.Zero,
                Size = SongSelectionUILayout.SongListDisplay.Size,
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
                HorizontalAlignment = DTXMania.Game.Lib.UI.Components.TextAlignment.Left
            };

            // Create breadcrumb label
            _breadcrumbLabel = new UILabel("")
            {
                Position = SongSelectionUILayout.UILabels.Breadcrumb.Position,
                Size = SongSelectionUILayout.UILabels.Breadcrumb.Size,
                TextColor = Color.Yellow,
                HasShadow = true,
                HorizontalAlignment = DTXMania.Game.Lib.UI.Components.TextAlignment.Left
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
                WhitePixel = _whitePixel,
                SongsRootPath = _configManager?.Config?.DTXPath
            };

            // Initialize graphics generator for status panel
            _statusPanel.InitializeGraphicsGenerator(_game.GraphicsDevice, _stageRenderTarget);

            // Initialize DTXManiaNX authentic graphics for status panel (Phase 3)
            _statusPanel.InitializeAuthenticGraphics(_resourceManager);

            // Status panel starts hidden and will be shown when a song is selected
            _statusPanel.Visible = false;

            _playHistoryPanel = new PlayHistoryPanel
            {
                ManagedFont = uiFont
            };
            _playHistoryPanel.Initialize(_resourceManager);

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
            _songListDisplay.SelectionChanged += (_, _) => UpdateStatusPanelFolderHint();
            _songListDisplay.SongActivated += OnSongActivated;
            _songListDisplay.DifficultyChanged += OnDifficultyChanged;

            // Add components to panel
            _mainPanel.AddChild(_titleLabel);
            _mainPanel.AddChild(_breadcrumbLabel);
            _mainPanel.AddChild(_songListDisplay);
            _mainPanel.AddChild(_statusPanel);
            _mainPanel.AddChild(_playHistoryPanel);
            _mainPanel.AddChild(_previewImagePanel);

            // Search/filter modal (guarded: text input is unavailable in headless/test environments)
            try
            {
                var textInputSource = _game.GetTextInputSource();
                if (textInputSource != null)
                {
                    _textInputSource = textInputSource;
                    _searchFilterModal = new SongSearchFilterModal(_textInputSource);
                    // Inject graphics resources
                    _searchFilterModal.WhitePixel = _whitePixel;
                    _searchFilterModal.Font = _statusPanel?.Font;
                    _searchFilterModal.FilterApplied += OnFilterApplied;
                    _searchFilterModal.FilterReset   += OnFilterReset;
                    _searchFilterModal.Cancelled     += OnFilterCancelled;
                    _mainPanel.AddChild(_searchFilterModal);
                }
            }
            catch (Exception ex)
            {
                // Window unavailable in headless/test environments; modal will be skipped
                System.Diagnostics.Debug.WriteLine(
                    $"SongSelectionStage: search-filter modal init skipped: {ex.Message}");
            }

            // Clean up if modal construction left _textInputSource orphaned
            if (_searchFilterModal == null && _textInputSource != null)
            {
                _textInputSource.Dispose();
                _textInputSource = null;
            }

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
                    var token = _cancellationTokenSource.Token;

                    // Start background task to initialize SongManager and fetch song list
                    // This task only fetches data and doesn't modify shared state or UI
                    _songInitializationTask = Task.Run(async () =>
                    {
                        try
                        {
                            // Check for cancellation before starting
                            token.ThrowIfCancellationRequested();
                            
                            // SongManager should already be initialized from StartupStage
                            // Just check if it's initialized and return the song list
                            if (!songManager.IsInitialized)
                            {
                                Debug.WriteLine("SongSelectionStage: SongManager not initialized from StartupStage - this should not happen");
                            }
                            
                            // Check for cancellation after initialization
                            token.ThrowIfCancellationRequested();
                            
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
                    }, token);

                    // For now, initialize with empty list
                    _currentSongList = new List<SongListNode>();
                }
                else
                {
                    // Initialize display with song list
                    _currentSongList = new List<SongListNode>(songManager.RootSongs);
                }

                PopulateSongList();
                ReapplyPersistedFilterIfActive();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Error loading songs: {ex.Message}");

                // Fallback to empty list
                _currentSongList = new List<SongListNode>();
                PopulateSongList();
            }
        }

        /// <summary>
        /// If the user had a filter active when they last left the stage, restore it
        /// so the song list reflects the persisted state immediately on re-entry.
        /// </summary>
        private void ReapplyPersistedFilterIfActive()
        {
            if (_filterCriteria.IsEmpty) return;

            RebuildFilteredView();
            PopulateSongListForCurrentMode();
            UpdateBreadcrumb();
            UpdateStatusPanelFolderHint();
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

                    // Reapply any persisted filter now that the real song list is available
                    ReapplyPersistedFilterIfActive();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SongSelectionStage: Error processing completed song initialization: {ex.Message}");
                    _currentSongList = new List<SongListNode>();
                    PopulateSongList();
                }
                finally
                {
                    // Clean up the task reference
                    _songInitializationTask = null;
                }

                if (_searchFilterModal != null && _searchFilterModal.IsOpen)
                    _searchFilterModal.IsLibraryReady = true;
            }
        }

        #endregion

        #region Event Handlers

        private void OnSongSelectionChanged(object sender, SongSelectionChangedEventArgs e)
        {
            
            _selectedSong = e.SelectedSong;
            _currentDifficulty = e.CurrentDifficulty;
            _playHistoryPanel?.UpdateSongInfo(e.SelectedSong, e.CurrentDifficulty);

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
            _playHistoryPanel?.UpdateSongInfo(e.Song, e.NewDifficulty);
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
            
            // Debounce stage transitions to prevent accidental double selections
            if (!_game.CanPerformStageTransition())
                return;

            _game.MarkStageTransition();
            
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

        private void OnFilterApplied(object sender, SongFilterCriteria criteria)
        {
            // Normalize level range so breadcrumb always shows the effective range.
            // SongListFilterService previously swapped silently, leaving _filterCriteria
            // inconsistent with the actual filter result.
            if (criteria.MinLevel.HasValue && criteria.MaxLevel.HasValue
                && criteria.MinLevel > criteria.MaxLevel)
            {
                criteria = criteria with { MinLevel = criteria.MaxLevel, MaxLevel = criteria.MinLevel };
            }
            _filterCriteria = criteria;
            RebuildFilteredView();
            PopulateSongListForCurrentMode();
            UpdateBreadcrumb();
            UpdateStatusPanelFolderHint();
            _inputManager?.ClearPendingCommands();
            _inputManager?.ResetKeyRepeatStates();
        }

        private void OnFilterReset(object sender, System.EventArgs e)
        {
            _filterCriteria = SongFilterCriteria.Default;
            _filteredView = null;
            _showEmptyFilterMessage = false;
            PopulateSongListForCurrentMode();
            UpdateBreadcrumb();
            UpdateStatusPanelFolderHint();
            _inputManager?.ClearPendingCommands();
            _inputManager?.ResetKeyRepeatStates();
        }

        private void OnFilterCancelled(object sender, System.EventArgs e)
        {
            // Discard draft; no state change
            _inputManager?.ClearPendingCommands();
            _inputManager?.ResetKeyRepeatStates();
        }

        private void RebuildFilteredView()
        {
            if (_filterCriteria.IsEmpty)
            {
                _filteredView = null;
                _showEmptyFilterMessage = false;
                return;
            }

            try
            {
                var roots = SongManager.Instance.RootSongs;
                _filteredView = _filterService.Apply(roots, _filterCriteria);
                _showEmptyFilterMessage = _filteredView.Count == 0;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SongSelectionStage: filter projection failed: {ex.Message}");
                // Reset filter state entirely so breadcrumb and list stay consistent.
                _filterCriteria = SongFilterCriteria.Default;
                _filteredView = null;
                _showEmptyFilterMessage = false;
            }
        }

        private void PopulateSongListForCurrentMode()
        {
            if (_filteredView != null)
                PopulateFilteredSongList();
            else
                PopulateSongList();
        }

        /// <summary>
        /// Repopulates the visible song list according to the active tab.
        /// AllSongs uses the existing hierarchical/filtered view; RecentPlays shows the
        /// cached flat recent-plays list (and toggles the empty-state flag).
        /// </summary>
        private void RefreshSongListForActiveTab()
        {
            if (_activeTab == SongSelectionTab.RecentPlays)
                PopulateRecentPlaysList();
            else if (_activeTab == SongSelectionTab.Bookmarks)
                PopulateBookmarksList();
            else
                PopulateSongListForCurrentMode();
        }

        private void PopulateRecentPlaysList()
        {
            var nodes = _recentPlayNodes ?? new List<SongListNode>();
            _songListDisplay.CurrentList = new List<SongListNode>(nodes);
            // Show empty message only when the load succeeded but returned nothing.
            // A failed load gets its own distinct message in the draw path.
            _showEmptyRecentMessage = !_recentPlaysLoadFailed && nodes.Count == 0;
        }

        private void PopulateBookmarksList()
        {
            var nodes = _bookmarkNodes ?? new List<SongListNode>();
            _songListDisplay.CurrentList = new List<SongListNode>(nodes);
            // Show empty message only when the load succeeded but returned nothing.
            // A failed load gets its own distinct message in the draw path.
            _showEmptyBookmarksMessage = !_bookmarksLoadFailed && nodes.Count == 0;
        }

        private void PopulateFilteredSongList()
        {
            var prev = _songListDisplay?.SelectedSong;
            var displayList = new System.Collections.Generic.List<SongListNode>(_filteredView!.Count);
            foreach (var r in _filteredView)
                displayList.Add(r.Node);
            _songListDisplay.CurrentList = displayList;
            _songListDisplay.SelectedIndex = ClampSelectionIndex(prev, displayList);
        }

        private void UpdateBreadcrumb()
        {
            string filterSummary = SummarizeFilter(_filterCriteria);
            if (!string.IsNullOrEmpty(filterSummary))
                _breadcrumbLabel.Text = filterSummary;
            else
                _breadcrumbLabel.Text = string.IsNullOrEmpty(_currentBreadcrumb)
                    ? "Root"
                    : _currentBreadcrumb;
        }

        private void UpdateStatusPanelFolderHint()
        {
            if (_statusPanel == null) return;
            if (_filteredView == null)
            {
                _statusPanel.FolderHint = "";
                return;
            }
            var selectedNode = _songListDisplay?.SelectedSong;
            if (selectedNode == null) { _statusPanel.FolderHint = ""; return; }

            foreach (var r in _filteredView)
            {
                if (ReferenceEquals(r.Node, selectedNode))
                {
                    _statusPanel.FolderHint = r.FolderPath;
                    return;
                }
            }
            _statusPanel.FolderHint = "";
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

            // Update owned fallback input manager (shared one is updated by BaseGame)
            if (_ownsInputManager)
                _inputManager?.Update(deltaTime);

            // Update phase
            UpdatePhase(deltaTime);

            // Roll back any bookmark toggles whose persist failed, so the in-memory star does
            // not silently diverge from the database. Must run on the update thread.
            ProcessPendingBookmarkReverts();

            // Handle input
            HandleInput();

            // Apply any pending tab list repopulate on the update thread.
            if (_tabListNeedsRefresh)
            {
                _tabListNeedsRefresh = false;
                RefreshSongListForActiveTab();
            }

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

            // Draw empty-state message when the active filter returns no results.
            // Only on the All Songs tab (filter is All-Songs-only) and skipped while the
            // search modal is open to avoid overlaying the modal / the Recent empty state.
            if (_activeTab == SongSelectionTab.AllSongs && _showEmptyFilterMessage && _font != null
                && (_searchFilterModal == null || !_searchFilterModal.IsOpen))
            {
                string msg = "No songs match this filter";
                _font.DrawString(_spriteBatch, msg,
                    new Vector2(SongSelectionUILayout.SongBars.UnselectedBarX + 100, SongSelectionUILayout.SongBars.SelectedBarY),
                    Microsoft.Xna.Framework.Color.LightGray);
            }

            // Draw the tab bar (skip while the search modal is open to avoid overlap).
            if (_font != null && (_searchFilterModal == null || !_searchFilterModal.IsOpen))
            {
                DrawTabBar();
            }

            // Draw status message for the Recent tab: distinct text for load failure vs.
            // genuinely empty, so a corrupted/locked score DB is not indistinguishable
            // from "no plays yet."
            if (_activeTab == SongSelectionTab.RecentPlays && _font != null
                && (_searchFilterModal == null || !_searchFilterModal.IsOpen)
                && (_recentPlaysLoadFailed || _showEmptyRecentMessage))
            {
                string msg = _recentPlaysLoadFailed
                    ? "Could not load recent plays"
                    : "No recent plays yet";
                _font.DrawString(_spriteBatch, msg,
                    new Vector2(SongSelectionUILayout.SongBars.UnselectedBarX + SongSelectionUILayout.SongBars.EmptyMessageOffsetX, SongSelectionUILayout.SongBars.SelectedBarY),
                    Microsoft.Xna.Framework.Color.LightGray);
            }

            // Draw status message for the Bookmarks tab: distinct text for load failure vs.
            // genuinely empty, mirroring the Recent-tab status block above.
            if (_activeTab == SongSelectionTab.Bookmarks && _font != null
                && (_searchFilterModal == null || !_searchFilterModal.IsOpen)
                && (_bookmarksLoadFailed || _showEmptyBookmarksMessage))
            {
                string msg = _bookmarksLoadFailed
                    ? "Could not load bookmarks"
                    : "No bookmarks yet";
                _font.DrawString(_spriteBatch, msg,
                    new Vector2(SongSelectionUILayout.SongBars.UnselectedBarX + SongSelectionUILayout.SongBars.EmptyMessageOffsetX, SongSelectionUILayout.SongBars.SelectedBarY),
                    Microsoft.Xna.Framework.Color.LightGray);
            }

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
            if (_inputManager == null)
                return;

            if (_searchFilterModal != null && _searchFilterModal.IsOpen)
            {
                ProcessModalKeys();
                return;
            }

            // Detect Backspace edge-press to open the search/filter modal.
            // Backspace is not a default InputCommand binding because the KeyAssign
            // UI uses it as a meta cancel-capture key, so we poll raw keyboard state here.
            DetectOpenSearchKey();
            DetectTabSwitchKey();
            DetectBookmarkKey();

            // If the modal was just opened this frame, skip processing normal
            // input commands so queued navigation/Back inputs don't leak through
            // underneath the modal on the same frame.
            if (_searchFilterModal != null && _searchFilterModal.IsOpen)
                return;

            ProcessInputCommands();
        }

        private void DetectOpenSearchKey()
        {
            // Route through InputManager so MCP/API key injection is picked up
            // alongside hardware presses. (Raw Keyboard.GetState() misses injected keys.)
            if (_inputManager != null && _inputManager.IsKeyPressed((int)Microsoft.Xna.Framework.Input.Keys.Back))
                OpenSearchFilterModal();
        }

        private void DetectTabSwitchKey()
        {
            // Tab is not in the InputCommandType key-map on purpose: queued commands are
            // not drained while the search modal is open, so a mapped Tab would accumulate
            // and fire stale tab-switches on modal close. Raw-poll here (non-modal path
            // only), matching DetectOpenSearchKey's handling of Backspace. IsKeyPressed
            // also surfaces MCP/E2E injected keys.
            if (_inputManager != null &&
                _inputManager.IsKeyPressed((int)Microsoft.Xna.Framework.Input.Keys.Tab))
            {
                SwitchToNextTab();
            }
        }

        private void DetectBookmarkKey()
        {
            // 'B' toggles a bookmark on the highlighted song. Routed through InputManager so
            // MCP/E2E injected keys are honored. Suppressed implicitly while the search modal
            // is open because HandleInput early-returns before calling this.
            if (_inputManager != null &&
                _inputManager.IsKeyPressed((int)Microsoft.Xna.Framework.Input.Keys.B))
            {
                ToggleBookmarkForSelectedSong();
            }
        }

        // Keys polled as raw input alongside the command-based navigation.
        // Tab and Back are text-editing keys not in the InputCommandType system.
        // Enter and Escape are polled here so they work even when Activate/Back
        // commands are suppressed (e.g. when SearchBox is focused and the bound
        // key produces text characters that would conflict with text entry).
        private static readonly Microsoft.Xna.Framework.Input.Keys[] ModalRawKeys = new[]
        {
            Microsoft.Xna.Framework.Input.Keys.Tab,
            Microsoft.Xna.Framework.Input.Keys.Back,
            Microsoft.Xna.Framework.Input.Keys.Enter,
            Microsoft.Xna.Framework.Input.Keys.Escape
        };

        private void ProcessModalKeys()
        {
            if (_inputManager == null) return;

            // 1. Command-based navigation (respects remapped key bindings).
            //    ALL commands are suppressed when the SearchBox is focused to
            //    prevent a remapped text-producing key (e.g. W → MoveUp, S → MoveDown,
            //    Space → Activate) from stealing focus or closing the modal while the
            //    TextInput event simultaneously inserts a character.
            //    Enter and Escape still reach HandleKey via ModalRawKeys.
            bool searchBoxFocused = _searchFilterModal.FocusedField == SongSearchFilterModal.Field.SearchBox;
            if (!searchBoxFocused)
            {
                var commandTypes = new[]
                {
                    InputCommandType.MoveUp,
                    InputCommandType.MoveDown,
                    InputCommandType.MoveLeft,
                    InputCommandType.MoveRight,
                    InputCommandType.Activate,
                    InputCommandType.Back
                };
                foreach (var cmd in commandTypes)
                {
                    if (_inputManager.IsCommandPressed(cmd))
                        _searchFilterModal.HandleCommand(cmd);
                }
            }
            else
            {
                // When SearchBox is focused, command-based navigation is suppressed to
                // prevent text-producing remapped keys (e.g. W→MoveUp) from stealing
                // focus during text entry.  However, physical arrow keys do not produce
                // text, so we poll them as raw input so keyboard users can navigate
                // away from the search box without needing Tab.
                var arrowKeys = new[]
                {
                    Microsoft.Xna.Framework.Input.Keys.Up,
                    Microsoft.Xna.Framework.Input.Keys.Down,
                    Microsoft.Xna.Framework.Input.Keys.Left,
                    Microsoft.Xna.Framework.Input.Keys.Right
                };
                foreach (var key in arrowKeys)
                {
                    if (_inputManager.IsKeyPressed((int)key))
                        _searchFilterModal.HandleKey(key);
                }
            }

            // 2. Raw keys for text editing (Tab = cycle focus, Back = backspace)
            //    These are not in the InputCommandType system.
            foreach (var key in ModalRawKeys)
            {
                if (_inputManager.IsKeyPressed((int)key))
                    _searchFilterModal.HandleKey(key);
            }

            // 3. Mouse click routing for modal buttons
            //    Treat a null previous state as "all buttons released" so that the
            //    very first click after the modal opens is not silently swallowed. The modal's
            //    button rects are authored in the fixed 1280x720 virtual canvas, so map the raw
            //    window mouse coords into virtual space before hit-testing; a click on the
            //    letterbox bars maps to null and is ignored.
            var mouseState = Microsoft.Xna.Framework.Input.Mouse.GetState();
            if ((_previousMouseState == null ||
                 _previousMouseState.Value.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Released) &&
                mouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed &&
                _game.MapMouseToVirtual(new Microsoft.Xna.Framework.Point(mouseState.X, mouseState.Y)) is { } virtualClick)
            {
                _searchFilterModal.HandleClick(virtualClick);
            }
            _previousMouseState = mouseState;
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
                if (!ExecuteInputCommand(command))
                    break;
            }
        }

        /// <summary>
        /// Execute a specific input command.
        /// Returns false when remaining queued commands should be discarded
        /// (e.g. after a state-changing action like filter reset).
        /// </summary>
        private bool ExecuteInputCommand(InputCommand command)
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
                        // Exit status panel mode - no debounce needed for navigation
                        _isInStatusPanel = false;
                    }
                    else if (_activeTab == SongSelectionTab.AllSongs && _filteredView != null)
                    {
                        // Clear active filter so subsequent Back presses navigate normally.
                        // Drain remaining queued commands to prevent a stale second Back
                        // from navigating away or exiting the stage in the same frame.
                        // Gated by AllSongs: filter state is All-Songs browse state and
                        // must not be touched while on the Recent tab.
                        OnFilterReset(this, System.EventArgs.Empty);
                        return false;
                    }
                    else if (_activeTab == SongSelectionTab.AllSongs && _navigationStack.Count > 0)
                    {
                        // Navigate back in folder structure - no debounce needed for navigation.
                        // Gated by AllSongs: the navigation stack is All-Songs browse state
                        // and must not be popped while on the Recent tab.
                        NavigateBack();
                    }
                    else
                    {
                        // Return to title stage - debounce only for stage transitions.
                        // Also reached from the Recent tab: Back on Recent exits the stage
                        // rather than triggering All-Songs browse actions.
                        if (_game.CanPerformStageTransition())
                        {
                            _game.MarkStageTransition();
                            StageManager?.ChangeStage(StageType.Title, new DTXManiaFadeTransition(SongSelectionUILayout.Timing.TransitionDuration));
                        }
                    }
                    break;

                case InputCommandType.IncreaseScrollSpeed:
                    _configManager?.AdjustScrollSpeed(AppPaths.GetConfigFilePath(), +1);
                    break;

                case InputCommandType.DecreaseScrollSpeed:
                    _configManager?.AdjustScrollSpeed(AppPaths.GetConfigFilePath(), -1);
                    break;

                case InputCommandType.OpenSearch:
                    OpenSearchFilterModal();
                    // Stop draining remaining queued commands so that a queued
                    // Activate/Back does not fire against the song list behind
                    // the modal on the same frame (matches the raw-Backspace
                    // guard at lines 1094-1095).
                    return false;

            }

            return true;
        }

        private void OpenSearchFilterModal()
        {
            if (_activeTab != SongSelectionTab.AllSongs) return;
            if (_searchFilterModal == null) return;
            _isInStatusPanel = false;
            // Reset mouse edge-detection state so the first click after the modal
            // opens is not swallowed (e.g. if the previous modal session ended
            // with a button press that left _previousMouseState as Pressed).
            _previousMouseState = null;
            // Synchronous-init path: when SongManager was already initialized at Activate
            // time, _songInitializationTask is null and _songInitializationProcessed never
            // flips to true — but _currentSongList is set. Treat that as ready.
            _searchFilterModal.IsLibraryReady =
                _currentSongList != null
                && (_songInitializationTask == null || _songInitializationProcessed);
            _searchFilterModal.Open(_filterCriteria);
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
                            _playHistoryPanel?.UpdateSongInfo(_selectedSong, _currentDifficulty);

                            // Difficulty changed - play navigation sound
                            PlayCursorMoveSound();
                        }
                    }
                }
            }
        }

        private void DrawTabBar()
        {
            float x = SongSelectionUILayout.Tabs.X;
            float y = SongSelectionUILayout.Tabs.Y;

            foreach (SongSelectionTab tab in AllTabs)
            {
                string label = tab.DisplayLabel();
                var color = tab == _activeTab
                    ? SongSelectionUILayout.Tabs.ActiveColor
                    : SongSelectionUILayout.Tabs.InactiveColor;

                _font.DrawString(_spriteBatch, label, new Vector2(x, y), color);

                var size = _font.MeasureString(label);
                x += size.X + SongSelectionUILayout.Tabs.Spacing;
            }
        }

        private void DrawBackground()
        {
            var viewport = _game.GraphicsDevice.Viewport;

            // Draw DTXManiaNX authentic background graphics (Phase 3)
            DrawStageBackground(_spriteBatch);
            DrawDTXManiaNXUIGraphics(viewport);
        }

        private void DrawDTXManiaNXUIGraphics(Viewport viewport)
        {
            // Main background is now drawn by BaseStage
            // Draw fallback gradient if no background texture loaded
            if (!IsBackgroundReady)
            {
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

        #endregion

        #region RenderTarget Management

        /// <summary>
        /// Initialize graphics resources needed by the stage.
        /// Overridable for unit testing.
        /// </summary>
        protected virtual void InitializeGraphicsResources()
        {
            _spriteBatch = new SpriteBatch(_game.GraphicsDevice);
            _resourceManager = _game.ResourceManager;

            // Create white pixel for drawing
            _whitePixel = new Texture2D(_game.GraphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });
        }

        /// <summary>
        /// Initialize stage-level RenderTarget for shared use by UI components
        /// </summary>
        protected virtual void InitializeStageRenderTargets()
        {
            // Create a single RenderTarget for all stage operations using RenderTargetManager.
            // Size should be large enough to accommodate all UI components.
            _stageRenderTarget = _game.GraphicsManager.RenderTargetManager
                .GetOrCreateRenderTarget("SongSelectionStage_Main", 1024, 1024);
        }

        /// <summary>
        /// Cleanup stage RenderTarget
        /// </summary>
        private void CleanupStageRenderTargets()
        {
            if (_stageRenderTarget == null)
            {
                return;
            }

            // Use RenderTargetManager to properly dispose the RenderTarget
            _game.GraphicsManager.RenderTargetManager.RemoveRenderTarget("SongSelectionStage_Main");
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
                _cursorMoveSound = _resourceManager.LoadSound(SoundPath.CursorMove);
            }
            catch (Exception)
            {
                _cursorMoveSound = null;
            }

            try
            {
                // Load now loading sound for song selection
                _gameStartSound = _resourceManager.LoadSound(SoundPath.NowLoading);
            }
            catch (Exception)
            {
                try
                {
                    // Fallback to decide sound if Now loading.ogg doesn't work
                    _gameStartSound = _resourceManager.LoadSound(SoundPath.Decide);
                }
                catch (Exception)
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
                            if (_previewSoundInstance != null)
                            {
                                _previewSoundInstance.Dispose();
                                _previewSoundInstance = null;
                            }

                            _previewSoundInstance = CreatePreviewSoundInstance(_previewSound);
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
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"SongSelectionStage: Failed to play preview sound: {ex.Message}");
                        try
                        {
                            _previewSoundInstance?.Dispose();
                        }
                        catch (Exception disposeEx)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"SongSelectionStage: Failed to dispose preview sound instance: {disposeEx.Message}");
                        }

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
                        System.Diagnostics.Debug.WriteLine(
                            $"SongSelectionStage: BGM fade out failed: {ex.Message}");
                        _isBgmFadingOut = false; // Stop retrying on error
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
                        System.Diagnostics.Debug.WriteLine(
                            $"SongSelectionStage: BGM fade in failed: {ex.Message}");
                        _isBgmFadingIn = false; // Stop retrying on error
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

            // Release previous preview sound reference before loading a new one.
            ReleaseManagedSound(ref _previewSound);

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
                    ReleaseManagedSound(ref _previewSound);
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SongSelectionStage: Failed to load preview sound: {ex.Message}");
                ReleaseManagedSound(ref _previewSound);
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
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"SongSelectionStage: Error stopping preview sound: {ex.Message}");
                }
                finally
                {
                    _previewSoundInstance.Dispose();
                    _previewSoundInstance = null;
                }
            }

            // Release managed preview sound reference.
            ReleaseManagedSound(ref _previewSound);
            
            // Reset timers
            _previewPlayDelay = 0.0;
            _isPreviewDelayActive = false;
            
            // Start BGM fade in
            StartBGMFade(false);
        }

        private static void ReleaseManagedSound(ref ISound sound)
        {
            sound?.RemoveReference();
            sound = null;
        }

        private static ISoundInstance CreatePreviewSoundInstance(ISound previewSound)
        {
            var rawInstance = previewSound?.CreateInstance();
            return rawInstance == null ? null : new SoundInstanceWrapper(rawInstance);
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
            catch (Exception)
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
                _gameStartSound?.Play(SongSelectionUILayout.Audio.GameStartSoundVolume);
            }
            catch (Exception)
            {
                // Game start sound failed, continue
            }
        }

        #endregion

        #region Scroll Speed

        private void OnScrollSpeedChanged(object sender, ScrollSpeedChangedEventArgs e)
        {
            // Label re-renders each frame from current config; nothing to do here today.
            // Hook kept for symmetry with PerformanceStage and to make future caching trivial.
        }

        #endregion

        #region Tab Switching

        /// <summary>
        /// Cycles to the next tab, exits status-panel mode, kicks a recent-plays reload
        /// when entering the Recent tab, and requests a list repopulate on the next update.
        /// Invoked by the Tab key and the Low Tom pad (wired in the input-handling path).
        /// </summary>
        private void SwitchToNextTab()
        {
            _activeTab = _activeTab.Next();
            _isInStatusPanel = false;

            if (_activeTab == SongSelectionTab.RecentPlays)
                BeginRecentPlaysLoad();

            if (_activeTab == SongSelectionTab.Bookmarks)
            {
                // Chain the load after any in-flight bookmark writes so the DB query reflects
                // the latest toggles instead of the pre-toggle snapshot. Without this, a
                // bookmark-on followed by a quick tab switch can query the DB before
                // SetBookmarkAsync commits, returning a stale list that omits the
                // just-bookmarked song (the optimistic reconciler only updates nodes already
                // present in _bookmarkNodes, so a newly bookmarked song wouldn't appear until
                // the next reload). Task.WhenAll of an empty/completed set settles immediately.
                var pending = _pendingBookmarkWrites.Values.ToArray();
                _ = Task.WhenAll(pending).ContinueWith(_ =>
                {
                    _ = BeginBookmarksLoad();
                }, TaskScheduler.Default);
            }

            _tabListNeedsRefresh = true;
            PlayCursorMoveSound();
        }

        /// <summary>
        /// Loads recent-plays nodes in the background and flags a repopulate when done.
        /// Safe to call when the singleton DB is unavailable (returns an empty list).
        /// Captures <see cref="_activationVersion"/> so a completion that lands after a
        /// subsequent Deactivate/Activate cycle is discarded instead of overwriting fresh
        /// state or spuriously flagging a list refresh.
        /// </summary>
        private void BeginRecentPlaysLoad()
        {
            int capturedVersion = _activationVersion;
            _ = SongManager.Instance.GetRecentlyPlayedNodesAsync(RecentPlaysLimit)
                .ContinueWith(task =>
                {
                    if (task.IsFaulted || task.IsCanceled)
                    {
                        // Log the full exception (type + stack), not just the base message,
                        // so DB/IO failures are diagnosable from the debug output.
                        System.Diagnostics.Debug.WriteLine(
                            $"SongSelectionStage: recent-plays load failed:\n{task.Exception}");
                        // Discard stale completions from a prior activation.
                        if (capturedVersion == _activationVersion)
                        {
                            _recentPlaysLoadFailed = true;
                            if (_activeTab == SongSelectionTab.RecentPlays)
                                _tabListNeedsRefresh = true;
                        }
                        return;
                    }
                    // Discard stale completions from a prior activation. Without this guard,
                    // a slow load from activation N can overwrite the _recentPlayNodes that
                    // activation N+1 already populated, and trigger an unwanted list rebuild.
                    if (capturedVersion != _activationVersion)
                        return;
                    _recentPlaysLoadFailed = false;
                    _recentPlayNodes = task.Result;
                    // Only request a list repopulate when the user is actually viewing the
                    // Recent tab. Activate() warms the cache while the user is on All Songs;
                    // flagging a refresh there would spuriously rebuild the All Songs list
                    // and reset selection/scroll. Tab switches into Recent already request a
                    // refresh via SwitchToNextTab and rely on this load to populate the list.
                    if (_activeTab == SongSelectionTab.RecentPlays)
                        _tabListNeedsRefresh = true;
                }, TaskScheduler.Default);
        }

        /// <summary>
        /// Loads bookmark nodes in the background and flags a repopulate when done.
        /// Safe when the DB is unavailable (returns an empty list). Captures
        /// <see cref="_activationVersion"/> so a completion that lands after a later
        /// Deactivate/Activate cycle is discarded, and assigns a per-load sequence id
        /// (<see cref="_bookmarksLoadVersion"/>) so an older same-activation load
        /// cannot overwrite the result of a newer load.
        /// </summary>
        private Task BeginBookmarksLoad()
        {
            int capturedVersion = _activationVersion;
            // Assign a monotonic per-load id so an older same-activation load (e.g., the
            // activation-time warm load still in flight when the user bookmarks a song and
            // switches to the Bookmarks tab) can detect it has been superseded by a newer
            // load and discard its stale result.
            int capturedLoadVersion = Interlocked.Increment(ref _bookmarksLoadVersion);
            return Task.Run(async () =>
            {
                List<SongListNode>? nodes = null;
                try
                {
                    nodes = await _loadBookmarkedNodesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Log the full exception (type + stack), not just the base message,
                    // so DB/IO failures are diagnosable from the debug output.
                    System.Diagnostics.Debug.WriteLine(
                        $"SongSelectionStage: bookmarks load failed:\n{ex}");
                    // Discard stale completions from a prior activation or a newer
                    // same-activation load.
                    if (capturedVersion == _activationVersion
                        && capturedLoadVersion == _bookmarksLoadVersion)
                    {
                        _bookmarksLoadFailed = true;
                        if (_activeTab == SongSelectionTab.Bookmarks)
                            _tabListNeedsRefresh = true;
                    }
                    return;
                }
                // Discard stale completions from a prior activation. Without this guard,
                // a slow load from activation N can overwrite the _bookmarkNodes that
                // activation N+1 already populated, and trigger an unwanted list rebuild.
                if (capturedVersion != _activationVersion)
                    return;
                // Discard completions from an older same-activation load. Without this
                // guard, the activation-time warm load (which queried the DB before a
                // bookmark toggle committed) can complete after a newer load triggered
                // by a tab switch, overwriting _bookmarkNodes with a stale pre-toggle
                // result that omits the just-bookmarked song.
                if (capturedLoadVersion != _bookmarksLoadVersion)
                    return;
                _bookmarksLoadFailed = false;
                _bookmarkNodes = nodes;
                // Only request a list repopulate when the user is actually viewing the
                // Bookmarks tab. Activate() warms the cache while the user is on All Songs;
                // flagging a refresh there would spuriously rebuild the All Songs list
                // and reset selection/scroll. Tab switches into Bookmarks already request a
                // refresh via SwitchToNextTab and rely on this load to populate the list.
                if (_activeTab == SongSelectionTab.Bookmarks)
                    _tabListNeedsRefresh = true;
            });
        }

        /// <summary>
        /// Toggles the bookmark flag on the highlighted song. No-op for non-song nodes
        /// (folders/back boxes). Updates the in-memory flag immediately so the star marker
        /// refreshes this frame, persists asynchronously, and—on the Bookmarks tab when
        /// un-bookmarking—removes the row from the cached list and requests a repopulate.
        /// </summary>
        private void ToggleBookmarkForSelectedSong()
        {
            var node = _songListDisplay?.SelectedSong;
            if (node == null || node.Type != NodeType.Score)
                return;

            var song = node.DatabaseSong;
            if (song == null)
                return;

            bool newState = !song.IsBookmarked;
            song.IsBookmarked = newState; // immediate, in-memory; star refreshes next draw.
            // Prefer the persisted id over the in-memory entity id. When re-enumerating
            // SET.def songs that already exist in the DB, AddSongAsync returns the existing
            // id without assigning it to the parsed SongEntity.Id (left at 0), while
            // DatabaseSongId holds the real persisted id. Using song.Id would pass 0 to
            // SetBookmarkAsync (a no-op) and reconcile against every zero-id node.
            int songId = node.DatabaseSongId ?? song.Id;
            // When neither the persisted id nor the entity id is set (e.g. an unpersisted
            // fallback node whose DatabaseSong.Id is still 0), keying the persistence write
            // and the cross-list reconciliation on the sentinel 0 would (a) be a silent
            // no-op in the DB and (b) flip IsBookmarked on EVERY unrelated zero-id node via
            // BookmarkStateReconciler.Apply. The optimistic in-memory flip on the selected
            // node above already refreshed its star marker, so bail out before touching the
            // shared persistence/reconciliation state to leave unrelated nodes untouched.
            if (songId <= 0)
                return;
            // Bump the per-song toggle generation so a fault from THIS toggle can be told apart
            // from a fault of an older, already-superseded toggle at rollback time.
            int toggleVersion = _bookmarkToggleVersion.AddOrUpdate(songId, 1, (_, v) => v + 1);

            // Persist asynchronously, serialized per song so rapid toggles apply in order and
            // the last user intent (the captured newState above) is what lands in the database.
            // Each write chains after the previous in-flight write for the same song; without
            // this, two overlapping fire-and-forget calls could complete out of order and leave
            // the DB divergent from the in-memory flag.
            var prior = _pendingBookmarkWrites.GetOrAdd(songId, _ => Task.CompletedTask);
            var write = prior.ContinueWith(
                _ => SongManager.Instance.SetBookmarkAsync(songId, newState),
                TaskScheduler.Default).Unwrap();
            _pendingBookmarkWrites[songId] = write;

            // If the persist fails, the optimistic in-memory flip would otherwise diverge from
            // the database (the star would vanish on next load with no feedback). Enqueue a
            // rollback to the pre-toggle state; OnUpdate applies it on the update thread so it
            // can't race the draw path. The captured toggleVersion lets the rollback be skipped
            // if a newer toggle has since superseded this one.
            write.ContinueWith(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.WriteLine(
                        $"SongSelectionStage: bookmark persist failed:\n{task.Exception}");
                    _pendingBookmarkReverts.Enqueue((songId, !newState, toggleVersion));
                }
            }, TaskScheduler.Default);

            // Reconcile the flag across every in-memory representation of this song (browse
            // tree, Recent list, Bookmarks list) so the star marker stays consistent across
            // tabs without a reload — each surface holds a distinct node/entity instance.
            BookmarkStateReconciler.Apply(SongManager.Instance.RootSongs, songId, newState);
            BookmarkStateReconciler.Apply(_recentPlayNodes, songId, newState);
            BookmarkStateReconciler.Apply(_bookmarkNodes, songId, newState);

            // On the Bookmarks tab, an un-bookmark should drop the row from view.
            if (_activeTab == SongSelectionTab.Bookmarks && !newState)
            {
                // _bookmarkNodes is volatile and may be replaced by a background BeginBookmarksLoad
                // continuation. RemoveAll targets the list reference seen here; if that reference was
                // just swapped, the removal is harmlessly lost — _tabListNeedsRefresh forces the next
                // update to repopulate from the new list regardless.
                _bookmarkNodes?.RemoveAll(n =>
                    ReferenceEquals(n, node) || (n.DatabaseSongId ?? n.DatabaseSong?.Id) == songId);
                _tabListNeedsRefresh = true;
            }

            PlayCursorMoveSound();
        }

        /// <summary>
        /// Drains <see cref="_pendingBookmarkReverts"/> on the update thread. For each rollback
        /// whose toggle generation is still current, the pre-toggle bookmark state is restored
        /// across every in-memory representation (browse tree, Recent list, Bookmarks list) so the
        /// star marker matches the database after a failed persist. Superseded reverts — those
        /// superseded by a newer toggle — are skipped so a stale fault cannot override a later,
        /// possibly successful toggle.
        /// </summary>
        private void ProcessPendingBookmarkReverts()
        {
            while (_pendingBookmarkReverts.TryDequeue(out var revert))
            {
                if (_bookmarkToggleVersion.TryGetValue(revert.SongId, out var current)
                    && current != revert.ToggleVersion)
                {
                    // A newer toggle has since changed the flag; this fault is stale.
                    continue;
                }

                BookmarkStateReconciler.Apply(SongManager.Instance.RootSongs, revert.SongId, revert.RevertTo);
                BookmarkStateReconciler.Apply(_recentPlayNodes, revert.SongId, revert.RevertTo);
                BookmarkStateReconciler.Apply(_bookmarkNodes, revert.SongId, revert.RevertTo);

                // On the Bookmarks tab, an un-bookmark that failed to persist wrongly removed
                // the row from view (the song is still bookmarked in the DB). Re-sync from the
                // authoritative store so the row reappears.
                if (_activeTab == SongSelectionTab.Bookmarks)
                    _ = BeginBookmarksLoad();
            }
        }

        private void HandleLaneHitForBookmark(int lane)
        {
            if (lane == FloorTomLaneIndex)
                ToggleBookmarkForSelectedSong();
        }

        private void OnTabSwitchLaneHit(object? sender, DTXMania.Game.Lib.Input.LaneHitEventArgs e)
        {
            // Ignore pad-driven tab switches while the search modal is open, matching the
            // Tab-key path (suppressed by HandleInput's modal early-return).
            if (_searchFilterModal != null && _searchFilterModal.IsOpen)
                return;

            HandleLaneHitForTabSwitch(e.Lane);
            HandleLaneHitForBookmark(e.Lane);
        }

        private void HandleLaneHitForTabSwitch(int lane)
        {
            if (lane == LowTomLaneIndex)
                SwitchToNextTab();
        }

        private void SubscribeTabSwitchLaneHits()
        {
            if (_inputManager is DTXMania.Game.Lib.Input.InputManagerCompat compat
                && compat.ModularInputManager != null)
            {
                // Defensive: remove before adding so re-Activate never double-subscribes.
                compat.ModularInputManager.OnLaneHit -= OnTabSwitchLaneHit;
                compat.ModularInputManager.OnLaneHit += OnTabSwitchLaneHit;
            }
        }

        private void UnsubscribeTabSwitchLaneHits()
        {
            if (_inputManager is DTXMania.Game.Lib.Input.InputManagerCompat compat
                && compat.ModularInputManager != null)
            {
                compat.ModularInputManager.OnLaneHit -= OnTabSwitchLaneHit;
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
            if (_filteredView != null && _songListDisplay?.SelectedSong?.Type != NodeType.Score)
            {
                // Filter is active; only Score nodes are valid targets in flat mode.
                return;
            }

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
        public void SetBackgroundMusic(ISound backgroundMusic, ISoundInstance backgroundMusicInstance)
        {
            // Release reference to existing background music before replacing (mirrors _previewSound handling)
            if (_backgroundMusic != null)
            {
                try
                {
                    _backgroundMusic.RemoveReference();
                }
                catch (Exception ex)
                {
                    // Log but don't throw - cleanup should be best-effort
                    System.Diagnostics.Debug.WriteLine(
                        $"SongSelectionStage.SetBackgroundMusic: Error releasing previous BGM reference: {ex.Message}");
                }
            }
            
            // Dispose existing instance before replacing to prevent resource leak
            if (_backgroundMusicInstance != null)
            {
                try
                {
                    _backgroundMusicInstance.Stop();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"SongSelectionStage.SetBackgroundMusic: Error stopping previous BGM instance: {ex.Message}");
                }

                try
                {
                    _backgroundMusicInstance.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"SongSelectionStage.SetBackgroundMusic: Error disposing previous BGM instance: {ex.Message}");
                }
            }
            
            _backgroundMusic = backgroundMusic;
            _backgroundMusicInstance = backgroundMusicInstance;
        }

        #endregion

        #region Telemetry

        public void PopulateTelemetry(GameTelemetrySnapshot telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);

            telemetry.SelectedSongTitle = _selectedSong?.DisplayTitle ?? _selectedSong?.Title;
            telemetry.SelectedDifficulty = _currentDifficulty;
            telemetry.InStatusPanel = _isInStatusPanel;
        }

        #endregion

        // Internal helper for tests (InternalsVisibleTo)
        internal static bool DefaultFilterCriteriaIsEmpty() =>
            SongFilterCriteria.Default.IsEmpty;

        public static int ClampSelectionIndex(SongListNode? previousSelected, System.Collections.Generic.IReadOnlyList<SongListNode>? newList)
        {
            if (newList == null || newList.Count == 0) return 0;
            if (previousSelected == null) return 0;
            for (int i = 0; i < newList.Count; i++)
            {
                if (ReferenceEquals(newList[i], previousSelected))
                    return i;
            }
            return 0;
        }

        // Breadcrumb filter summary helpers
        public static string SummarizeFilter(SongFilterCriteria c)
        {
            if (c.IsEmpty) return "";

            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(c.SearchQuery))
                parts.Add($"\"{c.SearchQuery}\"");

            string? levelPart = FormatLevel(c.MinLevel, c.MaxLevel);
            if (levelPart != null) parts.Add(levelPart);

            if (c.PlayedStatus != PlayedStatus.All)
                parts.Add(c.PlayedStatus.ToString());

            string? sortPart = FormatSort(c.SortBy, c.SortDescending);
            if (sortPart != null) parts.Add(sortPart);

            return "Filtered: " + string.Join(" | ", parts);
        }

        private static string? FormatLevel(int? min, int? max)
        {
            if (min is null && max is null) return null;
            if (min is not null && max is not null)
            {
                // Normalize inverted ranges to match the filter service behavior
                int lo = min.Value, hi = max.Value;
                if (lo > hi) (lo, hi) = (hi, lo);
                return $"Lv {lo}-{hi}";
            }
            if (min is not null) return $"Lv {min}+";
            return $"Lv <={max}";
        }

        private static string? FormatSort(SongSortCriteria by, bool desc)
        {
            // Default sort (Title ascending) is omitted from the summary
            if (by == SongSortCriteria.Title && !desc) return null;
            char arrow = desc ? 'v' : '^';
            return $"{by}{arrow}";
        }

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
