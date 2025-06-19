using Microsoft.Xna.Framework.Graphics;
using System;

namespace DTXMania.Test.Helpers
{
    /// <summary>
    /// Mock graphics device wrapper for unit tests
    /// </summary>
    public class MockGraphicsDevice : IDisposable
    {
        private readonly TestGraphicsDeviceService? _graphicsService;

        public GraphicsDevice? GraphicsDevice => _graphicsService?.GraphicsDevice;

        public MockGraphicsDevice()
        {
            try
            {
                _graphicsService = new TestGraphicsDeviceService();
            }
            catch (Exception)
            {
                // If we can't create a real graphics device (e.g., in CI), 
                // tests should handle null graphics devices gracefully
                _graphicsService = null;
            }
        }

        public void Dispose()
        {
            _graphicsService?.Dispose();
        }
    }
}
