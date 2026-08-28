# HPA-11 Chart Background Video Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans`. Keep planning and implementation on this same PR.

**Goal:** Play chart-authored DTX AVI video behind gameplay lanes/HUD, synchronized to the existing CX logical song clock across pause/resume and 50–150% Play Speed, with static-background fallback on all media failures.

**Architecture:** Add one `ChartVideoEvent` list finalized by `ChartTimingMap`; collect `#AVIxx` / `#VIDEOxx` across the whole chart file using the same pre-latch pattern as extended BPM definitions; use the existing `currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime)` as the only runtime clock; use FFMpegCore 5.4.0 for probe/output-pipe/cancellation/process ownership; feed a 3-frame cancellable CPU queue; select frames with a tiny pure helper; upload one texture on the game thread; composite from `DrawBackground()` through a GPU-free stage layout resolver at depth `0.95f`.

**Tech Stack:** .NET 8, C#, MonoGame 3.8.x, FFMpegCore 5.4.0 / existing FFmpeg runtime, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-27-hpa-11-chart-background-video-design.md`

## Global constraints

- One PR for HPA-11 planning + implementation.
- Support only `#AVIxx` / `#VIDEOxx` + channels `54` / `5A` in this slice.
- Both channels use the same CX background-video behavior; do **not** add a source-channel field only for hypothetical future differences.
- Explicitly ignore `#AVIPANxx`.
- No MovieMode, static BGA/BMP, video config/alpha, embedded movie audio, generic media framework, extra native backend, or seek.
- Reuse `ParsedChart.FinalizeChart()` / `ChartTimingMap`; no video timing calculator.
- Reuse the existing `currentTimeMs` from `_songTimer.GetCurrentMs(_currentGameTime)`; no `SongPositionMs` invention and no second Play Speed multiply.
- Call `FfmpegRuntime.EnsureConfigured()` but let **FFMpegCore** own binary lookup, probing, output pipes, cancellation, and process kill. No raw `Process`, no executable-path helper, no FfmpegRuntime change unless a real bug appears.
- `Start(path)` must return without waiting for probe/process startup on the game thread.
- Decode queue capacity is exactly **3 frames** and queue wait is generation-cancellable.
- No `-ss`: frame timestamp origin is media zero (`frameIndex * frameIntervalMs`).
- `PerformanceStage` owns z-order. Video depth is `0.95f`, between static background `1.0f` and lanes.
- CPU decode runs in background. `Texture2D` creation/update/draw stays on the game thread.
- Static background remains under video and is the fallback on missing/corrupt/unsupported/stale/catching-up media.
- Stop/cancel video on the same early transition path that already stops gameplay audio; dispose later in `CleanupComponents()`.
- Do not commit the 54 MB Group C corpus and do not block Task 2 on it being locally available.
- Keep implementation within ~3 engineer days. If Group C acceptance invalidates the bounded AVI -> RGBA design, stop and re-plan before broadening scope.

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

**Produces:** finalized video events with legacy-compatible definitions/ids/path semantics and existing CX timing ownership.

### Step 1: RED — pin parser behavior

Add focused tests first:

```text
#AVI01:bg.avi + #00054:01 -> one event
#VIDEO01:bg.avi           -> alias behavior
#0005A:01                 -> same supported event behavior
00                         -> ignored
multiple pairs             -> correct ticks
lowercase ids              -> uppercase
missing definition         -> event retained, empty path, parse succeeds
#AVIPAN01:...              -> explicitly ignored
```

Load-bearing late-definition case:

```text
#00054:01
#AVI01:bg.avi
```

Also pin repeated late definitions: final `#AVI01` / `#VIDEO01` assignment wins.

Expected on current `main`: RED because video events do not exist.

### Step 2: GREEN — model + whole-file definition collector

`ChartVideoEvent` contains only:

```text
Bar
Tick
TimeMs
VideoId
VideoFilePath
```

Do not add `Channel`; 54 and 5A deliberately normalize to the same behavior in HPA-11.

Add `ParsedChart.VideoEvents` and one add helper.

In each parser encoding attempt, keep one local video-definition dictionary.

Follow the existing `TryHandleExtendedBpmDefinition(...)` shape: for every non-comment line, before the `inDataSection` split, call a small `TryHandleVideoDefinition(...)` that:

- accepts only exact `#AVIxx` / `#VIDEOxx` command shapes;
- normalizes the two-character id uppercase;
- trims quoted values consistently with current headers;
- overwrites earlier definition for the same id;
- does not match `#AVIPANxx`;
- stays diagnostic/non-fatal on malformed input.

### Step 3: Reuse existing event-cell/tick semantics

Channels `54` and `5A` retain non-`00` two-character ids with `CalculatePairTick(...)`.

Do not create a third large copy of the BGM/note pair loop. If needed, extract one tiny non-zero event-pair iterator and reuse it for BGM + video. Do not broadly refactor note parsing.

### Step 4: Resolve after full scan through existing path semantics

Resolve every retained video event against the final definition map.

Call existing `ResolveBGMPath(definition, chart.FilePath)`; do not add `ResolveVideoPath` or duplicate relative-path/separator behavior.

Do not require `File.Exists` for parse success. Missing definitions leave `VideoFilePath` empty.

### Step 5: Finalize through `ChartTimingMap`

Extend existing finalization loops only:

- include video bars in `highestOccupiedBar`;
- resolve `TimeMs` via `TimingMap.CalculateTimeMs`;
- sort `VideoEvents` by `TimeMs`;
- include trigger time in the existing max-event horizon for `DurationMs`.

Do not inspect media duration.

### Step 6: RED/GREEN — timing reuse

Add `ParsedChartTests` for:

- channel `02`, `03`, `08` timing changes before video;
- sorting after finalization;
- video trigger beyond last note/BGM extending event horizon.

### Step 7: Verify Task 1

```bash
# macOS
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~DTXChartParser|FullyQualifiedName~ParsedChart"

# Windows
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~DTXChartParser|FullyQualifiedName~ParsedChart"
```

Expected: PASS.

---

## Task 2: Add the declared AVI/rawvideo profile and one cancellable FFMpegCore player

**Files:**

```text
Create:
  DTXMania.Game/Lib/Stage/Performance/IChartVideoPlayer.cs
  DTXMania.Game/Lib/Stage/Performance/FfmpegChartVideoPlayer.cs
  DTXMania.Game/Lib/Stage/Performance/VideoFrameSelector.cs
  DTXMania.Test/Stage/Performance/FfmpegChartVideoPlayerTests.cs
  DTXMania.Test/Stage/Performance/VideoFrameSelectorTests.cs
  DTXMania.Test/TestData/Video/tiny-raw-bgr24.avi

Modify:
  tools/ffmpeg/macos-arm64/build-runtime.sh
  tools/ffmpeg/macos-arm64/README.md
  DTXMania.Test/DTXMania.Test.csproj
  DTXMania.Test/DTXMania.Test.Mac.csproj
```

**Reuse unchanged unless a concrete defect is found:**

```text
DTXMania.Game/Lib/Resources/FfmpegRuntime.cs
DTXMania.Test/Resources/FfmpegRuntimeTests.cs
DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs
```

**Produces:** a non-blocking game-owned AVI -> RGBA player using FFMpegCore’s existing process/piping/cancellation behavior.

### Step 1: Declare and commit the automated first profile

The task does **not** wait for Group C.

Fixture/profile:

```text
container:     AVI
codec:         rawvideo
source pixfmt: bgr24
output pixfmt: rgba
frame rate:    constant
fixture:       TestData/Video/tiny-raw-bgr24.avi
```

Generate a tiny file with several visually distinct frames.

Add explicit `TestData/Video/**/*` `CopyToOutputDirectory=PreserveNewest` items to both test csprojs.

All FFmpeg-runtime tests join `[Collection("FfmpegRuntimeState")]`.

### Step 2: RED — pin FFMpegCore probe/pipe behavior

Add tests proving:

- `FfmpegRuntime.EnsureConfigured()` reports usable runtime;
- `FFProbe.AnalyseAsync(..., cancellationToken)` returns usable width/height/frame rate for the fixture;
- `FFMpegArguments.FromFileInput(...).OutputToPipe(sink)...CancellableThrough(token).ProcessAsynchronously()` yields at least two RGBA frames;
- missing/corrupt input is contained.

The sink is a small HPA-11 `IPipeSink` implementation that reads complete frame buffers and writes decoded-frame objects into the bounded queue.

Do **not** add an executable-path helper or raw `System.Diagnostics.Process` wrapper.

### Step 3: GREEN — minimally extend Apple Silicon runtime

Preserve FFmpeg 7.0.2, source hash, cache, system-dylib checks, runtime layout, and every HPA-512 audio capability.

Enable only the declared AVI/rawvideo -> RGBA path, expected to include:

```text
avi demuxer
rawvideo decoder
rawvideo encoder/muxer
format/swscale pixel conversion as required
pipe protocol (already present)
```

Update `validate_runtime()` so warm cache revalidation rejects a runtime that lacks the new surface.

Also validate a real fixture decode to RGBA rawvideo, conceptually:

```bash
ffmpeg -v error -i tiny-raw-bgr24.avi \
  -map 0:v:0 -an \
  -vf format=rgba \
  -frames:v 2 \
  -pix_fmt rgba -f rawvideo pipe:1
```

The acceptance criterion is successful RGBA bytes, not only capability-list greps.

Do not enable broad codec/container families.

### Step 4: Define a non-blocking narrow player API

Conceptual interface:

```text
Start(path)
Update(mediaTimeMs)
Draw(spriteBatch, destinationBounds, layerDepth)
Stop()
Dispose()
```

Contracts:

- `Start(path)` schedules a generation and returns before probe/process startup completes;
- Draw returns nothing until a timely frame exists;
- no seek/playback-rate/audio/device APIs;
- stage owns bounds/depth.

Test non-blocking startup with a controlled async barrier/seam around probe/start work: `Start()` must return while the barrier is still pending.

### Step 5: Implement FFMpegCore output pipe -> 3-frame cancellable queue

Generation flow:

```text
EnsureConfigured
  -> async FFProbe.AnalyseAsync
  -> FFMpegArguments.FromFileInput
  -> OutputToPipe(VideoFramePipeSink)
  -> CancellableThrough(generationToken)
  -> ProcessAsynchronously
```

`VideoFramePipeSink.ReadAsync(stream, token)` reads complete RGBA frame buffers.

Use capacity **3**:

```text
max raw queue bytes = 3 * width * height * 4
```

Reference budgets:

```text
640x480    ~3.5 MiB
1280x720  ~10.5 MiB
1920x1080 ~23.7 MiB
```

When queue is full, producer waits using the same generation token. Do not grow/drop future frames.

`Stop` and retrigger cancel that token. Cancellation must wake a producer blocked on a full queue and let FFMpegCore end/kill the process generation.

Add a deterministic test that fills the queue, stops the generation, and proves producer/process task completion within the test timeout—no deadlock.

### Step 6: No seek; define timestamp origin exactly

Do **not** use `-ss`.

Every generation starts at media zero:

```text
frameTimeMs = frameIndex * frameIntervalMs
```

Async startup/hitches are handled by decode catch-up + consumer skipping. Until the decoder reaches a sufficiently current frame, Draw returns nothing and the static background remains visible.

Pin the zero-origin rule in selector/player tests.

### Step 7: Pure hold/skip/stale selector

`VideoFrameSelector` has no GPU/FFmpeg/filesystem dependency.

Inputs:

```text
target media time
frame interval
current frame timestamp (optional)
queued frame timestamps
```

Output says:

- hold current;
- consume through latest due queued frame, skipping obsolete intermediates;
- no-frame because decoder is beyond the small frame-interval stale tolerance.

Tests cover slow progression, 150%-style jumps, update hitch, async-start catch-up, and stale fallback.

### Step 8: Texture + aspect-fit lifecycle

On game-thread `Update(mediaTimeMs)`:

- select due CPU frame;
- upload only selected frame;
- reuse texture while dimensions match;
- recreate texture when a new generation has different dimensions;
- never upload prior-generation dimensions.

If selector returns stale/no-frame, Draw renders nothing.

Keep aspect-fit geometry pure/testable.

### Step 9: Verify Task 2

```bash
# macOS Apple Silicon
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~FfmpegChartVideoPlayerTests|FullyQualifiedName~VideoFrameSelectorTests|FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~ManagedSound"

# Windows
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~FfmpegChartVideoPlayerTests|FullyQualifiedName~VideoFrameSelectorTests|FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~ManagedSound"
```

Also run the Mac builder/normal Game.Mac build so its `validate_runtime()` path executes.

Expected: new video tests pass and existing audio behavior stays green.

---

## Task 3: Wire scheduling/rendering/teardown into `PerformanceStage` and existing E2E

**Files:**

```text
Modify:
  DTXMania.Game/Lib/Stage/PerformanceStage.cs
  DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
  DTXMania.Test/Stage/Performance/PerformanceRendererStateTests.cs

Create:
  DTXMania.E2E/ChartBackgroundVideoSmokeTests.cs

Modify only if needed to stage fixture media:
  DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs
```

**Produces:** last-due scheduling, one logical clock, cross-platform-testable depth ownership, early cancellation, and repeatable launch/screenshot/exit evidence using existing automation.

### Step 1: RED — stage orchestration with one fake player

Reuse existing deterministic stage seams; do not launch FFmpeg.

Pin:

1. no events -> no start;
2. before first event -> no start;
3. crossing event -> one `Start(path)` plus `Update(max(0, currentTimeMs - event.TimeMs))`;
4. crossing several events -> only last due starts;
5. later updates forward exact logical media time with no second Play Speed multiply;
6. unresolved event leaves fallback and later valid event can still start;
7. early-stop path calls `Stop()`;
8. cleanup calls `Dispose()`.

### Step 2: Add only stage state needed

```text
IChartVideoPlayer _chartVideoPlayer
int _nextVideoEventIndex
ChartVideoEvent? _activeVideoEvent
```

Reset per activation.

No `VideoScheduler`, media manager, or config state.

### Step 3: Reuse `currentTimeMs` once

Inside existing block:

```csharp
if (_songTimer != null && _songTimer.IsPlaying)
{
    var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
    ...
}
```

pass the same value to `ProcessVideoEvents(currentTimeMs)`.

Rules:

1. consume all due unhandled video events;
2. keep only last due event from this update;
3. cancel previous generation;
4. `Start(path)` last due valid event;
5. failure/missing path leaves fallback;
6. active media gets `Update(max(0, currentTimeMs - activeEvent.TimeMs))`.

Do not call `GetCurrentMs` again and do not read `PlaybackModifiers.Speed`.

### Step 4: Stop early, dispose later

Extend the existing `StopGameplayAudioInstances()` early path with `_chartVideoPlayer?.Stop()` so Return-to-Song-Select and performance finalization cancel video before transition/fade.

`CleanupComponents()` disposes and clears the player.

Retrigger stops/cancels previous generation first.

### Step 5: Make draw layout pure and cross-platform-testable

Do **not** invoke private `DrawBackground()` in Mac tests.

Add a tiny GPU-free stage resolver, conceptually:

```text
ResolveChartVideoDrawLayout()
  -> bounds = PerformanceUILayout.Background.Bounds
  -> depth  = 0.95f
```

`DrawBackground()` uses this resolver when calling player Draw.

Cross-platform test pins:

```text
bounds == PerformanceUILayout.Background.Bounds
depth == 0.95f
```

Any actual SpriteBatch interaction test can remain Windows-only. Do not add `TestGraphicsDeviceService` to the Mac project.

### Step 6: Reuse existing E2E for launch/capture/exit evidence

Add one bounded HPA-11 E2E using existing `GameProcessDriver`, `JsonRpcGameClient`, prepared-chart activation, `Eventually`, and screenshot capture.

Use a small DTX fixture referencing the committed tiny AVI.

Automate:

- reach SongSelect fixture;
- prepare/activate chart;
- reach Performance;
- capture a screenshot artifact while video should be active;
- leave/finish performance;
- assert the game reaches expected next stage without hang.

Do not add an image-diff package or cross-platform process-tree inspector solely for HPA-11. Deterministic child teardown stays covered by Task 2 full-queue cancellation tests; screenshot artifact gives repeatable visual evidence using existing infrastructure.

Pause/resume/150% visual alignment remain final acceptance observations.

### Step 7: Focused stage/E2E verification

```bash
# Mac-capable pure/stage tests
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~PerformanceRendererStateTests"

# Windows equivalent
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~PerformanceRendererStateTests"

# Existing E2E project, filtered to HPA-11 smoke where supported
dotnet test DTXMania.E2E/DTXMania.E2E.csproj --no-restore \
  --filter "FullyQualifiedName~ChartBackgroundVideoSmokeTests"
```

Expected: PASS where the project’s normal E2E runtime prerequisites are available.

### Step 8: Full platform gates

```bash
# macOS
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore

# Windows
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --no-restore

git diff --check
```

Do not claim these pass until run on implementation head.

### Step 9: Group C acceptance — not an implementation prerequisite

When external corpus is available:

```text
DTXFiles.2/Test/mas.dtx
DTXFiles.2/Test/bg.avi
```

Record first:

```text
codec_name
pix_fmt
width/height
avg_frame_rate or r_frame_rate
```

Then verify on Windows + Apple Silicon macOS:

- AVI visible behind lanes/HUD;
- event timing follows chart;
- pause freezes visible frame;
- resume continues aligned;
- 100% and 150% follow logical chart time;
- decoder lag uses static fallback instead of free-running stale video;
- no-video chart unchanged.

If Group C needs one additional bounded codec/pixel-format capability, add exactly that and pin it. If it invalidates the bounded design, stop and re-plan.

Do not commit the corpus.

---

## Final review checklist

Before marking PR #158 ready:

- [ ] No AVIPAN/MovieMode/config/static-BGA/embedded-audio/generic-media/seek scope slipped in.
- [ ] `#AVIPAN01` rejection test is green.
- [ ] Video definitions use the whole-file pre-latch collector pattern; late definition test is green.
- [ ] Pair/tick parsing reuses existing semantics without a third large duplicate loop.
- [ ] `ChartVideoEvent` remains the minimal five-field model; no speculative source-channel field.
- [ ] Paths reuse `ResolveBGMPath`.
- [ ] Timing uses only `ChartTimingMap` + existing `currentTimeMs`.
- [ ] No raw `Process`, executable-path helper, or unnecessary `FfmpegRuntime` change exists.
- [ ] Player uses FFMpegCore `FFProbe.AnalyseAsync` + `OutputToPipe` + `CancellableThrough` + `ProcessAsynchronously`.
- [ ] `Start(path)` is non-blocking with respect to probe/process startup.
- [ ] Queue capacity is 3 and full-queue wait is cancellation-aware.
- [ ] Full-queue Stop/retrigger test proves no deadlock/process leak.
- [ ] No seek; frame timestamps originate at zero.
- [ ] Hold/skip/stale policy is GPU-free.
- [ ] Different-resolution retrigger replaces texture.
- [ ] Both test projects copy `TestData/Video/**`.
- [ ] Mac builder validates actual declared AVI/rawvideo -> RGBA fixture decode and preserves prior audio capabilities.
- [ ] Cross-platform pure draw-layout test pins `PerformanceUILayout.Background.Bounds` + `0.95f`.
- [ ] Early transition paths cancel video; cleanup disposes it.
- [ ] Existing E2E infrastructure captures HPA-11 performance evidence without a new harness.
- [ ] Full Mac + Windows tests/builds are freshly verified.
- [ ] Group C probe/smoke is recorded before final acceptance when corpus is available.

## Estimate

- Task 1 parser/timing: ~0.5 day
- Task 2 FFMpegCore player/runtime: ~1–1.5 days
- Task 3 stage wiring/E2E/acceptance: ~0.5–1 day

Target: **2–3 engineer days, one PR**.
