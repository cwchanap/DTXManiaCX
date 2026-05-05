using System.Collections.Generic;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Test.Config;

[Trait("Category", "Persistence")]
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
            var snapshot = inputMgr.GetKeyMappingSnapshot();
            Assert.Equal(InputCommandType.MoveUp, snapshot[Keys.Up]);
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
    public void ConfigManager_SaveSystemKeyBindings_MultiKeyCommand_ShouldPersistAllKeys()
    {
        var manager = new ConfigManager();

        manager.SaveSystemKeyBindings(new Dictionary<Keys, InputCommandType>
        {
            [Keys.Enter] = InputCommandType.Activate,
            [Keys.Space] = InputCommandType.Activate,
        });

        Assert.Equal("Enter,Space", manager.Config.SystemKeyBindings["SystemKey.Activate"]);
    }

    [Fact]
    public void ConfigManager_LoadSystemKeyBindings_ShouldApplyToInputManager()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.MoveUp"] = "W";

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.MoveUp, snapshot[Keys.W]);
        Assert.False(snapshot.TryGetValue(Keys.Up, out var upCommand) && upCommand == InputCommandType.MoveUp);
    }

    [Fact]
    public void ConfigManager_CreateConfiguredInputManager_ShouldApplySavedActivateBinding()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.Activate"] = "Tab";

        var inputMgr = manager.CreateConfiguredInputManager();

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.Activate, snapshot[Keys.Tab]);
        Assert.DoesNotContain(snapshot, kvp => kvp.Key == Keys.Enter && kvp.Value == InputCommandType.Activate);
        Assert.DoesNotContain(snapshot, kvp => kvp.Key == Keys.Space && kvp.Value == InputCommandType.Activate);
    }

    [Fact]
    public void ConfigManager_LoadSystemKeyBindings_MultiKeyEntry_ShouldApplyAllKeys()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.Activate"] = "Tab,Q";

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.Activate, snapshot[Keys.Tab]);
        Assert.Equal(InputCommandType.Activate, snapshot[Keys.Q]);
        Assert.DoesNotContain(snapshot, kvp => kvp.Key == Keys.Enter && kvp.Value == InputCommandType.Activate);
        Assert.DoesNotContain(snapshot, kvp => kvp.Key == Keys.Space && kvp.Value == InputCommandType.Activate);
    }

    [Fact]
    public void ConfigManager_LoadSystemKeyBindings_EmptyRequiredValue_ShouldPreserveFallbackBinding()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.Back"] = string.Empty;

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.True(snapshot.TryGetValue(Keys.Escape, out var backCommand));
        Assert.Equal(InputCommandType.Back, backCommand);
    }

    [Fact]
    public void ConfigManager_LoadSystemKeyBindings_EmptyMoveLeftValue_ShouldPreserveFallbackBinding()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.MoveLeft"] = string.Empty;

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.True(snapshot.TryGetValue(Keys.Left, out var moveLeftCommand));
        Assert.Equal(InputCommandType.MoveLeft, moveLeftCommand);
    }

    [Fact]
    public void ConfigManager_LoadSystemKeyBindings_DuplicatePhysicalKey_ShouldRestoreFallbackForRequiredCommand()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.MoveUp"] = "Tab";
        manager.Config.SystemKeyBindings["SystemKey.Activate"] = "Tab";

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.Activate, snapshot[Keys.Tab]);
        Assert.Equal(InputCommandType.MoveUp, snapshot[Keys.Up]);
    }

    [Fact]
    public void ConfigManager_LoadSystemKeyBindings_RequiredFallbackCollision_ShouldKeepBothCommandsBound()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.MoveLeft"] = "Right";

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.MoveLeft, snapshot[Keys.Left]);
        Assert.Equal(InputCommandType.MoveRight, snapshot[Keys.Right]);
    }

    [Fact]
    public void ConfigManager_LoadSystemKeyBindings_ReusedFallbackKeyWithOwnerRemapped_ShouldPreserveCustomBinding()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.MoveRight"] = "Q";
        manager.Config.SystemKeyBindings["SystemKey.MoveLeft"] = "Right";

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.MoveRight, snapshot[Keys.Q]);
        Assert.Equal(InputCommandType.MoveLeft, snapshot[Keys.Right]);
        Assert.False(snapshot.TryGetValue(Keys.Left, out var moveLeftCommand) && moveLeftCommand == InputCommandType.MoveLeft);
    }

    [Fact]
    public void ConfigManager_LoadSystemKeyBindings_DrumOverlap_ShouldRejectSystemBindingAndKeepRequiredFallback()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.Back"] = "Space";

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.Back, snapshot[Keys.Escape]);
        Assert.False(snapshot.TryGetValue(Keys.Space, out var spaceCommand) && spaceCommand == InputCommandType.Back);
    }

    [Fact]
    public void ConfigManager_LoadSystemKeyBindings_UnboundDrumLane_ShouldAllowFormerDefaultDrumKeyAsSystemBinding()
    {
        var manager = new ConfigManager();
        manager.Config.UnboundDrumLanes.Add(6);
        manager.Config.SystemKeyBindings["SystemKey.Back"] = "Space";

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.Back, snapshot[Keys.Space]);
        Assert.False(snapshot.TryGetValue(Keys.Escape, out var escapeCommand) && escapeCommand == InputCommandType.Back);
    }

    [Fact]
    public void ConfigManager_SaveSystemKeyBindings_UnboundRequiredCommand_ShouldPersistFallbackEntry()
    {
        var manager = new ConfigManager();
        var inputMgr = new InputManager();
        inputMgr.RemoveKeyMapping(Keys.Escape);

        manager.SaveSystemKeyBindings(inputMgr);

        Assert.True(manager.Config.SystemKeyBindings.ContainsKey("SystemKey.Back"));
        Assert.Equal("Escape", manager.Config.SystemKeyBindings["SystemKey.Back"]);
    }

    [Fact]
    public void ConfigManager_SaveSystemKeyBindings_SparseBindings_ShouldPreserveExistingRequiredBinding()
    {
        var manager = new ConfigManager();
        manager.Config.SystemKeyBindings["SystemKey.Back"] = "Tab";

        manager.SaveSystemKeyBindings(new Dictionary<Keys, InputCommandType>
        {
            [Keys.Space] = InputCommandType.Activate,
        });

        Assert.Equal("Tab", manager.Config.SystemKeyBindings["SystemKey.Back"]);
        Assert.Equal("Space", manager.Config.SystemKeyBindings["SystemKey.Activate"]);
        Assert.Equal("Up", manager.Config.SystemKeyBindings["SystemKey.MoveUp"]);
        Assert.Equal("Down", manager.Config.SystemKeyBindings["SystemKey.MoveDown"]);
        Assert.Equal("Left", manager.Config.SystemKeyBindings["SystemKey.MoveLeft"]);
        Assert.Equal("Right", manager.Config.SystemKeyBindings["SystemKey.MoveRight"]);
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

    // ─── EvictDrumKeyConflicts ─────────────────────────────────────────────

    [Fact]
    public void LoadSystemKeyBindings_DrumKeyOverlapsDefaultScrollSpeed_ShouldEvictDefaultScrollSpeedBinding()
    {
        // Scenario: player bound PageUp to a drum lane. The default PageUp -> IncreaseScrollSpeed
        // should be removed so pressing PageUp during performance doesn't change scroll speed.
        var manager = new ConfigManager();
        manager.Config.KeyBindings["Key.PageUp"] = 4; // bind PageUp to Snare Drum lane

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        // PageUp should NOT be mapped to IncreaseScrollSpeed
        Assert.False(snapshot.TryGetValue(Keys.PageUp, out var pageUpCmd)
            && pageUpCmd == InputCommandType.IncreaseScrollSpeed);
    }

    [Fact]
    public void LoadSystemKeyBindings_DrumKeyOverlapsDefaultScrollSpeed_ShouldKeepNonConflictingDefault()
    {
        // PageDown is NOT a drum key, so its default mapping should remain
        var manager = new ConfigManager();
        manager.Config.KeyBindings["Key.PageUp"] = 4; // only PageUp is a drum key

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.DecreaseScrollSpeed, snapshot[Keys.PageDown]);
    }

    [Fact]
    public void LoadSystemKeyBindings_DrumKeyOverlapsRequiredCommand_ShouldKeepRequiredBinding()
    {
        // A required system command (e.g., Back) should NOT be evicted even if it overlaps a drum key
        var manager = new ConfigManager();
        manager.Config.KeyBindings["Key.Escape"] = 0; // bind Escape to a drum lane

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        // Escape -> Back is required and should still be present
        Assert.Equal(InputCommandType.Back, snapshot[Keys.Escape]);
    }

    [Fact]
    public void LoadSystemKeyBindings_NoDrumOverlap_ShouldKeepDefaultScrollSpeedBindings()
    {
        // When no drum keys overlap, defaults should remain
        var manager = new ConfigManager();
        // No custom drum bindings — only defaults (A, S, D, F, G, J, K, L, Space, ;)

        var inputMgr = new InputManager();
        manager.LoadSystemKeyBindings(inputMgr);

        var snapshot = inputMgr.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.IncreaseScrollSpeed, snapshot[Keys.PageUp]);
        Assert.Equal(InputCommandType.DecreaseScrollSpeed, snapshot[Keys.PageDown]);
    }
}
