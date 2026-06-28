#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Input.Midi;
using Xunit;

namespace DTXMania.Test.Input.Midi;

[Trait("Category", "Input")]
public sealed class MidiInputSourceTests
{
    [Fact]
    public void Initialize_NoDevices_IsUnavailableAndDoesNotThrow()
    {
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(), _ => 0);

        source.Initialize();

        Assert.False(source.IsAvailable);
        Assert.Empty(source.DeviceNames);
    }

    [Fact]
    public void Initialize_DeviceStartFailure_SkipsDeviceAndKeepsRunning()
    {
        var device = new FakeMidiInputDevice("Broken Device", "broken") { ThrowOnStart = true };
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);

        source.Initialize();

        Assert.False(source.IsAvailable);
        Assert.Empty(source.DeviceNames);
        Assert.Equal(1, device.StartCount);
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public void Update_NoteOnAccepted_ReturnsMidiButtonState()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();

        device.Emit(36, 85, isPressed: true);
        var states = source.Update().ToList();

        var state = Assert.Single(states);
        Assert.Equal("MIDI.36", state.Id);
        Assert.True(state.IsPressed);
        Assert.Equal(85f / 127f, state.Velocity);
    }

    [Theory]
    [InlineData(85)]
    [InlineData(84)]
    public void Update_NoteOnEqualOrBelowThreshold_ReturnsNoButtonState(int velocity)
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 85);
        source.Initialize();

        device.Emit(36, velocity, isPressed: true);
        var states = source.Update().ToList();

        Assert.Empty(states);
    }

    [Fact]
    public void Update_NoteOffAfterAcceptedPress_ReturnsRelease()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();
        device.Emit(36, 85, isPressed: true);
        Assert.Single(source.Update());

        device.Emit(36, 0, isPressed: false);
        var states = source.Update().ToList();

        var state = Assert.Single(states);
        Assert.Equal("MIDI.36", state.Id);
        Assert.False(state.IsPressed);
        Assert.Equal(0f, state.Velocity);
    }

    [Fact]
    public void Update_NoteOffAfterFilteredPress_ReturnsNoRelease()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 85);
        source.Initialize();
        device.Emit(36, 85, isPressed: true);
        Assert.Empty(source.Update());

        device.Emit(36, 0, isPressed: false);
        var states = source.Update().ToList();

        Assert.Empty(states);
    }

    [Fact]
    public void Update_SameNoteFromTwoDevices_UsesSingleDeviceAgnosticButtonId()
    {
        var first = new FakeMidiInputDevice("Kit A", "kit-a");
        var second = new FakeMidiInputDevice("Kit B", "kit-b");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(first, second), _ => 0);
        source.Initialize();

        first.Emit(36, 85, isPressed: true);
        second.Emit(36, 100, isPressed: true);
        var states = source.Update().ToList();

        Assert.Equal(new[] { "MIDI.36", "MIDI.36" }, states.Select(state => state.Id));
        Assert.All(states, state => Assert.True(state.IsPressed));
    }

    [Fact]
    public void Update_SameNoteFromTwoDevices_ReleasesOnlyAfterBothDevicesRelease()
    {
        var first = new FakeMidiInputDevice("Kit A", "kit-a");
        var second = new FakeMidiInputDevice("Kit B", "kit-b");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(first, second), _ => 0);
        source.Initialize();
        first.Emit(36, 85, isPressed: true);
        second.Emit(36, 100, isPressed: true);
        Assert.Equal(2, source.Update().Count());

        first.Emit(36, 0, isPressed: false);
        Assert.Empty(source.Update());

        second.Emit(36, 0, isPressed: false);
        var states = source.Update().ToList();

        var state = Assert.Single(states);
        Assert.Equal("MIDI.36", state.Id);
        Assert.False(state.IsPressed);
        Assert.Equal(0f, state.Velocity);
    }

    [Fact]
    public void RefreshDevices_AddsNewDeviceAndDisposesRemovedDevice()
    {
        var removed = new FakeMidiInputDevice("Old Kit", "old");
        var added = new FakeMidiInputDevice("New Kit", "new");
        var backend = new FakeMidiDeviceBackend(removed);
        using var source = new MidiInputSource(backend, _ => 0);
        source.Initialize();

        backend.SetDevices(added);
        source.RefreshDevices();

        Assert.Equal(new[] { "New Kit" }, source.DeviceNames);
        Assert.Equal(1, removed.StopCount);
        Assert.Equal(1, removed.DisposeCount);
        Assert.Equal(1, added.StartCount);
        Assert.Equal(0, added.DisposeCount);
    }

    [Fact]
    public void RefreshDevices_SameStableIdKeepsActiveDeviceAndDisposesNewUnusedDevice()
    {
        var active = new FakeMidiInputDevice("Active Kit", "d1");
        var unusedReplacement = new FakeMidiInputDevice("Replacement Kit", "d1");
        var backend = new FakeMidiDeviceBackend(active);
        using var source = new MidiInputSource(backend, _ => 0);
        source.Initialize();

        backend.SetDevices(unusedReplacement);
        source.RefreshDevices();

        Assert.Equal(new[] { "Active Kit" }, source.DeviceNames);
        Assert.Equal(1, active.StartCount);
        Assert.Equal(0, active.StopCount);
        Assert.Equal(0, active.DisposeCount);
        Assert.Equal(0, unusedReplacement.StartCount);
        Assert.Equal(1, unusedReplacement.DisposeCount);
    }

    [Fact]
    public void RefreshDevices_RemovingPressedDeviceClearsAcceptedState()
    {
        var removed = new FakeMidiInputDevice("Removed Kit", "removed");
        var remaining = new FakeMidiInputDevice("Remaining Kit", "remaining");
        var backend = new FakeMidiDeviceBackend(removed, remaining);
        using var source = new MidiInputSource(backend, _ => 0);
        source.Initialize();
        removed.Emit(36, 85, isPressed: true);
        remaining.Emit(36, 100, isPressed: true);
        Assert.Equal(2, source.Update().Count());

        backend.SetDevices(remaining);
        source.RefreshDevices();

        remaining.Emit(36, 0, isPressed: false);
        var states = source.Update().ToList();

        var state = Assert.Single(states);
        Assert.Equal("MIDI.36", state.Id);
        Assert.False(state.IsPressed);
        Assert.Equal(0f, state.Velocity);
    }

    [Fact]
    public void Dispose_StopsAndDisposesAllDevices()
    {
        var first = new FakeMidiInputDevice("Kit A", "kit-a");
        var second = new FakeMidiInputDevice("Kit B", "kit-b");
        var source = new MidiInputSource(new FakeMidiDeviceBackend(first, second), _ => 0);
        source.Initialize();

        source.Dispose();

        Assert.Equal(1, first.StopCount);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.StopCount);
        Assert.Equal(1, second.DisposeCount);
    }

    private sealed class FakeMidiDeviceBackend : IMidiDeviceBackend
    {
        private IReadOnlyList<IMidiInputDevice> _devices;

        public FakeMidiDeviceBackend(params IMidiInputDevice[] devices)
        {
            _devices = devices;
        }

        public IReadOnlyList<IMidiInputDevice> GetInputDevices()
        {
            return _devices;
        }

        public void SetDevices(params IMidiInputDevice[] devices)
        {
            _devices = devices;
        }
    }

    private sealed class FakeMidiInputDevice : IMidiInputDevice
    {
        public event EventHandler<MidiNoteEventArgs>? NoteReceived;

        public FakeMidiInputDevice(string name, string stableId)
        {
            Name = name;
            StableId = stableId;
        }

        public string Name { get; }
        public string StableId { get; }
        public bool ThrowOnStart { get; init; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void Start()
        {
            StartCount++;
            if (ThrowOnStart)
                throw new InvalidOperationException("Start failed.");
        }

        public void Stop()
        {
            StopCount++;
        }

        public void Emit(int noteNumber, int velocity, bool isPressed)
        {
            NoteReceived?.Invoke(this, new MidiNoteEventArgs(StableId, noteNumber, velocity, isPressed));
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
