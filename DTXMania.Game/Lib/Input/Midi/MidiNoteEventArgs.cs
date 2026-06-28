#nullable enable

using System;

namespace DTXMania.Game.Lib.Input.Midi;

public sealed class MidiNoteEventArgs : EventArgs
{
    public MidiNoteEventArgs(string? deviceStableId, int noteNumber, int velocity, bool isPressed)
    {
        DeviceStableId = string.IsNullOrWhiteSpace(deviceStableId) ? "unknown" : deviceStableId;
        NoteNumber = Math.Clamp(noteNumber, 0, 127);
        Velocity = Math.Clamp(velocity, 0, 127);
        IsPressed = isPressed && Velocity > 0;
    }

    public string DeviceStableId { get; }
    public int NoteNumber { get; }
    public int Velocity { get; }
    public bool IsPressed { get; }
}
