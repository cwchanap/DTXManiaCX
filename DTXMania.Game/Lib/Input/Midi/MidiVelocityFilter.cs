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

    /// <summary>
    /// Returns <see langword="true"/> when a note-on at <paramref name="velocity"/> should be
    /// accepted as a pad hit. A note is accepted only when its velocity is <b>strictly greater
    /// than</b> the per-note threshold (configurable via <c>[MidiVelocityThresholds]</c>).
    /// <para>
    /// Design rationale for strict <c>&gt;</c> rather than <c>&gt;=</c>: the threshold represents
    /// the sensitivity floor — a note exactly at the threshold is treated as sub-threshold noise
    /// and filtered out. To accept a note at velocity <em>V</em>, set the threshold to <em>V − 1</em>.
    /// </para>
    /// </summary>
    /// <param name="noteNumber">MIDI note number (0–127).</param>
    /// <param name="velocity">Note-on velocity (1–127; velocity 0 is always rejected as it encodes NoteOff).</param>
    /// <returns><see langword="true"/> if the note should register as a hit.</returns>
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
