# HPA-191 Configurable Multiple Song Folders Design

**Issue:** [HPA-191](https://linear.app/cwchanap/issue/HPA-191/allow-change-song-folders)  
**Date:** 2026-08-01  
**Status:** Revised draft for review

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
configuration value, expose safe cross-platform editing in Config, audit every
legacy `DTXPath` runtime consumer, and reload the active library without routing
the game through boot-only startup state.

## Goals

- Allow the player to add, remove, and reorder song-library roots from Config.
- Support one or more persisted roots while retaining compatibility with
  existing `DTXPath` configurations.
- Apply valid changes immediately and perform at most one live library reload.
- Preserve the currently published hierarchy until a replacement reload has
  completed successfully.
- Preserve database records and user-owned data for roots removed from the
  active configuration or temporarily unavailable.
- Support Windows and macOS folder selection without referencing platform UI
  frameworks from shared game code.
- Reject configurations that would scan the same textual subtree more than
  once under the selected root-comparison policy.
- Keep startup behavior deterministic when some or all configured roots are
  unavailable.
- Serialize Config-initiated song-library mutation operations so NX score import
  and song-folder reload cannot write or rebuild the library concurrently.

## Non-goals

- DTXCreator changes.
- Changes to chart parsing, `set.def`, or `box.def` semantics.
- Song database schema changes.
- Filesystem watchers or automatic reload when a removable drive is mounted.
- Background rescans after every individual add, remove, or reorder action.
- A database-only optimization for reorder-only changes; any changed ordered
  root list performs one full enumeration/import in HPA-191.
- Editing or creating song files from Config.
- Synthetic top-level boxes for each configured root.
- Merging same-named folders or songs across different roots.
- Deleting retained database records for removed roots.
- Resolving filesystem aliases to a physical target. Symlinks, junctions, bind
  mounts, aliases, and case-sensitive macOS volumes can expose the same target
  through distinct paths; realpath/inode-based deduplication is deferred.
- General-purpose file-picker infrastructure beyond directory selection.
- Replacing the macOS AppleScript adapter with a native `NSOpenPanel` helper.
- Linux-specific picker integration; the configuration and reload contracts
  remain platform-neutral, but HPA-191 targets the existing Windows and macOS
  game projects.

## Chosen Approach

Add an ordered `SongRoots` collection as the sole configured-root source of
truth. Retain `DTXPath` only as a serialized compatibility mirror of the first
root. A dedicated Config overlay edits a working copy and commits it only when
the player selects **Apply**. An asynchronous platform-specific folder-picker
service supplies new paths. After configuration is atomically persisted, a
focused song-library reload service invokes the existing HPA-192
enumeration/import path once.

The old hierarchy remains published until the complete replacement batch has
committed and been published. A failed or cancelled pre-commit reload therefore
cannot leave Song Select displaying a partial library.

Three root states are distinguished throughout the design:

- **Configured roots:** the ordered canonical paths persisted in `Config.ini`.
- **Available roots:** configured roots that currently exist and pass a minimal
  access probe.
- **Active roots:** roots represented by the currently published `SongManager`
  hierarchy. Active roots change only after a successful publication, or during
  a clean startup that intentionally publishes an empty active library.

Configured roots and active roots may temporarily differ. For example, a player
can save new roots and then encounter a failed reload; Song Select must continue
to use the old active-root snapshot until a later reload or startup succeeds.

## Alternatives Considered

### Save and require restart

This is the smallest implementation, but it leaves the active library stale
until restart and gives weak feedback that the change succeeded. It is rejected
for the final feature because the existing bulk importer provides an atomic
publication boundary suitable for a live reload. Slice 2 temporarily uses this
behavior until Slice 3 lands.

### Transition Config back through StartupStage

This reuses the startup progress screen, but `StartupStage` owns boot-specific
phase state, activation generations, timing summaries, and exactly-once startup
telemetry. Re-entering it for a user-initiated operation would couple unrelated
lifecycles and make telemetry ambiguous. It is rejected.

### Rebuild from SQLite for reorder-only changes

The database hierarchy builder could potentially republish roots in a different
order without reparsing charts. HPA-191 deliberately does not add that branch.
Apply is an explicit refresh operation, and one full scan also observes any
filesystem changes made while the panel was open. A future measured
optimization may special-case reorder-only changes.

### Store one delimited `SongRoots=` value

A delimiter would conflict with legal path characters and complicate escaping
and hand editing. Indexed keys are easier to parse, order, diagnose, and migrate.

## Configuration Contract

### Runtime model

`ConfigData` gains an ordered mutable collection:

```csharp
public List<string> SongRoots { get; } = new();
```

The property remains get-only. Load, reset, and successful update operations
clear and refill the existing list rather than replacing the list reference.
Consumers must never retain or mutate that live collection. Every UI, event,
reload, and startup boundary receives an immutable copied snapshot.

`DTXPath` remains available for source and downgrade compatibility, but is a
mirror only:

- After load, reset, or a successful root update, it equals the first canonical
  configured root.
- Production startup, Config UI, reload, active-root filtering, previews, and
  other runtime behavior consume `SongRoots` or the active-root snapshot, never
  `DTXPath`.
- Direct legacy mutation of `DTXPath` is not a supported way to change the
  runtime library.

At least one configured root is always required. Availability is not required;
a single configured path may point to a disconnected removable drive.

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
4. Duplicate indexes use the last parsed value and emit a warning.
5. Values may contain `=` because `ConfigManager` splits on the first `=` only.
6. Save rewrites indexes densely from zero.
7. `DTXPath` is always written as the first root for downgrade compatibility.
8. `SongRoot.*` entries are authoritative whenever at least one structurally
   valid indexed entry exists.
9. When no valid indexed entry exists, load migrates legacy `DTXPath` into a
   one-element list.
10. When neither representation yields a valid path, load restores the managed
    default root.

A successful legacy migration is persisted immediately through the existing
atomic temp-file replacement.

### Legacy path migration

The existing migration from legacy `Songs` defaults to the current managed
`DTXFiles` location is retained. It applies to the fallback `DTXPath` value
before that value becomes `SongRoots[0]`.

Indexed custom roots are not remapped merely because their final segment is
named `Songs`; only known legacy default representations are migrated.

Previous releases may already have created an empty custom directory while a
removable or network path was unavailable. HPA-191 stops creating such paths,
but it does not delete or move directories created by earlier versions. The
migration is logged; filesystem cleanup is left to the user.

### Directory creation

The game may create the managed default `AppPaths.GetDefaultSongsPath()` when it
is selected or restored as the fallback root.

The game must not create arbitrary custom roots. A missing custom path may be a
removable drive, network mount, renamed directory, or user error. Creating it
would hide the problem and could produce an unwanted directory on the wrong
volume.

## Root Path Identity and Validation

Root comparison is intentionally distinct from HPA-192 chart identity.
`SongPathIdentity.CanonicalComparer` remains ordinal and continues to protect
exact persisted chart identities. HPA-191 does not redefine it as
platform-aware.

### Shared root policy

Add an internal `SongRootPolicy` in the shared `DTXMania.Game` assembly. It is
the only path used by Config load, Config UI, `SetSongRoots`, startup,
`SongManager` defensive root deduplication, and availability probing.

`SongRootPolicy.RootComparer` uses
`SongPathIdentity.LegacyAliasComparer`:

- Windows and macOS: ordinal ignore-case.
- Linux and other platforms: ordinal case-sensitive.

This is a deliberate user-facing root policy. On a case-sensitive macOS volume,
paths that differ only by case are still treated as duplicate configured roots
for HPA-191. Resolving actual filesystem case or targets is outside scope.

`SongPathIdentity` remains internal. No public API exposure is required because
`ConfigManager`, stages, `SongManager`, and `SongRootPolicy` live in the same
assembly. Tests use the repository's existing internal-test access pattern.

### Canonicalization

For each non-blank input:

1. Expand supported home-relative forms through `AppPaths`.
2. Resolve relative legacy paths against the same app-data base used today.
3. Convert to an absolute full path.
4. Normalize separators and trailing separators through
   `SongPathIdentity.Normalize`.
5. Compare roots with `SongRootPolicy.RootComparer`.
6. Persist the normalized absolute value.

Root order is semantically meaningful. It controls traversal order and the
ordering of root-level nodes in Song Select.

`SongManager.SetCurrentSearchPaths`, enumeration batch root creation, and other
defensive deduplication must use `SongRootPolicy.RootComparer`, not
`SongPathIdentity.CanonicalComparer`, so direct callers cannot reintroduce root
case aliases that Config rejects.

### Blocking structural errors

The following prevent Apply and leave the current configuration unchanged:

- No configured roots.
- Blank or syntactically invalid paths.
- Duplicate roots under `SongRootPolicy.RootComparer`.
- Overlapping roots where either normalized path is an ancestor of the other.

Overlap checks use the same root comparer for path-segment equality. Authored
order does not make an overlap valid.

Hand-edited invalid configuration must not prevent startup. During load, roots
are processed in numeric order and the first accepted root wins. Later duplicate
or overlapping entries are dropped with warnings. If all entries are rejected,
the managed default is restored.

Symlink, junction, mount, and alias targets are not resolved. Two textually
different non-overlapping paths that reach the same physical directory may both
scan in v1.

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

Its value summary is `1 folder` or `<n> folders`. The description states that
folders are scanned in displayed order and Apply reloads the library once.

### Overlay abstraction

Introduce `IConfigOverlayPanel` containing the common members currently carried
by `IKeyAssignPanel`:

- `IsActive`
- `Closed`
- `Saved`
- `Activate()` / `Deactivate()`
- `Update(...)`
- `Draw(...)`

`IKeyAssignPanel` inherits `IConfigOverlayPanel`, preserving its existing
`Saved`-before-`Closed` contract. `ConfigStage._activePanel` becomes
`IConfigOverlayPanel`. This is a targeted boundary correction, not a broader UI
refactor.

### SongFolderPanel ownership

`SongFolderPanel` receives:

- An immutable configured-root snapshot.
- `IFolderPickerService`.
- The shared root validation policy.
- A commit delegate owned by `ConfigStage`:

```csharp
Func<IReadOnlyList<string>, SongRootUpdateResult> commitRoots
```

The panel owns a private draft list. Opening, reordering, adding, removing, or
cancelling does not mutate `ConfigData`.

On **Apply**:

1. The panel validates its draft.
2. The panel passes an immutable draft snapshot to the Config-owned delegate.
3. The delegate is the only caller of `IConfigManager.SetSongRoots`.
4. Validation or persistence failure is returned to the panel; the panel stays
   open and shows the error.
5. On `Updated` or `Unchanged`, the panel stores the immutable committed
   snapshot, raises `Saved`, then raises `Closed`.
6. `ConfigStage` reads the committed snapshot during `Saved`. It starts a reload
   only for `Updated`; `Unchanged` closes without a scan.

The panel never holds `IConfigManager` and never persists roots independently.
This prevents double commits and keeps event ordering consistent with existing
Config panels.

Supported actions:

- **Add Folder** — invoke the platform picker and append a unique selection.
- **Remove** — remove the selected root when at least one draft root remains.
- **Move Up** / **Move Down** — change scan and display order.
- **Apply** — validate and commit the complete draft.
- **Cancel** — discard the draft and close.

Structural errors keep the panel open. Availability warnings appear beside
roots but do not disable Apply. Picker cancellation is a no-op; picker failure
leaves the draft unchanged and displays an error.

## Platform Folder Picker

Shared code depends on an asynchronous interface:

```csharp
public interface IFolderPickerService
{
    Task<FolderPickerResult> PickFolderAsync(
        string? initialDirectory,
        CancellationToken cancellationToken);
}
```

The result distinguishes selected, cancelled, unavailable, and failed outcomes
and carries an error message only for failure.

While a picker is active, the panel displays a selecting state and ignores
repeat Add actions. Picker completion is marshalled back to panel state only if
the panel activation generation is still current.

Platform composition selects the implementation:

- **Windows:** `FolderBrowserDialog` is acceptable because the Windows project
  already enables WinForms. The implementation owns the required STA thread or
  platform dispatcher; shared game/update code does not assume its thread is
  STA. The dialog starts from the selected root or managed default when valid.
- **macOS:** invoke `osascript` with `choose folder`, a clear prompt, and a
  default location when `initialDirectory` exists. Construct arguments through
  `ProcessStartInfo.ArgumentList`, never shell interpolation. Await process exit
  asynchronously, map user cancellation to `Cancelled`, terminate the process
  on cancellation when safe, and map privacy/automation denial or other
  non-cancellation failures to `Failed` with stderr in structured logs.

The shared project must not reference WinForms, AppleScript process
construction, or native picker types. Headless tests inject a deterministic fake
picker. A future native `NSOpenPanel` helper may replace AppleScript without
changing the shared interface.

## Commit and Persistence Flow

Applying roots is an explicit user commit and requires immediate persistence.

`IConfigManager` gains:

```csharp
SongRootUpdateResult SetSongRoots(
    string configFilePath,
    IReadOnlyList<string> roots);
```

The result contract is:

```csharp
public enum SongRootUpdateStatus
{
    Updated,
    Unchanged,
    ValidationFailed,
    PersistenceFailed
}

public sealed record SongRootUpdateResult(
    SongRootUpdateStatus Status,
    IReadOnlyList<string> CanonicalRoots,
    IReadOnlyList<SongRootDiagnostic> Diagnostics)
{
    public bool IsSuccess =>
        Status is SongRootUpdateStatus.Updated
            or SongRootUpdateStatus.Unchanged;
}
```

The implementation may adjust concrete type names but must preserve those four
outcomes and immutable snapshots.

The operation:

1. Validates and canonicalizes the complete list.
2. Captures prior `SongRoots` and `DTXPath` snapshots.
3. Clears/refills `SongRoots` and updates `DTXPath` in memory.
4. Writes the complete config immediately through atomic temp-file replacement.
5. Clears any pending deferred-save marker on success because all current
   settings were written.
6. Restores prior in-memory values if persistence fails.
7. Raises `SongRootsChanged` only after persistence succeeds.
8. Returns `Unchanged` without a write or event when the canonical ordered list
   is unchanged.

`SongRootsChangedEventArgs` carries copied immutable old and new root snapshots.
Subscriber exceptions are isolated using existing config-event behavior. A
subscriber failure cannot undo accepted persisted configuration.

## Live Library Reload

### Boundary

Introduce `ISongLibraryReloadService` so `ConfigStage` does not own raw
`SongManager` orchestration and tests do not need the singleton.

The production implementation:

- Resolves available roots from the persisted configured snapshot.
- Adapts `EnumerationProgress` to Config status.
- Invokes `SongManager.EnumerateAndImportSongsAsync` exactly once for any
  changed ordered list, including reorder-only changes.
- Returns the successful active-root set, aggregate counts, skipped-root
  warnings, busy, cancellation, partial post-commit failure, or terminal
  pre-commit failure.
- Allows one in-flight reload.

It does not call `NeedsEnumerationAsync` and does not invent a cache or
reorder-only path. HPA-192 remains the sole importer and publication authority.

### Apply sequence

1. `SongFolderPanel` validates and commits its draft.
2. `ConfigManager.SetSongRoots` atomically persists the canonical ordered list.
3. `Saved` fires before `Closed`; Config captures the committed snapshot.
4. `Unchanged` closes with no reload.
5. `Updated` acquires the Config song-operation slot and displays reload status.
6. The reload service probes all configured roots.
7. When at least one root is available, one enumeration/import starts with only
   those roots.
8. The old hierarchy remains active while discovery and persistence run.
9. On successful commit, finalization and publication replace the hierarchy and
   active-root snapshot atomically.
10. Config reports folder, chart, unavailable-folder, and error counts. Logical
    song counts remain structured-log detail rather than user-facing copy.

Unavailable configured roots are omitted from the scan and listed as warnings.
Their database records remain outside active import cleanup.

When no configured root is available, Config skips the importer and retains the
current active hierarchy. The roots remain saved and Config reports that the
current library is retained until a later successful reload or restart.

### Config operation mutual exclusion

`ConfigStage` owns one operation slot for Config-initiated song-library work:

```text
None | NxScoreImport | SongFolderReload
```

Both `StartNxScoreImport` and song-folder Apply must acquire this slot before
starting work and release it only in their terminal continuation.

Required behavior:

- Apply while NX import is running is rejected with a clear status; roots are
  not re-persisted and no reload starts.
- NX import while reload is running is rejected with a clear status.
- A second reload is rejected while reload is active.
- Existing `SongManager` enumeration-slot rejection is converted to a `Busy`
  reload result, never an unhandled `InvalidOperationException`.
- Future Config features that mutate or rebuild the song library must use the
  same slot.

The operation slot prevents the current NX import and reload paths from racing
on SQLite and hierarchy refresh. `SongManager` retains its own enumeration slot
as the lower-level defensive boundary for non-Config callers.

### Cancellation, commit, and publication

The HPA-192 transaction commit through hierarchy publication is treated as a
non-cancellable terminal section:

- Cancellation is honored during probing, discovery, parsing, matching, and
  persistence before the database commit.
- Once the bulk import commits successfully, the caller must not perform another
  cancellation check before `FinalizePendingNodes` and `PublishEnumeration`.
- A cancellation request arriving after commit is ignored until publication
  completes; the result is success, not cancellation.
- This rule prevents the database from reflecting the new active-root cleanup
  while the old hierarchy remains published solely because cancellation arrived
  in the commit-to-publish gap.

If an unexpected finalization or publication exception occurs after commit, the
old hierarchy remains atomically intact, but the database may already reflect
the new import. Report a distinct partial-success failure requiring restart;
do not claim rollback. This is an exceptional recovery path, not normal
cancellation behavior.

### Config deactivation

Leaving Config requests cancellation for whichever Config song operation owns
the slot. Deactivation never synchronously waits for filesystem or SQLite work
on the game/update thread.

Use the same pattern as `StartupStage`:

- Increment an activation generation.
- Cancel the operation CTS.
- Attach an execute-synchronously observation continuation.
- Observe any fault and dispose the CTS only after task termination.
- Suppress UI/status writes from stale generations.

The game-wide `SongManager` operation may finish after Config deactivates. A
post-commit reload still finalizes and publishes; only Config-specific visual
feedback is suppressed.

### Failure classes

Before database commit, failure or cancellation means:

- Persisted configured roots remain saved.
- Database transaction rolls back.
- Previous active hierarchy and active-root snapshot remain published.
- No partial hierarchy is exposed.
- Config reports that the saved roots will be retried on startup or Apply.

After database commit, cancellation no longer changes the success path. An
unexpected post-commit publication failure uses the partial-success recovery
message described above.

## Startup Integration

`StartupStage` reads an immutable snapshot of `Config.SongRoots`, never
`DTXPath`.

Before cache/enumeration selection, startup performs the shared availability
probe:

- Available roots are passed to `NeedsEnumerationAsync`, cache hierarchy
  construction, and enumeration.
- Unavailable roots are logged once with diagnostics.
- Root order remains configured order.

When no configured root is available during clean startup, startup publishes an
empty active hierarchy and empty active-root snapshot through a dedicated
`SongManager` operation that does not import, stale-clean, or delete database
records. Startup completes and Title remains reachable.

A later startup or Apply restores songs from retained data or enumeration using
the normal load decision.

## Runtime Consumer Audit

Slice 1 must grep every production `DTXPath` read and classify it. No runtime
consumer may silently remain first-root-only.

Known consumers include:

- `StartupStage`: switch to configured `SongRoots` snapshot.
- `ConfigStage`: replace the read-only DTX Folder item.
- `SongSelectionStage` / `PreviewImagePanel`: remove the single
  `SongsRootPath` fallback.
- Any E2E fixture or runtime helper that supplies the production configuration.

### Preview resolution

The selected chart's normalized absolute `SongChart.FilePath` remains the first
and authoritative source for its directory.

For legacy nodes whose directory is still relative,
`PreviewImagePanel.SongsRootPath` becomes an immutable ordered active-root
snapshot, for example `ActiveSongRootPaths`. The panel tries each active root in
order. It must not use configured roots because a failed reload can leave the
old hierarchy and old active roots authoritative.

`SongManager` exposes a copied read-only current-search-path snapshot for
SongSelection activation and refresh. A preview under root N greater than zero
must resolve without falling back to root zero.

## Active Views and Retained Data

Removed or unavailable-root rows remain in SQLite, but active Song Select views
must not surface them after a successful reload excludes that root.

Product rule:

- All Songs uses the published active hierarchy.
- Bookmarks and Recent filter database-backed results to the current active-root
  snapshot.
- If a reload fails and the previous hierarchy stays active, the previous roots'
  Bookmarks and Recent entries remain visible because those roots are still
  active.
- After a successful removal reload, off-root Bookmarks and Recent entries are
  hidden but retained.
- Re-adding and successfully publishing the root makes retained entries visible
  again with their user-owned data intact.

## Empty Library UX

Song Select must show a deliberate empty state rather than a blank list:

- Configured roots exist but none are active/available: **No song folders are
  currently available. Open Config to reconnect or change them.**
- At least one active root exists but contains no supported charts: **No songs
  were found in the active song folders.**

Exact copy may be localized later, but the two conditions remain distinct and
are covered by logic tests.

## SongManager and Database Interaction

No database migration is required.

The existing HPA-192 contracts remain in force:

- Enumeration and persistence receive an ordered root array.
- Root aliases are defensively deduplicated with `SongRootPolicy.RootComparer`.
- Cleanup affects only roots supplied to the successful import.
- Charts and songs outside those roots are retained.
- Bookmarks, score-speed variants, play counts, recent timestamps, ranks,
  full-combo state, and performance history remain user-owned.
- The replacement hierarchy is published only after transaction commit.

HPA-191 prevents textual parent/child overlaps before they reach `SongManager`.
The importer is not responsible for resolving physical aliases or choosing
which overlapping root the player intended.

Multiple roots remain flattened into the existing root-level hierarchy in
configured order. Equal display titles are allowed; identity remains path and
database based, and nodes are not merged.

## Logging and User Feedback

Structured logs include:

- Migration from `DTXPath` to indexed roots.
- Dropped malformed, duplicate, or overlapping hand-edited entries.
- Canonical old/new root lists after commit.
- Availability warnings by configured root.
- Picker selected/cancelled outcomes at debug level and failures at warning.
- Config operation-slot busy decisions.
- Reload start, success, cancellation, pre-commit failure, and post-commit
  partial failure with aggregate counts.

Normal logs must not emit new per-file success messages beyond HPA-192 progress.

Config user-facing examples:

- `Reloading songs: 48 / 100 charts`
- `Loaded 2 folders, 100 charts; 1 folder unavailable`
- `Folders saved; no configured folder is currently available`
- `Song library is busy importing NX scores`
- `Reload failed; previous song list retained`
- `Songs were updated but the list could not refresh; restart required`

## Testing Strategy

### Configuration unit tests

- Default config contains exactly the managed default root and mirrors it to
  `DTXPath`.
- Indexed roots round-trip in order and rewrite densely.
- Legacy `DTXPath` migrates to one indexed root and persists immediately.
- Legacy default `Songs` migration still resolves to `DTXFiles`.
- Indexed entries take precedence over `DTXPath`.
- Gaps, malformed indexes, duplicate indexes, and blank values behave as
  specified.
- Root duplicates use `SongRootPolicy.RootComparer`: ignore-case on Windows and
  macOS, ordinal on Linux.
- `SongPathIdentity.CanonicalComparer` remains ordinal for chart identity.
- Parent/child overlaps are rejected symmetrically.
- Symlink/junction aliases are documented but not resolved.
- Missing custom roots are not created.
- Managed default root is created when restored.
- Persistence failure restores old roots and emits no event.
- Successful update raises one event with immutable snapshots.
- `Unchanged`, `ValidationFailed`, and `PersistenceFailed` results are distinct.

### Config and panel tests

- System exposes **Song Folders** instead of a read-only path.
- Singular/plural count summaries are correct.
- Panel starts from an isolated draft.
- Add, remove, reorder, Apply, Cancel, and Back are deterministic.
- Async picker cancellation/failure does not mutate the draft.
- Stale picker completion cannot update a reactivated/disposed panel.
- Structural errors keep the panel open; availability warnings may be applied.
- Last configured root cannot be removed without adding another.
- Config's commit delegate is the only `SetSongRoots` caller.
- `Saved` fires before `Closed` after successful commit.
- Persistence failure keeps the panel open and starts no reload.
- Active overlay polymorphism continues supporting existing key panels.

### Operation and reload tests

- Unchanged ordered roots perform no write and no scan.
- Reorder-only changed roots perform exactly one full importer call.
- One successful Apply invokes importer once with available roots in order.
- Unavailable roots are skipped without blocking available roots.
- No available roots skip import and retain existing hierarchy.
- Apply is rejected while NX import runs.
- NX import is rejected while reload runs.
- Lower-level enumeration busy is mapped to a user-visible Busy result.
- Leaving Config cancels and asynchronously observes the active operation.
- Stale generation continuations cannot write Config UI state.
- Pre-commit failure/cancellation preserves old hierarchy.
- Cancellation after commit cannot suppress publication and reports success.
- Unexpected post-commit publication failure reports partial success/restart.

### Startup and runtime integration tests

- Startup consumes `SongRoots`, not compatibility `DTXPath`.
- Every production `DTXPath` consumer is removed or explicitly compatibility-only.
- Multiple roots appear in configured order.
- Root aliases are imported once under the selected root comparer.
- Charts under distinct roots are imported once each.
- Root N preview fallback resolves using active roots, not configured root zero.
- Removed-root songs disappear from active hierarchy after success but remain in
  SQLite with scores/bookmarks intact.
- Bookmarks and Recent hide off-active-root rows and restore them after re-add.
- Missing removable root does not block another root.
- No available roots publish an empty active library without database cleanup.
- Distinct Song Select empty states are selected correctly.
- Windows and macOS compile with only their picker implementation.
- Existing HPA-192 cancellation, retention, and performance tests remain green.

## Implementation Slices

The slices are hard dependencies and land in order: **Slice 1 → Slice 2 →
Slice 3**. Each is intended to fit within three engineer days.

### Slice 1: Canonical multi-root configuration and consumer migration

- Add `SongRoots`, indexed INI format, compatibility mirror, migration, result
  contract, immutable event snapshots, and immediate persistence.
- Add internal `SongRootPolicy` with the explicit root comparer, overlap
  validation, normalization, and availability probing.
- Align `SongManager` defensive root deduplication with the root policy.
- Update startup to consume root snapshots and publish an empty active library
  when no root is available.
- Audit and migrate all production `DTXPath` consumers, including active-root
  preview fallback and active-root snapshot exposure.
- Add configuration, startup, preview, and consumer-audit tests.
- Do not add Config editing UI or live reload.

### Slice 2: Cross-platform SongFolderPanel

Depends on Slice 1.

- Add `IConfigOverlayPanel` and adapt key-panel interface.
- Add asynchronous `IFolderPickerService` with Windows, macOS, and fake
  implementations.
- Replace **DTX Folder** with **Song Folders** and implement draft editing.
- Commit through Config's single commit delegate and `SetSongRoots`.
- Show temporary `restart required` status after `Updated` until Slice 3.
- Verify the persisted roots are consumed by Startup on the next launch.
- Add panel, picker-threading, and platform-boundary tests.

### Slice 3: Live reload and retained-data integration

Depends on Slices 1 and 2.

- Add `ISongLibraryReloadService`.
- Add Config operation mutual exclusion shared by NX import and reload.
- Add progress, unavailable-root summaries, non-blocking cancellation
  observation, commit-to-publish terminal semantics, and failure feedback.
- Add active-root filtering for Bookmarks/Recent and deliberate empty-state UX.
- Verify successful publication, old-hierarchy retention, post-commit recovery,
  and removed-root data retention.
- Add multi-root integration tests and Windows/macOS build validation.

## Acceptance Criteria

- Existing one-path `DTXPath` configurations migrate without library loss.
- A fresh install has one managed default song root.
- Config allows adding, removing, and reordering roots on Windows and macOS.
- Ordered roots survive restart and are read by startup after Slice 2.
- Root duplicate comparison is explicitly ignore-case on Windows/macOS and
  ordinal on Linux; chart canonical identity remains ordinal.
- Duplicate and textual parent/child-overlapping roots cannot be applied.
- Missing custom roots remain configured and are never created automatically.
- Previously auto-created custom directories are not deleted by migration.
- Unchanged roots do not scan; any changed ordered list performs at most one full
  scan, including reorder-only changes.
- Every available root is scanned in configured order.
- NX import and song reload cannot run concurrently from Config.
- Busy lower-level enumeration is reported without an unhandled exception.
- One unavailable root does not prevent available roots from loading.
- A successful reload replaces active hierarchy atomically.
- Pre-commit failure or cancellation never exposes a partial hierarchy.
- Cancellation after commit cannot suppress hierarchy publication.
- Config deactivation never blocks the game thread waiting for reload/import.
- No available root during Apply retains the previous active hierarchy.
- No available root during startup reaches Title with an empty active library
  and leaves SQLite untouched.
- Preview fallback under any active root resolves correctly.
- Removing a root hides its songs, Bookmarks, and Recent entries after successful
  reload while preserving their database records and user-owned data.
- Re-adding a root restores retained user data and active views.
- Song Select distinguishes unavailable folders from available-but-empty roots.
- `DTXPath` remains a compatibility mirror only; every runtime consumer uses
  configured or active root snapshots as appropriate.
- Symlink/junction/physical-target deduplication is explicitly outside scope.
- No DTXCreator, parser, song schema, or synthetic-root-navigation changes are
  included.
