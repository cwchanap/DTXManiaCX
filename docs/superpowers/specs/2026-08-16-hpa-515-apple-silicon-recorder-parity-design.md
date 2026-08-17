# HPA-515 Apple Silicon Recorder Parity Design

**Issue:** [HPA-515](https://linear.app/cwchanap/issue/HPA-515/port-the-proven-recorder-workflow-to-apple-silicon-macos-and-validate)  
**Date:** 2026-08-16  
**Status:** Proposed

## Goal

Port the accepted HPA-503/HPA-513 recording workflow to native Apple Silicon macOS with the smallest possible platform-specific change, then retain one inspected Mac recording and a concise operator runbook.

The successful journey and CLI stay unchanged:

```text
dtx-video record --chart <absolute-chart-path> --output <directory>

Song Select
-> at least 10 seconds of prepared preview audio
-> complete Song Transition
-> full AutoPlay Performance
-> completed Result held for at least 5 seconds
```

HPA-515 is parity work, not a second recorder architecture.

## Why this is the next actionable task

- HPA-513 is Done and accepted the shared recorder workflow on Windows.
- HPA-512 is Done and provides native `osx-arm64` FFmpeg/ffprobe for CX preview and gameplay audio.
- HPA-515 is the remaining high-priority unblocked leaf under HPA-500.
- HPA-505 and HPA-506 are explicitly optional hardening and should not precede parity.

## Current-state findings

The existing recorder is already mostly platform-neutral:

- `RecordWorkflow` drives the normal CX journey and contains no Windows-specific stage logic.
- `ObsWebSocketRecorder` only uses obs-websocket 5.x and does not care whether OBS captures through Game Capture or ScreenCaptureKit.
- `GameProcessDriver` already supports both `GameLaunchTarget.Project(...)` and `GameLaunchTarget.Executable(...)`.
- `GameProjectPaths` already exposes both Windows and Mac CX project paths.
- macOS CI already runs `DTXMania.VideoRecorder.Tests` and `DTXMania.Automation.Tests`.
- HPA-512 already packages `runtimes/osx-arm64/MMTools/{ffmpeg,ffprobe}` into Mac build/publish output and verifies the binaries are executable arm64 files.

The actual Windows assumptions are narrow:

1. `RecorderCommandLine.ValidateRecord` rejects every non-Windows host.
2. `RecorderGameLaunchPolicy` hard-codes `DTXMania.Game.Windows.csproj`.
3. `Program.RunDoctorAsync` hard-codes the Windows gate/project and Windows OBS guidance.

That is the intended implementation surface.

## Approaches considered

### A. Extend the existing recorder with a small platform-aware launch/preflight seam — recommended

Keep one `RecordWorkflow`, one OBS client, and one CLI. Reuse `GameProjectPaths.Current` and the existing Automation launch target types. Add only the Mac platform checks, optional packaged-executable selection, and Mac-specific `doctor`/documentation behavior.

**Pros**

- smallest change;
- preserves the proven Windows workflow;
- no duplicated workflow state;
- easy to test on the existing Windows + macOS CI matrix;
- leaves HPA-505/HPA-506 cleanly deferred.

**Cons**

- a few conditional Mac branches remain in recorder launch/preflight code.

### B. Move recorder platform selection into `DTXMania.Automation`

Teach Automation about recorder-specific Mac/Windows defaults and native FFmpeg preflight.

**Rejected:** Automation already has the generic primitives the recorder needs. Moving OBS/recorder policy into it would couple a reusable automation library to one consumer and expand the public API without a second use case.

### C. Add `IRecorderPlatform` with Windows/Mac implementations

Create platform adapters owning launch, doctor, capture guidance, and media/runtime checks.

**Rejected:** this is configuration-by-abstraction for two small branches. It adds more files and indirection than HPA-515 needs and conflicts with the ticket's explicit “no platform-adapter framework” constraint.

## Design decision

Use approach A.

The implementation should remain one recorder with three targeted extensions:

```text
RecorderCommandLine
  -> support Windows OR native Apple Silicon macOS

RecorderGameLaunchPolicy
  -> select current platform project by default
  -> optionally select an explicit packaged executable

Program doctor
  -> report shared gates
  -> add small platform-specific diagnostics/guidance
```

`RecordWorkflow`, `IGameRecordingControl`, `ObsWebSocketRecorder`, sandboxing, diagnostics, and artifact finalization remain unchanged unless the native proof exposes a real defect.

## Supported platform contract

### Windows

Keep current behavior unchanged. Do not tighten existing Windows validation as part of Mac parity.

### macOS

`record` is supported only when all of the following are true:

- macOS 13 or later;
- the recorder process is native `arm64`;
- the selected CX target is the Mac project or an explicit Mac executable;
- the expected native CX FFmpeg runtime is present and executable before the acceptance run.

Intel macOS and Rosetta are out of scope. There is no fallback to the Windows project, x64 Mac runtime, or a generic PATH-only CX audio configuration.

## Game launch selection

### Default project mode

When no override is provided:

1. resolve the repository root exactly as today;
2. use `GameProjectPaths.Current` to choose the platform project;
3. resolve that path under the repository root;
4. preserve repository root as the process working directory;
5. let `GameProcessDriver` continue launching it through `dotnet run --project ...`.

On Apple Silicon this resolves to:

```text
DTXMania.Game/DTXMania.Game.Mac.csproj
```

Do not add a `--platform` command option. The host platform already determines the supported project unambiguously.

### Optional packaged executable mode

Add one optional environment override:

```text
DTXMANIA_VIDEO_GAME_EXECUTABLE=<absolute executable path>
```

When set:

- require an absolute existing file;
- create `GameLaunchTarget.Executable(...)` through the existing Automation API;
- use the executable's parent directory as the game process working directory;
- preserve the disposable app-data root and launch-token behavior exactly as in project mode.

For the current Mac app bundle, the expected executable is:

```text
DTXMania.app/Contents/MacOS/DTXMania.Game.Mac
```

This is deliberately an executable override, not an app-bundle discovery feature. The recorder should not mount DMGs, search `/Applications`, parse `Info.plist`, or launch through `open`.

The override may work on Windows as well because the underlying Automation target is generic, but HPA-515 only needs it for explicit Mac packaged-layout validation.

## Native FFmpeg preflight

The recorder must not take a dependency on `DTXMania.Game` or duplicate `FfmpegRuntime`.

`doctor` should perform a small filesystem preflight against the target that will actually be launched:

### Default Mac project target

Require the normal Debug build output to contain:

```text
DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffmpeg
DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffprobe
```

The runbook should build `DTXMania.Game.Mac.csproj` before `doctor`. HPA-512 already owns generation/copy correctness and CI verification; HPA-515 only checks that the local proof target contains the runtime it is about to exercise.

### Explicit executable target

Resolve the runtime relative to the executable directory:

```text
<executable-dir>/runtimes/osx-arm64/MMTools/ffmpeg
<executable-dir>/runtimes/osx-arm64/MMTools/ffprobe
```

For both modes:

- both files must exist;
- both files must have executable permission on macOS;
- `doctor` reports the resolved runtime directory;
- missing/unusable files fail the Mac doctor gate with an actionable message.

Do not add a second FFmpeg resolver, codec probe, download path, or PATH fallback to the recorder. HPA-512's existing game/runtime tests plus the real MP3 acceptance chart provide the deeper proof.

The native acceptance record should additionally retain `file` output showing the selected game executable and bundled `ffmpeg`/`ffprobe` are arm64. That verification belongs to the proof, not a new runtime abstraction.

## `doctor` behavior

Keep `doctor` read-only with respect to OBS. It still performs only Hello/Identify/GetRecordStatus.

Shared gates remain:

- recorder configuration;
- repository root;
- selected CX target;
- source Config.ini validation;
- raw OBS output directory;
- OBS auth/status;
- optional recorder-side PATH `ffprobe` warning for final MP4 inspection.

Mac-specific gates add:

- macOS 13+;
- recorder process architecture is arm64;
- selected target is usable;
- native CX FFmpeg runtime pair is present/executable.

Mac guidance printed by `doctor` should say only:

```text
Dedicated DTXManiaCX OBS profile/collection/scene selected
ScreenCaptureKit application/window capture scoped to CX
CX application audio enabled through that source or one dedicated macOS Audio Capture source
Desktop Audio and microphone disabled for the recorded track
Hybrid MP4 configured
OBS Screen Recording permission granted manually
WebSocket enabled/authenticated
raw output directory matches DTXMANIA_VIDEO_OBS_OUTPUT_DIR
```

The tool must not claim it can inspect ScreenCaptureKit selection, distinguish stale source selection from privacy denial, inspect audio meters, or grant macOS privacy permission.

Windows doctor output should retain the existing Game Capture/application-audio guidance.

## Recording workflow

No workflow fork is introduced.

On macOS the existing sequence stays:

1. create disposable recorder app-data sandbox;
2. launch CX through `DTXMania.Automation`;
3. wait for populated Song Select;
4. prepare the exact chart with preview stopped;
5. start owned OBS recording;
6. play at least 10 seconds of preview;
7. activate the chart;
8. observe Song Transition;
9. complete full AutoPlay Performance;
10. observe completed Result and hold it for at least 5 seconds;
11. stop only the owned OBS recording;
12. verify/publish the raw artifact;
13. write diagnostics and delete only the successful sandbox.

No Mac-specific stage timing or retry policy is added.

## OBS / ScreenCaptureKit contract

Keep the OBS scene fully manual.

Required proof configuration:

- OBS Studio 30.2+;
- dedicated DTXManiaCX profile, scene collection, and scene;
- ScreenCaptureKit application/window capture scoped to CX;
- CX application audio enabled through that source or one dedicated macOS Audio Capture source;
- global Desktop Audio disabled for the recorded track;
- microphone inputs disabled for the recorded track;
- Hybrid MP4;
- authenticated obs-websocket 5.x;
- Screen Recording privacy permission granted manually;
- OBS idle before `record` starts.

No scene/source enumeration or auto-repair is added. HPA-505 remains the optional follow-up if manual setup proves too fragile.

## Acceptance chart

Use one short chart that is already indexed by normal CX Song Select and has preview audio.

The accepted Mac proof must exercise the HPA-512 runtime. Prefer an MP3 preview/BGM input; otherwise use another encoded input that demonstrably invokes the bundled native FFmpeg path.

Do not create a special recorder-only chart.

## Native acceptance evidence

Retain outside Git:

```text
<proof-root>/
  doctor.txt
  architecture.txt
  source-state-before.txt
  source-state-after.txt
  raw/
    <obs-produced-file>.mp4
  published/
    <published-file>.mp4
    diagnostics/<run-id>/
      run.json
      cx-stdout.log
      cx-stderr.log
```

The exact local path is not a product contract.

`architecture.txt` should record enough sanitized evidence to show:

- host is Apple Silicon;
- `dtx-video` process is arm64;
- selected CX executable/apphost is arm64;
- bundled `ffmpeg` is arm64;
- bundled `ffprobe` is arm64.

Do not commit binaries or private local paths.

## Source app-data isolation

Reuse the HPA-513 proof technique.

Immediately before and after the accepted Mac run, capture presence, size, and SHA-256 for source files when present:

```text
Config.ini
songs.db
songs.db-wal
songs.db-shm
```

Acceptance requires no content change and no new source WAL/SHM creation caused by the recorder.

Do not add recorder instrumentation for this; filesystem hashes are sufficient.

## Automated telemetry acceptance

Reuse the same `run.json` contract accepted by HPA-513. Require at least:

- `status == Completed`;
- selected song is present at `SongSelectReady`;
- preview state is Playing and elapsed preview >= 10,000 ms;
- Performance is ready, AutoPlay is enabled, and total notes > 0;
- Result is completed/cleared by song completion;
- `totalJudgements == totalNotes`;
- OBS Connect/Status/Start/Stop succeeded;
- raw/published paths are recorded;
- no failure fields or retained successful sandbox;
- verifier warning is absent or only the existing optional recorder-side PATH `ffprobe` warning.

Do not create a Mac-only diagnostics schema.

## Manual media acceptance

Watch the complete published MP4 and confirm:

- intended populated Song Select is first;
- preview begins after Song Select is visibly captured;
- preview audio lasts at least 10 seconds;
- Song Transition is complete;
- full AutoPlay gameplay is visible;
- BGM/chip audio is audible;
- Result is fully rendered and held at least 5 seconds;
- recording ends after the Result hold;
- no OBS UI, desktop, unrelated window, cursor, notification, microphone, or unrelated application audio is captured;
- CX audio is not duplicated or echoed;
- no aspect squeeze, severe stutter, or missing viewport region makes the recording unusable;
- no user-installed FFmpeg or Rosetta is required for CX preview/gameplay audio.

Strict codec/frame-rate/duration enforcement remains HPA-506.

## Failure and cleanup strategy

Do not duplicate all HPA-503/HPA-513 platform-neutral cleanup checks manually.

Automated tests remain the primary proof for:

- pre-existing OBS ownership;
- Start failure ownership;
- cancellation after owned OBS start;
- unexpected stage/performance/result failure;
- artifact-finalization failure;
- sandbox retention/deletion ordering.

Mac-native proof should add only high-value platform evidence:

1. `doctor` on the real Apple Silicon workstation;
2. one successful recording using the native bundled FFmpeg path;
3. one Ctrl+C cancellation after recorder-owned OBS start, confirming OBS stops and diagnostics/raw evidence are retained;
4. manual confirmation that an invalid ScreenCaptureKit selection or missing Screen Recording permission is an operator-visible setup problem, not something `doctor` falsely claims to classify.

Do not revoke privacy permissions or invent source-diagnostic APIs merely to create a synthetic failure test.

## Documentation outputs

The HPA-515 implementation/acceptance PR should add:

```text
docs/video-recorder/macos-obs-setup.md
docs/verification/hpa-515-apple-silicon-live-recording.md
```

`macos-obs-setup.md` should cover only:

- Apple Silicon/macOS prerequisites;
- default project-mode build;
- optional `DTXMANIA_VIDEO_GAME_EXECUTABLE` packaged-target usage;
- ScreenCaptureKit source and application-audio setup;
- Screen Recording permission;
- disabled desktop/microphone audio;
- WebSocket environment variables;
- `doctor` and `record` commands;
- native FFmpeg gate;
- artifact locations;
- concise troubleshooting.

`hpa-515-apple-silicon-live-recording.md` should record sanitized proof:

- accepted commit SHA;
- macOS version and Apple Silicon model/architecture;
- .NET and OBS versions;
- project or packaged-executable launch mode;
- non-sensitive chart identity;
- arm64 evidence summary;
- doctor result;
- successful command summary with private paths redacted;
- raw/published MP4 name, size, and SHA-256;
- key `run.json` acceptance values;
- source before/after comparison;
- Ctrl+C cleanup result;
- manual media checklist result;
- focused automated test command/results;
- warnings/deviations.

## Testing strategy

### Portable unit tests

Extend `DTXMania.VideoRecorder.Tests` for:

- environment parsing/validation of the optional game executable;
- default launch target resolves the current platform project;
- explicit executable launch target uses the exact executable and its parent working directory;
- existing Windows behavior remains unchanged;
- Mac native-runtime path resolution for project and executable launch modes;
- missing/unexecutable Mac runtime files produce actionable preflight failure.

Do not mock `RecordWorkflow` into a second Mac workflow test matrix; its existing tests remain shared.

### Existing CI

No workflow edit should be required initially:

- Windows already runs VideoRecorder/Automation tests;
- macOS already runs VideoRecorder/Automation tests;
- macOS already verifies bundled arm64 FFmpeg and executes focused audio tests.

Only touch CI if implementation proves an existing job does not execute the new tests. Do not add a dedicated recorder platform job pre-emptively.

### Native proof

Hosted CI does not replace real OBS + ScreenCaptureKit + privacy-permission acceptance. One local or self-hosted Apple Silicon proof remains required.

## Expected production/test file surface

Expected modifications:

```text
DTXMania.VideoRecorder/Configuration/RecorderEnvironment.cs
DTXMania.VideoRecorder/RecorderCommandLine.cs
DTXMania.VideoRecorder/Workflow/RecorderGameLaunchPolicy.cs
DTXMania.VideoRecorder/Program.cs
```

Expected small additions only if they keep doctor/preflight testable:

```text
DTXMania.VideoRecorder/Diagnostics/<one focused platform-preflight helper>.cs
DTXMania.VideoRecorder.Tests/<focused command-line/launch tests>.cs
DTXMania.VideoRecorder.Tests/Diagnostics/<focused platform-preflight tests>.cs
```

Expected documentation additions:

```text
docs/video-recorder/macos-obs-setup.md
docs/verification/hpa-515-apple-silicon-live-recording.md
```

Normally unchanged:

```text
DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs
DTXMania.VideoRecorder/Obs/**
DTXMania.VideoRecorder/Media/**
DTXMania.VideoRecorder/Sandbox/**
DTXMania.Automation/Process/GameProcessDriver.cs
DTXMania.Game/Lib/Resources/FfmpegRuntime.cs
DTXMania.Game/DTXMania.Game.Mac.csproj
.github/workflows/**
```

If implementation requires broad changes in the “normally unchanged” set, stop and re-evaluate scope before proceeding.

## Estimated implementation shape

One 2–3 engineer-day implementation/acceptance task, split into reviewer-friendly checkpoints:

1. cross-platform command validation + project/executable launch selection;
2. Mac `doctor` platform/native-runtime preflight + focused tests;
3. native Apple Silicon OBS proof and sanitized runbook/verification documents.

This is small enough to stay one HPA-515 implementation PR. If the native proof exposes an unrelated recorder or game defect, split that defect into a blocker instead of expanding HPA-515.

## Acceptance criteria

HPA-515 is complete when:

- the same `doctor`/`record` CLI contract works on Windows and native Apple Silicon macOS;
- default Mac project launch uses `DTXMania.Game.Mac.csproj` through Automation;
- optional explicit packaged Mac executable launch is supported without a new platform framework;
- Mac `doctor` rejects unsupported OS/architecture and missing/unusable native CX FFmpeg runtime;
- one inspected Apple Silicon MP4 covers Song Select -> >=10s preview -> transition -> full AutoPlay gameplay -> >=5s Result;
- MP3/encoded preview/gameplay audio works from bundled native FFmpeg without user-installed FFmpeg or Rosetta;
- normal CX app data remains unchanged;
- cancellation stops only recorder-owned OBS work and retains useful failed-run evidence;
- Windows behavior and shared recorder tests remain green;
- no ScreenCaptureKit diagnostic automation, strict media hardening, or adapter framework is introduced.

## Non-goals

- Intel Mac or Rosetta support;
- changing the shared recording journey;
- app/DMG discovery, mounting, or installation;
- automatic OBS scene/source selection;
- ScreenCaptureKit screenshot/source/meter diagnosis;
- macOS privacy-permission classification or mutation;
- bundled FFmpeg upgrades or a second FFmpeg resolver;
- recorder-side bundled `ffprobe` for final MP4 verification;
- MKV remux, transcoding, strict codec/frame-rate/duration policy;
- YouTube upload/editing/batch recording;
- general platform abstraction for hypothetical future operating systems.