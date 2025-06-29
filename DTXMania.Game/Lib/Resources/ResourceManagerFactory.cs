using Microsoft.Xna.Framework.Graphics;

namespace DTX.Resources
{
    /// <summary>
    /// Factory for creating ResourceManager instances
    /// </summary>
    public static class ResourceManagerFactory
    {
        /// <summary>
        /// Create a ResourceManager instance
        /// </summary>
        /// <param name="graphicsDevice">Graphics device</param>
        /// <returns>ResourceManager instance</returns>
        public static ResourceManager CreateResourceManager(GraphicsDevice graphicsDevice)
        {
            return new ResourceManager(graphicsDevice);
        }
    }
}
