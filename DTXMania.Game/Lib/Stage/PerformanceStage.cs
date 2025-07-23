using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game;
using DTX.Resources;
using DTX.UI;
using DTX.Input;
using DTX.Song;
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

            // Initialize UI components (placeholder for now)
            InitializeComponents();

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

            // Update performance components (placeholder)
            UpdateComponents(deltaTime);
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin();

            // Draw components in proper order:
            // Background → Lane Backgrounds → Judgement Line → Gauge → Score/Combo

            // Draw background (placeholder - solid color for now)
            DrawBackground();

            // Draw lane backgrounds (placeholder)
            DrawLaneBackgrounds();

            // Draw judgement line (placeholder)
            DrawJudgementLine();

            // Draw UI elements
            DrawUIElements();

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
            _laneBackgroundRenderer = new LaneBackgroundRenderer(graphicsDevice);
            _judgementLineRenderer = new JudgementLineRenderer(graphicsDevice);

            // Initialize score and combo displays
            _scoreDisplay = new ScoreDisplay(_resourceManager, graphicsDevice);
            _comboDisplay = new ComboDisplay(_resourceManager, graphicsDevice);

            // Initialize gauge display
            _gaugeDisplay = new GaugeDisplay(_resourceManager, graphicsDevice);

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
