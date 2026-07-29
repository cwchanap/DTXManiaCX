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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS8632

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
        private readonly object _activationGate = new();
        private long _activationGeneration;
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

        private readonly Stopwatch _startupStopwatch = new();
        private bool _startupSummaryWritten;
        private bool _hasRenderedStartupFrame;
        private bool _titleTransitionRequested;
        private StartupSongLoadPath _selectedLoadPath =
            StartupSongLoadPath.Unknown;
        private StartupSongLoadOutcome _songLoadOutcome =
            StartupSongLoadOutcome.Success;
        private string? _songLoadError;
        private SongEnumerationResult? _enumerationResult;
        private TimeSpan _databaseInitializationDuration;
        private TimeSpan _cacheHierarchyDuration;
        private bool _cacheFallbackAttempted;

        #endregion

        #region Properties

        public override StageType Type => StageType.Startup;

        private ISkinTheme Theme => _resourceManager?.CurrentTheme ?? SkinTheme.Empty;

        protected virtual bool ForceEnumeration => _forceEnumeration;

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

        private void ReleaseStartupFonts()
        {
            _font?.RemoveReference();
            _font = null;
            _boldFont?.RemoveReference();
            _boldFont = null;
            _statusFallbackFont?.RemoveReference();
            _statusFallbackFont = null;
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

        protected virtual void WriteStartupSummary(string line)
        {
            Console.Out.WriteLine(line);
        }

        #endregion

        #region BaseStage Implementation

        protected override void OnActivate()
        {
            System.Diagnostics.Debug.WriteLine("Activating Startup Stage");

            BeginActivationScope();
            _startupStopwatch.Restart();
            _startupSummaryWritten = false;
            _hasRenderedStartupFrame = false;
            _titleTransitionRequested = false;
            _selectedLoadPath = StartupSongLoadPath.Unknown;
            _songLoadOutcome = StartupSongLoadOutcome.Success;
            _songLoadError = null;
            _enumerationResult = null;
            _databaseInitializationDuration = TimeSpan.Zero;
            _cacheHierarchyDuration = TimeSpan.Zero;
            _cacheFallbackAttempted = false;
            _needsEnumeration = null;

            // Initialize graphics resources
            var graphicsDevice = GetGraphicsDeviceCore();
            _spriteBatch = CreateSpriteBatchCore(graphicsDevice);
            _whitePixel = CreateWhitePixelCore(graphicsDevice);

            // Initialize ResourceManager using factory
            _resourceManager = _game.ResourceManager;

            try
            {
                var theme = _resourceManager?.CurrentTheme ?? SkinTheme.Empty;
                var themedFamily = ResolveTextFontFamily(theme);
                var statusFamily = ResolveStatusFontFamily(theme);
                var usedThemedFaces = themedFamily.Length > 0 || statusFamily.Length > 0;

                try
                {
                    _font = CreateFontCore(_resourceManager, themedFamily,
                        ResolveTextFontSize(theme), FontStyle.Regular);
                    _boldFont = CreateFontCore(_resourceManager, statusFamily,
                        ResolveStatusFontSize(theme), FontStyle.Bold);
                }
                catch when (usedThemedFaces)
                {
                    // Themed faces failed — drop partials and fall back to NotoSerifJP.
                    ReleaseStartupFonts();

                    _font = CreateFontCore(_resourceManager, "NotoSerifJP",
                        ResolveTextFontSize(theme), FontStyle.Regular);
                    _boldFont = CreateFontCore(_resourceManager, "NotoSerifJP",
                        ResolveStatusFontSize(theme), FontStyle.Bold);
                }

                // Best-effort CJK fallback for Latin-only status faces; never fatal.
                try
                {
                    _statusFallbackFont = CreateStatusFallbackFontCore(_resourceManager, ResolveStatusFontSize(theme));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"StartupStage: status fallback font unavailable: {ex.Message}");
                    _statusFallbackFont = null;
                }
            }
            catch
            {
                ReleaseStartupFonts();
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
            _operationPerformedForPhase = null; // Reset per-phase op guard (see PerformPhaseOperationSync)

            // Add initial messages (DTXMania pattern)
            _progressMessages.Add("DTXMania powered by YAMAHA Silent Session Drums");

            System.Diagnostics.Debug.WriteLine("Startup Stage activated successfully");
            _game.ReportStartupActivated();
        }

        protected override void OnUpdate(double deltaTime)
        {
            _elapsedTime += deltaTime;
            UpdateCurrentPhase();

            if (_startupPhase == StartupPhase.Complete
                && _hasRenderedStartupFrame
                && !_titleTransitionRequested)
            {
                _titleTransitionRequested = true;
                WriteSummaryOnce();
                _game.ReportStartupSummaryAndTitleRequested();
                TryRecordExactlyOnce(
                    ResolveCriticalPathTrace(),
                    StartupCriticalPathMilestone.SummaryRequest);
                _game.StageManager?.ChangeStage(
                    StageType.Title,
                    new StartupToTitleTransition(1.0));
            }
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null)
                return;

            BeginSpriteBatchCore(_spriteBatch);
            DrawStartupContent();
            EndSpriteBatchCore(_spriteBatch);
            _hasRenderedStartupFrame = true;
            _game.ReportStartupFrameRendered();
        }

        private void DrawStartupContent()
        {
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
        }

        protected override void OnDeactivate()
        {
            System.Diagnostics.Debug.WriteLine("Deactivating Startup Stage");

            RetireActivationScope();

            // Release font references (re-acquired on re-activation)
            _font?.RemoveReference();
            _font = null;
            _boldFont?.RemoveReference();
            _boldFont = null;
            _statusFallbackFont?.RemoveReference();
            _statusFallbackFont = null;
        }

        private void BeginActivationScope()
        {
            Task pendingTask;
            CancellationTokenSource pendingCancellation;
            lock (_activationGate)
            {
                _activationGeneration++;
                pendingTask = _currentAsyncTask;
                pendingCancellation = _cancellationTokenSource;
                _currentAsyncTask = null;
                _cancellationTokenSource =
                    new CancellationTokenSource();
            }

            FailPendingActivationTrace(pendingTask);
            CancelAndObserveRetiredOperation(
                pendingTask,
                pendingCancellation);
        }

        private void RetireActivationScope()
        {
            Task pendingTask;
            CancellationTokenSource pendingCancellation;
            lock (_activationGate)
            {
                _activationGeneration++;
                pendingTask = _currentAsyncTask;
                pendingCancellation = _cancellationTokenSource;
                _currentAsyncTask = null;
                _cancellationTokenSource = null;
            }

            FailPendingActivationTrace(pendingTask);
            CancelAndObserveRetiredOperation(
                pendingTask,
                pendingCancellation);
        }

        private void FailPendingActivationTrace(Task pendingTask)
        {
            if (pendingTask == null || pendingTask.IsCompleted)
                return;

            TryFailCriticalPath(
                ResolveCriticalPathTrace(),
                "activation_generation_invalidated",
                "activation_generation_invalidated");
        }

        private static void CancelAndObserveRetiredOperation(
            Task pendingTask,
            CancellationTokenSource pendingCancellation)
        {
            if (pendingCancellation != null)
            {
                try
                {
                    pendingCancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // A terminal operation may already have released it.
                }
            }

            if (pendingTask == null)
            {
                pendingCancellation?.Dispose();
                return;
            }

            _ = pendingTask.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                    {
                        _ = completed.Exception;
                    }
                    pendingCancellation?.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void EnsureCurrentActivation(
            long activationGeneration,
            CancellationToken cancellationToken)
        {
            if (activationGeneration != _activationGeneration)
            {
                throw new OperationCanceledException(
                    cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                System.Diagnostics.Debug.WriteLine("Disposing Startup Stage resources");

                RetireActivationScope();

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

            var currentPhaseInfo = _phaseInfo[_startupPhase];
            _currentProgressMessage = currentPhaseInfo.message;
            PerformPhaseOperationSync(
                _startupPhase,
                _elapsedTime - _phaseStartTime);

            bool phaseComplete;
            if (HasAsyncOperation(_startupPhase))
            {
                phaseComplete = _currentAsyncTask?.IsCompleted ?? false;
                if (_currentAsyncTask == null)
                {
                    return;
                }

                if (!_currentAsyncTask.IsCompleted)
                {
                    _currentProgressMessage =
                        $"{currentPhaseInfo.message} (in progress)";
                }
                else if (_currentAsyncTask.IsCompletedSuccessfully)
                {
                    _currentProgressMessage =
                        $"{currentPhaseInfo.message.Replace("...", "")} - Complete";
                }
                else
                {
                    _currentProgressMessage =
                        $"{currentPhaseInfo.message.Replace("...", "")} - Error";
                    if (_currentAsyncTask.IsFaulted)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"{_startupPhase} task failed: " +
                            $"{_currentAsyncTask.Exception?.InnerException?.Message}");
                    }
                }

                if (phaseComplete)
                {
                    var criticalPathTrace = ResolveCriticalPathTrace();
                    if (_startupPhase == StartupPhase.SongListDB)
                    {
                        TryRecordExactlyOnce(
                            criticalPathTrace,
                            StartupCriticalPathMilestone.DatabaseObserved);
                    }
                    else if (_startupPhase == StartupPhase.EnumerateSongs)
                    {
                        TryRecordExactlyOnce(
                            criticalPathTrace,
                            StartupCriticalPathMilestone.EnumerationObserved);
                    }
                }
            }
            else
            {
                phaseComplete = true;
            }

            if (phaseComplete)
            {
                _progressMessages.Add(
                    currentPhaseInfo.message.Replace("...", ""));
                _currentAsyncTask = null;

                var nextPhase = GetNextPhase(_startupPhase);
                if (nextPhase != _startupPhase)
                {
                    _startupPhase = nextPhase;
                    _phaseStartTime = _elapsedTime;
                    System.Diagnostics.Debug.WriteLine(
                        $"Startup phase changed to: {_startupPhase}");
                }
            }
        }

        private void WriteSummaryOnce()
        {
            if (_startupSummaryWritten)
                return;

            _startupSummaryWritten = true;
            _startupStopwatch?.Stop();

            var batch = _enumerationResult?.Batch;
            var import = _enumerationResult?.Import;
            var summary = new StartupSongLoadSummary(
                _selectedLoadPath,
                _songLoadOutcome,
                _startupStopwatch?.Elapsed ?? TimeSpan.Zero,
                _databaseInitializationDuration,
                batch?.DiscoveryAndParsingDuration ?? TimeSpan.Zero,
                import?.PersistenceDuration ?? TimeSpan.Zero,
                import?.CleanupDuration ?? TimeSpan.Zero,
                _enumerationResult?.HierarchyDuration ??
                    _cacheHierarchyDuration,
                batch?.DiscoveredChartPaths.Count ?? 0,
                batch?.Candidates.Count ?? 0,
                batch?.PendingSongs.Count ?? 0,
                import?.Added ?? 0,
                import?.Updated ?? 0,
                import?.Preserved ?? 0,
                import?.Skipped ?? 0,
                import?.Conflicts ?? 0,
                import?.StaleCharts ?? 0,
                _songLoadError);
            WriteStartupSummary(summary.Format());
        }

        private void PerformPhaseOperationSync(StartupPhase phase, double _)
        {
            if (_operationPerformedForPhase == phase)
                return;
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
                        var databaseTask =
                            InitializeDatabaseServiceAsync();
                        _currentAsyncTask = databaseTask;
                        TryRecordDatabaseTaskReturned(
                            ResolveCriticalPathTrace(),
                            databaseTask.IsCompleted);
                    }
                    break;

                case StartupPhase.SongsDB:
                    System.Diagnostics.Debug.WriteLine(
                        "SongsDB display phase complete.");
                    break;

                case StartupPhase.LoadScoreCache:
                    System.Diagnostics.Debug.WriteLine(
                        "LoadScoreCache display phase complete.");
                    break;

                case StartupPhase.LoadScoreFiles:
                    System.Diagnostics.Debug.WriteLine(
                        "LoadScoreFiles display phase complete.");
                    break;

                case StartupPhase.EnumerateSongs:
                    if (_currentAsyncTask == null)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "Starting song load path selection...");
                        _currentAsyncTask = RunSongLoadAsync();
                    }
                    break;

                case StartupPhase.BuildSongLists:
                    System.Diagnostics.Debug.WriteLine(
                        "Song hierarchy already produced by selected load path.");
                    break;

                case StartupPhase.SaveSongsDB:
                    MarkSongManagerInitialized();
                    System.Diagnostics.Debug.WriteLine(
                        "SongManager fully initialized.");
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
                StartupPhase.EnumerateSongs => true,
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

        private protected virtual Task<bool> InitializeDatabaseServiceCoreAsync(
            string databasePath,
            IStartupSongLoadTimingObserver? observer)
        {
            if (observer == null)
            {
                return InitializeDatabaseServiceCoreAsync(databasePath);
            }
            return _songManager.InitializeDatabaseServiceAsync(
                databasePath,
                false,
                observer);
        }

        protected virtual Task<bool> NeedsEnumerationCoreAsync(string[] songPaths, bool forceEnumeration)
        {
            return _songManager.NeedsEnumerationAsync(songPaths, forceEnumeration);
        }

        protected virtual Task<SongEnumerationResult>
            EnumerateSongsCoreAsync(
                string[] songPaths,
                IProgress<EnumerationProgress> progressReporter,
                CancellationToken cancellationToken)
        {
            return _songManager.EnumerateAndImportSongsAsync(
                songPaths,
                progressReporter,
                cancellationToken);
        }

        private protected virtual Task<SongEnumerationResult>
            EnumerateSongsCoreAsync(
                string[] songPaths,
                IProgress<EnumerationProgress> progressReporter,
                CancellationToken cancellationToken,
                IStartupSongLoadTimingObserver? observer)
        {
            if (observer == null)
            {
                return EnumerateSongsCoreAsync(
                    songPaths,
                    progressReporter,
                    cancellationToken);
            }
            return _songManager.EnumerateAndImportSongsAsync(
                songPaths,
                progressReporter,
                cancellationToken,
                observer);
        }

        protected virtual Task BuildHierarchyFromDatabaseOnceCoreAsync(
            string[] songPaths)
        {
            return _songManager.BuildHierarchyFromDatabaseOnceAsync(songPaths);
        }

        protected virtual void MarkSongManagerInitialized()
        {
            _songManager.SetInitialized();
        }

        private Task InitializeDatabaseServiceAsync()
        {
            long activationGeneration;
            CancellationToken cancellationToken;
            lock (_activationGate)
            {
                activationGeneration = _activationGeneration;
                cancellationToken =
                    _cancellationTokenSource?.Token ??
                    CancellationToken.None;
            }

            return InitializeDatabaseServiceForActivationAsync(
                activationGeneration,
                cancellationToken);
        }

        private async Task InitializeDatabaseServiceForActivationAsync(
            long activationGeneration,
            CancellationToken cancellationToken)
        {
            var criticalPathTrace = ResolveCriticalPathTrace();
            TryRecordExactlyOnce(
                criticalPathTrace,
                StartupCriticalPathMilestone.DatabaseInvoke);
            var initialization = Stopwatch.StartNew();
            var initializationFailed = false;
            try
            {
                var databasePath = GetSongsDatabasePath();
                EnsureDirectory(Path.GetDirectoryName(databasePath) ?? "");
                bool success = await InitializeDatabaseServiceCoreAsync(
                        databasePath,
                        criticalPathTrace)
                    .ConfigureAwait(false);
                initializationFailed = !success;
                System.Diagnostics.Debug.WriteLine($"Database service initialization: {(success ? "SUCCESS" : "FAILED")}");
            }
            catch (Exception ex)
            {
                initializationFailed = true;
                System.Diagnostics.Debug.WriteLine($"Error during database service initialization: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                initialization.Stop();
                TryRecordExactlyOnce(
                    criticalPathTrace,
                    StartupCriticalPathMilestone.DatabaseTerminal);
                if (initializationFailed)
                {
                    TryFailCriticalPath(
                        criticalPathTrace,
                        "database_initialization_failed",
                        nameof(StartupCriticalPathMilestone.DatabaseTerminal));
                }
                lock (_activationGate)
                {
                    if (activationGeneration == _activationGeneration &&
                        !cancellationToken.IsCancellationRequested)
                    {
                        _databaseInitializationDuration =
                            initialization.Elapsed;
                    }
                }
            }
        }

        private StartupCriticalPathTrace? ResolveCriticalPathTrace()
        {
            try
            {
                return StartupCriticalPathHost.Resolve(_game);
            }
            catch
            {
                return null;
            }
        }

        private static void TryRecordExactlyOnce(
            StartupCriticalPathTrace? trace,
            StartupCriticalPathMilestone milestone)
        {
            try
            {
                trace?.RecordExactlyOnce(milestone);
            }
            catch
            {
            }
        }

        private static void TryRecordDatabaseTaskReturned(
            StartupCriticalPathTrace? trace,
            bool wasTerminal)
        {
            try
            {
                trace?.RecordDatabaseTaskReturned(wasTerminal);
            }
            catch
            {
            }
        }

        private static void TryRecordEnumerationTaskReturned(
            StartupCriticalPathTrace? trace,
            bool wasTerminal)
        {
            try
            {
                trace?.RecordEnumerationTaskReturned(wasTerminal);
            }
            catch
            {
            }
        }

        private static void TryFailCriticalPath(
            StartupCriticalPathTrace? trace,
            string error,
            string lastMilestone,
            bool cancellation = false)
        {
            try
            {
                trace?.Fail(error, lastMilestone, cancellation);
            }
            catch
            {
            }
        }

        private Task RunSongLoadAsync()
        {
            long activationGeneration;
            CancellationToken cancellationToken;
            lock (_activationGate)
            {
                activationGeneration = _activationGeneration;
                cancellationToken =
                    _cancellationTokenSource?.Token ??
                    CancellationToken.None;
            }

            return RunSongLoadForActivationAsync(
                activationGeneration,
                cancellationToken);
        }

        private async Task RunSongLoadForActivationAsync(
            long activationGeneration,
            CancellationToken cancellationToken)
        {
            try
            {
                Task<bool> needsEnumerationTask;
                lock (_activationGate)
                {
                    EnsureCurrentActivation(
                        activationGeneration,
                        cancellationToken);
                    needsEnumerationTask =
                        NeedsEnumerationCoreAsync(
                            _songPaths,
                            ForceEnumeration);
                }

                var needsEnumeration =
                    await needsEnumerationTask.ConfigureAwait(false);
                lock (_activationGate)
                {
                    EnsureCurrentActivation(
                        activationGeneration,
                        cancellationToken);
                    _needsEnumeration = needsEnumeration;
                    _selectedLoadPath = needsEnumeration
                        ? StartupSongLoadPath.Enumeration
                        : StartupSongLoadPath.Cache;
                }

                if (!needsEnumeration)
                {
                    await BuildCacheHierarchyForActivationAsync(
                            activationGeneration,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }

                Task<SongEnumerationResult> enumerationTask;
                var criticalPathTrace = ResolveCriticalPathTrace();
                lock (_activationGate)
                {
                    EnsureCurrentActivation(
                        activationGeneration,
                        cancellationToken);
                    TryRecordExactlyOnce(
                        criticalPathTrace,
                        StartupCriticalPathMilestone.EnumerationInvoke);
                    enumerationTask = EnumerateSongsCoreAsync(
                        _songPaths,
                        CreateEnumerationProgressReporterForActivation(
                            activationGeneration,
                            cancellationToken),
                        cancellationToken,
                        criticalPathTrace);
                    TryRecordEnumerationTaskReturned(
                        criticalPathTrace,
                        enumerationTask.IsCompleted);
                }

                var enumerationResult =
                    await enumerationTask.ConfigureAwait(false);
                lock (_activationGate)
                {
                    EnsureCurrentActivation(
                        activationGeneration,
                        cancellationToken);
                    if (enumerationResult == null)
                    {
                        throw new InvalidOperationException(
                            "Song enumeration completed without " +
                            "publishing a hierarchy.");
                    }
                    _enumerationResult = enumerationResult;
                }
            }
            catch (OperationCanceledException)
            {
                lock (_activationGate)
                {
                    if (activationGeneration == _activationGeneration)
                    {
                        _songLoadOutcome =
                            StartupSongLoadOutcome.Cancellation;
                        _songLoadError = "cancelled";
                    }
                }
                throw;
            }
            catch (Exception ex)
            {
                bool isCurrentActivation;
                bool shouldFallback;
                lock (_activationGate)
                {
                    isCurrentActivation =
                        activationGeneration == _activationGeneration;
                    shouldFallback =
                        isCurrentActivation &&
                        _selectedLoadPath != StartupSongLoadPath.Cache;
                    if (isCurrentActivation)
                    {
                        _songLoadOutcome =
                            StartupSongLoadOutcome.Failure;
                        _songLoadError = ex.Message;
                    }
                }

                if (!isCurrentActivation)
                {
                    throw;
                }
                if (shouldFallback)
                {
                    await TryBuildCacheFallbackOnceForActivationAsync(
                            activationGeneration,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                throw;
            }
        }

        private async Task BuildCacheHierarchyForActivationAsync(
            long activationGeneration,
            CancellationToken cancellationToken)
        {
            var hierarchy = Stopwatch.StartNew();
            Task hierarchyTask;
            lock (_activationGate)
            {
                EnsureCurrentActivation(
                    activationGeneration,
                    cancellationToken);
                hierarchyTask =
                    BuildHierarchyFromDatabaseOnceCoreAsync(_songPaths);
            }

            await hierarchyTask.ConfigureAwait(false);
            hierarchy.Stop();
            lock (_activationGate)
            {
                EnsureCurrentActivation(
                    activationGeneration,
                    cancellationToken);
                _cacheHierarchyDuration = hierarchy.Elapsed;
            }
        }

        private async Task TryBuildCacheFallbackOnceForActivationAsync(
            long activationGeneration,
            CancellationToken cancellationToken)
        {
            lock (_activationGate)
            {
                if (activationGeneration != _activationGeneration ||
                    cancellationToken.IsCancellationRequested ||
                    _cacheFallbackAttempted)
                {
                    return;
                }
                _cacheFallbackAttempted = true;
            }

            try
            {
                await BuildCacheHierarchyForActivationAsync(
                        activationGeneration,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception fallbackException)
            {
                System.Diagnostics.Debug.WriteLine(
                    "StartupStage: committed cache fallback failed: " +
                    fallbackException.Message);
            }
        }

        private IProgress<EnumerationProgress>
            CreateEnumerationProgressReporter()
        {
            lock (_activationGate)
            {
                return CreateEnumerationProgressReporterForActivation(
                    _activationGeneration,
                    _cancellationTokenSource?.Token ??
                    CancellationToken.None);
            }
        }

        private IProgress<EnumerationProgress>
            CreateEnumerationProgressReporterForActivation(
                long activationGeneration,
                CancellationToken cancellationToken) =>
            new Progress<EnumerationProgress>(progress =>
            {
                lock (_activationGate)
                {
                    if (activationGeneration != _activationGeneration ||
                        cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var phaseInfo =
                        _phaseInfo[StartupPhase.EnumerateSongs];
                    if (!string.IsNullOrEmpty(
                        progress.CurrentOperation))
                    {
                        _currentProgressMessage =
                            $"{phaseInfo.message} " +
                            $"{progress.CurrentOperation}";
                    }
                    else if (!string.IsNullOrEmpty(progress.CurrentFile))
                    {
                        _currentProgressMessage =
                            $"{phaseInfo.message} " +
                            $"[{progress.ProcessedCount} processed, " +
                            $"{progress.DiscoveredSongs} songs] " +
                            Path.GetFileName(progress.CurrentFile);
                    }
                    else if (!string.IsNullOrEmpty(
                        progress.CurrentDirectory))
                    {
                        _currentProgressMessage =
                            $"{phaseInfo.message} Scanning directory: " +
                            Path.GetFileName(progress.CurrentDirectory);
                    }
                    else
                    {
                        _currentProgressMessage =
                            $"{phaseInfo.message} " +
                            $"[{progress.ProcessedCount} processed, " +
                            $"{progress.DiscoveredSongs} songs found]";
                    }
                }
            });

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
        /// Status-text variant of <see cref="DrawTextWithFallback"/>: selects
        /// between the bold status face and its CJK fallback via
        /// <see cref="SelectStatusFont"/>, so non-ASCII characters (e.g. song
        /// filenames in the live progress message) render correctly when a
        /// skin sets <c>Startup.StatusFontFamily</c> to a Latin-only face.
        /// </summary>
        private void DrawStatusTextWithFallback(string text, int x, int y, Color color)
        {
            var font = SelectStatusFont(text, _boldFont, _statusFallbackFont ?? _boldFont);
            if (font != null)
            {
                font.DrawString(_spriteBatch, text, new Vector2(x, y), color);
            }
            else
            {
                DrawTextRect(x, y, text.Length * FALLBACK_CHAR_WIDTH, FALLBACK_SMALL_FONT_HEIGHT, color);
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
                    // Mirror DrawStatusLine's font selection: a Latin-only
                    // Startup.StatusFontFamily cannot render non-ASCII song
                    // filenames that appear in the live message, so route
                    // through SelectStatusFont to pick the CJK fallback face.
                    DrawStatusTextWithFallback(_currentProgressMessage, x, y, ResolveStatusColor(theme));
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

#pragma warning restore CS8632
