using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Performance stage for playing songs with the 9-lane GITADORA XG layout.
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
    public class PerformanceStage : BaseStage
    {
        #region Private Fields

        private SpriteBatch _spriteBatch;
        private IResourceManager _resourceManager;
        private UIManager _uiManager;
        private InputManagerCompat _inputManager;

        // Stage data
        private SongListNode _selectedSong;
        private int _selectedDifficulty;
        private int _songId;

        // Performance components
        private BackgroundRenderer _backgroundRenderer;
        private LaneBackgroundRenderer _laneBackgroundRenderer;
        private JudgementLineRenderer _judgementLineRenderer;
        private ScoreDisplay _scoreDisplay;
        private ComboDisplay _comboDisplay;
        private GaugeDisplay _gaugeDisplay;

        // Phase 2 components - Chart loading and note scrolling
        private ParsedChart _parsedChart;
        private ChartManager _chartManager;
        private AudioLoader _audioLoader;
        private SongTimer _songTimer;
        private NoteRenderer _noteRenderer;

        // Phase 3 components - Gameplay managers
        private JudgementManager _judgementManager;
        private ScoreManager _scoreManager;
        private ComboManager _comboManager;
        private GaugeManager _gaugeManager;
        private EffectsManager _effectsManager;
        private JudgementTextPopupManager _judgementTextPopupManager;

        // BGM management
        private Dictionary<string, ISound> _bgmSounds = new Dictionary<string, ISound>();
        private List<BGMEvent> _scheduledBGMEvents = new List<BGMEvent>();

        // UX components
        private BitmapFont _readyFont;

        // Gameplay state
        private bool _isLoading = true;
        private bool _isReady = false;
        private double _readyCountdown = 1.0; // 1 second ready period
        private GameTime _currentGameTime;
        private double _totalTime = 0.0;
        private double _stageElapsedTime = 0.0; // Track elapsed time since stage activation for miss detection
        private Texture2D _fallbackWhiteTexture;

        // Stage completion state
        private bool _stageCompleted = false;
        private bool _inputPaused = false;
        private PerformanceSummary _performanceSummary;
        private const double SongEndBufferSeconds = 3.0; // 3 seconds after song end
        
        // Note: Using global stage transition debouncing from BaseGame

        #endregion

        #region Constants


        #endregion

        #region Properties

        public override StageType Type => StageType.Performance;

        #endregion

        #region Constructor

        public PerformanceStage(BaseGame game) : base(game)
        {
            // Initialize core systems
            _spriteBatch = new SpriteBatch(game.GraphicsDevice);
            _resourceManager = game.ResourceManager;
            _uiManager = new UIManager();
            _inputManager = game.InputManager;
        }

        #endregion

        #region BaseStage Implementation

        protected override void OnActivate()
        {

            // Extract shared data from stage transition
            ExtractSharedData();

            // Initialize UI components
            InitializeComponents();

            // Start async chart loading and audio preparation
            _ = InitializeGameplayAsync();

        }

        protected override void OnDeactivate()
        {

            // Clean up components
            CleanupComponents();
        }

        protected override void OnUpdate(double deltaTime)
        {
            // Update total time for precise GameTime tracking
            _totalTime += deltaTime;

            // Track elapsed time since stage activation for miss detection
            _stageElapsedTime += deltaTime;

            // Create GameTime for precise timing
            _currentGameTime = new GameTime(TimeSpan.FromSeconds(_totalTime), TimeSpan.FromSeconds(deltaTime));

            // Update input manager
            _inputManager?.Update(deltaTime);
            

            // Handle input
            HandleInput();

            // Update UI manager
            _uiManager?.Update(deltaTime);

            // Update performance components
            UpdateComponents(deltaTime);

            // Update gameplay state
            UpdateGameplay(deltaTime);

            // Update song timer
            _songTimer?.Update(_currentGameTime);
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            // Draw components in proper order:
            // Background → Lanes → Notes → HitEffectsManager.Draw → JudgementTexts.Draw → JudgementLine → UI

            // Begin standard spritebatch for most elements
            _spriteBatch.Begin();

            // Draw background
            DrawBackground();

            // Draw lane backgrounds
            DrawLaneBackgrounds();

            // Draw scrolling notes (Phase 2)
            DrawNotes();

            _spriteBatch.End();

            // Begin additive blend mode for effects layer
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);

            // Draw hit effects with additive blending
            DrawHitEffects();

            _spriteBatch.End();

            // Resume standard rendering for remaining elements
            _spriteBatch.Begin();

            // Draw judgement text popups
            DrawJudgementTexts();

            // Draw judgement line
            DrawJudgementLine();

            // Draw UI elements (gauge, score, combo)
            DrawUIElements();

            // Draw ready state or loading indicator
            DrawGameplayState();

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

            // Initialize gauge display
            _gaugeDisplay = new GaugeDisplay(_resourceManager, graphicsDevice);

            // Initialize Phase 2 components
            _audioLoader = new AudioLoader(_resourceManager);
            _noteRenderer = new NoteRenderer(graphicsDevice, _resourceManager);
            _effectsManager = new EffectsManager(graphicsDevice, _resourceManager);
            _judgementTextPopupManager = new JudgementTextPopupManager(graphicsDevice, _resourceManager);

            // Initialize UX components
            InitializeReadyFont();

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
            _readyCountdown = 1.0; // Reset ready countdown

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

            // Cleanup gauge display
            _gaugeDisplay?.Dispose();
            _gaugeDisplay = null;

            // Cleanup Phase 2 components
            _songTimer?.Dispose();
            _songTimer = null;
            _audioLoader?.Dispose();
            _audioLoader = null;
            _noteRenderer?.Dispose();
            _noteRenderer = null;
            _effectsManager?.Dispose();
            _effectsManager = null;
            _judgementTextPopupManager?.Dispose();
            _judgementTextPopupManager = null;

            // Cleanup UX components
            _readyFont?.Dispose();
            _readyFont = null;

            // Cleanup fallback texture
            _fallbackWhiteTexture?.Dispose();
            _fallbackWhiteTexture = null;

            // Cleanup BGM sounds
            foreach (var sound in _bgmSounds.Values)
            {
                sound?.Dispose();
            }
            _bgmSounds.Clear();
            _scheduledBGMEvents.Clear();

            // Cleanup gameplay managers
            CleanupGameplayManagers();

            // Clear chart data
            _parsedChart = null;
            _chartManager = null;

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
                if (_game is BaseGame baseGame && baseGame.CanPerformStageTransition())
                {
                    baseGame.MarkStageTransition();
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
        }

        /// <summary>
        /// Handles ESC key and controller Back button input during performance.
        /// This method provides immediate exit functionality for players who want to
        /// return to song selection without completing the current song.
        /// 
        /// ESC/Back button behavior:
        /// - Immediately stops song playback and timing
        /// - Performs comprehensive resource cleanup to prevent memory leaks
        /// - Disposes audio components, graphics resources, and gameplay managers
        /// - Pauses input processing to prevent further judgement handling
        /// - Returns to song selection stage with smooth fade transition
        /// 
        /// Controller support:
        /// - Supports both keyboard ESC key and gamepad/controller Back button
        /// - Uses InputCommandType.Back for universal controller compatibility
        /// </summary>
        private void ReturnToSongSelect()
        {
            // 1. Stop the song timer
            _songTimer?.Stop();

            // 2. Deactivate judgement manager to stop processing input
            if (_judgementManager != null)
            {
                _judgementManager.IsActive = false;
            }

            // 3. Clean up components to free resources and avoid audio leaks
            CleanupComponents();

            // 4. Pause input to block further judgement processing
            _inputPaused = true;

            // Return to song selection stage
            StageManager?.ChangeStage(StageType.SongSelect,
                new DTXManiaFadeTransition(0.5), null);
        }

        #endregion

        #region Phase 2 - Chart Loading and Gameplay

        /// <summary>
        /// Initializes gameplay using pre-parsed chart data and loads audio
        /// </summary>
        private async Task InitializeGameplayAsync()
        {
            try
            {
                _isLoading = true;

                // Check if we have a parsed chart from shared data
                if (_parsedChart == null)
                {
                    // Fallback: parse chart if not provided (for backwards compatibility)
                    // Get the correct chart for the selected difficulty
                    var chart = _selectedSong.GetCurrentDifficultyChart(_selectedDifficulty);
                    var chartPath = chart?.FilePath;
                    if (string.IsNullOrEmpty(chartPath))
                        return;

                    _parsedChart = await DTXChartParser.ParseAsync(chartPath);
                }

                // Create chart manager
                _chartManager = new ChartManager(_parsedChart);

                // Initialize gameplay managers
                InitializeGameplayManagers();

                // Set BPM and scroll speed in note renderer
                _noteRenderer?.SetBpm(_parsedChart.Bpm);

                // Set scroll speed based on user preference
                // TODO: Get scroll speed from user config
                var scrollSpeedSetting = 100; // Default scroll speed
                _noteRenderer?.SetScrollSpeed(scrollSpeedSetting);

                // Load background audio - ALWAYS needed for SongTimer creation (master clock)
                if (!string.IsNullOrEmpty(_parsedChart.BackgroundAudioPath))
                {
                    // Use the correct chart path for the selected difficulty
                    var chart = _selectedSong.GetCurrentDifficultyChart(_selectedDifficulty);
                    var chartPath = chart?.FilePath ?? _selectedSong?.DatabaseChart?.FilePath;
                    await _audioLoader.PreloadForChartAsync(chartPath, _parsedChart.BackgroundAudioPath);
                }

                // Load BGM sounds for all BGM events (separate from background audio)
                await LoadBGMSoundsAsync();

                // Schedule BGM events for playback
                _scheduledBGMEvents = _parsedChart.BGMEvents.ToList();

                // Create song timer
                _songTimer = _audioLoader.CreateSongTimer();

                _isLoading = false;
                _isReady = true;
            }
            catch (Exception ex)
            {
                _isLoading = false;
                // Rethrowing the exception to make sure it's not silently ignored.
                // This will prevent the stage from being in a broken state.
                throw;
            }
        }

        /// <summary>
        /// Updates gameplay state and timing
        /// </summary>
        private void UpdateGameplay(double deltaTime)
        {
// Reduced logging for performance

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

            // Update effects manager
            _effectsManager?.Update(deltaTime);

            // Update judgement text popup manager
            _judgementTextPopupManager?.Update(deltaTime);

            // Handle BGM event scheduling and gameplay managers
            if (_songTimer != null && _songTimer.IsPlaying)
            {
                // Only run timing logic when the song is actually playing
                var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
                
                // Process BGM events during song playback
                ProcessBGMEvents(currentTimeMs);
                
                // Update gameplay managers with actual song time
                UpdateGameplayManagers(currentTimeMs);
                
                // Check for stage completion conditions
                CheckStageCompletion(currentTimeMs);
            }
        }

        /// <summary>
        /// Starts playing the song
        /// </summary>
        private void StartSong()
        {
            if (_songTimer != null && _currentGameTime != null)
            {
                // Start the song timer - this provides the master clock for notes and BGM events
                _songTimer.SetPosition(0.0, _currentGameTime);
                _songTimer.Play(_currentGameTime);
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
                    // Legacy approach: Play background audio immediately (no BGM events)
                    _songTimer.Volume = 1.0f; // Ensure background audio is audible
                }
            }
        }

        /// <summary>
        /// Draws scrolling notes
        /// </summary>
        private void DrawNotes()
        {
            if (_noteRenderer == null || _chartManager == null || _songTimer == null || _currentGameTime == null)
                return;

            if (!_songTimer.IsPlaying)
                return;

            // Get current song time using precise GameTime-based timing
            var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);

            // Get active notes using the same look-ahead time as scroll calculation
            var lookAheadMs = _noteRenderer.EffectiveLookAheadMs > 0 ? _noteRenderer.EffectiveLookAheadMs : 1500.0;
            var activeNotes = _chartManager.GetActiveNotes(currentTimeMs, lookAheadMs);

            // Draw the notes
            _noteRenderer.DrawNotes(_spriteBatch, activeNotes, currentTimeMs);
        }

        /// <summary>
        /// Draws gameplay state (loading, ready, etc.)
        /// </summary>
        private void DrawGameplayState()
        {
            if (_isLoading)
            {
                // Draw loading indicator
                DrawCenteredText("LOADING...", Color.White);
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
                // Create bitmap font for ready text display
                var consoleFontConfig = BitmapFont.CreateConsoleFontConfig();
                _readyFont = new BitmapFont(_spriteBatch.GraphicsDevice, _resourceManager, consoleFontConfig);
            }
            catch (Exception ex)
            {
                // Font initialization failed, fallback will be used
                _readyFont = null;
            }
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

            if (_readyFont?.IsLoaded == true)
            {
                // Use bitmap font if available
                var textSize = _readyFont.MeasureText(text);
                var textPosition = new Vector2(screenCenter.X - textSize.X / 2, screenCenter.Y - textSize.Y / 2);

                _readyFont.DrawText(_spriteBatch, text, (int)textPosition.X, (int)textPosition.Y, color);
            }
            else
            {
                // Fallback: draw colored rectangle as placeholder
                var rectWidth = text.Length * 12;
                var rectHeight = 20;
                var rectPosition = new Rectangle(
                    (int)(screenCenter.X - rectWidth / 2),
                    (int)(screenCenter.Y - rectHeight / 2),
                    rectWidth,
                    rectHeight
                );

                // Use the cached white texture for fallback rendering
                if (_fallbackWhiteTexture != null)
                {
                    _spriteBatch.Draw(_fallbackWhiteTexture, rectPosition, color);
                }
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
                    // BGM loading failed, continue with other sounds
                }
            }
        }

        /// <summary>
        /// Processes BGM events that should be triggered at the current time
        /// </summary>
        /// <param name="currentTimeMs">Current song time in milliseconds</param>
        private void ProcessBGMEvents(double currentTimeMs)
        {
            // Process BGM events that should be triggered (within a small tolerance)
            const double timingTolerance = 50.0; // 50ms tolerance for BGM triggering

            for (int i = _scheduledBGMEvents.Count - 1; i >= 0; i--)
            {
                var bgmEvent = _scheduledBGMEvents[i];

                // Check if it's time to trigger this BGM event
                if (currentTimeMs >= bgmEvent.TimeMs - timingTolerance)
                {
                    TriggerBGMEvent(bgmEvent);
                    _scheduledBGMEvents.RemoveAt(i);
                }
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
                    var instance = sound.CreateInstance();
                    instance?.Play();
                }
                catch (Exception ex)
                {
                    // BGM playback failed, continue with game
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
            
            _scoreManager = new ScoreManager(_chartManager.TotalNotes);
            _comboManager = new ComboManager();
            _gaugeManager = new GaugeManager();

            // Wire up event handlers for UI binding
            WireUpEventHandlers();
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
        }

        /// <summary>
        /// Handles judgement events and forwards them to all managers
        /// </summary>
        private void OnJudgementMade(object? sender, DTXMania.Game.Lib.Song.Entities.JudgementEvent e)
        {
            // Forward judgement to all managers
            _scoreManager?.ProcessJudgement(e);
            _comboManager?.ProcessJudgement(e);
            _gaugeManager?.ProcessJudgement(e);

            // Spawn hit effect for successful hits (non-Miss)
            if (e.IsHit())
            {
                _effectsManager?.SpawnHitEffect(e.Lane);

                // Trigger lane flash effect
                _noteRenderer?.TriggerLaneFlash(e.Lane);
            }

            // Spawn judgement text popup for all judgements
            _judgementTextPopupManager?.SpawnPopup(e);
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
            if (_gaugeDisplay != null)
            {
                _gaugeDisplay.SetValue(e.CurrentLife / 100.0f); // Convert to 0.0-1.0 range
            }
        }

        /// <summary>
        /// Handles player failure
        /// </summary>
        private void OnPlayerFailed(object? sender, FailureEventArgs e)
        {

            // Trigger stage completion on failure
            if (!_stageCompleted)
            {
                FinalizePerformance(CompletionReason.PlayerFailed);
            }
        }

        /// <summary>
        /// Updates gameplay managers during active gameplay
        /// </summary>
        private void UpdateGameplayManagers(double currentSongTimeMs)
        {
            // Always update judgement manager to ensure miss detection runs
            // Input processing is controlled by IsActive flag internally
            _judgementManager?.Update(currentSongTimeMs);
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

            // Dispose managers
            _judgementManager?.Dispose();
            _judgementManager = null;
            _scoreManager?.Dispose();
            _scoreManager = null;
            _comboManager?.Dispose();
            _comboManager = null;
            _gaugeManager?.Dispose();
            _gaugeManager = null;
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

            // Update gauge display
            _gaugeDisplay?.Update(deltaTime);
        }

        #endregion

        #region Drawing Methods

        private void DrawBackground()
        {
            // Draw background using BackgroundRenderer
            var viewport = _spriteBatch.GraphicsDevice.Viewport;
            var backgroundRect = new Rectangle(0, 0, viewport.Width, viewport.Height);

            _backgroundRenderer?.Draw(_spriteBatch, backgroundRect);
        }

        private void DrawLaneBackgrounds()
        {
            // Draw lane backgrounds using LaneBackgroundRenderer
            _laneBackgroundRenderer?.Draw(_spriteBatch);
        }

        private void DrawJudgementLine()
        {
            // Draw judgement line using JudgementLineRenderer
            _judgementLineRenderer?.Draw(_spriteBatch);
        }

        private void DrawHitEffects()
        {
            // Draw hit effects using EffectsManager
            _effectsManager?.Draw(_spriteBatch);
        }

        private void DrawJudgementTexts()
        {
            // Draw judgement text popups using JudgementTextPopupManager
            _judgementTextPopupManager?.Draw(_spriteBatch);
        }

        private void DrawUIElements()
        {
            // Draw gauge display first (background element)
            _gaugeDisplay?.Draw(_spriteBatch);

            // Draw score and combo displays
            _scoreDisplay?.Draw(_spriteBatch);
            _comboDisplay?.Draw(_spriteBatch);

            // Draw UI manager components
            _uiManager?.Draw(_spriteBatch, 0);
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

            // Check for song end
            if (currentTimeMs >= (_parsedChart.DurationMs + SongEndBufferSeconds * 1000))
            {
                FinalizePerformance(CompletionReason.SongComplete);
            }

            // Check for player failure
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

            // Build the performance summary
            _performanceSummary = new PerformanceSummary
            {
                Score = _scoreManager?.CurrentScore ?? 0,
                MaxCombo = _comboManager?.MaxCombo ?? 0,
                ClearFlag = reason != CompletionReason.PlayerFailed,
                JustCount = _judgementManager?.GetJudgementCount(JudgementType.Just) ?? 0,
                GreatCount = _judgementManager?.GetJudgementCount(JudgementType.Great) ?? 0,
                GoodCount = _judgementManager?.GetJudgementCount(JudgementType.Good) ?? 0,
                PoorCount = _judgementManager?.GetJudgementCount(JudgementType.Poor) ?? 0,
                MissCount = _judgementManager?.GetJudgementCount(JudgementType.Miss) ?? 0,
                TotalNotes = _chartManager?.TotalNotes ?? 0,
                FinalLife = _gaugeManager?.CurrentLife ?? 0.0f,
                CompletionReason = reason
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

            StageManager?.ChangeStage(StageType.Result, new InstantTransition(), sharedData);
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


    }
}
