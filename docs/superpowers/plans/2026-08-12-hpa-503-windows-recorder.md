# HPA-503 Windows Recorder Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build one Windows-first `dtx-video` command that launches CX through normal Title GAME START, waits for the real Song Select library, records one exact prepared chart through AutoPlay Result with OBS, and leaves the user's live CX data untouched.

**Architecture:** Add one plain `net8.0` recorder executable plus one plain test project. Reuse `DTXMania.Automation` for CX process/API control, keep one imperative `RecordWorkflow`, keep OBS behind four operations with a pure testable `ObsProtocol`, and use a unique disposable app-data root containing only a patched copy of the user's normalized `Config.ini`.

**Tech Stack:** .NET 8, `DTXMania.Automation`, built-in `ClientWebSocket`, `System.Text.Json`, xUnit, optional external `ffprobe`.

## Global Constraints

- Live HPA-503 recording is Windows x64 only; unit tests remain plain `net8.0` and run on Windows/macOS.
- `DTXMania.VideoRecorder` references `DTXMania.Automation` only; do not reference `DTXMania.Game`, `DTXMania.E2E`, or MCP.
- Keep production recorder types internal and expose them only through `InternalsVisibleTo("DTXMania.VideoRecorder.Tests")`.
- One unique disposable app-data root per record run; never copy the live `songs.db`, WAL, caches, scores, or history.
- Consume a current CX-normalized `Config.ini`: preserve `SkinPath=Default`, require filesystem path values to already be fully qualified, and reject relative/legacy values with an instruction to run CX once.
- Recorder-owned config overrides are limited to Game API enable/port/key, AutoPlay, NoFail, 1280x720, and windowed mode.
- Enter Song Select through Title GAME START using one 50 ms Enter keypress; never call `ChangeStageAsync`.
- Wait for a populated Song Select (`SelectedSongTitle` non-empty) before calling `PrepareVideoChartAsync` once.
- `HttpClient.Timeout = Timeout.InfiniteTimeSpan`; recorder-owned finite workflow bounds own cancellation.
- OBS has exactly four recorder-facing operations: connect, status, start, stop.
- Raw-path containment belongs only to `RecordingArtifactVerifier`, not the OBS client.
- Preserve raw OBS output; publish with copy semantics and fail on destination collision.
- `ffprobe` remains optional and PATH-based; do not add FFMpegCore/MMTools packages to the recorder.
- Diagnostics are one `run.json` plus CX stdout/stderr; never copy the sandbox `Config.ini`.
- No live OBS CI, scene/source screenshot validation, persistent recorder DB, batch queue, capture framework, or formal visual/audio acceptance in HPA-503.

---

## File structure

```text
DTXMania.VideoRecorder/
  DTXMania.VideoRecorder.csproj
  Properties/AssemblyInfo.cs
  Program.cs
  RecorderCommandLine.cs
  Configuration/RecorderEnvironment.cs
  Sandbox/RecordingSandbox.cs
  Obs/IObsRecorder.cs
  Obs/ObsProtocol.cs
  Obs/ObsWebSocketRecorder.cs
  Workflow/IGameRecordingControl.cs
  Workflow/AutomationGameRecordingControl.cs
  Workflow/RecordingStep.cs
  Workflow/RecordWorkflow.cs
  Diagnostics/RecorderDiagnostics.cs
  Media/RecordingArtifactVerifier.cs

DTXMania.VideoRecorder.Tests/
  DTXMania.VideoRecorder.Tests.csproj
  Sandbox/RecordingSandboxTests.cs
  Obs/ObsProtocolTests.cs
  Workflow/RecordWorkflowTests.cs
  Diagnostics/RecorderDiagnosticsTests.cs
  Media/RecordingArtifactVerifierTests.cs

Modify:
  DTXMania.sln
  .github/workflows/build-and-test.yml
```

Keep HPA-503 as one implementation PR. The five tasks below are reviewer/agent checkpoints, not separate Linear tickets or architectural layers.

---

### Task 1: Add recorder projects, CLI/environment contract, and normalized-config sandbox

**Files:**
- Create: `DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj`
- Create: `DTXMania.VideoRecorder/Properties/AssemblyInfo.cs`
- Create: `DTXMania.VideoRecorder/Program.cs`
- Create: `DTXMania.VideoRecorder/RecorderCommandLine.cs`
- Create: `DTXMania.VideoRecorder/Configuration/RecorderEnvironment.cs`
- Create: `DTXMania.VideoRecorder/Sandbox/RecordingSandbox.cs`
- Create: `DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj`
- Create: `DTXMania.VideoRecorder.Tests/Sandbox/RecordingSandboxTests.cs`
- Modify: `DTXMania.sln`

**Produces:**

```csharp
internal sealed record RecorderEnvironment(
    Uri ObsUrl,
    string ObsPassword,
    string ObsOutputDirectory,
    string SourceAppDataRoot);

internal sealed class RecordingSandbox
{
    public string RunRoot { get; }
    public string AppDataRoot { get; }
    public string ConfigPath { get; }
    public int ApiPort { get; }
    public string ApiKey { get; }

    public static RecordingSandbox Create(string sourceAppDataRoot);
    public Task DeleteOnSuccessAsync();
}
```

`RecorderCommandLine` supports only:

```text
doctor
record --chart <absolute path> --output <directory>
```

- [ ] **Step 1: Scaffold the two projects and test visibility.**

Use the same nullable/implicit-using and xUnit versions as `DTXMania.Automation.Tests`.

Production project reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\DTXMania.Automation\DTXMania.Automation.csproj" />
</ItemGroup>
```

Test visibility:

```csharp
// DTXMania.VideoRecorder/Properties/AssemblyInfo.cs
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DTXMania.VideoRecorder.Tests")]
```

Add both projects to `DTXMania.sln`.

Run:

```bash
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

Expected: both projects compile before behavior tests are added.

- [ ] **Step 2: Write failing normalized-config sandbox tests.**

Use a temporary source app-data directory and a source `Config.ini` containing absolute paths.

Required cases:

```csharp
[Fact]
public void Create_ShouldPreserveDefaultSkinToken()
{
    // source contains SkinPath=dEfAuLt
    // sandbox config contains the same token value, not <sandbox>/Default
}

[Fact]
public void Create_ShouldPreserveAbsoluteFilesystemPaths()
{
    // DTXPath, SongRoot.0/1, SystemSkinRoot and custom SkinPath are absolute.
    // Assert the copied values remain the same paths.
}

[Theory]
[InlineData("DTXPath=relative-songs")]
[InlineData("SongRoot.0=~/charts")]
[InlineData("SystemSkinRoot=System")]
[InlineData("SkinPath=Skins/Custom")]
public void Create_ShouldRejectNonNormalizedFilesystemPaths(string badLine)
{
    // Assert InvalidOperationException contains:
    // "Open CX once and exit normally"
}

[Fact]
public void Create_ShouldRejectMissingNormalizedSongRoot()
{
    // Current CX SaveConfig writes at least SongRoot.0.
    // Missing SongRoot.* fails instead of inventing compatibility behavior.
}

[Fact]
public void Create_ShouldOverrideOnlyRecorderOwnedKeys()
{
    // Preserve LastUsedSkin, ScrollSpeed, PlaySpeedPercent, PitchSemitones,
    // volumes and unrelated lines. Assert only API/AutoPlay/NoFail/1280x720/windowed change.
}

[Fact]
public void Create_ShouldNeverCopyLiveDatabaseOrCaches()
{
    // Put songs.db, songs.db-wal, Cache/, CrashReports/ beside source config.
    // Assert none exist under sandbox root.
}
```

Also cover successful delete and failure retention.

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter "FullyQualifiedName~RecordingSandboxTests"
```

Expected before implementation: FAIL.

- [ ] **Step 3: Implement strict source-config validation and copy/patch.**

Do not create a path-normalization helper.

Parse enough INI text to inspect/replace keys section-agnostically. Before copying:

```csharp
static bool IsDefaultSkinToken(string value) =>
    value.Trim().Equals("Default", StringComparison.OrdinalIgnoreCase);

static void RequireAbsolutePath(string key, string value)
{
    if (!Path.IsPathFullyQualified(value))
        throw new InvalidOperationException(
            $"Source Config.ini key '{key}' is not normalized. " +
            "Open CX once and exit normally, then retry dtx-video.");
}
```

Validation rules:

```text
SkinPath == Default     -> allowed, leave token unchanged
custom SkinPath         -> RequireAbsolutePath
DTXPath                 -> RequireAbsolutePath
SystemSkinRoot          -> RequireAbsolutePath
all SongRoot.<n> values -> RequireAbsolutePath
no SongRoot.<n>         -> fail with the same normalize-config guidance
LastUsedSkin            -> never interpret as a path
```

Then create:

```text
%TEMP%/DTXManiaCX-video/<run-id>/appdata/Config.ini
```

Patch only:

```text
EnableGameApi=True
GameApiPort=<ephemeral loopback port>
GameApiKey=<random secret>
AutoPlay=True
NoFail=True
ScreenWidth=1280
ScreenHeight=720
FullScreen=False
```

Use a small local ephemeral-port helper and `RandomNumberGenerator` for the API key. Do not move repo-root/port helpers into Automation.

- [ ] **Step 4: Implement CLI and environment validation.**

Environment:

```text
DTXMANIA_VIDEO_OBS_URL        default ws://127.0.0.1:4455
DTXMANIA_VIDEO_OBS_PASSWORD   optional when OBS auth disabled
DTXMANIA_VIDEO_OBS_OUTPUT_DIR required for record
```

`record` fails before mutation when:

```text
not Windows
chart path blank/not fully-qualified/missing
output directory cannot be created
source Config.ini missing or not normalized
OBS URL is non-loopback
OBS output directory missing/invalid
```

`doctor` parses no chart/output options and does not create a sandbox.

Do not add `System.CommandLine`.

- [ ] **Step 5: Verify and commit Task 1.**

```bash
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter "FullyQualifiedName~RecordingSandboxTests"
```

Commit:

```bash
git add DTXMania.VideoRecorder DTXMania.VideoRecorder.Tests DTXMania.sln
git commit -m "feat: add recorder sandbox foundation"
```

**Task 1 deliverable:** a compileable recorder shell with a disposable sandbox that accepts current CX config semantics without reimplementing config path compatibility.

---

### Task 2: Add pure OBS protocol mapping, the four-op socket client, and `doctor`

**Files:**
- Create: `DTXMania.VideoRecorder/Obs/IObsRecorder.cs`
- Create: `DTXMania.VideoRecorder/Obs/ObsProtocol.cs`
- Create: `DTXMania.VideoRecorder/Obs/ObsWebSocketRecorder.cs`
- Create: `DTXMania.VideoRecorder.Tests/Obs/ObsProtocolTests.cs`
- Modify: `DTXMania.VideoRecorder/Program.cs`

**Consumes:** `RecorderEnvironment` from Task 1.

**Produces:**

```csharp
internal sealed record ObsRecordStatus(bool IsRecording);

internal interface IObsRecorder : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken cancellationToken);
    Task StartRecordAsync(CancellationToken cancellationToken);
    Task<string> StopRecordAsync(CancellationToken cancellationToken);
}
```

Pure protocol owner:

```csharp
internal static class ObsProtocol
{
    internal static string ComputeAuthentication(
        string password,
        string salt,
        string challenge);

    internal static ObsRecordStatus ParseRecordStatus(JsonElement responseData);
    internal static void EnsureRequestSucceeded(string requestType, JsonElement requestStatus);
    internal static string ParseStopOutputPath(JsonElement responseData);
}
```

- [ ] **Step 1: Write failing `ObsProtocol` tests.**

Cover:

```csharp
[Fact]
public void ComputeAuthentication_ShouldFollowObsWebSocketV5Formula()
{
    // secret = Base64(SHA256(UTF8(password + salt)))
    // auth   = Base64(SHA256(UTF8(secret + challenge)))
    // Compute expected independently in the test and compare.
}

[Theory]
[InlineData(true)]
[InlineData(false)]
public void ParseRecordStatus_ShouldReadOutputActive(bool active)
{
    // Build responseData { "outputActive": active } and compare ObsRecordStatus.
}

[Fact]
public void EnsureRequestSucceeded_ShouldThrowForFailedRequestStatus()
{
    // requestStatus.result=false with code/comment -> actionable InvalidOperationException.
}

[Fact]
public void ParseStopOutputPath_ShouldRejectMissingPath()
{
    // responseData without outputPath -> InvalidOperationException.
}
```

Include malformed/wrong-kind JSON cases. No socket in these tests.

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter "FullyQualifiedName~ObsProtocolTests"
```

Expected before implementation: FAIL.

- [ ] **Step 2: Implement only the pure v5 protocol behavior.**

Use `SHA256`, `Convert.ToBase64String`, and `System.Text.Json`.

Support only the messages needed for:

```text
Hello -> Identify -> Identified
GetRecordStatus
StartRecord
StopRecord
```

`ObsProtocol` does not own sockets, retries, path containment, source/scene APIs, or generic arbitrary requests.

- [ ] **Step 3: Implement `ObsWebSocketRecorder`.**

Use built-in `ClientWebSocket`.

Keep socket responsibilities limited to:

```text
connect to configured loopback URI
read Hello
send Identify with ObsProtocol auth when required
require Identified
allocate monotonically unique request IDs
send one outstanding request at a time
ignore harmless event frames while awaiting its matching response
map response through ObsProtocol
```

`StartRecordAsync` must confirm active status before returning. Local recording ownership is set by the workflow only after this call succeeds.

`StopRecordAsync` returns `outputPath` exactly as OBS reports it. Do not validate containment here.

- [ ] **Step 4: Implement read-only `doctor`.**

Report pass/warn/fail for:

```text
Windows
repo root + GameProjectPaths.Windows exists
source Config.ini exists and satisfies Task-1 normalized contract
OBS URL is loopback
configured OBS raw output directory exists
OBS Hello/Identify succeeds
GetRecordStatus succeeds and reports active/inactive
ffprobe available/unavailable on PATH
```

Print the manual HPA-503 prerequisite:

```text
Dedicated OBS profile/collection/scene must already provide:
- CX window/program capture
- CX application audio
- Hybrid MP4 recording
- WebSocket enabled
- output directory matching DTXMANIA_VIDEO_OBS_OUTPUT_DIR
```

Also print:

```text
record uses a fresh sandbox songs.db; first-run library enumeration can take several minutes.
```

Do not create a sandbox, start/stop OBS, inspect sources/scenes, or launch CX.

- [ ] **Step 5: Run automated checks and the Task-2 live OBS gate.**

Automated:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter "FullyQualifiedName~ObsProtocolTests"
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
```

Windows manual gate with the dedicated OBS profile already open:

```text
dtx-video doctor
```

Require evidence that Hello/Identify and `GetRecordStatus` succeed, OBS is not recording, and the configured raw output directory is correct. OBS state must remain unchanged.

If the implementation environment cannot access that Windows OBS profile, mark this gate unverified; do not claim the socket path is proven and do not substitute a fake server.

- [ ] **Step 6: Commit Task 2.**

```bash
git add DTXMania.VideoRecorder/Obs DTXMania.VideoRecorder.Tests/Obs DTXMania.VideoRecorder/Program.cs
git commit -m "feat: add minimal OBS recorder client"
```

**Task 2 deliverable:** unit-tested protocol mapping plus the exact four-op live OBS client, with its real handshake/status path exercised independently through `doctor` when the required Windows environment is available.

---

### Task 3: Implement the normal CX journey and recorder ownership/cleanup

**Files:**
- Create: `DTXMania.VideoRecorder/Workflow/IGameRecordingControl.cs`
- Create: `DTXMania.VideoRecorder/Workflow/AutomationGameRecordingControl.cs`
- Create: `DTXMania.VideoRecorder/Workflow/RecordingStep.cs`
- Create: `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`
- Create: `DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs`
- Modify: `DTXMania.VideoRecorder/Program.cs`

**Consumes:** `RecordingSandbox`, `IObsRecorder`.

**Thin test seam:**

```csharp
internal interface IGameRecordingControl : IAsyncDisposable
{
    string StandardOutput { get; }
    string StandardError { get; }

    void Start(GameProcessStartOptions options);
    Task WaitForStartupAsync(TimeSpan timeout, CancellationToken cancellationToken);
    Task<GameStateSnapshot> GetGameStateAsync(CancellationToken cancellationToken);
    Task SendKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken);
    Task PrepareVideoChartAsync(string chartPath, CancellationToken cancellationToken);
    Task<string?> TakeScreenshotBase64Async(CancellationToken cancellationToken);
    Task StartPreparedPreviewAsync(CancellationToken cancellationToken);
    Task ActivatePreparedChartAsync(CancellationToken cancellationToken);
}
```

No `ChangeStageAsync` or stage-wait method belongs on this seam.

Recorder bounds:

```csharp
private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(5);
private static readonly TimeSpan StageTimeout = TimeSpan.FromMinutes(2);
private static readonly TimeSpan PerformanceTimeout = TimeSpan.FromMinutes(20);
private static readonly TimeSpan ExternalIoTimeout = TimeSpan.FromSeconds(15);
```

- [ ] **Step 1: Write failing exact-order and ownership tests.**

Use fake `IGameRecordingControl`, fake `IObsRecorder`, and a tiny controlled delay seam.

Happy-path call order must be:

```text
Start CX
WaitForStartupAsync
poll Title
SendKeyAsync("Enter", 50ms)
poll SongSelect with SelectedSongTitle non-empty
PrepareVideoChartAsync once
TakeScreenshotBase64Async
OBS Connect/GetRecordStatus
OBS Start
StartPreparedPreviewAsync
poll Playing + elapsed >= 10s
ActivatePreparedChartAsync
poll SongTransition
poll PerformanceReady + AutoPlay + TotalNotes>0
poll successful Result under PerformanceTimeout
TakeScreenshotBase64Async
5s delay with no game input
OBS Stop
cleanup
```

Assertions:

```csharp
Assert.DoesNotContain(calls, call => call.StartsWith("ChangeStage"));
Assert.Equal(1, calls.Count(call => call == "PrepareVideoChart"));
Assert.True(IndexOf("SongSelectReady") < IndexOf("PrepareVideoChart"));
Assert.True(IndexOf("PrepareVideoChart") < IndexOf("ObsStart"));
```

Failure cases:

```text
OBS already recording -> fail before Start, never Stop
OBS Start fails -> never acquire ownership, never Stop
failure after successful OBS Start -> exactly one bounded Stop
unexpected stage order -> fail, do not skip forward
AutoPlay false / zero notes / incomplete judgements / non-SongComplete -> fail
cancellation during preview, performance, result hold -> same finally cleanup
second cleanup call -> no duplicate OBS stop/process ownership action
```

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter "FullyQualifiedName~RecordWorkflowTests"
```

Expected before implementation: FAIL.

- [ ] **Step 2: Implement `AutomationGameRecordingControl` as delegation only.**

Construct an `HttpClient` with:

```csharp
Timeout = Timeout.InfiniteTimeSpan
```

Delegate to:

```text
GameProcessDriver.Start
GameProcessDriver.WaitForStartupAsync(client.GetHealthAsync, ...)
JsonRpcGameClient.GetGameStateAsync
JsonRpcGameClient.SendKeyAsync
JsonRpcGameClient.PrepareVideoChartAsync
JsonRpcGameClient.TakeScreenshotBase64Async
JsonRpcGameClient.StartPreparedPreviewAsync
JsonRpcGameClient.ActivatePreparedChartAsync
GameProcessDriver.DisposeAsync
```

Copy the small repo-root/ephemeral-port policy locally when needed. Do not change Automation for a recorder-specific stage helper.

- [ ] **Step 3: Implement the library-ready startup prefix.**

Use Automation `Eventually.UntilAsync` directly from `RecordWorkflow`:

```csharp
await game.WaitForStartupAsync(SetupTimeout, cancellationToken);

await Eventually.UntilAsync(
    token => game.GetGameStateAsync(token),
    state => state.StageType == "Title",
    SetupTimeout,
    TimeSpan.FromMilliseconds(250),
    "Title stage",
    cancellationToken);

await game.SendKeyAsync(
    "Enter",
    TimeSpan.FromMilliseconds(50),
    cancellationToken);

await Eventually.UntilAsync(
    token => game.GetGameStateAsync(token),
    state => state.StageType == "SongSelect"
        && !string.IsNullOrWhiteSpace(state.SelectedSongTitle),
    SetupTimeout,
    TimeSpan.FromMilliseconds(250),
    "populated Song Select library",
    cancellationToken);
```

Then call `PrepareVideoChartAsync` once with a linked `SetupTimeout` cancellation token.

Do not retry permanent "chart not available" errors and do not add library-ready telemetry.

- [ ] **Step 4: Implement the capture journey.**

After successful prepare:

```text
require non-empty CX screenshot base64
connect OBS
GetRecordStatus; reject active recording
StartRecord; only now set obsOwned=true
StartPreparedPreview
```

Prepared-preview gate under `StageTimeout`:

```text
StageType == SongSelect
PreparedPreviewState == Playing
PreparedPreviewElapsedMs >= 10_000
```

Activate once, then observe in order:

```text
SongTransition        under StageTimeout
Performance ready     under StageTimeout
Result completion     under PerformanceTimeout
```

Performance-ready predicate:

```text
StageType == Performance
PerformanceReady == true
AutoPlayEnabled == true
TotalNotes > 0
```

Result predicate:

```text
StageType == Result
StageCompleted == true
ClearFlag == true
CompletionReason == SongComplete
TotalNotes > 0
TotalJudgements == TotalNotes
```

Require a second non-empty CX screenshot, then wait exactly five seconds with no game input.

- [ ] **Step 5: Implement one ownership-aware `finally` path.**

State only what cleanup needs:

```csharp
bool obsOwned = false;
bool obsStopped = false;
string? rawOutputPath = null;
```

Normal stop:

```csharp
rawOutputPath = await StopObsWithTimeoutAsync();
obsStopped = true;
obsOwned = false;
```

Finally:

```text
if obsOwned && !obsStopped:
    attempt one StopRecordAsync bounded by ExternalIoTimeout
always DisposeAsync owned CX control/process
leave sandbox intact until Task-4 finalization decides success/failure cleanup
```

Cleanup exceptions are collected as secondary failures; do not replace the primary exception/cancellation.

`Program` maps Ctrl+C to cancellation (`e.Cancel = true`) so the same finally path runs.

- [ ] **Step 6: Verify and commit Task 3.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --filter "FullyQualifiedName~RecordWorkflowTests"
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
```

Commit:

```bash
git add DTXMania.VideoRecorder/Workflow DTXMania.VideoRecorder.Tests/Workflow DTXMania.VideoRecorder/Program.cs
git commit -m "feat: add Windows recording workflow"
```

**Task 3 deliverable:** a unit-tested orchestration path that cannot prepare before the Song Select library is populated and cannot stop OBS unless the run owns it.

---

### Task 4: Add compact diagnostics and single-owner artifact verification

**Files:**
- Create: `DTXMania.VideoRecorder/Diagnostics/RecorderDiagnostics.cs`
- Create: `DTXMania.VideoRecorder/Media/RecordingArtifactVerifier.cs`
- Create: `DTXMania.VideoRecorder.Tests/Diagnostics/RecorderDiagnosticsTests.cs`
- Create: `DTXMania.VideoRecorder.Tests/Media/RecordingArtifactVerifierTests.cs`
- Modify: `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`
- Modify: `DTXMania.VideoRecorder/Program.cs`

**Produces:**

```text
<output>/diagnostics/<run-id>/run.json
<output>/diagnostics/<run-id>/cx-stdout.log
<output>/diagnostics/<run-id>/cx-stderr.log
```

Suggested narrow API:

```csharp
internal sealed class RecorderDiagnostics
{
    public void Step(RecordingStep step);
    public void Telemetry(string name, GameStateSnapshot state);
    public void Obs(string operation, string outcome);
    public void Artifact(string? rawPath, string? publishedPath, string? warning);
    public void Failure(Exception exception, RecordingStep? lastCompletedStep);
    public Task WriteAsync(string directory, string stdout, string stderr, CancellationToken token);
}

internal sealed record RecordingArtifactResult(
    string RawPath,
    string PublishedPath,
    string? Warning);

internal static class RecordingArtifactVerifier
{
    public static Task<RecordingArtifactResult> VerifyAndPublishAsync(
        string rawPath,
        string configuredObsOutputDirectory,
        string publishDirectory,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing compact-diagnostics tests.**

Verify only the required contract:

```csharp
[Fact]
public async Task WriteAsync_ShouldCreateRunJsonAndCxLogsOnly()
{
    // Assert exact file names: run.json, cx-stdout.log, cx-stderr.log.
    // Assert no copied Config.ini.
}

[Fact]
public async Task WriteAsync_ShouldNotPersistKnownSecrets()
{
    const string apiKey = "api-secret-for-test";
    const string obsPassword = "obs-secret-for-test";
    // Exercise the public diagnostics inputs used by workflow.
    // Assert neither value occurs in any emitted file or safe failure summary.
}
```

`run.json` should contain step timeline, selected telemetry, OBS outcomes, raw/published paths, verifier warning, failure/last step, and retained sandbox path on failure.

Do not create separate summary/steps/telemetry JSON files or a generic redaction service.

- [ ] **Step 2: Write failing artifact-verifier tests.**

Use temporary directories/files.

Required cases:

```csharp
[Fact]
public async Task VerifyAndPublish_ShouldRejectRawPathOutsideObsRoot()
{
    // configured root and raw path are siblings -> fail before publish.
}

[Fact]
public async Task VerifyAndPublish_ShouldRejectMissingOrEmptyRawFile() { }

[Fact]
public async Task VerifyAndPublish_ShouldFailOnDestinationCollision() { }

[Fact]
public async Task VerifyAndPublish_ShouldCopyWithoutDeletingRaw() { }

[Fact]
public async Task VerifyAndPublish_ShouldWarnWhenFfprobeIsUnavailable() { }
```

For containment use full paths and `Path.GetRelativePath`:

```csharp
var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredObsOutputDirectory));
var raw = Path.GetFullPath(rawPath);
var relative = Path.GetRelativePath(root, raw);
var escapes = Path.IsPathRooted(relative)
    || relative == ".."
    || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
```

Do this only in `RecordingArtifactVerifier`; remove any raw-root containment from `ObsWebSocketRecorder`.

- [ ] **Step 3: Implement optional `ffprobe` without a new media dependency.**

Find `ffprobe` on `PATH`. If absent, return a warning.

When present, run one local process bounded by `ExternalIoTimeout`:

```text
ffprobe -v error -show_entries stream=codec_type -of json <raw-file>
```

Parse JSON and require at least one stream with:

```text
codec_type == video
codec_type == audio
```

Keep the `ProcessStartInfo` call private to `RecordingArtifactVerifier`; do not add a generic subprocess wrapper or FFMpegCore/MMTools package.

- [ ] **Step 4: Integrate finalization into `RecordWorkflow`.**

After normal OBS stop:

```text
VerifyAndPublishAsync(rawPath, obsOutputDir, requestedOutput)
record artifact result/warning
mark Completed
write diagnostics
success => delete sandbox
```

Failure/cancellation path:

```text
run ownership cleanup
record primary + cleanup failures
write diagnostics
retain sandbox and record retained path
```

Diagnostics must be attempted after CX output is available. A diagnostics-write failure is secondary and must not hide the original recording failure.

- [ ] **Step 5: Verify and commit Task 4.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
```

Commit:

```bash
git add DTXMania.VideoRecorder/Diagnostics DTXMania.VideoRecorder/Media \
  DTXMania.VideoRecorder.Tests/Diagnostics DTXMania.VideoRecorder.Tests/Media \
  DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs DTXMania.VideoRecorder/Program.cs
git commit -m "feat: finalize recorder artifacts"
```

**Task 4 deliverable:** compact diagnostics and one verifier that owns every trust/publish decision for the raw OBS artifact.

---

### Task 5: Add portable CI coverage and perform the first Windows proof

**Files:**
- Modify: `.github/workflows/build-and-test.yml`
- Create only when useful: `docs/verification/hpa-503-windows-recorder-proof.md`

- [ ] **Step 1: Add recorder unit tests to both normal OS jobs.**

Immediately after the existing Automation tests in Windows and macOS jobs:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --verbosity normal \
  --logger trx \
  --results-directory ./TestResults/VideoRecorder
```

Do not add OBS to CI.

- [ ] **Step 2: Run the authoritative Windows local verification set.**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

Automated verification must not require OBS.

- [ ] **Step 3: Re-run the Task-2 `doctor` gate immediately before proof.**

With the dedicated OBS profile/collection/scene already selected:

```text
dtx-video doctor
```

Confirm:

```text
OBS auth succeeds
OBS is not recording
raw output directory matches DTXMANIA_VIDEO_OBS_OUTPUT_DIR
manual profile requirements are printed
ffprobe status is accurate
```

Do not add source/scene introspection when this check cannot verify capture quality.

- [ ] **Step 4: Produce one real short-chart recording.**

Pick one chart that is:

```text
inside an active SongRoot
already indexable by CX
has a valid preview
comfortably shorter than the 20-minute PerformanceTimeout
```

Run:

```text
dtx-video record --chart <absolute-dtx-path> --output <proof-output-directory>
```

A fresh sandbox may spend minutes enumerating its library. Success evidence must show:

```text
Title -> 50ms Enter -> populated SongSelect
PrepareVideoChart succeeded after library readiness
>=10s PreparedPreviewState=Playing
SongTransition observed
PerformanceReady + AutoPlayEnabled + TotalNotes>0
Result: SongComplete + TotalJudgements==TotalNotes
Result screenshot barrier + 5s hold
owned OBS stop
raw MP4 still exists
published MP4 exists
run.json ends Completed
```

Open the MP4 manually only to confirm it is plausibly the intended recording. Do not turn this into HPA-513's formal quality/setup pass.

- [ ] **Step 5: Confirm isolation and capture evidence.**

Before/after proof:

```text
source Config.ini unchanged by recorder
source songs.db / WAL unchanged by recorder
sandbox owns its own newly built songs.db
successful run sandbox deleted after diagnostics/publication
raw OBS artifact preserved
```

If a durable note helps reviewers, write `docs/verification/hpa-503-windows-recorder-proof.md` with command/result/artifact locations but no secrets or binary MP4.

- [ ] **Step 6: Run final scope review.**

Remove anything that introduced:

```text
persistent recorder database/app-data cache
OBS source/scene screenshot or validation API
FFMpegCore/MMTools recorder dependencies
ChangeStageAsync navigation
new Automation stage/library helper APIs
batch queue/platform adapter/workflow framework
live DB/cache copying
```

Also confirm the design's four documented risks are reflected in implementation behavior rather than hidden by extra abstractions.

- [ ] **Step 7: Commit Task 5 and prepare the implementation PR.**

```bash
git add .github/workflows/build-and-test.yml docs/verification/hpa-503-windows-recorder-proof.md
git commit -m "test: validate recorder vertical slice"
```

If the optional verification note was not created, stage only the workflow file.

Implementation PR description must include:

```text
HPA-503 link
recorder + Automation + game/unit test results
Windows doctor result
proof artifact/diagnostic locations without secrets
raw + published MP4 existence
live app-data/database isolation result
explicit note that HPA-513 owns formal visual/audio/setup acceptance next
```

**Task 5 deliverable:** portable test coverage plus one real Windows proof of the complete HPA-503 vertical slice.

---

## Agent handoff

Execute Tasks 1-5 in order. Review after each task, but keep the implementation in one HPA-503 PR unless actual OBS protocol work alone makes the 2-3 engineer-day target impossible.

Reuse existing seams rather than moving policy into Automation:

```text
GameProcessDriver / JsonRpcGameClient
JsonRpcGameClient HPA-510 prepared-chart methods
GameStateSnapshot PreparedPreviewState / PreparedPreviewElapsedMs
Eventually.UntilAsync
GameProjectPaths.Windows
```

Copy only the small recorder-specific repo-root/ephemeral-port policy locally.

## Definition of done

HPA-503 is ready for implementation review only when:

- `doctor` is non-destructive and the live Windows Hello/Identify/status path has been exercised when the required OBS environment is available;
- `record` waits for Title, uses one normal 50 ms GAME START keypress, waits for a populated Song Select, then prepares once;
- source config is current/normalized, with `SkinPath=Default` preserved and filesystem paths already absolute;
- unique sandbox DB/state never touches the live DB and is removed only after a successful finalized run;
- preview timing uses CX `Playing` state and elapsed >=10 seconds;
- `SongTransition -> Performance -> Result` is observed in order;
- Performance proves AutoPlay/readiness/non-zero notes;
- Result proves complete judgements and `SongComplete`, then remains rendered for five seconds;
- only recorder-owned OBS/CX resources are stopped;
- `RecordingArtifactVerifier` alone validates raw-path containment and publication;
- raw + published non-empty MP4 exist, with video/audio streams required when `ffprobe` is available;
- diagnostics are exactly `run.json` plus CX stdout/stderr and contain no copied Config.ini/secrets;
- recorder tests pass on both OS CI jobs;
- one Windows proof is retained for HPA-513.