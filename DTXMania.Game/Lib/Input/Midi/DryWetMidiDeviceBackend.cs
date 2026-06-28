#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace DTXMania.Game.Lib.Input.Midi;

public sealed class DryWetMidiDeviceBackend : IMidiDeviceBackend
{
    public IReadOnlyList<IMidiInputDevice> GetInputDevices()
    {
        return InputDevice.GetAll()
            .Select((device, index) => new DryWetMidiInputDevice(device, $"{device.Name}#{index}"))
            .ToArray();
    }

    private sealed class DryWetMidiInputDevice : IMidiInputDevice
    {
        private readonly InputDevice _device;
        private readonly string _stableId;
        private bool _disposed;

        public DryWetMidiInputDevice(InputDevice device, string stableId)
        {
            _device = device;
            _stableId = stableId;
        }

        public string Name => _device.Name;

        public string StableId => _stableId;

        public event EventHandler<MidiNoteEventArgs>? NoteReceived;

        public void Start()
        {
            if (_disposed)
                return;

            _device.EventReceived += OnEventReceived;
            _device.StartEventsListening();
        }

        public void Stop()
        {
            if (_disposed)
                return;

            _device.EventReceived -= OnEventReceived;
            _device.StopEventsListening();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                Stop();
            }
            catch
            {
                // Dispose still owns releasing the native MIDI device.
            }

            _device.Dispose();
            _disposed = true;
        }

        private void OnEventReceived(object? sender, MidiEventReceivedEventArgs e)
        {
            switch (e.Event)
            {
                case NoteOnEvent noteOn:
                    var noteOnVelocity = (int)noteOn.Velocity;
                    NoteReceived?.Invoke(
                        this,
                        new MidiNoteEventArgs(
                            StableId,
                            (int)noteOn.NoteNumber,
                            noteOnVelocity,
                            noteOnVelocity > 0));
                    break;
                case NoteOffEvent noteOff:
                    NoteReceived?.Invoke(
                        this,
                        new MidiNoteEventArgs(
                            StableId,
                            (int)noteOff.NoteNumber,
                            (int)noteOff.Velocity,
                            isPressed: false));
                    break;
            }
        }
    }
}
