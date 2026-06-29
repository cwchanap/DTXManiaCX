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

    [Fact]
    public void Update_NoteOnWithOutOfRangeVelocity_ClampsToUnitRange()
    {
        // A misbehaving/injected backend could supply a velocity above the MIDI max (127). The
        // end-to-end guarantee is that ButtonState.Velocity never exceeds 1.0f. This invariant is
        // enforced at the MidiNoteEventArgs boundary (its constructor clamps velocity to [0,127]),
        // with ProcessNote applying an additional defensive clamp before normalizing. This test
        // verifies the invariant holds at the source's public Update() surface regardless of which
        // layer performs the clamp; note that because MidiNoteEventArgs is sealed and clamps in its
        // constructor, the ProcessNote clamp is provably unreachable for out-of-range values and
        // exists purely as defense-in-depth.
        var device = new FakeMidiInputDevice("Kit", "kit");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();

        device.Emit(36, 200, isPressed: true);
        var states = source.Update().ToList();

        var state = Assert.Single(states);
        Assert.Equal("MIDI.36", state.Id);
        Assert.True(state.IsPressed);
        Assert.Equal(1.0f, state.Velocity);
    }

    [Fact]
    public void ClearInjectedNotes_DropsPendingAndAcceptedState()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();

        // Queue a press but do NOT Update yet, so it sits in the pending queue.
        device.Emit(36, 85, isPressed: true);

        source.ClearInjectedNotes();

        // Pending note is gone -> Update produces nothing.
        Assert.Empty(source.Update().ToList());
        // Accepted-press set is also cleared -> GetPressedButtons is empty.
        Assert.Empty(source.GetPressedButtons());
    }

    [Fact]
    public void ClearInjectedNotes_AfterAcceptedPress_AllowsRePress()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();
        device.Emit(36, 85, isPressed: true);
        Assert.Single(source.Update()); // accept the press

        // After clearing, a release for the same note should produce nothing (no accepted state),
        // confirming ClearInjectedNotes reset the accepted-press tracking.
        source.ClearInjectedNotes();
        device.Emit(36, 0, isPressed: false);

        Assert.Empty(source.Update().ToList());
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
    public void RefreshDevices_RemovingDeviceBeforeUpdateDropsQueuedPress()
    {
        var removed = new FakeMidiInputDevice("Removed Kit", "removed");
        var backend = new FakeMidiDeviceBackend(removed);
        using var source = new MidiInputSource(backend, _ => 0);
        source.Initialize();
        removed.Emit(36, 85, isPressed: true);

        backend.SetDevices();
        source.RefreshDevices();

        Assert.Empty(source.Update());
        Assert.Empty(source.GetPressedButtons());
    }

    [Fact]
    public void RefreshDevices_ReplacingDeviceWithSameStableIdDropsOldQueuedPress()
    {
        var removed = new FakeMidiInputDevice("Old Kit", "same");
        var replacement = new FakeMidiInputDevice("New Kit", "same");
        var backend = new FakeMidiDeviceBackend(removed);
        using var source = new MidiInputSource(backend, _ => 0);
        source.Initialize();
        removed.Emit(36, 85, isPressed: true);

        backend.SetDevices();
        source.RefreshDevices();
        backend.SetDevices(replacement);
        source.RefreshDevices();

        Assert.Empty(source.Update());
        Assert.Empty(source.GetPressedButtons());
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

    // ---- Edge cases: backend enumeration failures ----

    [Fact]
    public void Initialize_BackendThrowsOnEnumeration_ContinuesWithNoDevices()
    {
        var backend = new FakeMidiDeviceBackend { ThrowOnGetDevices = true };
        using var source = new MidiInputSource(backend, _ => 0);

        source.Initialize();

        Assert.False(source.IsAvailable);
        Assert.Empty(source.DeviceNames);
    }

    [Fact]
    public void RefreshDevices_BackendThrowsOnEnumeration_RemovesAllDevices()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        var backend = new FakeMidiDeviceBackend(device);
        using var source = new MidiInputSource(backend, _ => 0);
        source.Initialize();
        Assert.True(source.IsAvailable);

        backend.ThrowOnGetDevices = true;
        source.RefreshDevices();

        // When enumeration fails, the discovered list is empty, so all existing devices
        // are removed. This is by design — a failed enumeration is treated as "no devices."
        Assert.False(source.IsAvailable);
        Assert.Empty(source.DeviceNames);
    }

    // ---- Edge cases: device lifecycle failures ----

    [Fact]
    public void RefreshDevices_RemovedDeviceStopThrows_StillDisposesAndContinues()
    {
        var removed = new FakeMidiInputDevice("Old Kit", "old") { ThrowOnStop = true };
        var added = new FakeMidiInputDevice("New Kit", "new");
        var backend = new FakeMidiDeviceBackend(removed);
        using var source = new MidiInputSource(backend, _ => 0);
        source.Initialize();

        backend.SetDevices(added);
        source.RefreshDevices();

        Assert.Equal(new[] { "New Kit" }, source.DeviceNames);
        Assert.Equal(1, removed.StopCount);
        Assert.Equal(1, removed.DisposeCount);
    }

    [Fact]
    public void RefreshDevices_RemovedDeviceDisposeThrows_ContinuesWithoutThrowing()
    {
        var removed = new FakeMidiInputDevice("Old Kit", "old") { ThrowOnDispose = true };
        var added = new FakeMidiInputDevice("New Kit", "new");
        var backend = new FakeMidiDeviceBackend(removed);
        using var source = new MidiInputSource(backend, _ => 0);
        source.Initialize();

        backend.SetDevices(added);
        source.RefreshDevices();

        Assert.Equal(new[] { "New Kit" }, source.DeviceNames);
        Assert.Equal(1, removed.DisposeCount);
    }

    // ---- Edge cases: empty/whitespace StableId ----

    [Fact]
    public void Initialize_DeviceWithEmptyStableId_IsDisposedAndNotTracked()
    {
        var device = new FakeMidiInputDevice("NoId", "  ");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);

        source.Initialize();

        Assert.False(source.IsAvailable);
        Assert.Equal(1, device.DisposeCount);
    }

    // ---- Edge cases: disposed source ----

    [Fact]
    public void Update_AfterDispose_ReturnsEmpty()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();
        source.Dispose();

        Assert.Empty(source.Update());
    }

    [Fact]
    public void GetPressedButtons_AfterDispose_ReturnsEmpty()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();
        device.Emit(36, 85, isPressed: true);
        source.Update().ToList(); // Process the note so it's in _acceptedPressedNotes.
        source.Dispose();

        Assert.Empty(source.GetPressedButtons());
    }

    [Fact]
    public void RefreshDevices_AfterDispose_IsNoOp()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();
        source.Dispose();

        source.RefreshDevices();

        // Device was already disposed by Dispose(); RefreshDevices should not re-access it.
        Assert.Equal(1, device.DisposeCount);
    }

    [Fact]
    public void Initialize_AfterDispose_IsNoOp()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();
        source.Dispose();

        source.Initialize();

        Assert.False(source.IsAvailable);
    }

    // ---- Edge cases: OnNoteReceived ----

    [Fact]
    public void OnNoteReceived_FromNonDeviceSender_NoteIsDropped()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();

        // Simulate a callback where sender is not an IMidiInputDevice (should be ignored).
        device.EmitFromWrongSender(36, 85, isPressed: true);

        Assert.Empty(source.Update());
    }

    [Fact]
    public void OnNoteReceived_AfterDispose_NoteIsDropped()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();
        source.Dispose();

        // Emit after dispose — OnNoteReceived should bail early.
        device.Emit(36, 85, isPressed: true);

        // Re-create a new source to verify nothing leaked (the disposed source's queue is drained).
        // The disposed source's Update() returns empty.
        Assert.Empty(source.Update());
    }

    // ---- Edge cases: ProcessNote device mismatch ----
    // (Already covered by RefreshDevices_ReplacingDeviceWithSameStableIdDropsOldQueuedPress above,
    // which verifies that queued notes from a replaced device are dropped during Update.)

    // ---- GetPressedButtons returns accepted pressed notes ----

    [Fact]
    public void GetPressedButtons_AfterAcceptedPress_ReturnsPressedNote()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();
        device.Emit(36, 85, isPressed: true);
        source.Update().ToList(); // Force enumeration to process the queued note.

        var pressed = source.GetPressedButtons().ToList();

        var state = Assert.Single(pressed);
        Assert.Equal("MIDI.36", state.Id);
        Assert.True(state.IsPressed);
    }

    [Fact]
    public void GetPressedButtons_AfterPressAndRelease_ReturnsEmpty()
    {
        var device = new FakeMidiInputDevice("Kit", "kit");
        using var source = new MidiInputSource(new FakeMidiDeviceBackend(device), _ => 0);
        source.Initialize();
        device.Emit(36, 85, isPressed: true);
        source.Update().ToList();
        device.Emit(36, 0, isPressed: false);
        source.Update().ToList();

        Assert.Empty(source.GetPressedButtons());
    }

    // ---- Constructor null-argument guards ----

    [Fact]
    public void Constructor_NullBackend_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MidiInputSource(null!, _ => 0));
    }

    [Fact]
    public void Constructor_NullThresholdProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MidiInputSource(new FakeMidiDeviceBackend(), null!));
    }

    private sealed class FakeMidiDeviceBackend : IMidiDeviceBackend
    {
        private IReadOnlyList<IMidiInputDevice> _devices;

        public FakeMidiDeviceBackend(params IMidiInputDevice[] devices)
        {
            _devices = devices;
        }

        public bool ThrowOnGetDevices { get; set; }

        public IReadOnlyList<IMidiInputDevice> GetInputDevices()
        {
            if (ThrowOnGetDevices)
                throw new InvalidOperationException("Enumeration failed.");
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
        public bool ThrowOnStop { get; init; }
        public bool ThrowOnDispose { get; init; }
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
            if (ThrowOnStop)
                throw new InvalidOperationException("Stop failed.");
        }

        public void Emit(int noteNumber, int velocity, bool isPressed)
        {
            NoteReceived?.Invoke(this, new MidiNoteEventArgs(StableId, noteNumber, velocity, isPressed));
        }

        public void EmitFromWrongSender(int noteNumber, int velocity, bool isPressed)
        {
            NoteReceived?.Invoke("not-a-device", new MidiNoteEventArgs(StableId, noteNumber, velocity, isPressed));
        }

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose)
                throw new InvalidOperationException("Dispose failed.");
        }
    }
}
