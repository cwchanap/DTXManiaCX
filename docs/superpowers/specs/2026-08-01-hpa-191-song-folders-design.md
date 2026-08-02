# HPA-191 Configurable Multiple Song Folders Design

**Issue:** [HPA-191](https://linear.app/cwchanap/issue/HPA-191/allow-change-song-folders)  
**Date:** 2026-08-01  
**Status:** Draft for review

## Context

DTXManiaCX currently persists one song-library path in `ConfigData.DTXPath` and
writes it as `DTXPath=` in `Config.ini`. `ConfigStage` displays that value as a
read-only **DTX Folder** item. During startup, `StartupStage` wraps the value in
a one-element string array before passing it into the song-loading pipeline.

The lower-level song system is already substantially multi-root capable:

- `SongManager` accepts arrays of search roots.
- Enumeration normalizes and deduplicates roots.
- Root traversal order is preserved in the published hierarchy.
- HPA-192 bulk import and cleanup are scoped to active roots.
- Database rows outside active roots are retained, protecting scores,
  bookmarks, performance history, and other user-owned state when a library is
  temporarily removed or unavailable.

HPA-191 therefore does not require a database schema change or another song
import architecture. The work is to make the ordered root list a first-class
configuration value, expose safe cross-platform editing in Config, and reload
the active library without routing the game through boot-only startup state.

## Goals

- Allow the player to add, remove, and reorder song-library roots from the
  Config stage.
- Support one or more persisted roots while retaining compatibility with
  existing `DTXPath` configurations.
- Apply valid changes immediately and perform one live library reload.
- Preserve the currently published hierarchy until a replacement reload has
  completed successfully.
- Preserve database records and user-owned data for roots removed from the
  active configuration or temporarily unavailable.
- Support Windows and macOS folder selection without referencing platform UI
  frameworks from shared game code.
- Reject configurations that would scan the same subtree more than once.
- Keep startup behavior deterministic when some or all configured roots are
  unavailable.

## Non-goals

- DTXCreator changes.
- Changes to chart parsing, `set.def`, or `box.def` semantics.
- Song database schema changes.
- Filesystem watchers or automatic reload when a removable drive is mounted.
- Background rescans after every individual add, remove, or reorder action.
- Editing or creating song files from Config.
- Synthetic top-level boxes for each configured root.
- Merging same-named folders or songs across different roots.
- Deleting retained database records for removed roots.
- General-purpose file-picker infrastructure beyond directory selection.
- Linux-specific picker integration; the configuration and reload contracts
  remain platform-neutral, but HPA-191 targets the existing Windows and macOS
  game projects.

## Chosen Approach

Add an ordered `SongRoots` collection as the sole runtime source of truth.
Retain `DTXPath` only as a serialized compatibility mirror of the first root.
A dedicated Config overlay edits a working copy and commits it only when the
player selects **Apply**. A platform-specific folder-picker service supplies new
paths. After the configuration is atomically persisted, a focused song-library
reload service invokes the existing HPA-192 enumeration/import path once.

The old hierarchy remains published until the complete replacement batch has
committed and been published. A failed or cancelled reload therefore cannot
leave Song Select displaying a partial library.

Three root states are distinguished throughout the design:

- **Configured roots:** the ordered canonical paths persisted in `Config.ini`.
- **Available roots:** configured roots that currently exist and pass a minimal
  access probe.
- **Active roots:** roots represented by the currently published `SongManager`
  hierarchy. Active roots change only after a successful reload, or during a
  clean startup that intentionally publishes an empty library.

This distinction allows removable-drive paths to remain configured without
pretending they are currently usable.

## Alternatives Considered

### Save and require restart

This is the smallest implementation, but it leaves the active library stale
until the player restarts and gives weak feedback that the change succeeded.
It is rejected because the existing bulk importer already provides an atomic
publication boundary suitable for a live reload.

### Transition Config back through StartupStage

This reuses the startup progress screen, but `StartupStage` owns boot-specific
phase state, activation generations, timing summaries, and exactly-once startup
telemetry. Re-entering it for a user-initiated configuration operation would
couple unrelated lifecycles and make telemetry ambiguous. It is rejected.

### Rescan after every list edit

This would repeatedly traverse and persist the same library while the player is
still composing the desired configuration. It is rejected in favor of a draft
list and one Apply operation.

### Store one delimited `SongRoots=` value

A delimiter would conflict with legal path characters and complicate escaping
and hand editing. Indexed keys are easier to parse, order, diagnose, and migrate.

## Configuration Contract

### Runtime model

`ConfigData` gains an ordered mutable collection:

```csharp
public List<string> SongRoots { get; } = new();
```

The collection is initialized with `AppPaths.GetDefaultSongsPath()`.

`DTXPath` remains available for source and downgrade compatibility, but is a
mirror only:

- After load, reset, or a successful root update, it equals the first canonical
  configured root.
- Production startup, Config UI, reload, and active-root filtering must consume
  `SongRoots`, never `DTXPath`.
- Direct legacy mutation of `DTXPath` is not a supported way to change the
  runtime library.

At least one configured root is always required. Availability is not required;
a single configured path may point to a currently disconnected removable drive.

### Persisted format

`Config.ini` stores roots in explicit order:

```ini
[System]
DTXPath=C:\DTX\Main
SongRoot.0=C:\DTX\Main
SongRoot.1=D:\Community Charts
```

Rules:

1. `SongRoot.<index>` uses a non-negative decimal integer index.
2. Roots are loaded in ascending numeric order; gaps are allowed.
3. Malformed suffixes, blank values, and negative indexes are ignored with a
   warning.
4. Duplicate indexes use the last parsed value, matching normal INI overwrite
   expectations, and emit a warning.
5. Values may contain `=` because `ConfigManager` already splits lines on the
   first `=` only.
6. Save rewrites indexes densely from zero.
7. `DTXPath` is always written as the first root for downgrade compatibility.
8. `SongRoot.*` entries are the authority whenever at least one structurally
   valid entry exists.
9. When no valid indexed entries exist, the loader migrates the legacy
   `DTXPath` value into a one-element root list.
10. When neither representation yields a usable path, the managed default root
    is restored.

A successful legacy migration is persisted immediately through the existing
atomic temp-file replacement so the next launch no longer depends on fallback
logic.

### Legacy path migration

The existing migration from legacy `Songs` defaults to the current managed
`DTXFiles` location is retained. It applies to the fallback `DTXPath` value
before that value becomes `SongRoots[0]`.

Indexed custom roots are not remapped merely because their final segment is
named `Songs`; only the known legacy default representations are migrated.

### Directory creation

The game may create the managed default `AppPaths.GetDefaultSongsPath()` when it
is selected or restored as the fallback root.

The game must not create arbitrary custom roots. A missing custom path may be a
removable drive, network mount, renamed directory, or user error. Creating it
would hide the problem and could produce unwanted directories on the wrong
volume.

## Path Identity and Validation

All roots are resolved and compared through one shared policy used by config
load, Config UI, and the setter.

### Canonicalization

For each non-blank input:

1. Expand supported home-relative forms through `AppPaths`.
2. Resolve relative legacy paths against the same app-data base used today.
3. Convert to an absolute full path.
4. Normalize separators and trailing separators through `SongPathIdentity`.
5. Compare with `SongPathIdentity.CanonicalComparer`, preserving platform path
   case behavior.
6. Persist the canonical absolute value.

Root order is semantically meaningful. It controls traversal order and the
ordering of root-level nodes in Song Select.

### Blocking structural errors

The following prevent Apply and leave the current configuration unchanged:

- No configured roots.
- Blank or syntactically invalid paths.
- Canonically duplicate roots.
- Overlapping roots where either path is an ancestor of the other.

Overlapping roots are rejected because scanning both a parent and its child
would discover the same charts twice. The policy performs a symmetric
ancestor check after canonicalization; authored order does not make an overlap
valid.

Hand-edited invalid configuration must not prevent startup. During load, roots
are processed in numeric order and the first accepted root wins. Later duplicate
or overlapping entries are dropped with warnings. If all entries are rejected,
the managed default is restored.

### Availability warnings

Missing and inaccessible paths are warnings, not structural errors. They remain
configured so removable or network libraries can return later.

A minimal availability probe must:

- Check that the directory exists.
- Attempt to begin a shallow directory enumeration to detect obvious access
  failures without recursively scanning charts.
- Catch path, I/O, and authorization errors and return a diagnostic instead of
  throwing.

Only roots passing this probe are supplied to a live reload or normal startup
load. A path may still fail later because availability can change during a
scan; that failure follows the reload error contract below.

## Config User Experience

### System menu entry

Replace the read-only **DTX Folder** item with a navigation item named
**Song Folders**.

Its value summary is:

- `1 folder` for one configured root.
- `<n> folders` for multiple roots.

The description states that the folders are scanned in the displayed order and
that Apply reloads the song library once.

### Overlay abstraction

`ConfigStage` currently models its active overlay as `IKeyAssignPanel`, although
the lifecycle and drawing contract is generally useful. Introduce
`IConfigOverlayPanel` containing the common members:

- `IsActive`
- `Closed`
- `Saved`
- `Activate()` / `Deactivate()`
- `Update(...)`
- `Draw(...)`

`IKeyAssignPanel` inherits `IConfigOverlayPanel`, preserving existing key-panel
contracts. `ConfigStage._activePanel` becomes `IConfigOverlayPanel`. This is a
targeted boundary correction required to host a non-key-assignment panel; it is
not a broader Config UI refactor.

### SongFolderPanel

`SongFolderPanel` receives:

- An immutable snapshot of configured roots.
- `IFolderPickerService`.
- The shared root validation policy.

It owns a private draft list. Opening, reordering, adding, removing, or
cancelling does not mutate `ConfigData`.

The panel displays the full selected path and a scrollable ordered list. Long
paths may be visually ellipsized, but validation and persistence always use the
complete value.

Supported actions:

- **Add Folder** — invoke the platform picker and append a unique selection.
- **Remove** — remove the selected root when at least one draft root remains.
- **Move Up** / **Move Down** — change scan and display order.
- **Apply** — validate and commit the complete draft.
- **Cancel** — discard the draft and close.

Input behavior follows existing Config conventions:

- Up/Down move selection.
- Left/Right change the selected action where applicable.
- Activate executes the highlighted action.
- Back behaves as Cancel.

Structural errors are shown in the panel and keep it open. Availability warnings
are shown beside affected roots but do not disable Apply.

A folder-picker cancellation is a no-op. A picker failure displays an error and
leaves the draft unchanged.

## Platform Folder Picker

Shared code depends only on:

```csharp
public interface IFolderPickerService
{
    FolderPickerResult PickFolder(string? initialDirectory);
}
```

The result distinguishes selected, cancelled, unavailable, and failed outcomes
and carries an error message only for failure.

Platform composition selects the implementation:

- **Windows:** use `System.Windows.Forms.FolderBrowserDialog`, already supported
  by the Windows project target. The dialog starts from the selected root or the
  managed default when possible.
- **macOS:** invoke the native folder chooser through a small `osascript`
  adapter using `choose folder`, returning its POSIX path. Process arguments are
  passed without shell interpolation, cancellation is mapped to Cancelled, and
  non-cancellation failures include stderr in structured logging.

The shared `DTXMania.Game` code must not reference WinForms, AppleScript process
construction, or other platform UI types. Picker implementations and service
registration live in platform-specific source files or projects.

Headless tests inject a deterministic fake picker.

## Commit and Persistence Flow

Applying roots is an explicit user commit, so it requires stronger semantics
than an ordinary deferred toggle edit.

`IConfigManager` gains a typed root update operation returning a result:

```csharp
SongRootUpdateResult SetSongRoots(
    string configFilePath,
    IReadOnlyList<string> roots);
```

The operation:

1. Validates and canonicalizes the complete list.
2. Captures the prior `SongRoots` and `DTXPath` values.
3. Updates both in memory.
4. Writes the complete config immediately through the existing atomic
   temp-file replacement.
5. Clears any pending deferred-save marker on success because all current
   settings were written.
6. Restores the prior in-memory values if persistence fails.
7. Raises `SongRootsChanged` only after persistence succeeds.
8. Is a no-op success when the canonical ordered list is unchanged.

Event subscribers are isolated using the same per-subscriber exception handling
as existing config events. A subscriber failure does not undo an accepted and
persisted configuration.

If persistence fails, the panel remains open, the old configuration remains the
truth, and no reload begins.

## Live Library Reload

### Boundary

Introduce an `ISongLibraryReloadService` abstraction so `ConfigStage` does not
own raw `SongManager` orchestration and tests do not need the singleton.

The production implementation:

- Resolves currently available roots from the persisted configured list.
- Adapts `EnumerationProgress` to a Config status string.
- Invokes `SongManager.EnumerateAndImportSongsAsync` once.
- Returns the successful active-root set, aggregate counts, warnings, or a
  terminal failure/cancellation result.
- Allows only one in-flight reload.

It does not create another import path. The existing HPA-192 batch builder,
bulk transaction, hierarchy finalization, and publication remain authoritative.

### Apply sequence

1. `SongFolderPanel` validates its draft.
2. `ConfigManager.SetSongRoots` atomically persists the canonical list.
3. The panel closes and Config displays a reload status.
4. The reload service probes all configured roots.
5. When at least one root is available, one enumeration/import starts with only
   those roots.
6. The old hierarchy remains active while discovery and persistence run.
7. On successful commit, `SongManager.PublishEnumeration` replaces the hierarchy
   and current active-root snapshot atomically.
8. Config reports loaded root, chart, logical-song, skipped-root, and error
   counts.

Unavailable configured roots are omitted from that scan and listed as warnings.
Their database records are retained because they are outside the active import
roots.

When no configured root is currently available, Config does not invoke the
importer and does not clear the current hierarchy. The roots remain saved and
Config reports that the current library was retained until a successful future
reload or restart.

### Concurrency and lifecycle

- A second Apply/reload request is rejected while one reload is in flight.
- Song-folder editing may be reopened only after the prior reload terminates.
- Leaving Config cancels the reload and observes its task.
- The cancellation source is disposed only after the operation terminates,
  preserving HPA-192's enumeration-slot and cancellation-winddown guarantees.
- Config deactivation must not block indefinitely waiting for filesystem work.
- The reload task may complete during a stage transition, but it must not write
  into disposed Config graphics or panel state. Status publication is guarded
  by an activation generation or equivalent lifetime token.

### Failure and cancellation

If discovery, import, or publication fails:

- The newly persisted configured roots remain saved.
- The previous active hierarchy and active-root snapshot remain published.
- No partial hierarchy is exposed.
- The player sees a concise failure message explaining that the saved roots will
  be retried on the next startup or Apply.
- Detailed exceptions and failed-root diagnostics are logged.

If cancellation occurs before publication, the same old-hierarchy guarantee
applies. If the HPA-192 operation has already committed and published before the
cancellation is observed, that successful publication remains authoritative;
the status must not claim that the library was rolled back.

## Startup Integration

`StartupStage` reads a snapshot of `Config.SongRoots`, never `DTXPath`.

Before selecting the cache or enumeration path, startup performs the shared
availability probe:

- Available roots are passed to `NeedsEnumerationAsync`, cache hierarchy
  construction, and enumeration.
- Unavailable roots are logged once with their diagnostics.
- Root order remains the configured order.

When no configured root is available during a clean startup, startup completes
successfully with an empty active hierarchy and an empty active-root snapshot.
It does not delete or stale-clean any database records. The title screen remains
reachable, and the player can enter Config to repair the paths.

A later successful startup or Apply restores songs from the retained database or
re-enumerates the returned roots according to the normal load-path decision.

## SongManager and Database Interaction

No database migration is required.

The existing HPA-192 contracts remain in force:

- Enumeration and persistence receive an ordered root array.
- Exact duplicate roots are defensively deduplicated again in `SongManager`.
- Cleanup affects only roots successfully supplied to the import.
- Charts and songs outside those roots are retained.
- Bookmarks, all score-speed variants, play counts, recent-play timestamps,
  ranks, full-combo state, and performance history remain user-owned data.
- The replacement hierarchy is published only after the transaction commits.

HPA-191 prevents overlapping configured roots before they reach `SongManager`.
The importer is not responsible for inferring which overlapping root the player
intended.

Multiple roots remain flattened into the existing root-level hierarchy in
configured order. Two roots may legitimately produce boxes or songs with equal
display titles; identity remains path/database based and the nodes are not
merged. Adding synthetic per-root containers would change navigation semantics
and is outside this issue.

## Logging and User Feedback

Structured logs include:

- Config migration from `DTXPath` to indexed roots.
- Dropped malformed, duplicate, or overlapping hand-edited entries.
- Selected and cancelled folder-picker outcomes at debug level.
- Picker failures at warning level.
- Canonical old and new root lists after a successful commit.
- Availability warnings by configured root.
- Reload start, success, cancellation, and failure with aggregate counts.

Normal logs must not emit per-file success messages beyond the progress behavior
already owned by HPA-192.

Config displays one concise status line for the current operation, for example:

- `Reloading songs: 48 / 100 charts`
- `Loaded 2 folders, 100 charts; 1 folder unavailable`
- `Folders saved; no configured folder is currently available`
- `Reload failed; previous song list retained`

## Testing Strategy

### Configuration unit tests

- Default config contains exactly the managed default root and mirrors it to
  `DTXPath`.
- Indexed roots round-trip in order and are rewritten densely.
- Legacy `DTXPath` migrates to one indexed root and persists immediately.
- Legacy default `Songs` migration still resolves to `DTXFiles`.
- Indexed entries take precedence over `DTXPath`.
- Gaps, malformed indexes, duplicate indexes, and blank values are handled as
  specified.
- Canonical duplicates are rejected by the setter and dropped safely on load.
- Parent/child overlaps are rejected symmetrically.
- Root comparison follows platform path semantics.
- Missing custom roots are not created.
- The managed default root is created when restored as fallback.
- Failed config persistence restores the prior in-memory roots and emits no
  change event.
- A successful update raises `SongRootsChanged` once after persistence.

### Config and panel tests

- The System category exposes **Song Folders** instead of a read-only path.
- The count summary uses singular and plural forms.
- Panel activation starts from an isolated draft.
- Add, remove, reorder, Apply, Cancel, and Back behavior are deterministic.
- Picker cancellation and failure do not mutate the draft.
- Structural errors keep the panel open.
- Missing paths appear as warnings and may be applied.
- The last configured root cannot be removed without adding another.
- Apply does not start a reload when persistence fails.
- Active overlay polymorphism continues to support existing key panels.

### Reload-service tests

- One successful Apply triggers exactly one importer call with roots in order.
- Unavailable roots are skipped without preventing available roots from loading.
- No available roots skip import and retain the existing hierarchy.
- Concurrent reload is rejected.
- Success publishes the new hierarchy and active roots.
- Failure and pre-publication cancellation preserve the previous hierarchy.
- Leaving Config cancels and safely observes the operation.
- Post-publication cancellation is reported as success, not rollback.

### Startup and integration tests

- Startup consumes `SongRoots`, not the compatibility `DTXPath` value.
- Multiple available roots appear in configured order.
- Charts under distinct roots are imported once each.
- Removed-root songs disappear from the active hierarchy after successful
  reload but remain in SQLite with scores and bookmarks intact.
- Re-adding the root restores retained user-owned state.
- A missing removable-drive root does not block another root.
- No available roots produce an empty active library without database cleanup.
- Windows and macOS projects compile with only their own picker implementation.
- Existing HPA-192 enumeration, cancellation, score-retention, and performance
  tests remain green.

## Implementation Slices

Each slice is intended to fit within three engineer days and be executable by a
junior code agent with repository-wide access.

### Slice 1: Canonical multi-root configuration

- Add `SongRoots`, the indexed INI format, compatibility mirror, migration, and
  immediate persistence behavior.
- Add shared canonicalization, overlap validation, and availability probing.
- Update `IConfigManager`, `ConfigManager`, `ConfigData`, and startup root
  consumption.
- Add configuration and startup unit tests.
- Do not add Config UI or live reload in this slice.

### Slice 2: Cross-platform SongFolderPanel

- Add `IConfigOverlayPanel` and adapt the existing key-panel interface.
- Add `IFolderPickerService` with Windows, macOS, and fake implementations.
- Replace **DTX Folder** with **Song Folders** and implement the draft editing
  panel.
- Commit through `SetSongRoots`, but show a temporary `restart required` status
  until Slice 3 supplies live reload.
- Add panel and platform-boundary tests.

### Slice 3: Live reload and retained-data integration

- Add `ISongLibraryReloadService` and Config lifecycle coordination.
- Add progress, unavailable-root summaries, cancellation, and failure feedback.
- Verify successful publication, old-hierarchy retention, and removed-root data
  retention.
- Add multi-root integration tests and Windows/macOS build validation.

## Acceptance Criteria

- Existing one-path `DTXPath` configurations migrate without losing the library.
- A fresh install has one managed default song root.
- Config allows adding, removing, and reordering roots on Windows and macOS.
- The ordered root list survives restart.
- Duplicate and parent/child-overlapping roots cannot be applied.
- Missing custom roots remain configured and are never created automatically.
- Applying a changed list performs at most one live scan.
- Every available root is scanned in configured order.
- One unavailable root does not prevent available roots from loading.
- A successful reload replaces the active hierarchy atomically.
- A failed or cancelled pre-publication reload never exposes a partial hierarchy.
- When no root is available during Apply, the previous active hierarchy remains
  visible and the saved configuration is retried later.
- When no root is available during startup, the game reaches Title with an empty
  active library and leaves the database untouched.
- Removing a root removes its songs from active views after a successful reload
  but preserves their bookmarks, scores, variants, and performance history.
- Re-adding a root restores retained user data.
- `DTXPath` remains a compatibility mirror only; runtime code consumes
  `SongRoots`.
- No DTXCreator, parser, song schema, or synthetic-root-navigation changes are
  included.
