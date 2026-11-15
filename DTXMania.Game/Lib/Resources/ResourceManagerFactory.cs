using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Resources
{
    public static class ResourceManagerFactory
    {
        /// <summary>
        /// Create a ResourceManager instance
        /// </summary>
        /// <param name="graphicsDevice">Graphics device</param>
        /// <param name="logger">Optional logger for load-failure classification;
        /// defaults to a null logger for independent tests</param>
        /// <returns>ResourceManager instance</returns>
#nullable enable
        public static ResourceManager CreateResourceManager(GraphicsDevice graphicsDevice, ILogger<ResourceManager>? logger = null)
#nullable restore
        {
            return new ResourceManager(graphicsDevice, logger);
        }
    }
}
