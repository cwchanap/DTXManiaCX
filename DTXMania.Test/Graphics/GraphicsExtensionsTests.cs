using DTX.Config;
using DTX.Graphics;

namespace DTXMania.Test.Graphics;

public class GraphicsExtensionsTests
{
    [Fact]
    public void ToGraphicsSettings_ShouldConvertConfigDataCorrectly()
    {
        // Arrange
        var config = new ConfigData
        {
            ScreenWidth = 1920,
            ScreenHeight = 1080,
            FullScreen = true,
            VSyncWait = false
        };

        // Act
        var settings = config.ToGraphicsSettings();

        // Assert
        Assert.Equal(1920, settings.Width);
        Assert.Equal(1080, settings.Height);
        Assert.True(settings.IsFullscreen);
        Assert.False(settings.VSync);
    }

    [Fact]
    public void UpdateFromGraphicsSettings_ShouldUpdateConfigDataCorrectly()
    {
        // Arrange
        var config = new ConfigData
        {
            ScreenWidth = 1280,
            ScreenHeight = 720,
            FullScreen = false,
            VSyncWait = true
        };

        var settings = new GraphicsSettings
        {
            Width = 1920,
            Height = 1080,
            IsFullscreen = true,
            VSync = false
        };

        // Act
        config.UpdateFromGraphicsSettings(settings);

        // Assert
        Assert.Equal(1920, config.ScreenWidth);
        Assert.Equal(1080, config.ScreenHeight);
        Assert.True(config.FullScreen);
        Assert.False(config.VSyncWait);
    }

    [Fact]
    public void ToGraphicsSettings_WithDefaultConfig_ShouldReturnValidSettings()
    {
        // Arrange
        var config = new ConfigData(); // Default values

        // Act
        var settings = config.ToGraphicsSettings();

        // Assert
        Assert.True(settings.IsValid());
        Assert.Equal(config.ScreenWidth, settings.Width);
        Assert.Equal(config.ScreenHeight, settings.Height);
        Assert.Equal(config.FullScreen, settings.IsFullscreen);
        Assert.Equal(config.VSyncWait, settings.VSync);
    }

    [Theory]
    [InlineData(800, 600, false, true)]
    [InlineData(1366, 768, true, false)]
    [InlineData(2560, 1440, false, false)]
    public void ConfigToGraphicsSettings_RoundTrip_ShouldPreserveValues(
        int width, int height, bool fullscreen, bool vsync)
    {
        // Arrange
        var originalConfig = new ConfigData
        {
            ScreenWidth = width,
            ScreenHeight = height,
            FullScreen = fullscreen,
            VSyncWait = vsync
        };

        // Act
        var settings = originalConfig.ToGraphicsSettings();
        var newConfig = new ConfigData();
        newConfig.UpdateFromGraphicsSettings(settings);

        // Assert
        Assert.Equal(originalConfig.ScreenWidth, newConfig.ScreenWidth);
        Assert.Equal(originalConfig.ScreenHeight, newConfig.ScreenHeight);
        Assert.Equal(originalConfig.FullScreen, newConfig.FullScreen);
        Assert.Equal(originalConfig.VSyncWait, newConfig.VSyncWait);
    }
}
