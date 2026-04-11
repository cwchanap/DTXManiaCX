using System.Runtime.Serialization;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Test.UI;

[Trait("Category", "Unit")]
public class UIImageTests
{
    [Fact]
    public void Constructor_WithNullTexture_ShouldUseDefaultState()
    {
        var image = new ExposedUIImage();

        Assert.Null(image.Texture);
        Assert.Null(image.SourceRectangle);
        Assert.Equal(Vector2.Zero, image.Size);
        Assert.Equal(Color.White, image.TintColor);
        Assert.Equal(Vector2.One, image.Scale);
        Assert.Equal(0f, image.Rotation);
        Assert.Equal(Vector2.Zero, image.Origin);
        Assert.Equal(SpriteEffects.None, image.SpriteEffects);
        Assert.True(image.MaintainAspectRatio);
        Assert.Equal(ImageScaleMode.Stretch, image.ScaleMode);
    }

    [Fact]
    public void PropertySetters_ShouldRoundTripAssignedValues()
    {
        var image = new ExposedUIImage();

        image.TintColor = Color.CornflowerBlue;
        image.Scale = new Vector2(1.5f, 0.75f);
        image.Rotation = 0.8f;
        image.Origin = new Vector2(12f, 9f);
        image.SpriteEffects = SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically;
        image.MaintainAspectRatio = false;
        image.ScaleMode = ImageScaleMode.UniformToFill;

        Assert.Equal(Color.CornflowerBlue, image.TintColor);
        Assert.Equal(new Vector2(1.5f, 0.75f), image.Scale);
        Assert.Equal(0.8f, image.Rotation);
        Assert.Equal(new Vector2(12f, 9f), image.Origin);
        Assert.Equal(SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically, image.SpriteEffects);
        Assert.False(image.MaintainAspectRatio);
        Assert.Equal(ImageScaleMode.UniformToFill, image.ScaleMode);
    }

    [Fact]
    public void TextureSetter_WhenClearedFromExistingTexture_ShouldResetSizeToZero()
    {
        var image = new ExposedUIImage();

        ReflectionHelpers.SetPrivateField(image, "_texture", CreateTextureStub());
        image.Size = new Vector2(320f, 180f);

        image.Texture = null;

        Assert.Null(image.Texture);
        Assert.Equal(Vector2.Zero, image.Size);
    }

    [Fact]
    public void SourceRectangleSetter_WhenTextureMissing_ShouldKeepSizeZero()
    {
        var image = new ExposedUIImage();

        image.SourceRectangle = new Rectangle(5, 10, 20, 30);

        Assert.Equal(new Rectangle(5, 10, 20, 30), image.SourceRectangle);
        Assert.Equal(Vector2.Zero, image.Size);
    }

    [Fact]
    public void CalculateDestinationRectangle_WithNoneScaleMode_ShouldUseSourceSizeAtBoundsOrigin()
    {
        var image = new ExposedUIImage
        {
            ScaleMode = ImageScaleMode.None
        };

        var result = InvokePrivate<Rectangle>(
            image,
            "CalculateDestinationRectangle",
            new Rectangle(10, 20, 200, 120),
            new Rectangle(0, 0, 80, 40));

        Assert.Equal(new Rectangle(10, 20, 80, 40), result);
    }

    [Fact]
    public void CalculateDestinationRectangle_WithStretchScaleMode_ShouldReturnBounds()
    {
        var image = new ExposedUIImage
        {
            ScaleMode = ImageScaleMode.Stretch
        };

        var bounds = new Rectangle(10, 20, 200, 120);
        var result = InvokePrivate<Rectangle>(
            image,
            "CalculateDestinationRectangle",
            bounds,
            new Rectangle(0, 0, 80, 40));

        Assert.Equal(bounds, result);
    }

    [Fact]
    public void CalculateDestinationRectangle_WithUniformScaleMode_ShouldFitWithinBounds()
    {
        var image = new ExposedUIImage
        {
            ScaleMode = ImageScaleMode.Uniform
        };

        var result = InvokePrivate<Rectangle>(
            image,
            "CalculateDestinationRectangle",
            new Rectangle(0, 0, 200, 120),
            new Rectangle(0, 0, 100, 40));

        Assert.Equal(new Rectangle(0, 20, 200, 80), result);
    }

    [Fact]
    public void CalculateDestinationRectangle_WithUniformToFillScaleMode_ShouldFillBounds()
    {
        var image = new ExposedUIImage
        {
            ScaleMode = ImageScaleMode.UniformToFill
        };

        var result = InvokePrivate<Rectangle>(
            image,
            "CalculateDestinationRectangle",
            new Rectangle(0, 0, 200, 120),
            new Rectangle(0, 0, 100, 40));

        Assert.Equal(new Rectangle(-50, 0, 300, 120), result);
    }

    [Fact]
    public void CalculateDestinationRectangle_WithUnknownScaleMode_ShouldFallbackToBounds()
    {
        var image = new ExposedUIImage
        {
            ScaleMode = (ImageScaleMode)999
        };

        var bounds = new Rectangle(10, 20, 200, 120);
        var result = InvokePrivate<Rectangle>(
            image,
            "CalculateDestinationRectangle",
            bounds,
            new Rectangle(0, 0, 80, 40));

        Assert.Equal(bounds, result);
    }

    [Theory]
    [InlineData(false, 0, 20, 200, 80)]
    [InlineData(true, -50, 0, 300, 120)]
    public void CalculateUniformScale_ShouldCenterScaledRectangle(bool fillBounds, int expectedX, int expectedY, int expectedWidth, int expectedHeight)
    {
        var image = new ExposedUIImage();

        var result = InvokePrivate<Rectangle>(
            image,
            "CalculateUniformScale",
            new Rectangle(0, 0, 200, 120),
            new Rectangle(0, 0, 100, 40),
            fillBounds);

        Assert.Equal(new Rectangle(expectedX, expectedY, expectedWidth, expectedHeight), result);
    }

    [Fact]
    public void CalculateFinalScale_ShouldCombineBaseScaleAndImageScale()
    {
        var image = new ExposedUIImage
        {
            Scale = new Vector2(2f, 0.5f)
        };

        var result = InvokePrivate<Vector2>(
            image,
            "CalculateFinalScale",
            new Rectangle(0, 0, 200, 90),
            new Rectangle(0, 0, 100, 30));

        Assert.Equal(new Vector2(4f, 1.5f), result);
    }

    [Fact]
    public void OnDraw_WhenTextureIsNull_ShouldReturnWithoutThrowing()
    {
        var image = new ExposedUIImage();

        var exception = Record.Exception(() => image.InvokeOnDraw(null!, 0.0));

        Assert.Null(exception);
    }

    [Fact]
    public void OnDraw_WhenInvisible_ShouldReturnWithoutThrowing()
    {
        var image = new ExposedUIImage
        {
            Visible = false
        };
        ReflectionHelpers.SetPrivateField(image, "_texture", CreateTextureStub());

        var exception = Record.Exception(() => image.InvokeOnDraw(null!, 0.0));

        Assert.Null(exception);
    }

    private static T InvokePrivate<T>(object target, string methodName, params object[] args)
    {
        return ReflectionHelpers.InvokePrivateMethod<T>(target, methodName, args)!;
    }

    private static Texture2D CreateTextureStub()
    {
#pragma warning disable SYSLIB0050
        return (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
#pragma warning restore SYSLIB0050
    }

    private sealed class ExposedUIImage : UIImage
    {
        public void InvokeOnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            base.OnDraw(spriteBatch, deltaTime);
        }
    }
}
