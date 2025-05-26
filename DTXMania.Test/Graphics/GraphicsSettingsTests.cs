using DTX.Graphics;

namespace DTXMania.Test.Graphics;

public class GraphicsSettingsTests
{
    [Fact]
    public void GraphicsSettings_DefaultValues_ShouldBeValid()
    {
        // Arrange & Act
        var settings = new GraphicsSettings();

        // Assert
        Assert.Equal(1280, settings.Width);
        Assert.Equal(720, settings.Height);
        Assert.False(settings.IsFullscreen);
        Assert.True(settings.VSync);
        Assert.True(settings.IsValid());
    }

    [Theory]
    [InlineData(1920, 1080, true)]
    [InlineData(1280, 720, true)]
    [InlineData(800, 600, true)]
    [InlineData(3840, 2160, true)]
    public void GraphicsSettings_ValidResolutions_ShouldBeValid(int width, int height, bool expectedValid)
    {
        // Arrange
        var settings = new GraphicsSettings
        {
            Width = width,
            Height = height
        };

        // Act
        var isValid = settings.IsValid();

        // Assert
        Assert.Equal(expectedValid, isValid);
    }

    [Theory]
    [InlineData(0, 720, false)]
    [InlineData(1280, 0, false)]
    [InlineData(-1, 720, false)]
    [InlineData(1280, -1, false)]
    [InlineData(10000, 720, false)]
    [InlineData(1280, 10000, false)]
    public void GraphicsSettings_InvalidResolutions_ShouldBeInvalid(int width, int height, bool expectedValid)
    {
        // Arrange
        var settings = new GraphicsSettings
        {
            Width = width,
            Height = height
        };

        // Act
        var isValid = settings.IsValid();

        // Assert
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void GraphicsSettings_Clone_ShouldCreateExactCopy()
    {
        // Arrange
        var original = new GraphicsSettings
        {
            Width = 1920,
            Height = 1080,
            IsFullscreen = true,
            VSync = false,
            MultiSampleCount = 4
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.Width, clone.Width);
        Assert.Equal(original.Height, clone.Height);
        Assert.Equal(original.IsFullscreen, clone.IsFullscreen);
        Assert.Equal(original.VSync, clone.VSync);
        Assert.Equal(original.MultiSampleCount, clone.MultiSampleCount);
    }

    [Fact]
    public void GraphicsSettings_Equals_ShouldCompareCorrectly()
    {
        // Arrange
        var settings1 = new GraphicsSettings
        {
            Width = 1920,
            Height = 1080,
            IsFullscreen = true,
            VSync = false
        };

        var settings2 = new GraphicsSettings
        {
            Width = 1920,
            Height = 1080,
            IsFullscreen = true,
            VSync = false
        };

        var settings3 = new GraphicsSettings
        {
            Width = 1280,
            Height = 720,
            IsFullscreen = false,
            VSync = true
        };

        // Act & Assert
        Assert.True(settings1.Equals(settings2));
        Assert.False(settings1.Equals(settings3));
        Assert.False(settings1.Equals(null));
    }

    [Fact]
    public void GraphicsSettings_AspectRatio_ShouldCalculateCorrectly()
    {
        // Arrange
        var settings = new GraphicsSettings
        {
            Width = 1920,
            Height = 1080
        };

        // Act
        var aspectRatio = settings.AspectRatio;

        // Assert
        Assert.Equal(16.0 / 9.0, aspectRatio, 5);
    }

    [Fact]
    public void GraphicsSettings_GetCommonResolutions_ShouldReturnExpectedResolutions()
    {
        // Act
        var resolutions = GraphicsSettings.GetCommonResolutions().ToList();

        // Assert
        Assert.NotEmpty(resolutions);
        Assert.Contains((1280, 720), resolutions);
        Assert.Contains((1920, 1080), resolutions);
        Assert.Contains((3840, 2160), resolutions);
    }

    [Fact]
    public void GraphicsSettings_ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var settings = new GraphicsSettings
        {
            Width = 1920,
            Height = 1080,
            IsFullscreen = true,
            VSync = false,
            MultiSampleCount = 4
        };

        // Act
        var result = settings.ToString();

        // Assert
        Assert.Contains("1920x1080", result);
        Assert.Contains("Fullscreen", result);
        Assert.Contains("VSync:False", result);
        Assert.Contains("MSAA:4", result);
    }
}
