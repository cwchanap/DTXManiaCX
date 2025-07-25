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
        private DateTime _songStartTime;

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
            System.Diagnostics.Debug.WriteLine("PerformanceStage: Activating");

            // Extract shared data from stage transition
            ExtractSharedData();

            // Initialize UI components
            InitializeComponents();

            // Start async chart loading and audio preparation
            _ = InitializeGameplayAsync();

            System.Diagnostics.Debug.WriteLine($"PerformanceStage: Activated with song: {_selectedSong?.DisplayTitle ?? "Unknown"}");
        }

        protected override void OnDeactivate()
        {
            System.Diagnostics.Debug.WriteLine("PerformanceStage: Deactivating");

            // Clean up components
            CleanupComponents();
        }

        protected override void OnUpdate(double deltaTime)
        {
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

            System.Diagnostics.Debug.WriteLine("PerformanceStage: Components initialized");
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

            System.Diagnostics.Debug.WriteLine("PerformanceStage: Components cleaned up");
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
                System.Diagnostics.Debug.WriteLine("PerformanceStage: ESC pressed, returning to SongSelect");
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
        /// Initializes chart parsing and audio loading asynchronously
        /// </summary>
        private async Task InitializeGameplayAsync()
        {
            try
            {
                _isLoading = true;
                System.Diagnostics.Debug.WriteLine("PerformanceStage: Starting chart loading...");

                // Get the chart file path from selected song
                var chartPath = _selectedSong?.DatabaseChart?.FilePath;
                if (string.IsNullOrEmpty(chartPath))
                {
                    System.Diagnostics.Debug.WriteLine("PerformanceStage: No chart path available");
                    return;
                }

                // Parse the DTX chart
                _parsedChart = await DTXChartParser.ParseAsync(chartPath);
                System.Diagnostics.Debug.WriteLine($"PerformanceStage: Parsed chart with {_parsedChart.TotalNotes} notes, BPM: {_parsedChart.Bpm}");

                // Create chart manager
                System.Diagnostics.Debug.WriteLine("PerformanceStage: Creating ChartManager...");
                _chartManager = new ChartManager(_parsedChart);
                System.Diagnostics.Debug.WriteLine($"PerformanceStage: ChartManager created successfully: {_chartManager != null}");

                // Set BPM in note renderer
                System.Diagnostics.Debug.WriteLine("PerformanceStage: Setting BPM in note renderer...");
                _noteRenderer?.SetBpm(_parsedChart.Bpm);
                System.Diagnostics.Debug.WriteLine("PerformanceStage: BPM set in note renderer");

                // Load background audio
                System.Diagnostics.Debug.WriteLine("PerformanceStage: Loading background audio...");
                if (!string.IsNullOrEmpty(_parsedChart.BackgroundAudioPath))
                {
                    await _audioLoader.PreloadForChartAsync(chartPath, _parsedChart.BackgroundAudioPath);
                    System.Diagnostics.Debug.WriteLine($"PerformanceStage: Loaded audio: {_parsedChart.BackgroundAudioPath}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("PerformanceStage: No background audio path specified");
                }

                // Create song timer
                System.Diagnostics.Debug.WriteLine("PerformanceStage: Creating SongTimer...");
                _songTimer = _audioLoader.CreateSongTimer();
                System.Diagnostics.Debug.WriteLine($"PerformanceStage: SongTimer created: {_songTimer != null}");

                _isLoading = false;
                _isReady = true;
                System.Diagnostics.Debug.WriteLine("PerformanceStage: Chart loading completed, entering ready state");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PerformanceStage: Failed to initialize gameplay: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"PerformanceStage: Ready countdown: {_readyCountdown:F2}s (deltaTime: {deltaTime:F4}s)");
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
            System.Diagnostics.Debug.WriteLine($"PerformanceStage: StartSong called, _songTimer is {(_songTimer != null ? "not null" : "null")}");
            if (_songTimer != null)
            {
                // Use simplified timing approach for Phase 2
                _songStartTime = DateTime.UtcNow;
                _songTimer.Play(new GameTime(TimeSpan.Zero, TimeSpan.Zero)); // Placeholder GameTime
                _isReady = false;
                System.Diagnostics.Debug.WriteLine($"PerformanceStage: Song started, IsPlaying: {_songTimer.IsPlaying}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("PerformanceStage: Cannot start song - SongTimer is null");
            }
        }

        /// <summary>
        /// Draws scrolling notes
        /// </summary>
        private void DrawNotes()
        {
            if (_noteRenderer == null || _chartManager == null || _songTimer == null)
            {
                System.Diagnostics.Debug.WriteLine($"PerformanceStage: DrawNotes - Missing components: noteRenderer={_noteRenderer != null}, chartManager={_chartManager != null}, songTimer={_songTimer != null}");
                return;
            }

            if (!_songTimer.IsPlaying)
            {
                System.Diagnostics.Debug.WriteLine("PerformanceStage: DrawNotes - SongTimer is not playing");
                return;
            }

            // Get current song time using simplified timing
            var currentTimeMs = (DateTime.UtcNow - _songStartTime).TotalMilliseconds;

            // Get active notes
            var activeNotes = _chartManager.GetActiveNotes(currentTimeMs, 3000.0); // 3 second look-ahead

            var activeNotesList = activeNotes.ToList();
            System.Diagnostics.Debug.WriteLine($"PerformanceStage: DrawNotes - currentTime: {currentTimeMs:F0}ms, activeNotes: {activeNotesList.Count}");

            // Draw the notes
            _noteRenderer.DrawNotes(_spriteBatch, activeNotesList, currentTimeMs);
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
                System.Diagnostics.Debug.WriteLine("PerformanceStage: Ready font initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PerformanceStage: Failed to initialize ready font: {ex.Message}");
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
    }
}
