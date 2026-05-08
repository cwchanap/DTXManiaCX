# Song Search & Filter UI — Design Spec

**Date:** 2026-05-07
**Status:** Design approved, ready for implementation plan
**Stage:** Song Selection (`SongSelectionStage`)

## Goal

Add a search, filter, and sort UI to the song selection stage so players can quickly find songs in libraries that have grown beyond comfortable hierarchical browsing. The feature is keyboard-driven, modal, and operates against the in-memory song database.

## Scope decisions

| Decision | Choice | Notes |
| --- | --- | --- |
| Feature set | Search + filter + sort | Full reorganization tools, not just search. |
| Interaction model | Modal overlay | Triggered by keyboard, dimmed list underneath. |
| Filter facets | Level range, Played status | Genre dropped — DTX `Genre` field is unreliable in practice. |
| Sort criteria | Title, Artist, Level (each Asc/Desc) | Genre dropped from sort selector for the same reason; `SongSortCriteria.Genre` stays in the public enum. |
| Search scope | Whole library, flattened | Hierarchy ignored during search. |
| Search match | Title + Artist, case-insensitive substring | Matches existing `SearchSongsAsync` semantics. |
| Search behavior | Submit on Enter (not live) | Deliberate, no per-keystroke re-filter. |
| Backing data | In-memory `SongListNode` set | No DB round-trips during filtering. |
| Persistence | Session-scoped (within app run) | Reset on app exit, on Reset button, or on `/q`. |
| Modal trigger | `Backspace` | Free in current `InputManager` bindings. |
| Text input | `GameWindow.TextInput` event | Standard MonoGame text-entry path. |
| Reset path | Reset button + `/q` slash command | Both work; `/q` matches DTXManiaNX habit. |
| Font | `ManagedFont` (MonoGame `SpriteFont`) | Consistent with most other Song-Select UI. |

## Architecture

### Approach: filter-as-view (not filter-as-replacement)

`SongSelectionStage` keeps `_hierarchicalCurrentList` (the current navigation level's children) untouched at all times. When a filter is active, a separate `_filteredView` projection is computed once on Apply and `SongListDisplay` is bound to it. Reset drops the projection and rebinds to `_hierarchicalCurrentList`. Hierarchy state and the navigation stack are never corrupted by filter activity.

### New files

**`DTXMania.Game/Lib/Song/Filtering/SongFilterCriteria.cs`**
Value record holding active filter state.

```csharp
public sealed record SongFilterCriteria(
    string SearchQuery,
    int? MinLevel,
    int? MaxLevel,
    PlayedStatus PlayedStatus,
    SongSortCriteria SortBy,
    bool SortDescending)
{
    public static SongFilterCriteria Default { get; } = new(
        SearchQuery: "",
        MinLevel: null,
        MaxLevel: null,
        PlayedStatus: PlayedStatus.All,
        SortBy: SongSortCriteria.Title,
        SortDescending: false);

    public bool IsEmpty =>
        string.IsNullOrEmpty(SearchQuery)
        && MinLevel is null
        && MaxLevel is null
        && PlayedStatus == PlayedStatus.All
        && SortBy == SongSortCriteria.Title
        && !SortDescending;
}
```

**`DTXMania.Game/Lib/Song/Filtering/PlayedStatus.cs`**

```csharp
public enum PlayedStatus { All, Unplayed, Played, Cleared }
```

**`DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs`**
Pure projection. Walks all `Score`-typed nodes from `SongManager`, captures parent breadcrumb, applies search → level → played → sort, returns a flat list of results. `BackBox` and `Random` nodes are excluded.

```csharp
public readonly record struct FilteredSongResult(
    SongListNode Node,
    string FolderPath); // e.g., "J-POP / 80s"

public interface ISongListFilterService
{
    IReadOnlyList<FilteredSongResult> Apply(
        IEnumerable<SongListNode> roots,
        SongFilterCriteria criteria);
}
```

Pipeline:

```
flatten(roots) → keep Score nodes only, capture parent breadcrumb
  → search:    Title or Artist contains query (StringComparison.OrdinalIgnoreCase)
  → level:     MaxDifficultyLevel ∈ [MinLevel ?? int.MinValue, MaxLevel ?? int.MaxValue]
  → played:    derive from SongScore (see below)
  → sort:      reuse SongListNode comparison logic; honor SortDescending
```

`PlayedStatus` derivation:
- `All`: no-op.
- `Unplayed`: no `SongScore` row OR all chart `PlayCount == 0`.
- `Played`: any chart `PlayCount > 0`.
- `Cleared`: any chart `BestRank` indicates a clear. Exact rank set TBD during implementation — read existing rank semantics from `SongScore`/`Rank` enum and document the chosen set in the implementation PR.

If `MinLevel > MaxLevel`, swap silently on Apply.

**`DTXMania.Game/Lib/UI/Components/UITextInput.cs`**
Reusable single-line text input element.
- Subscribes to `GameWindow.TextInput` only while focused.
- Renders caret, handles `Backspace` for character deletion, basic character insertion.
- Optional max-length clamp.
- Paste (`Ctrl+V`) deferred to v2 unless trivially supported on the current MonoGame variant on Mac (DesktopGL).
- Reusable beyond this feature.

**`DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs`**
The modal overlay. Composition:
- `UITextInput` (search box)
- Min level / Max level numeric inputs
- Played-status radio group
- Sort criterion selector (cycles Title → Artist → Level)
- Sort direction toggle (Asc / Desc)
- Reset button
- Apply button

Surfaces events: `FilterApplied(SongFilterCriteria)`, `FilterReset`, `Cancelled`.

### Modified files

- `SongSelectionStage.cs` — owns the session-scoped `SongFilterCriteria` and `_filteredView`; on Backspace opens the modal; binds `SongListDisplay` to `_filteredView ?? _hierarchicalCurrentList`; suspends folder navigation while filter active.
- `SongListDisplay.cs` — accept a "flat-mode" flag so `..(Back)` rows are hidden when displaying filtered results.
- `SongStatusPanel.cs` — when a filter is active, render the selected node's `FolderPath` as a small "From: …" label near the top of the panel.
- `InputManager.cs` — add `InputCommandType.OpenSearch`, map `Keys.Back` to it.
- `SongSelectionUILayout.cs` — add `SearchFilterModal` static class containing all modal layout constants (modal size ≈ 600 × 360, centered).

### Data flow

```
            [Backspace pressed in song list]
                          │
                          ▼
              SongSearchFilterModal opens
              populated with current criteria
                          │
              ┌───────────┼───────────┐
            Apply        Reset       Cancel
              │            │            │
              ▼            ▼            ▼
   service.Apply        criteria      no-op
   returns flat         = Default     (modal closes,
   FilteredSongResult   _filteredView  edits discarded)
                          = null
              │            │
              └────────────┘
                       │
                       ▼
   SongListDisplay.SetSongList(
     _filteredView ?? _hierarchicalCurrentList)
   SongStatusPanel.SetSelectedFolderPath(...)
   _breadcrumbLabel.Text = filtered ? summary : hierarchy path
```

`_filteredView` is rebuilt only on Apply, not per-frame.

## Modal UI

```
+------------------------------------------------+
|             SEARCH & FILTER                    |
|                                                |
|  Search:   [_______________________________]   |
|                                                |
|  Level:    Min [ 50 ]      Max [ 85 ]          |
|                                                |
|  Played:   ( ) All  (•) Unplayed               |
|            ( ) Played   ( ) Cleared            |
|                                                |
|  Sort by:  [ Title ▼ ]    Order: [ Asc ▼ ]     |
|                                                |
|         [ Reset ]      [ Apply ]               |
|                                                |
+------------------------------------------------+
```

### Focus model

Tab cycle order:
1. Search box (`UITextInput`) — focused on open.
2. Min level (numeric, `Left`/`Right` ±5; clamped to 0–99 range used by DTX charts; empty / 0 means "no minimum").
3. Max level (same bounds; empty / 0 means "no maximum").
4. Played status radio group (`Left`/`Right` cycles).
5. Sort criterion (cycles Title → Artist → Level).
6. Sort order (toggles Asc / Desc).
7. Reset button.
8. Apply button.

`Tab` / `Down` advances; `Shift+Tab` / `Up` reverses.

### Input handling while modal is open

- Stage-level `InputCommandType` processing is **suspended** — modal owns input.
- Opening the modal from status-panel mode (`_isInStatusPanel == true`) implicitly exits status-panel mode first; on modal close, the stage returns to song-list focus regardless of the previous panel state.
- `GameWindow.TextInput` subscribed only while search box is focused. Unsubscribed on focus loss / modal close.
- Direct keyboard handling in modal:
  - `Esc` → Cancel: close modal, discard edits, restore previous applied state.
  - `Enter` → Apply (default action) when not on Reset button; activates focused button otherwise.
  - `Tab` / `Shift+Tab` / `Up` / `Down` → focus cycling.
  - `Left` / `Right` → adjust focused field (numeric, radio, cycle).
  - `Backspace` → handled by `UITextInput` for character deletion. Does **not** re-open the modal (modal is already open; binding only triggers from the song list).

### Slash command `/q`

If the search box content is exactly `/q` (case-insensitive) and Enter is pressed, treat as Reset (clear all to defaults, close modal). Any other `/`-prefixed input is literal text — no other slash commands planned.

### Cancel vs Apply semantics

- Modal opens populated with the **currently-applied** state.
- Edits → `Esc` → discarded; applied state unchanged.
- Edits → Apply (or Enter from search box with non-`/q` content) → committed; projection recomputed.
- Reset (button or `/q`) → criteria reset to `Default`; projection cleared; modal closes.

## Result rendering & filter indicators

### Filter-active indicator

When a filter is active, the existing breadcrumb label at the top of the screen shows a compact summary instead of the hierarchy path:

```
Filtered: "beatles" · Lv 50–85 · Unplayed · Title↑
```

Empty parts elide. When no filter is active, breadcrumb behaves as today.

### Status panel folder origin

When a filter is active, `SongStatusPanel` renders an extra small label near the top: the breadcrumb path of the selected node, e.g. `From: J-POP / 80s`. The path is computed once during projection (walks `SongListNode.Parent`) and cached on `FilteredSongResult.FolderPath` so the panel doesn't rewalk per frame.

### Empty state

When the filter projection returns zero results, the song list area renders a centered `No songs match this filter` message using `ManagedFont`. Status panel and preview panel hide their content. User can press `Backspace` to re-open the modal and adjust, or `Esc` to leave the song selection stage.

### Selection clamping

- On Apply: keep previously-selected song if still in result set; otherwise select index 0.
- On Reset: select the song that was selected pre-filter, if it's still in the hierarchy at the current navigation level; otherwise select index 0.
- On performance-stage return: same rule — keep selection if valid; otherwise clamp.

### Folder navigation while filtered

- `Activate` (Enter) on a `Score` node → proceed to performance as normal.
- `Back` (Esc) on the song list → exits the song selection stage to Title, as today. Filter persists across that exit (session-scoped), so re-entering Song Select shows the same filtered view.

## Persistence

- `SongFilterCriteria` lives on `SongSelectionStage` as a private field.
- Survives: Esc → Title → re-enter Song Select within the same app run; performance round-trips; SongTransition.
- Resets on: app exit (no `Config.ini` save); Reset button; `/q` slash command.
- Sort criterion is **not** persisted to `Config.ini`.

## Edge cases

- **Library not yet loaded**: modal opens with search box and Apply disabled, showing a `Library still loading…` hint. Reset still works. Re-enables when `_songInitializationTask` completes.
- **Zero results**: empty-state message; navigation no-op; status / preview hidden.
- **Selected song removed by re-Apply**: clamp per rules above.
- **`MinLevel > MaxLevel`**: swap silently on Apply.
- **Empty search + no facets + default sort = noop filter**: treated as Reset; modal closes; projection cleared.
- **Very large libraries (10k+ songs)**: in-memory projection runs once on Apply; even at 50k songs a single linear scan + sort is well under a frame. No paging.
- **Missing `SongScore`**: treated as `Unplayed`. `Cleared` rank set TBD during implementation; document the chosen set.
- **IME / international input**: `GameWindow.TextInput` handles UTF-16 surrogates and committed IME text. In-progress composition events not handled in v1 — characters appear after commit only. Acceptable.
- **Clipboard paste**: deferred to v2 unless trivially available cross-platform.

## Error handling

- If `SongListFilterService.Apply` throws (defensive), log to `Debug.WriteLine`, leave the existing list reference unchanged, keep the modal open with an in-modal error message (`Filter failed — try a different query`). Don't swallow silently.
- Modal must always unsubscribe `GameWindow.TextInput` on Cancel / Apply / Reset / stage deactivate. Tested.

## Testing

| Test class | Mac suite? | Coverage |
| --- | --- | --- |
| `SongListFilterServiceTests` | ✅ | Search-only, level-only, played-only, sort-only, combinations, min>max swap, empty result, BackBox/Random exclusion, folder path. |
| `SongFilterCriteriaTests` | ✅ | Record equality, `Default`, `IsEmpty`. |
| `UITextInputTests` | ✅ | Caret advance/retreat, character insert, Backspace, max-length, focus on/off subscribe-unsubscribe (mock `GameWindow`). |
| `SongSearchFilterModalTests` | ❌ (graphics) | Focus cycling, `/q` detection, Apply / Reset / Cancel events. |
| `SongSelectionStageFilterTests` | ✅ where possible | Filter activation toggles `_filteredView`, selection clamping, persistence across stage re-entry, Reset clears filter, library-loading edge case disables Apply. |

`DTXMania.Test.Mac.csproj` updated to exclude graphics-dependent modal tests, include the rest.

## Implementation phasing (suggested)

1. **Data layer** — `SongFilterCriteria`, `PlayedStatus`, `SongListFilterService` + tests. No UI dependency.
2. **Reusable text input** — `UITextInput` + tests.
3. **Modal UI** — `SongSearchFilterModal`, layout constants, smoke-test by opening manually.
4. **Stage integration** — `SongSelectionStage` wiring (Backspace trigger, modal lifecycle, view binding, breadcrumb summary, status panel folder hint, empty state, selection clamping).
5. **Polish** — `/q` slash-command detection, error-handling path, library-loading edge case, IME smoke check. (Reset button itself ships with the modal in phase 3; its event wiring to clear `_filteredView` ships with stage integration in phase 4.)
6. **Manual UI checklist** — run before declaring done.

## Manual UI test checklist

- Open modal, type query, Enter → filtered list correct.
- Open modal, set level range, Enter → filtered list correct.
- Open modal, Played = Unplayed, Enter → only unplayed songs.
- Combine search + level + played → correct intersection.
- Sort changes reorder list correctly.
- Reset button clears filter.
- `/q` then Enter clears filter.
- Esc cancels edits without applying.
- Re-open modal — shows current applied state, not reset.
- Esc back to Title, re-enter Song Select — filter still applied.
- Empty result set shows `No songs match this filter`.
- Backspace in search box deletes a character; doesn't re-open modal.
- IME input (e.g., Japanese): characters commit correctly into search box (smoke).
- Performance round-trip preserves filter and selection where possible.
