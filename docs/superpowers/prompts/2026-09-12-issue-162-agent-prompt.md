# Implementation prompt — GitHub #162

Implement GitHub issue #162 on this existing PR/branch. Keep the scope deliberately small.

## Required production change

In `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`, keep the existing `SongLibraryEmptyState` / `ResolveLibraryEmptyState` behavior and the current `OnDraw` guard. Change only the empty-library recovery presentation so it uses these exact messages:

- `NoActiveRoots` -> `No song folders available — add one in CONFIG > Song Folders`
- `NoSupportedCharts` -> `No supported charts found — check CONFIG > Song Folders`

Extract only a small local helper/formatter if needed to make the copy directly unit-testable. Do not add a new service, component, command, or stage transition.

## Required tests

Extend `DTXMania.Test/Stage/SongSelectionPublicationTests.cs` near `ResolveLibraryEmptyState_ShouldDistinguishNoRootsFromRootsWithoutSupportedCharts` to pin both messages. If the helper can receive `HasSongs`, pin that it returns no recovery message.

Run the focused Song Selection tests and the normal `DTXMania.Test` suite when available.

## Preserve / do not touch

- No direct Song Select -> Config deep-link.
- No changes to Config, `SongFolderPanel`, persistence, song scanning/publication, tabs, filters, RANDOM SELECT, or navigation.
- Do not duplicate root paths into the Config master row; `SongFolderPanel` already owns path/availability diagnostics.
- Keep existing modal visibility guard, draw position, and `ResolveStatusTextColor` unless a focused test proves the new copy clips.
- Do not address #163, #164, or #165 in this PR.

Keep the PR as one ticket / one PR and update the PR description with test evidence when complete.
