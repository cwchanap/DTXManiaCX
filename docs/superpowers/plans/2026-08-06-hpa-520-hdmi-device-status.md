# HPA-520 HDMI Device Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the currently connected drum-device status on the Drum Mapping page using the existing MIDI lifecycle, bounded text layout, and platform-appropriate verification.

**Architecture:** Add one read-only device-name property to `ModularInputManager`, then format and draw a compact snapshot from `DrumConfigStage` on every frame. Keep `MidiInputSource` as the sole owner of enumeration and hot-plug state; reuse `TextHelper.TruncateToWidth`; add no new timer, event bus, device selector, or Windows-specific discovery path.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, existing DryWetMIDI-backed input subsystem.

## Global Constraints

- User-facing copy deliberately says `HDMI device`; internal APIs remain named `MIDI`.
- Exact no-device copy: `HDMI device: None detected (keyboard still works)`.
- Multiple-device copy: `HDMI devices (N): <first name>, +<N-1> more`.
- Reuse `MidiInputSource.DeviceNames`; do not enumerate devices from the stage.
- Reuse the existing three-second hot-plug scan; do not add another refresh mechanism.
- Reuse `TextHelper.TruncateToWidth`; do not create another truncation helper.
- Draw the status below the Reset control and bound it to the virtual viewport.
- Device names are UI-only; telemetry and crash context continue using count only.
- The status is informational and must not block keyboard or MIDI capture.
- Do not change drum bindings, velocity thresholds, popup state, or input routing.
- The Windows-only pad-selection crash is out of scope. Capture its managed crash report and stop if it occurs during verification.
- Add no new production files or dependencies.

---

## File Structure

**Modify:**

- `DTXMania.Game/Lib/Input/ModularInputManager.cs`
  - Expose a read-only snapshot of connected MIDI device display names.
  - Clarify the count-only telemetry/crash-context contract.
- `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`
  - Format compact HDMI-device copy.
  - Draw it below Reset and truncate it to the virtual viewport width.
- `DTXMania.Test/Input/ModularInputManagerTests.cs`
  - Verify empty, sorted, and refreshed snapshots.
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`
  - Verify exact zero, singular, and compact plural copy.

No file split is needed. Each change stays with the existing owner of that responsibility.

---

### Task 1: Expose Connected MIDI Device Names

**Files:**
- Modify: `DTXMania.Game/Lib/Input/ModularInputManager.cs`
- Test: `DTXMania.Test/Input/ModularInputManagerTests.cs`

**Interfaces:**
- Consumes: `MidiInputSource.DeviceNames : IReadOnlyList<string>`
- Produces: `ModularInputManager.ConnectedMidiDeviceNames : IReadOnlyList<string>`
- Preserves: `ModularInputManager.ConnectedMidiDeviceCount : int` as the telemetry/crash-context boundary

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

- [ ] **Step 4: Run the focused macOS test class and verify failure**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~ModularInputManagerTests"
```

Expected: compilation fails because `ConnectedMidiDeviceNames` does not exist.

- [ ] **Step 5: Add the pass-through property and clarify the count contract**

Place next to the existing MIDI availability/count properties:

```csharp
/// <summary>
/// Gets the current number of connected MIDI devices for telemetry and crash context.
/// Display names are available separately through ConnectedMidiDeviceNames and must
/// not be added to crash diagnostics.
/// </summary>
public int ConnectedMidiDeviceCount => _midiInputSource?.DeviceCount ?? 0;

/// <summary>
/// Gets a read-only snapshot of connected MIDI device display names for UI use.
/// Device names must not be added to telemetry or crash context.
/// </summary>
public IReadOnlyList<string> ConnectedMidiDeviceNames =>
    _midiInputSource?.DeviceNames ?? Array.Empty<string>();
```

Do not call `RefreshDevices()` from either getter. `MidiInputSource.DeviceNames` already returns a new sorted list under its lock.

- [ ] **Step 6: Run the focused macOS test class and verify success**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~ModularInputManagerTests"
```

Expected: PASS.

- [ ] **Step 7: Review the boundary**

Confirm:

- No `IMidiInputDevice` instance leaves the input subsystem.
- No stable ID is exposed.
- No mutable internal collection is returned.
- Reading the property has no side effects.
- `Game1`, `CrashContextPublisher`, breadcrumbs, and crash-field policy are unchanged.
- The per-frame snapshot allocation is accepted; do not add a stale stage cache without performance evidence.

- [ ] **Step 8: Commit Task 1**

```bash
git add \
  DTXMania.Game/Lib/Input/ModularInputManager.cs \
  DTXMania.Test/Input/ModularInputManagerTests.cs
git commit -m "feat: expose connected midi device names"
```

---

### Task 2: Format and Render a Bounded HDMI Device Status

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`
- Test: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

**Interfaces:**
- Consumes: `ModularInputManager.ConnectedMidiDeviceNames : IReadOnlyList<string>`
- Consumes: `TextHelper.TruncateToWidth(string, float, IFont) : string`
- Produces: `DrumConfigStage.FormatHdmiDeviceStatus(IReadOnlyList<string>) : string`

- [ ] **Step 1: Add the exact no-device formatter test**

Use the direct private-static reflection pattern already used by `GetResetButtonRect` tests:

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

- [ ] **Step 3: Add the compact multiple-device formatter test**

```csharp
[Fact]
public void FormatHdmiDeviceStatus_MultipleDevices_PreservesFirstAndSummarizesRest()
{
    var method = typeof(DrumConfigStage).GetMethod(
        "FormatHdmiDeviceStatus",
        BindingFlags.NonPublic | BindingFlags.Static);
    Assert.NotNull(method);

    var status = (string)method!.Invoke(
        null,
        new object[] { new[] { "Alpha Kit", "Beta Kit", "Zeta Kit" } })!;

    Assert.Equal(
        "HDMI devices (3): Alpha Kit, +2 more",
        status);
}
```

- [ ] **Step 4: Run the focused stage test class and verify failure**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~DrumConfigStageTests"
```

Expected: formatter tests fail because `FormatHdmiDeviceStatus` does not exist.

- [ ] **Step 5: Implement the pure formatter**

Add near existing stage geometry helpers:

```csharp
private static string FormatHdmiDeviceStatus(IReadOnlyList<string> deviceNames)
{
    if (deviceNames == null || deviceNames.Count == 0)
        return "HDMI device: None detected (keyboard still works)";

    if (deviceNames.Count == 1)
        return $"HDMI device: {deviceNames[0]}";

    return $"HDMI devices ({deviceNames.Count}): {deviceNames[0]}, +{deviceNames.Count - 1} more";
}
```

Do not re-sort in the stage. The source snapshot already supplies its existing ordinal order.

- [ ] **Step 6: Draw below Reset and truncate to the virtual viewport**

In `OnDraw`, keep the heading and add the status in the same `_font != null` block:

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
    var deviceStatus = FormatHdmiDeviceStatus(connectedDeviceNames);
    var visibleDeviceStatus = TextHelper.TruncateToWidth(
        deviceStatus,
        vw - 40,
        _font);

    _font.DrawString(
        _spriteBatch,
        visibleDeviceStatus,
        new Vector2(20, 48),
        DarkText);
}
```

Rationale:

- The Reset control ends at virtual `y = 42`; `y = 48` leaves a visible gap.
- `vw - 40` preserves a 20-pixel margin on both sides.
- `TextHelper.TruncateToWidth` protects against a single unusually long device name.
- Do not add a stage cache, new truncation helper, event subscription, hit rectangle, or focusable control.

- [ ] **Step 7: Run the focused stage test class and verify success**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~DrumConfigStageTests"
```

Expected: PASS.

- [ ] **Step 8: Verify interaction remains unchanged by inspection**

Confirm the diff does not modify:

- `OnUpdate`.
- `OpenPopup`.
- `ProcessPopupCapture`.
- `ApplyCapture`.
- MIDI-threshold adjustment.
- Focus, hover, popup, or Reset geometry.

No automated draw-path or screenshot test is required. The formatter is unit-tested, width bounding reuses the existing tested helper, and visual wiring receives a local smoke check.

- [ ] **Step 9: Commit Task 2**

```bash
git add \
  DTXMania.Game/Lib/Stage/DrumConfigStage.cs \
  DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
git commit -m "feat: show connected drum device in mapping page"
```

---

### Task 3: Local macOS Visual Smoke

**Files:**
- No planned code changes.

**Interfaces:**
- Consumes: completed Tasks 1 and 2.
- Produces: visual evidence that the no-device line is positioned correctly without hardware.

- [ ] **Step 1: Run the macOS game with no MIDI device**

```bash
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```

- [ ] **Step 2: Navigate to the Drum Mapping page**

Use the normal UI or the existing Game API/MCP navigation tools:

1. Open Config.
2. Open Drum Mapping.
3. Leave MIDI hardware disconnected.

- [ ] **Step 3: Capture a screenshot**

Use the Game API/MCP screenshot path when available; otherwise use the OS screenshot tool.

Confirm:

- The exact text is `HDMI device: None detected (keyboard still works)`.
- The status line is below and clear of Reset.
- The status line does not clip at the right edge.
- The heading and drum-kit content remain readable.

Do not invent a runtime device-name injection seam for this task. The current Game API injects input events, not device discovery names. Compact plural copy is covered by the formatter test, and long-name width bounding is delegated to the existing tested `TextHelper`.

- [ ] **Step 4: Record the visual result in the implementation PR**

Add the screenshot or a concise verification note to the implementation PR description. Do not commit generated screenshot artifacts unless repository convention requires it.

---

### Task 4: Regression, CI, and Windows Hardware Verification

**Files:**
- No planned code changes.
- Do not fix the Windows-only crash in this branch.

**Interfaces:**
- Consumes: completed Tasks 1–3.
- Produces: green local macOS tests, green PR CI, and Windows hardware verification.

- [ ] **Step 1: Run the combined focused macOS suites**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~ModularInputManagerTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: PASS.

- [ ] **Step 2: Run the full local macOS gates**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: both commands pass.

- [ ] **Step 3: Push and confirm the PR checks**

Confirm at minimum:

- `build-and-test-windows` is green.
- `build-and-test-macos` is green.
- Existing required checks remain green.

Do not require a macOS-only implementation environment to run the Windows full suite locally. The Windows CI job is the authoritative automated Windows gate.

- [ ] **Step 4: Verify disconnected Windows behavior**

With the physical drum device disconnected:

1. Start the Windows game.
2. Open Config → Drum Mapping.
3. Confirm `HDMI device: None detected (keyboard still works)`.
4. Open a drum-lane capture popup.
5. Confirm keyboard capture still works.

- [ ] **Step 5: Verify connection while the page remains open**

1. Return to Drum Mapping if needed.
2. Connect the physical drum device without restarting the game.
3. Wait up to `GameConstants.Input.DeviceScanIntervalMs` plus one rendered frame.
4. Confirm its device name appears.
5. Confirm no page reopen is required.
6. If multiple ports appear, confirm the first name and `+N more` summary.

If the name remains absent, inspect `ConnectedMidiDeviceNames`. If empty, stop and create a separate Windows device-discovery investigation. Do not add a second discovery API here.

- [ ] **Step 6: Verify MIDI capture remains unchanged**

1. Select a drum lane.
2. Hit one pad on the connected device.
3. Confirm the existing `MIDI.<note>` binding is captured.
4. Confirm existing velocity-threshold controls behave unchanged.

- [ ] **Step 7: Verify disconnect while the page remains open**

1. Disconnect the physical device.
2. Wait for the existing hot-plug interval.
3. Confirm the exact no-device text returns.
4. Confirm the stage remains responsive.
5. Confirm keyboard capture still works.

- [ ] **Step 8: Apply the Windows crash stop condition**

If selecting a pad crashes:

1. Retain the generated managed crash report.
2. Record the exact reproduction sequence and Windows/device details.
3. Stop work on this branch.
4. Create or update the separate Windows crash issue.
5. Do not add a broad `try/catch` to `DrumConfigStage` or the input update loop.

- [ ] **Step 9: Review the final diff**

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
- [ ] Compact plural copy preserves the first device name and summarizes the rest.
- [ ] Status is drawn below Reset at `(20, 48)`.
- [ ] Status width is bounded with `TextHelper.TruncateToWidth`.
- [ ] Count XML documentation clearly identifies the telemetry/crash-context boundary.
- [ ] Names remain UI-only.
- [ ] Empty, sorted, and refreshed snapshots are tested.
- [ ] Singular and plural status formatting is tested.
- [ ] Focused and full macOS gates pass locally.
- [ ] Required PR checks, including Windows build/test CI, are green.
- [ ] macOS no-device screenshot confirms basic layout.
- [ ] Real Windows connect, disconnect, keyboard fallback, and MIDI capture are verified.
- [ ] Any Windows crash or absent backend name is moved to a separate investigation.
