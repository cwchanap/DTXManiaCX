using System.Collections.Generic;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Utilities;

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
        Assert.Equal("System", GetLastPathSegment(config.SkinPath));
        Assert.Equal("DTXFiles", GetLastPathSegment(config.DTXPath));
        Assert.Equal(1280, config.ScreenWidth);
        Assert.Equal(720, config.ScreenHeight);
        Assert.False(config.FullScreen);
        Assert.True(config.VSyncWait);
        Assert.Equal(100, config.MasterVolume);
        Assert.Equal(100, config.BGMVolume);
        Assert.Equal(100, config.SEVolume);
        Assert.Equal(100, config.BufferSizeMs);
        Assert.NotNull(config.KeyBindings);
        Assert.NotNull(config.UnboundDrumButtons);
        Assert.Equal(100, config.ScrollSpeed);
        Assert.Equal(200, config.AudioLatencyOffsetMs);
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
    [InlineData("System/", "DTXFiles/")]
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

    [Theory]
    [InlineData(true, "System/", "CustomSkin")]
    [InlineData(false, "Skins/", "Default")]
    public void ConfigData_SetSkinSettings_ShouldUpdateCorrectly(bool useBoxDefSkin, string systemSkinRoot, string lastUsedSkin)
    {
        // Arrange
        var config = new ConfigData();

        // Act
        config.UseBoxDefSkin = useBoxDefSkin;
        config.SystemSkinRoot = systemSkinRoot;
        config.LastUsedSkin = lastUsedSkin;

        // Assert
        Assert.Equal(useBoxDefSkin, config.UseBoxDefSkin);
        Assert.Equal(systemSkinRoot, config.SystemSkinRoot);
        Assert.Equal(lastUsedSkin, config.LastUsedSkin);
    }

    [Fact]
    public void ConfigData_DefaultSkinSettings_ShouldBeCorrect()
    {
        // Arrange & Act
        var config = new ConfigData();

        // Assert
        Assert.True(config.UseBoxDefSkin);
        Assert.Equal("System", GetLastPathSegment(config.SystemSkinRoot));
        Assert.Equal("Default", config.LastUsedSkin);
    }

    private static string GetLastPathSegment(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, '/', '\\');
        return Path.GetFileName(trimmed);
    }

    // ---- BindingState snapshot/restore (used by both config stages' save-with-rollback) ----

    private static ConfigData ConfigWithBindingData() => new()
    {
        KeyBindings = new Dictionary<string, int> { ["Key.A"] = 0, ["Key.S"] = 4 },
        UnboundDrumLanes = new HashSet<int> { 3, 7 },
        UnboundDrumButtons = new HashSet<string> { "Key.B" },
        SystemKeyBindings = new Dictionary<string, string> { ["SystemKey.MoveUp"] = "Up" }
    };

    [Fact]
    public void SnapshotBindingState_RestoreBindingState_RoundTripsAllFourCollections()
    {
        var config = ConfigWithBindingData();
        var snapshot = config.SnapshotBindingState();

        // Mutate every collection (simulating the in-memory prepare before a failed disk write).
        config.KeyBindings.Clear();
        config.KeyBindings["Key.Z"] = 9;
        config.UnboundDrumLanes.Clear();
        config.UnboundDrumLanes.Add(1);
        config.UnboundDrumButtons.Clear();
        config.UnboundDrumButtons.Add("Key.Y");
        config.SystemKeyBindings.Clear();
        config.SystemKeyBindings["SystemKey.Back"] = "Escape";

        config.RestoreBindingState(snapshot);

        // Exact pre-mutation state restored across all four collections.
        Assert.Equal(2, config.KeyBindings.Count);
        Assert.Equal(0, config.KeyBindings["Key.A"]);
        Assert.Equal(4, config.KeyBindings["Key.S"]);
        Assert.DoesNotContain("Key.Z", config.KeyBindings.Keys);
        Assert.Equal(2, config.UnboundDrumLanes.Count);
        Assert.Contains(3, config.UnboundDrumLanes);
        Assert.Contains(7, config.UnboundDrumLanes);
        Assert.Equal(new[] { "Key.B" }, config.UnboundDrumButtons);
        Assert.Single(config.SystemKeyBindings);
        Assert.Equal("Up", config.SystemKeyBindings["SystemKey.MoveUp"]);
    }

    [Fact]
    public void SnapshotBindingState_IsAnIsolatedCopy_NotLiveWithConfig()
    {
        // A snapshot must not see later mutations — otherwise rolling back after the in-memory
        // prepare would restore the mutated state, not the pre-save state.
        var config = ConfigWithBindingData();
        var snapshot = config.SnapshotBindingState();

        config.KeyBindings["Key.A"] = 5; // mutate after snapshotting

        var restored = new ConfigData();
        restored.RestoreBindingState(snapshot);

        Assert.Equal(0, restored.KeyBindings["Key.A"]); // snapshot kept the original value
    }

    [Fact]
    public void RestoreBindingState_PreservesCollectionIdentity()
    {
        // Clear+re-add (not reference replacement) so external observers/tests that hold a
        // reference to the collection still see the restored state.
        var config = ConfigWithBindingData();
        var snapshot = config.SnapshotBindingState();
        var keyBindingsRef = config.KeyBindings;
        var unboundLanesRef = config.UnboundDrumLanes;
        var unboundButtonsRef = config.UnboundDrumButtons;
        var systemBindingsRef = config.SystemKeyBindings;

        config.RestoreBindingState(snapshot);

        Assert.Same(keyBindingsRef, config.KeyBindings);
        Assert.Same(unboundLanesRef, config.UnboundDrumLanes);
        Assert.Same(unboundButtonsRef, config.UnboundDrumButtons);
        Assert.Same(systemBindingsRef, config.SystemKeyBindings);
    }
}
