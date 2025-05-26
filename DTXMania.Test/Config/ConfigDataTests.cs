using DTX.Config;

namespace DTXMania.Test.Config;

public class ConfigDataTests
{
    [Fact]
    public void ConfigData_DefaultValues_ShouldBeValid()
    {
        // Arrange & Act
        var config = new ConfigData();

        // Assert
        Assert.Equal("NX1.5.0-MG", config.DTXManiaVersion);
        Assert.Equal("System/Default/", config.SkinPath);
        Assert.Equal("DTXFiles/", config.DTXPath);
        Assert.Equal(1280, config.ScreenWidth);
        Assert.Equal(720, config.ScreenHeight);
        Assert.False(config.FullScreen);
        Assert.True(config.VSyncWait);
        Assert.Equal(100, config.MasterVolume);
        Assert.Equal(100, config.BGMVolume);
        Assert.Equal(100, config.SEVolume);
        Assert.Equal(100, config.BufferSizeMs);
        Assert.NotNull(config.KeyBindings);
        Assert.Equal(100, config.ScrollSpeed);
        Assert.False(config.AutoPlay);
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    public void ConfigData_SetResolution_ShouldUpdateCorrectly(int width, int height)
    {
        // Arrange
        var config = new ConfigData();

        // Act
        config.ScreenWidth = width;
        config.ScreenHeight = height;

        // Assert
        Assert.Equal(width, config.ScreenWidth);
        Assert.Equal(height, config.ScreenHeight);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigData_SetFullScreen_ShouldUpdateCorrectly(bool fullScreen)
    {
        // Arrange
        var config = new ConfigData();

        // Act
        config.FullScreen = fullScreen;

        // Assert
        Assert.Equal(fullScreen, config.FullScreen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigData_SetVSyncWait_ShouldUpdateCorrectly(bool vsyncWait)
    {
        // Arrange
        var config = new ConfigData();

        // Act
        config.VSyncWait = vsyncWait;

        // Assert
        Assert.Equal(vsyncWait, config.VSyncWait);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void ConfigData_SetVolumeSettings_ShouldUpdateCorrectly(int volume)
    {
        // Arrange
        var config = new ConfigData();

        // Act
        config.MasterVolume = volume;
        config.BGMVolume = volume;
        config.SEVolume = volume;

        // Assert
        Assert.Equal(volume, config.MasterVolume);
        Assert.Equal(volume, config.BGMVolume);
        Assert.Equal(volume, config.SEVolume);
    }

    [Fact]
    public void ConfigData_KeyBindings_ShouldBeInitialized()
    {
        // Arrange & Act
        var config = new ConfigData();

        // Assert
        Assert.NotNull(config.KeyBindings);
        Assert.IsType<Dictionary<string, int>>(config.KeyBindings);
    }

    [Fact]
    public void ConfigData_KeyBindings_ShouldAllowAddingEntries()
    {
        // Arrange
        var config = new ConfigData();

        // Act
        config.KeyBindings["TestKey"] = 42;

        // Assert
        Assert.True(config.KeyBindings.ContainsKey("TestKey"));
        Assert.Equal(42, config.KeyBindings["TestKey"]);
    }

    [Theory]
    [InlineData("Custom/Skin/", "Custom/DTX/")]
    [InlineData("", "")]
    [InlineData("System/Default/", "DTXFiles/")]
    public void ConfigData_SetPaths_ShouldUpdateCorrectly(string skinPath, string dtxPath)
    {
        // Arrange
        var config = new ConfigData();

        // Act
        config.SkinPath = skinPath;
        config.DTXPath = dtxPath;

        // Assert
        Assert.Equal(skinPath, config.SkinPath);
        Assert.Equal(dtxPath, config.DTXPath);
    }
}
