using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Resources;

namespace DTX.Stage.Performance
{
    /// <summary>
    /// Life gauge display component for PerformanceStage
    /// Displays the current life gauge with DTXMania-style formatting
    /// This is a stub implementation for Phase 1
    /// </summary>
    public class GaugeDisplay : IDisposable
    {
        #region Private Fields

        private readonly ResourceManager _resourceManager;
        private readonly GraphicsDevice _graphicsDevice;
        private Texture2D _whiteTexture;
        private readonly Vector2 _position;
        private readonly Vector2 _size;
        private float _currentValue = 0.5f; // 50% life as default
        private Color _frameColor = Color.White;
        private Color _fillColor = Color.Green;
        private Color _backgroundColor = new Color(0, 0, 0, 128);
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
            set => _currentValue = MathHelper.Clamp(value, MinValue, MaxValue);
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

        public GaugeDisplay(ResourceManager resourceManager, GraphicsDevice graphicsDevice)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _position = PerformanceUILayout.GaugePosition;
            _size = PerformanceUILayout.GaugeSize;
            
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
                _whiteTexture = new Texture2D(_graphicsDevice, 1, 1);
                _whiteTexture.SetData(new[] { Color.White });
                System.Diagnostics.Debug.WriteLine("GaugeDisplay: White texture created");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GaugeDisplay: Failed to create white texture: {ex.Message}");
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

        private void DrawFrame(SpriteBatch spriteBatch, Rectangle frameRect)
        {
            // Draw frame as four rectangles (top, bottom, left, right)
            
            // Top
            var topRect = new Rectangle(frameRect.X, frameRect.Y, frameRect.Width, FrameThickness);
            spriteBatch.Draw(_whiteTexture, topRect, _frameColor);
            
            // Bottom
            var bottomRect = new Rectangle(frameRect.X, frameRect.Bottom - FrameThickness, frameRect.Width, FrameThickness);
            spriteBatch.Draw(_whiteTexture, bottomRect, _frameColor);
            
            // Left
            var leftRect = new Rectangle(frameRect.X, frameRect.Y, FrameThickness, frameRect.Height);
            spriteBatch.Draw(_whiteTexture, leftRect, _frameColor);
            
            // Right
            var rightRect = new Rectangle(frameRect.Right - FrameThickness, frameRect.Y, FrameThickness, frameRect.Height);
            spriteBatch.Draw(_whiteTexture, rightRect, _frameColor);
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
                    // Dispose managed resources
                    _whiteTexture?.Dispose();
                    _whiteTexture = null;
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
