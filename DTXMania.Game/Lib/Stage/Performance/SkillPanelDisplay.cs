#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Renders the left-side performance status panel: difficulty level,
    /// current skill percentage, and (optionally) the MAX badge sprite.
    /// </summary>
    public class SkillPanelDisplay : IDisposable
    {
        #region Private Fields

        private readonly GraphicsDevice _graphicsDevice;
        private readonly SongChart? _chart;
        private ManagedFont? _font;
        private ITexture? _maxBadgeTexture;
        private bool _disposed;

        #endregion

        #region Properties

        /// <summary>
        /// Current skill percentage (0.0 - 100.0) rendered as the large number.
        /// </summary>
        public double Skill { get; set; }

        /// <summary>
        /// Whether the MAX badge sprite should be drawn over the skill number.
        /// </summary>
        public bool ShowMax { get; set; }

        #endregion

        #region Constructor

        public SkillPanelDisplay(IResourceManager resourceManager, GraphicsDevice graphicsDevice, SongChart? chart)
        {
            if (resourceManager == null) throw new ArgumentNullException(nameof(resourceManager));
            _graphicsDevice  = graphicsDevice  ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _chart           = chart;

            try
            {
                _font = ManagedFont.CreateFont(_graphicsDevice, "NotoSerifJP", 24, FontStyle.Bold);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SkillPanelDisplay: Failed to create font: {ex.Message}");
                _font = null;
            }

            try
            {
                _maxBadgeTexture = resourceManager.LoadTexture(TexturePath.SkillMax);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SkillPanelDisplay: Failed to load MAX badge: {ex.Message}");
                _maxBadgeTexture = null;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Update the panel state. No animation yet.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime) { /* no animation yet */ }

        /// <summary>
        /// Draw the skill panel contents.
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        [ExcludeFromCodeCoverage]
        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null || _font == null) return;

            int level    = _chart?.DrumLevel    ?? 0;
            int levelDec = _chart?.DrumLevelDec ?? 0;

            string levelText = FormatLevelText(level, levelDec);
            _font.DrawStringWithShadow(spriteBatch, levelText,
                PerformanceUILayout.SkillPanel.LevelNumber.StartPosition,
                Color.White, PerformanceUILayout.Visual.StandardShadowColor, PerformanceUILayout.Visual.StandardShadowOffset);

            string skillText = FormatSkillText(Skill);
            _font.DrawStringWithShadow(spriteBatch, skillText,
                PerformanceUILayout.SkillPanel.SkillPercent.NumbersPosition,
                Color.White, PerformanceUILayout.Visual.StandardShadowColor, PerformanceUILayout.Visual.StandardShadowOffset);

            _font.DrawStringWithShadow(spriteBatch, "%",
                PerformanceUILayout.SkillPanel.SkillPercent.PercentPosition,
                Color.White, PerformanceUILayout.Visual.StandardShadowColor, PerformanceUILayout.Visual.StandardShadowOffset);

            if (ShowMax && _maxBadgeTexture != null)
            {
                _maxBadgeTexture.Draw(spriteBatch,
                    PerformanceUILayout.SkillPanel.SkillPercent.MaxBadgePosition);
            }
        }

        /// <summary>
        /// Format the difficulty level text using DTXManiaNX encoding rules.
        /// Match DTXManiaNX CScoreIni.tCalculateGameSkillFromPlayingSkill encoding:
        ///   level &gt;= 100 → displayed = level/100  (e.g. 850 → 8.50)
        ///   level &lt;  100 → displayed = level/10 + levelDec/100  (e.g. 80+50 → 8.50)
        /// </summary>
        public static string FormatLevelText(int level, int levelDec)
        {
            if (level <= 0) return "--";
            double actual = level >= 100
                ? level / 100.0
                : (level / 10.0) + (levelDec / 100.0);
            return actual.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format the skill percentage as a 6-character right-aligned string with two decimals.
        /// </summary>
        public static string FormatSkillText(double skill)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0,6:##0.00}", skill);
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
            if (_disposed) return;
            if (disposing)
            {
                _font?.Dispose();
                _font = null;
                _maxBadgeTexture?.RemoveReference();
                _maxBadgeTexture = null;
            }
            _disposed = true;
        }

        #endregion
    }
}
