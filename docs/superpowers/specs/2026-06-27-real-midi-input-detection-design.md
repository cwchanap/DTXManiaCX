# Real MIDI Input Detection For Gameplay

**Status:** Design approved, awaiting implementation planning
**Date:** 2026-06-27
**Scope:** Add real MIDI input from connected devices to gameplay and drum mapping, with device-agnostic note bindings and configurable per-note velocity filtering.

## Problem

DTXManiaCX already has a modular input architecture:

- `IInputSource` emits device-agnostic `ButtonState` changes.
- `InputRouter` maps button IDs through `KeyBindings`.
- `JudgementManager` consumes lane-hit events for gameplay.
- `DrumConfigStage` captures pressed buttons from `ModularInputManager.ConsumePressedButtons()`.

The current runtime only registers keyboard input sources. Config and tests already accept button IDs such as `MIDI.36`, but no real MIDI device is enumerated, opened, listened to, or routed into gameplay. This blocks real electronic drum-kit gameplay.

## Goals

1. Detect MIDI input from connected devices during gameplay.
2. Bind MIDI notes device-agnostically as `MIDI.<noteNumber>`, so note 36 from any device uses the same lane binding.
3. Preserve MIDI velocity on `ButtonState.Velocity`.
4. Add a per-note minimum velocity threshold, defaulting to `0` so no note is filtered unless the user configures it.
5. Configure MIDI velocity thresholds from the existing Drum Mapping config stage, attached to MIDI binding chips.
6. Support multiple MIDI input devices at once.
7. Handle missing devices, enumeration failures, and hot-plug changes without crashing or breaking keyboard input.
8. Keep hardware-dependent behavior behind testable abstractions so unit tests run without a physical MIDI device.

## Non-Goals

- Device-specific MIDI bindings such as `MIDI.<device>.<note>`.
- Joystick/gamepad input.
- NX's full input assignment model, buffered input mode, or per-pad threshold presets.
- MIDI chart/SMF file parsing.
- Audio latency calibration or judgement-window changes.
- A new global MIDI setup screen. The first UI surface is the Drum Mapping stage.

## Chosen Approach

Add MIDI as another `IInputSource`, backed by DryWetMIDI's Multimedia API. DryWetMIDI supports MIDI device access on Windows and macOS, matching this repo's game targets.

DryWetMIDI-specific code stays behind a small backend adapter so most tests use fake devices and fake MIDI events. The game loop only sees `ButtonState` objects, preserving the existing router, judgement, pad feedback, and capture paths.

## Alternatives Considered

### Alternative 1: MIDI As `IInputSource` (chosen)

This fits the current architecture. Gameplay, config capture, and API injection already converge on button IDs and lane routing, so MIDI can enter through the same path as keyboard input.

Tradeoff: MIDI-specific lifecycle and diagnostics need to live inside the input subsystem, but that is already where hot-plug scanning is planned.

### Alternative 2: Separate MIDI Manager Outside `InputRouter`

A standalone MIDI manager could call judgement logic directly.

Rejected because it would duplicate lane routing, capture behavior, pad feedback, and test seams that already exist.

### Alternative 3: Full NX-Style MIDI Assignment Now

This would include device-specific assignment, multiple binding slots per pad, buffered input modes, richer diagnostics, and threshold presets.

Rejected for the first slice because it is too broad. The current request is real gameplay input from MIDI devices, with device-agnostic note bindings and per-note velocity filtering.

## Architecture

### Package Dependency

Add `Melanchall.DryWetMidi` to both game projects:

- `DTXMania.Game/DTXMania.Game.Mac.csproj`
- `DTXMania.Game/DTXMania.Game.Windows.csproj`

Tests should avoid requiring real DryWetMIDI devices. If a test needs MIDI event types directly, keep it behind adapter-level fake objects rather than probing host hardware.

### MIDI Backend Adapter

Add a narrow adapter layer under `DTXMania.Game/Lib/Input/Midi/`:

```csharp
public interface IMidiDeviceBackend
{
    IReadOnlyList<IMidiInputDevice> GetInputDevices();
}

public interface IMidiInputDevice : IDisposable
{
    string Name { get; }
    string StableId { get; }
    event EventHandler<MidiNoteEventArgs> NoteReceived;
    void Start();
    void Stop();
}

public sealed class MidiNoteEventArgs : EventArgs
{
    public int NoteNumber { get; }
    public int Velocity { get; }
    public bool IsPressed { get; }
}
```

`DryWetMidiDeviceBackend` wraps DryWetMIDI:

- Enumerates `InputDevice.GetAll()`.
- Subscribes to `EventReceived`.
- Converts `NoteOnEvent`, `NoteOffEvent`, and note-on velocity `0` into `MidiNoteEventArgs`.
- Uses device name/stable ID only for lifecycle reconciliation and diagnostics, not for binding IDs.

### `MidiInputSource`

`MidiInputSource : IInputSource` owns the opened MIDI devices and drains queued note events during `Update()`.

Responsibilities:

- Open every available MIDI input device.
- Start listening for events.
- Queue translated note events from MIDI callbacks.
- During `Update()`, apply the velocity filter and return `ButtonState` changes.
- Emit button IDs as `MIDI.<noteNumber>`.
- Normalize velocity with `velocity / 127f`.
- Track accepted pressed notes by `(device stable ID, note number)` internally, while still emitting device-agnostic `MIDI.<noteNumber>` button IDs. This prevents a release from one device from clearing a note still held by another device.
- Expose `IsAvailable == true` when at least one device is listening.
- Dispose all opened devices.

The callback thread never calls gameplay code directly. It only enqueues note events into a thread-safe queue. Routing happens on the normal game update thread.

### Velocity Filter

Add a pure helper:

```csharp
public sealed class MidiVelocityFilter
{
    public bool ShouldAcceptPress(int noteNumber, int velocity);
}
```

Rules:

- Threshold range is `0..127`.
- Missing threshold means `0`.
- Threshold `0` means no filtering.
- A note-on with velocity `0` is release, not press.
- A press passes when `velocity > threshold`.
- A press is filtered when `velocity <= threshold`.
- Releases always pass through for notes that were previously accepted.

The strict `>` rule means threshold `1` filters only velocity `1` ghost hits, threshold `20` filters velocities `1..20`, and threshold `0` filters nothing except the MIDI-standard velocity-zero release form.

### Config

Extend `ConfigData`:

```csharp
public Dictionary<int, int> MidiVelocityThresholds { get; set; } = new();
```

Config file format:

```ini
[MidiVelocityThresholds]
MidiVelocity.36=20
MidiVelocity.38=12
```

Persistence rules:

- Save only nonzero thresholds.
- Clamp loaded values into `0..127`.
- Ignore invalid note numbers outside `0..127`.
- Missing note key means threshold `0`.
- Setting a threshold to `0` removes the key from persisted config.

Add config APIs:

```csharp
int GetMidiVelocityThreshold(int noteNumber);
void SetMidiVelocityThreshold(int noteNumber, int threshold);
```

`SetMidiVelocityThreshold` clamps input, updates `Config`, marks the config dirty, and keeps the change live immediately. `MidiInputSource` reads thresholds through a provider so threshold edits apply without reopening devices or restarting the game.

### Input Manager Wiring

`ModularInputManager.InitializeInputSources()` should add:

1. `KeyboardInputSource`
2. `MidiInputSource`

The current input-source setup initializes sources inside `AddInputSource()` and then calls `_inputRouter.Initialize()`, which initializes sources again. The implementation should remove that double-initialization path as a targeted cleanup before adding MIDI, because starting MIDI listeners twice is unsafe.

Hot-plug behavior uses the existing `GameConstants.Input.DeviceScanIntervalMs` cadence. `ModularInputManager.ScanForNewDevices()` should ask `MidiInputSource` to refresh devices:

- New devices are opened and started.
- Removed devices are stopped and disposed.
- Failed devices are logged and skipped.
- Keyboard input remains available regardless of MIDI failures.

Diagnostics should include MIDI source availability and opened device names/counts.

### Drum Mapping UI

The existing `DrumConfigStage` and `DrumCapturePopup` remain the configuration surface.

Capture behavior:

- Hitting a MIDI note while the popup is listening captures `MIDI.<noteNumber>`.
- Device identity is not shown in the binding ID and is not persisted.
- Keyboard binding behavior is unchanged.

MIDI chip behavior:

- Keyboard chips remain as they are.
- MIDI chips show the formatted note label and current threshold, for example `MIDI 36 v>20`.
- Each MIDI chip exposes compact decrement and increment hit areas for its threshold.
- Each click adjusts the threshold by `1`.
- Threshold adjustments clamp to `0..127`, call `ConfigManager.SetMidiVelocityThreshold(note, value)`, and leave the drum binding unchanged.
- Removing a MIDI binding does not delete that note's threshold. Thresholds are keyed by note number and may be reused if the note is rebound.

The popup stays intent-only for bindings. Threshold edits are separate config edits applied by `DrumConfigStage`, mirroring how the stage already applies captured bindings through `ConfigManager`.

## Data Flow

### Startup

```text
Game1 creates InputManagerCompat
  -> ModularInputManager loads KeyBindings from Config
  -> adds KeyboardInputSource
  -> adds MidiInputSource
       -> DryWetMidiDeviceBackend enumerates input devices
       -> each device starts event listening
```

### MIDI Hit

```text
MIDI device sends NoteOn(36, velocity 85)
  -> DryWetMIDI EventReceived
  -> backend converts to MidiNoteEventArgs(note=36, velocity=85, pressed=true)
  -> MidiInputSource queues event
  -> game Update calls MidiInputSource.Update()
  -> MidiVelocityFilter checks Config threshold for note 36
  -> emits ButtonState("MIDI.36", true, 85 / 127f)
  -> InputRouter looks up KeyBindings["MIDI.36"]
  -> OnLaneHit fires
  -> JudgementManager, pad feedback, score, combo, and chip-sound paths behave like keyboard input
```

### Filtered Ghost Hit

```text
Config threshold for note 38 is 20
MIDI device sends NoteOn(38, velocity 12)
  -> event is queued
  -> filter rejects press because 12 <= 20
  -> no ButtonState is returned
  -> no lane hit is routed
```

### Release

```text
MIDI device sends NoteOff(36)
  -> backend converts to pressed=false
  -> MidiInputSource clears the accepted pressed state for that device and note
  -> if no devices still hold accepted note 36, emits ButtonState("MIDI.36", false, 0)
  -> InputRouter ignores release for lane-hit events
```

## Error Handling

- No MIDI devices: source initializes as unavailable and returns no events.
- Device enumeration failure: log warning, keep keyboard input running.
- Device start failure: log warning with device name, skip that device.
- Device callback failure: catch/log around translation and skip the malformed event. If the device listener itself fails, remove that device during the next refresh.
- Hot-unplug: dispose removed device and clear accepted pressed state associated with that device.
- Unsupported host MIDI backend: treat as no MIDI devices available.
- DryWetMIDI package/runtime errors must not crash stage activation or gameplay update.

## Testing Strategy

All tests should run without real MIDI hardware.

### Unit Tests

`MidiVelocityFilterTests`:

- `ShouldAcceptPress_DefaultThreshold_AllowsVelocityOne`
- `ShouldAcceptPress_ZeroVelocity_IsReleaseNotPress`
- `ShouldAcceptPress_VelocityEqualThreshold_IsRejected`
- `ShouldAcceptPress_VelocityAboveThreshold_IsAccepted`
- `ShouldAcceptPress_InvalidThreshold_IsClamped`

`MidiInputSourceTests` with fake backend/devices:

- `Initialize_NoDevices_IsUnavailableAndDoesNotThrow`
- `Initialize_DeviceStartFailure_SkipsDeviceAndKeepsRunning`
- `Update_NoteOnAccepted_ReturnsMidiButtonState`
- `Update_NoteOnBelowThreshold_ReturnsNoButtonState`
- `Update_NoteOffAfterAcceptedPress_ReturnsRelease`
- `Update_NoteOffAfterFilteredPress_ReturnsNoRelease`
- `Update_SameNoteFromTwoDevices_UsesSingleDeviceAgnosticButtonId`
- `Update_SameNoteFromTwoDevices_ReleasesOnlyAfterBothDevicesRelease`
- `RefreshDevices_AddsNewDeviceAndDisposesRemovedDevice`
- `Dispose_StopsAndDisposesAllDevices`

`ConfigManagerTests`:

- `SaveAndLoadConfig_MidiVelocityThresholds_PreservesNonzeroThresholds`
- `SetMidiVelocityThreshold_Zero_RemovesPersistedThreshold`
- `LoadConfig_InvalidMidiVelocityThresholds_AreIgnoredOrClamped`

`InputRouterTests`:

- `Update_WithPressedBoundMidiButton_ShouldRaiseLaneHitEvent`

`DrumCapturePopupTests` / `DrumConfigStageTests`:

- MIDI binding chips include threshold controls.
- Increment/decrement changes the configured threshold for that note.
- Keyboard chips do not expose MIDI threshold controls.
- Removing a MIDI binding does not delete the threshold.

### Integration/Manual Verification

Manual run with a MIDI drum kit or virtual MIDI source:

1. Start the Mac or Windows game.
2. Open Config -> Drum Mapping.
3. Select a lane and hit a MIDI note.
4. Confirm the popup captures `MIDI.<noteNumber>`.
5. Adjust the velocity threshold for that MIDI chip.
6. Enter gameplay and confirm hits above threshold trigger the mapped lane.
7. Confirm hits at or below threshold do not trigger the lane.
8. Hot-plug a MIDI device and confirm keyboard input remains usable.

## Touch List

- `DTXMania.Game/DTXMania.Game.Mac.csproj` - add DryWetMIDI package reference.
- `DTXMania.Game/DTXMania.Game.Windows.csproj` - add DryWetMIDI package reference.
- `DTXMania.Game/Lib/Input/Midi/` - new backend adapter, event args, velocity filter, and MIDI input source.
- `DTXMania.Game/Lib/Input/ModularInputManager.cs` - add MIDI source, avoid double initialization, refresh MIDI devices during scan, include diagnostics.
- `DTXMania.Game/Lib/Input/KeyBindings.cs` - keep `MIDI.<note>` formatting; add parsing helpers if useful for UI/config.
- `DTXMania.Game/Lib/Config/ConfigData.cs` - add `MidiVelocityThresholds`.
- `DTXMania.Game/Lib/Config/ConfigManager.cs` - parse/save thresholds and add getter/setter APIs.
- `DTXMania.Game/Lib/Config/IConfigManager.cs` - expose threshold APIs.
- `DTXMania.Game/Lib/Stage/DrumConfig/DrumCapturePopup.cs` - expose MIDI threshold chip geometry/display state.
- `DTXMania.Game/Lib/Stage/DrumConfigStage.cs` - apply threshold increment/decrement through `ConfigManager`.
- `DTXMania.Test/` - add fake-device MIDI input tests, config tests, and drum mapping threshold tests.
- `DTXMania.Test/DTXMania.Test.Mac.csproj` - include new Mac-safe test files explicitly if needed.

## Risks And Mitigations

- **Native MIDI backend behavior differs by OS.** Keep DryWetMIDI isolated behind `IMidiDeviceBackend` and make failures non-fatal.
- **Callback thread races with game update.** Use a thread-safe queue and drain on the game update thread.
- **Double initialization opens listeners twice.** Fix source initialization before adding MIDI.
- **Device-agnostic binding can merge two devices sending the same note.** This is intentional for the first implementation and matches the approved design.
- **Threshold UI could crowd existing binding chips.** Show threshold controls only on MIDI chips and keep keyboard chips unchanged.
- **Velocity thresholds can hide valid soft hits if set too high.** Default is `0`; all filtering is opt-in and visible on MIDI chips.

## Out-of-Scope Follow-Ups

- Device-specific MIDI bindings.
- Dedicated MIDI diagnostics/config screen.
- Per-pad or per-lane threshold presets beyond per-note thresholds.
- Import/export of common e-drum note maps.
- Buffered input mode.
- Gamepad/guitar controller support.
