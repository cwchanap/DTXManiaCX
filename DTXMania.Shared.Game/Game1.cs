using DTX.Config;
using DTX.Stage;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Shared.Game;


public class BaseGame : Microsoft.Xna.Framework.Game
{
    private GraphicsDeviceManager _graphicsDeviceManager;
    private GraphicsDevice _graphicsDevice;
    private SpriteBatch _spriteBatch;
    private RenderTarget2D _renderTarget;

    public IStageManager StageManager { get; private set; }
    public IConfigManager ConfigManager { get; private set; }

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

        StageManager = new StageManager(this); // Initialize stage manager after config is loaded

        _graphicsDevice = _graphicsDeviceManager.GraphicsDevice;

        // TODO: Add your initialization logic here
        var config = ConfigManager.Config;
        _renderTarget = new RenderTarget2D(
            _graphicsDevice,
            config.ScreenWidth,
            config.ScreenHeight);
        _graphicsDeviceManager.PreferredBackBufferWidth = config.ScreenWidth;
        _graphicsDeviceManager.PreferredBackBufferHeight = config.ScreenHeight;
        _graphicsDeviceManager.IsFullScreen = config.FullScreen;
        _graphicsDeviceManager.SynchronizeWithVerticalRetrace = config.VSyncWait;
        _graphicsDeviceManager.ApplyChanges();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(_graphicsDevice);

        // TODO: use this.Content to load your game content here
        StageManager.ChangeStage(StageType.Startup);
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        // TODO: Add your update logic here
        StageManager.Update(gameTime.ElapsedGameTime.TotalSeconds); // Update stage manager after config is loaded

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        // Draw to render target first
        _graphicsDevice.SetRenderTarget(_renderTarget);
        _graphicsDevice.Clear(Color.Black);

        StageManager.Draw(gameTime.ElapsedGameTime.TotalSeconds); // Draw stage manager after config is loaded

        // Draw render target to screen
        _graphicsDevice.SetRenderTarget(null);
        _graphicsDevice.Clear(Color.Black);

        _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        _spriteBatch.Draw(_renderTarget,
            _graphicsDevice.Viewport.Bounds,
            Color.White);
        _spriteBatch.End();

        base.Draw(gameTime);
    }
}
