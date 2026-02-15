using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Utilities;
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
        var tempDir = Path.GetTempPath();
        var nonExistentFile = Path.Combine(tempDir, "NonExistent_Config.ini");

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
            Assert.Equal("TestSkin", GetLastPathSegment(manager.Config.SkinPath));
            Assert.Equal("TestDTX", GetLastPathSegment(manager.Config.DTXPath));
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

    private static string GetLastPathSegment(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, '/', '\\');
        return Path.GetFileName(trimmed);
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

    [Theory]
    [InlineData("NoFail=true", true)]
    [InlineData("NoFail=True", true)]
    [InlineData("NoFail=false", false)]
    [InlineData("NoFail=False", false)]
    [InlineData("NoFail=invalid", false)] // Should default to false for invalid input
    public void ConfigManager_ParseNoFail_ShouldHandleVariousInputs(string line, bool expectedNoFail)
    {
        // Arrange
        var manager = new ConfigManager();
        var tempDir = Path.GetTempPath();
        var tempFile = Path.Combine(tempDir, $"Test_NoFail_{Guid.NewGuid()}.ini");

        var iniContent = $@"[Game]
{line}
";

        try
        {
            File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

            // Act
            manager.LoadConfig(tempFile);

            // Assert
            Assert.Equal(expectedNoFail, manager.Config.NoFail);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigManager_SaveConfig_ShouldIncludeNoFailSetting()
    {
        // Arrange
        var manager = new ConfigManager();
        manager.Config.NoFail = true;
        var tempDir = Path.GetTempPath();
        var tempFile = Path.Combine(tempDir, $"Test_NoFail_Save_{Guid.NewGuid()}.ini");

        try
        {
            // Act
            manager.SaveConfig(tempFile);

            // Assert
            var content = File.ReadAllText(tempFile, Encoding.UTF8);
            Assert.Contains("NoFail=True", content);
            Assert.Contains("[Game]", content);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("AutoPlay=true", true)]
    [InlineData("AutoPlay=True", true)]
    [InlineData("AutoPlay=false", false)]
    [InlineData("AutoPlay=False", false)]
    [InlineData("AutoPlay=invalid", false)] // Should default to false for invalid input
    public void ConfigManager_ParseAutoPlay_ShouldHandleVariousInputs(string line, bool expectedAutoPlay)
    {
        // Arrange
        var manager = new ConfigManager();
        var tempDir = Path.GetTempPath();
        var tempFile = Path.Combine(tempDir, $"Test_AutoPlay_{Guid.NewGuid()}.ini");

        var iniContent = $@"[Game]
{line}
";

        try
        {
            // Act
            File.WriteAllText(tempFile, iniContent, Encoding.UTF8);
            manager.LoadConfig(tempFile);

            // Assert
            Assert.Equal(expectedAutoPlay, manager.Config.AutoPlay);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigManager_SaveConfig_ShouldIncludeAutoPlaySetting()
    {
        // Arrange
        var manager = new ConfigManager();
        manager.Config.AutoPlay = true;
        var tempDir = Path.GetTempPath();
        var tempFile = Path.Combine(tempDir, $"Test_AutoPlay_Save_{Guid.NewGuid()}.ini");

        try
        {
            // Act
            manager.SaveConfig(tempFile);

            // Assert
            var content = File.ReadAllText(tempFile, Encoding.UTF8);
            Assert.Contains("AutoPlay=True", content);
            Assert.Contains("[Game]", content);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigManager_LoadConfig_EnableGameApiWithoutKey_ShouldGenerateAndPersistKey()
    {
        // Arrange
        var manager = new ConfigManager();
        var tempFile = Path.Combine(Path.GetTempPath(), $"Test_ApiKey_{Guid.NewGuid():N}.ini");
        var iniContent = @"[Api]
EnableGameApi=true
GameApiPort=5070
";

        try
        {
            File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

            // Act
            manager.LoadConfig(tempFile);

            // Assert
            Assert.True(manager.Config.EnableGameApi);
            Assert.False(string.IsNullOrWhiteSpace(manager.Config.GameApiKey));
            Assert.Equal(32, manager.Config.GameApiKey.Length);
            Assert.All(manager.Config.GameApiKey, c => Assert.True(char.IsDigit(c) || (c >= 'a' && c <= 'f')));

            var savedContent = File.ReadAllText(tempFile, Encoding.UTF8);
            Assert.Contains($"GameApiKey={manager.Config.GameApiKey}", savedContent);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigManager_LoadConfig_ShouldParseValidKeyBindingsOnly()
    {
        // Arrange
        var manager = new ConfigManager();
        var tempFile = Path.Combine(Path.GetTempPath(), $"Test_KeyBindings_{Guid.NewGuid():N}.ini");
        var iniContent = @"[KeyBindings]
Key.A=4
Key.B=9
Key.InvalidLane=12
Key.Bad=abc
";

        try
        {
            File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

            // Act
            manager.LoadConfig(tempFile);

            // Assert - lanes 0-9 are valid (matching KeyBindings.BindButton contract)
            Assert.Equal(2, manager.Config.KeyBindings.Count);
            Assert.Equal(4, manager.Config.KeyBindings["Key.A"]);
            Assert.Equal(9, manager.Config.KeyBindings["Key.B"]);
            Assert.DoesNotContain("Key.InvalidLane", manager.Config.KeyBindings.Keys);
            Assert.DoesNotContain("Key.Bad", manager.Config.KeyBindings.Keys);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigManager_SaveAndLoadKeyBindings_ShouldRoundTripCustomBinding()
    {
        // Arrange
        var manager = new ConfigManager();
        var sourceBindings = new KeyBindings();
        sourceBindings.BindButton("Key.Z", 2); // non-default binding

        // Act
        manager.SaveKeyBindings(sourceBindings);

        var targetBindings = new KeyBindings();
        manager.LoadKeyBindings(targetBindings);

        // Assert
        Assert.Contains("Key.Z", manager.Config.KeyBindings.Keys);
        Assert.Equal(2, manager.Config.KeyBindings["Key.Z"]);
        Assert.DoesNotContain("Key.A", manager.Config.KeyBindings.Keys); // default binding should not be persisted
        Assert.Equal(2, targetBindings.GetLane("Key.Z"));
    }
}
