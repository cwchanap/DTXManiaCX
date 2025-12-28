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
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Performance;
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
        private PadRenderer _padRenderer;

        // BGM management
        private Dictionary<string, ISound> _bgmSounds = new Dictionary<string, ISound>();
        private List<BGMEvent> _scheduledBGMEvents = new List<BGMEvent>();

        // UX components
        private BitmapFont _readyFont;

        // Performance UI Assets
        private ITexture _backgroundTexture;
        private ITexture _shutterTexture; // Single shutter texture
        private ITexture _laneBgTexture;
        private ITexture _laneDividerTexture;
        private ITexture _laneFlashTexture;
        private ITexture _judgementLineTexture;
        private ITexture _gaugeBaseTexture;
        private ITexture _gaugeFillTexture;
        private ITexture _progressBaseTexture;
        private ITexture _progressFillTexture;
        private ITexture _comboDigitsTexture;
        private ITexture _scoreDigitsTexture;
        private ITexture _pauseOverlayTexture;
        private ITexture _dangerOverlayTexture;
        private ITexture _skillPanelTexture;
        
        // Judgement text textures (using sprite sheets)
        private ITexture _judgeStringsTexture;
        
        // Timing indicator textures (using sprite sheet)
        private ITexture _lagNumbersTexture;

        // Gameplay state
        private bool _isLoading = true;
        private bool _isReady = false;
        private double _readyCountdown = 1.0; // 1 second ready period
        private GameTime _currentGameTime;
        private double _totalTime = 0.0;
        private double _stageElapsedTime = 0.0; // Track elapsed time since stage activation for miss detection
        private Texture2D _fallbackWhiteTexture;
        
        // UI state tracking
        private bool _isPaused = false;
        private bool _isDanger = false;
        private float _currentGaugeValue = 0.5f; // 0.0 to 1.0
        private float _currentProgressValue = 0.0f; // 0.0 to 1.0

        // Stage completion state
        private bool _stageCompleted = false;
        private bool _inputPaused = false;
        private PerformanceSummary _performanceSummary;
        private const double SongEndBufferSeconds = 3.0; // 3 seconds after song end
        
        // Autoplay functionality
        private bool _autoPlayEnabled = false;
        private int _autoPlayNoteIndex = 0; // Track the next note to auto-hit
        
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

            // Initialize autoplay setting from config
            InitializeAutoPlay();

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


            // Draw components in proper Z-order (BackToFront sorting):
            // Background (1.0f) → Lanes (0.8f) → Pads (0.75f) → Notes (0.7f) → JudgementLine (0.6f) → JudgementTexts (0.5f)

            // Base pass: Background → Lanes → Pads → Notes → Judgement Line → Judgement Texts
            _spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend);
            
            // Draw background (furthest back - highest depth value)
            DrawBackground();

            // Draw lane backgrounds
            DrawLaneBackgrounds();

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

        private void InitializeAutoPlay()
        {
            // Get autoplay setting from config
            _autoPlayEnabled = _game?.ConfigManager?.Config?.AutoPlay ?? false;
            _autoPlayNoteIndex = 0;
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


            // Initialize Phase 2 components
            _audioLoader = new AudioLoader(_resourceManager);
            _noteRenderer = new NoteRenderer(graphicsDevice, _resourceManager);
            _effectsManager = new EffectsManager(graphicsDevice, _resourceManager);
            _judgementTextPopupManager = new JudgementTextPopupManager(graphicsDevice, _resourceManager);
            _padRenderer = new PadRenderer(graphicsDevice, _resourceManager);

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
            _readyCountdown = 1.0; // Reset ready countdown
            _autoPlayNoteIndex = 0; // Reset autoplay note index

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
            _padRenderer?.Dispose();
            _padRenderer = null;

            // Cleanup UX components
            _readyFont?.Dispose();
            _readyFont = null;
            
            // Cleanup performance UI assets
            CleanupPerformanceUIAssets();

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

            // 2. Deactivate judgement manager to stop processing input
            if (_judgementManager != null)
            {
                _judgementManager.IsActive = false;
            }

            // 3. Pause input to block further judgement processing
            _inputPaused = true;

            // 4. Component cleanup will be handled automatically by OnDeactivate() during stage transition
            // This prevents premature texture disposal that causes gauge flickering

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

            // Update pad renderer
            _padRenderer?.Update(deltaTime);

            // Handle BGM event scheduling and gameplay managers
            if (_songTimer != null && _songTimer.IsPlaying)
            {
                // Only run timing logic when the song is actually playing
                var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
                
                // Process BGM events during song playback
                ProcessBGMEvents(currentTimeMs);
                
                // Update gameplay managers with actual song time
                UpdateGameplayManagers(currentTimeMs);
                
                // Update song progress
                UpdateSongProgress(currentTimeMs);
                
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

            // Get current song time and active notes
            var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
            var lookAheadMs = _noteRenderer.EffectiveLookAheadMs > 0 ? _noteRenderer.EffectiveLookAheadMs : 1500.0;
            var activeNotes = _chartManager.GetActiveNotes(currentTimeMs, lookAheadMs);

            // Draw the notes
            _noteRenderer.DrawNotes(_spriteBatch, activeNotes, currentTimeMs);
        }

        /// <summary>
        /// Draws note overlay animations in effects pass
        /// </summary>
        private void DrawNoteOverlays()
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

            // Draw the note overlays with additive blending
            _noteRenderer.DrawNoteOverlays(_spriteBatch, activeNotes, currentTimeMs);
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
        /// Loads all performance UI assets (7_* files)
        /// </summary>
        private void LoadPerformanceUIAssets()
        {
            try
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
                
                // Load timing indicator sprite sheet using TexturePath constant  
                _lagNumbersTexture = TryLoadTexture(TexturePath.LagIndicator);
                
                // Load overlay textures using TexturePath constants
                _pauseOverlayTexture = TryLoadTexture(TexturePath.PauseOverlay);
                _dangerOverlayTexture = TryLoadTexture(TexturePath.Danger);
                
                // Load skill panel texture using TexturePath constant
                _skillPanelTexture = TryLoadTexture(TexturePath.SkillPanel);
                
            }
            catch (Exception ex)
            {
            }
        }
        
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
            
            _lagNumbersTexture?.RemoveReference();
            _lagNumbersTexture = null;
            
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

            // Try to use BitmapFont for actual text rendering
            if (_readyFont != null && _readyFont.IsLoaded)
            {
                // Measure text to center it properly
                var textSize = _readyFont.MeasureText(text);
                var textX = (int)(screenCenter.X - textSize.X / 2);
                var textY = (int)(screenCenter.Y - textSize.Y / 2);

                // Draw text at gameplay state depth (0.1f = front layer for UI pass)
                _readyFont.DrawText(_spriteBatch, text, textX, textY, color, BitmapFont.FontType.Normal, 0.1f);
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

                // Use the cached white texture for fallback rendering at proper gameplay state depth (0.1f)
                if (_fallbackWhiteTexture != null)
                {
                    _spriteBatch.Draw(_fallbackWhiteTexture, rectPosition, null, color, 0f, Vector2.Zero, SpriteEffects.None, 0.1f);
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
            // Trigger immediate pad press effect on input (regardless of judgement)
            _padRenderer?.TriggerPadPress(e.Lane, false); // false = key-down, not judged hit
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

                // Trigger pad press effect
                _padRenderer?.TriggerPadPress(e.Lane, true);
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
            // Check if NoFail is enabled in config
            bool noFailEnabled = _game?.ConfigManager?.Config?.NoFail ?? false;
            
            if (!noFailEnabled)
            {
                // Trigger stage completion on failure only if NoFail is disabled
                if (!_stageCompleted)
                {
                    FinalizePerformance(CompletionReason.PlayerFailed);
                }
            }
            // If NoFail is enabled, do nothing - let the player continue playing
        }

        /// <summary>
        /// Updates gameplay managers during active gameplay
        /// </summary>
        private void UpdateGameplayManagers(double currentSongTimeMs)
        {
            // Process autoplay if enabled
            if (_autoPlayEnabled)
            {
                ProcessAutoPlay(currentSongTimeMs);
            }

            // Always update judgement manager to ensure miss detection runs
            // Input processing is controlled by IsActive flag internally
            _judgementManager?.Update(currentSongTimeMs);
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
            int autoPlayWindowMs = 50; // Configurable window for autoplay timing (±50ms)
            
            // Process notes that should be auto-hit at current time
            while (_autoPlayNoteIndex < allNotes.Count)
            {
                var note = allNotes[_autoPlayNoteIndex];
                
                // Check timing difference
                var timeDifference = currentSongTimeMs - note.TimeMs;
                
                if (timeDifference < -autoPlayWindowMs)
                {
                    // This note is in the future, stop processing (do not increment index)
                    break;
                }
                else if (timeDifference > autoPlayWindowMs)
                {
                    // This note is too far in the past, skip it
                    _autoPlayNoteIndex++;
                }
                else
                {
                    // Within autoplay window - trigger autoplay hit
                    var noteData = _judgementManager.GetNoteRuntimeData(note.Id);
                    if (noteData?.Status == DTXMania.Game.Lib.Stage.Performance.NoteStatus.Pending)
                    {
                        // Auto-hit the note using the JudgementManager's test method
                        _judgementManager.TestTriggerLaneHit(note.LaneIndex, "AutoPlay");
                        
                        // Trigger pad press effect for autoplay
                        _padRenderer?.TriggerPadPress(note.LaneIndex, true);
                    }
                    
                    _autoPlayNoteIndex++;
                }
            }
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
        }

        #endregion

        #region Drawing Methods

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
        }

        private void DrawLaneBackgrounds()
        {
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
            if (_judgementLineTexture != null)
            {
                // Draw hit-bar (ScreenPlayDrums hit-bar.png) at judgement line depth
                var hitBarPos = PerformanceUILayout.HitBar.Position;
                var hitBarRect = new Rectangle((int)hitBarPos.X, (int)hitBarPos.Y, _judgementLineTexture.Width, _judgementLineTexture.Height);
                _judgementLineTexture.Draw(_spriteBatch, hitBarRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.6f);
            }
            else
            {
                // Fallback to JudgementLineRenderer
                _judgementLineRenderer?.Draw(_spriteBatch);
            }
        }

        private void DrawPads()
        {
            if (_padRenderer == null)
                return;

            _padRenderer.Draw(_spriteBatch);
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
            _comboDisplay?.Draw(_spriteBatch);
            
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
                if (_fallbackWhiteTexture != null)
                {
                    var fillRect = new Rectangle(barRect.X, barRect.Bottom - fillHeight, barRect.Width, fillHeight);
                    _spriteBatch.Draw(_fallbackWhiteTexture, fillRect, null, Color.LightBlue, 0f, Vector2.Zero, SpriteEffects.None, 0.2f);
                }
            }
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
                _dangerOverlayTexture.Draw(_spriteBatch, dangerRect, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.05f);
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

            // Check for song end
            if (currentTimeMs >= (_parsedChart.DurationMs + SongEndBufferSeconds * 1000))
            {
                FinalizePerformance(CompletionReason.SongComplete);
            }

            // Check for player failure (only if NoFail is disabled)
            bool noFailEnabled = _game?.ConfigManager?.Config?.NoFail ?? false;
            if (!noFailEnabled && _gaugeManager?.HasFailed == true)
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
