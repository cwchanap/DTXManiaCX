# HPA-512 Native Apple Silicon FFmpeg Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make normal CX Mac build/run/test/publish output carry one verified native Apple Silicon `ffmpeg`/`ffprobe` runtime, while preserving the existing real audio tests and Windows build behavior.

**Architecture:** Extract the exact FFmpeg 7.0.2 arm64 source recipe already shipping in `release.yml` into one cached shell builder. `DTXMania.Game.Mac.csproj` generates the files on macOS before MSBuild resolves output-copy items, adds the generated items inside that target, and exposes them through the normal `GetCopyToOutputDirectoryItems`/publish path. Existing `FfmpegRuntime` remains unchanged. Repair the existing MP3/OGG variant test inputs instead of adding a weaker duplicate variant test.

**Tech Stack:** .NET 8, MonoGame DesktopGL, FFMpegCore 5.4.0, FFmpeg 7.0.2 source build, Bash/MSBuild, xUnit, GitHub Actions `macos-15`.

## Global Constraints

- Keep FFmpeg 7.0.2 for this ticket; no version upgrade.
- Official source tarball SHA-256 stays `8646515b638a3ad303e23af6a3587734447cb8fc0a0c064ecdb8e95c4fd8b389`.
- First extract the **current shipping configure list exactly**. `--disable-autodetect`, `--disable-gpl`, and `--disable-nonfree` may be retained only after a separate clean cold build passes all capability checks.
- Supported macOS target is native Apple Silicon arm64 only; no Intel/Rosetta fallback.
- Remove `MMTools.Executables.MacOS.X64` from `DTXMania.Game.Mac.csproj`.
- Keep `FFMpegCore` 5.4.0 and `FfmpegRuntime` production resolution behavior unchanged.
- Runtime layout remains `runtimes/osx-arm64/MMTools/{ffmpeg,ffprobe}`.
- PATH remains a diagnostic fallback only; do not install/use PATH FFmpeg as production recovery.
- The native generation target must be `Condition="$([MSBuild]::IsOSPlatform('OSX'))"` because Windows tests reference `DTXMania.Game.Mac.csproj`.
- Preserve the current minimal CX codec/filter surface; do not add `libmp3lame`/`libvorbis` encoders to production only to generate test fixtures.
- Keep the existing `PlaybackModifiers(50, 12)` real variant test; do not add a second `PlaybackModifiers(125, 0)` variant smoke.
- Leave `ManagedSoundFFmpegPathTests` and the existing first-complete-candidate `FfmpegRuntimeCoverageTests` behavior unchanged.
- Ship upstream `COPYING.LGPLv2.1` as `Licenses/FFmpeg-LGPL-2.1.txt`.
- No new NuGet binary provider, committed production binaries, package-builder project, generic bootstrap framework, resolver, audio stack, recorder media work, OBS work, or signing/notarization redesign.

---

## Files

```text
Create:
  tools/ffmpeg/macos-arm64/build-runtime.sh
  tools/ffmpeg/macos-arm64/README.md
  DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3
  DTXMania.Test/TestData/Audio/ffmpeg-tone.ogg

Modify:
  DTXMania.Game/DTXMania.Game.Mac.csproj
  DTXMania.Game/Lib/Resources/ManagedSound.cs
  DTXMania.Test/DTXMania.Test.csproj
  DTXMania.Test/DTXMania.Test.Mac.csproj
  DTXMania.Test/Resources/FfmpegAudioVariantProcessorTests.cs
  DTXMania.Test/Resources/FfmpegRuntimeTests.cs
  DTXMania.Test/Resources/ManagedSoundTests.cs
  .github/workflows/build-and-test.yml
  .github/workflows/release.yml

Only modify if packaged-output verification proves execute bits are lost:
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

Cache root:

```text
${DTXMANIA_FFMPEG_CACHE_ROOT:-$HOME/Library/Caches/DTXManiaCX/ffmpeg}/7.0.2/osx-arm64
```

- [ ] **Step 1: Copy the currently shipping source recipe exactly out of `release.yml`.**

Start the script with:

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

Require exactly two positional destinations and normalize/create them before copying.

- [ ] **Step 2: Preserve the existing configure list before trying any license-hardening delta.**

Baseline configure flags must match the current release workflow:

```bash
./configure \
  --prefix="$install_dir" \
  --enable-static --disable-shared \
  --disable-doc --disable-htmlpages --disable-manpages \
  --disable-podpages --disable-txtpages \
  --disable-ffplay \
  --disable-everything \
  --enable-decoder=mp3float \
  --enable-decoder=vorbis \
  --enable-decoder=pcm_s16le,pcm_s24le,pcm_f32le,pcm_u8,pcm_alaw,pcm_mulaw \
  --enable-decoder=adpcm_ima_wav,adpcm_ms \
  --enable-demuxer=mp3 \
  --enable-demuxer=wav,ogg,pcm_s16le \
  --enable-parser=mpegaudio,vorbis \
  --enable-protocol=file,pipe \
  --enable-muxer=pcm_s16le \
  --enable-encoder=pcm_s16le \
  --enable-filter=aformat,anull,aresample,atempo,apad,atrim
```

Do not add `libmp3lame`, `libvorbis`, Homebrew libraries, or any other external codec dependency.

- [ ] **Step 3: Implement a versioned validated cache.**

Use:

```bash
cache_root="${DTXMANIA_FFMPEG_CACHE_ROOT:-$HOME/Library/Caches/DTXManiaCX/ffmpeg}"
cache_dir="$cache_root/$FFMPEG_VERSION/osx-arm64"
```

A cache hit is valid only when all are true:

```text
$cache_dir/ffmpeg exists + executable
$cache_dir/ffprobe exists + executable
$cache_dir/COPYING.LGPLv2.1 exists
$cache_dir/source.sha256 contains the exact pinned source hash
```

On a miss:

1. create a `mktemp -d` work root;
2. `curl -fsSL` the tarball;
3. verify with `shasum -a 256 -c`;
4. extract/configure/build/install under the temporary root;
5. validate before replacing the cache;
6. write `source.sha256` only after validation succeeds.

Use a shell `trap` to remove the temporary work root. Do not add cache locking.

- [ ] **Step 4: Move the current release capability checks into the builder.**

For each binary independently:

```bash
test -x "$bin"
file -b "$bin" | grep -qi 'arm64'
```

Run:

```bash
"$ffmpeg" -version
"$ffprobe" -version
```

Require:

```text
filters:  atempo apad atrim aformat aresample
decoders: mp3float vorbis pcm_s16le
demuxers: mp3 wav ogg s16le
encoder:  pcm_s16le
muxer:    s16le
```

Copy validated files with:

```bash
install -m 755 "$cache_dir/ffmpeg" "$runtime_output_dir/ffmpeg"
install -m 755 "$cache_dir/ffprobe" "$runtime_output_dir/ffprobe"
install -m 644 "$cache_dir/COPYING.LGPLv2.1" "$license_output_dir/FFmpeg-LGPL-2.1.txt"
```

Use `install`, not a permission-ambiguous copy, for the builder's own output.

- [ ] **Step 5: Prove the extracted shipping recipe with a cold build.**

Run on Apple Silicon:

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

This cold build is the baseline gate. Do not change configure flags before it passes.

- [ ] **Step 6: Trial explicit license/autodetect hardening only after the baseline is green.**

Add these three flags together:

```text
--disable-autodetect
--disable-gpl
--disable-nonfree
```

Delete the cache and repeat Step 5. Keep the three flags only if the clean build plus every architecture/filter/decoder/demuxer/encoder/muxer check passes. If not, revert the flags and retain the proven recipe; HPA-512 does not depend on this hardening.

- [ ] **Step 7: Prove the warm-cache path.**

```bash
rm -rf /tmp/dtx-ffmpeg-runtime /tmp/dtx-ffmpeg-licenses
time bash tools/ffmpeg/macos-arm64/build-runtime.sh \
  /tmp/dtx-ffmpeg-runtime \
  /tmp/dtx-ffmpeg-licenses

test -x /tmp/dtx-ffmpeg-runtime/ffmpeg
test -x /tmp/dtx-ffmpeg-runtime/ffprobe
```

Expected: no source compile; validated cached files are copied.

- [ ] **Step 8: Write provenance/update documentation.**

`README.md` must contain:

```text
FFmpeg 7.0.2
official source URL
pinned SHA-256
actual final configure command
why the minimal feature list exists
COPYING.LGPLv2.1 handling
cache path and DTXMANIA_FFMPEG_CACHE_ROOT override
clean-cache command
version + checksum update procedure
cold/warm verification commands
```

- [ ] **Step 9: Commit checkpoint.**

```bash
git add tools/ffmpeg/macos-arm64
git commit -m "build: extract native Apple Silicon ffmpeg runtime"
```

---

### Task 2: Make the Mac project own one generated-content copy contract

**Files:**
- Modify `DTXMania.Game/DTXMania.Game.Mac.csproj`

**Consumes:** Task 1 builder.

**Produces:** native runtime/license files in Game output, Test.Mac output through `ProjectReference`, and publish output, without executing the builder on Windows.

- [ ] **Step 1: Remove only the obsolete Mac x64 runtime package.**

Delete:

```xml
<PackageReference Include="MMTools.Executables.MacOS.X64" Version="1.0.6" />
```

Keep:

```xml
<PackageReference Include="FFMpegCore" Version="5.4.0" />
```

Do not change the Windows project or `FfmpegRuntime`.

- [ ] **Step 2: Define intermediate staging paths in the Mac project.**

Add properties equivalent to:

```xml
<PropertyGroup>
  <NativeFfmpegStagingRoot>$(BaseIntermediateOutputPath)ffmpeg-runtime</NativeFfmpegStagingRoot>
  <NativeFfmpegRuntimeDir>$(NativeFfmpegStagingRoot)/runtimes/osx-arm64/MMTools</NativeFfmpegRuntimeDir>
  <NativeFfmpegLicenseDir>$(NativeFfmpegStagingRoot)/Licenses</NativeFfmpegLicenseDir>
</PropertyGroup>
```

- [ ] **Step 3: Add the generated-content target with the exact lifecycle hooks.**

Use this shape, adapting XML escaping/path quoting only as required by MSBuild:

```xml
<Target Name="PrepareNativeFfmpegRuntime"
        Condition="$([MSBuild]::IsOSPlatform('OSX'))"
        BeforeTargets="BeforeBuild;AssignTargetPaths;GetCopyToOutputDirectoryItems">
  <Exec Command="bash &quot;$(MSBuildProjectDirectory)/../tools/ffmpeg/macos-arm64/build-runtime.sh&quot; &quot;$(NativeFfmpegRuntimeDir)&quot; &quot;$(NativeFfmpegLicenseDir)&quot;" />

  <ItemGroup>
    <None Include="$(NativeFfmpegRuntimeDir)/ffmpeg">
      <TargetPath>runtimes/osx-arm64/MMTools/ffmpeg</TargetPath>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
    </None>
    <None Include="$(NativeFfmpegRuntimeDir)/ffprobe">
      <TargetPath>runtimes/osx-arm64/MMTools/ffprobe</TargetPath>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
    </None>
    <None Include="$(NativeFfmpegLicenseDir)/FFmpeg-LGPL-2.1.txt">
      <TargetPath>Licenses/FFmpeg-LGPL-2.1.txt</TargetPath>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
    </None>
  </ItemGroup>
</Target>
```

The generated `None` items are intentionally added **inside** the target after the builder has created the files. Do not replace this with static top-level items for files that do not exist during evaluation.

Do not add a test-project FFmpeg copy target.

- [ ] **Step 4: Prove the target is inert on Windows before doing Mac validation.**

On Windows or Windows CI:

```powershell
dotnet build DTXMania.Test/DTXMania.Test.csproj -c Debug
```

Expected: succeeds without attempting `tools/ffmpeg/macos-arm64/build-runtime.sh`. This is required because `DTXMania.Test.csproj` references `DTXMania.Game.Mac.csproj`.

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

Do not use `dotnet run` as an acceptance command; the GUI does not terminate and adds no stronger packaging evidence.

- [ ] **Step 6: Prove ProjectReference propagation by explicitly building Test.Mac.**

```bash
dotnet build DTXMania.Test/DTXMania.Test.Mac.csproj -c Debug --no-restore

test_runtime="DTXMania.Test/bin/Debug/net8.0/runtimes/osx-arm64/MMTools"
test -x "$test_runtime/ffmpeg"
test -x "$test_runtime/ffprobe"
file -b "$test_runtime/ffmpeg" | grep -qi arm64
file -b "$test_runtime/ffprobe" | grep -qi arm64
test -f DTXMania.Test/bin/Debug/net8.0/Licenses/FFmpeg-LGPL-2.1.txt
```

If Game output passes but Test.Mac output is missing, fix the Game project's generated-item/target metadata. Do not add a duplicate test-project copy implementation.

If files exist but `test -x` fails, add the smallest macOS-only post-copy `chmod +x` target required for Game/Test/publish output and add a regression assertion. Do not weaken `FfmpegRuntime.IsRunnableFile`.

- [ ] **Step 7: Prove self-contained publish output.**

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

- [ ] **Step 8: Commit checkpoint.**

```bash
git add DTXMania.Game/DTXMania.Game.Mac.csproj
git commit -m "build: bundle native ffmpeg in Mac outputs"
```

---

### Task 3: Repair existing Audio tests and add only missing product gates

**Files:**
- Create `DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3`
- Create `DTXMania.Test/TestData/Audio/ffmpeg-tone.ogg`
- Modify `DTXMania.Test/DTXMania.Test.csproj`
- Modify `DTXMania.Test/DTXMania.Test.Mac.csproj`
- Modify `DTXMania.Test/Resources/FfmpegAudioVariantProcessorTests.cs`
- Modify `DTXMania.Test/Resources/FfmpegRuntimeTests.cs`
- Modify `DTXMania.Test/Resources/ManagedSoundTests.cs`
- Modify `DTXMania.Game/Lib/Resources/ManagedSound.cs`

**Consumes:** Task 2 test-output runtime contract.

**Produces:** existing real variant coverage that works with the minimal runtime, plus one bundled-path/execute-bit gate and one successful MP3 load.

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

These are small generated test inputs. Do not copy a third-party song/audio asset.

- [ ] **Step 2: Copy the fixtures from both test projects.**

Add to both `DTXMania.Test.csproj` and `DTXMania.Test.Mac.csproj`:

```xml
<ItemGroup>
  <None Include="TestData/Audio/**/*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

Both projects compile the shared Audio tests, so both need the same fixture path.

- [ ] **Step 3: Repair the existing real encoded-audio variant test instead of adding another variant smoke.**

In `PrepareAsync_EncodedAudio_ShouldNormalizeToRawPcm`, remove the `FFMpegArguments` fixture-encoding block that calls `libmp3lame` / `libvorbis`.

Keep the three rows and use this source selection shape:

```csharp
var source = extension switch
{
    "wav" => WriteToneWav("tone.wav", durationSeconds: 0.25),
    "mp3" => Path.Combine(
        AppContext.BaseDirectory,
        "TestData", "Audio", "ffmpeg-tone.mp3"),
    "ogg" => Path.Combine(
        AppContext.BaseDirectory,
        "TestData", "Audio", "ffmpeg-tone.ogg"),
    _ => throw new ArgumentOutOfRangeException(nameof(extension)),
};

Assert.True(File.Exists(source), $"Missing audio fixture: {source}");

var modifiers = new PlaybackModifiers(50, 12);
var artifact = await new FfmpegAudioVariantProcessor().PrepareAsync(
    source,
    modifiers,
    CancellationToken.None);
```

Keep the existing sample-rate/channel/byte-length/duration/pitch/frequency assertions unchanged.

Do not add `PlaybackModifiers(125, 0)` coverage; `PlaybackModifiers(50, 12)` already exercises the harder atempo chain.

- [ ] **Step 4: Add one real packaged-runtime filesystem gate to `FfmpegRuntimeTests`.**

Add required usings for `System.Runtime.InteropServices` and `AudioTestUtils` if not already present.

Add a test equivalent to:

```csharp
[Fact]
[Trait("Category", AudioTestUtils.AudioTestCategory)]
public void EnsureConfigured_OnNativeAppleSilicon_ShouldUseBundledExecutableRuntime()
{
    if (!OperatingSystem.IsMacOS() ||
        RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        return;

    var result = FfmpegRuntime.EnsureConfigured();
    Assert.True(result.IsAvailable, result.DiagnosticReason);

    var expected = Path.Combine(
        AppContext.BaseDirectory,
        "runtimes", "osx-arm64", "MMTools");
    Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(result.BinaryFolder!));

    const UnixFileMode execute =
        UnixFileMode.UserExecute |
        UnixFileMode.GroupExecute |
        UnixFileMode.OtherExecute;

    foreach (var name in new[] { "ffmpeg", "ffprobe" })
    {
        var path = Path.Combine(expected, name);
        Assert.True(File.Exists(path), $"Missing bundled runtime: {path}");
        Assert.NotEqual(0, File.GetUnixFileMode(path) & execute);
    }
}
```

Do not change `ManagedSoundFFmpegPathTests`; it already covers arm64 preference over x64. Do not retarget `FfmpegRuntimeCoverageTests` to duplicate that behavior.

- [ ] **Step 5: Add one successful ManagedSound MP3 test using the committed fixture.**

In `ManagedSoundTests` add:

```csharp
[Fact]
public void Constructor_WithValidMp3Fixture_CreatesSuccessfully()
{
    var path = Path.Combine(
        AppContext.BaseDirectory,
        "TestData", "Audio", "ffmpeg-tone.mp3");
    Assert.True(File.Exists(path), $"Missing audio fixture: {path}");

    using var sound = new ManagedSound(path);

    Assert.NotNull(sound.SoundEffect);
    Assert.True(sound.Duration > TimeSpan.Zero);
}
```

The class already carries the Audio trait. Run with `ALSOFT_DRIVERS=null` in CI.

- [ ] **Step 6: Fix the stale missing-runtime diagnostic in `ManagedSound.LoadMp3File`.**

Replace the package-specific text naming `MMTools.Executables.MacOS.X64` with one platform-neutral bundled-runtime message, for example:

```text
FFmpeg runtime not found. MP3 support requires the FFmpeg runtime bundled with DTXManiaCX. Rebuild or reinstall DTXManiaCX and verify the platform runtime contains both ffmpeg and ffprobe.
```

Do not change resolution logic or add recovery installation code.

- [ ] **Step 7: Run the repaired existing Audio gates from an already-built Test.Mac output.**

```bash
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug \
  --no-build \
  --filter "FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~FfmpegRuntimeTests|FullyQualifiedName~ManagedSoundTests" \
  --verbosity normal
```

Expected:

```text
WAV/MP3/OGG PrepareAsync_EncodedAudio rows pass with PlaybackModifiers(50, 12)
native BinaryFolder points to test-bin runtimes/osx-arm64/MMTools
both native files retain execute bits
ManagedSound valid MP3 load succeeds
existing cancellation/timeout tests remain green
```

- [ ] **Step 8: Run Windows shared-test regression.**

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj -c Debug --verbosity normal
```

Expected: Windows continues using its normal Windows runtime package; committed fixtures work identically and the Mac bootstrap target remains inert.

- [ ] **Step 9: Commit checkpoint.**

```bash
git add \
  DTXMania.Game/Lib/Resources/ManagedSound.cs \
  DTXMania.Test/DTXMania.Test.csproj \
  DTXMania.Test/DTXMania.Test.Mac.csproj \
  DTXMania.Test/TestData/Audio/ffmpeg-tone.mp3 \
  DTXMania.Test/TestData/Audio/ffmpeg-tone.ogg \
  DTXMania.Test/Resources/FfmpegAudioVariantProcessorTests.cs \
  DTXMania.Test/Resources/FfmpegRuntimeTests.cs \
  DTXMania.Test/Resources/ManagedSoundTests.cs

git commit -m "test: preserve audio coverage with minimal Mac ffmpeg"
```

---

### Task 4: Make native CI authoritative and collapse release duplication

**Files:**
- Modify `.github/workflows/build-and-test.yml`
- Modify `.github/workflows/release.yml`
- Modify `installer/macos/build-dmg.sh` / `installer/macos/test-build-dmg.sh` only if an execute-bit regression is actually reproduced

**Consumes:** Tasks 1–3.

**Produces:** one native CI path and one release path using the same project-owned runtime.

- [ ] **Step 1: Pin regular Mac CI to native Apple Silicon and cache only the FFmpeg source-build cache.**

Change:

```yaml
runs-on: macos-15
```

Add after checkout/setup:

```yaml
- name: Cache native FFmpeg runtime
  uses: actions/cache@v6
  with:
    path: ~/Library/Caches/DTXManiaCX/ffmpeg
    key: macos-arm64-ffmpeg-${{ hashFiles('tools/ffmpeg/macos-arm64/build-runtime.sh') }}
```

Do not cache `bin`, `obj`, or publish output.

- [ ] **Step 2: Restore through Test.Mac so all referenced projects are restored once.**

Use:

```yaml
- name: Restore dependencies
  run: dotnet restore DTXMania.Test/DTXMania.Test.Mac.csproj
```

- [ ] **Step 3: Keep a named Game build and verify its runtime.**

```yaml
- name: Build Mac game project
  run: dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug --no-restore

- name: Verify Mac game native FFmpeg runtime
  shell: bash
  run: |
    runtime="DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools"
    test -x "$runtime/ffmpeg"
    test -x "$runtime/ffprobe"
    file -b "$runtime/ffmpeg" | grep -qi arm64
    file -b "$runtime/ffprobe" | grep -qi arm64
    "$runtime/ffmpeg" -version
    "$runtime/ffprobe" -version
```

- [ ] **Step 4: Explicitly build Test.Mac before any `--no-build` test invocation.**

```yaml
- name: Build Mac test project
  run: dotnet build DTXMania.Test/DTXMania.Test.Mac.csproj --configuration Debug --no-restore

- name: Verify Mac test native FFmpeg runtime
  shell: bash
  run: |
    runtime="DTXMania.Test/bin/Debug/net8.0/runtimes/osx-arm64/MMTools"
    test -x "$runtime/ffmpeg"
    test -x "$runtime/ffprobe"
    file -b "$runtime/ffmpeg" | grep -qi arm64
    file -b "$runtime/ffprobe" | grep -qi arm64
```

This named build is the ProjectReference/copy-contract gate. Do not depend on a focused `dotnet test` accidentally building Test.Mac first.

- [ ] **Step 5: Run focused audio packaging/product gates with `--no-build`, then retain the existing full suite.**

```yaml
- name: Run native FFmpeg audio checks on macOS
  env:
    ALSOFT_DRIVERS: 'null'
  run: >
    dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
    --configuration Debug --no-build --verbosity normal
    --filter "FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~FfmpegRuntimeTests|FullyQualifiedName~ManagedSoundTests"
```

Then keep the existing full Mac test step with `--no-build` and coverage, plus Automation, VideoRecorder, and prepared-chart AudioE2E steps.

- [ ] **Step 6: Keep Windows CI as the cross-platform guard.**

Do not add a separate Windows FFmpeg pipeline. The existing Windows `dotnet test DTXMania.Test/DTXMania.Test.csproj` build path must remain green; it transitively evaluates/builds `DTXMania.Game.Mac.csproj` and therefore proves the macOS condition prevents the native builder from running there.

- [ ] **Step 7: Delete the duplicated native source-build body from `release.yml`.**

After the existing `dotnet publish ... -r osx-arm64`, require only:

```bash
runtime="publish/mac/runtimes/osx-arm64/MMTools"
test -x "$runtime/ffmpeg"
test -x "$runtime/ffprobe"
file -b "$runtime/ffmpeg" | grep -qi arm64
file -b "$runtime/ffprobe" | grep -qi arm64
test -f publish/mac/Licenses/FFmpeg-LGPL-2.1.txt
```

Do not duplicate feature checks in YAML; `build-runtime.sh` owns them.

- [ ] **Step 8: Verify the existing app-bundle copy preserves the same runtime.**

After `build-dmg.sh`:

```bash
runtime="output/DTXMania.app/Contents/MacOS/runtimes/osx-arm64/MMTools"
test -x "$runtime/ffmpeg"
test -x "$runtime/ffprobe"
file -b "$runtime/ffmpeg" | grep -qi arm64
file -b "$runtime/ffprobe" | grep -qi arm64
test -f output/DTXMania.app/Contents/MacOS/Licenses/FFmpeg-LGPL-2.1.txt
```

If this passes, leave `installer/macos/build-dmg.sh` unchanged. If it fails only because execute bits are lost, fix the existing publish-output copy path narrowly and add the matching regression to `test-build-dmg.sh`.

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

Run the existing prepared-chart `Category=AudioE2E` command on `macos-15`.

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

- [ ] `MMTools.Executables.MacOS.X64` is absent from the Mac project.
- [ ] Exact current `release.yml` FFmpeg recipe is reproducible from `build-runtime.sh`; optional `--disable-*` hardening is retained only after a clean verified build.
- [ ] Normal Apple Silicon Game output contains executable arm64 `ffmpeg` and `ffprobe`.
- [ ] `DTXMania.Test.Mac` output receives the same runtime through the Game `ProjectReference` copy contract.
- [ ] Windows builds `DTXMania.Test.csproj` without invoking the native Mac builder.
- [ ] Publish output contains the same runtime and FFmpeg LGPL license.
- [ ] Existing `ManagedSoundFFmpegPathTests.WithArm64Present_ShouldPreferArm64OverX64` remains unchanged and green.
- [ ] Existing `PrepareAsync_EncodedAudio_ShouldNormalizeToRawPcm` keeps WAV/MP3/OGG + `PlaybackModifiers(50, 12)` coverage without requiring bundled MP3/OGG encoders.
- [ ] Test-owned MP3/OGG fixtures are copied by both test projects.
- [ ] Native `FfmpegRuntime.EnsureConfigured().BinaryFolder` resolves the Test.Mac bundled arm64 directory and both files retain execute bits.
- [ ] `ManagedSound` successfully loads the valid MP3 fixture.
- [ ] Existing audio-variant cancellation/timeout tests remain green.
- [ ] Stale `MMTools.Executables.MacOS.X64` user diagnostic is removed.
- [ ] Regular Mac CI is pinned to `macos-15`, caches the script-built runtime, and explicitly builds Test.Mac before `--no-build` tests.
- [ ] Release YAML no longer owns a duplicate FFmpeg configure/build recipe.
- [ ] `.app` contains executable arm64 runtime files plus `FFmpeg-LGPL-2.1.txt`.
- [ ] Windows build/tests remain green and otherwise unchanged.

## Risks carried into implementation review

1. **Existing Audio test fixture encoding** — must be removed before the minimal runtime replaces the full x64 build, or Mac Audio tests fail on `libmp3lame`/`libvorbis` before testing the product path.
2. **Generated MSBuild content / ProjectReference propagation** — Game-bin success is insufficient; Test.Mac bin must be explicitly built and checked.
3. **Unix execute-bit preservation** — any lost bit makes `FfmpegRuntime.IsRunnableFile` reject the bundled runtime and fall back to PATH; fail rather than weakening the resolver.
4. **Windows evaluates the Mac project** — the generation target must remain macOS-only.
5. **Configure hardening delta** — do not conflate extraction with unproven flag changes; validate the shipping recipe first.

## Handoff

After this implementation merges, HPA-512 can close and HPA-515 has the native CX audio prerequisite it needs. Do not pull HPA-515 OBS/recorder work into this implementation PR.