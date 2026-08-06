# HPA-520 — HDMI Device Status in Drum Mapping

**Date:** 2026-08-06  
**Status:** Approved for implementation planning  
**Linear:** HPA-520

## Summary

Show the currently connected drum device on the existing Drum Mapping page so the player can confirm that the expected hardware is visible before assigning pads.

This is a small visibility feature. It does not change device discovery, input routing, bindings, velocity thresholds, diagnostics, or the capture-popup workflow.

## Product Copy Decision

The requested user-facing label is **HDMI device**. DTXManiaCX does not have an HDMI input abstraction; the connected drum hardware is enumerated by the existing MIDI backend and exposed through `MidiInputSource.DeviceNames`.

The label is retained as an explicit product requirement, but the disconnected copy must not imply that the whole assignment screen is unusable. The exact copy is:

| Device count | Text |
| --- | --- |
| 0 | `HDMI device: None detected (keyboard still works)` |
| 1 | `HDMI device: <device name>` |
| 2+ | `HDMI devices (N): <name 1>, <name 2>, ...` |

Internal APIs and types remain named **MIDI**. If the real Windows drum hardware does not appear in `MidiInputSource.DeviceNames`, implementation stops and device discovery moves to a separate Windows investigation. This ticket must not add a second discovery path.

## Goals

1. Show a clear no-device state without suggesting that keyboard assignment is disabled.
2. Show the connected device name when one device is detected.
3. Show device count and names when multiple devices are detected.
4. Reflect connect and disconnect changes while the page remains open.
5. Preserve keyboard assignment and all existing drum-binding behavior.
6. Keep the implementation small and reuse the existing device lifecycle.

## Non-Goals

- Debugging or fixing the Windows-only pad-selection crash.
- Adding a new device-enumeration backend.
- Adding a manual rescan button.
- Persisting or selecting a preferred device.
- Device-specific bindings.
- Adding a standalone device settings screen.
- Redesigning the Drum Mapping page.
- Changing MIDI capture, velocity thresholds, hot-plug timing, or crash diagnostics.

## Existing Architecture

`MidiInputSource` owns MIDI device enumeration and lifecycle. It already:

- Tracks active devices.
- Returns connected display names through a locked, sorted `DeviceNames` snapshot.
- Handles zero devices without failure.
- Refreshes devices through `RefreshDevices()`.

`ModularInputManager` owns registered input sources. It already:

- Exposes `MidiAvailable`.
- Exposes `ConnectedMidiDeviceCount`.
- Calls the existing device-refresh path every three seconds through the hot-plug scan.
- Uses device names in local diagnostics output, but publishes only device count to crash context.

`DrumConfigStage` owns the Drum Mapping page and renders the page title, drum kit, reset control, and capture popup.

The only missing seam is a read-only device-name snapshot at the `ModularInputManager` boundary for UI use.

## Chosen Design

### 1. Expose Connected Device Names

Add a read-only property to `ModularInputManager`:

```csharp
/// <summary>
/// Gets a read-only snapshot of connected MIDI device display names for UI use.
/// Device names must not be added to telemetry or crash context.
/// </summary>
public IReadOnlyList<string> ConnectedMidiDeviceNames =>
    _midiInputSource?.DeviceNames ?? Array.Empty<string>();
```

The property delegates to the existing source snapshot. It must not:

- Trigger device enumeration.
- Return device objects or stable identifiers.
- Expose mutable internal collections.
- Cache names independently of `MidiInputSource`.
- Feed device names into telemetry, breadcrumbs, or crash reports.

`MidiInputSource.DeviceNames` already returns a new sorted list while holding its synchronization lock, so the stage receives a safe snapshot.

### 2. Clarify the Existing Count Contract

Update the XML documentation for `ConnectedMidiDeviceCount` so the two APIs are not contradictory:

```csharp
/// <summary>
/// Gets the current number of connected MIDI devices for telemetry and crash context.
/// Display names are available separately through ConnectedMidiDeviceNames and must
/// not be added to crash diagnostics.
/// </summary>
public int ConnectedMidiDeviceCount => _midiInputSource?.DeviceCount ?? 0;
```

`Game1` and `CrashContextPublisher.PublishInput` continue using count only. No diagnostic schema changes are part of HPA-520.

### 3. Format Status Text in the Stage

Add a private static formatter to `DrumConfigStage`:

```csharp
private static string FormatHdmiDeviceStatus(IReadOnlyList<string> deviceNames)
```

The formatter follows the exact product-copy table above.

It remains `private static`, rather than widening production visibility solely for tests. This matches the existing private-static geometry helper pattern in `DrumConfigStageTests`; tests use direct reflection with `BindingFlags.NonPublic | BindingFlags.Static`.

### 4. Render the Current Snapshot

During `DrumConfigStage.OnDraw`, read the current snapshot:

```csharp
var deviceNames = _input?.ModularInputManager.ConnectedMidiDeviceNames
    ?? Array.Empty<string>();
```

Render the formatted text below the existing title at virtual position `(20, 40)` using the existing font and `DarkText` color.

The stage reads the property on each draw rather than caching it in `OnActivate`. The existing three-second hot-plug scan then updates the displayed text automatically without another timer or event subscription.

### 5. Preserve Existing Interaction

The status line is informational only.

It must not:

- Block opening a lane popup when no device is detected.
- Disable keyboard capture.
- Close or reset an open popup when device state changes.
- Change bindings or thresholds.
- Add focusable controls or hit-test rectangles.

## Data Flow

```text
Existing hot-plug timer expires
  -> ModularInputManager.ScanForNewDevices()
  -> MidiInputSource.RefreshDevices()
  -> MidiInputSource active device collection changes

Next DrumConfigStage draw
  -> ConnectedMidiDeviceNames returns a fresh snapshot
  -> FormatHdmiDeviceStatus formats the snapshot
  -> Stage draws one informational status line
```

No new persistent state is introduced.

## Diagnostics and Privacy Boundary

Connected device count remains the only MIDI-device data published to crash context. Device names are display-only and remain outside:

- `CrashContextPublisher.PublishInput`.
- Breadcrumb fields.
- Structured crash-report metadata.

This preserves the existing low-detail diagnostics contract while allowing the local configuration page to identify the attached hardware.

## Error Handling

- No MIDI source: display `HDMI device: None detected (keyboard still works)`.
- No connected devices: display the same message.
- Device enumeration failure: existing MIDI handling treats the discovered set as empty; the UI shows the no-device message.
- Device names with unexpected characters: render the backend-provided name through the existing font path.
- Windows pad-selection crash: retain the managed crash report and stop verification. Diagnosis belongs in a separate issue.

No broad exception handling is added to `DrumConfigStage`.

## Testing Strategy

### Input Boundary Tests

Add tests to `ModularInputManagerTests` using the existing fake MIDI backend:

- No devices returns an empty name snapshot.
- Multiple devices return sorted display names.
- Refreshing from one device set to another changes the returned snapshot.

Tests assert display names only, not stable IDs or concrete device instances.

### Stage Formatter Tests

Add headless tests to `DrumConfigStageTests`:

- Empty input formats `HDMI device: None detected (keyboard still works)`.
- One name formats the singular message.
- Multiple names format count and comma-separated names.

The formatter stays private and is tested through the direct reflection pattern already used in this test file.

### Draw Wiring

No automated screenshot, pixel, or graphics-device test is added. `OnDraw` is excluded from coverage and the new wiring is a small read-format-draw block beside the existing title. The manual Windows verification is the integration gate for the live draw path.

### Manual Windows Verification

With the actual drum device:

1. Open Drum Mapping while disconnected and confirm the exact no-device message.
2. Confirm keyboard assignment still works.
3. Connect the device without leaving the page.
4. Confirm the device name appears within the existing hot-plug interval.
5. Disconnect the device and confirm the no-device message returns.
6. Confirm a connected pad still captures its existing `MIDI.<note>` binding.

If selecting a lane crashes, retain the managed crash report and move the investigation to the separate Windows bug.

## File Impact

- Modify `DTXMania.Game/Lib/Input/ModularInputManager.cs`.
- Modify `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`.
- Modify `DTXMania.Test/Input/ModularInputManagerTests.cs`.
- Modify `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`.

No new production files or dependencies are required.

## Acceptance Criteria

- The Drum Mapping page shows `HDMI device: None detected (keyboard still works)` when no existing MIDI device is active.
- One connected device is shown by name.
- Multiple devices show their count and names.
- Connect and disconnect changes appear through the existing hot-plug scan without reopening the page.
- Keyboard assignment remains available while no device is detected.
- Existing MIDI note capture, bindings, popup behavior, and velocity thresholds remain unchanged.
- Crash context continues publishing count only; device names are not added.
- Focused tests cover empty, connected, multiple-device, and refreshed snapshots.
- Manual Windows verification is completed using the real hardware.

## Scope Estimate

One engineer day. The production change remains two small code edits plus focused tests and manual Windows verification.
