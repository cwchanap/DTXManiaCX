# HPA-520 HDMI Device Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the currently connected drum-device names on the Drum Mapping page using the existing MIDI lifecycle and hot-plug scan.

**Architecture:** Add one read-only UI snapshot to `ModularInputManager`, then format and draw that snapshot from `DrumConfigStage` on every frame. Keep `MidiInputSource` as the sole owner of enumeration and hot-plug state; keep crash diagnostics on device count only.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, existing DryWetMIDI-backed input subsystem.

## Global Constraints

- User-facing copy keeps the requested `HDMI device` label; internal APIs remain named `MIDI`.
- Exact no-device copy: `HDMI device: None detected (keyboard still works)`.
- Reuse `MidiInputSource.DeviceNames`; do not enumerate devices from the stage.
- Reuse the existing three-second hot-plug scan; do not add another refresh mechanism.
- Device names are UI-only. Telemetry, breadcrumbs, and crash context continue using count only.
- The status is informational and must not block keyboard or MIDI capture.
- Do not change drum bindings, velocity thresholds, popup state, or input routing.
- The Windows-only pad-selection crash is out of scope. Retain its managed crash report and stop if it occurs.
- Add no new production files, dependencies, timers, event buses, device selectors, or rescan controls.

---

## File Structure

**Modify:**

- `DTXMania.Game/Lib/Input/ModularInputManager.cs`
  - Exposes connected MIDI display names for UI use.
  - Clarifies that the existing count property is the diagnostics boundary.
- `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`
  - Formats and draws the user-facing HDMI device status line.
- `DTXMania.Test/Input/ModularInputManagerTests.cs`
  - Verifies empty, sorted, and refreshed name snapshots.
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`
  - Verifies exact no-device, singular, and plural copy.

No file split is needed. Each production edit belongs to the existing owner of that responsibility.

---

### Task 1: Expose Connected MIDI Device Names

**Files:**
- Modify: `DTXMania.Game/Lib/Input/ModularInputManager.cs`
- Test: `DTXMania.Test/Input/ModularInputManagerTests.cs`

**Interfaces:**
- Consumes: `MidiInputSource.DeviceNames : IReadOnlyList<string>`
- Produces: `ModularInputManager.ConnectedMidiDeviceNames : IReadOnlyList<string>`
- Preserves: `ModularInputManager.ConnectedMidiDeviceCount : int` as the telemetry/crash-context value

- [ ] **Step 1: Add the no-device failing test**

Add near `ConnectedMidiDeviceCount_ShouldBeZeroBeforeDevicesAndRefreshToCurrentCount`:

```csharp
[Fact]
public void ConnectedMidiDeviceNames_NoDevices_ReturnsEmpty()
{
    Assert.Empty(_inputManager.ConnectedMidiDeviceNames);
}
```

- [ ] **Step 2: Add the sorted-names failing test**

```csharp
[Fact]
public void ConnectedMidiDeviceNames_WithDevices_ReturnsSortedNames()
{
    _midiBackend.SetDevices(
        new TestMidiInputDevice("midi-b", "Zeta Kit"),
        new TestMidiInputDevice("midi-a", "Alpha Kit"));

    _inputManager.Update(GameConstants.Input.DeviceScanIntervalMs / 1000.0);

    Assert.Equal(
        new[] { "Alpha Kit", "Zeta Kit" },
        _inputManager.ConnectedMidiDeviceNames);
}
```

- [ ] **Step 3: Add the refresh failing test**

```csharp
[Fact]
public void ConnectedMidiDeviceNames_AfterRefresh_ReturnsUpdatedSnapshot()
{
    _midiBackend.SetDevices(
        new TestMidiInputDevice("midi-a", "First Kit"));
    _inputManager.Update(GameConstants.Input.DeviceScanIntervalMs / 1000.0);

    Assert.Equal(
        new[] { "First Kit" },
        _inputManager.ConnectedMidiDeviceNames);

    _midiBackend.SetDevices(
        new TestMidiInputDevice("midi-b", "Replacement Kit"));
    _inputManager.Update(GameConstants.Input.DeviceScanIntervalMs / 1000.0);

    Assert.Equal(
        new[] { "Replacement Kit" },
        _inputManager.ConnectedMidiDeviceNames);
}
```

- [ ] **Step 4: Run the focused tests and verify failure**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~ModularInputManagerTests"
```

Windows:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj `
  --filter "FullyQualifiedName~ModularInputManagerTests"
```

Expected: compilation fails because `ConnectedMidiDeviceNames` does not exist.

- [ ] **Step 5: Update the count-property contract**

Replace the existing `ConnectedMidiDeviceCount` XML documentation with:

```csharp
/// <summary>
/// Gets the current number of connected MIDI devices for telemetry and crash context.
/// Display names are available separately through ConnectedMidiDeviceNames and must
/// not be added to crash diagnostics.
/// </summary>
public int ConnectedMidiDeviceCount => _midiInputSource?.DeviceCount ?? 0;
```

Do not change any `Game1` or `CrashContextPublisher.PublishInput` call sites.

- [ ] **Step 6: Add the minimal UI snapshot property**

Place next to `ConnectedMidiDeviceCount`:

```csharp
/// <summary>
/// Gets a read-only snapshot of connected MIDI device display names for UI use.
/// Device names must not be added to telemetry or crash context.
/// </summary>
public IReadOnlyList<string> ConnectedMidiDeviceNames =>
    _midiInputSource?.DeviceNames ?? Array.Empty<string>();
```

Do not call `RefreshDevices()` from this getter. `MidiInputSource.DeviceNames` already returns a new sorted list under its lock.

- [ ] **Step 7: Run the focused tests and verify success**

Run the same focused command for the current platform.

Expected: all `ModularInputManagerTests` pass.

- [ ] **Step 8: Review the boundary**

Confirm:

- No `IMidiInputDevice` instances leave the input subsystem.
- No stable IDs are exposed.
- No mutable internal collection is returned.
- Reading the property has no side effects.
- `CrashContextPublisher.PublishInput` still receives only `ConnectedMidiDeviceCount`.

- [ ] **Step 9: Commit Task 1**

```bash
git add \
  DTXMania.Game/Lib/Input/ModularInputManager.cs \
  DTXMania.Test/Input/ModularInputManagerTests.cs
git commit -m "feat: expose connected midi device names"
```

---

### Task 2: Format and Render HDMI Device Status

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`
- Test: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

**Interfaces:**
- Consumes: `ModularInputManager.ConnectedMidiDeviceNames : IReadOnlyList<string>`
- Produces: private `DrumConfigStage.FormatHdmiDeviceStatus(IReadOnlyList<string>) : string`

The formatter remains private to match the existing private-static helper pattern in `DrumConfigStageTests`. Do not widen production visibility solely for testing.

- [ ] **Step 1: Add the no-device formatter test**

Use the direct private-static reflection pattern already present in this test file:

```csharp
[Fact]
public void FormatHdmiDeviceStatus_NoDevices_ShowsKeyboardFallback()
{
    var method = typeof(DrumConfigStage).GetMethod(
        "FormatHdmiDeviceStatus",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert.NotNull(method);

    var status = (string)method!.Invoke(
        null,
        new object[] { Array.Empty<string>() })!;

    Assert.Equal(
        "HDMI device: None detected (keyboard still works)",
        status);
}
```

- [ ] **Step 2: Add the single-device formatter test**

```csharp
[Fact]
public void FormatHdmiDeviceStatus_OneDevice_ShowsDeviceName()
{
    var method = typeof(DrumConfigStage).GetMethod(
        "FormatHdmiDeviceStatus",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert.NotNull(method);

    var status = (string)method!.Invoke(
        null,
        new object[] { new[] { "Roland TD-17" } })!;

    Assert.Equal("HDMI device: Roland TD-17", status);
}
```

- [ ] **Step 3: Add the multiple-device formatter test**

```csharp
[Fact]
public void FormatHdmiDeviceStatus_MultipleDevices_ShowsCountAndNames()
{
    var method = typeof(DrumConfigStage).GetMethod(
        "FormatHdmiDeviceStatus",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert.NotNull(method);

    var status = (string)method!.Invoke(
        null,
        new object[] { new[] { "Alpha Kit", "Zeta Kit" } })!;

    Assert.Equal(
        "HDMI devices (2): Alpha Kit, Zeta Kit",
        status);
}
```

- [ ] **Step 4: Run the formatter tests and verify failure**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~DrumConfigStageTests"
```

Windows:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj `
  --filter "FullyQualifiedName~DrumConfigStageTests"
```

Expected: formatter tests fail because `FormatHdmiDeviceStatus` does not exist.

- [ ] **Step 5: Implement the pure formatter**

Add near the existing stage geometry helpers:

```csharp
private static string FormatHdmiDeviceStatus(IReadOnlyList<string> deviceNames)
{
    if (deviceNames == null || deviceNames.Count == 0)
        return "HDMI device: None detected (keyboard still works)";

    if (deviceNames.Count == 1)
        return $"HDMI device: {deviceNames[0]}";

    return $"HDMI devices ({deviceNames.Count}): {string.Join(", ", deviceNames)}";
}
```

Do not add normalization, truncation, or filtering in this task.

- [ ] **Step 6: Draw the live device status**

In `OnDraw`, replace the existing single-line `_font` condition with:

```csharp
if (_font != null)
{
    _font.DrawString(
        _spriteBatch,
        "DRUM MAPPING  -  click a piece, then hit your input.  Back: save & exit",
        new Vector2(20, 16),
        DarkText);

    var connectedDeviceNames =
        _input?.ModularInputManager.ConnectedMidiDeviceNames
        ?? Array.Empty<string>();
    _font.DrawString(
        _spriteBatch,
        FormatHdmiDeviceStatus(connectedDeviceNames),
        new Vector2(20, 40),
        DarkText);
}
```

Keep the read inside `OnDraw`. Do not add a stage cache, event subscription, hit rectangle, or focusable control.

- [ ] **Step 7: Run the focused stage tests and verify success**

Run the same focused command for the current platform.

Expected: all `DrumConfigStageTests` pass.

- [ ] **Step 8: Verify interaction remains unchanged by inspection**

Confirm the diff does not modify:

- `OnUpdate`.
- `OpenPopup`.
- `ProcessPopupCapture`.
- `ApplyCapture`.
- MIDI-threshold adjustment.
- Focus, hover, popup, or reset geometry.

No automated draw-path test is required. `OnDraw` is excluded from coverage, the wiring is a small adjacent read-format-draw block, and Windows hardware verification is the integration gate.

- [ ] **Step 9: Commit Task 2**

```bash
git add \
  DTXMania.Game/Lib/Stage/DrumConfigStage.cs \
  DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
git commit -m "feat: show connected drum device in mapping page"
```

---

### Task 3: Cross-Platform Regression and Windows Hardware Verification

**Files:**
- No planned code changes.
- Do not fix the Windows-only crash in this branch.

**Interfaces:**
- Consumes: completed Tasks 1 and 2.
- Produces: verified connected/disconnected behavior and a retained crash report if the separate crash reproduces.

- [ ] **Step 1: Run both focused suites on macOS**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~ModularInputManagerTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: PASS with no hardware dependency.

- [ ] **Step 2: Run both focused suites on Windows**

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj `
  --filter "FullyQualifiedName~ModularInputManagerTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: PASS.

- [ ] **Step 3: Run the exact repository-wide macOS gates**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: both commands pass.

- [ ] **Step 4: Run the exact repository-wide Windows gates**

```powershell
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

Expected: both commands pass.

No gameplay E2E addition is required for this informational text-only feature.

- [ ] **Step 5: Verify disconnected Windows behavior**

With the physical drum device disconnected:

1. Start the Windows game.
2. Open Config → Drum Mapping.
3. Confirm the exact text `HDMI device: None detected (keyboard still works)`.
4. Open a drum-lane capture popup.
5. Confirm keyboard capture still works.

- [ ] **Step 6: Verify connection while the page remains open**

1. Return to the Drum Mapping page if needed.
2. Connect the physical drum device without restarting the game.
3. Wait up to `GameConstants.Input.DeviceScanIntervalMs` plus one rendered frame.
4. Confirm the device name appears.
5. Confirm no page reopen is required.

If the name remains absent, inspect `ConnectedMidiDeviceNames`. If it is empty, stop and create a separate Windows device-discovery investigation. Do not add a second discovery API here.

- [ ] **Step 7: Verify MIDI capture remains unchanged**

1. Select a drum lane.
2. Hit one pad on the connected device.
3. Confirm the existing `MIDI.<note>` binding is captured.
4. Confirm existing velocity-threshold controls behave unchanged.

- [ ] **Step 8: Verify disconnect while the page remains open**

1. Disconnect the physical device.
2. Wait for the existing hot-plug interval.
3. Confirm the exact no-device text returns.
4. Confirm the stage remains responsive.
5. Confirm keyboard capture still works.

- [ ] **Step 9: Apply the Windows crash stop condition**

If selecting a pad crashes:

1. Retain the generated managed crash report.
2. Record the exact reproduction sequence and Windows/device details.
3. Stop work on this branch.
4. Create or update the separate Windows crash issue.
5. Do not add a broad `try/catch` to `DrumConfigStage` or the input update loop.

- [ ] **Step 10: Review the final diff**

```bash
git diff main...HEAD -- \
  DTXMania.Game/Lib/Input/ModularInputManager.cs \
  DTXMania.Game/Lib/Stage/DrumConfigStage.cs \
  DTXMania.Test/Input/ModularInputManagerTests.cs \
  DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
```

Confirm there are no changes to diagnostics payloads, input routing, popup behavior, bindings, thresholds, or unrelated files.

---

## Completion Checklist

- [ ] Exact no-device copy is implemented and tested.
- [ ] Count XML documentation clearly identifies the telemetry/crash-context boundary.
- [ ] Names remain UI-only.
- [ ] Empty, sorted, and refreshed snapshots are tested.
- [ ] Singular and plural status formatting is tested.
- [ ] Focused macOS and Windows tests pass.
- [ ] Full macOS and Windows build/test commands pass.
- [ ] Real Windows connect, disconnect, keyboard fallback, and MIDI capture are verified.
- [ ] Any Windows crash or absent backend name is moved to a separate investigation.
