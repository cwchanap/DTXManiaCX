# HPA-513 Windows Live Recording Acceptance Implementation Plan

> **For agentic workers:** Execute this as a proof/documentation task. Do not change recorder production code inside HPA-513. If the proof exposes a real defect, stop, file a focused blocker, fix it separately, then rerun this plan against the fixed commit.

**Goal:** Produce and accept the first real Windows x64 DTXManiaCX AutoPlay recording using the merged HPA-503 `dtx-video` recorder, prove source app-data isolation and OBS ownership, and commit a reusable Windows setup guide plus sanitized verification record.

**Architecture:** No new runtime architecture. Use the existing `DTXMania.VideoRecorder`, dedicated manually configured OBS profile/scene, existing diagnostics, and existing portable tests. The implementation PR should normally be documentation-only.

**Expected effort:** < 1 engineer day once a Windows + OBS proof workstation and a short indexed chart are available.

## Global constraints

- Use a clean checkout of the commit being accepted.
- Windows 10 version 2004+ or Windows 11, x64.
- OBS Studio 30.2+ with obs-websocket 5.x.
- Keep OBS setup manual; no source/scene automation.
- Use one short indexed chart with valid preview audio.
- Do not commit MP4s, raw diagnostics, passwords, API keys, or private absolute paths.
- Do not change `DTXMania.VideoRecorder` production/test code in this ticket.
- If a product bug appears, open a separate blocker/fix PR and rerun acceptance after it merges.
- HPA-506/HPA-504 remain optional follow-ups; do not pull their stricter media/preflight scope into this task.

## Intended execution PR files

```text
Add:
  docs/video-recorder/windows-obs-setup.md
  docs/verification/hpa-513-windows-live-recording.md

Normally unchanged:
  DTXMania.VideoRecorder/**
  DTXMania.VideoRecorder.Tests/**
  DTXMania.Automation/**
  DTXMania.Game/**
```

---

## Task 1: Prepare the proof workstation and capture the baseline

**Produces:** an isolated local proof directory, a known accepted commit candidate, source app-data before-state, and a correctly configured idle OBS instance.

- [ ] **Step 1: Update and build the exact commit under test.**

From repository root:

```powershell
git status --short
git rev-parse HEAD
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj --configuration Debug
```

Require a clean working tree before the native proof. Record the full commit SHA for the verification document.

- [ ] **Step 2: Select one short indexed chart.**

Use an existing chart already visible to normal CX Song Select and known to have preview audio. Record a non-sensitive chart identity for the verification document; do not commit the private absolute chart path.

Do not create a special test chart or modify the song database for HPA-513.

- [ ] **Step 3: Create a proof directory outside the Git checkout.**

Example only:

```text
C:\DTXManiaCX-HPA-513\
  raw\
  published\
```

Configure OBS raw recording output to the `raw` directory. The exact local path is not part of the product contract.

- [ ] **Step 4: Configure the dedicated OBS profile/collection/scene manually.**

Required settings:

```text
Game Capture -> DTXManiaCX game window/process
Application Audio Capture -> DTXManiaCX
Desktop Audio -> disabled
Microphone -> disabled
Recording format -> Hybrid MP4
obs-websocket -> enabled + authenticated
OBS output directory -> proof raw directory
```

Keep OBS open and inactive.

- [ ] **Step 5: Export recorder environment variables in the proof shell.**

```powershell
$env:DTXMANIA_VIDEO_OBS_URL = "ws://127.0.0.1:4455"
$env:DTXMANIA_VIDEO_OBS_PASSWORD = "<local-secret>"
$env:DTXMANIA_VIDEO_OBS_OUTPUT_DIR = "<absolute-raw-directory>"
```

Only set `DTXMANIA_APPDATA_ROOT` when the proof intentionally uses a non-default source CX app-data root.

Do not echo the OBS password into retained command transcripts.

- [ ] **Step 6: Capture source app-data before-state.**

For the source CX app-data root, capture presence, file size, and SHA-256 for:

```text
Config.ini
songs.db
songs.db-wal
songs.db-shm
```

A small local PowerShell snippet is sufficient. Handle missing WAL/SHM files as `absent`; do not create them for the proof.

Save the result as `<proof-root>\source-state-before.txt`.

- [ ] **Step 7: Commit nothing yet.**

Task 1 is workstation preparation only.

---

## Task 2: Prove preflight and ownership/failure behavior

**Produces:** passing `doctor`, focused automated cleanup proof, and native OBS ownership evidence.

- [ ] **Step 1: Run focused portable recorder tests.**

```powershell
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj --configuration Debug --no-restore --filter "FullyQualifiedName~RecordWorkflowTests|FullyQualifiedName~RecordFinalizationTests|FullyQualifiedName~RecordingArtifactVerifierTests"
```

Record pass/fail counts in the verification document. These tests remain the proof for platform-neutral launch/stage/finalization cleanup behavior.

- [ ] **Step 2: Run `doctor` with OBS idle.**

```powershell
dotnet run --project DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj --configuration Debug --no-build -- doctor 2>&1 | Tee-Object -FilePath "<proof-root>\doctor.txt"
```

Require exit code `0` and these gates:

```text
Windows: passed
Repository: passed
Game project: passed
Source config validation: passed
Raw output directory: passed
OBS auth/status: Hello + Identify succeeded
OBS recording status: inactive
OBS state mutation: none
```

`ffprobe` absent is acceptable and should be recorded as the existing warning-only condition.

- [ ] **Step 3: Native pre-existing OBS ownership check.**

1. Start recording manually in OBS.
2. Run `record` with the valid proof chart/output directory.
3. Require the recorder to reject the run because OBS is already recording.
4. Confirm OBS continues recording after the command fails.
5. Stop the manually owned OBS recording yourself.

Record outcome only; the disposable manual artifact from this ownership check does not need to be retained.

- [ ] **Step 4: Lightweight invalid/unindexed chart check.**

Run one `record` attempt using an invalid or unindexed chart input.

Require:

- non-zero exit;
- actionable failure;
- no unrelated OBS recording is started/stopped;
- no source app-data mutation.

Do not manufacture a destructive CX launch failure. Existing automated coverage is sufficient for that platform-neutral path.

- [ ] **Step 5: Do not commit yet.**

If either native ownership check reveals a product defect, stop HPA-513 and open a focused blocker.

---

## Task 3: Produce the accepted happy-path recording

**Produces:** one raw MP4, one published MP4, completed recorder diagnostics, and native cancellation proof.

- [ ] **Step 1: Run the happy path once.**

With OBS idle:

```powershell
dotnet run --project DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj --configuration Debug --no-build -- `
  record --chart "<absolute-chart-path>" --output "<proof-root>\published"
```

Require exit code `0`.

Retain:

```text
raw OBS MP4
published MP4
published\diagnostics\<run-id>\run.json
published\diagnostics\<run-id>\cx-stdout.log
published\diagnostics\<run-id>\cx-stderr.log
```

- [ ] **Step 2: Inspect `run.json`.**

Require `status == Completed` and verify the key telemetry:

```text
SongSelectReady -> non-empty selectedSongTitle
PreviewReady -> preparedPreviewState == Playing
                preparedPreviewElapsedMs >= 10000
PerformanceReady -> performanceReady == true
                    autoPlayEnabled == true
                    totalNotes > 0
ResultCompleted -> stageCompleted == true
                   clearFlag == true
                   completionReason == SongComplete
                   totalJudgements == totalNotes
OBS -> Connect/Status/Start/Stop all succeeded
Completed -> present
failure/failureType/retainedSandboxPath -> absent
```

Also confirm raw/published paths point to the expected proof directories.

- [ ] **Step 3: Native Ctrl+C ownership check.**

Start a second normal `record` run. After OBS has started and preview/gameplay is active, press Ctrl+C once.

Require:

- the command exits through cancellation;
- OBS is no longer recording afterward;
- any partial raw artifact remains available;
- failed-run diagnostics are written when possible;
- the failed-run sandbox is retained and referenced by diagnostics.

Inspect the retained sandbox only as needed, then delete it manually after the proof is recorded.

Do not use the cancellation run as the accepted MP4.

---

## Task 4: Validate source isolation and manually accept the media

**Produces:** deterministic source before/after comparison and formal human acceptance of the published MP4.

- [ ] **Step 1: Capture source app-data after-state.**

Repeat the exact same presence/size/SHA-256 capture used in Task 1 and save it as:

```text
<proof-root>\source-state-after.txt
```

Require no change to source `Config.ini`, `songs.db`, `songs.db-wal`, or `songs.db-shm`, including no newly created source WAL/SHM file.

- [ ] **Step 2: Confirm recorder cleanup/publication behavior.**

Require:

- successful-run sandbox deleted;
- raw OBS MP4 still exists;
- published MP4 exists;
- diagnostics directory exists;
- raw and published MP4 are non-empty.

Calculate byte size and SHA-256 for both MP4s for the verification document.

- [ ] **Step 3: Watch the complete published MP4.**

Confirm all of the following:

```text
[ ] intended populated Song Select is first
[ ] preview audio begins after visible Song Select
[ ] at least 10 seconds of actual preview audio
[ ] complete Song Transition
[ ] full AutoPlay gameplay
[ ] BGM/chip audio audible and plausible
[ ] fully rendered Result
[ ] Result remains visible >= 5 seconds
[ ] recording ends after Result hold
[ ] no OBS UI / desktop / unrelated window
[ ] no cursor or notification captured
[ ] no microphone or unrelated application audio
[ ] no duplicated/echoed CX audio
[ ] no severe stutter, clipping, aspect squeeze, or missing viewport region
```

Do not add automated quality scoring or new media thresholds.

- [ ] **Step 4: Optional `ffprobe` sanity only when already available.**

If `ffprobe` is on PATH, record that the verifier found both audio and video streams. Do not install an FFmpeg dependency solely to satisfy HPA-513.

---

## Task 5: Commit the reusable runbook and sanitized verification record

**Files:**

```text
docs/video-recorder/windows-obs-setup.md
docs/verification/hpa-513-windows-live-recording.md
```

- [ ] **Step 1: Write `windows-obs-setup.md`.**

Keep it concise and operator-focused:

```text
Prerequisites
Dedicated OBS profile/scene setup
Application audio + disabled desktop/mic
Hybrid MP4 + WebSocket setup
Required environment variables
doctor command
record command
How to choose an indexed chart
Where raw/published/diagnostics land
Success indicators
Troubleshooting:
  source Config.ini not normalized
  OBS auth failure
  OBS already recording
  chart invalid/unindexed
  preview unavailable
  ffprobe optional warning
```

Do not duplicate OBS upstream documentation beyond the settings this recorder requires.

- [ ] **Step 2: Write `hpa-513-windows-live-recording.md`.**

Record only sanitized evidence:

```text
accepted commit SHA
Windows version / architecture
.NET SDK/host version
OBS version
non-sensitive chart identity
doctor result summary
successful command summary with private paths redacted
raw MP4 filename + size + SHA-256
published MP4 filename + size + SHA-256
run.json acceptance values
source before/after result
pre-existing OBS ownership result
Ctrl+C ownership result
invalid/unindexed chart result
manual media checklist result
focused automated test command + pass counts
warnings/deviations
```

Do not paste full raw logs or local absolute paths.

- [ ] **Step 3: Final repo validation.**

```powershell
git diff --check
git status --short
```

Expected execution PR diff: two documentation files only.

- [ ] **Step 4: Commit and open the HPA-513 execution PR.**

Suggested commit:

```text
docs: record HPA-513 Windows recorder acceptance
```

Suggested PR title:

```text
docs: validate first Windows live recording
```

The PR body should state explicitly that no recorder code changed, identify the accepted commit, summarize native/automated proof, and link HPA-513.

---

## Completion checklist

HPA-513 is complete only when:

- [ ] `doctor` passes against real Windows + dedicated OBS;
- [ ] one accepted raw and published MP4 are retained outside Git;
- [ ] diagnostics show the complete 10-second-preview / full-gameplay / five-second-Result journey;
- [ ] source Config.ini/database/WAL/SHM are byte-for-byte unchanged;
- [ ] pre-existing OBS recording is not owned/stopped by the recorder;
- [ ] Ctrl+C after owned start stops only the recorder-owned OBS recording;
- [ ] focused portable recorder cleanup/finalization tests pass;
- [ ] manual video/audio inspection passes;
- [ ] `docs/video-recorder/windows-obs-setup.md` is committed;
- [ ] `docs/verification/hpa-513-windows-live-recording.md` is committed;
- [ ] no production-code changes, MP4s, secrets, private paths, or raw logs are included in the HPA-513 execution PR.

After this closes, HPA-515 becomes the next recorder-platform task. Optional HPA-504/HPA-506 hardening remains deferred until real usage justifies it.
