using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Update;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Game;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
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

        // HPA-796: the stage's cached white pixel (ITexture); _whitePixel is its borrowed raw texture.
        private ITexture _whitePixelResource;
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

        private string[] _songPaths = Constants.SongPaths.Default;
        private readonly string _bundledSongsRoot = BundledSongs.Root;

        // Which phase's kick-off has already run (each phase's operation runs once,
        // no matter how late the first update of that phase arrives).
        private StartupPhase? _operationPerformedForPhase;

        // Terminal outcome of the current async phase's worker chain (HPA-799).
        // The scope posts exactly one success/error result per phase onto the
        // update thread; async phases wait for it instead of polling a Task.
        // Null until the posted result applies.
        private bool? _asyncPhaseSucceeded;

        // Loading simulation (since we don't have actual song loading yet)
        private readonly Dictionary<StartupPhase, (string message, double duration)> _phaseInfo;
        private double _phaseStartTime;

        private bool _hasRenderedStartupFrame;
        private bool _titleTransitionRequested;

        #endregion

        #region Properties

        public override StageType Type => StageType.Startup;

        private ISkinTheme Theme => _resourceManager?.CurrentTheme ?? SkinTheme.Empty;

        /// <summary>
        /// When false (production), SongManager's freshness check decides between a
        /// full scan and the songs.db cache, so an unchanged library skips the scan.
        /// </summary>
        protected virtual bool ForceEnumeration => false;

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

        #endregion

        #region BaseStage Implementation

        protected override void OnActivate()
        {

            _hasRenderedStartupFrame = false;
            _titleTransitionRequested = false;

            // Initialize graphics resources
            var graphicsDevice = GetGraphicsDeviceCore();
            _spriteBatch = CreateSpriteBatchCore(graphicsDevice);

            // Initialize ResourceManager using factory
            _resourceManager = _game.ResourceManager;

            try
            {
                // HPA-796: acquire the cached white pixel once per activation; no private 1×1 texture
                _whitePixelResource = _resourceManager?.CreateTextureFromColor(Color.White);
                _whitePixel = _whitePixelResource?.Texture;

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
                catch (Exception)
                {
                    _statusFallbackFont = null;
                }
            }
            catch
            {
                ReleaseStartupFonts();
                _whitePixelResource?.RemoveReference();
                _whitePixelResource = null;
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
            _operationPerformedForPhase = null; // Reset per-phase op guard (see PerformPhaseOperationSync)

            // Add initial messages (DTXMania pattern)
            _progressMessages.Add("DTXMania powered by YAMAHA Silent Session Drums");

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
                _game.ReportStartupSummaryAndTitleRequested();
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
            // Report the first rendered frame exactly once; subsequent draws
            // must not re-emit the startup-frame-rendered notification.
            if (!_hasRenderedStartupFrame)
            {
                _hasRenderedStartupFrame = true;
                _game.ReportStartupFrameRendered();
            }
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

            // The activation scope was retired (and pending work dropped) by
            // BaseStage.Deactivate before this hook; only UI resources go here.

            // Release font references (re-acquired on re-activation)
            _font?.RemoveReference();
            _font = null;
            _boldFont?.RemoveReference();
            _boldFont = null;
            _statusFallbackFont?.RemoveReference();
            _statusFallbackFont = null;

            // HPA-796: release the cached white pixel acquired on activation (previously only
            // the fonts were released here, so a Startup → Title → Startup round trip overwrote
            // an outstanding reference). Dispose's null-safe release still guards disposal
            // without deactivation.
            _whitePixelResource?.RemoveReference();
            _whitePixelResource = null;
            _whitePixel = null;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // This override body runs BEFORE base.Dispose deactivates, so retire
                // the activation scope here first: pending owned work is dropped before
                // any resource below is released. Idempotent — a deactivated stage has
                // already retired its scope.
                RetireBackgroundWork();

                // Cleanup MonoGame resources - using reference counting for managed textures
                // Font and cached white pixel refs are released in OnDeactivate (called by
                // BaseStage.Dispose → Deactivate) but guard against disposal without deactivation
                _font?.RemoveReference();
                _boldFont?.RemoveReference();
                _statusFallbackFont?.RemoveReference();
                _whitePixelResource?.RemoveReference();
                _spriteBatch?.Dispose();

                _font = null;
                _boldFont = null;
                _statusFallbackFont = null;
                _whitePixel = null;
                _whitePixelResource = null;
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
            }
            catch (Exception)
            {
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
            PerformPhaseOperationSync(
                _startupPhase,
                _elapsedTime - _phaseStartTime);

            bool phaseComplete;
            if (HasAsyncOperation(_startupPhase))
            {
                // Async phases advance only on the scope's posted terminal result
                // (HPA-799); the worker catches faults before posting, so there is
                // no task state to poll here.
                if (_asyncPhaseSucceeded == null)
                {
                    return;
                }

                phaseComplete = true;
            }
            else
            {
                phaseComplete = true;
            }

            if (phaseComplete)
            {
                _progressMessages.Add(
                    currentPhaseInfo.message.Replace("...", ""));

                var nextPhase = GetNextPhase(_startupPhase);
                if (nextPhase != _startupPhase)
                {
                    _startupPhase = nextPhase;
                    _phaseStartTime = _elapsedTime;
                }
            }
        }

        private void PerformPhaseOperationSync(StartupPhase phase, double _)
        {
            if (_operationPerformedForPhase == phase)
                return;
            _operationPerformedForPhase = phase;

            // Set the default status message on phase ENTRY only (HPA-799): later
            // updates must not erase Latest progress reports before Draw.
            _currentProgressMessage = _phaseInfo[phase].message;
            _asyncPhaseSucceeded = null;

            switch (phase)
            {
                case StartupPhase.SystemSounds:
                    // Load system sounds (placeholder)
                    break;

                case StartupPhase.ConfigValidation:
                    // Load and validate configuration
                    var config = _configManager.Config;
                    if (config != null)
                    {
                        _songPaths = BundledSongs.AppendTo(config.SongRoots, _bundledSongsRoot);

                        // Basic validation - check if config loaded successfully
                        bool isValid = config.ScreenWidth > 0 &&
                                    config.ScreenHeight > 0;

                    }
                    else
                    {
                    }
                    break;

                case StartupPhase.SongListDB:
                    StartDatabaseInitialization();
                    break;

                case StartupPhase.SongsDB:
                    break;

                case StartupPhase.LoadScoreCache:
                    break;

                case StartupPhase.LoadScoreFiles:
                    break;

                case StartupPhase.EnumerateSongs:
                    StartSongEnumeration();
                    break;

                case StartupPhase.BuildSongLists:
                    break;

                case StartupPhase.SaveSongsDB:
                    MarkSongManagerInitialized();
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

        protected virtual Task BuildHierarchyFromDatabaseOnceCoreAsync(
            string[] songPaths)
        {
            return _songManager.BuildHierarchyFromDatabaseOnceAsync(songPaths);
        }

        protected virtual void MarkSongManagerInitialized()
        {
            _songManager.SetInitialized();
        }

        protected virtual void PublishEmptyLibraryCore()
        {
            _songManager.PublishEmptyLibrary();
        }

        private async Task InitializeDatabaseServiceAsync()
        {
            try
            {
                var databasePath = GetSongsDatabasePath();
                EnsureDirectory(Path.GetDirectoryName(databasePath) ?? "");
                bool success = await InitializeDatabaseServiceCoreAsync(
                        databasePath)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Database service initialization failed");
            }
        }

        /// <summary>
        /// Starts the SongListDB phase's worker chain on the stage scope (HPA-799).
        /// The chain never faults: InitializeDatabaseServiceAsync catches and logs
        /// all failures, so the phase completes either way (startup continues on
        /// error as before).
        /// </summary>
        private void StartDatabaseInitialization()
        {
            var scope = BackgroundWork!;
            var phaseMessage = _phaseInfo[StartupPhase.SongListDB].message;
            _currentProgressMessage = $"{phaseMessage} (in progress)";

            scope.Run<bool>(
                _ =>
                {
                    InitializeDatabaseServiceAsync().GetAwaiter().GetResult();
                    return true;
                },
                onSuccess: _ =>
                {
                    _asyncPhaseSucceeded = true;
                    _currentProgressMessage =
                        $"{phaseMessage.Replace("...", "")} - Complete";
                },
                onError: exception =>
                {
                    _asyncPhaseSucceeded = false;
                    _currentProgressMessage =
                        $"{phaseMessage.Replace("...", "")} - Error";
                    Logger.LogWarning(
                        exception,
                        "Startup phase {Phase} failed; continuing startup",
                        StartupPhase.SongListDB);
                });
        }

        /// <summary>
        /// Starts the EnumerateSongs phase's worker chain on the stage scope
        /// (HPA-799). One worker-local multi-step chain runs needs-enumeration,
        /// enumeration and the cache fallback; it publishes the library as before
        /// and posts exactly one terminal result via the scope's success/error
        /// callbacks. Progress streams through a Latest reporter so at most one
        /// status update is queued per frame.
        /// </summary>
        private void StartSongEnumeration()
        {
            // Snapshot update-thread state before Run: the scope field can be
            // retired (and nulled) while the worker is still finishing.
            var scope = BackgroundWork!;
            var songPaths = _songPaths;
            var phaseMessage = _phaseInfo[StartupPhase.EnumerateSongs].message;
            _currentProgressMessage = $"{phaseMessage} (in progress)";
            IProgress<EnumerationProgress> progress = new InlineProgress<EnumerationProgress>(
                scope.Latest<EnumerationProgress>(ApplyEnumerationProgress));

            scope.Run<bool>(
                _ =>
                {
                    try
                    {
                        return RunSongLoadChainAsync(songPaths, progress, scope.Token)
                            .GetAwaiter()
                            .GetResult();
                    }
                    catch (OperationCanceledException cancellation)
                        when (!scope.Token.IsCancellationRequested)
                    {
                        // Cancellation independent of stage retirement (e.g.
                        // SongManager.CancelEnumeration) must still publish a
                        // terminal outcome or the phase would never advance;
                        // retirement stays silent (Run swallows that OCE).
                        throw new InvalidOperationException(
                            "Song enumeration was canceled.",
                            cancellation);
                    }
                },
                onSuccess: _ =>
                {
                    _asyncPhaseSucceeded = true;
                    _currentProgressMessage =
                        $"{phaseMessage.Replace("...", "")} - Complete";
                },
                onError: exception =>
                {
                    _asyncPhaseSucceeded = false;
                    _currentProgressMessage =
                        $"{phaseMessage.Replace("...", "")} - Error";
                    // The phase still advances below (startup must not hang), so
                    // this posted error is the only place the failure is surfaced.
                    Logger.LogWarning(
                        exception,
                        "Startup phase {Phase} failed; continuing startup",
                        StartupPhase.EnumerateSongs);
                });
        }

        /// <summary>
        /// The worker-local enumeration chain. Activation fencing is the scope
        /// token: retirement cancels it, and every checkpoint after an await
        /// rethrows cancellation so stale results are never applied.
        /// </summary>
        private async Task<bool> RunSongLoadChainAsync(
            string[] songPaths,
            IProgress<EnumerationProgress> progress,
            CancellationToken cancellationToken)
        {
            // Chain-local guard state: the chain runs once per activation, so a
            // local preserves the exactly-once cache-fallback contract.
            var cacheLoadPathSelected = false;
            var cacheFallbackAttempted = false;
            try
            {
                if (songPaths.Length == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PublishEmptyLibraryCore();
                    return true;
                }

                var needsEnumeration =
                    await NeedsEnumerationCoreAsync(songPaths, ForceEnumeration)
                        .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                cacheLoadPathSelected = !needsEnumeration;

                if (!needsEnumeration)
                {
                    await BuildHierarchyFromDatabaseOnceCoreAsync(songPaths)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                }

                var enumerationResult = await EnumerateSongsCoreAsync(
                        songPaths,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (enumerationResult == null)
                {
                    throw new InvalidOperationException(
                        "Song enumeration completed without " +
                        "publishing a hierarchy.");
                }

                if (enumerationResult.Outcome ==
                    SongEnumerationOutcome.NoActiveRoots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PublishEmptyLibraryCore();
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Cache fallback keeps startup alive on enumeration failures: one
                // attempt per chain, never after the cache path was selected and
                // never after retirement. The original failure is rethrown so Run
                // posts it to the update thread.
                if (cacheLoadPathSelected ||
                    cacheFallbackAttempted ||
                    cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                cacheFallbackAttempted = true;
                try
                {
                    await BuildHierarchyFromDatabaseOnceCoreAsync(songPaths)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Fallback failures never mask the original enumeration error.
                }
                throw;
            }
        }

        private void ApplyEnumerationProgress(EnumerationProgress progress)
        {
            var phaseInfo = _phaseInfo[StartupPhase.EnumerateSongs];
            if (!string.IsNullOrEmpty(progress.CurrentOperation))
            {
                _currentProgressMessage =
                    $"{phaseInfo.message} {progress.CurrentOperation}";
            }
            else if (!string.IsNullOrEmpty(progress.CurrentFile))
            {
                _currentProgressMessage =
                    $"{phaseInfo.message} " +
                    $"[{progress.ProcessedCount} processed, " +
                    $"{progress.DiscoveredSongs} songs] " +
                    Path.GetFileName(progress.CurrentFile);
            }
            else if (!string.IsNullOrEmpty(progress.CurrentDirectory))
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
            string versionText = $"DTXManiaCX v{ApplicationVersion.DisplayWithPrerelease} - MonoGame Edition";
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

    /// <summary>
    /// Synchronous <see cref="IProgress{T}"/> adapter that hands each report to the
    /// stage scope's Latest reporter, so at most one status update per frame is
    /// queued for the update thread (no SynchronizationContext involved).
    /// </summary>
    internal sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(Action<T> report)
        {
            _report = report ?? throw new ArgumentNullException(nameof(report));
        }

        public void Report(T value) => _report(value);
    }
}

#pragma warning restore CS8632