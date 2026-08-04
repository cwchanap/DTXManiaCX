# HPA-191 Configurable Multiple Song Folders Design

**Issue:** [HPA-191](https://linear.app/cwchanap/issue/HPA-191/allow-change-song-folders)  
**Date:** 2026-08-01  
**Status:** Revised after third-pass design review

## Context

DTXManiaCX currently has one configured song-library root:

- `ConfigData.DTXPath` is serialized as `DTXPath=`.
- `ConfigStage` shows a read-only **DTX Folder** item.
- `StartupStage` wraps the path in a one-element array.
- `SongSelectionStage` gives `PreviewImagePanel` one fallback root.

The lower-level song system already accepts ordered root arrays. HPA-192 owns
filesystem traversal, bulk persistence, retained-data cleanup, hierarchy
finalization, and publication after database commit. HPA-191 therefore does not
add another importer or a database schema migration.

The work is to make ordered roots a first-class configuration value, expose safe
cross-platform editing, align every runtime consumer, and support live
publication while stages are active.

Several current implementation details affect the design:

- `SongPathIdentity.CanonicalComparer` is ordinal, while
  `LegacyAliasComparer` is ignore-case on Windows/macOS and ordinal elsewhere.
- `SongManager.SetCurrentSearchPaths` and enumeration-batch root deduplication
  currently use the ordinal comparer.
- `SongManager.RootSongs` currently returns `_rootSongs.AsReadOnly()`, which is a
  live wrapper over a list mutated by `PublishEnumeration`.
- `SetCurrentSearchPaths` currently ignores empty arrays, so it cannot clear a
  stale active-root snapshot.
- `NormalizeConfigPaths` currently creates every custom `DTXPath` directory.
- `ConfigStage` NX import uses a separate `_importRunning` flag and writes status
  from a background task.
- No production stage currently observes `EnumerationCompleted`.

Those behaviors are safe enough during startup-only publication, but they are
not sufficient for an in-game library reload.

## Goals

- Allow players to add, remove, and reorder song-library roots from Config.
- Persist one or more ordered roots while migrating legacy `DTXPath` safely.
- Perform at most one full reload for a changed ordered root list.
- Keep the old published hierarchy until a replacement commits and publishes.
- Preserve bookmarks, scores, variants, history, and other user data belonging
  to removed or temporarily unavailable roots.
- Keep Config-initiated NX import and song-folder reload mutually exclusive.
- Make publication safe while Song Select and other consumers are active.
- Keep configured, available, and active roots distinct.
- Keep every implementation slice within roughly three engineer days.

## Non-goals

- DTXCreator changes.
- Parser, `set.def`, or `box.def` semantic changes.
- Song database schema changes.
- Filesystem watchers or automatic mounting detection.
- Scanning after every draft edit.
- A database-only reorder fast path.
- Synthetic top-level root boxes.
- Merging same-named folders or songs across roots.
- Deleting retained rows for removed roots.
- Resolving symlinks, junctions, aliases, bind mounts, or physical filesystem
  targets. Textually different paths may still reach the same physical folder.
- Linux-specific folder-picker UI.
- Replacing the initial macOS AppleScript adapter with `NSOpenPanel`.

## Root State Model

The design distinguishes three ordered snapshots:

- **Configured roots:** normalized paths persisted in `Config.ini`.
- **Available roots:** configured roots that appear accessible at a particular
  check.
- **Active roots:** roots represented by the currently published hierarchy.

Configured and active roots may differ. For example, Config may persist a new
root list and then encounter a pre-commit reload failure. Runtime surfaces that
represent the published library must continue using the old active snapshot.

## Chosen Architecture

1. `ConfigData` gains ordered `SongRoots`; `DTXPath` becomes a compatibility
   mirror of the first root.
2. An internal `SongRootPolicy` owns normalization, duplicate comparison,
   overlap detection, and availability classification.
3. `SongManager` exposes copied, versioned library snapshots. It never hands out
   a live view of the mutable root list.
4. `SongFolderPanel` edits an isolated draft and delegates Apply to one
   Config-owned operation.
5. That Config operation owns the entire changed-root sequence: lease
   acquisition, persistence, reload task construction, terminal observation,
   and lease release.
6. HPA-192 remains the importer and commit/publication authority.
7. Song Select observes publication versions and reconciles on the update
   thread.

## Alternatives Considered

### Save and require restart

This is the temporary Slice 2 behavior but not the completed feature. It leaves
the active library stale and gives weak confirmation.

### Re-enter StartupStage

Rejected. Startup owns boot-only phases, activation generations, timing traces,
and exactly-once telemetry.

### Reorder from SQLite without parsing

Deferred. Any changed ordered list performs one full scan in HPA-191. This also
observes filesystem changes made while Config was open.

### Transfer a lease through `Saved` event handling

Rejected after review. The previous draft acquired a lease in the panel commit
delegate, persisted roots, raised `Saved`, and transferred release ownership to
a reload continuation constructed by another handler. Although `try/finally`
and failure injection could make that correct, the ownership boundary was
unnecessarily subtle.

The final design keeps acquire, persist, reload construction, continuation
registration, and release ownership in one Config method. `Saved` remains a UI
lifecycle notification only.

### Collapse orchestration and persistence result types

Rejected. `Busy` belongs to Config operation coordination and must not leak into
`IConfigManager`. The types remain separate but are composed rather than
repeating independent logic.

## Configuration Contract

### Runtime model

`ConfigData` gains:

```csharp
public List<string> SongRoots { get; } = new();
```

The collection is get-only. Load, reset, and successful updates clear and refill
it rather than replacing the list reference. Consumers receive copied immutable
snapshots and must never retain or mutate the live list.

`DTXPath` remains for serialization and downgrade compatibility only:

- It always mirrors the first configured root after load/reset/update.
- Runtime behavior consumes configured or active root snapshots instead.
- Direct mutation is not a supported update mechanism.

At least one configured root is required, but it may currently be unavailable.

### Persisted format

```ini
[System]
DTXPath=C:\DTX\Main
SongRoot.0=C:\DTX\Main
SongRoot.1=D:\Community Charts
```

The existing INI parser is section-agnostic: section headers are presentation
only and keys are globally parsed. HPA-191 must not add section-scoped parsing
for `SongRoot.*` unless the entire configuration parser is redesigned in a
separate issue.

Rules:

1. `SongRoot.<index>` uses a non-negative decimal index.
2. Entries load in ascending numeric order; gaps are allowed.
3. Blank values, malformed suffixes, and negative indexes are ignored with a
   warning.
4. Duplicate indexes use the last parsed value and emit a warning.
5. Values may contain `=` because parsing splits only on the first `=`.
6. Save rewrites indexes densely from zero.
7. Indexed roots are authoritative when at least one structurally valid entry
   exists.
8. Otherwise legacy `DTXPath` is migrated into one root.
9. If neither representation yields a valid path, restore the managed default.
10. `DTXPath` is serialized as the first normalized root.

At the beginning of every `LoadConfig`, clear `SongRoots` before parsing, just as
`MidiVelocityThresholds` is cleared today. Loading twice into the same manager
must not accumulate prior roots.

A successful migration is persisted immediately through the existing atomic
temp-file replacement.

### Legacy default migration

The known legacy default forms of `Songs` continue migrating to the managed
`DTXFiles` directory. Indexed custom paths ending in `Songs` are not remapped.

### Deliberate removal of custom-root auto-creation

Slice 1a deliberately removes the current unconditional
`EnsureDirectorySafe(Config.DTXPath)` behavior.

After HPA-191:

- Only `AppPaths.GetDefaultSongsPath()` may be created automatically when it is
  selected or restored as fallback.
- Missing custom roots remain configured but unavailable.
- Load, migration, Apply, startup, and availability checks never create custom
  folders.
- Empty folders previously created by old builds are not deleted.

This is an intentional product behavior change.

### Downgrade mirror caveat

`DTXPath` is best-effort compatibility. If its first root is an unavailable
removable/network path and the user runs an older build, that build may recreate
an empty directory through its legacy unconditional behavior. HPA-191 cannot
control behavior after downgrade.

## SongRootPolicy

### Testable comparer selection

`LegacyAliasComparer` is resolved from the running OS, so both comparison
branches cannot be covered reliably by platform CI alone. `SongRootPolicy`
therefore has an explicit test seam:

```csharp
internal sealed class SongRootPolicy
{
    internal SongRootPolicy(StringComparer rootComparer);

    internal static StringComparer CreateRootComparer(bool ignoreCase) =>
        ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static SongRootPolicy PlatformDefault { get; }
}
```

Production creates `PlatformDefault` using Windows/macOS ignore-case behavior.
Tests instantiate both comparer modes on every CI platform. The policy is
internal; no public API exposure is required.

### Normalization

For each input:

1. Reject blank values.
2. Expand supported home-relative forms through `AppPaths`.
3. Resolve legacy relative values against the existing app-data base.
4. Convert to a full absolute path.
5. Normalize separators and trim ending separators through
   `SongPathIdentity.Normalize`.
6. Compare roots with the policy comparer.
7. Persist the normalized absolute value.

### Duplicate policy

- Windows/macOS mode: case-insensitive.
- Linux/ordinal mode: case-sensitive.
- Preserve first occurrence and authored order.

This root policy does not change ordinal chart identity.

### Overlap policy

Root-overlap validation must not call
`SongPathIdentity.IsUnderNormalizedRoot` or rely on `Path.GetRelativePath`.
Those APIs follow platform path semantics that disagree with the chosen
ignore-case macOS root policy.

Implement dedicated segment-wise comparison:

1. Parse the normalized root prefix/volume/share and remaining path segments.
2. Compare root prefixes and each segment with `RootComparer`.
3. Equality is a duplicate.
4. If every segment of the shorter root matches the corresponding segment of
   the longer root, the shorter root is an ancestor and the pair is rejected.
5. Perform the check symmetrically.

Tests cover case-differing parent/child paths in ignore-case mode, including the
macOS policy case `/Users/me/Songs` and `/Users/me/SONGS/Extra`.

Physical aliases remain outside scope.

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
    IReadOnlyList<SongRootDiagnostic> Diagnostics);
```

The method:

1. Validates/canonicalizes the complete list.
2. Returns `Unchanged` without writing when ordered roots are equal.
3. Captures prior `SongRoots` and `DTXPath` snapshots.
4. Clears/refills `SongRoots` and updates `DTXPath` in memory.
5. Writes the complete configuration atomically.
6. Restores prior in-memory state if persistence fails.
7. Raises `SongRootsChanged` only after persistence succeeds.
8. Returns copied immutable snapshots.

After a successful explicit save, clear `_pendingSavePath` only when it denotes
the same normalized config file that was just written. A pending save for a
different path must not be silently discarded.

Event subscriber exceptions are isolated and cannot roll back persisted state.

## Safe SongManager Snapshots

### Root list

The existing `RootSongs` getter must no longer return `_rootSongs.AsReadOnly()`.
That wrapper is live and can throw `InvalidOperationException` when
`PublishEnumeration` mutates the backing list during external enumeration.

Use one of these equivalent contracts:

```csharp
public IReadOnlyList<SongListNode> RootSongs
{
    get
    {
        lock (_lockObject)
            return _rootSongs.ToArray();
    }
}
```

or a mandatory `GetRootSongsSnapshot()` API. Existing runtime consumers are
migrated to the copied snapshot contract.

### Composite publication snapshot

Add a coherent versioned snapshot:

```csharp
public sealed record SongLibrarySnapshot(
    long Version,
    IReadOnlyList<SongListNode> RootSongs,
    IReadOnlyList<string> ActiveRoots);

public SongLibrarySnapshot GetPublishedLibrarySnapshot();
```

The method copies root nodes and active roots under the same `_lockObject` and
returns one publication version. Consumers must not combine separately-read
roots and active paths.

### Publication event

Publish a notification only after the new hierarchy and active roots are both
installed:

```csharp
public event EventHandler<SongLibraryPublishedEventArgs>? SongLibraryPublished;
```

The event carries the monotonically increasing version and copied active-root
snapshot. Handlers must not mutate game UI directly.

`EnumerationCompleted` may remain for compatibility, but HPA-191 uses the
explicit publication event.

### Publication implementation

Under `_lockObject`:

1. Replace root contents.
2. Replace `_currentSearchPaths` with a copied active-root array.
3. Update compatibility counts.
4. Increment publication version.
5. Capture event data.

Invoke subscribers after releasing the lock, with per-subscriber exception
isolation.

### Empty publication

Add a dedicated `PublishEmptyLibrary()` operation that:

- clears root nodes;
- assigns `_currentSearchPaths = Array.Empty<string>()`;
- resets relevant counts;
- increments publication version; and
- raises the same publication event.

Also remove or revise `SetCurrentSearchPaths`'s empty-array early return so an
explicit empty snapshot cannot leave stale roots. Empty publication must not
call database import or stale cleanup.

### Concurrency coverage

Tests must prove that:

- a filter projection can iterate a copied root snapshot while another thread
  publishes a replacement;
- bookmark reconciliation can iterate a copied snapshot during publication;
- no collection-modified exception occurs;
- a snapshot remains stable after later publication; and
- roots and active paths come from the same version.

## Defensive Root Alignment

`SetCurrentSearchPaths`, enumeration-batch root creation, and every other
root-deduplication boundary use `SongRootPolicy.RootComparer`.

This deliberately changes startup cache/database behavior as well as fresh
enumeration. Direct tests cover:

- `SetCurrentSearchPaths` first-occurrence/order behavior;
- immutable copied current-path snapshots;
- `LoadScoreCacheAsync`;
- `BuildSongListFromDatabasePublicAsync`;
- `NeedsEnumerationAsync`;
- fresh enumeration; and
- both comparer modes through the injected policy seam.

## Root Availability and Import Authority

A shallow availability check remains useful only for early UX and the
all-unavailable decision. It is not authoritative because filesystem state can
change immediately afterward.

### Preflight responsibilities

- Detect obvious all-unavailable cases without starting a costly reload.
- Give immediate Config/startup diagnostics.
- Never create directories.

### Importer responsibilities

When preflight finds at least one apparently available root, pass the complete
configured snapshot to HPA-192. The enumeration batch is authoritative for:

- normalized active roots;
- missing/invalid/inaccessible root diagnostics;
- the final unavailable-root count; and
- what was actually scanned.

User-facing unavailable counts after an attempted scan come from
`batch.Errors` entries where `IsRootFailure`, not from preflight results.

After the batch is built, if `ActiveRoots` is empty:

- do not begin database import;
- do not publish the empty batch through the normal reload path;
- return a structured `NoAvailableRoots` result with batch diagnostics.

Config retains the old hierarchy. Startup explicitly calls
`PublishEmptyLibrary()` because clean startup has no prior active library to
retain.

This guard closes the TOCTOU case where roots disappear after preflight but
before traversal.

## Config UI

### Menu entry

Replace **DTX Folder** with **Song Folders**. Display `1 folder` or `<n>
folders`. The description says Apply saves the ordered list and reloads once.

### Overlay abstraction

Introduce `IConfigOverlayPanel` containing the common lifecycle currently on
`IKeyAssignPanel`. `IKeyAssignPanel` inherits it, preserving
`Saved`-before-`Closed` behavior.

### SongFolderPanel

The panel receives:

- an immutable configured-root snapshot;
- `IFolderPickerService`;
- validation/availability formatting; and
- a Config-owned apply delegate.

It owns a private draft. Add/remove/reorder/Cancel/Back do not mutate config.

Supported actions:

- **Add Folder**
- **Remove** while at least one draft root remains
- **Move Up** / **Move Down**
- **Apply**
- **Cancel**

Structural errors keep the panel open. Availability warnings do not block
Apply.

## Folder Picker

```csharp
public interface IFolderPickerService
{
    Task<FolderPickerResult> PickFolderAsync(
        string? initialDirectory,
        CancellationToken cancellationToken);
}
```

The result distinguishes Selected, Cancelled, Unavailable, and Failed. A stale
picker completion cannot update an inactive/reactivated panel.

### Windows

Use `FolderBrowserDialog` behind a platform implementation that owns STA
dispatch. Shared update code does not assume STA.

### macOS

Use `osascript` Standard Additions `choose folder`, with a prompt and optional
existing default location. Build arguments with `ProcessStartInfo.ArgumentList`,
await asynchronously, distinguish normal cancellation, and log stderr for
failures.

The first invocation may display system consent/privacy UI depending on the
host process and selected location. Do not promise a particular System Settings
pane. Authorization denial maps to Failed.

## Config Song Operation Coordinator

Introduce one Config-scoped coordinator/lease:

```text
None | NxScoreImport | SongFolderReload
```

The coordinator provides atomic acquire and exactly-once release. Future Config
operations that mutate/rebuild the library use the same coordinator.

### Apply ownership

The panel calls one Config-owned method, conceptually:

```csharp
SongFolderApplyResult ApplySongRoots(IReadOnlyList<string> draft);
```

This method owns the full synchronous handoff:

1. Canonicalize and compare the draft.
2. Return Unchanged without acquiring or writing.
3. Acquire `SongFolderReload`; return Busy without persistence if unavailable.
4. Call `SetSongRoots`.
5. On validation/persistence failure, return failure and release in `finally`.
6. Construct the reload task.
7. Register terminal observation/release continuation.
8. Transfer lease ownership to that continuation only after registration
   succeeds.
9. Return Updated/ReloadStarted.
10. Release synchronously in `finally` on every path where ownership was not
    transferred.

The panel closes and raises `Saved` only after Updated or Unchanged. `Saved` is
not responsible for constructing the reload task or transferring the lease.

`SongFolderApplyResult` composes `SongRootUpdateResult` and adds orchestration
states such as Busy or ReloadStartFailed; it does not duplicate persistence
logic or add Busy to `IConfigManager`.

### NX import migration

Slice 3a must migrate `StartNxScoreImport` completely onto the same coordinator:

- replace `_importRunning` check-then-set with atomic lease acquisition;
- use activation-generation fencing;
- marshal progress/status updates through an update-thread queue or pending
  state, never write UI-visible status directly from the worker;
- observe the task and faults;
- dispose its CTS only after terminal completion;
- release the lease exactly once in the terminal continuation; and
- cancel without synchronously blocking on deactivation.

Merely checking the shared slot while retaining the old fire-and-forget
lifecycle is insufficient.

## Live Reload

`ISongLibraryReloadService` adapts Config to HPA-192. It:

- accepts an immutable configured snapshot;
- performs preflight only for early all-unavailable detection;
- invokes the existing enumeration/import path once for a changed list;
- maps lower-level enumeration-busy exceptions to Busy;
- reports progress through thread-safe state;
- returns success, NoAvailableRoots, Busy, cancellation, pre-commit failure, or
  post-commit partial failure.

### Commit/publication terminal section

Cancellation is honored before database commit. After commit succeeds, no
cancellation check may occur before hierarchy finalization/publication. A late
request is reported as success after publication.

Unexpected finalization/publication failure after commit reports a distinct
partial-success/restart-required result and never claims rollback.

### Config deactivation

Deactivation:

- increments activation generation;
- requests cancellation;
- attaches an execute-synchronously observation continuation;
- observes faults;
- disposes CTS after termination;
- releases lease in terminal continuation; and
- suppresses stale UI writes.

It never waits synchronously for filesystem or SQLite work on the game thread.

## Startup Integration

Startup reads an immutable configured-root snapshot.

- Preflight detects an obvious all-unavailable case.
- Cache/enumeration decisions use the aligned root policy.
- If enumeration produces no active roots, no import/publication occurs through
  the normal path.
- Startup then calls `PublishEmptyLibrary()` without database cleanup.
- Title remains reachable and SQLite rows remain untouched.

If at least one root is active, startup uses the normal cache/enumeration path
and publishes the resulting versioned snapshot.

## Runtime Consumer Audit

Slice 1b must grep every production `DTXPath` read and classify it. No runtime
consumer remains silently first-root-only.

Known production consumers:

- Startup configuration.
- Config display.
- Song Select preview fallback.
- E2E/runtime fixture setup.

Add a source architecture test or explicit allowlist that permits `DTXPath` only
inside compatibility serialization/migration code.

## Preview Resolution

The normalized absolute chart path remains authoritative. For legacy nodes with
relative directories, `PreviewImagePanel` receives the active-root snapshot and
tries roots in order. It never uses newly configured roots when an old
hierarchy remains active.

## Active Song Select Publication Handling

Publication while Song Select is deactivated needs no extra mechanism because
normal activation rebuilds its list. The additional contract applies only while
Song Select is already active.

On activation, subscribe to `SongLibraryPublished`; on deactivation, unsubscribe.
The handler only records the newest pending version.

On the update thread:

1. Fetch one `SongLibrarySnapshot`.
2. Ignore stale versions.
3. Replace the local root list and active-root fallback snapshot.
4. Restore navigation by stable folder path/database identity when possible;
   otherwise return to root.
5. Restore selected chart by stable chart/database identity when possible;
   otherwise choose the first valid item.
6. Stop preview and clear status/history when the old selection disappeared.
7. Rebuild All Songs, filters, Bookmarks, Recent, breadcrumb, preview roots, and
   empty-state data from the same publication version.
8. Never mutate UI collections from the publication callback thread.

Multiple fast publications collapse to the newest version.

All direct `RootSongs` consumers, including filter projection and bookmark
reconciliation, now iterate copied snapshots. Tests publish concurrently while
those operations enumerate.

## Active Views and Retained Data

- All Songs uses the active published hierarchy.
- Bookmarks and Recent filter database-backed results to active roots.
- A failed reload leaves old active entries visible.
- Successful removal hides off-root entries but retains rows.
- Successful re-add restores them with user data.

## Empty Library UX

Song Select distinguishes:

- no active/available roots: **No song folders are currently available. Open
  Config to reconnect or change them.**
- active roots with no supported charts: **No songs were found in the active
  song folders.**

## Logging

Structured logs include:

- legacy migration and indexed parsing warnings;
- deliberate suppression of custom-root creation;
- root-policy mode and dropped duplicates/overlaps;
- preflight warnings and authoritative batch root failures;
- operation lease acquisition/release failures;
- reload/NX import terminal outcomes;
- publication versions; and
- Song Select reconciliation outcomes.

Do not add per-file success logs beyond existing HPA-192 progress.

## Testing Strategy

### Slice 1a configuration tests

- default/mirror behavior;
- indexed round-trip and dense rewrite;
- section-agnostic parsing remains unchanged;
- second LoadConfig clears roots before parse;
- legacy migration;
- malformed/duplicate indexes;
- missing custom paths are never created;
- managed default creation;
- atomic persistence rollback;
- deferred-save marker clears only for the matching path; and
- immutable event snapshots.

### Slice 1b root and SongManager tests

- both comparer modes run on every platform through the injected seam;
- first-occurrence/order deduplication;
- ignore-case and ordinal overlap matrices;
- macOS-style case-differing parent/child rejection;
- no use of `Path.GetRelativePath` for overlap policy;
- SetCurrentSearchPaths empty input clears active roots;
- PublishEmptyLibrary installs one coherent empty version;
- RootSongs returns a stable copied snapshot;
- filter projection remains safe during concurrent publication;
- bookmark reconciliation remains safe during concurrent publication;
- roots and active paths share one version;
- startup cache/database callers use the same policy;
- importer short-circuits before persistence/publication when batch active roots
  are empty; and
- authoritative unavailable counts come from root-failure batch errors.

### Slice 2 UI/picker tests

- menu and singular/plural summaries;
- isolated panel draft;
- add/remove/reorder/apply/cancel/back;
- structural errors vs availability warnings;
- async picker cancellation/failure;
- stale picker generation;
- Windows STA boundary;
- macOS cancellation vs authorization failure; and
- persisted roots are consumed on restart.

### Slice 3a operation/reload tests

- unchanged Apply does not acquire/write/scan;
- Busy Apply does not persist;
- one full scan for reorder-only change;
- lease release on validation/persistence/task-construction/continuation errors;
- lower-level enumeration Busy mapping;
- NoAvailableRoots retains old Config hierarchy;
- batch root failures drive final warning counts;
- pre-commit cancellation preserves old hierarchy;
- post-commit cancellation cannot suppress publication;
- NX import and reload cannot overlap;
- NX import progress never writes Config UI from a worker;
- deactivation cancellation is non-blocking; and
- both task lifecycles dispose CTS and release lease exactly once.

### Slice 3b Song Select/integration tests

- publication callback only records a version;
- update-thread reconciliation uses one coherent snapshot;
- concurrent filter iteration cannot observe collection mutation;
- stable selection/navigation restoration;
- removed selection stops preview and clears panels;
- publication inside a removed box returns safely to root;
- Bookmarks/Recent refresh against active roots;
- multiple publications apply newest only;
- notifications after deactivation are ignored;
- root N preview fallback;
- removed-root data remains stored and reappears after re-add;
- empty states are distinct; and
- Windows/macOS builds remain isolated to their picker implementation.

## Implementation Slices

The slices are hard dependencies and land in order.

### Slice 1a: Configuration model and persistence

- Add `SongRoots`, indexed format, mirror, migration, and load clearing.
- Add persistence result/event snapshots.
- Remove custom-root auto-creation.
- Preserve section-agnostic parsing.
- Correct pending-save clearing.
- Add focused config tests.

### Slice 1b: Root policy, SongManager snapshots, and startup consumers

- Add testable `SongRootPolicy`.
- Add segment-wise overlap detection.
- Align all SongManager root deduplication.
- Replace live RootSongs view with copied/versioned snapshots.
- Add empty publication and remove empty-path early-return behavior.
- Add authoritative no-active-root enumeration result.
- Update startup, preview fallback, and runtime consumer audit.
- Add concurrency and startup/cache-path tests.

### Slice 2: Cross-platform SongFolderPanel

- Add `IConfigOverlayPanel`.
- Add asynchronous Windows/macOS/fake pickers.
- Implement draft editing and Config-owned Apply delegate.
- Use restart-required status until live reload lands.
- Add panel and platform tests.

### Slice 3a: Config operation coordinator and live reload

- Add atomic scoped lease coordinator.
- Implement single-method Apply ownership.
- Add reload service and HPA-192 adaptation.
- Migrate NX import fully onto the same lifecycle.
- Add non-blocking cancellation/observation and progress marshalling.
- Add operation/reload tests.

### Slice 3b: Active Song Select reconciliation and retained views

- Add publication subscription/version handling.
- Reconcile hierarchy/navigation/selection on update thread.
- Refresh filters, Bookmarks, Recent, previews, and empty states coherently.
- Add concurrent-publication and retained-data integration tests.

Each slice is intended to fit within three engineer days. If Slice 3b expands
beyond that during planning, split retained-data tab refresh from core hierarchy
reconciliation rather than broadening one task.

## Acceptance Criteria

- Legacy one-root configuration migrates without library loss.
- Indexed roots persist in order and load repeatedly without accumulation.
- INI parsing remains section-agnostic.
- `DTXPath` remains a compatibility mirror only.
- Missing custom roots are never auto-created.
- Root comparison supports testable ignore-case and ordinal modes.
- Duplicate and comparer-aware parent/child roots cannot be applied.
- Physical-target deduplication remains out of scope.
- RootSongs never exposes a live mutable-list wrapper.
- Concurrent publication cannot break filter/bookmark enumeration.
- Empty publication clears hierarchy and active roots in one version.
- Startup and every SongManager root path use the same policy.
- Changed root lists perform at most one full scan; unchanged lists perform none.
- Config root-failure counts reflect what the enumeration batch actually saw.
- A no-active-root batch never imports or publishes through normal reload.
- Config retains the old hierarchy when no root is active.
- Startup publishes a deliberate empty library without cleanup when no root is
  active.
- Busy Apply does not persist.
- NX import and reload share one throw-safe lifecycle and cannot overlap.
- Config deactivation never blocks the update thread.
- Commit-to-publication is non-cancellable.
- Active Song Select reconciles a new publication on the update thread.
- Bookmarks/Recent show only active-root rows while retained rows remain stored.
- Preview fallback works for every active root.
- Re-adding a root restores retained user data.
- No DTXCreator, parser, schema, or synthetic-root-navigation change is included.
