# Recent Plays Tab in Song Selection — Design

**Date:** 2026-06-03
**Status:** Approved (pending implementation plan)

## Overview

Add a tab bar to the Song Selection stage with two tabs: **All Songs** (the existing
hierarchical browse view) and **Recent**. Selecting the Recent tab replaces the song-list
contents with a flat list of up to 20 songs, ordered by most-recently-played first. The
data comes from the existing `SongScore.LastPlayedAt` field — there are no schema changes,
and the currently-unused `PerformanceHistory` table is left untouched.

The tab infrastructure is intentionally lightweight (an enum plus per-tab node providers)
but structured so a future tab (e.g. Favorites) is a small additive change rather than a
refactor.

## Goals

- Let players quickly return to songs they played recently.
- Reuse the existing song-list rendering, difficulty cycling, status panel, preview audio,
  and activation flow so Recent rows behave identically to browse rows.
- Allow tab switching from both keyboard (Tab) and the drum kit (Low Tom pad), since this is
  a drum game.

## Non-Goals

- No per-play history log (one row per song, not one row per play event).
- No wiring up of the `PerformanceHistory` table.
- No filtering/search within the Recent tab.
- No folders/boxes in the Recent list — it is flat.

## Requirements Summary

| Decision | Choice |
| --- | --- |
| Presentation | Top tab bar with multiple tabs (`All Songs`, `Recent`) |
| Data granularity | One row per song (aggregate), using `SongScore.LastPlayedAt` |
| List size / order | Up to 20 songs, most-recently-played first |
| Tab-switch input | `Tab` key **and** Low Tom drum pad (lane 8) |
| Tab architecture | Lightweight enum + data-driven per-tab node providers |

## Architecture

### Data Layer

**`SongDatabaseService.GetRecentlyPlayedSongsAsync(int limit = 20)`**

- Queries `SongScores` where `LastPlayedAt != null`.
- Groups by song: a song with multiple difficulty charts collapses to a single row, using
  the **maximum** `LastPlayedAt` across its charts.
- Orders descending by that timestamp, takes `limit`.
- Loads each resulting song with all its charts and scores.
- Returns an ordered collection carrying the song entity, its charts, and the last-played
  timestamp (timestamp retained for ordering and potential future display).

**`SongManager.GetRecentlyPlayedNodesAsync(int limit = 20)`**

- Calls the DB service.
- Converts each result into a `SongListNode` via the existing
  `CreateSongNodeFromDatabaseEntities(SongEntity song, SongChart[] charts)`, so the Recent
  nodes are first-class (full chart/difficulty data, scores, preview metadata).
- Returns `List<SongListNode>`, all `Type = Score`, flat (no `Box`/`BackBox`).

### Tab Model (lightweight, data-driven)

- New enum `SongSelectionTab { AllSongs, RecentPlays }`.
- `SongSelectionStage` holds `_activeTab` plus a small tab-descriptor list. Each descriptor
  carries: a display label and a reference to the node-list provider that feeds
  `SongListDisplay` for that tab.
  - `AllSongs` provider: the existing hierarchical/filtered view (unchanged).
  - `RecentPlays` provider: the recent-nodes list from `GetRecentlyPlayedNodesAsync`.
- Adding a future tab = add an enum value, one descriptor, and one provider method. No
  abstract framework / interface hierarchy is introduced.

### UI — Tab Bar

- A lightweight render block at the top of the stage (in the existing title/breadcrumb
  region) drawing the tab labels and highlighting the active tab.
- Uses the stage's existing font and `_whitePixel` direct-draw path.
- Layout values centralized in a new `Tabs` section under `SongSelectionUILayout`.
- Guarded for headless/test environments like the other draw paths in the stage.

### Input

- New `InputCommandType.NextTab`.
- Mapped from the **Tab key** in `InputManager`'s key map. It is routed to tab-switching
  only when no modal is open; while the search/filter modal is open it continues to consume
  Tab as a raw key (unchanged behavior).
- **Low Tom drum pad**: the stage subscribes to
  `InputManagerCompat.ModularInputManager.OnLaneHit` and fires `NextTab` when `e.Lane == 8`
  (Low Tom in the lane-hit numbering; note this lane is shared with Right Cymbal in the
  default bindings, so either pad triggers the switch). Subscription is established on stage
  activation and removed on deactivation.
- `NextTab` cycles `AllSongs → RecentPlays → AllSongs`. A cursor-move sound plays on switch.

## Behavior & Edge Cases

- **Empty Recent list:** show a centered "No recent plays yet" message (reusing the existing
  empty-filter message path). The list is non-interactive; the user can Tab back to All
  Songs.
- **Search/filter modal:** applies only to `All Songs`. The Recent tab shows the unfiltered
  20. Opening search is ignored/disabled while on the Recent tab.
- **Refresh timing:** the recent list is (re)queried on stage activation and when switching
  into the Recent tab, so a song just played moves to the top when the user returns from the
  Result stage.
- **Selecting a recent song:** identical to All Songs — difficulty cycling, status panel,
  preview audio, and Enter→Performance reuse the existing flows.
- **Active tab on entry:** resets to `All Songs` on each activation for predictability.
- **Filter state:** All Songs filter/sort state is preserved across a tab round-trip
  (switch to Recent and back leaves the All Songs view as it was).

## Testing

- **DB query** (`GetRecentlyPlayedSongsAsync`): newest-first ordering; the 20-row cap;
  per-song grouping (a multi-chart song appears once, at its latest play); exclusion of rows
  with `LastPlayedAt == null`. Use the existing in-memory/SQLite test pattern.
- **Tab state machine:** `NextTab` cycles correctly; switching swaps the list source;
  All Songs filter state is preserved across a round-trip.
- **Input mapping:** the Tab key and a lane-8 hit both enqueue `NextTab`; pressing Tab while
  the modal is open does not switch tabs.
- **Empty-state logic:** headless-safe rendering decision when the recent list is empty.
- **Mac test project:** any new tests must stay graphics-free or be excluded from
  `DTXMania.Test.Mac.csproj` (per its explicit compile-include list).

## Affected Files (anticipated)

- `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs` — new recent-plays query.
- `DTXMania.Game/Lib/Song/SongManager.cs` — new node-building wrapper.
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs` — tab state, tab bar render, input wiring.
- `DTXMania.Game/Lib/Input/InputManager.cs` — `NextTab` command + Tab key mapping.
- `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs` — new `Tabs` layout section.
- New `SongSelectionTab` enum (location with the stage or song-select UI types).
- Tests under `DTXMania.Test/` (graphics-free; otherwise excluded from the Mac project).

## Risks / Notes

- **Lane numbering:** the visual `PerformanceUILayout.LaneType.LT = 6`, but the `OnLaneHit`
  event uses the `KeyBindings` lane scheme where **Low Tom is lane 8** (shared with Right
  Cymbal). Implementation must use the lane-hit scheme (8), not the visual scheme (6).
- **Tab key vs. modal:** ensure the Tab→`NextTab` routing yields to the modal's existing raw
  Tab handling when the modal is open, to avoid regressions in search.
