# Song Bookmarks — Design

**Date:** 2026-06-05
**Status:** Approved (pending implementation plan)

## Overview

Let players bookmark songs they want to find again. A bookmark is a per-song on/off
flag toggled from the song list with a dedicated key and drum pad. Bookmarked songs are
surfaced two ways: a new **Bookmarks** tab in the Song Selection stage (a flat,
alphabetically-sorted list), and a small star marker on bookmarked song bars in the
**All Songs** list.

This feature mirrors the existing **Recent** tab (`2026-06-03-recent-plays-tab-design.md`)
wherever possible — the tab enum, async node loading, stale-continuation guard, and
empty/failed states are the same patterns — plus a new write path (toggling the flag) and a
single additive schema change.

## Goals

- Let players mark/unmark any song and quickly find marked songs later.
- Reuse the existing song-list rendering, difficulty cycling, status panel, preview audio,
  and activation flow so Bookmark rows behave identically to browse rows.
- Allow toggling from both keyboard and the drum kit (Floor Tom pad), since this is a drum game.
- Persist bookmarks across app restarts and song-library rescans.

## Non-Goals

- No per-chart/per-difficulty bookmarks — a bookmark applies to the whole song.
- No folders/boxes in the Bookmarks list — it is flat.
- No filtering/search within the Bookmarks tab.
- No cap/limit on the number of bookmarks.
- No bookmark indicator on the song status/detail panel (only the list star + the tab).

## Requirements Summary

| Decision | Choice |
| --- | --- |
| Granularity | One bookmark per song (whole song) |
| Storage | `IsBookmarked` boolean column on the `Songs` table |
| Toggle input | `B` key **and** Floor Tom drum pad (lane-hit index 1) |
| Surfacing | Dedicated **Bookmarks** tab **and** a star marker in the All Songs list |
| Bookmarks list order | Alphabetical by `Title` (stable tiebreak by `Id`) |
| Bookmarks list size | Uncapped |
| Schema upgrade | Guarded, idempotent `ALTER TABLE` gated by a `__DatabaseVersion` feature row |

## Data Model & Schema Upgrade

Add a single column to the `Song` entity:

```csharp
public bool IsBookmarked { get; set; } = false;
```

- The column is `NOT NULL DEFAULT 0` (`INTEGER` in SQLite).
- **Fresh databases** get the column automatically via `EnsureCreated`/`OnModelCreating`.
- **Existing databases** need a one-time additive migration because the project uses
  `EnsureCreatedAsync()` (not EF migrations), which never alters an existing schema. During
  `SongDatabaseService` initialization, run an idempotent
  `ALTER TABLE Songs ADD COLUMN IsBookmarked INTEGER NOT NULL DEFAULT 0`, gated by a new
  `__DatabaseVersion` feature row (`Feature='SongBookmarks'`) so it executes exactly once.
  This mirrors the existing Unicode-collation reconfiguration path
  (`SongDatabaseService.cs` ~line 676–760).
- The `ALTER TABLE` is also guarded defensively against the column already existing
  (e.g. inspect `PRAGMA table_info(Songs)` or treat a duplicate-column error as success), so
  a missing/partial version row can never crash startup.

**Durability across rescans:** songs are matched by chart `FilePath` during re-enumeration,
so an existing `Song` row (and its `Id`) is preserved across rescans — the bookmark flag
rides along with it. When a song is orphaned (its files vanish) and removed during cleanup,
its bookmark is removed with the row. No separate cleanup logic is required. This is the
primary reason a column on `Songs` is preferred over a separate `Bookmark` table.

## Database Service (`SongDatabaseService`)

Two new methods, following the shape of `GetRecentlyPlayedSongsAsync`:

- `Task SetBookmarkAsync(int songId, bool bookmarked)` — loads the `Song` by id, sets
  `IsBookmarked`, saves. No-op (and no throw) if the song id is not found.
- `Task<List<Song>> GetBookmarkedSongsAsync()` — `Where(s => s.IsBookmarked)`, eager-loads
  `Charts` then `Charts.Scores` (same graph the Recent query loads so callers can build
  fully-populated `SongListNode`s), ordered `OrderBy(s => s.Title).ThenBy(s => s.Id)`.
  Uncapped.

`SongManager` gets a thin async wrapper `GetBookmarkedNodesAsync()` returning
`List<SongListNode>`, mirroring the existing `GetRecentlyPlayedNodesAsync()`, plus a
`SetBookmarkAsync(int songId, bool bookmarked)` pass-through used by the toggle path.

## Toggle Interaction (`SongSelectionStage`)

New private method `ToggleBookmarkForSelectedSong()`:

1. Resolve the highlighted `SongListNode`. Ignore the action if it is not a real song
   (`NodeType.Score`) — folders/back-boxes are not bookmarkable.
2. Flip the song's `IsBookmarked` state in memory so the star refreshes immediately on the
   current frame.
3. Persist via `SongManager.SetBookmarkAsync(songId, newState)` (fire-and-forget with the
   same stale-safe continuation/error-logging discipline used by `BeginRecentPlaysLoad`; a
   persistence failure logs and does not crash the stage).
4. If the active tab is **Bookmarks** and the song was just **un**-bookmarked, remove its row
   from the displayed list (and reconcile the selection index) so the list stays consistent.

Input wiring:

- **Keyboard:** the `B` key, routed through `InputManager` so MCP/API key injection works,
  added to the appropriate handled-keys set in `HandleInput()`.
- **Drum pad:** Floor Tom, lane-hit index **1** in the KeyBindings numbering (distinct from
  the tab-switch pad, Low Tom = lane 8). Handled through the same `OnLaneHit` subscription
  the tab switch already uses — extend the existing `OnTabSwitchLaneHit`/`HandleLaneHit...`
  path (or add a sibling handler) rather than adding a second subscription.
- Both inputs are suppressed while the search modal is open, matching the existing
  tab-switch guards.

## Bookmarks Tab

- Add `Bookmarks` to the `SongSelectionTab` enum.
- Extend `SongSelectionTabExtensions.Next()` to cycle
  `AllSongs → RecentPlays → Bookmarks → AllSongs`, and add the `Bookmarks` arm to
  `DisplayLabel()` (label: `"Bookmarks"`).
- Async load mirrors recent-plays exactly:
  - `private volatile List<SongListNode>? _bookmarkNodes;`
  - `BeginBookmarksLoad()` calls `SongManager.GetBookmarkedNodesAsync()` with the same
    activation-token guard against stale continuations and a `_bookmarksLoadFailed` flag.
  - Triggered on stage activation and when switching to the Bookmarks tab (`SwitchToNextTab`).
  - `PopulateBookmarksList()` and an empty/failed-state message
    (empty: "No bookmarks yet"; failure: an error message), gated to the active tab and
    non-modal state like the Recent equivalents.
- Back/navigation actions remain gated to the All Songs tab, as they already are.

## Star Indicator in All Songs

- Render a small star marker on song bars whose node is bookmarked, in the All Songs list
  (and naturally in the Bookmarks tab, where every row is bookmarked).
- The bookmarked state is read from the `SongListNode`/song the bar represents; toggling
  updates that state in memory (see Toggle step 2) so the marker appears/disappears on the
  next frame without a reload.
- Keep the "is this bar bookmarked" decision in a render-agnostic, unit-testable spot; the
  actual sprite draw lives on the song-bar rendering path. (Note: `SongBarRenderer` tests are
  excluded from the Mac test project, so logic that must be tested on Mac should not live
  inside the renderer.)

## Testing

`SongDatabaseService` (Mac-safe):
- Set then read back a bookmark; clear it; toggling is idempotent.
- `GetBookmarkedSongsAsync` returns only bookmarked songs, alphabetical by title with the
  `Id` tiebreak, and eager-loads the Charts+Scores graph.
- Schema upgrade: against a DB created without the column, initialization adds it once,
  is idempotent on a second run, and tolerates the column already existing.
- Orphan cleanup removes a bookmarked song's row when its files are gone (no dangling flag).

`SongSelectionStage` (mirroring the recent-plays edge-case tests):
- Toggle on a `Score` node flips state and persists; toggle on a folder/back node is a no-op.
- Stale-continuation guard: a slow bookmarks load from a previous activation does not
  overwrite the current activation's list.
- Tab-switch gating and empty/failed-state rendering for the Bookmarks tab.
- Un-bookmarking on the Bookmarks tab removes the row and keeps the selection valid.
- Toggle inputs are ignored while the search modal is open.

## Risks & Mitigations

- **Schema upgrade on existing DBs is the main risk.** Mitigated by the version-gated,
  idempotent, defensively-guarded `ALTER TABLE` and a dedicated test against a
  column-less database.
- **Floor Tom is a primary play pad**, but song-select consumes no lane hits except the
  tab-switch (lane 8), so reusing it for the bookmark toggle has no conflict in this stage.
