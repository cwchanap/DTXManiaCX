# HPA-515 Apple Silicon Recorder Parity Implementation Plan

> **For agentic workers:** use `superpowers:subagent-driven-development` or `superpowers:executing-plans`. Keep HPA-515 as Mac parity for the existing recorder. Do not add a second workflow, platform adapter, or packaged-app mode.

**Goal:** Make the existing `dtx-video` project-mode workflow run natively on Apple Silicon macOS, certify and launch the same prebuilt Debug artifact, fail unsupported/missing-runtime runs before sandbox/OBS side effects, and retain one inspected ScreenCaptureKit recording plus concise proof documentation.

**Architecture:** Preserve `RecordWorkflow`, OBS ownership, sandboxing, diagnostics, and finalization. Add one additive project-run-argument seam to Automation so the recorder can launch `--no-build --configuration Debug`; resolve the current-platform target once; evaluate one Mac preflight against that exact Debug output; pass the result into the side-effecting record method; and reuse the same result in doctor.

**Expected effort:** 2–3 engineer days including one real Apple Silicon/OBS acceptance run.

## Global constraints

- Keep `dtx-video doctor` and `dtx-video record --chart ... --output ...` unchanged.
- Project mode only: Windows project on Windows, Mac project on macOS.
- Keep one `RecordWorkflow` and one `ObsWebSocketRecorder`.
- Mac certification is macOS 13+ and native arm64 only.
- The recorder must launch the exact prebuilt Debug artifact that preflight checked.
- Do not silently build inside the recorder launch path.
- Run failed-preflight rejection before sandbox creation or OBS client construction.
- Keep `DTXMANIA_APPDATA_ROOT` as the explicit source-app-data override.
- Mirror the authoritative `AppPaths.GetAppDataRoot()` default behavior without adding a production `DTXMania.Game -> DTXMania.Automation` dependency.
- Require HPA-512 `osx-arm64` bundled `ffmpeg` + `ffprobe` for recorder certification even though CX itself can fall back to other RIDs/PATH.
- Keep recorder-side final-MP4 PATH `ffprobe` optional as today.
- Keep ScreenCaptureKit source/audio/privacy configuration manual; HPA-505 owns automatic diagnostics.
- Keep strict codec/remux policy deferred to HPA-506.
- Do not edit CI unless existing jobs prove they skip the new tests.

## Planned files

```text
Modify:
  DTXMania.Automation/Process/GameProcessStartOptions.cs
  DTXMania.Automation/Process/GameProcessDriver.cs
  DTXMania.Automation.Tests/Process/GameProcessDriverTests.cs

  DTXMania.VideoRecorder/RecorderCommandLine.cs
  DTXMania.VideoRecorder/Workflow/RecorderGameLaunchPolicy.cs
  DTXMania.VideoRecorder/Program.cs

Create:
  DTXMania.VideoRecorder/Diagnostics/RecorderPlatformPreflight.cs
  DTXMania.VideoRecorder.Tests/RecorderCommandLineTests.cs
  DTXMania.VideoRecorder.Tests/Workflow/RecorderGameLaunchPolicyTests.cs
  DTXMania.VideoRecorder.Tests/Diagnostics/RecorderPlatformPreflightTests.cs
  DTXMania.VideoRecorder.Tests/ProgramTests.cs

  docs/video-recorder/macos-obs-setup.md
  docs/verification/hpa-515-apple-silicon-live-recording.md

Normally unchanged:
  DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs
  DTXMania.VideoRecorder/Obs/**
  DTXMania.VideoRecorder/Media/**
  DTXMania.VideoRecorder/Sandbox/**
  DTXMania.Game/Lib/Resources/FfmpegRuntime.cs
  DTXMania.Game/Lib/Utilities/AppPaths.cs
  DTXMania.Game/DTXMania.Game.Mac.csproj
  .github/workflows/**
```

## Risks to keep visible

- Preflight and launch are one contract: Debug + no-build. Do not change one without the other.
- `--no-build` pins checked output to launched output, but does not prove source did not change after the manual build. The acceptance procedure builds immediately before evidence capture and records the commit SHA; do not invent a build-manifest system.
- Mac OBS/source/privacy failures may be operator setup rather than recorder defects.
- The recorder app-data default mirrors `AppPaths`; keep the link explicit and tests host-independent. Do not solve one small rule by making the game depend on Automation.
- macOS 13 is intentional: the accepted OBS application-audio path requires macOS 13+.

---

## Task 1: Launch the exact prebuilt Debug artifact

**Scope:** small additive Automation seam + recorder launch policy. No workflow changes.

**Files:**
- Modify: `DTXMania.Automation/Process/GameProcessStartOptions.cs`
- Modify: `DTXMania.Automation/Process/GameProcessDriver.cs`
- Modify: `DTXMania.Automation.Tests/Process/GameProcessDriverTests.cs`
- Modify: `DTXMania.VideoRecorder/Workflow/RecorderGameLaunchPolicy.cs`
- Create: `DTXMania.VideoRecorder.Tests/Workflow/RecorderGameLaunchPolicyTests.cs`

### 1.1 Add failing Automation coverage for project-run arguments

Extend `GameProcessStartOptions` conceptually with one optional project-only list:

```csharp
IReadOnlyList<string>? ProjectRunArguments = null
```

Add a test that:

1. creates the existing child fixture;
2. builds it in Debug;
3. changes the source so a rebuild would fail;
4. launches it through `GameProcessDriver` with:

```text
--no-build
--configuration
Debug
```

5. asserts the previously built child still runs and returns the existing expected output/exit code.

This is the key proof that `GameProcessDriver` actually honors no-build instead of silently rebuilding.

Also add a small validation test that project-run arguments supplied to an executable target are rejected rather than ignored.

Keep the existing default project-launch test unchanged; omission of the new field must preserve current build-on-run behavior for E2E/other callers.

### 1.2 Implement the additive Automation seam

For project targets only, append `ProjectRunArguments` to the existing command after:

```text
dotnet run --project <path>
```

Do not change process ownership, environment, readiness, cancellation, stdout/stderr, or executable-target behavior.

### 1.3 Add shared recorder target resolution

`RecorderGameLaunchPolicy` owns:

```csharp
internal sealed record ResolvedRecorderTarget(
    string RepositoryRoot,
    string WorkingDirectory,
    GameLaunchTarget Target);

internal static ResolvedRecorderTarget ResolveTarget(string startDirectory);

internal static GameProcessStartOptions CreateOptions(
    RecordingSandbox sandbox,
    ResolvedRecorderTarget target);
```

`ResolveTarget`:

- reuses the existing repository-root walk;
- selects `GameProjectPaths.Current`;
- resolves the absolute project path;
- uses repository root as working directory.

`CreateOptions` adds only:

- sandbox app-data root;
- fresh launch token;
- recorder-owned project args:

```text
--no-build --configuration Debug
```

Do not add executable/app-bundle launch support.

### 1.4 Recorder launch-policy tests

Cover:

```text
ResolveTarget_FromRepositoryRoot
ResolveTarget_FromNestedDirectory
CreateOptions_PreservesResolvedTarget
CreateOptions_AddsFreshLaunchToken
CreateOptions_UsesNoBuildDebugArguments
```

Use a temporary fake repo containing the solution marker and `GameProjectPaths.Current` project file.

### 1.5 Run focused tests

```bash
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~GameProcessDriverTests"

dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderGameLaunchPolicyTests"
```

### 1.6 Commit checkpoint

```bash
git add \
  DTXMania.Automation/Process/GameProcessStartOptions.cs \
  DTXMania.Automation/Process/GameProcessDriver.cs \
  DTXMania.Automation.Tests/Process/GameProcessDriverTests.cs \
  DTXMania.VideoRecorder/Workflow/RecorderGameLaunchPolicy.cs \
  DTXMania.VideoRecorder.Tests/Workflow/RecorderGameLaunchPolicyTests.cs

git commit -m "feat: pin recorder project launch to prebuilt output"
```

---

## Task 2: Make command/platform/app-data inputs testable

**Scope:** widen recorder host support and pin the mirrored app-data contract. No OBS work.

**Files:**
- Modify: `DTXMania.VideoRecorder/RecorderCommandLine.cs`
- Create: `DTXMania.VideoRecorder.Tests/RecorderCommandLineTests.cs`

### 2.1 Add command-line coverage before changing behavior

Cover existing behavior first:

- required record arguments;
- absolute/existing chart;
- writable publish/OBS directories;
- loopback OBS URL;
- `DTXMANIA_APPDATA_ROOT` override.

Then add platform/default-root cases.

### 2.2 Widen only the basic record OS gate

`ValidateRecord` should allow Windows or macOS and continue rejecting unsupported OSes.

Do not put version, architecture, project-output, FFmpeg, or OBS source checks into command parsing.

### 2.3 Make default app-data resolution injectable

Keep the production wrapper simple, but expose an internal pure/testable resolver that accepts enough facts to avoid ambient-host tests, for example:

```text
isWindows
isMacOS
folder-path lookup
HOME environment lookup
```

Mirror `AppPaths.GetAppDataRoot()` default behavior:

```text
Windows:
  LocalApplicationData/DTXManiaCX

macOS:
  UserProfile
  -> Personal fallback
  -> $HOME fallback
  -> Library/Application Support/DTXManiaCX

Other:
  ApplicationData/DTXManiaCX
  -> existing home fallback when unusable
```

Keep a code comment naming `DTXMania.Game/Lib/Utilities/AppPaths.cs` as the authoritative contract being mirrored.

Do **not** add a `DTXMania.Game -> DTXMania.Automation` project reference and do not create a shared assembly for this one helper.

### 2.4 Host-independent default-root tests

Run both Windows and Mac cases on every host by injecting facts/folder values. Include the Mac fallback chain:

```text
UserProfile available
UserProfile empty -> Personal
UserProfile + Personal empty -> HOME
```

Do not rely on “macOS CI will cover this branch” as the only test.

### 2.5 Run focused tests

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderCommandLineTests"
```

### 2.6 Commit checkpoint

```bash
git add \
  DTXMania.VideoRecorder/RecorderCommandLine.cs \
  DTXMania.VideoRecorder.Tests/RecorderCommandLineTests.cs

git commit -m "feat: allow recorder commands on macOS"
```

---

## Task 3: Add the shared preflight and enforce pre-OBS ordering

**Scope:** one preflight result used by record and doctor; one real ordering test.

**Files:**
- Create: `DTXMania.VideoRecorder/Diagnostics/RecorderPlatformPreflight.cs`
- Modify: `DTXMania.VideoRecorder/Program.cs`
- Create: `DTXMania.VideoRecorder.Tests/Diagnostics/RecorderPlatformPreflightTests.cs`
- Create: `DTXMania.VideoRecorder.Tests/ProgramTests.cs`

### 3.1 Define a small testable preflight

Keep the helper internal. A practical shape is:

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

Exact names may follow repository style.

### 3.2 Preflight behavior

Windows:

- no new native-runtime/version/architecture rejection.

macOS hard gates:

```text
macOS >= 13
ProcessArchitecture == Arm64
resolved target is DTXMania.Game.Mac.csproj
project exists
<project-dir>/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffmpeg exists + executable
<project-dir>/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffprobe exists + executable
```

The failure for missing/unusable Debug runtime must name the recovery command:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
```

The bundled-runtime hard failure is intentional recorder certification policy: a working PATH FFmpeg is not accepted as HPA-515 evidence.

Do not call `FfmpegRuntime`, search PATH for CX audio runtime, probe codecs, download binaries, inspect OBS sources, or inspect macOS privacy state.

### 3.3 Preflight unit tests

Using synthetic facts/temp files, cover on every host:

```text
Windows -> no new platform/runtime failure
macOS 12 arm64 -> version failure
macOS 13 x64 -> arm64 failure
macOS 13 arm64 + wrong project -> target failure
macOS 13 arm64 + missing ffmpeg -> runtime failure + build command
macOS 13 arm64 + missing ffprobe -> runtime failure + build command
macOS 13 arm64 + non-executable pair -> runtime failure
macOS 13 arm64 + executable pair -> pass
```

Use `File.SetUnixFileMode` where available for executable-bit cases.

### 3.4 Enforce the record ordering in the method signature

Make `Program.RunRecordAsync` internal and pass:

```text
command
environment
ResolvedRecorderTarget
RecorderPlatformPreflightResult
```

At the very top of the side-effecting path, reject a failed preflight before:

- `RecordingSandbox.Create`;
- diagnostics run-root creation;
- game control construction;
- OBS client construction;
- `RecordWorkflow` creation.

`Main` ordering becomes:

```text
Parse
ReadEnvironment
Validate
ResolveTarget
Evaluate preflight
RunRecordAsync(...target, preflight...)
```

Do not hide this check inside `RecordWorkflow`.

### 3.5 Add the ordering regression test

Add one `ProgramTests` case that calls internal `RunRecordAsync` with a failing preflight and a temporary source-app-data root.

Assert:

- actionable preflight exception is returned;
- no recorder sandbox/run directory is created under the temp root;
- the method returns before any OBS connection can be attempted.

The test should not require an OBS server. Its ability to complete without OBS is part of the invariant.

### 3.6 Rework doctor to use the same target/result

Delete doctor's duplicate repository-root/project reconstruction and the hard-coded Windows gate.

Doctor should:

1. resolve `ResolvedRecorderTarget` without a sandbox;
2. evaluate the same `RecorderPlatformPreflight`;
3. report each gate through the existing `Program.ReportGate` helper;
4. continue the existing source config/raw-output/OBS auth-status checks;
5. remain read-only: Hello + Identify + GetRecordStatus only.

Do not add a second gate-printer abstraction.

### 3.7 Pin doctor Mac parity

Factor only the platform/preflight reporting portion enough to test it without a live OBS server.

Add one test with a synthetic passing Mac preflight asserting that platform reporting succeeds and no separate Windows-only gate remains.

This is not a full doctor integration harness; keep it small.

### 3.8 Platform-specific doctor guidance

Windows retains existing Game Capture/application-audio wording.

macOS prints only:

```text
Dedicated profile/collection/scene selected
ScreenCaptureKit application/window capture scoped to CX
CX application audio configured
Desktop Audio disabled
Microphone disabled
Hybrid MP4 configured
Screen Recording permission granted manually
WebSocket enabled/authenticated
Raw output directory matches DTXMANIA_VIDEO_OBS_OUTPUT_DIR
```

Do not claim source/privacy state was programmatically verified.

### 3.9 Run focused + full tests

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderPlatformPreflightTests|FullyQualifiedName~ProgramTests|FullyQualifiedName~RecorderGameLaunchPolicyTests|FullyQualifiedName~RecorderCommandLineTests"

dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug
```

Do not edit CI unless the PR run shows the existing OS matrix skipped the new tests.

### 3.10 Commit checkpoint

```bash
git add \
  DTXMania.VideoRecorder/Diagnostics/RecorderPlatformPreflight.cs \
  DTXMania.VideoRecorder/Program.cs \
  DTXMania.VideoRecorder.Tests/Diagnostics/RecorderPlatformPreflightTests.cs \
  DTXMania.VideoRecorder.Tests/ProgramTests.cs

git commit -m "feat: preflight native Mac recorder before OBS"
```

---

## Task 4: Prove the native Debug target before OBS

**Scope:** real Apple Silicon validation of the exact no-build artifact. No product changes unless a directly related defect is proven.

### 4.1 Record proof host facts

```bash
uname -m
sw_vers -productVersion
dotnet --info
git rev-parse HEAD
```

Require native `arm64` and macOS 13+.

### 4.2 Build the exact configuration the recorder will launch

Immediately before architecture/doctor/record evidence:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
```

Do not use publish/DMG/app-bundle mode for HPA-515.

### 4.3 Inspect the exact Debug artifacts

```bash
file DTXMania.Game/bin/Debug/net8.0/DTXMania.Game.Mac
file DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffmpeg
file DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffprobe
```

Retain sanitized output in `architecture.txt`.

The subsequent recorder launch uses `--no-build`, so these are the artifacts it will run.

### 4.4 Reuse the existing HPA-512 audio proof

Run the same focused native audio tests already used by macOS CI. Do not invent a new recorder-only FFmpeg test.

### 4.5 Run recorder/Automation suites on the Mac

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug
```

### 4.6 Run doctor before configuring a record

Run `dtx-video doctor` and retain `doctor.txt`.

Require automated gates for:

- Mac host/version;
- arm64 process;
- Mac project;
- Debug native runtime pair;
- source config/output path;
- OBS auth/status.

Manual OBS source/privacy statements remain guidance, not automated proof.

---

## Task 5: Perform one real recording and retain acceptance evidence

**Scope:** one successful ScreenCaptureKit journey, one cancellation check, and two docs.

**Files:**
- Create: `docs/video-recorder/macos-obs-setup.md`
- Create: `docs/verification/hpa-515-apple-silicon-live-recording.md`

### 5.1 Configure OBS manually

Use:

- OBS Studio 30.2+;
- dedicated DTXManiaCX profile/collection/scene;
- ScreenCaptureKit application/window capture scoped to CX;
- CX application audio through the source or one dedicated macOS Audio Capture source;
- Desktop Audio disabled for the recorded track;
- microphone disabled for the recorded track;
- Hybrid MP4;
- authenticated obs-websocket 5.x;
- Screen Recording permission granted manually;
- OBS idle before recorder start.

Do not add source discovery/auto-repair code if setup is wrong.

### 5.2 Select one acceptance chart

Use a short chart already indexed by normal Song Select with preview audio. Prefer MP3/another encoded source that exercises HPA-512.

Do not create a recorder-only chart.

### 5.3 Capture source app-data state before record

Against the resolved normal Mac root, capture presence, size, and SHA-256 for:

```text
Config.ini
songs.db
songs.db-wal
songs.db-shm
```

Store sanitized evidence outside Git.

### 5.4 Run the successful acceptance journey

```text
Song Select populated
-> preview stopped/prepared
-> OBS recorder-owned start
-> >= 10 seconds preview audio
-> Song Transition
-> full AutoPlay Performance
-> completed Result
-> >= 5 seconds Result hold
-> recorder-owned OBS stop
-> verify/publish
```

Because Task 4 built immediately beforehand and launch uses `--no-build`, do not run another implicit build between architecture evidence and record.

### 5.5 Validate `run.json`

Require at least:

- `status == Completed`;
- selected song present at `SongSelectReady`;
- preview Playing and elapsed >= 10,000 ms;
- Performance ready, AutoPlay enabled, total notes > 0;
- Result completed/cleared;
- `totalJudgements == totalNotes`;
- OBS Connect/Status/Start/Stop succeeded;
- raw/published paths present;
- no failure fields/retained successful sandbox;
- verifier warning absent or only the existing optional PATH-`ffprobe` warning.

### 5.6 Capture source app-data state after record

Repeat the same hashes. Acceptance requires no content change and no new WAL/SHM caused by the recorder.

### 5.7 Run one Ctrl+C cleanup case

Cancel only after recorder-owned OBS recording has started.

Confirm:

- recorder-owned OBS work stops;
- diagnostics/raw evidence remain;
- sandbox remains for failure diagnosis;
- unrelated/pre-existing OBS ownership is not stopped.

Do not manufacture privacy failures or revoke permissions.

### 5.8 Watch the complete published MP4

Confirm:

- populated Song Select is first;
- preview audio >= 10 seconds;
- complete transition;
- full AutoPlay gameplay;
- BGM/chip audio audible;
- Result fully rendered >= 5 seconds;
- recording ends after Result hold;
- no OBS UI/desktop/unrelated windows/cursor/notification/mic/unrelated audio;
- no duplicated/echoed CX audio;
- no unusable aspect squeeze/stutter/missing viewport;
- no Rosetta or user-installed FFmpeg needed for CX audio.

Strict codec/frame-rate/duration enforcement remains HPA-506.

### 5.9 Write the Mac setup runbook

`docs/video-recorder/macos-obs-setup.md` should cover only:

- Apple Silicon + macOS 13+ prerequisite;
- required Debug build command;
- why recorder uses no-build after doctor/preflight;
- ScreenCaptureKit/application-audio setup;
- Screen Recording permission;
- disabled Desktop Audio/microphone;
- WebSocket environment variables;
- doctor/record commands;
- missing-runtime error and exact rebuild command;
- artifact locations;
- concise troubleshooting.

Do not document packaged-app mode or PATH FFmpeg as accepted certification.

### 5.10 Write sanitized verification record

`docs/verification/hpa-515-apple-silicon-live-recording.md` should include:

- accepted commit SHA;
- Mac model/macOS/architecture;
- .NET + OBS versions;
- chart identity without private local path;
- Debug build command/result;
- apphost/ffmpeg/ffprobe arm64 evidence;
- doctor result;
- sanitized record command;
- raw/published MP4 name, size, SHA-256;
- key `run.json` values;
- source before/after comparison;
- Ctrl+C result;
- manual media checklist;
- focused/full automated test results;
- warnings/deviations.

### 5.11 Final verification

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug
git diff --check
```

Review the diff and confirm there is no scope creep into HPA-505/HPA-506, packaged launch, workflow subclasses, or CI unless evidence required it.

### 5.12 Commit checkpoint

```bash
git add \
  docs/video-recorder/macos-obs-setup.md \
  docs/verification/hpa-515-apple-silicon-live-recording.md

git commit -m "docs: verify Apple Silicon recorder parity"
```

---

## Completion criteria

HPA-515 is complete when all are true:

- unchanged recorder CLI works on native Apple Silicon macOS 13+;
- record/doctor resolve the same Mac project;
- Automation supports optional project-run arguments without changing default callers;
- recorder launch is `--no-build --configuration Debug`;
- preflight checks that exact Debug runtime and names the exact build recovery command;
- failed preflight is automatically proven to create no sandbox and require no OBS server;
- doctor has no Windows-only hard gate and a synthetic Mac pass case is tested;
- recorder default app-data resolution matches the `AppPaths` contract and both OS branches are host-independently tested;
- one native encoded-audio acceptance recording succeeds;
- source app data is unchanged;
- Ctrl+C ownership/retention behavior is proven;
- complete MP4 is manually accepted;
- Mac runbook + sanitized verification record are committed;
- Windows/default Automation behavior remains unchanged;
- no platform adapter, packaged-app mode, HPA-505, or HPA-506 scope is introduced.
