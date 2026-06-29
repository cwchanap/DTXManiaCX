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

            // MidiInputSource.StopAndDisposeDevice is the sole production disposer and
            // always calls Stop() first, logging any failure via its ILogger. We do not
            // re-Stop() here: (1) it would be a no-op on the production path (Stop()
            // early-returns once _listening is false), and (2) wrapping it in a bare
            // catch would silently swallow any native MidiDeviceException, making a
            // teardown failure indistinguishable from clean shutdown. Instead the
            // idempotent -= below detaches the handler for the direct-Dispose() case,
            // and DryWetMidi's InputDevice.Dispose() releases the native listener.
            _device.EventReceived -= OnEventReceived;

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
            if (TryConvertEvent(e.Event, StableId, out var args))
                NoteReceived?.Invoke(this, args!);
        }
    }

    /// <summary>
    /// Converts a DryWetMidi <see cref="MidiEvent"/> into a <see cref="MidiNoteEventArgs"/>
    /// for the given stable device id. NoteOn with velocity &gt; 0 reports a press; NoteOn with
    /// velocity 0 and NoteOff both report a release. Non-note events are ignored.
    /// Extracted from <see cref="DryWetMidiInputDevice.OnEventReceived"/> for unit testing.
    /// </summary>
    internal static bool TryConvertEvent(MidiEvent e, string stableId, out MidiNoteEventArgs? args)
    {
        switch (e)
        {
            case NoteOnEvent noteOn:
                var noteOnVelocity = (int)noteOn.Velocity;
                args = new MidiNoteEventArgs(
                    stableId,
                    (int)noteOn.NoteNumber,
                    noteOnVelocity,
                    noteOnVelocity > 0);
                return true;
            case NoteOffEvent noteOff:
                args = new MidiNoteEventArgs(
                    stableId,
                    (int)noteOff.NoteNumber,
                    (int)noteOff.Velocity,
                    isPressed: false);
                return true;
            default:
                args = default;
                return false;
        }
    }
}
