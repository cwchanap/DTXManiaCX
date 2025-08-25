using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Lane background renderer component for PerformanceStage
    /// Renders the 9-lane GITADORA XG layout with colored backgrounds
    /// </summary>
    public class LaneBackgroundRenderer : IDisposable
    {
        #region Private Fields

        private readonly ITexture _whiteTexture;
        private bool _disposed = false;

        #endregion

        #region Constructor

        public LaneBackgroundRenderer(IResourceManager resourceManager)
        {
            if (resourceManager == null)
                throw new ArgumentNullException(nameof(resourceManager));

            try
            {
                _whiteTexture = resourceManager.CreateTextureFromColor(Color.White);
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
            
            spriteBatch.Draw(_whiteTexture.Texture, laneRect, null, transparentColor, 0f, Vector2.Zero, SpriteEffects.None, 0.9f);
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
            
            spriteBatch.Draw(_whiteTexture.Texture, laneRect, null, colorWithAlpha, 0f, Vector2.Zero, SpriteEffects.None, 0.9f);
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
                    _whiteTexture?.RemoveReference();
                 }
 
                 _disposed = true;
             }
         }

        #endregion
    }
}
