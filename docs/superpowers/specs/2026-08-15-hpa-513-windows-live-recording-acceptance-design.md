# HPA-513 Windows Live Recording Acceptance Design

**Issue:** [HPA-513](https://linear.app/cwchanap/issue/HPA-513/produce-and-validate-the-first-live-windows-recording)  
**Date:** 2026-08-15  
**Status:** Proposed

## Goal

Prove the already-merged HPA-503 recorder on one real Windows x64 workstation with a dedicated OBS configuration, retain one accepted recording/evidence bundle, and document the minimum setup needed to repeat it.

This is an acceptance and documentation slice, not a second recorder implementation.

The successful captured journey must be:

```text
Song Select
-> at least 10 seconds of prepared preview audio
-> complete Song Transition
-> full AutoPlay Performance
-> completed Result held for at least 5 seconds
```

HPA-503 already owns the reusable recorder implementation and portable automated coverage. HPA-513 owns the deferred native Windows/OBS proof.

## Why this is the next actionable task

- HPA-503 is complete and its implementation is merged.
- HPA-513 is now unblocked.
- HPA-513 blocks HPA-515 Apple Silicon parity and the optional Windows OBS preflight work.
- The remaining backlog recorder items are explicitly optional hardening and should not precede the first real proof.

## Scope

### In scope

- one Windows 10 2004+ or Windows 11 x64 proof workstation;
- OBS Studio 30.2+ with obs-websocket 5.x;
- one dedicated OBS profile/collection/scene configured manually;
- one short indexed DTX chart with valid preview audio;
- successful `dtx-video doctor`;
- one successful `dtx-video record` run;
- source CX app-data isolation evidence;
- manual video/audio inspection;
- a small set of native ownership/failure checks where real OBS behavior matters;
- a concise Windows OBS setup/runbook;
- a sanitized committed verification record.

### Out of scope

- recorder architecture changes;
- automatic OBS scene/source/audio diagnosis;
- persistent recorder databases or song caches;
- MKV fallback/remux/re-encoding;
- Apple Silicon implementation or acceptance;
- YouTube upload/editing/overlays/batch processing;
- committing MP4s, raw recorder diagnostics, private chart paths, or secrets to Git.

If the native proof exposes a recorder defect, HPA-513 stops at that defect. Fix the defect in a separate focused issue/PR, then rerun acceptance. Do not mix production-code changes into the proof PR because that makes the evidence ambiguous about which implementation was actually accepted.

## Existing implementation to validate

Use the current HPA-503 seams as-is:

- `DTXMania.VideoRecorder/Program.cs`
  - `doctor` validates the local environment and performs read-only OBS Hello/Identify/GetRecordStatus;
  - `record` owns the CX process, OBS recording started by the run, finalization, diagnostics, and sandbox cleanup.
- `DTXMania.VideoRecorder/RecorderCommandLine.cs`
  - `DTXMANIA_VIDEO_OBS_URL` defaults to `ws://127.0.0.1:4455` and must stay loopback;
  - `DTXMANIA_VIDEO_OBS_PASSWORD` carries the local WebSocket secret;
  - `DTXMANIA_VIDEO_OBS_OUTPUT_DIR` is the existing raw OBS output directory;
  - `DTXMANIA_APPDATA_ROOT` may override the source CX app-data root.
- `RecordWorkflow`
  - waits for populated Song Select before preparing the exact chart;
  - starts OBS before prepared preview;
  - requires preview elapsed >= 10,000 ms;
  - observes Song Transition;
  - requires ready AutoPlay Performance with non-zero notes;
  - requires completed/cleared Result with `TotalJudgements == TotalNotes`;
  - captures a Result screenshot barrier and performs a five-second no-input hold;
  - stops only OBS recording owned by this run.
- `RecorderDiagnostics`
  - writes `run.json`, `cx-stdout.log`, and `cx-stderr.log` under `<output>/diagnostics/<run-id>/`;
  - records stage telemetry and OBS outcomes;
  - redacts the known recorder API key and OBS password.

HPA-513 should validate these contracts rather than add another abstraction around them.

## Proof outputs

### Committed files

The HPA-513 execution PR should add only two durable documents unless a separate bug fix becomes necessary:

```text
docs/video-recorder/windows-obs-setup.md
docs/verification/hpa-513-windows-live-recording.md
```

`windows-obs-setup.md` is the reusable operator runbook.  
`hpa-513-windows-live-recording.md` is the sanitized proof record for the accepted run.

### Local retained evidence

Keep the full evidence outside the Git checkout, for example under a dedicated proof directory on the Windows workstation:

```text
<proof-root>/
  doctor.txt
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

The exact local directory layout is not a product contract. The important rule is that raw/published media and unsanitized workstation paths stay out of Git while the committed verification document records enough metadata to reproduce and audit the result.

## OBS configuration contract

Keep OBS setup manual and minimal:

1. Create/select a dedicated DTXManiaCX profile and scene collection.
2. Configure Game Capture for the CX game window/process.
3. Configure application audio capture for CX.
4. Disable Desktop Audio and microphone inputs for the recorded track.
5. Configure Hybrid MP4 recording.
6. Enable obs-websocket 5.x authentication.
7. Set the OBS recording directory to the same path exported as `DTXMANIA_VIDEO_OBS_OUTPUT_DIR`.
8. Keep OBS open and idle before `record` starts.

Do not add scene enumeration, source inspection, screenshots, or auto-repair to the recorder for this ticket. The human inspection below is the acceptance gate for capture correctness.

## Source app-data isolation proof

The recorder is required to use a disposable sandbox and must not mutate normal CX state.

Immediately before the proof run, record presence, byte size, and SHA-256 for the source files when they exist:

```text
Config.ini
songs.db
songs.db-wal
songs.db-shm
```

Repeat the same capture immediately after the successful run.

Acceptance:

- every file present before the run has the same size and SHA-256 afterward;
- files absent before the run are not newly created by the recorder in the source app-data root;
- the successful recorder sandbox has been deleted after finalization;
- the raw OBS file remains in the OBS output directory;
- the published copy and diagnostics remain under the requested output directory.

Do not rely on timestamps alone. Content hashes give a simple deterministic proof and avoid adding any new recorder instrumentation.

## Happy-path native run

### Preflight

From the repository root on the proof workstation:

```powershell
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj --configuration Debug

dotnet run --project DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj --configuration Debug --no-build -- doctor
```

Retain the sanitized doctor output. It must report:

- Windows available;
- repository/game project/source config available;
- source config validation passed;
- raw output directory valid;
- OBS Hello/Identify succeeded;
- OBS recording inactive;
- no OBS state mutation.

`ffprobe` may be absent; that remains a warning-only condition under HPA-503.

### Recording

Run exactly one short indexed chart:

```powershell
dotnet run --project DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj --configuration Debug --no-build -- \
  record --chart "<absolute-chart-path>" --output "<proof-root>\published"
```

Do not put secrets on the command line. OBS password remains in the environment.

The run must finish with exit code `0`, print both raw and published output paths, and leave a completed diagnostics bundle.

## Automated telemetry acceptance

Inspect `run.json` from the successful run. Require:

- `status == Completed`;
- the expected ordered steps are present from `Started` through `Completed`;
- `SongSelectReady` has a non-empty selected song;
- `PreviewReady` reports `preparedPreviewState == Playing` and `preparedPreviewElapsedMs >= 10000`;
- `PerformanceReady` reports `performanceReady == true`, `autoPlayEnabled == true`, and `totalNotes > 0`;
- `ResultCompleted` reports `stageCompleted == true`, `clearFlag == true`, `completionReason == SongComplete`, and `totalJudgements == totalNotes`;
- OBS Connect/Status/Start/Stop all succeeded;
- raw and published paths are recorded;
- `failure`, `failureType`, and retained sandbox path are absent;
- the verifier warning is either absent or only the expected optional-`ffprobe` warning.

The diagnostics are evidence for game/recorder state; they do not replace visual/audio inspection.

## Manual media acceptance

Watch the complete published MP4 at normal speed and confirm:

- the captured content starts with the intended populated Song Select presentation;
- preview audio starts only after Song Select is visibly captured;
- at least ten seconds of real preview audio are present;
- Song Transition is visually complete;
- the whole AutoPlay gameplay is visible and audible;
- BGM/chip audio is present and plausible;
- the Result screen is fully rendered and remains visible for at least five seconds;
- the recording ends after the Result hold;
- no OBS UI, desktop, unrelated window, cursor, notification, microphone, or unrelated application audio is captured;
- CX audio is not duplicated or echoed;
- there is no obvious clipping, severe stutter, aspect-ratio error, or missing frame region that makes the proof unusable.

Do not introduce numeric quality thresholds or frame-analysis tooling for this first proof. HPA-506 owns stricter media policy if real usage needs it.

## Failure and ownership proof

Do not manually replay every portable unit-test scenario. Reuse HPA-503 automated coverage for platform-neutral cleanup and run only the native checks that add real Windows/OBS evidence.

### Re-run focused automated tests

At minimum retain a passing result for recorder tests covering:

- pre-existing OBS recording is never started/stopped by the recorder;
- Start failure does not stop unowned OBS state;
- cancellation after recorder-owned OBS start stops owned recording;
- unexpected stage/performance/result failures stop owned recording;
- finalization preserves the raw artifact and does not delete the sandbox before diagnostics/publication complete.

### Native check 1: pre-existing OBS ownership

1. Start an OBS recording manually.
2. Run `dtx-video record` with the valid proof chart.
3. Require the recorder to fail before starting its own recording.
4. Confirm the manually started OBS recording is still active.
5. Stop that manual recording yourself.

This proves the real OBS ownership boundary without adding recorder APIs.

### Native check 2: cancellation after owned start

1. Start a normal proof recording.
2. After the recorder has started OBS and preview/gameplay is active, press Ctrl+C once.
3. Require the command to exit through its cancellation path.
4. Confirm OBS is no longer recording.
5. Confirm the partial raw OBS artifact and diagnostics are retained when available.
6. Confirm the failed-run sandbox is retained and recorded for diagnostics.

Delete the retained failed-run sandbox manually after inspection.

### Lightweight input failure

Use one invalid or unindexed chart attempt and confirm it fails without starting or stopping unrelated OBS recording. This is sufficient native confirmation for chart validation; do not manufacture a separate destructive CX-launch failure on the proof workstation.

The existing automated suite remains the proof for platform-neutral CX launch/start/timeout cleanup behavior.

## Documentation contract

`docs/video-recorder/windows-obs-setup.md` should contain only the information an operator needs:

- prerequisites;
- exact OBS profile/scene/audio settings;
- required environment variables;
- `doctor` and `record` commands;
- how to choose an indexed chart;
- artifact locations;
- success indicators;
- common troubleshooting for source config normalization, OBS auth, OBS already recording, unindexed chart, missing preview, and optional `ffprobe`.

Do not turn this into general OBS documentation.

`docs/verification/hpa-513-windows-live-recording.md` should record:

- accepted commit SHA;
- Windows/.NET/OBS versions;
- chart identity in a non-sensitive form;
- doctor result summary;
- successful command summary with secrets/absolute private paths redacted;
- raw/published MP4 file names, byte sizes, and SHA-256;
- `run.json` acceptance values;
- source-state before/after comparison;
- native ownership/cancellation check outcomes;
- manual visual/audio checklist result;
- focused automated test command/results;
- any warnings or deviations.

The verification document should not embed raw logs, full local paths, passwords, API keys, or video binaries.

## Stop conditions

The acceptance run is not complete if any of the following occurs:

- `doctor` cannot authenticate to OBS or reports OBS already active;
- the exact chart cannot be prepared from the populated library;
- preview is shorter than ten seconds;
- Song Transition, Performance, or completed Result is missing;
- Result is not held for five seconds;
- the source Config.ini/database/WAL state changes;
- OBS captures desktop/OBS UI/unrelated audio or misses CX audio;
- the recorder leaves OBS active after owned cancellation/failure;
- published/raw media is missing or unreadable;
- diagnostics report failure or inconsistent gameplay totals.

When a stop condition indicates a product defect, file a focused blocker and fix it separately before claiming HPA-513 acceptance.

## Acceptance

HPA-513 is complete when one real Windows x64 recording is accepted end-to-end; the source CX app-data state is proven unchanged; the real OBS ownership/cancellation boundary is demonstrated; the portable recorder regression tests still pass; a minimal reproducible Windows OBS runbook is committed; and a sanitized verification record ties the accepted media/evidence to the exact repository commit.
