using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Input.Midi;
using Xunit;

namespace DTXMania.Test.Input;

[Trait("Category", "Unit")]
public sealed class InputManagerCompatTests : IDisposable
{
    private readonly InputManagerCompat _manager;

    public InputManagerCompatTests()
    {
        _manager = new InputManagerCompat(new ConfigManager(), new TestMidiDeviceBackend());
    }

    public void Dispose()
    {
        _manager.Dispose();
    }

    [Fact]
    public void IsCommandPressed_WhenMoveUpInjected_ShouldReturnTrueAndQueueCommand()
    {
        _manager.ModularInputManager.InjectButton("Key.Up", isPressed: true);

        _manager.Update(0.016);

        Assert.True(_manager.IsCommandPressed(InputCommandType.MoveUp));

        var command = _manager.GetNextCommand();
        Assert.True(command.HasValue);
        Assert.Equal(InputCommandType.MoveUp, command.Value.Type);
        Assert.False(command.Value.IsRepeat);
    }

    [Fact]
    public void IsBackActionTriggered_WhenEscapeInjected_ShouldReturnTrue()
    {
        _manager.ModularInputManager.InjectButton("Key.Escape", isPressed: true);

        _manager.Update(0.016);

        Assert.True(_manager.IsBackActionTriggered());
        Assert.True(_manager.IsCommandPressed(InputCommandType.Back));
    }

    [Fact]
    public void IsCommandPressed_WhenActivateInjected_ShouldReturnTrue()
    {
        _manager.ModularInputManager.InjectButton("Key.Enter", isPressed: true);

        _manager.Update(0.016);

        Assert.True(_manager.IsCommandPressed(InputCommandType.Activate));
    }

    [Fact]
    public void IsCommandPressed_OnNextFrameWithoutNewInjection_ShouldResetInjectedCommandState()
    {
        _manager.ModularInputManager.InjectButton("Key.Enter", isPressed: true);
        _manager.Update(0.016);
        Assert.True(_manager.IsCommandPressed(InputCommandType.Activate));

        _manager.Update(0.016);

        Assert.False(_manager.IsCommandPressed(InputCommandType.Activate));
    }

    [Fact]
    public void ClearPendingCommands_AfterInjection_PreventsCommandsFromAppearing()
    {
        _manager.ModularInputManager.InjectButton("Key.Up", isPressed: true);
        // Do NOT call Update — the button is still in the injected queue.

        _manager.ClearPendingCommands();
        _manager.Update(0.016);

        Assert.False(_manager.IsCommandPressed(InputCommandType.MoveUp));
        Assert.False(_manager.GetNextCommand().HasValue);
    }

    // NOTE: Constructor_WhenSimulatedMidiEnvEnabled_ShouldUseInjectableMidiBackend now lives in
    // InputManagerCompatSimulatedMidiEnvTests, isolated in the non-parallel "SimulatedMidiEnv"
    // collection because it mutates the process-wide DTXMANIA_ENABLE_SIMULATED_MIDI env var.
}
