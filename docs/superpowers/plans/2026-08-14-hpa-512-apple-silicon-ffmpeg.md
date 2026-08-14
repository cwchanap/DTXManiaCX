# HPA-512 Native Apple Silicon FFmpeg Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make normal CX Mac build/run/publish output carry a verified native Apple Silicon `ffmpeg`/`ffprobe` runtime so MP3 preview and non-default playback variants work without Rosetta or user-installed FFmpeg.

**Architecture:** Extract the already-proven FFmpeg 7.0.2 arm64 source build from `release.yml` into one reusable shell builder with a versioned user cache. `DTXMania.Game.Mac.csproj` owns copying that runtime into build and publish outputs; existing `FfmpegRuntime` remains the runtime resolver. Native `macos-15` CI validates the real files and focused audio paths, and release reuses the same project-owned output.

**Tech Stack:** .NET 8, MonoGame DesktopGL, FFMpegCore 5.4.0, FFmpeg 7.0.2 source build, Bash/MSBuild, xUnit, GitHub Actions `macos-15`.

## Global Constraints

- Keep FFmpeg 7.0.2 for this ticket; this is packaging consolidation, not an FFmpeg upgrade.
- Official source tarball SHA-256 stays `8646515b638a3ad303e23af6a3587734447cb8fc0a0c064ecdb8e95c4fd8b389`.
- Supported macOS target is native Apple Silicon arm64 only; no Intel/Rosetta fallback.
- Remove `MMTools.Executables.MacOS.X64` from `DTXMania.Game.Mac.csproj`.
- Keep `FFMpegCore` 5.4.0 and the existing `FfmpegRuntime` production resolution flow.
- Runtime layout stays `runtimes/osx-arm64/MMTools/{ffmpeg,ffprobe}`.
- PATH remains diagnostic fallback only; never install/use PATH FFmpeg as production recovery.
- Preserve Windows project/runtime behavior unchanged.
- Preserve the current minimal CX codec/filter surface; do not grow a general FFmpeg build.
- Ship upstream LGPL license material with build/publish/release output.
- No new NuGet binary provider, binary commits, package-builder project, generic bootstrap framework, signing/notarization redesign, recorder media work, or OBS work.

---

## Files

```text
Create:
  tools/ffmpeg/macos-arm64/build-runtime.sh
  tools/ffmpeg/macos-arm64/README.md
  DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3
  DTXMania.Test/Resources/FfmpegNativeRuntimeIntegrationTests.cs

Modify:
  DTXMania.Game/DTXMania.Game.Mac.csproj
  DTXMania.Test/Resources/FfmpegRuntimeCoverageTests.cs
  .github/workflows/build-and-test.yml
  .github/workflows/release.yml

Only modify if the packaged-app verification exposes a real permission-copy bug:
  installer/macos/build-dmg.sh
  installer/macos/test-build-dmg.sh
```

Keep all four tasks in one HPA-512 implementation PR.

---

### Task 1: Extract the reproducible native FFmpeg builder

**Files:**
- Create `tools/ffmpeg/macos-arm64/build-runtime.sh`
- Create `tools/ffmpeg/macos-arm64/README.md`

**Produces:**

```text
build-runtime.sh <runtime-output-dir> <license-output-dir>

runtime-output-dir/
  ffmpeg
  ffprobe

license-output-dir/
  FFmpeg-LGPL-2.1.txt
```

The builder maintains its reusable validated cache at:

```text
${DTXMANIA_FFMPEG_CACHE_ROOT:-$HOME/Library/Caches/DTXManiaCX/ffmpeg}/7.0.2/osx-arm64
```

- [ ] **Step 1: Copy the proven source recipe out of `release.yml` into the script.**

Use constants at the top of the script:

```bash
FFMPEG_VERSION="7.0.2"
FFMPEG_TARBALL_SHA256="8646515b638a3ad303e23af6a3587734447cb8fc0a0c064ecdb8e95c4fd8b389"
FFMPEG_URL="https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz"
```

Fail before download/build unless:

```bash
[[ "$(uname -s)" == "Darwin" ]]
[[ "$(uname -m)" == "arm64" ]]
```

Error text must identify native Apple Silicon as the supported Mac build rather than suggesting Rosetta.

- [ ] **Step 2: Preserve the existing minimal feature set and make dependency/license behavior deterministic.**

Start from the current release configure list and add explicit fail-closed flags:

```text
--disable-autodetect
--disable-gpl
--disable-nonfree
```

Keep the current required decoders/demuxers/parsers/protocols/encoder/muxer/filters exactly as documented in the design spec. Do not add Homebrew codecs or external libraries.

- [ ] **Step 3: Make the versioned cache copy-only on a valid hit.**

A cache hit is valid only when all are true:

```text
cached ffmpeg exists + executable
cached ffprobe exists + executable
cached COPYING.LGPLv2.1 exists
cached source.sha256 contains the exact pinned tarball hash
```

On a miss, build into a temporary directory, validate it, then replace the versioned cache. Do not add cross-process locking; CI/local build concurrency does not justify it here.

- [ ] **Step 4: Keep all native verification inside the builder.**

For both binaries independently require:

```bash
file -b "$bin" | grep -qi 'arm64'
```

Require the same feature checks currently embedded in `release.yml`:

```text
filters:  atempo apad atrim aformat aresample
decoders: mp3float vorbis pcm_s16le
demuxers: mp3 wav ogg s16le
encoder:  pcm_s16le
muxer:    s16le
```

Run `ffmpeg -version` and `ffprobe -version` successfully before caching.

- [ ] **Step 5: Copy license and provenance documentation.**

Copy `COPYING.LGPLv2.1` from the verified source tree/cache to `FFmpeg-LGPL-2.1.txt` in the caller's license output directory.

Document in `README.md`:

```text
version + source URL + source SHA-256
why the minimal configure set exists
cache location and DTXMANIA_FFMPEG_CACHE_ROOT override
rm -rf cache command for a clean rebuild
version-update procedure
local build/verify commands
```

- [ ] **Step 6: Validate a cold build and a warm-cache copy.**

Run on Apple Silicon:

```bash
rm -rf "$HOME/Library/Caches/DTXManiaCX/ffmpeg/7.0.2/osx-arm64"
rm -rf /tmp/dtx-ffmpeg-runtime /tmp/dtx-ffmpeg-licenses
bash tools/ffmpeg/macos-arm64/build-runtime.sh \
  /tmp/dtx-ffmpeg-runtime \
  /tmp/dtx-ffmpeg-licenses

test -x /tmp/dtx-ffmpeg-runtime/ffmpeg
test -x /tmp/dtx-ffmpeg-runtime/ffprobe
file /tmp/dtx-ffmpeg-runtime/ffmpeg /tmp/dtx-ffmpeg-runtime/ffprobe
test -f /tmp/dtx-ffmpeg-licenses/FFmpeg-LGPL-2.1.txt

rm -rf /tmp/dtx-ffmpeg-runtime /tmp/dtx-ffmpeg-licenses
time bash tools/ffmpeg/macos-arm64/build-runtime.sh \
  /tmp/dtx-ffmpeg-runtime \
  /tmp/dtx-ffmpeg-licenses
```

Expected: first command builds from source; second command reuses the cache and only verifies/copies.

- [ ] **Step 7: Commit checkpoint.**

```bash
git add tools/ffmpeg/macos-arm64
git commit -m "build: extract native Apple Silicon ffmpeg runtime"
```

---

### Task 2: Make the Mac project own build/run/publish runtime placement

**Files:**
- Modify `DTXMania.Game/DTXMania.Game.Mac.csproj`
- Modify `DTXMania.Test/Resources/FfmpegRuntimeCoverageTests.cs`

**Consumes:** `tools/ffmpeg/macos-arm64/build-runtime.sh <runtime-dir> <license-dir>` from Task 1.

**Produces:** normal Mac build and publish outputs containing the native runtime and license.

- [ ] **Step 1: Tighten the existing resolver contract test before project wiring.**

Change `GetFFmpegBinaryFolder_ShouldPreferFirstCompleteCandidate` so the complete preferred candidate is:

```text
<assembly>/runtimes/osx-arm64/MMTools
```

and a complete `osx-x64/MMTools` candidate also exists.

Expected assertion:

```csharp
Assert.Equal(arm64, result);
```

Keep the split-folder and PATH tests unchanged.

- [ ] **Step 2: Remove only the obsolete x64 runtime package.**

Delete from `DTXMania.Game.Mac.csproj`:

```xml
<PackageReference Include="MMTools.Executables.MacOS.X64" Version="1.0.6" />
```

Do not change `FFMpegCore` or the Windows project.

- [ ] **Step 3: Add Mac-only MSBuild targets for normal build and publish.**

Keep the target local to `DTXMania.Game.Mac.csproj`.

After a successful `Build` on macOS, invoke:

```text
bash tools/ffmpeg/macos-arm64/build-runtime.sh
  $(TargetDir)/runtimes/osx-arm64/MMTools
  $(TargetDir)/Licenses
```

After `Publish`, invoke the same script for:

```text
$(PublishDir)/runtimes/osx-arm64/MMTools
$(PublishDir)/Licenses
```

Use a repository-root-relative script path based on `$(MSBuildProjectDirectory)`, not the shell working directory.

Do not add shared `.props`/`.targets` files for this single platform project.

- [ ] **Step 4: Prove normal `dotnet build` and `dotnet run` output is self-contained.**

Run on Apple Silicon:

```bash
dotnet restore DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug

test -x DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffmpeg
test -x DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffprobe
test -f DTXMania.Game/bin/Debug/net8.0/Licenses/FFmpeg-LGPL-2.1.txt
```

Launch once with the normal project command and confirm the process itself starts as arm64:

```bash
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```

No PATH FFmpeg installation is part of the success path.

- [ ] **Step 5: Prove self-contained publish output.**

```bash
rm -rf /tmp/dtx-publish-arm64
dotnet publish DTXMania.Game/DTXMania.Game.Mac.csproj \
  -c Release -r osx-arm64 --self-contained \
  -p:PublishReadyToRun=false -p:TieredCompilation=false \
  -o /tmp/dtx-publish-arm64

test -x /tmp/dtx-publish-arm64/runtimes/osx-arm64/MMTools/ffmpeg
test -x /tmp/dtx-publish-arm64/runtimes/osx-arm64/MMTools/ffprobe
test -f /tmp/dtx-publish-arm64/Licenses/FFmpeg-LGPL-2.1.txt
```

- [ ] **Step 6: Run focused resolver tests and commit checkpoint.**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~FfmpegRuntime" \
  --verbosity normal

git add DTXMania.Game/DTXMania.Game.Mac.csproj \
        DTXMania.Test/Resources/FfmpegRuntimeCoverageTests.cs
git commit -m "build: bundle native ffmpeg in Mac outputs"
```

---

### Task 3: Add native MP3 and real audio-variant smoke coverage

**Files:**
- Create `DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3`
- Create `DTXMania.Test/Resources/FfmpegNativeRuntimeIntegrationTests.cs`
- Modify `DTXMania.Test/DTXMania.Test.Mac.csproj` only if the existing test-data include does not copy the new fixture

**Consumes:** bundled runtime produced by the Mac project.

**Produces:** two native integration gates: real MP3 `ManagedSound` load and one real non-default `FfmpegAudioVariantProcessor` transform.

- [ ] **Step 1: Generate one project-owned MP3 fixture.**

On a development Mac with a full FFmpeg installed for test-fixture generation only:

```bash
mkdir -p DTXMania.Test/TestData/Audio
ffmpeg -hide_banner -loglevel error \
  -f lavfi -i "sine=frequency=440:sample_rate=44100:duration=1" \
  -ac 1 -codec:a libmp3lame -b:a 64k \
  -y DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3
```

The committed tone contains no third-party media. Do not make fixture generation part of production build.

- [ ] **Step 2: Add one arm64-only runtime-location/process smoke.**

In `FfmpegNativeRuntimeIntegrationTests`, return without asserting native behavior when the host is not `OperatingSystem.IsMacOS()` + `Architecture.Arm64`; Windows remains covered by normal tests.

On native Mac require:

```text
FfmpegRuntime.EnsureConfigured().IsAvailable == true
BinaryFolder ends with runtimes/osx-arm64/MMTools
ffmpeg and ffprobe exist and are executable
process execution of ffmpeg -version succeeds
process execution of ffprobe -version succeeds
```

Do not create a reusable process-runner abstraction for two smoke commands.

- [ ] **Step 3: Add the real MP3 `ManagedSound` smoke.**

With `ALSOFT_DRIVERS=null` in the test process/CI environment:

```csharp
using var sound = new ManagedSound(mp3Fixture);
Assert.True(sound.Duration > TimeSpan.Zero);
```

This must exercise the packaged runtime through `FfmpegRuntime`; do not configure FFMpegCore to PATH inside the test.

- [ ] **Step 4: Add one real variant transform smoke.**

Generate a small PCM WAV in the test temporary directory and run the public default processor:

```csharp
var processor = new FfmpegAudioVariantProcessor();
var artifact = await processor.PrepareAsync(
    wavPath,
    new PlaybackModifiers(/* use the existing non-default play-speed factory/constructor */),
    cancellationToken);
```

Use the repository's actual `PlaybackModifiers` API and one supported non-default speed already covered by unit tests. Assert a non-empty prepared artifact, then dispose/delete via the existing artifact ownership contract.

This test must use the real backend; do not pass the internal fake backend used by unit tests.

- [ ] **Step 5: Keep existing cancellation/timeout tests as the cleanup gate.**

Do not duplicate cancellation machinery in the native integration class. Run the existing `FfmpegAudioVariantProcessorTests` together with the two new native smokes.

- [ ] **Step 6: Run focused native tests.**

```bash
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~FfmpegNativeRuntimeIntegrationTests|FullyQualifiedName~FfmpegAudioVariantProcessorTests" \
  --verbosity normal
```

Expected: native runtime, MP3 load, real variant transform, and existing cancellation/timeout coverage all pass.

- [ ] **Step 7: Commit checkpoint.**

```bash
git add DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3 \
        DTXMania.Test/Resources/FfmpegNativeRuntimeIntegrationTests.cs \
        DTXMania.Test/DTXMania.Test.Mac.csproj
git commit -m "test: verify native Mac ffmpeg audio paths"
```

---

### Task 4: Make native CI authoritative and remove release duplication

**Files:**
- Modify `.github/workflows/build-and-test.yml`
- Modify `.github/workflows/release.yml`
- Modify `installer/macos/build-dmg.sh` / `test-build-dmg.sh` only if verification proves execute bits are lost

**Consumes:** project-owned build/publish runtime from Tasks 1–3.

**Produces:** one native CI path and one release path using the same runtime source of truth.

- [ ] **Step 1: Pin regular Mac CI to a native Apple Silicon runner.**

Change:

```yaml
runs-on: macos-latest
```

to:

```yaml
runs-on: macos-15
```

for `build-and-test-macos`.

This makes the regular Mac build/test result actual arm64 evidence, matching the existing release runner.

- [ ] **Step 2: Cache the source-built runtime for PR speed.**

Before the Mac build, add `actions/cache` for:

```text
~/Library/Caches/DTXManiaCX/ffmpeg
```

Key it with OS/architecture plus:

```text
hashFiles('tools/ffmpeg/macos-arm64/build-runtime.sh')
```

Do not cache build/publish outputs.

- [ ] **Step 3: Verify the actual game build output before tests.**

Immediately after `dotnet build`:

```bash
runtime="DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools"
test -x "$runtime/ffmpeg"
test -x "$runtime/ffprobe"
file -b "$runtime/ffmpeg" | grep -qi arm64
file -b "$runtime/ffprobe" | grep -qi arm64
"$runtime/ffmpeg" -version
"$runtime/ffprobe" -version
```

Set `ALSOFT_DRIVERS=null` when running the focused/native audio tests.

Keep the full Mac unit suite, Automation suite, and prepared-chart AudioE2E afterward.

- [ ] **Step 4: Delete the duplicated FFmpeg source-build body from `release.yml`.**

The `dotnet publish` step now produces:

```text
publish/mac/runtimes/osx-arm64/MMTools/ffmpeg
publish/mac/runtimes/osx-arm64/MMTools/ffprobe
publish/mac/Licenses/FFmpeg-LGPL-2.1.txt
```

Replace the existing long `Build native arm64 ffmpeg` step with a short publish-artifact verification:

```bash
runtime="publish/mac/runtimes/osx-arm64/MMTools"
test -x "$runtime/ffmpeg"
test -x "$runtime/ffprobe"
test -f publish/mac/Licenses/FFmpeg-LGPL-2.1.txt
file -b "$runtime/ffmpeg" | grep -qi arm64
file -b "$runtime/ffprobe" | grep -qi arm64
```

The feature-set verification remains in `build-runtime.sh`; do not duplicate it in YAML.

- [ ] **Step 5: Verify the packaged `.app` after `build-dmg.sh`.**

After bundle creation, require:

```bash
app_runtime="output/DTXMania.app/Contents/MacOS/runtimes/osx-arm64/MMTools"
test -x "$app_runtime/ffmpeg"
test -x "$app_runtime/ffprobe"
file -b "$app_runtime/ffmpeg" | grep -qi arm64
file -b "$app_runtime/ffprobe" | grep -qi arm64
test -f output/DTXMania.app/Contents/MacOS/Licenses/FFmpeg-LGPL-2.1.txt
```

If this passes, do not touch `build-dmg.sh`. If execute bits are actually lost, fix only its existing `cp -R` staging behavior and extend `test-build-dmg.sh` for that exact regression.

- [ ] **Step 6: Run the final local blast radius.**

On Apple Silicon:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj -c Debug --verbosity normal
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj -c Debug --verbosity normal
```

On Windows/Windows CI:

```powershell
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj -c Debug
dotnet test DTXMania.Test/DTXMania.Test.csproj -c Debug --verbosity normal
```

Also run the Mac prepared-chart AudioE2E on the native runner using the existing workflow command.

- [ ] **Step 7: Commit checkpoint.**

```bash
git add .github/workflows/build-and-test.yml \
        .github/workflows/release.yml \
        installer/macos/build-dmg.sh \
        installer/macos/test-build-dmg.sh
git commit -m "ci: validate bundled Apple Silicon ffmpeg"
```

Stage only installer files if they actually changed.

---

## Final acceptance checklist

- [ ] `MMTools.Executables.MacOS.X64` is absent from the Mac project.
- [ ] Normal Apple Silicon `dotnet build` output contains executable arm64 `ffmpeg` and `ffprobe`.
- [ ] Normal `dotnet run` resolves the bundled arm64 runtime without PATH/Rosetta.
- [ ] `dotnet publish -r osx-arm64` contains the same runtime and FFmpeg LGPL license.
- [ ] `FfmpegRuntime` preference test proves `osx-arm64` wins over an available x64 candidate.
- [ ] Real packaged-runtime `ffmpeg -version` and `ffprobe -version` smoke passes.
- [ ] MP3 preview load through `ManagedSound` passes with the bundled runtime.
- [ ] One real non-default `FfmpegAudioVariantProcessor` transform passes.
- [ ] Existing cancellation/timeout cleanup tests remain green.
- [ ] Regular Mac CI is pinned to `macos-15` and caches the source-built runtime.
- [ ] Release YAML no longer owns a duplicate FFmpeg configure/build recipe.
- [ ] `.app` bundle retains both runtime files, arm64 architecture, execute bits, and license.
- [ ] Windows build/tests are unchanged and green.

## Handoff

After this implementation merges, HPA-512 can close and HPA-515 has the native CX audio prerequisite it needs. Do not pull HPA-515 OBS/recorder work into this implementation PR.