using DTXMania.Game.Lib;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.JsonRpc;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DTXMania.Game;


public class BaseGame : Microsoft.Xna.Framework.Game, IGameContext
{
    private GraphicsDeviceManager _graphicsDeviceManager;
    private IGraphicsManager _graphicsManager;
    private SpriteBatch _spriteBatch;
    private RenderTarget2D _renderTarget;
    private readonly ILoggerFactory _loggerFactory;

    public IStageManager StageManager { get; protected set; }
    public IConfigManager ConfigManager { get; protected set; }
    public InputManagerCompat InputManager { get; protected set; }
    public IGraphicsManager GraphicsManager => _graphicsManager;
    public IResourceManager ResourceManager { get; protected set; }

    
    // Global stage transition debouncing
    private double _totalGameTime = 0.0;
    private double _lastStageTransitionTime = 0.0;
    private const double GLOBAL_STAGE_TRANSITION_DEBOUNCE_DELAY = 0.5; // 500ms debounce

    // JSON-RPC server for MCP communication
    private JsonRpcServer? _jsonRpcServer;
    private GameApiImplementation? _gameApiImplementation;
    private CancellationTokenSource? _gameApiCancellation;
    
    /// <summary>
    /// Checks if enough time has passed since the last stage transition to allow a new one
    /// </summary>
    public bool CanPerformStageTransition()
    {
        return _totalGameTime - _lastStageTransitionTime >= GLOBAL_STAGE_TRANSITION_DEBOUNCE_DELAY;
    }
    
    /// <summary>
    /// Marks that a stage transition is occurring, updating the debounce timer
    /// </summary>
    public void MarkStageTransition()
    {
        _lastStageTransitionTime = _totalGameTime;
    }

    public BaseGame()
    {
        _graphicsDeviceManager = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        });
    }

    protected override void Initialize()
    {
        // Initialize managers
        ConfigManager = new ConfigManager();
        ConfigManager.LoadConfig("Config.ini");

        // Initialize graphics manager
        _graphicsManager = new GraphicsManager(this, _graphicsDeviceManager);

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

        // Initialize graphics manager after base initialization
        _graphicsManager.Initialize();

        System.Diagnostics.Debug.WriteLine($"Graphics Manager initialized with settings: {_graphicsManager.Settings}");

        // Create main render target using the graphics manager
        _renderTarget = _graphicsManager.RenderTargetManager.GetOrCreateRenderTarget(
            "MainRenderTarget",
            config.ScreenWidth,
            config.ScreenHeight);

        System.Diagnostics.Debug.WriteLine($"Main render target created: {config.ScreenWidth}x{config.ScreenHeight}");
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // Initialize shared resource manager
        ResourceManager = ResourceManagerFactory.CreateResourceManager(GraphicsDevice);

        // Initialize font factory after content is loaded
        ManagedFont.InitializeFontFactory(Content);

        // Initialize StageManager after ResourceManager is available
        StageManager = new StageManager(this);

        StageManager?.ChangeStage(StageType.Startup);

        // Initialize Game API server for MCP communication if enabled
        var config = ConfigManager.Config;
        if (config.EnableGameApi)
        {
            // Security: Validate API key is present when API is enabled
            if (string.IsNullOrEmpty(config.GameApiKey))
            {
                System.Diagnostics.Debug.WriteLine("WARNING: Game API is enabled but no API key is configured. " +
                                                  "API server will not start. Please set GameApiKey in Config.ini or " +
                                                  "delete Config.ini to auto-generate a secure key.");
            }
            else
            {
                _gameApiImplementation = new GameApiImplementation(this, _loggerFactory.CreateLogger<GameApiImplementation>());
                _jsonRpcServer = new JsonRpcServer(_gameApiImplementation, config.GameApiPort, config.GameApiKey, _loggerFactory.CreateLogger<JsonRpcServer>());
                _gameApiCancellation = new CancellationTokenSource();
                
                // Start API server with proper error handling
                _ = StartGameApiServerAsync();
            }
        }
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
            await _jsonRpcServer.StartAsync(cancellationToken);
            System.Diagnostics.Debug.WriteLine($"JSON-RPC server started successfully on port {config.GameApiPort}");
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested during startup
            System.Diagnostics.Debug.WriteLine("JSON-RPC server startup was cancelled");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start JSON-RPC server on port {config.GameApiPort}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine("Troubleshooting: Ensure the port is not in use by another application. " +
                                              $"You can change the port in Config.ini via GameApiPort setting.");
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
        if ((keyboardState.IsKeyDown(Keys.LeftAlt) || keyboardState.IsKeyDown(Keys.RightAlt)) &&
            InputManager?.IsKeyPressed((int)Keys.Enter) == true)
        {
            _graphicsManager?.ToggleFullscreen();
        }

        // Update stage manager after config is loaded
        StageManager?.Update(gameTime.ElapsedGameTime.TotalSeconds);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (!_graphicsManager.IsDeviceAvailable)
            return;

        // Ensure render target is valid before using it
        if (_renderTarget == null || _renderTarget.IsDisposed)
        {
            var config = ConfigManager.Config;
            _renderTarget = _graphicsManager.RenderTargetManager.GetOrCreateRenderTarget(
                "MainRenderTarget",
                config.ScreenWidth,
                config.ScreenHeight);
        }

        // Draw to render target first
        GraphicsDevice.SetRenderTarget(_renderTarget);
        GraphicsDevice.Clear(Color.Black);

        StageManager?.Draw(gameTime.ElapsedGameTime.TotalSeconds);

        // Draw render target to screen
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);

        if (_renderTarget != null && !_renderTarget.IsDisposed)
        {
            _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
            _spriteBatch.Draw(_renderTarget,
                GraphicsDevice.Viewport.Bounds,
                Color.White);
            _spriteBatch.End();
        }

        base.Draw(gameTime);
    }

    private void OnGraphicsSettingsChanged(object sender, GraphicsSettingsChangedEventArgs e)
    {
        // Update configuration when graphics settings change
        ConfigManager.Config.UpdateFromGraphicsSettings(e.NewSettings);

        // Log the change
        System.Diagnostics.Debug.WriteLine($"Graphics settings changed: {e.OldSettings} -> {e.NewSettings}");

        // Always recreate render target when graphics settings change
        // This handles resolution changes, fullscreen toggle, and other graphics changes
        try
        {
            // Dispose old render target if it exists
            if (_renderTarget != null && !_renderTarget.IsDisposed)
            {
                _renderTarget.Dispose();
            }

            // Create new render target with current settings
            _renderTarget = _graphicsManager.RenderTargetManager.GetOrCreateRenderTarget(
                "MainRenderTarget",
                e.NewSettings.Width,
                e.NewSettings.Height);

            System.Diagnostics.Debug.WriteLine($"Render target recreated: {e.NewSettings.Width}x{e.NewSettings.Height}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error recreating render target: {ex.Message}");
            _renderTarget = null; // Will be recreated in Draw() method
        }
    }

    private void OnGraphicsDeviceLost(object sender, EventArgs e)
    {
        // Handle device lost scenario
        // For now, just log that it happened
        System.Diagnostics.Debug.WriteLine("Graphics device lost");
    }

    private void OnGraphicsDeviceReset(object sender, EventArgs e)
    {
        // Handle device reset scenario
        // Render targets are automatically recreated by the graphics manager
        System.Diagnostics.Debug.WriteLine("Graphics device reset");

        // Ensure our main render target is recreated after device reset
        try
        {
            var config = ConfigManager.Config;
            _renderTarget = _graphicsManager.RenderTargetManager.GetOrCreateRenderTarget(
                "MainRenderTarget",
                config.ScreenWidth,
                config.ScreenHeight);

            System.Diagnostics.Debug.WriteLine("Main render target recreated after device reset");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error recreating render target after device reset: {ex.Message}");
            _renderTarget = null; // Will be recreated in Draw() method
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Dispose StageManager first to properly cleanup all stages
            if (StageManager != null)
            {
                StageManager.Dispose();
                StageManager = null;
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
                    System.Diagnostics.Debug.WriteLine($"Error stopping JSON-RPC server: {ex.Message}");
                }
                finally
                {
                    _jsonRpcServer.Dispose();
                    _jsonRpcServer = null;
                }
            }

            // Dispose other resources
            _spriteBatch?.Dispose();
            _renderTarget?.Dispose();
            _loggerFactory.Dispose();
        }
        base.Dispose(disposing);
    }
}

public class Game1 : BaseGame
{
}
