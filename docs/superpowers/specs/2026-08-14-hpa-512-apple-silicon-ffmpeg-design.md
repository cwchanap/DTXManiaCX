# HPA-512 Native Apple Silicon FFmpeg Design

**Issue:** HPA-512 — Package native Apple Silicon FFmpeg for CX gameplay audio  
**Date:** 2026-08-14  
**Status:** Proposed for review

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

HPA-513 is the next Windows acceptance milestone, but its HPA-503 implementation is still stacked behind the open HPA-503 planning PR before it reaches `main`.

HPA-512 is high priority, unblocked, and independently blocks HPA-515 Apple Silicon recording parity.

## Current state

The repository is already close to the desired design:

- `DTXMania.Game.Mac.csproj` references `FFMpegCore` plus `MMTools.Executables.MacOS.X64`.
- `FfmpegRuntime` already prefers `runtimes/osx-arm64/MMTools` before the x64/Windows/Linux candidates and requires both binaries to be runnable.
- `FfmpegAudioVariantProcessor` and `ManagedSound` already share `FfmpegRuntime`.
- the release workflow already builds a minimal native arm64 FFmpeg 7.0.2 from the official source tarball, pins its SHA-256, copies `ffmpeg`/`ffprobe` to the exact expected runtime folder, and validates architecture plus required codecs/filters.
- that native runtime is created only after `dotnet publish`, so ordinary Mac build/run/test output still depends on the x64 package or PATH.

The missing work is to turn the proven release-only recipe into a normal Mac project build input and remove duplicate ownership from `release.yml`.

## Approaches considered

### A. Extract the existing official source build and expose it as normal Mac project content — chosen

Create one small build script from the already-proven release recipe. Before normal output-copy resolution, the Mac project asks that script to populate an intermediate staging directory. The generated runtime/license files are declared as ordinary MSBuild items with `CopyToOutputDirectory` and `CopyToPublishDirectory`, so they also flow through `ProjectReference` into the Mac test output.

A versioned user cache makes repeated builds copy-only after the first successful native compile.

**Pros**

- reuses known working configure flags and checksum;
- no new package/provider/supply chain;
- exact existing `FfmpegRuntime` layout;
- one MSBuild content path covers run, tests, and publish;
- no Rosetta dependency;
- minimal release-workflow code after consolidation;
- license/features remain under project control.

**Cons**

- first native Mac build compiles FFmpeg once;
- requires normal Apple developer command-line build tools.

This is the smallest long-term shape because the repository already owns the same source build in CI.

### B. Replace MMTools with a third-party arm64 binary/NuGet package — rejected

This shortens first-build time but adds another binary publisher, package layout, update cadence, and license/provenance surface. It also requires adapting that package back into the existing `runtimes/osx-arm64/MMTools` contract.

Do not add that dependency while a working upstream-source recipe already exists.

### C. Commit `ffmpeg` and `ffprobe` binaries to the repository — rejected

This makes builds fast but turns large generated binaries into source-controlled artifacts and makes updates/review/provenance worse. It is unnecessary for this project.

## Chosen design

### 1. One source of truth for the native runtime

Add:

```text
tools/ffmpeg/macos-arm64/build-runtime.sh
tools/ffmpeg/macos-arm64/README.md
```

Move the current native build recipe out of `.github/workflows/release.yml` into `build-runtime.sh`.

Keep the currently proven source version for this ticket:

```text
FFmpeg 7.0.2
source: official ffmpeg.org release tarball
SHA-256: 8646515b638a3ad303e23af6a3587734447cb8fc0a0c064ecdb8e95c4fd8b389
```

HPA-512 is not an FFmpeg-version upgrade. A later version bump is one isolated script/documentation change plus validation.

Retain the current minimal feature set required by CX and make dependency/license intent explicit:

```text
static executables; no shared dylib deployment
disable docs / ffplay / everything by default
disable autodetect
disable GPL and nonfree features

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

`--disable-autodetect`, `--disable-gpl`, and `--disable-nonfree` make the build fail closed instead of accidentally changing dependency/redistribution behavior based on what happens to be installed on a developer or runner machine.

The script must:

1. reject non-macOS/non-arm64 hosts with an actionable message;
2. download the exact source tarball when the cache is missing;
3. verify SHA-256 before extraction;
4. configure/build/install the minimal runtime;
5. verify both outputs are executable arm64 Mach-O binaries;
6. verify the required filters, decoders, demuxers, encoder, and muxer;
7. cache the validated pair under a versioned user cache;
8. copy `ffmpeg`, `ffprobe`, and the upstream LGPL license into staging destinations supplied by the caller.

Use a simple cache:

```text
~/Library/Caches/DTXManiaCX/ffmpeg/7.0.2/osx-arm64/
```

The cache is an optimization only. Deleting it forces a clean verified rebuild.

Do not create a package-builder project, runtime manifest schema, download service, lock daemon, or generic cross-platform FFmpeg installer.

### 2. Mac project owns one transitive output/publish path

Update `DTXMania.Game/DTXMania.Game.Mac.csproj`:

- remove `MMTools.Executables.MacOS.X64`;
- keep `FFMpegCore` unchanged;
- define an intermediate staging root under the existing project `obj` tree, for example:

```text
$(BaseIntermediateOutputPath)ffmpeg-runtime/
  runtimes/osx-arm64/MMTools/ffmpeg
  runtimes/osx-arm64/MMTools/ffprobe
  Licenses/FFmpeg-LGPL-2.1.txt
```

- add one macOS-only target early enough to populate that staging directory before MSBuild resolves files copied to build/reference outputs;
- declare the staged files as normal `None`/content items with target links:

```text
runtimes/osx-arm64/MMTools/ffmpeg
runtimes/osx-arm64/MMTools/ffprobe
Licenses/FFmpeg-LGPL-2.1.txt
```

and both:

```text
CopyToOutputDirectory=PreserveNewest
CopyToPublishDirectory=PreserveNewest
```

This matters because `DTXMania.Test.Mac.csproj` references the game project. Generated files that are merely copied into `DTXMania.Game/bin` after the build are not a reliable project-reference contract; declared copy items are.

The builder's user cache keeps the early target cheap after the first compile: each normal build stages from cache, then MSBuild handles output/test/publish copying consistently.

The Mac project is already the platform boundary, so do not introduce shared cross-platform MSBuild props/targets for one Apple-Silicon-only dependency.

Intel macOS remains out of scope. A Mac x64 host should fail the native bootstrap instead of silently selecting Rosetta.

### 3. Keep runtime code stable

Do not redesign `FfmpegRuntime`.

Its current contract is already correct for HPA-512:

```text
bundled osx-arm64
-> bundled osx-x64
-> Windows/Linux candidates
-> PATH diagnostic fallback
```

Once the x64 package is removed, the supported Mac project only supplies the arm64 folder. PATH remains a development diagnostic fallback, not the supported runtime source.

Only tighten focused tests so the first complete arm64 folder demonstrably beats an otherwise-complete x64 folder.

Do not add architecture registries, RID providers, process wrappers, or a second availability service.

### 4. Native audio validation

Use the existing seams instead of creating a parallel audio harness.

Add one small generated/committed test tone under `DTXMania.Test/TestData/Audio/` for the real MP3 decode path. The test asset is project-generated, not third-party media. Add that test-data folder to the existing Mac test output-copy rules.

On native Apple Silicon verify from the **test assembly output**:

1. `RuntimeInformation.ProcessArchitecture == Arm64`;
2. `FfmpegRuntime.EnsureConfigured()` reports the bundled `osx-arm64/MMTools` directory next to the test/game assembly;
3. both binaries execute normally;
4. `new ManagedSound(mp3Fixture)` succeeds with a non-zero duration;
5. `new FfmpegAudioVariantProcessor().PrepareAsync(wav, new PlaybackModifiers(125, 0), token)` produces non-empty PCM;
6. cancellation/timeout coverage already owned by `FfmpegAudioVariantProcessorTests` remains green.

The test-output location is intentional: it proves the project-reference propagation that HPA-512 needs for normal Mac tests, rather than only proving files exist in `DTXMania.Game/bin`.

Do not broaden this into a codec matrix. MP3 preview plus one real non-default variant path are the product gates named by HPA-512.

### 5. CI and release consolidation

Pin the regular macOS build/test job to the same native Apple Silicon class already used by release (`macos-15`) so it is real arm64 evidence rather than an ambiguous `macos-latest` alias.

Add an Actions cache for the versioned FFmpeg user cache keyed by the builder script. A cache miss compiles; a hit keeps routine PR builds fast.

After the Mac project build, verify the actual game output:

```text
test -x .../runtimes/osx-arm64/MMTools/ffmpeg
test -x .../runtimes/osx-arm64/MMTools/ffprobe
file -b each binary contains arm64
```

Then run focused native runtime/MP3/variant tests from `DTXMania.Test.Mac` before the normal full Mac suite and prepared-chart AudioE2E.

In `release.yml` delete the large inline source-build step. `dotnet publish` now carries the declared runtime content itself. Keep a short fail-closed artifact check before `build-dmg.sh`.

After `build-dmg.sh`, also verify the copied app bundle still contains executable arm64 binaries and the license at:

```text
output/DTXMania.app/Contents/MacOS/runtimes/osx-arm64/MMTools/
output/DTXMania.app/Contents/MacOS/Licenses/FFmpeg-LGPL-2.1.txt
```

`build-dmg.sh` already copies publish output recursively; no installer change is needed unless this verification exposes a real permission-loss bug.

### 6. Provenance and update procedure

`tools/ffmpeg/macos-arm64/README.md` is the human-maintained provenance/update document. Record:

- FFmpeg version;
- exact official source URL;
- source SHA-256;
- why the selected configure features exist;
- license mode/constraints;
- cache location;
- how to force a clean rebuild;
- how to update version + checksum;
- local validation commands.

Ship the exact `COPYING.LGPLv2.1` from the verified source tarball as:

```text
Licenses/FFmpeg-LGPL-2.1.txt
```

Do not add a general third-party-license generator for one dependency.

## Error handling

Keep failures early and actionable:

```text
wrong host/architecture
-> "Native CX macOS runtime requires Apple Silicon (arm64)."

download/checksum/configure/build failure
-> fail the Mac build; never fall back to the x64 NuGet runtime

missing/non-executable/wrong-architecture staging output
-> fail the builder or CI/release verification

runtime unexpectedly unavailable after a successful build
-> existing FfmpegRuntime diagnostic + focused test failure
```

Do not silently install Homebrew FFmpeg or use PATH as production recovery.

## Testing strategy

### Fast/unit

- update `FfmpegRuntimeCoverageTests` so a complete `osx-arm64` candidate is preferred over complete `osx-x64`;
- keep missing/split candidate and PATH tests unchanged;
- keep existing audio-variant unit/cancellation tests unchanged.

### Native integration

On `macos-15`:

- normal Mac `dotnet build` contains executable arm64 runtime files;
- the Mac test output also contains them through `ProjectReference` copy-item propagation;
- focused MP3 `ManagedSound` smoke passes;
- focused real `FfmpegAudioVariantProcessor` smoke with `new PlaybackModifiers(125, 0)` passes;
- `dotnet publish -r osx-arm64` contains the same runtime + license;
- `.app` bundle preserves both binaries and execute bits.

### Regression

- Windows build/tests remain unchanged and must stay green;
- existing macOS full test suite and prepared-chart audio E2E remain green.

## Scope boundaries

In scope:

- native FFmpeg/ffprobe source bootstrap for the supported Mac project;
- one transitive Mac build/test/publish copy contract;
- x64 MMTools package removal from the Mac project;
- provenance/license material;
- native Apple Silicon verification and release deduplication.

Out of scope:

- FFmpeg 8.x upgrade;
- Intel macOS/Rosetta support;
- Windows runtime changes;
- recorder MP4 validation/remux;
- OBS/ScreenCaptureKit work;
- code signing/notarization redesign;
- general codec expansion;
- generic dependency/bootstrap framework.

## Implementation shape

Keep one implementation PR with four reviewer-sized checkpoints:

1. extract and verify the reproducible native FFmpeg builder + provenance;
2. wire the Mac project staging/copy contract and remove x64 MMTools;
3. add native runtime/MP3/variant validation from the test output;
4. pin/cache CI and collapse release duplication.

This gives HPA-515 one clear prerequisite: the ordinary supported Mac build already carries the audio runtime it needs.