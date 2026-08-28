# HPA-11 Chart Background Video Design

## Summary

HPA-11 adds the first focused CX vertical slice for chart-authored background video during `PerformanceStage`.

The architecture stays deliberately small:

1. `ParsedChart.FinalizeChart()` / `ChartTimingMap` remains the only authored `(bar, tick) -> TimeMs` compiler.
2. `PerformanceStage` uses the existing logical song time it already reads from `_songTimer.GetCurrentMs(_currentGameTime)`; video never owns a second clock and Play Speed is never multiplied a second time.
3. One FFmpeg-backed player owns one child decode process, a small bounded CPU-frame queue, and one current MonoGame texture.
4. The existing static performance background always remains underneath video and is the fallback for every media failure.

The first slice supports legacy `#AVIxx` / `#VIDEOxx` definitions plus DTX channels `54` and `5A`, normalized to one CX background-video behavior. AVIPAN, MovieMode, static BGA/BMP, video configuration, embedded movie audio, and broad codec/container support remain out of scope.

## Verified reuse survey

### Chart timing already has one owner

`DTXMania.Game/Lib/Song/Components/ParsedChart.cs` already retains authored positions for notes/BGM and finalizes them through `ChartTimingMap`.

`ChartVideoEvent` should be a sibling of `BGMEvent` and join those same finalization loops. Do not add a video-specific time calculator.

### The real gameplay clock is `GetCurrentMs(...)`

`PlaybackClock` applies the frozen Play Speed factor and freezes its logical anchor while paused. `SongTimer` wraps it and exposes the logical chart position through:

```csharp
_songTimer.GetCurrentMs(_currentGameTime)
```

`PerformanceStage.UpdateGameplay()` already reads this once inside the `_songTimer.IsPlaying` branch and passes the resulting `currentTimeMs` to BGM, gameplay managers, progress, and completion.

HPA-11 should reuse that same `currentTimeMs` value. There is no `SongTimer.SongPositionMs` property on current `main`, and the design must not invent one.

### Background z-order is depth-based, not call-order based

`PerformanceStage.OnDraw()` begins the base pass with `SpriteSortMode.BackToFront` and documents the existing depth contract:

```text
static background 1.0
lane fallback     0.9
lane strips       0.8
measure lines     0.78
...
sprite notes      0.05
```

Therefore a video draw using the default sprite depth (`0.0`) would render in front of gameplay even if called immediately after the background.

`PerformanceStage` owns this composition contract. HPA-11 uses one named stage-owned video depth of approximately `0.95f`, between the static background and lane backgrounds, and uses `PerformanceUILayout.Background.Bounds` as the gameplay background bounds.

The decoder/player must not hard-code its own z-order. The stage passes the layer depth into the player draw call (or otherwise performs the final texture draw itself).

### The parser has a header latch that video must not copy blindly

`DTXChartParser.ParseFileContentWithTimingAsync()` currently sets `inDataSection` after the first timeline/data marker and stops calling `ParseHeaderCommand()` afterward. That is acceptable for current WAV/header behavior but is not compatible with the required legacy video contract.

NX processes `#AVIxx` / `#VIDEOxx` definitions throughout the file, and later definitions replace earlier ones even when authored after timeline rows.

HPA-11 therefore collects video definitions independently across the whole file, not only while `!inDataSection`. Timeline events and definitions are resolved after the full scan.

Definition ids and timeline cell ids are exactly two characters and normalized to uppercase, matching the existing WAV/BGM id convention.

Video path resolution must call the existing `DTXChartParser.ResolveBGMPath(...)` helper rather than forking a second chart-relative path policy. The helper name is historical; duplicating its semantics is worse than reusing it.

### FFmpeg resolution is already centralized

`FfmpegRuntime.EnsureConfigured()` owns packaged-runtime/PATH resolution for both platforms.

Windows uses the full `MMTools.Executables.Windows.X64` package. Apple Silicon uses the HPA-512 source-built runtime with `--disable-everything` plus an explicit audio-only capability list.

HPA-11 must extend that existing Mac capability surface narrowly. It must not add Media Foundation, AVFoundation, MonoGame `VideoPlayer`, a second NuGet binary provider, or another FFmpeg resolver.

The player needs the actual `ffmpeg` / `ffprobe` executable paths for child processes. Do not duplicate `ffmpeg` vs `ffmpeg.exe` naming or bundled-folder/PATH behavior in the player. Add one small internal executable-path helper to `FfmpegRuntime` (with focused tests) that derives a command path from the already-resolved runtime availability. `BinaryFolder == null` continues to mean PATH lookup.

Any test class that invokes or mutates shared FFmpeg runtime state joins the existing `[Collection("FfmpegRuntimeState")]` serialization contract.

### Performance already has an early-stop path

`ReturnToSongSelect()` and `FinalizePerformance()` both stop logical time and call `StopGameplayAudioInstances()` before the stage transition/fade. `CleanupComponents()` happens later.

An owned FFmpeg process must stop on that same early-stop path so it cannot burn CPU or leave a last video frame active during the song-select/result transition. Full player disposal still belongs in `CleanupComponents()`.

Do not add a generic media lifecycle manager for this.

## Legacy DTX/NX contract for this slice

The required compatibility surface is:

- `#AVIxx:path` and `#VIDEOxx:path` are aliases;
- `xx` is a two-character base-36-style resource id, normalized uppercase in CX;
- later definitions for the same id win, regardless of whether a timeline event appeared earlier;
- channel `54` triggers movie playback;
- channel `5A` triggers movie playback;
- `00` cells mean no event;
- both channels use the same CX rendering behavior in HPA-11.

NX also supports AVIPAN and a much larger DirectShow/SharpDX rendering matrix. None of that is required here.

## Supported first media profile

The repository corpus manifest identifies the representative acceptance file:

```text
DTXFiles.2/Test/mas.dtx
DTXFiles.2/Test/bg.avi
```

The manifest proves the file identity/size, not its codec or pixel format. The current planning branch must not guess those facts.

Before changing the Apple Silicon FFmpeg configure surface, implementation Task 2 must run `ffprobe` against the actual corpus `bg.avi` whenever that corpus is available and record at least:

```text
codec_name
pix_fmt
width
height
avg_frame_rate (or r_frame_rate when required)
```

The tiny committed AVI fixture must then match the representative codec/pixel-format family closely enough that Windows cannot pass on a materially easier path than the Mac Group C file.

If the corpus is unavailable to the implementation environment, do not claim the representative decoder/pixel format is known. Keep the PR draft until the actual Group C probe is recorded before final acceptance.

The first-slice output contract is always CPU `RGBA` frames suitable for MonoGame `Texture2D.SetData`.

Because the Mac builder uses `--disable-everything`, HPA-11 must enable and validate every component needed for the actual AVI -> RGBA raw-frame command, including:

- AVI demuxing;
- the actual representative video decoder (do not assume `rawvideo` until probed);
- rawvideo output encoder/muxer;
- pixel-format conversion support (`format` / `scale` and the swscale path as required by the FFmpeg build);
- `rgba` output pixel-format support;
- existing `pipe` protocol.

Validation must include an actual tiny-fixture decode command to `-pix_fmt rgba -f rawvideo pipe:1`, not only capability-list greps. Preserve every HPA-512 audio capability and validation gate.

Do not preemptively enable H.264, HEVC, VP9, MPEG-4, subtitles, network protocols, or unrelated formats.

## Design

### 1. `ChartVideoEvent` joins existing chart finalization

Create one small model next to `BGMEvent`:

```text
Bar
Tick
TimeMs
VideoId
VideoFilePath
```

Add `ParsedChart.VideoEvents` and one add helper.

`FinalizeChart()` must:

- include normalized video-event bars in `highestOccupiedBar`;
- resolve `TimeMs` through `TimingMap.CalculateTimeMs`;
- sort video events by `TimeMs`;
- include video trigger times in the existing chart event horizon used for `DurationMs`.

Do not include movie file duration in chart duration.

### 2. Whole-file video definition collection

Use one parser-attempt-local video-definition dictionary.

For every non-comment line, before the `inDataSection` header/measure split, attempt to collect exactly shaped video definitions:

```text
#AVI01:bg.avi
#VIDEO01:bg.avi
```

This collector is independent of `ParseHeaderCommand()` and therefore still runs after timeline data begins.

Rules:

- command prefix is case-insensitive;
- id is exactly two characters and stored uppercase;
- quoted-value handling should match existing header value behavior;
- later assignment replaces earlier assignment;
- malformed definition is ignored diagnostically and must not abort chart parsing.

Timeline channels `54` and `5A` use the existing two-character cell subdivision rule. Non-`00` cell ids are uppercased and retained as `ChartVideoEvent` authored positions.

After the complete scan, resolve every event against the final definition table using existing `ResolveBGMPath(...)`. Missing definitions leave `VideoFilePath` empty.

Do not add a public definition registry or a second path helper.

### 3. One narrow player contract with stage-owned draw depth

Add one gameplay-owned interface under `Lib/Stage/Performance` with only the stage needs, conceptually:

```text
Start(path, initialMediaTimeMs)
Update(mediaTimeMs)
Draw(spriteBatch, destinationBounds, layerDepth)
Stop()
Dispose()
```

The exact success/result shape may be adjusted during implementation, but these properties are mandatory:

- `Start` never performs whole-file decode on the game thread;
- FFmpeg/probe/process failures are contained and diagnostic;
- CPU decoding happens off the game thread;
- texture creation/update and draw happen on the game thread;
- the stage supplies destination bounds and layer depth;
- `Stop` cancels the current generation and terminates the owned child process;
- `Dispose` additionally releases the current texture/resources.

Do not add generic media, playback-rate, audio, device, or plugin APIs.

### 4. FFmpeg stdout -> bounded blocking queue

The player pipeline is:

```text
ffprobe -> source dimensions / frame rate / codec facts
ffmpeg  -> RGBA raw frames on stdout
worker  -> bounded CPU frame queue
Update  -> pure timestamp selection + game-thread texture upload
Draw    -> current texture at stage-provided bounds/depth
```

Use a small fixed-capacity queue. When the queue is full, the decode worker **waits**. It must not grow the queue and must not drop future frames: at slow Play Speed those frames have not become due yet.

The consumer is allowed to discard obsolete frames when logical target time jumps ahead (fast Play Speed or update hitch).

Do not use `-re` or any decoder wall clock as synchronization.

### 5. Keep frame selection pure

Do not bury hold/skip/stale policy in `Texture2D` or FFmpeg process code.

Add one tiny internal pure selector (a small helper/class, not a service) whose inputs are logical target media time, source frame interval, current-frame timestamp, and queued frame timestamps. Its result tells the player whether to:

- keep the current frame;
- consume/skip to the latest queued frame now due;
- expose no drawable frame because the decoder is materially stale.

This helper has no `GraphicsDevice`, `Texture2D`, FFmpeg, process, or filesystem dependency and is the primary home for slow/fast/hitch/stale unit tests.

The queue producer remains blocking; only the selector/consumer discards frames that are already obsolete for the authoritative logical time.

### 6. Texture lifecycle is generation/dimension aware

The player owns at most one current `Texture2D`.

- Reuse it across frames while width/height are unchanged.
- On retrigger to a video with different dimensions, dispose/replace the old texture before uploading the new frame.
- Do not call `SetData` with a buffer shape that belongs to the previous video generation.
- On playback failure/stale state, hiding the video does not require destroying the texture immediately; `Stop`/retrigger/dispose owns lifecycle cleanup.

### 7. Stage scheduling reuses the existing `currentTimeMs`

Inside the existing `_songTimer != null && _songTimer.IsPlaying` update branch, reuse the already-computed:

```csharp
var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
```

Pass that same value into video scheduling. Do not call the clock a second time and do not apply `PlaybackModifiers.Speed` again.

Scheduling rules:

1. consume every sorted video event with `TimeMs <= currentTimeMs` that has not been handled;
2. if several become due in one update, only the **last due** event starts—the intermediate events are already obsolete;
3. stop the previous generation before replacement;
4. unresolved/missing media leaves the video layer inactive but does not block later events;
5. for the active event:

```text
mediaTimeMs = max(0, currentTimeMs - activeEvent.TimeMs)
```

6. pass `mediaTimeMs` directly to the player.

Pause needs no second transport command: the existing gameplay update branch stops advancing logical video target time while `SongTimer.IsPlaying` is false, so the current texture remains held. Resume continues from the same `PlaybackClock` logical anchor.

No `VideoScheduler` type is needed.

### 8. Draw inside `DrawBackground()` at a pinned depth

`DrawBackground()` remains the composition owner:

```text
static/fallback background at 1.0
chart video             at 0.95
lane fallback           at 0.9
lane strips             at 0.8
```

Draw the static/fallback background first, then ask the player to draw a timely frame using:

```text
bounds = PerformanceUILayout.Background.Bounds
layerDepth = 0.95f
```

Use aspect-fit geometry so 4:3 video is not stretched to 16:9. Uncovered area shows the existing static background.

Do not depend on call order alone and do not put the depth constant inside the decoder.

### 9. Stop early; dispose later

Extend the existing early-stop helper path used by both `ReturnToSongSelect()` and `FinalizePerformance()` so `_chartVideoPlayer.Stop()` is called before the transition/fade, alongside current gameplay audio stopping.

`CleanupComponents()` then disposes and clears the player.

Retrigger also calls `Stop()` before starting the replacement generation.

Do not let an FFmpeg process survive until deferred deactivation cleanup.

### 10. Failure behavior

Background video is optional presentation, never gameplay state.

The following are non-fatal and fall back to the static background:

- unresolved definition/path;
- missing/corrupt/unsupported video;
- FFmpeg runtime unavailable;
- ffprobe failure;
- invalid dimensions/frame rate;
- child process exit/read failure;
- decoder behind logical time;
- texture upload failure;
- cancellation during retrigger/exit.

Log one useful diagnostic per failed playback generation; do not log every frame and do not show a modal/toast.

A later valid event in the same chart may still start.

## Test strategy

### Parser/finalization

Extend existing parser/chart tests to pin:

- `#AVIxx` and `#VIDEOxx` aliases;
- ids uppercased and exactly two characters;
- channel 54 and 5A events;
- `00` ignored;
- correct authored ticks for multiple cells;
- **event first, later `#AVIxx` definition afterward** resolves successfully despite the parser data-section latch;
- repeated late definition wins;
- video path uses the same `ResolveBGMPath` semantics as WAV/BGM;
- missing definition stays playable with empty path;
- channel 02/03/08 timing changes flow through `ChartTimingMap`;
- sorting and chart duration/event horizon include video triggers.

### Runtime/player

Before editing Mac flags, probe Group C `bg.avi` when present and record codec/pixel-format facts in the PR.

Create a tiny deterministic AVI fixture matching that first profile. `TestData/Video/**` must be explicitly copied in **both** `DTXMania.Test.csproj` and `DTXMania.Test.Mac.csproj`, mirroring the existing `TestData/Audio/**` item.

Runtime/player tests pin:

- shared FFmpeg executable-path helper for bundled-folder and PATH cases;
- Apple Silicon builder still contains every previous audio capability;
- actual required AVI decoder/demuxer plus `format`/`scale`/RGBA conversion surface;
- an actual tiny-fixture `ffmpeg ... -pix_fmt rgba -f rawvideo pipe:1` decode succeeds;
- probe returns usable dimensions/frame rate;
- pure selector holds, skips, and returns stale/no-frame correctly without GPU;
- bounded producer uses wait/block-on-full semantics;
- missing/corrupt input is contained;
- cancellation/retrigger terminates the prior process generation;
- dimension change requires texture replacement;
- aspect-fit geometry preserves source ratio.

Tests that touch shared runtime state join `[Collection("FfmpegRuntimeState")]`.

Do not require `TestGraphicsDeviceService` on Mac just to test frame-selection policy.

### `PerformanceStageDeterministicTests` / renderer-state tests

Use one fake `IChartVideoPlayer`; do not launch FFmpeg.

Pin:

- no events -> no player start;
- before event -> no start;
- crossing event -> one start with `max(0, currentTimeMs - event.TimeMs)`;
- hitch crossing multiple events -> only last due starts;
- subsequent updates pass the same logical `currentTimeMs` domain with no second speed multiply;
- missing/failed event does not block later valid event;
- existing early-stop path stops video before transition;
- cleanup disposes video;
- `DrawBackground()` passes `PerformanceUILayout.Background.Bounds` and the pinned `0.95f` depth to the fake player while static background remains at `1.0f`.

The draw-depth contract is required because `SpriteSortMode.BackToFront` makes default depth incorrect.

### Platform / Group C acceptance

Automated gates:

```text
macOS: DTXMania.Test.Mac + Game.Mac build
Windows: DTXMania.Test + Game.Windows build
```

Representative Group C smoke on Windows and Apple Silicon macOS must verify:

- actual `bg.avi` probe facts match the enabled capability contract;
- movie is behind lanes/HUD;
- event start timing follows finalized chart time;
- pause freezes the visible frame;
- resume continues aligned;
- 150% Play Speed follows logical chart time rather than 100% wall time;
- decoder lag falls back to static background instead of freewheeling stale video;
- no-video chart is unchanged;
- leaving performance stops the FFmpeg child before transition completion.

Do not commit the large Group C corpus or create a new permanent E2E harness solely for this feature.

## Expected implementation surface

Production:

```text
Create:
  DTXMania.Game/Lib/Song/Components/ChartVideoEvent.cs
  DTXMania.Game/Lib/Stage/Performance/IChartVideoPlayer.cs
  DTXMania.Game/Lib/Stage/Performance/FfmpegChartVideoPlayer.cs
  DTXMania.Game/Lib/Stage/Performance/VideoFrameSelector.cs   # tiny pure helper

Modify:
  DTXMania.Game/Lib/Song/Components/ParsedChart.cs
  DTXMania.Game/Lib/Song/DTXChartParser.cs
  DTXMania.Game/Lib/Resources/FfmpegRuntime.cs
  DTXMania.Game/Lib/Stage/PerformanceStage.cs
  tools/ffmpeg/macos-arm64/build-runtime.sh
  tools/ffmpeg/macos-arm64/README.md
```

Tests/data:

```text
Create:
  DTXMania.Test/TestData/Video/tiny-uncompressed.avi
  DTXMania.Test/Stage/Performance/FfmpegChartVideoPlayerTests.cs
  DTXMania.Test/Stage/Performance/VideoFrameSelectorTests.cs

Modify:
  DTXMania.Test/DTXMania.Test.csproj
  DTXMania.Test/DTXMania.Test.Mac.csproj
  DTXMania.Test/Resources/FfmpegRuntimeTests.cs
  DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs
  DTXMania.Test/Song/DTXChartParserTests.cs and/or DTXChartParserAdditionalTests.cs
  DTXMania.Test/Song/ParsedChartTests.cs
  DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
  DTXMania.Test/Stage/Performance/PerformanceRendererStateTests.cs
```

This file list is a boundary, not a requirement to create extra abstraction. Keep the pure selector tiny; do not add a scheduler/service/framework.

## Rejected alternatives

- **Second clock / media-owned Play Speed:** rejected because `PlaybackClock` already owns logical rate and pause semantics.
- **Call-order-only background rendering:** rejected because the current sprite batch is depth sorted.
- **Reuse `ParseHeaderCommand()` for video definitions:** rejected because the current `inDataSection` latch would lose valid late definitions.
- **Drop frames when producer queue is full:** rejected because slow Play Speed still needs not-yet-due frames; producer backpressure is the correct bounded behavior.
- **GPU-bound frame-selection tests:** rejected because hold/skip/stale is pure timing logic and Mac deliberately excludes the graphics-device test helper.
- **Media Foundation / AVFoundation / MonoGame `VideoPlayer`:** rejected because CX already owns FFmpeg cross-platform resolution.
- **Port NX `CActPerfAVI` / AVIPAN / MovieMode:** rejected as unnecessary legacy rendering scope.
- **Whole-file predecode:** rejected due to unbounded memory.
- **Generic media lifecycle/scheduler framework:** rejected; stage scheduling and existing cleanup seams are sufficient.

## Delivery boundary

Keep planning and implementation on this single HPA-11 PR and within roughly 2–3 engineer days.

If the actual Group C probe or smoke proves the bounded FFmpeg-to-RGBA approach materially larger than this design, stop and re-plan HPA-11 before broadening scope.