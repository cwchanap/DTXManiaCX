# HPA-529 Managed Crash Report Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add process-boundary managed crash capture that preserves console logging, writes privacy-safe local ZIP or emergency-text reports, keeps the newest five reports, and proves `Update`/`Draw` exceptions reach the executable boundary on WindowsDX and DesktopGL.

**Architecture:** `CrashReportRuntime` is created at process entry and owns the shared `ILoggerFactory`, bounded crash buffers, cached context, sanitizer, and report store. `Game1` receives only an `IGameCrashDiagnostics` facade; fatal capture occurs in an explicit entry-point lifecycle before guarded game disposal. The crash path reads immutable copies of already-cached state and performs no network, database, graphics, device-enumeration, shell, or cross-thread work.

**Tech Stack:** .NET 8, C# 12, MonoGame 3.8 WindowsDX/DesktopGL, `Microsoft.Extensions.Logging`, `System.Text.Json`, `System.IO.Compression`, xUnit, Moq, existing `DTXMania.E2E` process harness.

## Global Constraints

- Scope is limited to managed fatal exceptions that reach the executable process boundary during game construction, startup, `Update`, `Draw`, or `Game.Run()`.
- Do not add `AppDomain.UnhandledException`, `FirstChanceException`, or `TaskScheduler.UnobservedTaskException` handlers.
- Do not add automatic upload, telemetry, screenshots, native dumps, hang detection, or GitHub integration.
- A recoverable crash-report bootstrap failure must preserve fully functional console logging; only crash buffering, capture, cached context, and inbox behavior degrade to no-ops.
- `CrashReportRuntime` is the sole owner and disposer of the shared `ILoggerFactory`; `BaseGame` must neither create nor dispose it.
- Replace `using var game` in `Program.cs` with explicit capture-before-dispose lifecycle code. Disposal failure is secondary and cannot replace the original exception.
- Fatal capture must use cached state only: no device enumeration, filesystem scan beyond report persistence, SQLite access, network access, graphics calls, shell launching, cross-thread dispatch, or blocking waits.
- Sanitize before persistence. Unknown dynamic strings, arbitrary rendered log messages, complete configuration objects, secrets, paths, usernames, song/chart identity, hardware IDs, binding names, and MIDI note identities must not reach disk.
- Keep one latest-five quota across completed ZIP and emergency-text reports inside `CrashReportStore`.
- WindowsDX callback propagation must run automatically in the existing Windows E2E job.
- DesktopGL callback propagation must be verified manually on Apple Silicon and recorded with exact commands, commit/build identity, OS/runtime information, and observed results.
- Production code must not expose a crash-injection switch in Release builds. The E2E injection hook must be compiled only under `DEBUG`.
- The canonical design and normative amendments are:
  - `docs/superpowers/specs/2026-08-02-hpa-519-managed-crash-report-design.md`
  - `docs/superpowers/specs/2026-08-02-hpa-519-managed-crash-report-design-review.md`

---

## File Structure

### New production files

- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs` — public game-facing interfaces, enums, report summaries, and no-op singleton implementations.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportRuntime.cs` — process-lifetime composition, enabled/console-only bootstrap, fatal snapshot capture, and logger ownership.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogFieldPolicy.cs` — centralized event/template/property allowlist and scalar normalization.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogBufferProvider.cs` — bounded thread-safe `ILoggerProvider` and immutable snapshot API.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashBreadcrumbBuffer.cs` — bounded semantic breadcrumb sink and immutable snapshot API.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextSnapshotStore.cs` — atomically replaced cached context sections and sensitive-path registry.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportSanitizer.cs` — path/secret/source-location redaction and conservative exception-message policy.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportArchiveWriter.cs` — versioned ZIP entries and versioned emergency-text header/body serialization.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportStore.cs` — lazy directory creation, atomic finalization, discovery, stale-temp cleanup, fallback, and unified retention.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/GameEntryPoint.cs` — injectable executable lifecycle seam that preserves the original fatal exception.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs` — deterministic mapping from existing config/graphics/input/stage state into allowlisted cached context.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/DebugCrashInjection.cs` — `DEBUG`-only `Update`/`Draw` injection hook used by real-process E2E verification.

### Modified production files

- `DTXMania.Game/Program.cs`
- `DTXMania.Game/Game1.cs`
- `DTXMania.Game/Lib/Utilities/AppPaths.cs`
- `DTXMania.Game/Lib/Stage/IStageGame.cs`
- `DTXMania.Game/Lib/Stage/StageManager.cs`
- `DTXMania.Game/Lib/Input/Midi/MidiInputSource.cs`
- `DTXMania.Game/Lib/Input/ModularInputManager.cs`

### New test files

- `DTXMania.Test/CrashReporting/CrashReportContractsTests.cs`
- `DTXMania.Test/CrashReporting/CrashLogBufferProviderTests.cs`
- `DTXMania.Test/CrashReporting/CrashBreadcrumbBufferTests.cs`
- `DTXMania.Test/CrashReporting/CrashContextSnapshotStoreTests.cs`
- `DTXMania.Test/CrashReporting/CrashReportSanitizerTests.cs`
- `DTXMania.Test/CrashReporting/CrashReportStoreTests.cs`
- `DTXMania.Test/CrashReporting/CrashReportRuntimeTests.cs`
- `DTXMania.Test/CrashReporting/GameEntryPointTests.cs`
- `DTXMania.E2E/CrashReportingSmokeTests.cs`

### Modified test and CI files

- `DTXMania.Test/Utilities/AppPathsTests.cs`
- `DTXMania.Test/BaseGameTests.cs`
- `DTXMania.Test/Stage/StageManagerTransitionTests.cs`
- `DTXMania.Test/Input/Midi/MidiInputSourceTests.cs`
- `DTXMania.Test/Input/ModularInputManagerTests.cs`
- `DTXMania.E2E/Process/GameProcessDriver.cs`
- `.github/workflows/build-and-test.yml` only if an explicit crash-smoke filter step is needed; prefer the existing `Category=E2E` step when the new tests fit its runtime budget.

### Verification evidence

- `docs/verification/hpa-529-macos-crash-propagation.md` — actual Apple Silicon/DesktopGL `Update` and `Draw` verification evidence, written only after the commands have been executed.

---

### Task 1: Define stable crash-report contracts and the app-data path

**Files:**
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs`
- Modify: `DTXMania.Game/Lib/Utilities/AppPaths.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportContractsTests.cs`
- Test: `DTXMania.Test/Utilities/AppPathsTests.cs`

**Interfaces:**
- Produces:
  - `CrashReportFormat`
  - `CrashContextKind`
  - `CrashContextStatus`
  - `CrashReportSummary`
  - `CrashContextSnapshot`
  - `CrashBreadcrumb`
  - `ICrashBreadcrumbSink`
  - `ICrashContextSink`
  - `ICrashSensitiveDataSink`
  - `ICrashReportInbox`
  - `IGameCrashDiagnostics`
  - no-op singleton implementations used by console-only mode and isolated tests
  - `AppPaths.GetCrashReportsRoot()`

- [ ] **Step 1: Write failing contract and path tests**

Create tests that lock the exact public shapes and path behavior:

```csharp
[Fact]
public void GetCrashReportsRoot_ShouldUseAppDataCrashReportsDirectory()
{
    var previous = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
    var root = Path.Combine(Path.GetTempPath(), "dtx-crash-root-" + Guid.NewGuid().ToString("N"));

    try
    {
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", root);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(root), "CrashReports"),
            AppPaths.GetCrashReportsRoot());
    }
    finally
    {
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previous);
    }
}

[Fact]
public void EmptyInbox_ShouldReturnNoReportsAndRejectMutationsWithoutThrowing()
{
    var inbox = EmptyCrashReportInbox.Instance;

    Assert.Empty(inbox.GetReports());
    Assert.False(inbox.TryAcknowledge("missing", out var acknowledgeError));
    Assert.Equal("report_not_found", acknowledgeError);
    Assert.False(inbox.TryDelete("missing", out var deleteError));
    Assert.Equal("report_not_found", deleteError);
}
```

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~AppPathsTests|FullyQualifiedName~CrashReportContractsTests"
```

Expected: compile failure because the crash-report contracts and `GetCrashReportsRoot()` do not exist.

- [ ] **Step 3: Implement the exact contract model**

Use one focused contract file with these signatures:

```csharp
namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

public enum CrashReportFormat
{
    ZipBundle,
    EmergencyText
}

public enum CrashContextKind
{
    Process,
    Application,
    Startup,
    Configuration,
    Stage,
    Graphics,
    Audio,
    Input
}

public enum CrashContextStatus
{
    Available,
    NotInitialized,
    Unavailable,
    CollectionFailed
}

public sealed record CrashReportSummary(
    string ReportId,
    DateTimeOffset CapturedAtUtc,
    string BuildId,
    string OperatingSystem,
    string ProcessArchitecture,
    string StageOrMilestone,
    string ExceptionType,
    CrashReportFormat Format,
    string FileName);

public sealed record CrashContextSnapshot(
    CrashContextKind Kind,
    CrashContextStatus Status,
    IReadOnlyDictionary<string, object?> Fields,
    string? FailureCode = null);

public sealed record CrashBreadcrumb(
    DateTimeOffset TimestampUtc,
    string EventName,
    IReadOnlyDictionary<string, object?> Properties);

public interface ICrashBreadcrumbSink
{
    void Record(string eventName, IReadOnlyDictionary<string, object?>? properties = null);
}

public interface ICrashContextSink
{
    void SetSnapshot(CrashContextSnapshot snapshot);
}

public interface ICrashSensitiveDataSink
{
    void RegisterPath(string? path);
}

public interface ICrashReportInbox
{
    IReadOnlyList<CrashReportSummary> GetReports();
    bool TryAcknowledge(string reportId, out string? errorCode);
    bool TryDelete(string reportId, out string? errorCode);
}

public interface IGameCrashDiagnostics
{
    ILoggerFactory LoggerFactory { get; }
    ICrashBreadcrumbSink Breadcrumbs { get; }
    ICrashContextSink Contexts { get; }
    ICrashSensitiveDataSink SensitiveData { get; }
    ICrashReportInbox Inbox { get; }
}
```

Add internal or public sealed no-op singletons for each sink and `EmptyCrashReportInbox`. They must not allocate per call and must never throw.

- [ ] **Step 4: Add the crash-report root**

In `AppPaths` add:

```csharp
public static string GetCrashReportsRoot()
{
    return Path.GetFullPath(Path.Combine(GetAppDataRoot(), "CrashReports"));
}
```

Do not create the directory here. Directory creation remains lazy inside `CrashReportStore`.

- [ ] **Step 5: Run Windows and macOS-compatible unit tests**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~AppPathsTests|FullyQualifiedName~CrashReportContractsTests"
```

On macOS also run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~AppPathsTests|FullyQualifiedName~CrashReportContractsTests"
```

Expected: all focused tests pass.

- [ ] **Step 6: Commit**

```bash
git add \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs \
  DTXMania.Game/Lib/Utilities/AppPaths.cs \
  DTXMania.Test/CrashReporting/CrashReportContractsTests.cs \
  DTXMania.Test/Utilities/AppPathsTests.cs
git commit -m "feat: define crash report contracts"
```

---

### Task 2: Implement centralized privacy policy and bounded in-memory diagnostics

**Files:**
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogFieldPolicy.cs`
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogBufferProvider.cs`
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashBreadcrumbBuffer.cs`
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextSnapshotStore.cs`
- Test: `DTXMania.Test/CrashReporting/CrashLogBufferProviderTests.cs`
- Test: `DTXMania.Test/CrashReporting/CrashBreadcrumbBufferTests.cs`
- Test: `DTXMania.Test/CrashReporting/CrashContextSnapshotStoreTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts.
- Produces:
  - `CrashLogFieldPolicy.Default`
  - internal immutable `CrashLogRecord`
  - `CrashLogBufferProvider.Snapshot()`
  - `CrashBreadcrumbBuffer.Snapshot()`
  - `CrashContextSnapshotStore.Snapshot()`
  - `CrashContextSnapshotStore.SensitivePathSnapshot()`

- [ ] **Step 1: Write failing log-policy tests**

Cover the security-critical behavior:

```csharp
[Fact]
public void UnknownRenderedMessage_ShouldBeOmitted()
{
    using var provider = new CrashLogBufferProvider(
        CrashLogFieldPolicy.Default,
        TimeProvider.System,
        capacity: 8);
    using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
    var logger = factory.CreateLogger("test");

    logger.LogInformation($"Loaded song Secret Song Name");

    var record = Assert.Single(provider.Snapshot());
    Assert.Equal("[UNCLASSIFIED MESSAGE OMITTED]", record.MessageTemplate);
    Assert.Empty(record.Properties);
}

[Fact]
public void UnknownStringValue_ShouldBeRedactedByCentralPolicy()
{
    var normalized = CrashLogFieldPolicy.Default.NormalizeProperty(
        propertyName: "Status",
        value: "Secret Song");

    Assert.Equal("[REDACTED]", normalized);
}
```

Also test:
- capacity drops oldest records;
- snapshot is an immutable copy;
- numeric, Boolean, enum, and `DateTimeOffset` values normalize deterministically;
- event/template mismatch results in an omitted template;
- exception type may be retained but exception message is not stored by the logger buffer.

- [ ] **Step 2: Write failing breadcrumb and context tests**

```csharp
[Fact]
public void BreadcrumbSnapshot_ShouldCopyMutableBuffer()
{
    var buffer = new CrashBreadcrumbBuffer(TimeProvider.System, capacity: 2);

    buffer.Record("stage_transition_requested",
        new Dictionary<string, object?> { ["TargetStage"] = StageType.Title });
    var snapshot = buffer.Snapshot();
    buffer.Record("stage_transition_completed",
        new Dictionary<string, object?> { ["TargetStage"] = StageType.Title });

    Assert.Single(snapshot);
    Assert.Equal("stage_transition_requested", snapshot[0].EventName);
}

[Fact]
public void ContextStore_ShouldReplaceOneSectionAtomically()
{
    var store = new CrashContextSnapshotStore();

    store.SetSnapshot(new CrashContextSnapshot(
        CrashContextKind.Stage,
        CrashContextStatus.Available,
        new Dictionary<string, object?> { ["Stage"] = StageType.Startup }));
    store.SetSnapshot(new CrashContextSnapshot(
        CrashContextKind.Stage,
        CrashContextStatus.Available,
        new Dictionary<string, object?> { ["Stage"] = StageType.Title }));

    var stage = Assert.Single(store.Snapshot(), item => item.Kind == CrashContextKind.Stage);
    Assert.Equal(StageType.Title, stage.Fields["Stage"]);
}
```

Also test duplicate sensitive paths are normalized and deduplicated with `AppPaths.SkinPathComparer`.

- [ ] **Step 3: Run the focused tests and verify failure**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~CrashLogBufferProviderTests|FullyQualifiedName~CrashBreadcrumbBufferTests|FullyQualifiedName~CrashContextSnapshotStoreTests"
```

Expected: compile failure because the buffers and policy do not exist.

- [ ] **Step 4: Implement `CrashLogFieldPolicy`**

Define one immutable policy. Do not let individual logger call sites decide privacy.

The default policy must:
- allow only named `EventId` values registered in one dictionary;
- preserve the registered message template, not `formatter(state, exception)`;
- read `{OriginalFormat}` from structured logger state;
- retain only these property names initially:

```text
Stage
PreviousStage
TargetStage
Milestone
Width
Height
Fullscreen
VSync
MidiDeviceCount
Enabled
Count
Status
```

- allow `bool`, integral numeric types, floating-point numeric types, enums, `DateTime`, `DateTimeOffset`, and `Guid`;
- convert unknown strings to `[REDACTED]`;
- convert unsupported objects to `[REDACTED]`;
- limit normalized text to 128 characters;
- represent unknown templates as `[UNCLASSIFIED MESSAGE OMITTED]`.

Use IDs `5100`–`5199` for crash-safe lifecycle events so they do not collide with ordinary event ID `0`.

- [ ] **Step 5: Implement the bounded log provider**

Use a lock-protected circular queue or `Queue<T>` with fixed capacity. `ILogger.Log` must:
1. extract `{OriginalFormat}` without rendering the message;
2. classify the event/template through `CrashLogFieldPolicy`;
3. copy only normalized allowed properties;
4. retain exception type and sanitized stack later, but not exception message;
5. append one immutable record and evict the oldest record when full.

Do not serialize JSON or allocate ZIP data on the log hot path.

- [ ] **Step 6: Implement breadcrumb and context stores**

`CrashBreadcrumbBuffer` uses the same scalar normalization policy but accepts only stable event names declared in a `HashSet<string>`. Unknown event names are recorded as `unknown_event` without caller properties.

`CrashContextSnapshotStore`:
- stores one immutable snapshot per `CrashContextKind`;
- replaces sections under a lock;
- returns copied arrays from `Snapshot()`;
- implements `ICrashSensitiveDataSink`;
- normalizes registered paths with `Path.GetFullPath` inside a narrow exception filter;
- never throws from `RegisterPath`.

- [ ] **Step 7: Run focused tests**

Run the Task 2 filter from Step 3.

Expected: all focused tests pass.

- [ ] **Step 8: Commit**

```bash
git add \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogFieldPolicy.cs \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashLogBufferProvider.cs \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashBreadcrumbBuffer.cs \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextSnapshotStore.cs \
  DTXMania.Test/CrashReporting/CrashLogBufferProviderTests.cs \
  DTXMania.Test/CrashReporting/CrashBreadcrumbBufferTests.cs \
  DTXMania.Test/CrashReporting/CrashContextSnapshotStoreTests.cs
git commit -m "feat: buffer crash-safe diagnostics"
```

---

### Task 3: Implement sanitization, report serialization, atomic storage, fallback, and retention

**Files:**
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportSanitizer.cs`
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportArchiveWriter.cs`
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportStore.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportSanitizerTests.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportStoreTests.cs`

**Interfaces:**
- Consumes:
  - `CrashReportSummary`
  - immutable log, breadcrumb, context, and sensitive-path snapshots from Tasks 1–2
- Produces:

```csharp
internal sealed record CrashCaptureData(
    Exception Exception,
    IReadOnlyList<CrashLogRecord> Logs,
    IReadOnlyList<CrashBreadcrumb> Breadcrumbs,
    IReadOnlyList<CrashContextSnapshot> Context,
    IReadOnlyList<string> SensitivePaths);

internal sealed record CrashReportWriteResult(
    CrashReportSummary? Report,
    bool UsedEmergencyFallback,
    string? FailureCode);

internal sealed record CrashReportDocument(
    CrashReportSummary Summary,
    Exception Exception,
    IReadOnlyList<CrashLogRecord> Logs,
    IReadOnlyList<CrashBreadcrumb> Breadcrumbs,
    IReadOnlyList<CrashContextSnapshot> Context,
    IReadOnlyList<string> SensitivePaths);

internal interface ICrashReportArtifactWriter
{
    void WriteZip(Stream destination, CrashReportDocument document);
    void WriteEmergencyText(Stream destination, CrashReportDocument document);
}

internal sealed class CrashReportStore
{
    CrashReportWriteResult Capture(CrashCaptureData data);
    IReadOnlyList<CrashReportSummary> DiscoverCompletedReports();
    void Cleanup();
}
```

- [ ] **Step 1: Write failing sanitizer tests**

Use deliberately sensitive values:

```csharp
[Fact]
public void SanitizeStackTrace_ShouldRemoveHomeAndSourcePaths()
{
    var home = Path.Combine(Path.GetTempPath(), "Users", "alice");
    var sanitizer = new CrashReportSanitizer(
        [home, Path.Combine(home, "Library", "Application Support", "DTXManiaCX")]);

    var input =
        $"at Example.Run() in {Path.Combine(home, "src", "Game.cs")}:line 42";

    var result = sanitizer.SanitizeStackTrace(input);

    Assert.DoesNotContain("alice", result, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Game.cs", result, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("[SOURCE]", result);
}

[Fact]
public void SanitizeExceptionMessage_ShouldOmitArbitraryContent()
{
    var sanitizer = new CrashReportSanitizer([]);

    Assert.Equal(
        "[EXCEPTION MESSAGE OMITTED]",
        sanitizer.SanitizeExceptionMessage("Failed to load Secret Song Name"));
}
```

Also test:
- API-key-like values;
- URI query strings;
- registered song and skin roots;
- Windows and Unix absolute paths;
- username segments;
- nested inner exceptions;
- sanitizer failure returns `[REDACTED]`, never original input.

- [ ] **Step 2: Write failing store tests for the complete contract**

Required tests:
1. ZIP contains exactly `report.json`, `exception.txt`, `logs.ndjson`, `breadcrumbs.json`, and `README.txt`.
2. `report.json` has schema version `1` and format-neutral summary fields.
3. ZIP is first written with a `.tmp` name and finalized in the same directory.
4. A throwing `ICrashReportArtifactWriter.WriteZip` produces one emergency `.txt` through `WriteEmergencyText`.
5. Emergency text starts with a versioned allowlisted header and can be discovered as `CrashReportFormat.EmergencyText`.
6. Corrupt emergency header falls back to filename ID/time and `Unknown` fields.
7. Six mixed ZIP/text reports retain exactly five newest reports.
8. Stale `.tmp` files older than 24 hours are deleted; newer temporary files are ignored, not treated as completed reports.
9. Discovery never reads or exposes the free-form emergency exception body.
10. Unwritable capture returns `Report = null` and a safe `FailureCode` without throwing.

Define `CrashStoreFixture` as a private test helper in `CrashReportStoreTests.cs`; it owns a temporary directory, fake `TimeProvider`, fake `ICrashReportArtifactWriter`, deterministic report IDs, and cleanup.

Example retention test:

```csharp
[Fact]
public void Capture_SixMixedReports_ShouldRetainNewestFiveAcrossFormats()
{
    using var fixture = CrashStoreFixture.Create();
    var store = fixture.CreateStore();

    for (var index = 0; index < 6; index++)
    {
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        fixture.ArtifactWriter.FailZip = index % 2 == 1;
        Assert.NotNull(store.Capture(fixture.CreateCapture(index)).Report);
    }

    var reports = store.DiscoverCompletedReports();

    Assert.Equal(5, reports.Count);
    Assert.DoesNotContain(reports, report => report.ReportId == fixture.ReportId(0));
}
```

- [ ] **Step 3: Run tests and verify failure**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~CrashReportSanitizerTests|FullyQualifiedName~CrashReportStoreTests"
```

Expected: compile failure because sanitizer, writer, and store do not exist.

- [ ] **Step 4: Implement conservative sanitization**

`CrashReportSanitizer` must:
- precompute normalized sensitive path prefixes;
- replace registered prefixes with `[PATH]`;
- strip source file portions from stack frames while retaining type/method and line number;
- remove URI query strings;
- replace absolute paths not already registered with `[PATH]`;
- replace likely username/home segments with `[USER]`;
- omit arbitrary exception messages by default as `[EXCEPTION MESSAGE OMITTED]`;
- permit only fixed crash-subsystem-generated failure codes and labels;
- bound every output field.

Do not attempt to infer whether an arbitrary phrase is a song title. Omission is the safe default.

- [ ] **Step 5: Implement versioned ZIP serialization**

`CrashReportArchiveWriter` implements `ICrashReportArtifactWriter`.

`WriteZip` writes:
- `report.json` — schema `1`, report ID, UTC timestamp, assembly informational version with assembly-version fallback, OS/runtime/architecture, summary, context statuses, included entries, truncation flags;
- `exception.txt` — exception type, omitted/sanitized message field, sanitized stack, bounded inner chain;
- `logs.ndjson` — newest records that fit both 500-entry and 512-KB limits;
- `breadcrumbs.json` — newest 100 breadcrumbs;
- `README.txt` — local-only generation, no automatic upload, inspect before attaching.

Use deterministic UTF-8 without BOM and `JsonSerializerOptions.WriteIndented = true` only for `report.json` and `breadcrumbs.json`.

`WriteEmergencyText` starts with this grammar; angle-bracket terms below are values supplied from `CrashReportDocument.Summary`, not unresolved implementation fields:

```text
DTXMANIACX-CRASH-REPORT 1
ReportId: <report-id-value>
CapturedAtUtc: <round-trip-utc-value>
BuildId: <sanitized-build-value>
OperatingSystem: <sanitized-os-value>
ProcessArchitecture: <architecture-value>
StageOrMilestone: <sanitized-value-or-Unknown>
ExceptionType: <exception-type-value>
---
```

The body after `---` is human-readable sanitized emergency content and is never parsed by title-screen code.

- [ ] **Step 6: Implement `CrashReportStore`**

Construction:

```csharp
internal CrashReportStore(
    string rootPath,
    ICrashReportArtifactWriter writer,
    TimeProvider timeProvider,
    TextWriter errorWriter)
```

Capture flow:
1. generate `crash-yyyyMMdd-HHmmssZ-<6 lowercase hex>.zip`;
2. lazily create the root;
3. write `.<report-id>.tmp` in the same directory;
4. flush/close;
5. `File.Move(temp, final, overwrite: false)`;
6. if ZIP writing/finalization fails, delete temp best-effort and repeat with `.txt`;
7. enforce combined retention;
8. return a summary or a fixed failure code.

Narrowly catch expected I/O/path exceptions. Do not catch `OutOfMemoryException`, `StackOverflowException`, or `AccessViolationException`.

Discovery:
- parse `report.json` from ZIP without extracting;
- parse only the emergency header before `---`;
- sort newest first by captured timestamp, then filename ordinal;
- never include `.tmp`.

- [ ] **Step 7: Run focused tests and both test project builds**

Run the Task 3 filter, then:

```bash
dotnet build DTXMania.Test/DTXMania.Test.csproj --configuration Debug
dotnet build DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug
```

Expected: focused tests pass and both test projects compile.

- [ ] **Step 8: Commit**

```bash
git add \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportSanitizer.cs \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportArchiveWriter.cs \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportStore.cs \
  DTXMania.Test/CrashReporting/CrashReportSanitizerTests.cs \
  DTXMania.Test/CrashReporting/CrashReportStoreTests.cs
git commit -m "feat: persist sanitized crash reports"
```

---

### Task 4: Compose the process runtime and replace implicit game disposal

**Files:**
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportRuntime.cs`
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/GameEntryPoint.cs`
- Modify: `DTXMania.Game/Program.cs`
- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.Game/Lib/Stage/IStageGame.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportRuntimeTests.cs`
- Test: `DTXMania.Test/CrashReporting/GameEntryPointTests.cs`
- Test: `DTXMania.Test/BaseGameTests.cs`
- Test: `DTXMania.Test/Stage/IStageGameContractTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces:
  - `CrashReportRuntime.CreateBestEffort(StartupTimingTrace, TextWriter)`
  - `CrashReportRuntime.GameDiagnostics`
  - `CrashReportRuntime.CaptureFatal(Exception)`
  - `CrashReportRuntime.RecordSecondaryFailure(string, Exception)`
  - `IGameApplication`
  - `ICrashRuntimeLifetime`
  - `GameEntryPoint.Run(Func<IGameApplication>, ICrashRuntimeLifetime, TextWriter)`
  - production constructors:
    - `Game1(StartupTimingTrace, IGameCrashDiagnostics)`
    - `BaseGame(StartupTimingTrace, IGameCrashDiagnostics)`

- [ ] **Step 1: Write runtime-degradation tests**

```csharp
[Fact]
public void CreateBestEffort_WhenStoreFactoryFails_ShouldPreserveConsoleLogger()
{
    using var errorWriter = new StringWriter();
    using var runtime = CrashReportRuntime.CreateBestEffort(
        StartupTimingTrace.Disabled,
        errorWriter,
        storeFactory: () => throw new UnauthorizedAccessException("denied"));

    var logger = runtime.GameDiagnostics.LoggerFactory.CreateLogger("probe");
    var exception = Record.Exception(() => logger.LogInformation("console survives"));

    Assert.Null(exception);
    Assert.False(runtime.IsCaptureEnabled);
    Assert.Same(EmptyCrashReportInbox.Instance, runtime.GameDiagnostics.Inbox);
    Assert.Contains("crash_reporting_disabled", errorWriter.ToString());
}
```

Also test:
- enabled runtime installs both console and crash providers;
- `CaptureFatal` never throws when store capture fails;
- runtime owns/disposes factory exactly once;
- disabled mode does not allocate report files;
- `OutOfMemoryException` from the injected store factory is not swallowed.

Use an internal test overload for dependency injection; keep the production overload small.

- [ ] **Step 2: Write lifecycle tests before changing `Program.cs`**

Define:

```csharp
internal interface IGameApplication : IDisposable
{
    void Run();
}

internal interface ICrashRuntimeLifetime : IDisposable
{
    void CaptureFatal(Exception exception);
    void RecordSecondaryFailure(string failureCode, Exception exception);
}
```

Test the exact ordering with fakes:

```csharp
[Fact]
public void Run_WhenGameThrows_ShouldCaptureBeforeDisposeAndPreserveOriginalFailure()
{
    var calls = new List<string>();
    var game = new FakeGameApplication(
        run: () => { calls.Add("run"); throw new InvalidOperationException("fatal"); },
        dispose: () => calls.Add("dispose"));
    var runtime = new FakeCrashRuntime(
        capture: _ => calls.Add("capture"),
        dispose: () => calls.Add("runtime_dispose"));

    var exitCode = GameEntryPoint.Run(
        () => game,
        runtime,
        TextWriter.Null);

    Assert.Equal(1, exitCode);
    Assert.Equal(
        ["run", "capture", "dispose", "runtime_dispose"],
        calls);
}
```

Add tests where:
- game construction throws;
- capture reports an internal failure;
- game disposal throws;
- runtime disposal throws;
- normal run returns `0`;
- the original exception type is passed to `CaptureFatal`;
- a secondary disposal failure is reported without replacing the original.

- [ ] **Step 3: Run tests and verify failure**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~CrashReportRuntimeTests|FullyQualifiedName~GameEntryPointTests|FullyQualifiedName~BaseGameTests.LoggerFactory"
```

Expected: compile failure because runtime/lifecycle types and injected constructors do not exist.

- [ ] **Step 4: Implement enabled and console-only runtime composition**

`CrashReportRuntime` owns:
- one `ILoggerFactory`;
- optional `CrashLogBufferProvider`;
- optional `CrashBreadcrumbBuffer`;
- optional `CrashContextSnapshotStore`;
- optional `CrashReportStore`;
- one immutable `IGameCrashDiagnostics` facade.

Production bootstrap:

```csharp
public static CrashReportRuntime CreateBestEffort(
    StartupTimingTrace startupTrace,
    TextWriter? errorWriter = null)
```

The production method must:
1. construct crash components that perform no report-directory I/O;
2. create one logger factory with `AddConsole()` and, when enabled, `AddProvider(crashLogBufferProvider)`;
3. on an expected crash-component or provider setup failure, dispose the partial factory if one exists and create one fresh console-only factory;
4. write one sanitized `crash_reporting_disabled code=<fixed-code>` line to `errorWriter ?? Console.Error`;
5. return a disabled runtime.

Use a narrow expected-exception filter. Do not use `catch (Exception)` for bootstrap degradation.

`CaptureFatal`:
- takes immutable copies of logs, breadcrumbs, contexts, and sensitive paths;
- delegates to `CrashReportStore.Capture`;
- catches only expected report I/O/serialization failures;
- writes a fixed sanitized stderr failure line;
- never rethrows a report failure over the game failure.

- [ ] **Step 5: Replace game construction and ownership**

Change production constructors to:

```csharp
internal BaseGame(
    StartupTimingTrace startupTimingTrace,
    IGameCrashDiagnostics crashDiagnostics)
{
    _startupTimingTrace =
        startupTimingTrace ?? throw new ArgumentNullException(nameof(startupTimingTrace));
    _gameCrashDiagnostics =
        crashDiagnostics ?? throw new ArgumentNullException(nameof(crashDiagnostics));

    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    _graphicsDeviceManager = new GraphicsDeviceManager(this);
    Content.RootDirectory = "Content";
    IsMouseVisible = true;

    _loggerFactory = _gameCrashDiagnostics.LoggerFactory;
    _logger = _loggerFactory.CreateLogger<BaseGame>();
}

public class Game1 : BaseGame, IGameApplication
{
    internal Game1(
        StartupTimingTrace startupTimingTrace,
        IGameCrashDiagnostics crashDiagnostics)
        : base(startupTimingTrace, crashDiagnostics)
    {
    }
}
```

Remove production parameterless constructors. Do not add a constructor that creates its own logger factory.

Remove `_loggerFactory.Dispose()` from `BaseGame.DisposeManagedResources()`.

Expose only this additional stage-facing property through `IStageGame`:

```csharp
ICrashReportInbox CrashReportInbox { get; }
```

Do not expose the full runtime, store, or context registry through `IStageGame`.

- [ ] **Step 6: Implement explicit entry-point lifecycle**

`Program.cs` becomes:

```csharp
using DTXMania.Game;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;

var startupTrace = StartupTimingTrace.StartProcess();
var crashRuntime = CrashReportRuntime.CreateBestEffort(startupTrace, Console.Error);

return GameEntryPoint.Run(
    () => new Game1(startupTrace, crashRuntime.GameDiagnostics),
    crashRuntime,
    Console.Error);
```

`GameEntryPoint.Run` uses an explicit nullable game variable. It captures inside `catch` before entering guarded disposal. `finally` disposes the game and runtime independently. Do not use `using var`.

- [ ] **Step 7: Update constructor and disposal tests**

Update `BaseGameTests` helpers to inject a `TestGameCrashDiagnostics` or console-only diagnostics facade rather than setting `_loggerFactory` after construction where real construction is exercised.

Keep `ReflectionHelpers.CreateUninitialized<T>()` for tests that intentionally bypass the MonoGame constructor.

Add an assertion to disposal tests:

```csharp
loggerFactory.Verify(factory => factory.Dispose(), Times.Never);
```

when using a mock factory owned by the test runtime.

- [ ] **Step 8: Run focused and full unit tests**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~CrashReportRuntimeTests|FullyQualifiedName~GameEntryPointTests|FullyQualifiedName~BaseGameTests|FullyQualifiedName~IStageGameContractTests"
```

Then:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --configuration Debug
```

Expected: all tests pass.

- [ ] **Step 9: Commit**

```bash
git add \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportRuntime.cs \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/GameEntryPoint.cs \
  DTXMania.Game/Program.cs \
  DTXMania.Game/Game1.cs \
  DTXMania.Game/Lib/Stage/IStageGame.cs \
  DTXMania.Test/CrashReporting/CrashReportRuntimeTests.cs \
  DTXMania.Test/CrashReporting/GameEntryPointTests.cs \
  DTXMania.Test/BaseGameTests.cs \
  DTXMania.Test/Stage/IStageGameContractTests.cs
git commit -m "feat: capture fatal game exceptions"
```

---

### Task 5: Publish cached game context and semantic breadcrumbs

**Files:**
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs`
- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.Game/Lib/Stage/StageManager.cs`
- Modify: `DTXMania.Game/Lib/Input/Midi/MidiInputSource.cs`
- Modify: `DTXMania.Game/Lib/Input/ModularInputManager.cs`
- Test: `DTXMania.Test/BaseGameTests.cs`
- Test: `DTXMania.Test/Stage/StageManagerTransitionTests.cs`
- Test: `DTXMania.Test/Input/Midi/MidiInputSourceTests.cs`
- Test: `DTXMania.Test/Input/ModularInputManagerTests.cs`

**Interfaces:**
- Consumes: `IGameCrashDiagnostics` and sinks from Tasks 1–4.
- Produces cached sections for process/application/startup/configuration/stage/graphics/audio/input and stable breadcrumb events.

- [ ] **Step 1: Write failing context tests**

Lock the initial configuration mapping:

```csharp
[Fact]
public void PublishConfigurationContext_ShouldUseOnlyApprovedConcreteFields()
{
    var diagnostics = new RecordingGameCrashDiagnostics();
    var config = new ConfigData
    {
        ScreenWidth = 1920,
        ScreenHeight = 1080,
        FullScreen = true,
        VSyncWait = false,
        BufferSizeMs = 80,
        AutoPlay = true,
        NoFail = true,
        EnableGameApi = true,
        GameApiKey = "must-not-appear",
        DTXPath = @"C:\Secret\Songs",
        SkinPath = @"C:\Secret\Skin"
    };
    config.KeyBindings["Snare"] = 1;

    CrashContextPublisher.PublishConfiguration(diagnostics, config);

    var snapshot = diagnostics.Contexts.Single(CrashContextKind.Configuration);
    Assert.Equal(1920, snapshot.Fields["ScreenWidth"]);
    Assert.Equal(1, snapshot.Fields["KeyBindingCount"]);
    Assert.DoesNotContain(snapshot.Fields, field => field.Key.Contains("Key", StringComparison.Ordinal) && field.Value?.ToString() == "must-not-appear");
    Assert.DoesNotContain(snapshot.Fields.Values, value => value?.ToString()?.Contains("Secret", StringComparison.Ordinal) == true);
}
```

Use `CrashContextPublisher.PublishConfiguration` as the concrete helper; keep the class internal and deterministic.

- [ ] **Step 2: Write failing stage and input tests**

Stage manager tests must verify exact stable events:

```csharp
[Fact]
public void ChangeStage_ShouldRecordRequestedAndCompletedBreadcrumbs()
{
    var breadcrumbs = new RecordingBreadcrumbSink();
    var contexts = new RecordingContextSink();
    var manager = new StageManager(
        CreateStageGame(),
        NullLogger<StageManager>.Instance,
        breadcrumbs,
        contexts);

    manager.ChangeStage(StageType.Title);

    Assert.Contains(breadcrumbs.Events,
        item => item.EventName == "stage_transition_requested");
    Assert.Contains(breadcrumbs.Events,
        item => item.EventName == "stage_transition_completed");
    Assert.Equal(
        StageType.Title,
        contexts.Single(CrashContextKind.Stage).Fields["Stage"]);
}
```

Input tests:
- `MidiInputSource.DeviceCount` is count-only and thread-safe;
- `ModularInputManager.ConnectedMidiDeviceCount` returns zero before devices and current count after refresh;
- no device names or stable IDs appear in context/breadcrumb properties.

- [ ] **Step 3: Run focused tests and verify failure**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~BaseGameTests|FullyQualifiedName~StageManagerTransitionTests|FullyQualifiedName~MidiInputSourceTests|FullyQualifiedName~ModularInputManagerTests"
```

Expected: focused new tests fail because instrumentation is absent.

- [ ] **Step 4: Publish process and application context at runtime creation**

In `CrashReportRuntime` seed:
- runtime framework description;
- OS description;
- process architecture;
- process start UTC;
- application informational version with assembly-version fallback.

Use fixed field names registered in `CrashLogFieldPolicy`. Do not include command-line arguments or environment variables.

- [ ] **Step 5: Publish startup and configuration snapshots from `BaseGame`**

At existing lifecycle points:
- after config load: publish approved config fields and register `DTXPath`, `SkinPath`, app-data root, config path, database path, playback cache root, and crash root only as sensitive prefixes;
- after `InputManager` construction: set input section;
- after graphics initialization: set graphics section;
- in `ReportStartupActivated`, `ReportStartupFrameRendered`, and `ReportStartupSummaryAndTitleRequested`: update a fixed `Milestone` value;
- when no global audio-device abstraction exists: publish `CrashContextStatus.Unavailable` for `Audio` with fixed `FailureCode = "audio_device_summary_unavailable"`; do not perform active discovery.

The configuration section may contain only:
- `ScreenWidth`
- `ScreenHeight`
- `FullScreen`
- `VSyncWait`
- `BufferSizeMs`
- `AutoPlay`
- `NoFail`
- `EnableGameApi`
- `KeyBindingCount`
- `SystemKeyBindingCount`
- `UnboundDrumLaneCount`
- `UnboundDrumButtonCount`
- `MidiVelocityThresholdCount`

- [ ] **Step 6: Instrument graphics events**

Publish count/enum-only graphics values:
- width;
- height;
- fullscreen;
- VSync;
- back-buffer format enum;
- depth-stencil format enum;
- MSAA count;
- device availability.

Record fixed breadcrumbs:
- `graphics_device_lost`
- `graphics_device_reset`
- `graphics_settings_changed`

Do not use `GraphicsSettings.ToString()` because it creates an uncontrolled rendered string.

- [ ] **Step 7: Instrument stage transitions centrally**

Extend the current constructor without breaking logger-only tests:

```csharp
public StageManager(
    IStageGame game,
    ILogger<StageManager>? logger = null,
    ICrashBreadcrumbSink? breadcrumbs = null,
    ICrashContextSink? contexts = null)
```

Default missing sinks to no-op singletons.

Record:
- `stage_transition_requested` after transition validation and before start;
- `stage_transition_completed` after successful activation;
- stage context after activation;
- `stage_transition_rejected` only with fixed reason enum/string from a closed set.

Never include shared-data values or song identity.

Update `BaseGame.CreateLoadContentServices()` to create the manager as:

```csharp
new StageManager(
    this,
    _loggerFactory.CreateLogger<StageManager>(),
    _gameCrashDiagnostics.Breadcrumbs,
    _gameCrashDiagnostics.Contexts)
```

- [ ] **Step 8: Add count-only MIDI context**

Add:

```csharp
public int DeviceCount
{
    get
    {
        lock (_sync)
            return _devices.Count;
    }
}
```

to `MidiInputSource`, and:

```csharp
public int ConnectedMidiDeviceCount => _midiInputSource?.DeviceCount ?? 0;
```

to `ModularInputManager`.

In `BaseGame.Update`, after `InputManager.Update`, compare the current count with a cached previous count. Only on change:
- update the input context with `MidiDeviceCount`;
- record `midi_device_count_changed` with the count.

Do not call `RefreshDevices`, `DeviceNames`, or the MIDI backend from crash capture.

- [ ] **Step 9: Register crash-safe event IDs**

Where recent logs add value, use fixed IDs from `5100`–`5199` and templates registered in `CrashLogFieldPolicy`, for example:

```csharp
private static readonly EventId StageTransitionRequestedEvent =
    new(5110, "StageTransitionRequested");

_logger.LogDebug(
    StageTransitionRequestedEvent,
    "Stage transition requested: {PreviousStage} -> {TargetStage}",
    previousStageType ?? StageType.Startup,
    stageType);
```

Do not convert every existing log call. Unregistered logs remain category/level/timestamp records with omitted messages.

- [ ] **Step 10: Run focused and full unit tests**

Run the Step 3 filter, then both platform unit suites:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --configuration Debug
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug
```

Expected: all tests pass.

- [ ] **Step 11: Commit**

```bash
git add \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs \
  DTXMania.Game/Game1.cs \
  DTXMania.Game/Lib/Stage/StageManager.cs \
  DTXMania.Game/Lib/Input/Midi/MidiInputSource.cs \
  DTXMania.Game/Lib/Input/ModularInputManager.cs \
  DTXMania.Test/BaseGameTests.cs \
  DTXMania.Test/Stage/StageManagerTransitionTests.cs \
  DTXMania.Test/Input/Midi/MidiInputSourceTests.cs \
  DTXMania.Test/Input/ModularInputManagerTests.cs
git commit -m "feat: capture cached game crash context"
```

---

### Task 6: Prove end-to-end local capture, privacy, and fallback without MonoGame graphics

**Files:**
- Test: `DTXMania.Test/CrashReporting/CrashReportIntegrationTests.cs`
- Test helper (inside the same file): `TemporaryAppDataRoot` and `CrashReportTestReader`
- No production file change is expected; a production correction belongs in the task that introduced the defective contract and must retain that task's focused regression test.

**Interfaces:**
- Consumes: all prior production components.
- Produces: deterministic integration proof using `DTXMANIA_APPDATA_ROOT` and fake `IGameApplication`.

- [ ] **Step 1: Write the pre-graphics integration test**

Inside `CrashReportIntegrationTests.cs`, define `TemporaryAppDataRoot` as a disposable temporary-directory helper and `CrashReportTestReader.ReadAllText(string path)` to concatenate the approved ZIP entries or read the emergency text file. These helpers must not live in production code.

```csharp
[Fact]
public void Run_WhenFactoryThrowsBeforeGameConstruction_ShouldWriteOneSanitizedReport()
{
    using var appData = new TemporaryAppDataRoot();
    var previous = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");

    try
    {
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", appData.Path);
        var runtime = CrashReportRuntime.CreateBestEffort(
            StartupTimingTrace.Disabled,
            TextWriter.Null);

        var exitCode = GameEntryPoint.Run(
            () => throw new InvalidOperationException(
                $"song={Path.Combine(appData.Path, "Secret Song.dtx")}"),
            runtime,
            TextWriter.Null);

        Assert.Equal(1, exitCode);
        var report = Assert.Single(
            Directory.EnumerateFiles(AppPaths.GetCrashReportsRoot(), "crash-*"));
        var allText = CrashReportTestReader.ReadAllText(report);
        Assert.DoesNotContain("Secret Song", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(appData.Path, allText, StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
        Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previous);
    }
}
```

- [ ] **Step 2: Add integration tests for failure isolation**

Add tests for:
- one context snapshot marked `CollectionFailed` does not block the report;
- archive writer failure creates discoverable emergency text;
- both ZIP and text discovery expose `CrashReportSummary`;
- runtime bootstrap factory failure leaves game runner and console logger functional;
- game disposal failure does not change exit code or report’s primary exception type;
- six captures through `CrashReportRuntime` retain exactly five;
- no `.tmp` remains after successful or fallback capture;
- report text contains none of:
  - `GameApiKey`;
  - test username;
  - DTX/skin paths;
  - song title;
  - MIDI stable ID;
  - arbitrary rendered log string.

- [ ] **Step 3: Run integration tests and inspect one generated artifact**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~CrashReportIntegrationTests"
```

Keep one test artifact under the test result directory only when a test fails. Successful tests clean temporary app-data roots.

Expected: all integration tests pass.

- [ ] **Step 4: Run both unit suites**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --configuration Debug
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug
```

Expected: both suites pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Test/CrashReporting/CrashReportIntegrationTests.cs
git commit -m "test: verify crash report integration"
```

---

### Task 7: Add WindowsDX real-process `Update` and `Draw` crash verification

**Files:**
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/DebugCrashInjection.cs`
- Modify: `DTXMania.Game/Game1.cs`
- Modify: `DTXMania.E2E/Process/GameProcessDriver.cs`
- Create: `DTXMania.E2E/CrashReportingSmokeTests.cs`
- Modify: `.github/workflows/build-and-test.yml` only if required to isolate timeout/artifacts.

**Interfaces:**
- Consumes: process boundary and store from Tasks 1–6.
- Produces:
  - `DEBUG`-only `DTXMANIA_E2E_CRASH_INJECTION=update|draw`
  - `GameProcessDriver.Start(..., IReadOnlyDictionary<string,string?>? environmentOverrides)`
  - `GameProcessDriver.WaitForExitAsync(...)`
  - Windows CI proof for both callback paths.

- [ ] **Step 1: Write E2E support tests for process environment overrides**

Extend `GameProcessDriver` with:

```csharp
public void Start(
    string repoRoot,
    string gameProjectPath,
    E2EFixture fixture,
    bool enableSimulatedMidi = false,
    IReadOnlyDictionary<string, string?>? environmentOverrides = null)
```

Rules:
- `null` value removes an inherited variable;
- non-null value sets it;
- reserved fixture variables may not be overridden:
  - `DTXMANIA_APPDATA_ROOT`
  - `DTXMANIA_LAUNCH_TOKEN`.

Add:

```csharp
public async Task<int> WaitForExitAsync(
    TimeSpan timeout,
    CancellationToken cancellationToken)
```

that waits, drains redirected output, and returns the exit code.

Write `Category=E2E-Support` tests before implementation.

- [ ] **Step 2: Implement a `DEBUG`-only injection hook**

`DebugCrashInjection.cs` must be entirely wrapped in:

```csharp
#if DEBUG
...
#endif
```

It reads `DTXMANIA_E2E_CRASH_INJECTION` once, accepts only `update` or `draw`, and throws one fixed exception exactly once from the matching callback:

```csharp
throw new InvalidOperationException("DTXMANIA_E2E_CONTROLLED_CRASH");
```

Do not include song/config/path values. Release compilation must contain no reference to the environment variable or controlled exception text.

Call the hook:
- at the beginning of `BaseGame.Update` after the process has entered MonoGame update execution;
- at the beginning of `BaseGame.Draw` before graphics-dependent work.

- [ ] **Step 3: Write Windows crash smoke tests**

Add a private `ReadCrashSummary(string reportPath)` helper to `CrashReportingSmokeTests.cs`. It must open `report.json` from ZIP or parse only the emergency header before `---` and return a `CrashReportSummary`. Do not reference internal production parser types from the E2E assembly.

For both `update` and `draw`, the test must:
1. build an isolated fixture with `E2EFixtureBuilder`;
2. start the Windows game project with `DTXMANIA_E2E_CRASH_INJECTION` set to the theory value;
3. await `WaitForExitAsync` and assert a non-zero exit code;
4. enumerate `<fixture.AppDataRoot>/CrashReports` and assert exactly one completed `.zip` or `.txt`;
5. assert no `.tmp` exists;
6. call `ReadCrashSummary` and assert `ExceptionType == "InvalidOperationException"`;
7. inspect `exception.txt` or the emergency body and assert `DTXMANIA_E2E_CONTROLLED_CRASH` appears exactly once;
8. copy stdout, stderr, and report files to `fixture.ArtifactRoot` in the test's `finally` block.

Use this concrete test skeleton:

```csharp
[Theory(Timeout = 180_000)]
[InlineData("update")]
[InlineData("draw")]
public async Task ControlledCallbackCrash_ShouldReachProgramBoundaryExactlyOnce(
    string injectionPoint)
{
    var repoRoot = FindRepoRoot();
    var runRoot = Path.Combine(
        Path.GetTempPath(),
        "dtx-crash-e2e-" + Guid.NewGuid().ToString("N"));
    var fixture = E2EFixtureBuilder.Build(
        runRoot,
        repoRoot,
        GetAvailablePort());
    await using var process = new GameProcessDriver();

    try
    {
        process.Start(
            repoRoot,
            "DTXMania.Game/DTXMania.Game.Windows.csproj",
            fixture,
            environmentOverrides: new Dictionary<string, string?>
            {
                ["DTXMANIA_E2E_CRASH_INJECTION"] = injectionPoint
            });

        var exitCode = await process.WaitForExitAsync(
            TimeSpan.FromSeconds(120),
            CancellationToken.None);
        Assert.NotEqual(0, exitCode);

        var reportRoot = Path.Combine(fixture.AppDataRoot, "CrashReports");
        var reports = Directory.EnumerateFiles(reportRoot, "crash-*")
            .Where(path => Path.GetExtension(path) is ".zip" or ".txt")
            .ToArray();
        var reportPath = Assert.Single(reports);
        Assert.Empty(Directory.EnumerateFiles(reportRoot, "*.tmp"));

        var summary = ReadCrashSummary(reportPath);
        Assert.Equal("InvalidOperationException", summary.ExceptionType);
        Assert.Equal(
            1,
            ReadPrimaryExceptionText(reportPath)
                .Split("DTXMANIA_E2E_CONTROLLED_CRASH")
                .Length - 1);
    }
    finally
    {
        await E2EArtifactWriter.WriteTextAsync(
            fixture,
            $"crash-{injectionPoint}-stdout.log",
            process.StandardOutput);
        await E2EArtifactWriter.WriteTextAsync(
            fixture,
            $"crash-{injectionPoint}-stderr.log",
            process.StandardError);
    }
}
```

Use the existing overloads from `E2EFixtureBuilder`; adjust only the fixture builder arguments needed by the current signature, not the test behavior above.

- [ ] **Step 4: Run E2E support tests**

On Windows:

```powershell
dotnet test DTXMania.E2E/DTXMania.E2E.csproj `
  --configuration Debug `
  --filter "Category=E2E-Support"
```

Expected: support tests pass.

- [ ] **Step 5: Run crash smoke tests locally on Windows**

```powershell
$env:ALSOFT_DRIVERS = "null"
$env:DTXMANIA_E2E_GAME_PROJECT = "DTXMania.Game/DTXMania.Game.Windows.csproj"
dotnet test DTXMania.E2E/DTXMania.E2E.csproj `
  --configuration Debug `
  --filter "FullyQualifiedName~CrashReportingSmokeTests"
```

Expected: update and draw cases pass.

- [ ] **Step 6: Verify Release excludes the injection hook**

Build Release:

```powershell
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Release
```

Inspect the source-generated build output or use `strings`/PowerShell binary search on the built game assembly and assert it does not contain:

```text
DTXMANIA_E2E_CRASH_INJECTION
DTXMANIA_E2E_CONTROLLED_CRASH
```

Record the command and result in the implementation PR.

- [ ] **Step 7: Keep CI execution explicit**

The existing workflow already runs `Category=E2E` on Windows. If the two crash tests keep the total job within its current budget, no workflow edit is needed.

If isolation is required, add a named step after gameplay smoke:

```yaml
- name: Run crash reporting E2E
  env:
    ALSOFT_DRIVERS: 'null'
    DTXMANIA_E2E_GAME_PROJECT: DTXMania.Game/DTXMania.Game.Windows.csproj
    DTXMANIA_E2E_ARTIFACT_ROOT: TestResults/e2e-crash
  run: dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug --verbosity normal --logger trx --results-directory ./TestResults/e2e-crash --filter "FullyQualifiedName~CrashReportingSmokeTests"
```

When adding this step, exclude `CrashReportingSmokeTests` from the existing broad E2E step to avoid duplicate execution.

- [ ] **Step 8: Commit**

```bash
git add \
  DTXMania.Game/Lib/Diagnostics/CrashReporting/DebugCrashInjection.cs \
  DTXMania.Game/Game1.cs \
  DTXMania.E2E/Process/GameProcessDriver.cs \
  DTXMania.E2E/CrashReportingSmokeTests.cs \
  .github/workflows/build-and-test.yml
git commit -m "test: verify Windows crash propagation"
```

If the workflow file did not change, omit it from `git add`.

---

### Task 8: Record DesktopGL evidence and run final acceptance gates

**Files:**
- Create after executing the checks: `docs/verification/hpa-529-macos-crash-propagation.md`
- Modify only for defects discovered by verification: files from Tasks 1–7

**Interfaces:**
- Consumes: complete HPA-529 implementation.
- Produces: recorded Apple Silicon evidence and final acceptance evidence.

- [ ] **Step 1: Build the real macOS game in Debug and Release**

On Apple Silicon:

```bash
git rev-parse HEAD
sw_vers
uname -m
dotnet --info
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Release
```

Expected:
- architecture reports `arm64`;
- both builds succeed;
- Release assembly does not contain the debug injection variable or controlled exception text.

- [ ] **Step 2: Verify DesktopGL `Update` propagation**

```bash
run_root="$(mktemp -d /tmp/dtx-hpa529-update.XXXXXX)"
export DTXMANIA_APPDATA_ROOT="$run_root"
export DTXMANIA_E2E_CRASH_INJECTION="update"
set +e
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
exit_code=$?
set -e
test "$exit_code" -ne 0
find "$run_root/CrashReports" -maxdepth 1 -type f -name 'crash-*' -print
```

Assert:
- non-zero exit;
- exactly one completed `.zip` or `.txt`;
- zero `.tmp`;
- summary exception type is `InvalidOperationException`.

- [ ] **Step 3: Verify DesktopGL `Draw` propagation**

Repeat Step 2 with a fresh root and:

```bash
run_root="$(mktemp -d /tmp/dtx-hpa529-draw.XXXXXX)"
export DTXMANIA_APPDATA_ROOT="$run_root"
export DTXMANIA_E2E_CRASH_INJECTION="draw"
set +e
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
exit_code=$?
set -e
test "$exit_code" -ne 0
find "$run_root/CrashReports" -maxdepth 1 -type f -name 'crash-*' -print
```

Assert the same conditions as the update run.

- [ ] **Step 4: Inspect privacy-sensitive output**

For ZIP:

```bash
report_path="$(find "$DTXMANIA_APPDATA_ROOT/CrashReports" -maxdepth 1 -type f -name 'crash-*.zip' | head -n 1)"
test -n "$report_path"
unzip -p "$report_path" report.json
unzip -p "$report_path" exception.txt
unzip -p "$report_path" logs.ndjson
unzip -p "$report_path" breadcrumbs.json
```

For emergency text:

```bash
report_path="$(find "$DTXMANIA_APPDATA_ROOT/CrashReports" -maxdepth 1 -type f -name 'crash-*.txt' | head -n 1)"
test -n "$report_path"
sed -n '1,80p' "$report_path"
```

Confirm:
- no home path;
- no app-data path;
- no song/skin path;
- no API key;
- no username;
- no arbitrary rendered log content;
- report ID/time/build/OS/architecture/stage-or-milestone/exception type are present.

- [ ] **Step 5: Write the verification note with actual output**

Create `docs/verification/hpa-529-macos-crash-propagation.md` containing:
- commit SHA from `git rev-parse HEAD`;
- `sw_vers`, `uname -m`, and relevant `dotnet --info` lines;
- exact commands run;
- actual exit codes;
- actual generated report filenames and formats;
- update result;
- draw result;
- Release injection-string absence result;
- privacy inspection result.

Do not write placeholders. Copy the observed values from the commands.

- [ ] **Step 6: Run final unit and build gates**

On macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Release
```

On Windows or CI:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj --configuration Debug
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug --filter "Category=E2E-Support"
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --configuration Debug --filter "FullyQualifiedName~CrashReportingSmokeTests"
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Release
```

Expected: zero failed tests and both Release builds succeed.

- [ ] **Step 7: Perform a final report-schema and retention audit**

Using an isolated `DTXMANIA_APPDATA_ROOT`, generate six reports with at least one emergency fallback through an integration test hook. Confirm:
- exactly five completed reports remain;
- ZIP and text share one ordering;
- no temporary files remain;
- every report is discoverable as a `CrashReportSummary`;
- report failure never prevents ordinary console logging.

- [ ] **Step 8: Commit verification evidence**

```bash
git add docs/verification/hpa-529-macos-crash-propagation.md
git commit -m "docs: record macOS crash propagation"
```

---

## Final Self-Review Checklist

Before marking HPA-529 complete, verify every item with fresh command output:

- [ ] Production has no parameterless `Game1` or `BaseGame` constructor that self-creates diagnostics.
- [ ] `CrashReportRuntime` owns and disposes the shared logger factory exactly once.
- [ ] Disabled crash capture still emits ordinary console logs.
- [ ] `Program.cs` contains explicit capture-before-dispose lifecycle code and no `using var game`.
- [ ] Fatal capture uses immutable copies of cached state only.
- [ ] Context capture performs no device enumeration, SQLite access, network access, shell launch, graphics call, or cross-thread wait.
- [ ] Unknown logs and dynamic strings are omitted or redacted before persistence.
- [ ] The full `ConfigData` object is never serialized.
- [ ] ZIP schema contains exactly the five approved entries.
- [ ] Emergency text has a parseable allowlisted header and a free-form body that UI code never parses.
- [ ] One latest-five quota covers ZIP and emergency text.
- [ ] WindowsDX `Update` and `Draw` propagation passes in automated E2E.
- [ ] DesktopGL `Update` and `Draw` propagation has recorded Apple Silicon evidence.
- [ ] Release builds contain no debug crash-injection variable or controlled exception string.
- [ ] Windows and macOS unit suites pass.
- [ ] Windows and macOS Release builds succeed.
- [ ] HPA-530 can consume `CrashReportSummary` and `ICrashReportInbox` without parsing report files or implementing retention.
