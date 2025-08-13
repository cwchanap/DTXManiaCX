using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Config;
using DTXMania.Game;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib.Stage
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
        private Task _currentAsyncTask;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly string[] _songPaths = Constants.SongPaths.Default;

        // Filesystem change detection result (cached to avoid duplicate checks)
        private bool? _needsEnumeration = null;

        // Debug/testing flags
        private readonly bool _forceEnumeration = true; // TODO: Remove this or make configurable

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
                { StartupPhase.SongListDB, ("Initializing song database...", 0.3) },
                { StartupPhase.SongsDB, ("Loading songs.db...", 0.4) },
                { StartupPhase.LoadScoreCache, ("Loading cached song data...", 0.6) },
                { StartupPhase.LoadScoreFiles, ("Checking for filesystem changes...", 0.7) },
                { StartupPhase.EnumerateSongs, ("Scanning for new/modified songs...", 1.5) },
                { StartupPhase.BuildSongLists, ("Building song lists...", 0.3) },
                { StartupPhase.SaveSongsDB, ("Saving song database...", 0.2) },
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
            _resourceManager = _game.ResourceManager;

            // Initialize bitmap font for text rendering
            var consoleFontConfig = BitmapFont.CreateConsoleFontConfig();
            _bitmapFont = new BitmapFont(graphicsDevice, _resourceManager, consoleFontConfig);

            // Load background texture (DTXManiaNX uses 1_background.jpg)
            LoadBackgroundTexture();

            // Initialize state
            _elapsedTime = 0;
            _startupPhase = StartupPhase.SystemSounds;
            _phaseStartTime = 0;
            _progressMessages.Clear();
            _currentAsyncTask = null; // Reset task tracking

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

                // Cancel and await completion of the current async task if it exists
                if (_currentAsyncTask != null)
                {
                    try
                    {
                        // Request cancellation
                        _cancellationTokenSource?.Cancel();

                        // Wait for the task to complete, with a timeout.
                        System.Diagnostics.Debug.WriteLine("Waiting for current async task with a 5-second timeout...");
                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
                        var completedTask = Task.WhenAny(_currentAsyncTask, timeoutTask).Result;

                        if (completedTask == _currentAsyncTask)
                        {
                            // Task completed within the timeout.
                            // Calling Wait() on the completed task will not block but will propagate any exceptions.
                            _currentAsyncTask.Wait();
                            System.Diagnostics.Debug.WriteLine("Current async task completed gracefully.");
                        }
                        else
                        {
                            // Timeout occurred.
                            System.Diagnostics.Debug.WriteLine("Warning: Current async task timed out and will be abandoned.");
                        }

                        System.Diagnostics.Debug.WriteLine("Current async task disposed");
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
                        _currentAsyncTask = null;
                    }
                }

                // Dispose cancellation token source
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                // Cleanup MonoGame resources - using reference counting for managed textures
                _backgroundTexture?.RemoveReference();
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
                _backgroundTexture = _resourceManager.LoadTexture(TexturePath.StartupBackground);
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
            bool phaseComplete = false;
            
            // For async phases, wait for task completion AND minimum duration
            if (HasAsyncOperation(_startupPhase))
            {
                if (_currentAsyncTask != null)
                {
                    // Phase is complete when both minimum duration has passed AND task is completed
                    phaseComplete = phaseElapsed >= currentPhaseInfo.duration && _currentAsyncTask.IsCompleted;

                    if (!_currentAsyncTask.IsCompleted)
                    {
                        _currentProgressMessage = $"{currentPhaseInfo.message} (in progress)";
                    }
                    else if (_currentAsyncTask.IsCompletedSuccessfully)
                    {
                        _currentProgressMessage = $"{currentPhaseInfo.message.Replace("...", "")} ✓ Complete";
                    }
                    else if (_currentAsyncTask.IsFaulted)
                    {
                        _currentProgressMessage = $"{currentPhaseInfo.message.Replace("...", "")} ⚠ Error";
                        System.Diagnostics.Debug.WriteLine($"{_startupPhase} task failed: {_currentAsyncTask.Exception?.InnerException?.Message}");
                        // Continue to next phase even on error after minimum duration
                        phaseComplete = phaseElapsed >= currentPhaseInfo.duration;
                    }
                }
                else
                {
                    // No async task started yet, not complete
                    phaseComplete = false;
                }
            }
            else
            {
                // For non-async phases, complete based on duration only
                phaseComplete = phaseElapsed >= currentPhaseInfo.duration;
            }

            if (phaseComplete)
            {
                // Add completion message
                _progressMessages.Add($"✓ {currentPhaseInfo.message.Replace("...", "")}");

                // Reset async task for the next phase
                _currentAsyncTask = null;

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

                case StartupPhase.SongListDB:
                    // Initialize database service
                    if (_currentAsyncTask == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Starting database service initialization...");
                        _currentAsyncTask = InitializeDatabaseServiceAsync();
                    }
                    break;

                case StartupPhase.SongsDB:
                    // This is handled together with SongListDB in InitializeDatabaseServiceAsync
                    System.Diagnostics.Debug.WriteLine("SongsDB initialization (handled with SongListDB)");
                    break;

                case StartupPhase.LoadScoreCache:
                    // Try to load existing songs from database cache
                    if (_currentAsyncTask == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Starting score cache loading...");
                        _currentAsyncTask = LoadScoreCacheAsync();
                    }
                    break;

                case StartupPhase.LoadScoreFiles:
                    // Perform filesystem change detection during this phase
                    if (_currentAsyncTask == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Starting filesystem change detection...");
                        _currentAsyncTask = CheckFilesystemChangesAsync();
                    }
                    break;

                case StartupPhase.EnumerateSongs:
                    // Enumerate songs from file system (if cache loading failed or was outdated)
                    if (_currentAsyncTask == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Starting song enumeration...");
                        _currentAsyncTask = EnumerateSongsAsync();
                    }
                    break;

                case StartupPhase.BuildSongLists:
                    // Build final song lists
                    if (_currentAsyncTask == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Starting song lists building...");
                        _currentAsyncTask = BuildSongListsAsync();
                    }
                    break;

                case StartupPhase.SaveSongsDB:
                    // Save songs to database
                    if (_currentAsyncTask == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Starting songs DB save...");
                        _currentAsyncTask = SaveSongsDBAsync();
                    }
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
                StartupPhase.SongsDB => StartupPhase.LoadScoreCache,
                StartupPhase.LoadScoreCache => StartupPhase.LoadScoreFiles,
                StartupPhase.LoadScoreFiles => StartupPhase.EnumerateSongs,
                StartupPhase.EnumerateSongs => StartupPhase.BuildSongLists,
                StartupPhase.BuildSongLists => StartupPhase.SaveSongsDB,
                StartupPhase.SaveSongsDB => StartupPhase.Complete,
                _ => StartupPhase.Complete
            };
        }

        /// <summary>
        /// Determines if a phase requires async operations
        /// </summary>
        private bool HasAsyncOperation(StartupPhase phase)
        {
            return phase switch
            {
                StartupPhase.SongListDB => true,
                StartupPhase.LoadScoreCache => true,
                StartupPhase.LoadScoreFiles => true,
                StartupPhase.EnumerateSongs => true,
                StartupPhase.BuildSongLists => true,
                StartupPhase.SaveSongsDB => true,
                _ => false
            };
        }

        /// <summary>
        /// Initialize database service async operation
        /// </summary>
        private async Task InitializeDatabaseServiceAsync()
        {
            try
            {
                bool success = await _songManager.InitializeDatabaseServiceAsync("songs.db", false).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"Database service initialization: {(success ? "SUCCESS" : "FAILED")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during database service initialization: {ex.Message}");
            }
        }

        /// <summary>
        /// Load score cache async operation
        /// </summary>
        private async Task LoadScoreCacheAsync()
        {
            try
            {
                bool success = await _songManager.LoadScoreCacheAsync(_songPaths).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"Score cache loading: {(success ? "SUCCESS" : "FAILED - enumeration needed")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during score cache loading: {ex.Message}");
            }
        }

        /// <summary>
        /// Check filesystem changes async operation
        /// </summary>
        private async Task CheckFilesystemChangesAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Checking filesystem for changes...");
                
                // Perform detailed filesystem change detection and cache the result
                _needsEnumeration = await _songManager.NeedsEnumerationAsync(_songPaths, _forceEnumeration).ConfigureAwait(false);
                
                if (_needsEnumeration.Value)
                {
                    System.Diagnostics.Debug.WriteLine("Filesystem changes detected - enumeration will be needed");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No filesystem changes detected - database is up to date");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during filesystem change detection: {ex.Message}");
                // On error, assume enumeration is needed to be safe
                _needsEnumeration = true;
            }
        }

        /// <summary>
        /// Enumerate songs async operation with enhanced filesystem change detection
        /// </summary>
        private async Task EnumerateSongsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Using cached filesystem change detection result...");
                
                // Use cached result from CheckFilesystemChangesAsync to avoid duplicate filesystem checks
                bool needsEnumeration = _needsEnumeration ?? true; // Default to true if not set for safety
                
                if (!needsEnumeration)
                {
                    System.Diagnostics.Debug.WriteLine("No filesystem changes detected (cached), skipping enumeration");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("Filesystem changes detected (cached), proceeding with enumeration...");
                
                // Create progress reporter for detailed enumeration feedback
                var progressReporter = new Progress<EnumerationProgress>(progress =>
                {
                    // Update progress message with enumeration details
                    var phaseInfo = _phaseInfo[StartupPhase.EnumerateSongs];
                    if (!string.IsNullOrEmpty(progress.CurrentFile))
                    {
                        var fileName = Path.GetFileName(progress.CurrentFile);
                        _currentProgressMessage = $"{phaseInfo.message} [{progress.ProcessedCount} processed, {progress.DiscoveredSongs} songs] {fileName}";
                    }
                    else if (!string.IsNullOrEmpty(progress.CurrentDirectory))
                    {
                        var dirName = Path.GetFileName(progress.CurrentDirectory);
                        _currentProgressMessage = $"{phaseInfo.message} Scanning directory: {dirName}";
                    }
                    else
                    {
                        _currentProgressMessage = $"{phaseInfo.message} [{progress.ProcessedCount} processed, {progress.DiscoveredSongs} songs found]";
                    }
                });

                int songCount = await _songManager.EnumerateSongsOnlyAsync(_songPaths, progressReporter, _cancellationTokenSource.Token).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"Song enumeration complete: {songCount} songs found");
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

        /// <summary>
        /// Build song lists async operation - this populates the actual song list from database
        /// </summary>
        private async Task BuildSongListsAsync()
        {
            try
            {
                // The current BuildSongListsAsync method is just a placeholder
                // We need to call the actual method that builds the song list from database
                System.Diagnostics.Debug.WriteLine("Building song lists from database...");
                
                // This is the critical method that actually populates _rootSongs from the database
                await CallBuildSongListFromDatabaseAsync().ConfigureAwait(false);
                
                // Verify the results
                int songCount = _songManager.RootSongs.Count;
                System.Diagnostics.Debug.WriteLine($"Song lists building complete: {songCount} root nodes loaded");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during song lists building: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper method to call the private BuildSongListFromDatabaseAsync method
        /// </summary>
        private async Task CallBuildSongListFromDatabaseAsync()
        {
            try
            {
                // We need to use reflection or create a public method to call BuildSongListFromDatabaseAsync
                // For now, let's add a public wrapper method to SongManager
                await _songManager.BuildSongListFromDatabasePublicAsync(_songPaths).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calling BuildSongListFromDatabaseAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Save songs database async operation
        /// </summary>
        private async Task SaveSongsDBAsync()
        {
            try
            {
                bool success = await _songManager.SaveSongsDBAsync().ConfigureAwait(false);
                
                // Mark SongManager as fully initialized after successful save
                if (success)
                {
                    _songManager.SetInitialized();
                    int songCount = _songManager.RootSongs.Count;
                    System.Diagnostics.Debug.WriteLine($"SongManager fully initialized: {songCount} root nodes loaded");
                }
                
                System.Diagnostics.Debug.WriteLine($"Songs DB save: {(success ? "SUCCESS" : "FAILED")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during songs DB save: {ex.Message}");
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
                _bitmapFont.DrawText(_spriteBatch, text, x, y, Color.White, fontType);
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

                _bitmapFont.DrawText(_spriteBatch, versionText, x, y, Color.White, BitmapFont.FontType.Normal);
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

    // Enum for different phases of startup (order matches execution sequence)
    public enum StartupPhase
    {
        SystemSounds = 0,        // 0
        ConfigValidation = 1,    // 1  
        SongListDB = 2,          // 2
        SongsDB = 3,             // 3
        LoadScoreCache = 4,      // 4
        LoadScoreFiles = 5,      // 5
        EnumerateSongs = 6,      // 6
        BuildSongLists = 7,      // 7
        SaveSongsDB = 8,         // 8
        Complete = 9             // 9
    }
}
