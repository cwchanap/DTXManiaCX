# HPA-520 — HDMI Device Status in Drum Mapping

**Date:** 2026-08-06  
**Status:** Approved for implementation planning  
**Linear:** HPA-520

## Summary

Show the currently connected drum device on the existing Drum Mapping page so the player can confirm that the expected hardware is visible before assigning pads.

This is a small visibility feature. It does not change device discovery, input routing, bindings, velocity thresholds, or the capture popup workflow.

## Terminology and Implementation Assumption

The requested user-facing term is **HDMI device**. DTXManiaCX does not currently have an HDMI input abstraction. The connected drum hardware already enters the application through the existing MIDI device backend and is exposed by `MidiInputSource.DeviceNames`.

Therefore:

- User-facing copy uses **HDMI device** as requested.
- Internal APIs and types remain named **MIDI**.
- HPA-520 surfaces the device names already reported by `MidiInputSource`.
- If the real Windows hardware does not appear in `MidiInputSource.DeviceNames`, implementation stops. Device discovery must then be investigated separately instead of adding a second Windows-specific discovery path to this ticket.

## Goals

1. Show a clear disconnected state when no drum device is detected.
2. Show the connected device name when one device is detected.
3. Show device count and names when multiple devices are detected.
4. Reflect connect and disconnect changes while the page remains open.
5. Preserve keyboard assignment and all existing drum binding behavior.
6. Keep the implementation small and reuse the existing device lifecycle.

## Non-Goals

- Debugging or fixing the Windows-only pad-selection crash.
- Adding a new device enumeration backend.
- Adding a manual rescan button.
- Persisting a preferred device.
- Device-specific bindings.
- Adding a standalone device settings screen.
- Redesigning the Drum Mapping page.
- Changing MIDI capture, velocity thresholds, or hot-plug timing.

## Existing Architecture

`MidiInputSource` owns MIDI device enumeration and lifecycle. It already:

- Tracks active devices.
- Returns connected names through a locked `DeviceNames` snapshot.
- Handles zero devices without failure.
- Refreshes devices through `RefreshDevices()`.

`ModularInputManager` owns the registered input sources. It already:

- Exposes `MidiAvailable`.
- Exposes `ConnectedMidiDeviceCount`.
- Calls the existing device refresh path every three seconds through the hot-plug scan.

`DrumConfigStage` owns the Drum Mapping page and renders the page title, drum kit, reset control, and capture popup.

The missing seam is a read-only device-name snapshot at the `ModularInputManager` boundary.

## Chosen Design

### 1. Expose Connected Device Names

Add a read-only property to `ModularInputManager`:

```csharp
public IReadOnlyList<string> ConnectedMidiDeviceNames =>
    _midiInputSource?.DeviceNames ?? Array.Empty<string>();
```

The property delegates to the existing source snapshot. It must not:

- Trigger device enumeration.
- Return device objects or stable identifiers.
- Expose mutable internal collections.
- Cache names independently of `MidiInputSource`.

`MidiInputSource.DeviceNames` already returns a new sorted list while holding its synchronization lock, so the stage receives a safe snapshot.

### 2. Format Status Text in the Stage

Add a private static formatter to `DrumConfigStage`:

```csharp
private static string FormatHdmiDeviceStatus(IReadOnlyList<string> deviceNames)
```

Formatting rules are exact:

| Device count | Text |
| --- | --- |
| 0 | `HDMI device: Not connected` |
| 1 | `HDMI device: <device name>` |
| 2+ | `HDMI devices (N): <name 1>, <name 2>, ...` |

The formatter is pure and contains no graphics or input dependencies. Existing reflection-based stage tests can exercise it headlessly.

### 3. Render the Current Snapshot

During `DrumConfigStage.OnDraw`, read the current snapshot:

```csharp
var deviceNames = _input?.ModularInputManager.ConnectedMidiDeviceNames
    ?? Array.Empty<string>();
```

Render the formatted text below the existing title at virtual position `(20, 40)` using the existing font and `DarkText` color.

The stage must read the property on each draw rather than caching it in `OnActivate`. The existing three-second hot-plug scan then updates the displayed text automatically without another timer or event subscription.

### 4. Preserve Existing Interaction

The status line is informational only.

It must not:

- Block opening a lane popup when disconnected.
- Disable keyboard capture.
- Close or reset an open popup when device state changes.
- Change bindings or thresholds.
- Add focusable controls.

## Data Flow

```text
Existing hot-plug timer expires
  -> ModularInputManager.ScanForNewDevices()
  -> MidiInputSource.RefreshDevices()
  -> MidiInputSource active device collection changes

Next DrumConfigStage draw
  -> ConnectedMidiDeviceNames returns a fresh snapshot
  -> FormatHdmiDeviceStatus formats the snapshot
  -> Stage draws the status line
```

No new persistent state is introduced.

## Error Handling

- No MIDI source: display `HDMI device: Not connected`.
- No connected devices: display `HDMI device: Not connected`.
- Device enumeration failure: existing MIDI handling treats the discovered set as empty; the UI shows disconnected.
- Device names with unexpected characters: render the backend-provided name as normal text using the existing font behavior.
- Windows pad-selection crash: capture the managed crash report and stop verification. Diagnosis belongs in a separate issue.

No broad exception handling should be added to `DrumConfigStage` for this feature.

## Testing Strategy

### Input Boundary Tests

Add tests to `ModularInputManagerTests` using the existing fake MIDI backend:

- No devices returns an empty name snapshot.
- Multiple devices return sorted display names.
- Refreshing from one device set to another changes the returned snapshot.

Tests assert display names only, not stable IDs or concrete device instances.

### Stage Formatter Tests

Add headless tests to `DrumConfigStageTests`:

- Empty input formats the disconnected message.
- One name formats the singular message.
- Multiple names format count and comma-separated names.

No screenshot or pixel tests are needed for a single text line.

### Manual Windows Verification

With the actual drum device:

1. Open Drum Mapping while disconnected and confirm the disconnected message.
2. Connect the device without leaving the page.
3. Confirm the device name appears within the existing hot-plug interval.
4. Disconnect the device and confirm the status returns to disconnected.
5. Confirm keyboard assignment works in both states.
6. Confirm a connected pad still captures its existing `MIDI.<note>` binding.

If selecting a lane crashes, retain the managed crash report and move the investigation to the separate Windows bug.

## File Impact

- Modify `DTXMania.Game/Lib/Input/ModularInputManager.cs`.
- Modify `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`.
- Modify `DTXMania.Test/Input/ModularInputManagerTests.cs`.
- Modify `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`.

No new production files or dependencies are required.

## Acceptance Criteria

- The Drum Mapping page shows `HDMI device: Not connected` when no existing MIDI device is active.
- One connected device is shown by name.
- Multiple devices show their count and names.
- Connect and disconnect changes appear through the existing hot-plug scan without reopening the page.
- Keyboard assignment remains available while disconnected.
- Existing MIDI note capture, bindings, popup behavior, and velocity thresholds remain unchanged.
- Focused tests cover empty, connected, multiple-device, and refreshed snapshots.
- Manual Windows verification is completed using the real hardware.

## Scope Estimate

One engineer day. The production change should remain two small code edits plus focused tests and manual Windows verification.
