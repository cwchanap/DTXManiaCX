# HPA-506 — Strict recording artifacts and MKV fallback (design)

**Linear:** https://linear.app/cwchanap/issue/HPA-506/optional-add-strict-mp4-validation-and-mkv-fallback-remux  
**Delivery:** documentation-only review first; then implementation in **this same PR**, at most 3 engineer days.  
**Prerequisite:** HPA-513 (Windows live recording) is Done; HPA-515 (macOS live recording) is Done.

## Goal and product decision

Make a player-requested strict output mode for `dtx-video record` that either publishes a valid 720p60 H.264/AAC MP4 or returns an actionable error while **keeping the untouched OBS raw recording**. Accept an already-compliant Hybrid MP4 without re-encoding; convert only compatible MKV to MP4 by stream copy.

**Strict mode is explicitly opt-in** (proposed `--strict-media` record flag); the current non-strict recorder behavior remains the default. This is optional hardening of an already proven workflow, not a new recording pipeline. Keep the new code within the existing artifact-verifier ownership boundary.

## Verified codebase baseline (main, 2026-10-26)

- `DTXMania.VideoRecorder/Media/RecordingArtifactVerifier.cs` already owns raw path containment, existence and nonempty-file checks, optional `ffprobe` stream-type checking, destination collision checks and non-destructive publication. Its current default resolver searches PATH.
- `DTXMania.VideoRecorder/Workflow/RecordFinalization.cs` owns the ordered success boundary: verify/publish -> diagnostics -> sandbox deletion. **Keep that ordering.**
- `DTXMania.VideoRecorder/Program.cs` creates the verifier after `RecordWorkflow.RunAsync` succeeds; `RecorderCommandLine.cs` parses the existing `record --chart ... --output ...` arguments.
- `RecorderDiagnostics` already retains timestamps for `ObsStarted` and `ObsStopped`; use those to measure expected capture duration if useful, instead of adding a parallel timer to the workflow.
- `RecordingArtifactVerifierTests.cs` has local temporary-file and fake ffprobe process tests. Extend these, do not add a new test framework or real-OBS dependency.
- Recorder targets `net10.0` **in current main**. Do not change target frameworks as part of this task.

## Behavior contract

1. Non-strict `record` takes its existing path and warning behavior. No new dependency on FFmpeg, ffprobe or new configuration in default mode.
2. Strict mode requires a usable `ffprobe` before it can certify media. A missing, failing or timed-out probe causes a clear failure; it must never report strict success from a filename or file extension alone.
3. Read the actual container and video/audio streams. Require MP4 output, one usable primary H.264 video stream at **1280x720**, nominal **60 fps**, AAC **two-channel** audio and positive duration. Accept rational frame rates near nominal 60 only with an explicit documented tolerance; do not silently accept 30 fps or 59.94 as exact 60 without documenting the choice. Validate format from probe metadata, not path suffix.
4. Compliant MP4: preserve bytes (stage/copy/publish, no ffmpeg transform). Compatible MKV: run **FFmpeg stream-copy remux** with MP4 faststart metadata; probe the candidate MP4 again before publication. If streams are incompatible, fail rather than transcode.
5. Expected recording duration: derive from `ObsStarted` and `ObsStopped` evidence, provided both exist; compare with probed media duration using one named, documented tolerance accounting for OBS startup/stop lag. Prefer exposing a narrow existing-diagnostics elapsed-time helper over altering `RecordWorkflow` ownership/return contract. A missing expected duration is an actionable strict validation failure or an explicitly documented omission, **not a fabricated comparison**.
6. Publish with a unique temporary file in the destination directory, then a collision-safe rename only **after** successful validation. Clean that temporary file on error/cancellation. Keep the raw OBS artifact untouched on *every* outcome. Do not overwrite an existing destination; keep the existing containment policy.
7. Keep platform-independent behavior for Windows x64 and Apple Silicon Mac. Resolve `ffprobe`/`ffmpeg` from an explicit configured path when set, then from a recorder-bundled RID-local binary **if present**, then PATH; otherwise report unavailable. Do **not** ship a new FFmpeg binary or depend on the game's separate native runtime. The existing resolver must continue to work in non-strict mode. Avoid introducing a general binary/package manager.
8. Preserve diagnostic outcome, published path and warnings; do not mark completion or delete the sandbox if strict finalization fails.

## Non-goals / guardrails

- No re-encoding, quality presets, loudness normalization, edits, overlays or support for other media containers.
- No changing `RecordWorkflow` game/OBS lifecycle, OBS scene configuration, recorder automation service or existing Windows/macOS runbooks beyond a small strict-mode usage note.
- No new portable process-execution framework. Reuse the existing timeout/cancellation/process-launch patterns.
- No new binary packaging or high-volume media fixture corpus; use small generated fixtures only where essential.
- Avoid generic abstractions, separate finalizer services or cross-project refactoring unless the existing verifier truly becomes unreadable.

## Acceptance / proof

- Default `record` behavior and its existing tests remain unchanged.
- Strict compliant Hybrid MP4 publishes byte-identically with no remux invocation.
- Strict compatible MKV remuxes without encoding and publishes a fully validated MP4.
- Missing ffprobe, missing audio, corrupt media, wrong codec/size/fps/channels, material duration mismatch, FFmpeg failure, cancellation and output collision all fail without declaring completion; raw survives and no partial published output remains.
- Cross-platform unit tests cover probe parsing, path selection, command construction and publication cleanup. At least one local real FFmpeg/ffprobe smoke test on an explicitly created short media fixture, where installed; if the external tools are absent, report the skip plainly. The manual OBS recording proof stays out of automated CI.
