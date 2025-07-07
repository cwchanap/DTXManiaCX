using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Resources;
using DTX.UI.Layout;
using System;

namespace DTX.UI.Components
{
    /// <summary>
    /// Background panel for BPM and song length information
    /// Supports loading 5_BPM.png texture with fallback to generated background
    /// Matches DTXManiaNX appearance and positioning
    /// </summary>
    public class BPMBackgroundPanel : UIElement
    {
        #region Fields

        private IResourceManager _resourceManager;
        private ITexture _backgroundTexture;
        private ITexture _fallbackTexture;
        private DefaultGraphicsGenerator _graphicsGenerator;
        private bool _hasStatusPanel;
        private bool _disposed;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the status panel is present (affects positioning)
        /// </summary>
        public bool HasStatusPanel
        {
            get => _hasStatusPanel;
            set
            {
                if (_hasStatusPanel != value)
                {
                    _hasStatusPanel = value;
                    UpdatePositionAndSize();
                }
            }
        }

        /// <summary>
        /// Resource manager for loading textures
        /// </summary>
        public IResourceManager ResourceManager
        {
            get => _resourceManager;
            set
            {
                _resourceManager = value;
                LoadBackgroundTexture();
            }
        }

        /// <summary>
        /// Graphics generator for fallback background
        /// </summary>
        public DefaultGraphicsGenerator GraphicsGenerator
        {
            get => _graphicsGenerator;
            set
            {
                _graphicsGenerator = value;
                GenerateFallbackTexture();
            }
        }

        /// <summary>
        /// Whether the panel is using the authentic 5_BPM.png texture
        /// </summary>
        public bool IsUsingAuthenticTexture => _backgroundTexture != null && !_backgroundTexture.IsDisposed;

        #endregion

        #region Constructor

        public BPMBackgroundPanel()
        {
            _hasStatusPanel = true; // Default to with status panel
            UpdatePositionAndSize();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initialize the panel with resource manager and graphics generator
        /// </summary>
        public void Initialize(IResourceManager resourceManager, DefaultGraphicsGenerator graphicsGenerator)
        {
            _resourceManager = resourceManager;
            _graphicsGenerator = graphicsGenerator;

            LoadBackgroundTexture();
            GenerateFallbackTexture();
        }

        #endregion

        #region Protected Methods

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible || _disposed)
                return;

            var bounds = Bounds;

            // Try to use the authentic 5_BPM.png texture first
            var textureToUse = _backgroundTexture ?? _fallbackTexture;

            if (textureToUse != null && !textureToUse.IsDisposed)
            {
                try
                {
                    // Scale the texture to fit the panel bounds
                    var sourceRect = new Rectangle(0, 0, textureToUse.Width, textureToUse.Height);
                    textureToUse.Draw(spriteBatch, bounds, sourceRect, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
                }
                catch (ObjectDisposedException)
                {
                    // Texture was disposed, clear references
                    if (textureToUse == _backgroundTexture)
                        _backgroundTexture = null;
                    else if (textureToUse == _fallbackTexture)
                        _fallbackTexture = null;
                }
            }

            base.OnDraw(spriteBatch, deltaTime);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Update position and size based on status panel presence
        /// </summary>
        private void UpdatePositionAndSize()
        {
            if (_hasStatusPanel)
            {
                // X:90, Y:275 (with panel mode from DTXManiaNX)
                Position = SongSelectionUILayout.BPMSection.Position;
                Size = SongSelectionUILayout.BPMSection.Size;
            }
            else
            {
                // X:490, Y:385 (standalone mode from DTXManiaNX)
                Position = new Vector2(490, 385);
                Size = SongSelectionUILayout.BPMSection.Size;
            }
        }

        /// <summary>
        /// Load the authentic 5_BPM.png background texture
        /// </summary>
        private void LoadBackgroundTexture()
        {
            if (_resourceManager == null)
                return;

            try
            {
                _backgroundTexture = _resourceManager.LoadTexture("Graphics/5_BPM.png");
            }
            catch
            {
                _backgroundTexture = null;
            }
        }

        /// <summary>
        /// Generate fallback background texture when 5_BPM.png is unavailable
        /// </summary>
        private void GenerateFallbackTexture()
        {
            if (_graphicsGenerator == null)
                return;

            try
            {
                var size = SongSelectionUILayout.BPMSection.Size;
                _fallbackTexture = _graphicsGenerator.GenerateBPMBackground((int)size.X, (int)size.Y, true);
            }
            catch
            {
                _fallbackTexture = null;
            }
        }

        #endregion

        #region IDisposable Implementation

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Note: Don't dispose textures here as they're managed by ResourceManager and GraphicsGenerator
                _backgroundTexture = null;
                _fallbackTexture = null;
                _resourceManager = null;
                _graphicsGenerator = null;
                _disposed = true;
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}
