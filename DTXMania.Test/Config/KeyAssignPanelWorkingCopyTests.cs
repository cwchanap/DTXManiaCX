using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
using Microsoft.Xna.Framework.Input;
using System.Reflection;
using System.Runtime.Serialization;

namespace DTXMania.Test.Config;

public class KeyAssignPanelWorkingCopyTests
{
    [Fact]
    public void KeyAssignPanels_Save_ShouldLeaveLiveStateUntouchedUntilStageApplies()
    {
        using var inputManager = new InputManager();
        var systemPanel = new SystemKeyAssignPanel(inputManager);

        systemPanel.Activate();

        for (int i = 0; i < 5; i++)
        {
            PressKey(systemPanel, Keys.Down);
        }

        PressKey(systemPanel, Keys.Delete);
        PressKey(systemPanel, Keys.Down);
        PressKey(systemPanel, Keys.Enter);

        var liveSnapshot = inputManager.GetKeyMappingSnapshot();
        Assert.Equal(InputCommandType.Back, liveSnapshot[Keys.Escape]);

        var liveBindings = new KeyBindings();
        var drumPanel = new DrumKeyAssignPanel(CreateUnusedModularInputManager(liveBindings));

        drumPanel.Activate();
        PressKey(drumPanel, Keys.Delete);
        for (int i = 0; i < 10; i++)
        {
            PressKey(drumPanel, Keys.Down);
        }

        PressKey(drumPanel, Keys.Enter);

        Assert.Equal(0, liveBindings.GetLane("Key.A"));
    }

    private static void PressKey(IKeyAssignPanel panel, Keys key)
    {
        panel.Update(0.0, new KeyboardState(key), new KeyboardState());
    }

    private static ModularInputManager CreateUnusedModularInputManager(KeyBindings liveBindings)
    {
#pragma warning disable SYSLIB0050
        var manager = (ModularInputManager)FormatterServices.GetUninitializedObject(typeof(ModularInputManager));
#pragma warning restore SYSLIB0050
        var keyBindingsField = typeof(ModularInputManager)
            .GetField("_keyBindings", BindingFlags.Instance | BindingFlags.NonPublic);

        keyBindingsField?.SetValue(manager, liveBindings);
        return manager;
    }
}
