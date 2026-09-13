# Issue #162 — Empty Song Select recovery

## Goal

Make the empty Song Select state tell the player how to recover from the actual library state, without adding a second song-folder UI or a new stage-navigation flow.

## Scope

One PR for GitHub issue #162. Keep the existing `SongLibraryEmptyState` / `ResolveLibraryEmptyState` model and change only the empty-library presentation, its narrow layout contract, and focused tests.

## State contract

`ConfigData` and `ConfigManager` always normalize the song-root list to at least the default `DTXFiles` path. `SongManager.ActiveRoots`, however, contains only roots whose policy probe is `Available`. Therefore the two existing states mean:

- `NoActiveRoots`: configured roots exist, but none is currently available because the configured folder is missing, unreadable, or otherwise invalid. Recovery is to fix or repoint it in Config.
- `NoSupportedCharts`: at least one configured root is available, but the published library contains no supported chart. This includes the normal fresh-install case where the default `DTXFiles` directory exists but is empty. Recovery is to put DTX files there or choose another folder.

Do not reinterpret `NoActiveRoots` as "no folders configured."

## Copy contract

Expose one testable internal helper on `SongSelectionStage`:

```csharp
internal static string ResolveLibraryEmptyMessage(SongLibraryEmptyState state)
```

It returns ASCII-only copy:

- `NoActiveRoots`: `Song folder missing or unreadable - fix it in CONFIG > Song Folders`
- `NoSupportedCharts`: `No charts here - add DTX files or change CONFIG > Song Folders`
- `HasSongs`: empty string

Use plain `-`, not an em dash. The bundled SpriteFont path used by Song Select does not contain U+2014 and has `*` as its default fallback glyph, so unsupported punctuation would render incorrectly instead of reaching `ManagedFont`'s sanitizer.

The helper is an `internal static` seam rather than a private method reached through reflection. Do not add localization/string-table infrastructure for these two messages.

## Draw and layout contract

Keep the existing All Songs / no-filter / modal-closed draw guard and the theme-resolved status color.

Add one named layout constant next to `SongSelectionUILayout.SongBars.EmptyMessageOffsetX`:

```csharp
public const int EmptyMessageMaxWidth = BarWidth - EmptyMessageOffsetX;
```

Draw the resolved recovery copy at:

- X: `UnselectedBarX + EmptyMessageOffsetX`
- Y: `SelectedBarY`

Before drawing, pass the message through the existing `TextHelper.TruncateToWidth` overload for `IFont` using `EmptyMessageMaxWidth`. This makes clipping behavior deterministic rather than relying on a graphics-only visual check.

While a library recovery message is active, suppress `SongListDisplay`'s generic centered `No songs found` draw so the player sees one recovery message, not two competing empty-state strings. Keep the suppression scoped to this All Songs library-recovery state; Recent, Bookmarks, and filter-empty behavior stay unchanged.

## Tests

Keep the existing `ResolveLibraryEmptyState` coverage that distinguishes empty `ActiveRoots` from an available root with no supported chart.

In `SongSelectionEmptyStateTests`, directly test `ResolveLibraryEmptyMessage` and the layout contract:

- `NoActiveRoots` maps to the unavailable-folder recovery copy.
- `NoSupportedCharts` maps to the add-DTX/change-folder recovery copy.
- `HasSongs` returns empty.
- Recovery copy contains only printable ASCII U+0020–U+007E.
- `EmptyMessageMaxWidth == BarWidth - EmptyMessageOffsetX` and is positive.
- Reuse the repo's existing `IFont` mock pattern to run each recovery message through `TextHelper.TruncateToWidth` and assert the measured output is within `EmptyMessageMaxWidth` on the Mac test project.

No reflection helper and no graphics-device test are needed.

Run on macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionEmptyStateTests"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

## Existing behavior to preserve

- Do not change song discovery, publication, filtering, tabs, navigation stacks, or RANDOM SELECT.
- Do not add a Song Select -> Config deep-link in this PR.
- Do not duplicate configured paths into the `Song Folders: N folders` Config row. `SongFolderPanel` remains the owner of path display and inaccessible-root diagnostics.
- Keep the recovery message hidden while the search/filter modal is open.
- Keep the existing theme-resolved status text color.
- Do not add a second plan/spec/prompt document for this copy-only change.

## Acceptance

- When no configured root is available, Song Select says the folder is missing/unreadable and directs the player to `CONFIG > Song Folders` to fix it.
- When an available song root contains no supported charts, including a fresh empty `DTXFiles` directory, Song Select tells the player to add DTX files or change `CONFIG > Song Folders`.
- Recovery copy renders with bundled-font-safe ASCII and is truncated to the named song-bar width budget.
- The generic `No songs found` line is not drawn underneath the recovery copy.
- No new navigation, persistence, localization, or song-folder ownership architecture is introduced.
- Focused regression tests pin the state-to-copy mapping and render-safety contracts.
