# HPA-11 Chart Background Video Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans`. Keep planning and implementation on this same PR.

**Goal:** Play chart-authored DTX AVI video behind gameplay lanes/HUD, synchronized to the existing CX logical song clock across pause/resume and 50–150% Play Speed, with static-background fallback on all media failures.

**Architecture:** Add one `ChartVideoEvent` list finalized by `ChartTimingMap`; collect `#AVIxx` / `#VIDEOxx` definitions across the whole chart file; use the existing `currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime)` as the only runtime clock; decode the first supported AVI profile through the existing FFmpeg runtime into a bounded blocking CPU-frame queue; select frames with a tiny pure helper; upload one current texture on the game thread; composite it from `DrawBackground()` at stage-owned depth `0.95f`.

**Tech Stack:** .NET 8, C#, MonoGame 3.8.x, FFMpegCore 5.4.0 / existing FFmpeg runtime, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-27-hpa-11-chart-background-video-design.md`

## Global constraints

- One PR for HPA-11 planning + implementation.
- Support only `#AVIxx` / `#VIDEOxx` + channels `54` / `5A` in this slice.
- Both channels use the same CX background-video behavior.
- No AVIPAN, MovieMode, static BGA/BMP, video config/alpha, embedded movie audio, generic media framework, or extra native backend.
- Reuse `ParsedChart.FinalizeChart()` / `ChartTimingMap`; no video timing calculator.
- Reuse the **existing** `currentTimeMs` from `_songTimer.GetCurrentMs(_currentGameTime)`; there is no `SongPositionMs` property and no second Play Speed multiply.
- Reuse `FfmpegRuntime`; do not duplicate executable-name or PATH/bundled-runtime rules.
- `PerformanceStage` owns z-order. Video depth is `0.95f`, between static background `1.0f` and lane fallback `0.9f` / lane strips `0.8f`.
- CPU decode may run in background. `Texture2D` creation/update/draw stays on the game thread.
- Producer queue is bounded and blocks/waits when full. Do not grow it or drop not-yet-due future frames.
- Static background remains under video and is the fallback on missing/corrupt/unsupported/stale media.
- Stop the FFmpeg child on the same early transition path that already stops gameplay audio; dispose it later in `CleanupComponents()`.
- Do not commit the 54 MB Group C corpus.
- Keep the implementation within ~3 engineer days. If the actual Group C probe invalidates the bounded AVI -> RGBA design, stop and re-plan before broadening scope.

---

## Task 1: Add the whole-file DTX video event contract to existing chart timing

**Files:**

```text
Create:
  DTXMania.Game/Lib/Song/Components/ChartVideoEvent.cs

Modify:
  DTXMania.Game/Lib/Song/Components/ParsedChart.cs
  DTXMania.Game/Lib/Song/DTXChartParser.cs
  DTXMania.Test/Song/DTXChartParserTests.cs and/or DTXChartParserAdditionalTests.cs
  DTXMania.Test/Song/ParsedChartTests.cs
```

**Produces:** sorted/finalized video events whose ids/paths and timing match the legacy first-slice contract without copying the current header-only parser latch.

### Step 1: RED — pin parser behavior including the late-definition case

Add focused tests before production changes:

```text
#AVI01:bg.avi + #00054:01 -> one event
#VIDEO01:bg.avi           -> same alias behavior
#0005A:01                 -> same supported event behavior
00                         -> ignored
multiple pairs             -> correct ticks
lowercase ids              -> normalized uppercase
missing definition         -> event retained, empty path, parse succeeds
```

The load-bearing regression is explicitly:

```text
#00054:01
#AVI01:bg.avi
```

Current `ParseFileContentWithTimingAsync()` stops `ParseHeaderCommand()` once timeline data begins. The test must fail on current `main` and prove HPA-11 does **not** copy that latch for AVI definitions.

Also pin repeated late definitions: the last `#AVI01` / `#VIDEO01` assignment wins.

### Step 2: GREEN — add `ChartVideoEvent` and whole-file definition collection

`ChartVideoEvent` contains only:

```text
Bar
Tick
TimeMs
VideoId
VideoFilePath
```

Add `ParsedChart.VideoEvents` and one small add helper.

Inside each parser encoding attempt, create one local video-definition dictionary.

In `ParseFileContentWithTimingAsync()`, attempt to collect a video definition for **every** non-comment line before the `inDataSection` split. Do not route this through `ParseHeaderCommand()`.

Definition rules:

- `#AVIxx` and `#VIDEOxx` are case-insensitive aliases;
- `xx` is exactly two characters;
- normalize id uppercase;
- use the same quote trimming behavior as other header values;
- assignment overwrites an earlier value for the same id;
- malformed definitions are diagnostic/non-fatal.

Timeline channel `54` and `5A` parsing uses existing two-character pair subdivision. Normalize each non-`00` event id uppercase before storing it.

Do not add Movie/MovieFull enums or AVIPAN parsing.

### Step 3: Resolve after the full scan through existing path semantics

After the complete file has been read, resolve every retained video event against the **final** definition map.

Call existing private `ResolveBGMPath(definition, chart.FilePath)` for the resolved path. Do not create `ResolveVideoPath` or copy its chart-relative / path-separator fallback logic.

Do not require `File.Exists` for parse success. Missing definitions leave `VideoFilePath` empty.

### Step 4: Finalize through `ChartTimingMap`

Extend `ParsedChart.FinalizeChart()` only where existing event types are already handled:

- include video bars in `highestOccupiedBar`;
- resolve `TimeMs` through `TimingMap.CalculateTimeMs`;
- sort `VideoEvents` by `TimeMs`;
- include video trigger time in the existing max-event horizon for `DurationMs`.

Do not inspect media duration.

### Step 5: RED/GREEN — pin timing reuse

Add `ParsedChartTests` proving video follows channel `02`, `03`, and `08` timing through the same compiled map. Include:

- tempo/measure change before a video event;
- two events authored out of time order but sorted after finalization;
- a video trigger beyond the last note/BGM extending the event horizon.

### Step 6: Verify Task 1

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~DTXChartParser|FullyQualifiedName~ParsedChart"
```

Windows CI equivalent:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~DTXChartParser|FullyQualifiedName~ParsedChart"
```

Expected: focused tests pass.

---

## Task 2: Pin the real media profile, then add one bounded FFmpeg player

**Files:**

```text
Create:
  DTXMania.Game/Lib/Stage/Performance/IChartVideoPlayer.cs
  DTXMania.Game/Lib/Stage/Performance/FfmpegChartVideoPlayer.cs
  DTXMania.Game/Lib/Stage/Performance/VideoFrameSelector.cs
  DTXMania.Test/Stage/Performance/FfmpegChartVideoPlayerTests.cs
  DTXMania.Test/Stage/Performance/VideoFrameSelectorTests.cs
  DTXMania.Test/TestData/Video/tiny-uncompressed.avi

Modify:
  DTXMania.Game/Lib/Resources/FfmpegRuntime.cs
  tools/ffmpeg/macos-arm64/build-runtime.sh
  tools/ffmpeg/macos-arm64/README.md
  DTXMania.Test/DTXMania.Test.csproj
  DTXMania.Test/DTXMania.Test.Mac.csproj
  DTXMania.Test/Resources/FfmpegRuntimeTests.cs
  DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs
```

**Produces:** a game-owned AVI -> RGBA player using the one existing runtime, with a bounded producer and GPU-free timing-policy tests.

### Step 1: Probe Group C **before choosing Mac flags or generating the fixture**

The corpus manifest identifies `DTXFiles.2/Test/bg.avi` but does not identify its codec or pixel format.

When the corpus exists locally, run first:

```bash
ffprobe -v error \
  -select_streams v:0 \
  -show_entries stream=codec_name,pix_fmt,width,height,avg_frame_rate,r_frame_rate \
  -of json \
  DTXFiles.2/Test/bg.avi
```

Record the probe output in the PR discussion/verification note.

Use the actual `codec_name` and `pix_fmt` to choose the Mac decoder/conversion surface and to generate the tiny fixture. Do **not** assume `rawvideo` merely because the container is described as uncompressed AVI.

If this corpus is unavailable to the implementation environment, explicitly record that the decoder/pixel format remains unknown. Do not mark HPA-11 ready until an actual Group C probe is supplied before final acceptance.

### Step 2: RED — add matching tiny fixture and copy it explicitly

Generate a very small AVI with several visually distinct frames using the representative first-profile codec/pixel-format family.

Add explicit `TestData/Video/**/*` `CopyToOutputDirectory=PreserveNewest` items to **both**:

```text
DTXMania.Test/DTXMania.Test.csproj
DTXMania.Test/DTXMania.Test.Mac.csproj
```

The current projects only copy `TestData/NxScores/**` and `TestData/Audio/**`; this change is required, not optional.

Add a failing runtime test that:

- locates the copied fixture from test output;
- probes width/height/frame rate;
- decodes at least two frames to `RGBA` rawvideo.

All tests that invoke shared FFmpeg runtime state must join `[Collection("FfmpegRuntimeState")]`.

### Step 3: Centralize executable path lookup; do not duplicate `.exe` logic

`FfmpegChartVideoPlayer` will launch `ffmpeg` and `ffprobe` directly.

Add one small **internal** helper in `FfmpegRuntime` that resolves a command executable from the already-configured availability:

```text
BinaryFolder != null -> <BinaryFolder>/<platform binary name>
BinaryFolder == null -> platform binary name for PATH lookup
```

Reuse the existing private binary-name logic internally. Do not implement `OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"` again in the player.

Add focused `FfmpegRuntimeTests` for bundled-folder and PATH shapes.

Do not change runtime resolution precedence.

### Step 4: GREEN — extend Apple Silicon FFmpeg by the actual minimum

Keep FFmpeg 7.0.2, source hash, cache, system-dylib checks, runtime layout, and all HPA-512 audio capabilities.

Enable only what the probed AVI -> RGBA command needs. The resulting capability contract must cover:

```text
AVI demuxer
actual Group C decoder
rawvideo output encoder/muxer
format + scale pixel conversion path / swscale support as required
RGBA output pixel format
pipe protocol (already required)
```

Do not hard-code an unverified decoder name in the plan implementation. Use Step 1 probe facts.

Update `validate_runtime()` so a warm cache is rejected when the new capability surface is absent. In addition to listing capabilities, validate that `rgba` is supported and that the configured binary can actually decode the tiny AVI through the intended pipeline, conceptually:

```bash
ffmpeg -v error -i tiny-uncompressed.avi \
  -map 0:v:0 -an \
  -vf format=rgba \
  -frames:v 2 \
  -pix_fmt rgba -f rawvideo pipe:1
```

Use `scale`/swscale where required by the actual conversion path. The acceptance criterion is successful RGBA frame output, not merely a grep of `-decoders`.

Preserve all existing audio validation loops and tests.

Do not enable broad video codec/container families.

### Step 5: Define the narrow player API with stage-owned depth

`IChartVideoPlayer` remains stage-local and conceptually exposes:

```text
Start(path, initialMediaTimeMs)
Update(mediaTimeMs)
Draw(spriteBatch, destinationBounds, layerDepth)
Stop()
Dispose()
```

The stage owns `destinationBounds` and `layerDepth`; the decoder must not hard-code `0.95f`.

No generic seek/playback-rate/audio/device API.

### Step 6: Implement FFmpeg stdout -> bounded blocking CPU queue

`FfmpegChartVideoPlayer` should:

1. call `FfmpegRuntime.EnsureConfigured()` and the shared executable-path helper;
2. probe source width/height/frame rate;
3. launch one owned FFmpeg process that emits `RGBA` raw frames to stdout and ignores movie audio;
4. read complete frames off the game thread;
5. enqueue them into a small fixed-capacity queue;
6. **wait/block when full** rather than growing or dropping future frames;
7. tag frames using the constant frame interval for this first profile;
8. cancel/close/terminate the old process generation on retrigger/stop/dispose.

Do not use FFmpeg `-re`, timer callbacks, or whole-file predecode.

If `Start` occurs after the event is already in the past, seek/start near `initialMediaTimeMs` so the first visible generation is not deliberately replayed from zero.

### Step 7: Keep hold/skip/stale selection pure

`VideoFrameSelector` is a tiny internal pure helper, not a scheduler/service.

Inputs are the authoritative target media time, source frame interval, current-frame timestamp (if any), and queued timestamps. Output tells the player whether to:

- hold the current frame;
- consume through the latest queued frame due for target time, skipping obsolete intermediates;
- expose no frame because the decoder is more than the allowed few-frame tolerance behind.

Tests cover slow progression, high-speed jumps, update hitches, and stale fallback **without** creating a `GraphicsDevice` or FFmpeg process.

Do not use the Mac-excluded `TestGraphicsDeviceService` for these timing policies.

### Step 8: Upload one reusable texture and replace it on dimension changes

On game-thread `Update(mediaTimeMs)`:

- use the pure selector to choose the due CPU frame;
- upload only the selected frame;
- reuse the existing `Texture2D` while source width/height remain unchanged;
- on retrigger/different resolution, dispose and recreate the texture before `SetData`;
- never apply a frame buffer with dimensions from the previous generation.

If selected state is stale/no-frame, `Draw` renders nothing and the static background remains visible.

Aspect-fit geometry should be pure/testable and preserve source ratio inside the stage-provided bounds.

### Step 9: Verify Task 2

macOS Apple Silicon:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~FfmpegRuntimeTests|FullyQualifiedName~FfmpegBundledRuntimeTests|FullyQualifiedName~FfmpegChartVideoPlayerTests|FullyQualifiedName~VideoFrameSelectorTests|FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~ManagedSound"
```

Windows:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~FfmpegRuntimeTests|FullyQualifiedName~FfmpegChartVideoPlayerTests|FullyQualifiedName~VideoFrameSelectorTests|FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~ManagedSound"
```

Expected: new AVI/RGBA tests pass and previous HPA-512 audio tests stay green.

---

## Task 3: Wire scheduling/rendering/teardown into `PerformanceStage`

**Files:**

```text
Modify:
  DTXMania.Game/Lib/Stage/PerformanceStage.cs
  DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
  DTXMania.Test/Stage/Performance/PerformanceRendererStateTests.cs
```

**Produces:** last-due-event scheduling, one logical clock, correct depth composition, and early FFmpeg process stop on existing transition paths.

### Step 1: RED — orchestration and depth tests with one fake player

Reuse the existing deterministic stage test style. Inject/set one fake `IChartVideoPlayer`; do not launch FFmpeg.

Pin:

1. no video events -> no start;
2. before first event -> no start;
3. crossing one event -> start once at `max(0, currentTimeMs - event.TimeMs)`;
4. one update crossing several events -> only the **last due** event starts;
5. subsequent update -> exact logical media time is forwarded with no extra Play Speed multiply;
6. unresolved/missing event leaves video inactive and later valid event can still start;
7. existing early-stop transition path calls player `Stop()`;
8. cleanup calls player `Dispose()`.

In `PerformanceRendererStateTests`, pin `DrawBackground()` itself:

- static background draw remains depth `1.0f`;
- fake video player receives `PerformanceUILayout.Background.Bounds`;
- fake video player receives layer depth `0.95f`.

This is a functional contract, not a call-order assertion: the sprite batch uses `BackToFront` depth sorting.

### Step 2: Add only the stage state needed

Add:

```text
IChartVideoPlayer _chartVideoPlayer
int _nextVideoEventIndex
ChartVideoEvent? _activeVideoEvent
```

Reset cursor/active state on each activation.

Do not add `VideoScheduler`, a media manager, or config state.

### Step 3: Reuse the existing `currentTimeMs` once

Inside the current block:

```csharp
if (_songTimer != null && _songTimer.IsPlaying)
{
    var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
    ...
}
```

pass the same `currentTimeMs` into a small stage helper such as `ProcessVideoEvents(currentTimeMs)`.

Do not call `GetCurrentMs` again and do not read `PlaybackModifiers.Speed`.

Scheduling behavior:

1. consume all not-yet-handled video events whose `TimeMs <= currentTimeMs`;
2. retain only the last due event from that update;
3. stop prior generation;
4. start the last due valid media near `currentTimeMs - event.TimeMs`;
5. if missing/start failure, leave static fallback and continue;
6. for active media, call:

```text
player.Update(max(0, currentTimeMs - activeEvent.TimeMs))
```

Pause requires no extra video clock: when `SongTimer.IsPlaying` is false the stage no longer advances the target, so the current texture stays held. Resume continues from `PlaybackClock`'s frozen logical anchor.

### Step 4: Stop on existing early-stop paths, then dispose in cleanup

`ReturnToSongSelect()` and `FinalizePerformance()` already call `StopGameplayAudioInstances()` before their transition.

Extend that same helper path with `_chartVideoPlayer?.Stop()` so the FFmpeg child stops immediately rather than waiting for deferred `OnDeactivate()` / `CleanupComponents()`.

`CleanupComponents()` then disposes the player and clears the reference.

Retrigger also stops the previous generation before starting a new one.

Do not add a generic gameplay-media lifecycle abstraction.

### Step 5: Composite from `DrawBackground()` at explicit depth

Keep existing static/fallback background draw at `1.0f`.

Immediately after it, when a timely player frame exists, call the player with:

```text
PerformanceUILayout.Background.Bounds
0.95f
```

The existing BackToFront batch then orders:

```text
1.0  static background
0.95 video
0.9  lane fallback
0.8  lane strips
...
0.05 sprite notes
```

Do not rely on source call order and do not change lane alpha/assets.

### Step 6: Verify focused stage tests

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~PerformanceRendererStateTests"
```

Expected: orchestration, early-stop, and depth contracts pass without starting FFmpeg.

### Step 7: Run full platform test/build gates

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
```

Windows:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --no-restore
```

Also:

```bash
git diff --check
```

Do not claim these gates pass until the commands have been run on the implementation head.

### Step 8: Representative Group C smoke on Windows and Apple Silicon macOS

Use external corpus:

```text
DTXFiles.2/Test/mas.dtx
DTXFiles.2/Test/bg.avi
```

Before gameplay smoke, confirm the recorded `ffprobe` codec/pixel-format facts match the implemented Mac capability contract.

Verify:

- AVI is visible behind lanes/HUD, not on top of notes;
- start follows the chart-authored event;
- pause freezes the frame;
- resume continues aligned;
- 100% and 150% Play Speed follow logical chart time;
- decoder lag falls back to static background instead of free-running stale video;
- leaving performance stops the child process before the transition completes;
- no-video charts remain unchanged.

Record concise Windows + Mac results in the PR. Do not commit the corpus and do not add a new permanent E2E harness just for HPA-11.

---

## Final review checklist

Before marking PR #158 ready:

- [ ] No AVIPAN/MovieMode/config/static-BGA/embedded-audio/generic-media scope slipped in.
- [ ] Video definitions are collected across the whole file and the `event -> late definition` test is green.
- [ ] Video ids are exactly two characters and normalized uppercase.
- [ ] Video paths reuse `ResolveBGMPath`; no duplicate relative-path policy exists.
- [ ] Video timing is finalized only through `ChartTimingMap`.
- [ ] Stage uses the existing `currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime)` and never multiplies Play Speed again.
- [ ] Group C `bg.avi` codec/pixel format has been probed and recorded; Mac flags are based on that fact, not a guess.
- [ ] Mac runtime validates the complete AVI -> RGBA path, including pixel-format conversion, while all prior audio checks remain.
- [ ] FFmpeg executable lookup is centralized in `FfmpegRuntime`.
- [ ] FFmpeg-state tests use `[Collection("FfmpegRuntimeState")]`.
- [ ] `TestData/Video/**` is copied by both test projects.
- [ ] Decode producer blocks when its bounded queue is full; only the consumer skips obsolete frames.
- [ ] Hold/skip/stale policy is GPU-free pure logic.
- [ ] Different-resolution retrigger replaces the texture.
- [ ] `DrawBackground()` pins video to `PerformanceUILayout.Background.Bounds` at `0.95f`.
- [ ] Early transition paths stop the FFmpeg process; cleanup disposes it.
- [ ] Full Mac + Windows tests/builds are freshly verified.
- [ ] Group C smoke is recorded on Windows and Apple Silicon macOS.

## Estimate

- Task 1 parser/timing: ~0.5 day
- Task 2 corpus probe + FFmpeg/player: ~1–1.5 days
- Task 3 stage wiring + platform verification: ~0.5–1 day

Target: **2–3 engineer days, one PR**.