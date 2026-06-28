#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTXMania.Game.Lib.Input.Midi;

public sealed class MidiInputSource : IInputSource
{
    private readonly object _sync = new();
    private readonly IMidiDeviceBackend _backend;
    private readonly MidiVelocityFilter _velocityFilter;
    private readonly ILogger<MidiInputSource> _logger;
    private readonly Dictionary<string, IMidiInputDevice> _devices = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<MidiNoteEventArgs> _pendingNotes = new();
    private readonly HashSet<AcceptedNoteKey> _acceptedPressedNotes = new();
    private bool _disposed;

    public MidiInputSource(
        IMidiDeviceBackend backend,
        Func<int, int> thresholdProvider,
        ILogger<MidiInputSource>? logger = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _velocityFilter = new MidiVelocityFilter(thresholdProvider ?? throw new ArgumentNullException(nameof(thresholdProvider)));
        _logger = logger ?? NullLogger<MidiInputSource>.Instance;
    }

    public string Name => "MIDI";

    public bool IsAvailable
    {
        get
        {
            lock (_sync)
            {
                return _devices.Count > 0;
            }
        }
    }

    public IReadOnlyList<string> DeviceNames
    {
        get
        {
            lock (_sync)
            {
                return _devices.Values
                    .Select(device => device.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            }
        }
    }

    public void Initialize()
    {
        if (_disposed)
            return;

        RefreshDevices();
    }

    public IEnumerable<ButtonState> Update()
    {
        if (_disposed)
            yield break;

        while (_pendingNotes.TryDequeue(out var note))
        {
            ButtonState? state;
            lock (_sync)
            {
                if (_disposed)
                    yield break;

                state = ProcessNote(note);
            }

            if (state != null)
                yield return state;
        }
    }

    public IEnumerable<ButtonState> GetPressedButtons()
    {
        if (_disposed)
            yield break;

        List<int> notes;
        lock (_sync)
        {
            notes = _acceptedPressedNotes
                .Select(key => key.NoteNumber)
                .Distinct()
                .OrderBy(note => note)
                .ToList();
        }

        foreach (var note in notes)
            yield return new ButtonState(KeyBindings.CreateMidiButtonId(note), true, 1.0f);
    }

    public void RefreshDevices()
    {
        if (_disposed)
            return;

        IReadOnlyList<IMidiInputDevice> discoveredDevices;
        try
        {
            discoveredDevices = _backend.GetInputDevices() ?? Array.Empty<IMidiInputDevice>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate MIDI input devices.");
            discoveredDevices = Array.Empty<IMidiInputDevice>();
        }

        var candidates = SelectDeviceCandidates(discoveredDevices);
        List<IMidiInputDevice> devicesToRemove;
        var devicesToStart = new List<(string StableId, IMidiInputDevice Device)>();

        lock (_sync)
        {
            if (_disposed)
                return;

            devicesToRemove = _devices
                .Where(pair => !candidates.TryGetValue(pair.Key, out var candidate) || !ReferenceEquals(pair.Value, candidate))
                .Select(pair => pair.Value)
                .ToList();

            foreach (var device in devicesToRemove)
            {
                var stableId = FindStableId(device);
                if (stableId != null)
                    _devices.Remove(stableId);
            }

            foreach (var (stableId, device) in candidates)
            {
                if (!_devices.ContainsKey(stableId))
                    devicesToStart.Add((stableId, device));
            }
        }

        foreach (var device in devicesToRemove)
            StopAndDisposeDevice(device);

        foreach (var (stableId, device) in devicesToStart)
            TryStartAndTrackDevice(stableId, device);
    }

    public void Dispose()
    {
        List<IMidiInputDevice> devices;
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            devices = _devices.Values.ToList();
            _devices.Clear();
            _acceptedPressedNotes.Clear();
        }

        while (_pendingNotes.TryDequeue(out _))
        {
        }

        foreach (var device in devices)
            StopAndDisposeDevice(device);

        GC.SuppressFinalize(this);
    }

    private Dictionary<string, IMidiInputDevice> SelectDeviceCandidates(IReadOnlyList<IMidiInputDevice> devices)
    {
        var candidates = new Dictionary<string, IMidiInputDevice>(StringComparer.Ordinal);

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.StableId))
            {
                StopAndDisposeDevice(device);
                continue;
            }

            if (candidates.TryAdd(device.StableId, device))
                continue;

            StopAndDisposeDevice(device);
        }

        return candidates;
    }

    private void TryStartAndTrackDevice(string stableId, IMidiInputDevice device)
    {
        try
        {
            device.NoteReceived += OnNoteReceived;
            device.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start MIDI input device {DeviceName}.", device.Name);
            device.NoteReceived -= OnNoteReceived;
            StopAndDisposeDevice(device);
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                device.NoteReceived -= OnNoteReceived;
                StopAndDisposeDevice(device);
                return;
            }

            _devices[stableId] = device;
        }
    }

    private void StopAndDisposeDevice(IMidiInputDevice device)
    {
        device.NoteReceived -= OnNoteReceived;

        try
        {
            device.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop MIDI input device {DeviceName}.", device.Name);
        }

        try
        {
            device.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose MIDI input device {DeviceName}.", device.Name);
        }
    }

    private ButtonState? ProcessNote(MidiNoteEventArgs note)
    {
        var key = new AcceptedNoteKey(note.DeviceStableId, note.NoteNumber);

        if (note.IsPressed)
        {
            if (!_velocityFilter.ShouldAcceptPress(note.NoteNumber, note.Velocity))
                return null;

            _acceptedPressedNotes.Add(key);
            return new ButtonState(KeyBindings.CreateMidiButtonId(note.NoteNumber), true, note.Velocity / 127f);
        }

        if (!_acceptedPressedNotes.Remove(key))
            return null;

        var noteStillPressed = _acceptedPressedNotes.Any(pressed => pressed.NoteNumber == note.NoteNumber);
        return noteStillPressed
            ? null
            : new ButtonState(KeyBindings.CreateMidiButtonId(note.NoteNumber), false, 0.0f);
    }

    private void OnNoteReceived(object? sender, MidiNoteEventArgs e)
    {
        if (_disposed)
            return;

        _pendingNotes.Enqueue(e);
    }

    private string? FindStableId(IMidiInputDevice device)
    {
        foreach (var (stableId, currentDevice) in _devices)
        {
            if (ReferenceEquals(currentDevice, device))
                return stableId;
        }

        return null;
    }

    private readonly record struct AcceptedNoteKey(string DeviceStableId, int NoteNumber);
}
