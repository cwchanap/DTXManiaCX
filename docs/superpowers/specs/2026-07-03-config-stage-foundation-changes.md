# Config Stage NX Layout — Foundation Changes (Design Note)

Date: 2026-07-03
Status: Ratifying (post-implementation)
Branch: `refactor/config-stage-nx-layout`

## Purpose

The config-stage NX layout plan (`2026-07-01-config-stage-nx-layout.md`) scoped
work to three files: `ConfigUILayout.cs`, `ConfigStage.cs`, and
`ConfigUILayoutTests.cs`. During implementation, four foundation-level changes
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

## Scope decision

These four changes are tightly coupled to the config layout work: the NX
layout is incorrect without the virtual render target, and the panel opened
from Config is incorrect without the coordinate mapping. Splitting them into
a separate PR would require either a broken intermediate state (config layout
without the virtual target) or reverting the layout work to land the
foundation first. Keeping them in one PR with this ratification note is the
lower-risk path.

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
