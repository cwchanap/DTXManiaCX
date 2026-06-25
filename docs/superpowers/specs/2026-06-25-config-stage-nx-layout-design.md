# Config Stage NX Layout Revamp — Design

**Date:** 2026-06-25
**Status:** Approved (brainstorming), ready for implementation planning

## Summary

Rebuild `ConfigStage`'s UI from a single flat vertical list into the DTXManiaNX
two-column **master-detail** layout: a left category menu, a right per-category
item list, a description panel, and header/footer panels over the config
background. The behaviour model (persist-on-edit, single source of truth =
`ConfigManager.Config`) is unchanged — only the layout, navigation model, and
item grouping change.

All required skin assets (`4_*.png`) already exist in `System/Graphics/`. The
existing visual `DrumConfigStage` and `SystemKeyAssignPanel` are reused as-is.

## Goals

- Two-column master-detail layout matching the NX config screen structure.
- Left menu with three categories: **System**, **Drums**, **Exit**.
- Right item list per category, rendered with NX item-box skin art.
- Description panel showing English help text for the focused category/item.
- NX-style two-focus navigation (focus on menu ↔ focus on item list).
- Reuse the existing key-mapping UIs (no inline NX key list).
- Graceful fallback to solid fills + text when skin art is missing.

## Non-Goals (explicitly out of scope)

- **Guitar / Bass categories.** This is a drum-only port; NX's Guitar/Bass menu
  entries are dropped, not stubbed.
- **Inline NX key-assignment list (`CActConfigKeyAssign`).** Key mapping keeps the
  existing richer UIs: Drum Key Mapping → `DrumConfigStage`; System Key Mapping →
  `SystemKeyAssignPanel` overlay.
- **NX's animated 14-slot centered scroll carousel.** Replaced by a simpler
  top-anchored item-box list (each category has ≤7 items, so no scrolling is
  needed). Same NX look, minus the wheel animation.
- **Bilingual (JP/EN) description text.** English-only.
- Changes to `ConfigManager`, the persist-on-edit model, the `IConfigItem` value
  semantics, or the NX-import logic. Those are preserved as-is.

## Decisions (from brainstorming)

| Topic | Decision |
| --- | --- |
| Layout fidelity | Full NX master-detail (two-column + description + header/footer). |
| Categories | System / Drums / Exit (Guitar & Bass dropped). |
| Descriptions | English-only, per category **and** per item. |
| Key mapping | Reuse existing UIs (`DrumConfigStage`, `SystemKeyAssignPanel`). |
| Right column | Simple top-anchored item-box list, not the animated carousel. |
| Per-category selection | Remember the selected item index per category. |

## Visual Layout (1280×720, NX coordinates)

```
┌──────────────────────────────────────────────────────────────┐  4_header panel.png (0,0)
│  CONFIGURATION                                                 │
├───────────────┬──────────────────────────────────────────────┤
│ ▸ System      │  ┌────────────────────────────────────────┐  │  item-box rows, x=420
│   Drums       │  │ Screen Resolution           1920x1080  │◀ │  4_itembox.png /
│   Exit        │  │ Fullscreen                       OFF   │  │  4_itembox other.png
│               │  │ VSync Wait                        ON   │  │  cursor 4_itembox cursor.png
│ menu panel    │  │ Audio Latency Offset            30 ms  │  │  name x+20, value x+260
│ (245,140)     │  │ DTX Folder            …/path/to/dtx    │  │
│ cursor        │  │ System Key Mapping                ▸    │  │
│ 4_menu        │  │ Import NX Scores                  ▸    │  │
│ cursor.png    │  └────────────────────────────────────────┘  │
│               │                  ┌────────────────────────┐   │  4_Description Panel.png
│ │ item bar    │                  │ Sets the display       │   │  (800,270)
│ │ (x=400)     │                  │ resolution.            │   │
├───────────────┴──────────────────────────────────────────────┤
│  ↑↓ navigate   ←→ change   Enter select   Esc back            │  4_footer panel.png (bottom)
└──────────────────────────────────────────────────────────────┘
```

**Draw order (back to front):** `4_background.png` (0,0) → `4_item bar.png`
(400,0) → left `4_menu panel.png` (245,140) + `4_menu cursor.png`
(x=250, y=146+idx·32, ~170 wide) + category labels (centered ≈x=340, y start 144,
step 32) → right item-box rows (x=420) + item cursor + name (x+20) / value (x+260)
text → `4_Description Panel.png` (800,270) + wrapped text → `4_header panel.png`
(top) → `4_footer panel.png` (bottom).

These coordinates are ported directly from `CStageConfig.OnUpdateAndDraw` and
`CActConfigList` (item panel at x=420, name at +20, value at +260). All values are
centralized in `ConfigUILayout` rather than inlined.

**Fallbacks:** each texture load is best-effort (try/catch, null-check on draw).
Missing background → existing light `FallbackBackgroundColor` fill; missing panel
art → solid-rectangle stand-ins so text stays legible. Config remains fully usable
without the skin (mirrors the current `ConfigStage` and `DrumConfigStage` pattern).

## Categories & Item Mapping

| Category | Items (in order) | Notes |
| --- | --- | --- |
| **System** | Screen Resolution · Fullscreen · VSync Wait · Audio Latency Offset · DTX Folder · System Key Mapping · Import NX Scores | DTX Folder is read-only; System Key Mapping opens the overlay; Import NX Scores starts the async import. |
| **Drums** | Scroll Speed · Auto Play · No Fail · Drum Key Mapping | Drum Key Mapping navigates to `DrumConfigStage`. |
| **Exit** | _(none — the category itself is the action)_ | Activating Exit flushes the pending save and returns to Title. |

Every existing `IConfigItem` instance from today's `SetupConfigItems()` is reused;
only its grouping and an added `Description` change. No item is dropped.

## Navigation / Focus Model (NX two-focus)

Two focus states mirror NX's `bFocusIsOnMenu`:

**Focus = Menu (left column), the entry state:**
- ↑ / ↓ — change category (wraps); updates the right list and shows the
  category-level description.
- Enter / → on a settings category (System, Drums) — move focus into the right
  item list (focus = Items).
- Enter / → on **Exit** — flush pending save, `ChangeStage(Title)`.
- Esc / Back — flush pending save, `ChangeStage(Title)`.

**Focus = Items (right column):**
- ↑ / ↓ — move the selected item (wraps within the category).
- ← / → — change the focused item's value (`PreviousValue` / `NextValue`).
- Enter / Activate — `ToggleValue` for toggles; trigger nav items (DrumConfig
  navigation, System Key panel, NX import).
- Esc / Back — return focus to the **menu** (does NOT exit).

**Description panel:** shows the focused item's `Description` while focus = Items;
shows the focused category's `Description` while focus = Menu.

**Remembered selection:** each category keeps its own selected-item index, restored
when the user re-enters that category.

Input plumbing reuses the existing `IsConfigNavigationCommandPressed` /
`IsPanelCommandPressed` helpers and `InputCommandType` mapping unchanged.

## Component Architecture

Follows the existing `DrumConfig/` subfolder + `*UILayout` conventions.

- **`Lib/UI/Layout/ConfigUILayout.cs`** *(new)* — all coordinate/size constants:
  panel origins, menu cursor step, item-row origin/stride, name/value offsets,
  description panel origin and text inset, header/footer placement. Renderers carry
  no magic numbers.

- **`Lib/Stage/Config/`** *(new subfolder)*:
  - `ConfigCategory.cs` — model: `string Name`, `string Description`,
    `IReadOnlyList<IConfigItem> Items`, plus the remembered selected index.
  - `ConfigMenuRenderer.cs` — draws menu panel, menu cursor, category labels;
    highlights the current category and dims when focus = Items.
  - `ConfigItemListRenderer.cs` — draws item-box rows (`4_itembox.png` /
    `4_itembox other.png`), the item cursor, and name/value text; highlights the
    focused row, dims the whole list when focus = Menu.
  - `ConfigDescriptionRenderer.cs` — draws the description panel and word-wrapped
    text (simple greedy wrap to the panel's inner width).

- **`Lib/Resources/TexturePath.cs`** — add constants and register them in the
  existing path arrays: `ConfigBackground` (`Graphics/4_background.png`),
  `ConfigItemBar` (`4_item bar.png`), `ConfigMenuPanel` (`4_menu panel.png`),
  `ConfigMenuCursor` (`4_menu cursor.png`), `ConfigHeaderPanel`
  (`4_header panel.png`), `ConfigFooterPanel` (`4_footer panel.png`),
  `ConfigItemBox` (`4_itembox.png`), `ConfigItemBoxOther` (`4_itembox other.png`),
  `ConfigItemBoxCursor` (`4_itembox cursor.png`), `ConfigDescriptionPanel`
  (`4_Description Panel.png`).

- **`Lib/Config/IConfigItem.cs` + `ConfigItems.cs`** — add `string Description { get; }`
  to `IConfigItem`, default `""` on `BaseConfigItem`, settable via an optional ctor
  argument / object initializer. `NavigationConfigItem` and `ReadOnlyConfigItem`
  inherit it. No value-semantics change.

- **`Lib/Stage/ConfigStage.cs`** — refactor:
  - `SetupConfigItems()` → builds the three `ConfigCategory` objects (reusing the
    existing item instances, now with descriptions).
  - Replace flat-list `HandleInput()` / `DrawConfigItems()` / `DrawButtons()` with
    the focus-state machine + the three renderers.
  - `InitializeGraphics()` loads the new textures (best-effort) and disposes them in
    `OnDeactivate` / `Dispose`, matching the current font/background lifecycle.
  - Preserve unchanged: NX-import (`StartNxScoreImport` and its `InlineProgress`),
    key-panel wiring (`InitializePanels`, `OpenPanel`, `OnPanelSaved`/`Closed`),
    `FlushPendingSaveSafely`, and the persist-on-edit contract.

## Error Handling & Edge Cases

- **Missing skin textures** — best-effort load; null-checked draw with
  solid-rectangle / fallback-color stand-ins. Never throws; config stays usable.
- **Font load failure** — keep the current behaviour in `InitializeGraphics`:
  dispose the partially-acquired graphics resources and rethrow.
- **Empty category** — Exit has no items; guard item navigation against a
  zero-length list (no modulo-by-zero). Settings categories always have ≥1 item.
- **Description word-wrap** — greedy wrap to the panel inner width; over-long single
  tokens are drawn as-is (no crash), truncation acceptable.
- **Active key panel overlay** — when `_activePanel.IsActive`, it draws over the new
  layout and swallows input, exactly as today.

## Testing

- **Excluded from `DTXMania.Test.Mac.csproj`** (GraphicsDevice-dependent, like
  `SongBarRenderer*`): `ConfigMenuRenderer`, `ConfigItemListRenderer`,
  `ConfigDescriptionRenderer` tests. Add the new test files to the Mac project's
  explicit-exclude list.
- **Mac-safe logic tests** (no GraphicsDevice):
  - Category construction and item-to-category mapping (System has 7 items in
    order, Drums has 4, Exit has 0).
  - Focus-state transitions: Menu→Items on Enter over a settings category;
    Items→Menu on Esc; Exit/Esc on menu triggers flush + Title.
  - Category index wraps; per-category item index wraps and is remembered across
    category switches.
  - Each item exposes a non-empty `Description`; each category has a `Description`.
- **Update existing `ConfigStage` tests** to the category structure (current tests
  assume a flat `_configItems` list + Exit pseudo-button).
- Where focus/navigation logic needs to be exercised without a GraphicsDevice,
  follow the existing `ConfigStageLogicTests` pattern (subclass `ConfigStage`,
  override `InitializeGraphics`, drive state via `ReflectionHelpers` and the
  fallback-drawing paths, skipping real `OnActivate`).

## Out-of-Scope Follow-ups (noted, not built)

- NX's animated centered scroll carousel for the item list.
- Future Guitar/Bass categories if non-drum instruments are ever added.
- Bilingual description text.
