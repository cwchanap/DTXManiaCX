# Empty Song Select recovery design

## Decision

Treat #162 as a presentation/recovery-copy defect, not a new navigation feature.

`SongSelectionStage` already distinguishes these library states:

- no active roots;
- active roots with no supported charts;
- songs available.

The fix keeps that model and gives the first two states actionable Config guidance. The existing Config `SongFolderPanel` remains the single UI for viewing/editing paths and showing inaccessible-root reasons.

## Non-goals

- no direct Song Select -> Config deep-link;
- no duplicate song-root path summary in the Config master list;
- no song discovery or publication changes;
- no Config input changes (#163 is separate);
- no Song Select list rendering changes (#164 is separate).

## Implementation contract

Keep the existing empty-state draw guard and status-text theme. Replace the current terse copy with:

- `No song folders available — add one in CONFIG > Song Folders`
- `No supported charts found — check CONFIG > Song Folders`

Keep this as a small testable copy formatter/helper inside `SongSelectionStage`; avoid introducing a new service/component.
