#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Renders the right-side vertical skill gauge meter. Uses 7_Graph_main.png as
    /// background, 7_Graph_Gauge.png as the filling bar, and a small text overlay
    /// for the numeric value. Mirrors DTXManiaNX CActPerfSkillMeter (drums layout).
    /// </summary>
    public class SkillMeterDisplay : IDisposable
    {
        #region Private Fields

        private readonly GraphicsDevice _graphicsDevice;
        private ITexture? _backgroundTexture;
        private ITexture? _gaugeTexture;
        private ManagedFont? _font;
        private bool _disposed;

        /// <summary>
        /// 1px shadow offset — intentionally tighter than the (2,2) StandardShadowOffset
        /// because the meter renders a small 12pt font where a 2px drop looks blurry.
        /// </summary>
        private static readonly Vector2 MeterShadowOffset = new Vector2(1, 1);

        #endregion

        #region Properties

        /// <summary>
        /// Current skill percentage (0.0 - 100.0) drawn by the filling bar and overlay number.
        /// </summary>
        public double Skill { get; set; }

        #endregion

        #region Constructor

        public SkillMeterDisplay(IResourceManager resourceManager, GraphicsDevice graphicsDevice)
        {
            if (resourceManager == null) throw new ArgumentNullException(nameof(resourceManager));
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));

            try
            {
                _backgroundTexture = resourceManager.LoadTexture(TexturePath.PerfGraphMain);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SkillMeterDisplay: Failed to load background texture: {ex.Message}");
                _backgroundTexture = null;
            }

            try
            {
                _gaugeTexture = resourceManager.LoadTexture(TexturePath.PerfGraphGauge);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SkillMeterDisplay: Failed to load gauge texture: {ex.Message}");
                _gaugeTexture = null;
            }

            try
            {
                _font = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", 12, FontStyle.Bold);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SkillMeterDisplay: Failed to create font: {ex.Message}");
                _font = null;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Update the meter state. No animation yet.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime) { /* no animation yet */ }

        /// <summary>
        /// Draw the skill meter: background frame, filling bar, "current" label, and numeric overlay.
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        [ExcludeFromCodeCoverage]
        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null) return;

            // Clamp once so bar height, draw decision, and numeric overlay are consistent.
            double clampedSkill = Math.Clamp(Skill, 0.0, 100.0);

            var bgPos = PerformanceUILayout.SkillMeter.BackgroundPosition;

            // 1. Background frame
            if (_backgroundTexture != null)
            {
                _backgroundTexture.Draw(spriteBatch, bgPos,
                    PerformanceUILayout.SkillMeter.BackgroundSourceRect);
            }

            // 2-4. Filling bar
            int gaugeHeight = ComputeGaugeHeight(clampedSkill);
            int barTopY     = ComputeBarTopY(clampedSkill);
            if (ShouldDrawBar(clampedSkill) && _gaugeTexture != null)
            {
                var barPos = new Vector2(
                    bgPos.X + PerformanceUILayout.SkillMeter.GaugeOffset.X,
                    barTopY);
                var barSrc = new Rectangle(
                    (int)PerformanceUILayout.SkillMeter.GaugeSourceXY.X,
                    (int)PerformanceUILayout.SkillMeter.GaugeSourceXY.Y,
                    PerformanceUILayout.SkillMeter.GaugeWidth,
                    gaugeHeight);
                _gaugeTexture.Draw(spriteBatch, barPos, barSrc);
            }

            // 5. "Current" label
            if (_backgroundTexture != null)
            {
                _backgroundTexture.Draw(spriteBatch,
                    PerformanceUILayout.SkillMeter.LabelPosition,
                    PerformanceUILayout.SkillMeter.LabelSourceRect);
            }

            // 6. Numeric overlay
            if (_font != null)
            {
                string text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0,5:##0.00}", clampedSkill);
                var textPos = new Vector2(
                    bgPos.X + PerformanceUILayout.SkillMeter.GaugeOffset.X - 1,
                    barTopY + PerformanceUILayout.SkillMeter.NumberOffsetFromTopOfBar);
                _font.DrawStringWithShadow(spriteBatch, text, textPos,
                    Color.White, PerformanceUILayout.Visual.StandardShadowColor, MeterShadowOffset);
            }
        }

        /// <summary>
        /// Compute the filling bar height in pixels for a given skill value (0–100).
        /// Clamps the input so out-of-range skills do not overflow or invert the bar.
        /// </summary>
        public static int ComputeGaugeHeight(double skill)
        {
            double clamped = Math.Clamp(skill, 0.0, 100.0);
            return (int)(PerformanceUILayout.SkillMeter.GaugeMaxHeight * clamped / 100.0);
        }

        /// <summary>
        /// Compute the Y coordinate of the top of the filling bar.
        /// </summary>
        public static int ComputeBarTopY(double skill)
        {
            return PerformanceUILayout.SkillMeter.GaugeBaselineY - ComputeGaugeHeight(skill);
        }

        /// <summary>
        /// Whether the filling bar should be drawn at all for the given skill value.
        /// </summary>
        public static bool ShouldDrawBar(double skill) => skill > 0.0;

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _font?.Dispose();
                _font = null;
                _backgroundTexture?.RemoveReference();
                _backgroundTexture = null;
                _gaugeTexture?.RemoveReference();
                _gaugeTexture = null;
            }
            _disposed = true;
        }

        #endregion
    }
}
