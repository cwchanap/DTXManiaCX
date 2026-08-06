# HPA-520 — HDMI Device Status in Drum Mapping

**Date:** 2026-08-06  
**Status:** Approved for implementation planning  
**Linear:** HPA-520

## Summary

Show the currently connected drum device on the existing Drum Mapping page so the player can confirm that the expected hardware is visible before assigning pads.

This is a small visibility feature. It does not change device discovery, input routing, bindings, velocity thresholds, diagnostics, or the capture-popup workflow.

## Product Copy Decision

The requested user-facing label is **HDMI device**. This is a deliberate product term, not a description of the transport layer: DTXManiaCX has no HDMI input abstraction, and the connected drum hardware is enumerated by the existing MIDI backend through `MidiInputSource.DeviceNames`.

This terminology may be technically imprecise, but it is retained because the project lead explicitly requested it. The design mitigates the main UX risk by making the disconnected message explain that keyboard assignment still works. Internal APIs and types remain named **MIDI**.

If the real Windows drum hardware does not appear in `MidiInputSource.DeviceNames`, implementation stops and device discovery moves to a separate Windows investigation. This ticket must not add a second discovery path.

Exact copy:

| Device count | Text |
| --- | --- |
| 0 | `HDMI device: None detected (keyboard still works)` |
| 1 | `HDMI device: <device name>` |
| 2+ | `HDMI devices (N): <first name>, +<N-1> more` |

The disconnected copy must not imply that keyboard assignment is disabled. For multiple devices, preserve the first sorted device name and summarize the remainder instead of joining an unbounded list.

## Goals

1. Show a clear no-device state without suggesting that keyboard assignment is unavailable.
2. Show the connected device name when one device is detected.
3. Summarize multiple connected devices without overflowing the page.
4. Reflect connect and disconnect changes while the page remains open.
5. Preserve keyboard assignment and all existing drum-binding behavior.
6. Keep the implementation small and reuse the existing device lifecycle and text-layout helper.

## Non-Goals

- Debugging or fixing the Windows-only pad-selection crash.
- Adding a new device-enumeration backend.
- Adding a manual rescan button.
- Persisting or selecting a preferred device.
- Device-specific bindings.
- Adding a standalone device settings screen.
- Redesigning the Drum Mapping page.
- Changing MIDI capture, velocity thresholds, hot-plug timing, or crash diagnostics.
- Adding a new text truncation implementation.
- Adding a screenshot-based automated test.

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
- Uses device names in local diagnostics output, while publishing only device count to crash context.

`DrumConfigStage` owns the Drum Mapping page and renders the page title, drum kit, Reset control, and capture popup.

`TextHelper.TruncateToWidth(string, float, IFont)` already provides measured ellipsis truncation for the stage font type.

The missing seams are:

1. A read-only device-name snapshot at the `ModularInputManager` boundary for UI use.
2. A bounded status line that cannot overlap the Reset control or clip outside the virtual render target.

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

`MidiInputSource.DeviceNames` already returns a new sorted list while holding its synchronization lock. The per-frame snapshot allocation is accepted: the collection is tiny, reading live state avoids a stale stage cache, and there is no measured performance problem to justify another synchronization mechanism.

### 2. Clarify the Existing Count Contract

Update the XML documentation for `ConnectedMidiDeviceCount` so the count and name APIs have explicit, different consumers:

```csharp
/// <summary>
/// Gets the current number of connected MIDI devices for telemetry and crash context.
/// Display names are available separately through ConnectedMidiDeviceNames and must
/// not be added to crash diagnostics.
/// </summary>
public int ConnectedMidiDeviceCount => _midiInputSource?.DeviceCount ?? 0;
```

`Game1` and `CrashContextPublisher.PublishInput` continue using count only. No diagnostic schema change is part of HPA-520.

### 3. Format Compact Status Text

Add a private static formatter to `DrumConfigStage`:

```csharp
private static string FormatHdmiDeviceStatus(IReadOnlyList<string> deviceNames)
```

Rules:

```csharp
if (deviceNames == null || deviceNames.Count == 0)
    return "HDMI device: None detected (keyboard still works)";

if (deviceNames.Count == 1)
    return $"HDMI device: {deviceNames[0]}";

return $"HDMI devices ({deviceNames.Count}): {deviceNames[0]}, +{deviceNames.Count - 1} more";
```

The source snapshot is already sorted with `StringComparer.Ordinal`; the UI intentionally inherits that order and does not re-sort or alter the shared source behavior.

The formatter remains `private static`, rather than widening production visibility solely for tests. This matches the existing private-static helper pattern in `DrumConfigStageTests`.

### 4. Render Below Reset and Bound the Width

The Reset button occupies the top-right area and ends at virtual `y = 42`. Draw the status below it at `(20, 48)`.

During `DrumConfigStage.OnDraw`, read the current snapshot, format it, and reuse the existing measured truncation helper:

```csharp
var deviceNames = _input?.ModularInputManager.ConnectedMidiDeviceNames
    ?? Array.Empty<string>();
var status = FormatHdmiDeviceStatus(deviceNames);
var visibleStatus = TextHelper.TruncateToWidth(status, vw - 40, _font);

_font.DrawString(
    _spriteBatch,
    visibleStatus,
    new Vector2(20, 48),
    DarkText);
```

`vw - 40` leaves 20 pixels of margin on both sides of the virtual target. Moving the line below the Reset control removes the horizontal collision; truncation protects against a single unusually long device name and render-target clipping.

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
  -> ConnectedMidiDeviceNames returns a fresh sorted snapshot
  -> FormatHdmiDeviceStatus produces compact copy
  -> TextHelper.TruncateToWidth bounds the measured string
  -> Stage draws the status below the Reset control
```

No new persistent state is introduced.

## Error Handling

- No MIDI source or connected device: show the exact no-device copy.
- Device enumeration failure: existing MIDI handling produces an empty discovered set; the UI shows the no-device copy.
- Long or unusual device names: the existing font path handles unsupported characters, and `TextHelper.TruncateToWidth` bounds the rendered width.
- Windows pad-selection crash: retain the managed crash report and stop verification. Diagnosis belongs in a separate issue.

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

- Empty input formats the exact no-device message.
- One name formats the singular message.
- Multiple names preserve the first sorted name and summarize the remaining count.

Do not duplicate `TextHelper` tests. The draw site must call the existing helper; the helper already owns measurement and ellipsis behavior.

### Local macOS Visual Smoke

After implementation, launch the macOS build with no MIDI hardware, navigate to Config → Drum Mapping, and capture a screenshot through the existing Game API/MCP screenshot path or the normal OS screenshot tool.

Confirm:

- Exact no-device copy is visible.
- The line is below and clear of the Reset control.
- The line does not cover the drum kit header area.

A long/multi-device runtime screenshot is not required because the current Game API cannot inject device discovery names. Compact plural formatting is covered by unit tests, and width bounding reuses the existing tested `TextHelper` implementation.

### Manual Windows Verification

With the actual drum device:

1. Open Drum Mapping while disconnected and confirm the exact no-device message.
2. Connect the device without leaving the page.
3. Confirm its name appears within the existing hot-plug interval.
4. If the backend exposes multiple ports, confirm the compact `+N more` summary.
5. Disconnect the device and confirm the no-device status returns.
6. Confirm keyboard assignment works in both states.
7. Confirm a connected pad still captures its existing `MIDI.<note>` binding.

If selecting a lane crashes, retain the managed crash report and move the investigation to the separate Windows bug.

## Validation Gates

Local macOS:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Pull request CI:

- Confirm the Windows `build-and-test-windows` job is green.
- Confirm the macOS build/test job and all other required PR checks remain green.

The plan does not require pretending that a macOS-only implementation environment can execute the Windows suite locally.

## File Impact

- Modify `DTXMania.Game/Lib/Input/ModularInputManager.cs`.
- Modify `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`.
- Modify `DTXMania.Test/Input/ModularInputManagerTests.cs`.
- Modify `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`.

No new production files or dependencies are required.

## Acceptance Criteria

- No device shows `HDMI device: None detected (keyboard still works)`.
- One connected device is shown by name.
- Multiple devices show total count, first name, and `+N more`.
- The status is drawn below the Reset control and truncated to the virtual viewport width.
- Connect and disconnect changes appear through the existing hot-plug scan without reopening the page.
- Keyboard assignment remains available while no device is detected.
- Existing MIDI note capture, bindings, popup behavior, and velocity thresholds remain unchanged.
- Crash context continues publishing count only; device names remain UI-only.
- Focused tests cover empty, connected, multiple-device, and refreshed snapshots.
- A local macOS no-device screenshot confirms the basic layout.
- Manual Windows verification is completed using the real hardware.
- Windows PR CI is green.

## Scope Estimate

One engineer day. The production change remains two small code edits plus focused tests and platform-appropriate verification.
