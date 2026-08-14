# HPA-619 Windows Fullscreen Restore and Drum Mapping Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stabilize Windows fullscreen restore and make drum hover, keyboard focus, and popup lane selection match the rendered drum image in windowed and fullscreen modes.

**Architecture:** Keep the existing fixed 1280×720 virtual render target and letterbox transform. Task 1 makes the smallest native-window change by disabling MonoGame's Windows hardware mode switch; Task 2 keeps client-to-backbuffer normalization separate from the existing backbuffer-to-virtual mapping; Task 3 keeps keyboard focus in authored visual-zone order and converts explicitly to lane IDs. No task changes `GraphicsManager`, the windowed configuration, the render target, or the drum content contract.

**Tech Stack:** .NET 8, MonoGame 3.8.x, xUnit, existing `BaseGame`, `DrumKitLayout`, and drum-configuration components.

## Global Constraints

- Keep the fixed 1280×720 render target and the existing `CalculateLetterboxDestination`/`WindowToVirtualCoordinates` letterbox behavior.
- Preserve the configured windowed size in `ConfigData.ScreenWidth` and `ConfigData.ScreenHeight`; fullscreen must not overwrite those values.
- Do not modify `DTXMania.Game/Lib/Graphics/GraphicsManager.cs`, graphics settings ownership, or any graphics event/reapply path in this plan.
- Do not add a window-mode manager, display service, input-coordinate service, second drum layout model, or other broad abstraction. The two explicitly requested static coordinate/lane helpers remain narrow, pure helpers.
- Preserve existing drum bindings, MIDI capture, velocity thresholds, popup content, hover/selected lane IDs, and skin assets.
- Keep the three tasks independently reviewable. Each task has its own focused verification and conventional commit.
- Windows-focused and full unit-test commands use `DTXMania.Test/DTXMania.Test.csproj`. The macOS-safe test project remains `DTXMania.Test/DTXMania.Test.Mac.csproj`.

## Root-Cause Contract

The implementation separates two confirmed managed-code defects from the native fullscreen diagnosis:

1. **Primary native diagnosis under test:** Windows exclusive fullscreen restore instability is caused by MonoGame's default `HardwareModeSwitch` behavior. Task 1 changes only that property. A Windows manual smoke is required to confirm that soft fullscreen resolves every squeeze, deactivation, and hang sequence before any native scope is expanded.
2. **Confirmed coordinate defect:** `Mouse.GetState()` supplies WinForms client coordinates, while `BaseGame.MapMouseToVirtual()` currently sends them directly to a backbuffer viewport transform. Task 2 normalizes client pixels first.
3. **Confirmed zone/lane defect:** `DrumConfigStage` stores keyboard focus as a visual-zone index, but current activation and rendering call sites treat that index as a lane ID. Task 3 converts explicitly without changing authored zone order.

If the Task 1 manual symptoms persist, capture `Window.ClientBounds`, backbuffer dimensions, viewport bounds, fullscreen state, and hardware-mode state before proposing any additional native or graphics changes. Record the MonoGame package version resolved by restore with the manual evidence.

---

## Task 1: Disable Windows Hardware Mode Switching for Fullscreen Restore

**Files:**

- Modify: `DTXMania.Game/Game1.cs`

**Interfaces:**

- Consumes: the existing `GraphicsDeviceManager` construction in the `BaseGame` constructor.
- Produces: a Windows-only `HardwareModeSwitch = false` setting that is in place before any later graphics initialization or settings application.

### Step 1: Set the Windows-only hardware-mode flag

- [ ] In the `BaseGame` constructor, immediately after constructing `GraphicsDeviceManager`, add exactly this guarded assignment:

```csharp
_graphicsDeviceManager = new GraphicsDeviceManager(this);
if (OperatingSystem.IsWindows())
{
    _graphicsDeviceManager.HardwareModeSwitch = false;
}
```

- [ ] Leave the rest of `Game1.cs` unchanged. Do not expand this task with graphics lifecycle logic, alternate sizing policy, logging, or a unit-test seam for this native property.

### Step 2: Build both game targets

- [ ] Run the Windows build:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
```

- [ ] Run the macOS build to prove the platform guard remains cross-platform compilable:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

There is no RED/GREEN unit-test cycle for this task; a test-only seam would not exercise the native fullscreen behavior.

### Step 3: Commit the isolated native change

- [ ] Commit only `Game1.cs`:

```bash
git add DTXMania.Game/Game1.cs
git commit -m "fix: disable Windows hardware fullscreen switching"
```

### Step 4: Use the Windows focus-loss smoke as the confirmation gate

- [ ] On Windows, launch the game in windowed mode, toggle fullscreen with Alt+Enter, and repeat Win+D, Alt+Tab, minimize, and restore sequences. Confirm that the 16:9 virtual scene remains unsqueezed, the game remains active, and no deactivation or hang sequence occurs.
- [ ] If all sequences pass, keep Task 1 limited to the constructor assignment. If any sequence fails, capture the diagnostic fields named in the Root-Cause Contract before expanding scope; do not add graphics-manager or backbuffer policy changes to this task.

---

## Task 2: Normalize Client Mouse Coordinates Before Virtual Hit-Testing

**Files:**

- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.Test/BaseGameTests.cs`

**Interfaces:**

- Produces: `internal static Point? ClientToBackBufferCoordinates(Point clientPoint, Point clientSize, Rectangle backBufferViewport)`.
- Produces: `[ExcludeFromCodeCoverage] protected virtual Point? TryGetClientSize()` in `BaseGame`.
- Consumes: the existing `GetGameWindow()`, `TryGetViewportBounds()`, and `WindowToVirtualCoordinates()` seams.

### Step 1: Add RED tests for the pure coordinate helper

- [ ] In `BaseGameTests`, add these tests for `ClientToBackBufferCoordinates`:

```csharp
ClientToBackBufferCoordinates_SameSizeIsIdentity()
ClientToBackBufferCoordinates_ScalesClientIntoViewport()
ClientToBackBufferCoordinates_PointOutsideClientReturnsNull()
ClientToBackBufferCoordinates_NonPositiveClientOrViewportReturnsNull()
```

- [ ] Use a viewport with a non-zero origin in at least one case. Assert that equal client/viewport sizes preserve the point, a 1920×1080 client scales into a 1280×720 viewport, negative or right/bottom-edge client points return `null`, and any non-positive client or viewport dimension returns `null`.
- [ ] Include a non-16:9 integration case proving normalization happens before existing black-bar rejection:

```csharp
MapMouseToVirtual_Non16By9ClientNormalizesBeforeBlackBarRejection()
```

- [ ] Keep the existing raw fallback coverage and add the explicit regression:

```csharp
MapMouseToVirtual_WithoutGraphicsManager_ShouldReturnPointAsIs()
MapMouseToVirtual_WithGraphicsManagerButNoViewport_ShouldReturnPointAsIs()
MapMouseToVirtual_WithViewportButNoClientSize_PreservesExistingViewportMapping()
```

`MapMouseToVirtual_WithViewportButNoClientSize_PreservesExistingViewportMapping` must configure a viewport but make `TryGetClientSize()` return `null`; the raw point is then treated as already in backbuffer space and still passed through `WindowToVirtualCoordinates`.

- [ ] Add a client/backbuffer mismatch integration test:

```csharp
MapMouseToVirtual_WhenClientAndBackBufferSizesDiffer_NormalizesBeforeLetterboxMapping()
```

For a 1920×1080 client and 1280×720 backbuffer, assert that client center `(960, 540)` maps to virtual `(640, 360)` rather than being interpreted as a direct backbuffer coordinate. Add a case where a real client size has non-positive dimensions and assert that the result is `null`.

### Step 2: Run the RED BaseGame tests

- [ ] Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~BaseGameTests"
```

Expected result: the new helper, client-size seam, and normalization assertions fail because they are not implemented yet; the pre-existing raw fallback tests continue to describe the current behavior.

### Step 3: Implement the pure client-to-backbuffer helper

- [ ] Add this internal helper to `Game1`:

```csharp
internal static Point? ClientToBackBufferCoordinates(
    Point clientPoint,
    Point clientSize,
    Rectangle backBufferViewport)
{
    if (clientSize.X <= 0 || clientSize.Y <= 0 ||
        backBufferViewport.Width <= 0 || backBufferViewport.Height <= 0)
        return null;

    if (clientPoint.X < 0 || clientPoint.Y < 0 ||
        clientPoint.X >= clientSize.X || clientPoint.Y >= clientSize.Y)
        return null;

    int backBufferX = backBufferViewport.X +
        (int)Math.Round(clientPoint.X * backBufferViewport.Width / (double)clientSize.X);
    int backBufferY = backBufferViewport.Y +
        (int)Math.Round(clientPoint.Y * backBufferViewport.Height / (double)clientSize.Y);

    if (backBufferX >= backBufferViewport.Right)
        backBufferX = backBufferViewport.Right - 1;
    if (backBufferY >= backBufferViewport.Bottom)
        backBufferY = backBufferViewport.Bottom - 1;

    return new Point(backBufferX, backBufferY);
}
```

The helper must include the viewport origin and clamp rounding at the final right/bottom pixel. It must not perform the virtual-resolution or letterbox transform.

### Step 4: Add the client-size seam and preserve mapping fallbacks

- [ ] Add:

```csharp
[ExcludeFromCodeCoverage]
protected virtual Point? TryGetClientSize()
{
    var window = GetGameWindow();
    if (window == null)
        return null;

    return new Point(window.ClientBounds.Width, window.ClientBounds.Height);
}
```

- [ ] Update `MapMouseToVirtual` in this order:

1. If `_graphicsManager` is unavailable, return the raw point exactly as today.
2. Read `TryGetViewportBounds()`. If no viewport is available, return the raw point exactly as today.
3. Read `TryGetClientSize()`.
4. If a real client size is present with a non-positive width or height, return `null`.
5. If the client size is present and positive, call `ClientToBackBufferCoordinates`; return `null` when that helper rejects the point.
6. If the client size is unavailable (`null`), treat the input as already in backbuffer space without scaling.
7. In both client-size branches, pass the resulting backbuffer point through the existing `WindowToVirtualCoordinates` helper and return its result.

Do not fold the two transforms into a new abstraction, and do not alter the existing letterbox calculation.

### Step 5: Run GREEN tests and the full Windows unit project

- [ ] Re-run the focused tests:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~BaseGameTests"
```

Expected result: all BaseGame coordinate, fallback, invalid-size, and non-16:9 tests pass.

- [ ] Run the full Windows unit project:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

### Step 6: Commit the coordinate-only change

- [ ] Commit only the two Task 2 files:

```bash
git add DTXMania.Game/Game1.cs DTXMania.Test/BaseGameTests.cs
git commit -m "fix: normalize client mouse coordinates"
```

---

## Task 3: Keep Drum Focus as an Element Index and Resolve Lane IDs Explicitly

**Files:**

- Modify: `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitLayout.cs`
- Modify: `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`
- Modify: `DTXMania.Test/Stage/DrumConfig/DrumKitLayoutTests.cs`
- Modify: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

**Interfaces:**

- Produces: `internal static int GetLaneForZoneIndex(int zoneIndex)`.
- Produces: `internal static int FindZoneIndexByLane(int lane)`.
- Preserves: `_focusedElementIndex` as the stage's private focus field, with `0..ZoneCount-1` meaning authored zones and `ResetActionIndex` meaning Reset.

### Step 1: Add RED layout conversion tests

- [ ] In `DrumKitLayoutTests`, add:

```csharp
GetLaneForZoneIndex_ReturnsEachAuthoredZoneLane()
FindZoneIndexByLane_RoundTripsEveryAuthoredZone()
GetLaneForZoneIndex_ResetAndInvalidIndicesReturnMinusOne()
FindZoneIndexByLane_InvalidLaneReturnsMinusOne()
```

- [ ] Assert the non-identity mappings directly:

```text
GetLaneForZoneIndex(0) == 5
GetLaneForZoneIndex(2) == 9
FindZoneIndexByLane(4) == 5
```

- [ ] Keep Reset outside the lane mapping: `GetLaneForZoneIndex(DrumKitLayout.ResetActionIndex)` returns `-1`, and negative or out-of-range arguments return `-1`.

### Step 2: Add RED stage regression tests

- [ ] Rename every reflection-based `_focusIndex` reference in `DrumConfigStageTests` to `_focusedElementIndex` and assert the field's element-index contract.
- [ ] Update `ActivateFocusedElement_WhenZoneFocused_OpensPopupForThatLane` to focus visual zone `0` and assert popup lane `5`; do not use an identity-valued index.
- [ ] Add a second activation or mouse-click regression for visual zone `2` and assert popup lane `9`.
- [ ] Update the snare case to assert clicked lane `4`, focused element index `5`, and popup lane `4`.
- [ ] Add an invalid-lane `OpenPopup` test that snapshots `_selectedLane`, `_focusedElementIndex`, popup-open state, and `_skipCaptureThisFrame`, calls `OpenPopup` with an invalid lane, and asserts that every value is unchanged.
- [ ] Keep existing navigation assertions for authored element indices `0..ZoneCount-1` and `ResetActionIndex`; navigation must not be rewritten in lane-ID terms.

### Step 3: Run the RED drum tests

- [ ] Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~DrumKitLayoutTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected result: the non-identity activation/click assertions and the renamed field references fail against the current index/lane conflation.

### Step 4: Implement the explicit layout conversions

- [ ] Add these methods to `DrumKitLayout`:

```csharp
internal static int GetLaneForZoneIndex(int zoneIndex)
{
    if (zoneIndex < 0 || zoneIndex >= Zones.Count)
        return -1;

    return Zones[zoneIndex].Lane;
}

internal static int FindZoneIndexByLane(int lane)
{
    for (int i = 0; i < Zones.Count; i++)
    {
        if (Zones[i].Lane == lane)
            return i;
    }

    return -1;
}
```

Do not reorder `Zones`, add a dictionary, or cache a second lane table.

### Step 5: Correct stage state ownership and popup dispatch

- [ ] Rename the private field in `DrumConfigStage` to:

```csharp
private int _focusedElementIndex;
```

Use it only as an element index: `0..ZoneCount-1` for zones and `DrumKitLayout.ResetActionIndex` for Reset. Update arrow/Tab navigation, reset-button focus, reset detection, and tests to use this contract.

- [ ] Implement `OpenPopup(int lane)` with the conversion before any mutation:

```csharp
private void OpenPopup(int lane)
{
    int zoneIndex = DrumKitLayout.FindZoneIndexByLane(lane);
    if (zoneIndex < 0)
        return;

    _selectedLane = lane;
    _focusedElementIndex = zoneIndex;
    _popup!.Open(lane);
    _skipCaptureThisFrame = true;
}
```

An invalid lane must return before changing selected lane, focus, popup state, or the skip flag. For a valid lane, preserve the lane ID in `_selectedLane` and in the popup call while storing the visual zone index in `_focusedElementIndex`.

- [ ] Update `ActivateFocusedElement()` so Reset still calls `ResetDrumBindingsToDefault()`, while a zone resolves `GetLaneForZoneIndex(_focusedElementIndex)` and calls `OpenPopup` only for a non-negative lane.
- [ ] Update `OnDraw()` so `LaneHighlights.FocusedLane` receives the resolved lane ID from `GetLaneForZoneIndex`, or `-1` for Reset/inactive keyboard focus. Keep `_hoveredLane` and `_selectedLane` as lane IDs because hit-testing and renderer highlights already use those IDs.

### Step 6: Run GREEN drum tests and the full Windows unit project

- [ ] Re-run the focused tests:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~DrumKitLayoutTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected result: all authored-order, non-identity, invalid-lane, Reset, hover, and popup assertions pass.

- [ ] Run the full Windows unit project:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

### Step 7: Commit the isolated drum mapping change

- [ ] Commit the four Task 3 files:

```bash
git add DTXMania.Game/Lib/Stage/DrumConfig/DrumKitLayout.cs \
        DTXMania.Game/Lib/Stage/DrumConfigStage.cs \
        DTXMania.Test/Stage/DrumConfig/DrumKitLayoutTests.cs \
        DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
git commit -m "fix: map drum focus elements to lane IDs"
```

---

## Final Automated Verification

- [ ] Build the Windows target and run its full unit suite:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

- [ ] Build the macOS target and run the Mac-safe unit suite:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

- [ ] Confirm the diff does not modify `GraphicsManager`, `GraphicsExtensions`, `IGraphicsManager`, configuration schema, drum binding behavior, MIDI/velocity behavior, popup content, or assets.
- [ ] Confirm no analyzer warnings, skipped tests, weakened assertions, or unresolved plan placeholders remain.

## Manual Windows Verification and Handoff Evidence

- [ ] Restore the Windows game target and record the MonoGame package version resolved by restore:

```bash
dotnet restore DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet list DTXMania.Game/DTXMania.Game.Windows.csproj package --include-transitive | rg -i "MonoGame"
```

- [ ] Record the display resolution and Windows scaling percentage used for the smoke run.
- [ ] Launch windowed and confirm the configured `ScreenWidth×ScreenHeight` size remains the windowed size.
- [ ] Toggle fullscreen with Alt+Enter and confirm soft/borderless fullscreen fills the active display without stretching or squeezing the 16:9 virtual scene.
- [ ] Repeat Win+D, Alt+Tab, minimize, and restore at least five times each. Confirm no half-height centered viewport, desktop switch loop, black padded squeeze, deactivation hang, or restore hang occurs. This is the required Task 1 confirmation gate.
- [ ] Return to windowed mode and confirm the original configured windowed size is restored.
- [ ] On Config → Drum Mapping, hover and click all ten rendered drum images. Confirm each highlight and popup lane/name matches the authored image and the game remains active.
- [ ] Cycle all ten visual zones plus Reset with arrows and Tab. Confirm focus follows authored order and Enter opens the lane for the focused image.
- [ ] Repeat drum hover, click, and keyboard checks in both windowed and fullscreen modes, including one display-scaling setting above 100% when available.

If any fullscreen squeeze, deactivation, or hang sequence remains, stop and capture these values before changing scope: `Window.ClientBounds`, backbuffer width/height, `GraphicsDevice.Viewport.Bounds`, fullscreen state, and `HardwareModeSwitch` state. Include the resolved MonoGame version, automated build/test results, display resolution/scaling, and the per-sequence manual results in the implementation handoff.
