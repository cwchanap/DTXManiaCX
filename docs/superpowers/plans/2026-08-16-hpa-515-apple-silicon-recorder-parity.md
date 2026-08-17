# HPA-515 Apple Silicon Recorder Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. HPA-515 is Mac parity for the existing recorder. Keep one project-mode launch path and do not expand into packaged-app support.

**Goal:** Make the existing `dtx-video` project-mode workflow run natively on Apple Silicon macOS, fail unsupported/missing-runtime Mac runs before OBS starts, prove one real ScreenCaptureKit recording with bundled arm64 FFmpeg audio, and retain a concise Mac setup guide plus sanitized verification record.

**Architecture:** Preserve `RecordWorkflow`, OBS ownership, sandboxing, diagnostics, finalization, and `GameProcessDriver`. Extract one sandbox-free project-target resolver from `RecorderGameLaunchPolicy`, add one focused `RecorderPlatformPreflight` consumed by both record and doctor, and make the default Mac app-data path explicitly match CX. No platform adapter and no second launch mode.

**Tech Stack:** .NET 8, MonoGame Mac project, `DTXMania.Automation`, obs-websocket 5.x, OBS Studio 30.2+, ScreenCaptureKit, existing HPA-512 `osx-arm64` FFmpeg runtime.

**Expected effort:** 2–3 engineer days including one real Apple Silicon/OBS acceptance run.

## Global Constraints

- Keep `dtx-video doctor` and `dtx-video record --chart ... --output ...` unchanged.
- HPA-515 supports project mode only: Windows project on Windows, Mac project on macOS.
- Keep existing Windows behavior unchanged.
- Mac support is macOS 13+ on native arm64 only; no Intel/Rosetta fallback.
- Run the Mac host/runtime preflight before creating the recorder sandbox or touching OBS.
- Reuse the same `RecordWorkflow`; no Mac workflow/session subclass.
- Reuse `GameProcessDriver`; do not change its process ownership/readiness behavior.
- Reuse `ObsWebSocketRecorder`; do not add ScreenCaptureKit source discovery or privacy diagnosis.
- Reuse HPA-512's bundled `runtimes/osx-arm64/MMTools/{ffmpeg,ffprobe}`; no second runtime resolver/download/PATH fallback for CX audio.
- Recorder-side final-MP4 `ffprobe` remains optional PATH-based as today.
- Keep successful-run disposable app-data behavior unchanged.
- Keep `DTXMANIA_APPDATA_ROOT` as the explicit source-app-data override.
- Do not add strict media/remux/transcoding policy; HPA-506 owns that.
- Do not add automated Mac OBS source/audio/privacy diagnostics; HPA-505 owns that.
- Do not edit CI unless the existing Windows/macOS recorder jobs prove they do not execute the new tests.

## File Structure

```text
Modify:
  DTXMania.VideoRecorder/RecorderCommandLine.cs
  DTXMania.VideoRecorder/Workflow/RecorderGameLaunchPolicy.cs
  DTXMania.VideoRecorder/Program.cs

Create:
  DTXMania.VideoRecorder/Diagnostics/RecorderPlatformPreflight.cs
  DTXMania.VideoRecorder.Tests/RecorderCommandLineTests.cs
  DTXMania.VideoRecorder.Tests/Workflow/RecorderGameLaunchPolicyTests.cs
  DTXMania.VideoRecorder.Tests/Diagnostics/RecorderPlatformPreflightTests.cs
  docs/video-recorder/macos-obs-setup.md
  docs/verification/hpa-515-apple-silicon-live-recording.md

Normally unchanged:
  DTXMania.VideoRecorder/Workflow/RecordWorkflow.cs
  DTXMania.VideoRecorder/Obs/**
  DTXMania.VideoRecorder/Media/**
  DTXMania.VideoRecorder/Sandbox/**
  DTXMania.Automation/Process/GameProcessDriver.cs
  DTXMania.Game/Lib/Resources/FfmpegRuntime.cs
  DTXMania.Game/DTXMania.Game.Mac.csproj
  .github/workflows/**
```

## Risks to keep visible during execution

- A native proof failure may be manual OBS source/audio/privacy setup, not a code defect. Do not change recorder code without evidence.
- The runtime preflight intentionally targets `bin/Debug/net8.0` because `GameProcessDriver` launches `dotnet run --project` without `--configuration`. If that launch contract changes, update launch and preflight together.
- Missing bundled FFmpeg can look like a capture/audio failure after Song Select. The pre-record gate must fail before OBS to remove that ambiguity.
- Source-isolation evidence is invalid if the recorder hashes a different app-data root from CX. The explicit Mac default path is part of this task.

---

### Task 1: Share project-target resolution and align the Mac app-data default

**Files:**
- Modify: `DTXMania.VideoRecorder/Workflow/RecorderGameLaunchPolicy.cs`
- Modify: `DTXMania.VideoRecorder/RecorderCommandLine.cs`
- Modify: `DTXMania.VideoRecorder/Program.cs`
- Create: `DTXMania.VideoRecorder.Tests/Workflow/RecorderGameLaunchPolicyTests.cs`
- Create: `DTXMania.VideoRecorder.Tests/RecorderCommandLineTests.cs`

**Interfaces:**

`RecorderGameLaunchPolicy` should own one resolved target shape, kept internal to VideoRecorder:

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

`ResolveTarget` uses `GameProjectPaths.Current`, resolves the absolute project path under the repository root, and uses the repository root as working directory. `CreateOptions` only adds sandbox app-data plus a fresh launch token.

- [ ] **Step 1: Add failing launch-policy tests.**

Cover:

```text
ResolveTarget_FromRepositoryRoot
  -> RepositoryRoot == repo root
  -> WorkingDirectory == repo root
  -> Target.Kind == Project
  -> Target.Path == <repo>/<GameProjectPaths.Current>

ResolveTarget_FromNestedDirectory
  -> same target/root as repository root

CreateOptions
  -> preserves resolved working directory/target
  -> AppDataRoot == sandbox.AppDataRoot
  -> LaunchToken is non-empty and fresh across calls
```

Use a temporary fake repo containing `DTXMania.sln` plus the current-platform project path. Do not test executable launch behavior.

- [ ] **Step 2: Run the launch-policy tests and verify they fail before implementation.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderGameLaunchPolicyTests"
```

Expected: FAIL because `ResolvedRecorderTarget` / `ResolveTarget` do not exist.

- [ ] **Step 3: Implement the minimal shared target resolver.**

Replace the Windows-project constant inside `CreateOptions` with `ResolveTarget` + `GameProjectPaths.Current`.

Keep the repository walk already owned by `RecorderGameLaunchPolicy`. Do not move it to Automation and do not add a platform abstraction.

- [ ] **Step 4: Update record wiring to resolve the target before sandbox creation.**

In the record path, resolve the project target once and pass it forward. `RunRecordAsync` should no longer discover a project independently after creating the sandbox.

The next task inserts preflight between target resolution and `RunRecordAsync`.

- [ ] **Step 5: Add failing Mac default-app-data coverage.**

Use the existing injectable environment-variable reader. On the macOS test job, with `DTXMANIA_APPDATA_ROOT` unset, assert the resolved source root equals:

```text
$HOME/Library/Application Support/DTXManiaCX
```

Also retain coverage that an explicit `DTXMANIA_APPDATA_ROOT` wins on every platform.

- [ ] **Step 6: Run the focused command-line tests and verify the new Mac case fails before implementation.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderCommandLineTests"
```

The Mac-specific assertion is expected to be exercised by existing macOS CI.

- [ ] **Step 7: Make the Mac default explicit.**

`GetDefaultSourceAppDataRoot()` should use this branch before `SpecialFolder.LocalApplicationData`:

```csharp
if (OperatingSystem.IsMacOS())
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (string.IsNullOrWhiteSpace(home))
        throw new InvalidOperationException("Unable to determine the CX app-data root.");

    return Path.Combine(home, "Library", "Application Support", "DTXManiaCX");
}
```

Keep the Windows/default branch behavior unchanged.

- [ ] **Step 8: Widen only the basic record host gate.**

`RecorderCommandLine.ValidateRecord` should stop rejecting macOS solely because it is not Windows. It should allow Windows or macOS and continue rejecting unsupported OSes.

Do not put macOS version, process architecture, or Debug-runtime layout checks in the command parser; Task 2 validates the resolved launch target before recorder startup.

- [ ] **Step 9: Run the focused tests.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderCommandLineTests|FullyQualifiedName~RecorderGameLaunchPolicyTests"
```

Require all focused tests to pass on the current host. macOS CI remains the authoritative Mac-default-path execution.

- [ ] **Step 10: Commit Task 1.**

```bash
git add \
  DTXMania.VideoRecorder/RecorderCommandLine.cs \
  DTXMania.VideoRecorder/Workflow/RecorderGameLaunchPolicy.cs \
  DTXMania.VideoRecorder/Program.cs \
  DTXMania.VideoRecorder.Tests/RecorderCommandLineTests.cs \
  DTXMania.VideoRecorder.Tests/Workflow/RecorderGameLaunchPolicyTests.cs

git commit -m "feat: resolve recorder project target by platform"
```

---

### Task 2: Add one shared Mac preflight used by record and doctor

**Files:**
- Create: `DTXMania.VideoRecorder/Diagnostics/RecorderPlatformPreflight.cs`
- Modify: `DTXMania.VideoRecorder/Program.cs`
- Create: `DTXMania.VideoRecorder.Tests/Diagnostics/RecorderPlatformPreflightTests.cs`

**Interfaces:**

Keep the preflight internal and small. A practical shape is:

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

internal static RecorderPlatformPreflightResult Evaluate(
    ResolvedRecorderTarget target,
    RecorderPlatformFacts facts);

internal static RecorderPlatformFacts CaptureCurrentFacts();
```

Exact names may be adjusted to repository style, but retain one result consumed by both doctor and record. The helper owns target-relative Debug-runtime resolution so `Program` never rebuilds the path itself.

- [ ] **Step 1: Add failing pure/temp-filesystem preflight tests.**

Cover:

```text
Windows
  -> no new runtime/architecture/version rejection

macOS 12 arm64
  -> fail supported-version gate

macOS 13 x64
  -> fail native-arm64 gate

macOS 13 arm64 + Mac project + missing ffmpeg
  -> fail with ffmpeg path/detail

macOS 13 arm64 + Mac project + missing ffprobe
  -> fail with ffprobe path/detail

macOS 13 arm64 + both files not executable
  -> fail executable gate

macOS 13 arm64 + both executable
  -> pass
  -> NativeRuntimeDirectory == <Mac-project-dir>/bin/Debug/net8.0/runtimes/osx-arm64/MMTools
```

Use a temporary fake repo/project. On Unix, set execute bits with `File.SetUnixFileMode`. Keep path-resolution assertions runnable on all hosts even if executable-bit assertions are Mac/Unix-specific.

- [ ] **Step 2: Run the preflight tests and verify they fail before implementation.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderPlatformPreflightTests"
```

- [ ] **Step 3: Implement the preflight with no Game-project dependency.**

For macOS, resolve runtime from `target.Target.Path`:

```text
<project-dir>/bin/Debug/net8.0/runtimes/osx-arm64/MMTools
```

Validate host version, arm64 process, Mac project target, and executable `ffmpeg`/`ffprobe`.

Do not reference `FfmpegRuntime`, search PATH, probe codecs, download binaries, or inspect OBS/privacy state.

- [ ] **Step 4: Gate record before sandbox/OBS startup.**

After `RecorderCommandLine.Validate(...)` and `RecorderGameLaunchPolicy.ResolveTarget(...)`, evaluate current preflight.

If it fails, throw one actionable error containing the failed gate details **before** calling `RunRecordAsync`.

Required ordering:

```text
parse/read environment
-> command/path validation
-> ResolveTarget
-> RecorderPlatformPreflight
-> create sandbox
-> create OBS client
-> existing RecordWorkflow
```

Do not put this check inside `RecordWorkflow`; it is launch readiness, not gameplay workflow state.

- [ ] **Step 5: Rework doctor to consume the same target and preflight.**

Delete doctor's duplicate repository-root walk and hard-coded Windows project string.

Doctor should print the resolved project target and each preflight gate. On macOS it must make the required native runtime path visible.

If target resolution fails, report a failed repository/target gate without creating a sandbox.

- [ ] **Step 6: Keep doctor OBS behavior read-only.**

Preserve exactly:

```text
Hello
Identify
GetRecordStatus
```

No StartRecord/StopRecord or source APIs.

- [ ] **Step 7: Make doctor manual guidance platform-specific.**

Windows retains existing Game Capture/application-audio guidance.

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

- [ ] **Step 8: Run focused preflight/OBS tests.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderPlatformPreflightTests|FullyQualifiedName~RecorderGameLaunchPolicyTests|FullyQualifiedName~Obs"
```

- [ ] **Step 9: Run the whole recorder and Automation test projects.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug
```

Do not edit CI unless the PR run demonstrates the macOS job skipped the new tests.

- [ ] **Step 10: Commit Task 2.**

```bash
git add \
  DTXMania.VideoRecorder/Diagnostics/RecorderPlatformPreflight.cs \
  DTXMania.VideoRecorder/Program.cs \
  DTXMania.VideoRecorder.Tests/Diagnostics/RecorderPlatformPreflightTests.cs

git commit -m "feat: preflight native Mac recorder runtime"
```

---

### Task 3: Prove the native Mac target/runtime before involving OBS

**Files:** normally no new files.

**Produces:** a green native Mac baseline proving the project target and HPA-512 audio runtime before manual capture is introduced.

- [ ] **Step 1: Confirm the proof host.**

Run on the Apple Silicon workstation:

```bash
uname -m
sw_vers -productVersion
dotnet --info
```

Require:

```text
uname -m == arm64
macOS major version >= 13
.NET SDK 8.x available
```

- [ ] **Step 2: Build the exact Debug project target the recorder will launch.**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
```

Do not publish an app/DMG for HPA-515 acceptance.

- [ ] **Step 3: Verify architecture of the actual Debug artifacts.**

```bash
file DTXMania.Game/bin/Debug/net8.0/DTXMania.Game.Mac
file DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffmpeg
file DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffprobe
```

Require `arm64` for the game apphost and both bundled tools.

- [ ] **Step 4: Run HPA-512 focused audio coverage.**

```bash
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug \
  --no-build \
  --filter "FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~FfmpegBundledRuntimeTests|FullyQualifiedName~ManagedSoundTests"
```

Require the bundled-runtime/audio tests to pass before moving to OBS.

- [ ] **Step 5: Run recorder + Automation suites on the same host.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug
```

- [ ] **Step 6: Run `doctor` once with OBS unavailable or unconfigured only to confirm the local project/runtime gates are independently visible.**

It is acceptable for OBS auth/status to fail at this checkpoint. Require the platform, project target, and bundled-runtime gates to report correctly first.

If they do not, fix only HPA-515 launch/preflight defects before proceeding.

---

### Task 4: Produce and accept one real Apple Silicon ScreenCaptureKit recording

**Files:** local evidence only until Task 5.

**Produces:** one accepted Hybrid MP4 plus architecture, isolation, telemetry, cleanup, and manual media evidence.

- [ ] **Step 1: Create a proof directory outside the checkout and choose one indexed encoded-audio chart.**

Prefer MP3 preview/BGM or another encoded input that exercises the HPA-512 runtime.

Example local structure:

```text
<proof-root>/
  raw/
  published/
```

Do not add a recorder-only fixture/chart for this native proof.

- [ ] **Step 2: Configure OBS manually.**

Required:

```text
OBS Studio 30.2+
Dedicated DTXManiaCX profile + collection + scene
ScreenCaptureKit application/window capture scoped to CX
CX application audio through the source or one dedicated macOS Audio Capture source
Desktop Audio disabled for recorded track
Microphone disabled for recorded track
Hybrid MP4
obs-websocket 5.x authenticated
Screen Recording permission granted manually
OBS idle before recorder starts
```

Do not add source discovery or privacy automation if setup is wrong.

- [ ] **Step 3: Export the existing recorder environment.**

```bash
export DTXMANIA_VIDEO_OBS_URL='ws://127.0.0.1:4455'
export DTXMANIA_VIDEO_OBS_PASSWORD='<local-secret>'
export DTXMANIA_VIDEO_OBS_OUTPUT_DIR='<absolute-proof-root>/raw'
```

Leave `DTXMANIA_APPDATA_ROOT` unset for the primary proof so the newly explicit normal Mac path is exercised.

Do not write the secret into retained transcripts.

- [ ] **Step 4: Capture source app-data before-state.**

Against `~/Library/Application Support/DTXManiaCX`, record presence, size, and SHA-256 for:

```text
Config.ini
songs.db
songs.db-wal
songs.db-shm
```

Record missing WAL/SHM as absent; do not create them.

- [ ] **Step 5: Run live doctor with OBS idle.**

```bash
dotnet run --project DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj \
  --configuration Debug --no-build -- doctor
```

Require all recorder/platform/runtime/OBS gates to pass. Recorder-side PATH `ffprobe` may remain a warning-only final-media check.

- [ ] **Step 6: Run the accepted recording.**

```bash
dotnet run --project DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj \
  --configuration Debug --no-build -- \
  record --chart '<absolute-chart-path>' --output '<proof-root>/published'
```

Require exit code 0 and retain raw MP4, published MP4, `run.json`, CX stdout, and CX stderr outside Git.

- [ ] **Step 7: Inspect the shared `run.json` contract.**

Require:

```text
status == Completed
SongSelectReady -> selected song present
PreviewReady -> Playing and elapsed >= 10000 ms
PerformanceReady -> ready + AutoPlay + totalNotes > 0
ResultCompleted -> cleared by SongComplete + totalJudgements == totalNotes
OBS -> Connect/Status/Start/Stop succeeded
raw + published paths recorded
no failure fields / retained successful sandbox
```

Do not add Mac-only diagnostic fields unless a concrete evidence gap is discovered.

- [ ] **Step 8: Run one Ctrl+C ownership proof.**

Start a second valid recording. After recorder-owned OBS recording has started, press Ctrl+C once.

Require:

```text
command exits through cancellation
OBS is no longer recording
partial raw artifact remains when OBS produced one
diagnostics are retained when possible
failed-run sandbox is retained/referenced for inspection
```

Delete the failed-run sandbox manually after evidence is captured.

- [ ] **Step 9: Capture source app-data after-state and compare.**

Repeat the exact presence/size/SHA-256 capture from Step 4.

Require no content change and no newly created source WAL/SHM file caused by the recorder.

- [ ] **Step 10: Watch the complete published MP4.**

Require:

```text
[ ] intended populated Song Select is first
[ ] preview audio starts after visible Song Select
[ ] preview lasts >= 10 seconds
[ ] complete Song Transition
[ ] full AutoPlay gameplay
[ ] BGM/chip audio audible
[ ] fully rendered Result held >= 5 seconds
[ ] recording ends after Result hold
[ ] no OBS UI / desktop / unrelated window / cursor / notification
[ ] no microphone or unrelated application audio
[ ] no duplicated/echoed CX audio
[ ] no severe stutter, aspect squeeze, or missing viewport region
[ ] no Rosetta or user-installed FFmpeg required for CX audio
```

If capture/audio/privacy setup fails while recorder telemetry/runtime checks are healthy, fix OBS/privacy configuration rather than expanding recorder code.

---

### Task 5: Commit the Mac operator runbook and sanitized proof record

**Files:**
- Create: `docs/video-recorder/macos-obs-setup.md`
- Create: `docs/verification/hpa-515-apple-silicon-live-recording.md`

- [ ] **Step 1: Write the operator runbook.**

Keep it concise and project-mode-only:

```text
Apple Silicon + macOS 13+ prerequisites
Debug Mac project build
ScreenCaptureKit source setup
CX application-audio setup
Screen Recording permission
Desktop/microphone disabled
WebSocket environment variables
doctor command
record command
bundled FFmpeg preflight meaning
raw/published/diagnostics locations
troubleshooting: host/arm64/runtime/OBS auth/already-recording/source/privacy/audio
```

Do not document executable/app-bundle recording, HPA-505 diagnostics, or HPA-506 media hardening.

- [ ] **Step 2: Write the sanitized verification record.**

Record:

```text
accepted commit SHA
macOS version + arm64 host
.NET + OBS versions
non-sensitive chart identity
project target
arm64 apphost/ffmpeg/ffprobe evidence
doctor result summary
successful command with private paths redacted
raw/published MP4 filenames + sizes + SHA-256
key run.json acceptance values
source before/after hash result
Ctrl+C ownership result
manual media checklist result
focused automated-test commands + pass counts
warnings/deviations
```

Do not commit videos, raw logs, secrets, or private absolute paths.

- [ ] **Step 3: Run final automated verification on Apple Silicon.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug --no-build \
  --filter "FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~FfmpegBundledRuntimeTests|FullyQualifiedName~ManagedSoundTests"
git diff --check
```

Also require the normal Windows CI recorder/Automation jobs to remain green before merge.

- [ ] **Step 4: Commit the acceptance documentation.**

```bash
git add \
  docs/video-recorder/macos-obs-setup.md \
  docs/verification/hpa-515-apple-silicon-live-recording.md

git commit -m "docs: verify Apple Silicon recorder parity"
```

---

## Completion Checklist

Before marking HPA-515 complete, verify all of these are true:

```text
[ ] unchanged doctor / record CLI works on native Apple Silicon macOS 13+
[ ] record and doctor use one ResolvedRecorderTarget
[ ] Mac preflight runs before sandbox/OBS creation
[ ] missing/unusable bundled runtime fails before OBS
[ ] default Mac source root is ~/Library/Application Support/DTXManiaCX
[ ] one encoded-audio project-mode recording passes the HPA-513 journey contract
[ ] source Config.ini/database/WAL/SHM are unchanged
[ ] Ctrl+C stops only recorder-owned OBS work
[ ] manual ScreenCaptureKit/audio acceptance passes
[ ] Windows recorder behavior/tests remain green
[ ] no packaged executable mode/platform adapter/HPA-505/HPA-506 scope was introduced
```
