using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game;
using DTX.Resources;
using DTX.UI;
using DTX.Input;
using DTXMania.Game.Lib.Input;
using DTX.Stage.Performance;

namespace DTX.Stage
{
    /// <summary>
    /// Result stage for displaying performance results after song completion
    /// Shows score, accuracy, combo, and other performance metrics
    /// </summary>
    public class ResultStage : BaseStage
    {
        #region Private Fields

        private SpriteBatch _spriteBatch;
        private ResourceManager _resourceManager;
        private UIManager _uiManager;
        private InputManager _inputManager;

        // Result data
        private PerformanceSummary _performanceSummary;

        // UI components
        private BitmapFont _resultFont;
        private Texture2D _whitePixel;

        // State
        private double _elapsedTime = 0.0;

        #endregion

        #region Properties

        public override StageType Type => StageType.Result;

        #endregion

        #region Constructor

        public ResultStage(BaseGame game) : base(game)
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
            // Extract performance summary from shared data
            ExtractSharedData();

            // Initialize UI components
            InitializeComponents();

            System.Diagnostics.Debug.WriteLine($"ResultStage activated with performance summary: {_performanceSummary}");
        }

        protected override void OnDeactivate()
        {
            // Clean up components
            CleanupComponents();
        }

        protected override void OnUpdate(double deltaTime)
        {
            _elapsedTime += deltaTime;

            // Update input manager
            _inputManager?.Update(deltaTime);

            // Handle input
            HandleInput();

            // Update UI manager
            _uiManager?.Update(deltaTime);
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin();

            // Draw background
            DrawBackground();

            // Draw result information
            DrawResults();

            _spriteBatch.End();
        }

        #endregion

        #region Components

        private void ExtractSharedData()
        {
            if (_sharedData != null && _sharedData.TryGetValue("performanceSummary", out var summaryObj) && summaryObj is PerformanceSummary summary)
            {
                _performanceSummary = summary;
            }
            else
            {
                // Fallback: create default summary if none provided
                _performanceSummary = new PerformanceSummary
                {
                    Score = 0,
                    MaxCombo = 0,
                    ClearFlag = false,
                    CompletionReason = CompletionReason.Unknown
                };
                System.Diagnostics.Debug.WriteLine("ResultStage: No performance summary provided, using default");
            }
        }

        private void InitializeComponents()
        {
            // Create white pixel texture for backgrounds
            _whitePixel = new Texture2D(_spriteBatch.GraphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Initialize font for text display
            try
            {
                var consoleFontConfig = BitmapFont.CreateConsoleFontConfig();
                _resultFont = new BitmapFont(_spriteBatch.GraphicsDevice, _resourceManager, consoleFontConfig);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResultStage: Failed to load font: {ex.Message}");
                _resultFont = null;
            }
        }

        private void CleanupComponents()
        {
            // Cleanup resources
            _whitePixel?.Dispose();
            _whitePixel = null;
            _resultFont?.Dispose();
            _resultFont = null;
        }

        #endregion

        #region Input Handling

        private void HandleInput()
        {
            if (_inputManager == null)
                return;

            // Check for back action (ESC key or controller Back button) using consolidated method
            // Also handle Enter key for convenience
            if (_inputManager.IsBackActionTriggered() || _inputManager.IsKeyPressed((int)Keys.Enter))
            {
                ReturnToSongSelect();
            }
        }

        private void ReturnToSongSelect()
        {
            // Return to song selection stage
            StageManager?.ChangeStage(StageType.SongSelect,
                new DTXManiaFadeTransition(0.5), null);
        }

        #endregion

        #region Drawing

        private void DrawBackground()
        {
            // Draw a simple background
            var viewport = _spriteBatch.GraphicsDevice.Viewport;
            var backgroundRect = new Rectangle(0, 0, viewport.Width, viewport.Height);
            var backgroundColor = Color.DarkBlue * 0.8f;

            if (_whitePixel != null)
            {
                _spriteBatch.Draw(_whitePixel, backgroundRect, backgroundColor);
            }
        }

        private void DrawResults()
        {
            if (_performanceSummary == null)
                return;

            var viewport = _spriteBatch.GraphicsDevice.Viewport;
            var centerX = viewport.Width / 2;
            var startY = 100;
            var lineHeight = 40;
            var currentY = startY;

            // Draw performance results
            DrawResultLine("PERFORMANCE RESULTS", centerX, ref currentY, Color.Yellow, lineHeight);
            currentY += lineHeight / 2; // Extra space

            var clearText = _performanceSummary.ClearFlag ? "CLEARED" : "FAILED";
            var clearColor = _performanceSummary.ClearFlag ? Color.Green : Color.Red;
            DrawResultLine(clearText, centerX, ref currentY, clearColor, lineHeight);

            DrawResultLine($"Score: {_performanceSummary.Score:N0}", centerX, ref currentY, Color.White, lineHeight);
            DrawResultLine($"Max Combo: {_performanceSummary.MaxCombo}", centerX, ref currentY, Color.White, lineHeight);
            DrawResultLine($"Accuracy: {_performanceSummary.Accuracy:F1}%", centerX, ref currentY, Color.White, lineHeight);

            currentY += lineHeight / 2; // Extra space

            DrawResultLine("JUDGEMENT BREAKDOWN", centerX, ref currentY, Color.Cyan, lineHeight);
            DrawResultLine($"Just: {_performanceSummary.JustCount}", centerX, ref currentY, Color.White, lineHeight);
            DrawResultLine($"Great: {_performanceSummary.GreatCount}", centerX, ref currentY, Color.White, lineHeight);
            DrawResultLine($"Good: {_performanceSummary.GoodCount}", centerX, ref currentY, Color.White, lineHeight);
            DrawResultLine($"Poor: {_performanceSummary.PoorCount}", centerX, ref currentY, Color.White, lineHeight);
            DrawResultLine($"Miss: {_performanceSummary.MissCount}", centerX, ref currentY, Color.White, lineHeight);

            currentY += lineHeight; // Extra space

            DrawResultLine("Press ESC or ENTER to continue", centerX, ref currentY, Color.Gray, lineHeight);
        }

        private void DrawResultLine(string text, int centerX, ref int currentY, Color color, int lineHeight)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (_resultFont?.IsLoaded == true)
            {
                // Use bitmap font if available
                var textSize = _resultFont.MeasureText(text);
                var textPosition = new Vector2(centerX - textSize.X / 2, currentY);
                _resultFont.DrawText(_spriteBatch, text, (int)textPosition.X, (int)textPosition.Y, color);
            }
            else
            {
                // Fallback: draw colored rectangle as placeholder
                var rectWidth = text.Length * 8;
                var rectHeight = 20;
                var rectPosition = new Rectangle(centerX - rectWidth / 2, currentY, rectWidth, rectHeight);

                if (_whitePixel != null)
                {
                    _spriteBatch.Draw(_whitePixel, rectPosition, color);
                }
            }

            currentY += lineHeight;
        }

        #endregion

        #region Disposal

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _spriteBatch?.Dispose();
                _uiManager?.Dispose();
                CleanupComponents();
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}
