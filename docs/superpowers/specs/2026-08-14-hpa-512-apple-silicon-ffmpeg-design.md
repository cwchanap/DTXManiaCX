# HPA-512 Native Apple Silicon FFmpeg Design

**Issue:** HPA-512 — Package native Apple Silicon FFmpeg for CX gameplay audio  
**Date:** 2026-08-14  
**Status:** Revised after planning review

## Goal

Make the supported macOS build self-contained for the existing CX audio paths on native Apple Silicon:

```text
dotnet build / dotnet run / dotnet test / dotnet publish
-> runtimes/osx-arm64/MMTools/ffmpeg
-> runtimes/osx-arm64/MMTools/ffprobe
-> FfmpegRuntime resolves the bundled arm64 runtime
-> MP3 preview + non-default playback audio variants work without Rosetta or user-installed FFmpeg
```

Keep the task to one 2–3 engineer-day implementation PR. This is packaging/build plumbing around existing audio code, not an FFmpeg or audio-engine redesign.

## Why this is the next slice

HPA-513 is the next Windows acceptance milestone, but the HPA-503 implementation is still stacked behind the open HPA-503 planning PR before it reaches `main`.

HPA-512 is high priority, unblocked, and independently blocks HPA-515 Apple Silicon recording parity.

## Current state

The repository already contains most runtime behavior needed by HPA-512:

- `DTXMania.Game.Mac.csproj` references `FFMpegCore` plus the x64-only `MMTools.Executables.MacOS.X64` package.
- `FfmpegRuntime` already looks for `runtimes/osx-arm64/MMTools` before `osx-x64` and requires both `ffmpeg` and `ffprobe` to be runnable.
- `ManagedSound` and `FfmpegAudioVariantProcessor` already share `FfmpegRuntime`; no second runtime resolver is needed.
- `release.yml` already builds a native arm64 FFmpeg 7.0.2 from the official tarball, pins SHA-256 `8646515b638a3ad303e23af6a3587734447cb8fc0a0c064ecdb8e95c4fd8b389`, and validates the minimal codecs/filters CX actually uses.
- that native runtime is created only after `dotnet publish`, so ordinary build/test output still resolves the x64 package or PATH.

Two existing behaviors materially affect the design:

1. `FfmpegAudioVariantProcessorTests.PrepareAsync_EncodedAudio_ShouldNormalizeToRawPcm` is already the strongest real variant test. It runs real `FfmpegRuntime`, real `FfmpegAudioVariantProcessor`, and `new PlaybackModifiers(50, 12)` over WAV/MP3/OGG. Today its MP3/OGG rows first encode fixtures with `libmp3lame` / `libvorbis` through the configured FFmpeg. The shipping minimal arm64 runtime intentionally has only the `pcm_s16le` encoder, so replacing the x64 full build without repairing this test would make Mac Audio tests fail before the product path is exercised.
2. Mac CI currently builds only the Game project and then runs `DTXMania.Test.Mac` with `--no-build`. A generated runtime must therefore propagate through the Game `ProjectReference`, and the test project must be built explicitly before its `--no-build` suite.

Also, `DTXMania.Test.csproj` references `DTXMania.Game.Mac.csproj` on Windows. Any native bootstrap target in the Mac project must be explicitly macOS-only.

## Approaches considered

### A. Extract the proven source build, make the Mac project own one generated-content copy contract, and repair existing real-audio tests — chosen

Reuse the current release recipe, add one cached builder, generate the native runtime before MSBuild resolves copy items, and keep the existing audio test as the real variant gate using committed encoded fixtures.

**Pros**

- one upstream source/version/checksum;
- no new binary publisher or package layout;
- one runtime resolver and one runtime path;
- one build/test/publish copy contract;
- preserves the existing stricter `PlaybackModifiers(50, 12)` test instead of adding weaker duplicate coverage;
- Windows behavior stays unchanged.

**Cons**

- first Apple Silicon build compiles FFmpeg once;
- generated MSBuild content and Unix execute bits need explicit verification.

### B. Add a third-party arm64 FFmpeg NuGet/binary provider — rejected

This reduces first-build time but adds another supplier, update cadence, license/provenance surface, and layout adaptation. The repository already has a working upstream-source recipe.

### C. Expand the bundled runtime with MP3/OGG encoders only to preserve test fixture generation — rejected

Production does not need `libmp3lame` or `libvorbis` encoding. Adding external encoder dependencies solely because tests generate their own inputs increases the runtime and licensing/dependency surface. Commit tiny project-owned MP3/OGG fixtures instead.

## Chosen design

### 1. One source of truth for the native runtime

Add:

```text
tools/ffmpeg/macos-arm64/build-runtime.sh
tools/ffmpeg/macos-arm64/README.md
```

Extract the currently shipping configure recipe from `release.yml` first, unchanged:

```text
FFmpeg 7.0.2
source: https://ffmpeg.org/releases/ffmpeg-7.0.2.tar.xz
SHA-256: 8646515b638a3ad303e23af6a3587734447cb8fc0a0c064ecdb8e95c4fd8b389

--enable-static --disable-shared
--disable-doc --disable-htmlpages --disable-manpages
--disable-podpages --disable-txtpages
--disable-ffplay
--disable-everything

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

`--disable-autodetect`, `--disable-gpl`, and `--disable-nonfree` are sensible hardening but are **not** part of the proven recipe today. Do not make extraction depend on them. After a cold baseline build passes all existing architecture/capability checks, the implementation may add those three flags in the same PR only if another clean cold build remains green. Otherwise retain the proven flags and record the decision in the README.

The script must:

1. reject non-macOS/non-arm64 hosts with `Native CX macOS runtime requires Apple Silicon (arm64); Intel/Rosetta is not supported.`;
2. download the exact source tarball on a cache miss;
3. verify the pinned SHA-256 before extraction;
4. configure/build/install the minimal runtime;
5. independently verify `ffmpeg` and `ffprobe` are executable arm64 Mach-O binaries;
6. verify the required filters, decoders, demuxers, encoder, and muxer already checked by `release.yml`;
7. run `ffmpeg -version` and `ffprobe -version` successfully;
8. cache the validated runtime under a versioned user cache;
9. copy `ffmpeg`, `ffprobe`, and `COPYING.LGPLv2.1` to caller-provided destinations.

Use:

```text
${DTXMANIA_FFMPEG_CACHE_ROOT:-~/Library/Caches/DTXManiaCX/ffmpeg}/7.0.2/osx-arm64/
```

The cache is only an optimization. Deleting it forces a verified rebuild. Do not add a package-builder project, download service, lock daemon, or generic dependency bootstrap framework.

### 2. The Mac project owns a generated MSBuild copy contract

Remove only:

```xml
<PackageReference Include="MMTools.Executables.MacOS.X64" Version="1.0.6" />
```

Keep `FFMpegCore` and the Windows project unchanged.

Stage generated files under a project-local intermediate root, for example:

```text
$(BaseIntermediateOutputPath)ffmpeg-runtime/
  runtimes/osx-arm64/MMTools/ffmpeg
  runtimes/osx-arm64/MMTools/ffprobe
  Licenses/FFmpeg-LGPL-2.1.txt
```

The target must be explicit because these files do not exist during normal project evaluation and must also be visible when a referencing project asks for copy-to-output items:

```xml
<Target Name="PrepareNativeFfmpegRuntime"
        Condition="$([MSBuild]::IsOSPlatform('OSX'))"
        BeforeTargets="BeforeBuild;AssignTargetPaths;GetCopyToOutputDirectoryItems">
  <Exec Command="bash .../tools/ffmpeg/macos-arm64/build-runtime.sh ..." />
  <ItemGroup>
    <!-- Add generated None items here, after generation. -->
    <!-- Each item sets TargetPath, CopyToOutputDirectory=PreserveNewest, -->
    <!-- and CopyToPublishDirectory=PreserveNewest. -->
  </ItemGroup>
</Target>
```

Required target paths:

```text
runtimes/osx-arm64/MMTools/ffmpeg
runtimes/osx-arm64/MMTools/ffprobe
Licenses/FFmpeg-LGPL-2.1.txt
```

This one target must satisfy:

```text
Game bin output
-> ProjectReference copy into DTXMania.Test.Mac bin
-> publish output
-> .app bundle via existing cp -R
```

Do not add a second copy target to the test project.

Unix execute bits are part of the contract because `FfmpegRuntime.IsRunnableFile` intentionally rejects non-executable bundled binaries. Verify `test -x` in Game, Test.Mac, publish, and `.app` output. If MSBuild copy is proven to lose execute bits, add the smallest macOS-only post-copy `chmod +x` hook needed to the existing project flow; do not weaken `IsRunnableFile`.

The macOS condition is mandatory. Windows CI builds `DTXMania.Test.csproj`, which references the Mac game project; that build must not invoke `build-runtime.sh`.

### 3. Repair the existing real-FFmpeg tests instead of duplicating them

Add two tiny project-owned fixtures:

```text
DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3
DTXMania.Test/TestData/Audio/ffmpeg-tone.ogg
```

Generate them once from the same deterministic sine tone using a full developer FFmpeg, then commit them. They are test inputs, not production runtime assets.

Update both test project files to copy `TestData/Audio/**/*` to test output.

Modify `PrepareAsync_EncodedAudio_ShouldNormalizeToRawPcm`:

- keep the WAV row generated by the existing tone/WAV helper;
- use the committed MP3 fixture for the MP3 row;
- use the committed OGG fixture for the OGG row;
- delete the test-time `libmp3lame` / `libvorbis` encoding step;
- keep `new PlaybackModifiers(50, 12)` and all current duration/frequency assertions.

Do **not** add a second `PlaybackModifiers(125, 0)` variant smoke. The existing test is stricter and already exercises the real product processor.

Add only missing product gates:

1. a native Apple Silicon check in the existing `FfmpegRuntimeTests` that `EnsureConfigured().BinaryFolder` is the test assembly's `runtimes/osx-arm64/MMTools` directory and both files exist with Unix execute bits;
2. one successful valid-MP3 load in `ManagedSoundTests` using `ffmpeg-tone.mp3` and asserting non-zero duration.

Leave `ManagedSoundFFmpegPathTests` unchanged. It already proves a complete arm64 candidate wins over a complete x64 candidate. Leave the separate `FfmpegRuntimeCoverageTests` first-complete-candidate test unchanged as well.

Update the stale `ManagedSound.LoadMp3File` missing-binary diagnostic so it no longer tells macOS users to install `MMTools.Executables.MacOS.X64`. Use one generic bundled-runtime message; this is copy correction, not resolver redesign.

### 4. Native CI makes the copy contract observable

Change regular Mac CI to:

```yaml
runs-on: macos-15
```

Add an Actions cache for:

```text
~/Library/Caches/DTXManiaCX/ffmpeg
```

keyed by Apple Silicon plus `hashFiles('tools/ffmpeg/macos-arm64/build-runtime.sh')`.

Make build order explicit rather than relying on a focused `dotnet test` side effect:

```text
restore DTXMania.Test.Mac.csproj
build DTXMania.Game.Mac.csproj --no-restore
verify Game bin ffmpeg/ffprobe: test -x + file -b arm64
build DTXMania.Test.Mac.csproj --no-restore
verify Test.Mac bin ffmpeg/ffprobe: test -x + file -b arm64
run focused existing Audio tests + new runtime/MP3 checks with --no-build
run the existing full Mac suite with --no-build
run Automation / VideoRecorder / prepared-chart AudioE2E as today
```

This makes ProjectReference propagation a named build gate, not an accidental prerequisite of another test step.

Windows validation must continue to build/test `DTXMania.Test.csproj`; success proves the Mac project reference does not execute the native bootstrap on Windows.

### 5. Release consumes project-owned publish output

Delete the long inline FFmpeg source-build body from `release.yml` after `dotnet publish` owns runtime placement.

Keep short fail-closed checks for:

```text
publish/mac/runtimes/osx-arm64/MMTools/{ffmpeg,ffprobe}
publish/mac/Licenses/FFmpeg-LGPL-2.1.txt
```

Require `test -x` and `file -b ... | grep -qi arm64` for both binaries.

After `build-dmg.sh`, verify the same files under:

```text
output/DTXMania.app/Contents/MacOS/runtimes/osx-arm64/MMTools/
output/DTXMania.app/Contents/MacOS/Licenses/FFmpeg-LGPL-2.1.txt
```

`build-dmg.sh` already uses `cp -R` for publish output. Do not modify it unless this check proves a real execute-bit regression.

### 6. Provenance and diagnostics

`tools/ffmpeg/macos-arm64/README.md` records:

- FFmpeg version and exact official source URL;
- source SHA-256;
- actual final configure flags;
- why the minimal codec/filter set exists;
- LGPL source/license handling;
- cache location and override;
- clean-cache / version-update procedure;
- cold/warm verification commands.

Ship upstream `COPYING.LGPLv2.1` as:

```text
Licenses/FFmpeg-LGPL-2.1.txt
```

Update the stale MP3 error message to say the bundled platform FFmpeg runtime is missing/unusable and recommend rebuilding/reinstalling CX, rather than naming the removed Mac x64 NuGet package.

## Risks and mitigations

### Existing Audio tests depend on encoders the minimal runtime intentionally omits

**Risk:** MP3/OGG rows fail during fixture generation before the actual variant processor is tested.

**Mitigation:** commit deterministic encoded fixtures and keep the existing stricter `PlaybackModifiers(50, 12)` product test unchanged after input setup.

### Generated content may not propagate through ProjectReference or may lose execute bits

**Risk:** Game output works but `DTXMania.Test.Mac` lacks the runtime, or copied binaries become non-executable and `FfmpegRuntime` falls through to PATH.

**Mitigation:** generate/add items before `AssignTargetPaths` and `GetCopyToOutputDirectoryItems`; explicitly build Test.Mac; assert `test -x` in Game/Test/publish/app output; add a narrow chmod hook only if copying proves lossy.

### Windows evaluates the Mac project through DTXMania.Test.csproj

**Risk:** Windows CI attempts to execute the Bash Apple Silicon builder.

**Mitigation:** target condition is `$([MSBuild]::IsOSPlatform('OSX'))`; retain Windows full build/test as a required regression gate.

### Configure hardening differs from the currently shipping recipe

**Risk:** adding `--disable-autodetect/--disable-gpl/--disable-nonfree` during extraction causes a cold-build regression unrelated to HPA-512.

**Mitigation:** prove the extracted shipping recipe first; retain the hardening flags only after a second clean build passes the same capability gates.

## Scope boundaries

In scope:

- extract/cache the existing native FFmpeg source build;
- Mac build/test/publish generated-content integration;
- x64 MMTools package removal from the Mac project;
- repair existing real-FFmpeg Audio tests to use committed encoded fixtures;
- one bundled-runtime filesystem/execute-bit gate;
- one successful ManagedSound MP3 test;
- stale Mac x64 diagnostic cleanup;
- native Apple Silicon CI and release deduplication;
- provenance/license material.

Out of scope:

- FFmpeg version upgrade;
- Intel macOS or Rosetta fallback;
- Windows runtime changes;
- new audio engine, runtime resolver, or codec matrix;
- adding production MP3/OGG encoders solely for tests;
- recorder MP4 validation/remux;
- OBS/ScreenCaptureKit work;
- signing/notarization redesign;
- generic dependency/bootstrap framework.

## Implementation shape

Keep one implementation PR with four reviewer-sized checkpoints:

1. extract/cache/document the current release FFmpeg recipe and prove cold/warm builds;
2. add the macOS-only generated MSBuild copy contract, remove x64 MMTools, and prove Game/Test/publish propagation;
3. repair existing Audio fixtures/tests, add bundled-path/execute-bit + valid MP3 checks, and fix the stale diagnostic;
4. pin/cache native CI, explicitly build Test.Mac, and collapse release duplication to publish/.app verification.

This gives HPA-515 one clear prerequisite: the ordinary supported Mac build and its tests already carry the exact native audio runtime the packaged app will use.