# Config Stage — NX Layout Refinement (Design)

Date: 2026-07-01
Status: Approved (user requested scrolling be included for future list expansion)

## Problem

The Config stage UX is messy relative to DTXManiaNX:

- **Menu items not aligned** — the left category menu (System / Drums / Exit) uses
  metrics (`MenuLabelCenterX=335`, stride `34`, first-row `Y=156`) that drift from
  NX (`340` / `32` / `144`), and labels are not vertically centered in the cursor
  band.
- **Text hard to read** — the item-row art (`4_itembox.png`, 538×80) is a **dark
  left cell for the name** plus a **white right cell for the value**. The current
  code draws the value in light gold (`255,226,150`) and the selected value in
  `Yellow` — both illegible on the white cell. The description panel
  (`4_Description Panel.png`) is **white on top / black on bottom**, but the code
  draws all description text in light color from `y=288`, i.e. light text on the
  white upper region.
- **"Missing" panel background / distorted rows** — the 538×80 item-box art is
  squished into a `360×54` rectangle (`ItemRowRect`), distorting the art and
  collapsing the name/value cells so the value cell (white background) is cut off,
  reading as a missing background.

The runtime skin art **is** present at
`~/Library/Application Support/DTXManiaCX/System/Graphics/` (verified), so the fix
is layout constants + text colors, not missing assets.

## Goal

Refine the Config stage to match NX's visual language: aligned menu, correctly
proportioned item rows, and text colors that are legible against each art cell —
without changing behavior (navigation, value editing, save-on-exit, NX import).

## Scope decision: NX center-locked scrolling list (future-proof for long lists)

NX's item list is a vertically-scrolling list with the selected item locked to the
screen center; rows scroll past a fixed cursor. We adopt that model so the list
scales when categories grow (the primary reason to include scrolling now, while
System = 7 / Drums = 4). The architecture is a **scrolling viewport**:

- The **selected item is drawn at a fixed focus row** (NX center, panel-top y=189);
  every other item `i` renders at `focusY + (i − p)·stride`, where `p` is an
  animated scroll position that eases toward `selectedIndex`.
- **Non-cyclic display**: only real items (indices `0..count−1`) are drawn. A slot
  mapping outside that range draws nothing — so short lists show blank space above/
  below the centered selection (the authentic NX look, clearly "a scrollable list")
  and **never** show duplicate rows the way a naïve cyclic ring would for a 4-item
  list.
- **Wrap-safe animation**: `ConfigCategory` selection wraps (`% count`). Adjacent
  moves ease `p` by one row; a wrap (last↔first) **snaps** `p` to the target instead
  of scrolling the whole list backward. Concretely: on update, ease
  `p += (sel − p)·factor`, but if `|sel − p| > 1.5` (a wrap or multi-row jump), set
  `p = sel`.
- The **cursor is fixed** at the focus row (NX `4_itembox cursor.png` at 413,193);
  items scroll under it. Only real items within the visible band
  (header bottom 105 .. footer top 690) are drawn.

The scroll math (index → screen Y, and which items are visible) lives in a pure,
unit-tested `ConfigUILayout` helper; only the eased `p` value and its per-frame
step live in the stage. This keeps arbitrary-length support testable without a
GraphicsDevice.

## Authentic NX reference coordinates (1280×720)

From `DTXManiaNX/.../04.Config/CStageConfig.cs` and `CActConfigList.cs`:

| Element            | NX value |
|--------------------|----------|
| Background         | (0,0) 1280×720 |
| Item bar divider   | (400,0) 18×720 |
| Menu panel         | (245,140) 180×172 |
| Menu label center  | x≈340, first `menuY=144`, stride `32` |
| Menu cursor        | x=250, y=146+i·32, width≈170, height 32 (light bar) |
| Item panel X       | 420 |
| Item name          | (panelX+20, y+24) |
| Item value         | (panelX+260, y+24) |
| Item cursor        | (413,193), art `4_itembox cursor.png` 497×68 |
| Description panel  | (800,270) 280×360 |
| Item box art       | `4_itembox.png` 538×80 (dark name cell + white value cell); `4_itembox other.png` 438×80 (single dark cell) |

## Design

### 1. Left category menu (`ConfigUILayout` + `DrawCategoryMenu`)

- `MenuLabelCenterX = 335` (true panel center: 245 + 180/2).
- Menu cursor: `x=250`, `width=170`, `height=32`; first cursor `Y=148`, stride `32`
  (3 rows → 148 / 180 / 212, inside the 140..312 panel).
- Draw each label **vertically centered** in its 32-px cursor band
  (`labelY = cursorY + (32 - textHeight)/2`) instead of at a fixed row Y.
- **Selected** label color becomes **dark** (`SelectedMenuText ≈ 36,24,72`) because
  it sits on the light cursor bar; non-selected labels stay light
  (`LightText 235,238,248`) on the purple panel.

### 2. Item rows — scrolling viewport (`ConfigUILayout` + `DrawItemList`)

- `ItemListX = 420`; boxes drawn at the art's **native** size (no squish):
  - Normal box (`4_itembox.png`): **538×80** — dark name cell + white value cell.
    All rows — value items **and** navigation/read-only rows — use this single box
    so the list reads uniformly. NX has a separate `4_itembox other.png` (438×80,
    single dark cell) for nav rows, but it looked out of place next to value rows;
    we intentionally use the normal box for every row and do not load the "other"
    texture.
- `ItemRowStride = 67` (NX; boxes overlap 13 px, matching how the art tiles).
- `ItemFocusRowY = 189` — panel-top Y of the centered/selected row.
- Per-item panel-top Y (pure helper):
  `RowTopY(i, p) = ItemFocusRowY + round((i − p)·ItemRowStride)`, where `p` is the
  eased scroll position. Draw item `i` only when its box intersects the visible band
  `[105, 690]`. The selected item lands at `ItemFocusRowY` once `p` settles.
- **Name** text at `(boxX + 20, rowTopY + 24)` in `LightText` (dark cell → light).
- **Value** text at `(boxX + 260, rowTopY + 24)` in a **new dark**
  `ValueText (24,24,32)` (white cell → dark). Keep the existing width-truncation so a
  long value (e.g. a deep `DTXPath`) never overflows past the box / into the
  description panel.
- **Selected** row: name in `SelectedNameText (255,238,120)` (warm yellow on the
  dark cell); value in `SelectedValueText (168,52,0)` (dark orange on the white
  cell). No more yellow-on-white.
- Item cursor: `4_itembox cursor.png` (497×68) drawn **fixed** at `(413, 193)` (the
  focus row) when focus is on the item list; items scroll under it.
- The normal box (ends at 420+538 = 958) extends under the description panel
  (x=800); the description panel is drawn **after** the rows, covering the right ends
  exactly as NX does. Value text at 680 + width stays left of x=800 after truncation.

### 3. Description panel (`DrawDescriptionPanel`)

The panel is drawn **only while focus is on the item list** (`!_focusOnMenu`),
matching NX (`CStageConfig.cs:260`, `!bFocusIsOnMenu`). While browsing the
category menu the panel is suppressed so the busy GALAXY WAVE background stays
clear on entry and the panel does not overlap the item boxes until the player is
actually editing an item. The selected item sits at the focus row (y=189), above
the panel top (y=270), so it stays readable.

When focus is on the item list, the panel art is white (top ~270..430) over
black (~430..630); use both cells:

- **Title** (bold) — current item/category name — in the white upper region,
  **dark** text (≈ `24,24,32`) at ~`(818, 300)`.
- **Body** — the description string, wrapped — in the black lower region, **light**
  text (`LightText`) starting ~`(818, 445)`, wrap width 248, line height 22.

This removes the light-on-white illegibility and fills the two-tone panel the way
the art intends.

### 4. Unchanged

- Background, item-bar divider, header panel + "CONFIGURATION" title, footer panel
  + instructions text, NX-import status line — all keep their current positions and
  colors (not part of the reported problems). The header art is transparent with a
  "FREE STAGE" badge; the title sits on the (dark) background and stays light.
- All input handling, navigation, value editing, deferred save, and NX-score
  import logic are untouched. This is a **draw/layout-only** change.

## Files touched

- `DTXMania.Game/Lib/UI/Layout/ConfigUILayout.cs` — menu metrics; item-list scroll
  constants (`ItemListX`, native box sizes, `ItemRowStride`, `ItemFocusRowY`, visible
  band, name/value offsets, fixed cursor rect); the pure `RowTopY(i, p)` helper and a
  visibility predicate; description title + body positions.
- `DTXMania.Game/Lib/Stage/ConfigStage.cs` — a scroll field `p` (eased toward
  `SelectedIndex`, wrap-snapped) stepped in `OnUpdate`; `DrawCategoryMenu` (vertical
  centering, selected-label dark color); `DrawItemList` (native box sizes, scrolling
  viewport, name light / value dark, selected highlights, fixed cursor);
  `DrawDescriptionPanel` (title on white + body on black). New `Color` constants.
  Reset `p` to the selected index in `OnActivate` so re-entry starts settled.
- `DTXMania.Test/UI/ConfigUILayoutTests.cs` — update pinned constants to the new
  NX-aligned values; add tests for the scroll math (`RowTopY` centering the selected
  item, stride, visibility band) and menu/description positions.

## Testing

- Update `ConfigUILayoutTests` to assert the new constants and the scroll math:
  `RowTopY(sel, sel) == ItemFocusRowY` (selected item centered when settled),
  `RowTopY(i+1, p) − RowTopY(i, p) == ItemRowStride`, the visibility predicate for
  in- and out-of-band rows, menu row/cursor rects, and description title/body
  positions. These are the regression guard for arbitrary-length scrolling.
- Existing `ConfigStage*Tests` (logic: navigation, value editing, NX import) must
  continue to pass unchanged — behavior is not modified.
- Optionally assert the eased-scroll step converges toward / snaps on wrap (pure
  double math, no GraphicsDevice).
- Visual verification: launch the game, open Config, and screenshot each state
  (menu focus, item focus, a toggle row, the read-only `DTX Folder` row, an item
  with a long value, and scroll to the last item) to confirm alignment, contrast,
  the centered selection, and that the description title/body land on the correct
  art cells. Fine-tune name/value offsets and description Y against the screenshot.

## Non-goals

- NX's exact multi-stage accel-scroll curve — we use a single eased ease-toward step
  (wrap-snapped). Feel can be tuned later.
- Cyclic display that repeats items to fill the viewport for short lists.
- Any change to config values, item semantics, or `IConfigItem`.
- Header / footer / background art or their text.
