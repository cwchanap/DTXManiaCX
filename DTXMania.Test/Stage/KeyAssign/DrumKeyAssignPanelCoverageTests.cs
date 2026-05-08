using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Test.Stage.KeyAssign;

[Trait("Category", "Unit")]
public class DrumKeyAssignPanelCoverageTests
{
    /// <summary>
    /// Number of Down presses needed to navigate past all drum lanes
    /// to reach the Save footer row (LaneCount = 10 in DrumKeyAssignPanel).
    /// </summary>
    private const int PressesToReachSaveRow = 10;

    private static ModularInputManager CreateModularInputManager()
    {
        var configManager = new ConfigManager();
        return new ModularInputManager(configManager);
    }

    [Fact]
    public void GetFooterCancelLabel_WithDefaultNavigation_ShouldReturnCancelWithEscape()
    {
        var modularInputManager = CreateModularInputManager();
        var panel = new DrumKeyAssignPanel(modularInputManager);
        panel._navigationMappingProvider = null;
        panel._workingBindingsProvider = null;
        panel.Activate();

        var label = panel.GetFooterCancelLabel();

        Assert.Equal("CANCEL (ESCAPE)", label);
    }

    [Fact]
    public void GetFooterCancelLabel_WithNoBackBinding_ShouldReturnCancel()
    {
        var modularInputManager = CreateModularInputManager();
        var panel = new DrumKeyAssignPanel(modularInputManager);
        panel._navigationMappingProvider = () => new Dictionary<Keys, InputCommandType>
        {
            [Keys.Up] = InputCommandType.MoveUp,
            [Keys.Down] = InputCommandType.MoveDown
        };
        panel._workingBindingsProvider = null;
        panel.Activate();

        var label = panel.GetFooterCancelLabel();

        Assert.Equal("CANCEL", label);
    }

    [Fact]
    public void GetInstructionText_WithDefaultNavigation_ShouldIncludeAllControls()
    {
        var modularInputManager = CreateModularInputManager();
        var panel = new DrumKeyAssignPanel(modularInputManager);
        panel._navigationMappingProvider = null;
        panel._workingBindingsProvider = null;
        panel.Activate();

        var text = panel.GetInstructionText();

        Assert.Contains("UP/DOWN", text);
        Assert.Contains("Navigate", text);
        Assert.Contains("ENTER", text);
        Assert.Contains("Assign", text);
        Assert.Contains("DELETE", text);
        Assert.Contains("ESCAPE", text);
        Assert.Contains("Cancel", text);
    }

    [Fact]
    public void GetInstructionText_WithEmptyNavigationMapping_ShouldUseDefaults()
    {
        var modularInputManager = CreateModularInputManager();
        var panel = new DrumKeyAssignPanel(modularInputManager);
        panel._navigationMappingProvider = () => new Dictionary<Keys, InputCommandType>();
        panel._workingBindingsProvider = null;
        panel.Activate();

        var text = panel.GetInstructionText();

        Assert.Contains("UP/DOWN", text);
        Assert.Contains("ENTER", text);
        Assert.Contains("BACK", text);
    }

    [Fact]
    public void Activate_WithWorkingBindingsProvider_ShouldUseProvidedBindings()
    {
        var modularInputManager = CreateModularInputManager();
        var panel = new DrumKeyAssignPanel(modularInputManager);
        var customBindings = new KeyBindings();
        customBindings.BindButton("Key.Q", 0);
        panel._workingBindingsProvider = () => customBindings.Clone();
        panel._navigationMappingProvider = null;
        panel._liveSystemMappingProvider = () => new Dictionary<Keys, InputCommandType>();

        panel.Activate();

        var snapshot = panel.GetWorkingBindingsSnapshot();
        Assert.Equal(0, snapshot.GetLane("Key.Q"));
    }

    [Fact]
    public void Update_WhenAwaitingKeyAndBackPressed_ShouldReturnToBrowsing()
    {
        var modularInputManager = CreateModularInputManager();
        var panel = new DrumKeyAssignPanel(modularInputManager);
        panel._workingBindingsProvider = null;
        panel._navigationMappingProvider = null;
        panel._liveSystemMappingProvider = () => new Dictionary<Keys, InputCommandType>();
        panel._commandPressedProvider = null;
        panel.Activate();

        panel.Update(0.016, new KeyboardState(Keys.Enter), new KeyboardState());
        Assert.True(panel.IsActive);

        panel.Update(0.016, new KeyboardState(Keys.Escape), new KeyboardState());

        var snapshot = panel.GetWorkingBindingsSnapshot();
        Assert.Equal(-1, snapshot.GetLane("Key.Escape"));
    }

    [Fact]
    public void CommitWithPendingChanges_ShouldFireSavedAndClosed()
    {
        var modularInputManager = CreateModularInputManager();
        var panel = new DrumKeyAssignPanel(modularInputManager);
        panel._workingBindingsProvider = null;
        panel._navigationMappingProvider = null;
        panel._liveSystemMappingProvider = () => new Dictionary<Keys, InputCommandType>();
        panel.EvictSystemBinding = _ => { };
        panel.Activate();
        for (int i = 0; i < PressesToReachSaveRow; i++)
            panel.Update(0.016, new KeyboardState(Keys.Down), new KeyboardState());

        var savedFired = false;
        var closedFired = false;
        panel.Saved += (_, _) => savedFired = true;
        panel.Closed += (_, _) => closedFired = true;

        panel.Update(0.016, new KeyboardState(Keys.Enter), new KeyboardState());

        Assert.True(savedFired);
        Assert.True(closedFired);
        Assert.False(panel.IsActive);
    }

    [Fact]
    public void CancelWithoutSaving_ShouldFireClosedOnly()
    {
        var modularInputManager = CreateModularInputManager();
        var panel = new DrumKeyAssignPanel(modularInputManager);
        panel._workingBindingsProvider = null;
        panel._navigationMappingProvider = null;
        panel._liveSystemMappingProvider = () => new Dictionary<Keys, InputCommandType>();
        panel._commandPressedProvider = null;
        panel.Activate();

        var savedFired = false;
        var closedFired = false;
        panel.Saved += (_, _) => savedFired = true;
        panel.Closed += (_, _) => closedFired = true;

        panel.Update(0.016, new KeyboardState(Keys.Escape), new KeyboardState());

        Assert.False(savedFired);
        Assert.True(closedFired);
        Assert.False(panel.IsActive);
    }
}
