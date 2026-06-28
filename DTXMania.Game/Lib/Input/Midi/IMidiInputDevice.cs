#nullable enable

using System;

namespace DTXMania.Game.Lib.Input.Midi;

public interface IMidiInputDevice : IDisposable
{
    string Name { get; }
    string StableId { get; }
    event EventHandler<MidiNoteEventArgs>? NoteReceived;
    void Start();
    void Stop();
}
