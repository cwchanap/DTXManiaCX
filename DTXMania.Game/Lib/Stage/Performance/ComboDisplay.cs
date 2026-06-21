using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Combo display component for PerformanceStage
    /// Displays the current combo count with DTXMania-style formatting
    /// Hidden when combo is 0
    /// </summary>
    public class ComboDisplay : IDisposable
    {
        #region Private Fields

        private readonly IResourceManager _resourceManager;
        private readonly GraphicsDevice _graphicsDevice;
        private ManagedFont _comboFont;
        private ManagedFont _labelFont;
        private ITexture _comboTexture;
        private ITexture _comboTextureAlt;
        private readonly Vector2 _position;
        private int _currentCombo = 0;
        private string _comboText = "0";
        private string _labelText = "COMBO";
        private Color _textColor = Color.White;
        private Color _shadowColor = PerformanceUILayout.ComboDisplay.ShadowColor;
        private Vector2 _shadowOffset = new Vector2(2, 2); // From PerformanceUILayout.ComboDisplay.ShadowOffset
        private bool _disposed = false;
        private bool _visible = false;

        // Animation properties
        private float _scale = 1.0f;
        private float _targetScale = 1.0f;
        private float _scaleVelocity = 0.0f;
        private const float ScaleDamping = 0.2f; // From PerformanceUILayout.Animation.ScaleDamping
        private const float ComboHitScale = 1.5f; // From PerformanceUILayout.Effects.ComboHitScale

        #endregion

        #region Properties

        public readonly struct ComboGlyphDraw
        {
            public ComboGlyphDraw(string texturePath, Rectangle sourceRectangle, Vector2 position)
            {
                TexturePath = texturePath;
                SourceRectangle = sourceRectangle;
                Position = position;
            }

            public string TexturePath { get; }
            public Rectangle SourceRectangle { get; }
            public Vector2 Position { get; }
        }

        /// <summary>
        /// Current combo value
        /// </summary>
        public int Combo
        {
            get => _currentCombo;
            set
            {
                // Check if combo is increasing
                bool isIncreasing = value > _currentCombo;
                
                // Update combo value
                _currentCombo = Math.Max(0, value);
                
                // Update combo text
                _comboText = _currentCombo.ToString();
                
                // Update visibility
                _visible = _currentCombo > 0;
                
                // Trigger animation when combo increases
                if (isIncreasing && _visible)
                {
                    _targetScale = ComboHitScale;
                }
            }
        }

        /// <summary>
        /// Text color for the combo display
        /// </summary>
        public Color TextColor
        {
            get => _textColor;
            set => _textColor = value;
        }

        /// <summary>
        /// Shadow color for the combo display
        /// </summary>
        public Color ShadowColor
        {
            get => _shadowColor;
            set => _shadowColor = value;
        }

        /// <summary>
        /// Shadow offset for the combo display
        /// </summary>
        public Vector2 ShadowOffset
        {
            get => _shadowOffset;
            set => _shadowOffset = value;
        }

        #endregion

        #region Constructor

        public ComboDisplay(IResourceManager resourceManager, GraphicsDevice graphicsDevice)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _position = PerformanceUILayout.ComboPosition;

            // Initialize with default combo (hidden)
            Combo = 0;

            // Load fonts
            LoadFonts();

            // Load NX combo number sheets. Font rendering remains the fallback for
            // missing/custom skins, but the skin textures give the correct alignment.
            LoadTextures();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Update the combo display animation
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last update</param>
        public void Update(double deltaTime)
        {
            if (_disposed)
                return;

            // Animate scale with spring physics
            float dt = (float)deltaTime;
            
            // Spring animation toward target scale
            float scaleDiff = _targetScale - _scale;
            _scaleVelocity += scaleDiff * 8.0f * dt;
            _scaleVelocity *= (1.0f - ScaleDamping);
            
            _scale += _scaleVelocity * dt;
            
            // Reset target scale gradually
            _targetScale = MathHelper.Lerp(_targetScale, 1.0f, dt * 5.0f);
        }

        /// <summary>
        /// Draw the combo display
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        public void Draw(SpriteBatch spriteBatch)
        {
            if (_disposed || spriteBatch == null || !_visible)
                return;

            if (_comboTexture != null)
            {
                var glyphs = CalculateDrumComboGlyphs(_currentCombo, _position);

                // Pre-pass: validate all glyph textures are available before drawing any
                var allTexturesAvailable = true;
                foreach (var glyph in glyphs)
                {
                    var texture = glyph.TexturePath == TexturePath.ComboDisplayAlt ? _comboTextureAlt : _comboTexture;
                    if (texture == null)
                    {
                        allTexturesAvailable = false;
                        break;
                    }
                }

                if (allTexturesAvailable)
                {
                    foreach (var glyph in glyphs)
                    {
                        var texture = glyph.TexturePath == TexturePath.ComboDisplayAlt ? _comboTextureAlt : _comboTexture;
                        texture.Draw(spriteBatch, glyph.Position, glyph.SourceRectangle);
                    }
                    return;
                }
            }

            if (_comboFont == null || _labelFont == null)
                return;

            // Measure text sizes for centering
            Vector2 comboSize = _comboFont.MeasureString(_comboText);
            Vector2 labelSize = _labelFont.MeasureString(_labelText);
            
            // Calculate positions for centered text
            
            Vector2 labelPos = new Vector2(
                _position.X - (labelSize.X / 2),
                _position.Y
            );
            
            // Draw combo number with shadow and scaling
            // Apply scale to the combo text
            Vector2 origin = comboSize / 2;
            Vector2 scaledPos = _position - new Vector2(0, comboSize.Y / 2);

            // Draw shadow first
            spriteBatch.DrawString(
                _comboFont.SpriteFont,
                _comboText,
                scaledPos + _shadowOffset,
                _shadowColor,
                0f,
                origin,
                _scale,
                SpriteEffects.None,
                0f
            );

            // Draw main text
            spriteBatch.DrawString(
                _comboFont.SpriteFont,
                _comboText,
                scaledPos,
                _textColor,
                0f,
                origin,
                _scale,
                SpriteEffects.None,
                0f
            );
            
            // Draw "COMBO" label with shadow
            _labelFont.DrawStringWithShadow(
                spriteBatch,
                _labelText,
                labelPos,
                _textColor,
                _shadowColor,
                _shadowOffset
            );
        }

        #endregion

        #region Private Methods

        public static IReadOnlyList<ComboGlyphDraw> CalculateDrumComboGlyphs(int combo)
        {
            return CalculateDrumComboGlyphs(combo, PerformanceUILayout.Combo.BasePosition);
        }

        public static IReadOnlyList<ComboGlyphDraw> CalculateDrumComboGlyphs(int combo, Vector2 centerPosition)
        {
            if (combo <= 0)
                return Array.Empty<ComboGlyphDraw>();

            const int digitWidth = 120;
            const int digitHeight = 160;
            const int smallDigitWidth = 96;
            const int smallDigitHeight = 128;
            const int digitSpacing = -6;
            const int comboLabelWidth = 250;
            const int comboLabelHeight = 60;

            var digits = new List<int>();
            var value = combo;
            while (value > 0 && digits.Count < 10)
            {
                digits.Add(value % 10);
                value /= 10;
            }

            var glyphs = new List<ComboGlyphDraw>(digits.Count + 1);

            var labelX = (int)centerPosition.X - 68 - (int)(comboLabelWidth / 1.3f);
            var labelY = (int)centerPosition.Y + 162;
            glyphs.Add(new ComboGlyphDraw(
                TexturePath.ComboDisplay,
                new Rectangle(0, 320, comboLabelWidth, comboLabelHeight),
                new Vector2(labelX, labelY)));

            var x = (int)centerPosition.X;
            var useSmallDigits = digits.Count >= 4;
            var texturePath = useSmallDigits ? TexturePath.ComboDisplayAlt : TexturePath.ComboDisplay;
            var drawWidth = useSmallDigits ? smallDigitWidth : digitWidth;
            var drawHeight = useSmallDigits ? smallDigitHeight : digitHeight;
            var drawY = (int)centerPosition.Y + (useSmallDigits ? 20 : 0);

            for (var i = 0; i < digits.Count; i++)
            {
                var digit = digits[i];
                x -= drawWidth + digitSpacing;
                glyphs.Add(new ComboGlyphDraw(
                    texturePath,
                    new Rectangle((digit % 5) * drawWidth, (digit / 5) * drawHeight, drawWidth, drawHeight),
                    new Vector2(x, drawY)));
            }

            return glyphs;
        }

        private void LoadFonts()
        {
            try
            {
                // Create fonts for combo display
                _comboFont = ManagedFont.CreateFont(
                    _graphicsDevice,
                    "NotoSerifJP",
                    48,
                    FontStyle.Bold
                );

                _labelFont = ManagedFont.CreateFont(
                    _graphicsDevice,
                    "NotoSerifJP",
                    20,
                    FontStyle.Regular
                );
                
                System.Diagnostics.Debug.WriteLine("ComboDisplay: Fonts loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ComboDisplay: Failed to load fonts: {ex.Message}");
                _comboFont = null;
                _labelFont = null;
            }
        }

        private void LoadTextures()
        {
            try
            {
                _comboTexture = _resourceManager.LoadTexture(TexturePath.ComboDisplay);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ComboDisplay: Failed to load combo texture: {ex.Message}");
                _comboTexture = null;
            }

            try
            {
                _comboTextureAlt = _resourceManager.LoadTexture(TexturePath.ComboDisplayAlt);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ComboDisplay: Failed to load combo alt texture: {ex.Message}");
                _comboTextureAlt = null;
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
                    _comboFont?.Dispose();
                    _comboFont = null;
                    _labelFont?.Dispose();
                    _labelFont = null;
                    _comboTexture?.RemoveReference();
                    _comboTexture = null;
                    _comboTextureAlt?.RemoveReference();
                    _comboTextureAlt = null;
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
