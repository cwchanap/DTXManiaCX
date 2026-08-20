using System.Collections.Generic;
using System.IO;
using System.Text;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.TestData;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Input;
using Moq;
using Xunit;

namespace DTXMania.Test.Config;

// HPA-190: ConfigManager persists to a SQLite config database; the legacy
// Config.ini is import-only input. Each test owns a unique temp directory
// (config.db + Config.ini pair) via the internal ConfigManager test seam, and
// the class sandboxes DTXMANIA_APPDATA_ROOT so LoadConfig normalization never
// creates directories in the real user app-data. The "AppPaths" collection
// disables parallelization so no other test class touches AppPaths while the
// sandbox override is active.
// [Trait("Category", "Unit")] is applied at class level so every method (including the MIDI
// velocity-threshold cases) participates in category-filtered runs, matching the convention used
// across the other DTXMania.Test suites.
[Collection("AppPaths")]
[Trait("Category", "Unit")]
public class ConfigManagerTests : IDisposable
{
    private readonly string _sandbox;
    private readonly string? _previousAppDataRoot;

    public ConfigManagerTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "dtxmania-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _previousAppDataRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", _sandbox);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", _previousAppDataRoot);
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }

    /// <summary>Unique per-test directory holding a config.db + Config.ini pair.</summary>
    private string NewTestDir()
    {
        var dir = Path.Combine(_sandbox, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ConfigManager CreateManager(string dir) =>
        new(Path.Combine(dir, "config.db"), Path.Combine(dir, "Config.ini"));

    private static IReadOnlyDictionary<string, string> ReadRows(string dir) =>
        new SqliteConfigStore(Path.Combine(dir, "config.db")).Load();

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
    public void ConfigManager_LoadConfig_WithNoDatabase_ShouldCreateDefaultDatabase()
    {
        // Arrange
        var dir = NewTestDir();
        var manager = CreateManager(dir);

        // Act (should not throw)
        manager.LoadConfig();

        // Assert — first launch creates the database with default values.
        Assert.True(File.Exists(Path.Combine(dir, "config.db")));
        Assert.Equal("1280", ReadRows(dir)["ScreenWidth"]);
        Assert.Equal("720", ReadRows(dir)["ScreenHeight"]);
    }

    [Fact]
    public void ConfigManager_FlushPendingSave_ShouldPersistScalarSnapshot()
    {
        // Arrange
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();

        // Act — scalar setters mark the deferred save; flush persists them.
        manager.SetResolution(1920, 1080);
        manager.SetFullscreen(true);
        manager.SetVSync(false);
        manager.FlushPendingSave();

        // Assert
        var rows = ReadRows(dir);
        Assert.Equal("1920", rows["ScreenWidth"]);
        Assert.Equal("1080", rows["ScreenHeight"]);
        Assert.Equal("True", rows["FullScreen"]);
        Assert.Equal("False", rows["VSyncWait"]);
        Assert.Equal("200", rows["AudioLatencyOffsetMs"]);
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
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();
        var sourceBindings = new KeyBindings();
        sourceBindings.BindButton("MIDI.36", 6);
        sourceBindings.BindButton("Pad.A", 6);
        sourceBindings.UnbindKeyboardButtonsForLane(6);

        // Act
        manager.SetKeyBindings(sourceBindings);
        manager.FlushPendingSave();

        // Assert
        Assert.Contains(6, manager.Config.UnboundDrumLanes);
        Assert.Equal(6, manager.Config.KeyBindings["MIDI.36"]);
        Assert.Equal(6, manager.Config.KeyBindings["Pad.A"]);
        Assert.DoesNotContain("Key.Space", manager.Config.KeyBindings.Keys);

        var reloadedManager = CreateManager(dir);
        reloadedManager.LoadConfig();

        Assert.Contains(6, reloadedManager.Config.UnboundDrumLanes);
        Assert.Equal(6, reloadedManager.Config.KeyBindings["MIDI.36"]);
        Assert.Equal(6, reloadedManager.Config.KeyBindings["Pad.A"]);

        var targetBindings = new KeyBindings();
        reloadedManager.LoadKeyBindings(targetBindings);

        Assert.Equal(-1, targetBindings.GetLane("Key.Space"));
        Assert.Equal(6, targetBindings.GetLane("MIDI.36"));
        Assert.Equal(6, targetBindings.GetLane("Pad.A"));
    }

    [Fact]
    public void ConfigManager_SaveAndLoadConfig_RemappedDefaultKeyboardLane_ShouldPersistRemovedDefaultButton()
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();
        var sourceBindings = new KeyBindings();
        sourceBindings.UnbindButton("Key.Space");
        sourceBindings.BindButton("Key.B", 6);

        manager.SetKeyBindings(sourceBindings);

        Assert.DoesNotContain(6, manager.Config.UnboundDrumLanes);
        Assert.Contains("Key.Space", manager.Config.UnboundDrumButtons);
        Assert.Equal(6, manager.Config.KeyBindings["Key.B"]);

        manager.FlushPendingSave();

        var rows = ReadRows(dir);
        Assert.Equal("true", rows["Key.UnboundButton.Key.Space"]);

        var reloadedManager = CreateManager(dir);
        reloadedManager.LoadConfig();

        Assert.Contains("Key.Space", reloadedManager.Config.UnboundDrumButtons);
        Assert.DoesNotContain(6, reloadedManager.Config.UnboundDrumLanes);

        var targetBindings = new KeyBindings();
        reloadedManager.LoadKeyBindings(targetBindings);

        Assert.Equal(-1, targetBindings.GetLane("Key.Space"));
        Assert.Equal(6, targetBindings.GetLane("Key.B"));
    }

    [Fact]
    public void ConfigManager_LoadConfig_ValidIniContent_ShouldParseCorrectly()
    {
        // Arrange — a legacy INI imports on first launch.
        var dir = NewTestDir();
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
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        // Act
        var manager = CreateManager(dir);
        manager.LoadConfig();

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
        var dir = NewTestDir();
        var manager = CreateManager(dir);

        var iniContent = $@"[Display]
{line}
ScreenHeight=720
";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        manager.LoadConfig();

        Assert.Equal(expectedWidth, manager.Config.ScreenWidth);
    }

    [Theory]
    [InlineData("FullScreen=true", true)]
    [InlineData("FullScreen=True", true)]
    [InlineData("FullScreen=1", true)]
    [InlineData("FullScreen=on", true)]
    [InlineData("FullScreen=false", false)]
    [InlineData("FullScreen=False", false)]
    [InlineData("FullScreen=invalid", false)] // Should default to false on invalid
    public void ConfigManager_ParseFullScreen_ShouldHandleVariousInputs(string line, bool expectedFullScreen)
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);

        var iniContent = $@"[Display]
{line}
";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        manager.LoadConfig();

        Assert.Equal(expectedFullScreen, manager.Config.FullScreen);
    }

    [Theory]
    [InlineData("VSyncWait=true", true)]
    [InlineData("VSyncWait=false", false)]
    [InlineData("VSyncWait=invalid", true)] // Invalid keeps the default (true); only recognized truthy/falsey values assign
    public void ConfigManager_ParseVSyncWait_ShouldHandleVariousInputs(string line, bool expectedVSync)
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);

        var iniContent = $@"[Display]
{line}
";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        manager.LoadConfig();

        Assert.Equal(expectedVSync, manager.Config.VSyncWait);
    }

    [Theory]
    [InlineData("NoFail=true", true)]
    [InlineData("NoFail=True", true)]
    [InlineData("NoFail=false", false)]
    [InlineData("NoFail=False", false)]
    [InlineData("NoFail=invalid", false)] // Should default to false for invalid input
    public void ConfigManager_ParseNoFail_ShouldHandleVariousInputs(string line, bool expectedNoFail)
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);

        var iniContent = $@"[Game]
{line}
";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        manager.LoadConfig();

        Assert.Equal(expectedNoFail, manager.Config.NoFail);
    }

    [Fact]
    public void ConfigManager_FlushPendingSave_ShouldIncludeNoFailSetting()
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();
        manager.SetNoFail(true);

        manager.FlushPendingSave();

        Assert.Equal("True", ReadRows(dir)["NoFail"]);
    }

    [Theory]
    [InlineData("AutoPlay=true", true)]
    [InlineData("AutoPlay=True", true)]
    [InlineData("AutoPlay=false", false)]
    [InlineData("AutoPlay=False", false)]
    [InlineData("AutoPlay=invalid", false)] // Should default to false for invalid input
    public void ConfigManager_ParseAutoPlay_ShouldHandleVariousInputs(string line, bool expectedAutoPlay)
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);

        var iniContent = $@"[Game]
{line}
";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        manager.LoadConfig();

        Assert.Equal(expectedAutoPlay, manager.Config.AutoPlay);
    }

    [Fact]
    public void ConfigManager_FlushPendingSave_ShouldIncludeAutoPlaySetting()
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();
        manager.SetAutoPlay(true);

        manager.FlushPendingSave();

        Assert.Equal("True", ReadRows(dir)["AutoPlay"]);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("on")]
    public void ConfigManager_ParseMetronome_ShouldAcceptTruthyValues(string value)
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);

        File.WriteAllText(Path.Combine(dir, "Config.ini"), $"[Game]\nMetronome={value}\n", Encoding.UTF8);
        manager.LoadConfig();

        Assert.True(manager.Config.Metronome);
    }

    [Fact]
    public void ConfigManager_FlushPendingSave_ShouldWriteSingleMetronomeRow()
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();
        manager.SetMetronome(true);

        manager.FlushPendingSave();

        // Key/value rows are structurally unique — exactly one Metronome entry.
        Assert.Equal("True", ReadRows(dir)["Metronome"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigManager_SaveAndLoadConfig_ShouldPreserveMetronome(bool value)
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();
        manager.SetMetronome(value);
        manager.FlushPendingSave();

        var reloadedManager = CreateManager(dir);
        reloadedManager.LoadConfig();

        Assert.Equal(value, reloadedManager.Config.Metronome);
    }

    [Fact]
    public void SetMetronome_ShouldMutateAndUseDeferredSaveOnlyWhenChanged()
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();

        manager.SetMetronome(true);

        Assert.True(manager.Config.Metronome);
        Assert.Equal("False", ReadRows(dir)["Metronome"]); // deferred write not landed yet

        manager.FlushPendingSave();
        Assert.Equal("True", ReadRows(dir)["Metronome"]);

        // Unchanged setter is a no-op: nothing becomes pending again, so a
        // subsequent flush cannot resurrect a stale value.
        manager.SetMetronome(true);
        manager.FlushPendingSave();

        Assert.Equal("True", ReadRows(dir)["Metronome"]);
    }

    [Fact]
    public void ConfigManager_LoadConfig_EnableGameApiWithoutKey_ShouldGenerateAndPersistKey()
    {
        // Arrange — legacy INI import with Game API enabled but no key.
        var dir = NewTestDir();
        var iniContent = @"[Api]
EnableGameApi=true
GameApiPort=5070
";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        // Act
        var manager = CreateManager(dir);
        manager.LoadConfig();

        // Assert
        Assert.True(manager.Config.EnableGameApi);
        Assert.False(string.IsNullOrWhiteSpace(manager.Config.GameApiKey));
        Assert.Equal(32, manager.Config.GameApiKey.Length);
        Assert.All(manager.Config.GameApiKey, c => Assert.True(char.IsDigit(c) || (c >= 'a' && c <= 'f')));

        Assert.Equal(manager.Config.GameApiKey, ReadRows(dir)["GameApiKey"]);
    }

    [Fact]
    public void ConfigManager_LoadConfig_ShouldParseValidKeyBindingsOnly()
    {
        // Arrange
        var dir = NewTestDir();
        var iniContent = @"[KeyBindings]
Key.A=4
Key.B=9
Key.InvalidLane=12
Key.Bad=abc
";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        // Act
        var manager = CreateManager(dir);
        manager.LoadConfig();

        // Assert - lanes 0-9 are valid (matching KeyBindings.BindButton contract)
        Assert.Equal(2, manager.Config.KeyBindings.Count);
        Assert.Equal(4, manager.Config.KeyBindings["Key.A"]);
        Assert.Equal(9, manager.Config.KeyBindings["Key.B"]);
        Assert.DoesNotContain("Key.InvalidLane", manager.Config.KeyBindings.Keys);
        Assert.DoesNotContain("Key.Bad", manager.Config.KeyBindings.Keys);
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
        var dir = NewTestDir();
        var customPath = Path.Combine(dir, "CustomSongs");
        var iniContent = $"[System]\nDTXPath={customPath}\n";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        // Act
        var manager = CreateManager(dir);
        manager.LoadConfig();

        // Assert - Custom path should be honored
        Assert.Equal(Path.GetFullPath(customPath), manager.Config.DTXPath);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ConfigManager_LoadConfig_EmptyDTXPath_ShouldUseDefault()
    {
        // Arrange
        var dir = NewTestDir();
        var iniContent = "[System]\nDTXPath=\n";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        // Act
        var manager = CreateManager(dir);
        manager.LoadConfig();

        // Assert - Empty path should use default DTXFiles
        var dtxPathDir = Path.GetFileName(manager.Config.DTXPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Assert.Equal("DTXFiles", dtxPathDir);
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
        var dir = NewTestDir();
        var iniContent = $"[System]\nDTXPath={legacyPath}\n";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        // Act
        var manager = CreateManager(dir);
        manager.LoadConfig();

        // Assert
        Assert.Equal("DTXFiles", GetLastPathSegment(manager.Config.DTXPath));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ConfigManager_LoadConfig_AbsoluteLegacySongsDTXPath_ShouldUseDefault()
    {
        // Arrange
        var dir = NewTestDir();
        var legacyAbsolutePath = Path.Combine(Path.GetDirectoryName(AppPaths.GetDefaultSongsPath())!, "Songs");
        var iniContent = $"[System]\nDTXPath={legacyAbsolutePath}\n";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        // Act
        var manager = CreateManager(dir);
        manager.LoadConfig();

        // Assert
        Assert.Equal(AppPaths.GetDefaultSongsPath(), manager.Config.DTXPath);
    }

    [Fact]
    public void ConfigManager_LoadConfig_KeyBindingLane10_ShouldBeRejected()
    {
        // Arrange - lane 9 is the max valid lane (0-indexed for 10 NX drum lanes)
        var dir = NewTestDir();
        var iniContent = "[System]\n[KeyBindings]\nKey.A=10\nKey.B=9\n";
        File.WriteAllText(Path.Combine(dir, "Config.ini"), iniContent, Encoding.UTF8);

        // Act
        var manager = CreateManager(dir);
        manager.LoadConfig();

        // Assert - Lane 10 should be rejected, lane 9 accepted
        Assert.DoesNotContain("Key.A", manager.Config.KeyBindings.Keys);
        Assert.Contains("Key.B", manager.Config.KeyBindings.Keys);
        Assert.Equal(9, manager.Config.KeyBindings["Key.B"]);
    }

    [Fact]
    public void SetKeyBindings_MutatesConfig_MarksDirty_FiresEvent()
    {
        var dir = NewTestDir();
        // Arrange — LoadConfig establishes the store so MarkDirty stages a real
        // deferred write (and seeds the default database).
        var cm = CreateManager(dir);
        cm.LoadConfig();

        var raised = false;
        cm.KeyBindingsChanged += (_, _) => raised = true;

        // Act
        var kb = new KeyBindings();
        kb.BindButton("Key.X", 2);
        cm.SetKeyBindings(kb);

        // Assert — in-memory mutation + event
        Assert.Equal(2, cm.Config.KeyBindings["Key.X"]);
        Assert.True(raised);

        // Persist-on-edit: MarkDirty must have staged a deferred write, so
        // FlushPendingSave lands the binding in the database. The default
        // snapshot has no Key.X, so this only passes when the full
        // SetKeyBindings -> MarkDirty -> FlushPendingSave chain ran end-to-end.
        cm.FlushPendingSave();
        Assert.Equal("2", ReadRows(dir)["Key.X"]);
    }

    [Fact]
    public void SetKeyBindings_NoEvent_WhenNoSubscriber_DoesNotThrow() =>
        Assert.Null(Record.Exception(() => new ConfigManager().SetKeyBindings(new KeyBindings())));

    [Fact]
    public void SetSystemKeyBindings_MutatesConfig_MarksDirty_FiresEvent()
    {
        var dir = NewTestDir();
        // Arrange — LoadConfig establishes the store so MarkDirty stages a real
        // deferred write (and seeds the default database).
        var cm = CreateManager(dir);
        cm.LoadConfig();

        var raised = false;
        cm.SystemKeyBindingsChanged += (_, _) => raised = true;

        // Act
        cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType> { [Keys.Z] = InputCommandType.MoveUp });

        // Assert — in-memory mutation + event
        Assert.Contains("SystemKey.MoveUp", cm.Config.SystemKeyBindings.Keys);
        Assert.Equal("Z", cm.Config.SystemKeyBindings["SystemKey.MoveUp"]);
        Assert.True(raised);

        // Persist-on-edit: MarkDirty must have staged a deferred write, so
        // FlushPendingSave lands the binding in the database. The default
        // snapshot has no SystemKey.MoveUp=Z, so this only passes when the full
        // SetSystemKeyBindings -> MarkDirty -> FlushPendingSave chain ran
        // end-to-end.
        cm.FlushPendingSave();
        Assert.Equal("Z", ReadRows(dir)["SystemKey.MoveUp"]);
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
    /// FlushPendingSave lands its edit in the database. Because a successful
    /// flush clears the pending marker, the test interleaves setter -> flush ->
    /// assert per setter: dropping MarkDirty from any one setter leaves the
    /// database holding the previous (default) value and fails that assertion.
    /// </summary>
    [Fact]
    public void FlushPendingSave_AfterScalarEdits_WritesDatabase()
    {
        var dir = NewTestDir();
        var cm = CreateManager(dir);
        cm.LoadConfig();

        cm.SetNoFail(true);
        cm.FlushPendingSave();
        Assert.Equal("True", ReadRows(dir)["NoFail"]);

        cm.SetAutoPlay(true);
        cm.FlushPendingSave();
        Assert.Equal("True", ReadRows(dir)["AutoPlay"]);

        cm.SetAudioLatency(350);
        cm.FlushPendingSave();
        Assert.Equal("350", ReadRows(dir)["AudioLatencyOffsetMs"]);

        cm.SetResolution(1920, 1080);
        cm.FlushPendingSave();
        var rows = ReadRows(dir);
        Assert.Equal("1920", rows["ScreenWidth"]);
        Assert.Equal("1080", rows["ScreenHeight"]);

        cm.SetFullscreen(true);
        cm.FlushPendingSave();
        Assert.Equal("True", ReadRows(dir)["FullScreen"]);

        // VSyncWait defaults to True, so flip to False to prove the edit landed.
        cm.SetVSync(false);
        cm.FlushPendingSave();
        Assert.Equal("False", ReadRows(dir)["VSyncWait"]);
    }

    /// <summary>
    /// Pins FlushPendingSave's retry-on-failure contract: when a flush fails (exception
    /// caught internally), the pending marker is KEPT so the next flush retries.
    /// The scalar setters (e.g. SetNoFail) mark dirty without any path, so this
    /// test drives LoadConfig -> SetNoFail, then toggles the filesystem so the first
    /// flush fails and the second succeeds against the SAME database. The pending
    /// marker never changes.
    /// </summary>
    [Fact]
    public void FlushPendingSave_ShouldRetryAfterFailure()
    {
        var dir = NewTestDir();

        var configDbPath = Path.Combine(dir, "config.db");
        var manager = CreateManager(dir);

        manager.LoadConfig();

        // Sanity: LoadConfig created the default database.
        Assert.True(File.Exists(configDbPath));
        Assert.Equal("False", ReadRows(dir)["NoFail"]);

        // Scalar setter marks dirty.
        manager.SetNoFail(true);
        Assert.True(manager.Config.NoFail);

        // Break the filesystem at <dir>: replace the directory with a
        // regular file so the store's directory creation throws on save.
        using (var blocker = new ConfigStoreFailureScope(dir))
        {
            // First flush: the store save throws, the exception is caught
            // internally, and the pending marker is retained. Must NOT throw.
            manager.FlushPendingSave();

            // In-memory value survives the failed flush.
            Assert.True(manager.Config.NoFail);

            // The pending marker survived — proves the flush genuinely failed
            // and the edit will be retried (the directory itself is gone, so
            // File.Exists would be trivially false and prove nothing).
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(manager, "_hasPendingSave"));

            // Fix the filesystem at <dir>: remove the blocking file so the
            // retry can recreate the directory and write the database at the
            // SAME path.
            blocker.Repair();

            // Second flush: retries the retained pending save and succeeds.
            manager.FlushPendingSave();
        }

        // The edit now persists on retry.
        Assert.True(File.Exists(configDbPath));
        Assert.Equal("True", ReadRows(dir)["NoFail"]);
    }

    [Fact]
    public void FlushPendingSave_SuccessClearsPendingMarker()
    {
        // A successful flush clears the pending marker, so direct Config
        // mutations afterwards (which never mark dirty) cannot be resurrected
        // by an extra flush.
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();
        manager.SetNoFail(true);
        manager.FlushPendingSave();
        Assert.Equal("True", ReadRows(dir)["NoFail"]);

        // Direct mutation does not mark a deferred save. If the successful
        // flush cleared the marker, a second flush must leave True intact.
        manager.Config.NoFail = false;
        manager.FlushPendingSave();

        Assert.Equal("True", ReadRows(dir)["NoFail"]);
    }

    [Fact]
    public void ConfigManager_SaveAndLoadConfig_MidiVelocityThresholds_ShouldPreserveNonzeroThresholds()
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();

        manager.SetMidiVelocityThreshold(36, 20);
        manager.SetMidiVelocityThreshold(38, 12);
        manager.FlushPendingSave();

        var rows = ReadRows(dir);
        Assert.Equal("20", rows["MidiVelocity.36"]);
        Assert.Equal("12", rows["MidiVelocity.38"]);

        var reloaded = CreateManager(dir);
        reloaded.LoadConfig();

        Assert.Equal(20, reloaded.GetMidiVelocityThreshold(36));
        Assert.Equal(12, reloaded.GetMidiVelocityThreshold(38));
        Assert.Equal(0, reloaded.GetMidiVelocityThreshold(40));
    }

    [Fact]
    public void ConfigManager_SetMidiVelocityThreshold_Zero_ShouldRemovePersistedThreshold()
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();

        manager.SetMidiVelocityThreshold(36, 20);
        manager.SetMidiVelocityThreshold(36, 0);

        Assert.Equal(0, manager.GetMidiVelocityThreshold(36));
        Assert.False(manager.Config.MidiVelocityThresholds.ContainsKey(36));

        manager.FlushPendingSave();

        Assert.False(ReadRows(dir).ContainsKey("MidiVelocity.36"));
    }

    [Fact]
    public void ConfigManager_LoadConfig_InvalidMidiVelocityThresholds_ShouldIgnoreOrClamp()
    {
        var dir = NewTestDir();
        File.WriteAllText(Path.Combine(dir, "Config.ini"), string.Join(Environment.NewLine, new[]
        {
            "[MidiVelocityThresholds]",
            "MidiVelocity.36=300",
            "MidiVelocity.38=-4",
            "MidiVelocity.200=50",
            "MidiVelocity.bad=40",
            "MidiVelocity.40=abc"
        }));

        var manager = CreateManager(dir);
        manager.LoadConfig();

        Assert.Equal(127, manager.GetMidiVelocityThreshold(36));
        Assert.Equal(0, manager.GetMidiVelocityThreshold(38));
        Assert.Equal(0, manager.GetMidiVelocityThreshold(200));
        Assert.Equal(0, manager.GetMidiVelocityThreshold(40));
    }

    [Fact]
    public void ConfigManager_LoadConfig_MalformedMidiVelocityValue_ShouldLogWarning()
    {
        // A hand-edited "MidiVelocity.40=abc" must not silently vanish — the user's
        // sensitivity setting otherwise disappears with no clue why their kit feels
        // wrong after reload. Mirrors IsSupportedBindingKeyOrLog for MIDI.* binding keys.
        // Only a non-integer VALUE on a well-formed key triggers the warning; out-of-range
        // notes (MidiVelocity.200) and non-numeric notes (MidiVelocity.bad) are rejected
        // earlier by TryParseMidiVelocityThresholdKey and take a different (silent) path.
        var dir = NewTestDir();
        File.WriteAllText(Path.Combine(dir, "Config.ini"), string.Join(Environment.NewLine, new[]
        {
            "[MidiVelocityThresholds]",
            "MidiVelocity.36=20",    // valid integer value — loads, no warning
            "MidiVelocity.40=abc"    // malformed value — ignored AND warned
        }));

        var logger = new Mock<ILogger<ConfigManager>>();
        var manager = new ConfigManager(
            Path.Combine(dir, "config.db"),
            Path.Combine(dir, "Config.ini"),
            logger: logger.Object);
        manager.LoadConfig();

        // Valid entry still loads.
        Assert.Equal(20, manager.GetMidiVelocityThreshold(36));
        // Malformed entry is ignored (no threshold set).
        Assert.Equal(0, manager.GetMidiVelocityThreshold(40));

        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("MidiVelocity.40") && v.ToString()!.Contains("abc")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ConfigManager_LoadConfig_WithoutMidiVelocityThresholds_ShouldClearPreviouslyLoadedThresholds()
    {
        // Two loads against the same store: the second load's snapshot no
        // longer contains the threshold row, so the in-memory state is cleared.
        var dir = NewTestDir();
        var store = new SqliteConfigStore(Path.Combine(dir, "config.db"));
        store.Save(new Dictionary<string, string> { ["MidiVelocity.36"] = "20" });
        var manager = CreateManager(dir);

        manager.LoadConfig();
        Assert.Equal(20, manager.GetMidiVelocityThreshold(36));

        store.Save(new Dictionary<string, string> { ["ScreenWidth"] = "1280" });
        manager.LoadConfig();

        Assert.Equal(0, manager.GetMidiVelocityThreshold(36));
    }

    [Fact]
    public void ConfigManager_SetMidiVelocityThreshold_AfterLoadAndFlush_ShouldPersistThreshold()
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();

        manager.SetMidiVelocityThreshold(36, 20);
        manager.FlushPendingSave();

        Assert.Equal("20", ReadRows(dir)["MidiVelocity.36"]);

        var reloaded = CreateManager(dir);
        reloaded.LoadConfig();
        Assert.Equal(20, reloaded.GetMidiVelocityThreshold(36));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void ConfigManager_GetMidiVelocityThreshold_OutOfRangeNoteNumber_ReturnsZero(int noteNumber)
    {
        var manager = new ConfigManager();
        manager.SetMidiVelocityThreshold(36, 20);

        Assert.Equal(0, manager.GetMidiVelocityThreshold(noteNumber));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void ConfigManager_SetMidiVelocityThreshold_OutOfRangeNoteNumber_IsNoOp(int noteNumber)
    {
        var manager = new ConfigManager();

        manager.SetMidiVelocityThreshold(noteNumber, 50);

        Assert.False(manager.Config.MidiVelocityThresholds.ContainsKey(noteNumber));
        Assert.Equal(0, manager.GetMidiVelocityThreshold(noteNumber));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(-10)]
    public void ConfigManager_SetMidiVelocityThreshold_OutOfRangeVelocity_ClampsToValidRange(int velocity)
    {
        var manager = new ConfigManager();

        manager.SetMidiVelocityThreshold(36, velocity);

        // Velocity > 127 clamps to 127; velocity < 0 clamps to 0 (which removes the entry).
        if (velocity > 127)
        {
            Assert.Equal(127, manager.GetMidiVelocityThreshold(36));
        }
        else
        {
            Assert.Equal(0, manager.GetMidiVelocityThreshold(36));
            Assert.False(manager.Config.MidiVelocityThresholds.ContainsKey(36));
        }
    }

    [Fact]
    public void ConfigManager_SetMidiVelocityThreshold_AtBoundaryNoteNumbers_ShouldSucceed()
    {
        var manager = new ConfigManager();

        manager.SetMidiVelocityThreshold(0, 10);
        manager.SetMidiVelocityThreshold(127, 20);

        Assert.Equal(10, manager.GetMidiVelocityThreshold(0));
        Assert.Equal(20, manager.GetMidiVelocityThreshold(127));
    }

    [Fact]
    public void ConfigManager_LoadConfig_CanonicalMidiButtonId_ShouldBeAccepted()
    {
        var dir = NewTestDir();
        File.WriteAllText(Path.Combine(dir, "Config.ini"), string.Join(Environment.NewLine, new[]
        {
            "[KeyBindings]",
            "MIDI.36=5"
        }));

        var manager = CreateManager(dir);
        manager.LoadConfig();

        Assert.True(manager.Config.KeyBindings.ContainsKey("MIDI.36"));
        Assert.Equal(5, manager.Config.KeyBindings["MIDI.36"]);
    }

    [Theory]
    [InlineData("MIDI.036", "leading zero in note number")]
    [InlineData("MIDI.128", "note number out of range")]
    [InlineData("MIDI.abc", "non-numeric note number")]
    [InlineData("MIDI.",   "missing note number")]
    [InlineData("MIDI.-1", "negative note number")]
    public void ConfigManager_LoadConfig_MalformedMidiButtonId_ShouldBeRejected(
        string malformedKey, string description)
    {
        // Hand-edited configs with malformed MIDI IDs must not silently load as bindings
        // that can never be matched at lookup time (TryParseMidiButtonId is strict about
        // canonical decimal form). The parser should reject them outright.
        var dir = NewTestDir();
        File.WriteAllText(Path.Combine(dir, "Config.ini"), string.Join(Environment.NewLine, new[]
        {
            "[KeyBindings]",
            $"{malformedKey}=5"
        }));

        var manager = CreateManager(dir);
        manager.LoadConfig();

        Assert.False(
            manager.Config.KeyBindings.ContainsKey(malformedKey),
            $"Malformed key '{malformedKey}' ({description}) should have been rejected.");
    }

    [Fact]
    public void ConfigManager_LoadConfig_MalformedMidiButtonId_ShouldNotBlockCanonicalKeys()
    {
        // A single malformed key must not prevent other (valid) keys in the same
        // config file from loading correctly.
        var dir = NewTestDir();
        File.WriteAllText(Path.Combine(dir, "Config.ini"), string.Join(Environment.NewLine, new[]
        {
            "[KeyBindings]",
            "MIDI.036=3",
            "MIDI.36=5",
            "MIDI.38=7"
        }));

        var manager = CreateManager(dir);
        manager.LoadConfig();

        Assert.False(manager.Config.KeyBindings.ContainsKey("MIDI.036"));
        Assert.True(manager.Config.KeyBindings.ContainsKey("MIDI.36"));
        Assert.Equal(5, manager.Config.KeyBindings["MIDI.36"]);
        Assert.True(manager.Config.KeyBindings.ContainsKey("MIDI.38"));
        Assert.Equal(7, manager.Config.KeyBindings["MIDI.38"]);
    }

    [Fact]
    public void ConfigManager_LoadConfig_MalformedMidiUnboundButton_ShouldBeRejected()
    {
        var dir = NewTestDir();
        File.WriteAllText(Path.Combine(dir, "Config.ini"), string.Join(Environment.NewLine, new[]
        {
            "[KeyBindings]",
            "Key.UnboundButton.MIDI.036=True",
            "Key.UnboundButton.MIDI.36=True"
        }));

        var manager = CreateManager(dir);
        manager.LoadConfig();

        Assert.False(manager.Config.UnboundDrumButtons.Contains("MIDI.036"));
        Assert.True(manager.Config.UnboundDrumButtons.Contains("MIDI.36"));
    }

    [Fact]
    public void FlushPendingSave_WhenSongRootsAreEmpty_ShouldNotOverwriteTheLegacyDtxPathMirror()
    {
        var dir = NewTestDir();
        var manager = CreateManager(dir);
        manager.LoadConfig();
        // Force the defensive empty-roots branch: LoadConfig always populates at
        // least one managed default, so clear it to exercise the guard.
        var preservedDtxPath = "preserved-dtx-path";
        manager.Config.SongRoots.Clear();
        manager.Config.DTXPath = preservedDtxPath;

        manager.SetNoFail(true);
        manager.FlushPendingSave();

        // The empty-roots guard must skip reassigning DTXPath from SongRoots[0].
        Assert.Equal(preservedDtxPath, manager.Config.DTXPath);
        var rows = ReadRows(dir);
        Assert.DoesNotContain(rows.Keys, key => key.StartsWith("SongRoot.", StringComparison.Ordinal));
    }
}
