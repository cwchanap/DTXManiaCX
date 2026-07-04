# Config Stage NX Layout — Foundation Changes (Design Note)

Date: 2026-07-03
Status: Ratifying (post-implementation)
Branch: `refactor/config-stage-nx-layout`

## Purpose

The config-stage NX layout plan (`2026-07-01-config-stage-nx-layout.md`) scoped
work to three files: `ConfigUILayout.cs`, `ConfigStage.cs`, and
`ConfigUILayoutTests.cs`. During implementation, six foundation-level changes
were required to make the NX layout correct and testable. This note documents
why each was needed so the scope expansion is explicit rather than implicit.

## Foundation changes outside the original plan

### 1. Fixed virtual render target + letterboxing (commit `f73b8d9`)

**What:** `BaseGame` now renders every stage into a fixed 1280×720 render
target (`GameConstants.Display.VirtualWidth/Height`) and letterbox-scales that
target once to fill the physical window.

**Why needed:** The config layout constants are authored in DTXManiaNX's
1280×720 space. Without a fixed virtual target, stages drew directly to the
back buffer at the configured resolution (e.g. 1920×1080), so the authored
coordinates were wrong at any resolution other than 1280×720. The NX layout
requires pixel-accurate placement of the item boxes, cursor, and description
panel; a scaling back buffer breaks that. The fixed render target makes the
authored coordinates correct at every window size.

**Files:** `Game1.cs`, `GameConstants.cs` (new `Display` constants),
`ConfigStage.cs` (scissor clip for the item list).

### 2. Window-to-virtual coordinate mapping for hit-testing (commit `7e14f08`)

**What:** `BaseGame.WindowToVirtualCoordinates` and `MapMouseToVirtual` convert
raw window mouse positions into the 1280×720 virtual space. Clicks on
letterbox bars map to null.

**Why needed:** Once stages draw into the letterboxed virtual target, mouse
hit-testing must convert window coords back to virtual coords to match the
authored rectangles. Without this, clicks would test against the wrong
coordinates at any window size that isn't exactly 1280×720. Four stages had
per-stage viewport transform methods (`CreateViewportTransform`, `GetViewport`)
that were removed in favor of the single centralized mapping.

**Files:** `Game1.cs`, `ConfigStage.cs`, `DrumConfigStage.cs`,
`SongSelectionStage.cs`, `TitleStage.cs`.

### 3. Headless test seams (commit `8f1f7a5`)

**What:** Extracted `BaseGame.TryGetViewportBounds` and
`SystemKeyAssignPanel.DrawWhitePixel` as virtual seams so headless tests can
override them without a GraphicsDevice.

**Why needed:** The virtual render target and coordinate mapping logic needed
unit coverage, but `BaseGame` methods touch `GraphicsDevice` for viewport
bounds. The seams let tests verify `MapMouseToVirtual` and the
`SystemKeyAssignPanel` draw path without a device — the same pattern
`ConfigStage.BeginItemClip/EndItemClip` already uses.

**Files:** `Game1.cs`, `SystemKeyAssignPanel.cs`, `ConfigStage.cs`, plus
test files.

### 4. SystemKeyAssignPanel redesign (commit `5238bf1`)

**What:** `SystemKeyAssignPanel` was redesigned to the NX layout with the
virtual coordinate space, and its draw path was made testable via the
`DrawWhitePixel` seam.

**Why needed:** The panel is opened from the Config stage's System category
("System Key Mapping" item). It shared the same coordinate-space problem as
the rest of the Config stage — without the virtual target, its hit-testing
and layout were wrong at non-1280×720 resolutions. Fixing the Config stage
without fixing the panel it opens would leave a broken navigation path.

**Files:** `SystemKeyAssignPanel.cs`.

### 5. Centralize virtual resolution constants across layout classes (commits `15f4bff`, `cbcc0a7`)

**What:** The hardcoded `1280`/`720` magic numbers in the layout classes were
replaced with references to the single source of truth,
`GameConstants.Display.VirtualWidth/Height`. Each layout class now links to
that constant instead of restating the dimensions.

**Why needed:** Foundation change #1 introduced `GameConstants.Display` as the
single source for the virtual dimensions. Leaving the layout classes with
hardcoded `1280`/`720` would mean a future change to the virtual resolution
requires editing every layout file in lockstep — the exact duplication the
centralization was meant to eliminate. The layout classes author in 1280×720
space, so they are the primary consumers of the constant and the highest-value
place to wire it in.

**Files:** `DrumKitLayout.cs` (`DesignWidth`/`DesignHeight`),
`ConfigUILayout.cs` (`ScreenWidth`/`ScreenHeight`),
`PerformanceUILayout.cs` (`ScreenWidth`/`ScreenHeight`, plus the derived
`LaneHeight`, full-screen overlay bounds, centered positioning, and
proportional atlas bounds),
`ResultUILayout.cs` (`NXViewport.Width`/`Height`),
`SongSelectionUILayout.cs` (`SongListDisplay.Width`/`Height`).

Texture-atlas source rectangles (e.g. the 720px-tall lane source rects in
`PerformanceUILayout`) remain hardcoded by design — they describe texture
dimensions, not the screen.

### 6. Remove redundant per-stage viewport reads (commits `cbcc0a7`, `36e7496`)

**What:** Four stages had code that read `GraphicsDevice.Viewport` at draw or
UI-init time to size a panel or compute a transform. Once every stage renders
into the fixed 1280×720 virtual target, the back-buffer viewport is the window
size (not the virtual size), so those reads return the wrong dimensions. They
were replaced with direct references to the virtual dimensions.

**Why needed:** Foundation change #1 made the back-buffer viewport irrelevant
to stage layout — stages draw in virtual space and `BaseGame` letterboxes the
result. Any stage that still read `GraphicsDevice.Viewport` for sizing would
size against the window (e.g. 1920×1080) instead of the virtual target,
breaking the layout. `SongTransitionStage`, `SongSelectionStage`, and
`TitleStage` sized their main panel / fallback background from the viewport;
`ResultStage` computed a per-stage viewport transform that was always identity
under the virtual-target model (the transform mapped the virtual viewport to
itself). Removing the transform is a semantic change to `ResultStage` (it no
longer calls `ResultScreenRenderer.CreateViewportTransform`), but the call was
a no-op once the virtual target landed, so the visible behavior is unchanged.

**Files:** `ResultStage.cs` (removed the identity `CreateViewportTransform`
call from `OnDraw`, now uses a plain `_spriteBatch.Begin()`),
`SongTransitionStage.cs` (`InitializeUI` sizes the main panel to
`GameConstants.Display.VirtualWidth/Height` instead of the viewport),
`SongSelectionStage.cs` (`InitializeUI` sizes the main panel to
`SongSelectionUILayout.SongListDisplay.Size` instead of the viewport),
`TitleStage.cs` (fallback background draw uses
`GameConstants.Display.VirtualWidth/Height` instead of the viewport).

`SongSelectionStage.cs` and `TitleStage.cs` were also touched by foundation
change #2 (commit `7e14f08`) to remove their per-stage viewport transform
methods; this entry covers the separate panel-sizing fix from `36e7496`.

## Scope decision

These six changes are tightly coupled to the config layout work: the NX
layout is incorrect without the virtual render target (#1), the panel opened
from Config is incorrect without the coordinate mapping (#2), the layout
classes can't safely reference a single dimension source until it exists (#5),
and the per-stage viewport reads (#6) are dead code once the virtual target
lands. Splitting them into a separate PR would require either a broken
intermediate state (config layout without the virtual target, or layout
classes still carrying magic numbers after the centralization constant
exists) or reverting the layout work to land the foundation first. Keeping
them in one PR with this ratification note is the lower-risk path.

## Verification

- Visual scissor-clip verification performed (Task 7 Step 2): launched the
  Mac game, navigated to Config, scrolled past the top and bottom of the
  item list, and pixel-analyzed the screenshots. Zero item-box pixels
  spilled into the gap between the clip rect (`y ∈ [109, 686)`) and the
  header/footer in any scroll state. The default `SpriteSortMode.Deferred`
  is sufficient; no fallback to `SpriteSortMode.Immediate` needed.
- Automated scissor-clip regression guard: `ConfigStage` exposes the item-
  list clip via three seams — `GetItemClipRectangle()` (the rect to clip to),
  `CreateItemClipRasterizer()` (the rasterizer with `ScissorTestEnable`),
  and `ApplyScissorRectangle(Rectangle)` (the GraphicsDevice apply step).
  `RenderSpyConfigStage` overrides `ApplyScissorRectangle` to record the
  rect without a device, and tests assert the recorded rect equals
  `ConfigUILayout.InnerBoardRect` and the rasterizer has
  `ScissorTestEnable = true` / `CullMode = None`. A future change to the
  clip rect or rasterizer that breaks the "zero spill past the board"
  invariant now fails a test instead of regressing silently.
- Virtual-dimension source of truth: `GameConstants.Display.VirtualWidth/
  Height` is the single source for screen-dimension constants. The layout
  classes that author in 1280×720 space link to it: `ConfigUILayout`
  (`ScreenWidth`/`ScreenHeight`), `DrumKitLayout` (`DesignWidth`/
  `DesignHeight`), `PerformanceUILayout` (`ScreenWidth`/`ScreenHeight`,
  plus the derived `LaneHeight`, full-screen overlay bounds, centered
  positioning, and proportional atlas bounds), `ResultUILayout.NXViewport`
  (`Width`/`Height`), and `SongSelectionUILayout.SongListDisplay`
  (`Width`/`Height`). Texture-atlas source rectangles (e.g. the 720px-tall
  lane source rects in `PerformanceUILayout`) remain hardcoded by design —
  they describe texture dimensions, not the screen.

## Contracts and known drift

- **Scissor save/restore pairing:** `ConfigStage._savedScissorRectangle` is
  a default-initialized `Rectangle` (`{0,0,0,0}`) and is only meaningful
  after `ApplyScissorRectangle` writes the device's current scissor into it.
  `RestoreScissorRectangle` reads it back. The contract is that
  `RestoreScissorRectangle` must only be called after a matching
  `ApplyScissorRectangle`; `BeginItemClip`/`EndItemClip` enforce this via
  try/finally, so the pairing holds as long as callers go through that pair
  rather than calling the seams directly. Calling `RestoreScissorRectangle`
  without a prior `Apply` would set the device scissor to `{0,0,0,0}`.
- **`DescriptionBodyPos.Y` drift (445 → 448):** The design spec
  (`2026-07-01-config-stage-nx-layout-design.md`) places the description
  body at `~(818, 445)`. The implementation uses `(818, 448)`. The 3px
  shift is within the design spec's own `~` (approximate) tolerance and was
  applied during fine-tuning (the plan `2026-07-01-config-stage-nx-layout.md`
  and the layout test both assert 448). The design spec's `445` is stale;
  the implementation is the source of truth.
- **`GetItemValueText` prefix-stripping coupling:** `ConfigStage.GetItemValueText`
  extracts the value column by stripping the `"{Name}: "` prefix that
  `IConfigItem.GetDisplayText()` prepends. This is coupled to every concrete
  item type's `GetDisplayText` format (Dropdown/Toggle/Integer/ReadOnly all
  use `"{Name}: {value}"`); a future item type that omits the prefix would
  render an empty value here. The coupling is documented in-code at the
  method. Adding a `GetValueText()` to `IConfigItem` would decouple this,
  but that value-semantics change is an explicit non-goal of the NX layout
  revamp — so the coupling is documented rather than resolved.
