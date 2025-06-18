using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Concurrent;

namespace DTX.Resources
{
    /// <summary>
    /// Manages placeholder textures for different content types
    /// Provides immediate visual feedback while real textures load asynchronously
    /// </summary>
    public class PlaceholderTextureManager : IDisposable
    {
        #region Constants

        private const int DEFAULT_PLACEHOLDER_SIZE = 64;
        private const int TITLE_PLACEHOLDER_WIDTH = 400;
        private const int TITLE_PLACEHOLDER_HEIGHT = 24;
        private const int PREVIEW_PLACEHOLDER_SIZE = 128;
        private const int CLEAR_LAMP_WIDTH = 8;
        private const int CLEAR_LAMP_HEIGHT = 24;

        #endregion

        #region Private Fields

        private readonly GraphicsDevice _graphicsDevice;
        private readonly ConcurrentDictionary<string, ITexture> _placeholderCache;
        private bool _disposed = false;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialize placeholder texture manager
        /// </summary>
        public PlaceholderTextureManager(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _placeholderCache = new ConcurrentDictionary<string, ITexture>();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Get placeholder texture for song titles
        /// </summary>
        public ITexture GetTitlePlaceholder()
        {
            return GetOrCreatePlaceholder("title", TITLE_PLACEHOLDER_WIDTH, TITLE_PLACEHOLDER_HEIGHT, 
                CreateTitlePlaceholderPattern);
        }

        /// <summary>
        /// Get placeholder texture for preview images
        /// </summary>
        public ITexture GetPreviewImagePlaceholder()
        {
            return GetOrCreatePlaceholder("preview", PREVIEW_PLACEHOLDER_SIZE, PREVIEW_PLACEHOLDER_SIZE, 
                CreatePreviewImagePlaceholderPattern);
        }

        /// <summary>
        /// Get placeholder texture for clear lamps
        /// </summary>
        public ITexture GetClearLampPlaceholder()
        {
            return GetOrCreatePlaceholder("clearlamp", CLEAR_LAMP_WIDTH, CLEAR_LAMP_HEIGHT, 
                CreateClearLampPlaceholderPattern);
        }

        /// <summary>
        /// Get generic placeholder texture
        /// </summary>
        public ITexture GetGenericPlaceholder()
        {
            return GetOrCreatePlaceholder("generic", DEFAULT_PLACEHOLDER_SIZE, DEFAULT_PLACEHOLDER_SIZE, 
                CreateGenericPlaceholderPattern);
        }

        /// <summary>
        /// Clear all cached placeholder textures
        /// </summary>
        public void ClearCache()
        {
            foreach (var texture in _placeholderCache.Values)
            {
                texture?.Dispose();
            }
            _placeholderCache.Clear();
        }

        #endregion

        #region Private Methods

        private ITexture GetOrCreatePlaceholder(string key, int width, int height, 
                                               Func<int, int, Color[]> patternGenerator)
        {
            if (_placeholderCache.TryGetValue(key, out var cachedTexture))
            {
                cachedTexture?.AddReference();
                return cachedTexture;
            }

            try
            {
                var texture2D = new Texture2D(_graphicsDevice, width, height);
                var colorData = patternGenerator(width, height);
                texture2D.SetData(colorData);

                var managedTexture = new ManagedTexture(_graphicsDevice, texture2D, $"placeholder_{key}");
                _placeholderCache.TryAdd(key, managedTexture);
                
                managedTexture.AddReference();
                return managedTexture;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlaceholderTextureManager: Failed to create {key} placeholder: {ex.Message}");
                return null;
            }
        }

        private Color[] CreateTitlePlaceholderPattern(int width, int height)
        {
            var colorData = new Color[width * height];
            
            // Create a gradient pattern for title placeholders
            for (int i = 0; i < colorData.Length; i++)
            {
                int x = i % width;
                int y = i / width;
                
                // Horizontal gradient from dark to light gray
                float gradientFactor = (float)x / width;
                byte intensity = (byte)(64 + gradientFactor * 96); // Range: 64-160
                colorData[i] = new Color(intensity, intensity, intensity, (byte)255);
            }
            
            return colorData;
        }

        private Color[] CreatePreviewImagePlaceholderPattern(int width, int height)
        {
            var colorData = new Color[width * height];
            
            // Create a music note-like pattern for preview image placeholders
            for (int i = 0; i < colorData.Length; i++)
            {
                int x = i % width;
                int y = i / width;
                
                // Create a simple pattern that suggests a music note
                bool isNote = false;
                
                // Center circle (note head)
                int centerX = width / 2;
                int centerY = height / 2 + 10;
                int distanceFromCenter = (int)Math.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
                if (distanceFromCenter < 8)
                    isNote = true;
                
                // Vertical line (note stem)
                if (x >= centerX + 6 && x <= centerX + 8 && y >= centerY - 20 && y <= centerY)
                    isNote = true;
                
                colorData[i] = isNote ? Color.DarkSlateGray : Color.LightGray;
            }
            
            return colorData;
        }

        private Color[] CreateClearLampPlaceholderPattern(int width, int height)
        {
            var colorData = new Color[width * height];
            
            // Create a simple lamp-like pattern
            for (int i = 0; i < colorData.Length; i++)
            {
                int x = i % width;
                int y = i / width;
                
                // Vertical gradient suggesting a lamp
                float gradientFactor = (float)y / height;
                
                // Top is brighter (lamp on), bottom is darker
                byte intensity = (byte)(128 + (1.0f - gradientFactor) * 64); // Range: 128-192
                colorData[i] = new Color(intensity, intensity, (byte)0, (byte)255); // Yellow tint
            }
            
            return colorData;
        }

        private Color[] CreateGenericPlaceholderPattern(int width, int height)
        {
            var colorData = new Color[width * height];
            
            // Create a simple checkerboard pattern
            for (int i = 0; i < colorData.Length; i++)
            {
                int x = i % width;
                int y = i / width;
                
                // Checkerboard pattern
                bool isLight = ((x / 8) + (y / 8)) % 2 == 0;
                colorData[i] = isLight ? Color.LightGray : Color.DarkGray;
            }
            
            return colorData;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            ClearCache();
            _disposed = true;
        }

        #endregion
    }
}
