using System;
using System.Collections.Generic;
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

        // UX components
        private BitmapFont _readyFont;

        // Gameplay state
        private bool _isLoading = true;
        private bool _isReady = false;
        private double _readyCountdown = 1.0; // 1 second ready period (in seconds, not milliseconds)
        private GameTime _currentGameTime;
        private double _totalTime = 0.0;

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
                    var chart = GetCurrentDifficultyChart(_selectedSong, _selectedDifficulty);
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

                // Load background audio
                if (!string.IsNullOrEmpty(_parsedChart.BackgroundAudioPath))
                {
                    // Use the correct chart path for the selected difficulty
                    var chart = GetCurrentDifficultyChart(_selectedSong, _selectedDifficulty);
                    var chartPath = chart?.FilePath ?? _selectedSong?.DatabaseChart?.FilePath;
                    await _audioLoader.PreloadForChartAsync(chartPath, _parsedChart.BackgroundAudioPath);
                }
                else
                {
                }

                // Create song timer
                _songTimer = _audioLoader.CreateSongTimer();

                _isLoading = false;
                _isReady = true;
            }
            catch (Exception ex)
            {
                _isLoading = false;
                // TODO: Show error message to user
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
        }

        /// <summary>
        /// Starts playing the song
        /// </summary>
        private void StartSong()
        {
            if (_songTimer != null && _currentGameTime != null)
            {
                // Start the song immediately - notes and audio should be synchronized
                // The scroll system already handles note appearance timing via look-ahead
                _songTimer.SetPosition(0.0, _currentGameTime);
                _songTimer.Play(_currentGameTime);
                _isReady = false;
            }
            else
            {
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
            _noteRenderer.DrawNotes(_spriteBatch, activeNotes.ToList(), currentTimeMs);
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

                // Create a simple white texture if not available
                var whiteTexture = new Texture2D(_spriteBatch.GraphicsDevice, 1, 1);
                whiteTexture.SetData(new[] { Color.White });

                _spriteBatch.Draw(whiteTexture, rectPosition, color);

                whiteTexture.Dispose();
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

        #region Helper Methods

        /// <summary>
        /// Gets the chart for the current difficulty level
        /// </summary>
        private DTXMania.Game.Lib.Song.Entities.SongChart GetCurrentDifficultyChart(SongListNode currentSong, int currentDifficulty)
        {
            // If no song is selected, return null
            if (currentSong?.DatabaseSong == null)
            {
                return null;
            }

            // Get all charts for this song
            var allCharts = currentSong.DatabaseSong.Charts?.ToList();

            if (allCharts == null || allCharts.Count == 0)
            {
                var fallbackChart = currentSong.DatabaseChart;
                return fallbackChart; // Fallback to primary chart
            }

            // If we only have one chart, return it
            if (allCharts.Count == 1)
                return allCharts[0];

            // For simplicity, assume drums mode and map difficulty to chart index
            var drumCharts = allCharts.Where(chart => chart.HasDrumChart && chart.DrumLevel > 0)
                                     .OrderBy(chart => chart.DrumLevel)
                                     .ToList();

            if (drumCharts.Count == 0)
                return allCharts[0]; // Fallback if no drum charts

            // Map difficulty index to chart (0=easiest, higher=harder)
            int chartIndex = Math.Clamp(currentDifficulty, 0, drumCharts.Count - 1);
            return drumCharts[chartIndex];
        }

        #endregion
    }
}
