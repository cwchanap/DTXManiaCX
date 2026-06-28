#nullable enable

using System;
using DTXMania.Game.Lib.Input.Midi;
using Xunit;

namespace DTXMania.Test.Input.Midi;

[Trait("Category", "Input")]
public sealed class SimulatedMidiDeviceBackendTests
{
    [Fact]
    public void TryInjectNote_WithValidNoteAndVelocity_ReturnsTrueAfterStart()
    {
        var backend = new SimulatedMidiDeviceBackend();
        // GetInputDevices returns the simulated device; start it via MidiInputSource flow.
        var device = backend.GetInputDevices()[0];
        device.Start();

        var result = backend.TryInjectNote(36, 100, isPressed: true);

        Assert.True(result);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(128, 100)]
    public void TryInjectNote_WithOutOfRangeNoteNumber_ReturnsFalse(int noteNumber, int velocity)
    {
        var backend = new SimulatedMidiDeviceBackend();
        var device = backend.GetInputDevices()[0];
        device.Start();

        var result = backend.TryInjectNote(noteNumber, velocity, isPressed: true);

        Assert.False(result);
    }

    [Theory]
    [InlineData(36, -1)]
    [InlineData(36, 128)]
    public void TryInjectNote_WithOutOfRangeVelocity_ReturnsFalse(int noteNumber, int velocity)
    {
        var backend = new SimulatedMidiDeviceBackend();
        var device = backend.GetInputDevices()[0];
        device.Start();

        var result = backend.TryInjectNote(noteNumber, velocity, isPressed: true);

        Assert.False(result);
    }

    [Fact]
    public void TryInjectNote_BeforeStart_ReturnsFalse()
    {
        var backend = new SimulatedMidiDeviceBackend();
        // Device is not started yet — Emit returns false.

        var result = backend.TryInjectNote(36, 100, isPressed: true);

        Assert.False(result);
    }

    [Fact]
    public void TryInjectNote_AfterStop_ReturnsFalse()
    {
        var backend = new SimulatedMidiDeviceBackend();
        var device = backend.GetInputDevices()[0];
        device.Start();
        device.Stop();

        var result = backend.TryInjectNote(36, 100, isPressed: true);

        Assert.False(result);
    }

    [Fact]
    public void TryInjectNote_AfterDispose_ReturnsFalse()
    {
        var backend = new SimulatedMidiDeviceBackend();
        var device = backend.GetInputDevices()[0];
        device.Start();
        device.Dispose();

        var result = backend.TryInjectNote(36, 100, isPressed: true);

        Assert.False(result);
    }

    [Fact]
    public void TryInjectNote_WithSubscriber_RaisesNoteReceivedEvent()
    {
        var backend = new SimulatedMidiDeviceBackend();
        var device = backend.GetInputDevices()[0];
        device.Start();

        MidiNoteEventArgs? received = null;
        device.NoteReceived += (_, e) => received = e;

        backend.TryInjectNote(42, 80, isPressed: true);

        Assert.NotNull(received);
        Assert.Equal(42, received!.NoteNumber);
        Assert.Equal(80, received.Velocity);
        Assert.True(received.IsPressed);
    }

    [Fact]
    public void TryInjectNote_NoteOff_RaisesReleasedEvent()
    {
        var backend = new SimulatedMidiDeviceBackend();
        var device = backend.GetInputDevices()[0];
        device.Start();

        MidiNoteEventArgs? received = null;
        device.NoteReceived += (_, e) => received = e;

        backend.TryInjectNote(42, 0, isPressed: false);

        Assert.NotNull(received);
        Assert.False(received!.IsPressed);
    }

    [Fact]
    public void Start_AfterDispose_DoesNotRestart()
    {
        var backend = new SimulatedMidiDeviceBackend();
        var device = backend.GetInputDevices()[0];
        device.Start();
        device.Dispose();

        // Start after dispose should be a no-op (device stays disposed).
        device.Start();

        Assert.False(backend.TryInjectNote(36, 100, isPressed: true));
    }

    [Fact]
    public void GetInputDevices_AlwaysReturnsSingleDevice()
    {
        var backend = new SimulatedMidiDeviceBackend();

        var devices = backend.GetInputDevices();

        Assert.Single(devices);
        Assert.Equal("Simulated MIDI Device", devices[0].Name);
        Assert.Equal("simulated-midi-device", devices[0].StableId);
    }
}
