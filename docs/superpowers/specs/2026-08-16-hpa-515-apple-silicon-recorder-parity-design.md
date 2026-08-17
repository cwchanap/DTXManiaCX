# HPA-515 Apple Silicon Recorder Parity Design

**Issue:** [HPA-515](https://linear.app/cwchanap/issue/HPA-515/port-the-proven-recorder-workflow-to-apple-silicon-macos-and-validate)  
**Date:** 2026-08-16  
**Status:** Proposed

## Goal

Port the accepted HPA-503/HPA-513 recorder workflow to native Apple Silicon macOS with the smallest possible platform-specific change, then retain one inspected Mac recording and a concise operator runbook.

The public command and captured journey stay unchanged:

```text
dtx-video record --chart <absolute-chart-path> --output <directory>

Song Select
-> at least 10 seconds of prepared preview audio
-> complete Song Transition
-> full AutoPlay Performance
-> completed Result held for at least 5 seconds
```

HPA-515 is parity work. It is not a second recorder architecture and it does not add a second launch mode.

## Why this is the next actionable task

- HPA-513 is Done and accepted the shared recorder workflow on Windows.
- HPA-512 is Done and provides native `osx-arm64` FFmpeg/ffprobe for CX preview and gameplay audio.
- HPA-515 is the remaining high-priority unblocked leaf under HPA-500.
- HPA-505 and HPA-506 are optional hardening and should not precede parity.

## Current-state findings

The existing recorder is already mostly platform-neutral:

- `RecordWorkflow` contains no Windows-specific stage flow.
- `ObsWebSocketRecorder` speaks obs-websocket 5.x and does not care whether OBS captures with Game Capture or ScreenCaptureKit.
- `GameProcessDriver` already launches a project through `dotnet run --project`.
- `GameProjectPaths.Current` already selects the Windows or Mac CX project.
- macOS CI already runs `DTXMania.VideoRecorder.Tests` and `DTXMania.Automation.Tests`.
- HPA-512 already places executable arm64 `ffmpeg` and `ffprobe` under `runtimes/osx-arm64/MMTools` in Mac build/publish output.

The relevant Windows assumptions are narrow:

1. `RecorderCommandLine.ValidateRecord` rejects every non-Windows host.
2. `RecorderGameLaunchPolicy.CreateOptions` hard-codes `DTXMania.Game.Windows.csproj` and requires a `RecordingSandbox` even though target resolution itself does not.
3. `Program.RunDoctorAsync` duplicates repository-root discovery and hard-codes the Windows project/guidance.
4. the recorder's default app-data lookup does not explicitly encode CX's macOS `~/Library/Application Support/DTXManiaCX` contract.

Those are the intended implementation seams.

## Approaches considered

### A. Extend the existing recorder with one project-target resolver and one focused preflight — recommended

Keep one `RecordWorkflow`, one OBS client, one CLI, and one project launch path. Reuse `GameProjectPaths.Current` and `GameLaunchTarget.Project(...)`. Resolve the launch target once, run a small platform/runtime preflight before recording, and reuse both in `doctor`.

**Pros**

- smallest production change;
- preserves the proven Windows journey;
- prevents doctor and record from drifting onto different game targets;
- fails missing Mac runtime before OBS starts;
- naturally uses the existing Windows/macOS test matrix.

**Cons**

- a few Mac-specific checks remain in recorder preflight code.

### B. Move recorder platform policy into `DTXMania.Automation`

Rejected. Automation already exposes the generic project-path and launch primitives. Moving recorder-specific runtime/OBS policy there would broaden a reusable library for one consumer.

### C. Add `IRecorderPlatform` with Windows/Mac implementations

Rejected. Two platform branches do not justify an adapter framework. It adds indirection without a second workflow or launch model.

### D. Add packaged-executable recording now

Rejected for HPA-515. The accepted Windows proof uses project mode and the Mac parity proof should exercise the same contract. A packaged executable adds a second working-directory/runtime-layout path that the acceptance run would not prove.

If packaged-app recording becomes useful later, it can be added behind the single launch-policy seam created here.

## Design decision

Use approach A and keep HPA-515 project-mode-only.

The recorder gains three focused changes:

```text
RecorderGameLaunchPolicy
  -> ResolveTarget(...) without a sandbox
  -> current platform project + repository-root working directory
  -> CreateOptions(...) only adds sandbox app-data + launch token

RecorderPlatformPreflight
  -> validate native Mac host and bundled Debug runtime
  -> consume the already-resolved project target

Program / RecorderCommandLine
  -> permit macOS as a recorder host
  -> run preflight before record creates sandbox or talks to OBS
  -> doctor prints the same resolved target/preflight gates
```

`RecordWorkflow`, `IGameRecordingControl`, `ObsWebSocketRecorder`, sandbox semantics, diagnostics schema, finalization, and `GameProcessDriver` remain unchanged unless the native proof exposes a directly related defect.

## Project target resolution

Add one sandbox-free resolved-target result owned by `RecorderGameLaunchPolicy`, for example:

```csharp
internal sealed record ResolvedRecorderTarget(
    string RepositoryRoot,
    string WorkingDirectory,
    GameLaunchTarget Target);
```

`ResolveTarget(startDirectory)` must:

1. reuse the existing repository-root walk;
2. read `GameProjectPaths.Current`;
3. combine it with the repository root to produce an absolute project path;
4. create `GameLaunchTarget.Project(projectPath)`;
5. use the repository root as the working directory.

On Apple Silicon the target is:

```text
DTXMania.Game/DTXMania.Game.Mac.csproj
```

On Windows it remains:

```text
DTXMania.Game/DTXMania.Game.Windows.csproj
```

`CreateOptions` then combines the resolved target with:

- `RecordingSandbox.AppDataRoot`;
- a fresh launch token.

This separation lets `doctor` inspect exactly what `record` would launch without creating a disposable sandbox just to discover a path.

Do not add platform arguments, executable overrides, app-bundle discovery, DMG mounting, or `open`-based launch behavior.

## Supported platform contract

### Windows

Keep current behavior unchanged. Do not add new Windows version, architecture, or runtime requirements as part of Mac parity.

### macOS

Recording is supported only when all of the following are true:

- macOS 13 or later;
- the recorder process is native `arm64`;
- `GameProjectPaths.Current` resolves to the Mac project;
- the project exists;
- the expected HPA-512 Debug runtime contains executable `ffmpeg` and `ffprobe`.

Intel macOS and Rosetta are out of scope. There is no PATH fallback for the supported CX audio runtime.

`RecorderCommandLine` should continue owning syntax/path/environment validation and may widen the existing OS gate from Windows-only to Windows-or-macOS. Native host/build-layout validation belongs to the focused preflight below rather than command parsing.

## Native platform/runtime preflight

Add one internal pure-ish helper, `RecorderPlatformPreflight`, that consumes the already-resolved project target plus testable host/file facts.

For Windows, it preserves today's supported behavior and adds no new gate.

For macOS, it validates:

```text
macOS major version >= 13
process architecture == Arm64
resolved project == DTXMania.Game.Mac.csproj
project file exists
<project-dir>/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffmpeg exists + executable
<project-dir>/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffprobe exists + executable
```

The Debug layout is intentional. `GameProcessDriver` currently invokes:

```text
dotnet run --project <project>
```

without `--configuration`, so the recorder's supported project-mode launch is Debug. HPA-515 should state and test that contract instead of adding configuration/RID search logic.

The helper must not:

- reference `DTXMania.Game`;
- call or duplicate `FfmpegRuntime`;
- configure FFMpegCore;
- search PATH for the CX runtime;
- probe codecs;
- download FFmpeg;
- inspect OBS sources or privacy permissions.

The recorder-side `RecordingArtifactVerifier` may continue its existing optional PATH `ffprobe` lookup for final MP4 inspection. That is separate from the required bundled CX runtime.

### Record call site

A Mac `record` command must execute the same preflight **before** `RunRecordAsync` creates the disposable sandbox, initializes OBS, or starts the workflow.

This is intentionally a program/orchestration gate rather than a `RecorderCommandLine` build-layout check: command parsing validates the command; launch/preflight code validates the target that will actually run.

Missing/unusable bundled FFmpeg therefore fails loudly before any OBS recording can begin.

### Doctor call site

`doctor` resolves the same target and consumes the same preflight result to print individual pass/fail gates. It must not independently reconstruct the Mac project or runtime path.

## Default source app-data contract

Keep `DTXMANIA_APPDATA_ROOT` as the explicit override.

When no override is supplied, encode the CX macOS contract directly:

```text
~/Library/Application Support/DTXManiaCX
```

Do not depend on `SpecialFolder.LocalApplicationData` happening to map to the same location on current .NET/macOS. The source path used for sandbox creation and before/after isolation hashes must be the same path CX considers its normal data root.

Windows default behavior stays unchanged.

## `doctor` behavior

Keep `doctor` read-only with respect to OBS. It still performs only:

```text
Hello
Identify
GetRecordStatus
```

Shared output should include:

- recorder configuration;
- repository root;
- selected project target;
- source Config.ini validation;
- raw OBS output directory;
- OBS auth/status;
- optional recorder-side PATH `ffprobe` warning.

Mac-specific gates should include:

- macOS 13+;
- recorder process `arm64`;
- Mac project target;
- native CX runtime directory;
- bundled `ffmpeg` executable;
- bundled `ffprobe` executable.

Mac manual guidance should state only:

```text
Dedicated DTXManiaCX profile/collection/scene selected
ScreenCaptureKit application/window capture scoped to CX
CX application audio enabled through that source or one dedicated macOS Audio Capture source
Desktop Audio disabled for the recorded track
Microphone disabled for the recorded track
Hybrid MP4 configured
Screen Recording permission granted manually
WebSocket enabled/authenticated
raw output directory matches DTXMANIA_VIDEO_OBS_OUTPUT_DIR
```

The tool must not claim to inspect ScreenCaptureKit selection, audio meters, stale selectors, or macOS privacy state.

Windows doctor guidance remains the existing Game Capture/application-audio guidance.

## Recording workflow

No workflow fork is introduced.

After preflight succeeds, macOS follows the existing workflow unchanged:

1. create disposable recorder app-data sandbox;
2. launch the resolved Mac project through `DTXMania.Automation`;
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

No Mac-specific stage timing, retry policy, or diagnostics schema is added.

## OBS / ScreenCaptureKit contract

Keep OBS setup manual and minimal:

- OBS Studio 30.2+;
- dedicated DTXManiaCX profile, collection, and scene;
- ScreenCaptureKit application/window capture scoped to CX;
- CX application audio enabled through that source or one dedicated macOS Audio Capture source;
- global Desktop Audio disabled for the recorded track;
- microphone disabled for the recorded track;
- Hybrid MP4;
- authenticated obs-websocket 5.x;
- Screen Recording permission granted manually;
- OBS idle before `record` starts.

HPA-505 remains the optional source/audio/privacy diagnostic follow-up. HPA-506 remains the optional strict media/remux follow-up.

## Acceptance chart and native evidence

Use one short chart already indexed by normal CX Song Select with valid preview audio. Prefer MP3 preview/BGM or another encoded input that demonstrably exercises the HPA-512 runtime.

Retain outside Git:

```text
<proof-root>/
  doctor.txt
  architecture.txt
  source-state-before.txt
  source-state-after.txt
  raw/<obs-produced-file>.mp4
  published/<published-file>.mp4
  published/diagnostics/<run-id>/run.json
  published/diagnostics/<run-id>/cx-stdout.log
  published/diagnostics/<run-id>/cx-stderr.log
```

`architecture.txt` should show, without private paths or secrets:

- host is Apple Silicon;
- selected CX apphost is arm64;
- bundled `ffmpeg` is arm64;
- bundled `ffprobe` is arm64.

The preflight proves executable presence before OBS. The HPA-512 focused audio tests plus encoded acceptance chart prove the runtime is actually useful for CX audio.

## Source app-data isolation

Reuse the HPA-513 before/after hash proof against the resolved normal Mac app-data root.

Capture presence, byte size, and SHA-256 for:

```text
Config.ini
songs.db
songs.db-wal
songs.db-shm
```

Acceptance requires no content change and no new source WAL/SHM file caused by the recorder.

Do not add recorder instrumentation for this proof.

## Automated telemetry acceptance

Reuse the HPA-513 `run.json` contract. Require at least:

- `status == Completed`;
- selected song present at `SongSelectReady`;
- preview state Playing and elapsed preview >= 10,000 ms;
- Performance ready, AutoPlay enabled, total notes > 0;
- Result completed/cleared by song completion;
- `totalJudgements == totalNotes`;
- OBS Connect/Status/Start/Stop succeeded;
- raw/published paths recorded;
- no failure fields or retained successful sandbox;
- verifier warning absent or only the existing optional PATH-`ffprobe` warning.

## Manual media acceptance

Watch the complete published MP4 and confirm:

- intended populated Song Select is first;
- preview begins after Song Select is visibly captured and lasts at least 10 seconds;
- Song Transition is complete;
- full AutoPlay gameplay is visible and BGM/chip audio is audible;
- Result is fully rendered and held at least 5 seconds;
- recording ends after the Result hold;
- no OBS UI, desktop, unrelated window, cursor, notification, microphone, or unrelated application audio is captured;
- CX audio is not duplicated or echoed;
- no aspect squeeze, severe stutter, or missing viewport region makes the recording unusable;
- CX preview/gameplay audio does not require Rosetta or user-installed FFmpeg.

## Failure and cleanup strategy

Keep existing automated tests as the primary proof for platform-neutral ownership and cleanup.

The native Mac proof adds only high-value evidence:

1. real Apple Silicon `doctor`;
2. one successful encoded-audio recording;
3. one Ctrl+C after recorder-owned OBS start, confirming OBS stops and partial evidence is retained;
4. manual confirmation that ScreenCaptureKit/privacy setup failures remain operator-visible and are not falsely classified by `doctor`.

Do not revoke permissions or add source-diagnostic APIs merely to manufacture a failure case.

## Testing strategy

Extend `DTXMania.VideoRecorder.Tests` for:

- `ResolveTarget` choosing the current platform project and repository-root working directory;
- `CreateOptions` adding only sandbox app-data and a fresh launch token;
- Mac default app-data resolving explicitly to `~/Library/Application Support/DTXManiaCX` when no override is set;
- Windows record behavior remaining accepted;
- Mac host version/architecture gates;
- Mac Debug runtime path resolution from the already-resolved project target;
- missing or non-executable bundled runtime files producing actionable preflight failure;
- record preflight occurring before recorder/OBS workflow startup through a focused orchestration seam if needed.

Do not create a second Mac `RecordWorkflow` test matrix. Its existing tests remain shared.

No CI edit is planned initially. Existing Windows/macOS VideoRecorder and Automation jobs should execute the new tests; change CI only if implementation proves otherwise.

## Documentation outputs

The implementation/acceptance PR should add:

```text
docs/video-recorder/macos-obs-setup.md
docs/verification/hpa-515-apple-silicon-live-recording.md
```

The runbook should cover only native Apple Silicon prerequisites, Debug project build, ScreenCaptureKit/application-audio setup, Screen Recording permission, OBS/WebSocket environment, `doctor`, `record`, bundled-runtime diagnostics, artifacts, and troubleshooting.

The verification record should contain the accepted commit SHA, macOS/architecture/.NET/OBS versions, chart identity, arm64 evidence, doctor result, sanitized command, MP4 file metadata/hashes, key `run.json` values, source before/after comparison, Ctrl+C result, manual media checklist, focused automated-test results, and warnings/deviations.

Do not document a packaged executable mode in HPA-515.

## Risks

### Manual OBS/privacy setup may fail before code does

Hosted CI cannot prove ScreenCaptureKit selection, application-audio routing, or Screen Recording permission. The first native proof may require only an OBS/privacy correction, not a recorder change. Keep these as operator acceptance failures unless code evidence proves otherwise.

### Debug runtime layout is coupled to the current project launch contract

`GameProcessDriver` uses `dotnet run --project` without `--configuration`, which means Debug output. The preflight therefore deliberately checks `bin/Debug/net8.0`. If recorder launch configuration changes later, launch policy and preflight must change together.

### Missing bundled FFmpeg can look like a capture/audio problem

A missing native runtime could otherwise surface after Song Select/OBS start as bad preview/gameplay audio. Running preflight before sandbox/OBS creation makes this a deterministic recorder error instead.

### Wrong default app-data path invalidates the isolation proof

HPA-515's source hashes are meaningful only if the recorder reads the same normal data root as CX. The explicit Mac default-path rule is therefore part of parity, not incidental cleanup.

## Out of scope

- packaged executable/app-bundle recording;
- app-bundle discovery or DMG mounting;
- Intel Mac or Rosetta support;
- PATH fallback for the supported CX audio runtime;
- automated ScreenCaptureKit source/audio/privacy diagnosis;
- strict codec/frame-rate/duration enforcement;
- MKV fallback/remux/transcoding;
- YouTube upload/editing;
- new platform abstraction/framework;
- unrelated `GameProcessDriver` refactoring.

## Implementation slice

One 2–3 engineer-day implementation/acceptance PR is sufficient:

1. shared project target resolution + explicit Mac app-data default;
2. shared Mac host/runtime preflight used by both record and doctor;
3. native Apple Silicon build/audio proof before OBS;
4. one real ScreenCaptureKit recording + cleanup/isolation/media acceptance;
5. Mac operator runbook + sanitized verification record.

Each checkpoint is independently reviewable. If the native proof exposes an unrelated defect, file a focused blocker rather than expanding HPA-515.

## Acceptance criteria

HPA-515 is complete when:

- the unchanged recorder CLI works on native Apple Silicon macOS 13+;
- record and doctor resolve the same Mac project target;
- record rejects an unsupported Mac host or missing/unusable bundled runtime before starting OBS;
- the default Mac source app-data root matches CX's `~/Library/Application Support/DTXManiaCX` contract;
- one encoded-audio chart completes the accepted Song Select -> preview -> transition -> AutoPlay -> Result journey;
- one inspected Hybrid MP4 and sanitized evidence bundle are retained;
- normal CX app data remains unchanged;
- Ctrl+C stops only recorder-owned OBS work and retains useful failure evidence;
- Windows behavior remains unchanged;
- no platform adapter, packaged-executable mode, HPA-505, or HPA-506 scope is introduced.
