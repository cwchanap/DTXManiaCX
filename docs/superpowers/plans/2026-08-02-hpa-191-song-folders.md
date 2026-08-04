# HPA-191 Configurable Multiple Song Folders Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players persist, edit, and live-reload an ordered list of song-library roots while preserving HPA-192 atomic publication, retained user data, and a coherent active Song Select stage.

**Architecture:** Configuration owns ordered `SongRoots` and keeps `DTXPath` only as a compatibility mirror. `SongManager` centralizes comparer-aware root identity and publishes copied, versioned `SongLibrarySnapshot` values. Config serializes NX score import and song-root reload through one scoped operation coordinator; Song Select consumes publication only on the update thread.

**Tech Stack:** .NET 8, C# 12, MonoGame 3.8, Entity Framework Core SQLite, xUnit, WinForms on Windows, `/usr/bin/osascript` Standard Additions on macOS.

## Global Constraints

- DTXCreator is out of scope.
- Do not change chart parsing, `set.def`, `box.def`, database schema, or root-navigation semantics.
- `SongRoots` is the configured-root source of truth; `DTXPath` is serialization/migration compatibility only.
- Config.ini parsing remains section-agnostic and splits on the first `=` only.
- Create only `AppPaths.GetDefaultSongsPath()` automatically; never create a missing custom root.
- Root comparison is ordinal-ignore-case on Windows/macOS and ordinal on Linux/other platforms, through an injectable test seam.
- `SongPathIdentity.CanonicalComparer` remains ordinal for chart identity.
- Parent/child overlap uses segment-wise comparison through the root comparer; do not use `Path.GetRelativePath` for this policy.
- Physical-target deduplication for symlinks, junctions, aliases, bind mounts, or inodes remains out of scope.
- Never expose `_rootSongs` or `_currentSearchPaths` through a live collection wrapper.
- Hierarchy, active roots, counts, and publication version change as one coherent snapshot.
- Startup may explicitly publish an empty library without cleanup. A normal live reload with zero active roots must not import or publish.
- Any changed ordered root list, including reorder-only changes, performs one full HPA-192 scan; unchanged lists perform none.
- HPA-192 enumeration-batch root failures are authoritative for roots rejected at scan time.
- Database commit through hierarchy publication is a non-cancellable terminal section.
- Config NX score import and song-root reload share one exclusive, throw-safe operation lifecycle.
- Worker threads report immutable progress updates; only the game update thread changes Config or Song Select UI state.
- Runtime `Config.DTXPath` reads are forbidden outside an explicit compatibility allowlist.
- Platform picker source must not compile into the opposite platform target.
- Slices land in order: **1a → 1b → 2 → 3a → 3b**.
- Each slice must remain reviewable within three engineer days.

## Branch and PR Strategy

1. Merge documentation PR #109 first.
2. Create a fresh implementation branch from updated `main`; do not add production code to the documentation PR.
3. Use one implementation PR with five clearly separated slice commits and review gates.
4. Do not squash during implementation; retain slice boundaries until the complete feature passes final verification.

---

## File Map

### New shared files

- `DTXMania.Game/Lib/Config/SongRootConfigModels.cs` — persistence statuses, diagnostics, and immutable event snapshots.
- `DTXMania.Game/Lib/Song/SongRootPolicy.cs` — normalization, comparer construction, deduplication, overlap validation, and availability probing.
- `DTXMania.Game/Lib/Song/SongLibrarySnapshot.cs` — copied publication snapshot and event args.
- `DTXMania.Game/Lib/Stage/Config/IConfigOverlayPanel.cs` — generalized Config overlay lifecycle.
- `DTXMania.Game/Lib/Stage/Config/FolderPickerModels.cs` — picker and panel-apply contracts.
- `DTXMania.Game/Lib/Stage/Config/SongFolderPanel.cs` — isolated draft-list UI.
- `DTXMania.Game/Lib/Stage/Config/ConfigSongOperationCoordinator.cs` — exclusive scoped lease.
- `DTXMania.Game/Lib/Stage/Config/SongLibraryReloadModels.cs` — reload progress/result contracts.
- `DTXMania.Game/Lib/Stage/Config/SongLibraryReloadService.cs` — HPA-192 adapter.

### New platform files

- `DTXMania.Game/Platform/Windows/WindowsFolderPickerService.cs`
- `DTXMania.Game/Platform/Mac/MacFolderPickerService.cs`
- `DTXMania.Game/Platform/FolderPickerServiceFactory.Windows.cs`
- `DTXMania.Game/Platform/FolderPickerServiceFactory.Mac.cs`

### Primary modified files

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

---

## Slice 1a — Configuration Model and Persistence

### Task 1A: Persist Ordered Song Roots

**Files:**
- Create: `DTXMania.Game/Lib/Config/SongRootConfigModels.cs`
- Create: `DTXMania.Test/Config/SongRootConfigTests.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Test/Config/ConfigDataTests.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerTests.cs`

**Produces:**

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

This slice adds the stored model and compatibility behavior. The comparer-aware setter is completed in Slice 1b after `SongRootPolicy` exists.

- [ ] **Step 1: Write red tests for the persisted format**

Add tests:

```text
DefaultConfig_ShouldContainManagedSongRootAndMirror
LoadConfig_ShouldClearSongRootsBeforeSecondParse
LoadConfig_ShouldReadIndexedRootsInNumericOrder
LoadConfig_ShouldUseLastDuplicateIndex
LoadConfig_ShouldPreferIndexedRootsOverLegacyDTXPath
LoadConfig_ShouldMigrateLegacyDTXPathAndPersistIndexedRoot
SaveConfig_ShouldWriteDenseIndexesAndFirstRootMirror
LoadConfig_ShouldRemainSectionAgnostic
```

Use temporary Config.ini files. Include unrelated `[System]` and `[Other]` headers and prove keys remain global.

- [ ] **Step 2: Run the red tests**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongRootConfigTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigDataTests"
```

Expected: failures because `SongRoots` and indexed serialization do not exist.

- [ ] **Step 3: Implement load/save and migration**

Add:

```csharp
public List<string> SongRoots { get; } = new();
```

At every `LoadConfig` start, call `Config.SongRoots.Clear()` beside `MidiVelocityThresholds.Clear()`. Collect `SongRoot.<non-negative integer>` entries during parsing and finalize after all lines are read.

Precedence:

1. accepted indexed roots in numeric order;
2. migrated legacy `DTXPath`;
3. managed default root.

Save dense `SongRoot.0..N` and mirror the first root into `DTXPath`.

- [ ] **Step 4: Remove custom-root auto-creation**

Replace unconditional `EnsureDirectorySafe(Config.DTXPath)` with managed-default-only creation.

Tests must prove:

- missing custom paths remain configured;
- load/migration/save never creates them;
- managed default creation still works;
- old directories are not deleted.

- [ ] **Step 5: Correct pending-save clearing**

After a full save, clear `_pendingSavePath` only when its normalized path equals the path just written. A save to another file must retain the pending marker.

Add:

```text
SaveConfig_ShouldClearMatchingPendingPath
SaveConfig_ShouldRetainDifferentPendingPath
FlushPendingSave_ShouldRetryAfterFailure
```

- [ ] **Step 6: Verify immutable model snapshots**

Construct event args/results from mutable lists, mutate the originals, and assert exposed values are unchanged. Use copied read-only arrays, not caller-owned lists.

- [ ] **Step 7: Run the Slice 1a gate**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongRootConfigTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigDataTests"
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug
```

- [ ] **Step 8: Commit explicit files**

```bash
git add \
  DTXMania.Game/Lib/Config/SongRootConfigModels.cs \
  DTXMania.Game/Lib/Config/ConfigData.cs \
  DTXMania.Game/Lib/Config/ConfigManager.cs \
  DTXMania.Test/Config/SongRootConfigTests.cs \
  DTXMania.Test/Config/ConfigDataTests.cs \
  DTXMania.Test/Config/ConfigManagerTests.cs
git commit -m "feat: persist ordered song roots"
```

**Gate:** indexed format, repeated-load clearing, section-agnostic parsing, custom-directory behavior removal, and pending-save correctness.

---

## Slice 1b — Root Policy, Safe SongManager Snapshots, and Consumers

### Task 1B: Centralize Root Identity and Publication

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
- Modify: focused existing SongManager/startup/preview tests.

**Produces:**

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

All snapshot lists are copied and wrapped read-only. Published node graphs are not structurally mutated after publication.

- [ ] **Step 1: Write root-policy red tests**

Execute both policies on every CI host:

```csharp
[Theory]
[InlineData(true, true)]
[InlineData(false, false)]
public void DuplicatePolicy_ShouldFollowInjectedCaseMode(
    bool ignoreCase,
    bool expectedDuplicate)
```

Add overlap cases:

```text
/Users/me/Songs + /Users/me/SONGS/Extra
C:\Songs + c:\songs\Pack
/songs + /Songs/Pack in ordinal mode
```

- [ ] **Step 2: Implement segment-wise `SongRootPolicy`**

The policy must normalize, preserve first occurrence/order, detect duplicates, and compare root/path segments using the injected comparer. Do not delegate overlap to `Path.GetRelativePath`.

`ForCurrentPlatform()` uses the production OS policy; tests use `CreateComparer(true/false)`.

- [ ] **Step 3: Complete `SetSongRoots`**

Add to `IConfigManager`:

```csharp
SongRootUpdateResult SetSongRoots(
    string configFilePath,
    IReadOnlyList<string> roots);

event EventHandler<SongRootsChangedEventArgs>? SongRootsChanged;
```

Validate through `SongRootPolicy`, persist immediately, roll memory back on failure, raise one event after success, and return `Unchanged` without write/event for an equal canonical ordered list.

- [ ] **Step 4: Replace live SongManager views**

Make `SetCurrentSearchPaths` `internal` for direct tests while keeping it non-public.

Publication methods, not reads, increment `_publicationVersion`:

```csharp
private SongLibrarySnapshot CreateSnapshotLocked() =>
    new(
        _publicationVersion,
        Array.AsReadOnly(_rootSongs.ToArray()),
        Array.AsReadOnly(_currentSearchPaths.ToArray()),
        EnumeratedFileCount,
        DiscoveredScoreCount);
```

Required behavior:

- `GetLibrarySnapshot()` reads the current version without incrementing it.
- `PublishEnumeration` replaces hierarchy/roots/counts, increments once, captures one snapshot, then raises `SongLibraryPublished` outside the lock.
- `PublishEmptyLibrary` clears hierarchy/roots/counts, increments once, and publishes one empty snapshot.
- empty input to `SetCurrentSearchPaths` clears active roots.
- `RootSongs`, if retained, returns the copied snapshot list rather than `_rootSongs.AsReadOnly()`.
- `SetCurrentSearchPaths` and `CreateBatchBuilder` use `SongRootPolicy` deduplication.

- [ ] **Step 5: Add a non-null zero-active-root result**

Update models:

```csharp
public enum SongEnumerationOutcome
{
    ImportedAndPublished,
    NoActiveRoots
}

public sealed record SongEnumerationResult(
    SongEnumerationOutcome Outcome,
    SongEnumerationBatch Batch,
    SongBulkImportResult Import,
    TimeSpan HierarchyDuration);
```

Add `SongBulkImportResult.Empty` with empty read-only chart map, zero counts, and zero durations. When a complete batch has no active roots, return `NoActiveRoots` with `Empty`; do not import or publish, and always release the enumeration slot.

Startup with no accepted roots calls `PublishEmptyLibrary()` explicitly.

- [ ] **Step 6: Migrate startup and preview consumers**

`StartupStage` snapshots `Config.SongRoots`. `SongSelectionStage` initializes from one `SongLibrarySnapshot`.

Replace `PreviewImagePanel.SongsRootPath` with:

```csharp
public IReadOnlyList<string> ActiveSongRootPaths { get; set; }
```

Absolute chart path remains authoritative; relative fallback tries active roots in order.

- [ ] **Step 7: Add architecture and concurrency tests**

Prove:

- snapshots remain stable while another thread publishes;
- filter and bookmark reconciliation cannot observe collection mutation;
- hierarchy and active roots share one version;
- empty publication clears both;
- cache/database/enumeration callers use aligned deduplication;
- no production `Config.DTXPath` read remains outside an explicit allowlist.

- [ ] **Step 8: Run the Slice 1b gate**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongRootPolicyTests|FullyQualifiedName~SongManagerLibrarySnapshotTests|FullyQualifiedName~SongManagerBulkEnumerationTests|FullyQualifiedName~StartupStageLogicTests|FullyQualifiedName~PreviewImagePanelTests|FullyQualifiedName~DTXPathCompatibilityArchitectureTests"
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug
```

- [ ] **Step 9: Commit explicit files**

```bash
git add \
  DTXMania.Game/Lib/Song/SongRootPolicy.cs \
  DTXMania.Game/Lib/Song/SongLibrarySnapshot.cs \
  DTXMania.Game/Lib/Song/SongImportModels.cs \
  DTXMania.Game/Lib/Song/SongManager.cs \
  DTXMania.Game/Lib/Config/IConfigManager.cs \
  DTXMania.Game/Lib/Config/ConfigManager.cs \
  DTXMania.Game/Lib/Stage/StartupStage.cs \
  DTXMania.Game/Lib/Stage/SongSelectionStage.cs \
  DTXMania.Game/Lib/Song/Components/PreviewImagePanel.cs \
  DTXMania.Test/Song/SongRootPolicyTests.cs \
  DTXMania.Test/Song/SongManagerLibrarySnapshotTests.cs \
  DTXMania.Test/Config/DTXPathCompatibilityArchitectureTests.cs \
  DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs \
  DTXMania.Test/Stage/StartupStageLogicTests.cs \
  DTXMania.Test/UI/PreviewImagePanelTests.cs
git commit -m "feat: publish versioned song library snapshots"
```

**Gate:** no live root wrapper, no stale active roots after empty publication, both comparer modes tested, and no runtime `DTXPath` consumer.

---

## Slice 2 — Cross-Platform SongFolderPanel

### Task 2: Add Draft Editing and Platform Pickers

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Config/IConfigOverlayPanel.cs`
- Create: `DTXMania.Game/Lib/Stage/Config/FolderPickerModels.cs`
- Create: `DTXMania.Game/Lib/Stage/Config/SongFolderPanel.cs`
- Create: four platform files listed above.
- Create: `DTXMania.Test/Config/SongFolderPanelTests.cs`
- Create: `DTXMania.Test/Config/FolderPickerContractTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/KeyAssign/IKeyAssignPanel.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify: both game `.csproj` files.
- Modify: `DTXMania.Test/Config/ConfigStageLogicTests.cs`

**Produces:**

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

internal enum SongFolderApplyStatus
{
    Updated,
    Unchanged,
    Busy,
    ValidationFailed,
    PersistenceFailed,
    Started
}
```

`IKeyAssignPanel` inherits `IConfigOverlayPanel`.

- [ ] **Step 1: Write panel red tests**

Cover isolated draft, Add, Remove-last protection, Move Up/Down, Cancel/Back, structural errors, availability warnings, picker cancellation/failure, stale picker generation, and `Saved` before `Closed`.

- [ ] **Step 2: Generalize overlay lifecycle**

Extract only common members into `IConfigOverlayPanel`; preserve existing key-panel semantics and run key-assignment tests.

- [ ] **Step 3: Implement `SongFolderPanel`**

The panel owns copied draft state and receives a fakeable picker, root policy, and Config-owned apply delegate. It never holds `IConfigManager`.

- [ ] **Step 4: Implement platform pickers**

Windows uses `FolderBrowserDialog` on an owned STA dispatcher. macOS uses `/usr/bin/osascript`, `ProcessStartInfo.ArgumentList`, asynchronous exit, and distinct cancellation vs authorization failure mapping.

- [ ] **Step 5: Isolate compilation**

Add explicit `<Compile Remove=...>` entries so each target compiles exactly one picker implementation and factory.

- [ ] **Step 6: Wire Config with restart-required behavior**

Replace **DTX Folder** with **Song Folders**, showing `1 folder` or `<n> folders`. Slice 2 apply calls `SetSongRoots`; `Updated` closes with restart-required status, `Unchanged` closes silently, failures keep the panel open.

- [ ] **Step 7: Run the Slice 2 gate**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongFolderPanelTests|FullyQualifiedName~FolderPickerContractTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~KeyAssign"
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj -c Debug
```

- [ ] **Step 8: Commit explicit files**

```bash
git add \
  DTXMania.Game/Lib/Stage/Config/IConfigOverlayPanel.cs \
  DTXMania.Game/Lib/Stage/Config/FolderPickerModels.cs \
  DTXMania.Game/Lib/Stage/Config/SongFolderPanel.cs \
  DTXMania.Game/Lib/Stage/KeyAssign/IKeyAssignPanel.cs \
  DTXMania.Game/Lib/Stage/ConfigStage.cs \
  DTXMania.Game/Platform/Windows/WindowsFolderPickerService.cs \
  DTXMania.Game/Platform/Mac/MacFolderPickerService.cs \
  DTXMania.Game/Platform/FolderPickerServiceFactory.Windows.cs \
  DTXMania.Game/Platform/FolderPickerServiceFactory.Mac.cs \
  DTXMania.Game/DTXMania.Game.Windows.csproj \
  DTXMania.Game/DTXMania.Game.Mac.csproj \
  DTXMania.Test/Config/SongFolderPanelTests.cs \
  DTXMania.Test/Config/FolderPickerContractTests.cs \
  DTXMania.Test/Config/ConfigStageLogicTests.cs
git commit -m "feat: add song folder configuration panel"
```

**Gate:** isolated draft, async picker lifecycle, platform build isolation, and persisted roots consumed after restart.

---

## Slice 3a — Config Coordinator and Live Reload

### Task 3A: Serialize Config Song Operations

**Files:**
- Create: `DTXMania.Game/Lib/Stage/Config/ConfigSongOperationCoordinator.cs`
- Create: `DTXMania.Game/Lib/Stage/Config/SongLibraryReloadModels.cs`
- Create: `DTXMania.Game/Lib/Stage/Config/SongLibraryReloadService.cs`
- Create: `DTXMania.Test/Config/ConfigSongOperationCoordinatorTests.cs`
- Create: `DTXMania.Test/Config/SongLibraryReloadServiceTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify: `DTXMania.Test/Stage/ConfigStageNxImportTests.cs`
- Modify: `DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs`

**Produces:**

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

Persistence results remain in Config; orchestration adds Busy/Started without changing `IConfigManager` concerns.

- [ ] **Step 1: Write coordinator red tests**

Cover single ownership, cross-operation Busy, exactly-once release, task-construction throw, continuation-registration throw, and repeated Dispose.

- [ ] **Step 2: Implement the scoped coordinator**

Replace check-then-set booleans with atomic lease acquisition. One Config method owns:

```text
compare/validate
→ acquire lease
→ persist
→ create CTS
→ construct operation task
→ register progress and terminal observation
→ transfer release to terminal continuation
→ return Started
```

All synchronous steps stay inside one `try/finally`. `SongFolderPanel.Saved` does not transfer lease ownership.

- [ ] **Step 3: Implement reload result mapping**

Call HPA-192 once.

Map:

- occupied enumeration slot → Busy;
- `NoActiveRoots` → retain old hierarchy;
- success → published snapshot;
- pre-commit cancel/failure → retain old hierarchy;
- unexpected post-commit publication failure → partial-success/restart-required.

For completed enumeration, derive unavailable count from `Batch.Errors.Where(e => e.IsRootFailure)`. Preflight may show an early warning but never overrides batch truth.

- [ ] **Step 4: Migrate NX import fully**

Remove `_importRunning` ownership and worker writes to `_importStatus`. NX import uses the same coordinator, activation generation, immutable update queue, task observation, CTS disposal, and terminal release.

- [ ] **Step 5: Marshal progress on the update thread**

Worker continuations enqueue immutable updates tagged with activation generation. `ConfigStage.OnUpdate` drains current-generation updates only. Deactivation increments generation, requests cancellation, and never waits synchronously.

- [ ] **Step 6: Protect commit-to-publication**

Add a regression test proving cancellation after database commit cannot prevent finalization/publication.

- [ ] **Step 7: Run operation tests**

Required cases:

```text
UnchangedApply_ShouldNotAcquirePersistOrScan
BusyApply_ShouldNotPersist
ReorderOnlyApply_ShouldImportOnce
NoActiveRoots_ShouldRetainCurrentSnapshot
BatchRootFailures_ShouldDriveWarningCount
NxImportAndReload_ShouldNotOverlap
Deactivate_ShouldCancelWithoutBlocking
WorkerProgress_ShouldUpdateOnlyWhenDrained
EveryTerminalPath_ShouldDisposeAndReleaseOnce
```

- [ ] **Step 8: Run the Slice 3a gate**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~ConfigSongOperationCoordinatorTests|FullyQualifiedName~SongLibraryReloadServiceTests|FullyQualifiedName~ConfigStageNxImportTests|FullyQualifiedName~SongManagerBulkEnumerationTests"
```

- [ ] **Step 9: Commit explicit files**

```bash
git add \
  DTXMania.Game/Lib/Stage/Config/ConfigSongOperationCoordinator.cs \
  DTXMania.Game/Lib/Stage/Config/SongLibraryReloadModels.cs \
  DTXMania.Game/Lib/Stage/Config/SongLibraryReloadService.cs \
  DTXMania.Game/Lib/Stage/ConfigStage.cs \
  DTXMania.Test/Config/ConfigSongOperationCoordinatorTests.cs \
  DTXMania.Test/Config/SongLibraryReloadServiceTests.cs \
  DTXMania.Test/Stage/ConfigStageNxImportTests.cs \
  DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs
git commit -m "feat: live reload configured song roots"
```

**Gate:** no split lease ownership, no worker-thread UI writes, no NX/reload overlap, and no live publication for a zero-active-root batch.

---

## Slice 3b — Active Song Select Reconciliation

### Task 3B: Apply Publications on the Update Thread

**Files:**
- Create: `DTXMania.Test/Stage/SongSelectionPublicationTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/PreviewImagePanel.cs`
- Modify: `DTXMania.Test/Stage/SongSelectionStageLogicTests.cs`
- Modify: `DTXMania.Test/Stage/SongSelectionStageBookmarkToggleTests.cs`
- Modify: `DTXMania.Test/UI/PreviewImagePanelTests.cs`

**Consumes:** `SongLibraryPublished` and `GetLibrarySnapshot()` from Slice 1b.

- [ ] **Step 1: Write reconciliation red tests**

Cover active publication, retained/removed box navigation, retained/removed selected chart, Bookmarks, Recent, multiple publications, and post-deactivation notification.

- [ ] **Step 2: Subscribe safely**

Subscribe on activation and unsubscribe on deactivation. The event handler records only the highest pending version; it never mutates UI collections.

- [ ] **Step 3: Reconcile one snapshot in `OnUpdate`**

When pending version exceeds applied version:

1. fetch one current snapshot;
2. verify activation/version;
3. replace roots and active paths;
4. restore navigation by stable path/database identity when possible;
5. restore selected chart by stable identity when possible;
6. otherwise reset deterministically;
7. rebuild filter, Bookmarks, Recent, breadcrumb, preview roots, and empty state;
8. stop preview and clear panels when selection disappeared;
9. mark the version applied.

Never mix hierarchy from one version with active roots from another.

- [ ] **Step 4: Filter retained tabs by active roots**

Bookmarks and Recent hide off-active-root rows without deleting database records. Re-add makes retained rows visible again.

- [ ] **Step 5: Add explicit empty-state resolver**

Return distinct states for no active roots versus active roots containing no supported charts. Keep condition resolution in one testable method.

- [ ] **Step 6: Add concurrent-publication coverage**

Run filter projection and bookmark reconciliation on an old copied snapshot while publishing a new one. Assert no collection-modified exception and final UI uses only the newest version.

- [ ] **Step 7: Run the Slice 3b gate**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongSelectionPublicationTests|FullyQualifiedName~SongSelectionStageLogicTests|FullyQualifiedName~SongSelectionStageBookmark|FullyQualifiedName~PreviewImagePanelTests"
```

- [ ] **Step 8: Commit explicit files**

```bash
git add \
  DTXMania.Game/Lib/Stage/SongSelectionStage.cs \
  DTXMania.Game/Lib/Song/Components/PreviewImagePanel.cs \
  DTXMania.Test/Stage/SongSelectionPublicationTests.cs \
  DTXMania.Test/Stage/SongSelectionStageLogicTests.cs \
  DTXMania.Test/Stage/SongSelectionStageBookmarkToggleTests.cs \
  DTXMania.Test/UI/PreviewImagePanelTests.cs
git commit -m "feat: reconcile live song library publication"
```

**Gate:** active Song Select enumerates only copied snapshots, applies publication only on update, and keeps hierarchy/tabs/previews on one version.

---

## Final Verification

- [ ] **Run the complete unit suite**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj -c Release
```

Expected: zero failed tests.

- [ ] **Build both targets**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Release
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj -c Release
```

Expected: both succeed and compile only their own picker implementation.

- [ ] **Run the compatibility audit**

```bash
rg "Config\??\.DTXPath|ConfigData\.DTXPath|\.DTXPath" DTXMania.Game --glob '*.cs'
```

Expected: only allowlisted Config compatibility code.

- [ ] **Manual smoke matrix**

1. Migrate an existing one-root Config.ini and restart.
2. Add/reorder two local roots and verify one reload.
3. Combine one unavailable removable root with one available root and compare warning count with batch root failures.
4. Remove/re-add a root and verify retained scores, Bookmarks, and Recent.
5. Leave Config during reload and enter Song Select; verify deterministic post-publication reconciliation.
6. Start NX import and attempt Apply; verify Busy and no write.
7. Cancel platform pickers on Windows and macOS.

## Plan Self-Review Checklist

- [x] Every design acceptance criterion maps to a slice and test gate.
- [x] Both comparer branches are testable on every CI host.
- [x] Publication version increments only during publication.
- [x] Zero-active-root results keep a non-null empty import result.
- [x] Commit commands stage explicit files only.
- [x] No placeholders or undefined cross-slice interfaces remain.
