# HPA-619 Windows Fullscreen Restore and Drum Mapping Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the Windows fullscreen squeeze/hang path and make drum hover, keyboard focus, and popup lane selection match the rendered drum image in both fullscreen and windowed modes.

**Architecture:** Keep the existing fixed 1280×720 virtual render target. On Windows, use MonoGame soft/borderless fullscreen and keep logical windowed configuration separate from the physical fullscreen backbuffer size. Repair an out-of-sync presentation when the game regains focus. Normalize WinForms client mouse coordinates into backbuffer coordinates before applying the existing inverse letterbox transform. Keep drum keyboard focus as a visual-zone index and convert explicitly between zone indices and lane IDs.

**Tech Stack:** .NET 8, MonoGame WindowsDX 3.8.x, xUnit, existing `GraphicsManager`, `BaseGame`, and drum-configuration components.

**Linear:** [HPA-619](https://linear.app/cwchanap/issue/HPA-619/fix-windows-fullscreen-restore-squeeze-and-drum-pad-hitfocus-mapping)

## Global Constraints

- Do not introduce a window-mode manager, display service, input-coordinate service, or second drum layout model.
- Do not add an exclusive-versus-borderless user setting. Existing `FullScreen` remains the only persisted mode flag.
- Preserve `ConfigData.ScreenWidth` and `ScreenHeight` as the windowed size. Desktop-sized fullscreen backbuffers must not overwrite those values.
- Preserve the fixed 1280×720 render target and the existing `CalculateLetterboxDestination` behavior.
- Preserve `DrumKitLayout.Zones` order and geometry. Correct identity conversion instead of reordering data to match lane numbers.
- Keep the change cross-platform compilable. Windows-specific behavior should use the existing .NET platform check rather than a new project split.
- Do not add permanent per-frame logging. One low-volume repair log when an actual presentation mismatch is detected is acceptable.
- Unit-test pure state and coordinate logic headlessly. Win+D, Alt+Tab, minimize/restore, and Windows DPI behavior remain manual Windows verification.
- Do not modify drum bindings, MIDI capture, velocity thresholds, popup content, or skin assets.

## Root-Cause Contract

The implementation must address all three defects rather than treating the drum click as an independent native-window action:

1. WindowsDX currently uses MonoGame's default hardware mode switch. Exclusive fullscreen can be torn down during focus loss, while the game caches only the requested logical settings.
2. `Mouse.GetState()` reports WinForms client coordinates, while `BaseGame.MapMouseToVirtual()` currently treats them as backbuffer coordinates.
3. `DrumConfigStage._focusIndex` is a visual-zone index, but several call sites use it as a lane ID. The authored visual lane order is `5, 0, 9, 7, 8, 4, 1, 6, 2, 3`, not `0..9`.

---

## Task 1: Make Windows Fullscreen Borderless and Repair Presentation Drift

**Files:**

- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.Game/Lib/Graphics/GraphicsManager.cs`
- Modify: `DTXMania.Test/Graphics/GraphicsManagerLogicTests.cs`
- Modify: `DTXMania.Test/BaseGameTests.cs`

### Step 1: Add failing physical-backbuffer policy tests

- [ ] Add pure-policy tests to `GraphicsManagerLogicTests`:

```csharp
ResolveBackBufferSize_WindowedUsesLogicalSize()
ResolveBackBufferSize_BorderlessFullscreenUsesCurrentDisplaySize()
ResolveBackBufferSize_InvalidDisplaySizeFallsBackToLogicalSize()
```

Use representative values such as logical `1280×720` and display `2560×1440`. The helper contract is:

```csharp
internal static Point ResolveBackBufferSize(
    GraphicsSettings settings,
    Point currentDisplaySize,
    bool useDesktopSizedFullscreen)
```

Expected behavior:

- Windowed mode always returns the logical width and height.
- Borderless fullscreen returns the current display size when both dimensions are positive.
- An unavailable or invalid display size falls back to the logical width and height.

### Step 2: Add failing presentation-reconciliation tests

- [ ] Extend the existing testable `GraphicsManager` seam so tests can choose whether the actual presentation is synchronized.
- [ ] Add these tests:

```csharp
ApplySettings_WhenLogicalAndPresentationStateMatch_SkipsDeviceApply()
ApplySettings_WhenLogicalSettingsMatchButPresentationDiffers_ReappliesDeviceState()
ApplySettings_WhenRepairingPresentation_DoesNotRaiseLogicalSettingsChanged()
ApplySettings_WhenPresentationRepairFails_KeepsLogicalSettingsSnapshot()
```

The important contract is that `_currentSettings` remains logical configuration. A physical-only repair must call `ApplyChanges()` but must not emit `SettingsChanged`, because that event writes logical settings back to `ConfigData`.

### Step 3: Add a failing activation test

- [ ] In `BaseGameTests`, add:

```csharp
OnGameActivated_ReappliesCurrentLogicalGraphicsSettings()
```

Use a recording `IGraphicsManager` test double. Inject it into `BaseGame`, invoke `OnGameActivated`, and assert that `ApplySettings` receives the current `Settings` snapshot exactly once.

### Step 4: Run the focused tests and confirm they fail

- [ ] Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Windows.csproj \
  --filter "FullyQualifiedName~GraphicsManagerLogicTests|FullyQualifiedName~BaseGameTests"
```

Expected: the new policy, reconciliation, and activation tests fail because the production seams do not exist yet.

### Step 5: Configure soft fullscreen before the first device apply

- [ ] In the `BaseGame` constructor, immediately after creating `GraphicsDeviceManager`, set:

```csharp
if (OperatingSystem.IsWindows())
    _graphicsDeviceManager.HardwareModeSwitch = false;
```

Do not change this property during later toggles. MonoGame should consistently interpret the existing fullscreen flag as borderless/soft fullscreen.

### Step 6: Separate logical settings from the physical backbuffer

- [ ] Add the pure `ResolveBackBufferSize` helper to `GraphicsManager`.
- [ ] Add two narrow overridable seams for headless tests:

```csharp
protected virtual Point GetCurrentDisplaySize()
protected virtual bool UseDesktopSizedBorderlessFullscreen()
```

Production behavior:

- `GetCurrentDisplaySize()` reads the current graphics adapter's display mode when available and falls back to `GraphicsAdapter.DefaultAdapter.CurrentDisplayMode` before device creation.
- `UseDesktopSizedBorderlessFullscreen()` returns true only on Windows when `HardwareModeSwitch` is false.
- Do not add WinForms monitor enumeration or multi-monitor selection in this ticket.

- [ ] Update `SetDeviceManagerSettings(GraphicsSettings settings)` so `PreferredBackBufferWidth` and `PreferredBackBufferHeight` receive the resolved physical dimensions, while `_currentSettings` continues storing the original logical `GraphicsSettings`.

Conceptually:

```csharp
var physicalSize = ResolveBackBufferSize(
    settings,
    GetCurrentDisplaySize(),
    UseDesktopSizedBorderlessFullscreen());

_deviceManager.PreferredBackBufferWidth = physicalSize.X;
_deviceManager.PreferredBackBufferHeight = physicalSize.Y;
_deviceManager.IsFullScreen = settings.IsFullscreen;
_deviceManager.SynchronizeWithVerticalRetrace = settings.VSync;
```

Do not modify `GraphicsExtensions.ToGraphicsSettings` or `UpdateFromGraphicsSettings`; their logical configuration contract remains correct.

### Step 7: Reconcile actual presentation state

- [ ] Add:

```csharp
protected virtual bool IsPresentationSynchronized(GraphicsSettings settings)
```

When a live device exists, compare the expected physical dimensions and fullscreen mode against the current presentation parameters. Also verify the viewport dimensions when available, because drawing and mouse mapping both consume that viewport. Treat an unavailable graphics device as synchronized so pre-initialization/headless calls retain the existing no-op behavior.

- [ ] Refactor `ApplySettings` around two booleans:

```csharp
bool logicalChanged = !settings.Equals(_currentSettings);
bool presentationChanged = !IsPresentationSynchronized(settings);
```

Required flow:

1. Return immediately only when neither value changed.
2. Apply device-manager settings when either value changed.
3. Update `_currentSettings` and emit `SettingsChanged` only when `logicalChanged` is true.
4. Log one information message when repairing presentation drift without a logical change.
5. If a physical-only repair fails, keep the existing logical snapshot and return false. Do not retry the same failing state through the logical revert path.
6. Preserve the existing revert behavior for a failed real logical change.

### Step 8: Repair on activation and clean up the event subscription

- [ ] Subscribe `Activated += OnGameActivated` after `_graphicsManager` is created and its device events are wired.
- [ ] Implement `OnGameActivated` as a null-safe call to `_graphicsManager.ApplySettings(_graphicsManager.Settings)`. Log a warning only if the repair call returns false.
- [ ] Unsubscribe `Activated -= OnGameActivated` in `DisposeManagedResources` alongside the existing `Exiting` and graphics-event cleanup.

Do not perform an unconditional reset. `GraphicsManager.IsPresentationSynchronized` is the gate that keeps ordinary Alt+Tab returns cheap.

### Step 9: Run tests and commit

- [ ] Run the focused command from Step 4.
- [ ] Run the whole Windows unit project:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Windows.csproj
```

- [ ] Commit:

```bash
git add DTXMania.Game/Game1.cs \
        DTXMania.Game/Lib/Graphics/GraphicsManager.cs \
        DTXMania.Test/Graphics/GraphicsManagerLogicTests.cs \
        DTXMania.Test/BaseGameTests.cs
git commit -m "fix: stabilize Windows fullscreen presentation"
```

---

## Task 2: Normalize Client Mouse Coordinates Before Virtual Hit-Testing

**Files:**

- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.Test/BaseGameTests.cs`

### Step 1: Add failing coordinate-normalization tests

- [ ] Add pure-helper tests:

```csharp
ClientToBackBufferCoordinates_SameSizeIsIdentity()
ClientToBackBufferCoordinates_ScalesClientIntoBackBuffer()
ClientToBackBufferCoordinates_OutsideClientReturnsNull()
ClientToBackBufferCoordinates_InvalidBoundsReturnNull()
```

Use a contract shaped as:

```csharp
internal static Point? ClientToBackBufferCoordinates(
    Point clientPoint,
    Point clientSize,
    Rectangle backBufferViewport)
```

The helper must:

- Reject non-positive client or viewport dimensions.
- Reject negative points and points at or beyond the client right/bottom edge.
- Scale into the viewport, including its origin.
- Clamp rounding at the final right/bottom pixel to the viewport bounds.

- [ ] Add an integration-level mapping test using the existing `BaseGame` test subclass:

```csharp
MapMouseToVirtual_WhenClientAndBackBufferSizesDiffer_NormalizesBeforeLetterboxMapping()
```

Example: a `1920×1080` client and a `1280×720` backbuffer should map the client center to virtual `(640, 360)` rather than treating `(960, 540)` as a direct backbuffer point.

- [ ] Add a non-16:9 case proving that client normalization happens before the existing black-bar rejection.

### Step 2: Run the focused tests and confirm they fail

- [ ] Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Windows.csproj \
  --filter "FullyQualifiedName~BaseGameTests"
```

Expected: the new helper and client-size seam do not exist yet.

### Step 3: Add the client-size seam

- [ ] Add:

```csharp
[ExcludeFromCodeCoverage]
protected virtual Point? TryGetClientSize()
```

Use the existing `GetGameWindow()` seam and return only `ClientBounds.Width` and `ClientBounds.Height`. Return null when no window is available. Do not expose WinForms types or add a platform-specific input class.

### Step 4: Normalize inside `MapMouseToVirtual`

- [ ] Implement `ClientToBackBufferCoordinates` as a pure helper.
- [ ] Update `MapMouseToVirtual` in this order:

1. Preserve the current raw-point fallback when graphics/window information is unavailable in headless or pre-initialization contexts.
2. Read both the current client size and backbuffer viewport.
3. Convert the raw client point to a backbuffer point.
4. Return null when normalization rejects the point or either dimension is invalid.
5. Pass the normalized point into the existing `WindowToVirtualCoordinates` helper.

Do not combine the two transforms into one new abstraction. Keeping client-to-backbuffer scaling separate from backbuffer-to-virtual letterboxing makes each contract independently testable.

### Step 5: Run tests and commit

- [ ] Run the focused BaseGame tests.
- [ ] Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Windows.csproj
```

- [ ] Commit:

```bash
git add DTXMania.Game/Game1.cs DTXMania.Test/BaseGameTests.cs
git commit -m "fix: normalize mouse coordinates for fullscreen"
```

---

## Task 3: Keep Drum Focus as a Zone Index and Resolve Lane IDs Explicitly

**Files:**

- Modify: `DTXMania.Game/Lib/Stage/DrumConfig/DrumKitLayout.cs`
- Modify: `DTXMania.Game/Lib/Stage/DrumConfigStage.cs`
- Modify: `DTXMania.Test/Stage/DrumConfig/DrumKitLayoutTests.cs`
- Modify: `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`

### Step 1: Add failing zone/lane identity tests

- [ ] Add these pure layout tests:

```csharp
GetLaneForZoneIndex_ReturnsEachAuthoredZoneLane()
FindZoneIndexByLane_RoundTripsEveryAuthoredZone()
GetLaneForZoneIndex_ResetAndInvalidIndicesReturnMinusOne()
FindZoneIndexByLane_InvalidLaneReturnsMinusOne()
```

Add two small helpers to the layout contract:

```csharp
public static int GetLaneForZoneIndex(int zoneIndex)
public static int FindZoneIndexByLane(int lane)
```

Both return `-1` for invalid input. The Reset action is not a lane.

### Step 2: Strengthen stage regression tests with non-identity mappings

- [ ] Rename reflection references from `_focusIndex` to `_focusedZoneIndex` in existing tests.
- [ ] Change `ActivateFocusedElement_WhenZoneFocused_OpensPopupForThatLane` to use visual zone index `0` and assert popup lane `5`. Do not use an index whose numeric value happens to equal its lane.
- [ ] Update the snare click test to assert:

```text
clicked lane = 4
focused zone index = 5
popup lane = 4
```

- [ ] Add a representative mouse-click test for another non-identity zone, such as visual index `2` / ride lane `9`.
- [ ] Keep existing keyboard advance tests asserting zone indices, including Reset at `DrumKitLayout.ResetActionIndex`.

### Step 3: Run the focused tests and confirm they fail

- [ ] Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Windows.csproj \
  --filter "FullyQualifiedName~DrumKitLayoutTests|FullyQualifiedName~DrumConfigStageTests"
```

Expected: the non-identity activation and click assertions expose the current index/lane confusion.

### Step 4: Implement explicit conversion helpers

- [ ] Implement `GetLaneForZoneIndex` using bounds checking and `Zones[zoneIndex].Lane`.
- [ ] Implement `FindZoneIndexByLane` as one small loop over `Zones`.

Do not add dictionaries, cached lookup tables, or reorder `Zones`; there are only ten entries and this code is not performance-sensitive.

### Step 5: Correct `DrumConfigStage` state ownership

- [ ] Rename `_focusIndex` to `_focusedZoneIndex` throughout the stage.
- [ ] Keep arrow/Tab navigation and Reset logic operating only on `_focusedZoneIndex`.
- [ ] Update `OpenPopup(int lane)` so it:

1. Stores `_selectedLane = lane`.
2. Resolves `FindZoneIndexByLane(lane)` and updates `_focusedZoneIndex` only when a matching zone exists.
3. Opens the popup with the original lane ID.

- [ ] Update `ActivateFocusedElement()` so a non-Reset focus resolves `GetLaneForZoneIndex(_focusedZoneIndex)` and opens only a valid lane.
- [ ] Update `OnDraw()` so `LaneHighlights.FocusedLane` receives the resolved lane, never the zone index.
- [ ] Keep `_hoveredLane` and `_selectedLane` as lane IDs. Their current names and renderer contract are already correct.

### Step 6: Run tests and commit

- [ ] Run the focused drum tests.
- [ ] Run the whole Windows unit project.
- [ ] Commit:

```bash
git add DTXMania.Game/Lib/Stage/DrumConfig/DrumKitLayout.cs \
        DTXMania.Game/Lib/Stage/DrumConfigStage.cs \
        DTXMania.Test/Stage/DrumConfig/DrumKitLayoutTests.cs \
        DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
git commit -m "fix: map drum focus zones to lane identities"
```

---

## Final Automated Verification

- [ ] Run Windows build and tests:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet test DTXMania.Test/DTXMania.Test.Windows.csproj
```

- [ ] Run macOS compile/test regression checks where the environment supports them:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

- [ ] Confirm the diff does not modify `GraphicsExtensions`, `IGraphicsManager`, drum binding behavior, assets, or configuration schema unless a failing compiler/test proves one of those edits is unavoidable.
- [ ] Confirm no new analyzer warnings and no skipped or weakened existing tests.

## Manual Windows Verification

Record the machine resolution, Windows display-scaling percentage, MonoGame package version resolved by restore, and results in the implementation PR.

- [ ] Launch windowed and confirm the configured `ScreenWidth×ScreenHeight` size is retained.
- [ ] Toggle fullscreen with Alt+Enter and confirm borderless fullscreen fills the active display without stretching the 16:9 virtual scene.
- [ ] Repeat Win+D, Alt+Tab, minimize, and restore at least five times each. Confirm there is no half-height centered viewport, desktop switch loop, black padded squeeze, or hang.
- [ ] Return to windowed mode and confirm the original configured windowed size is restored.
- [ ] On Config → Drum Mapping, hover and click all ten drum images. Confirm the highlight and opened popup name/lane match each image and the game remains active.
- [ ] Cycle all ten zones plus Reset with arrows/Tab. Confirm focus follows authored visual order and Enter opens the focused image's lane.
- [ ] Repeat the drum checks in both windowed and fullscreen modes.
- [ ] Repeat at 100% and one greater-than-100% Windows display-scaling setting when available.

## Implementation PR Evidence

The implementation PR description must include:

- The resolved Windows and MonoGame version.
- Automated build/test commands and results.
- The manual focus-loss matrix with pass/fail results.
- The display resolution and scaling values used.
- Confirmation that `Config.ini` retains the configured windowed width/height after fullscreen toggles.
- Confirmation that every visual drum zone maps to the expected popup lane.

## Scope Estimate

Two engineer days. Each task is independently reviewable, and no task requires a new subsystem or data migration.
