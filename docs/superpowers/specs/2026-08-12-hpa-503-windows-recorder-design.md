# HPA-503 Windows Recorder Vertical Slice Design

**Issue:** [HPA-503](https://linear.app/cwchanap/issue/HPA-503/build-the-windows-recorder-vertical-slice)  
**Date:** 2026-08-12  
**Status:** Revised after second planning review

## Goal

Ship one small Windows-first `dtx-video` executable that records one exact indexed chart through the normal CX journey:

```text
Title -> GAME START -> Song Select
-> 10s prepared preview
-> Song Transition
-> AutoPlay Performance
-> rendered Result -> 5s hold
```

Title is traversed before OBS starts, so captured content begins at Song Select.

HPA-503 remains one 2–3 engineer-day implementation slice. HPA-513 owns the first native Windows recording, final OBS visual/audio acceptance, and setup documentation.

## Boundaries

Create one plain `net8.0` `DTXMania.VideoRecorder` plus tests. Production references only `DTXMania.Automation`.

Keep:

- one imperative `RecordWorkflow`;
- one four-operation `IObsRecorder`;
- built-in `ClientWebSocket` + `System.Text.Json`;
- one unique disposable app-data root per run;
- one `run.json` plus CX stdout/stderr diagnostics.

Do not add:

- `DTXMania.Game`, `DTXMania.E2E`, or MCP project references;
- DI, workflow/session frameworks, platform adapters, batch queues, or persistent recorder DB/cache;
- OBS scene/source enumeration, source screenshots, auto-setup, or capture-quality heuristics;
- live `songs.db`/WAL/cache copying;
- FFMpegCore/MMTools recorder dependencies, remux, re-encoding, or strict media policy.

## Chosen design

### 1. Commands and environment

Commands:

```text
dtx-video doctor
dtx-video record --chart <absolute-dtx-path> --output <directory>
```

OBS environment:

```text
DTXMANIA_VIDEO_OBS_URL         default ws://127.0.0.1:4455, loopback only
DTXMANIA_VIDEO_OBS_PASSWORD    may be blank when OBS auth is disabled
DTXMANIA_VIDEO_OBS_OUTPUT_DIR  required raw-output scope for record
```

`--output` is the separate published destination. Never emit the OBS password or generated CX API key in diagnostics.

### 2. Disposable config sandbox

Every record run creates:

```text
%TEMP%/DTXManiaCX-video/<run-id>/appdata
```

Copy only the source `Config.ini`. Never copy the live database or caches.

The recorder consumes a **current CX-normalized config** rather than partially reimplementing `AppPaths.ResolvePath`:

- preserve `SkinPath=Default` (case-insensitive) as the logical token;
- require `DTXPath`, every `SongRoot.<n>`, `SystemSkinRoot`, and custom `SkinPath` to already be fully qualified;
- require at least one `SongRoot.<n>`;
- preserve `LastUsedSkin` and all unrelated presentation settings verbatim;
- reject relative/legacy filesystem values with: `Open CX once and exit normally, then retry dtx-video.`

Patch only:

```text
EnableGameApi=True
GameApiPort=<per-run port>
GameApiKey=<per-run secret>
AutoPlay=True
NoFail=True
ScreenWidth=1280
ScreenHeight=720
FullScreen=False
```

Successful runs delete the sandbox after publication/diagnostics; failures retain it. Cleanup is idempotent.

A persistent recorder app-data root is intentionally deferred. It would reduce repeated enumeration but violates this ticket's disposable-run contract and would retain recorder-generated AutoPlay score/history state between recordings.

### 3. CX startup and library readiness

Reuse `GameProcessDriver`, `JsonRpcGameClient`, `GameStateSnapshot`, and `Eventually.UntilAsync`. Keep repo-root/ephemeral-port policy local to the recorder.

Use:

```csharp
HttpClient.Timeout = Timeout.InfiniteTimeSpan;
```

The recorder owns finite workflow cancellation.

Startup prefix:

```text
start CX
-> WaitForStartupAsync
-> wait StageType == Title
-> SendKeyAsync("Enter", 50ms)
-> wait StageType == SongSelect AND SelectedSongTitle is non-empty
-> PrepareVideoChartAsync once
```

The populated-Song-Select gate is required because Song Select activates while library initialization is still in progress; `PrepareVideoChart` cannot resolve while its applied library snapshot is null. The existing prepared-chart E2E already uses this readiness pattern.

Do not add `ChangeStageAsync`, new library-ready telemetry, or a new Automation stage-wait API.

### 4. OBS contract and testability

Keep exactly:

```csharp
internal interface IObsRecorder : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken token);
    Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken token);
    Task StartRecordAsync(CancellationToken token);
    Task<string> StopRecordAsync(CancellationToken token);
}
```

Split implementation internally:

```text
ObsProtocol
  auth computation
  narrow v5 request/response parsing
  record-status/start/stop result mapping
  required stop outputPath

ObsWebSocketRecorder
  ClientWebSocket lifetime
  Hello/Identify
  request IDs + one-outstanding-request correlation
  four IObsRecorder operations
```

Unit-test `ObsProtocol` directly. Do not build a fake WebSocket server. `dtx-video doctor` is the live socket/correlation gate; when a dedicated Windows OBS environment is unavailable during HPA-503, record that gate as unverified and complete it in HPA-513.

The workflow fails when OBS is already recording and acquires ownership only after its own StartRecord succeeds.

### 5. RecordWorkflow

Required sequence:

```text
validate -> sandbox -> launch/populated SongSelect -> prepare
-> CX screenshot barrier
-> OBS connect/status/start
-> StartPreparedPreview
-> wait Playing && PreparedPreviewElapsedMs >= 10_000
-> ActivatePreparedChart
-> SongTransition
-> PerformanceReady && AutoPlayEnabled && TotalNotes > 0
-> Result: SongComplete + StageCompleted + ClearFlag
           + TotalJudgements == TotalNotes
-> Result CX screenshot barrier -> 5s no-input hold
-> owned OBS stop -> verify/publish -> diagnostics/cleanup
```

Use four recorder-owned bounds only:

```text
SetupTimeout       5 minutes   startup/library/prepare
StageTimeout       2 minutes   preview/transition/readiness gates
PerformanceTimeout 20 minutes  gameplay to completed Result
ExternalIoTimeout  15 seconds  OBS/ffprobe/cleanup
```

The exact product waits remain CX-reported preview >=10 seconds and Result wall-clock hold ==5 seconds.

Use one ownership-aware `try/finally`; stop OBS only if this run successfully started it, always dispose the owned CX process, and let Ctrl+C flow through the same cancellation path.

### 6. Diagnostics

Write only:

```text
<output>/diagnostics/<run-id>/run.json
<output>/diagnostics/<run-id>/cx-stdout.log
<output>/diagnostics/<run-id>/cx-stderr.log
```

`run.json` contains the step timeline, selected telemetry snapshots, OBS outcomes, artifact paths/verifier warning, failure + last completed step, and retained sandbox path on failure.

Do not copy sandbox `Config.ini`. Use one focused test that known API/OBS secrets are absent; do not create a redaction subsystem.

### 7. Artifact verification

`RecordingArtifactVerifier` alone owns raw-output trust:

1. raw path fully qualified and contained by `DTXMANIA_VIDEO_OBS_OUTPUT_DIR`;
2. file exists and is non-empty;
3. when `ffprobe` exists on `PATH`, require at least one video and one audio stream;
4. when absent, record a warning and continue;
5. copy to `--output`, preserve raw file, fail on collision.

`ObsWebSocketRecorder` only returns the path; it does not duplicate containment validation.

Keep the `ffprobe` invocation local to the verifier. Existing FFMpegCore/MMTools support is game-project runtime infrastructure, not reusable recorder code, and HPA-503 explicitly allows probing to be conditional.

### 8. `doctor` and manual OBS prerequisite

`doctor` is read-only. It checks Windows/repo/config/OBS connection/status/output-dir and `ffprobe` availability.

It also prints the required preconfigured OBS assumptions:

```text
Dedicated profile/collection/scene already selected
CX window/program capture configured
CX application audio configured
Hybrid MP4 recording configured
WebSocket enabled
raw output directory matches DTXMANIA_VIDEO_OBS_OUTPUT_DIR
```

It does not inspect or repair those sources. HPA-513 owns producing and manually inspecting the first native Windows MP4 plus formal quality/setup acceptance.

`doctor` also warns that the intentionally fresh sandbox database may require several minutes for first-run library enumeration.

## Testing and proof

Automated tests cover:

- normalized-config acceptance/rejection, token preservation, owned overrides, no DB/cache copy, cleanup;
- pure OBS auth/response mapping;
- exact workflow ordering including populated Song Select before one-shot prepare;
- OBS ownership/failure/cancellation/idempotent cleanup;
- Performance/Result success predicates;
- compact diagnostics secret absence;
- verifier containment/missing-empty/collision/copy/optional-ffprobe behavior.

Run recorder tests on Windows and macOS CI. No live OBS CI.

Before completing HPA-503, run the portable recorder and Automation suites, the macOS-safe game suite, both normal OS CI definitions, and macOS/Windows-target builds. Record any unavailable native Windows/OBS gate honestly rather than substituting a fake server or hosted OBS CI.

HPA-513 completes the deferred native acceptance: successful Windows `doctor`, one short indexed-chart recording, raw/published MP4 and diagnostics, manual plausibility/quality inspection, and source CX app-data/database isolation evidence.

## Risks

- **Cold library enumeration:** accepted for the first native proof in HPA-513; persistent recorder caching is a follow-up only if real usage needs it.
- **Manual OBS misconfiguration:** structural MP4 checks cannot prove correct window/audio capture; HPA-513 owns the manual proof.
- **Legacy/hand-edited config:** relative paths are rejected rather than guessed; running CX once normalizes the supported source config.
- **Very long charts:** proof chart must fit comfortably inside the fixed Performance timeout; configurable timing is deferred.

## Acceptance

HPA-503 is complete when the recorder implementation enforces the required journey in portable tests; uses a unique disposable sandbox; waits for the actual Song Select library before prepare; preserves normalized config semantics; owns only its CX/OBS resources; implements raw preservation, publication, conditional video/audio probing, and compact secret-free diagnostics; and runs recorder tests in both normal OS CI jobs. HPA-513 owns the first native Windows MP4 and final acceptance.
