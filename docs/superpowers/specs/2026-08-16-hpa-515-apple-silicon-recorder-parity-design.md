# HPA-515 Apple Silicon Recorder Parity Design

**Issue:** [HPA-515](https://linear.app/cwchanap/issue/HPA-515/port-the-proven-recorder-workflow-to-apple-silicon-macos-and-validate)  
**Date:** 2026-08-16  
**Status:** Proposed

## Goal

Port the accepted HPA-503/HPA-513 recorder workflow to native Apple Silicon macOS with the smallest platform-specific change, then retain one inspected Mac recording and a concise operator runbook.

The public command and captured journey stay unchanged:

```text
dtx-video record --chart <absolute-chart-path> --output <directory>

Song Select
-> at least 10 seconds of prepared preview audio
-> complete Song Transition
-> full AutoPlay Performance
-> completed Result held for at least 5 seconds
```

HPA-515 is parity work. It keeps one workflow and one project-mode launch path.

## Current-state findings

The recorder is already mostly platform-neutral:

- `RecordWorkflow` contains no Windows-specific stage flow.
- `ObsWebSocketRecorder` only speaks obs-websocket 5.x.
- `GameProjectPaths.Current` already selects the Windows or Mac CX project.
- `GameProcessDriver` already owns project launch through `dotnet run --project`.
- macOS CI already runs the VideoRecorder and Automation test projects.
- HPA-512 already builds/copies native arm64 `ffmpeg` and `ffprobe` into the Mac build output.

The remaining Windows assumptions are narrow:

1. `RecorderCommandLine.ValidateRecord` rejects non-Windows hosts.
2. `RecorderGameLaunchPolicy.CreateOptions` hard-codes the Windows project and couples target discovery to sandbox creation.
3. `Program.RunDoctorAsync` duplicates repository/project discovery and prints a Windows-only gate.
4. recorder default app-data resolution is not explicitly pinned to the same macOS contract as `AppPaths.GetAppDataRoot()`.

There is one additional launch correctness issue that HPA-515 must address: a preflight cannot certify `bin/Debug` while `GameProcessDriver` immediately runs a build that may replace that output.

## Approaches considered

### A. One project target + prebuilt/no-build launch + one focused preflight — recommended

Keep the existing recorder architecture. Resolve the current-platform project once, require the Debug output before recording, launch that exact output via `dotnet run --no-build --configuration Debug`, and use one preflight result in both record and doctor.

**Pros**

- the runtime inspected before OBS is the runtime actually launched;
- clean-checkout failure is actionable rather than ambiguous;
- no stale-output check followed by an implicit rebuild;
- no workflow fork or platform adapter;
- existing Automation callers keep today's build-on-run behavior unless they opt into project arguments.

**Cons**

- `GameProcessStartOptions` / `GameProcessDriver` need one small additive extension for project-run arguments.

### B. Move recorder platform policy into `DTXMania.Automation`

Rejected. Automation should expose generic launch primitives, not OBS/recorder policy.

### C. Add `IRecorderPlatform`

Rejected. Two small platform branches do not justify a platform framework.

### D. Add packaged executable recording

Rejected for HPA-515. The accepted Windows proof is project mode and the Mac parity proof should exercise the same contract.

## Design decision

Use approach A.

```text
RecorderGameLaunchPolicy
  -> ResolveTarget(...) without a sandbox
  -> current-platform project + repository-root working directory
  -> CreateOptions(...) adds sandbox app-data, launch token,
     and recorder-owned project run arguments

GameProcessStartOptions / GameProcessDriver
  -> optional project run arguments
  -> recorder supplies: --no-build --configuration Debug
  -> all existing callers may omit them and retain current behavior

RecorderPlatformPreflight
  -> consumes the already-resolved project target
  -> validates Mac host + the exact Debug runtime that no-build will launch

Program
  -> record resolves target and evaluates preflight
  -> RunRecordAsync receives that target/result and rejects failure at its top
  -> only then create sandbox / OBS / workflow
  -> doctor consumes the same target/result
```

`RecordWorkflow`, OBS ownership, diagnostics schema, finalization, and sandbox semantics remain unchanged.

## Launch the artifact that was checked

### Shared target resolution

Add one sandbox-free recorder-local result:

```csharp
internal sealed record ResolvedRecorderTarget(
    string RepositoryRoot,
    string WorkingDirectory,
    GameLaunchTarget Target);
```

`RecorderGameLaunchPolicy.ResolveTarget(startDirectory)` must:

1. reuse the existing repository-root walk;
2. use `GameProjectPaths.Current`;
3. resolve an absolute project path under the repository root;
4. create `GameLaunchTarget.Project(projectPath)`;
5. use the repository root as the working directory.

On macOS this resolves to:

```text
DTXMania.Game/DTXMania.Game.Mac.csproj
```

On Windows it remains:

```text
DTXMania.Game/DTXMania.Game.Windows.csproj
```

### Additive Automation launch option

Extend `GameProcessStartOptions` with one optional project-only argument list, for example:

```csharp
IReadOnlyList<string>? ProjectRunArguments = null
```

For `GameLaunchKind.Project`, `GameProcessDriver.Start` appends those arguments to the `dotnet run` command after the project path. Existing callers that omit the field retain today's behavior.

The recorder's `CreateOptions(...)` supplies:

```text
--no-build
--configuration
Debug
```

The recorder therefore launches:

```text
dotnet run --project <resolved-project> --no-build --configuration Debug
```

Do not change E2E/default Automation callers to no-build as part of HPA-515.

If project-run arguments are supplied for an executable target, reject them rather than silently ignoring them.

### Prebuild contract

The recorder does not compile the game itself. Before `doctor` or `record`, the operator must build the resolved platform project so the Debug apphost and runtime output exist:

```bash
# macOS
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
# Windows
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Debug
```

If the required Debug output is missing, preflight fails before sandbox/OBS with an actionable message naming that exact command.

This intentionally changes HPA-515 from “build during launch” to “build, certify, then no-build launch”. It removes the clean-tree false failure/stale-output replacement race from the previous design. The same build → preflight → `--no-build` contract applies on both macOS and Windows: preflight certifies the Debug apphost that `dotnet run --no-build --configuration Debug` will execute, so a clean checkout cannot pass preflight and then fail at process launch after the sandbox is created.

## Supported platform contract

### Windows

Windows follows the same build → preflight → `--no-build` launch contract as macOS: the operator must build the Windows project first, and preflight certifies the Debug apphost (`<AssemblyName>.exe`) under the TFM-pinned `bin/Debug` directory before `doctor` or `record`. HPA-515 still does not add Windows version, architecture, or bundled-runtime requirements — only the prebuilt Debug apphost requirement is new.

### macOS

Recording is supported when:

- macOS 13 or later;
- the recorder process is native `arm64`;
- `GameProjectPaths.Current` resolves to the Mac project;
- the project exists;
- the expected Debug output contains executable HPA-512 `ffmpeg` and `ffprobe` under `runtimes/osx-arm64/MMTools`.

macOS 13 remains a hard requirement, not a warning. The HPA-515 capture contract requires CX application audio through OBS ScreenCaptureKit/macOS Audio Capture; OBS documents audio capture for this source path on macOS 13+ while macOS 12.3-12.6 is video-only.

Intel macOS and Rosetta are out of scope.

## Native platform/runtime preflight

Add one internal testable helper, `RecorderPlatformPreflight`, that consumes the resolved target plus injectable host/file facts.

A practical shape is:

```csharp
internal sealed record RecorderPlatformFacts(
    bool IsWindows,
    bool IsMacOS,
    Version OsVersion,
    Architecture ProcessArchitecture);

internal sealed record RecorderPreflightGate(
    string Name,
    bool Passed,
    string Detail);

internal sealed record RecorderPlatformPreflightResult(
    IReadOnlyList<RecorderPreflightGate> Gates,
    string? NativeRuntimeDirectory)
{
    public bool Passed => Gates.All(gate => gate.Passed);
}
```

For Windows, preserve today's supported behavior without a new native-runtime, OS-version, or bundled-runtime gate. Windows does gain one shared gate: the Debug apphost (`<AssemblyName>.exe`) under the TFM-pinned `bin/Debug` directory must exist and be a usable runtime binary, mirroring the Mac apphost gate so a clean checkout cannot pass preflight and then fail at `dotnet run --no-build` launch.

For macOS, validate:

```text
macOS major version >= 13
process architecture == Arm64
resolved project == DTXMania.Game.Mac.csproj
project file exists
<project-dir>/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffmpeg exists + executable
<project-dir>/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffprobe exists + executable
```

The helper must not reference `DTXMania.Game`, call `FfmpegRuntime`, configure FFMpegCore, probe codecs, download binaries, inspect OBS sources, or inspect privacy permissions.

### Bundled-runtime certification policy

`FfmpegRuntime` can fall back to other bundled RIDs or PATH. HPA-515 intentionally applies a stricter recorder certification rule:

> `dtx-video` refuses to certify a native Apple Silicon acceptance run unless the managed HPA-512 `osx-arm64` FFmpeg pair is present and executable.

This is not a claim that CX itself cannot run with PATH FFmpeg. It is a recorder proof requirement so the accepted Mac artifact demonstrates the runtime delivered by HPA-512 rather than an unmanaged local installation.

The recorder-side final-MP4 verifier may continue its existing optional PATH `ffprobe` lookup; that is a separate post-record inspection aid.

## Record ordering is an enforced invariant

Make `Program.RunRecordAsync` internal and pass the already-resolved target plus already-evaluated preflight result into it:

```text
parse/read environment
-> command/path validation
-> ResolveTarget
-> RecorderPlatformPreflight.Evaluate
-> RunRecordAsync(command, environment, target, preflight)
     -> fail immediately if preflight failed
     -> create sandbox
     -> create diagnostics/game/OBS
     -> existing RecordWorkflow
```

`RunRecordAsync` must reject a failed preflight at its first side-effect boundary, before creating the sandbox or OBS client.

This is more robust than relying only on statement order in `Main`: the method signature carries the launch-readiness decision into the side-effecting path, and a unit test can pin the invariant.

## Default source app-data contract

`AppPaths.GetAppDataRoot()` remains the authoritative game behavior. Do **not** make `DTXMania.Game` reference `DTXMania.Automation` solely to share one path helper; that would invert the intended dependency direction for production code.

Keep `DTXMANIA_APPDATA_ROOT` as the explicit override. For the default resolver, mirror the existing `AppPaths` behavior, including its home fallback order:

```text
Windows -> LocalApplicationData/DTXManiaCX
macOS   -> (UserProfile -> Personal -> $HOME)/Library/Application Support/DTXManiaCX
other   -> ApplicationData/DTXManiaCX with the existing home fallback
```

Make the recorder default-root resolver injectable/pure enough that Windows and macOS cases run on every test host. Do not leave the Mac branch dependent on ambient `OperatingSystem.IsMacOS()` or `Environment.GetFolderPath()` in unit tests.

Add comments/tests on the recorder side naming `AppPaths.GetAppDataRoot()` as the contract it mirrors. If a neutral shared runtime project is introduced for other reasons later, this small rule can move there; HPA-515 does not create one.

## `doctor` behavior

Keep doctor read-only with respect to OBS:

```text
Hello
Identify
GetRecordStatus
```

Delete the current hard-coded `Windows` gate. Doctor resolves the shared target and reports the same `RecorderPlatformPreflightResult` through the existing `Program.ReportGate` helper; do not add a second gate printer.

Shared output includes:

- recorder configuration;
- repository root;
- selected project target;
- source Config.ini validation;
- raw OBS output directory;
- OBS auth/status;
- optional recorder-side PATH `ffprobe` warning.

Mac gates include:

- macOS 13+;
- recorder process arm64;
- Mac project target;
- native runtime directory;
- bundled `ffmpeg` executable;
- bundled `ffprobe` executable.

Mac manual guidance remains intentionally non-diagnostic:

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

Add a focused test for doctor platform reporting so a synthetic passing Mac preflight is not rejected merely because the test host is not Windows.

## Recording workflow

After preflight succeeds, the existing sequence stays unchanged:

1. create disposable recorder app-data sandbox;
2. launch the resolved project with recorder-owned `--no-build --configuration Debug`;
3. wait for populated Song Select;
4. prepare the exact chart with preview stopped;
5. start owned OBS recording;
6. play at least 10 seconds of preview;
7. activate the chart;
8. observe Song Transition;
9. complete full AutoPlay Performance;
10. observe completed Result and hold at least 5 seconds;
11. stop only recorder-owned OBS recording;
12. verify/publish the raw artifact;
13. write diagnostics and delete only the successful sandbox.

No Mac-specific stage timing, retry policy, workflow subclass, or diagnostics schema is added.

## OBS / ScreenCaptureKit contract

Keep OBS setup manual:

- OBS Studio 30.2+;
- macOS 13+;
- dedicated DTXManiaCX profile, collection, and scene;
- ScreenCaptureKit application/window capture scoped to CX;
- CX application audio through that source or one dedicated macOS Audio Capture source;
- global Desktop Audio disabled for the recorded track;
- microphone disabled for the recorded track;
- Hybrid MP4;
- authenticated obs-websocket 5.x;
- Screen Recording permission granted manually;
- OBS idle before `record` starts.

HPA-505 remains optional source/audio/privacy diagnostics. HPA-506 remains optional strict media/remux handling.

## Acceptance chart and evidence

Use one short chart already indexed by normal CX Song Select with valid preview audio. Prefer MP3 preview/BGM or another encoded input that exercises HPA-512.

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

`architecture.txt` should show:

- host is Apple Silicon;
- the Debug CX apphost inspected before `record` is arm64;
- bundled `ffmpeg` is arm64;
- bundled `ffprobe` is arm64.

Because record launches with `--no-build`, those inspected Debug artifacts are the artifacts used by the acceptance run.

## Source app-data isolation

Reuse HPA-513 before/after hashes against the resolved normal Mac app-data root:

```text
Config.ini
songs.db
songs.db-wal
songs.db-shm
```

Capture presence, size, and SHA-256. Acceptance requires no content change and no new source WAL/SHM file caused by the recorder.

## Automated telemetry acceptance

Reuse HPA-513 `run.json`. Require at least:

- `status == Completed`;
- selected song present at `SongSelectReady`;
- preview Playing and elapsed >= 10,000 ms;
- Performance ready, AutoPlay enabled, total notes > 0;
- Result completed/cleared by song completion;
- `totalJudgements == totalNotes`;
- OBS Connect/Status/Start/Stop succeeded;
- raw/published paths recorded;
- no failure fields or retained successful sandbox;
- verifier warning absent or only the existing optional PATH-`ffprobe` warning.

## Manual media acceptance

Watch the complete MP4 and confirm:

- populated Song Select is first;
- preview is visibly captured and lasts at least 10 seconds;
- Song Transition is complete;
- full AutoPlay gameplay and BGM/chip audio are present;
- Result is fully rendered and held at least 5 seconds;
- recording ends after Result hold;
- no OBS UI, desktop, unrelated window, cursor, notification, microphone, or unrelated application audio is captured;
- CX audio is not duplicated/echoed;
- no aspect squeeze, severe stutter, or missing viewport region makes the recording unusable;
- CX audio does not require Rosetta or user-installed FFmpeg.

## Testing strategy

### Automation tests

Extend `GameProcessDriverTests` for the additive project-run-argument behavior:

- existing project launch with no extra arguments still builds/runs as today;
- build a child Debug project, then make its source uncompilable; launching with `--no-build --configuration Debug` still runs the already-built child, proving the driver honored no-build;
- project-run arguments on an executable target are rejected.

### VideoRecorder tests

Cover:

- shared target resolution from repository root and nested directories;
- recorder `CreateOptions` preserves the target/working directory and adds `--no-build --configuration Debug`, sandbox app-data, and a fresh launch token;
- both Windows and Mac default app-data resolution through injected platform/folder facts, including the `UserProfile -> Personal -> $HOME` Mac fallback;
- Windows record behavior remains accepted, with the new shared Debug apphost gate covered (passing apphost, missing apphost with Windows recovery command, and missing project / unreadable TFM falling back to the "Target framework" gate);
- Mac host version/architecture gates from synthetic facts;
- exact Debug runtime path resolution;
- missing/non-executable native runtime fails with the exact build command in the message;
- `RunRecordAsync` with a failed preflight creates no sandbox and cannot initialize OBS;
- doctor platform reporting accepts a passing synthetic Mac result and has no separate Windows-only gate.

Do not create a second Mac `RecordWorkflow` matrix.

No CI edit is planned initially; existing Windows/macOS jobs already run Automation and VideoRecorder tests.

## Native proof

On the Apple Silicon workstation:

1. capture host/.NET versions;
2. run `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug`;
3. use `file` on the Debug apphost and bundled `ffmpeg`/`ffprobe`;
4. run the existing focused HPA-512 audio tests;
5. run `dtx-video doctor` and confirm all automated gates;
6. configure OBS manually;
7. capture source app-data hashes;
8. run one successful `record` acceptance journey;
9. repeat source hashes;
10. run one Ctrl+C case after recorder-owned OBS start;
11. inspect the complete published MP4.

## Documentation outputs

The implementation/acceptance PR adds:

```text
docs/video-recorder/macos-obs-setup.md
docs/verification/hpa-515-apple-silicon-live-recording.md
```

The runbook covers Apple Silicon/macOS prerequisites, the required Debug build, ScreenCaptureKit/application-audio setup, Screen Recording permission, OBS/WebSocket environment, doctor/record commands, runtime-gate failure recovery, artifacts, and concise troubleshooting.

The verification record captures the accepted commit SHA, host/.NET/OBS versions, chart identity, Debug apphost/runtime architecture evidence, doctor result, sanitized record command, MP4 metadata/hash, key `run.json` values, source before/after comparison, Ctrl+C result, manual media checklist, focused automated tests, and deviations.

## Risks

### Build and launch must stay pinned

The recorder now intentionally checks Debug output and launches it with `--no-build --configuration Debug`. If launch configuration changes, preflight and recorder launch policy must change in the same patch.

### A stale prebuild is possible by operator choice

`--no-build` guarantees the checked output is the launched output, but it does not prove source files have not changed since the operator ran `dotnet build`. The native acceptance procedure therefore records the commit SHA and performs the build immediately before architecture/doctor/record evidence.

Do not add source-hash/build-manifest machinery for HPA-515.

### Manual OBS/privacy setup may fail before code does

Hosted CI cannot prove ScreenCaptureKit selection, application-audio routing, or Screen Recording permission. Treat these as operator setup failures unless code evidence proves otherwise.

### Bundled runtime certification is intentionally stricter than CX runtime fallback

The game may fall back to another bundled RID or PATH. The recorder intentionally refuses to certify that as HPA-515 native Apple Silicon evidence.

### App-data rule is mirrored, not shared

`AppPaths` remains authoritative and the recorder mirrors its small default-root rule to avoid a Game -> Automation production dependency. Keep the mirrored code/test comment explicit; if this rule grows, move it to a neutral shared runtime component rather than Automation.

## Out of scope

- packaged executable/app-bundle recording;
- app-bundle discovery or DMG mounting;
- Intel Mac or Rosetta support;
- accepting unmanaged PATH FFmpeg as HPA-515 certification;
- automated ScreenCaptureKit source/audio/privacy diagnosis;
- strict codec/frame-rate/duration enforcement;
- MKV fallback/remux/transcoding;
- YouTube upload/editing;
- platform adapter framework;
- changing default Automation/E2E build-on-run behavior;
- a new shared runtime project solely for app-data path resolution.

## Implementation slice

One 2–3 engineer-day implementation/acceptance PR remains sufficient:

1. pin recorder launch to a prebuilt Debug project and share target resolution;
2. add Mac preflight, testable app-data resolution, enforced preflight ordering, and doctor parity;
3. native Apple Silicon build/audio proof before OBS;
4. one real ScreenCaptureKit recording + cleanup/isolation/media acceptance;
5. Mac runbook + sanitized verification record.

## Acceptance criteria

HPA-515 is complete when:

- unchanged recorder CLI works on native Apple Silicon macOS 13+;
- record and doctor resolve the same Mac project target;
- recorder launches the same prebuilt Debug artifact that preflight certified;
- missing/unusable native runtime fails before sandbox/OBS with the exact build recovery command;
- failed preflight ordering is covered by an automated no-sandbox test;
- doctor no longer fails merely because the host is macOS;
- default Mac source app-data resolution matches the `AppPaths` contract and is host-independently unit tested;
- one encoded-audio chart completes Song Select -> preview -> transition -> AutoPlay -> Result;
- one inspected Hybrid MP4 and sanitized evidence bundle are retained;
- normal CX app data remains unchanged;
- Ctrl+C stops only recorder-owned OBS work and retains useful evidence;
- Windows/default Automation behavior remains unchanged;
- no platform adapter, packaged-executable mode, HPA-505, or HPA-506 scope is introduced.
