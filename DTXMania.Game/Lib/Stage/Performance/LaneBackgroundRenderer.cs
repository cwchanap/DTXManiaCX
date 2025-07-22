using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTX.Stage.Performance
{
    /// <summary>
    /// Lane background renderer component for PerformanceStage
    /// Renders the 9-lane GITADORA XG layout with colored backgrounds
    /// </summary>
    public class LaneBackgroundRenderer : IDisposable
    {
        #region Private Fields

        private static Texture2D _whiteTexture;
        private GraphicsDevice _graphicsDevice;
        private bool _disposed = false;

        #endregion

        #region Constructor

        public LaneBackgroundRenderer(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            try
            {
                if (_whiteTexture == null || _whiteTexture.IsDisposed)
                {
                    CreateWhiteTexture();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("LaneBackgroundRenderer could not be initialized because the white texture could not be created.", ex);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Update the lane background renderer (placeholder for future animation support)
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime)
        {
            // TODO: Add lane animation support in future phases
            // TODO: Add lane flash effects for note hits
            // TODO: Add lane color customization
        }

        /// <summary>
        /// Draw the lane backgrounds
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null || _whiteTexture == null)
                return;

            // Draw all 9 lanes with their respective colors
            for (int i = 0; i < PerformanceUILayout.LaneCount; i++)
            {
                DrawLane(spriteBatch, i);
            }
        }

        /// <summary>
        /// Draw a specific lane background
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="laneIndex">Lane index (0-8)</param>
        public void DrawLane(SpriteBatch spriteBatch, int laneIndex)
        {
            if (_disposed || spriteBatch == null || _whiteTexture == null)
                return;

            if (laneIndex < 0 || laneIndex >= PerformanceUILayout.LaneCount)
                return;

            // Get lane rectangle and color
            var laneRect = PerformanceUILayout.GetLaneRectangle(laneIndex);
            var laneColor = PerformanceUILayout.GetLaneColor(laneIndex);
            
            // Draw with transparency for placeholder effect
            var transparentColor = laneColor * 0.3f;
            
            spriteBatch.Draw(_whiteTexture, laneRect, transparentColor);
        }

        /// <summary>
        /// Draw lane backgrounds with custom alpha
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="alpha">Alpha transparency (0.0f to 1.0f)</param>
        public void Draw(SpriteBatch spriteBatch, float alpha)
        {
            if (_disposed || spriteBatch == null || _whiteTexture == null)
                return;

            alpha = MathHelper.Clamp(alpha, 0.0f, 1.0f);

            // Draw all 9 lanes with custom alpha
            for (int i = 0; i < PerformanceUILayout.LaneCount; i++)
            {
                DrawLane(spriteBatch, i, alpha);
            }
        }

        /// <summary>
        /// Draw a specific lane background with custom alpha
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <param name="alpha">Alpha transparency (0.0f to 1.0f)</param>
        public void DrawLane(SpriteBatch spriteBatch, int laneIndex, float alpha)
        {
            if (_disposed || spriteBatch == null || _whiteTexture == null)
                return;

            if (laneIndex < 0 || laneIndex >= PerformanceUILayout.LaneCount)
                return;

            alpha = MathHelper.Clamp(alpha, 0.0f, 1.0f);

            // Get lane rectangle and color
            var laneRect = PerformanceUILayout.GetLaneRectangle(laneIndex);
            var laneColor = PerformanceUILayout.GetLaneColor(laneIndex);
            
            // Apply custom alpha
            var colorWithAlpha = laneColor * alpha;
            
            spriteBatch.Draw(_whiteTexture, laneRect, colorWithAlpha);
        }

        #endregion

        #region Private Methods

        private void CreateWhiteTexture()
        {
            try
            {
                _whiteTexture = new Texture2D(_graphicsDevice, 1, 1);
                _whiteTexture.SetData(new[] { Color.White });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LaneBackgroundRenderer: Failed to create white texture: {ex.Message}");
                _whiteTexture = null;
                throw;
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
                    // Dispose managed resources
                    // _whiteTexture is static and shared, so we don't dispose it here.
                    // A central resource manager should handle its lifetime.
                    _graphicsDevice = null;
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
