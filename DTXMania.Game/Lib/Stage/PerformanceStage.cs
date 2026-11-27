#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
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
        private IPerformancePlayfield? _playfield;

        // Stage data - initialized via ExtractSharedData
        private SongListNode _selectedSong = null!;
        private int _selectedDifficulty;

        // Performance components - initialized in InitializeComponents
        private BackgroundRenderer _backgroundRenderer = null!;

        // Phase 2 components - Chart loading and note scrolling
        private ParsedChart _parsedChart = null!;

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
        private ITexture _dangerOverlayTexture = null!;
        
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
        
        // UI state tracking
        private bool _isDanger = false;
        private float _currentGaugeValue = 0.5f; // 0.0 to 1.0
        private float _currentProgressValue = 0.0f; // 0.0 to 1.0

        private bool _inputPaused = false;
        
        private static readonly GameplayRules EmptyRules = new(null);
        internal GameplayRules? Rules { get; private set; }
        private IReadOnlySet<int> FrozenAutoPlayLanes =>
            Rules?.AutoPlayLanes ?? FrozenSet<int>.Empty;
        private PerformanceVisualGates _visualGates;

        private readonly record struct PerformanceVisualGates(
            bool HideCombo,
            bool ShowHitTimingFeedback,
            DrumsNoteDisplayStyle RequestedNoteDisplayStyle);

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
                _spriteBatch.GraphicsDevice, message => LogPerformanceError(message));

        protected virtual IChartVideoPlayer? CreateChartVideoPlayerIfAvailable() =>
            _spriteBatch == null ? null : CreateChartVideoPlayer();

        /// <summary>
        /// Creates the chart video player and resets scheduling state. Extracted so
        /// the GPU-dependent creation can be excluded from headless coverage.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private bool InitializeChartVideoPlayer(StageBackgroundWorkScope? scope)
        {
            var player = CreateChartVideoPlayerIfAvailable();
            if (!ReferenceEquals(scope, BackgroundWork))
            {
                // A synchronous factory callback may already have started a different run.
                player?.Dispose();
                return false;
            }

            _chartVideoPlayer = player;
            _nextVideoEventIndex = 0;
            _activeVideoEvent = null;
            return true;
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

        protected virtual IPerformancePlayfield? CreateLanesPlayfield(GraphicsDevice? device, Texture2D? whitePixel)
        {
            if (device == null) return null;
            var labels = _selectedSong?.DifficultyLabels;
            var label = labels != null && _selectedDifficulty >= 0 && _selectedDifficulty < labels.Length
                ? labels[_selectedDifficulty] : null;
            return new LanesPlayfield(device, _resourceManager, whitePixel,
                _selectedSong?.GetCurrentDifficultyChart(_selectedDifficulty), label, _game.ConfigManager.Config);
        }

        protected virtual IPerformancePlayfield? CreateTabPlayfield(Func<IPerformancePlayfield?> create) =>
            _spriteBatch != null ? create() : null;

        internal virtual TabHudDisplay? CreateTabHud(GraphicsDevice? device, SongChart? chart,
            string? title, string? artist, Texture2D? whitePixel, SkinPalette palette, TabModeArt art)
        {
            if (device == null) return null;
            return new TabHudDisplay(device, chart, title, artist, whitePixel, palette, art,
                _game.LoggerFactory?.CreateLogger<TabHudDisplay>() ?? NullLogger<TabHudDisplay>.Instance);
        }

        protected virtual ITexture? CreateFallbackWhiteTexture() =>
            _spriteBatch != null ? _resourceManager.CreateTextureFromColor(Color.White) : null;

        #endregion

        #region BaseStage Implementation

        protected override void OnActivate()
        {
            var scope = BackgroundWork;
            var config = _game.ConfigManager.Config;
            if (!ReferenceEquals(scope, BackgroundWork)) return;
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
            FreezeRunConfiguration(config);
            PublishTelemetry();
            if (!ReferenceEquals(scope, BackgroundWork)) return;

            // Initialize UI components
            if (!InitializeComponents(scope))
                return;

            // Start chart/audio preparation on the activation's background-work scope;
            // every activation input is snapshotted on this (update) thread before Run.
            _ = StartGameplayInitialization();
            if (!ReferenceEquals(scope, BackgroundWork))
                return;

            var configManager = _game?.ConfigManager;
            if (!ReferenceEquals(scope, BackgroundWork)) return;
            if (configManager != null)
            {
                _subscribedConfigManager = configManager;
                configManager.ScrollSpeedChanged += OnScrollSpeedChanged;
            }
        }

        protected override void OnDeactivate()
        {
            var scope = BackgroundWork;
            // BaseStage.Deactivate already retired the activation's background-work
            // scope synchronously before this override, so no result can reach the
            // components during teardown.
            var configManager = _subscribedConfigManager;
            _subscribedConfigManager = null;
            Exception? failure = null;
            if (configManager != null)
            {
                try { configManager.ScrollSpeedChanged -= OnScrollSpeedChanged; }
                catch (Exception ex) { failure = ex; }
                if (ReferenceEquals(scope, BackgroundWork))
                {
                    try { configManager.FlushPendingSave(); }
                    catch (Exception ex) { failure ??= ex; }
                }
            }
            if (ReferenceEquals(scope, BackgroundWork))
            {
                try { CleanupComponents(); }
                catch (Exception ex) { failure ??= ex; }
            }
            try
            {
                if (ReferenceEquals(scope, BackgroundWork))
                    PublishTelemetry();
            }
            catch (Exception ex) { failure ??= ex; }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
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
            var viewport = _spriteBatch.GraphicsDevice.Viewport;
            DrawBasePass(new Rectangle(0, 0, viewport.Width, viewport.Height));

            _spriteBatch.End();

            // Overlay pass: Note overlays with alpha blending (above notes, below UI)
            _spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend);

            DrawPlayfieldOverlay(_spriteBatch);

            _spriteBatch.End();

            // UI pass: UI elements on top of everything else
            _spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend);

            // Draw UI elements (gauge, score, combo)
            DrawUIElements();

            // Draw scroll-speed indicator toast (on top of everything via depth 0.0)
            _scrollSpeedIndicator?.Draw(_spriteBatch);

            _spriteBatch.End();

            // Effects pass: Hit effects with additive blending (drawn on top of everything)
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);

            DrawPlayfieldAdditive(_spriteBatch);

            _spriteBatch.End();
        }

        internal void DrawPlayfieldBase(SpriteBatch batch)
        {
            _playfield?.DrawBase(batch, Rules?.ChartManager,
                _audioSession?.IsPlaying == true || _audioSession?.IsPaused == true,
                CurrentVisualSongTimeMs);
        }

        internal void DrawBasePass(Rectangle viewport)
        {
            DrawBackground(viewport);
            DrawPlayfieldBase(_spriteBatch);
        }

        internal void DrawPlayfieldOverlay(SpriteBatch batch)
        {
            _playfield?.DrawOverlay(batch, Rules?.ChartManager,
                _audioSession?.IsPlaying == true || _audioSession?.IsPaused == true,
                CurrentVisualSongTimeMs);
        }

        internal void DrawPlayfieldAdditive(SpriteBatch batch)
        {
            _playfield?.DrawAdditive(batch);
        }

        #endregion

        #region Initialization and Cleanup

        private double CurrentVisualSongTimeMs =>
            _audioSession != null && _currentGameTime != null
                ? _audioSession.GetCurrentMs(_currentGameTime) : 0.0;

        private void ExtractSharedData()
        {
            if (_transitionPayload is SongPlayRequest request)
            {
                _selectedSong = request.Song;
                _selectedDifficulty = request.Difficulty;
                _parsedChart = request.Chart;
            }
        }

        private void FreezeRunConfiguration(ConfigData? config)
        {
            var noteDisplayStyle = config?.NoteDisplayStyle ?? DrumsNoteDisplayStyle.Lanes;
            _visualGates = new PerformanceVisualGates(
                HideCombo: !(config?.ShowCombo ?? true),
                ShowHitTimingFeedback: config?.ShowHitTimingFeedback ?? false,
                RequestedNoteDisplayStyle: noteDisplayStyle);

            Rules = new GameplayRules(config);
        }

        private bool InitializeComponents(StageBackgroundWorkScope? scope)
        {
            ITexture? pixel = null;
            BackgroundRenderer? background = null;
            IPerformancePlayfield? candidate = null;
            bool pixelTransferred = false, leased = false, ready = false;
            Exception? firstFailure = null;
            void Release(Action action)
            {
                try { action(); }
                catch (Exception ex) { firstFailure ??= ex; }
            }

            bool InitializeLanes()
            {
                pixel = CreateFallbackWhiteTexture();
                if (!ReferenceEquals(scope, BackgroundWork)) return false;
                var whitePixel = pixel?.Texture;
                if (!ReferenceEquals(scope, BackgroundWork)) return false;

                // The factory may retire the stage while its candidate still borrows the pixel.
                pixel?.AddReference();
                leased = pixel != null;
                if (!ReferenceEquals(scope, BackgroundWork)) return false;
                _fallbackWhiteTextureResource = pixel;
                _fallbackWhiteTexture = whitePixel!;
                pixelTransferred = true;
                if (_spriteBatch != null)
                {
                    background = new BackgroundRenderer(_resourceManager);
                    if (!ReferenceEquals(scope, BackgroundWork)) return false;
                    var installed = background;
                    _backgroundRenderer = installed;
                    background = null;
                    _ = installed.LoadBackgroundAsync();
                    if (!ReferenceEquals(scope, BackgroundWork)) return false;
                }
                candidate = CreateLanesPlayfield(_spriteBatch?.GraphicsDevice, whitePixel);
                if (!ReferenceEquals(scope, BackgroundWork)) return false;
                _playfield = candidate;
                candidate = null;
                return true;
            }

            try
            {
                ready = InitializeLanes();
            }
            catch (Exception ex)
            {
                firstFailure = ex;
            }
            if (!ready)
            {
                BackgroundRenderer? installedBackground = null;
                ITexture? installedPixel = null;
                if (ReferenceEquals(scope, BackgroundWork))
                {
                    installedBackground = _backgroundRenderer;
                    installedPixel = _fallbackWhiteTextureResource;
                    _backgroundRenderer = null;
                    _fallbackWhiteTextureResource = null;
                    _fallbackWhiteTexture = null!;
                }
                Release(() => candidate?.Dispose());
                Release(() => background?.Dispose());
                Release(() => installedBackground?.Dispose());
                Release(() => installedPixel?.RemoveReference());
                if (!pixelTransferred) Release(() => pixel?.RemoveReference());
            }
            if (leased) Release(() => pixel!.RemoveReference());
            if (firstFailure != null) ExceptionDispatchInfo.Capture(firstFailure).Throw();
            if (!ready || !ReferenceEquals(scope, BackgroundWork)) return false;

            // The default factory guards the GPU-dependent player when headless.
            if (!InitializeChartVideoPlayer(scope))
                return false;
            if (_spriteBatch == null)
            {
                WireUpInputEventHandlers();
                return true;
            }

            // Initialize UX components
            InitializeReadyFont();
            if (!ReferenceEquals(scope, BackgroundWork)) return false;
            
            // Load performance UI assets using DTXManiaNX layout
            LoadPerformanceUIAssets();
            if (!ReferenceEquals(scope, BackgroundWork)) return false;

            // HPA-796: the fallback white pixel was already acquired at the top of this
            // method (before the renderers that borrow it); no private 1×1 texture here.

            // Wire pad feedback now, before preparation starts: OnLaneHitForPadFeedback
            // explicitly supports loading/countdown, so manual hits light pads while the
            // chart/audio prepare (and after a failed preparation). CleanupGameplayManagers
            // unsubscribes on teardown, keeping the lifecycle balanced.
            WireUpInputEventHandlers();
            return true;
        }

        private void CleanupComponents()
        {
            var scope = BackgroundWork;
            Exception? firstFailure = null;
            void Release(Action action)
            {
                try { action(); }
                catch (Exception ex) { firstFailure ??= ex; }
            }

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

            // Detach the retiring run before callbacks can synchronously activate another.
            var background = _backgroundRenderer;
            var playfield = _playfield;
            var readyFont = _readyFont;
            var scrollSpeedFont = _scrollSpeedFont;
            var backgroundTexture = _backgroundTexture;
            var shutterTexture = _shutterTexture;
            var dangerTexture = _dangerOverlayTexture;
            var pixel = _fallbackWhiteTextureResource;
            var rules = Rules;
            var audioSession = _audioSession;
            var videoPlayer = _chartVideoPlayer;
            _backgroundRenderer = null;
            _playfield = null;
            _readyFont = null!;
            _scrollSpeedFont = null;
            _scrollSpeedIndicator = null;
            _backgroundTexture = null!;
            _shutterTexture = null!;
            _dangerOverlayTexture = null!;
            _fallbackWhiteTextureResource = null;
            _fallbackWhiteTexture = null!;
            Rules = null;
            _audioSession = null;
            _parsedChart = null!;
            _chartVideoPlayer = null!;
            _activeVideoEvent = null;
            _nextVideoEventIndex = 0;

            // Remove the shared input subscription before any callback can install a fresh one.
            if (_inputManager?.ModularInputManager != null)
                _inputManager.ModularInputManager.OnLaneHit -= OnLaneHitForPadFeedback;
            if (rules?.JudgementManager is { } judgementManager)
            {
                judgementManager.JudgementMade -= OnJudgementMade;
                judgementManager.PlayerHitResolved -= OnPlayerHitResolved;
            }
            if (rules?.GaugeManager is { } gaugeManager)
            {
                gaugeManager.GaugeChanged -= OnGaugeChanged;
                gaugeManager.Failed -= OnPlayerFailed;
            }

            Release(() => background?.Dispose());
            Release(() => playfield?.Dispose());
            Release(() => readyFont?.RemoveReference());
            Release(() => scrollSpeedFont?.RemoveReference());
            Release(() => backgroundTexture?.RemoveReference());
            Release(() => shutterTexture?.RemoveReference());
            Release(() => dangerTexture?.RemoveReference());
            Release(() => pixel?.RemoveReference());
            Release(() => CleanupGameplayManagers(rules));
            Release(() => audioSession?.Dispose());
            rules?.ClearChart();
            Release(() => videoPlayer?.Dispose());

            if (firstFailure != null) ExceptionDispatchInfo.Capture(firstFailure).Throw();
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
            var scope = BackgroundWork;
            _isLoading = false;
            _isReady = false;
            _loadErrorMessage = message;
            PublishTelemetry();
            if (ReferenceEquals(scope, BackgroundWork))
                LogPerformanceError(message, exception);
        }

        private void OnScrollSpeedChanged(object? sender, ScrollSpeedChangedEventArgs e)
        {
            _playfield?.OnScrollSpeedChanged(e.NewPercent);
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
        internal virtual Task<PreparedGameplayAudioSet?> StartGameplayInitialization()
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
                    var failure = ExceptionDispatchInfo.Capture(exception);
                    audioVariantProcessorFactory = () => { failure.Throw(); return null!; };
                    audioVariantCacheFactory = () => { failure.Throw(); return null!; };
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
            var scope = BackgroundWork;
            if (scope == null) return;
            var pixelResource = _fallbackWhiteTextureResource;
            var leased = false;
            try
            {
                var lanes = _playfield;
                Func<IPerformancePlayfield?>? createTab = null;
                if (notationChart != null)
                {
                    var whitePixel = _fallbackWhiteTexture;
                    var automatedLanes = FrozenAutoPlayLanes;
                    var hideCombo = _visualGates.HideCombo;
                    var device = _spriteBatch?.GraphicsDevice;
                    var song = _selectedSong;
                    var difficulty = _selectedDifficulty;
                    var title = song?.DisplayTitle ?? song?.Title;
                    var artist = song?.DatabaseSong?.DisplayArtist;
                    var resources = _resourceManager;
                    var measureLines = chart.MeasureLines;
                    createTab = () => ReferenceEquals(scope, BackgroundWork)
                        ? TabPlayfield.Create(resources, notationChart, measureLines, whitePixel, automatedLanes, hideCombo,
                            (palette, art) => ReferenceEquals(scope, BackgroundWork)
                                ? CreateTabHud(device, song?.GetCurrentDifficultyChart(difficulty),
                                    title, artist, whitePixel, palette, art)
                                : null)
                        : null;
                }

                // Teardown can run inside any visual callback while its borrowers are still on the stack.
                pixelResource?.AddReference();
                leased = pixelResource != null;
                if (!ReferenceEquals(scope, BackgroundWork)) return;
                _parsedChart = chart;
                Rules!.BindChart(chart);

                // Initialize gameplay managers
                InitializeGameplayManagers();
                if (!ReferenceEquals(scope, BackgroundWork)) return;

                // Set BPM and scroll speed in note renderer. The scroll speed is read
                // at apply time, not snapshotted before Run: PageUp/PageDown stay
                // active during loading, and the stale snapshot would overwrite the
                // user's change and diverge from the live config and the toast.
                var scrollSpeed = _game?.ConfigManager?.Config?.ScrollSpeed ?? ScrollSpeedRange.Default;
                if (!ReferenceEquals(scope, BackgroundWork)) return;
                lanes?.OnChartPublished(chart.Bpm, scrollSpeed);
                if (!ReferenceEquals(scope, BackgroundWork)) return;

                // HPA-411: one-shot Tab renderer construction on the update thread
                // because it owns the Bravura and label font loads. A failure means
                // Lanes for the rest of the song.
                if (createTab != null)
                    TrySelectTabPlayfield(scope, lanes, createTab, chart.Bpm, scrollSpeed);
            }
            catch (Exception ex)
            {
                if (!ReferenceEquals(scope, BackgroundWork)) return;
                FailLoading($"Chart setup failed: {ex.Message}", ex);
            }
            finally
            {
                if (leased)
                {
                    try { pixelResource!.RemoveReference(); }
                    catch (Exception ex)
                    {
                        if (ReferenceEquals(scope, BackgroundWork))
                            LogPerformanceError("PerformanceStage: Chart publication pixel release failed.", ex);
                    }
                }
            }
            if (ReferenceEquals(scope, BackgroundWork))
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
        /// Select Tab only after its complete visual candidate has been prepared.
        /// </summary>
        private void TrySelectTabPlayfield(
            StageBackgroundWorkScope scope, IPerformancePlayfield? lanes,
            Func<IPerformancePlayfield?> create, double bpm, int scrollSpeed)
        {
            IPerformancePlayfield? candidate = null;
            try
            {
                candidate = CreateTabPlayfield(create);
                if (!ReferenceEquals(scope, BackgroundWork))
                {
                    var stale = candidate;
                    candidate = null;
                    stale?.Dispose();
                    return;
                }
                if (candidate == null)
                {
                    LogPerformanceError("PerformanceStage: Tab display unavailable; staying in Lanes for this song.");
                    return;
                }
                candidate.OnChartPublished(bpm, scrollSpeed);
                if (!ReferenceEquals(scope, BackgroundWork))
                {
                    var stale = candidate;
                    candidate = null;
                    stale.Dispose();
                    return;
                }
            }
            catch (Exception ex)
            {
                try { candidate?.Dispose(); }
                catch { }
                if (ReferenceEquals(scope, BackgroundWork))
                    LogPerformanceError("PerformanceStage: Tab construction failed; staying in Lanes for this song.", ex);
                return;
            }

            _playfield = candidate;
            try { lanes?.Dispose(); }
            catch (Exception ex)
            {
                if (ReferenceEquals(scope, BackgroundWork))
                    LogPerformanceError("PerformanceStage: Lanes cleanup failed after Tab selection.", ex);
            }
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
        protected ScrollSpeedIndicator InitializeReadyFont()
        {
            var scope = BackgroundWork;
            IFont? readyFont = null;
            IFont? scrollFont = null;
            bool transferred = false;
            try
            {
                // Themed skins may style the centered LOADING.../READY... text
                // with a Latin display family; NX keeps the serif.
                var theme = _resourceManager?.CurrentTheme ?? SkinTheme.Empty;
                var stateFamily = ResolveStateFontFamily(theme);
                if (!ReferenceEquals(scope, BackgroundWork)) return new ScrollSpeedIndicator(null);
                var stateSize = stateFamily.Length > 0 ? ResolveStateFontSize(theme) : 24;
                if (!ReferenceEquals(scope, BackgroundWork)) return new ScrollSpeedIndicator(null);
                readyFont = stateFamily.Length > 0
                    ? _resourceManager.LoadFont(stateFamily, stateSize)
                    : _resourceManager.LoadFont("NotoSerifJP", 24);
                if (!ReferenceEquals(scope, BackgroundWork)) return new ScrollSpeedIndicator(null);
                scrollFont = _resourceManager.LoadFont("NotoSerifJP", 14);
                if (!ReferenceEquals(scope, BackgroundWork)) return new ScrollSpeedIndicator(null);
                var indicator = new ScrollSpeedIndicator(scrollFont);
                _readyFont = readyFont;
                _scrollSpeedFont = scrollFont;
                _scrollSpeedIndicator = indicator;
                transferred = true;
                return indicator;
            }
            catch (Exception ex)
            {
                // Font initialization failed, fallback will be used; keep the
                // Release-visible diagnostic (logger is Console-backed).
                if (ReferenceEquals(scope, BackgroundWork))
                    Logger.LogError(ex, "InitializeReadyFont failed; the null-font fallback will be used");
                var fallback = new ScrollSpeedIndicator(null);
                if (ReferenceEquals(scope, BackgroundWork))
                {
                    _readyFont = null!;
                    _scrollSpeedFont = null;
                    _scrollSpeedIndicator = fallback;
                }
                return fallback;
            }
            finally
            {
                if (!transferred)
                {
                    try { readyFont?.RemoveReference(); }
                    catch (Exception ex)
                    {
                        if (ReferenceEquals(scope, BackgroundWork))
                            Logger.LogError(ex, "InitializeReadyFont failed to release readyFont during cleanup");
                    }
                    try { scrollFont?.RemoveReference(); }
                    catch (Exception ex)
                    {
                        if (ReferenceEquals(scope, BackgroundWork))
                            Logger.LogError(ex, "InitializeReadyFont failed to release scrollFont during cleanup");
                    }
                }
            }
        }
        
        /// <summary>
        /// Loads all performance UI assets (7_* files). Each load is best-effort:
        /// TryLoadTexture swallows per-asset failures and returns null, so a missing
        /// skin asset degrades gracefully without aborting the whole load.
        /// </summary>
        internal void LoadPerformanceUIAssets()
        {
            var scope = BackgroundWork;
            // Load background texture using TexturePath constant
            var background = TryLoadTexture(TexturePath.PerformanceBackgroundTexture);
            if (!ReferenceEquals(scope, BackgroundWork))
            {
                background?.RemoveReference();
                return;
            }
            _backgroundTexture = background;

            // Load shutter texture using TexturePath constant
            var shutter = TryLoadTexture(TexturePath.Shutter);
            if (!ReferenceEquals(scope, BackgroundWork))
            {
                shutter?.RemoveReference();
                return;
            }
            _shutterTexture = shutter;

            var danger = TryLoadTexture(TexturePath.Danger);
            if (!ReferenceEquals(scope, BackgroundWork))
            {
                danger?.RemoveReference();
                return;
            }
            _dangerOverlayTexture = danger;
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
            ITexture? texture = null;
            try
            {
                if (_resourceManager == null)
                    return null;
                _resourceManager.TryLoadTexture(path, out texture);
                return texture!;
            }
            catch
            {
                try { texture?.RemoveReference(); }
                catch { }
                return null;
            }
        }
        
        /// <summary>
        /// Cleans up all performance UI assets
        /// </summary>
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
            judgementManager.PlayerHitResolved += OnPlayerHitResolved;

            gaugeManager.GaugeChanged += OnGaugeChanged;
            gaugeManager.Failed += OnPlayerFailed;
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
            _playfield?.OnLaneInput(e.Lane);

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

            // The chip sound plays from OnPlayerHitResolved once the hit is judged, so each
            // hit of a chord sounds the note it judged.
        }

        /// <summary>
        /// Plays a player hit's chip sound: the note it judged, or the lane's nearest note for
        /// an empty hit. Runs in the same frame as the hit, when judgement drains the queue.
        /// </summary>
        private void OnPlayerHitResolved(object? sender, PlayerHitResolvedEventArgs e)
        {
            if (_audioSession?.IsPlaying != true)
                return;

            var note = Rules?.FindChipNoteForHit(e);
            if (note != null)
                _audioSession.PlayChip(note);
        }

        /// <summary>
        /// Handles visual feedback after gameplay rules process a judgement
        /// </summary>
        private void OnJudgementMade(object? sender, DTXMania.Game.Lib.Song.Entities.JudgementEvent e)
        {
            // The event invocation may have captured this handler before synchronous teardown.
            if (sender is JudgementManager && !ReferenceEquals(sender, Rules?.JudgementManager))
                return;

            // HPA-509: per-hit fast/slow visibility, computed once and shared by the
            // Lanes timing spawn and the Tab popup's fast/slow line. The cumulative
            // Fast/Slow stat tiles stay always-visible and independent of this gate.
            bool showPerHitTiming = _visualGates.ShowHitTimingFeedback
                && e.IsHit()
                && !FrozenAutoPlayLanes.Contains(e.Lane);

            var frame = (Rules ?? EmptyRules).BuildFrameState(CurrentVisualSongTimeMs, _currentProgressValue);
            _playfield?.OnJudgement(e, frame, showPerHitTiming);
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
            _playfield?.OnAutoHit(note.LaneIndex);
            _audioSession?.PlayChip(note);
        }

        /// <summary>
        /// Cleans up gameplay managers
        /// </summary>
        private void CleanupGameplayManagers(GameplayRules? rules)
        {
            rules?.ClearJudgement();
            rules?.ClearScoreAndCombo();
            rules?.ClearGaugeAndSkill();
        }

        #endregion

        #region Component Updates

        private void UpdateComponents(double deltaTime)
        {
            // Update background renderer
            _backgroundRenderer?.Update(deltaTime);

            _playfield?.Update(deltaTime, !_isLoading && !(_isReady && _readyCountdown > 0));

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

        private void DrawBackground(Rectangle viewport)
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
                DrawFallbackBackground(viewport);
            }

            // Chart background video above the static background, below the lanes.
            // The player renders nothing while no timely frame exists (inactive,
            // failed, or still-decoding media), leaving the fallback visible.
            var (videoBounds, videoDepth) = ResolveChartVideoDrawLayout();
            _chartVideoPlayer?.Draw(_spriteBatch, videoBounds, videoDepth);
        }

        internal virtual void DrawFallbackBackground(Rectangle viewport) =>
            _backgroundRenderer?.Draw(_spriteBatch, viewport, 1.0f);

        [ExcludeFromCodeCoverage]
        private void DrawUIElements() => DrawUiPass(_uiManager);

        internal void DrawUiPass(UIManager? uiManager)
        {
            // Draw shutters first (overlay elements)
            DrawShutters();

            DrawPlayfieldHud(_spriteBatch);

            uiManager?.Draw(_spriteBatch, 0);

            // Draw overlays last (topmost)
            DrawOverlays();

            // Draw ready state or loading indicator after the stage UI.
            DrawGameplayState();
        }

        internal void DrawPlayfieldHud(SpriteBatch batch)
        {
            _playfield?.DrawHud(batch, CurrentFrame);
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
        
        internal virtual void DrawFallbackRectangle(Rectangle destinationRectangle, Color color, float layerDepth)
        {
            if (_fallbackWhiteTextureResource != null)
            {
                DrawFallbackTexture(_fallbackWhiteTextureResource, destinationRectangle, color, layerDepth);
            }
        }

        [ExcludeFromCodeCoverage]
        internal virtual void DrawFallbackTexture(ITexture texture, Rectangle destinationRectangle, Color color, float layerDepth)
        {
            var rawTexture = texture.Texture;
            if (rawTexture != null)
                _spriteBatch.Draw(rawTexture, destinationRectangle, null, color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
        }
        
        /// <summary>
        /// Draws the danger overlay when appropriate.
        /// </summary>
        private void DrawOverlays()
        {
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
            var scope = BackgroundWork;
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
                Frame = (Rules ?? EmptyRules).BuildFrameState(rawMs, chartManager != null ? _currentProgressValue : 0f),
                NoteDisplayStyle = (_playfield?.Mode ?? DrumsNoteDisplayStyle.Lanes).ToString(),
                LastHit = _lastLaneHit
            };
            if (ReferenceEquals(scope, BackgroundWork))
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
            var shouldDispose = disposing && !_disposed;
            Exception? failure = null;
            try
            {
                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if (shouldDispose)
            {
                var spriteBatch = _spriteBatch;
                var uiManager = _uiManager;
                _spriteBatch = null!;
                _uiManager = null!;
                try { spriteBatch?.Dispose(); }
                catch (Exception ex) { failure ??= ex; }
                try { uiManager?.Dispose(); }
                catch (Exception ex) { failure ??= ex; }
            }

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
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
