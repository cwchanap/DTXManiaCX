# Config Stage NX Layout Revamp — Design

**Date:** 2026-06-25
**Status:** Implemented. Subsequently refined: `ConfigItemBoxOther` was removed and item-box rendering unified (see commit `09756ac`). References to `ConfigItemBoxOther` below are retained as the original design intent; the shipped code uses a single `ConfigItemBox` texture.

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

Follows the existing `*UILayout` convention and `ConfigStage`'s established
render-spy seam. **Rendering decision:** `ConfigStage` already routes its
primitive drawing through overridable `protected virtual` seams
(`BeginDrawFrame` / `EndDrawFrame` / `DrawFilledRectangle` / `GetViewport`) and
guards every texture draw with a null check, which lets `RenderSpyConfigStage`
exercise the draw logic on Mac with no GraphicsDevice. We therefore keep the NX
rendering **in-stage** through that same seam rather than introducing separate
GraphicsDevice-dependent renderer classes (which would force Mac-excluded tests).
Only the pure, fully-testable pieces are extracted into focused files.

- **`Lib/UI/Layout/ConfigUILayout.cs`** *(new, pure / no graphics)* — every
  coordinate, size, and helper that maps an index to a `Rectangle` / `Vector2`:
  panel origins, menu row stride, menu cursor rect, item-row rect, item name/value
  positions, description panel rect + text inset/wrap width, header/footer/title
  placement. The stage carries no magic numbers. Fully Mac-testable.

- **`Lib/Stage/Config/ConfigCategory.cs`** *(new, pure / no graphics)* — model:
  `string Name`, `string Description`, `IReadOnlyList<IConfigItem> Items`, mutable
  `int SelectedIndex`, `bool HasItems`, `IConfigItem? SelectedItem`, and
  `MoveSelectionUp()` / `MoveSelectionDown()` (wrap, no-op when empty). The **Exit**
  category is simply a category with zero items (`HasItems == false`); no special
  flag. Fully Mac-testable.

- **`Lib/Resources/TexturePath.cs`** — add constants and register them in
  `GetAllTexturePaths()` (all ten) and `GetPanelTextures()` (the nine panel/art
  ones, i.e. all except `ConfigBackground`): `ConfigBackground`
  (`Graphics/4_background.png`), `ConfigItemBar` (`4_item bar.png`),
  `ConfigMenuPanel` (`4_menu panel.png`), `ConfigMenuCursor` (`4_menu cursor.png`),
  `ConfigHeaderPanel` (`4_header panel.png`), `ConfigFooterPanel`
  (`4_footer panel.png`), `ConfigItemBox` (`4_itembox.png`), `ConfigItemBoxOther`
  (`4_itembox other.png`), `ConfigItemBoxCursor` (`4_itembox cursor.png`),
  `ConfigDescriptionPanel` (`4_Description Panel.png`).

- **`Lib/Config/IConfigItem.cs` + `ConfigItems.cs`** — add `string Description { get; }`
  to `IConfigItem`; `BaseConfigItem` implements it as `{ get; init; } = ""`, set via
  object initializer at item-construction. `NavigationConfigItem` and
  `ReadOnlyConfigItem` inherit it. No value-semantics change.

- **`Lib/Stage/ConfigStage.cs`** — refactor (single file, reusing its render seam):
  - State: replace `_configItems` / `_selectedIndex` with `_categories`
    (`List<ConfigCategory>`), `_currentCategoryIndex`, and `_focusOnMenu`.
  - `SetupConfigItems()` → builds the three `ConfigCategory` objects (reusing the
    existing item instances, now with `Description` set).
  - `HandleInput()` → the two-focus state machine (Menu ↔ Items).
  - Replace `DrawTitle` / `DrawConfigItems` / `DrawButtons` / `DrawInstructions`
    with new `protected virtual` draw methods consuming `ConfigUILayout`:
    `DrawConfigBackground`, `DrawItemBar`, `DrawCategoryMenu`, `DrawItemList`,
    `DrawDescriptionPanel`, `DrawHeaderFooter`. Selection highlights draw a texture
    when present, else a `DrawFilledRectangle` fallback (the assertable seam).
  - `InitializeGraphics()` loads the new textures best-effort; `OnDeactivate` /
    `Dispose` release them, matching the current font/background lifecycle.
  - Preserve unchanged: NX-import (`StartNxScoreImport` + `InlineProgress`),
    key-panel wiring (`InitializePanels`, `OpenPanel`, `OnPanelSaved`/`Closed`),
    `DrawImportStatus`, `FlushPendingSaveSafely`, persist-on-edit contract, and the
    `IsConfigNavigationCommandPressed` / `IsPanelCommandPressed` input readers.

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

Everything is Mac-testable — **no new `DTXMania.Test.Mac.csproj` exclusions** —
because rendering stays on `ConfigStage`'s render-spy seam and the new helpers are
pure.

- **`ConfigUILayout`** (pure): assert each helper returns the documented NX
  rectangle/position for given indices.
- **`ConfigCategory`** (pure): construction; `SelectedIndex` wrap on
  up/down; empty category (`HasItems == false`, `SelectedItem == null`, moves are
  no-ops).
- **Config item `Description`**: default `""`; round-trips through the object
  initializer.
- **`ConfigStage` logic** (existing reflection / `ForcedCommandInputManager`
  pattern, Mac-safe):
  - `SetupConfigItems` builds 3 categories — System (7 items, in order), Drums
    (4 items), Exit (0 items) — each with a non-empty `Description`, and each item
    with a non-empty `Description`.
  - Focus transitions: Menu→Items on Activate over System/Drums; Activate over Exit
    flushes + returns to Title; Esc in Items returns to Menu; Esc in Menu flushes +
    returns to Title.
  - Category index wraps on up/down; per-category item index wraps and is remembered
    across category switches.
- **`ConfigStage` rendering** (existing `RenderSpyConfigStage` pattern, Mac-safe,
  textures + fonts null): `DrawCategoryMenu` emits a highlight rect at
  `ConfigUILayout.MenuCursorRect(currentCategoryIndex)`; `DrawItemList` emits an
  item-cursor rect at `ItemCursorRect(selectedIndex)` only when `_focusOnMenu ==
  false`; `DrawConfigBackground` falls back to the light fill when the texture is
  absent.
- **Update existing `ConfigStageLogicTests` / `ConfigStageTests`** that assume the
  flat `_configItems` / `_selectedIndex` / Exit-pseudo-button model: delete the
  obsolete `DrawConfigItems` / `DrawButtons` / flat-list-navigation tests, add the
  category-structure and focus-model replacements above.

## Out-of-Scope Follow-ups (noted, not built)

- NX's animated centered scroll carousel for the item list.
- Future Guitar/Bass categories if non-drum instruments are ever added.
- Bilingual description text.
