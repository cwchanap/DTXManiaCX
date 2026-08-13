# HPA-503 Windows Recorder Vertical Slice Design

**Issue:** [HPA-503](https://linear.app/cwchanap/issue/HPA-503/build-the-windows-recorder-vertical-slice-and-produce-the-first-video)  
**Date:** 2026-08-12  
**Status:** Revised after second planning review

## Context

HPA-501 and HPA-510 are complete, so HPA-503 is the next unblocked Windows-first slice of the HPA-500 recording epic.

The goal is one local command that records one exact indexed chart through the normal CX journey:

```text
Title -> GAME START -> Song Select
-> 10 seconds of prepared preview
-> Song Transition
-> full AutoPlay Performance
-> rendered Result
-> 5-second Result hold
```

Only Song Select onward is captured. Title is traversed before OBS starts.

HPA-503 already absorbed the former sandbox and shared-OBS subtasks. Keep the implementation to one small external executable rather than rebuilding those abstractions as frameworks.

## Goals

- Add one plain `net8.0` `DTXMania.VideoRecorder` executable plus tests.
- Reference `DTXMania.Automation` as the only CX project dependency.
- Preserve the user's presentation/song configuration without touching the user's live database.
- Launch and own exactly one CX process.
- Start/stop only the OBS recording owned by this run.
- Drive the already-proven HPA-510 prepared-chart path through normal Title/Song Select behavior.
- Publish one non-empty Hybrid MP4 plus compact sanitized diagnostics.
- Produce one real Windows proof recording before broader hardening.

## Non-goals

- Apple Silicon live capture.
- Batch recording or recorder-cache optimization.
- A persistent recorder database/app-data lifecycle.
- DI, a workflow engine, recorder session state machine, platform-adapter hierarchy, or generic process/media framework.
- Generic OBS APIs, source/scene enumeration, source screenshots, volume meters, automatic scene creation, or capture-quality acceptance.
- Copying the live `songs.db`, WAL files, caches, scores, or history.
- MKV fallback, remux, re-encoding, codec/FPS/duration policy, editing, overlays, upload, or queueing.
- Formal OBS visual/audio acceptance or reproducible setup documentation; HPA-513 owns that pass.

## Review decisions

The second planning review found several useful corrections. The adopted decisions are:

1. **Wait for the Song Select library, not merely the stage.** `SongSelectionStage` becomes active while its library initializer is still running. `PrepareVideoChart` cannot resolve anything while `_appliedLibrarySnapshot` is null. Reuse the existing prepared-chart E2E readiness pattern before the one-shot prepare call.
2. **Require a CX-normalized source config.** Game-written config paths are normalized before `SaveConfig`. Preserve `SkinPath=Default`; accept already-absolute filesystem paths; reject relative/legacy hand edits rather than partially reimplementing `AppPaths.ResolvePath`.
3. **Make OBS protocol logic testable.** Split pure obs-websocket message/auth/response mapping into `ObsProtocol`; keep socket lifetime/request correlation in `ObsWebSocketRecorder` and prove that live path with `doctor` before implementing the full workflow.
4. **Keep one owner for raw-path trust.** `IObsRecorder.StopRecordAsync` returns OBS's raw path. `RecordingArtifactVerifier` alone validates containment/existence/media before publishing.
5. **Collapse operational knobs.** Use four recorder-owned timeout classes and one `run.json` plus CX stdout/stderr instead of many near-identical constants/artifacts.
6. **State the manual OBS prerequisite explicitly.** `doctor` reminds the operator what must already be configured without attempting to inspect or repair scene/source setup.

Three suggestions are intentionally deferred/rejected for this slice:

- **Persistent recorder app data:** useful for future bulk recording, but it conflicts with HPA-503's unique disposable-run contract and would retain recorder-generated AutoPlay score/history state between videos. Optimize after the first proof if cold enumeration is a demonstrated workflow problem.
- **Post-start OBS scene screenshot:** this requires extra OBS scene/source APIs and image-quality heuristics. That is capture acceptance, which remains HPA-513 scope.
- **FFMpegCore/MMTools in the recorder:** those dependencies currently belong to the game project. Pulling them into the external recorder would add runtime/package surface to replace a tiny optional `ffprobe` invocation even though HPA-503 explicitly makes probing conditional on availability.

## Architecture

### 1. Project boundary

Create:

```text
DTXMania.VideoRecorder/
DTXMania.VideoRecorder.Tests/
```

`DTXMania.VideoRecorder` targets plain `net8.0` and references only:

```text
DTXMania.Automation
```

No `DTXMania.Game`, `DTXMania.E2E`, or MCP project reference.

Production implementation types remain internal. Expose internals only to the sibling test project:

```csharp
[assembly: InternalsVisibleTo("DTXMania.VideoRecorder.Tests")]
```

`Program` constructs the small set of concrete collaborators directly. No DI container.

Commands:

```text
dtx-video doctor
dtx-video record --chart <absolute-dtx-path> --output <directory>
```

A hand-written parser is sufficient.

### 2. Environment contract

Machine-local OBS configuration stays outside CLI arguments:

```text
DTXMANIA_VIDEO_OBS_URL
DTXMANIA_VIDEO_OBS_PASSWORD
DTXMANIA_VIDEO_OBS_OUTPUT_DIR
```

Rules:

- URL defaults to `ws://127.0.0.1:4455` and must be loopback.
- Password may be blank only when OBS WebSocket authentication is disabled.
- OBS output directory is required for `record` and is the trusted raw-output scope.
- `--output` is a separate published destination.
- Never write the OBS password or generated CX API key to diagnostics.

### 3. Disposable app-data sandbox

Each `record` call creates a unique temporary root:

```text
%TEMP%/DTXManiaCX-video/<run-id>/appdata
```

Source app data comes from `DTXMANIA_APPDATA_ROOT` when set; otherwise use the normal Windows local-app-data CX root.

Require a source `Config.ini`, copy only that file, and patch the copy.

#### Source-config contract

HPA-503 consumes a current CX-written config rather than becoming another compatibility parser.

Accept:

- `SkinPath=Default` in any case; preserve the token unchanged.
- fully-qualified `DTXPath`.
- at least one fully-qualified `SongRoot.<n>`.
- fully-qualified `SystemSkinRoot`.
- a custom `SkinPath` only when it is fully qualified.
- `LastUsedSkin` verbatim.

Reject relative/legacy filesystem values or missing required normalized path keys with an actionable error such as:

```text
Source Config.ini is not normalized. Open CX once and exit normally, then retry dtx-video.
```

Do not duplicate `~`, macOS `Library/...`, legacy `Songs`, or other `AppPaths.ResolvePath` compatibility behavior in the recorder.

Patch only recorder-owned values:

```text
EnableGameApi=True
GameApiPort=<per-run loopback port>
GameApiKey=<per-run random secret>
AutoPlay=True
NoFail=True
ScreenWidth=1280
ScreenHeight=720
FullScreen=False
```

Preserve all unrelated config lines and visible preferences.

Never copy `songs.db`, WAL files, caches, crash reports, or score/history data. The sandbox intentionally builds its own database for this first vertical slice.

Cleanup:

- success: delete the run root after publication/diagnostics are safely written;
- failure/cancellation: retain it and record its path;
- cleanup is idempotent.

### 4. CX process and library readiness

Use `GameProcessDriver` as the only CX process owner and `JsonRpcGameClient` for the existing API.

Keep the small repo-root/ephemeral-port policy local to the recorder; HPA-501 intentionally left that policy outside Automation.

Construct the recorder `HttpClient` with:

```csharp
Timeout = Timeout.InfiniteTimeSpan
```

Workflow cancellation/timeouts, not `HttpClient.Timeout`, own the bound.

Startup prefix:

```text
GameProcessDriver.Start
-> WaitForStartupAsync
-> poll StageType == Title
-> SendKeyAsync("Enter", 50ms)
-> poll StageType == SongSelect AND SelectedSongTitle is non-empty
-> PrepareVideoChartAsync once
```

The non-empty selected song is the existing live-smoke proxy that the applied library has been projected. It avoids retrying an RPC whose "chart not available" error is also the correct permanent error for an unindexed chart.

Do not add library-ready telemetry or a new Automation stage-wait API for this ticket.

### 5. Narrow OBS client

Keep the recorder-facing seam at exactly four operations:

```csharp
internal interface IObsRecorder : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken cancellationToken);
    Task StartRecordAsync(CancellationToken cancellationToken);
    Task<string> StopRecordAsync(CancellationToken cancellationToken);
}
```

Split implementation responsibilities:

```text
ObsProtocol
- compute auth response
- build/parse the small v5 messages used by the recorder
- map GetRecordStatus / StartRecord / StopRecord responses
- require a stop output path

ObsWebSocketRecorder
- ClientWebSocket lifetime
- Hello -> Identify handshake
- request IDs
- one-outstanding-request correlation
- the four IObsRecorder operations
```

`ObsProtocol` is pure/internal and directly unit tested. Do not build a fake WebSocket server.

The live socket/correlation path is proven by running `dtx-video doctor` against the dedicated Windows OBS profile before `RecordWorkflow` is considered ready for full proof.

Ownership:

- fail if OBS is already recording;
- acquire local ownership only after StartRecord succeeds and active status is confirmed;
- stop only when this run owns the recording;
- `StopRecordAsync` returns OBS's raw path without deciding whether that path is trusted.

### 6. RecordWorkflow

`RecordWorkflow` is the sole orchestrator. A small `RecordingStep` enum is diagnostic metadata only.

Sequence:

```text
validate inputs/environment/source config
-> create disposable sandbox
-> launch owned CX
-> startup -> Title -> Enter -> populated SongSelect
-> PrepareVideoChartAsync
-> CX screenshot barrier
-> OBS connect/status; reject pre-existing recording
-> OBS start; acquire ownership
-> StartPreparedPreviewAsync
-> wait PreparedPreviewState == Playing
   and PreparedPreviewElapsedMs >= 10_000
-> ActivatePreparedChartAsync
-> observe SongTransition
-> PerformanceReady + AutoPlayEnabled + TotalNotes > 0
-> wait Result with complete successful telemetry
-> Result CX screenshot barrier
-> 5-second no-input hold
-> stop owned OBS recording
-> verify and copy-publish raw artifact
-> final diagnostics and cleanup
```

Result requires:

```text
StageType == Result
StageCompleted == true
ClearFlag == true
CompletionReason == SongComplete
TotalNotes > 0
TotalJudgements == TotalNotes
```

Do not use `ChangeStageAsync`.

### 7. Timeout policy

Keep only four meaningful recorder-owned bounds:

```text
SetupTimeout = 5 minutes
StageTimeout = 2 minutes
PerformanceTimeout = 20 minutes
ExternalIoTimeout = 15 seconds
```

Use them as follows:

- `SetupTimeout`: startup, Title, populated Song Select, prepare.
- `StageTimeout`: prepared-preview gate, SongTransition, Performance-ready, Result post-completion gates.
- `PerformanceTimeout`: Performance -> completed Result journey; proof chart must be comfortably shorter.
- `ExternalIoTimeout`: OBS operations, `ffprobe`, and bounded cleanup.

The exact product waits remain:

- CX-reported prepared preview elapsed >= 10 seconds;
- Result wall-clock hold of exactly 5 seconds.

Use a tiny internal delay seam for the 5-second hold if needed by tests; do not add a clock/testing package solely for this task.

### 8. Diagnostics

Write only:

```text
<output>/diagnostics/<run-id>/run.json
<output>/diagnostics/<run-id>/cx-stdout.log
<output>/diagnostics/<run-id>/cx-stderr.log
```

`run.json` owns:

- sanitized command/run summary;
- step timeline;
- selected telemetry snapshots;
- OBS connect/start/stop outcomes;
- raw/published paths;
- verifier warning/result;
- failure classification/message and last completed step;
- retained sandbox path on failure.

Do not copy sandbox `Config.ini`.

Use one focused test proving known OBS/API secrets are absent from `run.json` and recorder-owned error text. Do not build a broad redaction subsystem.

### 9. Artifact verification and publishing

`RecordingArtifactVerifier` is the single owner of raw-artifact trust:

1. returned path is fully qualified;
2. path is inside configured `DTXMANIA_VIDEO_OBS_OUTPUT_DIR` using Windows path semantics;
3. file exists and is non-empty;
4. if `ffprobe` is on `PATH`, invoke it with a local `ProcessStartInfo` and require at least one readable video stream and one audio stream;
5. if `ffprobe` is unavailable, record a warning and continue;
6. copy the raw file into `--output` without deleting the raw file;
7. fail on destination collision rather than overwrite.

Do not duplicate containment checks in `ObsWebSocketRecorder`.

Do not add FFMpegCore/MMTools packages to the recorder in HPA-503.

### 10. `doctor` and manual OBS prerequisite

`doctor` is read-only. It checks/reports:

- Windows platform;
- repo root and Windows game project;
- normalized source `Config.ini` contract;
- loopback OBS URL and configured output directory;
- OBS authentication and current record status;
- `ffprobe` availability.

It also prints the HPA-503 prerequisite that the operator must already have selected/configured a dedicated OBS profile/collection/scene with:

```text
- CX window/program capture
- CX application audio
- Hybrid MP4 recording
- WebSocket enabled
- output directory matching DTXMANIA_VIDEO_OBS_OUTPUT_DIR
```

`doctor` does not enumerate/inspect those OBS sources or judge visual/audio quality. HPA-513 documents and formally accepts that configuration.

It should also note that HPA-503 intentionally uses a fresh sandbox database, so first-run library enumeration can take several minutes.

## Testing

### Sandbox/config

- `SkinPath=Default` survives unchanged, case-insensitively.
- absolute path settings survive unchanged.
- relative/legacy path values fail with the normalize-config instruction.
- missing required normalized song/system path data fails clearly.
- unrelated visible preferences and `LastUsedSkin` survive.
- only recorder-owned keys are overridden.
- live DB/WAL/cache/crash data is never copied.
- success deletes the run root; failure retains it.

### OBS

Unit test `ObsProtocol` directly for:

- authenticated/unauthenticated handshake data;
- record-status mapping;
- start/stop failure mapping;
- missing stop output path;
- malformed/unsupported responses;
- no secret formatting.

No fake WebSocket server. The Task-2 `doctor` run proves live Hello/Identify/request correlation.

### Workflow

Use fake `IGameRecordingControl`, fake `IObsRecorder`, and controlled delay. Cover:

- exact Title -> Enter -> populated Song Select -> prepare ordering;
- OBS already active;
- start/stop failure and ownership;
- preview/stage/performance timeout classes;
- unexpected stage order;
- AutoPlay disabled, zero notes, incomplete judgements, unsuccessful completion;
- cancellation during preview/gameplay/Result hold;
- idempotent cleanup.

Do not duplicate Automation transport/process tests.

### Diagnostics/media

- one secret-absence test;
- path containment belongs only to verifier;
- missing/empty raw artifact;
- optional ffprobe video/audio result;
- collision fails closed;
- copy preserves raw artifact.

### CI and proof

Run the plain recorder tests on Windows and macOS CI. Do not add live OBS CI.

Before HPA-503 completes:

1. run `doctor` against the dedicated Windows OBS profile;
2. run one short indexed chart with a valid preview;
3. retain published MP4, raw MP4, and diagnostics;
4. manually open the MP4 only to confirm the proof is plausibly the intended capture;
5. confirm the source CX app data/database were untouched.

HPA-513 performs the formal visual/audio/setup acceptance pass next.

## Risks

- **Cold library cost:** every HPA-503 run intentionally uses a fresh DB and may re-enumerate the active library. This is accepted for the first proof; persistent recorder caching is a later optimization if usage proves it worthwhile.
- **Manual OBS misconfiguration:** the narrow four-op client cannot detect a wrong capture/audio source, so a black/silent file can pass structural checks. The HPA-503 proof includes a manual open; HPA-513 owns systematic acceptance/setup documentation.
- **Source config not normalized:** legacy/hand-edited relative paths are rejected rather than guessed. Running CX once normalizes the supported source configuration.
- **Very long charts:** the proof chart must fit comfortably inside the fixed Performance timeout. Timeout customization is deferred until a real use case needs it.

## Expected implementation shape

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
```

No repositories, managers, registries, platform adapters, or general capture/protocol frameworks.

## Acceptance mapping

HPA-503 is complete when one Windows command demonstrates the required journey while:

- using a unique disposable app-data root and never copying/touching the live DB;
- entering Song Select through normal Title GAME START and waiting for the library before prepare;
- preserving `SkinPath=Default` and requiring normalized absolute filesystem config paths;
- starting/stopping only its owned OBS recording;
- terminating only its owned CX process;
- preserving raw output and publishing a non-empty MP4;
- structurally validating video/audio with `ffprobe` when available;
- writing compact secret-free diagnostics;
- passing recorder tests on both OS CI jobs;
- retaining one Windows proof for HPA-513.