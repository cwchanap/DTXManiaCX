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

        public GraphicsDevice? GraphicsDevice { get; private set; }

        /// <param name="forceEnable">Create a real device even when DTXMANIACX_ENABLE_GRAPHICS_TESTS
        /// is unset; lets a fixture opt in without mutating process-global env state (xUnit runs
        /// collections in parallel, so other classes must not observe that write).</param>
        public TestGraphicsDeviceService(bool forceEnable = false)
        {
            _game = null;
            if (!forceEnable && !IsGraphicsTestEnabled())
            {
                GraphicsDevice = null;
                return;
            }

            try
            {
                _game = new TestGame();
                _game.RunOneFrame();

                GraphicsDevice = _game.GraphicsDevice;
            }
            catch (Exception)
            {
                // If we can't create a real graphics device (e.g., in CI), create a null one
                // Tests should handle null graphics devices gracefully
                GraphicsDevice = null;
            }
        }

        private static bool IsGraphicsTestEnabled()
        {
            return string.Equals(Environment.GetEnvironmentVariable("DTXMANIACX_ENABLE_GRAPHICS_TESTS"), "1", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            try
            {
                _game?.Dispose();
            }
            catch
            {
                // Swallow disposal errors in headless/CI environments
            }
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
