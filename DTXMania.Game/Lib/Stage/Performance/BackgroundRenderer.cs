using System;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Background renderer component for PerformanceStage
    /// Handles async loading and rendering of background images with fallback support
    /// </summary>
    public class BackgroundRenderer : IDisposable
    {
        #region Private Fields

        private readonly IResourceManager _resourceManager;
        private ITexture _backgroundTexture;
        private bool _isLoading = false;
        private bool _loadingFailed = false;
        private bool _disposed = false;
        private Texture2D _whiteTexture;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the background is currently loading
        /// </summary>
        public bool IsLoading => _isLoading;

        /// <summary>
        /// Whether the background loading failed
        /// </summary>
        public bool LoadingFailed => _loadingFailed;

        /// <summary>
        /// Whether the background is ready to render
        /// </summary>
        public bool IsReady => _backgroundTexture != null && !_isLoading;

        #endregion

        #region Constructor

        public BackgroundRenderer(IResourceManager resourceManager)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Async load the background texture
        /// </summary>
        public async Task LoadBackgroundAsync()
        {
            if (_disposed)
                return;

            if (_isLoading || _backgroundTexture != null)
                return;

            _isLoading = true;
            _loadingFailed = false;

            try
            {
                System.Diagnostics.Debug.WriteLine("BackgroundRenderer: Starting async background load");

                // Load background texture using ResourceManager
                _backgroundTexture = _resourceManager.LoadTexture(TexturePath.PerformanceBackground);
                System.Diagnostics.Debug.WriteLine("BackgroundRenderer: Background texture loaded successfully");

                // Since the async wrapper is removed, this task completes synchronously.
                // For true async loading, ResourceManager would need a LoadTextureAsync method.
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackgroundRenderer: Failed to load background texture: {ex.Message}");
                _loadingFailed = true;
                _backgroundTexture = null;
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Update the background renderer (placeholder for future animation support)
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime)
        {
            // TODO: Add background animation support in future phases
            // TODO: Add video background support
            // TODO: Add background effects (fade, scroll, etc.)
        }

        /// <summary>
        /// Draw the background
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="destinationRectangle">Destination rectangle</param>
        public void Draw(SpriteBatch spriteBatch, Rectangle destinationRectangle)
        {
            Draw(spriteBatch, destinationRectangle, 1.0f);
        }

        /// <summary>
        /// Draw the background with specified depth
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="destinationRectangle">Destination rectangle</param>
        /// <param name="depth">Z-depth for rendering (0.0f = front, 1.0f = back)</param>
        public void Draw(SpriteBatch spriteBatch, Rectangle destinationRectangle, float depth)
        {
            if (_disposed || spriteBatch == null)
                return;

            if (_backgroundTexture != null && IsReady)
            {
                // Draw the loaded background texture with custom destination and depth
                _backgroundTexture.Draw(spriteBatch, destinationRectangle, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, depth);
            }
            else
            {
                // Draw fallback background color
                DrawFallbackBackground(spriteBatch, destinationRectangle, depth);
            }
        }

        #endregion

        #region Private Methods

        private void DrawFallbackBackground(SpriteBatch spriteBatch, Rectangle area)
        {
            DrawFallbackBackground(spriteBatch, area, 1.0f);
        }

        private void DrawFallbackBackground(SpriteBatch spriteBatch, Rectangle area, float depth)
        {
            // Create a simple 1x1 white texture for drawing colored rectangles
            // This is a temporary solution - in a real implementation, we'd cache this texture
            if (_whiteTexture == null)
            {
                _whiteTexture = new Texture2D(spriteBatch.GraphicsDevice, 1, 1);
                _whiteTexture.SetData(new[] { Color.White });
            }

            if (_whiteTexture != null)
            {
                spriteBatch.Draw(_whiteTexture, area, null, PerformanceUILayout.FallbackBackgroundColor, 0f, Vector2.Zero, SpriteEffects.None, depth);
            }
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources - using reference counting for managed textures
                    _backgroundTexture?.RemoveReference();
                    _backgroundTexture = null;
                    _whiteTexture?.Dispose();
                    
                    // Reset state variables to initial values for proper reactivation
                    _isLoading = false;
                    _loadingFailed = false;
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
