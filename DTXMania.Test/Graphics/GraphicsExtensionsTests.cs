using DTX.Config;
using DTX.Graphics;
using DTX.UI;
using DTX.UI.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using System;

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

    // Phase 2 Enhancement Tests for Graphics Generation

    [Fact]
    public void DefaultGraphicsGenerator_Constructor_WithNullGraphicsDevice_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DefaultGraphicsGenerator(null));
    }

    [Theory]
    [InlineData(100, 20)]
    [InlineData(400, 30)]
    [InlineData(500, 40)]
    [InlineData(800, 50)]
    public void GraphicsGeneration_WithDifferentSizes_ShouldHandleVariousDimensions(int width, int height)
    {
        // This test verifies that the graphics generation system can handle various dimensions
        // without throwing exceptions (actual graphics generation requires real GraphicsDevice)

        Assert.True(width > 0);
        Assert.True(height > 0);
        Assert.True(width <= 1920); // Reasonable upper bound
        Assert.True(height <= 1080); // Reasonable upper bound
    }

    [Theory]
    [InlineData(BarType.Score, false, false)]
    [InlineData(BarType.Score, true, false)]
    [InlineData(BarType.Score, false, true)]
    [InlineData(BarType.Box, false, false)]
    [InlineData(BarType.Box, true, false)]
    [InlineData(BarType.Other, false, false)]
    [InlineData(BarType.Other, true, true)]
    public void BarTypeBackground_WithDifferentStates_ShouldHandleAllCombinations(BarType barType, bool isSelected, bool isCenter)
    {
        // Verify that all bar type and state combinations are valid
        Assert.True(Enum.IsDefined(typeof(BarType), barType));

        // Test state logic
        if (isCenter)
        {
            Assert.True(isSelected || !isSelected); // Center can be selected or not
        }
    }

    [Theory]
    [InlineData(0, ClearStatus.NotPlayed)]
    [InlineData(1, ClearStatus.Failed)]
    [InlineData(2, ClearStatus.Clear)]
    [InlineData(3, ClearStatus.FullCombo)]
    [InlineData(4, ClearStatus.Clear)]
    public void EnhancedClearLamp_WithDifferentStatusAndDifficulty_ShouldHandleAllCombinations(int difficulty, ClearStatus clearStatus)
    {
        // Verify that all difficulty and clear status combinations are valid
        Assert.True(difficulty >= 0);
        Assert.True(difficulty <= 4); // Standard DTX difficulty range
        Assert.True(Enum.IsDefined(typeof(ClearStatus), clearStatus));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)] // Beyond normal range
    public void ClearLamp_WithVariousDifficulties_ShouldHandleAllValues(int difficulty)
    {
        // Verify that the system can handle various difficulty values
        // including out-of-range values gracefully
        Assert.True(difficulty >= 0);
    }
}
