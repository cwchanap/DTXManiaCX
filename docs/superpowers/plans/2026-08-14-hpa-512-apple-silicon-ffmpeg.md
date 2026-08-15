# HPA-512 Native Apple Silicon FFmpeg Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make normal CX Mac build/run/test/publish output carry one verified native Apple Silicon `ffmpeg`/`ffprobe` runtime while preserving the existing real audio coverage and Windows behavior.

**Architecture:** Extract the existing FFmpeg 7.0.2 arm64 release recipe into one cached, host-independent shell builder. `DTXMania.Game.Mac.csproj` generates/stages the runtime on macOS and exposes it through the existing MSBuild copy/publish graph. `FfmpegRuntime` remains unchanged. Repair the existing encoded-audio test inputs instead of weakening coverage or expanding production codecs.

**Tech Stack:** .NET 8, MonoGame DesktopGL, FFMpegCore 5.4.0, FFmpeg 7.0.2 source build, Bash/MSBuild, xUnit, GitHub Actions.

## Prerequisite

HPA-623 must merge and leave `main` green before this plan starts.

HPA-623 owns the pre-existing CI bug where `DTXMania.Test.Mac.csproj --no-build` currently runs zero tests because only the Game project was built. HPA-512 must start from a Mac job that already executes the real Test.Mac suite and fails when coverage is missing.

Do not fold HPA-623 into the HPA-512 implementation PR.

## Global Constraints

- Keep FFmpeg `7.0.2`; no version upgrade.
- Keep source SHA-256 `8646515b638a3ad303e23af6a3587734447cb8fc0a0c064ecdb8e95c4fd8b389`.
- Add `--disable-autodetect` from the start so developer Homebrew state cannot affect the built runtime.
- Do not add `--disable-gpl` or `--disable-nonfree`; document that they are unnecessary for this minimal no-`--enable-lib*` build.
- Supported Mac target is native Apple Silicon arm64 only. After this change, `DTXMania.Game.Mac.csproj` is expected to fail on Intel Macs.
- Remove `MMTools.Executables.MacOS.X64` from `DTXMania.Game.Mac.csproj`.
- Keep `FFMpegCore` 5.4.0 and all `FfmpegRuntime` resolution behavior unchanged.
- Runtime layout stays `runtimes/osx-arm64/MMTools/{ffmpeg,ffprobe}`.
- PATH remains diagnostic fallback only; never treat PATH FFmpeg as proof that packaging works.
- Keep the native generation target macOS-only because Windows tests reference/evaluate `DTXMania.Game.Mac.csproj`.
- Do not add `libmp3lame`/`libvorbis` encoders to production.
- Keep the existing `PlaybackModifiers(50, 12)` real variant test; do not add the weaker `PlaybackModifiers(125, 0)` smoke.
- Leave `ManagedSoundFFmpegPathTests`, `FfmpegRuntimeCoverageTests`, and production resolver logic unchanged.
- Ship upstream `COPYING.LGPLv2.1` as `Licenses/FFmpeg-LGPL-2.1.txt`.
- `DTXMania.VideoRecorder` continues using PATH `ffprobe` for recording artifact verification. HPA-512 does not add MP4/H.264 support or wire the bundled runtime into the recorder.
- No new NuGet binary provider, committed production binaries, package-builder project, generic bootstrap framework, resolver, audio stack, recorder media work, OBS work, or signing/notarization redesign.

---

## Files

```text
Create:
  tools/ffmpeg/macos-arm64/build-runtime.sh
  tools/ffmpeg/macos-arm64/README.md
  tools/ffmpeg/macos-arm64/test-cache-lock.sh
  DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3
  DTXMania.Test/TestData/Audio/ffmpeg-tone.ogg
  DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs
  DTXMania.Test/Resources/ManagedSoundErrorPathTests.cs

Modify:
  DTXMania.Game/DTXMania.Game.Mac.csproj
  DTXMania.Game/Lib/Resources/ManagedSound.cs
  DTXMania.Test/DTXMania.Test.csproj
  DTXMania.Test/DTXMania.Test.Mac.csproj
  DTXMania.Test/Resources/FfmpegAudioVariantProcessorTests.cs
  DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs
  DTXMania.Test/Resources/FfmpegRuntimeTests.cs
  DTXMania.Test/Resources/ManagedSoundTests.cs
  .github/workflows/build-and-test.yml
  .github/workflows/release.yml

Do not modify:
  DTXMania.Game/Lib/Resources/FfmpegRuntime.cs
  DTXMania.Test/Resources/ManagedSoundFFmpegPathTests.cs

Only modify if packaged-output verification proves execute bits are lost:
  installer/macos/build-dmg.sh
  installer/macos/test-build-dmg.sh
```

> **Plan drift note (2026-08-15):** `FfmpegRuntimeTests.cs`, `FfmpegBundledRuntimeTests.cs`, `FfmpegAudioVariantProcessorTests.cs`, and `ManagedSoundTests.cs` were modified only to add the `[Collection("FfmpegRuntimeState")]` attribute — a one-line, behavior-preserving change that serializes FFmpeg-runtime-state tests. `FfmpegRuntimeTests.cs` is therefore no longer in the "Do not modify" set; it is in "Modify" with that single-attribute scope. `ManagedSoundErrorPathTests.cs` was introduced after the original file inventory was written and is now listed in both the top-level "Create" set and Task 3 below. The `FfmpegRuntimeStateCollection` is declared with `DisableParallelization = true` because its members mutate process-wide state (`FfmpegRuntime.Configuration`, `GlobalFFOptions`, and the process `PATH`); a plain collection would only prevent intra-collection concurrency and leave a race where unrelated test collections observe the mutated globals.

Keep the four HPA-512 tasks in one implementation PR after HPA-623 is merged.

---

### Task 1: Extract a reproducible native FFmpeg builder

**Files:**
- Create `tools/ffmpeg/macos-arm64/build-runtime.sh`
- Create `tools/ffmpeg/macos-arm64/README.md`
- Create `tools/ffmpeg/macos-arm64/test-cache-lock.sh`

**Produces:**

```text
build-runtime.sh <runtime-output-dir> <license-output-dir>

runtime-output-dir/
  ffmpeg
  ffprobe

license-output-dir/
  FFmpeg-LGPL-2.1.txt
```

Cache root:

```text
${DTXMANIA_FFMPEG_CACHE_ROOT:-$HOME/Library/Caches/DTXManiaCX/ffmpeg}/7.0.2/osx-arm64
```

- [ ] **Step 1: Create the script shell and hard-fail unsupported hosts.**

Use:

```bash
#!/usr/bin/env bash
set -euo pipefail

FFMPEG_VERSION="7.0.2"
FFMPEG_TARBALL_SHA256="8646515b638a3ad303e23af6a3587734447cb8fc0a0c064ecdb8e95c4fd8b389"
FFMPEG_URL="https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz"

if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "arm64" ]]; then
  echo "Native CX macOS runtime requires Apple Silicon (arm64); Intel/Rosetta is not supported." >&2
  exit 1
fi
```

Require exactly two output-directory arguments and create them before final copy.

- [ ] **Step 2: Move the current release configure surface into the builder and add only `--disable-autodetect`.**

Use the existing release flags plus:

```text
--disable-autodetect
```

Required configure surface:

```bash
./configure \
  --prefix="$install_dir" \
  --enable-static --disable-shared \
  --disable-doc --disable-htmlpages --disable-manpages \
  --disable-podpages --disable-txtpages \
  --disable-ffplay \
  --disable-everything \
  --disable-autodetect \
  --enable-decoder=mp3float \
  --enable-decoder=vorbis \
  --enable-decoder=pcm_s16le,pcm_s24le,pcm_f32le,pcm_u8,pcm_alaw,pcm_mulaw \
  --enable-decoder=adpcm_ima_wav,adpcm_ms \
  --enable-demuxer=mp3 \
  --enable-demuxer=wav,ogg,pcm_s16le \
  --enable-parser=mpegaudio,vorbis \
  --enable-protocol=file,pipe,unix \
  --enable-muxer=pcm_s16le \
  --enable-encoder=pcm_s16le \
  --enable-filter=aformat,anull,aresample,atempo,apad,atrim
```

Do not add `--enable-lib*`, `libmp3lame`, `libvorbis`, `--disable-gpl`, or `--disable-nonfree`.

- [ ] **Step 3: Implement the validated versioned cache.**

A cache hit is valid only when:

```text
ffmpeg exists + executable
ffprobe exists + executable
COPYING.LGPLv2.1 exists
source.sha256 contains the exact pinned source hash
```

On cache miss:

1. create a `mktemp -d` work root;
2. download source with `curl -fsSL`;
3. verify SHA-256 before extraction;
4. configure/build/install under the temporary root;
5. validate all runtime requirements below;
6. replace the cache only after validation succeeds;
7. write `source.sha256` only after successful validation.

Use a cleanup `trap`. Add a `mkdir`-based cache lock (`$cache_root/.build-lock`) held through cache validation, possible replacement, and output copies so concurrent invocations (e.g. parallel `dotnet build` and `dotnet publish`) cannot corrupt the cache or race on staging. A waiting invocation must revalidate after acquiring the lock so it can reuse a runtime produced by the previous invocation. Add `tools/ffmpeg/macos-arm64/test-cache-lock.sh` to verify the lock prevents concurrent cache replacement.

- [ ] **Step 4: Add architecture, capability, and portability validation.**

For both executables:

```bash
test -x "$bin"
file -b "$bin" | grep -qi arm64
"$bin" -version
```

For FFmpeg require:

```text
filters:  atempo apad atrim aformat aresample
decoders: mp3float vorbis pcm_s16le adpcm_ima_wav adpcm_ms
demuxers: mp3 wav ogg s16le
protocols: file pipe unix
encoder:  pcm_s16le
muxer:    s16le
```

The two ADPCM decoders are load-bearing for non-default playback of ADPCM WAV sources and must be checked explicitly.

The `unix` protocol is required by FFMpegCore's pipe-based invocation on macOS; without it the bundled runtime cannot decode encoded audio.

Revalidate the full capability surface on every cache hit (not just on cache miss) so a runtime built before a capability amendment (such as adding `unix`) cannot remain accepted solely because its source hash is still current.

Also validate dynamic dependencies for **both** executables:

```bash
otool -L "$bin"
```

Ignore the first `otool -L` header line. Every listed dependency must resolve under either:

```text
/usr/lib/
/System/Library/
```

Fail if `/opt/homebrew`, `/usr/local`, a build temp directory, or any other non-system dylib path appears.

- [ ] **Step 5: Copy validated output with explicit modes.**

Use:

```bash
install -m 755 "$cache_dir/ffmpeg" "$runtime_output_dir/ffmpeg"
install -m 755 "$cache_dir/ffprobe" "$runtime_output_dir/ffprobe"
install -m 644 "$cache_dir/COPYING.LGPLv2.1" "$license_output_dir/FFmpeg-LGPL-2.1.txt"
```

Do not use a permission-ambiguous builder copy.

- [ ] **Step 6: Prove a cold build.**

Run on Apple Silicon with any existing runtime cache removed:

```bash
rm -rf "$HOME/Library/Caches/DTXManiaCX/ffmpeg/7.0.2/osx-arm64"
rm -rf /tmp/dtx-ffmpeg-runtime /tmp/dtx-ffmpeg-licenses

bash tools/ffmpeg/macos-arm64/build-runtime.sh \
  /tmp/dtx-ffmpeg-runtime \
  /tmp/dtx-ffmpeg-licenses

test -x /tmp/dtx-ffmpeg-runtime/ffmpeg
test -x /tmp/dtx-ffmpeg-runtime/ffprobe
file -b /tmp/dtx-ffmpeg-runtime/ffmpeg | grep -qi arm64
file -b /tmp/dtx-ffmpeg-runtime/ffprobe | grep -qi arm64
test -f /tmp/dtx-ffmpeg-licenses/FFmpeg-LGPL-2.1.txt
```

The builder itself must already have failed if capability or `otool -L` checks fail.

- [ ] **Step 7: Prove the warm-cache path.**

Repeat the builder without deleting the cache. Expected: no source compile; validated cached files are copied.

- [ ] **Step 7b: Prove the cache lock prevents concurrent corruption.**

Run the cache lock regression test:

```bash
bash tools/ffmpeg/macos-arm64/test-cache-lock.sh
```

Expected: `PASS: cache lock held through output copy; replacement waited for complete copy`. This verifies that a second invocation cannot replace the cache until the first has finished copying all output files.

- [ ] **Step 8: Document provenance and support boundary.**

README must include:

```text
FFmpeg 7.0.2
official source URL
pinned SHA-256
exact configure command including --disable-autodetect and --enable-protocol=file,pipe,unix
why --disable-gpl / --disable-nonfree are omitted
required codec/filter/protocol surface including ADPCM decoders and unix protocol
system-dylib-only otool policy
COPYING.LGPLv2.1 handling
cache path and override
cache lock behavior (mkdir-based .build-lock, held through validation/replacement/copy)
clean-cache command
first-build compile behavior
Intel Mac build unsupported/hard-fail behavior
version/checksum update procedure
cold/warm verification commands
```

- [ ] **Step 9: Commit checkpoint.**

```bash
git add tools/ffmpeg/macos-arm64
git commit -m "build: extract native Apple Silicon ffmpeg runtime"
```

---

### Task 2: Make the Mac project own one incremental generated-content copy contract

**Files:**
- Modify `DTXMania.Game/DTXMania.Game.Mac.csproj`

**Consumes:** Task 1 builder.

**Produces:** one native runtime/license contract for Game output, Test.Mac output through `ProjectReference`, and publish output, while remaining inert on Windows.

- [ ] **Step 1: Remove only the obsolete x64 runtime package.**

Delete:

```xml
<PackageReference Include="MMTools.Executables.MacOS.X64" Version="1.0.6" />
```

Keep `FFMpegCore` unchanged. Do not edit `FfmpegRuntime`.

- [ ] **Step 2: Add project-local staging properties.**

Use equivalent properties:

```xml
<PropertyGroup>
  <NativeFfmpegStagingRoot>$(BaseIntermediateOutputPath)ffmpeg-runtime</NativeFfmpegStagingRoot>
  <NativeFfmpegRuntimeDir>$(NativeFfmpegStagingRoot)/runtimes/osx-arm64/MMTools</NativeFfmpegRuntimeDir>
  <NativeFfmpegLicenseDir>$(NativeFfmpegStagingRoot)/Licenses</NativeFfmpegLicenseDir>
</PropertyGroup>
```

- [ ] **Step 3: Add the macOS-only generated-content target.**

Keep the target hooked before:

```text
BeforeBuild;AssignTargetPaths;GetCopyToOutputDirectoryItems
```

Use this shape:

```xml
<Target Name="PrepareNativeFfmpegRuntime"
        Condition="$([MSBuild]::IsOSPlatform('OSX'))"
        BeforeTargets="BeforeBuild;AssignTargetPaths;GetCopyToOutputDirectoryItems"
        Inputs="$(MSBuildProjectDirectory)/../tools/ffmpeg/macos-arm64/build-runtime.sh"
        Outputs="$(NativeFfmpegRuntimeDir)/ffmpeg;$(NativeFfmpegRuntimeDir)/ffprobe;$(NativeFfmpegLicenseDir)/FFmpeg-LGPL-2.1.txt">
  <Exec
    Command="bash &quot;$(MSBuildProjectDirectory)/../tools/ffmpeg/macos-arm64/build-runtime.sh&quot; &quot;$(NativeFfmpegRuntimeDir)&quot; &quot;$(NativeFfmpegLicenseDir)&quot;" />
</Target>

<Target Name="DeclareNativeFfmpegRuntimeItems"
        Condition="$([MSBuild]::IsOSPlatform('OSX'))"
        AfterTargets="PrepareNativeFfmpegRuntime"
        BeforeTargets="AssignTargetPaths;GetCopyToOutputDirectoryItems">
  <ItemGroup>
    <!-- Set TargetPath, CopyToOutputDirectory=PreserveNewest, -->
    <!-- and CopyToPublishDirectory=PreserveNewest for ffmpeg, ffprobe, and license. -->
  </ItemGroup>
</Target>
```

The `PrepareNativeFfmpegRuntime` target uses `Inputs`/`Outputs` so that any change to `build-runtime.sh` (version, checksum, configure flags, protocols) forces re-staging even when the old staged binaries still exist. Without this, editing the builder and clearing only the FFmpeg cache would silently reuse stale staged binaries.

The `DeclareNativeFfmpegRuntimeItems` target is split out so the `None` items are declared on every build, even when `PrepareNativeFfmpegRuntime` is skipped by incremental build. If the items were inside the incremental target, a warm build would skip the target body and the files would never be copied to output.

Do not add a Test.Mac-specific FFmpeg copy target.

- [ ] **Step 4: Prove Windows evaluation is inert.**

On Windows/Windows CI:

```powershell
dotnet build DTXMania.Test/DTXMania.Test.csproj -c Debug
```

Expected: no Bash builder invocation.

- [ ] **Step 5: Prove Game build output on Apple Silicon.**

```bash
dotnet restore DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug --no-restore

runtime="DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools"
test -x "$runtime/ffmpeg"
test -x "$runtime/ffprobe"
file -b "$runtime/ffmpeg" | grep -qi arm64
file -b "$runtime/ffprobe" | grep -qi arm64
test -f DTXMania.Game/bin/Debug/net8.0/Licenses/FFmpeg-LGPL-2.1.txt
```

Run a second `dotnet build` with staging intact and confirm the `PrepareNativeFfmpegRuntime` target is skipped by incremental build (all outputs up-to-date with respect to `build-runtime.sh`). Then `touch tools/ffmpeg/macos-arm64/build-runtime.sh` and rebuild to confirm the target re-runs and re-stages from the validated cache.

- [ ] **Step 6: Prove ProjectReference propagation.**

```bash
dotnet build DTXMania.Test/DTXMania.Test.Mac.csproj -c Debug --no-restore

test_runtime="DTXMania.Test/bin/Debug/net8.0/runtimes/osx-arm64/MMTools"
test -x "$test_runtime/ffmpeg"
test -x "$test_runtime/ffprobe"
file -b "$test_runtime/ffmpeg" | grep -qi arm64
file -b "$test_runtime/ffprobe" | grep -qi arm64
test -f DTXMania.Test/bin/Debug/net8.0/Licenses/FFmpeg-LGPL-2.1.txt
```

If Game succeeds but Test.Mac misses files, fix the Game target/item metadata. Do not duplicate copy logic in the test project.

- [ ] **Step 7: Prove publish output.**

```bash
rm -rf /tmp/dtx-publish-arm64
dotnet publish DTXMania.Game/DTXMania.Game.Mac.csproj \
  -c Release -r osx-arm64 --self-contained \
  -p:PublishReadyToRun=false -p:TieredCompilation=false \
  -o /tmp/dtx-publish-arm64

runtime="/tmp/dtx-publish-arm64/runtimes/osx-arm64/MMTools"
test -x "$runtime/ffmpeg"
test -x "$runtime/ffprobe"
file -b "$runtime/ffmpeg" | grep -qi arm64
file -b "$runtime/ffprobe" | grep -qi arm64
test -f /tmp/dtx-publish-arm64/Licenses/FFmpeg-LGPL-2.1.txt
```

If copy steps lose execute bits, add only the smallest macOS-specific chmod hook needed by the existing build graph. Do not weaken `IsRunnableFile`.

- [ ] **Step 8: Record first-run latency risk.**

In the builder README/risk notes, state that a clean runtime cache miss during `dotnet run` compiles FFmpeg before launch and may exceed an E2E startup timeout. CI must warm/build before launching E2E rather than adding background bootstrap machinery.

- [ ] **Step 9: Commit checkpoint.**

```bash
git add DTXMania.Game/DTXMania.Game.Mac.csproj tools/ffmpeg/macos-arm64/README.md
git commit -m "build: bundle native ffmpeg in Mac outputs"
```

---

### Task 3: Repair existing Audio coverage and add the bundled-runtime PATH guard

**Files:**
- Create `DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3`
- Create `DTXMania.Test/TestData/Audio/ffmpeg-tone.ogg`
- Create `DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs`
- Create `DTXMania.Test/Resources/ManagedSoundErrorPathTests.cs`
- Modify `DTXMania.Test/DTXMania.Test.csproj`
- Modify `DTXMania.Test/DTXMania.Test.Mac.csproj`
- Modify `DTXMania.Test/Resources/FfmpegAudioVariantProcessorTests.cs`
- Modify `DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs`
- Modify `DTXMania.Test/Resources/FfmpegRuntimeTests.cs`
- Modify `DTXMania.Test/Resources/ManagedSoundTests.cs`
- Modify `DTXMania.Game/Lib/Resources/ManagedSound.cs`

> **Drift note (2026-08-15):** `FfmpegRuntimeTests.cs`, `FfmpegBundledRuntimeTests.cs`, `FfmpegAudioVariantProcessorTests.cs`, and `ManagedSoundTests.cs` were modified only to add the `[Collection("FfmpegRuntimeState")]` attribute (one line each, no logic change). `ManagedSoundErrorPathTests.cs` is the new error-path coverage class and also declares `FfmpegRuntimeStateCollection` with `[CollectionDefinition("FfmpegRuntimeState", DisableParallelization = true)]`. The non-parallel contract is mandatory because the error-path tests mutate process-wide state (`FfmpegRuntime.Configuration` via reflection, `GlobalFFOptions`, and the process `PATH`); without `DisableParallelization = true` an xUnit collection only blocks intra-collection concurrency, leaving a race where unrelated collections observe the mutated globals.

**Consumes:** Task 2 Test.Mac output contract.

**Produces:** the existing strong variant test working with the minimal runtime, plus one Audio-class proof that the bundle—not PATH—was selected.

- [ ] **Step 1: Generate deterministic project-owned MP3 and OGG fixtures once.**

Using a full development FFmpeg only for fixture creation:

```bash
mkdir -p DTXMania.Test/TestData/Audio

ffmpeg -hide_banner -loglevel error \
  -f lavfi -i "sine=frequency=440:sample_rate=44100:duration=0.25" \
  -ac 1 -codec:a libmp3lame -b:a 64k \
  -y DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3

ffmpeg -hide_banner -loglevel error \
  -f lavfi -i "sine=frequency=440:sample_rate=44100:duration=0.25" \
  -ac 1 -codec:a libvorbis -q:a 4 \
  -y DTXMania.Test/TestData/Audio/ffmpeg-tone.ogg
```

These are small project-owned test inputs, not production assets.

- [ ] **Step 2: Copy fixtures from both test projects.**

Mirror the existing TestData copy pattern. Ensure `TestData/Audio/**/*` lands under the same relative path in both Windows and Mac test output.

- [ ] **Step 3: Repair `PrepareAsync_EncodedAudio_ShouldNormalizeToRawPcm`.**

Keep:

- generated WAV source;
- committed MP3 source;
- committed OGG source;
- `new PlaybackModifiers(50, 12)`;
- current sample-rate/channel/byte-length/duration/pitch/frequency assertions.

Delete only the fixture-generation `FFMpegArguments` calls using `libmp3lame` / `libvorbis`.

Do not add a second weaker variant smoke.

- [ ] **Step 4: Add a dedicated Audio-trait bundled-runtime class.**

Create `FfmpegBundledRuntimeTests.cs` with class-level Audio trait. Do **not** add real filesystem/runtime behavior to `FfmpegRuntimeTests`, which remains a Unit-trait logic class.

Test shape:

```csharp
[Trait("Category", AudioTestUtils.AudioTestCategory)]
public class FfmpegBundledRuntimeTests
{
    [Fact]
    public void EnsureConfigured_OnNativeAppleSilicon_ShouldUseBundledExecutableRuntime()
    {
        if (!OperatingSystem.IsMacOS() ||
            RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            return;

        var result = FfmpegRuntime.EnsureConfigured();
        Assert.True(result.IsAvailable, result.DiagnosticReason);
        Assert.NotNull(result.BinaryFolder);

        var expected = Path.Combine(
            AppContext.BaseDirectory,
            "runtimes", "osx-arm64", "MMTools");

        Assert.Equal(
            Path.GetFullPath(expected),
            Path.GetFullPath(result.BinaryFolder!));

        // Assert ffmpeg + ffprobe exist and each has at least one Unix execute bit.
    }
}
```

`Assert.NotNull(result.BinaryFolder)` is mandatory. `ProbePathAvailability` returns `BinaryFolder: null`, so this is the assertion that prevents Homebrew/PATH from satisfying the test.

- [ ] **Step 5: Add one successful ManagedSound MP3 load.**

Use `ffmpeg-tone.mp3` from `AppContext.BaseDirectory/TestData/Audio` and assert non-zero duration / valid loaded sound state.

- [ ] **Step 6: Fix the stale missing-runtime diagnostic.**

Remove user guidance naming `MMTools.Executables.MacOS.X64`. Replace it with a generic message that the bundled platform FFmpeg runtime is missing/unusable and CX should be rebuilt/reinstalled.

Do not change resolution or install anything at runtime.

- [ ] **Step 7: Run focused Test.Mac Audio checks before any Homebrew FFmpeg installation in CI.**

From an already-built Test.Mac output:

```bash
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug \
  --no-build \
  --filter "FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~FfmpegBundledRuntimeTests|FullyQualifiedName~ManagedSoundTests" \
  --verbosity normal
```

Expected on Apple Silicon:

```text
WAV/MP3/OGG existing variant rows pass with PlaybackModifiers(50, 12)
BinaryFolder is non-null and equals Test.Mac bundled osx-arm64/MMTools
ffmpeg + ffprobe retain execute bits
valid MP3 ManagedSound load succeeds
```

- [ ] **Step 8: Run the real full Mac suite established by HPA-623.**

```bash
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  -c Debug --no-build --verbosity normal
```

Do not accept a sub-second build-only result. The command must show test discovery/execution and a real summary.

- [ ] **Step 9: Run Windows shared-test regression.**

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj -c Debug --verbosity normal
```

- [ ] **Step 10: Commit checkpoint.**

```bash
git add \
  DTXMania.Game/Lib/Resources/ManagedSound.cs \
  DTXMania.Test/DTXMania.Test.csproj \
  DTXMania.Test/DTXMania.Test.Mac.csproj \
  DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3 \
  DTXMania.Test/TestData/Audio/ffmpeg-tone.ogg \
  DTXMania.Test/Resources/FfmpegAudioVariantProcessorTests.cs \
  DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs \
  DTXMania.Test/Resources/FfmpegRuntimeTests.cs \
  DTXMania.Test/Resources/ManagedSoundErrorPathTests.cs \
  DTXMania.Test/Resources/ManagedSoundTests.cs

git commit -m "test: preserve audio coverage with minimal Mac ffmpeg"
```

---

### Task 4: Make native packaging checks authoritative and collapse release duplication

**Files:**
- Modify `.github/workflows/build-and-test.yml`
- Modify `.github/workflows/release.yml`
- Modify installer Mac scripts only if execute-bit loss is reproduced

**Consumes:** Tasks 1–3 and the already-green HPA-623 Mac test baseline.

**Produces:** one CI/release path using the same project-owned runtime without allowing PATH FFmpeg to mask packaging failures.

- [ ] **Step 1: Pin the regular Mac job to an explicit Apple Silicon runner and cache only the FFmpeg runtime cache.**

Replace `macos-latest` with the repository's chosen explicit Apple Silicon runner label. Keep the runner choice arm64 and stable; do not rely on `macos-latest` drift.

Add cache:

```yaml
- name: Cache native FFmpeg runtime
  uses: actions/cache@v6
  with:
    path: ~/Library/Caches/DTXManiaCX/ffmpeg
    key: macos-arm64-ffmpeg-${{ hashFiles('tools/ffmpeg/macos-arm64/build-runtime.sh') }}
```

Do not cache `bin`, `obj`, or publish output.

- [ ] **Step 2: Restore Test.Mac once, then keep named Game/Test builds.**

```text
restore DTXMania.Test.Mac.csproj
build DTXMania.Game.Mac.csproj --no-restore
verify Game runtime
build DTXMania.Test.Mac.csproj --no-restore
verify Test.Mac runtime
```

For both outputs require:

```bash
test -x ffmpeg
test -x ffprobe
file -b ffmpeg | grep -qi arm64
file -b ffprobe | grep -qi arm64
```

- [ ] **Step 3: Run the bundle-sensitive Audio checks before installing Homebrew FFmpeg.**

Run the Task 3 focused Audio filter immediately after Test.Mac build/runtime verification.

Then run the full real Mac suite with `--no-build` and coverage as established by HPA-623.

Keep the HPA-623 missing-coverage check fail-closed; do not weaken it back to a warning.

- [ ] **Step 4: Retain Automation, VideoRecorder, and prepared-chart AudioE2E.**

These remain after the normal Test.Mac suite. HPA-512 does not change recorder artifact verification behavior.

- [ ] **Step 5: Keep Homebrew FFmpeg installation after all HPA-512 bundle-sensitive checks.**

The existing CX Neon SFX validation needs a full FFmpeg. Keep its `brew install ffmpeg` step **after**:

```text
Game runtime verification
Test.Mac runtime verification
FfmpegBundledRuntimeTests
focused Audio tests
full Test.Mac suite
prepared-chart AudioE2E
```

This ordering prevents PATH FFmpeg from hiding a missing/broken bundle.

- [ ] **Step 6: Keep Windows CI as the cross-platform guard.**

Do not add a separate Windows FFmpeg pipeline. Existing Windows build/tests must stay green and prove the Mac bootstrap target remains inert when the Mac project is evaluated on Windows.

- [ ] **Step 7: Delete the duplicated source-build body from `release.yml`.**

After normal `dotnet publish -r osx-arm64`, release YAML should only verify the project-owned output:

```bash
runtime="publish/mac/runtimes/osx-arm64/MMTools"
test -x "$runtime/ffmpeg"
test -x "$runtime/ffprobe"
file -b "$runtime/ffmpeg" | grep -qi arm64
file -b "$runtime/ffprobe" | grep -qi arm64
test -f publish/mac/Licenses/FFmpeg-LGPL-2.1.txt
```

Do not duplicate builder capability or `otool -L` checks in YAML; `build-runtime.sh` owns those.

- [ ] **Step 8: Verify the existing `.app` copy preserves the runtime.**

After `build-dmg.sh`:

```bash
runtime="output/DTXMania.app/Contents/MacOS/runtimes/osx-arm64/MMTools"
test -x "$runtime/ffmpeg"
test -x "$runtime/ffprobe"
file -b "$runtime/ffmpeg" | grep -qi arm64
file -b "$runtime/ffprobe" | grep -qi arm64
test -f output/DTXMania.app/Contents/MacOS/Licenses/FFmpeg-LGPL-2.1.txt
```

If this passes, leave installer scripts unchanged. If only execute bits are lost, fix the existing copy path narrowly and add the matching regression test.

- [ ] **Step 9: Run final blast radius.**

Apple Silicon:

```bash
dotnet restore DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Debug --no-restore
dotnet build DTXMania.Test/DTXMania.Test.Mac.csproj -c Debug --no-restore
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj -c Debug --no-build --verbosity normal
dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj -c Debug --verbosity normal
dotnet test DTXMania.VideoRecorder.Tests/DTXMania.VideoRecorder.Tests.csproj -c Debug --verbosity normal
```

Run the existing prepared-chart `Category=AudioE2E` command before Homebrew FFmpeg is installed.

Windows/Windows CI:

```powershell
dotnet build DTXMania.Test/DTXMania.Test.csproj -c Debug
dotnet test DTXMania.Test/DTXMania.Test.csproj -c Debug --no-build --verbosity normal
```

- [ ] **Step 10: Commit checkpoint.**

```bash
git add .github/workflows/build-and-test.yml .github/workflows/release.yml
if ! git diff --quiet -- installer/macos/build-dmg.sh installer/macos/test-build-dmg.sh; then
  git add installer/macos/build-dmg.sh installer/macos/test-build-dmg.sh
fi
git commit -m "ci: validate bundled Apple Silicon ffmpeg"
```

---

## Final acceptance checklist

- [ ] HPA-623 is merged first and Mac CI already executes a real Test.Mac suite with fail-closed coverage.
- [ ] `MMTools.Executables.MacOS.X64` is absent from the Mac project.
- [ ] Builder uses FFmpeg 7.0.2 + pinned source SHA + `--disable-autodetect`.
- [ ] Builder configure surface enables `--enable-protocol=file,pipe,unix` (not just `file,pipe`).
- [ ] Builder validates `file`, `pipe`, and `unix` protocols on every invocation, including cache hits.
- [ ] Builder revalidates the full capability surface on every cache hit so a stale cached runtime built before a capability amendment cannot remain accepted.
- [ ] Builder holds a `mkdir`-based cache lock through cache validation, replacement, and output copies; `test-cache-lock.sh` verifies the lock prevents concurrent cache corruption.
- [ ] Builder explicitly verifies `adpcm_ima_wav` and `adpcm_ms` in addition to existing decoders.
- [ ] `otool -L` proves both shipped executables depend only on macOS system libraries.
- [ ] Normal Apple Silicon Game output contains executable arm64 `ffmpeg` and `ffprobe`.
- [ ] Warm/staged builds skip the `PrepareNativeFfmpegRuntime` target by incremental build (Inputs/Outputs), not by file-existence condition.
- [ ] Editing `build-runtime.sh` and rebuilding re-stages the runtime even when staged binaries already exist.
- [ ] `DeclareNativeFfmpegRuntimeItems` runs on every macOS build so `None` items are always declared, even when the staging target is skipped.
- [ ] Test.Mac receives the same runtime through the Game `ProjectReference` copy contract.
- [ ] Windows evaluates/builds the Mac project without invoking the native builder.
- [ ] Publish and `.app` output contain the same runtime and LGPL license.
- [ ] Existing arm64 preference tests remain unchanged and green.
- [ ] Existing `PrepareAsync_EncodedAudio_ShouldNormalizeToRawPcm` keeps WAV/MP3/OGG + `PlaybackModifiers(50, 12)` coverage without production encoders.
- [ ] New `FfmpegBundledRuntimeTests` is Audio-trait, not Unit-trait.
- [ ] Native bundled-runtime test asserts `BinaryFolder` is non-null and equals Test.Mac's bundled arm64 directory.
- [ ] `ManagedSound` successfully loads the valid MP3 fixture.
- [ ] Stale x64-package user diagnostic is removed.
- [ ] Homebrew FFmpeg is installed only after bundle-sensitive Mac audio/E2E checks.
- [ ] Release YAML no longer owns a duplicate configure/build recipe.
- [ ] Intel Mac build failure is documented as the intentional support boundary.
- [ ] Recorder artifact verification remains PATH-ffprobe based; no bundled MP4 verifier is introduced.

## Risks carried into implementation review

1. **Cold-build latency** — a first `dotnet run` may compile FFmpeg and exceed E2E startup expectations; warm the cache before E2E.
2. **Host dependency leakage** — `--disable-autodetect` plus `otool -L` must prevent Homebrew-linked binaries from entering the cache.
3. **Generated MSBuild propagation** — Game-bin success alone is insufficient; Test.Mac and publish outputs must be checked.
4. **Unix execute bits** — any lost bit makes `IsRunnableFile` reject the bundle; fail/fix copying rather than weakening the resolver.
5. **PATH masking** — `IsAvailable == true` is insufficient; `BinaryFolder != null` and exact bundled path are required.
6. **Intel support** — the Mac project intentionally hard-fails outside native arm64.
7. **Stale staging after builder changes** — the MSBuild staging target must use `Inputs`/`Outputs` keyed on `build-runtime.sh` so editing the builder (version, checksum, configure flags, protocols) forces re-staging even when old staged binaries still exist. File-existence-only conditions silently reuse stale runtimes.
8. **Concurrent cache access** — parallel `dotnet build` and `dotnet publish` invocations can race on the shared FFmpeg cache; a `mkdir`-based cache lock held through validation, replacement, and output copies prevents corruption.
9. **Missing `unix` protocol** — FFMpegCore's pipe-based invocation on macOS requires the `unix` protocol; without it the bundled runtime cannot decode encoded audio. The protocol must be in the configure surface and validated on every cache hit.

## Handoff

After HPA-512 merges, HPA-515 has the native **CX preview/gameplay audio** prerequisite it requires. `DTXMania.VideoRecorder` continues resolving its artifact-verification `ffprobe` from PATH and remains outside this ticket.