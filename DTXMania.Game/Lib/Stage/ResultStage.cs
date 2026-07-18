using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game;
using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Stage.Result;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Result stage for displaying performance results after song completion
    /// Shows score, accuracy, combo, and other performance metrics
    /// </summary>
    public class ResultStage : BaseStage, IStageTelemetryProvider
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
        private IFont _smallResultFont;
        private IFont _largeResultFont;
        private IFont _valueResultFont;
        private IFont _countResultFont;
        private ResultScreenModel _resultModel;
        private ResultRevealState _revealState;
        private ResultScreenRenderer _resultRenderer;
        private ISound _resultSound;
        private ISound _newRecordSound;
        private Texture2D _whitePixel;
        private bool _newRecordSoundPlayed;


        // State
        private double _elapsedTime = 0.0;
        
        // Note: Using global stage transition debouncing from BaseGame

        private const string StageClearSoundPath = SoundPath.StageClear;
        private const string FullComboSoundPath = SoundPath.FullCombo;
        private const string ExcellentSoundPath = SoundPath.Excellent;
        private const string NewRecordSoundPath = SoundPath.NewRecord;

        #endregion

        #region Properties

        public override StageType Type => StageType.Result;

        #endregion

        #region Constructor

        public ResultStage(IStageGame game) : base(game)
        {
            // Initialize core systems
            _spriteBatch = CreateSpriteBatch(game.GraphicsDevice);
            _resourceManager = game.ResourceManager;
            _uiManager = new UIManager();
            _inputManager = game.InputManager;
        }

        /// <summary>
        /// Creates the <see cref="SpriteBatch"/> used by this stage. Extracted as a seam so
        /// headless tests can override it (returning null) instead of constructing a real
        /// SpriteBatch, whose internal <see cref="GraphicsResource"/> finalizers crash on an
        /// uninitialized <see cref="GraphicsDevice"/>. Mirrors PerformanceStage.CreateSpriteBatch.
        /// </summary>
        [ExcludeFromCodeCoverage]
        protected virtual SpriteBatch CreateSpriteBatch(GraphicsDevice graphicsDevice)
            => graphicsDevice != null ? new SpriteBatch(graphicsDevice) : null;

        #endregion

        #region BaseStage Implementation

        protected override void OnActivate()
        {
            ExtractSharedData();

            var selectedChart = ResolveSelectedChart();
            var previousScore = ResolvePreviousScore(selectedChart);
            _resultModel = CreateResultScreenModel(
                _performanceSummary,
                _selectedSong,
                _selectedDifficulty,
                selectedChart,
                previousScore);

            PersistPerformanceSummary(selectedChart);

            InitializeComponents();
            _revealState = new ResultRevealState();
            _newRecordSoundPlayed = false;
            PlayResultSound();

            // Clear any pending input commands to prevent auto-transition
            if (_inputManager != null)
            {
                _inputManager.ClearPendingCommands();
                _inputManager.ResetKeyRepeatStates();
            }

            System.Diagnostics.Trace.WriteLine($"ResultStage activated with performance summary: {_performanceSummary}");
        }

        protected override void OnDeactivate()
        {
            // Clean up components
            CleanupComponents();
        }

        protected override void OnUpdate(double deltaTime)
        {
            _elapsedTime += deltaTime;
            _revealState?.Update(deltaTime);
            PlayNewRecordSoundIfReady();

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

            // BaseGame now renders every stage into the fixed 1280×720 virtual render target and
            // letterbox-scales it once to the window, so no per-stage viewport transform is needed
            // here (the previous CreateViewportTransform call was always identity under that model).
            _spriteBatch.Begin();

            if (_resultRenderer != null && _resultModel != null && _revealState != null)
            {
                _resultRenderer.Draw(_spriteBatch, _resultModel, _revealState);
            }
            else
            {
                DrawBackground();
            }

            _spriteBatch.End();
        }

        #endregion

        #region Components

        private void ExtractSharedData()
        {
            _selectedSong = null;
            _selectedDifficulty = 0;

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

            // Initialize fonts and renderer (core visual components)
            try
            {
                _resultFont = CreateResultFont();
                _smallResultFont = CreateSmallResultFont();
                _largeResultFont = CreateLargeResultFont();
                _valueResultFont = CreateValueFont();
                _countResultFont = CreateCountFont();
                _resultRenderer = CreateResultRenderer();
                _resultRenderer?.Load(_resultModel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ResultStage: Failed to initialize result renderer: {ex.Message}");
                _resultFont?.RemoveReference();
                _smallResultFont?.RemoveReference();
                _largeResultFont?.RemoveReference();
                _valueResultFont?.RemoveReference();
                _countResultFont?.RemoveReference();
                _resultRenderer?.Dispose();
                _resultFont = null;
                _smallResultFont = null;
                _largeResultFont = null;
                _valueResultFont = null;
                _countResultFont = null;
                _resultRenderer = null;
            }

            // Initialize sounds (optional — failure should not affect visuals)
            try
            {
                _resultSound = LoadSoundForPlate(_resultModel?.PlateKind ?? ResultPlateKind.StageCleared);
                _newRecordSound = _resultModel?.NewRecord == true ? TryLoadSound(NewRecordSoundPath) : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ResultStage: Failed to load result sounds: {ex.Message}");
                _resultSound = null;
                _newRecordSound = null;
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
            var theme = _resourceManager.CurrentTheme ?? SkinTheme.Empty;
            return _resourceManager.LoadFont(
                "NotoSerifJP", ResultScreenRenderer.ResolveNormalFontSize(theme));
        }

        [ExcludeFromCodeCoverage]
        internal virtual IFont CreateSmallResultFont()
        {
            return _resourceManager.LoadFont("NotoSerifJP", ResultUILayout.Fonts.Small);
        }

        [ExcludeFromCodeCoverage]
        internal virtual IFont CreateLargeResultFont()
        {
            var theme = _resourceManager.CurrentTheme ?? SkinTheme.Empty;
            return _resourceManager.LoadFont(
                "NotoSerifJP", ResultScreenRenderer.ResolveLargeFontSize(theme), FontStyle.Bold);
        }

        /// <summary>
        /// Optional Latin display font for numeric values; null (NX) keeps the
        /// serif fonts for every text.
        /// </summary>
        [ExcludeFromCodeCoverage]
        internal virtual IFont CreateValueFont()
        {
            var theme = _resourceManager.CurrentTheme ?? SkinTheme.Empty;
            var family = ResultScreenRenderer.ResolveValueFontFamily(theme);
            return family.Length == 0
                ? null
                : _resourceManager.LoadFont(family, ResultScreenRenderer.ResolveValueFontSize(theme));
        }

        /// <summary>
        /// Smaller companion of <see cref="CreateValueFont"/> for judgement
        /// counts and percentages.
        /// </summary>
        [ExcludeFromCodeCoverage]
        internal virtual IFont CreateCountFont()
        {
            var theme = _resourceManager.CurrentTheme ?? SkinTheme.Empty;
            var family = ResultScreenRenderer.ResolveValueFontFamily(theme);
            return family.Length == 0
                ? null
                : _resourceManager.LoadFont(family, ResultScreenRenderer.ResolveCountFontSize(theme));
        }

        internal virtual ResultScreenRenderer CreateResultRenderer()
        {
            return new ResultScreenRenderer(_resourceManager, _smallResultFont, _resultFont, _largeResultFont,
                _valueResultFont, _countResultFont);
        }

        private void CleanupComponents()
        {
            // Cleanup resources
            _whitePixel?.Dispose();
            _whitePixel = null;
            _resultFont?.RemoveReference();
            _resultFont = null;
            _smallResultFont?.RemoveReference();
            _smallResultFont = null;
            _valueResultFont?.RemoveReference();
            _valueResultFont = null;
            _countResultFont?.RemoveReference();
            _countResultFont = null;
            _largeResultFont?.RemoveReference();
            _largeResultFont = null;
            _resultRenderer?.Dispose();
            _resultRenderer = null;
            _resultSound?.RemoveReference();
            _resultSound = null;
            _newRecordSound?.RemoveReference();
            _newRecordSound = null;
        }

        internal virtual SongChart ResolveSelectedChart()
        {
            return _selectedSong?.GetCurrentDifficultyChart(_selectedDifficulty);
        }

        internal virtual SongScore ResolvePreviousScore(SongChart selectedChart)
        {
            if (_selectedSong?.Scores != null && selectedChart != null)
            {
                foreach (var score in _selectedSong.Scores)
                {
                    if (score?.ChartId == selectedChart.Id && selectedChart.Id != 0)
                        return score;
                }
            }

            return _selectedSong?.GetScore(_selectedDifficulty);
        }

        internal virtual ResultScreenModel CreateResultScreenModel(
            PerformanceSummary summary,
            DTXMania.Game.Lib.Song.SongListNode selectedSong,
            int selectedDifficulty,
            SongChart selectedChart,
            SongScore previousScore)
        {
            return ResultScreenModel.Create(summary, selectedSong, selectedDifficulty, selectedChart, previousScore);
        }

        private void PersistPerformanceSummary(SongChart selectedChart)
        {
            if (selectedChart == null || selectedChart.Id <= 0 || _performanceSummary == null)
                return;

            _ = SongManager.Instance.UpdateScoreAsync(
                    selectedChart.Id,
                    EInstrumentPart.DRUMS,
                    _performanceSummary)
                .ContinueWith(
                    task => System.Diagnostics.Trace.WriteLine($"ResultStage: Failed to persist score: {task.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
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
                if (ExecuteInputCommand(command.Value))
                {
                    _inputManager.ClearPendingCommands();
                    break;
                }
            }
        }

        private bool ExecuteInputCommand(InputCommand command)
        {
            switch (command.Type)
            {
                case InputCommandType.Activate:
                case InputCommandType.Back:
                    if (_revealState != null && !_revealState.IsComplete)
                    {
                        _revealState.Complete();
                        PlayNewRecordSoundIfReady();
                        return true;
                    }

                    if (_game.CanPerformStageTransition())
                    {
                        _game.MarkStageTransition();
                        ReturnToSongSelect();
                    }
                    break;
            }

            return false;
        }

        private void ReturnToSongSelect()
        {
            // Return to song selection stage
            StageManager?.ChangeStage(StageType.SongSelect,
                new DTXManiaFadeTransition(0.5), null);
        }

        #endregion

        #region Sound

        private ISound LoadSoundForPlate(ResultPlateKind plateKind)
        {
            return plateKind switch
            {
                ResultPlateKind.Excellent => TryLoadSound(ExcellentSoundPath),
                ResultPlateKind.FullCombo => TryLoadSound(FullComboSoundPath),
                _ => TryLoadSound(StageClearSoundPath)
            };
        }

        private ISound TryLoadSound(string path)
        {
            if (_resourceManager == null || string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                if (!_resourceManager.ResourceExists(path))
                    return null;

                return _resourceManager.LoadSound(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ResultStage: Failed to load sound '{path}': {ex.Message}");
                return null;
            }
        }

        private void PlayResultSound()
        {
            _resultSound?.Play();
        }

        private void PlayNewRecordSoundIfReady()
        {
            if (_newRecordSoundPlayed || _resultModel?.NewRecord != true || _revealState?.IsComplete != true)
                return;

            _newRecordSound?.Play();
            _newRecordSoundPlayed = true;
        }

        #endregion

        #region Drawing

        [ExcludeFromCodeCoverage]
        private void DrawBackground()
        {
            // Draw DTXManiaNX authentic background graphics (8_background.jpg)
            DrawStageBackground(_spriteBatch);

            // Draw fallback if no background loaded.
            // BaseGame renders every stage into the fixed 1280x720 virtual render target and
            // letterbox-scales it once to the window, so author the fallback rect in virtual
            // coords (no per-stage viewport transform is applied here).
            if (!IsBackgroundReady)
            {
                var backgroundRect = new Rectangle(0, 0,
                    ResultUILayout.NXViewport.Width, ResultUILayout.NXViewport.Height);
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

        #endregion

        #region Telemetry

        public void PopulateTelemetry(GameTelemetrySnapshot telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);

            telemetry.SelectedSongTitle = _selectedSong?.DisplayTitle ?? _selectedSong?.Title;
            telemetry.SelectedDifficulty = _selectedDifficulty;

            if (_performanceSummary != null)
            {
                telemetry.ApplyPerformanceSummary(_performanceSummary);
                telemetry.StageCompleted = true;
            }
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
