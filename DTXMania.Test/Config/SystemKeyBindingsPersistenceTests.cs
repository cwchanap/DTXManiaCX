using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Test.Config;

public class SystemKeyBindingsPersistenceTests
{
    // ─── ParseConfigLine ──────────────────────────────────────────────────────

    [Fact]
    public void ConfigManager_LoadConfig_ShouldParseSystemKeyBindings()
    {
        var ini = """
            [SystemKeyBindings]
            SystemKey.MoveUp=W
            SystemKey.MoveDown=S
            SystemKey.Activate=Space
            """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, ini);
            var manager = new ConfigManager();
            manager.LoadConfig(tempFile);

            Assert.Equal("W", manager.Config.SystemKeyBindings["SystemKey.MoveUp"]);
            Assert.Equal("S", manager.Config.SystemKeyBindings["SystemKey.MoveDown"]);
            Assert.Equal("Space", manager.Config.SystemKeyBindings["SystemKey.Activate"]);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void ConfigManager_LoadConfig_InvalidKeyName_ShouldSkipEntry()
    {
        var ini = "SystemKey.MoveUp=NotAValidKey\n";
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, ini);
            var manager = new ConfigManager();
            manager.LoadConfig(tempFile);

            // The entry is stored as-is; LoadSystemKeyBindings will silently skip it
            // when parsing enum values.
            manager.Config.SystemKeyBindings.TryGetValue("SystemKey.MoveUp", out var val);
            Assert.Equal("NotAValidKey", val);

            var inputMgr = new InputManager();
            // Should not throw – invalid values are skipped
            manager.LoadSystemKeyBindings(inputMgr);
            // Default Up->MoveUp should still be in place
            Assert.True(inputMgr.GetKeyMappingSnapshot().Values.Contains(InputCommandType.MoveUp));
        }
        finally { File.Delete(tempFile); }
    }

    // ─── SaveConfig round-trip ────────────────────────────────────────────────

    [Fact]
    public void ConfigManager_SaveConfig_ShouldWriteSystemKeyBindingsSection()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.MoveUp"] = "W";
        manager.Config.SystemKeyBindings["SystemKey.Back"] = "Escape";

        var tempFile = Path.GetTempFileName();
        try
        {
            manager.SaveConfig(tempFile);
            var content = File.ReadAllText(tempFile);
            Assert.Contains("[SystemKeyBindings]", content);
            Assert.Contains("SystemKey.MoveUp=W", content);
            Assert.Contains("SystemKey.Back=Escape", content);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void ConfigManager_RoundTrip_SystemKeyBindings_ShouldPreserveValues()
    {
        var manager = new ConfigManager();
        var inputMgr = new InputManager();

        // Set custom system binding: W -> MoveUp
        inputMgr.SetKeyMapping(Keys.W, InputCommandType.MoveUp);
        inputMgr.RemoveKeyMapping(Keys.Up); // remove default Up -> MoveUp
        manager.SaveSystemKeyBindings(inputMgr);

        var tempFile = Path.GetTempFileName();
        try
        {
            manager.SaveConfig(tempFile);

            // Load into a fresh manager and apply
            var manager2 = new ConfigManager();
            manager2.LoadConfig(tempFile);
            var inputMgr2 = new InputManager();
            manager2.LoadSystemKeyBindings(inputMgr2);

            var snapshot = inputMgr2.GetKeyMappingSnapshot();
            Assert.Equal(InputCommandType.MoveUp, snapshot[Keys.W]);
            // The default Keys.Up -> MoveUp binding must have been replaced, not duplicated.
            Assert.False(snapshot.TryGetValue(Keys.Up, out var upCommand) && upCommand == InputCommandType.MoveUp);
        }
        finally { File.Delete(tempFile); }
    }

    // ─── SaveSystemKeyBindings / LoadSystemKeyBindings ────────────────────────

    [Fact]
    public void ConfigManager_SaveSystemKeyBindings_ShouldPopulateConfigDict()
    {
        var manager = new ConfigManager();
        var inputMgr = new InputManager();
        inputMgr.SetKeyMapping(Keys.W, InputCommandType.MoveUp);

        manager.SaveSystemKeyBindings(inputMgr);

        Assert.Contains("SystemKey.MoveUp", manager.Config.SystemKeyBindings.Keys);
        Assert.Equal("W", manager.Config.SystemKeyBindings["SystemKey.MoveUp"]);
    }

    [Fact]
    public void ConfigManager_LoadSystemKeyBindings_ShouldApplyToInputManager()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.MoveUp"] = "W";

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.True(snapshot.ContainsKey(Keys.W));
        Assert.Equal(InputCommandType.MoveUp, snapshot[Keys.W]);
    }

    // ─── InputManager mutation API ────────────────────────────────────────────

    [Fact]
    public void InputManager_SetKeyMapping_ShouldAddEntry()
    {
        var mgr = new InputManager();
        mgr.SetKeyMapping(Keys.W, InputCommandType.MoveUp);
        Assert.Equal(InputCommandType.MoveUp, mgr.GetKeyMappingSnapshot()[Keys.W]);
    }

    [Fact]
    public void InputManager_RemoveKeyMapping_ShouldRemoveEntry()
    {
        var mgr = new InputManager();
        mgr.RemoveKeyMapping(Keys.Up);
        Assert.False(mgr.GetKeyMappingSnapshot().ContainsKey(Keys.Up));
    }

    [Fact]
    public void InputManager_GetKeyMappingSnapshot_ShouldReturnCopy()
    {
        var mgr = new InputManager();
        var snap1 = mgr.GetKeyMappingSnapshot();
        mgr.SetKeyMapping(Keys.F1, InputCommandType.MoveUp);
        var snap2 = mgr.GetKeyMappingSnapshot();

        // snap1 must not be modified
        Assert.False(snap1.ContainsKey(Keys.F1));
        Assert.True(snap2.ContainsKey(Keys.F1));
    }
}
