# HPA-515 Apple Silicon Recorder Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to execute this plan task-by-task. Keep the implementation minimal; HPA-515 is Mac parity for the existing recorder, not a recorder/platform redesign.

**Goal:** Make the existing `dtx-video` workflow run natively on Apple Silicon macOS, prove one real ScreenCaptureKit/OBS recording with bundled arm64 FFmpeg audio, and retain a concise Mac setup guide plus sanitized verification record.

**Architecture:** Preserve the existing `RecordWorkflow`, OBS client, sandbox, diagnostics, and finalization. Remove only the recorder's three Windows assumptions by making command validation and launch selection platform-aware and adding one focused Mac preflight helper for `doctor`. Use the existing `GameProjectPaths` / `GameLaunchTarget.Project` / `GameLaunchTarget.Executable` primitives from `DTXMania.Automation`; do not add platform adapters.

**Tech stack:** .NET 8, MonoGame CX Mac project, `DTXMania.Automation`, obs-websocket 5.x, OBS Studio 30.2+, ScreenCaptureKit, existing HPA-512 native FFmpeg runtime.

**Expected effort:** 2–3 engineer days including one real Apple Silicon/OBS acceptance run.

## Global constraints

- Keep `dtx-video doctor` and `dtx-video record --chart ... --output ...` unchanged.
- Windows behavior must remain unchanged.
- Mac support is macOS 13+ on native arm64 only; no Intel Mac or Rosetta fallback.
- Reuse the same `RecordWorkflow`; no Mac workflow/session subclass.
- Reuse `GameProcessDriver`; do not change process ownership/readiness semantics unless a proven blocker requires it.
- Reuse `ObsWebSocketRecorder`; no ScreenCaptureKit source discovery or permission diagnosis.
- Reuse HPA-512's bundled `osx-arm64` FFmpeg; do not add another resolver/download/dependency.
- Recorder-side MP4 `ffprobe` stays optional PATH-based as today.
- Keep the successful-run disposable app-data behavior unchanged.
- Do not add strict media policy, remux, or transcoding; HPA-506 owns that.
- Do not add automated Mac OBS diagnostics; HPA-505 owns that.
- If the native proof exposes an unrelated recorder/game defect, stop and file a focused blocker instead of expanding HPA-515.

## Intended implementation PR file surface

```text
Modify:
  DTXMania.VideoRecorder/Configuration/RecorderEnvironment.cs
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

---

## Task 1: Make recorder validation and game launch target platform-aware

**Files:**

- Modify: `DTXMania.VideoRecorder/Configuration/RecorderEnvironment.cs`
- Modify: `DTXMania.VideoRecorder/RecorderCommandLine.cs`
- Modify: `DTXMania.VideoRecorder/Workflow/RecorderGameLaunchPolicy.cs`
- Create: `DTXMania.VideoRecorder.Tests/RecorderCommandLineTests.cs`
- Create: `DTXMania.VideoRecorder.Tests/Workflow/RecorderGameLaunchPolicyTests.cs`

**Interfaces:**

- Add optional environment setting `DTXMANIA_VIDEO_GAME_EXECUTABLE` to `RecorderEnvironment`.
- Keep the public CLI verbs/options unchanged.
- Keep returning the existing `GameProcessStartOptions`; no new public Automation types.
- Default launch target is the current platform project from `GameProjectPaths.Current`.
- Explicit executable mode uses `GameLaunchTarget.Executable` and executable-parent working directory.

- [ ] **Step 1: Add failing command-line environment tests.**

Cover these behaviors:

```text
unset DTXMANIA_VIDEO_GAME_EXECUTABLE
  -> RecorderEnvironment.GameExecutablePath == null

absolute existing executable path
  -> value is normalized to a full path

relative executable path
  -> rejected with an actionable validation error

missing executable path
  -> rejected before record starts
```

Do not add a new CLI option for the game target.

- [ ] **Step 2: Run the focused command-line tests and confirm the new cases fail.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderCommandLineTests"
```

Expected before implementation: the new `GameExecutablePath`/environment behavior does not exist.

- [ ] **Step 3: Extend `RecorderEnvironment` and environment parsing minimally.**

Add one nullable field for the optional executable target and one environment-variable constant:

```text
DTXMANIA_VIDEO_GAME_EXECUTABLE
```

Normalize it only when non-empty. Preserve all existing OBS/app-data environment behavior.

Validation rule:

```text
unset -> default project mode
set   -> absolute existing file required
```

Do not inspect `.app` metadata or search for executables.

- [ ] **Step 4: Replace the Windows-only record gate with the intended support contract.**

`ValidateRecord` should allow:

```text
Windows -> existing behavior
macOS   -> only macOS 13+ and native arm64
other   -> PlatformNotSupportedException
```

Do not tighten existing Windows architecture/version behavior in this ticket.

Keep chart/output/OBS/source validation ordering deterministic and actionable.

- [ ] **Step 5: Add failing launch-policy tests.**

Required cases:

```text
default target
  -> resolves repository root
  -> uses GameProjectPaths.Current
  -> creates GameLaunchTarget.Project(absolute current-platform project)
  -> working directory is repository root

explicit executable
  -> exact executable is selected through GameLaunchTarget.Executable
  -> working directory is executable parent
  -> sandbox AppDataRoot is preserved
  -> launch token remains fresh/non-empty
```

Tests may branch on `OperatingSystem.IsWindows()` / `OperatingSystem.IsMacOS()`; the repository already runs VideoRecorder tests on both CI platforms.

- [ ] **Step 6: Run the launch-policy tests and confirm the new cases fail.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderGameLaunchPolicyTests"
```

- [ ] **Step 7: Generalize `RecorderGameLaunchPolicy.CreateOptions`.**

Keep repository-root resolution in this class.

Implementation rules:

```text
if GameExecutablePath is null:
  target = Project(repoRoot + GameProjectPaths.Current)
  workingDirectory = repoRoot
else:
  target = Executable(GameExecutablePath)
  workingDirectory = parent(GameExecutablePath)
```

Do not modify `GameProcessDriver`.

- [ ] **Step 8: Wire `RunRecordAsync` to pass the selected executable override into the launch policy.**

No other workflow construction changes are expected.

- [ ] **Step 9: Run the focused tests until green.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderCommandLineTests|FullyQualifiedName~RecorderGameLaunchPolicyTests"
```

- [ ] **Step 10: Commit Task 1.**

```bash
git add \
  DTXMania.VideoRecorder/Configuration/RecorderEnvironment.cs \
  DTXMania.VideoRecorder/RecorderCommandLine.cs \
  DTXMania.VideoRecorder/Workflow/RecorderGameLaunchPolicy.cs \
  DTXMania.VideoRecorder/Program.cs \
  DTXMania.VideoRecorder.Tests/RecorderCommandLineTests.cs \
  DTXMania.VideoRecorder.Tests/Workflow/RecorderGameLaunchPolicyTests.cs

git commit -m "feat: support Mac recorder launch targets"
```

---

## Task 2: Add a focused Mac `doctor` preflight without changing the recorder workflow

**Files:**

- Create: `DTXMania.VideoRecorder/Diagnostics/RecorderPlatformPreflight.cs`
- Modify: `DTXMania.VideoRecorder/Program.cs`
- Create: `DTXMania.VideoRecorder.Tests/Diagnostics/RecorderPlatformPreflightTests.cs`

**Interfaces:**

Keep this helper internal to VideoRecorder. It owns only platform/target/native-runtime preflight needed by `doctor`; it does not launch CX or talk to OBS.

The helper must support these inputs already available to `Program`:

```text
repository root
GameProcessStartOptions / selected GameLaunchTarget
current OS/version/process architecture
```

It must expose enough result detail for `Program` to print individual pass/fail gates and the resolved Mac runtime directory. Do not create a generic plug-in interface.

- [ ] **Step 1: Add failing preflight tests for project and executable runtime layout.**

Use temporary directories/files rather than the real build output.

Required path rules:

```text
Mac project mode:
  <repo>/DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/{ffmpeg,ffprobe}

Mac explicit executable mode:
  <executable-dir>/runtimes/osx-arm64/MMTools/{ffmpeg,ffprobe}
```

Required failure cases:

```text
missing ffmpeg
missing ffprobe
non-executable runtime file on macOS
unsupported macOS version
non-arm64 Mac process
```

The path-resolution tests should remain runnable on either OS. Unix execute-bit assertions may be guarded to macOS.

- [ ] **Step 2: Run the focused preflight tests and confirm they fail.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderPlatformPreflightTests"
```

- [ ] **Step 3: Implement the smallest internal preflight helper.**

Responsibilities only:

```text
validate supported host for record/doctor reporting
resolve selected target description
resolve expected Mac native runtime directory
verify ffmpeg + ffprobe exist
verify executable permission on macOS
return actionable gate detail
```

Do not:

```text
reference DTXMania.Game
configure FFMpegCore
probe codecs
search PATH for CX audio runtime
download FFmpeg
inspect OBS sources
modify privacy permissions
```

- [ ] **Step 4: Rework `RunDoctorAsync` to use the same launch target selection as `record`.**

Remove the hard-coded Windows project path and duplicate repository-root policy.

`doctor` should report:

```text
Recorder platform
Repository
Selected game target
Source config
Raw output directory
OBS URL/auth/status
```

On macOS additionally report:

```text
macOS >= 13
process architecture == arm64
native CX FFmpeg runtime directory
ffmpeg executable
ffprobe executable
```

The existing recorder-side PATH `ffprobe` message remains optional and must be clearly distinguished from the bundled CX FFmpeg gate.

- [ ] **Step 5: Make manual OBS prerequisite text platform-specific.**

Windows keeps existing Game Capture/application-audio text.

macOS prints only the minimal operator checklist:

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

Never print a claim that source selection or privacy permission was programmatically verified.

- [ ] **Step 6: Keep OBS mutation behavior unchanged.**

`doctor` must still perform only:

```text
Hello
Identify
GetRecordStatus
```

No StartRecord/StopRecord and no source APIs.

- [ ] **Step 7: Run focused preflight + existing OBS tests.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~RecorderPlatformPreflightTests|FullyQualifiedName~Obs"
```

- [ ] **Step 8: Run the whole VideoRecorder and Automation test projects.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug
```

Do not edit CI merely to create a new job; these suites already run on Windows and macOS CI.

- [ ] **Step 9: Commit Task 2.**

```bash
git add \
  DTXMania.VideoRecorder/Diagnostics/RecorderPlatformPreflight.cs \
  DTXMania.VideoRecorder/Program.cs \
  DTXMania.VideoRecorder.Tests/Diagnostics/RecorderPlatformPreflightTests.cs

git commit -m "feat: add Mac recorder doctor preflight"
```

---

## Task 3: Validate the implementation on Apple Silicon before involving OBS

**Files:** normally no new files.

**Produces:** a green native Mac build/test baseline and proof that the default CX target carries the HPA-512 runtime.

- [ ] **Step 1: Confirm the host is native Apple Silicon.**

Run on the proof workstation:

```bash
uname -m
sw_vers -productVersion
dotnet --info
```

Require:

```text
uname -m -> arm64
macOS major version -> 13 or later
.NET -> SDK 8.x available
```

- [ ] **Step 2: Build the Mac game before `doctor`.**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
```

This materializes the HPA-512 runtime in the same Debug output used by default `dotnet run` project mode.

- [ ] **Step 3: Verify native runtime architecture directly.**

```bash
file DTXMania.Game/bin/Debug/net8.0/DTXMania.Game.Mac
file DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffmpeg
file DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffprobe
```

Require each relevant executable description to include `arm64`.

- [ ] **Step 4: Run Mac game/audio tests that prove bundled runtime use.**

```bash
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug \
  --no-build \
  --filter "FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~FfmpegBundledRuntimeTests|FullyQualifiedName~ManagedSoundTests"
```

- [ ] **Step 5: Run recorder + Automation suites on the same host.**

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug
```

- [ ] **Step 6: Exercise both launch-selection modes without broadening the task.**

Default project mode is required for the live proof.

For explicit executable mode, use an existing published/app-bundle executable if already available on the workstation. It is sufficient to verify `doctor` resolves the target/runtime correctly; HPA-515 does not require two full videos.

Do not build a new DMG workflow just for this test.

- [ ] **Step 7: Do not commit acceptance evidence yet.**

If any Mac-only code defect is found, fix it inside HPA-515 only when it is directly caused by launch/preflight parity. Otherwise open a focused blocker.

---

## Task 4: Produce and accept one native Apple Silicon recording

**Files:** local evidence only during this task.

**Produces:** one accepted MP4, recorder diagnostics, source-isolation proof, and one cancellation/ownership proof.

- [ ] **Step 1: Prepare one proof directory outside the Git checkout.**

Create local variables pointing at actual workstation paths:

```bash
export HPA515_PROOF_ROOT="$HOME/DTXManiaCX-HPA-515-proof"
export HPA515_RAW="$HPA515_PROOF_ROOT/raw"
export HPA515_PUBLISHED="$HPA515_PROOF_ROOT/published"
mkdir -p "$HPA515_RAW" "$HPA515_PUBLISHED"
```

Choose one existing indexed chart with MP3 or another encoded preview/BGM that exercises bundled FFmpeg and export its absolute path:

```bash
export HPA515_CHART="$(python3 -c 'import os; print(os.path.abspath(os.environ["HPA515_CHART_INPUT"]))')"
```

Before running the command, set `HPA515_CHART_INPUT` to the actual selected chart path in the local shell. Do not commit it.

- [ ] **Step 2: Configure OBS manually.**

Required state:

```text
OBS 30.2+
Dedicated DTXManiaCX profile/collection/scene
ScreenCaptureKit app/window source -> CX
CX application audio enabled through that source or one dedicated macOS Audio Capture source
Desktop Audio disabled
Microphone disabled
Hybrid MP4
Screen Recording permission granted
obs-websocket 5.x enabled/authenticated
Recording directory == HPA515_RAW
OBS idle
```

Visually confirm the source shows CX before starting the recorder. This manual check is the source/permission gate for HPA-515.

- [ ] **Step 3: Export recorder environment.**

```bash
export DTXMANIA_VIDEO_OBS_URL="ws://127.0.0.1:4455"
export DTXMANIA_VIDEO_OBS_PASSWORD="$HPA515_LOCAL_OBS_PASSWORD"
export DTXMANIA_VIDEO_OBS_OUTPUT_DIR="$HPA515_RAW"
unset DTXMANIA_VIDEO_GAME_EXECUTABLE
```

`HPA515_LOCAL_OBS_PASSWORD` is a local secret and must never be written to committed evidence.

- [ ] **Step 4: Capture source app-data before-state.**

For the actual source CX app-data root, record presence, byte size, and SHA-256 for:

```text
Config.ini
songs.db
songs.db-wal
songs.db-shm
```

Save the sanitized local output to:

```text
$HPA515_PROOF_ROOT/source-state-before.txt
```

Absent WAL/SHM files remain absent; do not create them for the proof.

- [ ] **Step 5: Run `doctor` and retain output.**

```bash
dotnet run --project DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj \
  --configuration Debug -- doctor \
  2>&1 | tee "$HPA515_PROOF_ROOT/doctor.txt"
```

Require exit code `0` and these Mac gates:

```text
supported macOS
arm64 recorder process
repository found
Mac game target found
native CX ffmpeg found/executable
native CX ffprobe found/executable
source config valid
raw output directory valid
OBS auth/status succeeded
OBS recording inactive
OBS state mutation none
```

- [ ] **Step 6: Retain architecture evidence.**

Write sanitized `file` output for the game apphost and bundled FFmpeg pair to:

```text
$HPA515_PROOF_ROOT/architecture.txt
```

Require `arm64` for all three.

- [ ] **Step 7: Run the accepted recording.**

```bash
dotnet run --project DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj \
  --configuration Debug --no-build -- \
  record --chart "$HPA515_CHART" --output "$HPA515_PUBLISHED"
```

Require exit code `0` and retain:

```text
raw OBS MP4
published MP4
published/diagnostics/<run-id>/run.json
published/diagnostics/<run-id>/cx-stdout.log
published/diagnostics/<run-id>/cx-stderr.log
```

- [ ] **Step 8: Validate `run.json` using the shared Windows contract.**

Require:

```text
status == Completed
SongSelectReady -> selected song present
PreviewReady -> Playing and elapsed >= 10000 ms
PerformanceReady -> ready, AutoPlay enabled, totalNotes > 0
ResultCompleted -> completed, clear, SongComplete, totalJudgements == totalNotes
OBS Connect/Status/Start/Stop -> success
raw/published paths -> present
failure/failureType/retained successful sandbox -> absent
```

Do not add Mac-only telemetry just to make the proof easier.

- [ ] **Step 9: Run one native Ctrl+C ownership check.**

Start a second valid `record` run. After OBS is recorder-owned and preview/gameplay is active, press Ctrl+C once.

Require:

```text
command exits via cancellation
OBS is no longer recording
partial raw artifact retained when available
failed-run diagnostics retained when available
failed-run sandbox retained and referenced for debugging
normal source app-data unchanged
```

Delete the failed-run sandbox manually only after evidence is inspected.

- [ ] **Step 10: Capture source app-data after-state.**

Repeat the exact same presence/size/SHA-256 capture as Step 4.

Require no source Config.ini/database/WAL/SHM content change and no new source WAL/SHM creation caused by the recorder.

- [ ] **Step 11: Watch the complete published MP4.**

Confirm:

```text
[ ] intended populated Song Select first
[ ] preview begins after visible Song Select
[ ] >= 10 seconds actual preview audio
[ ] complete Song Transition
[ ] full AutoPlay gameplay
[ ] BGM/chip audio audible
[ ] fully rendered Result
[ ] Result held >= 5 seconds
[ ] recording ends after Result hold
[ ] no OBS UI / desktop / unrelated window
[ ] no cursor / notifications
[ ] no microphone / unrelated application audio
[ ] no duplicated or echoed CX audio
[ ] no aspect squeeze / severe stutter / missing viewport region
[ ] encoded preview/gameplay audio works without user-installed FFmpeg or Rosetta
```

If capture selection/privacy is wrong, fix the manual OBS setup and rerun. Do not add automatic source diagnosis to HPA-515.

---

## Task 5: Commit the Mac runbook and sanitized verification record

**Files:**

- Create: `docs/video-recorder/macos-obs-setup.md`
- Create: `docs/verification/hpa-515-apple-silicon-live-recording.md`

- [ ] **Step 1: Write `macos-obs-setup.md`.**

Keep it operator-focused and concise. Required sections:

```text
Prerequisites: macOS 13+, Apple Silicon, OBS 30.2+
Build the default Mac project target
Optional DTXMANIA_VIDEO_GAME_EXECUTABLE usage
ScreenCaptureKit app/window source
CX application audio source
Screen Recording permission
Disable Desktop Audio + microphone
Hybrid MP4 + obs-websocket
Required environment variables
doctor command
record command
Native bundled FFmpeg gate
Raw/published/diagnostics locations
Troubleshooting:
  unsupported/non-arm64 host
  missing bundled runtime
  OBS auth/already-recording
  Screen Recording permission
  stale/wrong ScreenCaptureKit target
  duplicated/missing audio
  invalid/unindexed chart
  optional recorder-side ffprobe warning
```

Do not turn this into general OBS/macOS documentation.

- [ ] **Step 2: Write `hpa-515-apple-silicon-live-recording.md`.**

Record only sanitized evidence:

```text
accepted commit SHA
macOS version + arm64 host
.NET SDK/host version
OBS version
launch mode (project or explicit executable)
non-sensitive chart identity
arm64 game/ffmpeg/ffprobe evidence summary
doctor gate summary
successful command summary with private paths redacted
raw MP4 filename + size + SHA-256
published MP4 filename + size + SHA-256
key run.json values
source before/after result
Ctrl+C ownership/cleanup result
manual media checklist result
focused test commands + pass counts
warnings/deviations
```

Never embed passwords, API keys, private absolute chart paths, raw logs, or MP4 binaries.

- [ ] **Step 3: Run final cross-platform-safe validation on the implementation branch.**

On Apple Silicon:

```bash
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --configuration Debug
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug --no-build
```

Then rely on the existing GitHub Windows/macOS CI jobs for the authoritative cross-platform gate.

- [ ] **Step 4: Confirm scope before committing.**

```bash
git status --short
git diff --stat main...HEAD
git diff --check
```

Reject unrelated changes and any unexpected edits under:

```text
RecordWorkflow.cs
Obs/
Media/
Sandbox/
GameProcessDriver.cs
FfmpegRuntime.cs
.github/workflows/
```

unless a separately reviewed, directly necessary fix was explicitly added.

- [ ] **Step 5: Commit the acceptance documents.**

```bash
git add \
  docs/video-recorder/macos-obs-setup.md \
  docs/verification/hpa-515-apple-silicon-live-recording.md

git commit -m "docs: record Apple Silicon recorder acceptance"
```

- [ ] **Step 6: Final PR checklist.**

The implementation PR is ready for review only when all are true:

```text
[ ] same CLI contract on Windows + Mac
[ ] Mac default project launch is DTXMania.Game.Mac.csproj
[ ] optional explicit packaged executable works
[ ] Mac doctor fails unsupported OS/arch and unusable bundled runtime
[ ] existing Windows VideoRecorder/Automation tests green
[ ] Mac VideoRecorder/Automation/audio tests green
[ ] real Apple Silicon doctor green
[ ] accepted MP4 manually inspected
[ ] source app-data unchanged
[ ] Ctrl+C stops only owned OBS recording
[ ] no ScreenCaptureKit automation/platform framework/strict media scope added
```

## Implementation-agent handoff

Execute this as one HPA-515 implementation PR with three review checkpoints:

1. **Launch parity:** command/environment validation and platform project/executable selection.
2. **Doctor parity:** Mac host/native-runtime gates with focused tests; shared workflow remains untouched.
3. **Native acceptance:** real Apple Silicon OBS proof plus the two committed docs.

Do not parallelize Tasks 1 and 2 against different assumptions: Task 2 consumes the exact launch target selected by Task 1. Task 4 cannot be accepted until Tasks 1–3 are green.