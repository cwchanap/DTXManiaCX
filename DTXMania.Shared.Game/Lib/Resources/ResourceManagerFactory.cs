using Microsoft.Xna.Framework.Graphics;

namespace DTX.Resources
{
    /// <summary>
    /// Factory for creating ResourceManager instances with platform-specific dependencies
    /// </summary>
    public static class ResourceManagerFactory
    {
        private static IFontFactory _fontFactory;

        /// <summary>
        /// Set the platform-specific font factory
        /// This should be called by the platform-specific application during initialization
        /// </summary>
        /// <param name="fontFactory">Platform-specific font factory implementation</param>
        public static void SetFontFactory(IFontFactory fontFactory)
        {
            _fontFactory = fontFactory;
        }

        /// <summary>
        /// Create a ResourceManager with platform-specific dependencies
        /// </summary>
        /// <param name="graphicsDevice">Graphics device</param>
        /// <returns>ResourceManager instance with platform-specific font factory if available</returns>
        public static ResourceManager CreateResourceManager(GraphicsDevice graphicsDevice)
        {
            if (_fontFactory != null)
            {
                return new ResourceManager(graphicsDevice, _fontFactory);
            }
            else
            {
                // Fallback to basic ResourceManager (font loading will fail)
                return new ResourceManager(graphicsDevice);
            }
        }

        /// <summary>
        /// Check if a font factory has been configured
        /// </summary>
        public static bool HasFontFactory => _fontFactory != null;
    }
}
