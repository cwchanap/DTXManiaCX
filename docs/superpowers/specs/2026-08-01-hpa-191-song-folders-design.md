# HPA-191 Configurable Multiple Song Folders Design

**Issue:** [HPA-191](https://linear.app/cwchanap/issue/HPA-191/allow-change-song-folders)  
**Date:** 2026-08-01  
**Status:** Revised after second-pass design review

## Context

DTXManiaCX currently persists one song-library path in `ConfigData.DTXPath`,
writes it as `DTXPath=` in `Config.ini`, displays it as a read-only **DTX
Folder** item in `ConfigStage`, and wraps it in a one-element array during
startup.

The lower-level song system is already substantially multi-root capable:

- `SongManager` accepts ordered arrays of search roots.
- Enumeration normalizes and deduplicates roots.
- Root traversal order is preserved in the published hierarchy.
- HPA-192 bulk import and stale cleanup are scoped to the active roots supplied
  to a completed import.
- Rows outside active roots are retained, protecting bookmarks, score variants,
  play history, ranks, full-combo state, and performance history when a library
  is removed or unavailable.

HPA-191 therefore does not introduce another importer or a database schema
migration. It makes the ordered root list a first-class configuration value,
adds safe Config editing and platform folder selection, migrates every runtime
consumer away from the single-root compatibility property, and performs one
atomic live reload without routing the game through boot-only `StartupStage`
state.

## Goals

- Allow players to add, remove, and reorder song-library roots from Config.
- Persist one or more ordered roots while migrating existing `DTXPath`
  configurations without library loss.
- Apply valid changes immediately and perform at most one live reload.
- Preserve the currently published hierarchy until a replacement library has
  committed and published successfully.
- Preserve database rows and user-owned state for removed or temporarily
  unavailable roots.
- Support Windows and macOS folder selection without platform UI dependencies
  in shared game code.
- Reject textual duplicate or parent/child root combinations that would scan the
  same subtree more than once under the selected root-comparison policy.
- Keep startup deterministic when some or all configured roots are unavailable.
- Serialize Config-initiated library mutation operations so NX score import and
  song-folder reload cannot race.
- Keep an already-active Song Select stage coherent when a reload publishes
  after Config has deactivated.

## Non-goals

- DTXCreator changes.
- Chart parser, `set.def`, or `box.def` semantic changes.
- Song database schema changes.
- Filesystem watchers or automatic reload when removable media mounts.
- Rescanning after each individual draft edit.
- A database-only reorder fast path. Any changed ordered root list performs one
  full enumeration/import in HPA-191.
- Editing or creating chart files from Config.
- Synthetic top-level boxes for each configured root.
- Merging same-named folders or songs across roots.
- Deleting retained records for removed roots.
- Resolving filesystem aliases to physical targets. Symlinks, junctions, bind
  mounts, aliases, and case-sensitive macOS volumes may expose one target through
  distinct paths; realpath/inode-based deduplication is deferred.
- General-purpose file-picker infrastructure beyond folder selection.
- Replacing the macOS AppleScript adapter with a native `NSOpenPanel` helper.
- Linux-specific picker integration. The configuration and reload contracts
  remain platform-neutral, but HPA-191 targets the existing Windows and macOS
  projects.

## Chosen Approach

Add an ordered `SongRoots` collection as the sole configured-root source of
truth. Keep `DTXPath` only as a serialized compatibility mirror of the first
root. A Config overlay edits a private draft and commits through one
Config-owned delegate. An asynchronous platform picker supplies new paths.
After the changed list is persisted atomically, a focused reload service invokes
the existing HPA-192 enumeration/import path once.

The old hierarchy remains active until the complete replacement batch commits,
finalizes, and publishes. Failure or cancellation before commit cannot expose a
partial list.

The design distinguishes three root states:

- **Configured roots:** ordered normalized paths persisted in `Config.ini`.
- **Available roots:** configured roots that currently exist and pass a shallow
  access probe.
- **Active roots:** roots represented by the currently published `SongManager`
  hierarchy.

Configured and active roots may differ after a failed reload. Runtime surfaces
that represent the published library must use the active-root snapshot, not the
new configured list.

## Alternatives Considered

### Save and require restart

This is the smallest change but leaves the active library stale and provides
weak confirmation. It is rejected for the completed feature. Slice 2 uses a
temporary restart-required state until Slice 3 lands.

### Transition Config through StartupStage

`StartupStage` owns boot phases, activation generations, timing summaries, and
exactly-once startup telemetry. Re-entering it for a user operation would couple
unrelated lifecycles and make telemetry ambiguous. It is rejected.

### Rebuild from SQLite for reorder-only changes

The hierarchy builder could potentially republish roots in a new order without
parsing charts. HPA-191 deliberately does not add that branch. Apply is an
explicit refresh and one full scan also observes filesystem changes made while
the panel was open. A future measured optimization may specialize reorder-only
changes.

### Store one delimited value

A delimiter conflicts with legal path characters and complicates escaping and
hand editing. Indexed keys are easier to parse, order, diagnose, and migrate.

## Configuration Contract

### Runtime model

`ConfigData` gains:

```csharp
public List<string> SongRoots { get; } = new();
```

The property remains get-only. Load, reset, and successful updates clear and
refill the existing collection instead of replacing its reference. Consumers
must never retain or mutate the live list. Startup, UI, events, and reload
boundaries receive copied immutable snapshots.

`DTXPath` remains for source and downgrade compatibility only:

- After load, reset, or a successful update, it equals the first normalized
  configured root.
- Startup, Config, reload, active views, previews, and all other runtime behavior
  consume configured or active root snapshots instead.
- Direct mutation of `DTXPath` is not a supported runtime update mechanism.

At least one configured root is required. Availability is not required; the
only root may be a disconnected removable or network location.

### Persisted format

```ini
[System]
DTXPath=C:\DTX\Main
SongRoot.0=C:\DTX\Main
SongRoot.1=D:\Community Charts
```

Rules:

1. `SongRoot.<index>` uses a non-negative decimal integer.
2. Entries load in ascending numeric order; gaps are allowed.
3. Malformed suffixes, negative indexes, and blank values are ignored with a
   warning.
4. Duplicate indexes use the last parsed value and emit a warning.
5. Values may contain `=` because parsing splits on the first `=` only.
6. Save rewrites indexes densely from zero.
7. `DTXPath` mirrors the first root.
8. At least one structurally valid indexed entry makes `SongRoot.*`
   authoritative.
9. With no valid indexed entry, load migrates legacy `DTXPath` into a one-root
   list.
10. If neither representation yields a valid path, load restores the managed
    default.

A successful legacy migration is immediately persisted through the existing
atomic temp-file replacement.

### Downgrade compatibility caveat

The `DTXPath` mirror is best-effort compatibility, not a guarantee that older
build behavior is safe. If the first root is an unavailable removable or
network path and the user runs a pre-HPA-191 build, that older build may execute
its unconditional directory-creation behavior and create an empty directory at
that path. HPA-191 cannot prevent behavior after a downgrade; ordering a stable
local root first reduces that risk.

### Legacy path migration

The existing known-default migration from `Songs` to the managed `DTXFiles`
location remains. It applies to the fallback legacy value before it becomes
`SongRoots[0]`. Indexed custom roots are not remapped merely because their final
segment is named `Songs`.

Previous builds may already have created empty custom directories while a
removable or network root was unavailable. HPA-191 does not delete or move such
folders.

### Deliberate removal of custom-root auto-creation

Today `NormalizeConfigPaths` unconditionally calls
`EnsureDirectorySafe(Config.DTXPath)`, creating every configured custom path at
load. Slice 1 deliberately removes that behavior.

After HPA-191:

- The game may create only `AppPaths.GetDefaultSongsPath()` when the managed
  default is selected or restored as fallback.
- Missing custom roots are retained as unavailable configuration values.
- No load, migration, Apply, startup, or availability probe creates a custom
  directory.
- Existing directories created by older versions are left untouched.

This is an intentional product behavior change, not merely an implementation
detail of the multi-root format.

## Root Path Identity and Validation

Root identity is intentionally separate from HPA-192 chart identity.
`SongPathIdentity.CanonicalComparer` remains ordinal for exact persisted chart
identity.

### Shared root policy

Add internal `SongRootPolicy` in the shared `DTXMania.Game` assembly. Config
load, Config UI, `SetSongRoots`, startup, defensive `SongManager` root
deduplication, overlap checks, and availability probing all use this policy.

`SongRootPolicy.RootComparer` uses
`SongPathIdentity.LegacyAliasComparer`:

- Windows and macOS: ordinal ignore-case.
- Linux and other platforms: ordinal case-sensitive.

On a case-sensitive macOS volume, case-only root variants are still treated as
duplicates for HPA-191. Physical-case and real-target discovery are out of
scope.

`SongPathIdentity` remains internal. No public exposure is necessary because the
policy and consumers live in the shared assembly; tests use the repository's
internal-test access pattern.

### Canonicalization

For each non-blank input:

1. Expand supported home-relative forms through `AppPaths`.
2. Resolve legacy relative paths against the existing app-data base.
3. Convert to an absolute full path.
4. Normalize separators and trailing separators with
   `SongPathIdentity.Normalize`.
5. Compare roots with `SongRootPolicy.RootComparer`.
6. Persist the normalized absolute value.

Root order controls traversal and root-level presentation order.

### Defensive SongManager alignment

`SongManager.SetCurrentSearchPaths`, enumeration-batch root creation, and every
other defensive root deduplication point must use
`SongRootPolicy.RootComparer`, not the ordinal chart comparer. Direct callers
must not reintroduce aliases that Config rejects.

This is a deliberate behavior change for all `SetCurrentSearchPaths` callers,
including startup cache/database paths such as `LoadScoreCacheAsync`,
`BuildSongListFromDatabasePublicAsync`, and `NeedsEnumerationAsync`. On Windows
and macOS, roots differing only by case will now collapse to one root in those
paths as well as during fresh enumeration.

Slice 1 must add focused coverage for `SetCurrentSearchPaths` behavior and its
snapshot, plus caller-path tests for cache load, database hierarchy rebuild, and
enumeration decisions. The comparer change must not rely only on Config-panel
integration tests.

### Blocking structural errors

Apply is blocked by:

- No roots.
- Blank or syntactically invalid paths.
- Duplicates under `SongRootPolicy.RootComparer`.
- Parent/child overlap in either direction after normalization.

Overlap segment comparison uses the same root comparer. Authored order does not
make overlap valid.

Hand-edited invalid configuration must not prevent startup. Load processes
entries in numeric order; the first accepted root wins and later duplicate or
overlapping entries are dropped with warnings. If all are rejected, the managed
default is restored.

Symlink, junction, mount, and alias targets are not resolved. Two textually
different non-overlapping paths reaching the same physical directory may both
scan in v1.

### Availability warnings

Missing and inaccessible roots are warnings, not structural failures. They stay
configured so removable and network libraries can return later.

The shallow probe:

- Checks directory existence.
- Attempts to begin a shallow directory enumeration.
- Catches path, I/O, and authorization failures and returns a diagnostic.
- Never recursively scans charts and never creates directories.

Only available roots are supplied to startup or live reload. A root may still
fail during scanning if availability changes; that follows the reload failure
contract.

## Config User Experience

### System menu entry

Replace **DTX Folder** with a navigation item named **Song Folders**. Its value
is `1 folder` or `<n> folders`. The description says folders are scanned in the
displayed order and Apply reloads once.

### Overlay abstraction

Introduce `IConfigOverlayPanel` with the common lifecycle currently carried by
`IKeyAssignPanel`:

- `IsActive`
- `Closed`
- `Saved`
- `Activate()` / `Deactivate()`
- `Update(...)`
- `Draw(...)`

`IKeyAssignPanel` inherits it and keeps the existing `Saved`-before-`Closed`
contract. `ConfigStage._activePanel` becomes `IConfigOverlayPanel`. This is a
targeted boundary correction, not a broad UI rewrite.

### SongFolderPanel ownership

`SongFolderPanel` receives:

- An immutable configured-root snapshot.
- `IFolderPickerService`.
- The shared validation policy.
- A Config-owned commit delegate:

```csharp
Func<IReadOnlyList<string>, SongFolderCommitResult> commitRoots
```

The panel owns a private draft. Add, remove, reorder, Back, and Cancel never
mutate configuration.

The delegate is the only commit owner:

1. Canonicalize and compare the draft with the current configured snapshot.
2. Return `Unchanged` without acquiring a slot or writing.
3. For a changed list, reserve the Config operation slot as
   `SongFolderReload` before persistence.
4. Return `Busy` without persisting if NX import or another reload owns the
   slot.
5. Call `SetSongRoots` only while holding the reservation.
6. Release the slot immediately on validation or persistence failure.
7. Retain the reservation on `Updated` for handoff to the reload task.

On Apply, failure or `Busy` keeps the panel open with a diagnostic. On `Updated`
or `Unchanged`, the panel stores an immutable committed snapshot, raises
`Saved`, then `Closed`.

The panel never holds `IConfigManager` and never persists independently.

```csharp
public enum SongFolderCommitStatus
{
    Updated,
    Unchanged,
    Busy,
    ValidationFailed,
    PersistenceFailed
}

public sealed record SongFolderCommitResult(
    SongFolderCommitStatus Status,
    IReadOnlyList<string> CanonicalRoots,
    IReadOnlyList<SongRootDiagnostic> Diagnostics);
```

Supported actions:

- **Add Folder**
- **Remove** while one draft root remains
- **Move Up** / **Move Down**
- **Apply**
- **Cancel**

Structural errors keep the panel open. Availability warnings do not disable
Apply. Picker cancellation is a no-op; picker failure leaves the draft
unchanged.

## Platform Folder Picker

Shared code uses:

```csharp
public interface IFolderPickerService
{
    Task<FolderPickerResult> PickFolderAsync(
        string? initialDirectory,
        CancellationToken cancellationToken);
}
```

The result distinguishes Selected, Cancelled, Unavailable, and Failed. While a
picker is active, repeat Add actions are ignored. Completion updates panel state
only when its activation generation remains current.

### Windows

`FolderBrowserDialog` is acceptable because the Windows project already enables
WinForms. The platform implementation owns STA dispatch; shared update code does
not assume it runs on an STA thread. The dialog starts from the selected root or
managed default when valid.

### macOS

Invoke `osascript` using Standard Additions `choose folder`, with a clear prompt
and `default location` when the initial directory exists. Build arguments with
`ProcessStartInfo.ArgumentList`, never shell interpolation. Await exit
asynchronously, distinguish the standard user-cancel error, terminate on
cancellation when safe, and log stderr for non-cancellation failures.

The first invocation may display macOS system consent or privacy UI depending on
the host process and selected location. The product must not promise a specific
System Settings pane. Cancellation remains a normal `Cancelled` result;
permission or authorization denial is `Failed` with a concise user message and
structured diagnostic.

Shared code must not reference WinForms, AppleScript construction, or native
picker types. Headless tests inject a deterministic fake. A future
`NSOpenPanel` helper may replace the adapter without changing the interface.

## Config Persistence API

`IConfigManager` gains:

```csharp
SongRootUpdateResult SetSongRoots(
    string configFilePath,
    IReadOnlyList<string> roots);
```

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

The persistence operation:

1. Validates and canonicalizes the complete list.
2. Captures prior `SongRoots` and `DTXPath` snapshots.
3. Clears/refills `SongRoots` and updates the mirror in memory.
4. Writes the complete config through atomic temp-file replacement.
5. Clears the deferred-save marker on success because all settings were written.
6. Restores prior in-memory values if persistence fails.
7. Raises `SongRootsChanged` only after persistence succeeds.
8. Returns `Unchanged` without write or event for an equal ordered list.

Event args carry copied immutable old/new snapshots. Subscriber exceptions are
isolated and cannot roll back accepted configuration.

## Live Library Reload

### Boundary

Introduce `ISongLibraryReloadService` so `ConfigStage` does not directly own raw
singleton orchestration.

The production service:

- Probes the persisted configured snapshot.
- Adapts `EnumerationProgress` to Config status.
- Calls `SongManager.EnumerateAndImportSongsAsync` exactly once for every changed
  ordered list, including reorder-only changes.
- Returns active roots, aggregate counts, unavailable warnings, Busy,
  cancellation, pre-commit failure, or post-commit partial failure.
- Allows one in-flight reload.

It does not call `NeedsEnumerationAsync` or create another cache/reorder path.

### Apply sequence

1. Panel validates the draft locally.
2. Config's delegate returns `Unchanged` or reserves `SongFolderReload`.
3. A busy slot returns `Busy`; no persistence occurs.
4. With the slot reserved, `SetSongRoots` persists the canonical list.
5. Failure releases the slot and keeps the panel open.
6. `Updated` raises `Saved` before `Closed`.
7. The `Saved` handler starts reload using the reservation.
8. Reload probes configured roots.
9. With at least one available root, it starts one enumeration/import.
10. The previous hierarchy stays active during discovery and persistence.
11. Commit, finalization, and publication replace hierarchy and active roots.
12. Config reports folder, chart, unavailable-folder, and error counts.

If no root is available, import is skipped, the previous hierarchy is retained,
a warning is shown, and the operation slot is released.

### Config operation mutual exclusion

`ConfigStage` owns:

```text
None | NxScoreImport | SongFolderReload
```

NX import and changed-root Apply acquire the same slot. Apply while NX import is
active returns `Busy` without persistence. NX import while reload is active is
rejected. A second reload is rejected. Lower-level
`InvalidOperationException` from an occupied SongManager enumeration slot is
mapped to a user-visible Busy result.

### Throw-safe reservation handoff

The reservation-to-task handoff is a strict implementation invariant. Acquiring
the slot, persisting, raising `Saved`, constructing the reload task, and
transferring release ownership must use `try/finally` or an equivalent scoped
lease.

Required ownership rule:

- Before the reload task is attached, synchronous code owns the lease and must
  release it in `finally` on every exit or exception.
- Only after the terminal continuation is attached may release ownership move
  to that continuation.
- Task-construction, event-subscriber, or continuation-registration failure
  cannot orphan the slot.
- The terminal continuation releases exactly once.

A dedicated test injects a throw at each handoff point and verifies the next
operation can acquire the slot.

### Cancellation, commit, and publication

Database commit through hierarchy publication is non-cancellable:

- Cancellation is honored during probing, discovery, parsing, matching, and
  persistence before commit.
- After commit succeeds, no cancellation check occurs before
  `FinalizePendingNodes` and `PublishEnumeration`.
- A request arriving after commit is ignored until publication completes and is
  reported as success.

This prevents the database from reflecting new active-root cleanup while the
old hierarchy remains solely because cancellation arrived in the commit-to-
publish gap.

An unexpected finalization/publication exception after commit reports a distinct
partial-success/restart-required outcome. It must not claim rollback.

### Config deactivation

Leaving Config requests cancellation but never blocks the update thread waiting
for filesystem or SQLite work.

Use the `StartupStage` retirement pattern:

- Increment an activation generation.
- Cancel the operation CTS.
- Attach an execute-synchronously observation continuation.
- Observe faults and dispose the CTS only after termination.
- Release the operation lease in the terminal continuation.
- Suppress Config UI/status writes from stale generations.

A post-commit reload may finish and publish after Config deactivates.

## Startup Integration

`StartupStage` reads an immutable `SongRoots` snapshot, never `DTXPath`.

Before cache/enumeration selection:

- Probe configured roots.
- Pass available roots in configured order to `NeedsEnumerationAsync`, cache
  hierarchy construction, or enumeration.
- Log unavailable roots once.

When no root is available on clean startup, publish an empty active hierarchy
and empty active-root snapshot through a dedicated SongManager operation that
does not import, stale-clean, or delete database rows. Startup completes and
Title remains reachable.

## Runtime Consumer Audit

Slice 1 must grep and classify every production `DTXPath` read. No runtime
consumer may silently remain first-root-only.

Known consumers:

- `StartupStage`: configured `SongRoots` snapshot.
- `ConfigStage`: replace the read-only item.
- `SongSelectionStage` / `PreviewImagePanel`: active-root fallback snapshot.
- E2E fixtures and runtime helpers that provide production configuration.

A source-level architecture test or explicit allowlist must fail if a new
runtime `Config.DTXPath` read is introduced outside serialization/migration
compatibility code.

### Preview resolution

The normalized absolute `SongChart.FilePath` is authoritative. For legacy nodes
with relative directories, replace `PreviewImagePanel.SongsRootPath` with an
immutable ordered active-root snapshot such as `ActiveSongRootPaths`, trying
each active root in order.

Configured roots must not be used for this fallback because failed reload leaves
the previous active hierarchy authoritative. Root N greater than zero must
resolve correctly.

`SongManager` exposes a copied current-search-path snapshot for Song Select
activation and refresh.

## Active Song Select Publication Handling

A reload can commit and publish after Config deactivates. A player may reach an
already-active Song Select stage before that terminal publication. Today Song
Select copies `SongManager.RootSongs` during initialization and does not observe
later publication, so merely proving that the stale list does not crash is
insufficient.

HPA-191 requires an update-thread refresh contract:

- Song Select subscribes on activation and unsubscribes on deactivation to a
  library-published notification. The existing `EnumerationCompleted` event may
  be used if its post-publication contract is made explicit; otherwise add a
  dedicated `SongLibraryPublished` event carrying a monotonically increasing
  publication version and active-root snapshot.
- The event handler never mutates UI collections. It records a pending version
  or enqueues a refresh request guarded by the stage activation version.
- `OnUpdate` snapshots `RootSongs` and active roots and replaces the displayed
  root list on the game thread.
- If the current navigation path still exists, restore it by stable path or
  database identity. Otherwise reset to the root.
- Preserve the selected chart by stable chart/database identity when possible;
  otherwise select the first valid item.
- Stop previews and clear status/history panels when the selected item no longer
  exists.
- Rebuild active All Songs, Bookmarks, Recent, filter, breadcrumb, preview-root,
  and empty-state data from one publication version so the UI never mixes old
  and new snapshots.
- Stale or deactivated-stage notifications are ignored.

Integration coverage must publish a replacement library while Song Select is
active, including while inside a box and while Bookmarks or Recent is selected,
and verify safe deterministic reconciliation rather than stale display or
background-thread mutation.

## Active Views and Retained Data

Removed/unavailable rows stay in SQLite, but active views do not surface them
after a successful publication excludes their root:

- All Songs uses the active hierarchy.
- Bookmarks and Recent filter database-backed rows to active roots.
- If reload fails, old active roots and their entries remain visible.
- After successful removal, off-root entries are hidden but retained.
- Successful re-add makes them visible with user data intact.

## Empty Library UX

Song Select distinguishes:

- No active roots and no configured root currently available: **No song folders
  are currently available. Open Config to reconnect or change them.**
- At least one active root but no supported charts: **No songs were found in the
  active song folders.**

Exact wording may be localized later; the conditions remain distinct.

## SongManager and Database Interaction

No database migration is required.

HPA-192 contracts remain authoritative:

- Enumeration/persistence receive an ordered root array.
- Root aliases are defensively deduplicated through `SongRootPolicy`.
- Cleanup affects only roots supplied to a successful import.
- Off-root charts and songs are retained.
- User-owned scores, bookmarks, timestamps, ranks, combos, and history are
  preserved.
- Publication follows transaction commit.

HPA-191 prevents textual overlap before import. The importer does not resolve
physical aliases.

Multiple roots remain flattened into the current root-level hierarchy. Equal
display titles are allowed; identity remains path/database based.

## Logging and User Feedback

Structured logs include:

- `DTXPath` migration and indexed-root parsing warnings.
- Deliberate suppression of custom-root directory creation.
- Old/new normalized root snapshots after commit.
- Availability diagnostics.
- Picker selected/cancelled outcomes at debug level and failures at warning.
- Config operation-slot busy decisions and lease handoff failures.
- Reload start, success, cancellation, pre-commit failure, and post-commit
  partial failure.
- Song Select publication-version refresh and reconciliation outcome.

Do not add per-file success logs beyond HPA-192 progress.

User-facing examples:

- `Reloading songs: 48 / 100 charts`
- `Loaded 2 folders, 100 charts; 1 folder unavailable`
- `Folders saved; no configured folder is currently available`
- `Song library is busy importing NX scores`
- `Reload failed; previous song list retained`
- `Songs were updated but the list could not refresh; restart required`

## Testing Strategy

### Configuration tests

- Default config has one managed root and matching `DTXPath`.
- Indexed roots round-trip in order and rewrite densely.
- Legacy `DTXPath` migrates and persists immediately.
- Known legacy `Songs` default migrates to `DTXFiles`.
- Indexed entries take precedence.
- Gaps, malformed indexes, duplicate indexes, and blanks behave as specified.
- Root comparer is ignore-case on Windows/macOS and ordinal on Linux.
- Chart canonical comparer remains ordinal.
- Parent/child overlap is rejected symmetrically.
- Physical aliases are documented but unresolved.
- Custom missing roots are not created.
- The managed default is created when restored.
- Tests explicitly prove removal of the current unconditional custom-root
  `EnsureDirectorySafe` behavior.
- Persistence failure restores prior state and emits no event.
- Successful update emits immutable old/new snapshots once.
- Persistence statuses are distinct.
- Downgrade mirror behavior is documented and serialization remains stable.

### SongManager root-policy tests

- `SetCurrentSearchPaths` directly deduplicates case aliases under
  `SongRootPolicy` and preserves first occurrence/order.
- Its copied snapshot cannot be externally mutated.
- `LoadScoreCacheAsync`, `BuildSongListFromDatabasePublicAsync`, and
  `NeedsEnumerationAsync` receive the aligned deduplicated roots.
- Fresh enumeration and cache hierarchy behavior agree on root identity.
- Linux case-distinct roots remain distinct.

### Config and panel tests

- System exposes **Song Folders**.
- Singular/plural summaries are correct.
- Panel uses an isolated draft.
- Add/remove/reorder/Apply/Cancel/Back are deterministic.
- Async picker cancellation/failure does not mutate the draft.
- Stale picker completion cannot update an inactive panel.
- Structural errors block; availability warnings may be applied.
- Last root cannot be removed without replacement.
- Config's delegate is the only `SetSongRoots` caller.
- Busy changed-root Apply does not persist.
- Persistence failure releases the lease and starts no reload.
- `Saved` precedes `Closed` after success.
- Throws at each reservation-handoff point cannot orphan the lease.
- Key panels still work through overlay polymorphism.
- macOS permission/authorization failure maps to Failed, while normal picker
  cancellation maps to Cancelled.

### Reload and lifecycle tests

- Unchanged roots do not write, acquire, or scan.
- Reorder-only change invokes one full import.
- Successful Apply invokes importer once with available roots in order.
- Unavailable roots do not block available ones.
- No available roots skip import, retain hierarchy, and release the lease.
- Apply is rejected without persistence during NX import.
- NX import is rejected during reload.
- Lower-level busy maps to user-visible Busy.
- Leaving Config cancels and observes without blocking.
- Stale continuations cannot write Config UI.
- Pre-commit failure/cancellation preserves old hierarchy.
- Post-commit cancellation cannot suppress publication.
- Unexpected post-commit publication failure reports partial success.

### Song Select publication tests

- Publication while Song Select is active is processed only on the update
  thread.
- Root list, active roots, filters, Bookmarks, Recent, preview fallback, and
  empty-state data use the same publication version.
- Selection is restored by stable identity when retained.
- Removed selection resets deterministically and stops preview/status state.
- Publication while inside a removed box resets to root safely.
- Publication during Bookmarks or Recent refreshes that tab against active roots.
- Notifications after deactivation are ignored.
- Multiple fast publications apply only the newest version.

### Startup and integration tests

- Startup uses `SongRoots`, not compatibility `DTXPath`.
- Production `DTXPath` reads are absent outside compatibility allowlist.
- Multiple roots appear in configured order.
- Root aliases import once under the root comparer.
- Distinct roots import each chart once.
- Root N preview fallback uses active roots.
- Removed-root songs disappear after successful reload but remain stored.
- Bookmarks/Recent hide off-active entries and restore after re-add.
- Missing removable root does not block another root.
- No available roots publish an empty active library without cleanup.
- Empty states are distinct.
- Windows and macOS compile only their picker implementation.
- Existing HPA-192 cancellation, retention, and performance tests stay green.

## Implementation Slices

Slices are hard dependencies and land in order: **Slice 1 → Slice 2 → Slice
3**. Each is scoped to at most three engineer days.

### Slice 1: Canonical multi-root configuration and consumer migration

- Add `SongRoots`, indexed INI format, mirror, migration, persistence result,
  immutable event snapshots, and immediate persistence.
- Replace unconditional custom-root directory creation with managed-default-only
  creation and add behavior-removal tests.
- Add internal `SongRootPolicy` with normalization, explicit comparer, overlap,
  and availability probing.
- Align all `SongManager` root deduplication with the policy.
- Add direct `SetCurrentSearchPaths` and startup cache/database caller coverage.
- Update startup to consume snapshots and publish empty active state when needed.
- Audit all production `DTXPath` consumers and add an architecture allowlist
  test.
- Add active-root snapshot exposure and multi-root preview fallback.
- Do not add Config editing UI or live reload.

### Slice 2: Cross-platform SongFolderPanel

Depends on Slice 1.

- Add `IConfigOverlayPanel` and adapt key panels.
- Add asynchronous Windows, macOS, and fake picker implementations.
- Include macOS consent/authorization UX and result mapping.
- Replace **DTX Folder** with **Song Folders** and implement draft editing.
- Commit through Config's single delegate.
- Show temporary restart-required status after `Updated`.
- Verify startup consumes persisted roots on next launch.
- Add panel, threading, privacy-failure, and platform-boundary tests.

### Slice 3: Live reload and active-stage reconciliation

Depends on Slices 1 and 2.

- Add `ISongLibraryReloadService`.
- Add Config mutual exclusion and scoped lease handoff with throw-safe release.
- Add progress, warnings, non-blocking retirement, commit-to-publish terminal
  semantics, and failure feedback.
- Add active-root filtering for Bookmarks/Recent and deliberate empty states.
- Add update-thread Song Select reconciliation for out-of-band publication,
  including stable selection/navigation restoration.
- Verify successful publication, retained old hierarchy on pre-commit failure,
  post-commit recovery, and removed-root data retention.
- Add multi-root lifecycle integration tests and Windows/macOS builds.

## Acceptance Criteria

- Existing one-path configurations migrate without library loss.
- Fresh install has one managed default root.
- Config adds, removes, and reorders roots on Windows and macOS.
- Ordered roots survive restart and startup reads them.
- Root comparison is ignore-case on Windows/macOS and ordinal on Linux; chart
  canonical identity remains ordinal.
- `SetCurrentSearchPaths` and startup cache/database paths use the same policy
  with direct tests.
- Duplicate and textual parent/child roots cannot be applied.
- Missing custom roots remain configured and are never auto-created.
- Removal of the current custom-path `EnsureDirectorySafe` behavior is explicit
  and tested.
- Existing auto-created directories are not deleted.
- The first-root downgrade mirror and its older-build recreation caveat are
  documented.
- Unchanged roots do not acquire or scan; any changed ordered list performs at
  most one full scan.
- Busy changed-root Apply does not persist.
- Every available root scans in configured order.
- NX import and reload cannot run concurrently.
- Slot handoff is throw-safe and cannot orphan ownership.
- One unavailable root does not block available roots.
- Successful reload atomically replaces hierarchy and active roots.
- Pre-commit failure/cancellation never exposes partial hierarchy.
- Post-commit cancellation cannot suppress publication.
- Config deactivation never blocks the game thread.
- Song Select safely reconciles an out-of-band publication while active.
- No available root during Apply retains the previous active hierarchy.
- No available root during startup reaches Title with empty active state and
  leaves SQLite untouched.
- Preview fallback works under every active root.
- Removing a root hides its songs, Bookmarks, and Recent entries while retaining
  database data.
- Re-adding restores retained state and active views.
- Song Select distinguishes unavailable roots from available-but-empty roots.
- `DTXPath` remains compatibility-only; runtime consumers use configured or
  active snapshots as appropriate.
- Physical-target deduplication remains explicitly outside scope.
- No DTXCreator, parser, schema, or synthetic-root-navigation change is
  included.
