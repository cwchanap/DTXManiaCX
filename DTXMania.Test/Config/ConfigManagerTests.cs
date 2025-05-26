using DTX.Config;
using System.Text;

namespace DTXMania.Test.Config;

public class ConfigManagerTests
{
    [Fact]
    public void ConfigManager_Constructor_ShouldInitializeWithDefaultConfig()
    {
        // Arrange & Act
        var manager = new ConfigManager();

        // Assert
        Assert.NotNull(manager.Config);
        Assert.Equal("NX1.5.0-MG", manager.Config.DTXManiaVersion);
        Assert.Equal(1280, manager.Config.ScreenWidth);
        Assert.Equal(720, manager.Config.ScreenHeight);
    }

    [Fact]
    public void ConfigManager_ResetToDefaults_ShouldCreateNewDefaultConfig()
    {
        // Arrange
        var manager = new ConfigManager();
        manager.Config.ScreenWidth = 1920;
        manager.Config.ScreenHeight = 1080;

        // Act
        manager.ResetToDefaults();

        // Assert
        Assert.Equal(1280, manager.Config.ScreenWidth);
        Assert.Equal(720, manager.Config.ScreenHeight);
    }

    [Fact]
    public void ConfigManager_LoadConfig_NonExistentFile_ShouldNotThrow()
    {
        // Arrange
        var manager = new ConfigManager();
        var nonExistentFile = "NonExistent_Config.ini";

        // Act & Assert (should not throw)
        manager.LoadConfig(nonExistentFile);
    }

    [Fact]
    public void ConfigManager_SaveConfig_ShouldCreateValidIniFile()
    {
        // Arrange
        var manager = new ConfigManager();
        var tempFile = Path.GetTempFileName();

        try
        {
            manager.Config.ScreenWidth = 1920;
            manager.Config.ScreenHeight = 1080;
            manager.Config.FullScreen = true;
            manager.Config.VSyncWait = false;

            // Act
            manager.SaveConfig(tempFile);

            // Assert
            Assert.True(File.Exists(tempFile));
            var content = File.ReadAllText(tempFile);
            Assert.Contains("ScreenWidth=1920", content);
            Assert.Contains("ScreenHeight=1080", content);
            Assert.Contains("FullScreen=True", content);
            Assert.Contains("VSyncWait=False", content);
            Assert.Contains("[System]", content);
            Assert.Contains("[Display]", content);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigManager_LoadConfig_ValidIniContent_ShouldParseCorrectly()
    {
        // Arrange
        var manager = new ConfigManager();
        var tempFile = Path.GetTempFileName();

        var iniContent = @"; Test Config File
[System]
DTXManiaVersion=TestVersion
SkinPath=TestSkin/
DTXPath=TestDTX/

[Display]
ScreenWidth=1920
ScreenHeight=1080
FullScreen=true
VSyncWait=false
";

        try
        {
            File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

            // Act
            manager.LoadConfig(tempFile);

            // Assert
            Assert.Equal("TestVersion", manager.Config.DTXManiaVersion);
            Assert.Equal("TestSkin/", manager.Config.SkinPath);
            Assert.Equal("TestDTX/", manager.Config.DTXPath);
            Assert.Equal(1920, manager.Config.ScreenWidth);
            Assert.Equal(1080, manager.Config.ScreenHeight);
            Assert.True(manager.Config.FullScreen);
            Assert.False(manager.Config.VSyncWait);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("ScreenWidth=800", 800)]
    [InlineData("ScreenWidth=1366", 1366)]
    [InlineData("ScreenWidth=invalid", 1280)] // Should keep default on invalid
    public void ConfigManager_ParseScreenWidth_ShouldHandleVariousInputs(string line, int expectedWidth)
    {
        // Arrange
        var manager = new ConfigManager();
        var tempFile = Path.GetTempFileName();

        var iniContent = $@"[Display]
{line}
ScreenHeight=720
";

        try
        {
            File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

            // Act
            manager.LoadConfig(tempFile);

            // Assert
            Assert.Equal(expectedWidth, manager.Config.ScreenWidth);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("FullScreen=true", true)]
    [InlineData("FullScreen=True", true)]
    [InlineData("FullScreen=false", false)]
    [InlineData("FullScreen=False", false)]
    [InlineData("FullScreen=invalid", false)] // Should default to false on invalid
    public void ConfigManager_ParseFullScreen_ShouldHandleVariousInputs(string line, bool expectedFullScreen)
    {
        // Arrange
        var manager = new ConfigManager();
        var tempFile = Path.GetTempFileName();

        var iniContent = $@"[Display]
{line}
";

        try
        {
            File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

            // Act
            manager.LoadConfig(tempFile);

            // Assert
            Assert.Equal(expectedFullScreen, manager.Config.FullScreen);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("VSyncWait=true", true)]
    [InlineData("VSyncWait=false", false)]
    [InlineData("VSyncWait=invalid", false)] // Should default to false on invalid (ToLower() != "true")
    public void ConfigManager_ParseVSyncWait_ShouldHandleVariousInputs(string line, bool expectedVSync)
    {
        // Arrange
        var manager = new ConfigManager();
        var tempFile = Path.GetTempFileName();

        var iniContent = $@"[Display]
{line}
";

        try
        {
            File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

            // Act
            manager.LoadConfig(tempFile);

            // Assert
            Assert.Equal(expectedVSync, manager.Config.VSyncWait);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
