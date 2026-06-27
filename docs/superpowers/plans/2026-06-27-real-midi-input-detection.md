# Real MIDI Input Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add real MIDI input from connected devices to gameplay and drum mapping, with device-agnostic `MIDI.<note>` bindings and configurable per-note velocity thresholds.

**Architecture:** MIDI enters the existing input pipeline as another `IInputSource`. DryWetMIDI is isolated behind a backend adapter, while `MidiInputSource` drains backend callbacks on the game update thread, applies config-backed velocity thresholds, and emits `ButtonState` objects to the existing `InputRouter`.

**Tech Stack:** .NET 8, MonoGame, xUnit, Moq, DryWetMIDI `Melanchall.DryWetMidi`, existing DTXManiaCX `ConfigManager`/`ModularInputManager` input architecture.

---

## File Structure

- Create `DTXMania.Game/Lib/Input/Midi/MidiNoteEventArgs.cs`: backend-independent note event model including device stable ID, note, velocity, and pressed/released state.
- Create `DTXMania.Game/Lib/Input/Midi/IMidiInputDevice.cs`: disposable input-device abstraction.
- Create `DTXMania.Game/Lib/Input/Midi/IMidiDeviceBackend.cs`: backend abstraction used by `MidiInputSource`.
- Create `DTXMania.Game/Lib/Input/Midi/MidiVelocityFilter.cs`: pure config-backed threshold logic.
- Create `DTXMania.Game/Lib/Input/Midi/MidiInputSource.cs`: `IInputSource` implementation that owns device lifecycle, queue draining, filtering, and device refresh.
- Create `DTXMania.Game/Lib/Input/Midi/DryWetMidiDeviceBackend.cs`: DryWetMIDI adapter for real OS MIDI devices.
- Modify `DTXMania.Game/Lib/Input/KeyBindings.cs`: add `TryParseMidiButtonId`.
- Modify `DTXMania.Game/Lib/Config/ConfigData.cs`: add `MidiVelocityThresholds`.
- Modify `DTXMania.Game/Lib/Config/IConfigManager.cs`: expose MIDI velocity getter/setter.
- Modify `DTXMania.Game/Lib/Config/ConfigManager.cs`: parse, save, clamp, remove, and dirty-track velocity thresholds.
- Modify `DTXMania.Game/Lib/Input/ModularInputManager.cs`: add MIDI source, fix double initialization, refresh MIDI devices during scan, update diagnostics.
- Modify `DTXMania.Game/Lib/Stage/DrumConfig/DrumCapturePopup.cs`: expose MIDI threshold display and hit rectangles for MIDI binding chips.
- Modify `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`: apply threshold increment/decrement edits through `ConfigManager`.
- Modify `DTXMania.Game/DTXMania.Game.Mac.csproj` and `DTXMania.Game/DTXMania.Game.Windows.csproj`: add `Melanchall.DryWetMidi` package.
- Create `DTXMania.Test/Input/Midi/MidiVelocityFilterTests.cs`: pure threshold tests.
- Create `DTXMania.Test/Input/Midi/MidiInputSourceTests.cs`: fake backend/device tests.
- Modify `DTXMania.Test/Config/ConfigManagerTests.cs`: threshold persistence tests.
- Modify `DTXMania.Test/Input/InputRouterTests.cs`: `MIDI.36` route test.
- Modify `DTXMania.Test/Input/KeyBindingsTests.cs`: MIDI button parsing tests.
- Modify `DTXMania.Test/Input/ModularInputManagerTests.cs`: source count/diagnostic/refresh behavior tests.
- Modify `DTXMania.Test/Stage/DrumConfig/DrumCapturePopupTests.cs`: threshold chip geometry/display tests.
- Modify `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`: threshold edit helper tests.

## Task 1: Config-Backed MIDI Velocity Thresholds

**Files:**
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Test: `DTXMania.Test/Config/ConfigManagerTests.cs`

- [ ] **Step 1: Write failing config tests**

Append these tests to `DTXMania.Test/Config/ConfigManagerTests.cs`:

```csharp
[Fact]
public void ConfigManager_SaveAndLoadConfig_MidiVelocityThresholds_ShouldPreserveNonzeroThresholds()
{
    var manager = new ConfigManager();
    manager.SetMidiVelocityThreshold(36, 20);
    manager.SetMidiVelocityThreshold(38, 12);

    var tempFile = Path.GetTempFileName();
    try
    {
        manager.SaveConfig(tempFile);
        var text = File.ReadAllText(tempFile);
        Assert.Contains("[MidiVelocityThresholds]", text);
        Assert.Contains("MidiVelocity.36=20", text);
        Assert.Contains("MidiVelocity.38=12", text);

        var reloaded = new ConfigManager();
        reloaded.LoadConfig(tempFile);

        Assert.Equal(20, reloaded.GetMidiVelocityThreshold(36));
        Assert.Equal(12, reloaded.GetMidiVelocityThreshold(38));
        Assert.Equal(0, reloaded.GetMidiVelocityThreshold(40));
    }
    finally
    {
        File.Delete(tempFile);
    }
}

[Fact]
public void ConfigManager_SetMidiVelocityThreshold_Zero_ShouldRemovePersistedThreshold()
{
    var manager = new ConfigManager();
    manager.SetMidiVelocityThreshold(36, 20);
    manager.SetMidiVelocityThreshold(36, 0);

    Assert.Equal(0, manager.GetMidiVelocityThreshold(36));
    Assert.False(manager.Config.MidiVelocityThresholds.ContainsKey(36));

    var tempFile = Path.GetTempFileName();
    try
    {
        manager.SaveConfig(tempFile);
        var text = File.ReadAllText(tempFile);
        Assert.DoesNotContain("MidiVelocity.36=", text);
    }
    finally
    {
        File.Delete(tempFile);
    }
}

[Fact]
public void ConfigManager_LoadConfig_InvalidMidiVelocityThresholds_ShouldIgnoreOrClamp()
{
    var tempFile = Path.GetTempFileName();
    File.WriteAllText(tempFile, string.Join(Environment.NewLine, new[]
    {
        "[MidiVelocityThresholds]",
        "MidiVelocity.36=300",
        "MidiVelocity.38=-4",
        "MidiVelocity.200=50",
        "MidiVelocity.bad=40",
        "MidiVelocity.40=abc"
    }));

    try
    {
        var manager = new ConfigManager();
        manager.LoadConfig(tempFile);

        Assert.Equal(127, manager.GetMidiVelocityThreshold(36));
        Assert.Equal(0, manager.GetMidiVelocityThreshold(38));
        Assert.Equal(0, manager.GetMidiVelocityThreshold(200));
        Assert.Equal(0, manager.GetMidiVelocityThreshold(40));
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

- [ ] **Step 2: Run config tests and verify failure**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerTests"
```

Expected: FAIL because `ConfigData.MidiVelocityThresholds`, `GetMidiVelocityThreshold`, and `SetMidiVelocityThreshold` do not exist.

- [ ] **Step 3: Add `ConfigData` storage**

In `DTXMania.Game/Lib/Config/ConfigData.cs`, add this property after `SystemKeyBindings`:

```csharp
public Dictionary<int, int> MidiVelocityThresholds { get; set; } = new();
```

- [ ] **Step 4: Add config interface methods**

In `DTXMania.Game/Lib/Config/IConfigManager.cs`, add these methods after `SetSystemKeyBindings`:

```csharp
/// <summary>Gets the MIDI minimum velocity threshold for a note. Missing notes default to 0.</summary>
int GetMidiVelocityThreshold(int noteNumber);

/// <summary>Sets a MIDI minimum velocity threshold, clamped to 0..127, and marks config dirty.</summary>
void SetMidiVelocityThreshold(int noteNumber, int threshold);
```

- [ ] **Step 5: Implement parsing, saving, and setters**

In `DTXMania.Game/Lib/Config/ConfigManager.cs`, add constants near the class fields:

```csharp
private const string MidiVelocityPrefix = "MidiVelocity.";
```

Add this branch near the top of the `default:` block in `ParseConfigLine`, before key-binding parsing:

```csharp
if (TryParseMidiVelocityThresholdKey(key, out var midiNoteNumber))
{
    if (int.TryParse(value, out var midiThreshold))
    {
        SetMidiVelocityThresholdInMemory(midiNoteNumber, midiThreshold);
    }
}
else if (key.StartsWith("Key.Unbound.") &&
         int.TryParse(key.Substring("Key.Unbound.".Length), out var unboundLane))
{
    if (unboundLane >= 0 && unboundLane <= 9 &&
        TryParseBool(value, out var isUnbound) &&
        isUnbound)
    {
        Config.UnboundDrumLanes.Add(unboundLane);
    }
}
```

Keep the existing `Key.Unbound`, `Key.UnboundButton`, button binding, and `SystemKey` branches after this new first branch.

In `SaveConfig`, after the `[SystemKeyBindings]` block and before the atomic write, add:

```csharp
var savedMidiThresholds = Config.MidiVelocityThresholds
    .Where(kvp => kvp.Key >= 0 && kvp.Key <= 127 && kvp.Value > 0)
    .OrderBy(kvp => kvp.Key)
    .ToList();
if (savedMidiThresholds.Count > 0)
{
    sb.AppendLine();
    sb.AppendLine("[MidiVelocityThresholds]");
    foreach (var kvp in savedMidiThresholds)
    {
        sb.AppendLine($"{MidiVelocityPrefix}{kvp.Key}={Math.Clamp(kvp.Value, 0, 127)}");
    }
}
```

Add these methods near the other public setters:

```csharp
public int GetMidiVelocityThreshold(int noteNumber)
{
    if (noteNumber < 0 || noteNumber > 127)
        return 0;

    return Config.MidiVelocityThresholds.TryGetValue(noteNumber, out var threshold)
        ? Math.Clamp(threshold, 0, 127)
        : 0;
}

public void SetMidiVelocityThreshold(int noteNumber, int threshold)
{
    if (noteNumber < 0 || noteNumber > 127)
        return;

    SetMidiVelocityThresholdInMemory(noteNumber, threshold);
    MarkDirty();
}

private void SetMidiVelocityThresholdInMemory(int noteNumber, int threshold)
{
    if (noteNumber < 0 || noteNumber > 127)
        return;

    var clamped = Math.Clamp(threshold, 0, 127);
    if (clamped == 0)
    {
        Config.MidiVelocityThresholds.Remove(noteNumber);
        return;
    }

    Config.MidiVelocityThresholds[noteNumber] = clamped;
}

private static bool TryParseMidiVelocityThresholdKey(string key, out int noteNumber)
{
    noteNumber = default;
    if (string.IsNullOrWhiteSpace(key) ||
        !key.StartsWith(MidiVelocityPrefix, StringComparison.Ordinal) ||
        key.Length <= MidiVelocityPrefix.Length)
    {
        return false;
    }

    return int.TryParse(key.Substring(MidiVelocityPrefix.Length), out noteNumber) &&
           noteNumber >= 0 &&
           noteNumber <= 127;
}
```

- [ ] **Step 6: Run config tests and verify pass**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
rtk git add DTXMania.Game/Lib/Config/ConfigData.cs DTXMania.Game/Lib/Config/IConfigManager.cs DTXMania.Game/Lib/Config/ConfigManager.cs DTXMania.Test/Config/ConfigManagerTests.cs
rtk git commit -m "feat: persist midi velocity thresholds"
```

## Task 2: MIDI Button Parsing Helper

**Files:**
- Modify: `DTXMania.Game/Lib/Input/KeyBindings.cs`
- Test: `DTXMania.Test/Input/KeyBindingsTests.cs`

- [ ] **Step 1: Write failing parsing tests**

Append these tests to `DTXMania.Test/Input/KeyBindingsTests.cs`:

```csharp
[Theory]
[InlineData("MIDI.0", 0)]
[InlineData("MIDI.36", 36)]
[InlineData("MIDI.127", 127)]
public void TryParseMidiButtonId_ValidId_ShouldReturnNoteNumber(string buttonId, int expected)
{
    Assert.True(KeyBindings.TryParseMidiButtonId(buttonId, out var noteNumber));
    Assert.Equal(expected, noteNumber);
}

[Theory]
[InlineData("")]
[InlineData("MIDI.")]
[InlineData("MIDI.bad")]
[InlineData("MIDI.-1")]
[InlineData("MIDI.128")]
[InlineData("Key.A")]
[InlineData("Pad.A")]
public void TryParseMidiButtonId_InvalidId_ShouldReturnFalse(string buttonId)
{
    Assert.False(KeyBindings.TryParseMidiButtonId(buttonId, out var noteNumber));
    Assert.Equal(0, noteNumber);
}
```

- [ ] **Step 2: Run helper tests and verify failure**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~KeyBindingsTests"
```

Expected: FAIL because `TryParseMidiButtonId` does not exist.

- [ ] **Step 3: Implement helper**

In `DTXMania.Game/Lib/Input/KeyBindings.cs`, add this method near `CreateMidiButtonId`:

```csharp
public static bool TryParseMidiButtonId(string buttonId, out int noteNumber)
{
    noteNumber = default;
    const string prefix = "MIDI.";
    if (string.IsNullOrWhiteSpace(buttonId) ||
        !buttonId.StartsWith(prefix, StringComparison.Ordinal) ||
        buttonId.Length <= prefix.Length)
    {
        return false;
    }

    return int.TryParse(buttonId.Substring(prefix.Length), out noteNumber) &&
           noteNumber >= 0 &&
           noteNumber <= 127;
}
```

- [ ] **Step 4: Run helper tests and verify pass**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~KeyBindingsTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
rtk git add DTXMania.Game/Lib/Input/KeyBindings.cs DTXMania.Test/Input/KeyBindingsTests.cs
rtk git commit -m "feat: parse midi button ids"
```

## Task 3: Pure MIDI Velocity Filter

**Files:**
- Create: `DTXMania.Game/Lib/Input/Midi/MidiVelocityFilter.cs`
- Test: `DTXMania.Test/Input/Midi/MidiVelocityFilterTests.cs`

- [ ] **Step 1: Write failing filter tests**

Create `DTXMania.Test/Input/Midi/MidiVelocityFilterTests.cs`:

```csharp
using DTXMania.Game.Lib.Input.Midi;
using Xunit;

namespace DTXMania.Test.Input.Midi;

[Trait("Category", "Input")]
public sealed class MidiVelocityFilterTests
{
    [Fact]
    public void ShouldAcceptPress_DefaultThreshold_AllowsVelocityOne()
    {
        var filter = new MidiVelocityFilter(_ => 0);

        Assert.True(filter.ShouldAcceptPress(36, 1));
    }

    [Fact]
    public void ShouldAcceptPress_ZeroVelocity_IsRejectedAsPress()
    {
        var filter = new MidiVelocityFilter(_ => 0);

        Assert.False(filter.ShouldAcceptPress(36, 0));
    }

    [Fact]
    public void ShouldAcceptPress_VelocityEqualThreshold_IsRejected()
    {
        var filter = new MidiVelocityFilter(_ => 20);

        Assert.False(filter.ShouldAcceptPress(36, 20));
    }

    [Fact]
    public void ShouldAcceptPress_VelocityAboveThreshold_IsAccepted()
    {
        var filter = new MidiVelocityFilter(_ => 20);

        Assert.True(filter.ShouldAcceptPress(36, 21));
    }

    [Fact]
    public void ShouldAcceptPress_ThresholdProviderOutOfRange_IsClamped()
    {
        var high = new MidiVelocityFilter(_ => 300);
        var low = new MidiVelocityFilter(_ => -10);

        Assert.False(high.ShouldAcceptPress(36, 127));
        Assert.True(low.ShouldAcceptPress(36, 1));
    }

    [Fact]
    public void ShouldAcceptPress_InvalidNoteNumber_IsRejected()
    {
        var filter = new MidiVelocityFilter(_ => 0);

        Assert.False(filter.ShouldAcceptPress(-1, 100));
        Assert.False(filter.ShouldAcceptPress(128, 100));
    }
}
```

- [ ] **Step 2: Run filter tests and verify failure**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~MidiVelocityFilterTests"
```

Expected: FAIL because `MidiVelocityFilter` does not exist.

- [ ] **Step 3: Implement filter**

Create `DTXMania.Game/Lib/Input/Midi/MidiVelocityFilter.cs`:

```csharp
#nullable enable

using System;

namespace DTXMania.Game.Lib.Input.Midi;

public sealed class MidiVelocityFilter
{
    private readonly Func<int, int> _thresholdProvider;

    public MidiVelocityFilter(Func<int, int> thresholdProvider)
    {
        _thresholdProvider = thresholdProvider ?? throw new ArgumentNullException(nameof(thresholdProvider));
    }

    public bool ShouldAcceptPress(int noteNumber, int velocity)
    {
        if (noteNumber < 0 || noteNumber > 127)
            return false;

        if (velocity <= 0)
            return false;

        var clampedVelocity = Math.Clamp(velocity, 0, 127);
        var threshold = Math.Clamp(_thresholdProvider(noteNumber), 0, 127);
        return clampedVelocity > threshold;
    }
}
```

- [ ] **Step 4: Run filter tests and verify pass**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~MidiVelocityFilterTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
rtk git add DTXMania.Game/Lib/Input/Midi/MidiVelocityFilter.cs DTXMania.Test/Input/Midi/MidiVelocityFilterTests.cs
rtk git commit -m "feat: add midi velocity filter"
```

## Task 4: Fake-Backend MIDI Input Source

**Files:**
- Create: `DTXMania.Game/Lib/Input/Midi/MidiNoteEventArgs.cs`
- Create: `DTXMania.Game/Lib/Input/Midi/IMidiInputDevice.cs`
- Create: `DTXMania.Game/Lib/Input/Midi/IMidiDeviceBackend.cs`
- Create: `DTXMania.Game/Lib/Input/Midi/MidiInputSource.cs`
- Test: `DTXMania.Test/Input/Midi/MidiInputSourceTests.cs`

- [ ] **Step 1: Write failing input-source tests**

Create `DTXMania.Test/Input/Midi/MidiInputSourceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Input.Midi;
using Xunit;

namespace DTXMania.Test.Input.Midi;

[Trait("Category", "Input")]
public sealed class MidiInputSourceTests
{
    [Fact]
    public void Initialize_NoDevices_IsUnavailableAndDoesNotThrow()
    {
        using var source = new MidiInputSource(new FakeMidiBackend(), _ => 0);

        source.Initialize();

        Assert.False(source.IsAvailable);
        Assert.Empty(source.DeviceNames);
    }

    [Fact]
    public void Initialize_DeviceStartFailure_SkipsDeviceAndKeepsRunning()
    {
        var backend = new FakeMidiBackend(new FakeMidiDevice("bad", "Bad Device") { ThrowOnStart = true });
        using var source = new MidiInputSource(backend, _ => 0);

        source.Initialize();

        Assert.False(source.IsAvailable);
        Assert.Empty(source.DeviceNames);
    }

    [Fact]
    public void Update_NoteOnAccepted_ReturnsMidiButtonState()
    {
        var device = new FakeMidiDevice("d1", "Kit");
        using var source = new MidiInputSource(new FakeMidiBackend(device), _ => 0);
        source.Initialize();

        device.Emit(36, 85, isPressed: true);
        var state = Assert.Single(source.Update());

        Assert.Equal("MIDI.36", state.Id);
        Assert.True(state.IsPressed);
        Assert.Equal(85f / 127f, state.Velocity, precision: 4);
    }

    [Fact]
    public void Update_NoteOnBelowThreshold_ReturnsNoButtonState()
    {
        var device = new FakeMidiDevice("d1", "Kit");
        using var source = new MidiInputSource(new FakeMidiBackend(device), _ => 20);
        source.Initialize();

        device.Emit(36, 20, isPressed: true);

        Assert.Empty(source.Update());
    }

    [Fact]
    public void Update_NoteOffAfterAcceptedPress_ReturnsRelease()
    {
        var device = new FakeMidiDevice("d1", "Kit");
        using var source = new MidiInputSource(new FakeMidiBackend(device), _ => 0);
        source.Initialize();

        device.Emit(36, 90, isPressed: true);
        Assert.Single(source.Update());

        device.Emit(36, 0, isPressed: false);
        var release = Assert.Single(source.Update());

        Assert.Equal("MIDI.36", release.Id);
        Assert.False(release.IsPressed);
        Assert.Equal(0f, release.Velocity);
    }

    [Fact]
    public void Update_NoteOffAfterFilteredPress_ReturnsNoRelease()
    {
        var device = new FakeMidiDevice("d1", "Kit");
        using var source = new MidiInputSource(new FakeMidiBackend(device), _ => 40);
        source.Initialize();

        device.Emit(36, 20, isPressed: true);
        Assert.Empty(source.Update());

        device.Emit(36, 0, isPressed: false);
        Assert.Empty(source.Update());
    }

    [Fact]
    public void Update_SameNoteFromTwoDevices_UsesSingleDeviceAgnosticButtonId()
    {
        var d1 = new FakeMidiDevice("d1", "Kit 1");
        var d2 = new FakeMidiDevice("d2", "Kit 2");
        using var source = new MidiInputSource(new FakeMidiBackend(d1, d2), _ => 0);
        source.Initialize();

        d1.Emit(36, 70, isPressed: true);
        d2.Emit(36, 80, isPressed: true);
        var states = source.Update().ToList();

        Assert.Equal(2, states.Count);
        Assert.All(states, state => Assert.Equal("MIDI.36", state.Id));
        Assert.All(states, state => Assert.True(state.IsPressed));
    }

    [Fact]
    public void Update_SameNoteFromTwoDevices_ReleasesOnlyAfterBothDevicesRelease()
    {
        var d1 = new FakeMidiDevice("d1", "Kit 1");
        var d2 = new FakeMidiDevice("d2", "Kit 2");
        using var source = new MidiInputSource(new FakeMidiBackend(d1, d2), _ => 0);
        source.Initialize();

        d1.Emit(36, 70, isPressed: true);
        d2.Emit(36, 80, isPressed: true);
        Assert.Equal(2, source.Update().Count());

        d1.Emit(36, 0, isPressed: false);
        Assert.Empty(source.Update());

        d2.Emit(36, 0, isPressed: false);
        var release = Assert.Single(source.Update());
        Assert.Equal("MIDI.36", release.Id);
        Assert.False(release.IsPressed);
    }

    [Fact]
    public void RefreshDevices_AddsNewDeviceAndDisposesRemovedDevice()
    {
        var d1 = new FakeMidiDevice("d1", "Kit 1");
        var backend = new FakeMidiBackend(d1);
        using var source = new MidiInputSource(backend, _ => 0);
        source.Initialize();
        Assert.Equal(new[] { "Kit 1" }, source.DeviceNames);

        var d2 = new FakeMidiDevice("d2", "Kit 2");
        backend.SetDevices(d2);
        source.RefreshDevices();

        Assert.True(d1.Disposed);
        Assert.Equal(new[] { "Kit 2" }, source.DeviceNames);
        Assert.True(d2.Started);
    }

    [Fact]
    public void Dispose_StopsAndDisposesAllDevices()
    {
        var d1 = new FakeMidiDevice("d1", "Kit 1");
        var d2 = new FakeMidiDevice("d2", "Kit 2");
        var source = new MidiInputSource(new FakeMidiBackend(d1, d2), _ => 0);
        source.Initialize();

        source.Dispose();

        Assert.True(d1.Stopped);
        Assert.True(d1.Disposed);
        Assert.True(d2.Stopped);
        Assert.True(d2.Disposed);
    }

    private sealed class FakeMidiBackend : IMidiDeviceBackend
    {
        private readonly List<IMidiInputDevice> _devices;

        public FakeMidiBackend(params IMidiInputDevice[] devices)
        {
            _devices = devices.ToList();
        }

        public IReadOnlyList<IMidiInputDevice> GetInputDevices() => _devices.ToArray();

        public void SetDevices(params IMidiInputDevice[] devices)
        {
            _devices.Clear();
            _devices.AddRange(devices);
        }
    }

    private sealed class FakeMidiDevice : IMidiInputDevice
    {
        public string StableId { get; }
        public string Name { get; }
        public bool ThrowOnStart { get; init; }
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public event EventHandler<MidiNoteEventArgs>? NoteReceived;

        public FakeMidiDevice(string stableId, string name)
        {
            StableId = stableId;
            Name = name;
        }

        public void Start()
        {
            if (ThrowOnStart)
                throw new InvalidOperationException("start failed");
            Started = true;
        }

        public void Stop()
        {
            Stopped = true;
        }

        public void Emit(int noteNumber, int velocity, bool isPressed)
        {
            NoteReceived?.Invoke(this, new MidiNoteEventArgs(StableId, noteNumber, velocity, isPressed));
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
```

- [ ] **Step 2: Run source tests and verify failure**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~MidiInputSourceTests"
```

Expected: FAIL because MIDI abstractions and `MidiInputSource` do not exist.

- [ ] **Step 3: Add MIDI event and backend abstractions**

Create `DTXMania.Game/Lib/Input/Midi/MidiNoteEventArgs.cs`:

```csharp
#nullable enable

using System;

namespace DTXMania.Game.Lib.Input.Midi;

public sealed class MidiNoteEventArgs : EventArgs
{
    public string DeviceStableId { get; }
    public int NoteNumber { get; }
    public int Velocity { get; }
    public bool IsPressed { get; }

    public MidiNoteEventArgs(string deviceStableId, int noteNumber, int velocity, bool isPressed)
    {
        DeviceStableId = string.IsNullOrWhiteSpace(deviceStableId) ? "unknown" : deviceStableId;
        NoteNumber = Math.Clamp(noteNumber, 0, 127);
        Velocity = Math.Clamp(velocity, 0, 127);
        IsPressed = isPressed && Velocity > 0;
    }
}
```

Create `DTXMania.Game/Lib/Input/Midi/IMidiInputDevice.cs`:

```csharp
#nullable enable

using System;

namespace DTXMania.Game.Lib.Input.Midi;

public interface IMidiInputDevice : IDisposable
{
    string Name { get; }
    string StableId { get; }
    event EventHandler<MidiNoteEventArgs>? NoteReceived;
    void Start();
    void Stop();
}
```

Create `DTXMania.Game/Lib/Input/Midi/IMidiDeviceBackend.cs`:

```csharp
#nullable enable

using System.Collections.Generic;

namespace DTXMania.Game.Lib.Input.Midi;

public interface IMidiDeviceBackend
{
    IReadOnlyList<IMidiInputDevice> GetInputDevices();
}
```

- [ ] **Step 4: Implement `MidiInputSource`**

Create `DTXMania.Game/Lib/Input/Midi/MidiInputSource.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTXMania.Game.Lib.Input.Midi;

public sealed class MidiInputSource : IInputSource
{
    private readonly IMidiDeviceBackend _backend;
    private readonly MidiVelocityFilter _velocityFilter;
    private readonly ILogger<MidiInputSource> _logger;
    private readonly ConcurrentQueue<MidiNoteEventArgs> _events = new();
    private readonly Dictionary<string, IMidiInputDevice> _devices = new(StringComparer.Ordinal);
    private readonly HashSet<(string DeviceStableId, int NoteNumber)> _acceptedPressedNotes = new();
    private bool _initialized;
    private bool _disposed;

    public string Name => "MIDI";
    public bool IsAvailable => _devices.Count > 0;
    public IReadOnlyList<string> DeviceNames => _devices.Values.Select(device => device.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

    public MidiInputSource(
        IMidiDeviceBackend backend,
        Func<int, int> thresholdProvider,
        ILogger<MidiInputSource>? logger = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _velocityFilter = new MidiVelocityFilter(thresholdProvider ?? throw new ArgumentNullException(nameof(thresholdProvider)));
        _logger = logger ?? NullLogger<MidiInputSource>.Instance;
    }

    public void Initialize()
    {
        if (_disposed)
            return;

        if (_initialized)
            return;

        RefreshDevices();
        _initialized = true;
    }

    public IEnumerable<ButtonState> Update()
    {
        if (_disposed)
            yield break;

        while (_events.TryDequeue(out var midiEvent))
        {
            foreach (var state in ProcessEvent(midiEvent))
                yield return state;
        }
    }

    public IEnumerable<ButtonState> GetPressedButtons()
    {
        if (_disposed)
            yield break;

        foreach (var note in _acceptedPressedNotes.Select(item => item.NoteNumber).Distinct().OrderBy(note => note))
            yield return new ButtonState(KeyBindings.CreateMidiButtonId(note), true, 1.0f);
    }

    public void RefreshDevices()
    {
        if (_disposed)
            return;

        IReadOnlyList<IMidiInputDevice> discovered;
        try
        {
            discovered = _backend.GetInputDevices();
        }
        catch
        {
            _logger.LogWarning("Failed to enumerate MIDI input devices; MIDI input is unavailable for this refresh.");
            discovered = Array.Empty<IMidiInputDevice>();
        }

        var discoveredById = discovered
            .Where(device => !string.IsNullOrWhiteSpace(device.StableId))
            .GroupBy(device => device.StableId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var removedId in _devices.Keys.Except(discoveredById.Keys, StringComparer.Ordinal).ToList())
        {
            RemoveDevice(removedId);
        }

        foreach (var kvp in discoveredById)
        {
            if (_devices.TryGetValue(kvp.Key, out var existingDevice))
            {
                if (!ReferenceEquals(existingDevice, kvp.Value))
                {
                    try { kvp.Value.Dispose(); } catch { }
                }
                continue;
            }

            AddDevice(kvp.Value);
        }
    }

    private IEnumerable<ButtonState> ProcessEvent(MidiNoteEventArgs midiEvent)
    {
        var key = (midiEvent.DeviceStableId, midiEvent.NoteNumber);
        var buttonId = KeyBindings.CreateMidiButtonId(midiEvent.NoteNumber);

        if (midiEvent.IsPressed)
        {
            if (!_velocityFilter.ShouldAcceptPress(midiEvent.NoteNumber, midiEvent.Velocity))
                yield break;

            _acceptedPressedNotes.Add(key);
            yield return new ButtonState(buttonId, true, midiEvent.Velocity / 127f);
            yield break;
        }

        if (!_acceptedPressedNotes.Remove(key))
            yield break;

        if (_acceptedPressedNotes.Any(item => item.NoteNumber == midiEvent.NoteNumber))
            yield break;

        yield return new ButtonState(buttonId, false, 0.0f);
    }

    private void AddDevice(IMidiInputDevice device)
    {
        try
        {
            device.NoteReceived += OnNoteReceived;
            device.Start();
            _devices[device.StableId] = device;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start MIDI input device {DeviceName}; skipping it.", device.Name);
            device.NoteReceived -= OnNoteReceived;
            try { device.Dispose(); } catch { }
        }
    }

    private void RemoveDevice(string stableId)
    {
        if (!_devices.Remove(stableId, out var device))
            return;

        device.NoteReceived -= OnNoteReceived;
        try { device.Stop(); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to stop MIDI input device {DeviceName}.", device.Name); }
        try { device.Dispose(); } catch (Exception ex) { _logger.LogDebug(ex, "Failed to dispose MIDI input device {DeviceName}.", device.Name); }
        _acceptedPressedNotes.RemoveWhere(item => item.DeviceStableId == stableId);
    }

    private void OnNoteReceived(object? sender, MidiNoteEventArgs e)
    {
        if (_disposed)
            return;

        _events.Enqueue(e);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var stableId in _devices.Keys.ToList())
            RemoveDevice(stableId);

        while (_events.TryDequeue(out _)) { }
        _acceptedPressedNotes.Clear();
        _disposed = true;
    }
}
```

- [ ] **Step 5: Run source tests and verify pass**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~MidiInputSourceTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
rtk git add DTXMania.Game/Lib/Input/Midi DTXMania.Test/Input/Midi
rtk git commit -m "feat: add midi input source"
```

## Task 5: DryWetMIDI Backend

**Files:**
- Modify: `DTXMania.Game/DTXMania.Game.Mac.csproj`
- Modify: `DTXMania.Game/DTXMania.Game.Windows.csproj`
- Create: `DTXMania.Game/Lib/Input/Midi/DryWetMidiDeviceBackend.cs`

- [ ] **Step 1: Add DryWetMIDI package references**

Add this package reference to both game project files inside the existing package `ItemGroup`:

```xml
<PackageReference Include="Melanchall.DryWetMidi" Version="8.0.3" />
```

- [ ] **Step 2: Restore/build to verify dependency resolution**

Run:

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Expected: PASS restore/build. No source file references DryWetMIDI yet in this step.

- [ ] **Step 3: Implement backend**

Create `DTXMania.Game/Lib/Input/Midi/DryWetMidiDeviceBackend.cs`:

```csharp
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
            .Cast<IMidiInputDevice>()
            .ToArray();
    }

    private sealed class DryWetMidiInputDevice : IMidiInputDevice
    {
        private readonly InputDevice _device;
        private bool _disposed;

        public string Name => _device.Name;
        public string StableId { get; }
        public event EventHandler<MidiNoteEventArgs>? NoteReceived;

        public DryWetMidiInputDevice(InputDevice device, string stableId)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            StableId = stableId;
        }

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

        private void OnEventReceived(object? sender, MidiEventReceivedEventArgs e)
        {
            switch (e.Event)
            {
                case NoteOnEvent noteOn:
                    NoteReceived?.Invoke(this, new MidiNoteEventArgs(
                        StableId,
                        (int)noteOn.NoteNumber,
                        (int)noteOn.Velocity,
                        (int)noteOn.Velocity > 0));
                    break;

                case NoteOffEvent noteOff:
                    NoteReceived?.Invoke(this, new MidiNoteEventArgs(
                        StableId,
                        (int)noteOff.NoteNumber,
                        (int)noteOff.Velocity,
                        isPressed: false));
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try { Stop(); } catch { }
            _device.Dispose();
            _disposed = true;
        }
    }
}
```

- [ ] **Step 4: Build Mac game and run MIDI tests**

Run:

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~Midi"
```

Expected: PASS. No test should require physical MIDI hardware.

- [ ] **Step 5: Commit**

```bash
rtk git add DTXMania.Game/DTXMania.Game.Mac.csproj DTXMania.Game/DTXMania.Game.Windows.csproj DTXMania.Game/Lib/Input/Midi/DryWetMidiDeviceBackend.cs
rtk git commit -m "feat: add drywetmidi backend"
```

## Task 6: Modular Input Manager MIDI Wiring

**Files:**
- Modify: `DTXMania.Game/Lib/Input/ModularInputManager.cs`
- Test: `DTXMania.Test/Input/ModularInputManagerTests.cs`

- [ ] **Step 1: Write failing manager tests**

Append these tests to `DTXMania.Test/Input/ModularInputManagerTests.cs`:

```csharp
[Fact]
public void Constructor_ShouldRegisterKeyboardAndMidiSources()
{
    using var manager = new ModularInputManager(new ConfigManager());

    Assert.Equal(2, manager.InputRouter.GetSourceCount());
    Assert.Contains("MIDI", manager.GetDiagnosticsInfo());
}

[Fact]
public void AddInputSource_ShouldInitializeSourceExactlyOnce()
{
    using var manager = new ModularInputManager(new ConfigManager());
    var source = new CountingInputSource();

    manager.AddInputSource(source);

    Assert.Equal(1, source.InitializeCount);
}

private sealed class CountingInputSource : IInputSource
{
    public int InitializeCount { get; private set; }
    public string Name => "Counting";
    public bool IsAvailable => true;
    public void Initialize() => InitializeCount++;
    public IEnumerable<ButtonState> Update() => Array.Empty<ButtonState>();
    public IEnumerable<ButtonState> GetPressedButtons() => Array.Empty<ButtonState>();
    public void Dispose() { }
}
```

- [ ] **Step 2: Run manager tests and verify failure**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ModularInputManagerTests"
```

Expected: FAIL because MIDI source is not registered and current initialization behavior double-initializes initial sources.

- [ ] **Step 3: Add MIDI source field and registration helper**

In `DTXMania.Game/Lib/Input/ModularInputManager.cs`, add:

```csharp
using DTXMania.Game.Lib.Input.Midi;
```

Add a private field:

```csharp
private MidiInputSource? _midiInputSource;
```

Replace `InitializeInputSources()` with:

```csharp
private void InitializeInputSources()
{
    RegisterInputSource(new KeyboardInputSource());

    _midiInputSource = new MidiInputSource(
        new DryWetMidiDeviceBackend(),
        noteNumber => _configManager.GetMidiVelocityThreshold(noteNumber));
    RegisterInputSource(_midiInputSource);

    _inputRouter.Initialize();
}
```

Add this private helper:

```csharp
private void RegisterInputSource(IInputSource source)
{
    _inputSources.Add(source);
    _inputRouter.AddInputSource(source);
}
```

Change the public `AddInputSource` method to:

```csharp
public void AddInputSource(IInputSource source)
{
    if (source == null) throw new ArgumentNullException(nameof(source));
    RegisterInputSource(source);
    source.Initialize();
}
```

- [ ] **Step 4: Wire hot-plug refresh and diagnostics**

Replace `ScanForNewDevices()` with:

```csharp
private void ScanForNewDevices()
{
    _midiInputSource?.RefreshDevices();
}
```

In `GetDiagnosticsInfo()`, after listing each source, add:

```csharp
if (_midiInputSource != null)
{
    var deviceNames = _midiInputSource.DeviceNames;
    info += $"  MIDI Devices: {deviceNames.Count}\n";
    foreach (var deviceName in deviceNames)
    {
        info += $"    - {deviceName}\n";
    }
}
```

In `Dispose(bool disposing)`, after `_previousKeyStates?.Clear();`, add:

```csharp
_midiInputSource = null;
```

- [ ] **Step 5: Run manager tests and input tests**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ModularInputManagerTests|FullyQualifiedName~MidiInputSourceTests|FullyQualifiedName~InputRouterTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
rtk git add DTXMania.Game/Lib/Input/ModularInputManager.cs DTXMania.Test/Input/ModularInputManagerTests.cs
rtk git commit -m "feat: wire midi input source"
```

## Task 7: Route Bound MIDI Buttons Through `InputRouter`

**Files:**
- Modify: `DTXMania.Test/Input/InputRouterTests.cs`

- [ ] **Step 1: Add routing regression test**

Append this test to `DTXMania.Test/Input/InputRouterTests.cs`:

```csharp
[Fact]
public void Update_WithPressedBoundMidiButton_ShouldRaiseLaneHitEvent()
{
    _keyBindings.ClearAllBindings();
    _keyBindings.BindButton("MIDI.36", 6);

    var source = new Mock<IInputSource>();
    source.Setup(s => s.Update()).Returns(new[] { new ButtonState("MIDI.36", true, 85f / 127f) });
    _router.AddInputSource(source.Object);

    LaneHitEventArgs? captured = null;
    _router.OnLaneHit += (_, args) => captured = args;

    _router.Update();

    Assert.NotNull(captured);
    Assert.Equal(6, captured!.Lane);
    Assert.Equal("MIDI.36", captured.Button.Id);
    Assert.Equal(85f / 127f, captured.Button.Velocity, precision: 4);
}
```

- [ ] **Step 2: Run router tests**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~InputRouterTests"
```

Expected: PASS. The existing router should already support this because `KeyBindings` is device-agnostic.

- [ ] **Step 3: Commit**

```bash
rtk git add DTXMania.Test/Input/InputRouterTests.cs
rtk git commit -m "test: cover midi input routing"
```

## Task 8: Drum Capture Popup MIDI Threshold Chips

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/DrumConfig/DrumCapturePopup.cs`
- Test: `DTXMania.Test/Stage/DrumConfig/DrumCapturePopupTests.cs`

- [ ] **Step 1: Write failing popup tests**

Modify the test helper in `DrumCapturePopupTests`:

```csharp
private readonly Dictionary<int, int> _thresholds = new();

private DrumCapturePopup NewPopup() => new(
    () => _drum,
    () => _system,
    note => _thresholds.TryGetValue(note, out var threshold) ? threshold : 0);
```

Append these tests:

```csharp
[Fact]
public void GetBindingChips_MidiBinding_IncludesThresholdAndAdjustmentRects()
{
    _drum["MIDI.36"] = 4;
    _thresholds[36] = 20;
    var popup = NewPopup();
    popup.Open(4);

    var chip = popup.GetBindingChips(1280, 720).Single();

    Assert.True(chip.IsMidi);
    Assert.Equal(36, chip.MidiNoteNumber);
    Assert.Equal(20, chip.MidiVelocityThreshold);
    Assert.Contains("v>20", chip.Label);
    Assert.True(chip.DecrementVelocityThreshold.Width > 0);
    Assert.True(chip.IncrementVelocityThreshold.Width > 0);
    Assert.True(chip.Bounds.Contains(chip.DecrementVelocityThreshold));
    Assert.True(chip.Bounds.Contains(chip.IncrementVelocityThreshold));
}

[Fact]
public void GetBindingChips_KeyboardBinding_HasNoThresholdControls()
{
    _drum["Key.S"] = 4;
    var popup = NewPopup();
    popup.Open(4);

    var chip = popup.GetBindingChips(1280, 720).Single();

    Assert.False(chip.IsMidi);
    Assert.Equal(-1, chip.MidiNoteNumber);
    Assert.Equal(0, chip.MidiVelocityThreshold);
    Assert.Equal(Rectangle.Empty, chip.DecrementVelocityThreshold);
    Assert.Equal(Rectangle.Empty, chip.IncrementVelocityThreshold);
}

[Fact]
public void Draw_WhenOpenWithMidiBinding_RendersThresholdLabel()
{
    _drum["MIDI.36"] = 4;
    _thresholds[36] = 20;
    var popup = NewPopup();
    popup.Open(4);
    var font = new Mock<IFont>();
    font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(10, 10));

    popup.Draw(CreateFakeSpriteBatch(), font.Object, whitePixel: null, 1280, 720);

    font.Verify(f => f.DrawString(
        It.IsAny<SpriteBatch>(),
        It.Is<string>(s => s.Contains("MIDI 36") && s.Contains("v>20")),
        It.IsAny<Vector2>(),
        It.IsAny<Color>()), Times.AtLeastOnce);
}
```

- [ ] **Step 2: Run popup tests and verify failure**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumCapturePopupTests"
```

Expected: FAIL because `DrumCapturePopup` does not expose MIDI threshold chip metadata.

- [ ] **Step 3: Add threshold provider to popup**

In `DrumCapturePopup`, add field:

```csharp
private readonly Func<int, int> _midiVelocityThresholdProvider;
```

Change the constructor to:

```csharp
public DrumCapturePopup(
    Func<IReadOnlyDictionary<string, int>> drumBindingsProvider,
    Func<IReadOnlyDictionary<Keys, InputCommandType>> systemMappingProvider,
    Func<int, int>? midiVelocityThresholdProvider = null)
{
    _drumBindingsProvider = drumBindingsProvider ?? throw new ArgumentNullException(nameof(drumBindingsProvider));
    _systemMappingProvider = systemMappingProvider ?? throw new ArgumentNullException(nameof(systemMappingProvider));
    _midiVelocityThresholdProvider = midiVelocityThresholdProvider ?? (_ => 0);
}
```

- [ ] **Step 4: Extend chip metadata and geometry**

Add constants near existing chip constants:

```csharp
private const int ThresholdButtonSize = 18;
private const int ThresholdButtonGap = 4;
```

Replace `DrumBindingChip` with:

```csharp
public readonly struct DrumBindingChip
{
    public string ButtonId { get; }
    public string Label { get; }
    public Rectangle Bounds { get; }
    public Rectangle Remove { get; }
    public bool IsMidi { get; }
    public int MidiNoteNumber { get; }
    public int MidiVelocityThreshold { get; }
    public Rectangle DecrementVelocityThreshold { get; }
    public Rectangle IncrementVelocityThreshold { get; }

    public DrumBindingChip(
        string buttonId,
        string label,
        Rectangle bounds,
        Rectangle remove,
        bool isMidi,
        int midiNoteNumber,
        int midiVelocityThreshold,
        Rectangle decrementVelocityThreshold,
        Rectangle incrementVelocityThreshold)
    {
        ButtonId = buttonId;
        Label = label;
        Bounds = bounds;
        Remove = remove;
        IsMidi = isMidi;
        MidiNoteNumber = midiNoteNumber;
        MidiVelocityThreshold = midiVelocityThreshold;
        DecrementVelocityThreshold = decrementVelocityThreshold;
        IncrementVelocityThreshold = incrementVelocityThreshold;
    }
}
```

In `GetBindingChips`, replace label/width/chip creation with:

```csharp
var isMidi = KeyBindings.TryParseMidiButtonId(id, out var midiNoteNumber);
var threshold = isMidi ? _midiVelocityThresholdProvider(midiNoteNumber) : 0;
var label = isMidi
    ? $"{KeyBindings.FormatButtonId(id)} v>{Math.Clamp(threshold, 0, 127)}"
    : KeyBindings.FormatButtonId(id);
int textWidth = label.Length * ChipCharWidth;
int thresholdControlsWidth = isMidi
    ? ThresholdButtonSize + ThresholdButtonGap + ThresholdButtonSize + ThresholdButtonGap
    : 0;
int chipWidth = Math.Max(ChipMinWidth,
    ChipPadX + textWidth + thresholdControlsWidth + RemoveMargin + RemoveBoxSize + RemoveMargin);
```

After calculating `remove`, add:

```csharp
var increment = Rectangle.Empty;
var decrement = Rectangle.Empty;
if (isMidi)
{
    increment = new Rectangle(
        remove.Left - ThresholdButtonGap - ThresholdButtonSize,
        remove.Y,
        ThresholdButtonSize,
        ThresholdButtonSize);
    decrement = new Rectangle(
        increment.Left - ThresholdButtonGap - ThresholdButtonSize,
        remove.Y,
        ThresholdButtonSize,
        ThresholdButtonSize);
}
```

Replace chip creation with:

```csharp
chips.Add(new DrumBindingChip(
    id,
    label,
    bounds,
    remove,
    isMidi,
    isMidi ? midiNoteNumber : -1,
    Math.Clamp(threshold, 0, 127),
    decrement,
    increment));
```

- [ ] **Step 5: Draw threshold buttons**

In `Draw`, inside the `whitePixel != null` chip loop after drawing `chip.Remove`, add:

```csharp
if (chip.IsMidi)
{
    spriteBatch.Draw(whitePixel, chip.DecrementVelocityThreshold, new Color(70, 92, 118));
    spriteBatch.Draw(whitePixel, chip.IncrementVelocityThreshold, new Color(70, 92, 118));
}
```

Inside the font chip label loop, draw `-` and `+` inside the threshold rectangles:

```csharp
if (chip.IsMidi)
{
    font.DrawString(spriteBatch, "-", new Vector2(chip.DecrementVelocityThreshold.X + 5, chip.DecrementVelocityThreshold.Y - 1), Color.White);
    font.DrawString(spriteBatch, "+", new Vector2(chip.IncrementVelocityThreshold.X + 4, chip.IncrementVelocityThreshold.Y - 1), Color.White);
}
```

Keep the existing chip label draw using `chip.Label`.

- [ ] **Step 6: Run popup tests**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumCapturePopupTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
rtk git add DTXMania.Game/Lib/Stage/DrumConfig/DrumCapturePopup.cs DTXMania.Test/Stage/DrumConfig/DrumCapturePopupTests.cs
rtk git commit -m "feat: show midi velocity thresholds in drum mapping"
```

## Task 9: Drum Config Stage Threshold Edits

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`
- Test: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

- [ ] **Step 1: Write failing stage helper tests**

Append these tests to `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`:

```csharp
[Fact]
public void AdjustMidiVelocityThreshold_IncrementsAndClampsInConfig()
{
    var (stage, cm, _) = CreateWiredStage();
    cm.SetMidiVelocityThreshold(36, 126);

    ReflectionHelpers.InvokePrivateMethod(stage, "AdjustMidiVelocityThreshold", 36, 1);
    Assert.Equal(127, cm.GetMidiVelocityThreshold(36));

    ReflectionHelpers.InvokePrivateMethod(stage, "AdjustMidiVelocityThreshold", 36, 1);
    Assert.Equal(127, cm.GetMidiVelocityThreshold(36));
}

[Fact]
public void AdjustMidiVelocityThreshold_DecrementsAndRemovesAtZero()
{
    var (stage, cm, _) = CreateWiredStage();
    cm.SetMidiVelocityThreshold(36, 1);

    ReflectionHelpers.InvokePrivateMethod(stage, "AdjustMidiVelocityThreshold", 36, -1);

    Assert.Equal(0, cm.GetMidiVelocityThreshold(36));
    Assert.False(cm.Config.MidiVelocityThresholds.ContainsKey(36));
}

[Fact]
public void RemoveBindingFromConfig_DoesNotRemoveMidiVelocityThreshold()
{
    var (stage, cm, _) = CreateWiredStage();
    ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "MIDI.36", 4);
    cm.SetMidiVelocityThreshold(36, 20);

    ReflectionHelpers.InvokePrivateMethod(stage, "RemoveBindingFromConfig", "MIDI.36");

    Assert.False(cm.Config.KeyBindings.ContainsKey("MIDI.36"));
    Assert.Equal(20, cm.GetMidiVelocityThreshold(36));
}
```

- [ ] **Step 2: Run stage tests and verify failure**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumConfigStageTests"
```

Expected: FAIL because `AdjustMidiVelocityThreshold` does not exist and popup wiring does not provide threshold data.

- [ ] **Step 3: Wire threshold provider into popup**

In `DrumConfigStage.OnActivate`, change popup construction to:

```csharp
_popup = new DrumCapturePopup(
    () => _input!.ModularInputManager.KeyBindings.ButtonToLane,
    () => _input!.GetKeyMappingSnapshot(),
    note => _configManager.GetMidiVelocityThreshold(note));
```

- [ ] **Step 4: Handle threshold clicks**

In `UpdatePopup`, inside the `foreach (var chip in _popup.GetBindingChips(vp.Width, vp.Height))` loop, add threshold hit-testing before remove hit-testing:

```csharp
if (chip.IsMidi && chip.DecrementVelocityThreshold.Contains(mouse.X, mouse.Y))
{
    AdjustMidiVelocityThreshold(chip.MidiNoteNumber, -1);
    return;
}

if (chip.IsMidi && chip.IncrementVelocityThreshold.Contains(mouse.X, mouse.Y))
{
    AdjustMidiVelocityThreshold(chip.MidiNoteNumber, 1);
    return;
}
```

Keep the existing remove hit-test after these two checks.

- [ ] **Step 5: Add threshold edit helper**

Add this private method near the other edit helpers:

```csharp
private void AdjustMidiVelocityThreshold(int noteNumber, int delta)
{
    if (noteNumber < 0 || noteNumber > 127)
        return;

    var current = _configManager.GetMidiVelocityThreshold(noteNumber);
    var next = Math.Clamp(current + delta, 0, 127);
    _configManager.SetMidiVelocityThreshold(noteNumber, next);
}
```

- [ ] **Step 6: Run stage tests**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DrumConfigStageTests|FullyQualifiedName~DrumCapturePopupTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
rtk git add DTXMania.Game/Lib/Stage/DrumConfigStage.cs DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
rtk git commit -m "feat: edit midi velocity thresholds in drum mapping"
```

## Task 10: Full Verification

**Files:**
- All files touched by previous tasks.

- [ ] **Step 1: Run focused input/config/drum mapping tests**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~Midi|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~InputRouterTests|FullyQualifiedName~KeyBindingsTests|FullyQualifiedName~ModularInputManagerTests|FullyQualifiedName~DrumCapturePopupTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: PASS.

- [ ] **Step 2: Run full Mac-safe test suite**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: PASS.

- [ ] **Step 3: Build Mac game**

Run:

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Expected: PASS.

- [ ] **Step 4: Inspect git diff for accidental broad changes**

Run:

```bash
rtk git diff --stat
rtk git diff -- DTXManiaNX
```

Expected: `DTXManiaNX` diff is empty. Diff stat contains only planned files.

- [ ] **Step 5: Manual MIDI smoke**

Run the game:

```bash
rtk dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```

Manual checks:

1. Open Config -> Drum Mapping.
2. Select a lane and strike a MIDI drum note.
3. Confirm the chip reads `MIDI <note> v>0`.
4. Click `+` and confirm the chip updates to `v>1`.
5. Click `-` and confirm the chip returns to `v>0`.
6. Enter gameplay with the note bound to a lane.
7. Confirm a hit above threshold triggers the lane.
8. Raise the threshold above a soft hit velocity and confirm that soft hit no longer triggers the lane.

Expected: Gameplay still accepts keyboard input throughout the smoke run.

- [ ] **Step 6: Final commit if verification changed files**

If verification produced only generated `bin/`, `obj/`, or `TestResults/` files, do not commit them. If a previous task missed a planned source/test edit, commit only that source/test edit:

```bash
rtk git status --short
rtk git add <planned-source-or-test-file>
rtk git commit -m "fix: complete midi input verification"
```
