using System;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public class ManagedSpriteTextureTests
    {
        [Fact]
        public void PropertyAndIndexHelpers_ShouldReturnExpectedValues()
        {
            var texture = CreateSpriteTexture(spriteWidth: 16, spriteHeight: 8, spritesPerRow: 4, totalSprites: 16);

            Assert.Equal(16, texture.SpriteWidth);
            Assert.Equal(8, texture.SpriteHeight);
            Assert.Equal(4, texture.SpritesPerRow);
            Assert.Equal(16, texture.TotalSprites);
            Assert.Equal(new Vector2(16, 8), texture.SpriteSize);

            Assert.Equal(new Rectangle(16, 8, 16, 8), texture.GetSpriteSourceRectangle(5));
            Assert.Equal((1, 2), texture.GetRowCol(6));
            Assert.Equal(11, texture.GetSpriteIndex(2, 3));
        }

        [Fact]
        public void IndexHelpers_WhenValuesAreOutOfRange_ShouldThrow()
        {
            var texture = CreateSpriteTexture(spriteWidth: 16, spriteHeight: 8, spritesPerRow: 4, totalSprites: 16);

            Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetSpriteSourceRectangle(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetSpriteSourceRectangle(16));
            Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetSpriteSourceRectangle(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetSpriteSourceRectangle(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetSpriteSourceRectangle(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetRowCol(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetRowCol(16));
            Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetSpriteIndex(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetSpriteIndex(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetSpriteIndex(0, 4));
        }

        [Fact]
        public void DrawSpriteOverloads_WhenTextureIsNull_ShouldReturnWithoutThrowing()
        {
            var texture = CreateSpriteTexture(spriteWidth: 16, spriteHeight: 8, spritesPerRow: 4, totalSprites: 16);

            var exception = Record.Exception(() =>
            {
                texture.DrawSprite((SpriteBatch)null!, 0, Vector2.Zero);
                texture.DrawSprite((SpriteBatch)null!, 1, new Vector2(10, 20), new Vector2(2, 3));
                texture.DrawSprite((SpriteBatch)null!, 2, new Vector2(30, 40), new Vector2(1.5f, 0.5f), 0.25f, new Vector2(2, 1), Color.Cyan);
                texture.DrawSprite((SpriteBatch)null!, 0, 1, new Vector2(50, 60));
                texture.DrawSprite((SpriteBatch)null!, 1, 2, new Vector2(70, 80), 0.5f);
                texture.DrawSprite((SpriteBatch)null!, 3, new Rectangle(5, 6, 20, 10));
            });

            Assert.Null(exception);
        }

        private static ManagedSpriteTexture CreateSpriteTexture(int spriteWidth, int spriteHeight, int spritesPerRow, int totalSprites)
        {
#pragma warning disable SYSLIB0050
            var texture = (ManagedSpriteTexture)FormatterServices.GetUninitializedObject(typeof(ManagedSpriteTexture));
#pragma warning restore SYSLIB0050

            ReflectionHelpers.SetPrivateField(texture, "_texture", null);
            ReflectionHelpers.SetPrivateField(texture, "_sourcePath", "test.png");
            ReflectionHelpers.SetPrivateField(texture, "_referenceCount", 0);
            ReflectionHelpers.SetPrivateField(texture, "_disposed", false);
            ReflectionHelpers.SetPrivateField(texture, "_lockObject", new object());
            ReflectionHelpers.SetPrivateField(texture, "_transparency", 255);
            ReflectionHelpers.SetPrivateField(texture, "_scaleRatio", Vector3.One);
            ReflectionHelpers.SetPrivateField(texture, "_zAxisRotation", 0f);
            ReflectionHelpers.SetPrivateField(texture, "_additiveBlending", false);
            ReflectionHelpers.SetPrivateField(texture, "_spriteWidth", spriteWidth);
            ReflectionHelpers.SetPrivateField(texture, "_spriteHeight", spriteHeight);
            ReflectionHelpers.SetPrivateField(texture, "_spritesPerRow", spritesPerRow);
            ReflectionHelpers.SetPrivateField(texture, "_totalSprites", totalSprites);

            return texture;
        }
    }
}
