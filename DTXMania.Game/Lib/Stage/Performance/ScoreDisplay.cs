using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Score display component for PerformanceStage
    /// Displays the current score with DTXMania-style formatting
    /// </summary>
    public class ScoreDisplay : IDisposable
    {
        #region Private Fields

        private readonly GraphicsDevice _graphicsDevice;
        private ManagedFont _scoreFont;
        private ManagedFont _titleFont;
        private ITexture _scoreNumbersTexture;
        private readonly Vector2 _position;
        private const string TitleText = "SCORE";

        // 7_score numbersGD.png layout (NX CActPerfDrumsScore): ten 36x50 digit cells on the top row,
        // and the "SCORE" label sprite at (0,50,86,28).
        private const int DigitCellWidth = 36;
        private const int DigitCellHeight = 50;
        private static readonly Rectangle LabelSourceRect = new Rectangle(0, 50, 86, 28);
        private int _currentScore = 0;
        private string _scoreText = "0000000";
        private Color _textColor = Color.White;
        private Color _shadowColor = PerformanceUILayout.Visual.StandardShadowColor;
        private Vector2 _shadowOffset = new Vector2(2, 2); // From PerformanceUILayout.ScoreDisplay.ShadowOffset
        private bool _disposed = false;

        // Score formatting
        private const string ScoreFormat = "0000000";
        private const int MaxScore = 9999999; // From PerformanceUILayout.ScoreDisplay.MaxScore

        #endregion

        #region Properties

        /// <summary>
        /// Current score value
        /// </summary>
        public int Score
        {
            get => _currentScore;
            set
            {
                // Clamp score to valid range
                _currentScore = Math.Clamp(value, 0, MaxScore);
                
                // Update score text with proper formatting
                _scoreText = _currentScore.ToString(ScoreFormat);
            }
        }

        /// <summary>
        /// Text color for the score display
        /// </summary>
        public Color TextColor
        {
            get => _textColor;
            set => _textColor = value;
        }

        /// <summary>
        /// Shadow color for the score display
        /// </summary>
        public Color ShadowColor
        {
            get => _shadowColor;
            set => _shadowColor = value;
        }

        /// <summary>
        /// Shadow offset for the score display
        /// </summary>
        public Vector2 ShadowOffset
        {
            get => _shadowOffset;
            set => _shadowOffset = value;
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Constructs the score display.
        /// </summary>
        /// <param name="resourceManager">
        /// Retained for constructor-signature consistency with the sibling performance
        /// components (e.g. <see cref="ComboDisplay"/>, <see cref="GaugeDisplay"/>), which
        /// all accept an <see cref="IResourceManager"/>. This component builds its font
        /// through the <see cref="ManagedFont"/> factory directly, but also loads the
        /// <see cref="TexturePath.ScoreNumbers"/> bitmap font via the manager. Do not
        /// remove without updating every performance-component constructor call site.
        /// </param>
        /// <param name="graphicsDevice">Graphics device used for font rendering.</param>
        public ScoreDisplay(IResourceManager resourceManager, GraphicsDevice graphicsDevice)
        {
            ArgumentNullException.ThrowIfNull(resourceManager);
            ArgumentNullException.ThrowIfNull(graphicsDevice);
            _graphicsDevice = graphicsDevice;
            _position = PerformanceUILayout.ScorePosition;

            // Initialize with default score
            Score = 0;

            // Preferred renderer: the DTXManiaNX 7_score numbersGD.png bitmap font, which places the
            // digits and "SCORE" label at exact pixel positions. The system font is only a fallback.
            try
            {
                _scoreNumbersTexture = resourceManager.LoadTexture(TexturePath.ScoreNumbers);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ScoreDisplay: Failed to load score numbers texture: {ex.Message}");
                _scoreNumbersTexture = null;
            }

            // Load font (used as the fallback when the bitmap is unavailable).
            try
            {
                LoadFont();
            }
            catch (Exception ex)
            {
                // The system font is only a fallback for the bitmap score sprite. When the bitmap
                // loaded successfully the display can render without it, so treat the font as
                // optional. Only abort construction when neither the bitmap nor the font is
                // available (Draw() would otherwise have nothing to render).
                if (_scoreNumbersTexture == null)
                {
                    throw new InvalidOperationException(
                        "ScoreDisplay could not be initialized because the font could not be loaded.", ex);
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Update the score display
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime)
        {
            // No animation or update logic needed for now
            // Future: Add score animation effects
        }

        /// <summary>
        /// Draw the score display
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null)
                return;

            // Preferred: bitmap font, positioned exactly as DTXManiaNX (digits start at
            // Score.FirstDigitPosition, advancing by DigitSpacing; label sprite at Score.LabelPosition).
            if (_scoreNumbersTexture != null)
            {
                DrawBitmapScore(spriteBatch);
                return;
            }

            if (_scoreFont == null)
                return;

            // Fallback: system font. Draw the "SCORE" title above the digits since the bitmap label
            // sprite is unavailable here.
            _titleFont?.DrawStringWithShadow(
                spriteBatch,
                TitleText,
                PerformanceUILayout.Score.LabelPosition,
                _textColor,
                _shadowColor,
                _shadowOffset
            );

            // Draw score with shadow effect
            _scoreFont.DrawStringWithShadow(
                spriteBatch,
                _scoreText,
                _position,
                _textColor,
                _shadowColor,
                _shadowOffset
            );
        }

        /// <summary>
        /// Draws the score using the 7_score numbersGD.png bitmap, mirroring DTXManiaNX
        /// CActPerfDrumsScore: the "SCORE" label sprite followed by seven 36x50 digit cells.
        /// </summary>
        private void DrawBitmapScore(SpriteBatch spriteBatch)
        {
            // "SCORE" label sprite.
            _scoreNumbersTexture.Draw(spriteBatch, PerformanceUILayout.Score.LabelPosition, LabelSourceRect);

            var digitPos = PerformanceUILayout.Score.FirstDigitPosition;
            for (int i = 0; i < _scoreText.Length; i++)
            {
                char c = _scoreText[i];
                if (c >= '0' && c <= '9')
                {
                    var source = new Rectangle((c - '0') * DigitCellWidth, 0, DigitCellWidth, DigitCellHeight);
                    var position = new Vector2(digitPos.X + i * PerformanceUILayout.Score.DigitSpacing, digitPos.Y);
                    _scoreNumbersTexture.Draw(spriteBatch, position, source);
                }
            }
        }

        #endregion

        #region Private Methods

        private void LoadFont()
        {
            try
            {
                // Create a font for score display
                // Using a larger font size for visibility
                _scoreFont = ManagedFont.CreateFont(
                    _graphicsDevice,
                    "NotoSerifJP",
                    32,
                    FontStyle.Bold
                );

                // Smaller font for the "SCORE" title that sits in the 86x28 label slot above the digits.
                _titleFont = ManagedFont.CreateFont(
                    _graphicsDevice,
                    "NotoSerifJP",
                    22,
                    FontStyle.Bold
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ScoreDisplay: Failed to load font: {ex.Message}");
                // If _scoreFont was created before _titleFont threw, dispose it so the fallback
                // font resource is not leaked on the font-failure path. _titleFont is disposed
                // defensively too in case a future change assigns it before the failure point.
                _scoreFont?.Dispose();
                _scoreFont = null;
                _titleFont?.Dispose();
                _titleFont = null;
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
                    _scoreFont?.Dispose();
                    _scoreFont = null;
                    _titleFont?.Dispose();
                    _titleFont = null;
                    _scoreNumbersTexture?.RemoveReference();
                    _scoreNumbersTexture = null;
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
