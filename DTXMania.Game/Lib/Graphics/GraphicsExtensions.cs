using DTX.Config;

namespace DTX.Graphics
{
    /// <summary>
    /// Extension methods for graphics management
    /// </summary>
    public static class GraphicsExtensions
    {
        /// <summary>
        /// Creates graphics settings from configuration data
        /// </summary>
        /// <param name="config">Configuration data</param>
        /// <returns>Graphics settings</returns>
        public static GraphicsSettings ToGraphicsSettings(this ConfigData config)
        {
            return new GraphicsSettings
            {
                Width = config.ScreenWidth,
                Height = config.ScreenHeight,
                IsFullscreen = config.FullScreen,
                VSync = config.VSyncWait
            };
        }

        /// <summary>
        /// Updates configuration data from graphics settings
        /// </summary>
        /// <param name="config">Configuration data to update</param>
        /// <param name="settings">Graphics settings</param>
        public static void UpdateFromGraphicsSettings(this ConfigData config, GraphicsSettings settings)
        {
            config.ScreenWidth = settings.Width;
            config.ScreenHeight = settings.Height;
            config.FullScreen = settings.IsFullscreen;
            config.VSyncWait = settings.VSync;
        }

        /// <summary>
        /// Applies graphics settings and updates configuration
        /// </summary>
        /// <param name="graphicsManager">Graphics manager</param>
        /// <param name="config">Configuration to update</param>
        /// <param name="settings">Settings to apply</param>
        /// <returns>True if settings were applied successfully</returns>
        public static bool ApplySettingsAndUpdateConfig(this IGraphicsManager graphicsManager, 
            ConfigData config, GraphicsSettings settings)
        {
            if (graphicsManager.ApplySettings(settings))
            {
                config.UpdateFromGraphicsSettings(settings);
                return true;
            }
            return false;
        }
    }
}
