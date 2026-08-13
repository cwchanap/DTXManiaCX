# HPA-503 Windows Recorder Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a small external `dtx-video` tool that records one exact indexed chart through Song Select -> controlled preview -> normal AutoPlay gameplay -> Result using the existing CX Automation API and a manually prepared Windows OBS profile.

**Architecture:** Build one plain `net8.0` recorder executable plus one plain test project. Keep `RecordWorkflow` as the only orchestrator, reuse `DTXMania.Automation` for CX process/API control, use one four-operation `IObsRecorder` seam for OBS, and isolate the user's live data by copying/patching only `Config.ini` into per-run temporary app data.

**Tech Stack:** .NET 8, `DTXMania.Automation`, built-in `ClientWebSocket`, `System.Text.Json`, xUnit, optional external `ffprobe`.

## Global Constraints

- Live recording support in HPA-503 is Windows x64 only.
- `DTXMania.VideoRecorder` must target plain `net8.0` and reference `DTXMania.Automation` only; no `DTXMania.Game`, `DTXMania.E2E`, or MCP reference.
- Do not introduce DI, a workflow engine, a recorder session state machine, a platform-adapter framework, or generic process/OBS/media abstractions.
- Preserve the user's song roots, selected skin, scroll speed, play speed, pitch, volumes, and other presentation preferences by copying the live `Config.ini`.
- Never copy the live `songs.db`, WAL files, score/history data, or caches.
- Recording-owned config overrides are limited to Game API enable/port/key, AutoPlay, NoFail, 1280x720, and windowed mode.
- OBS settings/secrets come from environment configuration, never CLI arguments or diagnostics.
- Stop OBS only when this run successfully started the recording.
- Always terminate the CX process launched by this run.
- Preserve raw OBS output; publishing is a copy, not a move/delete.
- `ffprobe` validation is conditional on `ffprobe` being available on `PATH`.
- HPA-513 owns formal visual/audio acceptance and OBS setup documentation; HPA-503 needs only the first proof recording.

---

## File structure to add/change

```text
DTXMania.VideoRecorder/
  DTXMania.VideoRecorder.csproj
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

### Task 1: Add recorder projects, environment contract, and disposable config sandbox

**Files:**
- Create: `DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj`
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

- [ ] **Step 1: Scaffold the two plain `net8.0` projects and solution entries.**

Use the same nullable/implicit-using and xUnit package versions as `DTXMania.Automation.Tests`. Add exactly one production project reference from VideoRecorder to `DTXMania.Automation`.

Validation:

```bash
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
```

Expected: projects build before behavior tests are added.

- [ ] **Step 2: Write failing sandbox tests for configuration preservation and isolation.**

Cover all of these as independent assertions:

1. copied config preserves ordinary lines such as scroll/play/pitch/volume/`LastUsedSkin`;
2. relative `DTXPath`, every `SongRoot.N`, `SkinPath`, and `SystemSkinRoot` are rewritten to absolute paths resolved against the source app-data root;
3. already-absolute path values remain semantically unchanged;
4. the sandbox overrides only:
   - `EnableGameApi=True`;
   - generated `GameApiPort`;
   - generated non-empty `GameApiKey`;
   - `AutoPlay=True`;
   - `NoFail=True`;
   - `ScreenWidth=1280`;
   - `ScreenHeight=720`;
   - `FullScreen=False`;
5. no source `songs.db`, `songs.db-wal`, cache, or crash-report content is copied;
6. missing source `Config.ini` fails before CX launch;
7. successful cleanup deletes the run root;
8. failure path leaves the run root intact.

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~RecordingSandboxTests"
```

Expected before implementation: FAIL.

- [ ] **Step 3: Implement the minimal copy-and-patch sandbox.**

Implementation rules:

- create a unique `%TEMP%/DTXManiaCX-video/<run-id>/appdata`;
- read the source INI as text; do not reference game config types;
- treat keys section-agnostically, matching current `ConfigManager` behavior;
- canonicalize only the four path-key families listed above;
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

**Task 1 deliverable:** a compilable `dtx-video` shell with deterministic environment/config validation and a tested disposable app-data sandbox that cannot copy live score data.

---

### Task 2: Add the narrow OBS WebSocket client and read-only `doctor`

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

Reject events/unknown messages unless they are harmless protocol noise. Do not add source, scene, volume, screenshot, reconnect, or arbitrary-request features.

- [ ] **Step 3: Implement `doctor` as a read-only preflight.**

Required output is a concise pass/warn/fail summary for:

- Windows platform;
- repository root and `GameProjectPaths.Windows` existence;
- source CX `Config.ini`;
- OBS URL and configured raw output directory;
- OBS connection/authentication;
- current OBS record status, warning/failing clearly if already recording;
- `ffprobe` availability on `PATH`.

`doctor` must not create the sandbox, launch CX, start/stop OBS, inspect scenes/sources, or record media.

- [ ] **Step 4: Verify Task 2 and commit.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~ObsWebSocketRecorderTests"
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
```

Manual non-destructive check on Windows with the dedicated OBS profile open:

```text
dtx-video doctor
```

Expected: connection/status preflight only; OBS recording state unchanged.

Commit scope:

```text
feat: add minimal OBS recorder client
```

**Task 2 deliverable:** the exact four-operation OBS seam, with meaningful raw-output ownership checks and a non-destructive dependency preflight.

---

### Task 3: Implement the imperative CX-to-OBS recording workflow, diagnostics, and artifact verification

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

`IGameRecordingControl` exists only so workflow tests do not launch MonoGame. Its production implementation delegates to `GameProcessDriver`, `JsonRpcGameClient`, `GameStateSnapshot`, and Automation polling. It must not reimplement transport, process readiness, or game state.

It needs only operations equivalent to:

```text
Start owned Windows CX process with sandbox launch options
Wait for startup
Wait for required stage/state with bounded timeout
PrepareVideoChartAsync
TakeScreenshotBase64Async
StartPreparedPreviewAsync
ActivatePreparedChartAsync
Read GameStateSnapshot
Expose captured stdout/stderr for diagnostics
Dispose owned process
```

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

- [ ] **Step 1: Write failing workflow-order and ownership tests.**

Use a fake `IGameRecordingControl`, fake `IObsRecorder`, and controlled delay/time seam. Assert exact call order for the happy path:

```text
start CX
-> wait startup/SongSelect
-> prepare chart
-> screenshot barrier
-> OBS status
-> OBS start
-> preview start
-> wait preview >= 10s
-> activate chart
-> SongTransition
-> Performance
-> Result
-> Result screenshot barrier
-> 5s hold
-> OBS stop
-> verify/publish
-> cleanup
```

Also cover:

- OBS already recording: fail before starting OBS and never call Stop;
- OBS start fails: never claim ownership and never call Stop;
- after successful OBS start, every later failure attempts exactly one bounded Stop;
- cancellation during preview, Performance, and Result hold uses the same cleanup path;
- cleanup can be called twice without duplicate stop/process ownership actions;
- unexpected stage order fails rather than skipping forward.

Run:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --filter "FullyQualifiedName~RecordWorkflowTests"
```

Expected before implementation: FAIL.

- [ ] **Step 2: Implement `AutomationGameRecordingControl` as a delegation adapter.**

Use:

- `GameProjectPaths.Windows`;
- `GameProcessDriver` with sandbox `AppDataRoot`, per-run `LaunchToken`, and repository working directory;
- `JsonRpcGameClient.GetHealthAsync` + `WaitForStartupAsync`;
- `GetGameStateAsync`, prepared-chart methods, and screenshot method already added by HPA-510;
- Automation's bounded polling helper rather than a new polling framework.

Stage names used by the workflow are exactly:

```text
SongSelect
SongTransition
Performance
Result
```

Do not synthesize keyboard navigation or call `ChangeStageAsync`.

- [ ] **Step 3: Implement the happy-path `RecordWorkflow` with bounded gates.**

Freeze timeout values as named internal constants/options, not scattered literals. Use practical MVP bounds consistent with current E2E behavior; they may be generous but must be finite.

Required assertions:

**Prepared preview gate**

```text
PreparedPreviewState remains Playing
PreparedPreviewElapsedMs >= 10_000
```

**Performance gate**

```text
StageType == Performance
PerformanceReady == true
AutoPlayEnabled == true
TotalNotes > 0
```

**Result gate**

```text
StageType == Result
StageCompleted == true
ClearFlag == true
CompletionReason == SongComplete
TotalNotes > 0
TotalJudgements == TotalNotes
```

After the Result screenshot succeeds, delay exactly five seconds with no game input.

- [ ] **Step 4: Write and implement diagnostics/redaction tests.**

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

Record the retained sandbox path only on failure/cancellation.

- [ ] **Step 5: Write and implement artifact verification tests.**

Validate:

1. raw path is absolute and inside configured OBS output directory;
2. raw file exists and length > 0;
3. optional `ffprobe` result contains at least one video and one audio stream when the executable is available;
4. missing `ffprobe` is a warning, not failure;
5. destination collision fails without overwrite;
6. publish uses copy semantics and the raw file still exists afterward.

Keep the `ffprobe` process call local to `RecordingArtifactVerifier`; do not build a generic subprocess layer.

- [ ] **Step 6: Implement unified cleanup and Ctrl+C cancellation.**

`Program` creates one cancellation source and maps Ctrl+C to cancellation without immediate process termination.

The workflow `finally` path:

```text
if OBS ownership was acquired and not yet released:
    bounded StopRecordAsync
always dispose the owned CX control/process
write final diagnostics
if success:
    delete sandbox
else:
    preserve sandbox
```

Cleanup failures are appended to diagnostics and must not replace the original exception/cancellation as the primary failure.

- [ ] **Step 7: Verify Task 3 and commit.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj
```

Expected: all recorder and Automation tests green with no live OBS/game requirement.

Commit scope:

```text
feat: add Windows recording workflow
```

**Task 3 deliverable:** a fully unit-tested orchestration path that owns exactly one CX process and, only after successful start, one OBS recording.

---

### Task 4: Add portable CI coverage and perform the first Windows proof recording

**Files:**
- Modify: `.github/workflows/build-and-test.yml`
- Modify only if proof evidence needs a short note: `docs/verification/hpa-503-windows-recorder-proof.md`

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

- [ ] **Step 3: Run `doctor` against the manually prepared Windows OBS profile.**

Confirm:

- WebSocket authentication succeeds;
- OBS reports not currently recording;
- configured raw output directory matches the profile;
- `ffprobe` availability is reported accurately;
- no OBS state is modified.

Fix only blockers required for the HPA-503 narrow contract.

- [ ] **Step 4: Produce the first real recording.**

Choose one short chart that is already indexed under an active song root and has a valid preview. Run:

```text
dtx-video record --chart <absolute-dtx-path> --output <proof-output-directory>
```

Retain evidence that the run completed:

- published MP4;
- raw OBS MP4 still present;
- diagnostics directory;
- successful step timeline ending in `Completed`;
- telemetry proving 10 seconds of prepared preview, normal SongTransition, AutoPlay Performance, complete judgements, and Result completion.

Manually confirm only that the MP4 opens and is plausibly the intended recording. Do not turn this into HPA-513's formal visual/audio acceptance pass.

- [ ] **Step 5: Confirm live user data isolation.**

Before/after the proof run, verify the source app-data location and source `songs.db` were not modified by the recorder-owned CX process. The sandbox may have its own newly built `songs.db`; that is expected.

Document the evidence path in the PR description or a short `docs/verification/hpa-503-windows-recorder-proof.md` if a durable repository note is useful. Do not commit binary MP4 evidence to git.

- [ ] **Step 6: Run final self-review against HPA-503 boundaries.**

Reject/remove any implementation that introduced:

- source/scene enumeration;
- automatic OBS scene setup;
- MKV/remux/re-encode paths;
- general media metadata models;
- platform adapter hierarchy;
- batch recording queue;
- a recorder-wide state-machine framework;
- game/E2E project references from the recorder;
- copied live database/caches.

- [ ] **Step 7: Commit and prepare the implementation PR.**

Commit scope:

```text
test: validate recorder vertical slice
```

Implementation PR description must link HPA-503 and include:

- automated test results;
- Windows `doctor` result;
- proof-run artifact/diagnostic location without secrets;
- confirmation that raw and published MP4 both exist;
- confirmation that live app data/database were untouched;
- explicit statement that HPA-513 remains the next formal acceptance/documentation task.

**Task 4 deliverable:** portable CI coverage plus one real Windows recording proving the complete HPA-503 vertical slice.

---

## Agent handoff boundaries

A junior implementation agent should execute the four tasks in order. Each task is reviewable independently, but the final implementation should remain one HPA-503 PR unless the OBS protocol implementation alone demonstrably pushes the work beyond the 2–3 engineer-day budget.

Do not redesign HPA-501/HPA-510 seams while implementing this ticket. If a prerequisite contract is missing, make the smallest additive change to `DTXMania.Automation` necessary for the recorder and call it out explicitly in review rather than creating a parallel abstraction.

## Definition of done

The implementation is ready for HPA-503 review only when:

- `dtx-video doctor` is non-destructive and diagnoses the narrow Windows/OBS prerequisites;
- `dtx-video record` records the exact required CX journey for one indexed chart;
- preview timing is based on `PreparedPreviewElapsedMs >= 10_000`, not caller wall-clock sleep;
- activation observes normal `SongTransition -> Performance -> Result` order;
- Performance proves AutoPlay, readiness, and non-zero notes;
- Result proves complete judgements and successful completion, then remains rendered for five seconds;
- the run owns/stops only its OBS recording and owns/terminates only its CX process;
- live CX app data and score DB remain unchanged;
- raw and published non-empty MP4 files exist;
- `ffprobe` proves video+audio streams when available;
- diagnostics are useful and secret-free;
- recorder tests pass on Windows and macOS CI;
- one Windows proof run is retained for HPA-513 to inspect next.
