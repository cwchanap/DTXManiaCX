#nullable enable

using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Input.Midi;

public sealed class SimulatedMidiDeviceBackend : IMidiDeviceBackend, IMidiNoteInjector
{
    private readonly SimulatedMidiInputDevice _device = new();

    public IReadOnlyList<IMidiInputDevice> GetInputDevices() => new[] { _device };

    public bool TryInjectNote(int noteNumber, int velocity, bool isPressed)
    {
        if (noteNumber < 0 || noteNumber > 127)
            return false;

        if (velocity < 0 || velocity > 127)
            return false;

        return _device.Emit(noteNumber, velocity, isPressed);
    }

    private sealed class SimulatedMidiInputDevice : IMidiInputDevice
    {
        private bool _started;
        private bool _disposed;

        public string Name => "Simulated MIDI Device";

        public string StableId => "simulated-midi-device";

        public event EventHandler<MidiNoteEventArgs>? NoteReceived;

        public void Start()
        {
            if (_disposed)
                return;

            _started = true;
        }

        public void Stop()
        {
            _started = false;
        }

        public bool Emit(int noteNumber, int velocity, bool isPressed)
        {
            if (_disposed || !_started)
                return false;

            NoteReceived?.Invoke(
                this,
                new MidiNoteEventArgs(StableId, noteNumber, velocity, isPressed));
            return true;
        }

        public void Dispose()
        {
            _started = false;
            _disposed = true;
            NoteReceived = null;
        }
    }
}
