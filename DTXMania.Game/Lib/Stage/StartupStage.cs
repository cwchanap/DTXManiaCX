using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Utilities;
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
        private const int DefaultFontSize = 14;
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
        private IResourceManager _resourceManager;
        private IFont _font;
        private IFont _boldFont;
        // Serif stand-in for the status line, loaded only when the skin supplies a
        // Latin-only display face (see SelectStatusFont).
        private IFont _statusFallbackFont;

        // DTXMania pattern: progress tracking
        private readonly List<string> _progressMessages;
        private string _currentProgressMessage = "";
        private StartupPhase _startupPhase = StartupPhase.SystemSounds;

        // Services for actual functionality
        private readonly SongManager _songManager;
        private readonly IConfigManager _configManager;

        // Task tracking for async operations
        private Task _currentAsyncTask;
        private CancellationTokenSource _cancellationTokenSource;
        private string[] _songPaths = Constants.SongPaths.Default;

        // Which phase's kick-off has already run (each phase's operation runs once,
        // no matter how late the first update of that phase arrives).
        private StartupPhase? _operationPerformedForPhase;

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

        private ISkinTheme Theme => _resourceManager?.CurrentTheme ?? SkinTheme.Empty;

        #endregion

        #region Theme Resolution

        // The startup screen is a corner-anchored console in NX: white 14px serif
        // log in the top-left, version line in the top-right, and a 400x20
        // gray/green bar 120px above the bottom edge with the raw phase name
        // spelled out beside it. Every key below defaults to those values, so a
        // themeless skin renders exactly as before; skins opt into a centered
        // composition by setting the "0 means NX" anchors.

        internal static string ResolveTextFontFamily(ISkinTheme theme)
            => theme.GetString("Startup.TextFontFamily", string.Empty);

        /// <summary>
        /// Face for the live status line. Defaults to the body family, so a skin
        /// naming one face uses it throughout; naming both pairs a display face
        /// against the body/telemetry face.
        /// </summary>
        internal static string ResolveStatusFontFamily(ISkinTheme theme)
            => theme.GetString("Startup.StatusFontFamily", ResolveTextFontFamily(theme));

        internal static int ResolveTextFontSize(ISkinTheme theme)
            => theme.GetInt("Startup.TextFontSize", DefaultFontSize);

        /// <summary>
        /// Size of the emphasised (live status) face. Follows the body size unless
        /// the skin asks for its own step up, keeping the type scale shallow.
        /// </summary>
        internal static int ResolveStatusFontSize(ISkinTheme theme)
            => theme.GetInt("Startup.StatusFontSize", ResolveTextFontSize(theme));

        internal static int ResolveLogX(ISkinTheme theme) => theme.GetInt("Startup.LogX", MARGIN_EDGE);

        internal static int ResolveLogY(ISkinTheme theme) => theme.GetInt("Startup.LogY", MARGIN_EDGE);

        internal static int ResolveLogLineHeight(ISkinTheme theme)
            => theme.GetInt("Startup.LogLineHeight", LINE_HEIGHT);

        internal static Color ResolveLogColor(ISkinTheme theme)
            => theme.GetColor("Startup.LogText", Color.White);

        /// <summary>
        /// Horizontal centre of the live status line. 0 (NX) keeps the message
        /// trailing the log instead of standing on its own.
        /// </summary>
        internal static int ResolveStatusCenterX(ISkinTheme theme)
            => theme.GetInt("Startup.StatusCenterX", 0);

        internal static int ResolveStatusY(ISkinTheme theme) => theme.GetInt("Startup.StatusY", 0);

        internal static Color ResolveStatusColor(ISkinTheme theme)
            => theme.GetColor("Startup.StatusText", Color.Yellow);

        /// <summary>
        /// Width the status line is held inside. 0 (NX) lets it run as wide as it
        /// likes; a centred layout ties it to the progress rail so the block below
        /// the horizon keeps one silhouette.
        /// </summary>
        internal static int ResolveStatusMaxWidth(ISkinTheme theme)
            => theme.GetInt("Startup.StatusMaxWidth", 0);

        // Enumeration streams "[n processed, m songs] <filename>" through the
        // status line, which is far wider than the phase captions. Shrink it back
        // inside the cap, but not so far that it stops being readable.
        internal const float StatusMinScale = 0.65f;

        internal static float ComputeStatusScale(Func<string, float> measure, string text, int maxWidth)
        {
            if (maxWidth <= 0 || string.IsNullOrEmpty(text))
                return 1f;

            float width = measure(text);
            if (width <= maxWidth)
                return 1f;

            return Math.Max(maxWidth / width, StatusMinScale);
        }

        internal static int ResolveProgressBarWidth(ISkinTheme theme)
            => theme.GetInt("Startup.ProgressBarWidth", PROGRESS_BAR_WIDTH);

        internal static int ResolveProgressBarHeight(ISkinTheme theme)
            => theme.GetInt("Startup.ProgressBarHeight", PROGRESS_BAR_HEIGHT);

        /// <summary>
        /// Absolute top of the bar. 0 (NX) means "measured up from the bottom edge".
        /// </summary>
        internal static int ResolveProgressBarY(ISkinTheme theme)
            => theme.GetInt("Startup.ProgressBarY", 0);

        internal static int ResolveProgressBarTop(ISkinTheme theme, int viewportHeight)
        {
            int themedY = ResolveProgressBarY(theme);
            return themedY > 0 ? themedY : viewportHeight - PROGRESS_BAR_BOTTOM_MARGIN;
        }

        internal static Color ResolveProgressBarBackColor(ISkinTheme theme)
            => theme.GetColor("Startup.ProgressBarBack", Color.DarkGray);

        internal static Color ResolveProgressBarFillColor(ISkinTheme theme)
            => theme.GetColor("Startup.ProgressBarFill", Color.LightGreen);

        /// <summary>
        /// Baseline of the step/percent ledger under the bar. 0 (NX) keeps the
        /// single phase-name readout hanging off the bar's right end.
        /// </summary>
        internal static int ResolveProgressReadoutY(ISkinTheme theme)
            => theme.GetInt("Startup.ProgressReadoutY", 0);

        internal static Color ResolveProgressReadoutColor(ISkinTheme theme)
            => theme.GetColor("Startup.ProgressReadoutText", Color.White);

        internal static int ResolveVersionY(ISkinTheme theme)
            => theme.GetInt("Startup.VersionY", MARGIN_TOP);

        /// <summary>
        /// Right edge the version line is aligned to. 0 (NX) means "one edge
        /// margin in from the right side of the viewport".
        /// </summary>
        internal static int ResolveVersionRightEdge(ISkinTheme theme, int viewportWidth)
        {
            int themedX = theme.GetInt("Startup.VersionRightX", 0);
            return themedX > 0 ? themedX : viewportWidth - MARGIN_EDGE;
        }

        internal static Color ResolveVersionColor(ISkinTheme theme)
            => theme.GetColor("Startup.VersionText", Color.White);

        /// <summary>
        /// The status line carries live song filenames during enumeration, so it
        /// drops back to the CJK-capable face whenever the text leaves ASCII.
        /// </summary>
        internal static IFont SelectStatusFont(string text, IFont displayFont, IFont fallbackFont)
            => displayFont != null && DTXMania.Game.Lib.UI.DisplayText.IsAsciiDisplayable(text)
                ? displayFont
                : fallbackFont;

        /// <summary>
        /// "STEP 04 / 10" — how far through the boot sequence, without leaking the
        /// internal phase name to the player.
        /// </summary>
        internal static string FormatStepReadout(StartupPhase phase, int totalPhases)
            => $"STEP {(int)phase + 1:00} / {totalPhases:00}";

        internal static string FormatPercentReadout(double overallProgress)
            => $"{(Math.Clamp(overallProgress, 0, 1) * 100):F0}%";

        internal static string FormatPhaseReadout(StartupPhase phase, double overallProgress)
            => $"{phase} ({(overallProgress * 100):F1}%)";

        #endregion

        #region Constructor

        public StartupStage(IStageGame game) : base(game)
        {
            _progressMessages = new List<string>();

            // Initialize services - use singleton for SongManager
            _songManager = SongManager.Instance;
            _configManager = _game.ConfigManager;

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

        #region Graphics Hooks

        protected virtual GraphicsDevice GetGraphicsDeviceCore()
        {
            return _game.GraphicsDevice;
        }

        protected virtual Viewport GetViewportCore()
        {
            return GetGraphicsDeviceCore().Viewport;
        }

        protected virtual SpriteBatch CreateSpriteBatchCore(GraphicsDevice graphicsDevice)
        {
            return new SpriteBatch(graphicsDevice);
        }

        protected virtual Texture2D CreateWhitePixelCore(GraphicsDevice graphicsDevice)
        {
            var whitePixel = new Texture2D(graphicsDevice, 1, 1);
            whitePixel.SetData(new[] { Color.White });
            return whitePixel;
        }

        protected virtual IFont CreateFontCore(IResourceManager resourceManager, string fontFamily, int size, FontStyle style)
        {
            return resourceManager.LoadFont(fontFamily.Length > 0 ? fontFamily : "NotoSerifJP", size, style);
        }

        /// <summary>
        /// Loads the serif face used when the status text leaves ASCII. Only needed
        /// when the skin swapped in a Latin-only display face; themeless skins are
        /// already drawing in the serif.
        /// </summary>
        protected virtual IFont CreateStatusFallbackFontCore(IResourceManager resourceManager, int size)
        {
            if (resourceManager == null ||
                ResolveStatusFontFamily(resourceManager.CurrentTheme ?? SkinTheme.Empty).Length == 0)
            {
                return null;
            }

            return resourceManager.LoadFont("NotoSerifJP", size, FontStyle.Bold);
        }

        protected virtual void BeginSpriteBatchCore(SpriteBatch spriteBatch)
        {
            spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        }

        protected virtual void EndSpriteBatchCore(SpriteBatch spriteBatch)
        {
            spriteBatch.End();
        }

        protected virtual void DrawSolidRectCore(SpriteBatch spriteBatch, Texture2D texture, Rectangle destination, Color color)
        {
            spriteBatch.Draw(texture, destination, color);
        }

        #endregion

        #region BaseStage Implementation

        protected override void OnActivate()
        {
            System.Diagnostics.Debug.WriteLine("Activating Startup Stage");

            // Initialize graphics resources
            var graphicsDevice = GetGraphicsDeviceCore();
            _spriteBatch = CreateSpriteBatchCore(graphicsDevice);
            _whitePixel = CreateWhitePixelCore(graphicsDevice);

            // Initialize ResourceManager using factory
            _resourceManager = _game.ResourceManager;

            try
            {
                var theme = _resourceManager?.CurrentTheme ?? SkinTheme.Empty;
                _font = CreateFontCore(_resourceManager, ResolveTextFontFamily(theme),
                    ResolveTextFontSize(theme), FontStyle.Regular);
                _boldFont = CreateFontCore(_resourceManager, ResolveStatusFontFamily(theme),
                    ResolveStatusFontSize(theme), FontStyle.Bold);
                _statusFallbackFont = CreateStatusFallbackFontCore(_resourceManager, ResolveStatusFontSize(theme));
            }
            catch
            {
                _font?.RemoveReference();
                _font = null;
                _boldFont?.RemoveReference();
                _boldFont = null;
                _statusFallbackFont?.RemoveReference();
                _statusFallbackFont = null;
                _whitePixel?.Dispose();
                _whitePixel = null;
                _spriteBatch?.Dispose();
                _spriteBatch = null;
                throw;
            }

            // Load background texture (DTXManiaNX uses 1_background.jpg)

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

            BeginSpriteBatchCore(_spriteBatch);

            // Draw background
            DrawStageBackground(_spriteBatch);
            
            // Draw fallback if no background loaded
            if (!IsBackgroundReady && _whitePixel != null)
            {
                var viewport = GetViewportCore();
                DrawSolidRectCore(_spriteBatch, _whitePixel, new Rectangle(0, 0, viewport.Width, viewport.Height), new Color(16, 16, 32));
            }

            // Draw version info (DTXMania pattern)
            DrawVersionInfo();

            // Draw progress messages (DTXMania pattern)
            DrawProgressMessages();

            // Draw the live status line (themed layouts only)
            DrawStatusLine();

            // Draw current progress
            DrawCurrentProgress();

            EndSpriteBatchCore(_spriteBatch);
        }

        protected override void OnDeactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating Startup Stage");

            // Release font references (re-acquired on re-activation)
            _font?.RemoveReference();
            _font = null;
            _boldFont?.RemoveReference();
            _boldFont = null;
            _statusFallbackFont?.RemoveReference();
            _statusFallbackFont = null;
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
                // Font refs are released in OnDeactivate (called by BaseStage.Dispose → Deactivate)
                // but guard against disposal without deactivation
                _font?.RemoveReference();
                _boldFont?.RemoveReference();
                _statusFallbackFont?.RemoveReference();
                _whitePixel?.Dispose();
                _spriteBatch?.Dispose();

                _font = null;
                _boldFont = null;
                _statusFallbackFont = null;
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
                        // ASCII only: the bundled font faces have no check/warning
                        // glyph, so those characters rendered as the '*' fallback.
                        _currentProgressMessage = $"{currentPhaseInfo.message.Replace("...", "")} - Complete";
                    }
                    else if (_currentAsyncTask.IsFaulted)
                    {
                        _currentProgressMessage = $"{currentPhaseInfo.message.Replace("...", "")} - Error";
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
                // Add completion message (no marker glyph: see the note above)
                _progressMessages.Add(currentPhaseInfo.message.Replace("...", ""));

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
            // Only perform the operation once per phase — keyed by phase, not by a
            // time window. Gating on "phaseElapsed <= 0.1s" wedged startup forever
            // when a single slow frame straddled a phase boundary: the async
            // kick-off was skipped, and async phases never complete with a null task.
            if (_operationPerformedForPhase == phase) return;
            _operationPerformedForPhase = phase;

            switch (phase)
            {
                case StartupPhase.SystemSounds:
                    // Load system sounds (placeholder)
                    System.Diagnostics.Debug.WriteLine("Loading system sounds...");
                    break;

                case StartupPhase.ConfigValidation:
                    // Load and validate configuration
                    var config = _configManager.Config;
                    if (config != null)
                    {
                        _songPaths = new[] { config.DTXPath };

                        // Basic validation - check if config loaded successfully
                        bool isValid = config.ScreenWidth > 0 &&
                                    config.ScreenHeight > 0;

                        System.Diagnostics.Debug.WriteLine($"Configuration validation: {(isValid ? "PASSED" : "FAILED")}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Configuration validation: FAILED (config not loaded)");
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
        protected virtual string GetSongsDatabasePath()
        {
            return AppPaths.GetSongsDatabasePath();
        }

        protected virtual void EnsureDirectory(string path)
        {
            AppPaths.EnsureDirectory(path);
        }

        protected virtual Task<bool> InitializeDatabaseServiceCoreAsync(string databasePath)
        {
            return _songManager.InitializeDatabaseServiceAsync(databasePath, false);
        }

        protected virtual Task<bool> LoadScoreCacheCoreAsync(string[] songPaths)
        {
            return _songManager.LoadScoreCacheAsync(songPaths);
        }

        protected virtual Task<bool> NeedsEnumerationCoreAsync(string[] songPaths, bool forceEnumeration)
        {
            return _songManager.NeedsEnumerationAsync(songPaths, forceEnumeration);
        }

        protected virtual Task<int> EnumerateSongsOnlyCoreAsync(string[] songPaths, IProgress<EnumerationProgress> progressReporter, CancellationToken cancellationToken)
        {
            return _songManager.EnumerateSongsOnlyAsync(songPaths, progressReporter, cancellationToken);
        }

        protected virtual Task BuildSongListFromDatabaseCoreAsync(string[] songPaths)
        {
            return _songManager.BuildSongListFromDatabasePublicAsync(songPaths);
        }

        protected virtual int GetRootSongCount()
        {
            return _songManager.RootSongs.Count;
        }

        protected virtual Task<bool> SaveSongsDatabaseCoreAsync()
        {
            return _songManager.SaveSongsDBAsync();
        }

        protected virtual void MarkSongManagerInitialized()
        {
            _songManager.SetInitialized();
        }

        private async Task InitializeDatabaseServiceAsync()
        {
            try
            {
                var databasePath = GetSongsDatabasePath();
                EnsureDirectory(Path.GetDirectoryName(databasePath) ?? "");
                bool success = await InitializeDatabaseServiceCoreAsync(databasePath).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"Database service initialization: {(success ? "SUCCESS" : "FAILED")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during database service initialization: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Load score cache async operation
        /// </summary>
        private async Task LoadScoreCacheAsync()
        {
            try
            {
                bool success = await LoadScoreCacheCoreAsync(_songPaths).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"Score cache loading: {(success ? "SUCCESS" : "FAILED - enumeration needed")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during score cache loading: {ex.GetType().Name}: {ex.Message}");
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
                _needsEnumeration = await NeedsEnumerationCoreAsync(_songPaths, _forceEnumeration).ConfigureAwait(false);
                
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
                System.Diagnostics.Debug.WriteLine($"Error during filesystem change detection: {ex.GetType().Name}: {ex.Message}");
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

                int songCount = await EnumerateSongsOnlyCoreAsync(_songPaths, progressReporter, _cancellationTokenSource.Token).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"Song enumeration complete: {songCount} songs found");
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("Song enumeration was canceled.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during song enumeration: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Build song lists async operation - this populates the actual song list from database
        /// </summary>
        private async Task BuildSongListsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Building song lists from database...");
                await BuildSongListFromDatabaseCoreAsync(_songPaths).ConfigureAwait(false);
                int songCount = GetRootSongCount();
                System.Diagnostics.Debug.WriteLine($"Song lists building complete: {songCount} root nodes loaded");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during song lists building: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Save songs database async operation
        /// </summary>
        private async Task SaveSongsDBAsync()
        {
            try
            {
                bool success = await SaveSongsDatabaseCoreAsync().ConfigureAwait(false);
                
                // Mark SongManager as fully initialized after successful save
                if (success)
                {
                    MarkSongManagerInitialized();
                    int songCount = GetRootSongCount();
                    System.Diagnostics.Debug.WriteLine($"SongManager fully initialized: {songCount} root nodes loaded");
                }
                
                System.Diagnostics.Debug.WriteLine($"Songs DB save: {(success ? "SUCCESS" : "FAILED")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during songs DB save: {ex.GetType().Name}: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods - Drawing

        private void DrawTextWithFallback(string text, int x, int y, bool bold = false, Color? color = null)
        {
            var font = bold ? _boldFont : _font;
            if (font != null)
            {
                font.DrawString(_spriteBatch, text, new Vector2(x, y), color ?? Color.White);
            }
            else
            {
                int fallbackHeight = bold ? FALLBACK_SMALL_FONT_HEIGHT : FALLBACK_FONT_HEIGHT;
                DrawTextRect(x, y, text.Length * FALLBACK_CHAR_WIDTH, fallbackHeight, color ?? Color.White);
            }
        }

        /// <summary>
        /// Measured width of <paramref name="text"/>, falling back to the block
        /// metrics used when no font is available.
        /// </summary>
        private float MeasureTextWidth(string text, bool bold)
        {
            var font = bold ? _boldFont : _font;
            return font != null ? font.MeasureString(text).X : text.Length * FALLBACK_CHAR_WIDTH;
        }

        private void DrawRightAlignedTextWithFallback(string text, int rightX, int y, bool bold, Color color)
        {
            // Truncating (not rounding) the measured width keeps the NX version
            // line on exactly the pixel it has always used.
            int x = rightX - (int)MeasureTextWidth(text, bold);
            DrawTextWithFallback(text, x, y, bold, color);
        }

        // Background drawing is now handled by BaseStage

        private void DrawVersionInfo()
        {
            // Draw version info right-aligned against the edge margin (DTXMania pattern)
            const string versionText = "DTXManiaCX v1.0.0 - MonoGame Edition";
            var viewport = GetViewportCore();
            var theme = Theme;

            DrawRightAlignedTextWithFallback(versionText, ResolveVersionRightEdge(theme, viewport.Width),
                ResolveVersionY(theme), bold: false, color: ResolveVersionColor(theme));
        }

        private void DrawProgressMessages()
        {
            var theme = Theme;
            int x = ResolveLogX(theme);
            int y = ResolveLogY(theme);
            int lineHeight = ResolveLogLineHeight(theme);
            var logColor = ResolveLogColor(theme);
            bool statusIsSeparate = ResolveStatusCenterX(theme) > 0;

            lock (_progressMessages)
            {
                foreach (string message in _progressMessages)
                {
                    DrawTextWithFallback(message, x, y, color: logColor);
                    y += lineHeight;
                }

                // NX trails the live message under the log. Skins that give the
                // status its own centred line drop it here so it is not said twice.
                if (!statusIsSeparate && !string.IsNullOrEmpty(_currentProgressMessage))
                {
                    DrawTextWithFallback(_currentProgressMessage, x, y, bold: true, color: ResolveStatusColor(theme));
                }
            }
        }

        private void DrawStatusLine()
        {
            var theme = Theme;
            int centerX = ResolveStatusCenterX(theme);
            if (centerX <= 0)
                return;

            string message;
            lock (_progressMessages)
            {
                message = _currentProgressMessage;
            }

            if (string.IsNullOrEmpty(message))
                return;

            int y = ResolveStatusY(theme);
            var color = ResolveStatusColor(theme);
            var font = SelectStatusFont(message, _boldFont, _statusFallbackFont ?? _boldFont);

            if (font != null)
            {
                float scale = ComputeStatusScale(
                    text => font.MeasureString(text).X, message, ResolveStatusMaxWidth(theme));
                int x = centerX - (int)Math.Round(font.MeasureString(message).X * scale / 2f);
                var position = new Vector2(x, y);

                if (scale >= 1f)
                {
                    font.DrawString(_spriteBatch, message, position, color);
                }
                else
                {
                    font.DrawString(_spriteBatch, message, position, color, 0f, Vector2.Zero,
                        new Vector2(scale, scale), SpriteEffects.None, 0f);
                }
            }
            else
            {
                int width = message.Length * FALLBACK_CHAR_WIDTH;
                DrawTextRect(centerX - width / 2, y, width, FALLBACK_SMALL_FONT_HEIGHT, color);
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

            // Draw progress bar
            var theme = Theme;
            var viewport = GetViewportCore();
            int barWidth = ResolveProgressBarWidth(theme);
            int barHeight = ResolveProgressBarHeight(theme);
            int progressBarX = (viewport.Width - barWidth) / 2; // Center horizontally
            int progressBarY = ResolveProgressBarTop(theme, viewport.Height);

            // Draw progress bar background
            DrawTextRect(progressBarX, progressBarY, barWidth, barHeight, ResolveProgressBarBackColor(theme));

            // Draw progress bar foreground
            int progressWidth = (int)(barWidth * overallProgress);
            DrawTextRect(progressBarX, progressBarY, progressWidth, barHeight, ResolveProgressBarFillColor(theme));

            int readoutY = ResolveProgressReadoutY(theme);
            var readoutColor = ResolveProgressReadoutColor(theme);

            if (readoutY > 0)
            {
                // Ledger row under the rail: the step counter hangs off the bar's
                // left edge and the percentage off its right, so both readouts are
                // tied to the bar rather than floating beside it.
                DrawTextWithFallback(FormatStepReadout(_startupPhase, totalPhases),
                    progressBarX, readoutY, color: readoutColor);
                DrawRightAlignedTextWithFallback(FormatPercentReadout(overallProgress),
                    progressBarX + barWidth, readoutY, bold: false, color: readoutColor);
            }
            else
            {
                // Draw progress text next to the bar
                DrawTextWithFallback(FormatPhaseReadout(_startupPhase, overallProgress),
                    progressBarX + barWidth + MARGIN_EDGE, progressBarY + MARGIN_TOP, color: readoutColor);
            }
        }

        private void DrawTextRect(int x, int y, int width, int height, Color color)
        {
            if (_whitePixel != null)
            {
                DrawSolidRectCore(_spriteBatch, _whitePixel, new Rectangle(x, y, width, height), color);
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
