using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Individual judgement text popup that displays and animates
    /// Spawns on JudgementEvent and fades out over 0.6s while rising 30px
    /// </summary>
    public class JudgementTextPopup
    {
        #region Fields

        private readonly string _text;
        private readonly Vector2 _initialPosition;
        private float _alpha;
        private float _yOffset;
        private float _elapsedTime;
        private bool _isActive;

        // Animation constants
        private const float FadeDuration = 0.6f; // 0.6 seconds
        private const float RiseDistance = 30f;  // 30 pixels

        #endregion

        #region Properties

        public string Text => _text;
        public float Alpha => _alpha;
        public float YOffset => _yOffset;
        public bool IsActive => _isActive;
        public Vector2 CurrentPosition => new Vector2(_initialPosition.X, _initialPosition.Y - _yOffset);

        #endregion

        #region Constructor

        public JudgementTextPopup(string text, Vector2 position)
        {
            _text = text ?? string.Empty;
            _initialPosition = position;
            _alpha = 1.0f;
            _yOffset = 0f;
            _elapsedTime = 0f;
            _isActive = true;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Update the popup animation
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        /// <returns>True if popup is still active, false if completed</returns>
        public bool Update(double deltaTime)
        {
            if (!_isActive)
                return false;

            _elapsedTime += (float)deltaTime;

            // Calculate progress (0.0 to 1.0)
            float progress = _elapsedTime / FadeDuration;

            if (progress >= 1.0f)
            {
                // Animation complete
                _isActive = false;
                _alpha = 0f;
                _yOffset = RiseDistance;
                return false;
            }

            // Update alpha (fade out)
            _alpha = 1.0f - progress;

            // Update y offset (rise up)
            _yOffset = progress * RiseDistance;

            return true;
        }

        #endregion
    }

    /// <summary>
    /// Manages all judgement text popups for the performance stage
    /// Spawns popups on JudgementEvents and handles their lifecycle
    /// </summary>
    public class JudgementTextPopupManager : IDisposable
    {
        #region Fields

        private readonly List<JudgementTextPopup> _activePopups;
        private readonly BitmapFont _font;
        private readonly IResourceManager _resourceManager;
        private readonly GraphicsDevice _graphicsDevice;
        private bool _disposed = false;

        // Text colors for different judgement types
        private static readonly Dictionary<JudgementType, Color> JudgementColors = new Dictionary<JudgementType, Color>
        {
            { JudgementType.Just, Color.Yellow },
            { JudgementType.Great, Color.LightGreen },
            { JudgementType.Good, Color.LightBlue },
            { JudgementType.Poor, Color.Orange },
            { JudgementType.Miss, Color.Red }
        };

        // Text display strings for judgement types
        private static readonly Dictionary<JudgementType, string> JudgementTexts = new Dictionary<JudgementType, string>
        {
            { JudgementType.Just, "Perfect" },
            { JudgementType.Great, "Great" },
            { JudgementType.Good, "Good" },
            { JudgementType.Poor, "OK" },
            { JudgementType.Miss, "Miss" }
        };

        #endregion

        #region Constructor

        public JudgementTextPopupManager(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _activePopups = new List<JudgementTextPopup>();

            // Load the NotoSerifJP Bold 28 font
            try
            {
                var fontConfig = CreateJudgementTextFontConfig();
                _font = new BitmapFont(_graphicsDevice, _resourceManager, fontConfig);

                if (!_font.IsLoaded)
                {
                    System.Diagnostics.Debug.WriteLine("JudgementTextPopupManager: Failed to load judgement text font");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JudgementTextPopupManager: Error loading font: {ex.Message}");
                _font = null;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Spawn a judgement text popup for a judgement event
        /// </summary>
        /// <param name="judgementEvent">The judgement event that occurred</param>
        public void SpawnPopup(JudgementEvent judgementEvent)
        {
            if (_disposed || judgementEvent == null)
                return;

            // Get the lane center position
            var lanePosition = GetLaneCenterPosition(judgementEvent.Lane);
            
            // Get the text for this judgement type
            if (JudgementTexts.TryGetValue(judgementEvent.Type, out var text))
            {
                var popup = new JudgementTextPopup(text, lanePosition);
                _activePopups.Add(popup);
            }
        }

        /// <summary>
        /// Update all active popups
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime)
        {
            if (_disposed)
                return;

            // Update all popups and remove completed ones
            for (int i = _activePopups.Count - 1; i >= 0; i--)
            {
                if (!_activePopups[i].Update(deltaTime))
                {
                    _activePopups.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Draw all active popups
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null || _font == null || !_font.IsLoaded)
                return;

            foreach (var popup in _activePopups)
            {
                if (popup.IsActive && popup.Alpha > 0f)
                {
                    // Get color for the text (determine judgement type from text)
                    var color = GetJudgementColor(popup.Text);
                    
                    // Apply alpha fade
                    color = color * popup.Alpha;

                    // Draw the text at the current position
                    var position = popup.CurrentPosition;
                    _font.DrawText(spriteBatch, popup.Text, (int)position.X, (int)position.Y, color);
                }
            }
        }

        /// <summary>
        /// Clear all active popups
        /// </summary>
        public void ClearAll()
        {
            _activePopups.Clear();
        }

        /// <summary>
        /// Get the number of active popups
        /// </summary>
        public int ActivePopupCount => _activePopups.Count;

        #endregion

        #region Private Methods

        /// <summary>
        /// Get the center position for a specific lane
        /// </summary>
        /// <param name="laneIndex">Lane index (0-8)</param>
        /// <returns>Center position of the lane</returns>
        private Vector2 GetLaneCenterPosition(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= PerformanceUILayout.LaneCount)
            {
                // Default to center of screen if invalid lane
                return new Vector2(PerformanceUILayout.ScreenWidth / 2, PerformanceUILayout.JudgementLineY - 50);
            }

            // Get lane center X position
            var laneX = PerformanceUILayout.GetLaneX(laneIndex);
            
            // Position text slightly above the judgement line
            var textY = PerformanceUILayout.JudgementLineY - 50;

            return new Vector2(laneX, textY);
        }

        /// <summary>
        /// Get the color for a judgement type based on text
        /// </summary>
        /// <param name="text">The judgement text</param>
        /// <returns>Color for the judgement</returns>
        private Color GetJudgementColor(string text)
        {
            return text switch
            {
                "Perfect" => Color.Yellow,
                "Great" => Color.LightGreen,
                "Good" => Color.LightBlue,
                "OK" => Color.Orange,
                "Miss" => Color.Red,
                _ => Color.White
            };
        }

        /// <summary>
        /// Create font configuration for judgement text
        /// Based on NotoSerifJP Bold 28 specification
        /// </summary>
        private BitmapFont.BitmapFontConfig CreateJudgementTextFontConfig()
        {
            // Use the standardized judgement text font configuration
            return BitmapFont.CreateJudgementTextFontConfig();
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
                    // Clear all active popups
                    _activePopups.Clear();

                    // Dispose font
                    _font?.Dispose();
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
