#nullable enable

using System.Linq;
using DTXMania.Game.Lib.Input.Midi;
using Melanchall.DryWetMidi.Core;
using Xunit;
using SevenBitNumber = Melanchall.DryWetMidi.Common.SevenBitNumber;

namespace DTXMania.Test.Input.Midi;

[Trait("Category", "Input")]
public sealed class DryWetMidiDeviceBackendTests
{
    // GetInputDevices() is a thin wrapper around DryWetMIDI's InputDevice.GetAll() and
    // is intentionally not unit-tested here: it probes real host MIDI hardware (CoreMIDI
    // on macOS, WinMM on Windows), which can throw on headless/sandboxed CI runners where
    // the MIDI subsystem is unavailable. The design doc's testing strategy explicitly
    // requires tests to avoid probing host hardware. Coverage of device enumeration lives
    // in the manual/integration verification path (see design doc "Integration/Manual
    // Verification"). The pure conversion logic below is the unit-testable surface.

    [Fact]
    public void TryConvertEvent_NoteOnWithPositiveVelocity_ReturnsPressedNote()
    {
        var noteOn = new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)85);

        var result = DryWetMidiDeviceBackend.TryConvertEvent(noteOn, "dev#0", out var args);

        Assert.True(result);
        Assert.Equal("dev#0", args.DeviceStableId);
        Assert.Equal(36, args.NoteNumber);
        Assert.Equal(85, args.Velocity);
        Assert.True(args.IsPressed);
    }

    [Fact]
    public void TryConvertEvent_NoteOnWithZeroVelocity_ReturnsReleasedNote()
    {
        // NoteOn with velocity 0 is a common "note off" convention in MIDI.
        var noteOn = new NoteOnEvent((SevenBitNumber)42, (SevenBitNumber)0);

        var result = DryWetMidiDeviceBackend.TryConvertEvent(noteOn, "dev#0", out var args);

        Assert.True(result);
        Assert.Equal(42, args.NoteNumber);
        Assert.Equal(0, args.Velocity);
        Assert.False(args.IsPressed);
    }

    [Fact]
    public void TryConvertEvent_NoteOff_ReturnsReleasedNote()
    {
        var noteOff = new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)64);

        var result = DryWetMidiDeviceBackend.TryConvertEvent(noteOff, "dev#1", out var args);

        Assert.True(result);
        Assert.Equal("dev#1", args.DeviceStableId);
        Assert.Equal(60, args.NoteNumber);
        Assert.Equal(64, args.Velocity);
        Assert.False(args.IsPressed);
    }

    [Fact]
    public void TryConvertEvent_NonNoteEvent_ReturnsFalse()
    {
        var controlChange = new ControlChangeEvent((SevenBitNumber)0, (SevenBitNumber)0);

        var result = DryWetMidiDeviceBackend.TryConvertEvent(controlChange, "dev#0", out var args);

        Assert.False(result);
        Assert.Equal(default(MidiNoteEventArgs), args);
    }

    [Fact]
    public void TryConvertEvent_ProgramChangeEvent_ReturnsFalse()
    {
        var programChange = new ProgramChangeEvent((SevenBitNumber)0);

        var result = DryWetMidiDeviceBackend.TryConvertEvent(programChange, "dev#0", out var args);

        Assert.False(result);
    }
}
