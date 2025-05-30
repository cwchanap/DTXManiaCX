using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Stage;
using DTX.Resources;
using DTX.Services;
using DTX.Config;
using DTXMania.Shared.Game;
using System;
using System.Collections.Generic;
using System.IO;

namespace DTX.Stage
{
    /// <summary>
    /// Startup stage implementation based on DTXManiaNX CStageStartup
    /// Handles initial loading and displays progress information
    /// </summary>
    public class StartupStage : IStage
    {
        #region Fields

        private readonly BaseGame _game;
        private double _elapsedTime;
        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private ITexture _backgroundTexture;
        private IResourceManager _resourceManager;
        private bool _disposed = false;

        // DTXMania pattern: progress tracking
        private readonly List<string> _progressMessages;
        private string _currentProgressMessage = "";
        private StartupPhase _currentPhase = StartupPhase.SystemSounds;
        private bool _isFirstUpdate = true;

        // Services for actual functionality
        private SongEnumerationService _songEnumerationService;
        private ConfigurationValidator _configValidator;
        private ConfigManager _configManager;

        // Loading simulation (since we don't have actual song loading yet)
        private readonly Dictionary<StartupPhase, (string message, double duration)> _phaseInfo;
        private double _phaseStartTime;

        #endregion

        #region Properties

        public StageType Type => StageType.Startup;

        #endregion

        #region Constructor

        public StartupStage(BaseGame game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));

            _progressMessages = new List<string>();

            // Initialize services
            _songEnumerationService = new SongEnumerationService();
            _configValidator = new ConfigurationValidator();
            _configManager = new ConfigManager();

            // Initialize phase information (based on DTXManiaNX phases)
            _phaseInfo = new Dictionary<StartupPhase, (string, double)>
            {
                { StartupPhase.SystemSounds, ("Loading system sounds...", 0.5) },
                { StartupPhase.ConfigValidation, ("Validating configuration...", 0.3) },
                { StartupPhase.SongListDB, ("Loading songlist.db...", 0.3) },
                { StartupPhase.SongsDB, ("Loading songs.db...", 0.4) },
                { StartupPhase.EnumerateSongs, ("Enumerating songs...", 1.5) },
                { StartupPhase.LoadScoreCache, ("Loading score properties from songs.db...", 0.6) },
                { StartupPhase.LoadScoreFiles, ("Loading score properties from files...", 0.7) },
                { StartupPhase.BuildSongLists, ("Building songlists...", 0.3) },
                { StartupPhase.SaveSongsDB, ("Saving songs.db...", 0.2) },
                { StartupPhase.Complete, ("Setup done.", 0.1) }
            };
        }

        #endregion

        #region IStage Implementation

        public void Activate()
        {
            System.Diagnostics.Debug.WriteLine("Activating Startup Stage");

            // Initialize graphics resources
            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Initialize ResourceManager
            _resourceManager = new ResourceManager(graphicsDevice);

            // Load background texture (DTXManiaNX uses 1_background.jpg)
            LoadBackgroundTexture();

            // Initialize state
            _elapsedTime = 0;
            _isFirstUpdate = true;
            _currentPhase = StartupPhase.SystemSounds;
            _phaseStartTime = 0;
            _progressMessages.Clear();

            // Add initial messages (DTXMania pattern)
            _progressMessages.Add("DTXMania powered by YAMAHA Silent Session Drums");
            _progressMessages.Add($"Release: DTXManiaCX v1.0.0 [MonoGame Edition]");

            System.Diagnostics.Debug.WriteLine("Startup Stage activated successfully");
        }

        public void Update(double deltaTime)
        {
            _elapsedTime += deltaTime;

            // Handle first update
            if (_isFirstUpdate)
            {
                _isFirstUpdate = false;
                _phaseStartTime = _elapsedTime;
            }

            // Update current phase
            UpdateCurrentPhase();

            // Check if all phases are complete
            if (_currentPhase == StartupPhase.Complete)
            {
                double phaseElapsed = _elapsedTime - _phaseStartTime;
                if (phaseElapsed >= _phaseInfo[_currentPhase].duration)
                {
                    // Transition to Title stage (proper flow)
                    _game.StageManager?.ChangeStage(StageType.Title);
                }
            }
        }

        public void Draw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);

            // Draw background
            DrawBackground();

            // Draw progress messages (DTXMania pattern)
            DrawProgressMessages();

            // Draw current progress
            DrawCurrentProgress();

            _spriteBatch.End();
        }

        public void Deactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating Startup Stage");

            // Reset stage state for potential reactivation
            _elapsedTime = 0;
            _isFirstUpdate = true;
            _currentPhase = StartupPhase.SystemSounds;
            _phaseStartTime = 0;
            _progressMessages.Clear();
            _currentProgressMessage = "";
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    System.Diagnostics.Debug.WriteLine("Disposing Startup Stage resources");

                    // Cleanup MonoGame resources
                    _backgroundTexture?.Dispose();
                    _whitePixel?.Dispose();
                    _spriteBatch?.Dispose();
                    _resourceManager?.Dispose();

                    _backgroundTexture = null;
                    _whitePixel = null;
                    _spriteBatch = null;
                    _resourceManager = null;
                }
                _disposed = true;
            }
        }

        #endregion

        #region Private Methods - Resource Loading

        private void LoadBackgroundTexture()
        {
            try
            {
                // Use ResourceManager to load background texture with proper skin path resolution
                _backgroundTexture = _resourceManager.LoadTexture("Graphics/1_background.jpg");
                System.Diagnostics.Debug.WriteLine("Loaded startup background using ResourceManager");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load startup background: {ex.Message}");
                // ResourceManager will handle fallback automatically, so _backgroundTexture should still be valid
            }
        }



        #endregion

        #region Private Methods - Update Logic

        private void UpdateCurrentPhase()
        {
            if (_currentPhase == StartupPhase.Complete)
                return;

            double phaseElapsed = _elapsedTime - _phaseStartTime;
            var currentPhaseInfo = _phaseInfo[_currentPhase];

            // Update current progress message
            _currentProgressMessage = currentPhaseInfo.message;

            // Perform phase-specific operations
            PerformPhaseOperation(_currentPhase, phaseElapsed);

            // Check if current phase is complete
            if (phaseElapsed >= currentPhaseInfo.duration)
            {
                // Add completion message
                _progressMessages.Add($"{currentPhaseInfo.message} OK");

                // Move to next phase
                var nextPhase = GetNextPhase(_currentPhase);
                if (nextPhase != _currentPhase)
                {
                    _currentPhase = nextPhase;
                    _phaseStartTime = _elapsedTime;
                    System.Diagnostics.Debug.WriteLine($"Startup phase changed to: {_currentPhase}");
                }
            }
        }

        private void PerformPhaseOperation(StartupPhase phase, double phaseElapsed)
        {
            // Only perform operation once per phase (at the beginning)
            if (phaseElapsed > 0.1) return;

            switch (phase)
            {
                case StartupPhase.SystemSounds:
                    // Load system sounds (placeholder)
                    System.Diagnostics.Debug.WriteLine("Loading system sounds...");
                    break;

                case StartupPhase.ConfigValidation:
                    // Validate configuration
                    try
                    {
                        _configManager.LoadConfig("config.json");
                        var config = _configManager.Config;
                        var isValid = _configValidator.ValidateConfiguration(config);
                        System.Diagnostics.Debug.WriteLine($"Configuration validation: {(isValid ? "PASSED" : "FAILED")}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Configuration validation error: {ex.Message}");
                    }
                    break;

                case StartupPhase.EnumerateSongs:
                    // Start song enumeration
                    try
                    {
                        var songPaths = new[] { "Songs", "DTX", "Music" };
                        _ = _songEnumerationService.EnumerateSongsAsync(songPaths);
                        System.Diagnostics.Debug.WriteLine("Started song enumeration...");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Song enumeration error: {ex.Message}");
                    }
                    break;

                case StartupPhase.SongListDB:
                case StartupPhase.SongsDB:
                case StartupPhase.LoadScoreCache:
                case StartupPhase.LoadScoreFiles:
                case StartupPhase.BuildSongLists:
                case StartupPhase.SaveSongsDB:
                    // Placeholder operations for other phases
                    System.Diagnostics.Debug.WriteLine($"Performing {phase} operation...");
                    break;
            }
        }

        private StartupPhase GetNextPhase(StartupPhase currentPhase)
        {
            return currentPhase switch
            {
                StartupPhase.SystemSounds => StartupPhase.ConfigValidation,
                StartupPhase.ConfigValidation => StartupPhase.SongListDB,
                StartupPhase.SongListDB => StartupPhase.SongsDB,
                StartupPhase.SongsDB => StartupPhase.EnumerateSongs,
                StartupPhase.EnumerateSongs => StartupPhase.LoadScoreCache,
                StartupPhase.LoadScoreCache => StartupPhase.LoadScoreFiles,
                StartupPhase.LoadScoreFiles => StartupPhase.BuildSongLists,
                StartupPhase.BuildSongLists => StartupPhase.SaveSongsDB,
                StartupPhase.SaveSongsDB => StartupPhase.Complete,
                _ => StartupPhase.Complete
            };
        }

        #endregion

        #region Private Methods - Drawing

        private void DrawBackground()
        {
            if (_backgroundTexture != null)
            {
                var viewport = _game.GraphicsDevice.Viewport;
                _spriteBatch.Draw(_backgroundTexture.Texture,
                    new Rectangle(0, 0, viewport.Width, viewport.Height),
                    Color.White);
            }
            else if (_whitePixel != null)
            {
                // Fallback: draw solid dark background
                var viewport = _game.GraphicsDevice.Viewport;
                _spriteBatch.Draw(_whitePixel,
                    new Rectangle(0, 0, viewport.Width, viewport.Height),
                    new Color(16, 16, 32));
            }
        }

        private void DrawProgressMessages()
        {
            if (_whitePixel == null)
                return;

            // Draw progress messages (DTXMania pattern)
            int x = 10;
            int y = 10;
            const int lineHeight = 14;

            lock (_progressMessages)
            {
                foreach (string message in _progressMessages)
                {
                    // Draw text as rectangles (since we don't have fonts yet)
                    DrawTextRect(x, y, message.Length * 8, 12, Color.White);
                    y += lineHeight;
                }

                // Draw current progress message
                if (!string.IsNullOrEmpty(_currentProgressMessage))
                {
                    DrawTextRect(x, y, _currentProgressMessage.Length * 8, 12, Color.Yellow);
                }
            }
        }

        private void DrawCurrentProgress()
        {
            if (_whitePixel == null)
                return;

            // Calculate overall progress
            int totalPhases = _phaseInfo.Count;
            int currentPhaseIndex = (int)_currentPhase;
            double phaseElapsed = _elapsedTime - _phaseStartTime;
            double currentPhaseDuration = _phaseInfo[_currentPhase].duration;
            double phaseProgress = Math.Min(phaseElapsed / currentPhaseDuration, 1.0);

            double overallProgress = (currentPhaseIndex + phaseProgress) / totalPhases;

            // Draw progress bar
            const int progressBarX = 10;
            const int progressBarY = 200;
            const int progressBarWidth = 400;
            const int progressBarHeight = 20;

            // Background
            DrawTextRect(progressBarX, progressBarY, progressBarWidth, progressBarHeight, Color.DarkGray);

            // Progress
            int progressWidth = (int)(progressBarWidth * overallProgress);
            DrawTextRect(progressBarX, progressBarY, progressWidth, progressBarHeight, Color.LightGreen);

            // Progress percentage
            string progressText = $"{overallProgress * 100:F1}%";
            DrawTextRect(progressBarX + progressBarWidth + 10, progressBarY + 2, progressText.Length * 8, 16, Color.White);
        }

        private void DrawTextRect(int x, int y, int width, int height, Color color)
        {
            if (_whitePixel != null)
            {
                _spriteBatch.Draw(_whitePixel, new Rectangle(x, y, width, height), color);
            }
        }

        #endregion

        #region Enums

        private enum StartupPhase
        {
            SystemSounds = 0,
            ConfigValidation = 1,
            SongListDB = 2,
            SongsDB = 3,
            EnumerateSongs = 4,
            LoadScoreCache = 5,
            LoadScoreFiles = 6,
            BuildSongLists = 7,
            SaveSongsDB = 8,
            Complete = 9
        }

        #endregion
    }
}