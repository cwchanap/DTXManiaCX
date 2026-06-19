using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Test.Input;

[Trait("Category", "Unit")]
public sealed class InputManagerCompatLiveApplyTests
{
    [Fact]
    public void KeyBindingsChanged_ReloadsRuntimeDrumBindingsFromConfig()
    {
        var cm = new ConfigManager();
        var input = new InputManagerCompat(cm);

        try
        {
            var kb = new KeyBindings();
            kb.BindButton("Key.Z", 5);
            cm.SetKeyBindings(kb);

            // Runtime should reflect it WITHOUT manual reload
            Assert.Equal(5, input.ModularInputManager.KeyBindings.ButtonToLane["Key.Z"]);

            // Unidirectional invariant: the reload must NOT write back to Config.
            // Config still holds EXACTLY what SetKeyBindings wrote -- no mutation and no
            // extra keys leaked from the runtime's LoadDefaultBindings() overlay. The
            // reverse auto-save (ModularInputManager.cs:427-432) is suppressed during
            // reload today; this guards against a future regression until Task 2.3 removes it.
            Assert.Equal(5, cm.Config.KeyBindings["Key.Z"]);
            Assert.Equal(kb.ButtonToLane.Count, cm.Config.KeyBindings.Count);

            // The reload runs LoadDefaultBindings() before re-applying config; a default
            // binding unrelated to our edit should survive at its default lane (Key.A -> 0).
            Assert.True(input.ModularInputManager.KeyBindings.ButtonToLane.ContainsKey("Key.A"));
            Assert.Equal(0, input.ModularInputManager.KeyBindings.ButtonToLane["Key.A"]);
        }
        finally
        {
            input.Dispose();
        }
    }

    [Fact]
    public void SystemKeyBindingsChanged_RebuildsSystemMapFromConfig()
    {
        var cm = new ConfigManager();
        var input = new InputManagerCompat(cm);

        try
        {
            cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType> { [Keys.Z] = InputCommandType.MoveUp });

            var snapshot = input.GetKeyMappingSnapshot();
            Assert.Equal(InputCommandType.MoveUp, snapshot[Keys.Z]);
        }
        finally
        {
            input.Dispose();
        }
    }
}
