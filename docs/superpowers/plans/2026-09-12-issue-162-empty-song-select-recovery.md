# Issue #162 — Empty Song Select recovery

## Goal

Make the empty Song Select state tell the player exactly where to recover their song-folder configuration, without adding a second song-folder UI or a new stage-navigation flow.

## Scope

One PR for GitHub issue #162.

### Change

Update the existing `SongSelectionStage.OnDraw` empty-library presentation. Keep the current `SongLibraryEmptyState` / `ResolveLibraryEmptyState` distinction and change only the user-facing copy:

- `NoActiveRoots`: `No song folders available — add one in CONFIG > Song Folders`
- `NoSupportedCharts`: `No supported charts found — check CONFIG > Song Folders`

Prefer reusing one small pure/helper method for the copy so the contract can be tested without a graphics device. Do not introduce a new UI abstraction just for two strings.

### Tests

Extend the existing focused coverage in `DTXMania.Test/Stage/SongSelectionPublicationTests.cs` around `ResolveLibraryEmptyState_ShouldDistinguishNoRootsFromRootsWithoutSupportedCharts` to pin both recovery messages.

The test should prove:

1. no active roots resolves to the add/reconnect guidance;
2. active roots with no supported charts resolves to the check-folder guidance;
3. `HasSongs` does not produce an empty-state recovery message, if the helper exposes that state.

Run the targeted Song Selection tests, then the normal `DTXMania.Test` suite if practical.

## Existing behavior to preserve

- Do not change song discovery, publication, filtering, tabs, navigation stacks, or RANDOM SELECT.
- Do not add a Song Select -> Config deep-link in this PR.
- Do not duplicate configured paths into the `Song Folders: N folders` Config row. `SongFolderPanel` remains the owner of path display and inaccessible-root diagnostics.
- Keep the empty-state message hidden while the search/filter modal is open, matching the current draw guard.
- Keep the existing theme-resolved status text color and current empty-message position unless the new copy demonstrably clips.

## Acceptance

- Fresh/no-root Song Select tells the player to use `CONFIG > Song Folders` to add a folder.
- A configured/active root with zero supported charts tells the player to check `CONFIG > Song Folders`.
- No new navigation or persistence architecture is introduced.
- Focused regression tests cover the copy contract.
