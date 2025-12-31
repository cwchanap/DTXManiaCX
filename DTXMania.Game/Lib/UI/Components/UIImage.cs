#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DTXMania.Game.Lib.UI.Components
{
    /// <summary>
    /// Image UI component for texture rendering
    /// Supports scaling, tinting, and texture atlas regions
    /// </summary>
    public class UIImage : UIElement
    {
        #region Private Fields

        private Texture2D? _texture;
        private Rectangle? _sourceRectangle;
        private Color _tintColor = Color.White;
        private Vector2 _scale = Vector2.One;
        private float _rotation = 0f;
        private Vector2 _origin = Vector2.Zero;
        private SpriteEffects _spriteEffects = SpriteEffects.None;
        private bool _maintainAspectRatio = true;
        private ImageScaleMode _scaleMode = ImageScaleMode.Stretch;

        #endregion

        #region Constructor

        public UIImage(Texture2D? texture = null)
        {
            _texture = texture;
            UpdateSizeFromTexture();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Texture to render
        /// </summary>
        public Texture2D? Texture
        {
            get => _texture;
            set
            {
                if (_texture != value)
                {
                    _texture = value;
                    UpdateSizeFromTexture();
                }
            }
        }

        /// <summary>
        /// Source rectangle for texture atlas regions (null for full texture)
        /// </summary>
        public Rectangle? SourceRectangle
        {
            get => _sourceRectangle;
            set
            {
                if (_sourceRectangle != value)
                {
                    _sourceRectangle = value;
                    UpdateSizeFromTexture();
                }
            }
        }

        /// <summary>
        /// Tint color applied to the texture
        /// </summary>
        public Color TintColor
        {
            get => _tintColor;
            set => _tintColor = value;
        }

        /// <summary>
        /// Scale factor for the image
        /// </summary>
        public Vector2 Scale
        {
            get => _scale;
            set => _scale = value;
        }

        /// <summary>
        /// Rotation angle in radians
        /// </summary>
        public float Rotation
        {
            get => _rotation;
            set => _rotation = value;
        }

        /// <summary>
        /// Origin point for rotation and scaling
        /// </summary>
        public Vector2 Origin
        {
            get => _origin;
            set => _origin = value;
        }

        /// <summary>
        /// Sprite effects (flip horizontal/vertical)
        /// </summary>
        public SpriteEffects SpriteEffects
        {
            get => _spriteEffects;
            set => _spriteEffects = value;
        }

        /// <summary>
        /// Whether to maintain aspect ratio when scaling
        /// </summary>
        public bool MaintainAspectRatio
        {
            get => _maintainAspectRatio;
            set => _maintainAspectRatio = value;
        }

        /// <summary>
        /// How the image should be scaled within its bounds
        /// </summary>
        public ImageScaleMode ScaleMode
        {
            get => _scaleMode;
            set => _scaleMode = value;
        }

        #endregion

        #region Overridden Methods

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible || _texture == null)
                return;

            var bounds = Bounds;
            var sourceRect = _sourceRectangle ?? new Rectangle(0, 0, _texture.Width, _texture.Height);
            
            // Calculate destination rectangle based on scale mode
            var destRect = CalculateDestinationRectangle(bounds, sourceRect);
            
            // Calculate final scale and origin
            var finalScale = CalculateFinalScale(destRect, sourceRect);
            var finalOrigin = _origin;

            // Draw the texture
            if (_rotation != 0f || finalScale != Vector2.One || finalOrigin != Vector2.Zero)
            {
                // Use rotation/scale overload
                var position = new Vector2(destRect.X + finalOrigin.X, destRect.Y + finalOrigin.Y);
                spriteBatch.Draw(_texture, position, _sourceRectangle, _tintColor, 
                    _rotation, finalOrigin, finalScale, _spriteEffects, 0f);
            }
            else
            {
                // Use simple rectangle overload for better performance
                spriteBatch.Draw(_texture, destRect, _sourceRectangle, _tintColor);
            }

            base.OnDraw(spriteBatch, deltaTime);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Update the size based on the current texture and source rectangle
        /// </summary>
        private void UpdateSizeFromTexture()
        {
            if (_texture == null)
            {
                Size = Vector2.Zero;
                return;
            }

            var sourceSize = _sourceRectangle?.Size ?? new Point(_texture.Width, _texture.Height);
            Size = new Vector2(sourceSize.X, sourceSize.Y);
        }

        /// <summary>
        /// Calculate the destination rectangle based on scale mode
        /// </summary>
        /// <param name="bounds">UI element bounds</param>
        /// <param name="sourceRect">Source rectangle</param>
        /// <returns>Destination rectangle</returns>
        private Rectangle CalculateDestinationRectangle(Rectangle bounds, Rectangle sourceRect)
        {
            switch (_scaleMode)
            {
                case ImageScaleMode.None:
                    // No scaling, use original size
                    return new Rectangle(bounds.X, bounds.Y, sourceRect.Width, sourceRect.Height);

                case ImageScaleMode.Stretch:
                    // Stretch to fill bounds
                    return bounds;

                case ImageScaleMode.Uniform:
                    // Scale uniformly to fit within bounds
                    return CalculateUniformScale(bounds, sourceRect, false);

                case ImageScaleMode.UniformToFill:
                    // Scale uniformly to fill bounds (may crop)
                    return CalculateUniformScale(bounds, sourceRect, true);

                default:
                    return bounds;
            }
        }

        /// <summary>
        /// Calculate uniform scaling rectangle
        /// </summary>
        /// <param name="bounds">Target bounds</param>
        /// <param name="sourceRect">Source rectangle</param>
        /// <param name="fillBounds">Whether to fill bounds (may crop) or fit within bounds</param>
        /// <returns>Scaled rectangle</returns>
        private Rectangle CalculateUniformScale(Rectangle bounds, Rectangle sourceRect, bool fillBounds)
        {
            float scaleX = (float)bounds.Width / sourceRect.Width;
            float scaleY = (float)bounds.Height / sourceRect.Height;
            
            float scale = fillBounds ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            
            int scaledWidth = (int)(sourceRect.Width * scale);
            int scaledHeight = (int)(sourceRect.Height * scale);
            
            int x = bounds.X + (bounds.Width - scaledWidth) / 2;
            int y = bounds.Y + (bounds.Height - scaledHeight) / 2;
            
            return new Rectangle(x, y, scaledWidth, scaledHeight);
        }

        /// <summary>
        /// Calculate final scale factor
        /// </summary>
        /// <param name="destRect">Destination rectangle</param>
        /// <param name="sourceRect">Source rectangle</param>
        /// <returns>Final scale vector</returns>
        private Vector2 CalculateFinalScale(Rectangle destRect, Rectangle sourceRect)
        {
            var baseScale = new Vector2(
                (float)destRect.Width / sourceRect.Width,
                (float)destRect.Height / sourceRect.Height
            );
            
            return baseScale * _scale;
        }

        #endregion
    }

    /// <summary>
    /// Image scaling modes
    /// </summary>
    public enum ImageScaleMode
    {
        /// <summary>
        /// No scaling, use original size
        /// </summary>
        None,

        /// <summary>
        /// Stretch to fill the entire bounds (may distort aspect ratio)
        /// </summary>
        Stretch,

        /// <summary>
        /// Scale uniformly to fit within bounds (maintains aspect ratio)
        /// </summary>
        Uniform,

        /// <summary>
        /// Scale uniformly to fill bounds (maintains aspect ratio, may crop)
        /// </summary>
        UniformToFill
    }
}
