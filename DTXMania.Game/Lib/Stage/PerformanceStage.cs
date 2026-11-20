#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Notation;
using DTXMania.Game.Lib.Utilities;
// The UI layout namespace also declares a MeasureLine class; notation inputs use the chart-component one.
using MeasureLine = DTXMania.Game.Lib.Song.Components.MeasureLine;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Performance stage for playing songs with the 10-lane GITADORA XG layout.
    /// This class coordinates all gameplay components including timing, scoring, and visual feedback.
    /// Based on DTXManiaNX performance screen patterns.
    /// </summary>
    /// <remarks>
    /// The PerformanceStage manages the complete gameplay experience through several phases:
    /// 1. Initialization - Setup components and load chart data
    /// 2. Ready countdown - Brief preparation period before song starts
    /// 3. Active gameplay - Note scrolling, input processing, and judgement
    /// 4. Stage completion - Results calculation and transition to next stage
    /// 
    /// The stage uses an event-driven architecture where JudgementManager raises
    /// JudgementMade events that GameplayRules forwards to the gameplay managers,
    /// followed by stage visual feedback.
    /// </remarks>
    public class PerformanceStage : BaseStage, IStageTelemetryProvider
    {
        #region Private Fields

        private SpriteBatch _spriteBatch;
        private IResourceManager _resourceManager;
        private UIManager _uiManager;
        private InputManagerCompat _inputManager;

        // Stage data - initialized via ExtractSharedData
        private SongListNode _selectedSong = null!;
        private int _selectedDifficulty;

        // Performance components - initialized in InitializeComponents
        private BackgroundRenderer _backgroundRenderer = null!;
        private LaneBackgroundRenderer _laneBackgroundRenderer = null!;
        private JudgementLineRenderer _judgementLineRenderer = null!;
        private ScoreDisplay _scoreDisplay = null!;
        private ComboDisplay _comboDisplay = null!;

        // Phase 2 components - Chart loading and note scrolling
        private ParsedChart _parsedChart = null!;
        private NoteRenderer _noteRenderer = null!;

        // Phase 3 components - Gameplay managers
        private SkillPanelDisplay? _skillPanelDisplay;
        private SkillMeterDisplay? _skillMeterDisplay;
        private TabHudDisplay? _tabHudDisplay;
        private NxAttackEffectManager _nxAttackEffectManager = null!;
        private JudgementTextPopupManager _fontJudgementTextPopupManager = null!;
        private SpriteJudgementTextPopupManager _spriteJudgementTextPopupManager = null!;
        private HitTimingFeedbackDisplay _hitTimingFeedbackDisplay = null!;
        private PadRenderer _padRenderer = null!;

        // HPA-11 chart background video. The player is game-owned and non-blocking;
        // scheduling keeps one cursor into the chart's sorted VideoEvents plus the
        // event whose media generation is currently active.
        private IChartVideoPlayer _chartVideoPlayer = null!;
        private int _nextVideoEventIndex;
        private ChartVideoEvent? _activeVideoEvent;

        // Gameplay audio session (HPA-800): owns the prepared audio set, audio
        // loader, song timer, chip-sound cache, scheduled/active BGM playback
        // and the metronome. Bound on the update thread when background audio
        // preparation completes; null before that and after cleanup.
        private GameplayAudioSession? _audioSession;

        /// <summary>
        /// Bound gameplay audio session for this activation. The owned audio
        /// state and transport live inside it.
        /// </summary>
        internal GameplayAudioSession? AudioSession => _audioSession;

        private PlaybackModifiers _playbackModifiers = new(100, 0);
        private bool _metronomeEnabled;
        private AudioPreparationProgress? _audioPreparationProgress;
        private DateTime _audioPreparationStartedUtc;
        private string? _loadErrorMessage;

        // UX components
        private IFont _readyFont = null!;
        private IFont? _scrollSpeedFont;
        private ScrollSpeedIndicator? _scrollSpeedIndicator;
        private IConfigManager? _subscribedConfigManager;

        // Performance UI Assets - initialized in InitializeComponents
        private ITexture _backgroundTexture = null!;
        private ITexture _shutterTexture = null!; // Single shutter texture
        private ITexture _laneBgTexture = null!;
        private ITexture _laneDividerTexture = null!;
        private ITexture _laneFlashTexture = null!;
        private ITexture _judgementLineTexture = null!;
        private ITexture _gaugeBaseTexture = null!;
        private ITexture _gaugeFillTexture = null!;
        private ITexture _progressBaseTexture = null!;
        private ITexture _progressFillTexture = null!;
        private ITexture _comboDigitsTexture = null!;
        private ITexture _scoreDigitsTexture = null!;
        private ITexture _pauseOverlayTexture = null!;
        private ITexture _dangerOverlayTexture = null!;
        private ITexture _skillPanelTexture = null!;
        
        // Judgement text textures (using sprite sheets)
        private ITexture _judgeStringsTexture = null!;
        
        // Gameplay state
        private bool _isLoading = true;
        private bool _isReady = false;
        private double _readyCountdown = GameConstants.Performance.ReadyCountdownSeconds;
        private GameTime _currentGameTime = null!;
        private double _totalTime = 0.0;
        private double _stageElapsedTime = 0.0; // Track elapsed time since stage activation for miss detection
        // Input and lifecycle changes are captured together in the published snapshot.
        internal sealed record LastLaneHit(int Lane, string? ButtonId, double SongTimeMs);
        private LastLaneHit? _lastLaneHit;
        private PublishedTelemetry _publishedTelemetry = new();
        private Texture2D? _fallbackWhiteTexture;

        // HPA-796: the stage's cached white pixel (ITexture) acquired once per activation;
        // _fallbackWhiteTexture above is its borrowed raw texture.
        private ITexture? _fallbackWhiteTextureResource;
        private Action<Rectangle, Color, float>? _fallbackRectangleDrawer = null;
        
        // UI state tracking
        private bool _isPaused = false;
        private bool _isDanger = false;
        private float _currentGaugeValue = 0.5f; // 0.0 to 1.0
        private float _currentProgressValue = 0.0f; // 0.0 to 1.0

        private bool _inputPaused = false;
        
        internal GameplayRules? Rules { get; private set; }
        private IReadOnlySet<int> FrozenAutoPlayLanes =>
            Rules?.AutoPlayLanes ?? FrozenSet<int>.Empty;
        private PerformanceVisualGates _visualGates;

        private readonly record struct PerformanceVisualGates(
            bool HideLaneBackground,
            bool HideMeasureLines,
            bool HideJudgementLine,
            bool HideCombo,
            bool EnableLaneFlush,
            bool ShowHitTimingFeedback,
            DrumsNoteDisplayStyle RequestedNoteDisplayStyle);

        // HPA-411 Tab mode. The pure notation chart is built once on the activation
        // worker (when the requested style is Tab) and published together with the
        // parsed chart BEFORE audio preparation starts (ApplyParsedChart), so the
        // right playfield is on screen for the whole LOADING phase; the renderer
        // (which owns the font loads) is constructed exactly once on the update
        // thread there. The effective style is derived from the _notationStrip
        // reference alone — non-null means Tab, null means Lanes; a failed attempt
        // means Lanes for the rest of the song.
        private NotationStripRenderer? _notationStrip;

        // Skin art borrowed by the strip and the Tab HUD; released on cleanup.
        private TabModeArt? _tabModeArt;

        // HPA-509: the Tab HUD draws only while both the strip and its own resources
        // are live. A HUD whose fonts/primitive failed to load rolls the strip back
        // to Lanes in TryConstructNotationStrip, so a false read here already means
        // the consistent Lanes stack — never a strip under Lanes widgets.
        private bool IsTabHudActive => _notationStrip != null && _tabHudDisplay?.IsReady == true;
        
        // Note: Using global stage transition debouncing from BaseGame

        #endregion

        #region Constants

        /// <summary>HPA-11 chart background video depth: above the static background
        /// (1.0) and below the lane strips (0.8).</summary>
        internal const float ChartVideoLayerDepth = 0.95f;

        #endregion

        #region Properties

        public override StageType Type => StageType.Performance;

        #endregion

        #region Constructor

        public PerformanceStage(IStageGame game) : base(game)
        {
            // Initialize core systems
            _spriteBatch = CreateSpriteBatch(game.GraphicsDevice);
            _resourceManager = game.ResourceManager;
            _uiManager = new UIManager();
            _inputManager = game.InputManager;
        }

        /// <summary>
        /// Resolves the stage-independent processor factory used when a
        /// non-default playback profile requires prepared audio variants. The
        /// virtual is invoked on the update thread before the background
        /// worker launches; the returned delegate must not capture mutable
        /// stage state, so the worker can invoke it (construction is I/O-heavy
        /// and must stay off the update thread, after chart publication).
        /// </summary>
        protected virtual Func<IAudioVariantProcessor> CreateAudioVariantProcessorFactory()
            => () => new FfmpegAudioVariantProcessor();

        /// <inheritdoc cref="CreateAudioVariantProcessorFactory"/>
        protected virtual Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory()
            => () => new PlaybackAudioVariantCache();

        /// <summary>
        /// Creates the HPA-11 chart background video player. Extracted as a seam so
        /// headless tests can substitute a recording fake instead of launching FFmpeg.
        /// Generation failures are routed through the stage's Console-based error log,
        /// matching how the song timer and metronome report diagnostics.
        /// </summary>
        [ExcludeFromCodeCoverage]
        protected virtual IChartVideoPlayer CreateChartVideoPlayer()
            => new FfmpegChartVideoPlayer(
                _spriteBatch.GraphicsDevice,
                message => LogPerformanceError(message));

        /// <summary>
        /// Creates the chart video player and resets scheduling state. Extracted so
        /// the GPU-dependent creation can be excluded from headless coverage.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private void InitializeChartVideoPlayer()
        {
            _chartVideoPlayer = CreateChartVideoPlayer();
            _nextVideoEventIndex = 0;
            _activeVideoEvent = null;
        }

        /// <summary>
        /// Creates the <see cref="SpriteBatch"/> used by this stage. Extracted as a seam so
        /// headless tests can override it (returning null) instead of constructing a real
        /// SpriteBatch, whose internal <see cref="GraphicsResource"/> finalizers crash on an
        /// uninitialized <see cref="GraphicsDevice"/>.
        /// </summary>
        [ExcludeFromCodeCoverage]
        protected virtual SpriteBatch? CreateSpriteBatch(GraphicsDevice graphicsDevice)
            => graphicsDevice != null ? new SpriteBatch(graphicsDevice) : null;

        #endregion

        #region BaseStage Implementation

        protected override void OnActivate()
        {
            var config = _game.ConfigManager.Config;
            _metronomeEnabled = config.Metronome;
            _playbackModifiers = new PlaybackModifiers(
                PlaySpeedRange.SnapAndClamp(config.PlaySpeedPercent),
                PitchRange.SnapAndClamp(config.PitchSemitones));
            _loadErrorMessage = null;
            _audioPreparationProgress = null;
            _audioPreparationStartedUtc = DateTime.UtcNow;
            // Extract shared data from stage transition
            ExtractSharedData();

            // Freeze run configuration from config
            FreezeRunConfiguration();
            PublishTelemetry();

            // Initialize UI components
            InitializeComponents();

            // Start chart/audio preparation on the activation's background-work scope;
            // every activation input is snapshotted on this (update) thread before Run.
            StartGameplayInitialization();

            var configManager = _game?.ConfigManager;
            if (configManager != null)
            {
                _subscribedConfigManager = configManager;
                configManager.ScrollSpeedChanged += OnScrollSpeedChanged;
            }
        }

        protected override void OnDeactivate()
        {
            // BaseStage.Deactivate already retired the activation's background-work
            // scope synchronously before this override, so no result can reach the
            // components during teardown.
            if (_subscribedConfigManager != null)
            {
                _subscribedConfigManager.ScrollSpeedChanged -= OnScrollSpeedChanged;
                _subscribedConfigManager.FlushPendingSave();
                _subscribedConfigManager = null;
            }

            CleanupComponents();
            PublishTelemetry();
        }

        protected override void OnUpdate(double deltaTime)
        {
            var scope = BackgroundWork;
            // Update total time for precise GameTime tracking
            _totalTime += deltaTime;

            // Track elapsed time since stage activation for miss detection
            _stageElapsedTime += deltaTime;

            // Create GameTime for precise timing
            _currentGameTime = new GameTime(TimeSpan.FromSeconds(_totalTime), TimeSpan.FromSeconds(deltaTime));

            // Handle input
            HandleInput();
            if (!ReferenceEquals(scope, BackgroundWork))
                return;

            // Update UI manager
            _uiManager?.Update(deltaTime);

            // Update performance components
            UpdateComponents(deltaTime);

            // Update scroll-speed indicator toast
            _scrollSpeedIndicator?.Update(_currentGameTime);

            // Update gameplay state
            UpdateGameplay(deltaTime);
            if (!ReferenceEquals(scope, BackgroundWork))
                return;

            // Update song timer
            _audioSession?.UpdateTimer(_currentGameTime);
            if (scope != null && ReferenceEquals(scope, BackgroundWork))
                PublishTelemetry();
        }


        [ExcludeFromCodeCoverage]
        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;


            // BackToFront depth order (larger depth = farther back):
            //   Background (1.0f) → chart video (0.95f) → strip background (0.90f) →
            //   Lanes (0.8f) / strip staff+barlines (0.80f) → Measure lines (0.78f) →
            //   strip hit marker (0.72f) → fallback notes (0.70f) →
            //   strip beams/stems (0.65f) → JudgementLine (0.6f) → strip glyphs (0.55f) →
            //   strip key mask (0.40f) → strip labels (0.30f) → Pads (0.1f) →
            //   sprite notes (0.05f).
            // The strip layers come from PerformanceUILayout.NotationStrip; the chart
            // video (0.95f) must stay behind the strip's opaque background (0.90f) so
            // BGA video can never paint over the notation.

            // Base pass: Background → playfield (Lanes or Tab strip) → Judgement Texts
            _spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend);

            // Draw background (furthest back - highest depth value)
            DrawBackground();

            // HPA-411 Tab: the notation strip replaces the whole lane playfield — lane
            // backgrounds, measure lines, pads, falling notes and the judgement line
            // draw only in Lanes (same song clock and live scroll speed either way).
            if (_notationStrip != null)
            {
                DrawNotationStrip();

                // HPA-509: key flashes belong in the base BackToFront batch, directly
                // after the strip, at DepthKeyFlash (0.35) so they render behind the
                // strip labels (0.30) but above the key mask (0.40).
                _tabHudDisplay?.DrawKeyFlashes(_spriteBatch);
            }
            else
            {
                // Draw lane backgrounds
                DrawLaneBackgrounds();

                // Draw scrolling measure boundaries above lanes and behind gameplay objects
                DrawMeasureLines();

                // Draw pad indicators (above lane backgrounds, below notes)
                DrawPads();

                // Draw scrolling notes
                DrawNotes();

                // Draw judgement line
                DrawJudgementLine();
            }

            // Draw judgement text popups
            DrawJudgementTexts();

            _spriteBatch.End();

            // Overlay pass: Note overlays with alpha blending (above notes, below UI)
            _spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend);

            // Draw note overlay animations with alpha blending (on top of base animations)
            // HPA-411 Tab: falling-note overlays do not apply to the notation strip.
            if (_notationStrip == null)
                DrawNoteOverlays();

            _spriteBatch.End();

            // UI pass: UI elements on top of everything else
            _spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend);

            // Draw UI elements (gauge, score, combo)
            DrawUIElements();

            // Draw ready state or loading indicator
            DrawGameplayState();

            // Draw scroll-speed indicator toast (on top of everything via depth 0.0)
            _scrollSpeedIndicator?.Draw(_spriteBatch);

            _spriteBatch.End();

            // Effects pass: Hit effects with additive blending (drawn on top of everything)
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);

            // Draw hit effects with additive blending — sparks land at lane positions,
            // so they apply only in Lanes mode and never over the notation strip.
            if (_notationStrip == null)
                DrawHitEffects();

            _spriteBatch.End();
        }

        #endregion

        #region Initialization and Cleanup

        private void ExtractSharedData()
        {
            if (_transitionPayload is SongPlayRequest request)
            {
                _selectedSong = request.Song;
                _selectedDifficulty = request.Difficulty;
                _parsedChart = request.Chart;
            }
        }

        private void FreezeRunConfiguration()
        {
            var config = _game?.ConfigManager?.Config;
            var laneDisplayMode = config?.LaneDisplayMode ?? DrumsLaneDisplayMode.AllOn;
            var noteDisplayStyle = config?.NoteDisplayStyle ?? DrumsNoteDisplayStyle.Lanes;
            _visualGates = new PerformanceVisualGates(
                HideLaneBackground: !laneDisplayMode.ShowsLaneBackground(),
                HideMeasureLines: !laneDisplayMode.ShowsMeasureLines(),
                HideJudgementLine: !(config?.ShowJudgementLine ?? true),
                HideCombo: !(config?.ShowCombo ?? true),
                EnableLaneFlush: config?.EnableLaneFlush ?? false,
                ShowHitTimingFeedback: config?.ShowHitTimingFeedback ?? false,
                RequestedNoteDisplayStyle: noteDisplayStyle);

            Rules = new GameplayRules(config);
        }

        private void InitializeComponents()
        {
            if (_spriteBatch == null)
            {
                WireUpInputEventHandlers();
                return;
            }

            // HPA-796: acquire the cached white pixel BEFORE constructing renderers —
            // JudgementLineRenderer (and later the Tab HUD) borrow its raw texture.
            _fallbackWhiteTextureResource = _resourceManager.CreateTextureFromColor(Color.White);
            _fallbackWhiteTexture = _fallbackWhiteTextureResource?.Texture!;

            // Initialize background renderer
            _backgroundRenderer = new BackgroundRenderer(_resourceManager);

            // Start async background loading
            _ = _backgroundRenderer.LoadBackgroundAsync();

            // Initialize lane background and judgement line renderers
            var graphicsDevice = _spriteBatch.GraphicsDevice;
            _laneBackgroundRenderer = new LaneBackgroundRenderer(_resourceManager);
            _judgementLineRenderer = new JudgementLineRenderer(graphicsDevice, _fallbackWhiteTexture!);

            // Initialize score and combo displays
            _scoreDisplay = new ScoreDisplay(_resourceManager, graphicsDevice);
            _comboDisplay = new ComboDisplay(_resourceManager, graphicsDevice);

            // GameplayRules initializes SkillManager once the chart and combo exist.
            var skillChart = _selectedSong?.GetCurrentDifficultyChart(_selectedDifficulty);
            // The difficulty badge name comes from the selected song-select slot (the chart entity has
            // none once loaded from the DB). This is the same label the song-select panel shows.
            string? difficultyLabel = null;
            var labels = _selectedSong?.DifficultyLabels;
            if (labels != null && _selectedDifficulty >= 0 && _selectedDifficulty < labels.Length)
            {
                difficultyLabel = labels[_selectedDifficulty];
            }
            _skillPanelDisplay = new SkillPanelDisplay(_resourceManager, graphicsDevice, skillChart, difficultyLabel);
            _skillMeterDisplay = new SkillMeterDisplay(_resourceManager, graphicsDevice);

            // HPA-509: the Tab-mode HUD is built later, inside TryConstructNotationStrip —
            // only once the strip has actually published, so Lanes songs never pay for
            // its fonts or borrow the shared white pixel (it exists by then).

            // Initialize Phase 2 components
            _noteRenderer = new NoteRenderer(graphicsDevice, _resourceManager);
            _nxAttackEffectManager = new NxAttackEffectManager(_resourceManager);
            _fontJudgementTextPopupManager = new JudgementTextPopupManager(graphicsDevice, _resourceManager);
            _spriteJudgementTextPopupManager = new SpriteJudgementTextPopupManager(
                _resourceManager,
                judgementEvent => _fontJudgementTextPopupManager?.SpawnPopup(judgementEvent));
            _hitTimingFeedbackDisplay = new HitTimingFeedbackDisplay(_resourceManager);
            _padRenderer = new PadRenderer(graphicsDevice, _resourceManager);

            // HPA-11: chart background video player; scheduling state resets per activation.
            InitializeChartVideoPlayer();

            // Initialize UX components
            InitializeReadyFont();
            
            // Load performance UI assets using DTXManiaNX layout
            LoadPerformanceUIAssets();

            // HPA-796: the fallback white pixel was already acquired at the top of this
            // method (before the renderers that borrow it); no private 1×1 texture here.

            // Wire pad feedback now, before preparation starts: OnLaneHitForPadFeedback
            // explicitly supports loading/countdown, so manual hits light pads while the
            // chart/audio prepare (and after a failed preparation). CleanupGameplayManagers
            // unsubscribes on teardown, keeping the lifecycle balanced.
            WireUpInputEventHandlers();
        }

        // HPA-509: the three outcomes of a Tab HUD build. NotAttempted means no
        // GraphicsDevice existed to try with (headless coverage) and the strip
        // keeps its publish; Ready keeps the publish; Failed — the HUD threw or
        // acquired no resources — rolls the strip back so the song plays as a
        // consistent full-Lanes fallback instead of a strip under Lanes widgets.
        private enum TabHudBuildResult { NotAttempted, Ready, Failed }

        // HPA-509: build the Tab HUD once the strip has published. Deliberately
        // outside the strip's own try/catch so a HUD failure reports cleanly; the
        // caller decides the strip's fate from the result. Needs a live
        // GraphicsDevice — the attempt itself is unreachable under headless
        // coverage like the other renderer constructions in InitializeComponents.
        [ExcludeFromCodeCoverage]
        private TabHudBuildResult ConstructTabHud(SkinPalette palette, TabModeArt art)
        {
            var graphicsDevice = _spriteBatch?.GraphicsDevice;
            if (graphicsDevice == null)
            {
                LogPerformanceError(
                    "PerformanceStage: no GraphicsDevice for the Tab HUD; the strip stays published for this song.");
                return TabHudBuildResult.NotAttempted;
            }

            try
            {
                _tabHudDisplay = new TabHudDisplay(
                    graphicsDevice,
                    _selectedSong?.GetCurrentDifficultyChart(_selectedDifficulty),
                    _selectedSong?.DisplayTitle ?? _selectedSong?.Title,
                    _selectedSong?.DatabaseSong?.DisplayArtist,
                    _fallbackWhiteTexture,
                    palette,
                    art,
                    logger: _game.LoggerFactory?.CreateLogger<TabHudDisplay>()
                        ?? NullLogger<TabHudDisplay>.Instance);
                return _tabHudDisplay.IsReady
                    ? TabHudBuildResult.Ready
                    : TabHudBuildResult.Failed;
            }
            catch (Exception ex)
            {
                LogPerformanceError(
                    "PerformanceStage: Tab HUD construction failed.",
                    ex);
                _tabHudDisplay = null;
                return TabHudBuildResult.Failed;
            }
        }

        private void CleanupComponents()
        {

            // Reset state variables to initial values for proper reactivation
            _isLoading = true; // Initial state is loading
            _isReady = false; // Initial state is not ready
            Rules?.ResetCompletion();
            _inputPaused = false; // Initial state is input enabled
            _totalTime = 0.0; // Reset total time
            _stageElapsedTime = 0.0; // Reset elapsed time for miss detection
            _readyCountdown = 1.0; // Reset ready countdown
            Rules?.ResetAutoPlay();
            // Clear cached last-hit telemetry so a reactivated stage does not inherit the previous
            // song's hit data; PopulateTelemetry only reports fresh values once new hits land.
            _lastLaneHit = null;

            // Cleanup background renderer
            _backgroundRenderer?.Dispose();
            _backgroundRenderer = null;

            // Cleanup lane background and judgement line renderers
            _laneBackgroundRenderer?.Dispose();
            _laneBackgroundRenderer = null;
            _judgementLineRenderer?.Dispose();
            _judgementLineRenderer = null;

            // HPA-411: clear the strip reference BEFORE disposing the renderer — the
            // reference is the effective-mode flag, so it must read null before Dispose
            // runs.
            NotationStripRenderer? notationStrip = _notationStrip;
            _notationStrip = null;
            notationStrip?.Dispose();
            _tabModeArt?.Release();
            _tabModeArt = null;

            // Cleanup score and combo displays
            _scoreDisplay?.Dispose();
            _scoreDisplay = null;
            _comboDisplay?.Dispose();
            _comboDisplay = null;
            _skillPanelDisplay?.Dispose();
            _skillPanelDisplay = null;
            _skillMeterDisplay?.Dispose();
            _skillMeterDisplay = null;
            _tabHudDisplay?.Dispose();
            _tabHudDisplay = null;


            // Cleanup Phase 2 components
            _noteRenderer?.Dispose();
            _noteRenderer = null;
            _nxAttackEffectManager?.Dispose();
            _nxAttackEffectManager = null;
            _spriteJudgementTextPopupManager?.Dispose();
            _spriteJudgementTextPopupManager = null;
            _fontJudgementTextPopupManager?.Dispose();
            _fontJudgementTextPopupManager = null;
            _hitTimingFeedbackDisplay?.Dispose();
            _hitTimingFeedbackDisplay = null;
            _padRenderer?.Dispose();
            _padRenderer = null;

            // Cleanup UX components
            _readyFont?.RemoveReference();
            _readyFont = null;
            _scrollSpeedFont?.RemoveReference();
            _scrollSpeedFont = null;

            // Cleanup performance UI assets
            CleanupPerformanceUIAssets();

            // Cleanup fallback texture — released only after every borrowing child
            // renderer above was disposed.
            _fallbackWhiteTextureResource?.RemoveReference();
            _fallbackWhiteTextureResource = null;
            _fallbackWhiteTexture = null!;

            // Cleanup gameplay managers
            CleanupGameplayManagers();

            // Release the gameplay audio session: song timer, audio loader,
            // live BGM instances, chip cache, metronome sound references and
            // the owned prepared audio set.
            _audioSession?.Dispose();
            _audioSession = null;

            // Clear chart data
            _parsedChart = null;
            Rules?.ClearChart();

            // Cleanup HPA-11 chart background video player
            _chartVideoPlayer?.Dispose();
            _chartVideoPlayer = null;
            _activeVideoEvent = null;
            _nextVideoEventIndex = 0;

        }

        #endregion

        #region Input Handling

        private void HandleInput()
        {
            if (_inputManager == null)
                return;

            // Check for back action (ESC key or controller Back button) using consolidated method with debounce
            if (_inputManager.IsBackActionTriggered())
            {
                if (_game.CanPerformStageTransition())
                {
                    _game.MarkStageTransition();
                    ReturnToSongSelect();
                }
            }


            // Only process gameplay input when song is actively playing (not during loading or ready countdown)
            if (_audioSession?.IsPlaying == true && !_inputPaused && !_isLoading && !(_isReady && _readyCountdown > 0))
            {
                // Input manager is already being updated in OnUpdate(),
                // so we don't need to do anything special here.
                // The ModularInputManager will automatically trigger lane hit events
                // which the JudgementManager is subscribed to.
            }

            // Scroll-speed adjust hotkeys (PageUp/PageDown) — active throughout performance,
            // not gated on song playback so player can pre-adjust during the ready countdown.
            var configManager = _game?.ConfigManager;
            if (configManager != null)
            {
                if (_inputManager.IsCommandPressed(InputCommandType.IncreaseScrollSpeed))
                {
                    configManager.AdjustScrollSpeed(+1);
                }
                else if (_inputManager.IsCommandPressed(InputCommandType.DecreaseScrollSpeed))
                {
                    configManager.AdjustScrollSpeed(-1);
                }
            }
        }

        /// <summary>
        /// Handles ESC key and controller Back button input during performance.
        /// This method provides immediate exit functionality for players who want to
        /// return to song selection without completing the current song.
        /// 
        /// ESC/Back button behavior:
        /// - Immediately stops song playback and timing
        /// - Stops scheduled BGM and chip sound instances so audio does not bleed
        ///   into the transition or the next stage during the fade
        /// - Deactivates judgement manager to prevent further input processing
        /// - Pauses input processing to prevent further judgement handling
        /// - Returns to song selection stage with smooth fade transition
        /// - Resource cleanup is deferred to OnDeactivate() to prevent texture flickering
        /// 
        /// Controller support:
        /// - Supports both keyboard ESC key and gamepad/controller Back button
        /// - Uses InputCommandType.Back for universal controller compatibility
        /// </summary>
        private void ReturnToSongSelect()
        {
            // 1. Stop audible gameplay audio instances (song timer, scheduled
            //    BGM tracks and chip sounds) immediately. The transition uses a
            //    0.5s fade, so without this the backing tracks and chip
            //    instances owned by the session would keep playing after
            //    gameplay has logically stopped and bleed into the
            //    song-selection stage. Full disposal remains in
            //    CleanupComponents via OnDeactivate.
            StopGameplayAudioInstances();

            // 2. Deactivate judgement manager to stop processing input
            if (Rules?.JudgementManager is { } judgementManager)
            {
                judgementManager.IsActive = false;
            }

            // 3. Pause input to block further judgement processing
            _inputPaused = true;

            // 4. Component cleanup will be handled automatically by OnDeactivate() during stage transition
            // This prevents premature texture disposal that causes gauge flickering

            // Return to song selection stage
            PublishTelemetry();
            StageManager?.ChangeStage(StageType.SongSelect,
                new DTXManiaFadeTransition(0.5));
        }

        /// <summary>
        /// Stops the song timer and all active scheduled-BGM and chip-sound
        /// instances immediately via the gameplay audio session.
        /// Used by both gameplay exit paths (ReturnToSongSelect and FinalizePerformance)
        /// to silence audio before the stage transition begins. The instances are
        /// tracked independently from the song timer (scheduled BGM) and by
        /// ChipSoundCache (chip sounds); stopping only the song timer leaves them
        /// audible through the transition fade. Full stop-and-dispose of these
        /// instances remains in CleanupComponents.
        /// </summary>
        private void StopGameplayAudioInstances()
        {
            // Stop the song timer plus all scheduled-BGM and chip-sound
            // instances owned by the session.
            _audioSession?.StopAll();

            // Cancel the chart background video generation so the last decoded
            // frame does not persist through the transition fade. Full disposal
            // remains in CleanupComponents.
            _chartVideoPlayer?.Stop();
        }

        private void FailLoading(string message, Exception? exception = null)
        {
            _isLoading = false;
            _isReady = false;
            _loadErrorMessage = message;
            PublishTelemetry();
            LogPerformanceError(message, exception);
        }

        private void OnScrollSpeedChanged(object? sender, ScrollSpeedChangedEventArgs e)
        {
            _noteRenderer?.SetScrollSpeed(e.NewPercent);
            _scrollSpeedIndicator?.Show(e.NewPercent);
        }

        #endregion

        #region Phase 2 - Chart Loading and Gameplay

        /// <summary>
        /// Starts gameplay preparation on the activation's background-work scope
        /// (HPA-799). Every activation input is snapshotted on this (update) thread
        /// before Run, so the worker and its continuations never read stage fields
        /// that <see cref="CleanupComponents"/> nulls. The worker parses the fallback
        /// chart and builds the Tab notation chart, immediately posts them to the
        /// update thread (<see cref="ApplyParsedChart"/>: chart, managers and the Tab
        /// strip, so the correct playfield shows while audio is still preparing),
        /// then prepares the audio set into a local result. The update-thread success
        /// callback binds the audio and flips the stage to ready; the error callback
        /// fails the load. Progress streams through a Latest reporter so at most one
        /// report is queued per frame. Returns the scope's worker task so tests can
        /// await the real terminal (result payload included) instead of inferring
        /// completion from stage state; production callers safely ignore it.
        /// </summary>
        private Task<PreparedGameplayAudioSet?> StartGameplayInitialization()
        {
            var scope = BackgroundWork;

            // The run starts in the loading state; the success/error callbacks publish
            // readiness or the failure below.
            _isLoading = true;

            // Snapshot the activation inputs before Run. CleanupComponents can null
            // these fields while the worker is still running.
            var chartSnapshot = _parsedChart;
            var songSnapshot = _selectedSong;
            var difficultySnapshot = _selectedDifficulty;
            var playbackModifiers = _playbackModifiers;
            var requestedNoteDisplayStyle = _visualGates.RequestedNoteDisplayStyle;
            string? fallbackChartPath = null;
            if (chartSnapshot == null)
            {
                if (songSnapshot == null)
                {
                    FailLoading("No song was selected.");
                    return Task.FromResult<PreparedGameplayAudioSet?>(null);
                }

                var chartPath = songSnapshot.GetCurrentDifficultyChart(difficultySnapshot)?.FilePath;
                if (string.IsNullOrEmpty(chartPath))
                {
                    FailLoading("The selected chart path is unavailable.");
                    return Task.FromResult<PreparedGameplayAudioSet?>(null);
                }

                fallbackChartPath = chartPath;
            }

            if (scope == null)
                return Task.FromResult<PreparedGameplayAudioSet?>(null);

            // Resolve the logger on this (update) thread: the Logger property lazily
            // assigns _logger, so resolving it from the worker would mutate stage
            // state off-thread. The worker reports only through the captured logger.
            ILogger workerLogger = Logger;

            IProgress<AudioPreparationProgress> progress =
                new InlineProgress<AudioPreparationProgress>(
                    scope.Latest<AudioPreparationProgress>(ApplyAudioPreparationProgress));

            // Resolve the stage-independent dependency factories on this
            // (update) thread before launch: the producing virtuals are stage
            // members, so the worker must never call them off-thread. What the
            // worker receives is a pair of delegates it invokes itself, so the
            // I/O-heavy processor/cache construction stays worker-side — off
            // the update thread and after chart publication, exactly as before.
            // Only non-default playback profiles need them; default profiles
            // keep passing null. A synchronous failure producing the factories
            // is deferred into a throwing factory so the worker still publishes
            // the chart first and reports the fault through the normal error
            // path instead of bypassing publication.
            Func<IAudioVariantProcessor>? audioVariantProcessorFactory = null;
            Func<PlaybackAudioVariantCache>? audioVariantCacheFactory = null;
            if (!playbackModifiers.IsDefault)
            {
                try
                {
                    audioVariantProcessorFactory = CreateAudioVariantProcessorFactory();
                    audioVariantCacheFactory = CreateAudioVariantCacheFactory();
                }
                catch (Exception exception)
                {
                    var failure = exception;
                    audioVariantProcessorFactory = () => throw failure;
                    audioVariantCacheFactory = () => throw failure;
                }
            }

            // The chart publication is a FIFO post, so it always applies before this
            // run's terminal callback; a retired scope drops it (nothing owned).
            Action<ParsedChart, NotationChart?> publishChart = (chart, notationChart) =>
                scope.Post(() => ApplyParsedChart(chart, notationChart));

            return scope.Run(
                cancellationToken => GameplayAudioSession.Prepare(
                    chartSnapshot,
                    fallbackChartPath,
                    requestedNoteDisplayStyle,
                    playbackModifiers,
                    workerLogger,
                    progress,
                    publishChart,
                    audioVariantProcessorFactory,
                    audioVariantCacheFactory,
                    cancellationToken),
                onSuccess: audioSet => ApplyPreparedAudio(audioSet),
                onError: exception =>
                {
                    // An earlier failure (e.g. chart setup) already recorded the
                    // load outcome: keep its root-cause message and log this later
                    // audio fault alongside it instead of overwriting it.
                    if (_loadErrorMessage == null)
                        FailLoading($"Audio preparation failed: {exception.Message}", exception);
                    else
                        LogPerformanceError(
                            $"Audio preparation failed: {exception.Message}", exception);
                },
                onDropped: audioSet => audioSet.Dispose());
        }

        /// <summary>
        /// Update-thread publication of the parsed chart ahead of audio preparation:
        /// the chart, <see cref="ChartManager"/>, gameplay managers, note renderer
        /// tempo/scroll speed and (Tab) the notation strip. A failure fails the load with a chart-setup message; the
        /// terminal audio callback then discards its audio instead of binding it.
        /// </summary>
        private void ApplyParsedChart(ParsedChart chart, NotationChart? notationChart)
        {
            try
            {
                _parsedChart = chart;
                Rules!.BindChart(chart);

                // Initialize gameplay managers
                InitializeGameplayManagers();

                // Set BPM and scroll speed in note renderer. The scroll speed is read
                // at apply time, not snapshotted before Run: PageUp/PageDown stay
                // active during loading, and the stale snapshot would overwrite the
                // user's change and diverge from the live config and the toast.
                _noteRenderer?.SetBpm(chart.Bpm);
                _noteRenderer?.SetScrollSpeed(
                    _game?.ConfigManager?.Config?.ScrollSpeed ?? ScrollSpeedRange.Default);

                // HPA-411: one-shot Tab renderer construction on the update thread
                // because it owns the Bravura and label font loads. A failure means
                // Lanes for the rest of the song.
                if (notationChart != null)
                    TryConstructNotationStrip(notationChart, chart.MeasureLines);
            }
            catch (Exception ex)
            {
                FailLoading($"Chart setup failed: {ex.Message}", ex);
            }
            PublishTelemetry();
        }

        /// <summary>
        /// Update-thread success callback: binds the prepared audio to the already
        /// published chart and flips the stage to ready. The audio set's ownership
        /// transfers to the stage inside the try block; the finally disposes the
        /// locally owned set on every path that did not transfer ownership, and the
        /// catch fails the load with the original exception. A stale result never
        /// reaches this method (the scope drops it via <c>onDropped</c> instead).
        /// </summary>
        private void ApplyPreparedAudio(PreparedGameplayAudioSet audioSet)
        {
            var ownershipTransferred = false;
            try
            {
                // The chart publication failed (or never arrived): the load is
                // already failed, so there is nothing to bind the audio to.
                if (_loadErrorMessage != null || _parsedChart == null)
                    return;

                // Bind the prepared audio: ownership moves to the session here,
                // so from this point session disposal via CleanupComponents (not
                // the finally below) disposes it.
                var session = new GameplayAudioSession(
                    _resourceManager,
                    _game?.LoggerFactory,
                    Logger,
                    _playbackModifiers,
                    _metronomeEnabled,
                    PlayMetronomeClick);
                _audioSession = session;
                ownershipTransferred = true;
                session.Bind(_parsedChart, audioSet);

                _isLoading = false;
                _isReady = true;
            }
            catch (Exception ex)
            {
                FailLoading($"Audio binding failed: {ex.Message}", ex);
            }
            finally
            {
                if (!ownershipTransferred)
                    audioSet.Dispose();
                PublishTelemetry();
            }
        }

        /// <summary>
        /// Latest apply for audio preparation progress. Called on the update thread
        /// during the scope drain; retirement drops superseded and post-teardown
        /// reports inside the scope.
        /// </summary>
        private void ApplyAudioPreparationProgress(AudioPreparationProgress progress)
        {
            _audioPreparationProgress = progress;
            PublishTelemetry();
        }

        /// <summary>
        /// One-shot HPA-411 Tab renderer construction on the update thread (from the
        /// initialization success callback) because the renderer owns the Bravura and
        /// label font loads. Exactly one attempt happens per activation — a failure
        /// keeps Lanes for this song. A strip that is already published is never
        /// replaced.
        /// </summary>
        private void TryConstructNotationStrip(
            NotationChart notationChart,
            IReadOnlyList<MeasureLine> measureLines)
        {
            if (_notationStrip != null)
                return;

            // The skin art is published with the strip; every path that does not
            // publish falls through to the release below.
            var art = TabModeArt.None;
            try
            {
                // One theme read styles both halves of Tab mode (strip + HUD).
                var theme = _resourceManager?.CurrentTheme;
                var palette = SkinPalette.Resolve(theme);
                art = TabModeArt.Load(_resourceManager, theme);
                var renderer = new NotationStripRenderer(
                    _resourceManager,
                    notationChart,
                    measureLines,
                    palette,
                    art);
                if (renderer.IsReady)
                {
                    _notationStrip = renderer;
                    if (ConstructTabHud(palette, art) != TabHudBuildResult.Failed)
                    {
                        _tabModeArt = art;
                        return;
                    }

                    // HPA-509 review: a strip without its HUD draws the
                    // Lanes widgets back over the strip's hit marker and
                    // key labels — an unreadable mixed mode. Roll the whole
                    // decision back to a consistent Lanes fallback (null
                    // the reference BEFORE disposing, like CleanupComponents).
                    _notationStrip = null;
                    renderer.Dispose();
                    _tabHudDisplay?.Dispose();
                    _tabHudDisplay = null;
                    LogPerformanceError(
                        "PerformanceStage: Tab HUD unavailable; Tab display falls back to Lanes for this song.");
                }
                else
                {
                    LogPerformanceError(
                        "PerformanceStage: Bravura notation fonts unavailable; Tab display falls back to Lanes for this song.");
                    renderer.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogPerformanceError(
                    "PerformanceStage: notation renderer construction failed; staying in Lanes for this song.",
                    ex);
            }

            art.Release();
        }

        /// <summary>
        /// Metronome click sink passed to the session at bind time. Kept as the
        /// stage's virtual seam so test subclasses can observe markers; the
        /// base implementation delegates to the bound session, which owns the
        /// click sounds and the once-per-run failure suppression.
        /// </summary>
        protected virtual void PlayMetronomeClick(BeatMarker marker)
        {
            _audioSession?.PlayMetronomeClick(marker);
        }

        /// <summary>
        /// Updates gameplay state and timing
        /// </summary>
        private void UpdateGameplay(double deltaTime)
        {
            var scope = BackgroundWork;
            if (_isLoading)
            {
                return;
            }

            // Handle ready countdown
            if (_isReady && _readyCountdown > 0)
            {
                _readyCountdown -= deltaTime;
                if (_readyCountdown <= 0)
                {
                    StartSong();
                }
                // During ready countdown, don't run miss detection yet
                // Notes should only be missed after the song officially starts
                // The countdown time is preparation time, not song time
                return;
            }

            // Update note renderer
            _noteRenderer?.Update(deltaTime);

            // Update NX attack effects
            _nxAttackEffectManager?.Update(deltaTime);

            // Update judgement text popup managers
            _spriteJudgementTextPopupManager?.Update(deltaTime);
            _fontJudgementTextPopupManager?.Update(deltaTime);
            _hitTimingFeedbackDisplay?.Update(deltaTime);

            // Handle BGM event scheduling and gameplay managers
            if (_audioSession != null && _audioSession.IsPlaying)
            {
                // Only run timing logic when the song is actually playing
                var currentTimeMs = _audioSession.GetCurrentMs(_currentGameTime);
                
                // Process BGM events during song playback
                _audioSession.Advance(currentTimeMs);

                // HPA-11: schedule chart background video events on the same
                // logical clock (never re-read from GetCurrentMs, never speed-scaled).
                ProcessVideoEvents(currentTimeMs);

                // Dual-clock architecture:
                // - Raw song clock (currentTimeMs): autoplay, BGM events, note visuals,
                //   progress, completion — everything that must stay synced with the chart.
                // - Compensated clock (playerJudgementTimeMs): player hits and miss scanning.
                //   Subtracting AudioLatencyOffsetMs aligns hit detection with what the
                //   player actually hears, compensating for audio buffer/driver output delay.
                var playerJudgementTimeMs = GetPlayerJudgementTimeMs(currentTimeMs);

                UpdateGameplayManagers(currentTimeMs, playerJudgementTimeMs);
                if (!ReferenceEquals(scope, BackgroundWork))
                    return;
                
                // Update song progress
                UpdateSongProgress(currentTimeMs);
                
                // Check for stage completion conditions
                var completion = Rules?.CheckCompletion(currentTimeMs, _parsedChart?.DurationMs, _totalTime);
                if (completion is { } reason)
                    FinalizePerformance(reason);
            }
        }

        /// <summary>
        /// Compensates the logical song clock for real-time audio output latency
        /// when judging player input. A real latency interval advances a different
        /// amount of chart time at each play speed, so the configured real
        /// milliseconds are scaled by the frozen speed before subtraction.
        /// Autoplay, visuals, and BGM events use raw logical time. Miss
        /// scanning uses the compensated clock so the player keeps the full
        /// hit window after a note becomes audible.
        /// </summary>
        private double GetPlayerJudgementTimeMs(double currentAudioClockMs)
            => GameplayRules.GetPlayerJudgementTimeMs(
                currentAudioClockMs,
                _game?.ConfigManager?.Config?.AudioLatencyOffsetMs ?? 0,
                _playbackModifiers);

        /// <summary>
        /// Starts playing the song
        /// </summary>
        private void StartSong()
        {
            if (_audioSession != null && _currentGameTime != null)
            {
                // Start the song timer - this provides the master clock for notes and BGM events
                Rules?.ResetSongEndBuffer();
                bool playbackStarted = _audioSession.Start(_currentGameTime, _parsedChart);

                if (!playbackStarted)
                {
                    LogPerformanceError("PerformanceStage: Failed to start song playback - audio may be unavailable");
                    // Continue anyway - the game can still run without audio
                }

                _isReady = false;

                // Activate the judgement manager now that the song is playing
                if (Rules?.JudgementManager is { } judgementManager)
                {
                    judgementManager.IsActive = true;
                }
            }
        }

        private void DrawMeasureLines()
        {
            if (_visualGates.HideMeasureLines)
                return;

            var chartManager = Rules?.ChartManager;
            if (_noteRenderer == null ||
                chartManager == null ||
                _audioSession == null ||
                _currentGameTime == null)
            {
                return;
            }

            if (!_audioSession.IsPlaying && !_audioSession.IsPaused)
                return;

            var currentTimeMs = _audioSession.GetCurrentMs(_currentGameTime);
            var lookAheadMs = _noteRenderer.EffectiveLookAheadMs > 0
                ? _noteRenderer.EffectiveLookAheadMs
                : PerformanceUILayout.NoteDefaultLookAheadMs;
            var activeLines = chartManager.GetActiveMeasureLines(
                currentTimeMs,
                lookAheadMs,
                _noteRenderer.MeasureLinePastGraceMs);

            _noteRenderer.DrawMeasureLines(
                _spriteBatch,
                activeLines,
                currentTimeMs);
        }

        /// <summary>
        /// Draws scrolling notes.
        /// </summary>
        private void DrawNotes()
        {
            var chartManager = Rules?.ChartManager;
            if (_noteRenderer == null || chartManager == null || _audioSession == null || _currentGameTime == null)
                return;

            if (!_audioSession.IsPlaying && !_audioSession.IsPaused)
                return;

            // Get current song time and active notes
            var currentTimeMs = _audioSession.GetCurrentMs(_currentGameTime);
            var lookAheadMs = _noteRenderer.EffectiveLookAheadMs > 0 ? _noteRenderer.EffectiveLookAheadMs : 1500.0;
            var activeNotes = chartManager.GetActiveNotes(currentTimeMs, lookAheadMs);

            // Draw the notes
            _noteRenderer.DrawNotes(_spriteBatch, activeNotes, currentTimeMs);
        }

        /// <summary>
        /// Draws note overlay animations in effects pass.
        /// </summary>
        private void DrawNoteOverlays()
        {
            var chartManager = Rules?.ChartManager;
            if (_noteRenderer == null || chartManager == null || _audioSession == null || _currentGameTime == null)
                return;

            if (!_audioSession.IsPlaying && !_audioSession.IsPaused)
                return;

            // Get current song time using precise GameTime-based timing
            var currentTimeMs = _audioSession.GetCurrentMs(_currentGameTime);

            // Get active notes using the same look-ahead time as scroll calculation
            var lookAheadMs = _noteRenderer.EffectiveLookAheadMs > 0 ? _noteRenderer.EffectiveLookAheadMs : 1500.0;
            var activeNotes = chartManager.GetActiveNotes(currentTimeMs, lookAheadMs);

            // Draw the note overlays with additive blending
            _noteRenderer.DrawNoteOverlays(_spriteBatch, activeNotes, currentTimeMs);
        }

        /// <summary>
        /// Draws the HPA-411 notation strip in the base BackToFront batch with the same
        /// logical song clock as the falling notes and the live scroll-speed percentage.
        /// The strip is the entire Tab playfield, so it stays on screen whenever it
        /// exists — loading, the READY countdown, pause and the exit fade — at the
        /// clock's current logical position (0 before transport starts).
        /// </summary>
        private void DrawNotationStrip()
        {
            var currentTimeMs = (_audioSession != null && _currentGameTime != null)
                ? _audioSession.GetCurrentMs(_currentGameTime)
                : 0.0;
            var speedPercent = _game?.ConfigManager?.Config?.ScrollSpeed ?? ScrollSpeedRange.Default;
            _notationStrip?.Draw(_spriteBatch, currentTimeMs, speedPercent);
        }

        /// <summary>
        /// Draws gameplay state (loading, ready, etc.)
        /// </summary>
        private void DrawGameplayState()
        {
            if (!string.IsNullOrEmpty(_loadErrorMessage))
            {
                DrawCenteredText("AUDIO LOAD FAILED - PRESS BACK", Color.Red);
            }
            else if (_isLoading)
            {
                var elapsed = DateTime.UtcNow - _audioPreparationStartedUtc;
                if (_audioPreparationProgress != null &&
                    elapsed.TotalSeconds >= PerformanceUILayout.AudioPreparationProgressDelaySeconds)
                {
                    DrawCenteredText(
                        $"Preparing audio {_audioPreparationProgress.CompletedCount}/" +
                        $"{_audioPreparationProgress.TotalCount}",
                        Color.White);
                }
                else
                {
                    DrawCenteredText("LOADING...", Color.White);
                }
            }
            else if (_isReady && _readyCountdown > 0)
            {
                // Draw ready countdown with pulsing effect
                // Use total time for consistent pulsing instead of countdown (which decreases)
                var alpha = (float)(0.5 + 0.5 * Math.Sin(_totalTime * DTXMania.Game.Lib.UI.Layout.PerformanceUILayout.ReadyPulseFrequency * 2 * Math.PI));
                var readyColor = Color.Yellow * alpha;
                DrawCenteredText("READY...", readyColor);
            }
        }

        /// <summary>
        /// Initializes the font for ready state display
        /// </summary>
        private void InitializeReadyFont()
        {
            try
            {
                // Themed skins may style the centered LOADING.../READY... text
                // with a Latin display family; NX keeps the serif.
                var theme = _resourceManager?.CurrentTheme ?? SkinTheme.Empty;
                var stateFamily = ResolveStateFontFamily(theme);
                _readyFont = stateFamily.Length > 0
                    ? _resourceManager.LoadFont(stateFamily, ResolveStateFontSize(theme))
                    : _resourceManager.LoadFont("NotoSerifJP", 24);
                _scrollSpeedFont = _resourceManager.LoadFont("NotoSerifJP", 14);
                _scrollSpeedIndicator = new ScrollSpeedIndicator(_scrollSpeedFont);
            }
            catch (Exception ex)
            {
                // Font initialization failed, fallback will be used; keep the
                // Release-visible diagnostic (logger is Console-backed).
                Logger.LogError(ex, "InitializeReadyFont failed; the null-font fallback will be used");
                _readyFont?.RemoveReference();
                _readyFont = null;
                _scrollSpeedFont?.RemoveReference();
                _scrollSpeedFont = null;
                _scrollSpeedIndicator = new ScrollSpeedIndicator(null);
            }
        }
        
        /// <summary>
        /// Loads all performance UI assets (7_* files). Each load is best-effort:
        /// TryLoadTexture swallows per-asset failures and returns null, so a missing
        /// skin asset degrades gracefully without aborting the whole load.
        /// </summary>
        private void LoadPerformanceUIAssets()
        {
            // Load background texture using TexturePath constant
            _backgroundTexture = TryLoadTexture(TexturePath.PerformanceBackgroundTexture);

            // Load shutter texture using TexturePath constant
            _shutterTexture = TryLoadTexture(TexturePath.Shutter);

            // Load lane strip textures (7_Paret.png) using TexturePath constant
            _laneBgTexture = TryLoadTexture(TexturePath.LaneStrips);

            // Load lane covers (7_lanes_Cover_cls.png) using TexturePath constant
            _laneDividerTexture = TryLoadTexture(TexturePath.LaneCovers);

            // Load lane flush texture (will be used for effects) using TexturePath constant
            _laneFlashTexture = TryLoadTexture(TexturePath.LaneFlushPrefix + "default.png");

            // Load hit-bar (judgement line) using TexturePath constant
            _judgementLineTexture = TryLoadTexture(TexturePath.HitBar);

            // Load gauge textures using TexturePath constants
            _gaugeBaseTexture = TryLoadTexture(TexturePath.GaugeFrame);
            _gaugeFillTexture = TryLoadTexture(TexturePath.GaugeFill);

            // Load progress bar textures using TexturePath constants
            _progressBaseTexture = TryLoadTexture(TexturePath.ProgressFrame);
            _progressFillTexture = TryLoadTexture(TexturePath.ProgressFill);

            // Load digit textures using TexturePath constants
            _comboDigitsTexture = TryLoadTexture(TexturePath.ComboDisplay);
            _scoreDigitsTexture = TryLoadTexture(TexturePath.ScoreNumbers);

            // Load judgement text sprite sheet using TexturePath constant
            _judgeStringsTexture = TryLoadTexture(TexturePath.JudgeStrings);

            // Load overlay textures using TexturePath constants
            _pauseOverlayTexture = TryLoadTexture(TexturePath.PauseOverlay);
            _dangerOverlayTexture = TryLoadTexture(TexturePath.Danger);

            // Load skill panel texture. Themed skins may point this at a
            // performance-specific sheet (the shared NX art bakes labels laid
            // out for the result screen). A themed override that is empty or
            // points at a missing file would otherwise leave the panel drawn
            // as a single pixel (LoadTexture's 1x1 fallback) or null, so the
            // loader retries with the NX default asset on either failure mode.
            _skillPanelTexture = LoadSkillPanelTexture(
                _resourceManager?.CurrentTheme ?? SkinTheme.Empty);
        }

        /// <summary>
        /// Loads the performance skill-panel texture, retrying with the NX
        /// default asset when the themed override is empty, missing, or
        /// resolves to LoadTexture's 1x1 fallback. Returns null only when the
        /// default asset also fails to load.
        /// </summary>
        private ITexture LoadSkillPanelTexture(ISkinTheme theme)
        {
            var overridePath = ResolveSkillPanelTexturePath(theme);
            var texture = TryLoadTexture(overridePath);
            if (texture != null && texture.Width > 1 && texture.Height > 1)
                return texture;

            // Override was empty, missing, or the 1x1 fallback. Dispose the
            // fallback and retry with the NX default asset path. The fallback
            // is uncached (CreateFallbackTexture does not add it to the
            // ResourceManager cache) and RemoveReference() deliberately does
            // not auto-dispose, so disposing here is the only way to release
            // the underlying GPU Texture2D — otherwise every Performance-stage
            // activation with a missing/invalid themed override leaks a 1x1
            // texture.
            texture?.Dispose();
            var defaultPath = TexturePath.SkillPanel;
            if (string.Equals(overridePath, defaultPath, StringComparison.Ordinal))
                return null; // already tried the default; nothing left to retry

            // The second attempt may also fail (missing default asset in a
            // broken installation). Validate it the same way: a 1x1 result is
            // CreateFallbackTexture's uncached fallback, which would leak
            // because CleanupPerformanceUIAssets only calls RemoveReference
            // (which does not dispose). Dispose and return null so the panel
            // is simply not drawn rather than leaking a GPU texture on every
            // activation.
            var defaultTexture = TryLoadTexture(defaultPath);
            if (defaultTexture != null && defaultTexture.Width > 1 && defaultTexture.Height > 1)
                return defaultTexture;
            defaultTexture?.Dispose();
            return null;
        }

        /// <summary>
        /// Skill-panel art for the performance stage: "Performance.SkillPanelTexture"
        /// → NX 7_SkillPanel.png (shared with the result screen). An empty or
        /// whitespace themed value is coerced to the NX default so a malformed
        /// `Performance.SkillPanelTexture=` line cannot blank the panel.
        /// </summary>
        internal static string ResolveSkillPanelTexturePath(ISkinTheme theme)
        {
            var path = theme.GetString("Performance.SkillPanelTexture", TexturePath.SkillPanel);
            return string.IsNullOrWhiteSpace(path) ? TexturePath.SkillPanel : path;
        }

        /// <summary>
        /// Optional display font family for the centered LOADING.../READY...
        /// text: "Performance.StateFontFamily", empty = NX serif.
        /// </summary>
        internal static string ResolveStateFontFamily(ISkinTheme theme) =>
            theme.GetString("Performance.StateFontFamily", string.Empty);

        /// <summary>
        /// Point size for the state text: "Performance.StateFontSize" → NX 24.
        /// </summary>
        internal static int ResolveStateFontSize(ISkinTheme theme) =>
            theme.GetInt("Performance.StateFontSize", 24);
        
        /// <summary>
        /// Safely tries to load a texture, returning null on failure
        /// </summary>
        private ITexture TryLoadTexture(string path)
        {
            try
            {
                if (_resourceManager == null)
                    return null;
                _resourceManager.TryLoadTexture(path, out var texture);
                return texture;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Cleans up all performance UI assets
        /// </summary>
        private void CleanupPerformanceUIAssets()
        {
            // Clean up textures using reference counting
            _backgroundTexture?.RemoveReference();
            _backgroundTexture = null;
            
            _shutterTexture?.RemoveReference();
            _shutterTexture = null;
            
            _laneBgTexture?.RemoveReference();
            _laneBgTexture = null;
            
            _laneDividerTexture?.RemoveReference();
            _laneDividerTexture = null;
            
            _laneFlashTexture?.RemoveReference();
            _laneFlashTexture = null;
            
            _judgementLineTexture?.RemoveReference();
            _judgementLineTexture = null;
            
            _gaugeBaseTexture?.RemoveReference();
            _gaugeBaseTexture = null;
            
            _gaugeFillTexture?.RemoveReference();
            _gaugeFillTexture = null;
            
            _progressBaseTexture?.RemoveReference();
            _progressBaseTexture = null;
            
            _progressFillTexture?.RemoveReference();
            _progressFillTexture = null;
            
            _comboDigitsTexture?.RemoveReference();
            _comboDigitsTexture = null;
            
            _scoreDigitsTexture?.RemoveReference();
            _scoreDigitsTexture = null;
            
            _judgeStringsTexture?.RemoveReference();
            _judgeStringsTexture = null;
            
            _pauseOverlayTexture?.RemoveReference();
            _pauseOverlayTexture = null;
            
            _dangerOverlayTexture?.RemoveReference();
            _dangerOverlayTexture = null;
            
            _skillPanelTexture?.RemoveReference();
            _skillPanelTexture = null;
        }

        /// <summary>
        /// Draws centered text on screen
        /// </summary>
        private void DrawCenteredText(string text, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Calculate center position
            var screenCenter = new Vector2(PerformanceUILayout.ScreenWidth / 2, PerformanceUILayout.ScreenHeight / 2);

            if (_readyFont != null)
            {
                var textSize = _readyFont.MeasureString(text);
                var textX = (int)(screenCenter.X - textSize.X / 2);
                var textY = (int)(screenCenter.Y - textSize.Y / 2);

                _readyFont.DrawString(_spriteBatch, text, new Vector2(textX, textY), color,
                    rotation: 0f, origin: Vector2.Zero, scale: Vector2.One,
                    effects: SpriteEffects.None, layerDepth: 0.1f);
            }
            else
            {
                // Fallback: draw colored rectangle as placeholder with proper depth
                var rectWidth = text.Length * 12;
                var rectHeight = 20;
                var rectPosition = new Rectangle(
                    (int)(screenCenter.X - rectWidth / 2),
                    (int)(screenCenter.Y - rectHeight / 2),
                    rectWidth,
                    rectHeight
                );

                DrawFallbackRectangle(rectPosition, color, 0.1f);
            }
        }

        #endregion

        #region BGM Management

        /// <summary>
        /// Processes chart background video events that should be triggered at the
        /// current time (HPA-11). Consumes all due unhandled events but starts only
        /// the last due one; an unresolved definition (empty path) leaves the video
        /// inactive without blocking later events. The active generation receives
        /// <c>Update(max(0, currentTimeMs - activeEvent.TimeMs))</c> — raw logical
        /// chart time, never rescaled by playback speed.
        /// </summary>
        /// <param name="currentTimeMs">Current song time in milliseconds (shared with BGM scheduling).</param>
        private void ProcessVideoEvents(double currentTimeMs)
        {
            var videoEvents = _parsedChart?.VideoEvents;
            if (videoEvents == null || videoEvents.Count == 0 || _chartVideoPlayer == null)
                return;

            // Consume all due unhandled events; only the last due event from one
            // update starts (Start itself cancels the previous generation).
            ChartVideoEvent? dueEvent = null;
            while (_nextVideoEventIndex < videoEvents.Count &&
                   videoEvents[_nextVideoEventIndex].TimeMs <= currentTimeMs)
            {
                dueEvent = videoEvents[_nextVideoEventIndex];
                _nextVideoEventIndex++;
            }

            if (dueEvent != null)
            {
                if (string.IsNullOrEmpty(dueEvent.VideoFilePath))
                {
                    // Unresolved definition: keep the static-background fallback
                    // (video inactive) without blocking later valid events.
                    if (_activeVideoEvent != null)
                    {
                        _chartVideoPlayer.Stop();
                        _activeVideoEvent = null;
                    }
                }
                else
                {
                    _chartVideoPlayer.Start(dueEvent.VideoFilePath);
                    _activeVideoEvent = dueEvent;
                }
            }

            if (_activeVideoEvent != null)
            {
                _chartVideoPlayer.Update(Math.Max(0.0, currentTimeMs - _activeVideoEvent.TimeMs));
            }
        }

        #endregion

        #region Phase 3 - Gameplay Managers

        /// <summary>
        /// Initializes gameplay managers and wires up event handlers
        /// </summary>
        private void InitializeGameplayManagers()
        {
            if (Rules?.ChartManager == null || _inputManager == null)
                return;

            // Initialize managers
            Rules!.InitializeJudgement(_inputManager);

            Rules.InitializeScoreAndCombo();
            Rules.InitializeGaugeAndSkill();

            // Initialize UI state values
            _currentGaugeValue = PerformanceUILayout.GaugeSettings.StartingLife / 100.0f;
            _currentProgressValue = 0.0f;
            _isPaused = false;
            _isDanger = false;

            // Wire up event handlers for UI binding
            WireUpEventHandlers();

            // Pad feedback is wired earlier, at InitializeComponents, so hits light
            // pads during loading/countdown; nothing to subscribe here.
        }

        /// <summary>
        /// Wires up event handlers between managers and UI components
        /// </summary>
        private void WireUpEventHandlers()
        {
            var judgementManager = Rules?.JudgementManager;
            var scoreManager = Rules?.ScoreManager;
            var comboManager = Rules?.ComboManager;
            var gaugeManager = Rules?.GaugeManager;
            var skillManager = Rules?.SkillManager;
            if (judgementManager == null || scoreManager == null || comboManager == null
                || gaugeManager == null || skillManager == null)
                return;

            // Rules subscribe first; the stage handles visual feedback afterwards.
            judgementManager.JudgementMade += OnJudgementMade;

            // Subscribe to manager events for UI updates
            scoreManager.ScoreChanged += OnScoreChanged;
            comboManager.ComboChanged += OnComboChanged;
            gaugeManager.GaugeChanged += OnGaugeChanged;
            gaugeManager.Failed += OnPlayerFailed;
            skillManager.SkillChanged += OnSkillChanged;
        }

        /// <summary>
        /// Wires up input event handlers for immediate pad feedback. Idempotent:
        /// installed once at component initialization (before preparation starts) so
        /// hits light pads during loading/countdown and even after a failed
        /// preparation, and the gameplay-manager init must not double-subscribe.
        /// </summary>
        private void WireUpInputEventHandlers()
        {
            if (_inputManager?.ModularInputManager != null)
            {
                // Subscribe to lane hit events for immediate visual feedback (before judgement)
                _inputManager.ModularInputManager.OnLaneHit -= OnLaneHitForPadFeedback;
                _inputManager.ModularInputManager.OnLaneHit += OnLaneHitForPadFeedback;
            }
        }

        /// <summary>
        /// Handles lane hit events for immediate pad visual feedback
        /// </summary>
        private void OnLaneHitForPadFeedback(object? sender, LaneHitEventArgs e)
        {
            // Suppress physical pad feedback only on automated lanes; manual
            // lanes keep their immediate key-down animation. Autoplay's own
            // hit animation is driven from PlayAutoNote.
            if (FrozenAutoPlayLanes.Contains(e.Lane)) return;

            // Trigger immediate pad press effect on input (regardless of judgement). Visual pad
            // feedback is intentionally allowed during loading/countdown so the player sees their
            // input registered, even before the song clock starts.
            _padRenderer?.TriggerPadPress(e.Lane, false); // false = key-down, not judged hit

            // HPA-509 Tab: manual input refreshes the lane's key flash immediately,
            // even without a judged note (judgement later overwrites the same slot).
            if (_notationStrip != null)
                _tabHudDisplay?.TriggerKeyFlash(e.Lane);

            // Only record gameplay telemetry and chip sounds while the chart is actively playing.
            // During loading, the ready countdown, or after stop, the song clock is not advancing,
            // so telemetry must not capture these non-gameplay hits.
            if (_audioSession?.IsPlaying != true)
                return;

            // v1 intentionally frame-samples the logical clock when the queued input
            // is handled. Do not combine LaneHitEventArgs.Timestamp (DateTime-based)
            // with GameTime; that would create a mixed-clock judgement path.
            var logicalSongTimeMs = _audioSession.GetCurrentMs(_currentGameTime);

            // Publish all three values as one immutable reference so the API thread reads
            // a consistent snapshot (lane + button + logical time from the same hit).
            _lastLaneHit = new LastLaneHit(
                e.Lane,
                e.Button.Id,
                logicalSongTimeMs);

            // Use the same compensated clock that JudgementManager.Update receives
            // so chip sound lookup mirrors the judgement window. Without this, a hit
            // judged Perfect at the compensated time can fall outside the chip lookup
            // window (PoorWindow < AudioLatencyOffsetMs) and fail to play its chip.
            var chipLookupTimeMs =
                GetPlayerJudgementTimeMs(logicalSongTimeMs);
            var nearest = Rules?.FindNearestNoteForChip(e.Lane, chipLookupTimeMs);
            if (nearest != null)
                _audioSession?.PlayChip(nearest);
        }

        /// <summary>
        /// Handles visual feedback after gameplay rules process a judgement
        /// </summary>
        private void OnJudgementMade(object? sender, DTXMania.Game.Lib.Song.Entities.JudgementEvent e)
        {
            // The event invocation may have captured this handler before synchronous teardown.
            if (sender is JudgementManager && !ReferenceEquals(sender, Rules?.JudgementManager))
                return;

            // Spawn hit effect for successful hits (non-Miss)
            if (e.IsHit())
            {
                _nxAttackEffectManager?.Spawn(e.Lane, e.Type);

                // Trigger pad press effect
                _padRenderer?.TriggerPadPress(e.Lane, true);

                // Lane flush flashes the lane rectangles, which the notation strip
                // replaces entirely — triggering it in Tab mode is dead state churn.
                if (_visualGates.EnableLaneFlush && _notationStrip == null)
                    _noteRenderer?.TriggerLaneFlash(e.Lane);
            }

            // HPA-509: per-hit fast/slow visibility, computed once and shared by the
            // Lanes timing spawn and the Tab popup's fast/slow line. The cumulative
            // Fast/Slow stat tiles stay always-visible and independent of this gate.
            bool showPerHitTiming = _visualGates.ShowHitTimingFeedback
                && e.IsHit()
                && !FrozenAutoPlayLanes.Contains(e.Lane);

            // The Lanes-vs-Tab split keys on the HUD's readiness, not the strip alone:
            // a Tab HUD that failed to load rolls the strip back to Lanes, so the
            // legacy popup path stays consistent with what is on screen (HPA-509 review).
            if (!IsTabHudActive)
            {
                // Lanes: spawn judgement text popup for all judgements (unchanged).
                _spriteJudgementTextPopupManager?.SpawnPopup(e);

                if (showPerHitTiming)
                {
                    _hitTimingFeedbackDisplay?.Spawn(e);
                }
            }
            else
            {
                // HPA-509 Tab: the legacy popups draw at Lanes positions, so their
                // spawns are gated (DrawJudgementTexts stays untouched and finds no
                // transients) and the Tab HUD's own newest-wins slot is fed instead.
                // Every judgement also refreshes the lane's key cap: its lane color for
                // hits, danger for Miss, so instrument identity survives rapid popup
                // replaces.
                _tabHudDisplay?.TriggerKeyFlash(e.Lane, isMiss: !e.IsHit());

                // Partial autoplay: an automated lane's judgement must not replace
                // the player's popup — its word carries no timing and would wipe the
                // player's fast/slow line within milliseconds (HPA-509 review). The
                // lane still flashes above; under full autoplay every judgement feeds
                // the popup, matching what the Lanes HUD shows.
                bool automatedPartialLane = FrozenAutoPlayLanes.Contains(e.Lane)
                    && FrozenAutoPlayLanes.Count < PerformanceUILayout.LaneCount;
                if (!automatedPartialLane)
                    _tabHudDisplay?.NotifyJudgement(e.Type, showPerHitTiming, e.DeltaMs);
            }
        }

        /// <summary>
        /// Handles score changes and updates UI
        /// </summary>
        private void OnScoreChanged(object? sender, ScoreChangedEventArgs e)
        {
            // Update score display
            if (_scoreDisplay != null)
            {
                _scoreDisplay.Score = e.CurrentScore;
            }
        }

        /// <summary>
        /// Handles combo changes and updates UI
        /// </summary>
        private void OnComboChanged(object? sender, ComboChangedEventArgs e)
        {
            // Update combo display
            if (_comboDisplay != null)
            {
                _comboDisplay.Combo = e.CurrentCombo;
            }
        }

        /// <summary>
        /// Handles gauge changes and updates UI
        /// </summary>
        private void OnGaugeChanged(object? sender, GaugeChangedEventArgs e)
        {
            // Update gauge display
            // Legacy gauge display removed - now using asset-based gauge in DrawGaugeElements()

            // Update our internal gauge value for asset rendering
            _currentGaugeValue = e.CurrentLife / 100.0f;

            // Update danger state based on gauge level
            _isDanger = _currentGaugeValue < PerformanceUILayout.GaugeSettings.DangerThreshold / 100.0f;
        }

        /// <summary>
        /// Handles skill changes for the animated skill meter.
        /// </summary>
        private void OnSkillChanged(object? sender, SkillChangedEventArgs e)
        {
            if (_skillMeterDisplay != null)
            {
                _skillMeterDisplay.Skill = e.CurrentSkill;
            }
        }

        /// <summary>
        /// Handles player failure
        /// </summary>
        private void OnPlayerFailed(object? sender, FailureEventArgs e)
        {
            FinalizePerformance(CompletionReason.PlayerFailed);
        }

        /// <summary>
        /// Updates gameplay managers during active gameplay
        /// </summary>
        private void UpdateGameplayManagers(
            double logicalSongTimeMs,
            double pendingHitTimeMs)
        {
            var scope = BackgroundWork;
            var rules = Rules;
            if (rules != null && rules.AutoPlayLanes.Count > 0)
                rules.ProcessAutoPlay(logicalSongTimeMs, note =>
                {
                    if (ReferenceEquals(rules, Rules))
                        PlayAutoNote(note);
                });

            if (!ReferenceEquals(scope, BackgroundWork))
                return;
            _audioSession?.UpdateMetronome(logicalSongTimeMs);
            if (!ReferenceEquals(scope, BackgroundWork))
                return;

            // Pending hits and timeout misses both use the latency-compensated
            // logical time. Miss scanning must share the compensated clock so a
            // note is not marked missed before the player hears it: using raw
            // time would shrink the reaction window by AudioLatencyOffsetMs
            // whenever the user configures a manual latency offset.
            Rules?.UpdateJudgement(pendingHitTimeMs);
        }
        
        /// <summary>
        /// Updates song progress value for progress bar rendering
        /// </summary>
        private void UpdateSongProgress(double currentSongTimeMs)
        {
            if (_parsedChart != null && _parsedChart.DurationMs > 0)
            {
                _currentProgressValue = (float)Math.Clamp(currentSongTimeMs / _parsedChart.DurationMs, 0.0, 1.0);
            }
        }

        /// <summary>
        /// Applies stage-owned feedback after rules resolve an autoplay note.
        /// </summary>
        private void PlayAutoNote(Note note)
        {
            _padRenderer?.TriggerPadPress(note.LaneIndex, true);
            _audioSession?.PlayChip(note);
        }

        /// <summary>
        /// Cleans up gameplay managers
        /// </summary>
        private void CleanupGameplayManagers()
        {
            // Unsubscribe from events
            if (Rules?.JudgementManager is { } judgementManager)
            {
                judgementManager.JudgementMade -= OnJudgementMade;
            }

            if (Rules?.ScoreManager is { } scoreManager)
            {
                scoreManager.ScoreChanged -= OnScoreChanged;
            }

            if (Rules?.ComboManager is { } comboManager)
            {
                comboManager.ComboChanged -= OnComboChanged;
            }

            if (Rules?.GaugeManager is { } gaugeManager)
            {
                gaugeManager.GaugeChanged -= OnGaugeChanged;
                gaugeManager.Failed -= OnPlayerFailed;
            }

            if (Rules?.SkillManager is { } skillManager)
            {
                skillManager.SkillChanged -= OnSkillChanged;
            }

            // Unsubscribe from input events
            if (_inputManager?.ModularInputManager != null)
            {
                _inputManager.ModularInputManager.OnLaneHit -= OnLaneHitForPadFeedback;
            }

            // Dispose managers
            Rules?.ClearJudgement();
            Rules?.ClearScoreAndCombo();
            Rules?.ClearGaugeAndSkill();
        }

        #endregion

        #region Component Updates

        private void UpdateComponents(double deltaTime)
        {
            // Update background renderer
            _backgroundRenderer?.Update(deltaTime);

            // Update lane background and judgement line renderers
            _laneBackgroundRenderer?.Update(deltaTime);
            _judgementLineRenderer?.Update(deltaTime);

            // Update score and combo displays
            _scoreDisplay?.Update(deltaTime);
            _comboDisplay?.Update(deltaTime);
            _skillPanelDisplay?.Update(deltaTime);
            _skillMeterDisplay?.Update(deltaTime);

            // HPA-509 Tab: advance the popup slot and key flash decay on every
            // frame — UpdateGameplay returns early during loading and the READY
            // countdown, which would leave a triggered flash lit at full alpha.
            _tabHudDisplay?.Update(deltaTime);

            // Same gating: pad presses fire during loading/countdown (see
            // OnLaneHitForPadFeedback), so the 90 ms decay must tick here or a
            // press would stay lit until gameplay starts.
            _padRenderer?.Update(deltaTime);

            // Update gauge display
        }

        #endregion

        #region Drawing Methods

        /// <summary>
        /// GPU-free draw layout for the chart background video (HPA-11): the video
        /// fills the background bounds at depth 0.95 — above the static background
        /// (1.0) and below the lane strips (0.8). Extracted so cross-platform tests
        /// can pin both values without a graphics device.
        /// </summary>
        internal (Rectangle Bounds, float LayerDepth) ResolveChartVideoDrawLayout()
            => (PerformanceUILayout.Background.Bounds, ChartVideoLayerDepth);

        private void DrawBackground()
        {
            if (_backgroundTexture != null)
            {
                // Draw performance background (7_background.jpg) at furthest back depth
                var backgroundRect = PerformanceUILayout.Background.Bounds;
                _backgroundTexture.Draw(_spriteBatch, backgroundRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 1.0f);
            }
            else
            {
                // Fallback to BackgroundRenderer with consistent depth
                var viewport = _spriteBatch.GraphicsDevice.Viewport;
                var backgroundRect = new Rectangle(0, 0, viewport.Width, viewport.Height);
                _backgroundRenderer?.Draw(_spriteBatch, backgroundRect, 1.0f);
            }

            // Chart background video above the static background, below the lanes.
            // The player renders nothing while no timely frame exists (inactive,
            // failed, or still-decoding media), leaving the fallback visible.
            var (videoBounds, videoDepth) = ResolveChartVideoDrawLayout();
            _chartVideoPlayer?.Draw(_spriteBatch, videoBounds, videoDepth);
        }

        private void DrawLaneBackgrounds()
        {
            if (_visualGates.HideLaneBackground)
                return;

            // Use actual 7_paret.png texture with maximum visibility
            if (_laneBgTexture != null)
            {
                int actualLaneCount = Math.Min(PerformanceUILayout.LaneCount, PerformanceUILayout.LaneStrips.SourceRects.Length);
                
                for (int i = 0; i < actualLaneCount; i++)
                {
                    var sourceRect = PerformanceUILayout.LaneStrips.SourceRects[i];
                    var destRect = PerformanceUILayout.LaneStrips.GetDestinationRect(i);
                    
                    // Normal lane rendering
                    Color laneColor = Color.White;
                    
                    // Draw at background depth
                    _laneBgTexture.Draw(_spriteBatch, destRect, sourceRect, laneColor, 0f, Vector2.Zero, SpriteEffects.None, 0.8f);
                }
            }
            
            // Use LaneBackgroundRenderer as fallback only when lane background texture is not available
            if (_laneBgTexture == null)
            {
                _laneBackgroundRenderer?.Draw(_spriteBatch);
            }
            
            // Draw lane covers if available (7_lanes_Cover_cls.png)
            if (_laneDividerTexture != null)
            {
                // Draw lane covers for unused/hidden lanes at same depth as lanes
                // This would be controlled by game settings to hide specific lanes
                // For now, we'll skip drawing covers to show all lanes
            }
            
            // Draw lane flash overlay if available (effects layer)
            if (_laneFlashTexture != null)
            {
                // Lane flash effects would be drawn per-lane based on hit state
                // This would be implemented in the effects manager
            }
        }

        private void DrawJudgementLine()
        {
            if (_visualGates.HideJudgementLine)
                return;

            if (_judgementLineTexture != null)
            {
                // NX stretches the 8px hit-bar strip across the full drum lane panel.
                var hitBarRect = PerformanceUILayout.HitBar.Bounds;
                var sourceRect = PerformanceUILayout.HitBar.GetSourceRect(_judgementLineTexture.Width, _judgementLineTexture.Height);
                _judgementLineTexture.Draw(_spriteBatch, hitBarRect, sourceRect, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.6f);
            }
            else
            {
                // Fallback to JudgementLineRenderer
                _judgementLineRenderer?.Draw(_spriteBatch);
            }
        }

        [ExcludeFromCodeCoverage]
        private void DrawPads()
        {
            if (_padRenderer == null)
                return;

            _padRenderer.Draw(_spriteBatch);
        }

        [ExcludeFromCodeCoverage]
        private void DrawHitEffects()
        {
            // Draw hit effects using NxAttackEffectManager
            _nxAttackEffectManager?.Draw(_spriteBatch);
        }

        [ExcludeFromCodeCoverage]
        private void DrawJudgementTexts()
        {
            // Draw judgement text popups using sprite sheets with font fallback
            _spriteJudgementTextPopupManager?.Draw(_spriteBatch);
            _fontJudgementTextPopupManager?.Draw(_spriteBatch);
            _hitTimingFeedbackDisplay?.Draw(_spriteBatch);
        }

        [ExcludeFromCodeCoverage]
        private void DrawUIElements()
        {
            // HPA-411 Tab: the skill panel covers the strip's hit marker and key labels,
            // and the progress bar/skill meter obscure the notation look-ahead, so all
            // four draws are skipped while Tab is effective. Their underlying
            // metric/state updates keep running. HPA-509 Tab: the legacy-position
            // gauge/score/combo draws are skipped for the same reason — the Tab HUD
            // renders those values in its own regions instead. Only the legacy-position
            // draws are gated; the managers keep updating either way.
            // The decision keys on the HUD's readiness, which now implies the strip:
            // a HUD that failed to load rolls the strip back in
            // TryConstructNotationStrip, so an inactive Tab HUD means consistent
            // Lanes — including judgement popups via OnJudgementMade (HPA-509 review).
            bool tabHudActive = IsTabHudActive;
            var frame = CurrentFrame;

            // Draw shutters first (overlay elements)
            DrawShutters();

            if (!tabHudActive)
            {
                // Draw skill panel
                DrawSkillPanel();

                // Draw progress bar
                DrawProgressBar();

                _skillPanelDisplay?.Draw(_spriteBatch, frame);
                _skillMeterDisplay?.Draw(_spriteBatch);
            }

            if (tabHudActive)
            {
                // HPA-509: compact Tab HUD (header/combo/LIFE/progress/stats).
                _tabHudDisplay?.Draw(_spriteBatch, frame, _visualGates.HideCombo);
            }
            else
            {
                // Draw gauge elements
                DrawGaugeElements();

                // Draw existing UI components as fallback
                // Legacy gauge display removed - using asset-based gauge only

                _scoreDisplay?.Draw(_spriteBatch);
                if (!_visualGates.HideCombo)
                    _comboDisplay?.Draw(_spriteBatch);
            }

            _uiManager?.Draw(_spriteBatch, 0);

            // Draw overlays last (topmost)
            DrawOverlays();
        }

        internal PerformanceFrameState CurrentFrame => Volatile.Read(ref _publishedTelemetry).Frame;

        /// <summary>
        /// Draws shutter animation using DTXManiaNX layout
        /// </summary>
        private void DrawShutters()
        {
            // Shutter is only drawn during intro/outro animations, not during active gameplay
            // During normal gameplay, the shutter should be fully open (not visible)
            if (_shutterTexture != null && CurrentPhase != StagePhase.Normal)
            {
                // Draw shutter at UI overlay depth - should appear above gameplay elements
                var shutterPos = PerformanceUILayout.Shutter.StartPosition;
                var shutterRect = new Rectangle((int)shutterPos.X, (int)shutterPos.Y, _shutterTexture.Width, _shutterTexture.Height);
                _shutterTexture.Draw(_spriteBatch, shutterRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.2f);
            }
        }
        
        /// <summary>
        /// Draws life gauge with base and fill using DTXManiaNX layout
        /// </summary>
        private void DrawGaugeElements()
        {
            if (_gaugeBaseTexture != null)
            {
                // Draw gauge frame at DTXManiaNX position (294, 626) at UI depth
                var framePos = PerformanceUILayout.Gauge.FramePosition;
                var frameRect = new Rectangle((int)framePos.X, (int)framePos.Y, _gaugeBaseTexture.Width, _gaugeBaseTexture.Height);
                _gaugeBaseTexture.Draw(_spriteBatch, frameRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.19f);
            }
            
            if (_gaugeFillTexture != null && _currentGaugeValue > 0)
            {
                // Draw gauge fill at origin position with life percentage width at UI depth
                var fillOrigin = PerformanceUILayout.Gauge.FillOrigin;
                var fillHeight = PerformanceUILayout.Gauge.FillHeight;
                var maxWidth = _gaugeFillTexture.Width; // Use actual texture width instead of hardcoded value
                var fillWidth = (int)(maxWidth * _currentGaugeValue);
                
                var sourceRect = new Rectangle(0, 0, fillWidth, fillHeight);
                var destRect = new Rectangle((int)fillOrigin.X, (int)fillOrigin.Y, fillWidth, fillHeight);
                
                _gaugeFillTexture.Draw(_spriteBatch, destRect, sourceRect, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.18f);
            }
        }
        
        /// <summary>
        /// Draws song progress bar using DTXManiaNX layout (right side)
        /// </summary>
        private void DrawProgressBar()
        {
            if (_progressBaseTexture != null)
            {
                // Draw progress frame (853, 0, 60, 540) at UI depth
                var frameRect = PerformanceUILayout.Progress.FrameBounds;
                // Draw progress frame (853, 0, 60, 540) at UI depth
                var progressRect = new Rectangle(frameRect.X, frameRect.Y, _progressBaseTexture.Width, _progressBaseTexture.Height);
                _progressBaseTexture.Draw(_spriteBatch, progressRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.2f);
            }
            
            // Draw progress fill using generated colored segments
            if (_currentProgressValue > 0)
            {
                var barRect = PerformanceUILayout.Progress.BarBounds;
                var fillHeight = (int)(barRect.Height * _currentProgressValue);
                
                // Use fallback white texture to draw colored progress fill at UI depth
                if (_fallbackRectangleDrawer != null || _fallbackWhiteTexture != null)
                {
                    var fillRect = new Rectangle(barRect.X, barRect.Bottom - fillHeight, barRect.Width, fillHeight);
                    DrawFallbackRectangle(fillRect, Color.LightBlue, 0.2f);
                }
            }
        }

        private void DrawFallbackRectangle(Rectangle destinationRectangle, Color color, float layerDepth)
        {
            if (_fallbackRectangleDrawer != null)
            {
                _fallbackRectangleDrawer(destinationRectangle, color, layerDepth);
                return;
            }

            if (_fallbackWhiteTexture != null)
            {
                DrawFallbackTexture(_fallbackWhiteTexture, destinationRectangle, color, layerDepth);
            }
        }

        [ExcludeFromCodeCoverage]
        internal virtual void DrawFallbackTexture(Texture2D texture, Rectangle destinationRectangle, Color color, float layerDepth)
        {
            _spriteBatch.Draw(texture, destinationRectangle, null, color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
        }
        
        /// <summary>
        /// Draws skill panel using DTXManiaNX layout
        /// </summary>
        private void DrawSkillPanel()
        {
            if (_skillPanelTexture != null)
            {
                // Draw skill panel at DTXManiaNX position (22, 250) at UI depth
                var panelPos = PerformanceUILayout.SkillPanel.PanelPosition;
                var panelRect = new Rectangle((int)panelPos.X, (int)panelPos.Y, _skillPanelTexture.Width, _skillPanelTexture.Height);
                _skillPanelTexture.Draw(_spriteBatch, panelRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.2f);
            }
        }
        
        /// <summary>
        /// Draws pause and danger overlays when appropriate
        /// </summary>
        private void DrawOverlays()
        {
            if (_isPaused && _pauseOverlayTexture != null)
            {
                // Draw pause overlay fullscreen at topmost depth
                // Draw pause overlay fullscreen at topmost depth
                var pauseRect = new Rectangle(0, 0, _pauseOverlayTexture.Width, _pauseOverlayTexture.Height);
                _pauseOverlayTexture.Draw(_spriteBatch, pauseRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.05f);
            }
            
            if (_isDanger && _dangerOverlayTexture != null)
            {
                // Draw danger tint overlay with pulsing effect at topmost depth
                // Tile the danger texture across the screen if needed
                var tileSize = PerformanceUILayout.Danger.TileSize;
                var alpha = 0.3f + 0.2f * (float)Math.Sin(_totalTime * 4.0);
                
                // Simple fullscreen draw for now - tiling could be added later
                // Draw danger tint overlay with pulsing effect at topmost depth
                var dangerRect = new Rectangle(0, 0, _dangerOverlayTexture.Width, _dangerOverlayTexture.Height);
                _dangerOverlayTexture.Draw(_spriteBatch, dangerRect, null, Color.White * alpha, 0f, Vector2.Zero, SpriteEffects.None, 0.05f);
            }
        }

        #endregion

        #region Stage Completion

        /// <summary>
        /// Finalizes the performance, pauses input, stops the song timer, and prepares the performance summary
        /// </summary>
        private void FinalizePerformance(CompletionReason reason)
        {
            if (Rules?.TryComplete() != true)
                return;

            // Pause input handling and deactivate judgement manager
            _inputPaused = true;
            if (Rules?.JudgementManager is { } judgementManager)
            {
                judgementManager.IsActive = false;
            }

            // Stop audible gameplay audio instances (song timer, scheduled BGM
            // tracks and chip sounds) immediately. The transition to ResultStage is
            // instant, but scheduled BGM tracks are SoundEffectInstances tracked
            // independently from the song timer; without this they would keep
            // playing after gameplay has logically ended and bleed into the
            // result stage. Full disposal remains in CleanupComponents.
            StopGameplayAudioInstances();

            // Build the performance summary
            var summaryChart = _selectedSong?.GetCurrentDifficultyChart(_selectedDifficulty);
            var summary = Rules!.BuildSummary(reason, _playbackModifiers,
                summaryChart?.DrumLevel ?? 0, summaryChart?.DrumLevelDec ?? 0);

            // Pass the summary to ResultStage
            // No debounce needed here since this is an automatic transition based on game completion
            PublishTelemetry();
            StageManager?.ChangeStage(StageType.Result, new InstantTransition(),
                new PerformanceResult(summary, _selectedSong, _selectedDifficulty));
        }

        #endregion


        #region Telemetry

        private sealed record PublishedTelemetry
        {
            internal string? SelectedSongTitle { get; init; }
            internal int SelectedDifficulty { get; init; }
            internal bool PerformanceReady { get; init; }
            internal int PlaySpeedPercent { get; init; } = 100;
            internal int PitchSemitones { get; init; }
            internal bool PlaybackProfileFrozen { get; init; } = true;
            internal int AudioPreparationCompleted { get; init; }
            internal int AudioPreparationTotal { get; init; }
            internal int AudioPreparationCacheHits { get; init; }
            internal long PreparedAudioBytes { get; init; }
            internal bool AutoPlayEnabled { get; init; }
            internal bool StageCompleted { get; init; }
            internal bool ClockReadable { get; init; }
            internal PerformanceFrameState Frame { get; init; }
            internal string NoteDisplayStyle { get; init; } = nameof(DrumsNoteDisplayStyle.Lanes);
            internal LastLaneHit? LastHit { get; init; }
        }

        // Called only by the update/lifecycle thread; the API thread never touches live objects.
        private void PublishTelemetry()
        {
            var audioSession = _audioSession;
            var currentGameTime = _currentGameTime;
            var chartManager = Rules?.ChartManager;
            var progress = _audioPreparationProgress;
            var readable = audioSession != null && (audioSession.IsPlaying || audioSession.IsPaused);
            var rawMs = audioSession != null && currentGameTime != null
                ? audioSession.GetCurrentMs(currentGameTime) : 0.0;
            var snapshot = new PublishedTelemetry
            {
                SelectedSongTitle = _selectedSong?.DisplayTitle ?? _selectedSong?.Title,
                SelectedDifficulty = _selectedDifficulty,
                PerformanceReady = !_isLoading && chartManager != null && audioSession != null
                    && Rules?.IsCompleted != true && (_isReady || readable),
                PlaySpeedPercent = _playbackModifiers.PlaySpeedPercent,
                PitchSemitones = _playbackModifiers.PitchSemitones,
                AudioPreparationCompleted = progress?.CompletedCount ?? 0,
                AudioPreparationTotal = progress?.TotalCount ?? 0,
                AudioPreparationCacheHits = progress?.CacheHitCount ?? 0,
                PreparedAudioBytes = audioSession?.PreparedSet?.DecodedPcmBytes
                    ?? progress?.DecodedByteEstimate ?? 0L,
                AutoPlayEnabled = FrozenAutoPlayLanes.Count == PerformanceUILayout.LaneCount,
                StageCompleted = Rules?.IsCompleted == true,
                ClockReadable = readable,
                // Lanes and the pre-refactor Tab HUD retain the last gameplay progress after Stop.
                Frame = Rules?.BuildFrameState(rawMs, chartManager != null ? _currentProgressValue : 0f) ?? default,
                NoteDisplayStyle = _notationStrip != null
                    ? nameof(DrumsNoteDisplayStyle.Tab) : nameof(DrumsNoteDisplayStyle.Lanes),
                LastHit = _lastLaneHit
            };
            Volatile.Write(ref _publishedTelemetry, snapshot);
        }

        public void PopulateTelemetry(GameTelemetrySnapshot telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);
            var snapshot = Volatile.Read(ref _publishedTelemetry);
            telemetry.SelectedSongTitle = snapshot.SelectedSongTitle;
            telemetry.SelectedDifficulty = snapshot.SelectedDifficulty;
            telemetry.PerformanceReady = snapshot.PerformanceReady;
            telemetry.PlaySpeedPercent = snapshot.PlaySpeedPercent;
            telemetry.PitchSemitones = snapshot.PitchSemitones;
            telemetry.PlaybackProfileFrozen = snapshot.PlaybackProfileFrozen;
            telemetry.AudioPreparationCompleted = snapshot.AudioPreparationCompleted;
            telemetry.AudioPreparationTotal = snapshot.AudioPreparationTotal;
            telemetry.AudioPreparationCacheHits = snapshot.AudioPreparationCacheHits;
            telemetry.PreparedAudioBytes = snapshot.PreparedAudioBytes;
            telemetry.AutoPlayEnabled = snapshot.AutoPlayEnabled;
            telemetry.StageCompleted = snapshot.StageCompleted;
            telemetry.CurrentSongTimeMs = snapshot.ClockReadable ? snapshot.Frame.ElapsedMs : 0.0;
            telemetry.Score = snapshot.Frame.Score;
            telemetry.CurrentCombo = snapshot.Frame.Combo;
            telemetry.MaxCombo = snapshot.Frame.MaxCombo;
            telemetry.Gauge = snapshot.Frame.Gauge;
            telemetry.HasFailed = snapshot.Frame.HasFailed;
            telemetry.TotalNotes = snapshot.Frame.TotalNotes;
            telemetry.PerfectCount = snapshot.Frame.StatusPerfectCount;
            telemetry.GreatCount = snapshot.Frame.StatusGreatCount;
            telemetry.GoodCount = snapshot.Frame.StatusGoodCount;
            telemetry.PoorCount = snapshot.Frame.StatusPoorCount;
            telemetry.MissCount = snapshot.Frame.StatusMissCount;
            telemetry.NoteDisplayStyle = snapshot.NoteDisplayStyle;
            telemetry.LastLaneHitLane = snapshot.LastHit?.Lane;
            telemetry.LastLaneHitButtonId = snapshot.LastHit?.ButtonId;
            telemetry.LastLaneHitSongTimeMs = snapshot.LastHit?.SongTimeMs;
        }

        #endregion

        #region Disposal

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _spriteBatch?.Dispose();
                _uiManager?.Dispose();
            }

            base.Dispose(disposing);
        }

        #endregion

        #region Logging

        /// <summary>
        /// Logs performance-related errors and warnings
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="exception">Optional exception to include in the log</param>
        private void LogPerformanceError(string message, Exception? exception = null)
        {
            // Routed through the stage logger (Console-backed in production) so
            // the error stays visible in all build configurations.
            GameplayAudioSession.LogCapturedError(Logger, message, exception);
        }

        #endregion
    }
}
