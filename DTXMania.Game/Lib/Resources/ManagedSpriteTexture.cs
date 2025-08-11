using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Managed sprite texture implementation for handling spritesheets
    /// Extends ManagedTexture with sprite-specific functionality
    /// </summary>
    public class ManagedSpriteTexture : ManagedTexture, ISpriteTexture
    {
        #region Private Fields

        private readonly int _spriteWidth;
        private readonly int _spriteHeight;
        private readonly int _spritesPerRow;
        private readonly int _totalSprites;

        #endregion

        #region Constructors

        /// <summary>
        /// Create sprite texture from file path
        /// </summary>
        public ManagedSpriteTexture(GraphicsDevice graphicsDevice, string filePath, string sourcePath,
                                   int spriteWidth, int spriteHeight,
                                   TextureCreationParams creationParams = null)
            : base(graphicsDevice, filePath, sourcePath, creationParams)
        {
            _spriteWidth = spriteWidth;
            _spriteHeight = spriteHeight;
            _spritesPerRow = Width / spriteWidth;
            _totalSprites = (Width / spriteWidth) * (Height / spriteHeight);
        }

        /// <summary>
        /// Create sprite texture from existing Texture2D
        /// </summary>
        public ManagedSpriteTexture(GraphicsDevice graphicsDevice, Texture2D texture, string sourcePath,
                                   int spriteWidth, int spriteHeight)
            : base(graphicsDevice, texture, sourcePath)
        {
            _spriteWidth = spriteWidth;
            _spriteHeight = spriteHeight;
            _spritesPerRow = Width / spriteWidth;
            _totalSprites = (Width / spriteWidth) * (Height / spriteHeight);
        }

        #endregion

        #region ISpriteTexture Properties

        public int SpriteWidth => _spriteWidth;
        public int SpriteHeight => _spriteHeight;
        public int SpritesPerRow => _spritesPerRow;
        public int TotalSprites => _totalSprites;
        public Vector2 SpriteSize => new Vector2(_spriteWidth, _spriteHeight);

        #endregion

        #region Sprite Methods

        /// <summary>
        /// Gets the source rectangle for a specific sprite index
        /// </summary>
        public Rectangle GetSpriteSourceRectangle(int spriteIndex)
        {
            if (spriteIndex < 0 || spriteIndex >= _totalSprites)
                throw new ArgumentOutOfRangeException(nameof(spriteIndex), $"Sprite index must be between 0 and {_totalSprites - 1}");

            int row = spriteIndex / _spritesPerRow;
            int col = spriteIndex % _spritesPerRow;

            return new Rectangle(
                col * _spriteWidth,
                row * _spriteHeight,
                _spriteWidth,
                _spriteHeight
            );
        }

        /// <summary>
        /// Gets the source rectangle for sprite at specific row and column
        /// </summary>
        public Rectangle GetSpriteSourceRectangle(int row, int col)
        {
            int totalRows = Height / _spriteHeight;
            if (row < 0 || row >= totalRows || col < 0 || col >= _spritesPerRow)
                throw new ArgumentOutOfRangeException("Row or column index is out of range");

            return new Rectangle(
                col * _spriteWidth,
                row * _spriteHeight,
                _spriteWidth,
                _spriteHeight
            );
        }

        #endregion

        #region Drawing Methods

        /// <summary>
        /// Draw specific sprite by index
        /// </summary>
        public void DrawSprite(SpriteBatch spriteBatch, int spriteIndex, Vector2 position)
        {
            if (IsDisposed || Texture == null)
                return;

            var sourceRect = GetSpriteSourceRectangle(spriteIndex);
            var color = Color.White * (Transparency / 255f);
            spriteBatch.Draw(Texture, position, sourceRect, color);
        }

        /// <summary>
        /// Draw specific sprite by index with scaling
        /// </summary>
        public void DrawSprite(SpriteBatch spriteBatch, int spriteIndex, Vector2 position, Vector2 scale)
        {
            if (IsDisposed || Texture == null)
                return;

            var sourceRect = GetSpriteSourceRectangle(spriteIndex);
            var color = Color.White * (Transparency / 255f);
            var finalScale = scale * new Vector2(ScaleRatio.X, ScaleRatio.Y);
            var rotation = ZAxisRotation;

            spriteBatch.Draw(Texture, position, sourceRect, color, rotation, Vector2.Zero, finalScale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// Draw specific sprite by index with full control
        /// </summary>
        public void DrawSprite(SpriteBatch spriteBatch, int spriteIndex, Vector2 position, Vector2 scale, 
                              float rotation, Vector2 origin, Color tintColor)
        {
            if (IsDisposed || Texture == null)
                return;

            var sourceRect = GetSpriteSourceRectangle(spriteIndex);
            var finalColor = tintColor * (Transparency / 255f);
            var finalScale = scale * new Vector2(ScaleRatio.X, ScaleRatio.Y);
            var finalRotation = rotation + ZAxisRotation;

            spriteBatch.Draw(Texture, position, sourceRect, finalColor, finalRotation, origin, finalScale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// Draw specific sprite by row and column
        /// </summary>
        public void DrawSprite(SpriteBatch spriteBatch, int row, int col, Vector2 position)
        {
            if (IsDisposed || Texture == null)
                return;

            var sourceRect = GetSpriteSourceRectangle(row, col);
            var color = Color.White * (Transparency / 255f);
            spriteBatch.Draw(Texture, position, sourceRect, color);
        }

        /// <summary>
        /// Draw sprite to destination rectangle
        /// </summary>
        public void DrawSprite(SpriteBatch spriteBatch, int spriteIndex, Rectangle destinationRectangle)
        {
            if (IsDisposed || Texture == null)
                return;

            var sourceRect = GetSpriteSourceRectangle(spriteIndex);
            var color = Color.White * (Transparency / 255f);
            spriteBatch.Draw(Texture, destinationRectangle, sourceRect, color);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Get sprite index from row and column
        /// </summary>
        public int GetSpriteIndex(int row, int col)
        {
            if (row < 0 || col < 0 || col >= _spritesPerRow)
                throw new ArgumentOutOfRangeException("Row or column index is out of range");

            return row * _spritesPerRow + col;
        }

        /// <summary>
        /// Get row and column from sprite index
        /// </summary>
        public (int row, int col) GetRowCol(int spriteIndex)
        {
            if (spriteIndex < 0 || spriteIndex >= _totalSprites)
                throw new ArgumentOutOfRangeException(nameof(spriteIndex));

            int row = spriteIndex / _spritesPerRow;
            int col = spriteIndex % _spritesPerRow;
            return (row, col);
        }

        #endregion
    }

    /// <summary>
    /// Interface for sprite texture functionality
    /// </summary>
    public interface ISpriteTexture : ITexture
    {
        int SpriteWidth { get; }
        int SpriteHeight { get; }
        int SpritesPerRow { get; }
        int TotalSprites { get; }
        Vector2 SpriteSize { get; }

        Rectangle GetSpriteSourceRectangle(int spriteIndex);
        Rectangle GetSpriteSourceRectangle(int row, int col);
        void DrawSprite(SpriteBatch spriteBatch, int spriteIndex, Vector2 position);
        void DrawSprite(SpriteBatch spriteBatch, int spriteIndex, Vector2 position, Vector2 scale);
        void DrawSprite(SpriteBatch spriteBatch, int spriteIndex, Vector2 position, Vector2 scale, float rotation, Vector2 origin, Color tintColor);
        void DrawSprite(SpriteBatch spriteBatch, int row, int col, Vector2 position);
        void DrawSprite(SpriteBatch spriteBatch, int spriteIndex, Rectangle destinationRectangle);
        int GetSpriteIndex(int row, int col);
        (int row, int col) GetRowCol(int spriteIndex);
    }
}