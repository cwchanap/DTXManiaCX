# HPA-192 Batched Song Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce fresh-startup loading for the fixed 100-chart corpus by at least 70 percent by parsing first and committing the complete library through one EF Core context, one explicit SQLite transaction, and one `SaveChangesAsync` call.

**Architecture:** `SongManager` will collect a complete, temporary enumeration batch and hierarchy without writing SQLite. `SongDatabaseService` will reconcile that batch against one eagerly loaded entity graph, save atomically, and return a normalized-path-to-entity map that lets `SongManager` hydrate and publish the pending hierarchy without reload queries. `StartupStage` will select the enumeration or cache path once, advance phases on completed work, and emit one Release-safe aggregate summary after at least one rendered startup frame.

**Tech Stack:** .NET 8, C# 12, MonoGame 3.8, Entity Framework Core SQLite, Microsoft.Data.Sqlite, xUnit, Moq, macOS shell tooling.

## Global Constraints

- Keep production `_forceEnumeration = true`; enabling or repairing cached-startup selection is outside HPA-192.
- Do not redesign parser I/O or parse charts in parallel.
- Do not introduce concurrent SQLite writers or multiple persistence batches.
- Store normalized absolute chart paths and use Windows case-insensitive comparison only on Windows; use ordinal comparison on macOS and Linux.
- Preserve existing `Song.Id`, `SongChart.Id`, chart-to-song associations, bookmarks, every score speed variant, recent-play fields, and performance history.
- Preserve an existing `SongChart.FileHash`; do not calculate MD5 for a new bulk-imported chart.
- Add a chart path to the discovery set before parsing it.
- Do not import or delete stale records after cancellation, root traversal failure, or an otherwise incomplete enumeration.
- Do not invoke the old `CleanupStaleChartsAsync` from either startup path.
- Use exactly one `SongDbContext`, one explicit transaction, and one `SaveChangesAsync` inside each bulk import.
- Publish `_rootSongs` only after the import commits.
- Keep `DiscoveredScoreCount` and `EnumeratedFileCount` as compatibility mirrors; UI progress continues through `EnumerationProgress`.
- Keep `AddSongAsync` as a legacy/test helper, but remove both production enumeration callers.
- Remove normal per-directory, per-file, and per-asset success diagnostics; keep warnings, conflicts, parse failures, and unexpected errors in Debug output.
- Write exactly one concise `HPA192_STARTUP` summary line to standard output for success, cancellation, and failure.
- Keep the third-party benchmark assets machine-local; commit only their manifest and measured report.
- Run macOS-safe tests locally; the full Windows suite remains a CI gate.

---

## File Structure

- Create `DTXMania.Game/Lib/Stage/StartupSongLoadSummary.cs` for the stable Release-safe summary contract and invariant formatter.
- Create `DTXMania.Game/Lib/Song/SongPathIdentity.cs` for path normalization, root containment, and group-key construction.
- Create `DTXMania.Game/Lib/Song/SongImportModels.cs` for enumeration, import, result, timing, and pending-node contracts.
- Modify `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs` to own the atomic bulk reconciliation.
- Modify `DTXMania.Game/Lib/Song/SongManager.cs` to collect batches, retain temporary hierarchy, call the importer once, and publish after commit.
- Modify `DTXMania.Game/Lib/Stage/StartupStage.cs` to remove duration gates, select one load path, handle outcomes, and aggregate timings.
- Create focused tests in `DTXMania.Test/Stage/StartupSongLoadSummaryTests.cs`, `DTXMania.Test/Song/SongPathIdentityTests.cs`, `DTXMania.Test/Song/SongDatabaseServiceBulkImportTests.cs`, and `DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs`.
- Modify existing compatibility/regression tests in `DTXMania.Test/Stage/StartupStageLogicTests.cs`, `DTXMania.Test/Song/SongManagerTests.cs`, and `DTXMania.Test/Song/SongManagerCoverageTests.cs`.
- Create `tools/hpa192/benchmark-startup.sh` for repeatable isolated Release runs through the existing Game API.
- Create `docs/performance/HPA-192-corpus-manifest.tsv` and `docs/performance/HPA-192-startup-benchmark.md` for the immutable benchmark evidence.

---

### Task 1: Add a Stable Release-Safe Startup Summary

**Files:**
- Create: `DTXMania.Game/Lib/Stage/StartupSongLoadSummary.cs`
- Create: `DTXMania.Test/Stage/StartupSongLoadSummaryTests.cs`
- Modify: `DTXMania.Game/Lib/Stage/StartupStage.cs:45-78,347-468,580-650`

**Interfaces:**
- Produces: `StartupSongLoadPath`, `StartupSongLoadOutcome`, and `StartupSongLoadSummary.Format()`.
- Produces: one line beginning with `HPA192_STARTUP ` on `Console.Out`.
- Preserves: current phase durations and current persistence behavior until the baseline is recorded in Task 2.

- [ ] **Step 1: Write the failing formatter tests**

```csharp
[Fact]
public void Format_ShouldProduceOneInvariantMachineReadableLine()
{
    var summary = new StartupSongLoadSummary(
        StartupSongLoadPath.Enumeration,
        StartupSongLoadOutcome.Success,
        TimeSpan.FromMilliseconds(1250),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(700),
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(75),
        discoveredCharts: 100,
        parsedCharts: 98,
        logicalGroups: 27,
        added: 98,
        updated: 0,
        preserved: 0,
        skipped: 2,
        conflicts: 0,
        stale: 0,
        error: null);

    Assert.Equal(
        "HPA192_STARTUP path=enumeration outcome=success total_ms=1250 db_init_ms=100 " +
        "discovery_parse_ms=700 persistence_ms=300 cleanup_ms=25 hierarchy_ms=75 " +
        "discovered=100 parsed=98 groups=27 added=98 updated=0 preserved=0 " +
        "skipped=2 conflicts=0 stale=0 error=none",
        summary.Format());
}

[Fact]
public void Format_ShouldSanitizeFailureTextOntoOneLine()
{
    var summary = StartupSongLoadSummary.Failed(
        StartupSongLoadPath.Enumeration,
        TimeSpan.FromMilliseconds(20),
        "SQLite write\nfailed");

    var text = summary.Format();

    Assert.DoesNotContain('\n', text);
    Assert.Contains("outcome=failure", text);
    Assert.Contains("error=SQLite_write_failed", text);
}
```

- [ ] **Step 2: Run the focused test and verify the red state**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~StartupSongLoadSummaryTests"
```

Expected: compilation fails because `StartupSongLoadSummary` does not exist.

- [ ] **Step 3: Implement the summary contract**

```csharp
namespace DTXMania.Game.Lib.Stage;

internal enum StartupSongLoadPath
{
    Unknown,
    Cache,
    Enumeration
}

internal enum StartupSongLoadOutcome
{
    Success,
    Cancellation,
    Failure
}

internal sealed record StartupSongLoadSummary(
    StartupSongLoadPath Path,
    StartupSongLoadOutcome Outcome,
    TimeSpan Total,
    TimeSpan DatabaseInitialization,
    TimeSpan DiscoveryAndParsing,
    TimeSpan Persistence,
    TimeSpan Cleanup,
    TimeSpan Hierarchy,
    int DiscoveredCharts,
    int ParsedCharts,
    int LogicalGroups,
    int Added,
    int Updated,
    int Preserved,
    int Skipped,
    int Conflicts,
    int Stale,
    string? Error)
{
    public string Format()
    {
        static long Ms(TimeSpan value) => (long)Math.Round(value.TotalMilliseconds);
        static string Token(string value) =>
            string.Concat(value.Select(character =>
                char.IsLetterOrDigit(character) || character is '.' or '-'
                    ? character
                    : '_'));

        return FormattableString.Invariant(
            $"HPA192_STARTUP path={Path.ToString().ToLowerInvariant()} " +
            $"outcome={Outcome.ToString().ToLowerInvariant()} total_ms={Ms(Total)} " +
            $"db_init_ms={Ms(DatabaseInitialization)} " +
            $"discovery_parse_ms={Ms(DiscoveryAndParsing)} " +
            $"persistence_ms={Ms(Persistence)} cleanup_ms={Ms(Cleanup)} " +
            $"hierarchy_ms={Ms(Hierarchy)} discovered={DiscoveredCharts} " +
            $"parsed={ParsedCharts} groups={LogicalGroups} added={Added} " +
            $"updated={Updated} preserved={Preserved} skipped={Skipped} " +
            $"conflicts={Conflicts} stale={Stale} " +
            $"error={(string.IsNullOrWhiteSpace(Error) ? "none" : Token(Error))}");
    }

    public static StartupSongLoadSummary Failed(
        StartupSongLoadPath path,
        TimeSpan total,
        string error) =>
        new(path, StartupSongLoadOutcome.Failure, total,
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            TimeSpan.Zero, 0, 0, 0, 0, 0, 0, 0, 0, 0, error);
}
```

- [ ] **Step 4: Instrument the existing startup without changing its behavior**

Add a wall-clock stopwatch, a per-phase stopwatch, a phase-duration dictionary,
and a one-shot summary guard to `StartupStage`. Start them in `OnActivate`.
Record each phase duration immediately before `UpdateCurrentPhase` advances to
the next phase. Map the current baseline phases as follows:

```csharp
private readonly Stopwatch _startupStopwatch = new();
private readonly Stopwatch _phaseStopwatch = new();
private readonly Dictionary<StartupPhase, TimeSpan> _measuredPhaseDurations = new();
private bool _startupSummaryWritten;

private TimeSpan GetBaselineDuration(StartupPhase phase) =>
    _measuredPhaseDurations.TryGetValue(phase, out var duration)
        ? duration
        : TimeSpan.Zero;

private static int CountScoreNodes(IEnumerable<SongListNode> nodes) =>
    nodes.Sum(node =>
        (node.Type == NodeType.Score ? 1 : 0) + CountScoreNodes(node.Children));

private StartupSongLoadSummary CreateBaselineSummary() =>
    new(
        _needsEnumeration == false
            ? StartupSongLoadPath.Cache
            : StartupSongLoadPath.Enumeration,
        StartupSongLoadOutcome.Success,
        _startupStopwatch.Elapsed,
        GetBaselineDuration(StartupPhase.SongListDB),
        GetBaselineDuration(StartupPhase.EnumerateSongs),
        TimeSpan.Zero,
        TimeSpan.Zero,
        GetBaselineDuration(StartupPhase.BuildSongLists),
        _songManager.EnumeratedFileCount,
        _songManager.DiscoveredScoreCount,
        CountScoreNodes(_songManager.RootSongs),
        _songManager.DiscoveredScoreCount,
        0,
        0,
        Math.Max(0, _songManager.EnumeratedFileCount - _songManager.DiscoveredScoreCount),
        0,
        0,
        null);

protected virtual void WriteStartupSummary(string line)
{
    Console.WriteLine(line);
}
```

`OnActivate` clears `_measuredPhaseDurations`, resets and starts both
stopwatches, and clears `_startupSummaryWritten`. Each phase advance stores
`_phaseStopwatch.Elapsed` under the phase being left, then restarts the phase
stopwatch. The Title transition calls:

```csharp
if (!_startupSummaryWritten)
{
    _startupSummaryWritten = true;
    _startupStopwatch.Stop();
    WriteStartupSummary(CreateBaselineSummary().Format());
}
```

Call `WriteStartupSummary(CreateBaselineSummary().Format())` exactly once when
the current duration-gated `Complete` phase first requests Title. The baseline
`discovery_parse_ms` intentionally includes the current interleaved per-chart
database work; document that fact in Task 2.

- [ ] **Step 5: Verify the formatter and startup regression tests**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~StartupSongLoadSummaryTests|FullyQualifiedName~StartupStageLogicTests"
```

Expected: PASS.

- [ ] **Step 6: Commit the measurement contract**

```bash
rtk git add DTXMania.Game/Lib/Stage/StartupSongLoadSummary.cs \
  DTXMania.Game/Lib/Stage/StartupStage.cs \
  DTXMania.Test/Stage/StartupSongLoadSummaryTests.cs
rtk git commit -m "feat: add startup timing summary"
```

---

### Task 2: Freeze the Corpus and Record the Baseline

**Files:**
- Create: `tools/hpa192/benchmark-startup.sh`
- Create: `docs/performance/HPA-192-corpus-manifest.tsv`
- Create: `docs/performance/HPA-192-startup-benchmark.md`

**Interfaces:**
- Consumes: the `HPA192_STARTUP` line from Task 1.
- Produces: three isolated fresh-startup runs for any supplied commit.
- Produces: a committed manifest for the machine-local 100-chart corpus.
- Produces: baseline external wall times and summary timings before import optimization.

- [ ] **Step 1: Create the isolated benchmark runner**

The script accepts a corpus path, a label, and a run count. It builds once,
creates a fresh app-data root and `Config.ini` per run, waits for Title through
`getGameState`, captures stdout, and kills the game after the measurement.

```bash
#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
corpus="${1:?usage: benchmark-startup.sh CORPUS LABEL [RUNS]}"
label="${2:?usage: benchmark-startup.sh CORPUS LABEL [RUNS]}"
runs="${3:-3}"
project="$repo_root/DTXMania.Game/DTXMania.Game.Mac.csproj"
result_root="$repo_root/TestResults/hpa-192/$label"
api_key="hpa-192-benchmark-key"
api_port=48912

mkdir -p "$result_root"
dotnet build "$project" -c Release

for run in $(seq 1 "$runs"); do
    run_root="$(mktemp -d "${TMPDIR:-/tmp}/hpa-192-run.XXXXXX")"
    appdata="$run_root/appdata"
    mkdir -p "$appdata"
    cp -R "$repo_root/System" "$run_root/System"

    {
        printf '%s\n' '[System]'
        printf 'SkinPath=%s\n' "$run_root/System"
        printf 'DTXPath=%s\n' "$corpus"
        printf '%s\n' '[Skin]'
        printf 'SystemSkinRoot=%s\n' "$run_root/System"
        printf '%s\n' '[Display]' 'ScreenWidth=1280' 'ScreenHeight=720'
        printf '%s\n' 'FullScreen=False' 'VSyncWait=False'
        printf '%s\n' '[Api]' 'EnableGameApi=True'
        printf 'GameApiPort=%s\n' "$api_port"
        printf 'GameApiKey=%s\n' "$api_key"
    } > "$appdata/Config.ini"

    stdout="$result_root/run-$run.stdout.log"
    stderr="$result_root/run-$run.stderr.log"
    start="$(perl -MTime::HiRes=time -e 'printf "%.6f", time')"
    DTXMANIA_APPDATA_ROOT="$appdata" \
      DTXMANIA_LAUNCH_TOKEN="hpa192-$label-$run" \
      dotnet run --no-build -c Release --project "$project" \
      >"$stdout" 2>"$stderr" &
    game_pid=$!

    reached_title=false
    for attempt in $(seq 1 1200); do
        state="$(curl -fsS \
          -H "X-Api-Key: $api_key" \
          -H 'Content-Type: application/json' \
          -d '{"jsonrpc":"2.0","id":1,"method":"getGameState","params":null}' \
          "http://127.0.0.1:$api_port/jsonrpc" 2>/dev/null || true)"
        if [[ "$state" == *"TitleStage"* ]]; then
            reached_title=true
            break
        fi
        sleep 0.05
    done

    end="$(perl -MTime::HiRes=time -e 'printf "%.6f", time')"
    kill "$game_pid" 2>/dev/null || true
    wait "$game_pid" 2>/dev/null || true

    if [[ "$reached_title" != true ]]; then
        printf 'run %s did not reach Title\n' "$run" >&2
        exit 1
    fi

    wall_ms="$(perl -e 'printf "%.0f", 1000 * ($ARGV[1] - $ARGV[0])' "$start" "$end")"
    summary="$(grep '^HPA192_STARTUP ' "$stdout" | tail -1)"
    printf 'run=%s wall_ms=%s %s\n' "$run" "$wall_ms" "$summary" |
      tee "$result_root/run-$run.result.txt"
    rm -rf "$run_root"
done
```

The execution command is:

```bash
rtk bash tools/hpa192/benchmark-startup.sh \
  "/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles" \
  baseline 3
```

- [ ] **Step 2: Generate and verify the corpus manifest**

Generate one TSV row for every file below the configured DTX root, sorted by
relative path, with byte length and SHA-256:

```bash
rtk bash -lc '
root="/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles"
rtk rg --files "$root" | LC_ALL=C sort | while IFS= read -r file; do
  relative="${file#"$root"/}"
  size="$(stat -f "%z" "$file")"
  hash="$(shasum -a 256 "$file" | awk "{print \\$1}")"
  printf "%s\t%s\t%s\n" "$relative" "$size" "$hash"
done
' > docs/performance/HPA-192-corpus-manifest.tsv
```

Verify the supported-chart count separately:

```bash
rtk rg --files \
  -g '*.dtx' -g '*.gda' -g '*.g2d' -g '*.bms' -g '*.bme' -g '*.bml' \
  "/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles" |
  rtk wc -l
```

Expected: `100`.

- [ ] **Step 3: Record all three baseline runs**

Populate `docs/performance/HPA-192-startup-benchmark.md` with:

- Apple hardware model, macOS version, and `dotnet --version`.
- The current instrumentation commit.
- Corpus path, 100 chart files, and 27 logical `set.def` groups.
- The SHA-256 of `HPA-192-corpus-manifest.tsv`.
- Every `run-N.result.txt` line.
- The median external wall time.
- A note that baseline `discovery_parse_ms` includes interleaved parsing,
  per-chart SQLite writes, cleanup, and reloads.

Use the actual generated values; do not round individual runs before computing
the median.

- [ ] **Step 4: Commit the reproducibility evidence**

```bash
rtk git add tools/hpa192/benchmark-startup.sh \
  docs/performance/HPA-192-corpus-manifest.tsv \
  docs/performance/HPA-192-startup-benchmark.md
rtk git commit -m "docs: record HPA-192 startup baseline"
```

---

### Task 3: Add Path Identity and Import Contracts

**Files:**
- Create: `DTXMania.Game/Lib/Song/SongPathIdentity.cs`
- Create: `DTXMania.Game/Lib/Song/SongImportModels.cs`
- Create: `DTXMania.Test/Song/SongPathIdentityTests.cs`

**Interfaces:**
- Produces: `SongPathIdentity.Normalize`, `IsUnderRoot`, `ForSetDefinition`, and `ForOrdinaryChart`.
- Produces: `SongImportCandidate`, `SongBulkImportRequest`, `SongBulkImportResult`, `SongEnumerationBatch`, `PendingSongNode`, and `SongEnumerationError`.
- Consumes: parsed `Song`, parsed `SongChart`, and existing `SongListNode`.

- [ ] **Step 1: Write failing identity tests**

```csharp
[Fact]
public void Normalize_ShouldReturnAbsoluteTrimmedPlatformPath()
{
    var root = Path.Combine(Path.GetTempPath(), "HPA-192", Guid.NewGuid().ToString("N"));
    var input = Path.Combine(root, "Songs", ".", "chart.dtx");

    var normalized = SongPathIdentity.Normalize(input);

    Assert.Equal(Path.GetFullPath(Path.Combine(root, "Songs", "chart.dtx")), normalized);
}

[Fact]
public void IsUnderRoot_ShouldRejectSiblingPrefix()
{
    var root = Path.Combine(Path.GetTempPath(), "Songs");
    var sibling = Path.Combine(Path.GetTempPath(), "Songs-Backup", "chart.dtx");

    Assert.False(SongPathIdentity.IsUnderRoot(sibling, root));
}

[Fact]
public void OrdinaryGroupKey_ShouldIncludeDirectoryTitleAndArtist()
{
    var first = SongPathIdentity.ForOrdinaryChart("/songs/a/one.dtx", "Same", "Artist");
    var second = SongPathIdentity.ForOrdinaryChart("/songs/b/two.dtx", "Same", "Artist");

    Assert.NotEqual(first, second);
}

[Fact]
public void SetDefinitionGroupKey_ShouldIgnoreChartTitleDifferences()
{
    var key = SongPathIdentity.ForSetDefinition("/songs/group/set.def");

    Assert.StartsWith("set|", key, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the identity tests and verify failure**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~SongPathIdentityTests"
```

Expected: compilation fails because `SongPathIdentity` does not exist.

- [ ] **Step 3: Implement path identity**

```csharp
internal static class SongPathIdentity
{
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    public static bool IsUnderRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(Normalize(root), Normalize(path));
        return relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    public static string ForSetDefinition(string setDefPath) =>
        $"set|{Normalize(setDefPath)}";

    public static string ForOrdinaryChart(string chartPath, string title, string artist)
    {
        var directory = Path.GetDirectoryName(Normalize(chartPath))
            ?? throw new InvalidOperationException("A chart path must have a directory.");
        return $"dir|{directory}\u001f{title}\u001f{artist}";
    }
}
```

- [ ] **Step 4: Add the import and enumeration contracts**

Use these exact roles and property names:

```csharp
internal sealed record SongImportCandidate(
    Song ParsedSong,
    SongChart ParsedChart,
    string NormalizedChartPath,
    string GroupKey,
    int GroupOrder);

internal sealed record SongBulkImportRequest(
    IReadOnlyList<string> ActiveRoots,
    IReadOnlySet<string> DiscoveredChartPaths,
    IReadOnlyList<SongImportCandidate> Candidates);

internal sealed record PendingSongNode(
    string GroupKey,
    SongListNode Placeholder,
    IReadOnlyList<string> OrderedChartPaths);

internal sealed record SongEnumerationError(
    string Path,
    string Message,
    bool IsRootFailure);

internal sealed class SongEnumerationBatch
{
    public required IReadOnlyList<string> ActiveRoots { get; init; }
    public required HashSet<string> DiscoveredChartPaths { get; init; }
    public required List<SongImportCandidate> Candidates { get; init; }
    public required List<SongListNode> RootNodes { get; init; }
    public required List<PendingSongNode> PendingSongs { get; init; }
    public required List<SongEnumerationError> Errors { get; init; }
    public required TimeSpan DiscoveryAndParsingDuration { get; init; }
    public bool IsComplete { get; init; }
}

internal sealed record SongBulkImportResult(
    IReadOnlyDictionary<string, SongChart> ChartsByPath,
    int Added,
    int Updated,
    int Preserved,
    int Skipped,
    int Conflicts,
    int StaleCharts,
    int StaleSongs,
    TimeSpan PersistenceDuration,
    TimeSpan CleanupDuration);

internal sealed record SongEnumerationResult(
    SongEnumerationBatch Batch,
    SongBulkImportResult Import,
    TimeSpan HierarchyDuration);
```

All path sets and dictionaries must be constructed with
`SongPathIdentity.Comparer`.

- [ ] **Step 5: Run tests and commit**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~SongPathIdentityTests"
rtk git add DTXMania.Game/Lib/Song/SongPathIdentity.cs \
  DTXMania.Game/Lib/Song/SongImportModels.cs \
  DTXMania.Test/Song/SongPathIdentityTests.cs
rtk git commit -m "feat: add song import contracts"
```

---

### Task 4: Implement the Atomic Fresh Bulk Import

**Files:**
- Create: `DTXMania.Test/Song/SongDatabaseServiceBulkImportTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs:21-68,288-405,974-1010,1654-1704`

**Interfaces:**
- Consumes: `SongBulkImportRequest`.
- Produces: `Task<SongBulkImportResult> ImportSongsAsync(SongBulkImportRequest request, CancellationToken cancellationToken)`.
- Produces: generated IDs and eager score graphs through `SongBulkImportResult.ChartsByPath`.
- Preserves: `AddSongAsync` unchanged as a legacy/test helper.

- [ ] **Step 1: Write the fresh-import and single-save tests**

Use an open in-memory `SqliteConnection`, create the schema with a normal
`SongDbContext`, and inject a `CountingSongDbContext` into the service:

```csharp
private sealed class CountingSongDbContext : SongDbContext
{
    public int SaveChangesAsyncCalls { get; private set; }
    public bool HadTransactionAtSave { get; private set; }
    public Func<CancellationToken, Task>? BeforeSaveAsync { get; init; }

    public CountingSongDbContext(DbContextOptions<SongDbContext> options)
        : base(options)
    {
    }

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        SaveChangesAsyncCalls++;
        HadTransactionAtSave = Database.CurrentTransaction != null;
        if (BeforeSaveAsync != null)
            await BeforeSaveAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }
}

private readonly SqliteConnection _connection;
private readonly DbContextOptions<SongDbContext> _options;
private readonly SongDatabaseService _service;
private CountingSongDbContext _countingContext = null!;
private Func<CancellationToken, Task>? _beforeSaveAsync;
private int _createdContexts;

public SongDatabaseServiceBulkImportTests()
{
    _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
    _connection.Open();
    _options = new DbContextOptionsBuilder<SongDbContext>()
        .UseSqlite(_connection)
        .Options;
    using (var schema = new SongDbContext(_options))
        schema.Database.EnsureCreated();

    _service = new SongDatabaseService(_options, () =>
    {
        _createdContexts++;
        _countingContext = new CountingSongDbContext(_options)
        {
            BeforeSaveAsync = _beforeSaveAsync
        };
        return _countingContext;
    });
}

private static SongImportCandidate Candidate(
    string title,
    string groupKey,
    int groupOrder,
    string path,
    int drumLevel) =>
    new(
        new Song { Title = title, Artist = "Fixture Artist", Genre = "Fixture" },
        new SongChart
        {
            FilePath = path,
            FileSize = 123,
            LastModified = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc),
            DrumLevel = drumLevel,
            HasDrumChart = drumLevel > 0
        },
        SongPathIdentity.Normalize(path),
        groupKey,
        groupOrder);

private static SongBulkImportRequest CreateRequest(
    params SongImportCandidate[] candidates)
{
    var roots = new[] { SongPathIdentity.Normalize("/songs") };
    return new SongBulkImportRequest(
        roots,
        candidates.Select(candidate => candidate.NormalizedChartPath)
            .ToHashSet(SongPathIdentity.Comparer),
        candidates);
}

private static SongImportCandidate OneCandidate() =>
    Candidate("One", "dir|one", 0, "/songs/one/chart.dtx", 50);

private async Task ExecuteSqlAsync(string sql)
{
    await using var context = new SongDbContext(_options);
    await context.Database.ExecuteSqlRawAsync(sql);
}

private async Task AssertDatabaseCountsAsync(int songs, int charts, int scores)
{
    await using var context = new SongDbContext(_options);
    Assert.Equal(songs, await context.Songs.CountAsync());
    Assert.Equal(charts, await context.SongCharts.CountAsync());
    Assert.Equal(scores, await context.SongScores.CountAsync());
}

[Fact]
public async Task ImportSongsAsync_FreshGroup_ShouldUseOneContextTransactionAndSave()
{
    var request = CreateRequest(
        Candidate("Basic", "set|group", 1, "/songs/group/basic.dtx", drumLevel: 20),
        Candidate("Extreme", "set|group", 2, "/songs/group/extreme.dtx", drumLevel: 80));

    var result = await _service.ImportSongsAsync(request, CancellationToken.None);

    Assert.Equal(1, _createdContexts);
    Assert.Equal(1, _countingContext.SaveChangesAsyncCalls);
    Assert.True(_countingContext.HadTransactionAtSave);
    Assert.Equal(2, result.Added);
    Assert.All(result.ChartsByPath.Values, chart => Assert.True(chart.Id > 0));
    Assert.Single(result.ChartsByPath.Values.Select(chart => chart.SongId).Distinct());
    Assert.All(result.ChartsByPath.Values, chart => Assert.Single(chart.Scores));
    Assert.All(result.ChartsByPath.Values, chart => Assert.Equal("", chart.FileHash));
}
```

- [ ] **Step 2: Write cancellation and SQLite failure rollback tests**

```csharp
[Fact]
public async Task ImportSongsAsync_WhenCancelledAtSave_ShouldRollBackEverything()
{
    using var cancellation = new CancellationTokenSource();
    _beforeSaveAsync = token =>
        Task.FromException(new OperationCanceledException(token));

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        _service.ImportSongsAsync(CreateRequest(OneCandidate()), cancellation.Token));

    await AssertDatabaseCountsAsync(songs: 0, charts: 0, scores: 0);
}

[Fact]
public async Task ImportSongsAsync_WhenSqliteTriggerRejectsChart_ShouldRollBackSongToo()
{
    await ExecuteSqlAsync(
        "CREATE TRIGGER fail_chart BEFORE INSERT ON SongCharts " +
        "BEGIN SELECT RAISE(ABORT, 'forced import failure'); END;");

    await Assert.ThrowsAsync<DbUpdateException>(() =>
        _service.ImportSongsAsync(CreateRequest(OneCandidate()), CancellationToken.None));

    await AssertDatabaseCountsAsync(songs: 0, charts: 0, scores: 0);
}
```

- [ ] **Step 3: Run the focused tests and verify failure**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~SongDatabaseServiceBulkImportTests"
```

Expected: compilation fails because `ImportSongsAsync` and the context factory
seam do not exist.

- [ ] **Step 4: Add the test-only context factory seam**

Keep `_options` for all existing constructors and add:

```csharp
private readonly Func<SongDbContext>? _contextFactory;

internal SongDatabaseService(
    DbContextOptions<SongDbContext> options,
    Func<SongDbContext> contextFactory)
{
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _contextFactory = contextFactory
        ?? throw new ArgumentNullException(nameof(contextFactory));
    _databasePath = string.Empty;
    _isInitialized = true;
}

public SongDbContext CreateContext()
{
    lock (_initializationLock)
    {
        if (!_isInitialized)
            throw new InvalidOperationException(
                "Database must be initialized before creating contexts. " +
                "Call InitializeDatabaseAsync() first.");
    }

    return _contextFactory?.Invoke() ?? new SongDbContext(_options);
}
```

- [ ] **Step 5: Implement the transaction shell and fresh-group reconciliation**

```csharp
internal async Task<SongBulkImportResult> ImportSongsAsync(
    SongBulkImportRequest request,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(request);
    cancellationToken.ThrowIfCancellationRequested();

    await using var context = CreateContext();
    await using var transaction =
        await context.Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

    try
    {
        var persistence = Stopwatch.StartNew();
        var chartsByPath = new Dictionary<string, SongChart>(SongPathIdentity.Comparer);
        var added = 0;

        foreach (var group in request.Candidates
            .OrderBy(candidate => candidate.GroupKey, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.GroupOrder)
            .GroupBy(candidate => candidate.GroupKey, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primary = group.OrderBy(candidate => candidate.GroupOrder)
                .ThenBy(candidate => candidate.NormalizedChartPath, SongPathIdentity.Comparer)
                .First();
            var song = CreateSongFromParsed(primary.ParsedSong);
            context.Songs.Add(song);

            foreach (var candidate in group)
            {
                var chart = CreateChartFromParsed(candidate.ParsedChart, song);
                chart.FilePath = candidate.NormalizedChartPath;
                AddMissingInitialScores(chart);
                context.SongCharts.Add(chart);
                chartsByPath.Add(candidate.NormalizedChartPath, chart);
                added++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        persistence.Stop();

        return new SongBulkImportResult(
            chartsByPath, added, 0, 0, 0, 0, 0, 0,
            persistence.Elapsed, TimeSpan.Zero);
    }
    catch
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception rollbackException)
        {
            Debug.WriteLine(
                $"SongDatabaseService: rollback failed: {rollbackException.Message}");
        }

        throw;
    }
}
```

`CreateSongFromParsed` copies `Title`, `Artist`, `Genre`, and `Comment`.
`CreateChartFromParsed` copies every persisted chart metadata field from
`SongChart.cs` except `Id`, `SongId`, `Song`, `Scores`, and `FileHash`.
`AddMissingInitialScores` creates one default-speed row for each available
positive-level instrument and sets both `SongScore.Chart` and
`SongScore.Instrument`; it does not call `SaveChangesAsync`.

- [ ] **Step 6: Verify atomicity and commit**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~SongDatabaseServiceBulkImportTests"
rtk git add DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs \
  DTXMania.Test/Song/SongDatabaseServiceBulkImportTests.cs
rtk git commit -m "feat: add atomic song bulk import"
```

---

### Task 5: Add Rescan Preservation, Scoped Cleanup, and Conflict Handling

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Modify: `DTXMania.Test/Song/SongDatabaseServiceBulkImportTests.cs`

**Interfaces:**
- Extends: `ImportSongsAsync` from Task 4.
- Produces: accurate added, updated, preserved, skipped, conflict, and stale counts.
- Preserves: all user-owned rows and identifiers on rescan.

- [ ] **Step 1: Write the preservation test**

Seed one bookmarked song and chart with:

- The default-speed score populated with best, last, rank, count, combo,
  recent-play, and NX snapshot values.
- A second `PlaySpeedPercent = 150` score.
- One `PerformanceHistory` row attached to each score.
- A non-empty legacy `FileHash`.

Then import the same normalized chart path with changed parsed metadata:

```csharp
private sealed record SeededSong(Song Song, SongChart Chart);

private async Task<SeededSong> SeedPlayedSongAsync()
{
    await using var context = new SongDbContext(_options);
    var song = new Song
    {
        Title = "Original",
        Artist = "Fixture Artist",
        Genre = "Original Genre",
        IsBookmarked = true
    };
    var chart = new SongChart
    {
        Song = song,
        FilePath = SongPathIdentity.Normalize("/songs/played/chart.dtx"),
        FileHash = "legacy-md5",
        DrumLevel = 50,
        HasDrumChart = true
    };
    var defaultScore = new SongScore
    {
        Chart = chart,
        Instrument = EInstrumentPart.DRUMS,
        PlaySpeedPercent = 100,
        BestScore = 900_000,
        LastScore = 800_000,
        BestRank = 90,
        PlayCount = 4,
        ClearCount = 3,
        FullCombo = true,
        LastPlayedAt = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
        NxImportedPlayCount = 2,
        NxImportedClearCount = 1
    };
    var fastScore = new SongScore
    {
        Chart = chart,
        Instrument = EInstrumentPart.DRUMS,
        PlaySpeedPercent = 150,
        BestScore = 700_000,
        PlayCount = 1
    };
    defaultScore.PerformanceHistory.Add(new PerformanceHistory
    {
        Song = song,
        SongScore = defaultScore,
        PerformedAt = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
        DisplayOrder = 1,
        HistoryLine = "default"
    });
    fastScore.PerformanceHistory.Add(new PerformanceHistory
    {
        Song = song,
        SongScore = fastScore,
        PerformedAt = new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc),
        DisplayOrder = 1,
        HistoryLine = "fast"
    });
    chart.Scores.Add(defaultScore);
    chart.Scores.Add(fastScore);
    song.Charts.Add(chart);
    context.Songs.Add(song);
    await context.SaveChangesAsync();
    return new SeededSong(song, chart);
}

[Fact]
public async Task ImportSongsAsync_Rescan_ShouldUpdateMetadataAndPreserveUserState()
{
    var seeded = await SeedPlayedSongAsync();
    var candidate = Candidate(
        title: "Renamed",
        groupKey: "dir|same",
        groupOrder: 0,
        path: seeded.Chart.FilePath,
        drumLevel: 75);

    var result = await _service.ImportSongsAsync(
        CreateRequest(candidate),
        CancellationToken.None);

    var chart = result.ChartsByPath[SongPathIdentity.Normalize(seeded.Chart.FilePath)];
    Assert.Equal(seeded.Song.Id, chart.SongId);
    Assert.Equal(seeded.Chart.Id, chart.Id);
    Assert.True(chart.Song.IsBookmarked);
    Assert.Equal("Renamed", chart.Song.Title);
    Assert.Equal(75, chart.DrumLevel);
    Assert.Equal(seeded.Chart.FileHash, chart.FileHash);
    Assert.Equal(new[] { 100, 150 }, chart.Scores
        .Select(score => score.PlaySpeedPercent)
        .OrderBy(speed => speed));
    Assert.All(chart.Scores, score => Assert.NotEmpty(score.PerformanceHistory));
    Assert.Equal(1, result.Updated);
    Assert.Equal(0, result.Added);
}
```

- [ ] **Step 2: Write identity, cleanup, and ambiguity tests**

Add focused tests with these exact assertions:

```csharp
private static SongBulkImportRequest CreateEmptyRequest(
    IReadOnlyList<string> activeRoots,
    IReadOnlyList<string> discoveredPaths) =>
    new(
        activeRoots.Select(SongPathIdentity.Normalize).ToArray(),
        discoveredPaths.Select(SongPathIdentity.Normalize)
            .ToHashSet(SongPathIdentity.Comparer),
        Array.Empty<SongImportCandidate>());

private async Task<SongChart> SeedChartAsync(
    string path,
    string songTitle = "Seed")
{
    await using var context = new SongDbContext(_options);
    var song = new Song { Title = songTitle, Artist = "Fixture Artist" };
    var chart = new SongChart
    {
        Song = song,
        FilePath = SongPathIdentity.Normalize(path),
        DrumLevel = 50,
        HasDrumChart = true
    };
    song.Charts.Add(chart);
    context.Songs.Add(song);
    await context.SaveChangesAsync();
    return chart;
}

private async Task<bool> ChartExistsAsync(int chartId)
{
    await using var context = new SongDbContext(_options);
    return await context.SongCharts.AnyAsync(chart => chart.Id == chartId);
}

[Fact]
public async Task ImportSongsAsync_SameMetadataInDifferentDirectories_ShouldCreateTwoSongs()
{
    var request = CreateRequest(
        Candidate("Same", "dir|a", 0, "/songs/a/chart.dtx", 40),
        Candidate("Same", "dir|b", 0, "/songs/b/chart.dtx", 40));

    var result = await _service.ImportSongsAsync(request, CancellationToken.None);

    Assert.Equal(2, result.ChartsByPath.Values.Select(chart => chart.SongId).Distinct().Count());
}

[Fact]
public async Task ImportSongsAsync_DiscoveryDiff_ShouldDeleteOnlyInsideActiveRoots()
{
    var staleInside = await SeedChartAsync("/songs/active/stale.dtx");
    var outside = await SeedChartAsync("/songs/other/keep.dtx");
    var request = CreateEmptyRequest(
        activeRoots: new[] { "/songs/active" },
        discoveredPaths: Array.Empty<string>());

    var result = await _service.ImportSongsAsync(request, CancellationToken.None);

    Assert.Equal(1, result.StaleCharts);
    Assert.False(await ChartExistsAsync(staleInside.Id));
    Assert.True(await ChartExistsAsync(outside.Id));
}

[Fact]
public async Task ImportSongsAsync_AmbiguousLegacyGroup_ShouldKeepAssociationsAndReportConflict()
{
    var first = await SeedChartAsync("/songs/group/basic.dtx", songTitle: "A");
    var second = await SeedChartAsync("/songs/group/extreme.dtx", songTitle: "B");
    var request = CreateRequest(
        Candidate("Unified", "set|group", 1, first.FilePath, 20),
        Candidate("Unified", "set|group", 2, second.FilePath, 80),
        Candidate("Unified", "set|group", 3, "/songs/group/master.dtx", 95));

    var result = await _service.ImportSongsAsync(request, CancellationToken.None);

    Assert.Equal(first.SongId, result.ChartsByPath[SongPathIdentity.Normalize(first.FilePath)].SongId);
    Assert.Equal(second.SongId, result.ChartsByPath[SongPathIdentity.Normalize(second.FilePath)].SongId);
    Assert.DoesNotContain(
        result.ChartsByPath[SongPathIdentity.Normalize("/songs/group/master.dtx")].SongId,
        new[] { first.SongId, second.SongId });
    Assert.Equal(1, result.Conflicts);
}
```

Also assert that:

- Two `set.def` candidates with one group key share one song.
- Reimport does not add a duplicate `(ChartId, Instrument, PlaySpeedPercent)`.
- A discovered but skipped candidate path protects its old chart from cleanup.
- Removing the final chart removes its empty song in the same save.

- [ ] **Step 3: Run the tests and verify the red state**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~SongDatabaseServiceBulkImportTests"
```

Expected: rescan, cleanup, and conflict assertions fail against the fresh-only
implementation.

- [ ] **Step 4: Preload and index the existing graph once**

At the beginning of `ImportSongsAsync`, load one graph:

```csharp
var persistedSongs = await context.Songs
    .Include(song => song.Charts)
        .ThenInclude(chart => chart.Scores)
            .ThenInclude(score => score.PerformanceHistory)
    .ToListAsync(cancellationToken)
    .ConfigureAwait(false);

var persistedCharts = persistedSongs
    .SelectMany(song => song.Charts)
    .Where(chart => request.ActiveRoots.Any(root =>
        SongPathIdentity.IsUnderRoot(chart.FilePath, root)))
    .ToDictionary(
        chart => SongPathIdentity.Normalize(chart.FilePath),
        SongPathIdentity.Comparer);
```

Deduplicate candidate paths before grouping. Count later duplicates as skipped
and retain the first candidate in deterministic group/order/path order.

- [ ] **Step 5: Reconcile groups without reparenting ambiguous charts**

For each group:

1. Collect matched existing charts by normalized path.
2. Collect distinct matched `SongId` values.
3. Zero IDs: create one new song from the deterministic primary.
4. One ID: update that song's parsed metadata and attach new siblings to it.
5. Multiple IDs: increment `Conflicts`; update each matched chart in place,
   retain each existing association, and create one separate song for any new
   siblings.
6. Copy parsed chart metadata into existing charts while preserving `Id`,
   `SongId`, `Song`, `Scores`, and `FileHash`.
7. Create only missing default-speed instrument keys.
8. Classify a matched chart as updated when a persisted metadata value changes;
   otherwise classify it as preserved.

Emit one Debug diagnostic per ambiguous group:

```csharp
Debug.WriteLine(
    $"SongDatabaseService: ambiguous import group '{group.Key}' " +
    $"matched song ids [{string.Join(",", matchedSongIds.Order())}]");
```

- [ ] **Step 6: Apply discovery-set cleanup before the only save**

```csharp
var cleanup = Stopwatch.StartNew();
var staleCharts = persistedCharts
    .Where(pair => !request.DiscoveredChartPaths.Contains(pair.Key))
    .Select(pair => pair.Value)
    .ToList();

context.SongCharts.RemoveRange(staleCharts);

var staleChartSet = staleCharts.ToHashSet();
var emptySongs = persistedSongs
    .Where(song => song.Charts.Count > 0
        && song.Charts.All(staleChartSet.Contains))
    .ToList();
context.Songs.RemoveRange(emptySongs);
cleanup.Stop();
```

Do not call `CleanupStaleChartsAsync` and do not call `File.Exists` during this
cleanup. Pass the caller token to the one `SaveChangesAsync` and `CommitAsync`.

- [ ] **Step 7: Verify the full importer suite and commit**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~SongDatabaseServiceBulkImportTests|FullyQualifiedName~SongDatabaseServiceBookmarkTests|FullyQualifiedName~SongDatabaseServiceCoverageTests"
rtk git add DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs \
  DTXMania.Test/Song/SongDatabaseServiceBulkImportTests.cs
rtk git commit -m "feat: preserve song state during bulk import"
```

---

### Task 6: Refactor SongManager to Enumerate First and Publish After Commit

**Files:**
- Create: `DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs`
- Modify: `DTXMania.Game/Lib/Song/SongManager.cs:64-180,304-402,564-641,659-870,871-1003,1008-1055,1399-1610,2860-3055,3114-3185`
- Modify: `DTXMania.Test/Song/SongManagerTests.cs:119-180,424-620`
- Modify: `DTXMania.Test/Song/SongManagerCoverageTests.cs:306-330,1113-1160`
- Verify: `DTXMania.Test/Song/SetDefParserTests.cs`

**Interfaces:**
- Consumes: `Task<SongBulkImportResult> SongDatabaseService.ImportSongsAsync(SongBulkImportRequest request, CancellationToken cancellationToken)`.
- Produces: `Task<SongEnumerationResult> EnumerateAndImportSongsAsync(string[] searchPaths, IProgress<EnumerationProgress>? progress = null, CancellationToken cancellationToken = default)`.
- Preserves: `Task<int> EnumerateSongsAsync(string[] searchPaths, IProgress<EnumerationProgress>? progress = null, CancellationToken cancellationToken = default)` and `Task<int> EnumerateSongsOnlyAsync(string[] searchPaths, IProgress<EnumerationProgress>? progress = null, CancellationToken cancellationToken = default)` as count-returning compatibility wrappers.
- Produces: completed temporary hierarchy only after commit.

- [ ] **Step 1: Write batch-completeness and publication tests**

```csharp
private readonly string _testRoot = Path.Combine(
    Path.GetTempPath(), "HPA-192-SongManager", Guid.NewGuid().ToString("N"));
private readonly string _songsRoot;
private readonly string _databasePath;
private readonly SongManager _manager;
private int _seededChartCount;
private int _bulkImportCalls;

public SongManagerBulkEnumerationTests()
{
    _songsRoot = Path.Combine(_testRoot, "Songs");
    _databasePath = Path.Combine(_testRoot, "songs.db");
    Directory.CreateDirectory(_songsRoot);
    SongManager.ResetInstanceForTesting();
    _manager = SongManager.Instance;
}

private string WriteChart(string relativePath, string title, int drumLevel)
{
    var path = Path.Combine(_testRoot, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllLines(path, new[]
    {
        $"#TITLE: {title}",
        "#ARTIST: Fixture Artist",
        "#BPM: 120",
        $"#DLEVEL: {drumLevel}"
    });
    return path;
}

private static IEnumerable<SongListNode> FlattenScoreNodes(
    IEnumerable<SongListNode> roots)
{
    foreach (var node in roots)
    {
        if (node.Type == NodeType.Score)
            yield return node;
        foreach (var child in FlattenScoreNodes(node.Children))
            yield return child;
    }
}

private async Task<int> CountChartsAsync()
{
    await using var context = _manager.DatabaseService!.CreateContext();
    return await context.SongCharts.CountAsync();
}

private async Task SeedPublishedLibraryAsync()
{
    WriteChart("Songs/Seed/seed.dtx", "Seed", 40);
    await _manager.InitializeDatabaseServiceAsync(_databasePath);
    await _manager.EnumerateAndImportSongsAsync(
        new[] { _songsRoot }, null, CancellationToken.None);
    _seededChartCount = await CountChartsAsync();
}

public void Dispose()
{
    SongManager.ResetInstanceForTesting();
    if (Directory.Exists(_testRoot))
        Directory.Delete(_testRoot, recursive: true);
}

[Fact]
public async Task EnumerateAndImportSongsAsync_ShouldPublishCommittedHierarchyWithoutReload()
{
    await _manager.InitializeDatabaseServiceAsync(_databasePath);
    WriteChart("Songs/A/basic.dtx", title: "Grouped", drumLevel: 20);
    WriteChart("Songs/A/extreme.dtx", title: "Grouped", drumLevel: 80);

    var result = await _manager.EnumerateAndImportSongsAsync(
        new[] { _songsRoot },
        progress: null,
        CancellationToken.None);

    Assert.True(result.Batch.IsComplete);
    Assert.Equal(2, result.Batch.DiscoveredChartPaths.Count);
    Assert.Equal(2, result.Batch.Candidates.Count);
    var node = Assert.Single(FlattenScoreNodes(_manager.RootSongs));
    Assert.Equal(2, node.AvailableDifficulties);
    Assert.All(node.Scores.Where(score => score != null), score => Assert.True(score!.ChartId > 0));
}

[Fact]
public async Task EnumerateAndImportSongsAsync_WhenCancelled_ShouldLeaveDatabaseAndRootSongsUnchanged()
{
    await SeedPublishedLibraryAsync();
    var originalRoots = _manager.RootSongs.ToArray();
    using var cancellation = new CancellationTokenSource();
    var progress = new Progress<EnumerationProgress>(_ => cancellation.Cancel());

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, progress, cancellation.Token));

    Assert.Equal(originalRoots, _manager.RootSongs);
    Assert.Equal(_seededChartCount, await CountChartsAsync());
}
```

- [ ] **Step 2: Write parse-failure and root-failure tests**

Add internal delegate seams on `SongManager` for tests:

```csharp
internal Func<string, Task<(Song song, SongChart chart)>> ParseSongEntitiesCoreAsync
    { get; set; } = DTXChartParser.ParseSongEntitiesAsync;
internal Func<string, IEnumerable<string>> EnumerateFilesCore
    { get; set; } = Directory.EnumerateFiles;
internal Func<string, IEnumerable<string>> EnumerateDirectoriesCore
    { get; set; } = Directory.EnumerateDirectories;
internal Func<
    SongDatabaseService,
    SongBulkImportRequest,
    CancellationToken,
    Task<SongBulkImportResult>> ImportSongsCoreAsync
    { get; set; } = static (database, request, token) =>
        database.ImportSongsAsync(request, token);
```

Tests then assert:

```csharp
[Fact]
public async Task BuildEnumerationBatchAsync_WhenChartParseFails_ShouldRetainDiscoveredPath()
{
    var chartPath = WriteChart("Songs/Broken/chart.dtx", "Broken", 50);
    _manager.ParseSongEntitiesCoreAsync = path =>
        Task.FromException<(Song, SongChart)>(
            new InvalidDataException($"malformed {path}"));

    var batch = await _manager.BuildEnumerationBatchAsync(
        new[] { _songsRoot }, null, CancellationToken.None);

    Assert.True(batch.IsComplete);
    Assert.Contains(SongPathIdentity.Normalize(chartPath), batch.DiscoveredChartPaths);
    Assert.Empty(batch.Candidates);
    Assert.Contains(batch.Errors, error => error.Path == SongPathIdentity.Normalize(chartPath)
        && !error.IsRootFailure);
}

[Fact]
public async Task EnumerateAndImportSongsAsync_WhenRootTraversalFails_ShouldNotCallImporter()
{
    var realImporter = _manager.ImportSongsCoreAsync;
    _manager.ImportSongsCoreAsync = async (database, request, token) =>
    {
        _bulkImportCalls++;
        return await realImporter(database, request, token);
    };
    _manager.EnumerateDirectoriesCore = _ =>
        throw new IOException("root unavailable");

    await Assert.ThrowsAsync<IOException>(() =>
        _manager.EnumerateAndImportSongsAsync(
            new[] { _songsRoot }, null, CancellationToken.None));

    Assert.Equal(0, _bulkImportCalls);
}
```

Reset all delegate seams in `SongManager.Clear` and
`ResetInstanceForTesting` so one singleton test cannot contaminate another.

- [ ] **Step 3: Run the focused tests and verify failure**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~SongManagerBulkEnumerationTests"
```

Expected: compilation fails because the batch-building and import methods do
not exist.

- [ ] **Step 4: Build a complete temporary enumeration batch**

Refactor traversal so one local `SongEnumerationBatchBuilder` state is passed
through recursive calls. The state must:

- Normalize active roots once.
- Reject a missing, blank, or inaccessible configured root as an incomplete
  root failure; do not silently skip it.
- Add a supported path to `DiscoveredChartPaths` before invoking the parser.
- Add recoverable chart or `set.def` parse failures to `Errors`.
- Let `OperationCanceledException`, root enumeration `IOException`, and root
  `UnauthorizedAccessException` propagate.
- Build box nodes in a temporary root list.
- Put a placeholder score node at its final root/child position.
- Add one `PendingSongNode` containing the placeholder and ordered normalized
  chart paths for each ordinary or `set.def` group.
- Parse each `set.def` chart once in difficulty order.
- Use `SongPathIdentity.ForSetDefinition(setDefPath)` for a definition group.
- Use `SongPathIdentity.ForOrdinaryChart(path, title, artist)` for an ordinary
  chart group.
- Stop the discovery stopwatch only after all roots finish.
- Set `IsComplete = true` only at that point.

Use this nested builder so incomplete state cannot accidentally be returned as
importable:

```csharp
private sealed class SongEnumerationBatchBuilder
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public SongEnumerationBatchBuilder(IReadOnlyList<string> activeRoots)
    {
        ActiveRoots = activeRoots;
    }

    public IReadOnlyList<string> ActiveRoots { get; }
    public HashSet<string> DiscoveredChartPaths { get; } =
        new(SongPathIdentity.Comparer);
    public List<SongImportCandidate> Candidates { get; } = new();
    public List<SongListNode> RootNodes { get; } = new();
    public List<PendingSongNode> PendingSongs { get; } = new();
    public List<SongEnumerationError> Errors { get; } = new();

    public SongEnumerationBatch Complete()
    {
        _stopwatch.Stop();
        return new SongEnumerationBatch
        {
            ActiveRoots = ActiveRoots,
            DiscoveredChartPaths = DiscoveredChartPaths,
            Candidates = Candidates,
            RootNodes = RootNodes,
            PendingSongs = PendingSongs,
            Errors = Errors,
            DiscoveryAndParsingDuration = _stopwatch.Elapsed,
            IsComplete = true
        };
    }
}

private static SongEnumerationBatchBuilder CreateBatchBuilder(
    IEnumerable<string> searchPaths)
{
    ArgumentNullException.ThrowIfNull(searchPaths);
    var roots = searchPaths.Select(path =>
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new DirectoryNotFoundException("A configured song root is blank.");
        var normalized = SongPathIdentity.Normalize(path);
        if (!Directory.Exists(normalized))
            throw new DirectoryNotFoundException(
                $"Configured song root does not exist: {normalized}");
        return normalized;
    })
    .Distinct(SongPathIdentity.Comparer)
    .ToArray();
    return new SongEnumerationBatchBuilder(roots);
}
```

The outer method must not catch cancellation or unexpected exceptions:

```csharp
internal async Task<SongEnumerationBatch> BuildEnumerationBatchAsync(
    string[] searchPaths,
    IProgress<EnumerationProgress>? progress,
    CancellationToken cancellationToken)
{
    var builder = CreateBatchBuilder(searchPaths);
    foreach (var root in builder.ActiveRoots)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnumerateDirectoryIntoBatchAsync(
            root, parent: null, builder, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    return builder.Complete();
}
```

- [ ] **Step 5: Import once, hydrate pending nodes, then publish**

```csharp
public async Task<SongEnumerationResult> EnumerateAndImportSongsAsync(
    string[] searchPaths,
    IProgress<EnumerationProgress>? progress = null,
    CancellationToken cancellationToken = default)
{
    var linked = BeginEnumeration(cancellationToken);
    try
    {
        var batch = await BuildEnumerationBatchAsync(
            searchPaths, progress, linked.Token).ConfigureAwait(false);

        if (!batch.IsComplete)
            throw new InvalidOperationException(
                "An incomplete enumeration cannot be imported.");

        var database = GetDatabaseServiceSnapshot()
            ?? throw new InvalidOperationException("Song database is not initialized.");
        var import = await ImportSongsCoreAsync(
            database,
            new SongBulkImportRequest(
                batch.ActiveRoots,
                batch.DiscoveredChartPaths,
                batch.Candidates),
            linked.Token).ConfigureAwait(false);

        var hierarchy = Stopwatch.StartNew();
        FinalizePendingNodes(batch, import.ChartsByPath);
        hierarchy.Stop();
        PublishEnumeration(batch);

        return new SongEnumerationResult(batch, import, hierarchy.Elapsed);
    }
    finally
    {
        EndEnumeration(linked);
    }
}
```

Implement enumeration lifetime under `_lockObject`:

```csharp
private CancellationTokenSource BeginEnumeration(CancellationToken token)
{
    lock (_lockObject)
    {
        if (_enumCancellation is { IsCancellationRequested: false })
            throw new InvalidOperationException("Song enumeration is already in progress.");
        _enumCancellation?.Dispose();
        _enumCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        return _enumCancellation;
    }
}

private void EndEnumeration(CancellationTokenSource source)
{
    lock (_lockObject)
    {
        if (ReferenceEquals(_enumCancellation, source))
            _enumCancellation = null;
    }
    source.Dispose();
}
```

Keep the existing count-returning wrapper's concurrent-enumeration guard so its
current `0` result remains compatible; only the new startup method treats a
concurrent call as invalid.

`FinalizePendingNodes` must:

1. Resolve every ordered path through `ChartsByPath`.
2. Use the resolved chart's eager `Song` and all same-`SongId` charts to call
   `CreateSongNodeFromDatabaseEntities`.
3. Replace the placeholder at the same parent-child or root-list index.
4. Set `Parent` and `DatabaseSongId`.
5. Never call `GetSongWithChartsAsync`.

`PublishEnumeration` replaces `_rootSongs`, then updates
`DiscoveredScoreCount`, `EnumeratedFileCount`, and events. It runs only after
the importer returns successfully.

- [ ] **Step 6: Remove the production hot-path calls and swallowed outcomes**

Remove production enumeration calls to:

- `SongDatabaseService.AddSongAsync`.
- `SongDatabaseService.GetSongWithChartsAsync`.
- `SongDatabaseService.CleanupStaleChartsAsync`.
- `GroupSongNodesBySong`.

Remove the `BuildSongListFromDatabaseAsync` startup cleanup call. Keep the
method as the single cache/fallback hierarchy builder, but load `GetSongsAsync`
once before iterating active roots.

Change compatibility wrappers to rethrow cancellation and failure:

```csharp
public async Task<int> EnumerateSongsAsync(
    string[] searchPaths,
    IProgress<EnumerationProgress>? progress = null,
    CancellationToken cancellationToken = default)
{
    lock (_lockObject)
    {
        if (_enumCancellation is { IsCancellationRequested: false })
            return 0;
    }
    var result = await EnumerateAndImportSongsAsync(
        searchPaths, progress, cancellationToken).ConfigureAwait(false);
    return result.Batch.Candidates.Count;
}
```

Delete normal success diagnostics inside the traversal and grouping path.

- [ ] **Step 7: Update conflicting legacy test expectations**

Make these intentional contract changes explicit:

- `EnumerateSongsAsync_WithCancelledToken_ShouldThrowOperationCanceledException`.
- `EnumerateSongsAsync_WhenCancelledAfterFirstProgressReport_ShouldThrowAndNotPublishPartialCount`.
- Rename the existing same-title/artist cross-directory test to
  `EnumerateSongsAsync_WithSameMetadataInDifferentDirectories_ShouldKeepSeparateSongs`
  and expect two score nodes.
- Keep all `set.def`, `box.def`, bookmark, and same-directory grouping tests.

- [ ] **Step 8: Run song-manager regressions and commit**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~SongManagerBulkEnumerationTests|FullyQualifiedName~SongManagerTests|FullyQualifiedName~SongManagerCoverageTests|FullyQualifiedName~SetDefParserTests|FullyQualifiedName~SongManagerBookmarkTests"
rtk git add DTXMania.Game/Lib/Song/SongManager.cs \
  DTXMania.Test/Song/SongManagerBulkEnumerationTests.cs \
  DTXMania.Test/Song/SongManagerTests.cs \
  DTXMania.Test/Song/SongManagerCoverageTests.cs
rtk git commit -m "refactor: batch startup song enumeration"
```

---

### Task 7: Make Startup Completion-Driven and Select One Load Path

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/StartupStage.cs:45-78,256-268,347-468,580-950`
- Modify: `DTXMania.Test/Stage/StartupStageLogicTests.cs:20-220,320-740,880-1050`
- Modify: `DTXMania.Game/Lib/Stage/StartupSongLoadSummary.cs`

**Interfaces:**
- Consumes: `SongEnumerationResult` from Task 6.
- Produces: one cache/enumeration decision and one hierarchy build.
- Produces: terminal task outcomes without duration waits.
- Produces: a one-frame Title-transition guard.

- [ ] **Step 1: Replace duration-based tests with completion-based tests**

Write or update tests to assert:

```csharp
[Fact]
public void UpdateCurrentPhase_WhenAsyncTaskCompletesImmediately_ShouldAdvanceAtAnyElapsedTime()
{
    var stage = CreateStage(
        phase: StartupPhase.SongListDB,
        elapsedTime: 0.001,
        phaseStartTime: 0.0,
        currentAsyncTask: Task.CompletedTask);

    ReflectionHelpers.InvokePrivateMethod(stage, "UpdateCurrentPhase");

    Assert.Equal(
        StartupPhase.SongsDB,
        ReflectionHelpers.GetPrivateField<StartupPhase>(stage, "_startupPhase"));
}

[Fact]
public void OnUpdate_WhenCompleteBeforeAnyDraw_ShouldNotRequestTitle()
{
    var stageManager = new Mock<IStageManager>();
    var game = ReflectionHelpers.CreateGame();
    ReflectionHelpers.SetPrivateField(
        game, "<StageManager>k__BackingField", stageManager.Object);
    var stage = CreateStage(
        phase: StartupPhase.Complete,
        elapsedTime: 0.0,
        phaseStartTime: 0.0,
        game: game);

    ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.001);

    stageManager.Verify(
        manager => manager.ChangeStage(
            It.IsAny<StageType>(), It.IsAny<IStageTransition>()),
        Times.Never);
}

[Fact]
public void OnUpdate_WhenCompleteAfterOneDraw_ShouldRequestTitleOnce()
{
    var stageManager = new Mock<IStageManager>();
    var game = ReflectionHelpers.CreateGame();
    ReflectionHelpers.SetPrivateField(
        game, "<StageManager>k__BackingField", stageManager.Object);
    var stage = new GraphicsControlledStartupStage(game);
    ReflectionHelpers.SetPrivateField(stage, "_startupPhase", StartupPhase.Complete);
    ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", stage.SpriteBatchStub);
    ReflectionHelpers.SetPrivateField(stage, "_whitePixel", stage.WhitePixelStub);
    ReflectionHelpers.InvokePrivateMethod(stage, "OnDraw", 0.001);

    ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.001);
    ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.001);

    stageManager.Verify(
        manager => manager.ChangeStage(
            StageType.Title,
            It.IsAny<StartupToTitleTransition>()),
        Times.Once);
}
```

- [ ] **Step 2: Write one-path orchestration tests**

Update `ControlledStartupStage` so enumeration returns a
`SongEnumerationResult`. Assert:

```csharp
[Fact]
public async Task RunSongLoadAsync_WhenEnumerationNeeded_ShouldNotBuildDatabaseHierarchy()
{
    var stage = CreateControlledStage();
    stage.NextNeedsEnumerationResult = true;

    var task = (Task)ReflectionHelpers.InvokePrivateMethod(
        stage, "RunSongLoadAsync")!;
    await task;

    Assert.Equal(1, stage.NeedsEnumerationCalls);
    Assert.Equal(1, stage.EnumerateSongsCalls);
    Assert.Equal(0, stage.BuildSongListCalls);
    Assert.Equal(0, stage.SaveSongsDatabaseCalls);
}

[Fact]
public async Task RunSongLoadAsync_WhenCacheValid_ShouldBuildDatabaseHierarchyOnce()
{
    var stage = CreateControlledStage();
    stage.NextNeedsEnumerationResult = false;
    stage.ForceEnumerationForTest = false;

    var task = (Task)ReflectionHelpers.InvokePrivateMethod(
        stage, "RunSongLoadAsync")!;
    await task;

    Assert.Equal(1, stage.NeedsEnumerationCalls);
    Assert.Equal(0, stage.EnumerateSongsCalls);
    Assert.Equal(1, stage.BuildSongListCalls);
    Assert.Equal(0, stage.SaveSongsDatabaseCalls);
}
```

The test subclass may override a new `protected virtual bool ForceEnumeration`
property to exercise `false`; production continues to return `true`.
Add this exact override to `ControlledStartupStage`:

```csharp
public bool ForceEnumerationForTest { get; set; } = true;
protected override bool ForceEnumeration => ForceEnumerationForTest;

public SongEnumerationResult NextEnumerationResult { get; set; } =
    CreateEmptyEnumerationResult();

protected override Task<SongEnumerationResult> EnumerateSongsCoreAsync(
    string[] songPaths,
    IProgress<EnumerationProgress> progressReporter,
    CancellationToken cancellationToken)
{
    EnumerateSongsCalls++;
    LastSongPaths = songPaths;
    LastEnumerationToken = cancellationToken;
    if (ReportedEnumerationProgress != null)
        progressReporter.Report(ReportedEnumerationProgress);
    return Task.FromResult(NextEnumerationResult);
}

private static SongEnumerationResult CreateEmptyEnumerationResult()
{
    var batch = new SongEnumerationBatch
    {
        ActiveRoots = Array.Empty<string>(),
        DiscoveredChartPaths = new HashSet<string>(SongPathIdentity.Comparer),
        Candidates = new List<SongImportCandidate>(),
        RootNodes = new List<SongListNode>(),
        PendingSongs = new List<PendingSongNode>(),
        Errors = new List<SongEnumerationError>(),
        DiscoveryAndParsingDuration = TimeSpan.Zero,
        IsComplete = true
    };
    var import = new SongBulkImportResult(
        new Dictionary<string, SongChart>(SongPathIdentity.Comparer),
        0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, TimeSpan.Zero);
    return new SongEnumerationResult(batch, import, TimeSpan.Zero);
}
```

- [ ] **Step 3: Write failure and cancellation fallback tests**

Assert:

- Cancellation sets summary outcome `Cancellation`, preserves roots, does not
  run cache fallback, and leaves the phase terminal rather than hanging.
- Enumeration failure sets summary outcome `Failure`, calls
  `BuildSongListFromDatabaseCoreAsync` once without cleanup, preserves the
  original exception on the task, and leaves a visible `Error` progress string.
- A failed cache fallback leaves an empty or previously published hierarchy and
  still reaches the terminal phase.

- [ ] **Step 4: Run the startup tests and verify failure**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~StartupStageLogicTests"
```

Expected: old duration assertions and old save/build calls fail.

- [ ] **Step 5: Remove duration gating and add the frame guard**

Keep phase messages, but remove duration from phase completion decisions.
Add:

```csharp
private bool _hasRenderedStartupFrame;
private bool _titleTransitionRequested;

protected override void OnUpdate(double deltaTime)
{
    _elapsedTime += deltaTime;
    UpdateCurrentPhase();

    if (_startupPhase == StartupPhase.Complete
        && _hasRenderedStartupFrame
        && !_titleTransitionRequested)
    {
        _titleTransitionRequested = true;
        WriteSummaryOnce();
        _game.StageManager?.ChangeStage(
            StageType.Title,
            new StartupToTitleTransition(1.0));
    }
}

protected override void OnDraw(double deltaTime)
{
    if (_spriteBatch == null)
        return;

    BeginSpriteBatchCore(_spriteBatch);
    DrawStartupContent();
    EndSpriteBatchCore(_spriteBatch);
    _hasRenderedStartupFrame = true;
}
```

Extract the existing draw statements between `BeginSpriteBatchCore` and
`EndSpriteBatchCore` into `DrawStartupContent` without changing their order or
rendering behavior.

Reset both booleans in `OnActivate`. For a synchronous phase, advance after
`PerformPhaseOperationSync` runs. For an asynchronous phase, advance only when
its task is `RanToCompletion`, `Faulted`, or `Canceled`; do not inspect elapsed
duration.

- [ ] **Step 6: Select and execute the song-load path once**

Replace separate cache load, change detection, enumeration, rebuild, and save
work with:

```csharp
private StartupSongLoadPath _selectedLoadPath = StartupSongLoadPath.Unknown;
private StartupSongLoadOutcome _songLoadOutcome = StartupSongLoadOutcome.Success;
private string? _songLoadError;
private SongEnumerationResult? _enumerationResult;
private TimeSpan _databaseInitializationDuration;
private TimeSpan _cacheHierarchyDuration;

protected virtual bool ForceEnumeration => _forceEnumeration;

private async Task RunSongLoadAsync()
{
    _needsEnumeration = await NeedsEnumerationCoreAsync(
        _songPaths, ForceEnumeration).ConfigureAwait(false);

    _selectedLoadPath = _needsEnumeration.Value
        ? StartupSongLoadPath.Enumeration
        : StartupSongLoadPath.Cache;

    if (_needsEnumeration.Value)
    {
        _enumerationResult = await EnumerateSongsCoreAsync(
            _songPaths,
            CreateEnumerationProgressReporter(),
            _cancellationTokenSource.Token).ConfigureAwait(false);
        return;
    }

    var hierarchy = Stopwatch.StartNew();
    await BuildSongListFromDatabaseCoreAsync(_songPaths).ConfigureAwait(false);
    hierarchy.Stop();
    _cacheHierarchyDuration = hierarchy.Elapsed;
}

private IProgress<EnumerationProgress> CreateEnumerationProgressReporter() =>
    new Progress<EnumerationProgress>(progress =>
    {
        var phaseInfo = _phaseInfo[StartupPhase.EnumerateSongs];
        if (!string.IsNullOrEmpty(progress.CurrentFile))
        {
            _currentProgressMessage =
                $"{phaseInfo.message} [{progress.ProcessedCount} processed, " +
                $"{progress.DiscoveredSongs} songs] " +
                Path.GetFileName(progress.CurrentFile);
        }
        else if (!string.IsNullOrEmpty(progress.CurrentDirectory))
        {
            _currentProgressMessage =
                $"{phaseInfo.message} Scanning directory: " +
                Path.GetFileName(progress.CurrentDirectory);
        }
        else
        {
            _currentProgressMessage =
                $"{phaseInfo.message} [{progress.ProcessedCount} processed, " +
                $"{progress.DiscoveredSongs} songs found]";
        }
    });
```

Start this once from the existing user-facing song-load phases. Make
`BuildSongLists` a no-op confirmation phase and make `SaveSongsDB` finalization
call only `MarkSongManagerInitialized`; remove the database-statistics query.

Use this exact operation mapping:

- `SystemSounds`: synchronous existing placeholder.
- `ConfigValidation`: synchronous path/config validation.
- `SongListDB`: asynchronous database initialization.
- `SongsDB`, `LoadScoreCache`, and `LoadScoreFiles`: synchronous display-only
  phases; they do not query or scan.
- `EnumerateSongs`: asynchronous `RunSongLoadAsync`, which selects cache or
  enumeration once.
- `BuildSongLists`: synchronous display-only confirmation because the selected
  path already produced the hierarchy.
- `SaveSongsDB`: synchronous `MarkSongManagerInitialized`; it does not query
  database statistics.

Use this seam:

```csharp
protected virtual Task<SongEnumerationResult> EnumerateSongsCoreAsync(
    string[] songPaths,
    IProgress<EnumerationProgress> progressReporter,
    CancellationToken cancellationToken) =>
    _songManager.EnumerateAndImportSongsAsync(
        songPaths, progressReporter, cancellationToken);
```

- [ ] **Step 7: Propagate outcomes and write the final aggregate summary**

Do not swallow `OperationCanceledException`. On another exception:

1. Set failure outcome and error text.
2. Attempt `BuildSongListFromDatabaseCoreAsync` once.
3. Keep the original exception as the phase task outcome.
4. Do not run stale cleanup.

Implement that disposition around the enumeration branch:

```csharp
try
{
    _enumerationResult = await EnumerateSongsCoreAsync(
        _songPaths,
        CreateEnumerationProgressReporter(),
        _cancellationTokenSource.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    _songLoadOutcome = StartupSongLoadOutcome.Cancellation;
    _songLoadError = "cancelled";
    throw;
}
catch (Exception ex)
{
    _songLoadOutcome = StartupSongLoadOutcome.Failure;
    _songLoadError = ex.Message;
    try
    {
        var fallback = Stopwatch.StartNew();
        await BuildSongListFromDatabaseCoreAsync(_songPaths).ConfigureAwait(false);
        fallback.Stop();
        _cacheHierarchyDuration = fallback.Elapsed;
    }
    catch (Exception fallbackException)
    {
        Debug.WriteLine(
            $"StartupStage: committed cache fallback failed: " +
            $"{fallbackException.Message}");
    }
    throw;
}
```

Build the final summary from:

- Database initialization stopwatch.
- `SongEnumerationBatch.DiscoveryAndParsingDuration`.
- `SongBulkImportResult.PersistenceDuration`.
- `SongBulkImportResult.CleanupDuration`.
- `SongEnumerationResult.HierarchyDuration` or cache hierarchy stopwatch.
- Counts in the enumeration/import result.

Write it through the existing `WriteStartupSummary` one-shot method for
success, cancellation, and failure.

```csharp
private void WriteSummaryOnce()
{
    if (_startupSummaryWritten)
        return;
    _startupSummaryWritten = true;
    _startupStopwatch.Stop();

    var batch = _enumerationResult?.Batch;
    var import = _enumerationResult?.Import;
    var summary = new StartupSongLoadSummary(
        _selectedLoadPath,
        _songLoadOutcome,
        _startupStopwatch.Elapsed,
        _databaseInitializationDuration,
        batch?.DiscoveryAndParsingDuration ?? TimeSpan.Zero,
        import?.PersistenceDuration ?? TimeSpan.Zero,
        import?.CleanupDuration ?? TimeSpan.Zero,
        _enumerationResult?.HierarchyDuration ?? _cacheHierarchyDuration,
        batch?.DiscoveredChartPaths.Count ?? 0,
        batch?.Candidates.Count ?? 0,
        batch?.PendingSongs.Count ?? 0,
        import?.Added ?? 0,
        import?.Updated ?? 0,
        import?.Preserved ?? 0,
        import?.Skipped ?? 0,
        import?.Conflicts ?? 0,
        import?.StaleCharts ?? 0,
        _songLoadError);
    WriteStartupSummary(summary.Format());
}
```

Measure `_databaseInitializationDuration` around
`InitializeDatabaseServiceCoreAsync`. Reset all new fields in `OnActivate`.

- [ ] **Step 8: Verify startup and song-manager tests, then commit**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~StartupStageLogicTests|FullyQualifiedName~StartupSongLoadSummaryTests|FullyQualifiedName~SongManagerBulkEnumerationTests"
rtk git add DTXMania.Game/Lib/Stage/StartupStage.cs \
  DTXMania.Game/Lib/Stage/StartupSongLoadSummary.cs \
  DTXMania.Test/Stage/StartupStageLogicTests.cs
rtk git commit -m "refactor: make startup completion driven"
```

---

### Task 8: Run Regression Suites and Record the Optimized Benchmark

**Files:**
- Modify: `docs/performance/HPA-192-startup-benchmark.md`
- Verify: `docs/performance/HPA-192-corpus-manifest.tsv`

**Interfaces:**
- Consumes: all implementation commits and the Task 2 runner.
- Produces: three optimized fresh runs, medians, percentage improvement, and acceptance result.

- [ ] **Step 1: Run focused persistence and startup suites**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~SongDatabaseServiceBulkImportTests|FullyQualifiedName~SongManagerBulkEnumerationTests|FullyQualifiedName~StartupStageLogicTests|FullyQualifiedName~StartupSongLoadSummaryTests"
```

Expected: PASS.

- [ ] **Step 2: Run the complete macOS-safe suite**

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: PASS with zero failed tests.

- [ ] **Step 3: Build the macOS game in Release**

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Release
```

Expected: build succeeds with zero errors.

- [ ] **Step 4: Verify the corpus did not change**

Regenerate the TSV to a temporary file with the Task 2 command and compare:

```bash
rtk bash -lc '
root="/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles"
rtk rg --files "$root" | LC_ALL=C sort | while IFS= read -r file; do
  relative="${file#"$root"/}"
  size="$(stat -f "%z" "$file")"
  hash="$(shasum -a 256 "$file" | awk "{print \\$1}")"
  printf "%s\t%s\t%s\n" "$relative" "$size" "$hash"
done
' > /private/tmp/HPA-192-corpus-manifest.tsv
rtk diff -u \
  docs/performance/HPA-192-corpus-manifest.tsv \
  /private/tmp/HPA-192-corpus-manifest.tsv
```

Expected: no output and exit status 0. Reconfirm the supported-chart count is
`100`.

- [ ] **Step 5: Run three optimized fresh imports**

```bash
rtk bash tools/hpa192/benchmark-startup.sh \
  "/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles" \
  optimized 3
```

Expected for every run:

- Game reaches Title.
- Exactly one `HPA192_STARTUP` line is captured.
- `path=enumeration` and `outcome=success`.
- `discovered=100`, `parsed=100`, and `groups=27`.
- Fresh database inspection reports 100 charts and 27 songs.

- [ ] **Step 6: Complete the benchmark report**

Append every optimized run and compute:

```text
improvement_percent =
    100 * (baseline_median_ms - optimized_median_ms) / baseline_median_ms
```

Record:

- Optimized commit.
- Three external wall times and three aggregate summary lines.
- Baseline and optimized medians.
- Percentage improvement.
- Whether improvement is at least 70 percent.
- Whether optimized median is at most 8 seconds.
- The largest remaining measured subphase if the 8-second target is missed.

- [ ] **Step 7: Review the production hot path**

Verify with source search:

```bash
rtk rg -n "AddSongAsync|GetSongWithChartsAsync|CleanupStaleChartsAsync|SaveChangesAsync" \
  DTXMania.Game/Lib/Song/SongManager.cs \
  DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs
```

Expected:

- No `AddSongAsync`, `GetSongWithChartsAsync`, or
  `CleanupStaleChartsAsync` call in production enumeration.
- One `SaveChangesAsync` call inside `ImportSongsAsync`.
- Legacy helper calls remain only inside `AddSongAsync` and unrelated score
  persistence methods.

- [ ] **Step 8: Commit the final evidence**

```bash
rtk git add docs/performance/HPA-192-startup-benchmark.md
rtk git commit -m "docs: record HPA-192 optimized benchmark"
```

---

## Final Acceptance Checklist

- [ ] Fresh import uses one context, one explicit transaction, and one save.
- [ ] No per-chart database reload remains.
- [ ] Existing IDs, bookmarks, speed variants, score state, recent play, and history survive rescans.
- [ ] Same metadata in different directories remains separate.
- [ ] `set.def` charts share one logical song.
- [ ] Stale cleanup is discovery-set scoped and runs only for a complete batch.
- [ ] Records outside active roots remain untouched.
- [ ] Cancellation and failure roll back and do not publish temporary hierarchy.
- [ ] Enumeration publishes its own hierarchy; cache/fallback builds from SQLite once.
- [ ] Startup phases advance on operation completion and Title waits for one rendered frame.
- [ ] One aggregate stdout line exists for success, cancellation, and failure.
- [ ] The macOS-safe suite and Release build pass.
- [ ] The unchanged 100-chart corpus has three baseline and three optimized measurements.
- [ ] Median fresh-startup improvement is at least 70 percent; the report states whether the 8-second target is met.
