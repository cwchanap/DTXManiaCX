#nullable enable

using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.JsonRpc;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Game.Lib.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DTXMania.Game;

/// <summary>
/// Base game class for DTXManiaCX that manages core game systems.
/// </summary>
/// <remarks>
/// This class coordinates:
/// - Graphics management via IGraphicsManager
/// - Stage management via IStageManager with debounced transitions
/// - Input handling via InputManagerCompat
/// - Resource management via IResourceManager
/// - Configuration via IConfigManager
/// - Optional JSON-RPC server for MCP communication
/// 
/// Uses structured logging via ILogger for diagnostics.
/// Stage transition debouncing is configured via GameConstants.StageTransition.DebounceDelaySeconds.
/// </remarks>
public class BaseGame : Microsoft.Xna.Framework.Game, IGameContext, IStageGame
{
    internal readonly record struct LoadContentServices(
        SpriteBatch SpriteBatch,
        IResourceManager ResourceManager,
        IStageManager StageManager);

    private GraphicsDeviceManager _graphicsDeviceManager;
    private IGraphicsManager _graphicsManager = null!;
    private SpriteBatch _spriteBatch = null!;
    private RenderTarget2D _renderTarget = null!;
    private readonly ILoggerFactory _loggerFactory;

    public IStageManager StageManager { get; protected set; } = null!;
    public IConfigManager ConfigManager { get; protected set; } = null!;
    public InputManagerCompat InputManager { get; protected set; } = null!;
    IInputManagerCompat? IGameContext.InputManager => InputManager;
    public IGraphicsManager GraphicsManager => _graphicsManager;
    IGraphicsManager? IGameContext.GraphicsManager => _graphicsManager;
    public IResourceManager ResourceManager { get; protected set; } = null!;

    /// <summary>Logger factory shared with subsystems (graphics, API, ...). Exposed so stages can
    /// create typed loggers that survive Release builds, unlike <c>System.Diagnostics.Debug</c>,
    /// whose calls are compiled out via <c>[Conditional("DEBUG")]</c>.</summary>
    public ILoggerFactory LoggerFactory => _loggerFactory;


    // Global stage transition debouncing
    private double _totalGameTime = 0.0;
    private double _lastStageTransitionTime = 0.0;
    
    // Logger for debugging and diagnostics
    private readonly ILogger<BaseGame> _logger = null!;

    // JSON-RPC server for MCP communication
    private JsonRpcServer? _jsonRpcServer;
    private GameApiImplementation? _gameApiImplementation;
    private CancellationTokenSource? _gameApiCancellation;
    private Task? _gameApiStartTask;

    // Main-thread action queue for thread-safe game API calls
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    // Screenshot capture: set by a background thread, fulfilled during Draw()
    private TaskCompletionSource<byte[]?>? _pendingScreenshot;
    
    /// <summary>
    /// Checks if enough time has passed since the last stage transition to allow a new one
    /// </summary>
    public bool CanPerformStageTransition()
    {
        return _totalGameTime - _lastStageTransitionTime >= GameConstants.StageTransition.DebounceDelaySeconds;
    }
    
    /// <summary>
    /// Marks that a stage transition is occurring, updating the debounce timer
    /// </summary>
    public void MarkStageTransition()
    {
        _lastStageTransitionTime = _totalGameTime;
    }

    /// <summary>
    /// Requests game process termination. Implements <see cref="IStageGame.RequestExit"/>
    /// so stages can exit through the interface without depending on the concrete type.
    /// </summary>
    public void RequestExit()
    {
        Exit();
    }

    /// <summary>
    /// Builds a <see cref="WindowTextInputSource"/> from the OS window for text input,
    /// or returns null when no window is available (headless/test environments).
    /// Implements <see cref="IStageGame.GetTextInputSource"/>.
    /// </summary>
    public ITextInputSource? GetTextInputSource()
    {
        // If no graphics manager is available (e.g. headless tests with an uninitialized game,
        // or before Initialize completes), there is no OS window to source text input from.
        // Mirrors the headless guard in <see cref="MapMouseToVirtual"/>; also avoids touching
        // <see cref="Microsoft.Xna.Framework.Game.Window"/> on an uninitialized instance, whose
        // MonoGame getter dereferences a platform field and would otherwise throw.
        if (_graphicsManager == null)
            return null;
        var window = GetGameWindow();
        return window != null ? new WindowTextInputSource(window) : null;
    }

    /// <summary>
    /// Returns the OS <see cref="GameWindow"/> used to build a <see cref="WindowTextInputSource"/>,
    /// or null when no window is available. Extracted as a seam (mirroring
    /// <see cref="TryGetViewportBounds"/>) so headless tests can override it without touching the
    /// MonoGame <see cref="Microsoft.Xna.Framework.Game.Window"/> getter, which dereferences a
    /// platform field and throws on an uninitialized instance.
    /// </summary>
    [ExcludeFromCodeCoverage]
    protected virtual GameWindow? GetGameWindow() => Window;

    void IGameContext.QueueMainThreadAction(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _mainThreadActions.Enqueue(action);
    }

    Task<byte[]?> IGameContext.CaptureScreenshotAsync()
    {
        var tcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Use Interlocked.CompareExchange to allow only one pending screenshot at a time
        var previous = Interlocked.CompareExchange(ref _pendingScreenshot, tcs, null);
        if (previous != null)
        {
            // Another screenshot is already pending; return a failure immediately
            return Task.FromResult<byte[]?>(null);
        }
        return tcs.Task;
    }

    public BaseGame()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _graphicsDeviceManager = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        // Fully qualified: this class also exposes a LoggerFactory instance property (for stages),
        // so the unqualified name would otherwise bind to that property instead of the static factory.
        _loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        });
        
        _logger = _loggerFactory.CreateLogger<BaseGame>();
    }

    [ExcludeFromCodeCoverage]
    protected override void Initialize()
    {
        // Capture the main game thread as the update thread for the debug-only
        // skin-switch assertion in ResourceManager.SetSkinPath. Initialize runs once
        // on the game loop thread, before any Update/Draw. No-op in Release builds.
        // Fully qualified because this class also exposes an IResourceManager property
        // named ResourceManager; the static method belongs to the concrete type.
        global::DTXMania.Game.Lib.Resources.ResourceManager.RegisterUpdateThread();

        // Initialize managers
        ConfigManager = new ConfigManager();
        ConfigManager.LoadConfig(AppPaths.GetConfigFilePath());

        // Ensure deferred config writes (e.g. scroll-speed changes) are flushed
        // even if the normal deactivate path is skipped during abrupt exit.
        Exiting += OnGameExiting;

        // Initialize graphics manager
        _graphicsManager = new GraphicsManager(this, _graphicsDeviceManager, _loggerFactory.CreateLogger<GraphicsManager>());

        // Apply graphics settings from config
        var config = ConfigManager.Config;
        var graphicsSettings = config.ToGraphicsSettings();

        _graphicsManager.ApplySettings(graphicsSettings);

        // Subscribe to graphics events
        _graphicsManager.SettingsChanged += OnGraphicsSettingsChanged;
        _graphicsManager.DeviceLost += OnGraphicsDeviceLost;
        _graphicsManager.DeviceReset += OnGraphicsDeviceReset;

        base.Initialize();

        // Initialize managers that are needed after base.Initialize() calls LoadContent()
        // InputManager must be created before StageManager since stages need InputManager in their constructors
        InputManager = new InputManagerCompat(ConfigManager);

        // Apply saved system key bindings on top of defaults
        ApplySavedSystemKeyBindings();

        // Initialize graphics manager after base initialization
        _graphicsManager.Initialize();

        _logger.LogInformation("Graphics Manager initialized with settings: {Settings}", _graphicsManager.Settings);

        // Create the main render target at the fixed virtual resolution (NOT the configured
        // screen resolution). Every stage draws its 1280x720-authored layout 1:1 into this
        // target; Draw() then letterbox-scales it once to fill the physical window. Sizing the
        // target to the configured resolution instead would leave stages that draw 1:1 stranded
        // in the top-left corner of a larger target.
        _renderTarget = _graphicsManager.RenderTargetManager.GetOrCreateRenderTarget(
            "MainRenderTarget",
            GameConstants.Display.VirtualWidth,
            GameConstants.Display.VirtualHeight);

        _logger.LogInformation("Main render target created: {Width}x{Height}",
            GameConstants.Display.VirtualWidth, GameConstants.Display.VirtualHeight);
    }

    protected override void LoadContent()
    {
        var services = CreateLoadContentServices();
        _spriteBatch = services.SpriteBatch;
        ResourceManager = services.ResourceManager;
        StageManager = services.StageManager;

        var config = ConfigManager.Config;
        ResourceManager.SetUseBoxDefSkin(config.UseBoxDefSkin);
        ResourceManager.SetSkinPath(config.SkinPath);

        StageManager?.ChangeStage(StageType.Startup);

        // Initialize Game API server for MCP communication if enabled
        if (TryInitializeGameApi(config))
        {
            _gameApiStartTask = QueueGameApiStartup();
        }
    }

    internal virtual LoadContentServices CreateLoadContentServices()
    {
        var resourceManager = ResourceManagerFactory.CreateResourceManager(GraphicsDevice);
        ManagedFont.InitializeFontFactory(Content);

        return new LoadContentServices(
            new SpriteBatch(GraphicsDevice),
            resourceManager,
            new StageManager(this));
    }

    internal virtual Task QueueGameApiStartup()
    {
        return StartGameApiServerAsync();
    }

    internal virtual Task StartJsonRpcServerAsync(JsonRpcServer server, CancellationToken cancellationToken)
    {
        return server.StartAsync(cancellationToken);
    }

    private void ApplySavedSystemKeyBindings()
    {
        if (ConfigManager is ConfigManager concreteConfig)
            concreteConfig.LoadSystemKeyBindings(InputManager);
    }

    private bool TryInitializeGameApi(ConfigData config)
    {
        if (!config.EnableGameApi)
            return false;

        // Security: Validate API key is present when API is enabled
        if (string.IsNullOrWhiteSpace(config.GameApiKey))
        {
            _logger.LogWarning("Game API is enabled but no API key is configured. " +
                              "API server will not start. Please set GameApiKey in Config.ini or " +
                              "delete Config.ini to auto-generate a secure key.");
            return false;
        }

        _gameApiImplementation = new GameApiImplementation(this, _loggerFactory.CreateLogger<GameApiImplementation>());
        _jsonRpcServer = new JsonRpcServer(_gameApiImplementation, config.GameApiPort, config.GameApiKey, _loggerFactory.CreateLogger<JsonRpcServer>());
        _gameApiCancellation = new CancellationTokenSource();
        return true;
    }

    private async Task StartGameApiServerAsync()
    {
        if (_jsonRpcServer == null || _gameApiCancellation == null)
            return;

        var config = ConfigManager.Config;
        var cancellationToken = _gameApiCancellation.Token;
        
        try
        {
            // Pass cancellation token to allow graceful shutdown
            await StartJsonRpcServerAsync(_jsonRpcServer, cancellationToken);
            _logger.LogInformation("JSON-RPC server started successfully on port {Port}", config.GameApiPort);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested during startup
            _logger.LogInformation("JSON-RPC server startup was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start JSON-RPC server on port {Port}. Ensure the port is not in use by another application. You can change the port in Config.ini via GameApiPort setting.", config.GameApiPort);
            // Continue without JSON-RPC server if it fails to start
        }
    }

    protected override void Update(GameTime gameTime)
    {
        // Update total game time for global debouncing
        _totalGameTime += gameTime.ElapsedGameTime.TotalSeconds;
        
        // Handle gamepad back button (but let stages handle ESC key)
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            Exit();

        // Update input manager before stage manager updates
        InputManager?.Update(gameTime.ElapsedGameTime.TotalSeconds);

        // Handle Alt+Enter for fullscreen toggle
        var keyboardState = Keyboard.GetState();
        if (ShouldToggleFullscreen(keyboardState))
        {
            _graphicsManager?.ToggleFullscreen();
        }

        // Update stage manager after config is loaded
        StageManager?.Update(gameTime.ElapsedGameTime.TotalSeconds);

        DrainMainThreadActions();
        CompleteBaseUpdate(gameTime);
    }

    internal virtual bool ShouldToggleFullscreen(KeyboardState keyboardState)
    {
        return (keyboardState.IsKeyDown(Keys.LeftAlt) || keyboardState.IsKeyDown(Keys.RightAlt)) &&
               InputManager?.IsKeyPressed((int)Keys.Enter) == true;
    }

    internal virtual void CompleteBaseUpdate(GameTime gameTime)
    {
        base.Update(gameTime);
    }

    private void DrainMainThreadActions()
    {
        // Cap at 64 actions per frame to prevent frame starvation
        const int MaxMainThreadActionsPerFrame = 64;
        int actionsProcessed = 0;
        while (actionsProcessed < MaxMainThreadActionsPerFrame && _mainThreadActions.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { _logger.LogError(ex, "Main-thread action from Game API threw an exception"); }
            actionsProcessed++;
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        if (!_graphicsManager.IsDeviceAvailable)
        {
            // Fulfill any pending screenshot with null so callers are not blocked indefinitely
            var skippedScreenshot = Interlocked.Exchange(ref _pendingScreenshot, null);
            skippedScreenshot?.TrySetResult(null);
            return;
        }

        // Ensure render target is valid before using it
        if (_renderTarget == null || _renderTarget.IsDisposed)
        {
            _renderTarget = _graphicsManager.RenderTargetManager.GetOrCreateRenderTarget(
                "MainRenderTarget",
                GameConstants.Display.VirtualWidth,
                GameConstants.Display.VirtualHeight);
        }

        // Draw to render target first
        SetDrawRenderTarget(_renderTarget);
        ClearDrawSurface(Color.Black);

        StageManager?.Draw(gameTime.ElapsedGameTime.TotalSeconds);

        // Fulfill any pending screenshot request while the render target is still bound
        // so its contents are guaranteed valid (DiscardContents permits the driver to
        // invalidate pixels once the target is unbound).
        var pendingScreenshot = Interlocked.Exchange(ref _pendingScreenshot, null);
        if (pendingScreenshot != null)
        {
            try
            {
                pendingScreenshot.SetResult(CapturePendingScreenshot(_renderTarget));
            }
            catch (Exception ex)
            {
                pendingScreenshot.SetException(ex);
            }
        }

        // Draw render target to screen
        SetDrawRenderTarget(null);
        ClearDrawSurface(Color.Black);

        if (_renderTarget != null && !_renderTarget.IsDisposed)
        {
            DrawRenderTargetToBackBuffer(_renderTarget);
        }

        CompleteBaseDraw(gameTime);
    }

    [ExcludeFromCodeCoverage]
    internal virtual void SetDrawRenderTarget(RenderTarget2D? renderTarget)
    {
        GraphicsDevice.SetRenderTarget(renderTarget);
    }

    [ExcludeFromCodeCoverage]
    internal virtual void ClearDrawSurface(Color color)
    {
        GraphicsDevice.Clear(color);
    }

    internal virtual byte[]? CapturePendingScreenshot(RenderTarget2D? renderTarget)
    {
        return CaptureRenderTargetAsPng(renderTarget);
    }

    [ExcludeFromCodeCoverage]
    internal virtual void DrawRenderTargetToBackBuffer(RenderTarget2D renderTarget)
    {
        // Scale the virtual-resolution render target up to fill the physical window while
        // preserving 16:9 aspect (adds black bars if the window is a different aspect ratio),
        // rather than stretching to the raw viewport bounds.
        var destination = CalculateLetterboxDestination(
            GraphicsDevice.Viewport.Bounds, renderTarget.Width, renderTarget.Height);

        _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        _spriteBatch.Draw(renderTarget, destination, Color.White);
        _spriteBatch.End();
    }

    /// <summary>
    /// Computes the aspect-preserving, centered destination rectangle for blitting a
    /// <paramref name="virtualWidth"/>x<paramref name="virtualHeight"/> render target into
    /// <paramref name="viewport"/>. The result fills the viewport on the fitting axis and is
    /// centered (letterboxed/pillarboxed) on the other.
    /// </summary>
    internal static Rectangle CalculateLetterboxDestination(Rectangle viewport, int virtualWidth, int virtualHeight)
    {
        // Guard against degenerate inputs (zero/negative viewport or virtual dims) that would
        // produce division by zero or negative destination sizes. Not reachable in normal
        // operation (the render target and back buffer always have positive dims), but the
        // guard makes the contract explicit and prevents NaN/negative rects if a future caller
        // passes a zero-sized viewport (e.g. a minimized window on some platforms).
        if (virtualWidth <= 0 || virtualHeight <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
            return Rectangle.Empty;

        float scale = Math.Min(viewport.Width / (float)virtualWidth, viewport.Height / (float)virtualHeight);
        int destWidth = (int)Math.Round(virtualWidth * scale);
        int destHeight = (int)Math.Round(virtualHeight * scale);
        int destX = viewport.X + (viewport.Width - destWidth) / 2;
        int destY = viewport.Y + (viewport.Height - destHeight) / 2;
        return new Rectangle(destX, destY, destWidth, destHeight);
    }

    /// <summary>
    /// Inverse of <see cref="CalculateLetterboxDestination"/>: maps a mouse position given in
    /// physical window/client pixels into the fixed <paramref name="virtualWidth"/>x
    /// <paramref name="virtualHeight"/> design space the stages author against. Returns null when
    /// the point falls outside the letterboxed destination (i.e. on the black bars), so callers
    /// can treat it as "no hit". Stages draw into the 1280x720 render target and read
    /// <see cref="GraphicsDevice.Viewport"/> during Update (which is the back buffer = window
    /// size, NOT the 1280x720 target), so raw mouse coords must be routed through this before
    /// hit-testing against design-space rectangles.
    /// </summary>
    internal static Point? WindowToVirtualCoordinates(Point windowPoint, Rectangle viewport, int virtualWidth, int virtualHeight)
    {
        var dest = CalculateLetterboxDestination(viewport, virtualWidth, virtualHeight);
        if (windowPoint.X < dest.X || windowPoint.X >= dest.Right ||
            windowPoint.Y < dest.Y || windowPoint.Y >= dest.Bottom)
            return null;

        int vx = (int)Math.Round((windowPoint.X - dest.X) * virtualWidth / (float)dest.Width);
        int vy = (int)Math.Round((windowPoint.Y - dest.Y) * virtualHeight / (float)dest.Height);
        // Clamp to virtual bounds to guard against rounding at the right/bottom edge.
        if (vx < 0) vx = 0;
        if (vy < 0) vy = 0;
        if (vx >= virtualWidth) vx = virtualWidth - 1;
        if (vy >= virtualHeight) vy = virtualHeight - 1;
        return new Point(vx, vy);
    }

    /// <summary>
    /// Instance wrapper around <see cref="WindowToVirtualCoordinates"/> using the current
    /// <see cref="GraphicsDevice.Viewport"/> (the back buffer during Update) and the fixed
    /// virtual resolution from <see cref="GameConstants.Display"/>. Stages call this during
    /// hit-testing to convert raw window mouse coords into the 1280x720 design space their
    /// rectangles are authored in.
    /// </summary>
    internal Point? MapMouseToVirtual(Point windowPoint)
    {
        // If no graphics device is available (e.g. headless tests with an uninitialized game, or
        // before Initialize completes), assume a 1:1 window->virtual mapping so hit-testing
        // against design-space rects still works instead of crashing.
        if (_graphicsManager == null)
            return windowPoint;
        var viewport = TryGetViewportBounds();
        if (viewport == null)
            return windowPoint;
        return WindowToVirtualCoordinates(windowPoint, viewport.Value,
            GameConstants.Display.VirtualWidth, GameConstants.Display.VirtualHeight);
    }

    /// <summary>
    /// Explicit <see cref="IStageGame.MapMouseToVirtual"/> implementation. The implicit
    /// implementer above stays <c>internal</c> so existing in-game callers keep their current
    /// access level; this thin forwarding member lets <see cref="BaseGame"/> satisfy the
    /// public <see cref="IStageGame"/> contract without widening the original method.
    /// </summary>
    Point? IStageGame.MapMouseToVirtual(Point windowPoint) => MapMouseToVirtual(windowPoint);

    /// <summary>
    /// Returns the current back-buffer viewport bounds (used by <see cref="MapMouseToVirtual"/>),
    /// or null when no <see cref="GraphicsDevice"/> is available (headless tests / pre-Initialize).
    /// Extracted as a seam so headless tests can override it with a known rectangle instead of
    /// requiring a live GraphicsDevice.
    /// </summary>
    [ExcludeFromCodeCoverage]
    protected virtual Rectangle? TryGetViewportBounds()
    {
        var graphicsDevice = GraphicsDevice;
        return graphicsDevice == null ? null : graphicsDevice.Viewport.Bounds;
    }

    [ExcludeFromCodeCoverage]
    internal virtual void CompleteBaseDraw(GameTime gameTime)
    {
        base.Draw(gameTime);
    }

    private static byte[]? CaptureRenderTargetAsPng(RenderTarget2D? renderTarget)
    {
        if (renderTarget == null || renderTarget.IsDisposed)
            return null;

        using var stream = new MemoryStream();
        renderTarget.SaveAsPng(stream, renderTarget.Width, renderTarget.Height);
        return stream.ToArray();
    }

    private void OnGraphicsSettingsChanged(object? sender, GraphicsSettingsChangedEventArgs e)
    {
        // Update configuration when graphics settings change
        ConfigManager.Config.UpdateFromGraphicsSettings(e.NewSettings);

        // Log the change
        _logger.LogInformation("Graphics settings changed: {OldSettings} -> {NewSettings}", e.OldSettings, e.NewSettings);

        // Always recreate render target when graphics settings change
        // This handles resolution changes, fullscreen toggle, and other graphics changes
        try
        {
            // Dispose old render target if it exists
            if (_renderTarget != null && !_renderTarget.IsDisposed)
            {
                _renderTarget.Dispose();
            }

            // Recreate at the fixed virtual resolution. The render target is decoupled from the
            // display resolution; the new physical size only affects the letterbox blit in Draw().
            _renderTarget = _graphicsManager.RenderTargetManager.GetOrCreateRenderTarget(
                "MainRenderTarget",
                GameConstants.Display.VirtualWidth,
                GameConstants.Display.VirtualHeight);

            _logger.LogInformation("Render target recreated: {Width}x{Height}",
                GameConstants.Display.VirtualWidth, GameConstants.Display.VirtualHeight);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recreating render target");
            _renderTarget = null; // Will be recreated in Draw() method
        }
    }

    private void OnGraphicsDeviceLost(object? sender, EventArgs e)
    {
        // Handle device lost scenario
        // For now, just log that it happened
        _logger.LogWarning("Graphics device lost");
    }

    private void OnGameExiting(object? sender, EventArgs e)
    {
        // Belt-and-suspenders: ensure any deferred config write (e.g. scroll-speed)
        // is flushed even if the normal stage-deactivation path is skipped.
        try
        {
            if (ConfigManager is ConfigManager concrete)
                concrete.FlushPendingSave();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush pending config save on exit");
        }
    }

    private void OnGraphicsDeviceReset(object? sender, EventArgs e)
    {
        // Handle device reset scenario
        // Render targets are automatically recreated by the graphics manager
        _logger.LogInformation("Graphics device reset");

        // Ensure our main render target is recreated after device reset
        try
        {
            _renderTarget = _graphicsManager.RenderTargetManager.GetOrCreateRenderTarget(
                "MainRenderTarget",
                GameConstants.Display.VirtualWidth,
                GameConstants.Display.VirtualHeight);

            _logger.LogInformation("Main render target recreated after device reset");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recreating render target after device reset");
            _renderTarget = null; // Will be recreated in Draw() method
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeManagedResources();
        }
        base.Dispose(disposing);
    }

    private void DisposeManagedResources()
    {
        // Unsubscribe from Game.Exiting to avoid double-flush during normal shutdown
        Exiting -= OnGameExiting;

        // Dispose StageManager first to properly cleanup all stages
        if (StageManager != null)
        {
            StageManager.Dispose();
            StageManager = null!;
        }

        // Dispose other managers
        if (_graphicsManager != null)
        {
            _graphicsManager.SettingsChanged -= OnGraphicsSettingsChanged;
            _graphicsManager.DeviceLost -= OnGraphicsDeviceLost;
            _graphicsManager.DeviceReset -= OnGraphicsDeviceReset;
            _graphicsManager.Dispose();
        }

        // Dispose resource manager
        ResourceManager?.Dispose();

        // Stop and dispose game API server
        if (_gameApiCancellation is not null)
        {
            _gameApiCancellation.Cancel();
        }

        if (_gameApiStartTask is not null)
        {
            try
            {
                _gameApiStartTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for JSON-RPC server startup to complete");
            }
            finally
            {
                _gameApiStartTask = null;
            }
        }

        if (_gameApiCancellation is not null)
        {
            _gameApiCancellation.Dispose();
            _gameApiCancellation = null;
        }

        if (_jsonRpcServer != null)
        {
            try
            {
                // Use GetAwaiter().GetResult() instead of .Wait() to avoid potential deadlocks
                // This properly propagates exceptions and is safer in synchronous dispose contexts
                // Note: StopAsync has a built-in timeout, so we don't need an external CancellationToken
                _jsonRpcServer.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping JSON-RPC server");
            }
            finally
            {
                _jsonRpcServer.Dispose();
                _jsonRpcServer = null;
            }
        }

        // Complete any pending screenshot task to prevent callers from blocking indefinitely
        var pendingScreenshot = Interlocked.Exchange(ref _pendingScreenshot, null);
        if (pendingScreenshot != null)
        {
            try { pendingScreenshot.TrySetCanceled(); }
            catch { /* Best effort - task may already be completed */ }
        }

        // Dispose other resources
        _spriteBatch?.Dispose();
        _renderTarget?.Dispose();
        _loggerFactory.Dispose();
    }
}

public class Game1 : BaseGame
{
}
