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
    private readonly object _refreshSync = new();
    private readonly IMidiDeviceBackend _backend;
    private readonly MidiVelocityFilter _velocityFilter;
    private readonly ILogger<MidiInputSource> _logger;
    private readonly Dictionary<string, IMidiInputDevice> _devices = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<QueuedMidiNote> _pendingNotes = new();
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

        while (_pendingNotes.TryDequeue(out var queuedNote))
        {
            ButtonState? state;
            lock (_sync)
            {
                if (_disposed)
                    yield break;

                state = ProcessNote(queuedNote);
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

    /// <summary>
    /// Clears any pending injected note events and accepted-press state so they cannot leak
    /// across stage transitions (e.g. a note injected near the end of one song replaying in the
    /// next). Called from <see cref="ModularInputManager.ClearInjectedState"/> alongside the
    /// keyboard/injected-button cleanup so all injected input types are reset consistently.
    /// </summary>
    public void ClearInjectedNotes()
    {
        while (_pendingNotes.TryDequeue(out _)) { }
        lock (_sync)
        {
            _acceptedPressedNotes.Clear();
        }
    }

    public void RefreshDevices()
    {
        lock (_refreshSync)
        {
            RefreshDevicesCore();
        }
    }

    private void RefreshDevicesCore()
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

        var discoveredDeviceGroups = GroupDevicesByStableId(discoveredDevices);
        List<IMidiInputDevice> devicesToRemove;
        var devicesToDispose = new List<IMidiInputDevice>();
        var devicesToStart = new List<(string StableId, IMidiInputDevice Device)>();

        lock (_sync)
        {
            if (_disposed)
                return;

            var discoveredStableIds = discoveredDeviceGroups.Keys.ToHashSet(StringComparer.Ordinal);
            var removedStableIds = _devices.Keys
                .Where(stableId => !discoveredStableIds.Contains(stableId))
                .ToList();

            devicesToRemove = removedStableIds
                .Select(stableId => _devices[stableId])
                .ToList();

            foreach (var stableId in removedStableIds)
            {
                _devices.Remove(stableId);
                _acceptedPressedNotes.RemoveWhere(key => key.DeviceStableId == stableId);
            }

            foreach (var (stableId, devices) in discoveredDeviceGroups)
            {
                if (_devices.TryGetValue(stableId, out var activeDevice))
                {
                    devicesToDispose.AddRange(devices.Where(device => !ReferenceEquals(device, activeDevice)));
                    continue;
                }

                devicesToStart.Add((stableId, devices[0]));
                devicesToDispose.AddRange(devices.Skip(1));
            }
        }

        foreach (var device in devicesToRemove)
            StopAndDisposeDevice(device);

        foreach (var device in devicesToDispose)
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

    private Dictionary<string, List<IMidiInputDevice>> GroupDevicesByStableId(IReadOnlyList<IMidiInputDevice> devices)
    {
        var groups = new Dictionary<string, List<IMidiInputDevice>>(StringComparer.Ordinal);

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.StableId))
            {
                StopAndDisposeDevice(device);
                continue;
            }

            if (!groups.TryGetValue(device.StableId, out var deviceGroup))
            {
                deviceGroup = new List<IMidiInputDevice>();
                groups.Add(device.StableId, deviceGroup);
            }

            deviceGroup.Add(device);
        }

        return groups;
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

    private ButtonState? ProcessNote(QueuedMidiNote queuedNote)
    {
        var note = queuedNote.Note;
        if (!_devices.TryGetValue(note.DeviceStableId, out var activeDevice) ||
            !ReferenceEquals(activeDevice, queuedNote.SourceDevice))
        {
            return null;
        }

        var key = new AcceptedNoteKey(note.DeviceStableId, note.NoteNumber);

        if (note.IsPressed)
        {
            if (!_velocityFilter.ShouldAcceptPress(note.NoteNumber, note.Velocity))
                return null;

            _acceptedPressedNotes.Add(key);
            // Clamp the raw velocity to the valid MIDI range [0,127] before normalizing so a
            // misbehaving/injected backend cannot produce a ButtonState.Velocity above 1.0f.
            var clampedVelocity = Math.Max(0, Math.Min(127, note.Velocity));
            return new ButtonState(KeyBindings.CreateMidiButtonId(note.NoteNumber), true, clampedVelocity / 127f);
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

        if (sender is not IMidiInputDevice sourceDevice)
            return;

        _pendingNotes.Enqueue(new QueuedMidiNote(sourceDevice, e));
    }

    private readonly record struct QueuedMidiNote(IMidiInputDevice SourceDevice, MidiNoteEventArgs Note);

    private readonly record struct AcceptedNoteKey(string DeviceStableId, int NoteNumber);
}
