using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Judgement line renderer component for PerformanceStage
    /// Renders the horizontal judgement line where notes are hit
    /// </summary>
    public class JudgementLineRenderer : IDisposable
    {
        #region Private Fields

        private Texture2D _whiteTexture;
        private GraphicsDevice _graphicsDevice;
        private bool _disposed = false;

        // Judgement line properties
        private Color _lineColor = Color.White;
        private int _lineThickness = 2; // From PerformanceUILayout.JudgementLine.DefaultThickness
        private float _alpha = 1.0f;

        #endregion

        #region Properties

        /// <summary>
        /// Color of the judgement line
        /// </summary>
        public Color LineColor
        {
            get => _lineColor;
            set => _lineColor = value;
        }

        /// <summary>
        /// Thickness of the judgement line in pixels
        /// </summary>
        public int LineThickness
        {
            get => _lineThickness;
            set => _lineThickness = Math.Max(1, value);
        }

        /// <summary>
        /// Alpha transparency of the judgement line (0.0f to 1.0f)
        /// </summary>
        public float Alpha
        {
            get => _alpha;
            set => _alpha = MathHelper.Clamp(value, 0.0f, 1.0f);
        }

        #endregion

        #region Constructor

        public JudgementLineRenderer(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            try
            {
                CreateWhiteTexture();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("JudgementLineRenderer could not be initialized because the white texture could not be created.", ex);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Update the judgement line renderer (placeholder for future animation support)
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime)
        {
            // TODO: Add judgement line animation support in future phases
            // TODO: Add judgement line flash effects for perfect hits
            // TODO: Add judgement line pulse effects
        }

        /// <summary>
        /// Draw the judgement line
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null || _whiteTexture == null)
                return;

            // Calculate judgement line rectangle
            var lineRect = GetJudgementLineRectangle();
            
            // Apply alpha to line color
            var colorWithAlpha = _lineColor * _alpha;
            
            spriteBatch.Draw(_whiteTexture, lineRect, colorWithAlpha);
        }

        /// <summary>
        /// Draw the judgement line with custom color
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="color">Custom color for the line</param>
        public void Draw(SpriteBatch spriteBatch, Color color)
        {
            if (_disposed || spriteBatch == null || _whiteTexture == null)
                return;

            // Calculate judgement line rectangle
            var lineRect = GetJudgementLineRectangle();
            
            // Apply alpha to custom color
            var colorWithAlpha = color * _alpha;
            
            spriteBatch.Draw(_whiteTexture, lineRect, colorWithAlpha);
        }

        /// <summary>
        /// Draw the judgement line with custom color and alpha
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="color">Custom color for the line</param>
        /// <param name="alpha">Custom alpha transparency</param>
        public void Draw(SpriteBatch spriteBatch, Color color, float alpha)
        {
            if (_disposed || spriteBatch == null || _whiteTexture == null)
                return;

            alpha = MathHelper.Clamp(alpha, 0.0f, 1.0f);

            // Calculate judgement line rectangle
            var lineRect = GetJudgementLineRectangle();
            
            // Apply custom alpha to color
            var colorWithAlpha = color * alpha;
            
            spriteBatch.Draw(_whiteTexture, lineRect, colorWithAlpha);
        }

        #endregion

        #region Private Methods

        private Rectangle GetJudgementLineRectangle()
        {
            // Calculate the judgement line rectangle spanning all lanes
            var leftX = PerformanceUILayout.GetLaneLeftX(0);
            var rightX = PerformanceUILayout.GetLaneRightX(PerformanceUILayout.LaneCount - 1);
            var width = rightX - leftX;
            
            return new Rectangle(
                leftX,
                PerformanceUILayout.JudgementLineY,
                width,
                _lineThickness
            );
        }

        private void CreateWhiteTexture()
        {
            try
            {
                _whiteTexture = new Texture2D(_graphicsDevice, 1, 1);
                _whiteTexture.SetData(new[] { Color.White });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JudgementLineRenderer: Failed to create white texture: {ex.Message}");
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
                    _whiteTexture?.Dispose();
                    _whiteTexture = null;
                    _graphicsDevice = null;
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
