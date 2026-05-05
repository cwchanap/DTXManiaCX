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
        private readonly BitmapFont? _font;
        private string _text = string.Empty;
        private float _remainingSeconds;
        private bool _hasShown;

        public ScrollSpeedIndicator(BitmapFont? font)
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
            _font.DrawText(spriteBatch, _text,
                PerformanceUILayout.ScrollSpeedIndicatorX,
                PerformanceUILayout.ScrollSpeedIndicatorY,
                color, BitmapFont.FontType.Normal, 0.0f);
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
