# HPA-503 Windows Recorder Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a small external `dtx-video` tool that launches CX through the normal Title GAME START path and records one exact indexed chart from rendered Song Select -> controlled preview -> normal AutoPlay gameplay -> Result using the existing CX Automation API and a manually prepared Windows OBS profile.

**Architecture:** Build one plain `net8.0` recorder executable plus one plain test project. Keep `RecordWorkflow` as the only orchestrator, reuse `DTXMania.Automation` for CX process/API control, use one four-operation `IObsRecorder` seam for OBS, and isolate the user's live data by copying/patching only `Config.ini` into per-run temporary app data. Keep implementation types internal and expose them only to the sibling test assembly.

**Tech Stack:** .NET 8, `DTXMania.Automation`, built-in `ClientWebSocket`, `System.Text.Json`, xUnit, optional external `ffprobe`.

## Global Constraints

- Live recording support in HPA-503 is Windows x64 only.
- `DTXMania.VideoRecorder` must target plain `net8.0` and reference `DTXMania.Automation` only; no `DTXMania.Game`, `DTXMania.E2E`, or MCP reference.
- Do not introduce DI, a workflow engine, a recorder session state machine, a platform-adapter framework, or generic process/OBS/media abstractions.
- Preserve the user's song roots, selected skin semantics, scroll speed, play speed, pitch, volumes, and other presentation preferences by copying the live `Config.ini`.
- `SkinPath=Default` is a logical token and must remain a token; do not rewrite it as an app-data filesystem path.
- Never copy the live `songs.db`, WAL files, score/history data, or caches.
- Recording-owned config overrides are limited to Game API enable/port/key, AutoPlay, NoFail, 1280x720, and windowed mode.
- OBS settings/secrets come from environment configuration, never CLI arguments or diagnostics.
- Stop OBS only when this run successfully started the recording.
- Always terminate the CX process launched by this run.
- Enter Song Select through Title's normal GAME START selection with one 50 ms Enter keypress; never call `ChangeStageAsync`.
- Reuse `Eventually.UntilAsync`; do not add `WaitForStageAsync` or another polling abstraction to Automation.
- Use an infinite `HttpClient.Timeout` and recorder-owned finite operation timeouts; do not copy the E2E harness's five-second transport timeout.
- Preserve raw OBS output; publishing is a copy, not a move/delete.
- `ffprobe` validation is conditional on `ffprobe` being available on `PATH`.
- HPA-513 owns formal visual/audio acceptance and OBS setup documentation; HPA-503 needs only the first proof recording.

---

## File structure to add/change

```text
DTXMania.VideoRecorder/
  DTXMania.VideoRecorder.csproj
  Properties/AssemblyInfo.cs
  Program.cs
  RecorderCommandLine.cs
  Configuration/RecorderEnvironment.cs
  Sandbox/RecordingSandbox.cs
  Obs/IObsRecorder.cs
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
  Obs/ObsWebSocketRecorderTests.cs
  Workflow/RecordWorkflowTests.cs
  Diagnostics/RecorderDiagnosticsTests.cs
  Media/RecordingArtifactVerifierTests.cs

Modify:
  DTXMania.sln
  .github/workflows/build-and-test.yml
```

`IGameRecordingControl` is an internal unit-test seam over the already-tested Automation classes. It must remain a thin delegate/adapter, not gain recorder state or duplicate JSON-RPC/process behavior.

---

### Task 1: Add recorder projects, test visibility, environment contract, and token-safe config sandbox

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

**Interfaces/contracts:**

```text
RecorderEnvironment
- Uri ObsUrl
- string ObsPassword
- string ObsOutputDirectory
- string SourceAppDataRoot

RecordingSandbox
- string RunRoot
- string AppDataRoot
- string ConfigPath
- int ApiPort
- string ApiKey
- Task DeleteOnSuccessAsync()
- no destructive cleanup on failure

RecorderCommandLine
- doctor
- record --chart <absolute path> --output <directory>
```

`DTXMANIA_VIDEO_OBS_URL` defaults to loopback port 4455. `DTXMANIA_VIDEO_OBS_OUTPUT_DIR` is required for recording. Resolve source app data from `DTXMANIA_APPDATA_ROOT` first, otherwise `%LOCALAPPDATA%/DTXManiaCX`.

- [ ] **Step 1: Scaffold the two plain `net8.0` projects, solution entries, and test visibility.**

Use the same nullable/implicit-using and xUnit package versions as `DTXMania.Automation.Tests`. Add exactly one production project reference from VideoRecorder to `DTXMania.Automation`.

Keep production types internal. Add:

```csharp
// DTXMania.VideoRecorder/Properties/AssemblyInfo.cs
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DTXMania.VideoRecorder.Tests")]
```

Validation:

```bash
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

Expected: projects compile before behavior tests are added.

- [ ] **Step 2: Write failing sandbox tests for config preservation, path semantics, and isolation.**

Cover all of these as independent assertions:

1. `SkinPath=Default` survives the copy unchanged; token detection is case-insensitive;
2. relative filesystem `DTXPath`, every `SongRoot.N`, non-Default `SkinPath`, and `SystemSkinRoot` are rewritten to absolute paths resolved against the source app-data root;
3. already-absolute values for those path keys remain unchanged;
4. `LastUsedSkin` is never rewritten;
5. copied config preserves ordinary lines such as scroll/play/pitch/volume;
6. the sandbox overrides only:
   - `EnableGameApi=True`;
   - generated `GameApiPort`;
   - generated non-empty `GameApiKey`;
   - `AutoPlay=True`;
   - `NoFail=True`;
   - `ScreenWidth=1280`;
   - `ScreenHeight=720`;
   - `FullScreen=False`;
7. no source `songs.db`, `songs.db-wal`, cache, or crash-report content is copied;
8. missing source `Config.ini` fails before CX launch;
9. successful cleanup deletes the run root;
10. failure path leaves the run root intact.

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~RecordingSandboxTests"
```

Expected before implementation: FAIL.

- [ ] **Step 3: Implement the minimal copy-and-patch sandbox.**

Create one path-normalization helper with this exact policy:

```csharp
private static string NormalizeCopiedPath(string key, string value, string sourceAppDataRoot)
{
    if (key.Equals("SkinPath", StringComparison.OrdinalIgnoreCase)
        && value.Trim().Equals("Default", StringComparison.OrdinalIgnoreCase))
    {
        return value;
    }

    if (Path.IsPathRooted(value))
        return value;

    return Path.GetFullPath(Path.Combine(sourceAppDataRoot, value));
}
```

Apply it only to `DTXPath`, `SongRoot.<n>`, `SkinPath`, and `SystemSkinRoot`. Never pass `LastUsedSkin` through it.

Additional implementation rules:

- create a unique `%TEMP%/DTXManiaCX-video/<run-id>/appdata`;
- read the source INI as text; do not reference game config types;
- treat keys section-agnostically, matching current `ConfigManager` behavior;
- replace an existing owned key in place; append a missing owned key once;
- choose a per-run loopback port using a tiny local helper and generate the API key with cryptographically random bytes;
- do not create a general INI library.

Run the focused sandbox tests until green.

- [ ] **Step 4: Implement the two-command parser and environment loader.**

`record` rejects:

- non-Windows execution;
- blank/non-absolute/missing chart;
- invalid/missing output directory after a create-directory attempt;
- missing source config;
- non-loopback OBS URL;
- missing/invalid OBS output directory.

`doctor` parses no recording chart/output options and performs no mutation yet; Task 2 wires its preflight behavior.

Do not add `System.CommandLine` or another CLI dependency.

- [ ] **Step 5: Verify Task 1 and commit.**

```bash
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~RecordingSandboxTests"
```

Commit scope:

```text
feat: add recorder sandbox foundation
```

**Task 1 deliverable:** a compilable `dtx-video` shell with deterministic environment/config validation and a tested disposable app-data sandbox that preserves `Default` skin semantics and cannot copy live score data.

---

### Task 2: Add the narrow OBS WebSocket client and prove it live through `doctor`

**Files:**
- Create: `DTXMania.VideoRecorder/Obs/IObsRecorder.cs`
- Create: `DTXMania.VideoRecorder/Obs/ObsWebSocketRecorder.cs`
- Create: `DTXMania.VideoRecorder.Tests/Obs/ObsWebSocketRecorderTests.cs`
- Modify: `DTXMania.VideoRecorder/Program.cs`
- Modify: `DTXMania.VideoRecorder/RecorderCommandLine.cs`

**Interface:**

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

Do not expose generic `SendRequestAsync` from the interface.

- [ ] **Step 1: Write failing pure/unit tests for the OBS contract.**

Test narrow protocol helpers/response mapping without opening a real socket:

- authenticated and unauthenticated handshake material is accepted/rejected correctly;
- `GetRecordStatus` maps active/inactive state;
- failed start/stop responses become actionable exceptions;
- stop response without a raw output path fails;
- raw output path outside configured `DTXMANIA_VIDEO_OBS_OUTPUT_DIR` fails;
- path equality/containment follows Windows path semantics;
- secrets never appear in exception formatting owned by the recorder.

Do not build an in-process fake obs-websocket server.

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~ObsWebSocketRecorderTests"
```

Expected before implementation: FAIL.

- [ ] **Step 2: Implement only the obs-websocket 5.x operations needed by `IObsRecorder`.**

Use built-in `ClientWebSocket` and `System.Text.Json`.

Keep these responsibilities private inside `ObsWebSocketRecorder`:

- Hello/Identify authentication handshake;
- monotonically unique request IDs;
- request/response correlation for one outstanding workflow request at a time;
- `GetRecordStatus`;
- `StartRecord`;
- `StopRecord` and returned raw path;
- configured raw-output-directory containment check.

Reject malformed/unsupported protocol messages clearly. Ignore only harmless events that do not affect the outstanding request. Do not add source, scene, volume, screenshot, reconnect, or arbitrary-request features.

- [ ] **Step 3: Implement `doctor` as a read-only preflight.**

Required output is a concise pass/warn/fail summary for:

- Windows platform;
- repository root and `GameProjectPaths.Windows` existence;
- source CX `Config.ini`;
- OBS URL and configured raw output directory;
- OBS connection/authentication;
- current OBS record status, warning/failing clearly if already recording;
- `ffprobe` availability on `PATH`.

Also print one informational note:

```text
record uses a fresh sandbox songs.db; the first run can take several minutes to rebuild the active song library before Song Select.
```

`doctor` must not create the sandbox, launch CX, start/stop OBS, inspect scenes/sources, or record media.

- [ ] **Step 4: Run automated OBS checks.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~ObsWebSocketRecorderTests"
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
```

Expected: unit tests and build pass without a live OBS requirement.

- [ ] **Step 5: Complete the Task-2 live OBS gate before debugging the full workflow.**

On Windows, open the dedicated OBS profile and run:

```text
dtx-video doctor
```

Require evidence that:

- WebSocket Hello/Identify authentication succeeds;
- `GetRecordStatus` succeeds;
- OBS is reported as not recording;
- the configured raw output directory is the intended dedicated profile directory;
- OBS recording state remains unchanged.

If the current implementation environment cannot access Windows + the dedicated OBS profile, record this gate as unverified rather than assuming the client works. Do not use `RecordWorkflow` as the first live protocol test.

- [ ] **Step 6: Commit Task 2.**

Commit scope:

```text
feat: add minimal OBS recorder client
```

**Task 2 deliverable:** the exact four-operation OBS seam plus a non-destructive `doctor`; the real obs-websocket handshake/status/output-directory path has been proven independently of `RecordWorkflow` before the full Windows proof.

---

### Task 3: Implement normal Title entry, bounded CX-to-OBS workflow, diagnostics, and artifact verification

**Files:**
- Create: `DTXMania.VideoRecorder/Workflow/IGameRecordingControl.cs`
- Create: `DTXMania.VideoRecorder/Workflow/AutomationGameRecordingControl.cs`
- Create: `DTXMania.VideoRecorder/Workflow/RecordingStep.cs`
- Create: `DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs`
- Create: `DTXMania.VideoRecorder/Diagnostics/RecorderDiagnostics.cs`
- Create: `DTXMania.VideoRecorder/Media/RecordingArtifactVerifier.cs`
- Create: `DTXMania.VideoRecorder.Tests/Workflow/RecordWorkflowTests.cs`
- Create: `DTXMania.VideoRecorder.Tests/Diagnostics/RecorderDiagnosticsTests.cs`
- Create: `DTXMania.VideoRecorder.Tests/Media/RecordingArtifactVerifierTests.cs`
- Modify: `DTXMania.VideoRecorder/Program.cs`

**Thin game-control seam:**

`IGameRecordingControl` exists only so workflow tests do not launch MonoGame. Its production implementation delegates to `GameProcessDriver` and `JsonRpcGameClient`; `RecordWorkflow` owns polling through `Eventually.UntilAsync` over `GetGameStateAsync`.

Use this shape or the smallest equivalent:

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

`AutomationGameRecordingControl` should create/use `JsonRpcGameClient` with:

```csharp
httpClient.Timeout = Timeout.InfiniteTimeSpan;
```

Do not copy E2E's five-second `HttpClient.Timeout`.

**Diagnostic steps:**

```text
Starting
PreparingChart
StartingRecording
Previewing
Playing
ShowingResult
StoppingRecording
Completed
```

**Recorder-owned timeout constants:**

```csharp
private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
private static readonly TimeSpan TitleTimeout = TimeSpan.FromMinutes(5);
private static readonly TimeSpan SongSelectTimeout = TimeSpan.FromMinutes(2);
private static readonly TimeSpan PrepareChartTimeout = TimeSpan.FromMinutes(5);
private static readonly TimeSpan PreviewTimeout = TimeSpan.FromMinutes(2);
private static readonly TimeSpan SongTransitionTimeout = TimeSpan.FromSeconds(30);
private static readonly TimeSpan PerformanceReadyTimeout = TimeSpan.FromMinutes(2);
private static readonly TimeSpan PerformanceCompletionTimeout = TimeSpan.FromMinutes(20);
private static readonly TimeSpan ResultTimeout = TimeSpan.FromMinutes(1);
private static readonly TimeSpan ObsOperationTimeout = TimeSpan.FromSeconds(15);
private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(15);
```

Do not turn these into CLI or environment settings in HPA-503.

- [ ] **Step 1: Write failing workflow-order and ownership tests, including normal Title entry.**

Use a fake `IGameRecordingControl`, fake `IObsRecorder`, and controlled delay/time seam. Assert exact happy-path order:

```text
start CX
-> WaitForStartupAsync
-> poll Title
-> SendKeyAsync("Enter", 50ms)
-> poll SongSelect
-> PrepareVideoChartAsync
-> screenshot barrier
-> OBS status
-> OBS start
-> StartPreparedPreviewAsync
-> wait PreparedPreviewState=Playing and elapsed >= 10s
-> ActivatePreparedChartAsync
-> SongTransition
-> Performance ready/AutoPlay/non-zero notes
-> Result complete
-> Result screenshot barrier
-> 5s hold
-> OBS stop
-> verify/publish
-> cleanup
```

Also assert:

- no `ChangeStageAsync` equivalent exists on `IGameRecordingControl`;
- OBS already recording: fail before starting OBS and never call Stop;
- OBS start fails: never claim ownership and never call Stop;
- after successful OBS start, every later failure attempts exactly one bounded Stop;
- cancellation during preview, Performance, and Result hold uses the same cleanup path;
- cleanup can be called twice without duplicate stop/process ownership actions;
- unexpected stage order fails rather than skipping forward;
- a timeout is classified by the named workflow gate, not by `HttpClient.Timeout`.

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~RecordWorkflowTests"
```

Expected before implementation: FAIL.

- [ ] **Step 2: Implement `AutomationGameRecordingControl` as a delegation adapter.**

Reuse:

- `GameProjectPaths.Windows`;
- `GameProcessDriver` with sandbox `AppDataRoot`, per-run `LaunchToken`, and repository working directory;
- `JsonRpcGameClient.GetHealthAsync` + `GameProcessDriver.WaitForStartupAsync`;
- `JsonRpcGameClient.GetGameStateAsync`;
- `JsonRpcGameClient.SendKeyAsync`;
- HPA-510 `PrepareVideoChartAsync`, `StartPreparedPreviewAsync`, `ActivatePreparedChartAsync`, and `TakeScreenshotBase64Async`.

Copy the small repository-root/ephemeral-port policy locally when needed; HPA-501 intentionally left E2E policy out of Automation.

Do not add `WaitForStageAsync` to Automation and do not reimplement transport/process readiness.

- [ ] **Step 3: Implement normal Title -> Song Select setup before OBS starts.**

After `WaitForStartupAsync`:

```csharp
await Eventually.UntilAsync(
    game.GetGameStateAsync,
    state => state.StageType == "Title",
    TitleTimeout,
    pollInterval,
    "Title stage",
    cancellationToken);

await game.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellationToken);

await Eventually.UntilAsync(
    game.GetGameStateAsync,
    state => state.StageType == "SongSelect",
    SongSelectTimeout,
    pollInterval,
    "SongSelect stage",
    cancellationToken);
```

Only after Song Select is reached should the workflow call `PrepareVideoChartAsync`. OBS must still be disconnected/not recording during this setup, so Title never appears in the captured video.

- [ ] **Step 4: Implement the happy-path `RecordWorkflow` with recorder-owned bounded gates.**

For direct RPC operations that can legitimately be slow, especially prepare, derive a linked cancellation token from the applicable named timeout instead of relying on `HttpClient.Timeout`.

Required assertions:

**Prepared preview gate**

```text
StageType == SongSelect
PreparedPreviewState == Playing
PreparedPreviewElapsedMs >= 10_000
```

**Performance entry gate**

```text
StageType == Performance
PerformanceReady == true
AutoPlayEnabled == true
TotalNotes > 0
```

**Result completion gate**

```text
StageType == Result
StageCompleted == true
ClearFlag == true
CompletionReason == SongComplete
TotalNotes > 0
TotalJudgements == TotalNotes
```

Observe `SongTransition -> Performance -> Result` in order. Do not skip stages when polling.

After the Result screenshot succeeds, delay exactly five seconds with no game input.

For delay testing, use `TimeProvider` only if a fake is already available without a new package. Otherwise add one tiny internal delay delegate/interface local to `RecordWorkflow`; do not add `TimeProvider.Testing`.

- [ ] **Step 5: Write and implement diagnostics/redaction tests.**

Diagnostics location:

```text
<requested-output>/diagnostics/<run-id>/
```

At minimum write:

```text
summary.json
steps.json
telemetry.json
cx-stdout.log
cx-stderr.log
```

The JSON shape can remain private. Tests must prove neither the per-run `GameApiKey` nor `DTXMANIA_VIDEO_OBS_PASSWORD` value appears in any diagnostic file or recorder-owned exception message.

Do not copy the sandbox `Config.ini` into diagnostics. Record the retained sandbox path only on failure/cancellation.

- [ ] **Step 6: Write and implement artifact verification tests.**

Validate:

1. raw path is absolute and inside configured OBS output directory;
2. raw file exists and length > 0;
3. optional `ffprobe` result contains at least one video and one audio stream when the executable is available;
4. missing `ffprobe` is a warning, not failure;
5. destination collision fails without overwrite;
6. publish uses copy semantics and the raw file still exists afterward.

Keep the `ffprobe` process call local to `RecordingArtifactVerifier`; do not build a generic subprocess layer.

- [ ] **Step 7: Implement unified cleanup and Ctrl+C cancellation.**

`Program` creates one cancellation source and maps Ctrl+C to cancellation without immediate process termination.

The workflow `finally` path:

```text
if OBS ownership was acquired and not yet released:
    StopRecordAsync with CleanupTimeout
always dispose the owned CX control/process
write final diagnostics
if success:
    delete sandbox
else:
    preserve sandbox
```

Cleanup failures are appended to diagnostics and must not replace the original exception/cancellation as the primary failure.

- [ ] **Step 8: Verify Task 3 and commit.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
```

Expected: all recorder and Automation tests pass with no live OBS/game requirement.

Commit scope:

```text
feat: add Windows recording workflow
```

**Task 3 deliverable:** a unit-tested orchestration path that enters Song Select normally, tolerates real cold-library/preview latency through explicit minute-scale bounds, and owns exactly one CX process plus at most one recorder-started OBS recording.

---

### Task 4: Add portable CI coverage and perform the first full Windows recording proof

**Files:**
- Modify: `.github/workflows/build-and-test.yml`
- Create only if durable proof evidence is useful: `docs/verification/hpa-503-windows-recorder-proof.md`

Do not add live OBS automation to GitHub Actions.

- [ ] **Step 1: Add recorder tests to both normal OS CI jobs.**

After the existing Automation test step in both Windows and macOS jobs, run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug --verbosity normal --logger trx --results-directory ./TestResults/VideoRecorder
```

The test project is plain `net8.0`; live Windows behavior is guarded by the CLI and not exercised in portable unit tests.

- [ ] **Step 2: Run the complete local verification set.**

Windows authoritative commands:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

Do not require a live OBS server for any automated test command above.

- [ ] **Step 3: Produce the first real recording using the OBS profile already proven by Task 2.**

Choose one short chart that:

- is already indexed under an active song root;
- has a valid preview;
- has a duration comfortably below the fixed 20-minute `PerformanceCompletionTimeout`.

Run:

```text
dtx-video record --chart <absolute-dtx-path> --output <proof-output-directory>
```

The first sandbox run may spend several minutes rebuilding its fresh `songs.db`; do not classify that as a recorder failure unless the named startup/Title/SongSelect bounds are actually exceeded.

Retain evidence that the run completed:

- published MP4;
- raw OBS MP4 still present;
- diagnostics directory;
- successful step timeline ending in `Completed`;
- telemetry proving Title -> normal GAME START -> SongSelect setup, at least 10 seconds of prepared preview, normal SongTransition, AutoPlay Performance, complete judgements, and Result completion.

Manually confirm only that the MP4 opens and is plausibly the intended recording. Do not turn this into HPA-513's formal visual/audio acceptance pass.

- [ ] **Step 4: Confirm live user data isolation.**

Before/after the proof run, verify the source app-data location and source `songs.db` were not modified by the recorder-owned CX process. The sandbox may have its own newly built `songs.db`; that is expected.

Also confirm a source config using `SkinPath=Default` still produced the bundled default skin behavior rather than redirecting to a writable app-data `System` path.

Document the evidence path in the PR description or `docs/verification/hpa-503-windows-recorder-proof.md`. Do not commit binary MP4 evidence to git.

- [ ] **Step 5: Run final self-review against HPA-503 boundaries.**

Reject/remove any implementation that introduced:

- source/scene enumeration;
- automatic OBS scene setup;
- MKV/remux/re-encode paths;
- general media metadata models;
- platform adapter hierarchy;
- batch recording queue;
- a recorder-wide state-machine framework;
- `ChangeStageAsync` navigation;
- new generic stage-wait APIs in Automation;
- configurable timeout frameworks;
- game/E2E project references from the recorder;
- copied live database/caches.

- [ ] **Step 6: Commit and prepare the implementation PR.**

Commit scope:

```text
test: validate recorder vertical slice
```

Implementation PR description must link HPA-503 and include:

- automated test results;
- Task-2 Windows `doctor` live-handshake result;
- full proof-run artifact/diagnostic location without secrets;
- confirmation that raw and published MP4 both exist;
- confirmation that live app data/database were untouched;
- confirmation that Title was traversed through normal GAME START before recording began;
- explicit statement that HPA-513 remains the next formal acceptance/documentation task.

**Task 4 deliverable:** portable CI coverage plus one real Windows recording proving the complete HPA-503 vertical slice.

---

## Agent handoff boundaries

A junior implementation agent should execute the four tasks in order. Each task is reviewable independently, but the final implementation should remain one HPA-503 PR unless the OBS protocol implementation alone demonstrably pushes the work beyond the 2–3 engineer-day budget.

Do not redesign HPA-501/HPA-510 seams while implementing this ticket. If a prerequisite contract is missing, make the smallest additive change to `DTXMania.Automation` necessary for the recorder and call it out explicitly in review rather than creating a parallel abstraction.

In particular:

- copy the small recorder-specific repository-root/ephemeral-port policy locally rather than moving E2E policy into Automation;
- call the HPA-510 methods already on `JsonRpcGameClient`;
- read `PreparedPreviewState` and `PreparedPreviewElapsedMs` from `GameStateSnapshot`;
- poll with `Eventually.UntilAsync`;
- do not add `WaitForStageAsync` to Automation.

## Definition of done

The implementation is ready for HPA-503 review only when:

- `dtx-video doctor` is non-destructive and its real Windows OBS handshake/status/output-directory path was proven before the full workflow proof;
- `dtx-video record` launches CX, waits for Title, sends one 50 ms Enter to normal GAME START, then waits for Song Select before preparation;
- `SkinPath=Default` remains the logical default-skin token in the sandbox;
- relative filesystem config paths are resolved against the source app-data root, while absolute paths and `LastUsedSkin` remain unchanged;
- `HttpClient.Timeout` cannot abort a slow prepare/cold import; recorder-owned named timeouts bound each workflow gate instead;
- preview timing is based on `PreparedPreviewState == Playing` and `PreparedPreviewElapsedMs >= 10_000`, not caller wall-clock sleep;
- activation observes normal `SongTransition -> Performance -> Result` order;
- Performance proves AutoPlay, readiness, and non-zero notes;
- Result proves complete judgements and successful completion, then remains rendered for five seconds;
- the run owns/stops only its OBS recording and owns/terminates only its CX process;
- live CX app data and score DB remain unchanged;
- raw and published non-empty MP4 files exist;
- `ffprobe` proves video+audio streams when available;
- diagnostics are useful and secret-free, and never include the sandbox Config.ini;
- recorder tests pass on Windows and macOS CI;
- one full Windows proof run is retained for HPA-513 to inspect next.
