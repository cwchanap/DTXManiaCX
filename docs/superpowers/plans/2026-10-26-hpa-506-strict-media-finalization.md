# HPA-506 — Strict recorder artifacts: implementation plan

**Ticket:** https://linear.app/cwchanap/issue/HPA-506/optional-add-strict-mp4-validation-and-mkv-fallback-remux  
**Design:** [2026-10-26-hpa-506-strict-media-finalization-design.md](../specs/2026-10-26-hpa-506-strict-media-finalization-design.md)  
**Execution:** one Linear issue, **one PR** (this PR), <=3 engineer days. Current commit is planning/documentation only; production edits and tests follow on the same branch after review.

## First: reuse and verify

Read `RecordingArtifactVerifier`, `RecordFinalization`, `RecordWorkflow`, `RecorderDiagnostics`, `RecorderCommandLine`, `Program`, and corresponding tests before editing. The existing verifier is the single owner of publish policy; finalization already owns the success and sandbox-deletion boundary. Confirm these facts remain true on the branch.

**Critical constraint:** Do not split this into another implementation PR or introduce an alternative recording/finalization path. Do not move OBS recording lifecycle responsibilities into media validation.

## Implementation steps (ordered, reviewable commits within this PR)

### 1. Opt-in strict contract and probe model — ~0.5 day

- Add one `record --strict-media` boolean option to `RecorderCommand` / `RecorderCommandLine`, default false; preserve `doctor` and existing `record` invocation semantics. Add parsing and usage tests.
- Extend the existing verifier with a strict-mode argument/options seam, leaving the default path untouched. Keep the current verifier as the owner of all raw path checks and publishing.
- Expand the existing `ffprobe` JSON request/parse logic **only in strict mode** to inspect container, relevant stream metadata, duration and rational frame rate. Use a small immutable media-info record or pure parser; do not introduce a generic multimedia object model.
- Resolve the tool paths in one narrow helper, using explicit recorder path configuration -> recorder-local RID binary if present -> PATH. Check the actual platform launch policy (including Windows batch wrappers) rather than assuming found files are executable. Avoid adding package/binary downloads.

### 2. Validate MP4 and remux compatible MKV — ~1 day

- Strict MP4: validate and copy to a unique destination-side temporary filename; only then rename/publish to the collision-checked target. **No re-encode or faststart rewrite** of a compliant MP4.
- Strict MKV: reject bad codecs or streams before invoking `ffmpeg`; run stream-copy output to a destination-side temporary file (`-c copy -movflags +faststart`, explicit `-f mp4` for a non-.mp4 temporary suffix). Probe the remuxed result again and enforce the same final contract.
- Use the current process timeout, redirected output and cancellation safeguards; ensure subprocess termination on timeout/cancel. Keep output messages actionable.
- Preserve raw OBS input; never delete or move it. Leave neither a partially published file nor a stranded temp file after strict failure. Do not overwrite collisions.
- Derive capture-duration expectation from existing diagnostic `ObsStarted` and `ObsStopped` step timestamps via a small read-only method, avoiding a new stopwatch or workflow result type. Choose and document a modest tolerance justified by OBS latency and test its boundary.

### 3. Wire the existing success boundary and diagnostics — ~0.25 day

- In `Program.RunRecordAsync`, send the flag and measured capture duration into the verifier. Keep `RecordFinalization.CompleteAsync` callbacks/order unchanged.
- Failure means the existing exception path writes diagnostics and retains the sandbox; success still records raw/published paths and deletes the sandbox in the existing order. Keep `doctor` mostly unchanged; it may report strict-tool readiness but should not turn optional FFmpeg into a default failure.
- Add short CLI usage notes to the recorder runbook. Do not expand the OBS doctor or create a configuration UI.

### 4. Focused verification — ~0.75–1 day

Extend `DTXMania.VideoRecorder.Tests` (mainly `Media/RecordingArtifactVerifierTests.cs`, `RecorderCommandLineTests.cs` and existing finalization tests):

- Strict ffprobe model accepts valid H.264 1280x720 ~60 fps / AAC stereo MP4; refuses missing stream, corrupt JSON, wrong codec, size, fps, channels, zero/invalid duration and wrong container.
- Valid strict MP4 remains byte-identical and **does not invoke ffmpeg**. Compatible MKV invokes exactly stream-copy remux, validates the resulting MP4, then publishes.
- Missing tools, failed/timeout subprocess, cancellation, wrong raw root, existing destination and duration mismatch preserve the raw file, leave no published output/temp residue and never mark run completed.
- Default non-strict fake ffprobe/warning tests remain green. Exercise existing Windows batch quoting/resolution tests; add only targeted cases if changed.
- Run `dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj` and relevant recorder build on the available host; check that the tests actually run (nonzero count). Windows CI and macOS CI are required for their supported platforms.
- If FFmpeg and ffprobe are installed locally, generate a small real 2-second test media file for one end-to-end strict MP4/MKV verification without OBS; report binary versions and results. If absent, document that limitation instead of claiming live validation.

## Scope cut if estimated work exceeds 3 engineer days

Keep strict codec/dimension/fps/audio verification, non-destructive staging and compliant MP4 publication as the core. **Before coding**, raise the decision to the project lead if the MKV remux and real-tool proof would push beyond 3 days; do not silently split one ticket into multiple PRs or introduce elaborate test infrastructure. No unrelated cleanup.

## Implementation acceptance checklist

- [ ] Docs reviewed; design stays within existing verifier/finalization architecture
- [ ] Explicit opt-in flag; legacy behavior remains intact
- [ ] Strict MP4 / MKV behavior and tool availability policy covered by tests
- [ ] Raw preserved; no published artifacts after strict validation failures
- [ ] Cross-platform test suites run and results recorded, including nonzero discovery
- [ ] Real short-fixture proof (or explicit local-tool limitation), no claim of live OBS proof
- [ ] Update this PR description with implementation/evidence and mark Ready only after completion
