using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Resources;

namespace DTX.Stage.Performance
{
    /// <summary>
    /// Score display component for PerformanceStage
    /// Displays the current score with DTXMania-style formatting
    /// </summary>
    public class ScoreDisplay : IDisposable
    {
        #region Private Fields

        private readonly ResourceManager _resourceManager;
        private readonly GraphicsDevice _graphicsDevice;
        private ManagedFont _scoreFont;
        private readonly Vector2 _position;
        private int _currentScore = 0;
        private string _scoreText = "0000000";
        private Color _textColor = Color.White;
        private Color _shadowColor = new Color(0, 0, 0, 128);
        private Vector2 _shadowOffset = new Vector2(2, 2);
        private bool _disposed = false;

        // Score formatting
        private const string ScoreFormat = "0000000";
        private const int MaxScore = 9999999;

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

        public ScoreDisplay(ResourceManager resourceManager, GraphicsDevice graphicsDevice)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _position = PerformanceUILayout.ScorePosition;

            // Initialize with default score
            Score = 0;

            // Load font
            LoadFont();
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
                
                System.Diagnostics.Debug.WriteLine("ScoreDisplay: Font loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ScoreDisplay: Failed to load font: {ex.Message}");
                _scoreFont = null;
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
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
