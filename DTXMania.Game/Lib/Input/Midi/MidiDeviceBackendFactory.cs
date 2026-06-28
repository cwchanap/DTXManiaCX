#nullable enable

using System;

namespace DTXMania.Game.Lib.Input.Midi;

public static class MidiDeviceBackendFactory
{
    public const string EnableSimulatedMidiEnvironmentVariable = "DTXMANIA_ENABLE_SIMULATED_MIDI";

    public static IMidiDeviceBackend CreateDefault()
    {
        return IsSimulatedMidiEnabled()
            ? new SimulatedMidiDeviceBackend()
            : new DryWetMidiDeviceBackend();
    }

    private static bool IsSimulatedMidiEnabled()
    {
        var raw = Environment.GetEnvironmentVariable(EnableSimulatedMidiEnvironmentVariable);
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
