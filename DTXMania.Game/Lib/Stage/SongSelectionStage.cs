using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
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
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage.SongSelection;
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
        // Themed display font for the tab bar; null = draw tabs with _font (NX).
        private IFont _tabFont;
        private IFont _historyFont;
        private Texture2D _whitePixel;

        // HPA-796: the stage owns one cached white pixel for this activation (RemoveReference
        // on teardown); _whitePixel above is the borrowed raw texture lent to the UI components.
        private ITexture _whitePixelResource;

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

        private static readonly SongSelectionTab[] AllTabs = System.Enum.GetValues<SongSelectionTab>();

        // True once the activation's initializer terminal has been applied on the update
        // thread (success or fallback), so the search modal stops waiting on the library.
        private bool _libraryInitializationCompleted;

        // UI Components - Enhanced DTXManiaNX style
        private UIManager _uiManager;
        private SongListDisplay _songListDisplay;
        private SongStatusPanel _statusPanel;
        private PlayHistoryPanel _playHistoryPanel;
        private PreviewImagePanel _previewImagePanel;
        private UILabel _titleLabel;
        private UILabel _breadcrumbLabel;
        private UILabel _playbackProfileLabel;
        private UIPanel _mainPanel;

        // Input tracking using InputManager
        private InputManager _inputManager;
        private bool _ownsInputManager;
        private IConfigManager _configManager;

        // Navigation state
        private Stack<SongListNode> _navigationStack;
        private string _currentBreadcrumb = "";
        private bool _isProjectingPreparedSelection;
        private PreparedChartSelection _preparedChartSelection;

        // Status panel navigation state
        private bool _isInStatusPanel = false;

        // Search/filter modal
        private SongSearchFilterModal? _searchFilterModal = null;
        private ITextInputSource? _textInputSource = null;
        private Microsoft.Xna.Framework.Input.MouseState? _previousMouseState = null;

        // Quick Settings popup (bass drum x2, NX quick config)
        private QuickSettingsPopup? _quickSettingsPopup = null;
        private int _quickSettingsPlaySpeedAtOpen;

        // NX CCommandHistory: two hits of the same pad within 500 ms make a double hit.
        private const double PadDoubleHitWindowSeconds = 0.5;
        private int _lastDoubleHitPadLane = -1;
        private double _lastDoubleHitPadTime;

        // DTXMania pattern: timing and animation
        private double _elapsedTime;
        private SongSelectionPhase _selectionPhase = SongSelectionPhase.FadeIn;
        private double _phaseStartTime;        // Performance optimization: Input debouncing
        private double _lastNavigationTime = 0;
        
        // Note: Using global stage transition debouncing from BaseGame

        // Constants for DTXMania-style display - using values from SongSelectionUILayout
        private const int CENTER_INDEX = SongSelectionUILayout.SongBars.CenterIndex;

        private const int FloorTomLaneIndex = (int)PerformanceUILayout.LaneType.FT;
        private const int HiHatLaneIndex = (int)PerformanceUILayout.LaneType.HH;
        private const int BassDrumLaneIndex = (int)PerformanceUILayout.LaneType.DB;

        // RenderTarget management for stage-level resource pooling
        private RenderTarget2D _stageRenderTarget;

        // Preview sound functionality (HPA-802): the controller owns the audio
        // facts (managed sound/instance refs, delayed start, prepared playback
        // state and elapsed time); the stage selects paths and owns the prepared
        // selection record.
        private readonly SongSelectionPreviewController _previewController;

        // Library publication intake (HPA-802): owns the publication subscription, the
        // applied snapshot/score-identity index/version marker, active roots, flat-row
        // eligibility and the library-empty classification. The library is resolved
        // lazily and the stage drives reconciliation linearly.
        private readonly SongSelectionLibraryIntake _intake;

        // Tabs and bookmark writes (HPA-802): owns the active tab, the Recent/Bookmarks
        // caches with their distinct empty/error states, the refresh request/consumption
        // protocol, flat-tab loads, and the durable bookmark write path. Resolves the
        // library lazily; the stage keeps input/overlay policy, navigation and the
        // visible-list projection.
        private readonly SongSelectionTabs _tabs;
        
        // Navigation sound functionality (same as TitleStage)
        private ISound _cursorMoveSound;
        private ISound _gameStartSound;

        private sealed record PreparedChartSelection(
            SongListNode Node,
            SongChart Chart,
            int DifficultyIndex,
            string TelemetryIdentity);

        private sealed record PreparedChartResolution(
            SongListNode Node,
            SongChart Chart,
            int DifficultyIndex,
            IReadOnlyList<string> AncestorBoxPaths);

        #endregion

        #region Properties

        public override StageType Type => StageType.SongSelect;

        /// <summary>
        /// The game-owned song library, resolved on use (HPA-801). Operations that
        /// need it fail clearly when the game exposes none — no silent fallback to
        /// a shared instance.
        /// </summary>
        private SongLibrary Library =>
            _game.SongLibrary ?? throw new InvalidOperationException(
                "Song selection requires the game-owned song library, but the game exposes none " +
                "(BaseGame.CreateLoadContentServices must construct it before the StageManager).");

        #endregion

        #region Constructor

        public SongSelectionStage(IStageGame game) : this(game, createPreviewInstance: null)
        {
        }

        // HPA-802 review: narrow test seams — forwards the instance-creation delegate
        // to the preview controller so headless tests can drive the real
        // successful-play path (MonoGame's SoundEffectInstance cannot be constructed
        // in tests), the tabs-owner factory so stage-level tests can own the owner
        // instance (constructor seam, not owner-field reflection), and the tabs
        // loader/writer seams so stage-level tests can gate bookmark loads/writes
        // without touching owner state. Production uses the single-argument constructor.
        internal SongSelectionStage(
            IStageGame game,
            Func<ISound?, ISoundInstance?>? createPreviewInstance,
            Func<SongSelectionLibraryIntake, Func<StageBackgroundWorkScope?>, ILogger, SongSelectionTabs>? createTabs = null,
            Func<Task<List<SongListNode>>>? loadRecentPlaysAsync = null,
            Func<Task<List<SongListNode>>>? loadBookmarkedNodesAsync = null,
            Func<SongLibrary, int, bool, Task>? setBookmarkAsync = null) : base(game)
        {
            _navigationStack = new Stack<SongListNode>();
            // HPA-802: the preview controller resolves the resource manager lazily,
            // only when a preview loads — construction does no filesystem work.
            _previewController = createPreviewInstance == null
                ? new SongSelectionPreviewController(() => _resourceManager, Logger)
                : new SongSelectionPreviewController(() => _resourceManager, Logger, createPreviewInstance);
            // HPA-802: the intake resolves the game-owned library lazily —
            // construction does no database or filesystem work.
            _intake = new SongSelectionLibraryIntake(() => Library);
            // The tabs owner wires the real query functions; the optional seams exist
            // only for tests (deterministic TCS gates over loads/writes).
            _tabs = createTabs != null
                ? createTabs(_intake, () => BackgroundWork, Logger)
                : new SongSelectionTabs(
                    () => Library,
                    _intake,
                    () => BackgroundWork,
                    Logger,
                    loadRecentPlaysAsync,
                    loadBookmarkedNodesAsync,
                    setBookmarkAsync);
            AssignInputManager(game.InputManager);
        }

        #endregion

        #region Stage Lifecycle

        // Payload contract is nullability-annotated; the rest of this legacy file stays in its original (oblivious) nullable context.
        #nullable enable
        public override void Activate(StageTransitionPayload? payload = null)
        #nullable restore
        {
            base.Activate(payload);

            // Use game's shared InputManager (supports MCP key injection)
            AssignInputManager(_game.InputManager);
            _inputManager?.ClearPendingCommands();
            // base.Activate already retired the prior activation's scope, so no queued
            // initializer/publication/tab callback can write into this activation.
            _intake.Reset();
            _libraryInitializationCompleted = false;

            // The handler is bound to this activation's fresh scope, so a retained delegate
            // from an earlier activation can never write into this one.
            _intake.Subscribe(BackgroundWork, ApplyPendingLibraryPublication);

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
            catch (Exception)
            {
                // Only log critical errors

                // Create a simple fallback font for testing
                try
                {
                    uiFont = CreateFallbackFont();
                }
                catch (Exception)
                {
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
            catch (Exception)
            {
                // RT init failed (device-lost, OOM) — release all acquired resources
                uiFont?.RemoveReference();
                uiFont = null;
                _headerPanelTexture?.RemoveReference();
                _headerPanelTexture = null;
                _footerPanelTexture?.RemoveReference();
                _footerPanelTexture = null;
                _whitePixelResource?.RemoveReference();
                _whitePixelResource = null;
                _whitePixel = null;
                _spriteBatch?.Dispose();
                _spriteBatch = null;
                ReleaseManagedSound(ref _cursorMoveSound);
                ReleaseManagedSound(ref _gameStartSound);
                throw;
            }

            // Retain the loaded font for direct draw paths (scroll-speed label, empty-filter message)
            _font = uiFont;

            // Optional themed display font for the tab bar; null keeps the shared UI font.
            try
            {
                var theme = CurrentTheme;
                var tabFamily = ResolveTabFontFamily(theme);
                if (tabFamily.Length > 0)
                    _tabFont = _resourceManager.LoadFont(tabFamily, ResolveTabFontSize(theme));
            }
            catch (Exception)
            {
                _tabFont = null;
            }

            // Initialize UI
            InitializeUI(uiFont);

            // Always start on All Songs for predictability; warm the recent-plays cache so
            // the Recent tab is populated the moment the user switches to it. Reset this
            // before projecting the activation snapshot so reconciliation never renders a
            // previous activation's tab while restoring hierarchy and selection.
            _tabs.ResetForActivation();

            // Start song loading after the tab state is reset. An initialized manager is
            // reconciled instead of raw-applied, which restores only paths still present in
            // the current snapshot and clears a removed hierarchy before it is displayed.
            InitializeSongList();
            _tabs.BeginRecentPlaysLoad();
            // Warm Bookmarks refresh enters the same deferred wait path as the tab switch
            // and rollback reloads: a write still pending from a previous activation
            // (durable across resets) settles before the warm query reads the DB.
            _tabs.RequestBookmarksRefresh();

            SubscribePadLaneHits();

            _currentPhase = StagePhase.FadeIn;
            _selectionPhase = SongSelectionPhase.FadeIn;
            _phaseStartTime = 0;
            _elapsedTime = 0;
            // The stage is cached and its clock restarts, so a hit left over from the
            // last visit must not pair with the first hit of this one.
            _lastDoubleHitPadLane = -1;
        }

        public override void Deactivate()
        {
            // HPA-799: retire the activation's background work FIRST, before any UI, input,
            // font, audio or render-target teardown — queued initializer, publication, tab-load
            // and bookmark-rollback callbacks drop here and can never reach disposed
            // resources. Idempotent; the pending UI rollback of an already-committed bookmark
            // write is dropped, never the write itself.
            RetireBackgroundWork();

            // Stop accepting publications before resources and UI collections begin tearing
            // down. A publisher already in flight carries a reporter bound to the retired
            // scope, so its signal drops instead of reaching torn-down UI state.
            _intake.Unsubscribe();

            if (_configManager != null)
            {
                _configManager.ScrollSpeedChanged -= OnScrollSpeedChanged;
                _configManager.FlushPendingSave();
            }

            _intake.Reset();

            // Clean up UI
            _uiManager?.Dispose();

            // Unsubscribe from GameWindow.TextInput so the modal's adapter doesn't
            // keep firing into a deactivated stage.
            _textInputSource?.Dispose();
            _textInputSource = null;
            _searchFilterModal = null;
            if (_quickSettingsPopup != null)
                _quickSettingsPopup.Closed -= OnQuickSettingsClosed;
            _quickSettingsPopup = null;

            UnsubscribePadLaneHits();

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
            _tabFont?.RemoveReference();
            _tabFont = null;
            _historyFont?.RemoveReference();
            _historyFont = null;

            // Clean up DTXManiaNX background graphics (Phase 3) - using reference counting
            _headerPanelTexture?.RemoveReference();
            _footerPanelTexture?.RemoveReference();

            // Clean up preview sound resources
            ClearPreparedPreviewState();

            // Clean up navigation sound (same as TitleStage)
            ReleaseManagedSound(ref _cursorMoveSound);
            
            // Clean up game start sound
            ReleaseManagedSound(ref _gameStartSound);

            // Clean up graphics resources
            _whitePixelResource?.RemoveReference();
            _whitePixelResource = null;
            _whitePixel = null;
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                // Bounded five-second wait before the game disposes the library: pending
                // durable bookmark writes must never race database disposal.
                _tabs.WaitForPendingWritesAtDisposal();
            }
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
                Logger.LogWarning(
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
            catch (Exception)
            {
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
            catch (Exception)
            {
            }

            try
            {
                _headerPanelTexture = _resourceManager.LoadTexture(TexturePath.SongSelectionHeaderPanel);
            }
            catch (Exception)
            {
            }

            try
            {
                _footerPanelTexture = _resourceManager.LoadTexture(TexturePath.SongSelectionFooterPanel);
            }
            catch (Exception)
            {
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

            // Create breadcrumb label. Without the UI font UILabel.OnDraw skips the label,
            // leaving the breadcrumb invisible.
            _breadcrumbLabel = new UILabel("")
            {
                Position = SongSelectionUILayout.UILabels.Breadcrumb.Position,
                Size = SongSelectionUILayout.UILabels.Breadcrumb.Size,
                Font = uiFont?.SpriteFont,
                TextColor = Color.Yellow,
                HasShadow = true,
                HorizontalAlignment = DTXMania.Game.Lib.UI.Components.TextAlignment.Left
            };

            // Active speed/pitch profile. Text is populated by SynchronizeActivePlaySpeed
            // from the same config snapshot used by speed-scoped score consumers.
            _playbackProfileLabel = new PlaybackProfileLabel()
            {
                Position = SongSelectionUILayout.UILabels.PlaybackProfile.Position,
                Size = SongSelectionUILayout.UILabels.PlaybackProfile.Size,
                Font = uiFont?.SpriteFont,
                TextColor = Color.LightGreen,
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
            catch (Exception)
            {
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
                ActiveSongRootPaths = Array.Empty<string>()
            };

            // Initialize graphics generator for status panel
            _statusPanel.InitializeGraphicsGenerator(_game.GraphicsDevice, _stageRenderTarget);

            // Initialize DTXManiaNX authentic graphics for status panel (Phase 3)
            _statusPanel.InitializeAuthenticGraphics(_resourceManager);

            // Status panel starts hidden and will be shown when a song is selected
            _statusPanel.Visible = false;

            // Optional themed face for the recent-plays rows; null keeps the shared UI font.
            var historyTheme = CurrentTheme;
            try
            {
                var historyFamily = ResolveHistoryFontFamily(historyTheme);
                if (historyFamily.Length > 0)
                    _historyFont = _resourceManager.LoadFont(historyFamily, ResolveHistoryFontSize(historyTheme));
            }
            catch (Exception)
            {
                _historyFont = null;
            }

            _playHistoryPanel = new PlayHistoryPanel
            {
                ManagedFont = _historyFont ?? uiFont,
                TextScale = ResolveHistoryTextScale(historyTheme)
            };
            _playHistoryPanel.Initialize(_resourceManager);

            // The selected config profile is the source of truth for every score-derived
            // song-select surface. Set it before CurrentList is populated and begins raising
            // selection events.
            SynchronizeActivePlaySpeed();

            // Initialize preview image panel
            try
            {
                _previewImagePanel.Initialize(_resourceManager);
                _previewImagePanel.Visible = true;
            }
            catch (Exception)
            {
            }

            // Wire up events
            _songListDisplay.SelectionChanged += OnSongSelectionChanged;
            _songListDisplay.SelectionChanged += (_, _) => UpdateStatusPanelFolderHint();
            _songListDisplay.SongActivated += OnSongActivated;
            _songListDisplay.DifficultyChanged += OnDifficultyChanged;

            // Add components to panel. The breadcrumb goes after the full-screen song-list
            // display so its text is not covered by the list's 70%-black background wash.
            _mainPanel.AddChild(_titleLabel);
            _mainPanel.AddChild(_songListDisplay);
            _mainPanel.AddChild(_breadcrumbLabel);
            _mainPanel.AddChild(_statusPanel);
            _mainPanel.AddChild(_playHistoryPanel);
            _mainPanel.AddChild(_previewImagePanel);
            _mainPanel.AddChild(_playbackProfileLabel);

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
            catch (Exception)
            {
                // Window unavailable in headless/test environments; modal will be skipped
            }

            // Clean up if modal construction left _textInputSource orphaned
            if (_searchFilterModal == null && _textInputSource != null)
            {
                _textInputSource.Dispose();
                _textInputSource = null;
            }

            _quickSettingsPopup = new QuickSettingsPopup
            {
                WhitePixel = _whitePixel,
                // Same face as the tab strip: the skin's display font (CX Neon: Orbitron),
                // else the shared UI font as NX does.
                Font = _tabFont?.SpriteFont ?? _statusPanel?.Font
            };
            _quickSettingsPopup.Initialize(_resourceManager);
            _quickSettingsPopup.Closed += OnQuickSettingsClosed;
            _mainPanel.AddChild(_quickSettingsPopup);

            // Add panel to UI manager
            _uiManager.AddRootContainer(_mainPanel);

            // Activate the main panel and all its children
            _mainPanel.Activate();
        }

        private void InitializeSongList()
        {
            try
            {
                var library = Library;

                // Check if the game-owned library is properly initialized
                if (!library.IsInitialized)
                {
                    // While no coherent snapshot is available, do not leave a BackBox from
                    // the previous activation visible over the empty placeholder list.
                    ClearNavigationForPendingLibrary();
                    _currentSongList = new List<SongListNode>();
                    PopulateSongList();
                    ReapplyPersistedFilterIfActive();

                    // Apply the newest coherent snapshot on the update thread at the next
                    // drain (HPA-799), fetched when the callback runs so an old initializer
                    // read can never overwrite a newer library publication. No worker is
                    // needed: the fetch is a cheap in-memory read.
                    BackgroundWork?.Post(ApplyInitializedLibrarySnapshot);
                }
                else
                {
                    // Re-entry can retain an old navigation stack. Project the activation
                    // snapshot through the same path as a live publication so retained boxes
                    // are restored and removed boxes cannot expose stale children.
                    ReconcileLibrarySnapshot(library.GetLibrarySnapshot());
                    _libraryInitializationCompleted = true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Song library initialization failed; showing an empty song list");
                ApplyLibraryInitializationFailure();
            }
        }

        private void ClearNavigationForPendingLibrary()
        {
            _navigationStack.Clear();
            _currentBreadcrumb = "";
            ClearRemovedSelectionPresentation();
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
            displayList.AddRange(_currentSongList);
            displayList.Add(SongListNode.CreateRandomNode());
            _songListDisplay.CurrentList = displayList; // Update the song list display
        }

        /// <summary>
        /// Initializer terminal applied on the update thread: fetches the newest coherent
        /// library snapshot HERE (never a copy taken on the worker) and reconciles it unless a
        /// newer publication has already been applied, so an old initializer can never
        /// overwrite restored navigation, a newer publication, or a newer manager snapshot.
        /// </summary>
        private void ApplyInitializedLibrarySnapshot()
        {
            try
            {
                // Signal 0 = initializer poll: accepts the first snapshot even at version
                // zero; once a snapshot is installed only a strictly newer one is accepted,
                // so an old initializer read can never overwrite a newer publication.
                var snapshot = _intake.TryGetNewer(0);
                if (snapshot != null)
                {
                    // Even the initializer may replace a hierarchy that was already being
                    // displayed. Reconcile instead of raw-applying so a retained box path
                    // and selection are restored against this exact snapshot, while a
                    // removed path is cleared before projection.
                    ReconcileLibrarySnapshot(snapshot);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Applying the song library initialization result failed");
                ApplyLibraryInitializationFailure();
                return;
            }

            _libraryInitializationCompleted = true;
            if (_searchFilterModal != null && _searchFilterModal.IsOpen)
                _searchFilterModal.IsLibraryReady = true;
        }

        /// <summary>
        /// Initializer failure fallback applied on the update thread: with no snapshot applied
        /// yet it shows an empty placeholder list, and it always unblocks an open search modal.
        /// </summary>
        private void ApplyLibraryInitializationFailure()
        {
            if (_intake.AppliedSnapshot == null)
                ShowPendingLibraryPlaceholder();

            _libraryInitializationCompleted = true;
            if (_searchFilterModal != null && _searchFilterModal.IsOpen)
                _searchFilterModal.IsLibraryReady = true;
        }

        /// <summary>
        /// The no-library placeholder shared by the initializer failure fallback and the
        /// no-prior-snapshot rollback: clears browse state and — so a failed snapshot's
        /// rows can never resurface through a stale filtered view or preview root — also
        /// the active filter view and the preview root paths (HPA-802).
        /// </summary>
        private void ShowPendingLibraryPlaceholder()
        {
            ClearNavigationForPendingLibrary();
            _filteredView = null;
            _showEmptyFilterMessage = false;
            if (_previewImagePanel != null)
                _previewImagePanel.ActiveSongRootPaths = Array.Empty<string>();
            _currentSongList = new List<SongListNode>();
            PopulateSongList();
        }

        /// <summary>
        /// Applies one coalesced publication signal on the update thread. The newest coherent
        /// snapshot is fetched HERE; the acknowledgment tracks the newer of the signal and the
        /// fetched snapshot so an older fetch can never reapply over a newer publication.
        /// </summary>
        private void ApplyPendingLibraryPublication(long publishedVersion)
        {
            var snapshot = _intake.TryGetNewer(publishedVersion);
            if (snapshot == null)
                return;

            ReconcileLibrarySnapshot(snapshot);
            _intake.Commit(publishedVersion);
        }

        private void ReconcileLibrarySnapshot(SongLibrarySnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            // Captured BEFORE the attempt: a failed attempt may already have truncated
            // the navigation stack or cleared the selection, so a restore must re-drive
            // the reconciliation from the pre-attempt browse state, not from whatever
            // the failed attempt left behind (HPA-802).
            var priorSnapshot = _intake.AppliedSnapshot;
            var navigationPaths = CaptureNavigationPaths();
            var previousSelection = _selectedSong ?? _songListDisplay?.SelectedSong;
            var previousSelectionIdentity = GetStableSelectionIdentity(previousSelection);
            try
            {
                ReconcileLibrarySnapshotCore(snapshot, navigationPaths, previousSelectionIdentity);
            }
            catch (Exception reconciliationFailure)
            {
                // A failed reconciliation must not leave the failed snapshot installed:
                // restore the last valid snapshot — or, when none was ever applied, the
                // pending-library placeholder — so readiness falls back and the next
                // drain retries from intact state (HPA-802). The triggering exception
                // is never masked.
                try
                {
                    _intake.Rollback(priorSnapshot);
                    if (priorSnapshot != null)
                    {
                        // Re-drive the same linear reconciliation against the last
                        // valid snapshot using the PRE-ATTEMPT navigation/selection.
                        // Selection notifications raised during this restore are
                        // contained per step (isFailureRestore), so a persistently
                        // faulting subscriber cannot abort the rollback midway and
                        // leave the stage on the list's row zero; any other fault is
                        // logged and the original exception below is still the one
                        // that propagates.
                        ReconcileLibrarySnapshotCore(priorSnapshot, navigationPaths, previousSelectionIdentity, isFailureRestore: true);
                    }
                    else
                    {
                        ShowPendingLibraryPlaceholder();
                    }
                }
                catch (Exception restoreFailure)
                {
                    Logger.LogWarning(restoreFailure, "Restoring the previous song library projection failed");
                }

                throw;
            }
        }

        /// <summary>
        /// The linear reconciliation body shared by the initializer, publication
        /// handler and failure restore: install the snapshot, project it into the
        /// stage UI, and only then acknowledge its version. The stable navigation and
        /// selection to restore are captured by the caller so an attempt and its
        /// failure restore always work from the same pre-attempt browse state.
        /// In a failure restore the selection-raising projection steps are contained
        /// individually, so the restored row, breadcrumb and status still complete
        /// when a subscriber faults on every selection notification; the primary
        /// attempt keeps propagating so a faulting reconcile is never acknowledged.
        /// </summary>
        private void ReconcileLibrarySnapshotCore(
            SongLibrarySnapshot snapshot,
            IReadOnlyList<string> navigationPaths,
            string? previousSelectionIdentity,
            bool isFailureRestore = false)
        {
            int tabListRefreshVersion = _tabs.BeginTabListRefreshConsumption();

            // Apply roots and root paths from this single snapshot before rebuilding any
            // projection. Navigation rebuild then selects child lists owned by that same tree.
            _intake.Install(snapshot);
            _currentSongList = snapshot.RootSongs.ToList();
            if (_previewImagePanel != null)
                _previewImagePanel.ActiveSongRootPaths = snapshot.ActiveRoots;
            RestoreNavigationPaths(navigationPaths, snapshot);
            RebuildFilteredViewForSnapshot(snapshot);

            var restoredSelection = FindVisibleSongByIdentity(previousSelectionIdentity);
            if (previousSelectionIdentity != null && restoredSelection == null)
            {
                ClearRemovedSelectionPresentation();
            }
            else if (restoredSelection != null)
            {
                // Set the logical selection BEFORE the display refresh: the refresh runs
                // selection callbacks that may throw, and a restored selection must
                // survive that fault. RestoreSelectedSong still syncs the display row.
                _selectedSong = restoredSelection;
            }

            if (_songListDisplay != null)
            {
                void SyncRestoredSelection()
                {
                    if (restoredSelection != null)
                        RestoreSelectedSong(restoredSelection);
                    else
                        _selectedSong = _songListDisplay.SelectedSong;
                }

                if (isFailureRestore)
                {
                    // Each selection-raising step is contained separately: the list
                    // refresh fires the row-zero ('..') notification BEFORE the final
                    // row can be re-selected, and a persistently faulting subscriber
                    // must not leave the stage and display stranded there (HPA-802).
                    try
                    {
                        RefreshSongListForActiveTab();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Restoring the song list projection failed");
                    }

                    try
                    {
                        SyncRestoredSelection();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Restoring the selected song failed");
                    }
                }
                else
                {
                    RefreshSongListForActiveTab();
                    SyncRestoredSelection();
                }
            }

            UpdateBreadcrumb();
            UpdateStatusPanelFolderHint();
            _tabs.CompleteTabListRefreshConsumption(tabListRefreshVersion);
            _intake.Commit(snapshot.Version);
        }

        private List<string> CaptureNavigationPaths()
        {
            var states = _navigationStack.Reverse().ToList();
            var paths = new List<string>(states.Count);

            for (int index = 0; index < states.Count; index++)
            {
                var parentNodes = states[index].Children;
                var childNodes = index + 1 < states.Count
                    ? states[index + 1].Children
                    : _currentSongList;
                var enteredBox = parentNodes.FirstOrDefault(node =>
                    node.Type == NodeType.Box && ReferenceEquals(node.Children, childNodes));
                var stablePath = GetStableBoxPath(enteredBox);
                if (stablePath == null)
                    break;
                paths.Add(stablePath);
            }

            return paths;
        }

        private void RestoreNavigationPaths(
            IReadOnlyList<string> navigationPaths,
            SongLibrarySnapshot snapshot)
        {
            _navigationStack.Clear();
            _currentSongList = snapshot.RootSongs.ToList();
            _currentBreadcrumb = "";

            foreach (var path in navigationPaths)
            {
                var box = _currentSongList.FirstOrDefault(node =>
                    node.Type == NodeType.Box
                    && StringComparer.Ordinal.Equals(GetStableBoxPath(node), path));
                if (box == null)
                    break;

                _navigationStack.Push(new SongListNode
                {
                    Children = _currentSongList,
                    Title = _currentBreadcrumb
                });
                _currentSongList = box.Children;
                _currentBreadcrumb = string.IsNullOrEmpty(_currentBreadcrumb)
                    ? box.DisplayTitle
                    : $"{_currentBreadcrumb} > {box.DisplayTitle}";
            }
        }

        private PreparedChartResolution? ResolvePreparedChart(string requestedChartPath)
        {
            var snapshot = _intake.AppliedSnapshot;
            if (string.IsNullOrWhiteSpace(requestedChartPath)
                || !Path.IsPathFullyQualified(requestedChartPath)
                || snapshot == null
                || !SongPathIdentity.TryNormalize(requestedChartPath, out var normalizedRequestedPath))
            {
                return null;
            }

            var normalizedActiveRoots = new List<string>();
            foreach (var activeRoot in snapshot.ActiveRoots)
            {
                if (SongPathIdentity.TryNormalize(activeRoot, out var normalizedRoot))
                    normalizedActiveRoots.Add(normalizedRoot);
            }

            if (!normalizedActiveRoots.Any(root =>
                    SongPathIdentity.IsUnderNormalizedRoot(normalizedRequestedPath, root)))
            {
                return null;
            }

            var matches = new List<(SongListNode Node, SongChart Chart, IReadOnlyList<string> AncestorBoxPaths)>();
            VisitPreparedChartNodes(
                snapshot.RootSongs,
                Array.Empty<string>(),
                normalizedRequestedPath,
                matches);

            if (matches.Count != 1)
                return null;

            var match = matches[0];
            if (!TryResolvePreparedDifficulty(
                    match.Node,
                    match.Chart,
                    normalizedRequestedPath,
                    out var difficultyIndex))
            {
                return null;
            }

            return new PreparedChartResolution(
                match.Node,
                match.Chart,
                difficultyIndex,
                match.AncestorBoxPaths);
        }

        private static void VisitPreparedChartNodes(
            IEnumerable<SongListNode> nodes,
            IReadOnlyList<string> ancestorBoxPaths,
            string normalizedRequestedPath,
            List<(SongListNode Node, SongChart Chart, IReadOnlyList<string> AncestorBoxPaths)> matches)
        {
            foreach (var node in nodes ?? Enumerable.Empty<SongListNode>())
            {
                if (node == null)
                    continue;

                if (node.Type == NodeType.Score)
                {
                    foreach (var chart in EnumerateNodeCharts(node))
                    {
                        if (!SongPathIdentity.TryNormalize(chart.FilePath, out var normalizedChartPath)
                            || !StringComparer.Ordinal.Equals(normalizedChartPath, normalizedRequestedPath))
                        {
                            continue;
                        }

                        matches.Add((node, chart, ancestorBoxPaths));
                    }
                }

                if (node.Type != NodeType.Box || node.Children == null)
                    continue;

                var boxPath = GetStableBoxPath(node);
                if (boxPath == null)
                    continue;

                VisitPreparedChartNodes(
                    node.Children,
                    ancestorBoxPaths.Concat(new[] { boxPath }).ToArray(),
                    normalizedRequestedPath,
                    matches);
            }
        }

        private static IEnumerable<SongChart> EnumerateNodeCharts(SongListNode node)
        {
            var seen = new HashSet<SongChart>();
            if (node.DatabaseChart != null && seen.Add(node.DatabaseChart))
                yield return node.DatabaseChart;

            // Prefer the filtered chart set the node's slots were built from:
            // DatabaseSong.Charts still carries rows whose files were dropped
            // as missing, so it could match a chart that cannot be played.
            var nodeCharts = node.ExistingCharts
                ?? (IEnumerable<SongChart>?)node.DatabaseSong?.Charts;
            foreach (var chart in nodeCharts ?? Enumerable.Empty<SongChart>())
            {
                if (chart != null && seen.Add(chart))
                    yield return chart;
            }
        }

        private static bool TryResolvePreparedDifficulty(
            SongListNode node,
            SongChart chart,
            string normalizedChartPath,
            out int difficultyIndex)
        {
            difficultyIndex = -1;
            var scores = node.Scores ?? Array.Empty<SongScore>();
            var matchingChartIdSlots = chart.Id != 0
                ? scores
                    .Select((score, index) => (score, index))
                    .Where(value => value.score != null
                        && value.score.ChartId != 0
                        && value.score.ChartId == chart.Id)
                    .ToList()
                : new List<(SongScore score, int index)>();

            if (matchingChartIdSlots.Count == 1)
            {
                difficultyIndex = matchingChartIdSlots[0].index;
                return true;
            }

            if (matchingChartIdSlots.Count > 1)
            {
                var drumSlots = matchingChartIdSlots
                    .Where(value => value.score.Instrument == EInstrumentPart.DRUMS)
                    .ToList();
                if (drumSlots.Count == 1)
                {
                    difficultyIndex = drumSlots[0].index;
                    return true;
                }
            }

            var exactFallbackSlots = scores
                .Select((score, index) => (score, index))
                .Where(value => value.score != null)
                .Where(value =>
                {
                    var fallbackChart = node.GetCurrentDifficultyChart(value.index);
                    return fallbackChart != null
                        && SongPathIdentity.TryNormalize(fallbackChart.FilePath, out var fallbackPath)
                        && StringComparer.Ordinal.Equals(fallbackPath, normalizedChartPath);
                })
                .Select(value => value.index)
                .ToList();

            if (exactFallbackSlots.Count == 1)
            {
                difficultyIndex = exactFallbackSlots[0];
                return true;
            }

            return false;
        }

        private bool ProjectPreparedChartSelection(PreparedChartResolution resolution)
        {
            var snapshot = _intake.AppliedSnapshot;
            if (resolution == null || snapshot == null || _songListDisplay == null)
                return false;

            var previousTab = _tabs.ActiveTab;
            var previousFilterCriteria = _filterCriteria;
            var previousFilteredView = _filteredView;
            var previousShowEmptyFilterMessage = _showEmptyFilterMessage;
            var previousSongList = _currentSongList;
            var previousBreadcrumb = _currentBreadcrumb;
            var previousNavigation = new List<SongListNode>(_navigationStack);

            _isProjectingPreparedSelection = true;
            try
            {
                _tabs.SetActiveTabQuiet(SongSelectionTab.AllSongs);
                _filterCriteria = SongFilterCriteria.Default;
                _filteredView = null;
                _showEmptyFilterMessage = false;

                RestoreNavigationPaths(resolution.AncestorBoxPaths, snapshot);
                RefreshSongListForActiveTab();

                var targetIndex = _songListDisplay.CurrentList.FindIndex(node =>
                    ReferenceEquals(node, resolution.Node));
                if (targetIndex < 0)
                {
                    RestoreBrowseState(
                        previousTab,
                        previousFilterCriteria,
                        previousFilteredView,
                        previousShowEmptyFilterMessage,
                        previousSongList,
                        previousBreadcrumb,
                        previousNavigation);
                    return false;
                }

                _songListDisplay.SetSelection(targetIndex, resolution.DifficultyIndex);
                UpdateBreadcrumb();
                UpdateStatusPanelFolderHint();
                return true;
            }
            finally
            {
                _isProjectingPreparedSelection = false;
            }
        }

        private void RestoreBrowseState(
            SongSelectionTab tab,
            SongFilterCriteria filterCriteria,
            System.Collections.Generic.IReadOnlyList<FilteredSongResult>? filteredView,
            bool showEmptyFilterMessage,
            List<SongListNode> songList,
            string breadcrumb,
            List<SongListNode> navigation)
        {
            _tabs.SetActiveTabQuiet(tab);
            _filterCriteria = filterCriteria;
            _filteredView = filteredView;
            _showEmptyFilterMessage = showEmptyFilterMessage;
            _currentSongList = songList;
            _currentBreadcrumb = breadcrumb;

            _navigationStack.Clear();
            for (var i = navigation.Count - 1; i >= 0; i--)
                _navigationStack.Push(navigation[i]);

            RefreshSongListForActiveTab();
        }

        internal (bool Success, string Error) PrepareVideoChart(string chartPath)
        {
            // Stable exits only: when the body throws, its exception must surface
            // unmasked; every normal return is a stable boundary.
            var result = PrepareVideoChartCore(chartPath);
            AssertPreparedSelectionStatePairing();
            return result;
        }

        private (bool Success, string Error) PrepareVideoChartCore(string chartPath)
        {
            if (string.IsNullOrWhiteSpace(chartPath))
                return PreparedFailure("A chart path is required.");

            var resolution = ResolvePreparedChart(chartPath);
            if (resolution == null)
                return PreparedFailure("The requested chart is not available in the active song library.");

            if (!TryGetChartPreviewPath(resolution.Chart, out var previewPath))
                return PreparedFailure("The requested chart does not declare a preview file.");

            if (!File.Exists(previewPath))
                return PreparedFailure("The requested chart preview file was not found.");

            // Tear down any existing prepared state and the ordinary interactive preview only
            // after all validation succeeds, so a rejected request preserves the current
            // prepared selection and loaded preview.
            ClearPreparedPreviewStateIfOwned();
            _previewController.Stop();

            // Project through the normal Song Select hierarchy before publishing prepared
            // state. The projection itself suppresses the intermediate selection events so
            // they cannot stop or replace this exact-chart preview.
            if (!ProjectPreparedChartSelection(resolution))
            {
                ResumeOrdinaryPreviewAfterPrepareFailure(resolution.Node);
                return PreparedFailure("The requested chart could not be selected.");
            }

            // LoadStopped keeps the loaded preview silent: prepared playback remains
            // stopped until StartPreparedPreview is called, and only a successful
            // stopped load publishes the preparation below.
            //
            // The identity record is built BEFORE the controller is armed: if it
            // throws, no Prepared state exists yet, so the pairing invariant still
            // holds and cannot mask the original exception.
            var preparedSelection = new PreparedChartSelection(
                resolution.Node,
                resolution.Chart,
                resolution.DifficultyIndex,
                BuildPreparedChartTelemetryIdentity(resolution.Chart));

            if (!_previewController.LoadStopped(previewPath))
            {
                ResumeOrdinaryPreviewAfterPrepareFailure(resolution.Node);
                return PreparedFailure("The requested chart preview could not be loaded.");
            }

            _preparedChartSelection = preparedSelection;
            return PreparedSuccess();
        }

        internal (bool Success, string Error) StartPreparedPreview()
        {
            var result = _preparedChartSelection != null && _previewController.HasLoadedSound
                ? _previewController.StartPrepared()
                : PreparedFailure("No prepared chart preview is available.");

            AssertPreparedSelectionStatePairing();
            return result;
        }

        internal (bool Success, string Error) CancelPreparedChart()
        {
            ClearPreparedPreviewStateIfOwned();

            AssertPreparedSelectionStatePairing();
            return PreparedSuccess();
        }

        internal (bool Success, string Error) ActivatePreparedChart()
        {
            var result = ActivatePreparedChartCore();

            AssertPreparedSelectionStatePairing();
            return result;
        }

        private (bool Success, string Error) ActivatePreparedChartCore()
        {
            var prepared = _preparedChartSelection;
            if (prepared == null)
                return PreparedFailure("No prepared chart is available.");

            var selectedSong = _selectedSong ?? _songListDisplay?.SelectedSong;
            if (!ReferenceEquals(selectedSong, prepared.Node)
                || _currentDifficulty != prepared.DifficultyIndex)
            {
                return PreparedFailure("The prepared chart is no longer selected.");
            }

            if (!CanStartSongSelection(prepared.Node, out var eligibilityError))
                return PreparedFailure(eligibilityError);

            // Eligibility is checked before cleanup so a debounce-blocked activation can be
            // retried without losing the stopped/playing prepared preview.
            ClearPreparedPreviewState();
            StartSongSelection(prepared.Node);
            return PreparedSuccess();
        }

        private static (bool Success, string Error) PreparedSuccess() => (true, null);

        private static (bool Success, string Error) PreparedFailure(string error) =>
            (false, error);

        private void ClearPreparedPreviewState()
        {
            // One synchronous stage operation: the controller's playback reset and
            // the stage record clear together, so a non-null record is never paired
            // with a stale prepared state (HPA-802).
            _previewController.Stop();
            _preparedChartSelection = null;
            AssertPreparedSelectionStatePairing();
        }

        // HPA-802 review: two-way invariant between the stage-owned prepared record
        // and the controller's playback state — a record exists exactly when the
        // controller state is not None (an unexpected stop keeps the pair as
        // record + Failed, explicit cleanup keeps null + None). Checked at stable
        // stage operation boundaries so a mismatch fails fast instead of surfacing
        // as stale playback or a lost identity.
        private void AssertPreparedSelectionStatePairing()
        {
            bool hasRecord = _preparedChartSelection != null;
            bool hasPlaybackState = _previewController.PreparedState != SongSelectionPreviewState.None;
            if (hasRecord == hasPlaybackState)
                return;

            throw new InvalidOperationException(
                $"Prepared chart identity and preview playback state are inconsistent: " +
                $"record={(hasRecord ? "set" : "null")}, playback={_previewController.PreparedState}.");
        }

        private void ClearPreparedPreviewStateIfOwned()
        {
            if (_preparedChartSelection == null)
                return;

            ClearPreparedPreviewState();
        }

        private void ResumeOrdinaryPreviewAfterPrepareFailure(SongListNode fallbackNode)
        {
            var selectedNode = _selectedSong ?? _songListDisplay?.SelectedSong ?? fallbackNode;
            if (selectedNode?.Type == NodeType.Score)
                LoadPreviewSound(selectedNode);
        }

        private static bool TryGetChartPreviewPath(SongChart chart, out string previewPath)
        {
            previewPath = null;
            if (chart == null
                || string.IsNullOrWhiteSpace(chart.FilePath)
                || string.IsNullOrWhiteSpace(chart.PreviewFile))
            {
                return false;
            }

            try
            {
                var chartDirectory = Path.GetDirectoryName(chart.FilePath);
                if (string.IsNullOrWhiteSpace(chartDirectory))
                    return false;

                previewPath = Path.GetFullPath(Path.Combine(chartDirectory, chart.PreviewFile));
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                previewPath = null;
                return false;
            }
        }

        private string BuildPreparedChartTelemetryIdentity(SongChart chart)
        {
            var snapshot = _intake.AppliedSnapshot;
            if (chart.Id != 0)
                return $"chart:{chart.Id}";

            if (!SongPathIdentity.TryNormalize(chart.FilePath, out var normalizedChartPath)
                || snapshot == null)
            {
                return "";
            }

            foreach (var activeRoot in snapshot.ActiveRoots)
            {
                if (!SongPathIdentity.TryNormalize(activeRoot, out var normalizedRoot)
                    || !SongPathIdentity.IsUnderNormalizedRoot(normalizedChartPath, normalizedRoot))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(normalizedRoot, normalizedChartPath);
                return relativePath.Replace(Path.DirectorySeparatorChar, '/');
            }

            return "";
        }

        private SongListNode? FindVisibleSongByIdentity(string? identity)
        {
            if (string.IsNullOrEmpty(identity))
                return null;

            IEnumerable<SongListNode> candidates;
            if (_tabs.ActiveTab == SongSelectionTab.RecentPlays)
            {
                candidates = _intake.Filter(_tabs.RecentPlayNodes);
            }
            else if (_tabs.ActiveTab == SongSelectionTab.Bookmarks)
            {
                candidates = _intake.Filter(_tabs.BookmarkNodes);
            }
            else if (_filteredView != null)
            {
                candidates = _filteredView.Select(result => result.Node);
            }
            else
            {
                candidates = _currentSongList;
            }

            return candidates.FirstOrDefault(node =>
                StringComparer.Ordinal.Equals(GetStableSelectionIdentity(node), identity));
        }

        private void RestoreSelectedSong(SongListNode restoredSelection)
        {
            if (_songListDisplay == null)
                return;

            for (int index = 0; index < _songListDisplay.CurrentList.Count; index++)
            {
                if (!ReferenceEquals(_songListDisplay.CurrentList[index], restoredSelection))
                    continue;

                _songListDisplay.SelectedIndex = index;
                _selectedSong = restoredSelection;
                return;
            }
        }

        private void ClearRemovedSelectionPresentation()
        {
            _selectedSong = null;
            _isInStatusPanel = false;
            // HPA-802: removal routes through the prepared cleanup path so the
            // identity is cleared together with playback, even without a record
            // (the ordinary preview must still stop).
            ClearPreparedPreviewState();
            _previewImagePanel?.ClearSelectedSong();
            if (_statusPanel != null)
            {
                _statusPanel.Visible = false;
                _statusPanel.UpdateSongInfo(null, _currentDifficulty);
            }
            _playHistoryPanel?.UpdateSongInfo(null, _currentDifficulty);
        }

        private static string? GetStableBoxPath(SongListNode? node)
        {
            if (node?.Type != NodeType.Box
                || !SongPathIdentity.TryNormalize(node.DirectoryPath, out var path))
            {
                return null;
            }

            return path;
        }

        private static string? GetStableSelectionIdentity(SongListNode? node)
        {
            if (node?.Type == NodeType.Box)
            {
                return SongPathIdentity.TryNormalize(node.DirectoryPath, out var boxPath)
                    ? $"box:{boxPath}"
                    : null;
            }

            if (node?.Type != NodeType.Score)
                return null;

            int songId = node.DatabaseSongId ?? node.DatabaseSong?.Id ?? 0;
            if (songId > 0)
                return $"song:{songId}";

            foreach (var path in SongSelectionLibraryIntake.GetNodeChartPaths(node))
            {
                if (SongPathIdentity.TryNormalize(path, out var normalized))
                    return $"chart:{normalized}";
            }

            return null;
        }


        #endregion

        #region Event Handlers

        private void OnSongSelectionChanged(object sender, SongSelectionChangedEventArgs e)
        {
            int playSpeedPercent = SynchronizeActivePlaySpeed();
            var preparedSelection = _preparedChartSelection;
            var leavingPreparedRow = preparedSelection != null
                && !ReferenceEquals(preparedSelection.Node, e.SelectedSong);
            _selectedSong = e.SelectedSong;
            _currentDifficulty = e.CurrentDifficulty;
            _playHistoryPanel?.UpdateSongInfo(
                e.SelectedSong,
                e.CurrentDifficulty,
                playSpeedPercent);

            // Prepared projection raises the normal selection event while the row is
            // being rebuilt. Keep presentation updates, but suppress preview teardown
            // and automatic loading until the entire projection has completed.
            var clearedPreparedPreview = false;
            if (!_isProjectingPreparedSelection && leavingPreparedRow)
            {
                ClearPreparedPreviewState();
                clearedPreparedPreview = true;
            }

            if (!_isProjectingPreparedSelection
                && !clearedPreparedPreview
                && (preparedSelection == null || leavingPreparedRow))
                _previewController.Stop();

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

                // Start preview sound loading - load immediately but delay playback.
                // The prepared projection owns its preview resource and must not be
                // replaced by the normal primary-chart preview path.
                if (!_isProjectingPreparedSelection && (preparedSelection == null || leavingPreparedRow))
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
                    _statusPanel?.UpdateSongInfo(
                        e.SelectedSong,
                        e.CurrentDifficulty,
                        playSpeedPercent);
                }
                
                // Every exit of this handler is a stable boundary, including the
                // scroll-incomplete early return.
                AssertPreparedSelectionStatePairing();
                return; // Skip other heavy updates during scrolling
            }

            // After scrolling completes - update everything
            // Process all selection changes immediately (debounce removed per senior engineer feedback)

            // Force immediate visual update
            _songListDisplay?.InvalidateVisuals();

            // Update status panel (now cached for performance)
            _statusPanel?.UpdateSongInfo(
                e.SelectedSong,
                e.CurrentDifficulty,
                playSpeedPercent);

            // Update breadcrumb (lightweight operation)
            UpdateBreadcrumb();

            AssertPreparedSelectionStatePairing();
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
            int playSpeedPercent = SynchronizeActivePlaySpeed();

            ClearPreparedSelectionIfDifficultyLeftIt(e.Song, e.NewDifficulty);

            // Update status panel
            _statusPanel.UpdateSongInfo(e.Song, e.NewDifficulty, playSpeedPercent);
            _playHistoryPanel?.UpdateSongInfo(e.Song, e.NewDifficulty, playSpeedPercent);
            AssertPreparedSelectionStatePairing();
        }

        // HPA-802 review: shared cleanup for every difficulty-change entry point —
        // leaving the prepared difficulty clears the prepared identity and playback
        // together and restores the ordinary delayed preview (never during projection).
        private void ClearPreparedSelectionIfDifficultyLeftIt(SongListNode? song, int newDifficulty)
        {
            var preparedSelection = _preparedChartSelection;
            var leavingPreparedDifficulty = preparedSelection != null
                && (!ReferenceEquals(preparedSelection.Node, song)
                    || preparedSelection.DifficultyIndex != newDifficulty);
            if (_isProjectingPreparedSelection || !leavingPreparedDifficulty)
                return;

            ClearPreparedPreviewState();
            if (song?.Type == NodeType.Score)
                LoadPreviewSound(song);
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
            if (!CanStartSongSelection(songNode, out _))
                return;

            StartSongSelection(songNode);
        }

        private bool CanStartSongSelection(SongListNode songNode, out string error)
        {
            if (songNode == null || songNode.Type != NodeType.Score)
            {
                error = "The selected item is not a playable song.";
                return false;
            }

            if (StageManager == null)
            {
                error = "The song transition manager is unavailable.";
                return false;
            }

            if (StageManager.IsTransitioning)
            {
                error = "The song transition is currently blocked.";
                return false;
            }

            // Debounce stage transitions to prevent accidental double selections.
            if (!_game.CanPerformStageTransition())
            {
                error = "The song transition is currently blocked.";
                return false;
            }

            error = null;
            return true;
        }

        private void StartSongSelection(SongListNode songNode)
        {
            _game.MarkStageTransition();

            // Hand the typed play request to the transition stage
            var request = new SongPlayRequest(songNode, _currentDifficulty);

            // Transition immediately to SongTransitionStage
            StageManager.ChangeStage(StageType.SongTransition, new InstantTransition(), request);
        }

        internal static List<SongListNode> CollectRandomCandidates(
            IEnumerable<SongListNode> nodes,
            bool includeSubBoxes)
        {
            var candidates = new List<SongListNode>();
            foreach (var node in nodes)
            {
                if (node.Type == NodeType.Score)
                    candidates.Add(node);
                else if (node.Type == NodeType.Box && (includeSubBoxes || node.IsSongRoot))
                    candidates.AddRange(CollectRandomCandidates(node.Children, includeSubBoxes));
            }

            return candidates;
        }

        private void SelectRandomSong()
        {
            if (_tabs.ActiveTab != SongSelectionTab.AllSongs)
                return;

            if (_currentSongList != null && _currentSongList.Count > 0)
            {
                var includeSubBoxes = _configManager?.Config?.RandomSelectFromSubBox ?? false;
                var songNodes = CollectRandomCandidates(_currentSongList, includeSubBoxes);

                if (songNodes.Count > 0)
                {
                    var random = new Random();
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
            var snapshot = _intake.AppliedSnapshot ?? Library.GetLibrarySnapshot();
            RebuildFilteredViewForSnapshot(snapshot);
        }

        private void RebuildFilteredViewForSnapshot(SongLibrarySnapshot snapshot)
        {
            if (_filterCriteria.IsEmpty)
            {
                _filteredView = null;
                _showEmptyFilterMessage = false;
                return;
            }

            try
            {
                _filteredView = _filterService.Apply(
                    snapshot.RootSongs,
                    _filterCriteria,
                    SynchronizeActivePlaySpeed());
                _showEmptyFilterMessage = _filteredView.Count == 0;
            }
            catch (System.Exception)
            {
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
        /// AllSongs uses the existing hierarchical/filtered view; RecentPlays/Bookmarks
        /// show the tab owner's projected flat rows (row building and empty/error flags
        /// are the tabs owner's; assigning them to the display is the stage's).
        /// </summary>
        private void RefreshSongListForActiveTab()
        {
            if (_tabs.ActiveTab == SongSelectionTab.RecentPlays)
                PopulateRecentPlaysList();
            else if (_tabs.ActiveTab == SongSelectionTab.Bookmarks)
                PopulateBookmarksList();
            else
                PopulateSongListForCurrentMode();

            // Same repopulate path as tab switches and load completions: the caption must
            // follow the active tab (cleared on Recent/Bookmarks, restored on All Songs)
            // even when those lists are empty and no selection callback fires.
            UpdateBreadcrumb();
        }

        private void PopulateRecentPlaysList()
        {
            _songListDisplay.CurrentList = _tabs.BuildRecentPlaysRows();
        }

        private void PopulateBookmarksList()
        {
            _songListDisplay.CurrentList = _tabs.BuildBookmarksRows();
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
            // The breadcrumb describes All-Songs hierarchy/filter state only; Recent and
            // Bookmarks project flat rows from a different scope, so the caption is cleared
            // there (even when their empty lists fire no selection callback) and restored
            // from retained state when All Songs becomes active again.
            if (_tabs.ActiveTab != SongSelectionTab.AllSongs)
            {
                _breadcrumbLabel.Text = "";
                return;
            }

            string filterSummary = SummarizeFilter(_filterCriteria);
            string caption = !string.IsNullOrEmpty(filterSummary)
                ? filterSummary
                : string.IsNullOrEmpty(_currentBreadcrumb) ? "Root" : _currentBreadcrumb;
            // UILabel auto-sizes and never clips; keep the visible caption inside the clear
            // band left of the preview panel, which draws later at this Y and would cover
            // overflow. A null font keeps the unmeasured failure path (label not drawn).
            _breadcrumbLabel.Text = TextHelper.TruncateToWidth(
                caption,
                SongSelectionUILayout.UILabels.Breadcrumb.MaxTextWidth,
                _breadcrumbLabel.Font);
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
            _previewController.Update(deltaTime);

            // Initializer, publication, tab-load and bookmark-rollback completions are
            // applied by BaseStage.Update's scope drain before this method runs (HPA-799).

            // Update owned fallback input manager (shared one is updated by BaseGame)
            if (_ownsInputManager)
                _inputManager?.Update(deltaTime);

            // Update phase
            UpdatePhase(deltaTime);

            // Handle input
            HandleInput();

            // Apply any pending tab list repopulate on the update thread.
            if (_tabs.TabListNeedsRefresh)
            {
                int tabListRefreshVersion = _tabs.BeginTabListRefreshConsumption();
                RefreshSongListForActiveTab();
                _tabs.CompleteTabListRefreshConsumption(tabListRefreshVersion);
            }

            // Update UI (PreviewImagePanel will handle its own timing)
            _uiManager?.Update(deltaTime);

            AssertPreparedSelectionStatePairing();
        }

        /// <summary>True while Search &amp; Filter or Quick Settings covers the stage.</summary>
        private bool IsOverlayOpen =>
            _searchFilterModal?.IsOpen == true || _quickSettingsPopup?.IsOpen == true;

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin();

            // Draw background
            DrawBackground();

            bool showLibraryRecoveryMessage =
                _tabs.ActiveTab == SongSelectionTab.AllSongs
                && _filteredView == null
                && _intake.EmptyState != SongSelectionLibraryEmptyState.HasSongs
                && _font != null
                && !IsOverlayOpen;

            // SongListDisplay has a generic centered "No songs found" fallback. Hide only
            // that component while the stage owns the more useful library-recovery message;
            // Recent, Bookmarks, and filter-empty rendering keep their existing behavior.
            bool songListWasVisible = _songListDisplay?.Visible ?? false;
            if (showLibraryRecoveryMessage && _songListDisplay != null)
                _songListDisplay.Visible = false;

            try
            {
                // Draw UI (PreviewImagePanel will handle its own drawing including delay)
                _uiManager?.Draw(_spriteBatch, deltaTime);
            }
            finally
            {
                if (showLibraryRecoveryMessage && _songListDisplay != null)
                    _songListDisplay.Visible = songListWasVisible;
            }

            // Draw empty-state message when the active filter returns no results.
            // Only on the All Songs tab (filter is All-Songs-only) and skipped while the
            // search modal is open to avoid overlaying the modal / the Recent empty state.
            if (_tabs.ActiveTab == SongSelectionTab.AllSongs && _showEmptyFilterMessage && _font != null
                && !IsOverlayOpen)
            {
                string msg = "No songs match this filter";
                _font.DrawString(_spriteBatch, msg,
                    new Vector2(SongSelectionUILayout.SongBars.UnselectedBarX + 100, SongSelectionUILayout.SongBars.SelectedBarY),
                    ResolveStatusTextColor(CurrentTheme));
            }

            if (showLibraryRecoveryMessage)
            {
                string msg = TextHelper.TruncateToWidth(
                    SongSelectionLibraryIntake.ResolveLibraryEmptyMessage(_intake.EmptyState),
                    SongSelectionUILayout.SongBars.EmptyMessageMaxWidth,
                    _font);
                _font.DrawString(_spriteBatch, msg,
                    new Vector2(
                        SongSelectionUILayout.SongBars.UnselectedBarX
                            + SongSelectionUILayout.SongBars.EmptyMessageOffsetX,
                        SongSelectionUILayout.SongBars.SelectedBarY),
                    ResolveStatusTextColor(CurrentTheme));
            }

            // Draw the tab bar (skip while the search modal is open to avoid overlap).
            // Prefer the shared body font, but still draw when only the tab face loaded.
            if ((_font != null || _tabFont != null)
                && !IsOverlayOpen)
            {
                DrawTabBar();
            }

            // Draw status message for the Recent tab: distinct text for load failure vs.
            // genuinely empty, so a corrupted/locked score DB is not indistinguishable
            // from "no plays yet."
            if (_tabs.ActiveTab == SongSelectionTab.RecentPlays && _font != null
                && !IsOverlayOpen
                && (_tabs.RecentPlaysLoadFailed || _tabs.ShowEmptyRecentMessage))
            {
                string msg = _tabs.RecentPlaysLoadFailed
                    ? "Could not load recent plays"
                    : "No recent plays yet";
                _font.DrawString(_spriteBatch, msg,
                    new Vector2(SongSelectionUILayout.SongBars.UnselectedBarX + SongSelectionUILayout.SongBars.EmptyMessageOffsetX, SongSelectionUILayout.SongBars.SelectedBarY),
                    ResolveStatusTextColor(CurrentTheme));
            }

            // Draw status message for the Bookmarks tab: distinct text for load failure vs.
            // genuinely empty, mirroring the Recent-tab status block above.
            if (_tabs.ActiveTab == SongSelectionTab.Bookmarks && _font != null
                && !IsOverlayOpen
                && (_tabs.BookmarksLoadFailed || _tabs.ShowEmptyBookmarksMessage))
            {
                string msg = _tabs.BookmarksLoadFailed
                    ? "Could not load bookmarks"
                    : "No bookmarks yet";
                _font.DrawString(_spriteBatch, msg,
                    new Vector2(SongSelectionUILayout.SongBars.UnselectedBarX + SongSelectionUILayout.SongBars.EmptyMessageOffsetX, SongSelectionUILayout.SongBars.SelectedBarY),
                    ResolveStatusTextColor(CurrentTheme));
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

            if (_quickSettingsPopup != null && _quickSettingsPopup.IsOpen)
            {
                var commands = _inputManager.GetInputCommands();
                while (commands.Count > 0 && _quickSettingsPopup.IsOpen)
                    _quickSettingsPopup.HandleCommand(commands.Dequeue().Type);
                return;
            }

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
                // Drum pads never type text, so they still drive the modal from the SearchBox.
                bool padPressed = _inputManager is InputManagerCompat compat && compat.IsPadCommandPressed(cmd);
                bool pressed = searchBoxFocused ? padPressed : _inputManager.IsCommandPressed(cmd);
                if (!pressed)
                    continue;

                // Pads have no left/right, so a pad Enter on a value field steps the value
                // instead of applying (NX menus work the same way).
                if (padPressed && cmd == InputCommandType.Activate
                    && _searchFilterModal.FocusedField is SongSearchFilterModal.Field.MinLevel
                        or SongSearchFilterModal.Field.MaxLevel
                        or SongSearchFilterModal.Field.PlayedStatus
                        or SongSearchFilterModal.Field.SortBy
                        or SongSearchFilterModal.Field.SortDirection)
                    _searchFilterModal.StepFocusedValue();
                else
                    _searchFilterModal.HandleCommand(cmd);
            }

            if (searchBoxFocused)
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
                    else if (_tabs.ActiveTab == SongSelectionTab.AllSongs && _filteredView != null)
                    {
                        // Clear active filter so subsequent Back presses navigate normally.
                        // Drain remaining queued commands to prevent a stale second Back
                        // from navigating away or exiting the stage in the same frame.
                        // Gated by AllSongs: filter state is All-Songs browse state and
                        // must not be touched while on the Recent tab.
                        OnFilterReset(this, System.EventArgs.Empty);
                        return false;
                    }
                    else if (_tabs.ActiveTab == SongSelectionTab.AllSongs && _navigationStack.Count > 0)
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
                    _configManager?.AdjustScrollSpeed(+1);
                    break;

                case InputCommandType.DecreaseScrollSpeed:
                    _configManager?.AdjustScrollSpeed(-1);
                    break;

                case InputCommandType.OpenSearch:
                    OpenSearchFilterModal();
                    // Modal opened -> stop draining remaining queued commands so a queued
                    // Activate/Back does not fire against the song list behind the modal on
                    // the same frame (matches the raw-Backspace guard at lines 1094-1095).
                    // Search unavailable/null (e.g. off All Songs) -> keep processing
                    // later commands normally.
                    return _searchFilterModal?.IsOpen != true;

            }

            return true;
        }

        private void OpenSearchFilterModal()
        {
            if (_tabs.ActiveTab != SongSelectionTab.AllSongs) return;
            if (_searchFilterModal == null) return;
            _isInStatusPanel = false;
            // Reset mouse edge-detection state so the first click after the modal
            // opens is not swallowed (e.g. if the previous modal session ended
            // with a button press that left _previousMouseState as Pressed).
            _previousMouseState = null;
            // The library is ready once this activation's initializer terminal applied
            // (HPA-799) and a projection exists.
            _searchFilterModal.IsLibraryReady =
                _currentSongList != null
                && _libraryInitializationCompleted;
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

                            // Backward cycling bypasses the display's DifficultyChanged
                            // event, so route through the same prepared cleanup as
                            // OnDifficultyChanged (HPA-802 review).
                            ClearPreparedSelectionIfDifficultyLeftIt(_selectedSong, _currentDifficulty);

                            // Update status panel
                            int playSpeedPercent = SynchronizeActivePlaySpeed();
                            _statusPanel.UpdateSongInfo(
                                _selectedSong,
                                _currentDifficulty,
                                playSpeedPercent);
                            _playHistoryPanel?.UpdateSongInfo(
                                _selectedSong,
                                _currentDifficulty,
                                playSpeedPercent);

                            // Difficulty changed - play navigation sound
                            PlayCursorMoveSound();
                        }
                    }
                }
            }

            AssertPreparedSelectionStatePairing();
        }

        /// <summary>
        /// Tab-bar label color: active resolves "SongSelect.TabActive" → "UI.Accent"
        /// → NX white; inactive resolves "SongSelect.TabInactive" → NX gray.
        /// </summary>
        internal static Color ResolveTabColor(bool active, ISkinTheme theme) => active
            ? theme.GetColor("SongSelect.TabActive",
                theme.GetColor("UI.Accent", SongSelectionUILayout.Tabs.ActiveColor))
            : theme.GetColor("SongSelect.TabInactive", SongSelectionUILayout.Tabs.InactiveColor);

        // The shared baked 16px UI font cannot be resized by LoadFont; scale only this label's ink.
        private sealed class PlaybackProfileLabel : UILabel
        {
            protected override void DrawText(SpriteBatch spriteBatch, SpriteFont font, string text, Vector2 position, Color color)
            {
                spriteBatch.DrawString(font, text, position, color, 0f, Vector2.Zero,
                    SongSelectionUILayout.UILabels.PlaybackProfile.TextScale, SpriteEffects.None, 0f);
            }
        }

        /// <summary>
        /// Creates the invariant user-facing playback-profile label.
        /// </summary>
        internal static string CreatePlaybackProfileText(
            int playSpeedPercent,
            int pitchSemitones)
            => PlaybackProfileFormatter.Format(playSpeedPercent, pitchSemitones);

        /// <summary>
        /// Copies the current config profile to every score-aware song-selection component
        /// and the profile label, then returns the canonical speed for explicit score lookups.
        /// </summary>
        private int SynchronizeActivePlaySpeed()
        {
            var config = _configManager?.Config;
            int playSpeedPercent = PlaySpeedRange.SnapAndClamp(
                config?.PlaySpeedPercent ?? PlaySpeedRange.Default);
            int pitchSemitones = PitchRange.SnapAndClamp(
                config?.PitchSemitones ?? PitchRange.Default);

            if (_songListDisplay != null)
                _songListDisplay.PlaySpeedPercent = playSpeedPercent;
            if (_statusPanel != null)
                _statusPanel.PlaySpeedPercent = playSpeedPercent;
            if (_playHistoryPanel != null)
                _playHistoryPanel.PlaySpeedPercent = playSpeedPercent;
            if (_playbackProfileLabel != null)
            {
                _playbackProfileLabel.Text = CreatePlaybackProfileText(
                    playSpeedPercent,
                    pitchSemitones);
                // UILabel auto-sizes when text changes and a SpriteFont is present.
                // Reassert the authored bounds so runtime refreshes retain the
                // centralized SongSelectionUILayout contract.
                _playbackProfileLabel.Size =
                    SongSelectionUILayout.UILabels.PlaybackProfile.Size;
            }

            return playSpeedPercent;
        }

        /// <summary>
        /// Optional display font family for the tab-bar labels
        /// ("All Songs" / "Recent" / "Bookmarks"): "SongSelect.TabFontFamily",
        /// empty = the shared UI font (NX behavior).
        /// </summary>
        internal static string ResolveTabFontFamily(ISkinTheme theme) =>
            theme.GetString("SongSelect.TabFontFamily", string.Empty);

        /// <summary>
        /// Point size for the tab font: "SongSelect.TabFontSize" → shared UI size.
        /// </summary>
        internal static int ResolveTabFontSize(ISkinTheme theme) =>
            theme.GetInt("SongSelect.TabFontSize", SongSelectionUILayout.Background.DefaultFontSize);

        /// <summary>
        /// Face for the recent-plays rows: "SongSelect.HistoryFontFamily" → empty,
        /// i.e. the shared UI font, as NX does. Those rows are date/result/score
        /// columns, so a monospace face keeps them aligned.
        /// </summary>
        internal static string ResolveHistoryFontFamily(ISkinTheme theme) =>
            theme.GetString("SongSelect.HistoryFontFamily", string.Empty);

        internal static int ResolveHistoryFontSize(ISkinTheme theme) =>
            theme.GetInt("SongSelect.HistoryFontSize", SongSelectionUILayout.Background.DefaultFontSize);

        /// <summary>
        /// Draw scale for the rows. NX shrinks the shared UI font to 0.8; a skin
        /// supplying a font baked at the right size draws it at 1.0.
        /// </summary>
        internal static float ResolveHistoryTextScale(ISkinTheme theme) =>
            theme.GetFloat("SongSelect.HistoryTextScale", SongSelectionUILayout.PlayHistoryPanel.FontScale);

        /// <summary>
        /// Color for transient status/empty-state messages ("No bookmarks yet",
        /// "No songs match this filter", ...): "SongSelect.StatusText" → NX LightGray.
        /// </summary>
        internal static Color ResolveStatusTextColor(ISkinTheme theme) =>
            theme.GetColor("SongSelect.StatusText", Color.LightGray);

        private ISkinTheme CurrentTheme => _resourceManager?.CurrentTheme ?? SkinTheme.Empty;

        private void DrawTabBar()
        {
            float x = SongSelectionUILayout.Tabs.X;
            float y = SongSelectionUILayout.Tabs.Y;
            var theme = CurrentTheme;
            var font = _tabFont ?? _font;

            foreach (SongSelectionTab tab in AllTabs)
            {
                string label = tab.DisplayLabel();
                var color = ResolveTabColor(tab == _tabs.ActiveTab, theme);

                font.DrawString(_spriteBatch, label, new Vector2(x, y), color);

                var size = font.MeasureString(label);
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

            // HPA-796: acquire the cached white pixel once per activation from the
            // ResourceManager cache (survives skin switches) instead of allocating a
            // private 1×1 texture.
            _whitePixelResource = _resourceManager.CreateTextureFromColor(Color.White);
            _whitePixel = _whitePixelResource?.Texture;
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

            // Use RenderTargetManager to properly dispose RenderTarget
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
                    _previewController.LoadDelayed(absolutePreviewPath);
                }
            }
            catch (Exception)
            {
                // Clear the failed sound reference and any armed delay.
                _previewController.Stop();
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
                // Cursor sound failed, continue
            }
        }

        private static void ReleaseManagedSound(ref ISound sound)
        {
            sound?.RemoveReference();
            sound = null;
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
        /// Cycles to the next tab. Invoked by the Tab key and the Floor Tom pad (wired
        /// in the input-handling path); tab state, loads and refresh requests are the
        /// tabs owner's, sounds and status-panel exit stay here.
        /// </summary>
        private void SwitchToNextTab() => SwitchToTab(_tabs.ActiveTab.Next());

        private void SwitchToTab(SongSelectionTab tab)
        {
            _isInStatusPanel = false;
            _tabs.SwitchToTab(tab);
            PlayCursorMoveSound();
        }

        #endregion

        #region Bookmark Toggle

        /// <summary>
        /// Toggles the bookmark on the highlighted song (stage input policy: only a
        /// selected song node qualifies; the flag flip, persistence, reconciliation and
        /// row removal are the tabs owner's). No-op for non-song nodes.
        /// </summary>
        private void ToggleBookmarkForSelectedSong()
        {
            var node = _songListDisplay?.SelectedSong;
            if (node == null || node.Type != NodeType.Score)
                return;

            if (_tabs.ToggleBookmark(node))
                PlayCursorMoveSound();
        }

        #endregion

        #region Pad Lane Hits and Quick Settings

        private void OnPadLaneHit(object? sender, DTXMania.Game.Lib.Input.LaneHitEventArgs e)
        {
            // Only real pads (not keyboard drum keys), and never under the search modal,
            // matching the Tab-key path (suppressed by HandleInput's modal early-return).
            if (!e.Button.IsPressed || DTXMania.Game.Lib.Input.KeyBindings.IsKeyboardButtonId(e.Button.Id))
                return;
            if (_searchFilterModal != null && _searchFilterModal.IsOpen)
                return;

            HandlePadLaneHit(e.Lane);
        }

        /// <summary>
        /// Song-select pad actions on top of <see cref="DrumPadSystemKeys"/> (which covers
        /// up/down/enter/back), following NX: floor tom cycles tabs like Tab and floor tom x2
        /// opens Search &amp; Filter (NX's sort menu), hi-hat x2 steps the difficulty up (it
        /// wraps, so there is no down), bass drum x2 opens or closes Quick Settings. While
        /// Quick Settings is open only bass drum x2 does anything here.
        /// </summary>
        private void HandlePadLaneHit(int lane)
        {
            bool quickSettingsOpen = _quickSettingsPopup?.IsOpen == true;
            if (lane == FloorTomLaneIndex && !quickSettingsOpen)
            {
                if (!IsPadDoubleHit(lane))
                {
                    SwitchToNextTab();
                }
                else
                {
                    // The first hit already moved a tab; search lives on All Songs.
                    if (_tabs.ActiveTab != SongSelectionTab.AllSongs)
                        SwitchToTab(SongSelectionTab.AllSongs);
                    OpenSearchFilterModal();
                }
            }
            else if (lane == HiHatLaneIndex && !quickSettingsOpen)
            {
                if (IsPadDoubleHit(lane))
                    CycleDifficulty(1);
            }
            else if (lane == BassDrumLaneIndex && IsPadDoubleHit(lane))
            {
                if (quickSettingsOpen)
                    _quickSettingsPopup!.Close();
                else
                    OpenQuickSettings();
            }
        }

        /// <summary>
        /// True when this hit and the previous double-hit pad hit are the same pad within
        /// <see cref="PadDoubleHitWindowSeconds"/>. A completed double hit starts over, so a
        /// third hit does not fire again.
        /// </summary>
        private bool IsPadDoubleHit(int lane)
        {
            double sinceLastHit = _elapsedTime - _lastDoubleHitPadTime;
            bool isDoubleHit = _lastDoubleHitPadLane == lane
                && sinceLastHit >= 0 && sinceLastHit <= PadDoubleHitWindowSeconds;
            _lastDoubleHitPadLane = isDoubleHit ? -1 : lane;
            _lastDoubleHitPadTime = _elapsedTime;
            return isDoubleHit;
        }

        private void OpenQuickSettings()
        {
            if (_quickSettingsPopup == null || _configManager == null)
                return;

            _isInStatusPanel = false;
            _quickSettingsPlaySpeedAtOpen = _configManager.Config.PlaySpeedPercent;
            _quickSettingsPopup.Open(QuickSettingsPopup.CreateItems(
                _configManager,
                () => FfmpegRuntime.EnsureConfigured().IsAvailable));
            _inputManager?.ClearPendingCommands();
            PlayCursorMoveSound();
        }

        private void OnQuickSettingsClosed(object? sender, EventArgs e)
        {
            try
            {
                _configManager?.FlushPendingSave();
            }
            catch (Exception)
            {
            }

            int playSpeedPercent = SynchronizeActivePlaySpeed();
            if (playSpeedPercent != _quickSettingsPlaySpeedAtOpen && !_filterCriteria.IsEmpty)
            {
                // Played/unplayed filters read the scores for the active speed.
                RebuildFilteredView();
                if (_tabs.ActiveTab == SongSelectionTab.AllSongs)
                    PopulateSongListForCurrentMode();
            }
            _statusPanel?.UpdateSongInfo(_selectedSong, _currentDifficulty, playSpeedPercent);
            _playHistoryPanel?.UpdateSongInfo(_selectedSong, _currentDifficulty, playSpeedPercent);
            _inputManager?.ClearPendingCommands();
            _inputManager?.ResetKeyRepeatStates();
        }

        private void SubscribePadLaneHits()
        {
            if (_inputManager is DTXMania.Game.Lib.Input.InputManagerCompat compat
                && compat.ModularInputManager != null)
            {
                // Defensive: remove before adding so re-Activate never double-subscribes.
                compat.ModularInputManager.OnLaneHit -= OnPadLaneHit;
                compat.ModularInputManager.OnLaneHit += OnPadLaneHit;
            }
        }

        private void UnsubscribePadLaneHits()
        {
            if (_inputManager is DTXMania.Game.Lib.Input.InputManagerCompat compat
                && compat.ModularInputManager != null)
            {
                compat.ModularInputManager.OnLaneHit -= OnPadLaneHit;
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

        #region Telemetry

        public void PopulateTelemetry(GameTelemetrySnapshot telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);

            telemetry.SelectedSongTitle = _selectedSong?.DisplayTitle ?? _selectedSong?.Title;
            telemetry.SelectedDifficulty = _currentDifficulty;
            telemetry.InStatusPanel = _isInStatusPanel;
            var config = _configManager?.Config ?? _game?.ConfigManager?.Config;
            telemetry.PlaySpeedPercent = PlaySpeedRange.SnapAndClamp(
                config?.PlaySpeedPercent ?? PlaySpeedRange.Default);
            telemetry.PitchSemitones = PitchRange.SnapAndClamp(
                config?.PitchSemitones ?? PitchRange.Default);
            telemetry.PlaybackProfileFrozen = false;
            telemetry.PreparedChartIdentity = _preparedChartSelection?.TelemetryIdentity;
            telemetry.PreparedPreviewState = _previewController.PreparedState.ToString();
            telemetry.PreparedPreviewElapsedMs = _previewController.PlayedMs;
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
