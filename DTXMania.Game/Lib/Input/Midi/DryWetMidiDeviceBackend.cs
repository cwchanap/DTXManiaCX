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
        // NOTE: StableId is derived from the enumeration index of InputDevice.GetAll().
        // DryWetMidi's public InputDevice API does not expose a persistent identity
        // (manufacturer/product/serial), so a replug or device reorder produces a new
        // StableId for the same physical device. MidiInputSource.RefreshDevicesCore then
        // treats it as removed+added, briefly interrupting input and clearing any held
        // note state for that StableId. This is a known hot-plug UX limitation tracked
        // as a follow-up; acceptable for the first real-MIDI slice.
        return InputDevice.GetAll()
            .Select((device, index) => new DryWetMidiInputDevice(device, $"{device.Name}#{index}"))
            .ToArray();
    }

    private sealed class DryWetMidiInputDevice : IMidiInputDevice
    {
        private readonly InputDevice _device;
        private readonly string _stableId;
        private bool _listening;
        private bool _disposed;

        public DryWetMidiInputDevice(InputDevice device, string stableId)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _stableId = stableId;
        }

        public string Name => _device.Name;

        public string StableId => _stableId;

        public event EventHandler<MidiNoteEventArgs>? NoteReceived;

        public void Start()
        {
            if (_disposed || _listening)
                return;

            _device.EventReceived += OnEventReceived;
            try
            {
                _device.StartEventsListening();
                _listening = true;
            }
            catch
            {
                _device.EventReceived -= OnEventReceived;
                throw;
            }
        }

        public void Stop()
        {
            if (_disposed || !_listening)
                return;

            _device.EventReceived -= OnEventReceived;
            try
            {
                _device.StopEventsListening();
            }
            finally
            {
                _listening = false;
            }
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

            try
            {
                _device.Dispose();
            }
            finally
            {
                _listening = false;
                _disposed = true;
            }
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
