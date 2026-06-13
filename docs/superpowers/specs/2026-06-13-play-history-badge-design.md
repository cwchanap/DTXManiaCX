# Play History Badge For Song Selection

**Date:** 2026-06-13

**Scope:** Add the DTXManiaNX-style play history badge to the song selection stage. The badge shows up to five recent plays for the currently selected difficulty, with date, result/grade, and achievement rate.

## Context

DTXManiaCX already has most of the raw pieces:

- NX score import parses `History0` through `History4`.
- `PerformanceHistory` persists imported history rows.
- `System/Graphics/5_play history panel.png` exists.
- `SongStatusPanel` already renders song-selection score metadata and difficulty changes.

The missing piece is the visible selection-stage badge. The existing history persistence is song-scoped, but the requested behavior is difficulty-scoped: changing the selected difficulty should change the badge contents.

## Goals

1. Show the play history badge in song selection for the currently selected difficulty only.
2. Show at most five rows, newest first.
3. Preserve imported NX row text where available.
4. Append CX-generated rows after each completed play.
5. Avoid showing ambiguous legacy song-wide history under the wrong difficulty.

## Non-Goals

- Do not redesign the song selection layout.
- Do not infer difficulty from old song-wide history text.
- Do not change the Recent Plays tab behavior.
- Do not add guitar/bass result persistence beyond the existing DRUMS result path.

## Data Model

`PerformanceHistory` becomes score-scoped while keeping `SongId`:

- Add nullable `SongScoreId`.
- Add `SongScore` navigation.
- Keep `SongId` for cleanup and compatibility with existing rows.
- Replace the unique `(SongId, DisplayOrder)` constraint with a unique `(SongScoreId, DisplayOrder)` constraint for scoped rows.
- Keep an index on `SongId` for lookup/cleanup.

New rows must always set `SongScoreId`. Existing legacy rows with null `SongScoreId` stay in the database but are not rendered by the selected-difficulty badge.

This prevents wrong-difficulty history from appearing. Re-running NX import repopulates history with score ownership.

The SQLite upgrade path must add `SongScoreId` as nullable and remove the old unique `(SongId, DisplayOrder)` index without deleting existing history rows.

## Import Flow

`NxScoreImporter.MergeAsync` already resolves the DRUMS `SongScore` for the chart. It should write parsed history rows to that score:

1. Load existing `PerformanceHistory` rows for the resolved `SongScoreId`.
2. Merge existing scoped rows and imported `NxHistoryLine` rows.
3. De-duplicate by `HistoryLine`.
4. Keep newest five by parsed date.
5. Delete/reinsert rows for that score with `DisplayOrder` 1 through 5.

History from two charts under the same song must remain separate because each chart has its own `SongScoreId`.

## CX Save Flow

`SongDatabaseService.UpdateScoreAsync(chartId, instrument, PerformanceSummary)` should append a visible history row after updating the score.

The row format should stay close to NX:

```text
{playCount}.{yy}/{M}/{d} {Cleared|Failed} ({grade}: {achievementRate:F2})
```

Use:

- `playCount`: the score's incremented play count.
- date: format the row from `DateTime.UtcNow.ToLocalTime()`, while storing `PerformedAt` in UTC.
- status: `Cleared` when `summary.ClearFlag` is true, otherwise `Failed`.
- grade: `SongScore.RankString((int)Math.Floor(summary.PlayingSkill))`.
- achievement rate: `summary.PlayingSkill`, because it is the NX-style 0-100 play-skill/achievement value already used by Result.

After insertion, keep only the newest five rows for that `SongScoreId` and rewrite display order.

## Song Loading

Song selection nodes need the selected score's rows without per-selection database queries.

Loading paths that already include `Charts -> Scores` should also load each score's `PerformanceHistory` rows. `SongListNode.PopulatePlayHistoryFromCharts` should copy scoped rows into the matching in-memory `SongScore` clone.

The in-memory score should expose a read-only list or array of history display lines. The renderer should read from the selected difficulty's score, not from the parent song.

## UI

Add a dedicated `PlayHistoryPanel` UI component as a sibling of `SongStatusPanel` in `SongSelectionStage`.

The component:

- Loads `TexturePath.PlayHistoryPanel` (`Graphics/5_play history panel.png`).
- Draws at NX coordinates `x=700`, `y=570`.
- Uses the asset natural size (`458x151`).
- Draws text at `x+18`, `y+32`.
- Draws five max rows, yellow, newest first.
- Uses the stage UI font/managed font path and shadow style consistent with nearby selection UI.
- Hides when the selected node is not a score.
- Shows the badge background for the selected difficulty even when it has no scoped history rows yet.
- Hides when the selected difficulty slot itself is empty.
- Updates on both song selection change and difficulty change.

The panel should be independent of `SongStatusPanel` internals. `SongSelectionStage` owns both components and sends them the same selected song/difficulty updates.

## Error Handling

- Missing `5_play history panel.png`: draw no background, but keep text rendering if a font exists.
- Missing font: no-op draw.
- Missing/empty history rows: draw the badge background without text rows.
- Disposed texture: clear the cached reference and skip background drawing.
- Database schema upgrade failure: skip history badge data rather than blocking song selection.

## Testing

Add focused coverage for:

- `PerformanceHistory` schema/query behavior: rows are scoped to `SongScoreId`, limited to five, ordered newest first.
- NX import: two charts for the same song keep separate history rows.
- CX result save: appends a current-play row with date, status, grade, and achievement rate, then drops the sixth-oldest row.
- Song-node loading: the selected score carries its scoped history lines into song selection.
- UI logic: `PlayHistoryPanel.UpdateSongInfo(song, difficulty)` hides on no data and switches rows when difficulty changes.
- Texture paths: `Graphics/5_play history panel.png` is exposed via `TexturePath` and included in panel texture lists.

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceHistory|FullyQualifiedName~NxScoreImporter|FullyQualifiedName~SongListNode|FullyQualifiedName~PlayHistoryPanel|FullyQualifiedName~TexturePath"
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

## Acceptance Criteria

1. Selecting a song difficulty with scoped history shows the play history badge.
2. Changing difficulty changes the five rows to that difficulty's history.
3. Folders, back entries, random entries, and unavailable difficulty slots show no badge.
4. NX-imported rows display their original text.
5. A newly completed CX play appears in that difficulty's badge after returning to song selection and reloading data.
6. Legacy song-wide rows with no `SongScoreId` do not render.
