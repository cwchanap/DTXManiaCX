using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Life gauge display component for PerformanceStage
    /// Displays the current life gauge with DTXMania-style formatting
    /// This is a stub implementation for Phase 1
    /// </summary>
    public class GaugeDisplay : IDisposable
    {
        #region Private Fields

        private readonly IResourceManager _resourceManager;
        private readonly GraphicsDevice _graphicsDevice;
        private Texture2D _whiteTexture;

        // HPA-796: the cached white pixel reference; _whiteTexture above is its borrowed raw texture.
        private ITexture _whiteTextureResource;
        private readonly Vector2 _position;
        private readonly Vector2 _size;
        private float _currentValue = 0.5f; // 50% life as default
        private Color _frameColor = Color.White;
        private Color _fillColor = Color.Green;
        private Color _backgroundColor = PerformanceUILayout.GaugeDisplay.BackgroundColor;
        private bool _disposed = false;

        // Gauge properties
        private const float MinValue = 0.0f;
        private const float MaxValue = 1.0f;
        private const int FrameThickness = 2;

        #endregion

        #region Properties

        /// <summary>
        /// Current gauge value (0.0f to 1.0f)
        /// </summary>
        public float Value
        {
            get => _currentValue;
            set
            {
                _currentValue = MathHelper.Clamp(value, MinValue, MaxValue);
                UpdateFillColor();
            }
        }

        /// <summary>
        /// Frame color for the gauge
        /// </summary>
        public Color FrameColor
        {
            get => _frameColor;
            set => _frameColor = value;
        }

        /// <summary>
        /// Fill color for the gauge
        /// </summary>
        public Color FillColor
        {
            get => _fillColor;
            set => _fillColor = value;
        }

        /// <summary>
        /// Background color for the gauge
        /// </summary>
        public Color BackgroundColor
        {
            get => _backgroundColor;
            set => _backgroundColor = value;
        }

        #endregion

        #region Constructor

        public GaugeDisplay(IResourceManager resourceManager, GraphicsDevice graphicsDevice)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _position = PerformanceUILayout.Gauge.FramePosition;
            _size = new Vector2(PerformanceUILayout.Gauge.FillWidth, PerformanceUILayout.Gauge.FillHeight);
            
            // Initialize with default value (50% life)
            SetValue(0.5f);
            
            // Create white texture for drawing
            CreateWhiteTexture();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Set the gauge value
        /// </summary>
        /// <param name="value">Value between 0.0f and 1.0f</param>
        public void SetValue(float value)
        {
            Value = value;
            
            // Update fill color based on value (DTXMania-style)
            UpdateFillColor();
        }

        /// <summary>
        /// Update the gauge display
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime)
        {
            // No animation or update logic needed for now
            // Future: Add gauge animation effects, pulsing, etc.
        }

        /// <summary>
        /// Draw the gauge display
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null || _whiteTexture == null)
                return;

            // Calculate rectangles
            var frameRect = new Rectangle((int)_position.X, (int)_position.Y, (int)_size.X, (int)_size.Y);
            var backgroundRect = new Rectangle(
                frameRect.X + FrameThickness,
                frameRect.Y + FrameThickness,
                frameRect.Width - (FrameThickness * 2),
                frameRect.Height - (FrameThickness * 2)
            );
            var fillRect = new Rectangle(
                backgroundRect.X,
                backgroundRect.Y,
                (int)(backgroundRect.Width * _currentValue),
                backgroundRect.Height
            );

            // Draw background
            spriteBatch.Draw(_whiteTexture, backgroundRect, _backgroundColor);

            // Draw fill
            if (_currentValue > 0)
            {
                spriteBatch.Draw(_whiteTexture, fillRect, _fillColor);
            }

            // Draw frame (top, bottom, left, right)
            DrawFrame(spriteBatch, frameRect);
        }

        #endregion

        #region Private Methods

        private void CreateWhiteTexture()
        {
            try
            {
                // HPA-796: borrow the cached white pixel from the ResourceManager instead
                // of allocating a private 1×1 texture.
                _whiteTextureResource = _resourceManager.CreateTextureFromColor(Color.White);
                _whiteTexture = _whiteTextureResource?.Texture;
            }
            catch (Exception)
            {
                _whiteTextureResource = null;
                _whiteTexture = null;
            }
        }

        private void UpdateFillColor()
        {
            // DTXMania-style color coding based on gauge value
            if (_currentValue >= 0.8f)
            {
                _fillColor = Color.Green;      // High life - green
            }
            else if (_currentValue >= 0.5f)
            {
                _fillColor = Color.Yellow;     // Medium life - yellow
            }
            else if (_currentValue >= 0.2f)
            {
                _fillColor = Color.Orange;     // Low life - orange
            }
            else
            {
                _fillColor = Color.Red;        // Critical life - red
            }
        }

        [ExcludeFromCodeCoverage]
        private void DrawFrame(SpriteBatch spriteBatch, Rectangle frameRect)
        {
            // HPA-796: the 2px overlapping-corner frame matches the shared Border geometry
            // exactly (top, bottom, left, right; same simple-overload draw semantics).
            ChamferedBox.Border(spriteBatch, _whiteTexture, frameRect, FrameThickness, _frameColor);
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
                    // Release the cached white pixel reference (HPA-796); the raw texture
                    // is borrowed from the ResourceManager cache and never disposed here.
                    _whiteTextureResource?.RemoveReference();
                    _whiteTextureResource = null;
                    _whiteTexture = null;
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
