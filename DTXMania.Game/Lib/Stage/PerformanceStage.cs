using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game;
using DTX.Resources;
using DTX.UI;
using DTX.Input;
using DTX.Song;
using DTX.Song.Components;
using DTX.Stage.Performance;
using DTXMania.Game.Lib.Song;

namespace DTX.Stage
{
    /// <summary>
    /// Performance stage for playing songs with the 9-lane GITADORA XG layout
    /// Based on DTXManiaNX performance screen patterns
    /// </summary>
    public class PerformanceStage : BaseStage
    {
        #region Private Fields

        private SpriteBatch _spriteBatch;
        private ResourceManager _resourceManager;
        private UIManager _uiManager;
        private InputManager _inputManager;

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

        // BGM management
        private Dictionary<string, ISound> _bgmSounds = new Dictionary<string, ISound>();
        private List<BGMEvent> _scheduledBGMEvents = new List<BGMEvent>();

        // UX components
        private BitmapFont _readyFont;

        // Gameplay state
        private bool _isLoading = true;
        private bool _isReady = false;
        private double _readyCountdown = 1.0; // 1 second ready period (in seconds, not milliseconds)
        private GameTime _currentGameTime;
        private double _totalTime = 0.0;
        private Texture2D _fallbackWhiteTexture;

        #endregion

        #region Properties

        public override StageType Type => StageType.Performance;

        #endregion

        #region Constructor

        public PerformanceStage(BaseGame game) : base(game)
        {
            // Initialize core systems
            _spriteBatch = new SpriteBatch(game.GraphicsDevice);
            _resourceManager = ResourceManagerFactory.CreateResourceManager(game.GraphicsDevice);
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

            _spriteBatch.Begin();

            // Draw components in proper order:
            // Background → Lane Backgrounds → Notes → Judgement Line → UI Elements

            // Draw background
            DrawBackground();

            // Draw lane backgrounds
            DrawLaneBackgrounds();

            // Draw scrolling notes (Phase 2)
            DrawNotes();

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

            // Initialize UX components
            InitializeReadyFont();

            // Create a reusable white texture for fallback rendering
            _fallbackWhiteTexture = new Texture2D(graphicsDevice, 1, 1);
            _fallbackWhiteTexture.SetData(new[] { Color.White });
        }

        private void CleanupComponents()
        {
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

            // Handle ESC key to return to song selection
            if (_inputManager.IsKeyPressed((int)Keys.Escape))
            {
                ReturnToSongSelect();
            }

            // TODO: Handle gameplay input in later phases
        }

        private void ReturnToSongSelect()
        {
            // Return to song selection stage
            ChangeStage(StageType.SongSelect, new DTXManiaFadeTransition(0.5));
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
                    {
                        return;
                    }

                    _parsedChart = await DTXChartParser.ParseAsync(chartPath);
                }

                // Create chart manager

                _chartManager = new ChartManager(_parsedChart);

                // Set BPM and scroll speed in note renderer
                _noteRenderer?.SetBpm(_parsedChart.Bpm);

                // Set scroll speed based on user preference (default 100% for now)
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
                else
                {
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
                System.Diagnostics.Debug.WriteLine($"[CRITICAL] PerformanceStage: Failed to initialize gameplay. Exception: {ex}");
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
                return;

            // Handle ready countdown
            if (_isReady && _readyCountdown > 0)
            {
                _readyCountdown -= deltaTime;
                if (_readyCountdown <= 0)
                {
                    StartSong();
                }
                return;
            }

            // Update note renderer
            _noteRenderer?.Update(deltaTime);

            // Handle BGM event scheduling
            if (_songTimer != null && _songTimer.IsPlaying)
            {
                var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
                ProcessBGMEvents(currentTimeMs);
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

                // Choose playback strategy based on BGM events
                if (_scheduledBGMEvents.Count > 0)
                {
                    // New approach: Use BGM events for timed playback, silence the background audio
                    _songTimer.Volume = 0.0f; // Mute the background audio since we'll use BGM events
                    System.Diagnostics.Debug.WriteLine($"PerformanceStage: Using BGM events for audio ({_scheduledBGMEvents.Count} events), background audio muted");
                }
                else
                {
                    // Legacy approach: Play background audio immediately (no BGM events)
                    _songTimer.Volume = 1.0f; // Ensure background audio is audible
                    System.Diagnostics.Debug.WriteLine("PerformanceStage: No BGM events found, using background audio");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("PerformanceStage: Cannot start song - no song timer available");
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
                var alpha = (float)(0.5 + 0.5 * Math.Sin(_readyCountdown * 0.01)); // Pulsing effect
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
                var textWidth = text.Length * 16; // Approximate character width
                var textHeight = 24; // Approximate character height
                var textPosition = new Vector2(screenCenter.X - textWidth / 2, screenCenter.Y - textHeight / 2);

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
                        System.Diagnostics.Debug.WriteLine($"PerformanceStage: Loaded BGM sound {bgmEvent.WavId}: {Path.GetFileName(bgmEvent.AudioFilePath)}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"PerformanceStage: BGM file not found: {bgmEvent.AudioFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PerformanceStage: Failed to load BGM {bgmEvent.WavId}: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"PerformanceStage: Triggered BGM event {bgmEvent.WavId} at {bgmEvent.TimeMs:F1}ms");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PerformanceStage: Failed to play BGM {bgmEvent.WavId}: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"PerformanceStage: BGM sound not loaded for event {bgmEvent.WavId}");
            }
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
