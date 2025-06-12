using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Stage;
using DTX.Resources;
using DTX.Song;
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
    public class StartupStage : BaseStage
    {
        #region Constants

        // UI Layout Constants
        private const int MARGIN_EDGE = 10;
        private const int MARGIN_TOP = 2;
        private const int LINE_HEIGHT = 18;
        private const int FALLBACK_CHAR_WIDTH = 8;
        private const int FALLBACK_FONT_HEIGHT = 16;
        private const int FALLBACK_SMALL_FONT_HEIGHT = 12;

        // Progress Bar Constants
        private const int PROGRESS_BAR_WIDTH = 400;
        private const int PROGRESS_BAR_HEIGHT = 20;
        private const int PROGRESS_BAR_BOTTOM_MARGIN = 120;

        #endregion

        #region Fields

        private double _elapsedTime;
        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private ITexture _backgroundTexture;
        private IResourceManager _resourceManager;
        private BitmapFont _bitmapFont;

        // DTXMania pattern: progress tracking
        private readonly List<string> _progressMessages;
        private string _currentProgressMessage = "";
        private StartupPhase _startupPhase = StartupPhase.SystemSounds;

        // Services for actual functionality
        private SongManager _songManager;
        private ConfigManager _configManager;

        // Loading simulation (since we don't have actual song loading yet)
        private readonly Dictionary<StartupPhase, (string message, double duration)> _phaseInfo;
        private double _phaseStartTime;

        #endregion

        #region Properties

        public override StageType Type => StageType.Startup;

        #endregion

        #region Constructor

        public StartupStage(BaseGame game) : base(game)
        {
            _progressMessages = new List<string>();

            // Initialize services
            _songManager = new SongManager();
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

        #region BaseStage Implementation

        protected override void OnActivate()
        {
            System.Diagnostics.Debug.WriteLine("Activating Startup Stage");

            // Initialize graphics resources
            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Initialize ResourceManager using factory
            _resourceManager = ResourceManagerFactory.CreateResourceManager(graphicsDevice);

            // Initialize bitmap font for text rendering
            _bitmapFont = new BitmapFont(graphicsDevice, _resourceManager);

            // Load background texture (DTXManiaNX uses 1_background.jpg)
            LoadBackgroundTexture();

            // Initialize state
            _elapsedTime = 0;
            _startupPhase = StartupPhase.SystemSounds;
            _phaseStartTime = 0;
            _progressMessages.Clear();

            // Add initial messages (DTXMania pattern)
            _progressMessages.Add("DTXMania powered by YAMAHA Silent Session Drums");

            System.Diagnostics.Debug.WriteLine("Startup Stage activated successfully");
        }

        protected override void OnUpdate(double deltaTime)
        {
            _elapsedTime += deltaTime;

            // Update current phase
            UpdateCurrentPhase();

            // Check if all phases are complete
            if (_startupPhase == StartupPhase.Complete)
            {
                double phaseElapsed = _elapsedTime - _phaseStartTime;
                if (phaseElapsed >= _phaseInfo[_startupPhase].duration)
                {
                    // Transition to Title stage with special startup transition
                    _game.StageManager?.ChangeStage(StageType.Title, new StartupToTitleTransition(1.0));
                }
            }
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);

            // Draw background
            DrawBackground();

            // Draw version info (DTXMania pattern)
            DrawVersionInfo();

            // Draw progress messages (DTXMania pattern)
            DrawProgressMessages();

            // Draw current progress
            DrawCurrentProgress();

            _spriteBatch.End();
        }

        protected override void OnDeactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating Startup Stage");
            // Startup stages typically don't get reactivated, so state reset is unnecessary
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                System.Diagnostics.Debug.WriteLine("Disposing Startup Stage resources");

                // Cleanup MonoGame resources
                _backgroundTexture?.Dispose();
                _bitmapFont?.Dispose();
                _whitePixel?.Dispose();
                _spriteBatch?.Dispose();
                _resourceManager?.Dispose();

                _backgroundTexture = null;
                _bitmapFont = null;
                _whitePixel = null;
                _spriteBatch = null;
                _resourceManager = null;
            }

            base.Dispose(disposing);
        }

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
            if (_startupPhase == StartupPhase.Complete)
                return;

            double phaseElapsed = _elapsedTime - _phaseStartTime;
            var currentPhaseInfo = _phaseInfo[_startupPhase];

            // Update current progress message
            _currentProgressMessage = currentPhaseInfo.message;

            // Perform phase-specific operations
            PerformPhaseOperation(_startupPhase, phaseElapsed);

            // Check if current phase is complete
            if (phaseElapsed >= currentPhaseInfo.duration)
            {
                // Add completion message
                _progressMessages.Add($"✓ {currentPhaseInfo.message.Replace("...", "")}");

                // Move to next phase
                var nextPhase = GetNextPhase(_startupPhase);
                if (nextPhase != _startupPhase)
                {
                    _startupPhase = nextPhase;
                    _phaseStartTime = _elapsedTime;
                    System.Diagnostics.Debug.WriteLine($"Startup phase changed to: {_startupPhase}");
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
                    // Load and validate configuration
                    try
                    {
                        _configManager.LoadConfig("Config.ini");
                        var config = _configManager.Config;

                        // Basic validation - check if config loaded successfully
                        bool isValid = config != null &&
                                     config.ScreenWidth > 0 &&
                                     config.ScreenHeight > 0;

                        System.Diagnostics.Debug.WriteLine($"Configuration validation: {(isValid ? "PASSED" : "FAILED")}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Configuration validation error: {ex.Message}");
                    }
                    break;

                case StartupPhase.EnumerateSongs:
                    // Start song enumeration with SongManager
                    try
                    {
                        // Point to the user's DTXFiles folder
                        var songPaths = new[] { "DTXFiles" };

                        // Use SongManager for enumeration
                        _ = _songManager.EnumerateSongsAsync(songPaths);
                        System.Diagnostics.Debug.WriteLine("Started song enumeration with SongManager (DTXFiles folder)...");
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

        /// <summary>
        /// Draws text using bitmap font if available, otherwise falls back to colored rectangle
        /// </summary>
        private void DrawTextWithFallback(string text, int x, int y, BitmapFont.FontType fontType = BitmapFont.FontType.Normal, Color? fallbackColor = null)
        {
            if (_bitmapFont?.IsLoaded == true)
            {
                _bitmapFont.DrawText(_spriteBatch, text, x, y, fontType);
            }
            else
            {
                // Calculate fallback dimensions based on font type
                int fallbackHeight = fontType == BitmapFont.FontType.Thin ? FALLBACK_SMALL_FONT_HEIGHT : FALLBACK_FONT_HEIGHT;
                Color color = fallbackColor ?? Color.White;
                DrawTextRect(x, y, text.Length * FALLBACK_CHAR_WIDTH, fallbackHeight, color);
            }
        }

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

        private void DrawVersionInfo()
        {
            // Draw version info in top-right corner (DTXMania pattern)
            const string versionText = "DTXManiaCX v1.0.0 - MonoGame Edition";
            var viewport = _game.GraphicsDevice.Viewport;

            if (_bitmapFont?.IsLoaded == true)
            {
                // Calculate position for top-right alignment
                var textSize = _bitmapFont.MeasureText(versionText);
                int x = viewport.Width - (int)textSize.X - MARGIN_EDGE;
                int y = MARGIN_TOP;

                _bitmapFont.DrawText(_spriteBatch, versionText, x, y, BitmapFont.FontType.Normal);
            }
            else
            {
                // Fallback to rectangle in top-right corner
                int x = viewport.Width - (versionText.Length * FALLBACK_CHAR_WIDTH) - MARGIN_EDGE;
                int y = MARGIN_TOP;
                DrawTextRect(x, y, versionText.Length * FALLBACK_CHAR_WIDTH, FALLBACK_FONT_HEIGHT, Color.White);
            }
        }

        private void DrawProgressMessages()
        {
            // Draw progress messages using bitmap font (DTXMania pattern)
            int x = MARGIN_EDGE;
            int y = MARGIN_EDGE;

            lock (_progressMessages)
            {
                foreach (string message in _progressMessages)
                {
                    DrawTextWithFallback(message, x, y);
                    y += LINE_HEIGHT;
                }

                // Draw current progress message in different color/style
                if (!string.IsNullOrEmpty(_currentProgressMessage))
                {
                    DrawTextWithFallback(_currentProgressMessage, x, y, BitmapFont.FontType.Thin, Color.Yellow);
                }
            }
        }

        private void DrawCurrentProgress()
        {
            if (_whitePixel == null)
                return;

            // Calculate overall progress
            int totalPhases = _phaseInfo.Count;
            int currentPhaseIndex = (int)_startupPhase;
            double phaseElapsed = _elapsedTime - _phaseStartTime;
            double currentPhaseDuration = _phaseInfo[_startupPhase].duration;
            double phaseProgress = Math.Min(phaseElapsed / currentPhaseDuration, 1.0);

            double overallProgress = (currentPhaseIndex + phaseProgress) / totalPhases;

            // Draw progress bar in lower middle of screen
            var viewport = _game.GraphicsDevice.Viewport;
            int progressBarX = (viewport.Width - PROGRESS_BAR_WIDTH) / 2; // Center horizontally
            int progressBarY = viewport.Height - PROGRESS_BAR_BOTTOM_MARGIN;

            // Background
            DrawTextRect(progressBarX, progressBarY, PROGRESS_BAR_WIDTH, PROGRESS_BAR_HEIGHT, Color.DarkGray);

            // Progress
            int progressWidth = (int)(PROGRESS_BAR_WIDTH * overallProgress);
            DrawTextRect(progressBarX, progressBarY, progressWidth, PROGRESS_BAR_HEIGHT, Color.LightGreen);

            // Progress percentage
            string progressText = $"{overallProgress * 100:F1}%";
            DrawTextWithFallback(progressText, progressBarX + PROGRESS_BAR_WIDTH + MARGIN_EDGE, progressBarY + MARGIN_TOP);
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
