# HPA-11 Chart Background Video Design

## Summary

HPA-11 adds the first focused CX vertical slice for chart-authored background video during `PerformanceStage`.

The architecture stays deliberately small:

1. `ParsedChart.FinalizeChart()` / `ChartTimingMap` remains the only authored `(bar, tick) -> TimeMs` compiler.
2. `PerformanceStage` reuses the existing logical song time already read from `_songTimer.GetCurrentMs(_currentGameTime)`; video never owns a second clock and Play Speed is never multiplied a second time.
3. One FFmpeg-backed player uses **FFMpegCore 5.4.0** for probing, output-pipe ownership, cancellation, and process teardown. CX does not hand-roll a second FFmpeg process/executable layer.
4. Decoded CPU frames are bounded, selected by a tiny pure timing helper, and uploaded to one MonoGame texture on the game thread.
5. The existing static performance background always remains underneath video and is the fallback for every media failure or decoder catch-up gap.

The first slice supports legacy `#AVIxx` / `#VIDEOxx` definitions plus DTX channels `54` and `5A`, normalized to one CX background-video behavior. AVIPAN, MovieMode, static BGA/BMP, video configuration, embedded movie audio, broad codec/container support, and seek are out of scope.

## Verified reuse survey

### Chart timing already has one owner

`DTXMania.Game/Lib/Song/Components/ParsedChart.cs` already retains authored positions for notes/BGM and finalizes them through `ChartTimingMap`.

`ChartVideoEvent` is a sibling of `BGMEvent` and joins those same finalization loops. Do not add a video-specific time calculator.

### The real gameplay clock is `GetCurrentMs(...)`

`PlaybackClock` applies the frozen Play Speed factor and freezes its logical anchor while paused. `SongTimer` exposes that logical chart position through:

```csharp
_songTimer.GetCurrentMs(_currentGameTime)
```

`PerformanceStage.UpdateGameplay()` already reads this once inside the `_songTimer.IsPlaying` branch and passes the resulting `currentTimeMs` to BGM, gameplay managers, progress, and completion.

HPA-11 reuses that same `currentTimeMs` value. There is no `SongTimer.SongPositionMs` property on current `main`, and the implementation must not invent one.

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

A default-depth (`0.0`) video draw would therefore render in front of gameplay even if called immediately after the background.

`PerformanceStage` owns the composition contract. HPA-11 uses one stage-owned video depth of `0.95f`, between the static background and lane backgrounds, with `PerformanceUILayout.Background.Bounds` as the gameplay background bounds.

Because invoking private `DrawBackground()` requires a real `SpriteBatch`/`GraphicsDevice` that the Mac test project intentionally avoids, expose this pair through one tiny GPU-free stage resolver, conceptually:

```text
ResolveChartVideoDrawLayout() -> (PerformanceUILayout.Background.Bounds, 0.95f)
```

`DrawBackground()` consumes that resolver. Cross-platform tests pin the resolver; any SpriteBatch-touching assertion remains Windows-only if needed. Do not move z-order into the decoder.

### Whole-file collection already has a parser precedent

`DTXChartParser.ParseFileContentWithTimingAsync()` currently stops `ParseHeaderCommand()` after the first timeline/data marker. Copying WAV header behavior would therefore lose valid late `#AVIxx` / `#VIDEOxx` definitions.

The parser already has the right whole-file shape immediately before that latch: `TryHandleExtendedBpmDefinition(...)` runs for every line and `continue`s when it consumes one.

HPA-11 follows that existing pattern with a small `TryHandleVideoDefinition(...)` collector before the `inDataSection` split. It is parser-attempt-local, runs throughout the file, and gives legacy-compatible “later definition wins” behavior.

Definition ids and timeline cell ids are exactly two characters and normalized uppercase. `#AVIPANxx` must not be mistaken for `#AVIxx`.

Video path resolution reuses the existing `DTXChartParser.ResolveBGMPath(...)` helper. The helper name is historical; duplicating its chart-relative/path-separator policy would be worse than reusing it.

For channel cells, reuse `CalculatePairTick(...)`. If adding 54/5A would otherwise create a third full copy of the BGM/note pair loop, extract only a tiny non-zero event-pair iterator and reuse it for BGM + video. Do not turn Task 1 into a broad parser refactor.

### FFMpegCore 5.4.0 already owns process plumbing

CX already calls `FfmpegRuntime.EnsureConfigured()` to configure `GlobalFFOptions` for bundled runtime vs PATH.

FFMpegCore 5.4.0 already exposes the exact facilities HPA-11 needs:

- `FFProbe.AnalyseAsync(path, cancellationToken: ...)`;
- `FFMpegArguments.FromFileInput(...)`;
- `OutputToPipe(IPipeSink, ...)`;
- `IPipeSink.ReadAsync(Stream, CancellationToken)`;
- `CancellableThrough(cancellationToken)`;
- `ProcessAsynchronously()`;
- internal FFMpegCore process cancellation that sends `q`, cancels pipe work, and kills the process when cancellation wins.

The existing audio path already uses `FFProbe.AnalyseAsync` and `CancellableThrough(...).ProcessAsynchronously()`, and `ManagedSound` already uses `OutputToPipe(...)`.

Therefore HPA-11 must **not** add:

- a new FFmpeg executable-path helper;
- `.exe`/PATH logic in the video player;
- a raw `System.Diagnostics.Process` wrapper;
- a second resolver beside `FfmpegRuntime` / FFMpegCore.

The player first calls `FfmpegRuntime.EnsureConfigured()`, then lets FFMpegCore use the configured global options.

Tests that invoke shared FFmpeg runtime state join the existing `[Collection("FfmpegRuntimeState")]` serialization contract.

### Performance already has an early-stop path

`ReturnToSongSelect()` and `FinalizePerformance()` both stop logical time and call `StopGameplayAudioInstances()` before transition/fade. `CleanupComponents()` happens later.

Video cancellation joins that early-stop path so the generation is canceled before the transition; full player disposal remains in `CleanupComponents()`.

The cancellation design must not deadlock when the decoder is blocked on a full queue. The same generation `CancellationToken` must reach both FFMpegCore (`CancellableThrough`) and the queue write (`WriteAsync`/equivalent cancellable wait).

Do not add a generic media lifecycle manager.

### Existing out-of-process automation is reusable acceptance infrastructure

`DTXMania.Automation` / `DTXMania.E2E` already provide:

- `GameProcessDriver` for cross-platform process launch;
- `JsonRpcGameClient` / prepared-chart activation;
- `Eventually` polling;
- screenshot capture on the game’s next `Draw()`.

HPA-11 should reuse those seams for a small repeatable video-chart launch/capture/exit smoke. Do not build another E2E harness, image-diff framework, or platform-specific child-process inspector solely for this feature.

## Legacy DTX/NX contract for this slice

The required compatibility surface is:

- `#AVIxx:path` and `#VIDEOxx:path` are aliases;
- `xx` is exactly two resource-id characters, normalized uppercase in CX;
- later definitions for the same id win, including definitions authored after timeline rows;
- channel `54` triggers movie playback;
- channel `5A` triggers movie playback;
- `00` cells mean no event;
- both channels use the same CX rendering behavior in HPA-11;
- `#AVIPANxx` is intentionally ignored.

`ChartVideoEvent` intentionally stores only:

```text
Bar
Tick
TimeMs
VideoId
VideoFilePath
```

It does **not** store the source channel. Preserving 54 vs 5A provenance only for a hypothetical future rendering distinction is YAGNI while both channels are deliberately normalized. If a future ticket adds different presentation semantics, add the field with that behavior.

## Declared first media profile

Implementation must not depend on the external 54 MB Group C corpus being present.

The automated first profile is explicitly declared as:

```text
container:     AVI
video codec:   FFmpeg rawvideo
source pixfmt: bgr24
output pixfmt: rgba
frame rate:    constant / usable rational rate from probe
movie audio:   ignored
```

Commit one tiny deterministic fixture such as:

```text
DTXMania.Test/TestData/Video/tiny-raw-bgr24.avi
```

with a few visually distinct frames.

The output contract is CPU RGBA frames suitable for `Texture2D.SetData`.

The Apple Silicon `--disable-everything` builder must enable only what this declared AVI -> RGBA path requires, expected to include:

- AVI demuxer;
- rawvideo decoder;
- rawvideo encoder/muxer for pipe output;
- pixel-format conversion support (`format` / swscale as required by the actual command);
- `pipe` protocol (already enabled).

Validation must execute an actual tiny-fixture decode to `-pix_fmt rgba -f rawvideo`, not only grep capability listings, while preserving all existing HPA-512 audio validation.

The repository corpus manifest still identifies the representative acceptance file:

```text
DTXFiles.2/Test/mas.dtx
DTXFiles.2/Test/bg.avi
```

When that corpus is available, run `ffprobe` during final acceptance and record `codec_name`, `pix_fmt`, dimensions, and frame rate. The corpus probe is an **acceptance check, not an implementation prerequisite**:

- if Group C matches the declared profile, record confirmation;
- if it needs one additional bounded decoder/pixel-format capability, add only that narrow capability and pin it;
- if it materially invalidates the bounded design, stop and re-plan before broadening scope.

Do not claim Group C’s codec/pixel format before it has actually been probed.

## Design

### 1. `ChartVideoEvent` joins existing chart finalization

Create one small model next to `BGMEvent` with the five fields above. Add `ParsedChart.VideoEvents` and one add helper.

`FinalizeChart()` must:

- include normalized video-event bars in `highestOccupiedBar`;
- resolve `TimeMs` through `TimingMap.CalculateTimeMs`;
- sort video events by `TimeMs`;
- include video trigger times in the existing chart event horizon used for `DurationMs`.

Do not include movie file duration in chart duration.

### 2. Whole-file video definition collection

For every non-comment line, before the `inDataSection` split, call a small collector shaped like the existing `TryHandleExtendedBpmDefinition(...)` precedent.

Accepted definitions:

```text
#AVI01:bg.avi
#VIDEO01:bg.avi
```

Rules:

- command prefix is case-insensitive;
- id is exactly two characters and stored uppercase;
- quoted-value handling matches existing header behavior;
- later assignment replaces earlier assignment;
- malformed definition is ignored diagnostically/non-fatally;
- `#AVIPAN01:...` is not accepted as an AVI definition.

Timeline channels `54` and `5A` retain non-`00` two-character ids with ticks calculated by existing `CalculatePairTick(...)` semantics.

After the complete scan, resolve every event against the final definition table using existing `ResolveBGMPath(...)`. Missing definitions leave `VideoFilePath` empty.

Do not add a public definition registry or second path helper.

### 3. One narrow non-blocking player contract

Add one gameplay-owned interface under `Lib/Stage/Performance`, conceptually:

```text
Start(path)
Update(mediaTimeMs)
Draw(spriteBatch, destinationBounds, layerDepth)
Stop()
Dispose()
```

`Start` has no seek/media-time parameter in this slice.

Mandatory behavior:

- `Start` returns after scheduling a new decode generation; it does not synchronously wait for `FFProbe` or FFmpeg process startup on the game thread;
- until probe/startup/first due frame completes, `Draw` renders nothing and the static background remains visible;
- probe/process failures are contained and logged once per generation;
- CPU decoding happens off the game thread;
- texture creation/update/draw happen on the game thread;
- the stage supplies destination bounds and layer depth;
- `Stop` cancels the current generation and immediately clears drawable-generation state without waiting on a full queue;
- `Dispose` additionally releases texture/resources.

Do not add generic seek, playback-rate, audio, device, or plugin APIs.

### 4. FFMpegCore output pipe -> bounded cancellable queue

The player pipeline is:

```text
FfmpegRuntime.EnsureConfigured()
  -> FFProbe.AnalyseAsync(..., generationToken)
  -> FFMpegArguments.FromFileInput(...)
  -> OutputToPipe(VideoFramePipeSink)
  -> CancellableThrough(generationToken)
  -> ProcessAsynchronously()
  -> IPipeSink.ReadAsync(stream, generationToken)
  -> bounded CPU frame queue
  -> game-thread Update / Texture2D.SetData
```

Use a capacity of **3 queued decoded frames**.

Raw RGBA queue budget is therefore:

```text
3 * width * height * 4 bytes
```

Examples:

- 640x480: ~3.5 MiB queued;
- 1280x720: ~10.5 MiB queued;
- 1920x1080: ~23.7 MiB queued.

Three frames are enough to stay slightly ahead without turning the queue into predecode storage.

When full, the producer waits using the generation cancellation token. It must not grow the queue or drop not-yet-due future frames. `Stop`/retrigger cancellation must wake a producer parked on a full queue.

Only the consumer may discard frames already obsolete for current logical time.

Do not use `-re`, a decoder wall clock, raw `Process`, or whole-file predecode.

### 5. No seek in the first slice; timestamp origin is zero

Do not use `-ss` in HPA-11.

A generation always decodes sequentially from media time zero. Frame timestamps are therefore deterministic for the declared constant-rate profile:

```text
frameTimeMs = frameIndex * frameIntervalMs
```

If async startup or an update hitch means logical media time is already ahead, the decoder catches up as fast as possible and the selector skips obsolete decoded frames. Until it catches up within the stale tolerance, the static background remains visible.

This avoids keyframe/seek-origin ambiguity in a raw frame pipe and keeps HPA-11’s only runtime clock in `PerformanceStage`.

### 6. Keep frame selection pure

Add one tiny internal `VideoFrameSelector` (helper, not service). Inputs are target logical media time, source frame interval, current-frame timestamp, and queued timestamps. Output tells the player whether to:

- hold the current frame;
- consume through the latest queued frame due for target time, skipping obsolete intermediates;
- expose no drawable frame because the decoder is materially stale.

Use a stale tolerance derived from a small number of frame intervals; no user setting.

Tests cover slow progression, high-speed jumps, update hitches, initial async-start catch-up, and stale fallback without a `GraphicsDevice` or FFmpeg process.

### 7. Texture lifecycle is generation/dimension aware

The player owns at most one current `Texture2D`.

- Reuse it while width/height are unchanged.
- On a new generation with different dimensions, dispose/replace before `SetData`.
- Never upload a buffer shape from the prior generation.
- Hiding a stale frame does not require immediate texture destruction; stop/retrigger/dispose owns cleanup.

### 8. Stage scheduling reuses the existing `currentTimeMs`

Inside the existing `_songTimer != null && _songTimer.IsPlaying` update branch, reuse:

```csharp
var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
```

Scheduling rules:

1. consume every not-yet-handled video event with `TimeMs <= currentTimeMs`;
2. if several become due in one update, only the **last due** event starts;
3. stop/cancel the previous generation before replacement;
4. unresolved/missing media leaves video inactive but does not block later events;
5. start the new generation with `Start(path)`;
6. for the active event, call:

```text
player.Update(max(0, currentTimeMs - activeEvent.TimeMs))
```

Do not call `GetCurrentMs` again and do not apply `PlaybackModifiers.Speed` again.

Pause needs no second transport clock: the current logical target stops advancing and the last timely texture remains held. Resume continues from the existing `PlaybackClock` anchor.

No `VideoScheduler` type is needed.

### 9. Draw through a GPU-free stage-owned layout resolver

`DrawBackground()` remains the composition owner:

```text
static/fallback background 1.0
chart video               0.95
lane fallback             0.9
lane strips               0.8
```

A small stage-owned pure resolver returns:

```text
bounds = PerformanceUILayout.Background.Bounds
layerDepth = 0.95f
```

`DrawBackground()` draws static/fallback background first, then asks the player to draw a timely frame with that resolved pair.

Aspect-fit geometry stays pure/testable so 4:3 video is not stretched to 16:9. Uncovered area shows the static background.

### 10. Stop early; dispose later

Extend the existing early-stop helper path used by both `ReturnToSongSelect()` and `FinalizePerformance()` so `_chartVideoPlayer.Stop()` runs before transition/fade.

The generation token is shared by:

- FFMpegCore `CancellableThrough(token)`;
- `FFProbe.AnalyseAsync(..., token)`;
- `IPipeSink.ReadAsync(..., token)`;
- bounded queue writes.

This makes full-queue cancellation a required behavior rather than an implementation accident.

`CleanupComponents()` disposes and clears the player. Retrigger stops the previous generation before starting another.

### 11. Failure behavior

Background video is optional presentation, never gameplay state.

The following are non-fatal and fall back to the static background:

- unresolved definition/path;
- missing/corrupt/unsupported video;
- FFmpeg runtime unavailable;
- probe failure;
- invalid dimensions/frame rate;
- process/pipe/read failure;
- decoder behind logical time;
- texture upload failure;
- cancellation during retrigger/exit.

Log one useful diagnostic per failed generation; do not log every frame and do not show a modal/toast. A later valid event may still start.

## Test strategy

### Parser/finalization

Pin:

- `#AVIxx` and `#VIDEOxx` aliases;
- ids uppercased and exactly two characters;
- `#AVIPAN01` explicitly ignored;
- channel 54 and 5A events;
- `00` ignored;
- correct ticks for multiple cells via existing pair/tick semantics;
- event first, later definition afterward resolves despite the data-section latch;
- repeated late definition wins;
- path uses `ResolveBGMPath` semantics;
- missing definition stays playable with empty path;
- channel 02/03/08 timing flows through `ChartTimingMap`;
- sorting and duration/event horizon include video triggers.

### Runtime/player

Use the committed `tiny-raw-bgr24.avi`; explicitly copy `TestData/Video/**` in both test csprojs.

Tests that invoke FFMpegCore join `[Collection("FfmpegRuntimeState")]`.

Pin:

- `Start` returns while a controlled probe/startup operation is still pending;
- declared fixture probes with usable dimensions/frame rate;
- declared fixture decodes through FFMpegCore `OutputToPipe` into RGBA frames;
- Mac builder preserves all previous audio capabilities and validates the AVI/rawvideo/RGBA path on the fixture;
- bounded queue capacity is 3;
- producer waits on full queue and cancellation unblocks it;
- stopping a full-queue generation completes without deadlock and the FFMpegCore processing task exits within the test timeout;
- frame timestamp origin is zero (`n * frameIntervalMs`) because seek is not used;
- pure selector holds/skips/stales correctly, including late-start catch-up;
- missing/corrupt input is contained;
- retrigger cancels prior generation;
- dimension change replaces texture;
- aspect-fit geometry preserves source ratio.

No new `FfmpegRuntime` executable-path tests are required; FFMpegCore already owns binary path lookup.

### PerformanceStage / renderer-state

Use one fake `IChartVideoPlayer`; do not launch FFmpeg.

Pin:

- no events -> no start;
- before event -> no start;
- crossing event -> one async `Start(path)` and logical `Update(max(0, currentTimeMs - event.TimeMs))`;
- hitch crossing multiple events -> only last due starts;
- subsequent updates pass the same logical time domain with no second speed multiply;
- missing/failed event does not block later valid event;
- existing early-stop path calls `Stop()`;
- cleanup calls `Dispose()`;
- GPU-free stage resolver returns `PerformanceUILayout.Background.Bounds` and `0.95f` on both Windows and Mac test projects.

Do not require a Mac `GraphicsDevice` to prove depth ownership.

### Existing E2E + Group C acceptance

Add one small HPA-11 smoke using existing `DTXMania.E2E` prepared-chart/JSON-RPC/screenshot infrastructure and the tiny video fixture. It should:

- launch the fixture chart into `Performance`;
- capture a performance screenshot artifact while video should be active;
- leave performance and verify the game reaches the expected next stage without hang;
- run on the existing supported E2E platform path where practical.

Do not add a new screenshot decoder/diff framework or portable process-tree inspector solely for this ticket. Visual depth, pause/resume, and 150% alignment remain acceptance observations; process teardown is pinned deterministically in player cancellation tests.

When Group C is available on Windows and Apple Silicon macOS, additionally record:

- actual `bg.avi` probe facts;
- movie behind lanes/HUD;
- event start timing;
- pause/resume alignment;
- 150% Play Speed logical alignment;
- stale fallback behavior;
- no-video chart unchanged.

Do not commit the large corpus.

## Expected implementation surface

Production:

```text
Create:
  DTXMania.Game/Lib/Song/Components/ChartVideoEvent.cs
  DTXMania.Game/Lib/Stage/Performance/IChartVideoPlayer.cs
  DTXMania.Game/Lib/Stage/Performance/FfmpegChartVideoPlayer.cs
  DTXMania.Game/Lib/Stage/Performance/VideoFrameSelector.cs

Modify:
  DTXMania.Game/Lib/Song/Components/ParsedChart.cs
  DTXMania.Game/Lib/Song/DTXChartParser.cs
  DTXMania.Game/Lib/Stage/PerformanceStage.cs
  tools/ffmpeg/macos-arm64/build-runtime.sh
  tools/ffmpeg/macos-arm64/README.md
```

`FfmpegRuntime.cs` is reused unchanged unless implementation discovers a concrete bug in its current configuration behavior.

Tests/data, likely:

```text
Create:
  DTXMania.Test/TestData/Video/tiny-raw-bgr24.avi
  DTXMania.Test/Stage/Performance/FfmpegChartVideoPlayerTests.cs
  DTXMania.Test/Stage/Performance/VideoFrameSelectorTests.cs
  DTXMania.E2E/ChartBackgroundVideoSmokeTests.cs

Modify:
  DTXMania.Test/DTXMania.Test.csproj
  DTXMania.Test/DTXMania.Test.Mac.csproj
  DTXMania.Test/Song/DTXChartParserTests.cs and/or DTXChartParserAdditionalTests.cs
  DTXMania.Test/Song/ParsedChartTests.cs
  DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
  DTXMania.Test/Stage/Performance/PerformanceRendererStateTests.cs
  DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs only if needed to stage the video fixture
```

Do not add new `FfmpegRuntimeTests` solely for executable paths and do not add another process wrapper.

## Rejected alternatives

- **Second clock / media-owned Play Speed:** existing `PlaybackClock` already owns rate and pause semantics.
- **Call-order-only background rendering:** current sprite batch is depth sorted.
- **Reuse `ParseHeaderCommand()` for video definitions:** current data-section latch loses valid late definitions.
- **Store source channel in `ChartVideoEvent` with no behavior:** speculative future provenance while 54/5A are deliberately normalized.
- **Raw `System.Diagnostics.Process` / executable-path helper:** FFMpegCore 5.4.0 already owns binary lookup, pipe, cancellation, and kill behavior.
- **Seek on late startup:** unnecessary first-slice complexity and ambiguous raw-frame timestamp origin; sequential decode + skip/stale fallback is sufficient.
- **Drop frames when producer queue is full:** slow Play Speed still needs not-yet-due frames; producer backpressure is correct.
- **Uncancellable blocking enqueue:** can deadlock stage exit while paused/full; generation cancellation must wake it.
- **GPU-bound frame-selection/depth tests:** hold/skip/stale and draw layout are pure contracts; Mac intentionally excludes the graphics-device helper.
- **Make Group C corpus an implementation prerequisite:** the artifact is external; declared fixture profile enables implementation and Group C remains final acceptance.
- **New E2E/image-diff/process-tree framework:** existing automation is sufficient for launch/capture/exit evidence; deeper visual checks stay bounded acceptance work.
- **Media Foundation / AVFoundation / MonoGame `VideoPlayer`:** CX already owns cross-platform FFmpeg.
- **Port NX `CActPerfAVI` / AVIPAN / MovieMode:** unnecessary legacy rendering scope.
- **Whole-file predecode:** unbounded memory.
- **Generic media lifecycle/scheduler framework:** stage scheduling and existing cleanup seams are sufficient.

## Delivery boundary

Keep planning and implementation on this single HPA-11 PR and within roughly 2–3 engineer days.

If Group C acceptance proves the declared AVI/rawvideo-to-RGBA approach materially larger than this bounded design, stop and re-plan HPA-11 before broadening scope.
