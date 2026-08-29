#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Utilities;

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
    /// JudgementMade events that are forwarded to ScoreManager, ComboManager,
    /// and GaugeManager for processing.
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
        private int _songId;

        // Performance components - initialized in InitializeComponents
        private BackgroundRenderer _backgroundRenderer = null!;
        private LaneBackgroundRenderer _laneBackgroundRenderer = null!;
        private JudgementLineRenderer _judgementLineRenderer = null!;
        private ScoreDisplay _scoreDisplay = null!;
        private ComboDisplay _comboDisplay = null!;

        // Phase 2 components - Chart loading and note scrolling
        private ParsedChart _parsedChart = null!;
        private ChartManager _chartManager = null!;
        private AudioLoader _audioLoader = null!;
        private SongTimer _songTimer = null!;
        private NoteRenderer _noteRenderer = null!;

        // Phase 3 components - Gameplay managers
        private JudgementManager _judgementManager = null!;
        private ScoreManager _scoreManager = null!;
        private ComboManager _comboManager = null!;
        private GaugeManager _gaugeManager = null!;
        private SkillManager? _skillManager;
        private SkillPanelDisplay? _skillPanelDisplay;
        private SkillMeterDisplay? _skillMeterDisplay;
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

        // BGM management
        private Dictionary<string, ISound> _bgmSounds = new Dictionary<string, ISound>();
        private List<BGMEvent> _scheduledBGMEvents = new List<BGMEvent>();
        private readonly List<SoundEffectInstance> _activeBgmInstances = new();

        // Drum chip-sound cache (per-note WAV playback for autoplay + player input)
        private ChipSoundCache _chipSoundCache = null!;
        private PreparedGameplayAudioSet? _preparedAudioSet;
        private PlaybackModifiers _playbackModifiers = new(100, 0);
        private bool _metronomeEnabled;
        private ISound? _metronomeBeatSound;
        private ISound? _metronomeAccentSound;
        private MetronomePlayer? _metronomePlayer;
        private CancellationTokenSource? _initializationCts;
        private Task? _initializationTask;
        private int _activationGeneration;
        private object? _audioLifecycleGate = new();
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
        private double? _chartEndReachedRealTimeSeconds;
        // Last-hit telemetry, published on the Update thread (OnLaneHitForPadFeedback) and
        // read on the Kestrel API thread (PopulateTelemetry). Collapsed into a single
        // immutable reference so the API thread can never observe a torn combination
        // (e.g. a stale lane paired with a fresh song time): reference assignment is
        // atomic on .NET, and the record's init-only fields are fully constructed before
        // the reference is published. "No hit yet" is represented by a null reference.
        // Matches the existing non-volatile cross-thread read pattern used by the other
        // PopulateTelemetry fields (_stageCompleted, _isLoading, ...); freshness is not a
        // goal here — only internal consistency of the three values.
        internal sealed record LastLaneHit(int Lane, string? ButtonId, double SongTimeMs);
        private LastLaneHit? _lastLaneHit;
        private Texture2D _fallbackWhiteTexture = null!;
        private Action<Rectangle, Color, float>? _fallbackRectangleDrawer = null;
        
        // UI state tracking
        private bool _isPaused = false;
        private bool _isDanger = false;
        private float _currentGaugeValue = 0.5f; // 0.0 to 1.0
        private float _currentProgressValue = 0.0f; // 0.0 to 1.0

        // Stage completion state
        private bool _stageCompleted = false;
        private bool _inputPaused = false;
        private PerformanceSummary _performanceSummary = null!;
        
        // Autoplay functionality. The lane set is copied from config at
        // activation (frozen for the run); the live ConfigData collection is
        // never retained. An empty set means fully manual play.
        private HashSet<int> _autoPlayLanes = new HashSet<int>();

        /// <summary>
        /// Read accessor for the frozen lane set. Lazily materializes an empty
        /// set so reflection-created (uninitialized) stage instances — common
        /// in tests — behave as fully manual play instead of throwing
        /// NullReferenceException.
        /// </summary>
        private HashSet<int> FrozenAutoPlayLanes => _autoPlayLanes ??= new HashSet<int>();

        private bool _autoAddGaugeEnabled = true;
        private GaugeDamageLevel _gaugeDamageLevel = GaugeDamageLevel.Normal;
        private int _riskyLimit = RiskyRange.Default;
        private bool _gaugeFailureEnabled = true;
        private int _autoPlayNoteIndex = 0; // Track the next note to auto-hit
        private PerformanceVisualGates _visualGates;

        private readonly record struct PerformanceVisualGates(
            bool HideLaneBackground,
            bool HideMeasureLines,
            bool HideJudgementLine,
            bool HideCombo,
            bool EnableLaneFlush,
            bool ShowHitTimingFeedback);
        
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

        protected virtual IAudioVariantProcessor CreateAudioVariantProcessor()
            => new FfmpegAudioVariantProcessor();

        protected virtual PlaybackAudioVariantCache CreateAudioVariantCache()
            => new();

        /// <summary>
        /// Creates the HPA-11 chart background video player. Extracted as a seam so
        /// headless tests can substitute a recording fake instead of launching FFmpeg.
        /// Generation failures are routed through the stage's Console-based error log,
        /// matching how the song timer and metronome report diagnostics.
        /// </summary>
        protected virtual IChartVideoPlayer CreateChartVideoPlayer()
            => new FfmpegChartVideoPlayer(
                _spriteBatch.GraphicsDevice,
                message => LogPerformanceError(message));

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
            var generation = ++_activationGeneration;
            _initializationCts?.Cancel();
            _initializationCts?.Dispose();
            _initializationCts = new CancellationTokenSource();
            // Extract shared data from stage transition
            ExtractSharedData();

            // Freeze run configuration from config
            FreezeRunConfiguration();

            // Initialize UI components
            InitializeComponents();

            // Start async chart loading and audio preparation
            _initializationTask = InitializeGameplayCoreAsync(
                generation,
                _initializationCts.Token,
                throwOnFailure: false);

            var configManager = _game?.ConfigManager;
            if (configManager != null)
            {
                _subscribedConfigManager = configManager;
                configManager.ScrollSpeedChanged += OnScrollSpeedChanged;
            }
        }

        protected override void OnDeactivate()
        {
            ++_activationGeneration;
            _initializationCts?.Cancel();
            ObserveInitializationTask();
            if (_subscribedConfigManager != null)
            {
                _subscribedConfigManager.ScrollSpeedChanged -= OnScrollSpeedChanged;
                _subscribedConfigManager.FlushPendingSave();
                _subscribedConfigManager = null;
            }

            // Serialize teardown against the final, synchronous publication step
            // of audio preparation.
            lock (AudioLifecycleGate)
            {
                CleanupComponents();
            }

            // Defer CTS disposal until the in-flight initialization task completes
            // to avoid racing the task's token read.
            var pendingTask = _initializationTask;
            var pendingCts = _initializationCts;
            _initializationTask = null;
            _initializationCts = null;

            if (pendingTask == null || pendingTask.IsCompleted)
            {
                pendingCts?.Dispose();
                return;
            }

            pendingTask.ContinueWith(
                _ => pendingCts?.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        protected override void OnUpdate(double deltaTime)
        {
            // Update total time for precise GameTime tracking
            _totalTime += deltaTime;

            // Track elapsed time since stage activation for miss detection
            _stageElapsedTime += deltaTime;

            // Create GameTime for precise timing
            _currentGameTime = new GameTime(TimeSpan.FromSeconds(_totalTime), TimeSpan.FromSeconds(deltaTime));

            // Handle input
            HandleInput();

            // Update UI manager
            _uiManager?.Update(deltaTime);

            // Update performance components
            UpdateComponents(deltaTime);

            // Update scroll-speed indicator toast
            _scrollSpeedIndicator?.Update(_currentGameTime);

            // Update gameplay state
            UpdateGameplay(deltaTime);

            // Update song timer
            _songTimer?.Update(_currentGameTime);
        }


        [ExcludeFromCodeCoverage]
        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;


            // BackToFront depth order:
            // Background (1.0f) → Lanes (0.8f) → Measure lines (0.78f) →
            // fallback notes (0.70f) → JudgementLine (0.6f) →
            // Pads (0.1f) → sprite notes (0.05f).

            // Base pass: Background → Lanes → Pads → Notes → Judgement Line → Judgement Texts
            _spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend);
            
            // Draw background (furthest back - highest depth value)
            DrawBackground();

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

            // Draw judgement text popups
            DrawJudgementTexts();

            _spriteBatch.End();

            // Overlay pass: Note overlays with alpha blending (above notes, below UI)
            _spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend);

            // Draw note overlay animations with alpha blending (on top of base animations)
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

            // Draw hit effects with additive blending
            DrawHitEffects();

            _spriteBatch.End();
        }

        #endregion

        #region Initialization and Cleanup

        private void ExtractSharedData()
        {
            if (_sharedData != null)
            {
                if (_sharedData.TryGetValue("selectedSong", out var songObj) && songObj is SongListNode song)
                {
                    _selectedSong = song;
                }

                if (_sharedData.TryGetValue("selectedDifficulty", out var difficultyObj) && difficultyObj is int difficulty)
                {
                    _selectedDifficulty = difficulty;
                }

                if (_sharedData.TryGetValue("songId", out var songIdObj) && songIdObj is int songId)
                {
                    _songId = songId;
                }

                // Extract parsed chart data if available
                if (_sharedData.TryGetValue("parsedChart", out var chartObj) && chartObj is ParsedChart parsedChart)
                {
                    _parsedChart = parsedChart;
                }
            }
        }

        private void FreezeRunConfiguration()
        {
            var config = _game?.ConfigManager?.Config;
            var laneDisplayMode = config?.LaneDisplayMode ?? DrumsLaneDisplayMode.AllOn;
            _visualGates = new PerformanceVisualGates(
                HideLaneBackground: !laneDisplayMode.ShowsLaneBackground(),
                HideMeasureLines: !laneDisplayMode.ShowsMeasureLines(),
                HideJudgementLine: !(config?.ShowJudgementLine ?? true),
                HideCombo: !(config?.ShowCombo ?? true),
                EnableLaneFlush: config?.EnableLaneFlush ?? false,
                ShowHitTimingFeedback: config?.ShowHitTimingFeedback ?? false);

            // Defensive copy: the run owns its lane set. Later config edits
            // (UI toggles, mid-song SetAutoPlayLane) must not alter which
            // lanes this performance automates.
            _autoPlayLanes = new HashSet<int>(config?.AutoPlayLanes ?? Enumerable.Empty<int>());
            _autoAddGaugeEnabled = config?.AutoAddGauge ?? true;
            _gaugeDamageLevel = config?.DamageLevel ?? GaugeDamageLevel.Normal;
            _riskyLimit = RiskyRange.Clamp(config?.Risky ?? RiskyRange.Default);
            _gaugeFailureEnabled = !(config?.NoFail ?? false);
            _autoPlayNoteIndex = 0;

            System.Diagnostics.Debug.WriteLine(
                $"[AutoPlay] FreezeRunConfiguration: lanes={string.Join(",", _autoPlayLanes.OrderBy(lane => lane))}, " +
                $"chartLoaded={_chartManager != null}, " +
                $"notesCount={_chartManager?.AllNotes.Count ?? -1}");
        }

        private void InitializeComponents()
        {
            // Initialize background renderer
            _backgroundRenderer = new BackgroundRenderer(_resourceManager);

            // Start async background loading
            _ = _backgroundRenderer.LoadBackgroundAsync();

            // Initialize lane background and judgement line renderers
            var graphicsDevice = _spriteBatch.GraphicsDevice;
            _laneBackgroundRenderer = new LaneBackgroundRenderer(_resourceManager);
            _judgementLineRenderer = new JudgementLineRenderer(graphicsDevice);

            // Initialize score and combo displays
            _scoreDisplay = new ScoreDisplay(_resourceManager, graphicsDevice);
            _comboDisplay = new ComboDisplay(_resourceManager, graphicsDevice);

            // Skill displays (SkillManager itself is constructed in InitializeGameplayManagers
            // because it needs ComboManager + ChartManager.TotalNotes which arrive later)
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

            // Initialize Phase 2 components
            _audioLoader = new AudioLoader(_resourceManager);
            _noteRenderer = new NoteRenderer(graphicsDevice, _resourceManager);
            _nxAttackEffectManager = new NxAttackEffectManager(_resourceManager);
            _fontJudgementTextPopupManager = new JudgementTextPopupManager(graphicsDevice, _resourceManager);
            _spriteJudgementTextPopupManager = new SpriteJudgementTextPopupManager(
                _resourceManager,
                judgementEvent => _fontJudgementTextPopupManager?.SpawnPopup(judgementEvent));
            _hitTimingFeedbackDisplay = new HitTimingFeedbackDisplay(_resourceManager);
            _padRenderer = new PadRenderer(graphicsDevice, _resourceManager);

            // HPA-11: chart background video player; scheduling state resets per activation.
            _chartVideoPlayer = CreateChartVideoPlayer();
            _nextVideoEventIndex = 0;
            _activeVideoEvent = null;

            // Initialize UX components
            InitializeReadyFont();
            
            // Load performance UI assets using DTXManiaNX layout
            LoadPerformanceUIAssets();

            // Create a reusable white texture for fallback rendering
            _fallbackWhiteTexture = new Texture2D(graphicsDevice, 1, 1);
            _fallbackWhiteTexture.SetData(new[] { Color.White });
        }

        private void CleanupComponents()
        {

            // Reset state variables to initial values for proper reactivation
            _isLoading = true; // Initial state is loading
            _isReady = false; // Initial state is not ready
            _stageCompleted = false; // Initial state is not completed
            _inputPaused = false; // Initial state is input enabled
            _totalTime = 0.0; // Reset total time
            _stageElapsedTime = 0.0; // Reset elapsed time for miss detection
            _chartEndReachedRealTimeSeconds = null;
            _readyCountdown = 1.0; // Reset ready countdown
            _autoPlayNoteIndex = 0; // Reset autoplay note index
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

            // Cleanup score and combo displays
            _scoreDisplay?.Dispose();
            _scoreDisplay = null;
            _comboDisplay?.Dispose();
            _comboDisplay = null;
            _skillPanelDisplay?.Dispose();
            _skillPanelDisplay = null;
            _skillMeterDisplay?.Dispose();
            _skillMeterDisplay = null;


            // Cleanup Phase 2 components
            _songTimer?.Dispose();
            _songTimer = null;
            _audioLoader?.Dispose();
            _audioLoader = null;
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

            // Cleanup fallback texture
            _fallbackWhiteTexture?.Dispose();
            _fallbackWhiteTexture = null;

            foreach (var instance in _activeBgmInstances ?? Enumerable.Empty<SoundEffectInstance>())
            {
                try { instance.Stop(); instance.Dispose(); }
                catch { }
            }
            _activeBgmInstances?.Clear();
            _bgmSounds?.Clear();
            _scheduledBGMEvents?.Clear();

            // Cleanup gameplay managers
            CleanupGameplayManagers();

            _metronomePlayer = null;
            _metronomeBeatSound?.RemoveReference();
            _metronomeBeatSound = null;
            _metronomeAccentSound?.RemoveReference();
            _metronomeAccentSound = null;

            _preparedAudioSet?.Dispose();
            _preparedAudioSet = null;

            // Clear chart data
            _parsedChart = null;
            _chartManager = null;

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
            if (_songTimer?.IsPlaying == true && !_inputPaused && !_isLoading && !(_isReady && _readyCountdown > 0))
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
            // 1. Stop the song timer
            _songTimer?.Stop();

            // 2. Stop audible gameplay audio instances (scheduled BGM tracks and
            //    chip sounds) immediately. The transition uses a 0.5s fade, so
            //    without this the backing tracks and chip instances owned by
            //    ChipSoundCache would keep playing after gameplay has logically
            //    stopped and bleed into the song-selection stage. Full disposal
            //    remains in CleanupComponents via OnDeactivate.
            StopGameplayAudioInstances();

            // 3. Deactivate judgement manager to stop processing input
            if (_judgementManager != null)
            {
                _judgementManager.IsActive = false;
            }

            // 4. Pause input to block further judgement processing
            _inputPaused = true;

            // 5. Component cleanup will be handled automatically by OnDeactivate() during stage transition
            // This prevents premature texture disposal that causes gauge flickering

            // Return to song selection stage
            StageManager?.ChangeStage(StageType.SongSelect,
                new DTXManiaFadeTransition(0.5), null);
        }

        /// <summary>
        /// Stops all active scheduled-BGM and chip-sound instances immediately.
        /// Used by both gameplay exit paths (ReturnToSongSelect and FinalizePerformance)
        /// to silence audio before the stage transition begins. The instances are
        /// tracked independently from the song timer (scheduled BGM) and by
        /// ChipSoundCache (chip sounds); stopping only the song timer leaves them
        /// audible through the transition fade. Full stop-and-dispose of these
        /// instances remains in CleanupComponents.
        /// </summary>
        private void StopGameplayAudioInstances()
        {
            // Stop scheduled BGM instances. These are SoundEffectInstances owned
            // by this stage (added in TriggerBGMEvent); CleanupComponents later
            // stops and disposes them, but we must stop them now so they do not
            // remain audible through the transition fade.
            if (_activeBgmInstances != null)
            {
                foreach (var instance in _activeBgmInstances)
                {
                    try { instance.Stop(); }
                    catch { /* Best effort — teardown will dispose. */ }
                }
            }

            // Stop chip sound instances owned by ChipSoundCache. StopAll leaves
            // the borrowed ISound objects and the instances themselves owned by
            // the cache; Dispose (called from CleanupGameplayManagers via
            // CleanupComponents) handles final stop-and-dispose.
            _chipSoundCache?.StopAll();

            // Cancel the chart background video generation so the last decoded
            // frame does not persist through the transition fade. Full disposal
            // remains in CleanupComponents.
            _chartVideoPlayer?.Stop();
        }

        private void ObserveInitializationTask()
        {
            var task = _initializationTask;
            if (task == null)
                return;

            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted |
                    TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void FailLoading(string message)
        {
            _isLoading = false;
            _isReady = false;
            _loadErrorMessage = message;
            LogPerformanceError(message);
        }

        private void OnScrollSpeedChanged(object? sender, ScrollSpeedChangedEventArgs e)
        {
            _noteRenderer?.SetScrollSpeed(e.NewPercent);
            _scrollSpeedIndicator?.Show(e.NewPercent);
        }

        #endregion

        #region Phase 2 - Chart Loading and Gameplay

        /// <summary>
        /// Initializes gameplay using pre-parsed chart data and loads audio
        /// </summary>
        private Task InitializeGameplayAsync()
        {
            // Compatibility entry point used by deterministic tests and older callers
            // that initialize directly without the normal OnActivate freeze, so capture
            // the configured visual/gameplay gates here before core initialization.
            FreezeRunConfiguration();

            if (_playbackModifiers.PlaySpeedPercent == 0)
                _playbackModifiers = new PlaybackModifiers(100, 0);

            return InitializeGameplayCoreAsync(
                _activationGeneration,
                CancellationToken.None,
                throwOnFailure: true);
        }

        private async Task InitializeGameplayCoreAsync(
            int generation,
            CancellationToken cancellationToken,
            bool throwOnFailure)
        {
            PreparedGameplayAudioSet? locallyPreparedSet = null;
            try
            {
                _isLoading = true;

                // Check if we have a parsed chart from shared data
                if (_parsedChart == null)
                {
                    // Guard against null song - can happen if shared data was missing
                    if (_selectedSong == null)
                    {
                        FailLoading("No song was selected.");
                        return;
                    }
                    
                    // Fallback: parse chart if not provided (for backwards compatibility)
                    // Get the correct chart for the selected difficulty
                    var chart = _selectedSong.GetCurrentDifficultyChart(_selectedDifficulty);
                    var chartPath = chart?.FilePath;
                    if (string.IsNullOrEmpty(chartPath))
                    {
                        FailLoading("The selected chart path is unavailable.");
                        return;
                    }

                    _parsedChart = await DTXChartParser.ParseAsync(chartPath);
                }
                cancellationToken.ThrowIfCancellationRequested();

                // Create chart manager
                _chartManager = new ChartManager(_parsedChart);

                // Initialize gameplay managers
                InitializeGameplayManagers();

                // Set BPM and scroll speed in note renderer
                _noteRenderer?.SetBpm(_parsedChart.Bpm);

                // Set scroll speed based on user preference (read from config, applied at chart load).
                // In-game adjustments are handled via ConfigManager.ScrollSpeedChanged subscription.
                var scrollSpeedSetting = _game?.ConfigManager?.Config?.ScrollSpeed ?? ScrollSpeedRange.Default;
                _noteRenderer?.SetScrollSpeed(scrollSpeedSetting);

                var noteWavIds = new HashSet<string>(
                    _parsedChart.Notes.Select(n => n.Value)
                        .Where(v => !string.IsNullOrEmpty(v)));
                var noteWavDefs = _parsedChart.WavDefinitions
                    .Where(kvp => noteWavIds.Contains(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                var progress = new InlineProgress<AudioPreparationProgress>(value =>
                {
                    if (generation == _activationGeneration &&
                        !cancellationToken.IsCancellationRequested)
                    {
                        _audioPreparationProgress = value;
                    }
                });
                locallyPreparedSet = await PreparedGameplayAudioSet.PrepareAsync(
                    _parsedChart.BackgroundAudioPath,
                    _parsedChart.BGMEvents.Select(bgm => bgm.AudioFilePath),
                    noteWavDefs,
                    _playbackModifiers,
                    _playbackModifiers.IsDefault ? null : CreateAudioVariantProcessor(),
                    _playbackModifiers.IsDefault ? null : CreateAudioVariantCache(),
                    progress,
                    cancellationToken);

                lock (AudioLifecycleGate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (generation != _activationGeneration)
                        return;

                    _preparedAudioSet = locallyPreparedSet;
                    locallyPreparedSet = null;
                    _audioLoader.BindBorrowedBackground(
                        _preparedAudioSet.MainBackground,
                        _parsedChart.BackgroundAudioPath);
                    _chipSoundCache = new ChipSoundCache(
                        _preparedAudioSet.ChipSoundsByWavId);
                    BindPreparedBGMSounds();

                    // Schedule BGM events for playback
                    _scheduledBGMEvents = _parsedChart.BGMEvents
                        .OrderBy(bgm => bgm.TimeMs)
                        .ToList();

                    // Create song timer with logging support
                    var songTimer = _audioLoader.CreateSongTimer(
                        _playbackModifiers.PlaySpeedPercent,
                        _preparedAudioSet.RuntimePitch,
                        message => LogPerformanceError(message));
                    if (songTimer == null)
                    {
                        LogPerformanceError("PerformanceStage: No audio timer available; using silent GameTime clock");
                        songTimer = new SongTimer(
                            _playbackModifiers.PlaySpeedPercent,
                            message => LogPerformanceError(message));
                    }
                    _songTimer = songTimer;

                    InitializeMetronome();

                    // Note: per-WAV #VOLUME/#PAN for the background track are applied in
                    // StartSong() (no-BGM-events path) rather than here, because StartSong()
                    // overwrites Volume when playback begins.
                    _isLoading = false;
                    _isReady = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (generation == _activationGeneration)
                    _isLoading = false;
            }
            catch (Exception ex)
            {
                if (generation == _activationGeneration)
                    FailLoading($"Audio preparation failed: {ex.Message}");
                if (throwOnFailure)
                    throw;
            }
            finally
            {
                locallyPreparedSet?.Dispose();
            }
        }

        private void InitializeMetronome()
        {
            if (!_metronomeEnabled || _parsedChart == null || _metronomePlayer != null)
                return;

            _metronomeBeatSound = TryLoadMetronomeSound(SoundPath.MetronomeBeat);
            _metronomeAccentSound = TryLoadMetronomeSound(SoundPath.MetronomeAccent);

            var speed = _playbackModifiers.PlaySpeedPercent > 0
                ? _playbackModifiers.Speed
                : 1.0;
            var maxLateChartMs = 100.0 * speed;
            _metronomePlayer = new MetronomePlayer(
                _parsedChart.BeatMarkers,
                maxLateChartMs,
                PlayMetronomeClick);
        }

        private ISound? TryLoadMetronomeSound(string path)
        {
            try
            {
                return _resourceManager?.LoadSound(path);
            }
            catch (Exception ex)
            {
                LogPerformanceError($"PerformanceStage: Metronome sound unavailable ({path}): {ex.Message}");
                return null;
            }
        }

        protected virtual void PlayMetronomeClick(BeatMarker marker)
        {
            try
            {
                var sound = marker.IsMeasureStart
                    ? _metronomeAccentSound
                    : _metronomeBeatSound;

                sound?.SoundEffect?.Play(1.0f, 0.0f, 0.0f);
            }
            catch (Exception ex)
            {
                LogPerformanceError($"PerformanceStage: Metronome click playback failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates gameplay state and timing
        /// </summary>
        private void UpdateGameplay(double deltaTime)
        {
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

            // Update pad renderer
            _padRenderer?.Update(deltaTime);

            // Handle BGM event scheduling and gameplay managers
            if (_songTimer != null && _songTimer.IsPlaying)
            {
                // Only run timing logic when the song is actually playing
                var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
                
                // Process BGM events during song playback
                ProcessBGMEvents(currentTimeMs);

                // HPA-11: schedule chart background video events on the same
                // logical clock (never re-read from GetCurrentMs, never speed-scaled).
                ProcessVideoEvents(currentTimeMs);

                // Dual-clock architecture:
                // - Raw song clock (currentTimeMs): autoplay, BGM events, note visuals,
                //   progress, completion — everything that must stay synced with the chart.
                // - Compensated clock (playerJudgementTimeMs): player input judgement only.
                //   Subtracting AudioLatencyOffsetMs aligns hit detection with what the
                //   player actually hears, compensating for audio buffer/driver output delay.
                var playerJudgementTimeMs = GetPlayerJudgementTimeMs(currentTimeMs);

                UpdateGameplayManagers(currentTimeMs, playerJudgementTimeMs);
                
                // Update song progress
                UpdateSongProgress(currentTimeMs);
                
                // Check for stage completion conditions
                CheckStageCompletion(currentTimeMs);
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
        {
            var offsetMs = _game?.ConfigManager?.Config?.AudioLatencyOffsetMs ?? 0;
            if (offsetMs <= 0)
                return currentAudioClockMs;

            // Normal activation always freezes a valid profile. The fallback
            // preserves the legacy 1.0x behavior for reflection-created tests
            // and compatibility callers that bypass OnActivate.
            var speed = _playbackModifiers.PlaySpeedPercent > 0
                ? _playbackModifiers.Speed
                : 1.0;
            var logicalOffsetMs = offsetMs * speed;
            return Math.Max(0.0, currentAudioClockMs - logicalOffsetMs);
        }

        /// <summary>
        /// Starts playing the song
        /// </summary>
        private void StartSong()
        {
            if (_songTimer != null && _currentGameTime != null)
            {
                // Start the song timer - this provides the master clock for notes and BGM events
                _chartEndReachedRealTimeSeconds = null;
                _songTimer.SetPosition(0.0, _currentGameTime);
                bool playbackStarted = _songTimer.Play(_currentGameTime);
                
                if (!playbackStarted)
                {
                    LogPerformanceError("PerformanceStage: Failed to start song playback - audio may be unavailable");
                    // Continue anyway - the game can still run without audio
                }
                
                _isReady = false;

                // Activate the judgement manager now that the song is playing
                if (_judgementManager != null)
                {
                    _judgementManager.IsActive = true;
                }

                // Choose playback strategy based on BGM events
                if (_scheduledBGMEvents.Count > 0)
                {
                    // New approach: Use BGM events for timed playback, silence the background audio
                    _songTimer.Volume = 0.0f; // Mute the background audio since we'll use BGM events
                }
                else
                {
                    // Legacy approach: Play background audio immediately (no BGM events).
                    // Honor the chart's per-WAV #VOLUME/#PAN for the master background track
                    // when a background WAV id is defined; otherwise default to full volume.
                    var backgroundWavId = _parsedChart?.BackgroundWavId;
                    if (!string.IsNullOrEmpty(backgroundWavId))
                    {
                        _songTimer.Volume = _parsedChart.GetVolume(backgroundWavId);
                        _songTimer.Pan = _parsedChart.GetPan(backgroundWavId);
                    }
                    else
                    {
                        _songTimer.Volume = 1.0f; // Ensure background audio is audible
                    }
                }
            }
        }

        private void DrawMeasureLines()
        {
            if (_visualGates.HideMeasureLines)
                return;

            if (_noteRenderer == null ||
                _chartManager == null ||
                _songTimer == null ||
                _currentGameTime == null)
            {
                return;
            }

            if (!_songTimer.IsPlaying && !_songTimer.IsPaused)
                return;

            var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
            var lookAheadMs = _noteRenderer.EffectiveLookAheadMs > 0
                ? _noteRenderer.EffectiveLookAheadMs
                : PerformanceUILayout.NoteDefaultLookAheadMs;
            var activeLines = _chartManager.GetActiveMeasureLines(
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
            if (_noteRenderer == null || _chartManager == null || _songTimer == null || _currentGameTime == null)
                return;

            if (!_songTimer.IsPlaying && !_songTimer.IsPaused)
                return;

            // Get current song time and active notes
            var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
            var lookAheadMs = _noteRenderer.EffectiveLookAheadMs > 0 ? _noteRenderer.EffectiveLookAheadMs : 1500.0;
            var activeNotes = _chartManager.GetActiveNotes(currentTimeMs, lookAheadMs);

            // Draw the notes
            _noteRenderer.DrawNotes(_spriteBatch, activeNotes, currentTimeMs);
        }

        /// <summary>
        /// Draws note overlay animations in effects pass.
        /// </summary>
        private void DrawNoteOverlays()
        {
            if (_noteRenderer == null || _chartManager == null || _songTimer == null || _currentGameTime == null)
                return;

            if (!_songTimer.IsPlaying && !_songTimer.IsPaused)
                return;

            // Get current song time using precise GameTime-based timing
            var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);

            // Get active notes using the same look-ahead time as scroll calculation
            var lookAheadMs = _noteRenderer.EffectiveLookAheadMs > 0 ? _noteRenderer.EffectiveLookAheadMs : 1500.0;
            var activeNotes = _chartManager.GetActiveNotes(currentTimeMs, lookAheadMs);

            // Draw the note overlays with additive blending
            _noteRenderer.DrawNoteOverlays(_spriteBatch, activeNotes, currentTimeMs);
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
                // Font initialization failed, fallback will be used
                System.Diagnostics.Trace.WriteLine($"[PerformanceStage] InitializeReadyFont failed: {ex.Message}");
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
                return _resourceManager?.LoadTexture(path);
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
        /// Loads all BGM sounds referenced by BGM events
        /// </summary>
        private async Task LoadBGMSoundsAsync()
        {
            if (_parsedChart?.BGMEvents == null)
                return;

            foreach (var bgmEvent in _parsedChart.BGMEvents)
            {
                if (string.IsNullOrEmpty(bgmEvent.AudioFilePath) || _bgmSounds.ContainsKey(bgmEvent.WavId))
                    continue;

                try
                {
                    if (File.Exists(bgmEvent.AudioFilePath))
                    {
                        var sound = new ManagedSound(bgmEvent.AudioFilePath);
                        _bgmSounds[bgmEvent.WavId] = sound;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[PerformanceStage] Failed to load BGM WAV id '{bgmEvent.WavId}': {ex.Message}");
                }
            }
        }

        private void BindPreparedBGMSounds()
        {
            _bgmSounds.Clear();
            if (_preparedAudioSet == null || _parsedChart?.BGMEvents == null)
                return;

            foreach (var bgmEvent in _parsedChart.BGMEvents)
            {
                if (string.IsNullOrWhiteSpace(bgmEvent.AudioFilePath))
                    continue;

                var sourcePath = Path.GetFullPath(bgmEvent.AudioFilePath);
                if (_preparedAudioSet.ScheduledBgmBySourcePath.TryGetValue(
                    sourcePath,
                    out var sound))
                {
                    _bgmSounds[bgmEvent.WavId] = sound;
                }
            }
        }

        /// <summary>
        /// Processes BGM events that should be triggered at the current time
        /// </summary>
        /// <param name="currentTimeMs">Current song time in milliseconds</param>
        private void ProcessBGMEvents(double currentTimeMs)
        {
            var dueEvents = _scheduledBGMEvents
                .Where(bgm => currentTimeMs >= bgm.TimeMs)
                .OrderBy(bgm => bgm.TimeMs)
                .ToArray();

            // Trigger from earliest to latest. Iterating backward here reverses
            // simultaneous/overdue BGM events after a slow frame.
            foreach (var bgmEvent in dueEvents)
            {
                TriggerBGMEvent(bgmEvent);
                _scheduledBGMEvents.Remove(bgmEvent);
            }

            // Reap finished instances so their native audio resources are released
            // during gameplay rather than lingering until stage teardown. Mirrors
            // ChipSoundCache.CleanupStoppedInstances.
            CleanupStoppedBgmInstances();
        }

        /// <summary>
        /// Removes and disposes BGM instances that have finished playing, retaining
        /// active instances for the teardown stop-and-dispose pass.
        /// </summary>
        private void CleanupStoppedBgmInstances()
        {
            if (_activeBgmInstances == null || _activeBgmInstances.Count == 0)
                return;

            for (int i = _activeBgmInstances.Count - 1; i >= 0; i--)
            {
                var instance = _activeBgmInstances[i];
                if (instance.State == SoundState.Stopped)
                {
                    try { instance.Dispose(); }
                    catch { }
                    _activeBgmInstances.RemoveAt(i);
                }
            }
        }

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

        /// <summary>
        /// Triggers a BGM event by playing its associated sound
        /// </summary>
        /// <param name="bgmEvent">BGM event to trigger</param>
        private void TriggerBGMEvent(BGMEvent bgmEvent)
        {
            if (_bgmSounds.TryGetValue(bgmEvent.WavId, out var sound))
            {
                try
                {
                    // Honor the chart's per-WAV #VOLUME/#PAN for backing-track audio;
                    // defaults to full volume, centered when undefined.
                    // NOTE: ConfigData.MasterVolume is not yet combined here — this is
                    // the integration point once master-volume routing is wired into Lib/.
                    float volume = _parsedChart?.GetVolume(bgmEvent.WavId) ?? 1.0f;
                    float pan = _parsedChart?.GetPan(bgmEvent.WavId) ?? 0.0f;
                    var instance = sound.Play(
                        volume,
                        _preparedAudioSet?.RuntimePitch ?? 0.0f,
                        pan);
                    if (instance != null)
                        _activeBgmInstances.Add(instance);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[PerformanceStage] BGM playback failed for WAV id '{bgmEvent.WavId}': {ex.Message}");
                }
            }
        }

        #endregion

        #region Phase 3 - Gameplay Managers

        /// <summary>
        /// Initializes gameplay managers and wires up event handlers
        /// </summary>
        private void InitializeGameplayManagers()
        {
            if (_chartManager == null || _inputManager == null)
                return;

            // Initialize managers
            _judgementManager = new JudgementManager(_inputManager, _chartManager);
            // Start with judgement manager inactive - it will be activated when song starts
            _judgementManager.IsActive = false;
            // Lane-scoped input filtering: automated lanes ignore physical input
            // while manual lanes keep full player control (the seam copies the
            // frozen set, so it stays run-owned). Unlike DTXManiaNX, which lets
            // player and autoplay coexist, automated lanes drop player hits.
            _judgementManager.SetIgnoredPlayerInputLanes(FrozenAutoPlayLanes);

            _scoreManager = new ScoreManager(_chartManager.TotalNotes);
            _comboManager = new ComboManager();
            _gaugeManager = new GaugeManager(
                GaugeManager.StartingLife,
                _gaugeDamageLevel,
                _riskyLimit,
                _gaugeFailureEnabled);
            _skillManager = new SkillManager(_chartManager.TotalNotes, _comboManager);

            // Initialize UI state values
            _currentGaugeValue = PerformanceUILayout.GaugeSettings.StartingLife / 100.0f;
            _currentProgressValue = 0.0f;
            _isPaused = false;
            _isDanger = false;

            // Wire up event handlers for UI binding
            WireUpEventHandlers();

            // Subscribe to input events for immediate pad feedback
            WireUpInputEventHandlers();
        }

        /// <summary>
        /// Wires up event handlers between managers and UI components
        /// </summary>
        private void WireUpEventHandlers()
        {
            if (_judgementManager == null || _scoreManager == null || _comboManager == null || _gaugeManager == null)
                return;

            // Subscribe to judgement events and forward to all managers
            _judgementManager.JudgementMade += OnJudgementMade;

            // Subscribe to manager events for UI updates
            _scoreManager.ScoreChanged += OnScoreChanged;
            _comboManager.ComboChanged += OnComboChanged;
            _gaugeManager.GaugeChanged += OnGaugeChanged;
            _gaugeManager.Failed += OnPlayerFailed;
            _skillManager.SkillChanged += OnSkillChanged;
        }

        /// <summary>
        /// Wires up input event handlers for immediate pad feedback
        /// </summary>
        private void WireUpInputEventHandlers()
        {
            if (_inputManager?.ModularInputManager != null)
            {
                // Subscribe to lane hit events for immediate visual feedback (before judgement)
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
            // hit animation is driven from ProcessAutoPlay.
            if (FrozenAutoPlayLanes.Contains(e.Lane)) return;

            // Trigger immediate pad press effect on input (regardless of judgement). Visual pad
            // feedback is intentionally allowed during loading/countdown so the player sees their
            // input registered, even before the song clock starts.
            _padRenderer?.TriggerPadPress(e.Lane, false); // false = key-down, not judged hit

            // Only record gameplay telemetry and chip sounds while the chart is actively playing.
            // During loading, the ready countdown, or after stop, the song clock is not advancing,
            // so telemetry must not capture these non-gameplay hits.
            if (_songTimer?.IsPlaying != true)
                return;

            // v1 intentionally frame-samples the logical clock when the queued input
            // is handled. Do not combine LaneHitEventArgs.Timestamp (DateTime-based)
            // with GameTime; that would create a mixed-clock judgement path.
            var logicalSongTimeMs = _songTimer.GetCurrentMs(_currentGameTime);

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
            var nearest = FindNearestNoteForChip(e.Lane, chipLookupTimeMs);
            if (nearest != null)
                PlayChipForNote(nearest);
        }

        /// <summary>
        /// Handles judgement events and forwards them to all managers
        /// </summary>
        private void OnJudgementMade(object? sender, DTXMania.Game.Lib.Song.Entities.JudgementEvent e)
        {
            // Forward judgement to all managers. Gauge follows the frozen
            // AutoAddGauge policy only for automated-lane judgements;
            // manual-lane judgements always reach the gauge.
            _scoreManager?.ProcessJudgement(e);
            _comboManager?.ProcessJudgement(e);
            if (!FrozenAutoPlayLanes.Contains(e.Lane) || _autoAddGaugeEnabled)
                _gaugeManager?.ProcessJudgement(e);
            _skillManager?.ProcessJudgement(e);
            _skillPanelDisplay?.ProcessJudgement(e, _comboManager?.MaxCombo ?? 0);

            // Spawn hit effect for successful hits (non-Miss)
            if (e.IsHit())
            {
                _nxAttackEffectManager?.Spawn(e.Lane, e.Type);

                // Trigger pad press effect
                _padRenderer?.TriggerPadPress(e.Lane, true);

                if (_visualGates.EnableLaneFlush)
                    _noteRenderer?.TriggerLaneFlash(e.Lane);
            }

            // Spawn judgement text popup for all judgements
            _spriteJudgementTextPopupManager?.SpawnPopup(e);

            if (_visualGates.ShowHitTimingFeedback
                && e.IsHit()
                && !FrozenAutoPlayLanes.Contains(e.Lane))
            {
                _hitTimingFeedbackDisplay?.Spawn(e);
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
        /// Handles skill changes and updates both display components.
        /// </summary>
        private void OnSkillChanged(object? sender, SkillChangedEventArgs e)
        {
            if (_skillPanelDisplay != null)
            {
                _skillPanelDisplay.Skill = e.CurrentSkill;
                _skillPanelDisplay.ShowMax = e.IsMax;
            }
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
            if (!_stageCompleted)
            {
                FinalizePerformance(CompletionReason.PlayerFailed);
            }
        }

        /// <summary>
        /// Updates gameplay managers during active gameplay
        /// </summary>
        private void UpdateGameplayManagers(
            double logicalSongTimeMs,
            double pendingHitTimeMs)
        {
            // Process autoplay when any lane is automated
            if (FrozenAutoPlayLanes.Count > 0)
            {
                ProcessAutoPlay(logicalSongTimeMs);
            }

            _metronomePlayer?.Update(logicalSongTimeMs);

            // Pending hits and timeout misses both use the latency-compensated
            // logical time. Miss scanning must share the compensated clock so a
            // note is not marked missed before the player hears it: using raw
            // time would shrink the reaction window by AudioLatencyOffsetMs
            // (default 200 ms equals the hit window, leaving zero reaction time).
            _judgementManager?.Update(pendingHitTimeMs, pendingHitTimeMs);
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
        /// Processes autoplay functionality by automatically hitting notes at perfect timing
        /// </summary>
        private void ProcessAutoPlay(double currentSongTimeMs)
        {
            if (_chartManager == null || _judgementManager == null)
                return;

            var allNotes = _chartManager.AllNotes;

            // Auto-hits notes at their exact time. Unlike player input, autoplay
            // should NEVER skip a pending note. The window only prevents triggering
            // notes that are in the future — any past-due note is auto-hit regardless
            // of how late the frame arrived (GC pause, hitch, low FPS, etc.).
            // One cursor serves all lanes: due notes on manual lanes are skipped
            // (left for normal judgement/miss handling) but still advance the
            // cursor so a manual note can never stall automated ones behind it.
            while (_autoPlayNoteIndex < allNotes.Count)
            {
                var note = allNotes[_autoPlayNoteIndex];
                
                // Check timing difference
                var timeDifference = currentSongTimeMs - note.TimeMs;
                
                if (timeDifference < 0)
                {
                    // This note is in the future, stop processing (do not increment index)
                    break;
                }

                // Note is at or past its time — always advance the cursor, but
                // only resolve notes on automated lanes.
                if (FrozenAutoPlayLanes.Contains(note.LaneIndex))
                {
                    var noteData = _judgementManager.GetNoteRuntimeData(note.Id);
                    if (noteData?.Status == DTXMania.Game.Lib.Stage.Performance.NoteStatus.Pending)
                    {
                        // Resolve directly by note ID, bypassing hit detection window.
                        // This ensures deterministic hits even when currentSongTimeMs is
                        // far past the note time (e.g., after GC pause or frame hitch).
                        _judgementManager.ResolveAutoHit(note.Id);

                        // Trigger pad press effect for autoplay
                        _padRenderer?.TriggerPadPress(note.LaneIndex, true);

                        // Play the per-note drum chip sound (silent if no cache or no WAV)
                        PlayChipForNote(note);
                    }
                }

                _autoPlayNoteIndex++;
            }
        }

        /// <summary>
        /// Plays the drum chip sound associated with a note's WAV id, if loaded.
        /// No-op when cache is unavailable or note has no associated WAV.
        /// </summary>
        private void PlayChipForNote(Note note)
        {
            if (note == null || string.IsNullOrEmpty(note.Value))
                return;

            // Honor the chart's per-WAV #VOLUME/#PAN; defaults to full volume, centered.
            // NOTE: ConfigData.MasterVolume is not yet combined here — this is the
            // integration point once master-volume routing is wired into Lib/.
            float volume = _parsedChart?.GetVolume(note.Value) ?? 1.0f;
            float pan = _parsedChart?.GetPan(note.Value) ?? 0.0f;
            _chipSoundCache?.Play(
                note.Value,
                volume,
                _preparedAudioSet?.RuntimePitch ?? 0.0f,
                pan);
        }

        /// <summary>
        /// Finds the nearest unhit note in a lane within the hit detection window.
        /// Used by player-input chip playback to mirror what JudgementManager would
        /// resolve. Returns null if no candidate exists.
        /// </summary>
        private Note? FindNearestNoteForChip(int laneIndex, double currentSongTimeMs)
        {
            if (_chartManager == null || _judgementManager == null)
                return null;

            // Use the Poor window (150ms) so chip sounds only play for non-Miss judgements.
            // The full HitDetectionWindowMs (200ms) includes Miss-range hits (151-200ms)
            // where the player hears a chip sound but receives a Miss judgement.
            double windowMs = TimingConstants.PoorWindowMs;

            var allNotes = _chartManager.AllNotes;
            int startIndex = _chartManager.BinarySearchStartIndex(currentSongTimeMs - windowMs);

            Note? nearest = null;
            double nearestDistance = double.MaxValue;

            for (int i = startIndex; i < allNotes.Count; i++)
            {
                var note = allNotes[i];
                if (note.TimeMs - currentSongTimeMs > windowMs)
                    break;
                if (note.LaneIndex != laneIndex)
                    continue;

                var data = _judgementManager.GetNoteRuntimeData(note.Id);
                if (data == null || data.Status != NoteStatus.Pending)
                    continue;

                var distance = Math.Abs(currentSongTimeMs - note.TimeMs);
                if (distance > windowMs)
                    continue;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = note;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Cleans up gameplay managers
        /// </summary>
        private void CleanupGameplayManagers()
        {
            // Unsubscribe from events
            if (_judgementManager != null)
            {
                _judgementManager.JudgementMade -= OnJudgementMade;
            }

            if (_scoreManager != null)
            {
                _scoreManager.ScoreChanged -= OnScoreChanged;
            }

            if (_comboManager != null)
            {
                _comboManager.ComboChanged -= OnComboChanged;
            }

            if (_gaugeManager != null)
            {
                _gaugeManager.GaugeChanged -= OnGaugeChanged;
                _gaugeManager.Failed -= OnPlayerFailed;
            }

            if (_skillManager != null)
            {
                _skillManager.SkillChanged -= OnSkillChanged;
            }

            // Unsubscribe from input events
            if (_inputManager?.ModularInputManager != null)
            {
                _inputManager.ModularInputManager.OnLaneHit -= OnLaneHitForPadFeedback;
            }

            // Dispose managers
            _judgementManager?.Dispose();
            _judgementManager = null;
            _scoreManager?.Dispose();
            _scoreManager = null;
            _comboManager?.Dispose();
            _comboManager = null;
            _gaugeManager?.Dispose();
            _gaugeManager = null;
            _skillManager?.Dispose();
            _skillManager = null;

            _chipSoundCache?.Dispose();
            _chipSoundCache = null!;
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
            // Draw shutters first (overlay elements)
            DrawShutters();
            
            // Draw skill panel
            DrawSkillPanel();
            
            // Draw gauge elements
            DrawGaugeElements();
            
            // Draw progress bar
            DrawProgressBar();
            
            // Draw existing UI components as fallback
            // Legacy gauge display removed - using asset-based gauge only
            
            _scoreDisplay?.Draw(_spriteBatch);
            if (!_visualGates.HideCombo)
                _comboDisplay?.Draw(_spriteBatch);
            _skillPanelDisplay?.Draw(_spriteBatch);
            _skillMeterDisplay?.Draw(_spriteBatch);
            
            _uiManager?.Draw(_spriteBatch, 0);
            
            // Draw overlays last (topmost)
            DrawOverlays();
        }
        
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
        /// Checks for stage completion based on fail or song end conditions
        /// </summary>
        private void CheckStageCompletion(double currentTimeMs)
        {
            if (_stageCompleted || _parsedChart == null)
                return;

            if (currentTimeMs < _parsedChart.DurationMs)
            {
                // Seeking or resetting logical time below chart end restarts the
                // real-time result delay on the next end crossing.
                _chartEndReachedRealTimeSeconds = null;
            }
            else
            {
                _chartEndReachedRealTimeSeconds ??= _totalTime;
                var realBufferElapsedSeconds =
                    _totalTime - _chartEndReachedRealTimeSeconds.Value;
                if (realBufferElapsedSeconds >=
                    GameConstants.Performance.SongEndBufferSeconds)
                {
                    FinalizePerformance(CompletionReason.SongComplete);
                    return;
                }
            }

            if (_gaugeManager?.HasFailed == true)
            {
                FinalizePerformance(CompletionReason.PlayerFailed);
            }
        }

        /// <summary>
        /// Finalizes the performance, pauses input, stops the song timer, and prepares the performance summary
        /// </summary>
        private void FinalizePerformance(CompletionReason reason)
        {
            if (_stageCompleted)
                return;

            // Mark the stage as completed
            _stageCompleted = true;

            // Pause input handling and deactivate judgement manager
            _inputPaused = true;
            if (_judgementManager != null)
            {
                _judgementManager.IsActive = false;
            }

            // Stop the song timer
            _songTimer?.Stop();

            // Stop audible gameplay audio instances (scheduled BGM tracks and
            // chip sounds) immediately. The transition to ResultStage is
            // instant, but scheduled BGM tracks are SoundEffectInstances tracked
            // independently from the song timer; without this they would keep
            // playing after gameplay has logically ended and bleed into the
            // result stage. Full disposal remains in CleanupComponents.
            StopGameplayAudioInstances();

            // Build the performance summary
            var summaryChart = _selectedSong?.GetCurrentDifficultyChart(_selectedDifficulty);
            int summaryLevel = summaryChart?.DrumLevel ?? 0;
            int summaryLevelDec = summaryChart?.DrumLevelDec ?? 0;
            double summaryPlayingSkill = _skillManager?.CurrentSkill ?? 0.0;
            double summaryGameSkill = SongScore.CalculateGameSkill(
                summaryPlayingSkill, summaryLevel, summaryLevelDec);

            _performanceSummary = new PerformanceSummary
            {
                RunId = Guid.NewGuid(),
                UsedAutoPlay = FrozenAutoPlayLanes.Count > 0,
                PlaySpeedPercent = _playbackModifiers.PlaySpeedPercent,
                PitchSemitones = _playbackModifiers.PitchSemitones,
                Score = _scoreManager?.CurrentScore ?? 0,
                MaxCombo = _comboManager?.MaxCombo ?? 0,
                ClearFlag = reason == CompletionReason.SongComplete,
                PerfectCount = _judgementManager?.GetJudgementCount(JudgementType.Perfect) ?? 0,
                GreatCount = _judgementManager?.GetJudgementCount(JudgementType.Great) ?? 0,
                GoodCount = _judgementManager?.GetJudgementCount(JudgementType.Good) ?? 0,
                PoorCount = _judgementManager?.GetJudgementCount(JudgementType.Poor) ?? 0,
                MissCount = _judgementManager?.GetJudgementCount(JudgementType.Miss) ?? 0,
                TotalNotes = _chartManager?.TotalNotes ?? 0,
                FinalLife = _gaugeManager?.CurrentLife ?? 0.0f,
                CompletionReason = reason,
                PlayingSkill = summaryPlayingSkill,
                GameSkill = summaryGameSkill,
                ChartLevel = summaryLevel,
                ChartLevelDec = summaryLevelDec
            };


            // Pass the summary to ResultStage
            TransitionToResultStage();
        }

        /// <summary>
        /// Handles the transition to the ResultStage
        /// </summary>
        private void TransitionToResultStage()
        {
            // No debounce needed here since this is an automatic transition based on game completion
            var sharedData = new Dictionary<string, object>
            {
                { "performanceSummary", _performanceSummary }
            };

            if (_selectedSong != null)
            {
                sharedData["selectedSong"] = _selectedSong;
            }
            sharedData["selectedDifficulty"] = _selectedDifficulty;

            StageManager?.ChangeStage(StageType.Result, new InstantTransition(), sharedData);
        }

        #endregion


        #region Telemetry

        public void PopulateTelemetry(GameTelemetrySnapshot telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);

            telemetry.SelectedSongTitle = _selectedSong?.DisplayTitle ?? _selectedSong?.Title;
            telemetry.SelectedDifficulty = _selectedDifficulty;
            // Snapshot _songTimer and _currentGameTime into locals before checking/calling.
            // PopulateTelemetry runs on the Kestrel API thread while the game thread can null
            // these fields during stage cleanup (line 448). Reading the field multiple times
            // (null check, then IsPlaying, then GetCurrentMs) opens a window where the game
            // thread nulls it between reads and throws NullReferenceException. A single local
            // read collapses that window. Matches the _lastLaneHit snapshot pattern below.
            var songTimer = _songTimer;
            var currentGameTime = _currentGameTime;
            var songTimerPlaying = songTimer != null && songTimer.IsPlaying;
            var songTimerPaused = songTimer != null && songTimer.IsPaused;
            var songTimerHasReadablePosition =
                songTimerPlaying || songTimerPaused;
            telemetry.PerformanceReady = !_isLoading
                && _chartManager != null
                && songTimer != null
                && !_stageCompleted
                && (_isReady || songTimerHasReadablePosition);
            telemetry.PlaySpeedPercent = _playbackModifiers.PlaySpeedPercent;
            telemetry.PitchSemitones = _playbackModifiers.PitchSemitones;
            telemetry.PlaybackProfileFrozen = true;
            var audioPreparationProgress = _audioPreparationProgress;
            var preparedAudioSet = _preparedAudioSet;
            telemetry.AudioPreparationCompleted =
                audioPreparationProgress?.CompletedCount ?? 0;
            telemetry.AudioPreparationTotal =
                audioPreparationProgress?.TotalCount ?? 0;
            telemetry.AudioPreparationCacheHits =
                audioPreparationProgress?.CacheHitCount ?? 0;
            telemetry.PreparedAudioBytes =
                preparedAudioSet?.DecodedPcmBytes
                ?? audioPreparationProgress?.DecodedByteEstimate
                ?? 0L;
            // Full autoplay only: partial automation reports as manual play.
            telemetry.AutoPlayEnabled = FrozenAutoPlayLanes.Count == PerformanceUILayout.LaneCount;
            telemetry.StageCompleted = _stageCompleted;
            telemetry.CurrentSongTimeMs =
                songTimerHasReadablePosition && currentGameTime != null
                ? songTimer!.GetCurrentMs(currentGameTime)
                : 0.0;
            telemetry.Score = _scoreManager?.CurrentScore ?? 0;
            telemetry.CurrentCombo = _comboManager?.CurrentCombo ?? 0;
            telemetry.MaxCombo = _comboManager?.MaxCombo ?? 0;
            telemetry.Gauge = _gaugeManager?.CurrentLife ?? 0.0f;
            telemetry.HasFailed = _gaugeManager?.HasFailed ?? false;
            telemetry.TotalNotes = _chartManager?.TotalNotes ?? 0;
            telemetry.PerfectCount = _judgementManager?.GetJudgementCount(JudgementType.Perfect) ?? 0;
            telemetry.GreatCount = _judgementManager?.GetJudgementCount(JudgementType.Great) ?? 0;
            telemetry.GoodCount = _judgementManager?.GetJudgementCount(JudgementType.Good) ?? 0;
            telemetry.PoorCount = _judgementManager?.GetJudgementCount(JudgementType.Poor) ?? 0;
            telemetry.MissCount = _judgementManager?.GetJudgementCount(JudgementType.Miss) ?? 0;
            // Snapshot the reference once (atomic read) then read its immutable fields, so
            // the API thread can never observe a torn combination across two different hits.
            var lastHit = _lastLaneHit;
            telemetry.LastLaneHitLane = lastHit?.Lane;
            telemetry.LastLaneHitButtonId = lastHit?.ButtonId;
            telemetry.LastLaneHitSongTimeMs = lastHit?.SongTimeMs;
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
            // Use Console.WriteLine to ensure logs are visible in all build configurations
            // (Debug.WriteLine is compiled out in Release builds)
            if (exception != null)
            {
                Console.WriteLine($"[PerformanceError] {message}: {exception.Message}");
                Console.WriteLine($"[PerformanceError] Stack trace: {exception.StackTrace}");
            }
            else
            {
                Console.WriteLine($"[PerformanceError] {message}");
            }
        }

        /// <summary>
        /// Reports preparation progress synchronously so a cancelled activation cannot
        /// leave queued callbacks that overwrite the next activation's loading state.
        /// </summary>
        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;

            public InlineProgress(Action<T> callback)
            {
                _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            }

            public void Report(T value) => _callback(value);
        }

        private object AudioLifecycleGate =>
            _audioLifecycleGate ??= new object();

        #endregion
    }
}
