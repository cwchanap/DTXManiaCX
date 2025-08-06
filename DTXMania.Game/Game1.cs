using DTX.Config;
using DTX.Graphics;
using DTX.Input;
using DTXMania.Game.Lib.Input;
using DTX.Resources;
using DTX.Stage;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace DTXMania.Game;


public class BaseGame : Microsoft.Xna.Framework.Game
{
    private GraphicsDeviceManager _graphicsDeviceManager;
    private IGraphicsManager _graphicsManager;
    private SpriteBatch _spriteBatch;
    private RenderTarget2D _renderTarget;

    public IStageManager StageManager { get; protected set; }
    public IConfigManager ConfigManager { get; protected set; }
    public InputManagerCompat InputManager { get; protected set; }
    public IGraphicsManager GraphicsManager => _graphicsManager;

    public BaseGame()
    {
        _graphicsDeviceManager = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
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

        // Initialize managers that are needed before base.Initialize() calls LoadContent()
        // InputManager must be created before StageManager since stages need InputManager in their constructors
        InputManager = new InputManagerCompat(ConfigManager);
        StageManager = new StageManager(this);

        base.Initialize();

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

        // Initialize font factory after content is loaded
        ManagedFont.InitializeFontFactory(Content);

        StageManager?.ChangeStage(StageType.Startup);
    }

    protected override void Update(GameTime gameTime)
    {
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

            // Dispose other resources
            _spriteBatch?.Dispose();
            _renderTarget?.Dispose();
        }
        base.Dispose(disposing);
    }
}

public class Game1 : BaseGame
{
}
