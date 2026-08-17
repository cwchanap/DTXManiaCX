# HPA-515 Apple Silicon recorder parity — verification record

> **Status: PARTIAL — code complete and host-validated; live OBS acceptance
> pending.** All automated sections below were captured on the recorded
> host/commit. Sections marked **PENDING** require OBS Studio to be
> installed and manually configured (see
> `docs/video-recorder/macos-obs-setup.md`); they must be completed before
> HPA-515 is accepted.

## Recorded environment

- Implementation commit: `900b1940` (`feat: preflight native Mac recorder
  before OBS`); docs commit adds this record.
- Host: MacBookPro18,3 (Apple M1 Pro), native `arm64`
- macOS: 26.5.2
- .NET SDK: 10.0.100

## Debug build (the exact artifact the recorder launches)

Command (run immediately before evidence capture):

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
```

Result: 0 errors (140 pre-existing nullable warnings).

## Architecture evidence (`file` on the launched output)

```text
DTXMania.Game/bin/Debug/net8.0/DTXMania.Game.Mac:
    Mach-O 64-bit executable arm64
DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffmpeg:
    Mach-O 64-bit executable arm64
DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffprobe:
    Mach-O 64-bit executable arm64
```

## Doctor result (pre-OBS gates)

`dtx-video doctor` on this host reported:

- Recorder configuration validation: passed
- macOS >= 13: passed
- Apple Silicon (arm64): passed
- Mac game project + project exists: passed (`DTXMania.Game/DTXMania.Game.Mac.csproj`)
- Bundled ffmpeg + ffprobe: passed (both under
  `bin/Debug/net8.0/runtimes/osx-arm64/MMTools/`)
- Source config + validation: passed
  (`~/Library/Application Support/DTXManiaCX/Config.ini`)
- Raw output directory: passed
- ffprobe on PATH: available (optional, final MP4 verification only)
- **OBS auth/status: FAILED — OBS Studio is not installed on this host.**
  This is operator setup, not a recorder defect; the gate failure is the
  expected behavior.
- OBS state mutation: none (doctor is read-only)

Doctor also printed the macOS manual OBS prerequisites (ScreenCaptureKit
source scoping, application audio, disabled Desktop Audio/microphone,
Hybrid MP4, Screen Recording permission, authenticated WebSocket) as
guidance only.

## Automated tests (all on this Mac)

- HPA-512 focused native audio (same filter as macOS CI):
  `FfmpegAudioVariantProcessorTests | FfmpegBundledRuntimeTests |
  ManagedSoundTests` — 86/86 passed
- `DTXMania.VideoRecorder.Tests` — 104/104 passed
- `DTXMania.Automation.Tests` — 66/66 passed

Key regression proofs included in those suites:

- `--no-build` launch honored by `GameProcessDriver` (prebuilt child runs
  after source is corrupted; a silent rebuild would fail the test)
- Failed preflight rejects before sandbox/OBS construction and needs no OBS
  server (mutation-tested: moving the rejection after sandbox creation
  fails the test)
- Both Windows and Mac app-data branches run on every host via injected
  facts (full `UserProfile -> Personal -> $HOME` Mac fallback chain)
- Doctor reports a synthetic passing Mac preflight with no Windows-only
  gate remaining

## PENDING — live OBS acceptance

The following require OBS Studio 30.2+ installed and manually configured
(dedicated profile/collection/scene, ScreenCaptureKit application capture
scoped to CX, CX application audio, Desktop Audio/microphone disabled,
Hybrid MP4, authenticated obs-websocket, Screen Recording permission
granted):

- OBS version recorded; doctor OBS auth/status gate passing
- One acceptance chart (short, Song Select-indexed, encoded preview audio)
  recorded via the full journey (Song Select → preview ≥ 10 s → transition
  → full AutoPlay performance → Result ≥ 5 s hold → recorder-owned stop)
- `run.json` acceptance values (status/song/preview/performance/result/
  judgements/OBS events/raw+published paths)
- Source app-data `Config.ini` / `songs.db` / WAL / SHM SHA-256 comparison
  before vs. after (must be unchanged)
- One Ctrl+C cancellation case after recorder-owned OBS start (recorder
  work stops, diagnostics retained, sandbox retained, unrelated OBS
  ownership untouched)
- Manual viewing of the complete published MP4 against the media checklist
  (Song Select first, audible preview/gameplay audio, full Result hold, no
  desktop/mic/cursor/unrelated audio, no duplicated audio, no aspect
  squeeze; ScreenCaptureKit content only)
- Published MP4 name, size, SHA-256

Strict codec/frame-rate/duration enforcement remains deferred to HPA-506.

## Warnings / deviations

- Doctor's macOS version gate detail renders a converted version label on
  Darwin 25+ hosts (display-only; gate outcome uses the correct comparison).
- Windows doctor output no longer prints repository/game-project gates:
  Windows preflight intentionally has no native gates.
- The chmod-based "non-executable runtime" preflight test is a no-op on
  Windows hosts (executable bit is Unix-only); macOS CI covers it.
