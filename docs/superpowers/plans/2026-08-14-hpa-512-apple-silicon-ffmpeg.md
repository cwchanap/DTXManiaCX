# HPA-512 Native Apple Silicon FFmpeg Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make normal CX Mac build/run/test/publish output carry a verified native Apple Silicon `ffmpeg`/`ffprobe` runtime so MP3 preview and non-default playback variants work without Rosetta or user-installed FFmpeg.

**Architecture:** Extract the already-proven FFmpeg 7.0.2 arm64 source build from `release.yml` into one reusable shell builder with a versioned user cache. `DTXMania.Game.Mac.csproj` stages the generated files under `obj` before MSBuild resolves output-copy items, then declares them as normal build/publish content so they propagate through `ProjectReference` into Mac tests. Existing `FfmpegRuntime` remains the runtime resolver. Native `macos-15` CI validates the real files and focused audio paths, and release reuses the same project-owned output.

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
  DTXMania.Test/DTXMania.Test.Mac.csproj
  DTXMania.Test/Resources/FfmpegRuntimeCoverageTests.cs
  .github/workflows/build-and-test.yml
  .github/workflows/release.yml

Only modify if packaged-app verification exposes a real permission-copy bug:
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

Use this failure meaning:

```text
Native CX macOS runtime requires Apple Silicon (arm64); Intel/Rosetta is not supported.
```

- [ ] **Step 2: Preserve the existing minimal feature set and make dependency/license behavior deterministic.**

Start from the current release configure list and add:

```text
--disable-autodetect
--disable-gpl
--disable-nonfree
```

Keep exactly these CX requirements:

```text
decoders: mp3float, vorbis, pcm_s16le, pcm_s24le, pcm_f32le, pcm_u8,
          pcm_alaw, pcm_mulaw, adpcm_ima_wav, adpcm_ms
demuxers: mp3, wav, ogg, pcm_s16le
parsers:  mpegaudio, vorbis
protocols: file, pipe
encoder:   pcm_s16le
muxer:     pcm_s16le
filters:   aformat, anull, aresample, atempo, apad, atrim
```

Do not add Homebrew/external codecs.

- [ ] **Step 3: Make the versioned cache copy-only on a valid hit.**

A cache hit is valid only when all are true:

```text
cached ffmpeg exists + executable
cached ffprobe exists + executable
cached COPYING.LGPLv2.1 exists
cached source.sha256 contains the exact pinned tarball hash
```

On a miss, build into a temporary directory, validate it, then replace the versioned cache. Do not add cross-process locking.

- [ ] **Step 4: Keep native verification inside the builder.**

For both binaries independently require:

```bash
file -b "$bin" | grep -qi 'arm64'
```

Require:

```text
filters:  atempo apad atrim aformat aresample
decoders: mp3float vorbis pcm_s16le
demuxers: mp3 wav ogg s16le
encoder:  pcm_s16le
muxer:    s16le
```

Run `ffmpeg -version` and `ffprobe -version` successfully before caching.

- [ ] **Step 5: Copy license and document provenance/update.**

Copy `COPYING.LGPLv2.1` from the verified source tree/cache to `FFmpeg-LGPL-2.1.txt` in the caller's license output directory.

`README.md` must record:

```text
version
source URL
source SHA-256
minimal configure rationale
cache location + DTXMANIA_FFMPEG_CACHE_ROOT override
clean-cache command
version/checksum update steps
cold/warm local verification commands
```

- [ ] **Step 6: Validate cold build and warm-cache copy.**

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

Expected: first command builds from source; second reuses cache and verifies/copies only.

- [ ] **Step 7: Commit checkpoint.**

```bash
git add tools/ffmpeg/macos-arm64
git commit -m "build: extract native Apple Silicon ffmpeg runtime"
```

---

### Task 2: Make the Mac project expose native runtime as normal content

**Files:**
- Modify `DTXMania.Game/DTXMania.Game.Mac.csproj`
- Modify `DTXMania.Test/Resources/FfmpegRuntimeCoverageTests.cs`

**Consumes:** `tools/ffmpeg/macos-arm64/build-runtime.sh <runtime-dir> <license-dir>` from Task 1.

**Produces:** one transitive build/test/publish content contract for native FFmpeg.

- [ ] **Step 1: Tighten the resolver preference test first.**

Change `GetFFmpegBinaryFolder_ShouldPreferFirstCompleteCandidate` so both of these are complete:

```text
<assembly>/runtimes/osx-arm64/MMTools
<assembly>/runtimes/osx-x64/MMTools
```

Expected assertion:

```csharp
Assert.Equal(arm64, result);
```

Keep split-folder and PATH tests unchanged.

- [ ] **Step 2: Remove only the obsolete x64 package.**

Delete:

```xml
<PackageReference Include="MMTools.Executables.MacOS.X64" Version="1.0.6" />
```

Keep `FFMpegCore` and the Windows project unchanged.

- [ ] **Step 3: Define one intermediate staging root in the Mac project.**

Use project-local intermediate paths under `$(BaseIntermediateOutputPath)`:

```text
ffmpeg-runtime/runtimes/osx-arm64/MMTools/ffmpeg
ffmpeg-runtime/runtimes/osx-arm64/MMTools/ffprobe
ffmpeg-runtime/Licenses/FFmpeg-LGPL-2.1.txt
```

Add a macOS-only target before `PrepareForBuild` that invokes the builder with the runtime and license staging directories. Resolve the script relative to `$(MSBuildProjectDirectory)` so the command does not depend on shell working directory.

The builder's user cache makes this target cheap on warm builds.

- [ ] **Step 4: Declare staged files as normal output + publish items.**

In `DTXMania.Game.Mac.csproj`, declare the exact staged files with links:

```text
runtimes/osx-arm64/MMTools/ffmpeg
runtimes/osx-arm64/MMTools/ffprobe
Licenses/FFmpeg-LGPL-2.1.txt
```

and:

```text
CopyToOutputDirectory=PreserveNewest
CopyToPublishDirectory=PreserveNewest
```

Do not post-copy directly into `bin`/publish. The declared items are intentional so `ProjectReference` can propagate them to `DTXMania.Test.Mac`.

- [ ] **Step 5: Prove normal game build output.**

```bash
dotnet restore DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug

runtime="DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools"
test -x "$runtime/ffmpeg"
test -x "$runtime/ffprobe"
test -f DTXMania.Game/bin/Debug/net8.0/Licenses/FFmpeg-LGPL-2.1.txt
```

Launch once through the normal supported command:

```bash
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```

No PATH FFmpeg installation is part of the success path.

- [ ] **Step 6: Prove project-reference propagation to Mac test output.**

```bash
dotnet build DTXMania.Test/DTXMania.Test.Mac.csproj -c Debug

test_runtime="DTXMania.Test/bin/Debug/net8.0/runtimes/osx-arm64/MMTools"
test -x "$test_runtime/ffmpeg"
test -x "$test_runtime/ffprobe"
test -f DTXMania.Test/bin/Debug/net8.0/Licenses/FFmpeg-LGPL-2.1.txt
```

If this fails, fix the Game project item metadata; do not add a second FFmpeg copy target to the test project.

- [ ] **Step 7: Prove self-contained publish output.**

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

- [ ] **Step 8: Run focused resolver tests and commit checkpoint.**

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
- Modify `DTXMania.Test/DTXMania.Test.Mac.csproj`

**Consumes:** native runtime propagated into the Mac test output by Task 2.

**Produces:** real bundled-runtime gates for MP3 `ManagedSound` and one non-default variant transform.

- [ ] **Step 1: Generate one project-owned MP3 fixture.**

Use a full development FFmpeg once to create the committed test asset:

```bash
mkdir -p DTXMania.Test/TestData/Audio
ffmpeg -hide_banner -loglevel error \
  -f lavfi -i "sine=frequency=440:sample_rate=44100:duration=1" \
  -ac 1 -codec:a libmp3lame -b:a 64k \
  -y DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3
```

The tone is generated project test data, not third-party media. Fixture generation is not part of production build.

- [ ] **Step 2: Copy the audio fixture into the Mac test output.**

Extend `DTXMania.Test.Mac.csproj` with the same shape already used for `TestData/NxScores`:

```xml
<None Include="TestData/Audio/**/*">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 3: Add the arm64-only bundled-runtime smoke.**

In `FfmpegNativeRuntimeIntegrationTests`, immediately return on hosts other than macOS arm64. On native Mac assert:

```text
FfmpegRuntime.EnsureConfigured().IsAvailable == true
BinaryFolder == test assembly/runtimes/osx-arm64/MMTools
ffmpeg exists + Unix execute bit
ffprobe exists + Unix execute bit
ffmpeg -version exits 0
ffprobe -version exits 0
```

Use `ProcessStartInfo` directly in this test class. Do not create a reusable process-runner abstraction for two commands.

- [ ] **Step 4: Add the real MP3 `ManagedSound` smoke.**

Resolve the copied fixture from `AppContext.BaseDirectory` and run with `ALSOFT_DRIVERS=null`:

```csharp
var mp3Path = Path.Combine(
    AppContext.BaseDirectory,
    "TestData",
    "Audio",
    "ffmpeg-tone.mp3");
using var sound = new ManagedSound(mp3Path);
Assert.True(sound.Duration > TimeSpan.Zero);
```

Do not configure FFMpegCore to PATH in the test.

- [ ] **Step 5: Add one real non-default variant smoke.**

Write a deterministic 44.1 kHz mono 16-bit PCM WAV to a temporary path in the test, then use the public production processor exactly as follows:

```csharp
var processor = new FfmpegAudioVariantProcessor();
var artifact = await processor.PrepareAsync(
    wavPath,
    new PlaybackModifiers(125, 0),
    cancellationToken);

Assert.True(artifact.PcmByteLength > 0);
Assert.Equal(44_100, artifact.SampleRate);
Assert.Equal(1, artifact.ChannelCount);
```

`125` is a valid non-default `PlaySpeedRange` value and exercises `atempo`; pitch remains default.

Clean only the source WAV/temp test directory in `finally`. `PreparedAudioArtifact` is in-memory and does not need disposal.

- [ ] **Step 6: Keep existing cancellation/timeout tests as the cleanup gate.**

Do not duplicate cancellation machinery in the integration class. Run existing `FfmpegAudioVariantProcessorTests` together with the new native tests.

- [ ] **Step 7: Run focused native tests.**

```bash
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug \
  --filter "FullyQualifiedName~FfmpegNativeRuntimeIntegrationTests|FullyQualifiedName~FfmpegAudioVariantProcessorTests" \
  --verbosity normal
```

Expected: bundled runtime location/process smoke, MP3 load, real 1.25x variant transform, and existing cancellation/timeout coverage pass.

- [ ] **Step 8: Commit checkpoint.**

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
- Modify `installer/macos/build-dmg.sh` / `installer/macos/test-build-dmg.sh` only if verification proves execute bits are lost

**Consumes:** project-owned staged/copied runtime from Tasks 1–3.

**Produces:** one native CI path and one release path using the same FFmpeg source of truth.

- [ ] **Step 1: Pin regular Mac CI to native Apple Silicon.**

Change `build-and-test-macos`:

```yaml
runs-on: macos-15
```

This matches the existing native release runner.

- [ ] **Step 2: Cache the source-built runtime for PR speed.**

Before the Mac build, cache:

```text
~/Library/Caches/DTXManiaCX/ffmpeg
```

with a key containing:

```text
macos-arm64
hashFiles('tools/ffmpeg/macos-arm64/build-runtime.sh')
```

Do not cache `bin`, `obj`, or publish output.

- [ ] **Step 3: Verify the actual Game build output.**

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

- [ ] **Step 4: Run the focused native audio gate before the full Mac suite.**

```bash
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug \
  --verbosity normal \
  --filter "FullyQualifiedName~FfmpegNativeRuntimeIntegrationTests|FullyQualifiedName~FfmpegAudioVariantProcessorTests"
```

Then retain the existing full Mac unit suite, Automation suite, and prepared-chart `AudioE2E`.

- [ ] **Step 5: Delete the duplicated source-build body from `release.yml`.**

`dotnet publish` must now produce:

```text
publish/mac/runtimes/osx-arm64/MMTools/ffmpeg
publish/mac/runtimes/osx-arm64/MMTools/ffprobe
publish/mac/Licenses/FFmpeg-LGPL-2.1.txt
```

Replace the current long native FFmpeg build step with a short artifact verification:

```bash
runtime="publish/mac/runtimes/osx-arm64/MMTools"
test -x "$runtime/ffmpeg"
test -x "$runtime/ffprobe"
test -f publish/mac/Licenses/FFmpeg-LGPL-2.1.txt
file -b "$runtime/ffmpeg" | grep -qi arm64
file -b "$runtime/ffprobe" | grep -qi arm64
```

Feature verification stays in `build-runtime.sh`; do not duplicate it in YAML.

- [ ] **Step 6: Verify the packaged `.app`.**

After `build-dmg.sh`:

```bash
app_runtime="output/DTXMania.app/Contents/MacOS/runtimes/osx-arm64/MMTools"
test -x "$app_runtime/ffmpeg"
test -x "$app_runtime/ffprobe"
file -b "$app_runtime/ffmpeg" | grep -qi arm64
file -b "$app_runtime/ffprobe" | grep -qi arm64
test -f output/DTXMania.app/Contents/MacOS/Licenses/FFmpeg-LGPL-2.1.txt
```

If this passes, do not touch `build-dmg.sh`. If execute bits are actually lost, fix only its existing publish-output copy and extend `test-build-dmg.sh` for that regression.

- [ ] **Step 7: Run final blast radius.**

On Apple Silicon:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj -c Debug --verbosity normal
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj -c Debug --verbosity normal
```

Run the existing prepared-chart `AudioE2E` command from the workflow on `macos-15`.

On Windows/Windows CI:

```powershell
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj -c Debug
dotnet test DTXMania.Test/DTXMania.Test.csproj -c Debug --verbosity normal
```

- [ ] **Step 8: Commit checkpoint.**

```bash
git add .github/workflows/build-and-test.yml .github/workflows/release.yml
git add installer/macos/build-dmg.sh installer/macos/test-build-dmg.sh 2>/dev/null || true
git commit -m "ci: validate bundled Apple Silicon ffmpeg"
```

Only installer files with real changes should be staged in the final commit.

---

## Final acceptance checklist

- [ ] `MMTools.Executables.MacOS.X64` is absent from the Mac project.
- [ ] Normal Apple Silicon `dotnet build` output contains executable arm64 `ffmpeg` and `ffprobe`.
- [ ] Mac test output receives the same runtime through the Game `ProjectReference` copy contract.
- [ ] Normal `dotnet run` resolves bundled arm64 runtime without PATH/Rosetta.
- [ ] `dotnet publish -r osx-arm64` contains the same runtime and FFmpeg LGPL license.
- [ ] `FfmpegRuntime` preference test proves `osx-arm64` wins over an available x64 candidate.
- [ ] Bundled `ffmpeg -version` and `ffprobe -version` smoke passes.
- [ ] MP3 load through `ManagedSound` passes with the bundled runtime.
- [ ] Real `FfmpegAudioVariantProcessor` transform with `new PlaybackModifiers(125, 0)` passes.
- [ ] Existing cancellation/timeout cleanup tests remain green.
- [ ] Regular Mac CI is pinned to `macos-15` and caches the source-built runtime.
- [ ] Release YAML no longer owns a duplicate FFmpeg configure/build recipe.
- [ ] `.app` bundle retains runtime files, arm64 architecture, execute bits, and license.
- [ ] Windows build/tests remain unchanged and green.

## Handoff

After this implementation merges, HPA-512 can close and HPA-515 has the native CX audio prerequisite it needs. Do not pull HPA-515 OBS/recorder work into this implementation PR.