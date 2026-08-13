# HPA-503 Windows Recorder Vertical Slice Design

**Issue:** [HPA-503](https://linear.app/cwchanap/issue/HPA-503/build-the-windows-recorder-vertical-slice-and-produce-the-first-video)  
**Date:** 2026-08-12  
**Status:** Proposed for planning-PR review

## Why this is the next task

HPA-503 was blocked by HPA-501 and HPA-510. Both prerequisites are now complete:

- HPA-501 supplied the plain `net8.0` `DTXMania.Automation` process, JSON-RPC, polling, and telemetry foundation.
- HPA-510 supplied exact-chart preparation, controlled preview playback, prepared-preview elapsed telemetry, normal Song Select activation, and screenshot render barriers.

That makes the Windows recorder vertical slice the highest-priority unblocked child of the HPA-500 recording epic. HPA-513 depends on its first real recording. Apple Silicon work remains intentionally after the Windows proof.

HPA-503 already absorbed the former sandbox and shared-OBS subtasks. Do not split those abstractions back out unless implementation evidence proves the combined slice cannot stay within roughly 2–3 engineer days.

## Goal

Deliver one local Windows command that records this exact normal CX journey for one indexed chart:

```text
Song Select
-> 10 seconds of chart preview
-> Song Transition
-> full AutoPlay Performance
-> rendered Result
-> 5-second Result hold
```

The command must leave the user's normal CX app data and score database unchanged, stop only the OBS recording it started, terminate only the CX process it launched, publish a non-empty MP4, and retain useful sanitized diagnostics.

## Non-goals

- Apple Silicon live capture.
- A reusable capture framework or platform-adapter hierarchy.
- A generic OBS client, source enumeration, scene creation, screenshots, volume meters, reconnect ownership recovery, or arbitrary request forwarding.
- A second process/JSON-RPC abstraction beside `DTXMania.Automation`.
- A new CX recording/session state machine.
- Copying or snapshotting the user's live `songs.db`.
- MKV fallback, remux, re-encoding, strict duration/FPS validation, YouTube upload, editing, overlays, or batch queues.
- Automatic OBS source/audio acceptance. HPA-513 owns formal visual/audio acceptance and setup documentation.

## Considered approaches

### A. One recorder executable with narrow internal components — chosen

Add a plain `net8.0` `DTXMania.VideoRecorder` executable plus a plain test project. The executable references only `DTXMania.Automation`; sandbox, OBS, diagnostics, and media checks stay private to the recorder.

This keeps ownership obvious and matches the consolidated HPA-503 scope without creating architecture that only one consumer needs.

### B. Separate sandbox, OBS, media, and workflow libraries

This gives stronger theoretical reuse, but HPA-503 currently has one consumer and the former sandbox/OBS tickets were intentionally merged into the vertical slice. Splitting them again would add project boundaries, public APIs, and coordination work before reuse is proven.

Rejected for YAGNI.

### C. Put recording orchestration in `DTXMania.E2E` or `DTXMania.Game`

E2E already has similar disposable-fixture concepts, while the game owns configuration types. Reusing those assemblies directly would make the shipping tool depend on test code or MonoGame/game internals and would blur the external-tool boundary established by HPA-501.

Rejected. Reuse behavior and conventions, not those assemblies.

## Chosen architecture

### 1. Project boundary

Create:

```text
DTXMania.VideoRecorder/
DTXMania.VideoRecorder.Tests/
```

`DTXMania.VideoRecorder` targets plain `net8.0` and has one project reference:

```text
DTXMania.Automation
```

It must not reference `DTXMania.Game`, `DTXMania.E2E`, or MCP.

Do not add a DI container. `Program` constructs the few concrete collaborators directly and passes them to `RecordWorkflow`.

The executable exposes only:

```text
dtx-video doctor
dtx-video record --chart <absolute-dtx-path> --output <directory>
```

A small hand-written parser is sufficient for two verbs and two required record options. Do not add a command-framework dependency for this MVP.

### 2. Environment configuration

Keep secrets and machine-local OBS settings out of CLI arguments and diagnostics.

Use a narrow environment seam:

```text
DTXMANIA_VIDEO_OBS_URL
DTXMANIA_VIDEO_OBS_PASSWORD
DTXMANIA_VIDEO_OBS_OUTPUT_DIR
```

Defaults/requirements:

- URL defaults to `ws://127.0.0.1:4455`.
- Password may be blank only when the user's OBS WebSocket server is configured without authentication.
- OBS output directory is required for `record`; it defines the trusted raw-artifact scope used by path verification.

The requested `--output` directory is the recorder's published destination and is intentionally separate from the raw OBS output directory.

Never persist the OBS password or generated CX Game API key in diagnostics.

### 3. Disposable CX app-data sandbox

Each record invocation creates a unique temporary run root such as:

```text
%TEMP%/DTXManiaCX-video/<run-id>/appdata
```

Source app data comes from `DTXMANIA_APPDATA_ROOT` when explicitly set; otherwise use the Windows CX default under local application data.

Require the source `Config.ini` to exist. Copy it into the sandbox, then patch the copy rather than constructing a second configuration model.

Before relocating the file, canonicalize path-valued presentation/song settings that may be relative to the source app-data root:

```text
DTXPath
SongRoot.<n>
SkinPath
SystemSkinRoot
```

Write those values as absolute paths in the sandbox copy. This preserves the user's active song roots and selected skin even though CX will resolve the copied config from a different app-data root.

Preserve all other existing configuration, including visible gameplay preferences such as scroll speed, play speed, pitch, volumes, and `LastUsedSkin`.

Patch only recording-owned values:

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

Do not copy `songs.db`, WAL files, caches, crash reports, or score/history data. CX builds a fresh `songs.db` in the sandbox from the preserved song roots.

The INI patcher is an internal text utility, not a replacement configuration system. It only needs to replace/add the keys above while preserving unrelated lines. Follow the existing section-agnostic key behavior rather than requiring a specific section placement.

Cleanup policy:

- success: delete the sandbox after diagnostics and published artifact are safely written;
- failure/cancellation: retain the run root and record its path in diagnostics;
- cleanup must be safe if called more than once.

### 4. CX launch and control reuse

Use `DTXMania.Automation.Process.GameProcessDriver` as the sole CX process owner.

For HPA-503 live execution:

- require Windows;
- resolve the repository root and `GameProjectPaths.Windows`;
- launch the Windows game project through `GameProcessDriver` with the sandbox app-data root, per-run launch token, and working directory;
- create `JsonRpcGameClient` with the sandbox API URL/key;
- use `WaitForStartupAsync` for bounded launch identity/readiness;
- use the existing Automation polling support for stage/telemetry waits;
- let `GameProcessDriver.DisposeAsync` own process-tree termination and stdout/stderr capture.

Do not add executable/RID inference, publish discovery, or a second process wrapper in this ticket.

### 5. Minimal OBS seam

Define one recorder-facing interface:

```text
IObsRecorder
- ConnectAsync
- GetRecordStatusAsync
- StartRecordAsync
- StopRecordAsync -> raw output path
```

The production implementation uses only built-in `ClientWebSocket` and `System.Text.Json` and implements the minimum obs-websocket 5.x handshake/authentication plus the requests required by these four operations.

Required ownership rules:

- connect only to the configured loopback endpoint for MVP;
- reject the workflow if OBS is already recording before this run starts;
- after `StartRecordAsync`, confirm OBS reports recording active;
- set local ownership only after this run successfully starts recording;
- `StopRecordAsync` is legal only when this instance owns the recording;
- after stop, return the raw OBS output path;
- validate the returned raw path is fully qualified and inside `DTXMANIA_VIDEO_OBS_OUTPUT_DIR` before publishing it.

No fake WebSocket server is required for tests. Keep protocol parsing/authentication/request-response correlation as small pure/internal units where useful, and test those units directly.

### 6. One imperative `RecordWorkflow`

`RecordWorkflow` is the sole orchestration owner. Do not introduce a generic workflow engine or capture state machine.

Track only a diagnostic step enum:

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

Required sequence:

1. Validate the absolute chart path, output directory, Windows platform, source config, and OBS configuration.
2. Create and patch the disposable app-data sandbox.
3. Launch the owned CX Windows process through Automation.
4. Wait for API readiness and `SongSelect`.
5. Call `PrepareVideoChartAsync` for the exact chart path.
6. Call `TakeScreenshotBase64Async` and require non-empty image data as the Song Select render barrier.
7. Connect to OBS and reject an already-active recording.
8. Start OBS and confirm active status.
9. Call `StartPreparedPreviewAsync`.
10. Poll until `PreparedPreviewElapsedMs >= 10_000` while prepared preview state remains healthy.
11. Call `ActivatePreparedChartAsync`.
12. Observe `SongTransition -> Performance -> Result` in that order with bounded waits.
13. During Performance require `PerformanceReady`, `AutoPlayEnabled`, `TotalNotes > 0`, and eventually complete successful song telemetry.
14. At Result require complete judgements (`TotalJudgements == TotalNotes`), `StageCompleted`, `ClearFlag`, and `CompletionReason == SongComplete`.
15. Call `TakeScreenshotBase64Async` again as the rendered Result barrier.
16. Hold Result for five seconds without sending input.
17. Stop the OBS recording owned by this run and capture its raw path.
18. Verify/publish the artifact.
19. Write final diagnostics, terminate the owned CX process, and apply sandbox cleanup policy.

All polling/timeouts are bounded. The only intentional product-duration waits are CX-reported preview playback and the five-second Result hold.

Use .NET 8 `TimeProvider` for delay/time seams rather than inventing a general clock abstraction. Tests can inject `FakeTimeProvider` only if the project already has the required testing package; otherwise use one tiny internal delay seam local to `RecordWorkflow`. Do not add a time framework solely for this task.

### 7. Failure and cleanup behavior

Run the whole owned-resource lifetime through one `try/finally` cleanup path, including Ctrl+C cancellation.

Cleanup order is conservative:

1. if this run owns an active OBS recording, attempt bounded stop and retain any returned raw path;
2. terminate/dispose the owned CX process;
3. write final diagnostics with the cleanup outcome;
4. delete the sandbox only on success.

Cleanup exceptions must be recorded without hiding the primary failure. Calling cleanup again must not restart/stall OBS or throw because CX already exited.

Never stop a recording that was active before this run.

### 8. Diagnostics

Create a compact diagnostics directory under the requested published output, for example:

```text
<output>/diagnostics/<run-id>/
```

Retain:

- sanitized invocation/job summary;
- `RecordingStep` timeline with timestamps;
- selected CX telemetry snapshots at major boundaries/failure;
- CX stdout/stderr;
- OBS connect/start/stop status without credentials;
- raw and published artifact paths;
- failure classification, message, and last completed step;
- retained sandbox path on failure.

Do not dump the copied `Config.ini` because it contains the per-run API key and may contain other user-specific settings. If configuration evidence is needed, write only an allowlisted summary of non-secret fields used by the run.

### 9. Basic media verification and publishing

MVP verification is deliberately shallow:

1. raw path is inside the configured OBS output directory;
2. file exists;
3. file length is greater than zero;
4. when `ffprobe` is available on `PATH`, require at least one readable video stream and one audio stream;
5. when `ffprobe` is unavailable, record a diagnostic warning and continue because the Linear contract explicitly makes probing conditional on availability.

Do not parse codecs, frame rates, duration tolerances, or remux.

Publish by copying the raw file into `--output` without deleting the OBS raw artifact. Preserve the OBS file name; fail clearly rather than silently overwrite an existing destination.

### 10. `doctor`

`doctor` is a read-only preflight for the same narrow dependencies:

- Windows platform;
- repository root and Windows game project exist;
- source CX `Config.ini` exists;
- OBS URL/output directory configuration is valid;
- OBS WebSocket connection/authentication succeeds;
- current OBS record status can be read;
- report whether OBS is already recording;
- report whether `ffprobe` is available.

`doctor` does not create a sandbox, mutate OBS, launch CX, inspect OBS sources/scenes, or validate capture/audio quality.

## Testing strategy

Create plain `net8.0` `DTXMania.VideoRecorder.Tests`, mirroring the dependency-light Automation tests.

Focus on behavior that can run without MonoGame or OBS:

### Sandbox/config tests

- relative source song/skin paths become absolute against source app data;
- indexed `SongRoot.N` values are preserved;
- visible preferences remain unchanged;
- only recording-owned keys are overridden;
- live `songs.db` is never copied;
- generated API key is not exposed through diagnostics;
- success deletes sandbox; failure retains it.

### OBS unit tests

- authentication calculation/handshake parsing where applicable;
- record-status response mapping;
- start/stop success and failure response mapping;
- raw output path outside configured OBS output directory is rejected;
- no general fake obs-websocket server.

### Workflow tests

Use fake `IObsRecorder`, fake/controlled delay, and a narrow fake game-control seam around the already-tested Automation client behavior. Cover:

- happy sequence and exact ordering;
- OBS already recording;
- OBS start/stop failure;
- preview, Performance, and Result timeout;
- unexpected stage order;
- AutoPlay disabled;
- zero notes, incomplete judgements, or unsuccessful completion;
- cancellation during preview, gameplay, and Result hold;
- cleanup ownership and idempotency;
- secret redaction;
- missing/empty raw output.

Do not duplicate Automation transport/process tests.

### CI and live proof

Run `DTXMania.VideoRecorder.Tests` on both Windows and macOS CI because the project is platform-neutral even though live recording is Windows-only. Do not add live OBS CI.

Before completing HPA-503, manually execute one Windows `record` command against a short indexed chart using the dedicated OBS profile and retain:

- the initial MP4;
- sanitized diagnostics.

That proof establishes the vertical slice. HPA-513 remains responsible for formal visual/audio acceptance and reproducible OBS setup documentation.

## Expected file shape

Keep the implementation small and responsibility-oriented. Exact names may adjust to repository conventions, but the intended ownership is:

```text
DTXMania.VideoRecorder/
  DTXMania.VideoRecorder.csproj
  Program.cs
  RecorderCommandLine.cs
  Configuration/RecorderEnvironment.cs
  Sandbox/RecordingSandbox.cs
  Obs/IObsRecorder.cs
  Obs/ObsWebSocketRecorder.cs
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
```

Do not create repositories, factories, managers, registries, platform adapters, or generic protocol/process frameworks around these files.

## Implementation sizing

This remains one implementation PR:

1. recorder project, CLI/environment, and disposable config sandbox;
2. narrow OBS client and raw-path ownership checks;
3. imperative workflow, diagnostics, media verification, and cleanup;
4. focused unit tests, portable CI entry, and one Windows proof run.

Target: roughly 2–3 engineer days. If implementation evidence shows the live OBS protocol work alone exceeds that budget, split only the live protocol portion while keeping its four-operation interface unchanged; do not pre-emptively decompose the task.

## Acceptance mapping

HPA-503 is complete when all of the following are demonstrated:

- one Windows command records the exact prepared-chart journey through normal CX stages;
- the user's normal CX app-data root and score database are untouched;
- recorder-owned overrides exist only in disposable app data;
- the run never stops a pre-existing OBS recording;
- the owned CX process is always terminated;
- a non-empty Hybrid MP4 is preserved raw and published to the requested directory;
- optional `ffprobe` validation proves video+audio streams when the tool is available;
- sanitized diagnostics identify the last completed step and retain useful failure evidence;
- the design remains a small external-tool layer on `DTXMania.Automation`, ready for later macOS reuse without introducing a platform framework now.
