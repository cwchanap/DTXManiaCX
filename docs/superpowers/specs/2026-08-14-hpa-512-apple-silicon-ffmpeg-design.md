# HPA-512 Native Apple Silicon FFmpeg Design

**Issue:** HPA-512 — Package native Apple Silicon FFmpeg for CX gameplay audio  
**Date:** 2026-08-14  
**Status:** Revised after second planning review

## Goal

Make the supported macOS build self-contained for the existing CX audio paths on native Apple Silicon:

```text
dotnet build / dotnet run / dotnet test / dotnet publish
-> runtimes/osx-arm64/MMTools/ffmpeg
-> runtimes/osx-arm64/MMTools/ffprobe
-> FfmpegRuntime resolves the bundled arm64 runtime
-> MP3 preview + non-default playback audio variants work without Rosetta or user-installed FFmpeg
```

Keep HPA-512 to one 2–3 engineer-day implementation PR after its CI prerequisite lands. This is packaging/build plumbing around existing audio code, not an FFmpeg, recorder, or audio-engine redesign.

## Prerequisite: establish real Mac test coverage first

HPA-623 must land before HPA-512 implementation.

Current `build-and-test.yml` builds only `DTXMania.Game.Mac.csproj`, then invokes `dotnet test DTXMania.Test.Mac.csproj --no-build`. On current `main`, that command exits after a sub-second MSBuild success without a `Test run for ...` line or test summary, and the following coverage step warns that `coverage.cobertura.xml` is missing.

Therefore the current Mac suite is **not** a merge gate for `DTXMania.Test.Mac` today. HPA-623 owns the baseline repair:

- build `DTXMania.Test.Mac.csproj` before its `--no-build` test step, or equivalently remove `--no-build` if that is the smaller fix;
- make missing Mac test coverage fail instead of warn;
- resolve any pre-existing failures without mixing FFmpeg packaging changes into that PR;
- get a green `main` baseline before HPA-512 starts.

HPA-512 may rely on this repaired baseline; it must not be the PR that causes the full Mac suite to execute for the first time.

## Why HPA-512 remains the next audio slice

HPA-513 is the next Windows recorder acceptance milestone, but the Windows implementation sequence is separate from this Mac audio packaging work.

HPA-512 is still a real prerequisite for HPA-515 Apple Silicon recorder parity, but for a narrower reason than the recorder itself:

- HPA-515 requires the **CX game process** to decode MP3 preview audio and prepare gameplay audio natively on arm64;
- `DTXMania.VideoRecorder` does not reference the Game project and does not consume the bundled audio runtime for artifact verification;
- `RecordingArtifactVerifier` continues resolving `ffprobe` from `PATH` for MP4 stream inspection.

The runtime produced by HPA-512 is intentionally audio-only. It does not need MP4/H.264 support and does not replace the recorder's PATH `ffprobe` contract.

## Current state

The repository already contains the runtime behavior HPA-512 should reuse:

- `DTXMania.Game.Mac.csproj` references `FFMpegCore` plus the x64-only `MMTools.Executables.MacOS.X64` package.
- `FfmpegRuntime.GetFFmpegBinaryFolder` already prefers `runtimes/osx-arm64/MMTools` before `osx-x64`; do not add another resolver.
- bundled resolution returns a non-null `BinaryFolder`; PATH fallback returns `BinaryFolder: null`, which gives tests a clean way to prove the bundled runtime won.
- `FfmpegRuntime.IsRunnableFile` intentionally requires Unix execute bits; do not weaken it.
- `release.yml` already builds FFmpeg 7.0.2 from the official tarball with pinned SHA-256 `8646515b638a3ad303e23af6a3587734447cb8fc0a0c064ecdb8e95c4fd8b389`.
- the release recipe explicitly enables `adpcm_ima_wav` and `adpcm_ms` because non-default playback routes WAV through FFmpeg.
- `FfmpegAudioVariantProcessorTests.PrepareAsync_EncodedAudio_ShouldNormalizeToRawPcm` is already the strongest real variant gate, but its MP3/OGG fixtures are generated at test time with encoders the minimal production runtime intentionally does not ship.

## Approaches considered

### A. Extract the proven source build, make the Mac project own generated runtime content, and repair the existing real-audio test — chosen

Reuse the current release recipe, make host reproducibility explicit, use one generated-content copy contract, and retain the stronger existing audio test with committed encoded fixtures.

**Pros**

- one upstream version/checksum;
- no new binary supplier or NuGet runtime package;
- one resolver and one runtime layout;
- one Game/Test/publish copy contract;
- preserves the stronger `PlaybackModifiers(50, 12)` gate;
- keeps Windows behavior unchanged.

**Cons**

- first Apple Silicon build compiles FFmpeg;
- generated MSBuild content, Mach-O dependencies, and execute bits need explicit verification.

### B. Add a third-party arm64 FFmpeg binary/NuGet provider — rejected

The repository already owns a pinned, checksum-verified upstream recipe. Another supplier adds provenance and maintenance without solving a missing architectural capability.

### C. Add MP3/OGG encoders to production for tests — rejected

Production only decodes these formats. Commit tiny project-owned encoded fixtures instead of widening the shipped runtime to satisfy test setup.

## Chosen design

### 1. One reproducible source builder

Add:

```text
tools/ffmpeg/macos-arm64/build-runtime.sh
tools/ffmpeg/macos-arm64/README.md
```

Keep FFmpeg 7.0.2 and the existing source checksum. Start from the current minimal release recipe, with one intentional reproducibility change:

```text
--disable-autodetect
```

Use it unconditionally. HPA-512 wants the same binary regardless of whether a developer machine happens to have Homebrew libraries installed.

Do **not** add `--disable-gpl` or `--disable-nonfree`. With `--disable-everything` and no external `--enable-lib*` dependencies, they do not add a useful HPA-512 gate; document that decision rather than spending another cold build on it.

Required runtime surface remains:

```text
decoders:
  mp3float
  vorbis
  pcm_s16le / pcm_s24le / pcm_f32le / pcm_u8 / pcm_alaw / pcm_mulaw
  adpcm_ima_wav / adpcm_ms

demuxers:
  mp3 / wav / ogg / pcm_s16le
parsers:
  mpegaudio / vorbis
protocols:
  file / pipe
encoder + muxer:
  pcm_s16le
filters:
  aformat / anull / aresample / atempo / apad / atrim
```

The builder must:

1. fail on any host other than native `Darwin arm64`;
2. download the exact official source tarball on cache miss;
3. verify the pinned SHA-256 before extraction;
4. configure/build/install with `--disable-autodetect` and no external codec libraries;
5. verify `ffmpeg` and `ffprobe` are executable arm64 Mach-O binaries;
6. verify filters, demuxers, encoder/muxer, and **all load-bearing decoders including `adpcm_ima_wav` and `adpcm_ms`**;
7. run `ffmpeg -version` and `ffprobe -version` successfully;
8. run `otool -L` on both executables and fail if any dependency resolves outside system locations (`/usr/lib` or `/System/Library`);
9. cache only a fully validated runtime under a versioned user cache;
10. copy `ffmpeg`, `ffprobe`, and `COPYING.LGPLv2.1` to caller-provided destinations with explicit modes.

Use:

```text
${DTXMANIA_FFMPEG_CACHE_ROOT:-$HOME/Library/Caches/DTXManiaCX/ffmpeg}/7.0.2/osx-arm64/
```

The cache is an optimization only. Do not add cache locking, a package-builder project, download service, or generic bootstrap framework.

### 2. The Mac project owns one generated-content copy contract

Remove only `MMTools.Executables.MacOS.X64`; keep `FFMpegCore` and `FfmpegRuntime` unchanged.

Stage generated files under a project-local intermediate root:

```text
$(BaseIntermediateOutputPath)ffmpeg-runtime/
  runtimes/osx-arm64/MMTools/ffmpeg
  runtimes/osx-arm64/MMTools/ffprobe
  Licenses/FFmpeg-LGPL-2.1.txt
```

Keep the existing macOS-only target hook before:

```text
BeforeBuild
AssignTargetPaths
GetCopyToOutputDirectoryItems
```

The target remains macOS-only because Windows tests evaluate/reference the Mac game project.

Avoid rebuilding/revalidating the runtime on every MSBuild invocation. Put the cache/staging existence condition on the **`Exec` only**:

```xml
<Exec
  Condition="!Exists('$(NativeFfmpegRuntimeDir)/ffmpeg') Or !Exists('$(NativeFfmpegRuntimeDir)/ffprobe')"
  Command="bash .../build-runtime.sh ..." />
```

The generated `ItemGroup` must remain unconditional within the target so copy items are declared on every relevant project invocation even when the files are already staged.

One target must propagate the runtime/license to:

```text
Game bin
-> DTXMania.Test.Mac bin through ProjectReference
-> publish output
-> existing .app copy
```

Do not add a Test.Mac-specific FFmpeg copy implementation.

Execute bits remain part of the contract. Verify them in Game, Test.Mac, publish, and `.app` output; add a narrow macOS chmod hook only if copying is empirically lossy.

**Intel consequence:** because the builder rejects non-arm64 and the Mac project requires this generated runtime, `DTXMania.Game.Mac.csproj` no longer builds at all on Intel Macs. This is intentional for the currently supported Mac target; it is stronger than merely saying there is no playback fallback.

### 3. Repair existing Audio coverage and add one real bundled-runtime guard

Add project-owned fixtures:

```text
DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3
DTXMania.Test/TestData/Audio/ffmpeg-tone.ogg
```

Copy them from both test projects, then repair `PrepareAsync_EncodedAudio_ShouldNormalizeToRawPcm`:

- generated WAV row stays;
- MP3/OGG rows read committed fixtures;
- remove test-time `libmp3lame` / `libvorbis` encoding;
- retain `PlaybackModifiers(50, 12)` and existing assertions.

Do not add a weaker second variant smoke.

Put the packaged-runtime filesystem test in a **new Audio-trait class**, not `FfmpegRuntimeTests` (which is a pure Unit-trait class):

```text
DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs
```

On native Apple Silicon it must assert:

- `EnsureConfigured().IsAvailable` is true;
- `BinaryFolder` is **non-null**;
- `BinaryFolder` equals the Test.Mac output `runtimes/osx-arm64/MMTools` directory;
- both binaries exist and retain Unix execute bits.

The non-null assertion is the PATH guard: PATH success returns `BinaryFolder: null`.

Keep `ManagedSoundFFmpegPathTests` and resolver logic unchanged. Add one valid MP3 `ManagedSound` load using the committed fixture and fix the stale user-facing message naming the removed x64 NuGet package.

### 4. HPA-512 CI validates packaging without PATH masking

After HPA-623 has made the Mac suite real and green, HPA-512 can add its runtime-specific CI checks:

1. use an explicit Apple Silicon macOS runner instead of relying on `macos-latest` drift;
2. cache only `~/Library/Caches/DTXManiaCX/ffmpeg`;
3. build Game and verify bundled arm64 runtime files;
4. build Test.Mac and verify ProjectReference propagation;
5. run the focused Audio/bundled-runtime checks;
6. run the already-established full Mac suite with `--no-build`;
7. retain Automation, VideoRecorder, and prepared-chart AudioE2E.

The existing `brew install ffmpeg` used by the CX Neon SFX validator must stay **after** the HPA-512 Audio/bundled-runtime checks. Otherwise a full Homebrew runtime on PATH can mask a broken bundle and defeat the new guard.

### 5. Release consumes project-owned publish output

Delete the duplicated FFmpeg source-build body from `release.yml` once `dotnet publish` produces the runtime itself.

Keep short fail-closed checks for runtime presence, execute bits, arm64 architecture, and license in publish and `.app` output. Capability and system-library checks belong in `build-runtime.sh`, not duplicated YAML.

Do not modify `build-dmg.sh` unless packaged-output verification proves it loses execute bits.

### 6. Recorder boundary stays explicit

HPA-512 ships an audio-focused runtime for CX. It does not wire the bundled runtime into `DTXMania.VideoRecorder` and does not add MP4/H.264 demux/decode features.

HPA-515 continues to use:

```text
CX Game -> bundled HPA-512 FFmpeg for preview/gameplay audio
Recorder artifact verification -> PATH ffprobe
```

This keeps HPA-512 a valid HPA-515 prerequisite without implying that the recorder gained a bundled media verifier.

## Risks and mitigations

### First-run build latency can affect E2E launches

**Risk:** a clean cache miss inside `dotnet run`/project build compiles FFmpeg and can make an E2E launch look hung or hit startup timeout.

**Mitigation:** warm/cache the runtime in CI before E2E execution and mention the first-build behavior in README/troubleshooting. Do not add background bootstrap machinery.

### Host libraries leak into the runtime

**Risk:** a developer machine with Homebrew could produce a runtime that works locally but links non-system dylibs unavailable on another Mac.

**Mitigation:** `--disable-autodetect` from the start plus `otool -L` fail-closed verification allowing only system library locations.

### Generated content may not propagate or may lose execute bits

**Risk:** Game works but Test.Mac/publish does not, or `IsRunnableFile` rejects copied binaries.

**Mitigation:** one generated-content target, explicit Game/Test/publish verification, and narrow chmod only if empirically necessary.

### PATH can hide packaging failures

**Risk:** a Homebrew FFmpeg makes `EnsureConfigured().IsAvailable` true despite missing bundled files.

**Mitigation:** the Audio packaged-runtime test requires non-null/equal `BinaryFolder`; install Homebrew FFmpeg only after those checks.

### Intel Mac builds fail

**Risk:** contributors on Intel Macs cannot build the Mac game project after this change.

**Mitigation:** state this explicitly as the chosen support boundary. Do not add Rosetta/Intel compatibility in HPA-512.

## Scope boundaries

In scope:

- extract/cache the existing native FFmpeg source build;
- deterministic host-independent configure (`--disable-autodetect`);
- capability + system-dylib verification;
- Mac build/test/publish generated-content integration;
- x64 MMTools package removal;
- repair existing real-FFmpeg Audio fixtures/tests;
- dedicated bundled-runtime Audio guard;
- one successful ManagedSound MP3 test;
- stale diagnostic cleanup;
- native CI packaging checks after HPA-623;
- release deduplication and license material.

Out of scope:

- FFmpeg version upgrade;
- Intel macOS or Rosetta support;
- Windows runtime changes;
- new resolver/audio engine/codec matrix;
- production MP3/OGG encoders solely for tests;
- recorder bundled ffprobe or MP4/H.264 support;
- OBS/ScreenCaptureKit work;
- signing/notarization redesign;
- generic dependency/bootstrap framework.

## Implementation shape

**Pre-req PR:** HPA-623 makes `DTXMania.Test.Mac` a real green CI gate and fails zero-test/missing-coverage runs.

Then HPA-512 remains one implementation PR with four reviewer-sized checkpoints:

1. reproducible cached FFmpeg builder with `--disable-autodetect`, full decoder checks, and `otool -L` portability gate;
2. incremental macOS-only generated MSBuild copy contract and Game/Test/publish verification;
3. committed fixtures, repaired existing variant test, dedicated bundled-runtime Audio guard, ManagedSound MP3 check, and diagnostic cleanup;
4. runtime-specific CI checks plus release YAML deduplication, keeping PATH/Homebrew FFmpeg after the bundle-sensitive tests.

After HPA-512 merges, HPA-515 gets the native **CX audio** prerequisite it needs; recorder artifact probing remains a separate PATH-based concern.