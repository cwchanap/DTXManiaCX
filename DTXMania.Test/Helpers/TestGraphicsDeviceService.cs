using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Runtime.InteropServices;

namespace DTXMania.Test.Helpers
{
    /// <summary>
    /// Simple test graphics device service for unit tests
    /// </summary>
    public class TestGraphicsDeviceService : IDisposable
    {
        private readonly Microsoft.Xna.Framework.Game? _game;
        private readonly GraphicsDeviceManager? _graphicsDeviceManager;

        public GraphicsDevice? GraphicsDevice { get; private set; }

        public TestGraphicsDeviceService()
        {
            _game = null;
            _graphicsDeviceManager = null;

            try
            {
                _game = new TestGame();
                _graphicsDeviceManager = new GraphicsDeviceManager(_game);
                _game.RunOneFrame();

                GraphicsDevice = _graphicsDeviceManager.GraphicsDevice;
            }
            catch (Exception)
            {
                // If we can't create a real graphics device (e.g., in CI), create a null one
                // Tests should handle null graphics devices gracefully
                GraphicsDevice = null;
            }
        }

        public void Dispose()
        {
            _graphicsDeviceManager?.Dispose();
            _game?.Dispose();
        }

        private class TestGame : Microsoft.Xna.Framework.Game
        {
            private readonly GraphicsDeviceManager _graphics;

            public TestGame()
            {
                _graphics = new GraphicsDeviceManager(this);
                Content.RootDirectory = "Content";
            }

            protected override void LoadContent()
            {
                // No content to load for tests
            }

            protected override void Update(GameTime gameTime)
            {
                // No update logic needed for tests
            }

            protected override void Draw(GameTime gameTime)
            {
                // No drawing needed for tests
            }
        }
    }
}
