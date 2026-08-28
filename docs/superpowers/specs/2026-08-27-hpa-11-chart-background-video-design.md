# HPA-11 Chart Background Video Design

## Summary

HPA-11 adds the first focused CX vertical slice for chart-authored background video during `PerformanceStage`.

The implementation should reuse two foundations that already exist on `main`:

1. `ParsedChart.FinalizeChart()` / `ChartTimingMap` already owns authored `(bar, tick) -> TimeMs` conversion for gameplay events.
2. `SongTimer.SongPositionMs` already exposes the logical gameplay timeline, including the frozen Play Speed profile and pause/resume behavior.

Video must therefore be a **follower of logical chart time**, not a second clock. The stage schedules chart video events using finalized event times; the video player presents the frame corresponding to `SongTimer.SongPositionMs - event.TimeMs`.

For media decoding, use the existing shared FFmpeg runtime rather than introducing MonoGame `VideoPlayer` assumptions or separate Windows/macOS native media stacks. Decode a deliberately small first format surface—representative uncompressed AVI—to CPU frames, upload only the current frame to one MonoGame texture on the game thread, and render it between the existing static background and gameplay lanes/HUD.

If video is absent, unresolved, unsupported, corrupt, or temporarily unable to provide a timely frame, gameplay continues with the existing static background.

## Why HPA-11 is the next actionable backlog item

The current active DTXManiaCX backlog has two core product items plus optional recorder hardening:

- HPA-11: chart background video playback;
- HPA-12: interactive key-sound latency validation;
- HPA-504/505/506: explicitly optional recorder hardening.

HPA-11 is unblocked, player-visible, and fully executable by implementation agents. HPA-12 begins with physical Windows/macOS audio-device measurements and evidence gathering before code changes can be justified, so it is not the best next agent-driven implementation slice.

This PR remains the single planning + implementation PR for HPA-11.

## Reuse survey

### Current chart timing ownership

`DTXMania.Game/Lib/Song/Components/ParsedChart.cs` already owns finalized chart timing:

- `Notes` and `BGMEvents` retain authored bar/tick positions;
- `ChartTimingMap` compiles channel 02/03/08 timing changes;
- `FinalizeChart()` calculates `TimeMs`, sorts events, and computes chart duration.

Video events should join this same lifecycle. Do not create a separate fixed-BPM video-time calculator.

### Current gameplay clock ownership

`DTXMania.Game/Lib/Stage/Performance/PlaybackClock.cs` converts monotonic game time into logical chart time using the frozen `PlaySpeedPercent` profile.

`DTXMania.Game/Lib/Stage/Performance/SongTimer.cs` exposes that logical position to `PerformanceStage` and already freezes/resumes correctly when gameplay pauses.

Therefore HPA-11 does not need a video stopwatch, a video-specific Play Speed multiplier, or explicit pause/resume timestamps. The video player is updated with the logical media position that the chart already uses.

### Current gameplay background ownership

`DTXMania.Game/Lib/Stage/Performance/BackgroundRenderer.cs` owns the normal skin background and fallback color. `PerformanceStage` draws that background before lanes and HUD.

Keep it. The video is an optional overlay in the same background layer:

```text
static/fallback background
  -> active chart video frame (when valid)
  -> lane background / notes / HUD / effects
```

This preserves today’s behavior automatically whenever video is unavailable.

### Current FFmpeg ownership

CX already ships `FFMpegCore` and centralizes executable resolution in `DTXMania.Game/Lib/Resources/FfmpegRuntime.cs`.

Windows already has a packaged FFmpeg runtime. Native Apple Silicon packaging was established by HPA-512 under:

```text
runtimes/osx-arm64/MMTools/{ffmpeg,ffprobe}
```

and is verified by `DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs`.

Reuse that runtime resolution and extend the existing Mac capability contract only with the video features HPA-11 actually needs. Do not add another binary resolver or NuGet FFmpeg provider.

The current Apple Silicon builder is intentionally audio-only (`--disable-everything`), so HPA-11 must add the minimal AVI/raw-video capabilities rather than assuming the bundled binary can already decode video.

## Legacy DTX/NX contract

Inspection of `DTXmaniaNX` pins the compatibility surface needed by this slice:

- video file definitions accept both `#AVIxx` and `#VIDEOxx`;
- the suffix is a two-character base-36 resource id;
- later definitions for the same id replace earlier ones;
- channel `54` (`EChannel.Movie`) triggers video playback;
- channel `5A` (`EChannel.MovieFull`) also triggers video playback;
- `00` cells mean no event;
- NX also supports `AVIPAN` and a much larger DirectShow/SharpDX positioning/mode matrix.

CX should intentionally implement only the plain movie contract in HPA-11.

### First-slice normalization

Both channel 54 and channel 5A become the same CX `ChartVideoEvent` behavior: play the referenced video in the normal gameplay background layer.

Do not add a `Movie`/`MovieFull` mode enum merely to preserve a distinction that this slice does not render differently. If a future product requirement needs distinct behavior, add it then.

`AVIPAN`, chart-authored crop/position animation, old MovieMode permutations, and static BGA/BMP channels are out of scope.

## Supported first media profile

The repository performance corpus already identifies `DTXFiles.2/Test/mas.dtx` with `bg.avi` as the representative uncompressed-AVI rendering load.

HPA-11 should guarantee this first profile:

- AVI container;
- uncompressed/raw video codec used by the representative corpus;
- constant/usable frame-rate metadata from `ffprobe`;
- video track only for rendering; embedded media audio is ignored;
- arbitrary source dimensions, rendered with aspect ratio preserved.

Do not preemptively enable H.264, HEVC, VP9, MPEG-4, subtitle, audio, streaming, network protocol, or broad container support. If inspection of the actual Group C `bg.avi` shows one additional decoder is required, add exactly that decoder in this PR and pin it in the capability test.

## Design

### 1. Add one chart video event model

Create one small `ChartVideoEvent` next to `BGMEvent` with only the data the runtime consumes:

```text
Bar
Tick
TimeMs
VideoId
VideoFilePath
```

`VideoFilePath` is empty when no final definition resolves the id.

Add `ParsedChart.VideoEvents` and `AddVideoEvent(...)`.

Do not expose a second public video-definition registry unless implementation proves a caller needs it. The parser can keep definitions local, then resolve every retained event after the complete file has been read. This naturally gives legacy-compatible “later definition wins” behavior even when an event appears before its `#AVIxx` definition.

### 2. Parse only `#AVIxx` / `#VIDEOxx` + channels 54 / 5A

Extend the existing parser attempt-local state with a case-insensitive video-definition dictionary.

Header behavior:

```text
#AVI01:bg.avi
#VIDEO01:bg.avi
```

Both define video id `01`. Resolve relative paths against the chart directory using the same path policy already used for chart-owned media. Do not require the file to exist while parsing.

Measure behavior:

```text
#00054:01
#0005A:01
```

Use the same two-character cell subdivision rule as other DTX event channels. Retain non-`00` references as `ChartVideoEvent` authored positions.

After the file scan, resolve each event’s `VideoFilePath` from the final definition table. A missing definition is non-fatal and leaves an empty path for runtime fallback/diagnostics.

Do not parse `#AVIPANxx` in this ticket.

### 3. Finalize video timing through `ChartTimingMap`

`ParsedChart.FinalizeChart()` must treat video events like BGM events:

- include their normalized bar in `highestOccupiedBar`;
- calculate `TimeMs` through `TimingMap.CalculateTimeMs(bar, tick)`;
- sort by resolved `TimeMs`;
- include the event trigger time when calculating `DurationMs`.

Do not include the media file’s own duration in chart duration. HPA-11 needs the event to be reachable before gameplay ends; it does not redefine DTX song-end semantics around movie length.

This makes channel 02/03/08 timing changes authoritative for notes, BGM, beat/measure markers, and video without another timeline implementation.

### 4. Keep one narrow video-player abstraction

Add a small gameplay-owned interface, for example `IChartVideoPlayer`, under `Lib/Stage/Performance`.

The interface only needs lifecycle/timeline behavior used by `PerformanceStage`, conceptually:

```text
Start(path, initialMediaTimeMs)
Update(mediaTimeMs)
Draw(spriteBatch, destinationBounds)
Stop()
Dispose()
```

Exact return types are an implementation detail, but the contract must make these properties true:

- `Start` must not block the game thread on whole-file decode;
- `Update` is called on the game thread and receives authoritative logical media time;
- GPU texture upload/draw occurs only on the game thread;
- decoder/process failure is contained inside the player and reported diagnostically;
- `Stop`/`Dispose` cancels decoding and terminates the owned FFmpeg process.

Do not create a generic media service, plugin model, playback manager hierarchy, or project-wide video abstraction.

### 5. FFmpeg-backed frame streaming

Implement one `FfmpegChartVideoPlayer` using the already configured shared runtime.

The simple pipeline is:

```text
ffprobe -> width / height / frame-rate metadata
ffmpeg  -> raw pixel frames on stdout
background decode worker -> small bounded CPU frame queue
game-thread Update       -> select latest frame due for logical media time
                           -> upload it into one reusable Texture2D
game-thread Draw         -> render current texture
```

The FFmpeg process should decode as fast as available CPU allows; do **not** use FFmpeg wall-clock real-time playback as the synchronization mechanism.

A small bounded queue is enough. Its purpose is to keep a few frames ahead without decoding the whole movie into RAM. The exact capacity is not a product contract; keep it single-digit unless profiling proves otherwise.

Frame timestamps may be derived from the probed constant frame rate for this first AVI profile. VFR-accurate PTS plumbing is not required by HPA-11.

When `Start` is requested after the event is already in the past—for example because one update crosses multiple events or decoder startup is delayed—start/seek near `initialMediaTimeMs` rather than deliberately replaying from frame zero and showing stale content.

Do not play or mix the video file’s audio track. DTX chart audio remains the sole gameplay audio authority.

### 6. Logical-time frame selection

For the currently active event:

```text
mediaTimeMs = max(0, SongTimer.SongPositionMs - activeEvent.TimeMs)
```

`PerformanceStage` passes that value to the video player every gameplay update.

This automatically handles:

- **100% Play Speed:** normal progression;
- **50–95%:** the same source frame is held longer because logical time advances more slowly;
- **105–150%:** stale decoded frames are skipped because logical time advances faster;
- **Pause:** `SongPositionMs` freezes, so the current frame freezes;
- **Resume:** logical time continues from the paused position;
- **update hitch:** the player catches up to the latest due frame instead of replaying every missed frame.

There is no separate playback-speed transform in the video backend.

### 7. Never knowingly display a badly stale frame

The decoder is a producer; `SongTimer` is still the authority.

If the decoder cannot provide a frame that is reasonably current for the target logical position, do not let an old frame advance on its own clock. The player may temporarily expose no drawable frame (leaving the static background visible) until it catches up.

Use the source frame interval to define the stale-frame check rather than a new user setting. A small tolerance of a couple of source-frame intervals is sufficient for the first slice.

This satisfies the HPA-11 requirement that non-default Play Speed must not silently desynchronize video: failure to keep up degrades to the known static fallback rather than pretending an incorrect video position is synchronized.

Do not add adaptive quality, transcoding, frame interpolation, or a recovery state machine.

### 8. Event scheduling in `PerformanceStage`

Maintain one cursor into sorted `ParsedChart.VideoEvents` plus the active event start time.

On update:

1. read `_songTimer.SongPositionMs` once;
2. consume every video event now due;
3. if several became due in one update, start only the **last** due event—intermediate events are already obsolete;
4. stop the previous movie before replacing it;
5. if the final due event has an empty/missing path or player startup fails, leave the static background active;
6. update the active player using `songTimeMs - event.TimeMs`.

Reset the cursor/player state for each performance activation. Stop the player during retry, stage deactivation, cancellation, and disposal using the same lifecycle points that already clean gameplay resources.

No separate scheduler class is needed unless implementation shows `PerformanceStage` cannot keep this small.

### 9. Rendering behavior

Keep `BackgroundRenderer` as the base layer.

When the player has a current frame, render it centered in the existing virtual gameplay background bounds with aspect ratio preserved. Use a tiny pure “aspect fit” rectangle helper so 4:3 legacy AVI is not stretched to 16:9.

Any uncovered letter/pillar-box area continues to show the existing static background. Do not add black-bar art, lane alpha, crop modes, or per-video positioning.

Both DTX channel 54 and 5A use this same CX rendering policy in HPA-11.

### 10. Failure behavior

Background video is optional presentation, never gameplay state.

The following must not prevent chart play or transition to Result:

- FFmpeg runtime unavailable;
- video definition missing;
- video file missing;
- `ffprobe` failure;
- unsupported/corrupt AVI;
- invalid dimensions/frame-rate metadata;
- FFmpeg process exit;
- decoder cancellation during retrigger/exit;
- texture upload failure.

Log one useful diagnostic per failed video start/playback generation and fall back to the existing background. Do not show a modal/toast or repeatedly log every frame.

A later valid video event in the same chart may still start after an earlier event fails.

## Apple Silicon FFmpeg amendment

The HPA-512 builder currently enables audio-only capabilities under `--disable-everything`.

Extend `tools/ffmpeg/macos-arm64/build-runtime.sh` only with the features required by the representative AVI-to-raw-frame pipeline. Expected minimum surface is:

```text
demuxer: avi
decoder: rawvideo
encoder: rawvideo
muxer:   rawvideo
protocol: pipe (already enabled)
```

If the actual Group C AVI reports a different uncompressed decoder name, use the probed corpus fact rather than this expectation and pin the exact resulting capability.

Extend the existing builder validation and `FfmpegBundledRuntimeTests` rather than creating a second runtime checker. Preserve all HPA-512 audio capabilities and tests.

Do not upgrade FFmpeg, broaden packaging, or change runtime lookup precedence.

## Test strategy

### Parser / chart timing tests

Extend existing `DTXChartParserTests` / `DTXChartParserAdditionalTests` and `ParsedChartTests` with focused contracts:

- `#AVIxx` definition + channel 54 resolves path/event;
- `#VIDEOxx` is an alias;
- channel 5A creates the same supported event behavior;
- `00` cells are ignored;
- multiple cells map to correct authored ticks;
- later definition wins even if definition appears after an event;
- missing definition keeps parsing playable and leaves unresolved path;
- channel 02/03/08 timing changes affect video `TimeMs` through the same timing map;
- video events participate in sorting and occupied-duration horizon.

Do not add AVIPAN tests.

### Video backend tests

Add a tiny deterministic uncompressed AVI fixture, not the 54 MB representative corpus, for automated decoder capability tests.

Pin at least:

- probe returns usable dimensions/frame rate;
- the configured FFmpeg runtime can decode the fixture to raw frames;
- logical frame selection holds/skips frames based on target media time rather than elapsed wall time;
- missing/corrupt input returns a contained failure;
- cancellation/retrigger terminates the prior decode generation;
- aspect-fit geometry preserves source ratio.

Keep GPU-specific assertions small. Do not build a fake FFmpeg server.

### `PerformanceStageDeterministicTests`

Reuse the existing reflection/test seams and inject/set one fake `IChartVideoPlayer`. Pin stage orchestration only:

- no video events -> no player start and existing background path unchanged;
- before event -> no start;
- crossing event -> start once at correct local media time;
- update hitch crossing multiple events -> only last due event starts;
- pause -> unchanged song position yields unchanged media position;
- resume/non-default Play Speed -> player receives `SongTimer` logical positions, not separately scaled time;
- failed/missing event does not block later gameplay or later valid video event;
- deactivation/retry stops owned video playback.

Do not launch FFmpeg from deterministic stage tests.

### Platform capability/smoke

Automated PR gates:

- macOS: `DTXMania.Test.Mac.csproj`, including bundled FFmpeg capability + tiny AVI decode;
- Windows: normal `DTXMania.Test.csproj`, including the same tiny AVI decode where the packaged runtime supports it;
- normal Game builds remain green on both platforms.

Representative acceptance uses the existing Group C corpus chart `DTXFiles.2/Test/mas.dtx` / `bg.avi` on both supported platforms. Verify:

- movie appears behind lanes/HUD;
- start timing follows the chart event;
- pause freezes the displayed frame and resume continues aligned;
- 150% Play Speed stays logically aligned or cleanly falls back rather than visibly running at 100%;
- no-video chart still looks identical to current CX.

Do not commit the large external corpus into the repository.

## Expected implementation surface

Production, likely:

```text
Create:
  DTXMania.Game/Lib/Song/Components/ChartVideoEvent.cs
  DTXMania.Game/Lib/Stage/Performance/IChartVideoPlayer.cs
  DTXMania.Game/Lib/Stage/Performance/FfmpegChartVideoPlayer.cs

Modify:
  DTXMania.Game/Lib/Song/Components/ParsedChart.cs
  DTXMania.Game/Lib/Song/DTXChartParser.cs
  DTXMania.Game/Lib/Stage/PerformanceStage.cs
  tools/ffmpeg/macos-arm64/build-runtime.sh
  DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs
```

Tests, likely:

```text
Create:
  DTXMania.Test/TestData/Video/tiny-uncompressed.avi
  DTXMania.Test/Stage/Performance/FfmpegChartVideoPlayerTests.cs

Modify:
  DTXMania.Test/Song/DTXChartParserTests.cs and/or DTXChartParserAdditionalTests.cs
  DTXMania.Test/Song/ParsedChartTests.cs
  DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
```

Do not change configuration/database/UI surfaces for HPA-11.

## Rejected alternatives

### MonoGame `VideoPlayer`

Rejected for this slice because CX needs predictable Windows + DesktopGL/macOS behavior and already owns a packaged cross-platform FFmpeg dependency. Adding another platform-sensitive media path would increase uncertainty rather than reduce work.

### Media Foundation on Windows + AVFoundation on macOS

Rejected because two native backends double implementation/testing/maintenance for a hobby project whose existing FFmpeg runtime already spans both targets.

### Port NX `CActPerfAVI` / DirectShow behavior

Rejected. NX carries years of DirectShow/SharpDX sizing, MovieMode, crop, and AVIPAN behavior that HPA-11 explicitly does not require. CX should preserve the chart trigger contract, not the legacy rendering subsystem.

### Predecode the whole movie

Rejected due to unbounded memory/storage cost on real song videos.

### Spawn FFmpeg once per rendered frame

Rejected because process startup/seek overhead would be much more expensive than one sequential decode process with a bounded queue.

### Let the decoder/media player own playback time

Rejected because it would create a second clock and would immediately make Play Speed and pause synchronization harder. `SongTimer` is already the correct authority.

### Add video settings now

Rejected. Video is automatically enabled when a supported chart event exists. MovieMode, alpha, enable toggles, and playback preferences are YAGNI for this first vertical slice.

## Delivery boundary

Keep planning and implementation on this one HPA-11 PR and target roughly 2–3 engineer days.

Do not preemptively split the ticket. If implementation proves the representative AVI needs a materially larger media subsystem than this design, stop the draft and re-plan the ticket before broadening scope; do not hide that growth behind a generic framework.
