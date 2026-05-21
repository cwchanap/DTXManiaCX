#nullable enable
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Brief on-screen toast that displays the current scroll-speed value
    /// for a short duration after the player adjusts it.
    /// </summary>
    public class ScrollSpeedIndicator
    {
        private readonly IFont? _font;
        private string _text = string.Empty;
        private float _remainingSeconds;
        private bool _hasShown;

        /// <summary>Current display text (set by Show).</summary>
        internal string Text => _text;

        /// <summary>Seconds remaining before the indicator hides.</summary>
        internal float RemainingSeconds => _remainingSeconds;

        /// <summary>Whether the indicator has been shown at least once and is still visible.</summary>
        internal bool IsVisible => _hasShown && _remainingSeconds > 0f;

        /// <summary>Current alpha value (0..1) used for fade-out near expiry.</summary>
        internal float Alpha => (!_hasShown || _remainingSeconds <= 0f) ? 0f : ComputeAlpha();

        public ScrollSpeedIndicator(IFont? font)
        {
            _font = font;
        }

        public void Show(int scrollSpeedPercent)
        {
            _text = "Scroll Speed " + ScrollSpeedRange.Format(scrollSpeedPercent);
            _remainingSeconds = PerformanceUILayout.ScrollSpeedIndicatorDurationSeconds;
            _hasShown = true;
        }

        public void Update(GameTime gameTime)
        {
            if (_remainingSeconds <= 0f)
                return;
            _remainingSeconds -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_remainingSeconds < 0f)
                _remainingSeconds = 0f;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (!_hasShown || _remainingSeconds <= 0f || _font == null)
                return;

            var alpha = ComputeAlpha();
            var color = Color.White * alpha;
            _font.DrawString(spriteBatch, _text,
                new Vector2(PerformanceUILayout.ScrollSpeedIndicatorX,
                            PerformanceUILayout.ScrollSpeedIndicatorY),
                color);
        }

        private float ComputeAlpha()
        {
            var fade = PerformanceUILayout.ScrollSpeedIndicatorFadeSeconds;
            if (_remainingSeconds >= fade)
                return 1f;
            return _remainingSeconds / fade;
        }
    }
}
