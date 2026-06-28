#nullable enable

using System;

namespace DTXMania.Game.Lib.Input.Midi;

public sealed class MidiVelocityFilter
{
    private readonly Func<int, int> _thresholdProvider;

    public MidiVelocityFilter(Func<int, int> thresholdProvider)
    {
        _thresholdProvider = thresholdProvider ?? throw new ArgumentNullException(nameof(thresholdProvider));
    }

    public bool ShouldAcceptPress(int noteNumber, int velocity)
    {
        if (noteNumber < 0 || noteNumber > 127)
            return false;

        if (velocity <= 0)
            return false;

        var clampedVelocity = Math.Clamp(velocity, 0, 127);
        var threshold = Math.Clamp(_thresholdProvider(noteNumber), 0, 127);
        return clampedVelocity > threshold;
    }
}
