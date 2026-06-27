using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Utilities;
using Microsoft.Xna.Framework.Input;
using System.Text;

namespace DTXMania.Test.Config;

// Several tests below mutate the process-wide DTXMANIA_APPDATA_ROOT env var and read/write
// AppPaths.GetConfigFilePath(). The "AppPaths" collection disables parallelization so no other
// test class touches AppPaths while these overrides are active (prevents flaky cross-class
// config writes under xUnit class parallelization).
[Collection("AppPaths")]
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
            Assert.Contains("AudioLatencyOffsetMs=200", content);
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
    public void ConfigManager_SaveKeyBindings_UnboundLane_ShouldTrackUnboundLane()
    {
        // Arrange
        var manager = new ConfigManager();
        var keyBindings = new KeyBindings();
        keyBindings.UnbindLane(4);

        // Act
        manager.SaveKeyBindings(keyBindings);

        // Assert
        Assert.Contains(4, manager.Config.UnboundDrumLanes);
    }

    [Fact]
    public void ConfigManager_SaveAndLoadConfig_ControllerOnlyLane_ShouldPreserveKeyboardUnbind()
    {
        // Arrange
        var manager = new ConfigManager();
        var sourceBindings = new KeyBindings();
        sourceBindings.BindButton("MIDI.36", 6);
        sourceBindings.BindButton("Pad.A", 6);
        sourceBindings.UnbindKeyboardButtonsForLane(6);

        // Act
        manager.SaveKeyBindings(sourceBindings);

        // Assert
        Assert.Contains(6, manager.Config.UnboundDrumLanes);
        Assert.Equal(6, manager.Config.KeyBindings["MIDI.36"]);
        Assert.Equal(6, manager.Config.KeyBindings["Pad.A"]);
        Assert.DoesNotContain("Key.Space", manager.Config.KeyBindings.Keys);

        var tempFile = Path.GetTempFileName();
        try
        {
            manager.SaveConfig(tempFile);

            var reloadedManager = new ConfigManager();
            reloadedManager.LoadConfig(tempFile);

            Assert.Contains(6, reloadedManager.Config.UnboundDrumLanes);
            Assert.Equal(6, reloadedManager.Config.KeyBindings["MIDI.36"]);
            Assert.Equal(6, reloadedManager.Config.KeyBindings["Pad.A"]);

            var targetBindings = new KeyBindings();
            reloadedManager.LoadKeyBindings(targetBindings);

            Assert.Equal(-1, targetBindings.GetLane("Key.Space"));
            Assert.Equal(6, targetBindings.GetLane("MIDI.36"));
            Assert.Equal(6, targetBindings.GetLane("Pad.A"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigManager_SaveAndLoadConfig_RemappedDefaultKeyboardLane_ShouldPersistRemovedDefaultButton()
    {
        var manager = new ConfigManager();
        var sourceBindings = new KeyBindings();
        sourceBindings.UnbindButton("Key.Space");
        sourceBindings.BindButton("Key.B", 6);

        manager.SaveKeyBindings(sourceBindings);

        Assert.DoesNotContain(6, manager.Config.UnboundDrumLanes);
        Assert.Contains("Key.Space", manager.Config.UnboundDrumButtons);
        Assert.Equal(6, manager.Config.KeyBindings["Key.B"]);

        var tempFile = Path.GetTempFileName();
        try
        {
            manager.SaveConfig(tempFile);

            var configText = File.ReadAllText(tempFile);
            Assert.Contains("Key.UnboundButton.Key.Space=true", configText);

            var reloadedManager = new ConfigManager();
            reloadedManager.LoadConfig(tempFile);

            Assert.Contains("Key.Space", reloadedManager.Config.UnboundDrumButtons);
            Assert.DoesNotContain(6, reloadedManager.Config.UnboundDrumLanes);

            var targetBindings = new KeyBindings();
            reloadedManager.LoadKeyBindings(targetBindings);

            Assert.Equal(-1, targetBindings.GetLane("Key.Space"));
            Assert.Equal(6, targetBindings.GetLane("Key.B"));
        }
        finally
        {
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

[Game]
AudioLatencyOffsetMs=350
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
            Assert.Equal(350, manager.Config.AudioLatencyOffsetMs);
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
        // SaveKeyBindings saves ALL bindings (including defaults) so removal of defaults
        // is correctly tracked. Default Key.A is present in sourceBindings and thus saved.
        Assert.Contains("Key.A", manager.Config.KeyBindings.Keys);
        Assert.Equal(2, targetBindings.GetLane("Key.Z"));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ConfigManager_LoadConfig_CustomDTXPath_ShouldBeHonored()
    {
        // Arrange
        var manager = new ConfigManager();
        var tempFile = Path.Combine(Path.GetTempPath(), $"Test_CustomPath_{Guid.NewGuid():N}.ini");
        var customPath = Path.Combine(Path.GetTempPath(), "CustomSongs");
        var iniContent = $"[System]\nDTXPath={customPath}\n";
        File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

        try
        {
            // Act
            manager.LoadConfig(tempFile);

            // Assert - Custom path should be honored
            Assert.Equal(Path.GetFullPath(customPath), manager.Config.DTXPath);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ConfigManager_LoadConfig_EmptyDTXPath_ShouldUseDefault()
    {
        // Arrange
        var manager = new ConfigManager();
        var tempFile = Path.Combine(Path.GetTempPath(), $"Test_EmptyPath_{Guid.NewGuid():N}.ini");
        var iniContent = "[System]\nDTXPath=\n";
        File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

        try
        {
            // Act
            manager.LoadConfig(tempFile);

            // Assert - Empty path should use default DTXFiles
            var dtxPathDir = Path.GetFileName(manager.Config.DTXPath.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            Assert.Equal("DTXFiles", dtxPathDir);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("Songs")]
    [InlineData("./Songs")]
    [InlineData(".\\Songs")]
    [InlineData("Songs/")]
    [InlineData("Songs\\")]
    public void ConfigManager_LoadConfig_LegacySongsDTXPath_ShouldUseDefault(string legacyPath)
    {
        // Arrange
        var manager = new ConfigManager();
        var tempFile = Path.Combine(Path.GetTempPath(), $"Test_LegacySongsPath_{Guid.NewGuid():N}.ini");
        var iniContent = $"[System]\nDTXPath={legacyPath}\n";
        File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

        try
        {
            // Act
            manager.LoadConfig(tempFile);

            // Assert
            Assert.Equal("DTXFiles", GetLastPathSegment(manager.Config.DTXPath));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ConfigManager_LoadConfig_AbsoluteLegacySongsDTXPath_ShouldUseDefault()
    {
        // Arrange
        var manager = new ConfigManager();
        var tempFile = Path.Combine(Path.GetTempPath(), $"Test_AbsoluteLegacySongsPath_{Guid.NewGuid():N}.ini");
        var legacyAbsolutePath = Path.Combine(Path.GetDirectoryName(AppPaths.GetDefaultSongsPath())!, "Songs");
        var iniContent = $"[System]\nDTXPath={legacyAbsolutePath}\n";
        File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

        try
        {
            // Act
            manager.LoadConfig(tempFile);

            // Assert
            Assert.Equal(AppPaths.GetDefaultSongsPath(), manager.Config.DTXPath);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigManager_LoadConfig_KeyBindingLane10_ShouldBeRejected()
    {
        // Arrange - lane 9 is the max valid lane (0-indexed for 10 NX drum lanes)
        var manager = new ConfigManager();
        var tempFile = Path.Combine(Path.GetTempPath(), $"Test_Lane10_{Guid.NewGuid():N}.ini");
        var iniContent = "[System]\n[KeyBindings]\nKey.A=10\nKey.B=9\n";
        File.WriteAllText(tempFile, iniContent, Encoding.UTF8);

        try
        {
            // Act
            manager.LoadConfig(tempFile);

            // Assert - Lane 10 should be rejected, lane 9 accepted
            Assert.DoesNotContain("Key.A", manager.Config.KeyBindings.Keys);
            Assert.Contains("Key.B", manager.Config.KeyBindings.Keys);
            Assert.Equal(9, manager.Config.KeyBindings["Key.B"]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SetKeyBindings_MutatesConfig_MarksDirty_FiresEvent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempDir);
        try
        {
            // Arrange — LoadConfig captures _loadedConfigPath so MarkDirty() stages a real
            // deferred write (and seeds a default config file at that path).
            var cm = new ConfigManager();
            cm.LoadConfig(AppPaths.GetConfigFilePath());

            var raised = false;
            cm.KeyBindingsChanged += (_, _) => raised = true;

            // Act
            var kb = new KeyBindings();
            kb.BindButton("Key.X", 2);
            cm.SetKeyBindings(kb);

            // Assert — in-memory mutation + event
            Assert.Equal(2, cm.Config.KeyBindings["Key.X"]);
            Assert.True(raised);

            // Persist-on-edit: MarkDirty() must have staged a deferred write against the
            // loaded path, so FlushPendingSave lands the binding on disk. The default config
            // written by LoadConfig has no Key.X, so this only passes when the full
            // SetKeyBindings -> MarkDirty -> FlushPendingSave chain ran end-to-end.
            cm.FlushPendingSave();
            Assert.True(File.Exists(AppPaths.GetConfigFilePath()));
            Assert.Contains("Key.X=2", File.ReadAllText(AppPaths.GetConfigFilePath()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SetKeyBindings_NoEvent_WhenNoSubscriber_DoesNotThrow() =>
        Assert.Null(Record.Exception(() => new ConfigManager().SetKeyBindings(new KeyBindings())));

    [Fact]
    public void SetSystemKeyBindings_MutatesConfig_MarksDirty_FiresEvent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempDir);
        try
        {
            // Arrange — LoadConfig captures _loadedConfigPath so MarkDirty() stages a real
            // deferred write (and seeds a default config file at that path).
            var cm = new ConfigManager();
            cm.LoadConfig(AppPaths.GetConfigFilePath());

            var raised = false;
            cm.SystemKeyBindingsChanged += (_, _) => raised = true;

            // Act
            cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType> { [Keys.Z] = InputCommandType.MoveUp });

            // Assert — in-memory mutation + event
            Assert.Contains("SystemKey.MoveUp", cm.Config.SystemKeyBindings.Keys);
            Assert.Equal("Z", cm.Config.SystemKeyBindings["SystemKey.MoveUp"]);
            Assert.True(raised);

            // Persist-on-edit: MarkDirty() must have staged a deferred write against the
            // loaded path, so FlushPendingSave lands the binding on disk. SaveConfig writes
            // SystemKey.<Command>=<keys>; the default config has no SystemKey.MoveUp=Z, so
            // this only passes when the full SetSystemKeyBindings -> MarkDirty ->
            // FlushPendingSave chain ran end-to-end.
            cm.FlushPendingSave();
            Assert.True(File.Exists(AppPaths.GetConfigFilePath()));
            Assert.Contains("SystemKey.MoveUp=Z", File.ReadAllText(AppPaths.GetConfigFilePath()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SetSystemKeyBindings_NoEvent_WhenNoSubscriber_DoesNotThrow() =>
        Assert.Null(Record.Exception(() => new ConfigManager().SetSystemKeyBindings(
            new Dictionary<Keys, InputCommandType> { [Keys.Z] = InputCommandType.MoveUp })));

    [Fact]
    public void SetKeyBindings_ThrowingSubscriber_DoesNotBreakOtherSubscribersOrRollback()
    {
        // RaiseEvent wraps each subscriber in try/catch so one bad listener cannot break the
        // edit or roll back Config. Config stays the truth; the remaining subscribers still fire.
        var cm = new ConfigManager();
        var goodFired = false;
        cm.KeyBindingsChanged += (_, _) => throw new InvalidOperationException("boom");
        cm.KeyBindingsChanged += (_, _) => goodFired = true;

        var kb = new KeyBindings();
        kb.BindButton("Key.X", 3);

        var ex = Record.Exception(() => cm.SetKeyBindings(kb));

        Assert.Null(ex);                       // bad subscriber swallowed, not propagated
        Assert.True(goodFired);                // second subscriber still received the event
        Assert.Equal(3, cm.Config.KeyBindings["Key.X"]); // Config mutation survived
    }

    [Fact]
    public void SetSystemKeyBindings_ThrowingSubscriber_DoesNotBreakOtherSubscribers()
    {
        var cm = new ConfigManager();
        var goodFired = false;
        cm.SystemKeyBindingsChanged += (_, _) => throw new InvalidOperationException("boom");
        cm.SystemKeyBindingsChanged += (_, _) => goodFired = true;

        var ex = Record.Exception(() => cm.SetSystemKeyBindings(
            new Dictionary<Keys, InputCommandType> { [Keys.Z] = InputCommandType.MoveUp }));

        Assert.Null(ex);
        Assert.True(goodFired);
        Assert.Equal("Z", cm.Config.SystemKeyBindings["SystemKey.MoveUp"]);
    }

    // --- Scalar setters (Task 1.4): dirty+flush, NO events ---

    [Fact]
    public void SetAutoPlay_Mutates_AndMarksDirty()
    {
        var cm = new ConfigManager();
        cm.SetAutoPlay(true);
        Assert.True(cm.Config.AutoPlay);
    }

    [Fact]
    public void SetNoFail_Mutates_AndMarksDirty()
    {
        var cm = new ConfigManager();
        cm.SetNoFail(true);
        Assert.True(cm.Config.NoFail);
    }

    [Fact]
    public void SetAudioLatency_Mutates_AndMarksDirty()
    {
        var cm = new ConfigManager();
        cm.SetAudioLatency(350);
        Assert.Equal(350, cm.Config.AudioLatencyOffsetMs);
    }

    [Fact]
    public void SetAudioLatency_Negative_ClampsToZero()
    {
        var cm = new ConfigManager();
        cm.SetAudioLatency(-50);
        Assert.Equal(0, cm.Config.AudioLatencyOffsetMs);
    }

    [Fact]
    public void SetResolution_Mutates_AndMarksDirty()
    {
        var cm = new ConfigManager();
        cm.SetResolution(1920, 1080);
        Assert.Equal(1920, cm.Config.ScreenWidth);
        Assert.Equal(1080, cm.Config.ScreenHeight);
    }

    [Fact]
    public void SetFullscreen_Mutates_AndMarksDirty()
    {
        var cm = new ConfigManager();
        cm.SetFullscreen(true);
        Assert.True(cm.Config.FullScreen);
    }

    [Fact]
    public void SetVSync_Mutates_AndMarksDirty()
    {
        var cm = new ConfigManager();
        cm.SetVSync(false);
        Assert.False(cm.Config.VSyncWait);
    }

    [Fact]
    public void ScalarSetters_DoNotFireEvents()
    {
        var cm = new ConfigManager();
        var fired = false;
        cm.ScrollSpeedChanged += (_, _) => fired = true;
        cm.KeyBindingsChanged += (_, _) => fired = true;
        cm.SystemKeyBindingsChanged += (_, _) => fired = true;

        // Sanity: prove the wiring can detect a real fire. Without this, a globally
        // broken event bus would let the scalar assertions below pass vacuously.
        cm.SetKeyBindings(new KeyBindings());
        Assert.True(fired, "sanity: a firing setter must trip the flag");
        fired = false;

        // Now assert scalar setters do NOT fire.
        cm.SetAutoPlay(true);
        cm.SetNoFail(true);
        cm.SetAudioLatency(100);
        cm.SetResolution(1920, 1080);
        cm.SetFullscreen(true);
        cm.SetVSync(true);

        Assert.False(fired);
    }

    /// <summary>
    /// Each scalar setter must independently mark dirty so a subsequent
    /// FlushPendingSave lands its edit on disk. Because FlushPendingSave clears
    /// the pending path, the test interleaves setter -> flush -> assert per
    /// setter: dropping MarkDirty from any one setter leaves the file holding
    /// the previous (default) value and fails that assertion.
    /// </summary>
    [Fact]
    public void FlushPendingSave_AfterScalarEdits_WritesFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var prev = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", dir);
        try
        {
            var cm = new ConfigManager();
            cm.LoadConfig(AppPaths.GetConfigFilePath());

            cm.SetNoFail(true);
            cm.FlushPendingSave();
            Assert.Contains("NoFail=True", File.ReadAllText(AppPaths.GetConfigFilePath()));

            cm.SetAutoPlay(true);
            cm.FlushPendingSave();
            Assert.Contains("AutoPlay=True", File.ReadAllText(AppPaths.GetConfigFilePath()));

            cm.SetAudioLatency(350);
            cm.FlushPendingSave();
            Assert.Contains("AudioLatencyOffsetMs=350", File.ReadAllText(AppPaths.GetConfigFilePath()));

            cm.SetResolution(1920, 1080);
            cm.FlushPendingSave();
            var contents = File.ReadAllText(AppPaths.GetConfigFilePath());
            Assert.Contains("ScreenWidth=1920", contents);
            Assert.Contains("ScreenHeight=1080", contents);

            cm.SetFullscreen(true);
            cm.FlushPendingSave();
            Assert.Contains("FullScreen=True", File.ReadAllText(AppPaths.GetConfigFilePath()));

            // VSyncWait defaults to True, so flip to False to prove the edit landed.
            cm.SetVSync(false);
            cm.FlushPendingSave();
            Assert.Contains("VSyncWait=False", File.ReadAllText(AppPaths.GetConfigFilePath()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", prev);
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Pins FlushPendingSave's retry-on-failure contract: when a flush fails (exception
    /// caught internally), _pendingSavePath is KEPT so the next flush retries the same
    /// path. The scalar setters (e.g. SetNoFail) mark dirty without taking a path, so
    /// _pendingSavePath flows from _loadedConfigPath. This test drives that chain through
    /// LoadConfig -> SetNoFail, then toggles the filesystem so the first flush fails and
    /// the second succeeds against the SAME path.
    ///
    /// Mechanism: GetConfigFilePath() resolves to "&lt;root&gt;/Config.ini", so
    /// EnsureConfigDirectory calls Directory.CreateDirectory("&lt;root&gt;"). Deleting the
    /// &lt;root&gt; directory and replacing it with a FILE at the same name makes that
    /// CreateDirectory throw IOException (a file already exists with that name). Removing
    /// the file makes the retry succeed. _pendingSavePath never changes.
    /// </summary>
    [Fact]
    public void FlushPendingSave_RetriesAfterFailure_WhenFilesystemBecomesWritable()
    {
        var root = Path.Combine(Path.GetTempPath(), "dtxmania-flush-retry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var prev = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", root);

        // The pending path is captured once; the filesystem at <root> is what changes.
        var configFilePath = AppPaths.GetConfigFilePath();

        try
        {
            var cm = new ConfigManager();
            cm.LoadConfig(configFilePath);

            // Sanity: LoadConfig wrote the default config and remembers the path.
            Assert.True(File.Exists(configFilePath));
            Assert.Contains("NoFail=False", File.ReadAllText(configFilePath));

            // Scalar setter marks dirty via _loadedConfigPath -> _pendingSavePath.
            cm.SetNoFail(true);
            Assert.True(cm.Config.NoFail);

            // Break the filesystem at <root>: replace the directory with a regular file
            // so Directory.CreateDirectory("<root>") throws on the next SaveConfig.
            Directory.Delete(root, recursive: true);
            File.WriteAllText(root, "blocker");

            // First flush: SaveConfig -> EnsureConfigDirectory throws, exception is
            // caught internally, _pendingSavePath is retained. Must NOT throw.
            cm.FlushPendingSave();

            // In-memory value survives the failed flush.
            Assert.True(cm.Config.NoFail);

            // The edit must NOT have landed yet — proves the flush genuinely failed.
            Assert.False(File.Exists(configFilePath));

            // Fix the filesystem at <root>: remove the blocking file so the retry can
            // recreate the directory and write the file at the SAME path.
            File.Delete(root);
            Directory.CreateDirectory(root);

            // Second flush: retries the retained _pendingSavePath and succeeds.
            cm.FlushPendingSave();

            // The edit now persists on retry.
            Assert.True(File.Exists(configFilePath));
            Assert.Contains("NoFail=True", File.ReadAllText(configFilePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", prev);
            // <root> may be a file or a directory depending on where the test landed;
            // clean up both possibilities.
            if (File.Exists(root))
                File.Delete(root);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConfigManager_SaveAndLoadConfig_MidiVelocityThresholds_ShouldPreserveNonzeroThresholds()
    {
        var manager = new ConfigManager();
        manager.SetMidiVelocityThreshold(36, 20);
        manager.SetMidiVelocityThreshold(38, 12);

        var tempFile = Path.GetTempFileName();
        try
        {
            manager.SaveConfig(tempFile);
            var text = File.ReadAllText(tempFile);
            Assert.Contains("[MidiVelocityThresholds]", text);
            Assert.Contains("MidiVelocity.36=20", text);
            Assert.Contains("MidiVelocity.38=12", text);

            var reloaded = new ConfigManager();
            reloaded.LoadConfig(tempFile);

            Assert.Equal(20, reloaded.GetMidiVelocityThreshold(36));
            Assert.Equal(12, reloaded.GetMidiVelocityThreshold(38));
            Assert.Equal(0, reloaded.GetMidiVelocityThreshold(40));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigManager_SetMidiVelocityThreshold_Zero_ShouldRemovePersistedThreshold()
    {
        var manager = new ConfigManager();
        manager.SetMidiVelocityThreshold(36, 20);
        manager.SetMidiVelocityThreshold(36, 0);

        Assert.Equal(0, manager.GetMidiVelocityThreshold(36));
        Assert.False(manager.Config.MidiVelocityThresholds.ContainsKey(36));

        var tempFile = Path.GetTempFileName();
        try
        {
            manager.SaveConfig(tempFile);
            var text = File.ReadAllText(tempFile);
            Assert.DoesNotContain("MidiVelocity.36=", text);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigManager_LoadConfig_InvalidMidiVelocityThresholds_ShouldIgnoreOrClamp()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, string.Join(Environment.NewLine, new[]
        {
            "[MidiVelocityThresholds]",
            "MidiVelocity.36=300",
            "MidiVelocity.38=-4",
            "MidiVelocity.200=50",
            "MidiVelocity.bad=40",
            "MidiVelocity.40=abc"
        }));

        try
        {
            var manager = new ConfigManager();
            manager.LoadConfig(tempFile);

            Assert.Equal(127, manager.GetMidiVelocityThreshold(36));
            Assert.Equal(0, manager.GetMidiVelocityThreshold(38));
            Assert.Equal(0, manager.GetMidiVelocityThreshold(200));
            Assert.Equal(0, manager.GetMidiVelocityThreshold(40));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
