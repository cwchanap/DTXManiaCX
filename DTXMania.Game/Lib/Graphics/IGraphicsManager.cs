using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DTXMania.Game.Lib.Graphics
{
    /// <summary>
    /// Interface for managing graphics device settings and state
    /// </summary>
    public interface IGraphicsManager : IDisposable
    {
        /// <summary>
        /// Gets the current graphics device
        /// </summary>
        GraphicsDevice GraphicsDevice { get; }

        /// <summary>
        /// Gets the current graphics settings
        /// </summary>
        GraphicsSettings Settings { get; }

        /// <summary>
        /// Gets whether the graphics device is currently available
        /// </summary>
        bool IsDeviceAvailable { get; }

        /// <summary>
        /// Gets the render target manager for this graphics manager
        /// </summary>
        RenderTargetManager RenderTargetManager { get; }

        /// <summary>
        /// Event fired when graphics settings are changed
        /// </summary>
        event EventHandler<GraphicsSettingsChangedEventArgs> SettingsChanged;

        /// <summary>
        /// Event fired when the graphics device is lost
        /// </summary>
        event EventHandler DeviceLost;

        /// <summary>
        /// Event fired when the graphics device is reset/restored
        /// </summary>
        event EventHandler DeviceReset;

        /// <summary>
        /// Initializes the graphics manager (call after base.Initialize())
        /// </summary>
        void Initialize();

        /// <summary>
        /// Applies new graphics settings
        /// </summary>
        /// <param name="settings">The new settings to apply</param>
        /// <returns>True if settings were applied successfully</returns>
        bool ApplySettings(GraphicsSettings settings);

        /// <summary>
        /// Changes the screen resolution
        /// </summary>
        /// <param name="width">New width</param>
        /// <param name="height">New height</param>
        /// <returns>True if resolution was changed successfully</returns>
        bool ChangeResolution(int width, int height);

        /// <summary>
        /// Toggles fullscreen mode
        /// </summary>
        /// <returns>True if fullscreen mode was toggled successfully</returns>
        bool ToggleFullscreen();

        /// <summary>
        /// Sets fullscreen mode
        /// </summary>
        /// <param name="fullscreen">True for fullscreen, false for windowed</param>
        /// <returns>True if fullscreen mode was set successfully</returns>
        bool SetFullscreen(bool fullscreen);

        /// <summary>
        /// Sets VSync mode
        /// </summary>
        /// <param name="vsync">True to enable VSync, false to disable</param>
        /// <returns>True if VSync was set successfully</returns>
        bool SetVSync(bool vsync);

        /// <summary>
        /// Gets available display modes for the current adapter
        /// </summary>
        /// <returns>Array of available display modes</returns>
        DisplayMode[] GetAvailableDisplayModes();

        /// <summary>
        /// Validates if the given resolution is supported
        /// </summary>
        /// <param name="width">Width to validate</param>
        /// <param name="height">Height to validate</param>
        /// <returns>True if resolution is supported</returns>
        bool IsResolutionSupported(int width, int height);

        /// <summary>
        /// Resets the graphics device (useful for device lost scenarios)
        /// </summary>
        /// <returns>True if device was reset successfully</returns>
        bool ResetDevice();
    }

    /// <summary>
    /// Event arguments for graphics settings changes
    /// </summary>
    public class GraphicsSettingsChangedEventArgs : EventArgs
    {
        public GraphicsSettings OldSettings { get; }
        public GraphicsSettings NewSettings { get; }

        public GraphicsSettingsChangedEventArgs(GraphicsSettings oldSettings, GraphicsSettings newSettings)
        {
            OldSettings = oldSettings;
            NewSettings = newSettings;
        }
    }
}
