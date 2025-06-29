using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Stage;
using DTX.Resources;
using DTX.Song;
using DTX.Config;
using DTXMania.Game;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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

        // Task tracking for async operations
        private Task _songEnumerationTask;
        private CancellationTokenSource _cancellationTokenSource;

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

            // Initialize services - use singleton for SongManager
            _songManager = SongManager.Instance;
            _configManager = new ConfigManager();

            // Initialize cancellation token source for async operations
            _cancellationTokenSource = new CancellationTokenSource();

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
            _songEnumerationTask = null; // Reset task tracking

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

                // Cancel and await completion of the enumeration task if it exists
                if (_songEnumerationTask != null)
                {
                    try
                    {
                        // Request cancellation
                        _cancellationTokenSource?.Cancel();

                        // Wait for the task to complete, with a timeout.
                        System.Diagnostics.Debug.WriteLine("Waiting for song enumeration task with a 5-second timeout...");
                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
                        var completedTask = Task.WhenAny(_songEnumerationTask, timeoutTask).Result;

                        if (completedTask == _songEnumerationTask)
                        {
                            // Task completed within the timeout.
                            // Calling Wait() on the completed task will not block but will propagate any exceptions.
                            _songEnumerationTask.Wait();
                            System.Diagnostics.Debug.WriteLine("Song enumeration task completed gracefully.");
                        }
                        else
                        {
                            // Timeout occurred.
                            System.Diagnostics.Debug.WriteLine("Warning: Song enumeration task timed out and will be abandoned.");
                        }

                        System.Diagnostics.Debug.WriteLine("Song enumeration task disposed");
                    }
                    catch (AggregateException ex)
                    {
                        // Handle task cancellation or other exceptions during disposal
                        System.Diagnostics.Debug.WriteLine($"Exception during task disposal: {ex.InnerException?.Message}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Exception during task disposal: {ex.Message}");
                    }
                    finally
                    {
                        _songEnumerationTask = null;
                    }
                }

                // Dispose cancellation token source
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

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

            // Perform phase-specific operations (non-blocking)
            PerformPhaseOperationSync(_startupPhase, phaseElapsed);

            // Check if current phase is complete
            bool phaseComplete = phaseElapsed >= currentPhaseInfo.duration;

            // Special handling for EnumerateSongs phase - wait for task completion
            if (_startupPhase == StartupPhase.EnumerateSongs && _songEnumerationTask != null)
            {
                phaseComplete = phaseComplete && _songEnumerationTask.IsCompleted;

                if (!_songEnumerationTask.IsCompleted)
                {
                    _currentProgressMessage = "Enumerating songs... (in progress)";
                }
                else if (_songEnumerationTask.IsCompletedSuccessfully)
                {
                    _currentProgressMessage = "Enumerating songs... ✓ Complete";
                }
                else if (_songEnumerationTask.IsFaulted)
                {
                    _currentProgressMessage = "Enumerating songs... ⚠ Error";
                    System.Diagnostics.Debug.WriteLine($"Song enumeration task failed: {_songEnumerationTask.Exception?.InnerException?.Message}");
                }
            }

            if (phaseComplete)
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

        private void PerformPhaseOperationSync(StartupPhase phase, double phaseElapsed)
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
                    // Initialize SongManager with song enumeration (async operation)
                    if (!_songManager.IsInitialized)
                    {
                        // Start async operation but track it for completion
                        if (_songEnumerationTask == null)
                        {
                            _songEnumerationTask = EnumerateSongsAsync(_cancellationTokenSource.Token);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("SongManager already initialized, skipping enumeration");
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

        private async Task EnumerateSongsAsync(CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                var songPaths = new[] { "DTXFiles" };
                await _songManager.InitializeAsync(songPaths, "songs.db", null, false, token).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine("SongManager initialized successfully");

                int songCount = _songManager.RootSongs.Count;
                System.Diagnostics.Debug.WriteLine($"SongManager: {songCount} root nodes loaded");

                if (!token.IsCancellationRequested)
                {
                    lock (_progressMessages) // Reuse existing lock object
                    {
                        _startupPhase = StartupPhase.Complete;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("Song enumeration was canceled.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during song enumeration: {ex.Message}");
            }
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

            double phaseProgress = Math.Clamp(phaseElapsed / currentPhaseDuration, 0, 1);
            double overallProgress = (currentPhaseIndex + phaseProgress) / totalPhases;

            string progressText = $"{_startupPhase} ({(overallProgress * 100):F1}%)";

            // Draw progress bar
            var viewport = _game.GraphicsDevice.Viewport;
            int progressBarX = (viewport.Width - PROGRESS_BAR_WIDTH) / 2; // Center horizontally
            int progressBarY = viewport.Height - PROGRESS_BAR_BOTTOM_MARGIN;

            // Draw progress bar background
            DrawTextRect(progressBarX, progressBarY, PROGRESS_BAR_WIDTH, PROGRESS_BAR_HEIGHT, Color.DarkGray);

            // Draw progress bar foreground
            int progressWidth = (int)(PROGRESS_BAR_WIDTH * overallProgress);
            DrawTextRect(progressBarX, progressBarY, progressWidth, PROGRESS_BAR_HEIGHT, Color.LightGreen);

            // Draw progress text next to the bar
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
    }

    // Enum for different phases of startup
    public enum StartupPhase
    {
        SystemSounds,
        ConfigValidation,
        SongListDB,
        SongsDB,
        EnumerateSongs,
        LoadScoreCache,
        LoadScoreFiles,
        BuildSongLists,
        SaveSongsDB,
        Complete
    }
}
