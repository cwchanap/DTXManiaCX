using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Input.Midi;
using Xunit;

namespace DTXMania.Test.Input;

/// <summary>
/// xUnit collection that disables parallelization for tests that mutate the process-wide
/// <c>DTXMANIA_ENABLE_SIMULATED_MIDI</c> environment variable. Because the env var is read by
/// <see cref="MidiDeviceBackendFactory.CreateDefault"/>, any parallel test that constructs an
/// <see cref="InputManagerCompat"/> with the default backend would otherwise observe the
/// simulated backend. Tests in this collection are serialized to isolate that mutation.
/// </summary>
[CollectionDefinition("SimulatedMidiEnv", DisableParallelization = true)]
public sealed class SimulatedMidiEnvCollectionDefinition
{
}

/// <summary>
/// The simulated-MIDI env var toggle is process-wide, so this scenario is isolated in its own
/// collection (see <see cref="SimulatedMidiEnvCollectionDefinition"/>) to avoid racing with
/// sibling <see cref="InputManagerCompatTests"/> constructor cases.
/// </summary>
[Collection("SimulatedMidiEnv")]
[Trait("Category", "Unit")]
public sealed class InputManagerCompatSimulatedMidiEnvTests
{
    [Fact]
    public void Constructor_WhenSimulatedMidiEnvEnabled_ShouldUseInjectableMidiBackend()
    {
        var previous = Environment.GetEnvironmentVariable(MidiDeviceBackendFactory.EnableSimulatedMidiEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(MidiDeviceBackendFactory.EnableSimulatedMidiEnvironmentVariable, "1");
            using var manager = new InputManagerCompat(new ConfigManager());

            Assert.True(manager.ModularInputManager.InjectMidiNote(36, 100, isPressed: true));
            manager.ModularInputManager.Update();

            var pressed = manager.ModularInputManager.ConsumePressedButtons();
            var button = Assert.Single(pressed, state => state.Id == "MIDI.36");
            Assert.Equal(100f / 127f, button.Velocity, precision: 4);
        }
        finally
        {
            Environment.SetEnvironmentVariable(MidiDeviceBackendFactory.EnableSimulatedMidiEnvironmentVariable, previous);
        }
    }
}
