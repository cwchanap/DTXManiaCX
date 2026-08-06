# HPA-520 HDMI Device Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the currently connected drum device names on the Drum Mapping page using the existing MIDI device lifecycle and hot-plug scan.

**Architecture:** Add one read-only device-name property to `ModularInputManager`, then format and draw that snapshot from `DrumConfigStage` on every frame. Keep `MidiInputSource` as the sole owner of enumeration and hot-plug state; add no new timer, event bus, device selector, or Windows-specific discovery path.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, existing DryWetMIDI-backed input subsystem.

## Global Constraints

- User-facing copy says `HDMI device`; internal APIs remain named `MIDI`.
- Reuse `MidiInputSource.DeviceNames`; do not enumerate devices from the stage.
- Reuse the existing three-second hot-plug scan; do not add another refresh mechanism.
- The status is informational and must not block keyboard or MIDI capture.
- Do not change drum bindings, velocity thresholds, popup state, or input routing.
- The Windows-only pad-selection crash is out of scope. Capture its managed crash report and stop if it occurs during verification.
- Add no new production files or dependencies.

---

## File Structure

**Modify:**

- `DTXMania.Game/Lib/Input/ModularInputManager.cs`
  - Exposes a read-only snapshot of currently connected MIDI device display names.
- `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`
  - Formats and draws the user-facing HDMI device status line.
- `DTXMania.Test/Input/ModularInputManagerTests.cs`
  - Verifies empty, sorted, and refreshed device-name snapshots.
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`
  - Verifies exact disconnected, singular, and plural status copy.

No file split is needed. Each production change is a small addition to the existing owner of that responsibility.

---

### Task 1: Expose Connected MIDI Device Names

**Files:**
- Modify: `DTXMania.Game/Lib/Input/ModularInputManager.cs`
- Test: `DTXMania.Test/Input/ModularInputManagerTests.cs`

**Interfaces:**
- Consumes: `MidiInputSource.DeviceNames : IReadOnlyList<string>`
- Produces: `ModularInputManager.ConnectedMidiDeviceNames : IReadOnlyList<string>`

- [ ] **Step 1: Add the no-device failing test**

Add this test near `ConnectedMidiDeviceCount_ShouldBeZeroBeforeDevicesAndRefreshToCurrentCount`:

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

- [ ] **Step 4: Run the focused tests and verify they fail**

Mac:

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

- [ ] **Step 5: Add the minimal pass-through property**

Place this property next to `ConnectedMidiDeviceCount`:

```csharp
/// <summary>
/// Gets a read-only snapshot of connected MIDI device display names.
/// Returns an empty snapshot when no MIDI source or device is available.
/// </summary>
public IReadOnlyList<string> ConnectedMidiDeviceNames =>
    _midiInputSource?.DeviceNames ?? Array.Empty<string>();
```

Do not call `RefreshDevices()` from this getter. `MidiInputSource.DeviceNames` already returns a new sorted list under its lock.

- [ ] **Step 6: Run the focused tests and verify they pass**

Run the same focused command for the current platform.

Expected: all `ModularInputManagerTests` pass.

- [ ] **Step 7: Review the boundary**

Confirm:

- No `IMidiInputDevice` instances leave the input subsystem.
- No stable IDs are exposed.
- No mutable internal collection is returned.
- Reading the property has no side effects.

- [ ] **Step 8: Commit Task 1**

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
- Produces: `DrumConfigStage.FormatHdmiDeviceStatus(IReadOnlyList<string>) : string`

- [ ] **Step 1: Add the disconnected formatter test**

Use the direct private-static reflection pattern already used in `DrumConfigStageTests`:

```csharp
[Fact]
public void FormatHdmiDeviceStatus_NoDevices_ShowsNotConnected()
{
    var method = typeof(DrumConfigStage).GetMethod(
        "FormatHdmiDeviceStatus",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert.NotNull(method);

    var status = (string)method!.Invoke(
        null,
        new object[] { Array.Empty<string>() })!;

    Assert.Equal("HDMI device: Not connected", status);
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

- [ ] **Step 4: Run the formatter tests and verify they fail**

Mac:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~DrumConfigStageTests"
```

Windows:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj `
  --filter "FullyQualifiedName~DrumConfigStageTests"
```

Expected: the formatter tests fail because `FormatHdmiDeviceStatus` does not exist.

- [ ] **Step 5: Implement the pure formatter**

Add this method near the existing stage geometry and formatting helpers:

```csharp
private static string FormatHdmiDeviceStatus(IReadOnlyList<string> deviceNames)
{
    if (deviceNames == null || deviceNames.Count == 0)
        return "HDMI device: Not connected";

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

Keep the read inside `OnDraw`. Do not add a stage field or populate names in `OnActivate`.

- [ ] **Step 7: Run the focused stage tests and verify they pass**

Run the same focused command for the current platform.

Expected: all `DrumConfigStageTests` pass.

- [ ] **Step 8: Verify interaction remains unchanged by inspection**

Confirm the change does not modify:

- `OnUpdate`.
- `OpenPopup`.
- `ProcessPopupCapture`.
- `ApplyCapture`.
- MIDI threshold adjustment.
- Focus, hover, or reset geometry.

The new line must have no hit rectangle or input handling.

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

- [ ] **Step 1: Run both focused suites together on macOS**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~ModularInputManagerTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: PASS with no hardware dependency.

- [ ] **Step 2: Run both focused suites together on Windows**

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj `
  --filter "FullyQualifiedName~ModularInputManagerTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: PASS.

- [ ] **Step 3: Run normal repository-required gates**

Run the build and test commands required by the repository for both supported platforms. Do not weaken or skip existing CI filters.

Expected: all required gates pass.

- [ ] **Step 4: Verify disconnected Windows behavior**

With the physical drum device disconnected:

1. Start the Windows game.
2. Open Config → Drum Mapping.
3. Confirm the page shows `HDMI device: Not connected`.
4. Open a drum-lane capture popup.
5. Confirm keyboard capture still works.

- [ ] **Step 5: Verify connection while the page remains open**

1. Connect the physical drum device without restarting the game.
2. Wait up to `GameConstants.Input.DeviceScanIntervalMs` plus one rendered frame.
3. Confirm the device name appears.
4. Confirm no page reopen is required.

If the device does not appear, inspect `ConnectedMidiDeviceNames`. If it remains empty, stop and create a separate Windows device-discovery investigation. Do not add a second discovery API here.

- [ ] **Step 6: Verify MIDI capture remains unchanged**

1. Select a drum lane.
2. Hit one pad on the connected device.
3. Confirm the existing `MIDI.<note>` binding is captured.
4. Confirm existing velocity-threshold controls behave unchanged.

- [ ] **Step 7: Verify disconnect while the page remains open**

1. Disconnect the physical device.
2. Wait for the existing hot-plug interval.
3. Confirm the status returns to `HDMI device: Not connected`.
4. Confirm the stage remains responsive.
5. Confirm keyboard capture still works.

- [ ] **Step 8: Apply the Windows crash stop condition**

If selecting a pad crashes:

1. Retain the generated managed crash report.
2. Record the exact reproduction sequence and Windows/device details.
3. Stop work on this branch.
4. Create or update the separate Windows crash issue.
5. Do not add a broad `try/catch` to `DrumConfigStage` or the input update loop.

- [ ] **Step 9: Review the final implementation diff**

```bash
git diff main...HEAD -- \
  DTXMania.Game/Lib/Input/ModularInputManager.cs \
  DTXMania.Game/Lib/Stage/DrumConfigStage.cs \
  DTXMania.Test/Input/ModularInputManagerTests.cs \
  DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
```

Confirm the production diff contains only:

- One read-only input property.
- One pure formatter.
- One status-line draw call.
- Focused tests.

- [ ] **Step 10: Prepare the implementation PR summary**

The implementation PR body must state:

- Connected names come from the existing MIDI backend.
- The page updates through the existing hot-plug scan.
- No input behavior or device lifecycle changed.
- Windows hardware verification results.
- The separate crash status, without claiming it was fixed.

---

## Final Acceptance Checklist

- [ ] No-device state shows `HDMI device: Not connected`.
- [ ] One device shows its display name.
- [ ] Multiple devices show count and names.
- [ ] Device changes appear without reopening the page.
- [ ] Keyboard assignment remains available while disconnected.
- [ ] MIDI capture and velocity thresholds remain unchanged.
- [ ] Mac focused tests pass.
- [ ] Windows focused tests pass.
- [ ] Repository-required gates pass.
- [ ] Actual Windows hardware verification is recorded.
- [ ] Any Windows crash is deferred with its managed crash report.
