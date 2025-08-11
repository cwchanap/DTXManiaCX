using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace DTXMania.Game.Lib.Graphics
{
    /// <summary>
    /// Manages graphics device settings and state
    /// </summary>
    public class GraphicsManager : IGraphicsManager, IDisposable
    {
        private readonly Game _game;
        private readonly GraphicsDeviceManager _deviceManager;
        private GraphicsSettings _currentSettings;
        private RenderTargetManager _renderTargetManager;
        private bool _disposed = false;

        public GraphicsDevice GraphicsDevice => _deviceManager.GraphicsDevice;
        public GraphicsSettings Settings => _currentSettings?.Clone();
        public bool IsDeviceAvailable => GraphicsDevice != null && !GraphicsDevice.IsDisposed;

        public event EventHandler<GraphicsSettingsChangedEventArgs> SettingsChanged;
        public event EventHandler DeviceLost;
        public event EventHandler DeviceReset;

        /// <summary>
        /// Gets the render target manager for this graphics manager
        /// </summary>
        public RenderTargetManager RenderTargetManager => _renderTargetManager;

        public GraphicsManager(Game game, GraphicsDeviceManager deviceManager)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
            
            // Initialize with current device manager settings
            _currentSettings = new GraphicsSettings
            {
                Width = _deviceManager.PreferredBackBufferWidth,
                Height = _deviceManager.PreferredBackBufferHeight,
                IsFullscreen = _deviceManager.IsFullScreen,
                VSync = _deviceManager.SynchronizeWithVerticalRetrace
            };

            // Subscribe to device events
            _game.GraphicsDevice.DeviceLost += OnDeviceLost;
            _game.GraphicsDevice.DeviceReset += OnDeviceReset;
        }

        public void Initialize()
        {
            if (GraphicsDevice != null)
            {
                _renderTargetManager = new RenderTargetManager(GraphicsDevice);
            }
        }

        public bool ApplySettings(GraphicsSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (!settings.IsValid())
                return false;

            if (settings.Equals(_currentSettings))
                return true; // No changes needed

            var oldSettings = _currentSettings.Clone();

            try
            {
                _deviceManager.PreferredBackBufferWidth = settings.Width;
                _deviceManager.PreferredBackBufferHeight = settings.Height;
                _deviceManager.IsFullScreen = settings.IsFullscreen;
                _deviceManager.SynchronizeWithVerticalRetrace = settings.VSync;

                _deviceManager.ApplyChanges();

                _currentSettings = settings.Clone();
                SettingsChanged?.Invoke(this, new GraphicsSettingsChangedEventArgs(oldSettings, _currentSettings));

                return true;
            }
            catch (Exception)
            {
                // Revert to old settings on failure
                try
                {
                    _deviceManager.PreferredBackBufferWidth = oldSettings.Width;
                    _deviceManager.PreferredBackBufferHeight = oldSettings.Height;
                    _deviceManager.IsFullScreen = oldSettings.IsFullscreen;
                    _deviceManager.SynchronizeWithVerticalRetrace = oldSettings.VSync;
                    _deviceManager.ApplyChanges();
                }
                catch
                {
                    // If we can't revert, we're in a bad state
                }

                return false;
            }
        }

        public bool ChangeResolution(int width, int height)
        {
            if (!IsResolutionSupported(width, height))
                return false;

            var newSettings = _currentSettings.Clone();
            newSettings.Width = width;
            newSettings.Height = height;

            return ApplySettings(newSettings);
        }

        public bool ToggleFullscreen()
        {
            var newSettings = _currentSettings.Clone();
            newSettings.IsFullscreen = !newSettings.IsFullscreen;
            return ApplySettings(newSettings);
        }

        public bool SetFullscreen(bool fullscreen)
        {
            var newSettings = _currentSettings.Clone();
            newSettings.IsFullscreen = fullscreen;
            return ApplySettings(newSettings);
        }

        public bool SetVSync(bool vsync)
        {
            var newSettings = _currentSettings.Clone();
            newSettings.VSync = vsync;
            return ApplySettings(newSettings);
        }

        public DisplayMode[] GetAvailableDisplayModes()
        {
            if (!IsDeviceAvailable)
                return new DisplayMode[0];

            try
            {
                return GraphicsDevice.Adapter.SupportedDisplayModes.ToArray();
            }
            catch
            {
                return new DisplayMode[0];
            }
        }

        public bool IsResolutionSupported(int width, int height)
        {
            if (width <= 0 || height <= 0 || width > 7680 || height > 4320)
                return false;

            if (!IsDeviceAvailable)
                return true; // Assume supported if device not available yet

            var availableModes = GetAvailableDisplayModes();
            return availableModes.Any(mode => mode.Width == width && mode.Height == height) ||
                   !_currentSettings.IsFullscreen; // Windowed mode is more flexible
        }

        public bool ResetDevice()
        {
            try
            {
                _deviceManager.ApplyChanges();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnDeviceLost(object sender, EventArgs e)
        {
            DeviceLost?.Invoke(this, EventArgs.Empty);
        }

        private void OnDeviceReset(object sender, EventArgs e)
        {
            // Recreate render targets after device reset
            _renderTargetManager?.RecreateAllRenderTargets();
            DeviceReset?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_game?.GraphicsDevice != null)
                {
                    _game.GraphicsDevice.DeviceLost -= OnDeviceLost;
                    _game.GraphicsDevice.DeviceReset -= OnDeviceReset;
                }

                _renderTargetManager?.Dispose();
                _disposed = true;
            }
        }
    }
}
