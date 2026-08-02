# HPA-191 Configurable Multiple Song Folders Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players persist, edit, and live-reload an ordered list of song-library roots while preserving atomic HPA-192 publication, retained user data, and a coherent active Song Select stage.

**Architecture:** Configuration owns the ordered `SongRoots` list and keeps `DTXPath` only as a compatibility mirror. `SongManager` owns comparer-aware root identity and publishes copied, versioned library snapshots rather than live collection wrappers. Config uses one operation coordinator for NX score import and song-root reload; Song Select consumes publication notifications only on the update thread.

**Tech Stack:** .NET 8, C# 12, MonoGame 3.8, Entity Framework Core SQLite, xUnit, WinForms on Windows, `osascript` Standard Additions on macOS.

## Global Constraints

- DTXCreator is out of scope.
- Do not change chart parsing, `set.def`, `box.def`, database schema, or synthetic root navigation.
- `SongRoots` is the configured-root source of truth; `DTXPath` is serialization/migration compatibility only.
- Keep Config.ini parsing section-agnostic and split each assignment on the first `=` only.
- Create only `AppPaths.GetDefaultSongsPath()` automatically; never create a missing custom root.
- Root comparison is ordinal-ignore-case on Windows/macOS and ordinal on Linux/other platforms, through a testable comparer seam.
- `SongPathIdentity.CanonicalComparer` remains ordinal for chart identity.
- Parent/child overlap uses segment-wise comparison through the root comparer; do not use `Path.GetRelativePath` for this policy.
- Physical-target deduplication for symlinks, junctions, aliases, bind mounts, or inodes remains out of scope.
- Never expose `_rootSongs` or `_currentSearchPaths` through a live collection wrapper.
- Publish hierarchy, active roots, counts, and publication version as one coherent snapshot.
- Startup may explicitly publish an empty library without database cleanup; a normal live reload with zero active roots must not import or publish.
- Any changed ordered root list, including reorder-only changes, performs one full HPA-192 scan; an unchanged list performs none.
- HPA-192 enumeration-batch root failures are authoritative for roots actually rejected at scan time.
- Database commit through hierarchy publication is a non-cancellable terminal section.
- Config NX score import and song-root reload share one exclusive, throw-safe operation lifecycle.
- Worker threads may report progress into a queue; only the game update thread mutates Config or Song Select UI state.
- Runtime `Config.DTXPath` reads are forbidden outside an explicit serialization/migration compatibility allowlist.
- Platform picker implementation files must not compile into the opposite platform project.
- The slices land in order: **1a → 1b → 2 → 3a → 3b**.
- Each slice must remain reviewable within three engineer days. Split a slice rather than broadening it.

---

## File Structure

### New shared production files

- `DTXMania.Game/Lib/Config/SongRootConfigModels.cs` — persistence statuses, diagnostics, and immutable change-event snapshots.
- `DTXMania.Game/Lib/Song/SongRootPolicy.cs` — normalization, testable comparer construction, deduplication, overlap validation, and availability probing.
- `DTXMania.Game/Lib/Song/SongLibrarySnapshot.cs` — immutable publication snapshot and event arguments.
- `DTXMania.Game/Lib/Stage/Config/IConfigOverlayPanel.cs` — common Config overlay lifecycle.
- `DTXMania.Game/Lib/Stage/Config/FolderPickerModels.cs` — picker interface/result contract.
- `DTXMania.Game/Lib/Stage/Config/SongFolderPanel.cs` — isolated draft-list UI.
- `DTXMania.Game/Lib/Stage/Config/ConfigSongOperationCoordinator.cs` — one exclusive Config song-operation lease.
- `DTXMania.Game/Lib/Stage/Config/SongLibraryReloadModels.cs` — orchestration statuses and progress snapshots.
- `DTXMania.Game/Lib/Stage/Config/SongLibraryReloadService.cs` — HPA-192 adapter and result mapping.

### New platform files

- `DTXMania.Game/Platform/Windows/WindowsFolderPickerService.cs`
- `DTXMania.Game/Platform/Mac/MacFolderPickerService.cs`
- `DTXMania.Game/Platform/FolderPickerServiceFactory.Windows.cs`
- `DTXMania.Game/Platform/FolderPickerServiceFactory.Mac.cs`

The Windows and macOS projects must exclude the opposite platform directory/factory file through explicit `<Compile Remove=...>` entries.

### Primary modified production files

- `DTXMania.Game/Lib/Config/ConfigData.cs`
- `DTXMania.Game/Lib/Config/IConfigManager.cs`
- `DTXMania.Game/Lib/Config/ConfigManager.cs`
- `DTXMania.Game/Lib/Song/SongImportModels.cs`
- `DTXMania.Game/Lib/Song/SongManager.cs`
- `DTXMania.Game/Lib/Stage/StartupStage.cs`
- `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- `DTXMania.Game/Lib/Stage/KeyAssign/IKeyAssignPanel.cs`
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- `DTXMania.Game/Lib/Song/Components/PreviewImagePanel.cs`
- `DTXMania.Game/DTXMania.Game.Windows.csproj`
- `DTXMania.Game/DTXMania.Game.Mac.csproj`

### New focused tests

- `DTXMania.Test/Config/SongRootConfigTests.cs`
- `DTXMania.Test/Config/DTXPathCompatibilityArchitectureTests.cs`
- `DTXMania.Test/Song/SongRootPolicyTests.cs`
- `DTXMania.Test/Song/SongManagerLibrarySnapshotTests.cs`
- `DTXMania.Test/Config/SongFolderPanelTests.cs`
- `DTXMania.Test/Config/FolderPickerContractTests.cs`
- `DTXMania.Test/Config/ConfigSongOperationCoordinatorTests.cs`
- `DTXMania.Test/Config/SongLibraryReloadServiceTests.cs`
- `DTXMania.Test/Stage/SongSelectionPublicationTests.cs`

Existing tests remain the regression home where the behavior already belongs, especially `ConfigManagerTests`, `StartupStageLogicTests`, `SongManagerBulkEnumerationTests`, `ConfigStageNxImportTests`, `PreviewImagePanelTests`, and `SongSelectionStageLogicTests`.

---

## Slice 1a — Configuration Model and Persistence

### Task 1A: Persist Ordered Song Roots Without Runtime Consumer Changes

**Files:**
- Create: `DTXMania.Game/Lib/Config/SongRootConfigModels.cs`
- Create: `DTXMania.Test/Config/SongRootConfigTests.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Test/Config/ConfigDataTests.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerTests.cs`

**Interfaces:**

```csharp
public enum SongRootUpdateStatus
{
    Updated,
    Unchanged,
    ValidationFailed,
    PersistenceFailed
}

public sealed record SongRootDiagnostic(
    string Path,
    string Message,
    bool IsWarning);

public sealed record SongRootUpdateResult(
    SongRootUpdateStatus Status,
    IReadOnlyList<string> CanonicalRoots,
    IReadOnlyList<SongRootDiagnostic> Diagnostics);

public sealed class SongRootsChangedEventArgs : EventArgs
{
    public IReadOnlyList<string> OldRoots { get; }
    public IReadOnlyList<string> NewRoots { get; }
}
```

This slice defines the models and file format but does not add Config UI or live reload. The final comparer-aware `SetSongRoots` implementation is completed in Slice 1b after `SongRootPolicy` exists.

- [ ] **Step 1: Add red tests for default, repeated load, and indexed parsing**

Add tests named:

```text
DefaultConfig_ShouldContainManagedSongRootAndMirror
LoadConfig_ShouldClearSongRootsBeforeSecondParse
LoadConfig_ShouldReadIndexedRootsInNumericOrder
LoadConfig_ShouldUseLastDuplicateIndexAndWarn
LoadConfig_ShouldPreferIndexedRootsOverLegacyDTXPath
LoadConfig_ShouldMigrateLegacyDTXPathAndPersistIndexedRoot
SaveConfig_ShouldWriteDenseIndexesAndFirstRootMirror
```

Use temporary Config.ini files. Include an INI with unrelated section headers and prove `SongRoot.*` remains global because the current parser does not track sections.

- [ ] **Step 2: Run the focused red tests**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongRootConfigTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigDataTests"
```

Expected: failures because `SongRoots` and indexed serialization do not exist.

- [ ] **Step 3: Add the ConfigData and load/save model**

Add to `ConfigData`:

```csharp
public List<string> SongRoots { get; } = new();
```

At the start of every `LoadConfig`, call `Config.SongRoots.Clear()` beside the existing MIDI-threshold clearing. Parse `SongRoot.<non-negative integer>` into a temporary index/value map, then finalize the list after all lines are read. Keep `[Section]` lines ignored exactly as today.

Finalize with this precedence:

1. Accepted indexed roots in ascending index order.
2. Legacy `DTXPath` migrated into one root.
3. Managed default root.

Save dense `SongRoot.0..N` entries and write `DTXPath` as the first root.

- [ ] **Step 4: Remove unconditional custom-root creation**

Replace the current `EnsureDirectorySafe(Config.DTXPath)` behavior with managed-default-only creation. Add tests proving:

- missing custom roots remain strings in Config;
- no custom directory is created during load, migration, or save;
- the managed default is created when selected as fallback;
- directories created by older versions are not deleted.

- [ ] **Step 5: Correct pending-save path clearing**

When an explicit full config save succeeds, clear `_pendingSavePath` only when its normalized path equals the file just written. A save to another path must leave the pending marker intact.

Add tests:

```text
SaveConfig_ShouldClearMatchingPendingPath
SaveConfig_ShouldRetainDifferentPendingPath
FlushPendingSave_ShouldRetryAfterFailure
```

- [ ] **Step 6: Add immutable persistence/event model tests**

Construct `SongRootsChangedEventArgs` from mutable source lists, mutate the sources, and prove the event snapshots remain unchanged. Do not raise the event from load/reset.

- [ ] **Step 7: Run Slice 1a gate**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongRootConfigTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigDataTests"

dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug
```

Expected: all focused tests pass and the macOS game project builds.

- [ ] **Step 8: Commit Slice 1a**

```bash
git add DTXMania.Game/Lib/Config \
  DTXMania.Test/Config/ConfigDataTests.cs \
  DTXMania.Test/Config/ConfigManagerTests.cs \
  DTXMania.Test/Config/SongRootConfigTests.cs
git commit -m "feat: persist ordered song roots"
```

**Review gate:** verify the serialized format, repeated-load clearing, section-agnostic behavior, custom-directory behavior removal, and pending-save path handling before starting Slice 1b.

---

## Slice 1b — Root Policy, SongManager Snapshots, and Startup Consumers

### Task 1B: Centralize Root Identity and Publish Safe Library Snapshots

**Files:**
- Create: `DTXMania.Game/Lib/Song/SongRootPolicy.cs`
- Create: `DTXMania.Game/Lib/Song/SongLibrarySnapshot.cs`
- Create: `DTXMania.Test/Song/SongRootPolicyTests.cs`
- Create: `DTXMania.Test/Song/SongManagerLibrarySnapshotTests.cs`
- Create: `DTXMania.Test/Config/DTXPathCompatibilityArchitectureTests.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Song/SongImportModels.cs`
- Modify: `DTXMania.Game/Lib/Song/SongManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/StartupStage.cs`
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/PreviewImagePanel.cs`
- Modify: relevant existing SongManager, startup, and preview tests.

**Interfaces:**

```csharp
internal sealed class SongRootPolicy
{
    internal SongRootPolicy(StringComparer comparer);
    internal static SongRootPolicy ForCurrentPlatform();
    internal static StringComparer CreateComparer(bool ignoreCase);

    internal SongRootValidationResult Validate(IReadOnlyList<string> roots);
    internal bool IsAncestor(string parent, string child);
    internal SongRootAvailability Probe(string normalizedRoot);
}

public sealed record SongLibrarySnapshot(
    long Version,
    IReadOnlyList<SongListNode> RootSongs,
    IReadOnlyList<string> ActiveRoots,
    int EnumeratedFileCount,
    int DiscoveredScoreCount);

public sealed class SongLibraryPublishedEventArgs : EventArgs
{
    public SongLibrarySnapshot Snapshot { get; }
}
```

`SongManager.GetLibrarySnapshot()` returns copied root and root-path arrays captured under `_lockObject`. `RootSongs` may remain as a compatibility property only if it returns the copied snapshot list, never `_rootSongs.AsReadOnly()`.

- [ ] **Step 1: Write the comparer and overlap red tests**

Add theory coverage that executes both policies on every host:

```csharp
[Theory]
[InlineData(true, true)]
[InlineData(false, false)]
public void DuplicatePolicy_ShouldFollowInjectedCaseMode(
    bool ignoreCase,
    bool expectedDuplicate)
```

Add explicit parent/child matrices including:

```text
/Users/me/Songs + /Users/me/SONGS/Extra
C:\Songs + c:\songs\Pack
/songs + /Songs/Pack under ordinal mode
```

Assert overlap compares path segments through the injected comparer and does not call `Path.GetRelativePath`.

- [ ] **Step 2: Implement `SongRootPolicy`**

The policy must:

1. Normalize absolute paths with `SongPathIdentity.Normalize`.
2. Preserve first occurrence and configured order.
3. Detect duplicate roots through the injected comparer.
4. Split normalized roots into root/segment components and compare each segment through the same comparer.
5. Reject same-path and parent/child overlap symmetrically.
6. Probe existence/access without creating directories.

Use `CreateComparer(true)` for ignore-case tests and `CreateComparer(false)` for ordinal tests. `ForCurrentPlatform()` selects the production mode.

- [ ] **Step 3: Complete the typed Config setter**

Add to `IConfigManager`:

```csharp
SongRootUpdateResult SetSongRoots(
    string configFilePath,
    IReadOnlyList<string> roots);

event EventHandler<SongRootsChangedEventArgs>? SongRootsChanged;
```

`ConfigManager.SetSongRoots` validates through `SongRootPolicy`, writes the complete config immediately, rolls back memory on persistence failure, raises one event after success, and returns `Unchanged` without writing or raising when the canonical ordered list is equal.

- [ ] **Step 4: Replace live library views with coherent snapshots**

Under `_lockObject`, capture:

```csharp
new SongLibrarySnapshot(
    ++_publicationVersion,
    _rootSongs.ToArray(),
    _currentSearchPaths.ToArray(),
    EnumeratedFileCount,
    DiscoveredScoreCount)
```

Required changes:

- `GetLibrarySnapshot()` returns a copied snapshot.
- `PublishEnumeration` replaces hierarchy/roots/counts, increments one version, then raises `SongLibraryPublished` outside the lock.
- `PublishEmptyLibrary` clears hierarchy, active roots, and counts in one version and raises the same event.
- `SetCurrentSearchPaths(Array.Empty<string>())` clears `_currentSearchPaths`; remove the current empty-input early return.
- defensive deduplication in `SetCurrentSearchPaths` and `CreateBatchBuilder` uses `SongRootPolicy`.

Do not mutate a previously published root list after it has been handed to a consumer.

- [ ] **Step 5: Add zero-active-root import behavior**

Extend `SongEnumerationResult` with an explicit outcome such as:

```csharp
public enum SongEnumerationOutcome
{
    ImportedAndPublished,
    NoActiveRoots
}
```

When `BuildEnumerationBatchAsync` returns a complete batch with no active roots:

- do not call `ImportSongsCoreAsync`;
- do not call `PublishEnumeration`;
- return `NoActiveRoots` with batch root-failure errors available to callers;
- always release the enumeration slot.

A clean startup with no available roots calls `PublishEmptyLibrary()` explicitly after deciding not to clean/import.

- [ ] **Step 6: Migrate startup and preview consumers**

`StartupStage` snapshots `Config.SongRoots`, uses the shared policy, and chooses cache/enumeration paths from the accepted roots. `SongSelectionStage` initializes from one `SongLibrarySnapshot`.

Replace `PreviewImagePanel.SongsRootPath` with:

```csharp
public IReadOnlyList<string> ActiveSongRootPaths { get; set; }
```

Absolute chart paths remain authoritative. Relative fallback tries active roots in order.

- [ ] **Step 7: Add architecture and concurrency regression tests**

Tests must prove:

- `RootSongs`/`GetLibrarySnapshot` remain stable while another thread publishes;
- filter projection and `BookmarkStateReconciler.Apply` cannot observe collection modification;
- root list and active roots share one publication version;
- empty publication clears both;
- `LoadScoreCacheAsync`, `BuildSongListFromDatabasePublicAsync`, and `NeedsEnumerationAsync` receive deduplicated roots;
- no production `Config.DTXPath` read remains outside Config serialization/migration allowlist.

The architecture test should scan `DTXMania.Game/**/*.cs` and list the allowed files explicitly so a future runtime read fails the test.

- [ ] **Step 8: Run Slice 1b gate**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongRootPolicyTests|FullyQualifiedName~SongManagerLibrarySnapshotTests|FullyQualifiedName~SongManagerBulkEnumerationTests|FullyQualifiedName~StartupStageLogicTests|FullyQualifiedName~PreviewImagePanelTests|FullyQualifiedName~DTXPathCompatibilityArchitectureTests"

dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug
```

- [ ] **Step 9: Commit Slice 1b**

```bash
git add DTXMania.Game/Lib/Song \
  DTXMania.Game/Lib/Config \
  DTXMania.Game/Lib/Stage/StartupStage.cs \
  DTXMania.Game/Lib/Stage/SongSelectionStage.cs \
  DTXMania.Test
git commit -m "feat: publish versioned song library snapshots"
```

**Review gate:** no live `RootSongs` wrapper, no empty-root stale snapshot, no platform-dependent untested comparer branch, and no runtime `DTXPath` consumer.

---

## Slice 2 — Cross-Platform SongFolderPanel

### Task 2: Add Draft Editing and Platform Folder Selection

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Config/IConfigOverlayPanel.cs`
- Create: `DTXMania.Game/Lib/Stage/Config/FolderPickerModels.cs`
- Create: `DTXMania.Game/Lib/Stage/Config/SongFolderPanel.cs`
- Create: platform picker/factory files listed in File Structure.
- Create: `DTXMania.Test/Config/SongFolderPanelTests.cs`
- Create: `DTXMania.Test/Config/FolderPickerContractTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/KeyAssign/IKeyAssignPanel.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify: both platform `.csproj` files.
- Modify: `DTXMania.Test/Config/ConfigStageLogicTests.cs`

**Interfaces:**

```csharp
public interface IConfigOverlayPanel
{
    bool IsActive { get; }
    event EventHandler? Saved;
    event EventHandler? Closed;
    void Activate();
    void Deactivate();
    void Update(double deltaTime, KeyboardState current, KeyboardState previous);
    void Draw(SpriteBatch spriteBatch, IFont? font, IFont? boldFont,
        Texture2D? whitePixel, int virtualWidth, int virtualHeight);
}

public interface IFolderPickerService
{
    Task<FolderPickerResult> PickFolderAsync(
        string? initialDirectory,
        CancellationToken cancellationToken);
}
```

`IKeyAssignPanel` inherits `IConfigOverlayPanel`. `ConfigStage._activePanel` uses the generalized interface.

- [ ] **Step 1: Add panel-lifecycle and draft red tests**

Cover:

```text
Activate_ShouldCopyConfiguredRoots
CancelAndBack_ShouldDiscardDraft
Add_ShouldAppendSelectedFolder
PickerCancellation_ShouldNotMutateDraft
Remove_ShouldProtectLastRoot
MoveUpAndMoveDown_ShouldPreserveSelection
StructuralError_ShouldKeepPanelOpen
AvailabilityWarning_ShouldAllowApply
Saved_ShouldFireBeforeClosed
```

Use an injected fake picker and fake apply delegate. The panel never receives `IConfigManager`.

- [ ] **Step 2: Add `IConfigOverlayPanel` and adapt key panels**

Move only the common lifecycle members into the new interface. Preserve the existing key-panel requirement that `Saved` fires before `Closed`.

Run existing key assignment tests immediately after this refactor.

- [ ] **Step 3: Implement `SongFolderPanel`**

The panel owns:

- a copied draft root list;
- selected row/action indexes;
- async picker state and activation generation;
- structural diagnostics and availability warnings;
- a Config-owned apply delegate.

Use the root policy for validation. Picker completion may modify draft state only if the captured panel generation is current.

- [ ] **Step 4: Add platform picker implementations**

Windows:

- use `FolderBrowserDialog`;
- own STA dispatch in the platform service;
- return Selected/Cancelled/Failed without blocking the game update thread.

macOS:

- invoke `/usr/bin/osascript` with `ProcessStartInfo.ArgumentList`;
- use `choose folder` and `default location` only when valid;
- await exit asynchronously;
- distinguish normal user cancellation from authorization/privacy failure;
- include stderr only in structured diagnostics.

Do not launch real dialogs in unit tests.

- [ ] **Step 5: Isolate platform compilation**

Add explicit compile exclusions:

```xml
<!-- Windows project -->
<Compile Remove="Platform/Mac/**/*.cs" />
<Compile Remove="Platform/FolderPickerServiceFactory.Mac.cs" />

<!-- Mac project -->
<Compile Remove="Platform/Windows/**/*.cs" />
<Compile Remove="Platform/FolderPickerServiceFactory.Windows.cs" />
```

The exact glob may be adjusted to the final directory layout, but each project must compile only one factory and one native implementation.

- [ ] **Step 6: Wire Config navigation with temporary restart behavior**

Replace read-only **DTX Folder** with a `NavigationConfigItem` named **Song Folders**. Display `1 folder` or `<n> folders`.

For Slice 2, the Config-owned delegate calls `SetSongRoots`. On `Updated`, close the panel and show a restart-required status. On `Unchanged`, close without status. On failure, keep the panel open.

- [ ] **Step 7: Run Slice 2 gate**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongFolderPanelTests|FullyQualifiedName~FolderPickerContractTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~KeyAssign"

dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj -c Debug
```

- [ ] **Step 8: Commit Slice 2**

```bash
git add DTXMania.Game/Lib/Stage/Config \
  DTXMania.Game/Lib/Stage/KeyAssign \
  DTXMania.Game/Lib/Stage/ConfigStage.cs \
  DTXMania.Game/Platform \
  DTXMania.Game/*.csproj \
  DTXMania.Test/Config
git commit -m "feat: add song folder configuration panel"
```

**Review gate:** panel draft isolation, platform build isolation, async picker lifecycle, and restart persistence through Startup.

---

## Slice 3a — Config Operation Coordinator and Live Reload

### Task 3A: Serialize Config Song Operations and Start One Live Reload

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Config/ConfigSongOperationCoordinator.cs`
- Create: `DTXMania.Game/Lib/Stage/Config/SongLibraryReloadModels.cs`
- Create: `DTXMania.Game/Lib/Stage/Config/SongLibraryReloadService.cs`
- Create: `DTXMania.Test/Config/ConfigSongOperationCoordinatorTests.cs`
- Create: `DTXMania.Test/Config/SongLibraryReloadServiceTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify: `DTXMania.Test/Stage/ConfigStageNxImportTests.cs`
- Modify: relevant SongManager bulk-enumeration tests.

**Interfaces:**

```csharp
internal enum ConfigSongOperationKind
{
    NxScoreImport,
    SongFolderReload
}

internal sealed class ConfigSongOperationLease : IDisposable
{
    public ConfigSongOperationKind Kind { get; }
}

internal interface ISongLibraryReloadService
{
    Task<SongLibraryReloadResult> ReloadAsync(
        IReadOnlyList<string> configuredRoots,
        IProgress<SongLibraryReloadProgress>? progress,
        CancellationToken cancellationToken);
}
```

Persistence and orchestration results remain separate. Config maps `SongRootUpdateResult` into a panel-facing orchestration result that may additionally be `Busy` or `Started`.

- [ ] **Step 1: Write coordinator red tests**

Cover:

```text
TryAcquire_ShouldAllowOneOwner
TryAcquire_ShouldRejectDifferentOperationWhileBusy
Dispose_ShouldReleaseExactlyOnce
ThrowDuringOperationConstruction_ShouldReleaseLease
TerminalContinuationRegistrationFailure_ShouldReleaseLease
```

Use an injectable continuation/task factory to force each synchronous handoff failure.

- [ ] **Step 2: Implement a scoped coordinator**

The coordinator owns one atomic state. A lease releases through `Dispose()` exactly once. Do not expose check-then-set booleans.

`ConfigStage` uses one method for song-root Apply:

```text
validate/compare
→ acquire lease
→ persist roots
→ create CTS
→ construct reload task
→ register observation/progress/terminal release
→ store operation handle
→ return Started to panel
```

All synchronous steps remain inside one `try/finally`. `Saved` only reports that the draft was accepted; it does not acquire, transfer, or release the lease.

- [ ] **Step 3: Implement the reload adapter**

`SongLibraryReloadService` calls the existing HPA-192 operation once with the configured roots.

Result mapping:

- occupied lower-level enumeration slot → `Busy`;
- `SongEnumerationOutcome.NoActiveRoots` → `NoAvailableRoots`, old hierarchy retained;
- successful import/publication → `Success`;
- pre-commit cancellation/failure → old hierarchy retained;
- unexpected post-commit publication failure → partial-success/restart-required.

For completed enumeration, count unavailable roots from `Batch.Errors.Where(error => error.IsRootFailure)`. A shallow preflight may provide early UI warnings, but it cannot override the batch result.

- [ ] **Step 4: Migrate NX import onto the same lifecycle**

Remove `_importRunning` check-then-set ownership. NX import must:

- acquire `ConfigSongOperationKind.NxScoreImport`;
- use the same activation generation;
- enqueue progress/status rather than writing `_importStatus` from a worker;
- cancel on Config deactivation;
- attach an observation continuation;
- dispose CTS and release lease exactly once in the terminal continuation.

Both operations must reject each other with a concise status.

- [ ] **Step 5: Add update-thread status marshalling**

Use a thread-safe queue of immutable operation updates. `ConfigStage.OnUpdate` drains only updates whose activation generation is current. Deactivation increments the generation before cancellation.

Never wait synchronously for filesystem or SQLite work during stage teardown.

- [ ] **Step 6: Preserve the commit-to-publication terminal section**

Ensure HPA-192 does not check cancellation after database commit and before `FinalizePendingNodes`/`PublishEnumeration`. Add a regression test where cancellation arrives after commit and publication still completes successfully.

- [ ] **Step 7: Add operation integration tests**

Required tests:

```text
UnchangedApply_ShouldNotAcquirePersistOrScan
BusyApply_ShouldNotPersist
ReorderOnlyApply_ShouldInvokeImporterOnce
NoActiveRoots_ShouldRetainCurrentSnapshot
BatchRootFailures_ShouldDriveWarningCount
NxImportAndReload_ShouldNotOverlap
Deactivate_ShouldCancelWithoutBlocking
WorkerProgress_ShouldReachUIOnlyThroughUpdateDrain
EveryTerminalPath_ShouldDisposeCtsAndReleaseLeaseOnce
```

- [ ] **Step 8: Run Slice 3a gate**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~ConfigSongOperationCoordinatorTests|FullyQualifiedName~SongLibraryReloadServiceTests|FullyQualifiedName~ConfigStageNxImportTests|FullyQualifiedName~SongManagerBulkEnumerationTests"
```

- [ ] **Step 9: Commit Slice 3a**

```bash
git add DTXMania.Game/Lib/Stage/Config \
  DTXMania.Game/Lib/Stage/ConfigStage.cs \
  DTXMania.Game/Lib/Song \
  DTXMania.Test/Config \
  DTXMania.Test/Stage/ConfigStageNxImportTests.cs \
  DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs
git commit -m "feat: live reload configured song roots"
```

**Review gate:** no split lease ownership, no worker-thread UI writes, no NX/reload overlap, and no normal publication for a zero-active-root batch.

---

## Slice 3b — Active Song Select Reconciliation and Retained Views

### Task 3B: Reconcile Runtime Publication on the Update Thread

**Files:**
- Create: `DTXMania.Test/Stage/SongSelectionPublicationTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/PreviewImagePanel.cs`
- Modify: existing Song Selection, bookmark, recent, and preview tests.

**Interfaces:**

`SongSelectionStage` subscribes to `SongManager.SongLibraryPublished` on activation and unsubscribes on deactivation. The event handler stores only the newest pending publication version; it never changes UI collections.

- [ ] **Step 1: Write publication-reconciliation red tests**

Create scenarios for:

```text
PublicationWhileActive_ShouldApplyOnUpdateThread
PublicationInsideRetainedBox_ShouldRestoreNavigation
PublicationInsideRemovedBox_ShouldResetToRoot
PublicationWithRetainedSelection_ShouldRestoreByStableIdentity
PublicationRemovingSelection_ShouldStopPreviewAndClearPanels
PublicationOnBookmarks_ShouldFilterToActiveRoots
PublicationOnRecent_ShouldFilterToActiveRoots
MultiplePublications_ShouldApplyNewestVersionOnly
NotificationAfterDeactivate_ShouldBeIgnored
```

Use test snapshots with stable chart/database identities and distinct publication versions.

- [ ] **Step 2: Add event subscription and pending-version state**

On activation:

- capture one `SongLibrarySnapshot`;
- subscribe to publication;
- remember current activation version.

The event handler atomically records the highest pending version only. On deactivation, unsubscribe before disposing stage resources.

- [ ] **Step 3: Reconcile one coherent snapshot in `OnUpdate`**

When a pending version exceeds the applied version:

1. fetch one current `SongLibrarySnapshot`;
2. ignore it if superseded or stage generation changed;
3. replace root-list and active-root state from that snapshot;
4. restore navigation by stable path/database identity if possible;
5. restore selected chart by stable chart/database identity if possible;
6. otherwise reset deterministically to root/first item;
7. rebuild filter, Bookmarks, Recent, breadcrumb, preview roots, and empty state;
8. stop preview and clear status/history when selection disappeared;
9. mark the snapshot version as applied.

Do not mix `RootSongs` from one version with active roots from another.

- [ ] **Step 4: Filter Bookmarks and Recent to active roots**

Database-backed tab loaders must filter chart paths through the active-root snapshot from the same publication version. Off-root rows remain stored and reappear after a successful root re-add.

- [ ] **Step 5: Implement deliberate empty states**

Distinguish:

```text
No active roots:
No song folders are currently available. Open Config to reconnect or change them.

Active roots with no supported charts:
No songs were found in the active song folders.
```

Keep copy in one testable resolver method so future localization does not duplicate conditions.

- [ ] **Step 6: Add concurrent-publication regression coverage**

Run filter projection and bookmark reconciliation against a copied snapshot while publishing another version. Assert no `InvalidOperationException`, no background-thread mutation, and final UI data all references the newest applied version.

- [ ] **Step 7: Run Slice 3b gate**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongSelectionPublicationTests|FullyQualifiedName~SongSelectionStageLogicTests|FullyQualifiedName~SongSelectionStageBookmark|FullyQualifiedName~PreviewImagePanelTests"
```

- [ ] **Step 8: Commit Slice 3b**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs \
  DTXMania.Game/Lib/Song/Components/PreviewImagePanel.cs \
  DTXMania.Test/Stage \
  DTXMania.Test/UI
git commit -m "feat: reconcile live song library publication"
```

**Review gate:** active Song Select never enumerates a live backing list, applies publication only on the update thread, and keeps hierarchy/tabs/previews on one version.

---

## Final Verification

- [ ] **Run the complete unit suite**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj -c Release
```

Expected: zero failed tests.

- [ ] **Build both game targets**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Release
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj -c Release
```

Expected: both builds succeed; each compiles only its own picker implementation.

- [ ] **Run production consumer audit**

```bash
rg "Config\??\.DTXPath|ConfigData\.DTXPath|\.DTXPath" DTXMania.Game --glob '*.cs'
```

Expected: matches only the compatibility property plus Config load/save/migration code allowed by `DTXPathCompatibilityArchitectureTests`.

- [ ] **Manual smoke checks**

1. Migrate an existing one-root Config.ini and verify roots persist after restart.
2. Add a second local root, reorder, Apply, and verify one reload.
3. Configure one missing removable root plus one available root; verify available songs load and warning count matches scan errors.
4. Remove a root; verify songs/bookmarks/recent disappear from active views but return after re-add.
5. Leave Config during reload and enter Song Select; verify post-publication reconciliation without stale selection or crash.
6. Start NX score import and attempt Apply; verify Busy and no config write.
7. Test picker cancellation on Windows and macOS.

- [ ] **Final commit/checkpoint**

Do not squash slice commits until all review gates pass. The final PR should show the five bounded implementation checkpoints clearly enough to bisect regressions.
