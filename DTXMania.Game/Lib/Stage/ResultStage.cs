using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage.Performance;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Result stage for displaying performance results after song completion
    /// Shows score, accuracy, combo, and other performance metrics
    /// </summary>
    public class ResultStage : BaseStage
    {
        #region Private Fields

        private SpriteBatch _spriteBatch;
        private IResourceManager _resourceManager;
        private UIManager _uiManager;
        private InputManager _inputManager;

        // Result data
        private PerformanceSummary _performanceSummary;
        private DTXMania.Game.Lib.Song.SongListNode? _selectedSong;
        private int _selectedDifficulty;

        // UI components
        private IFont _resultFont;
        private Texture2D _whitePixel;


        // State
        private double _elapsedTime = 0.0;
        
        // Note: Using global stage transition debouncing from BaseGame

        #endregion

        #region Properties

        public override StageType Type => StageType.Result;

        #endregion

        #region Constructor

        public ResultStage(BaseGame game) : base(game)
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
            // Extract performance summary from shared data
            ExtractSharedData();

            // Persist this play's score + skill values (fire-and-forget).
            if (_selectedSong != null && _performanceSummary != null)
            {
                var savedChart = _selectedSong.GetCurrentDifficultyChart(_selectedDifficulty);
                if (savedChart != null && savedChart.Id > 0)
                {
                    _ = DTXMania.Game.Lib.Song.SongManager.Instance.UpdateScoreAsync(
                        savedChart.Id,
                        DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS,
                        _performanceSummary);
                }
            }

            // Initialize UI components
            InitializeComponents();

            // Clear any pending input commands to prevent auto-transition
            if (_inputManager != null)
            {
                _inputManager.GetInputCommands(); // This clears the queue
            }

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

        [ExcludeFromCodeCoverage]
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

            if (_sharedData != null
                && _sharedData.TryGetValue("selectedSong", out var songObj)
                && songObj is DTXMania.Game.Lib.Song.SongListNode song)
            {
                _selectedSong = song;
            }
            if (_sharedData != null
                && _sharedData.TryGetValue("selectedDifficulty", out var difficultyObj)
                && difficultyObj is int difficulty)
            {
                _selectedDifficulty = difficulty;
            }
        }

        private void InitializeComponents()
        {
            // Create white pixel texture for backgrounds
            _whitePixel = CreateWhitePixel();

            // Initialize font for text display
            try
            {
                _resultFont = CreateResultFont();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResultStage: Failed to load font: {ex.Message}");
                _resultFont = null;
            }
        }

        [ExcludeFromCodeCoverage]
        internal virtual Texture2D CreateWhitePixel()
        {
            var whitePixel = new Texture2D(_spriteBatch.GraphicsDevice, 1, 1);
            whitePixel.SetData(new[] { Color.White });
            return whitePixel;
        }

        [ExcludeFromCodeCoverage]
        internal virtual IFont CreateResultFont()
        {
            return _resourceManager.LoadFont("NotoSerifJP", 14);
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

            // Process queued input commands from InputManager
            InputCommand? command;
            while ((command = _inputManager.GetNextCommand()) != null)
            {
                ExecuteInputCommand(command.Value);
            }
        }

        private void ExecuteInputCommand(InputCommand command)
        {
            switch (command.Type)
            {
                case InputCommandType.Activate:
                case InputCommandType.Back:
                    if (_game is BaseGame baseGame && baseGame.CanPerformStageTransition())
                    {
                        baseGame.MarkStageTransition();
                        ReturnToSongSelect();
                    }
                    break;
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

        [ExcludeFromCodeCoverage]
        private void DrawBackground()
        {
            var viewport = GetBackgroundViewport();

            // Draw DTXManiaNX authentic background graphics (8_background.jpg)
            DrawStageBackground(_spriteBatch);

            // Draw fallback if no background loaded
            if (!IsBackgroundReady)
            {
                var backgroundRect = new Rectangle(0, 0, viewport.Width, viewport.Height);
                var backgroundColor = ResultUILayout.Background.BackgroundColor;

                DrawFallbackBackground(backgroundRect, backgroundColor);
            }
        }

        [ExcludeFromCodeCoverage]
        internal virtual Viewport GetBackgroundViewport()
        {
            return _spriteBatch.GraphicsDevice.Viewport;
        }

        internal virtual void DrawFallbackBackground(Rectangle backgroundRect, Color backgroundColor)
        {
            if (_whitePixel != null)
            {
                DrawTexture(_whitePixel, backgroundRect, backgroundColor);
            }
        }

        [ExcludeFromCodeCoverage]
        internal virtual void DrawTexture(Texture2D texture, Rectangle destinationRectangle, Color color)
        {
            _spriteBatch.Draw(texture, destinationRectangle, color);
        }

        private void DrawResults()
        {
            if (_performanceSummary == null)
                return;

            var viewport = _spriteBatch.GraphicsDevice.Viewport;
            var centerX = viewport.Width / 2;
            var startY = ResultUILayout.ResultDisplay.StartY;
            var lineHeight = ResultUILayout.ResultDisplay.LineHeight;
            var currentY = startY;

            // Draw performance results
            DrawResultLine("PERFORMANCE RESULTS", centerX, ref currentY, ResultUILayout.ResultDisplay.TitleColor, lineHeight);
            currentY += ResultUILayout.ResultDisplay.ExtraSpacing; // Extra space

            var clearText = _performanceSummary.ClearFlag ? "CLEARED" : "FAILED";
            var clearColor = _performanceSummary.ClearFlag ? ResultUILayout.ResultDisplay.ClearedColor : ResultUILayout.ResultDisplay.FailedColor;
            DrawResultLine(clearText, centerX, ref currentY, clearColor, lineHeight);

            DrawResultLine($"Score: {_performanceSummary.Score:N0}", centerX, ref currentY, ResultUILayout.ResultDisplay.NormalTextColor, lineHeight);
            DrawResultLine($"Max Combo: {_performanceSummary.MaxCombo}", centerX, ref currentY, ResultUILayout.ResultDisplay.NormalTextColor, lineHeight);
            DrawResultLine($"Accuracy: {_performanceSummary.Accuracy:F1}%", centerX, ref currentY, ResultUILayout.ResultDisplay.NormalTextColor, lineHeight);

            currentY += ResultUILayout.ResultDisplay.ExtraSpacing; // Extra space

            DrawResultLine("JUDGEMENT BREAKDOWN", centerX, ref currentY, ResultUILayout.ResultDisplay.SectionHeaderColor, lineHeight);
            DrawResultLine($"Perfect: {_performanceSummary.PerfectCount}", centerX, ref currentY, ResultUILayout.ResultDisplay.NormalTextColor, lineHeight);
            DrawResultLine($"Great: {_performanceSummary.GreatCount}", centerX, ref currentY, ResultUILayout.ResultDisplay.NormalTextColor, lineHeight);
            DrawResultLine($"Good: {_performanceSummary.GoodCount}", centerX, ref currentY, ResultUILayout.ResultDisplay.NormalTextColor, lineHeight);
            DrawResultLine($"Poor: {_performanceSummary.PoorCount}", centerX, ref currentY, ResultUILayout.ResultDisplay.NormalTextColor, lineHeight);
            DrawResultLine($"Miss: {_performanceSummary.MissCount}", centerX, ref currentY, ResultUILayout.ResultDisplay.NormalTextColor, lineHeight);

            currentY += lineHeight; // Extra space

            DrawResultLine("Press ESC or ENTER to continue", centerX, ref currentY, ResultUILayout.ResultDisplay.InstructionTextColor, lineHeight);
        }

        private void DrawResultLine(string text, int centerX, ref int currentY, Color color, int lineHeight)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (_resultFont != null)
            {
                var textSize = _resultFont.MeasureString(text);
                var textPosition = new Vector2(centerX - textSize.X / 2, currentY);
                _resultFont.DrawString(_spriteBatch, text, textPosition, color);
            }
            else
            {
                // Fallback: draw colored rectangle as placeholder
                var rectWidth = text.Length * ResultUILayout.FallbackText.CharacterWidth;
                var rectHeight = ResultUILayout.FallbackText.RectHeight;
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
