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
        private readonly Vector2 _position;
        private const string TitleText = "SCORE";
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
        /// through the <see cref="ManagedFont"/> factory directly, so the manager is only
        /// used here for the null-argument guard. Do not remove without updating every
        /// performance-component constructor call site.
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

            // Load font
            try
            {
                LoadFont();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("ScoreDisplay could not be initialized because the font could not be loaded.", ex);
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
            if (_disposed || spriteBatch == null || _scoreFont == null)
                return;

            // Draw the "SCORE" title above the digits. The performance background is plain,
            // so the label must be rendered here (its slot is PerformanceUILayout.Score.LabelPosition).
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
                _scoreFont = null;
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
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
