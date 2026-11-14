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
        private readonly Exception? _deviceCreationError;

        public GraphicsDevice? GraphicsDevice { get; private set; }

        /// <summary>
        /// The device a device-required test needs. Throws when no device is available
        /// (either because the opt-in env var was not set or because device creation
        /// failed despite the opt-in), so the test fails instead of silently passing.
        /// </summary>
        public GraphicsDevice RequiredDevice =>
            GraphicsDevice ?? throw CreateMissingDeviceException();

        /// <summary>
        /// Builds a diagnostic exception explaining why no <see cref="GraphicsDevice"/>
        /// is available, distinguishing missing opt-in from device initialization failure.
        /// </summary>
        public InvalidOperationException CreateMissingDeviceException()
        {
            if (!GraphicsTestGate.IsEnabled)
            {
                return new InvalidOperationException(
                    $"{GraphicsTestGate.OptInVariable} is not set to 1, so no GraphicsDevice was created.");
            }

            return new InvalidOperationException(
                $"{GraphicsTestGate.OptInVariable}=1 was set but no GraphicsDevice could be created.",
                _deviceCreationError);
        }

        public TestGraphicsDeviceService()
        {
            _game = null;
            if (!GraphicsTestGate.IsEnabled)
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
            catch (Exception ex)
            {
                // Dispose the partially initialized TestGame immediately so callers that
                // throw out of a test constructor before Dispose() runs do not leak it.
                try
                {
                    _game?.Dispose();
                }
                catch
                {
                    // Swallow disposal errors in headless/CI environments
                }
                _game = null;

                // Device-required tests surface this through RequiredDevice; tests that
                // do not need graphics keep working without a device.
                _deviceCreationError = ex;
                GraphicsDevice = null;
            }
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
