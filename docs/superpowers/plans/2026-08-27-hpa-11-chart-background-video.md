# HPA-11 Chart Background Video Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans`. Keep planning and implementation on this same PR.

**Goal:** Play chart-authored DTX AVI video behind the gameplay lanes/HUD, synchronized to CX logical song time across pause/resume and the existing 50–150% Play Speed range, with static-background fallback on every media failure.

**Architecture:** Extend `ParsedChart` with one video-event list finalized by the existing `ChartTimingMap`. Parse only legacy `#AVIxx`/`#VIDEOxx` definitions and channels `54`/`5A`. Add one narrow FFmpeg-backed video player that streams a small bounded queue of raw frames and uploads the current logical-time frame to one MonoGame texture on the game thread. `PerformanceStage` remains the scheduler and uses `_songTimer.SongPositionMs` as the only playback clock.

**Tech Stack:** .NET 8, C#, MonoGame 3.8.4.1, FFMpegCore 5.4.0 / existing FFmpeg runtime, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-27-hpa-11-chart-background-video-design.md`

## Global constraints

- One PR for HPA-11 planning + implementation.
- First supported media profile is the representative **uncompressed AVI** path from Group C (`DTXFiles.2/Test/mas.dtx` / `bg.avi`).
- Parse `#AVIxx` and `#VIDEOxx` as aliases; later definition wins.
- Support DTX channels `54` and `5A` as the same CX background-video behavior.
- Defer AVIPAN, MovieMode, crop/pan animation, user video settings, lane/video alpha, chart static BGA/BMP, embedded video audio, subtitles, streaming, and broad codec/container support.
- Reuse `ParsedChart.FinalizeChart()` / `ChartTimingMap`; no video-specific timing calculator.
- Reuse `_songTimer.SongPositionMs`; no video stopwatch or second Play Speed transform.
- Reuse `FfmpegRuntime`; no second FFmpeg resolver or platform-specific Media Foundation/AVFoundation backend.
- Reuse `BackgroundRenderer`; static background remains beneath video and is the fallback.
- GPU texture creation/update/draw stays on the game thread. Background work may only produce CPU frame data / own the child FFmpeg process.
- Never block gameplay because video is missing, corrupt, unsupported, behind schedule, or unavailable.
- Do not commit the large Group C corpus. Use a tiny deterministic AVI fixture for automated tests.
- Keep implementation within the project’s ~3 engineer-day ceiling. If the representative AVI invalidates the bounded backend assumption, stop and re-plan before broadening the PR.

---

## Task 1: Add the DTX video event contract to the existing timing map

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

**Produces:** finalized, sorted chart video events with resolved paths and the same timing semantics as notes/BGM.

- [ ] **Step 1: Write parser characterization tests first.**

Pin only the first-slice contract:

```text
#AVI01:bg.avi + #00054:01 -> one event
#VIDEO01:bg.avi           -> same definition behavior
#0005A:01                 -> same supported event behavior
00 cells                  -> ignored
multiple cells            -> correct authored ticks
late/repeated definition  -> final definition wins
missing definition        -> event retained with unresolved/empty path; parse still succeeds
```

Use temp chart directories/files so path resolution is asserted without requiring the AVI to decode.

Expected on current `main`: tests fail because no video event model/parser exists.

- [ ] **Step 2: Add the smallest event model and parser state.**

`ChartVideoEvent` needs only:

```text
Bar
Tick
TimeMs
VideoId
VideoFilePath
```

Add `ParsedChart.VideoEvents` and a small add helper matching existing `BGMEvent` conventions.

Inside each parser encoding attempt, keep one local case-insensitive video-definition map. Parse `#AVIxx` / `#VIDEOxx` two-character ids and channels `54` / `5A` using the existing two-character measure-cell subdivision pattern.

Do not add a public video definition repository or Movie/MovieFull enum; neither has a consumer in this slice.

- [ ] **Step 3: Resolve paths after the complete file scan.**

Resolve each retained event from the **final** definition table after parsing, so a later definition wins even when authored after the event.

Resolve relative media paths against the chart directory using the same chart-owned path policy already used for WAV media. Do not make parse success depend on `File.Exists`.

Unresolved definitions leave `VideoFilePath` empty and are handled non-fatally at runtime.

- [ ] **Step 4: Put video events through `FinalizeChart()`.**

Extend only the existing finalization loops:

- include video event bars in `highestOccupiedBar`;
- calculate `TimeMs` through `TimingMap.CalculateTimeMs`;
- sort video events by `TimeMs`;
- include the video trigger time in the chart event horizon used for `DurationMs`.

Do not inspect media duration here.

- [ ] **Step 5: Pin timing-map reuse.**

Add focused `ParsedChartTests` proving a video event follows channel 02/03/08 timing changes exactly like existing finalized events. Include sorting and an event beyond the last note so the duration/horizon behavior cannot regress.

- [ ] **Step 6: Run focused tests.**

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

Expected: PASS.

---

## Task 2: Add one bounded FFmpeg video player and extend the existing Mac runtime capability

**Files:**

```text
Create:
  DTXMania.Game/Lib/Stage/Performance/IChartVideoPlayer.cs
  DTXMania.Game/Lib/Stage/Performance/FfmpegChartVideoPlayer.cs
  DTXMania.Test/Stage/Performance/FfmpegChartVideoPlayerTests.cs
  DTXMania.Test/TestData/Video/tiny-uncompressed.avi

Modify:
  tools/ffmpeg/macos-arm64/build-runtime.sh
  tools/ffmpeg/macos-arm64/README.md (only capability list/verification if needed)
  DTXMania.Test/Resources/FfmpegBundledRuntimeTests.cs
  DTXMania.Test project item lists only if the existing wildcard/content rules require it
```

**Produces:** a cross-platform, game-owned player that can decode the supported AVI profile without introducing a second clock or loading the whole movie into memory.

- [ ] **Step 1: Add a tiny deterministic AVI fixture and failing capability test.**

Generate/commit a very small uncompressed AVI with a few visually distinct frames. Keep it small enough for normal source control; do not use the 54 MB performance corpus.

Extend `FfmpegBundledRuntimeTests` (or a nearby focused runtime test) to prove the configured runtime can probe/decode that file to raw video on supported hosts.

On Apple Silicon current `main`, this should fail because the HPA-512 runtime was built audio-only.

- [ ] **Step 2: Extend the existing Apple Silicon builder minimally.**

Preserve FFmpeg 7.0.2, checksum, cache, runtime layout, audio capabilities, and resolver behavior.

Add only the capabilities required by the actual tiny fixture + Group C uncompressed AVI. Expected minimum:

```text
avi demuxer
rawvideo decoder
rawvideo encoder
rawvideo muxer
```

`pipe` is already required by the audio runtime.

Add the new capabilities to the builder’s existing validation so a stale warm cache cannot pass simply because the source hash is unchanged. Do not add broad video codec/container families.

Run the existing audio FFmpeg tests as part of this task to prove no HPA-512 regression.

- [ ] **Step 3: Define the narrow stage-facing player contract.**

Keep `IChartVideoPlayer` limited to the stage’s needs. It should support conceptually:

```text
Start(path, initialMediaTimeMs)
Update(mediaTimeMs)
Draw(spriteBatch, gameplayBackgroundBounds)
Stop()
Dispose()
```

Do not expose generic seek/playback-rate/audio/device APIs.

- [ ] **Step 4: Implement CPU decode + game-thread texture upload in one bounded component.**

`FfmpegChartVideoPlayer` should:

1. call/reuse `FfmpegRuntime.EnsureConfigured()`;
2. probe width/height/frame-rate through the configured FFmpeg/FFprobe surface;
3. launch one owned FFmpeg decode process producing raw pixel frames to stdout;
4. run frame reads off the game thread into a small bounded queue;
5. tag frames using the probed constant frame interval;
6. on game-thread `Update(mediaTimeMs)`, discard obsolete frames and upload only the newest frame due for that logical time into one reusable `Texture2D`;
7. keep the current frame while logical time remains within that frame’s interval;
8. if the current frame becomes materially stale and the decoder has not caught up, expose no video frame so the static background is shown rather than displaying known-wrong timing;
9. on `Stop`/`Dispose`/retrigger, cancel the decode worker, close pipes, and terminate the owned process.

Do not decode the whole AVI into memory. Do not use FFmpeg `-re`/wall-clock pacing. Do not output or play the movie’s audio stream.

When starting after the event time has already passed, initialize the decoder near the requested media position instead of intentionally replaying stale frames from zero.

- [ ] **Step 5: Add pure/frame-stream behavior tests.**

Pin behavior that does not need a live full game:

- valid AVI metadata/probe;
- at least two decoded frames from the tiny fixture;
- logical-time selection holds a frame at slow progression and skips obsolete frames when target time jumps;
- stale/behind output becomes non-drawable rather than advancing on wall time;
- missing/corrupt media is contained;
- restart/cancellation replaces the previous generation and does not leak a child process;
- aspect-fit rectangle preserves source ratio inside gameplay bounds.

Keep GPU-specific assertions minimal; do not build a fake media server or generic decoder test framework.

- [ ] **Step 6: Run focused runtime/backend tests.**

macOS Apple Silicon:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~FfmpegBundledRuntimeTests|FullyQualifiedName~FfmpegChartVideoPlayerTests|FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~ManagedSound"
```

Windows CI:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore \
  --filter "FullyQualifiedName~FfmpegChartVideoPlayerTests|FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~ManagedSound"
```

Expected: PASS.

---

## Task 3: Wire video events into `PerformanceStage` and prove product behavior

**Files:**

```text
Modify:
  DTXMania.Game/Lib/Stage/PerformanceStage.cs
  DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
  DTXMania.Test/Stage/Performance/PerformanceRendererStateTests.cs only if draw-order ownership is already asserted there
  docs/performance/HPA-192-corpus-manifest.tsv only if a verification note needs clarification; do not change corpus contents
```

**Produces:** chart video starts/retriggers at finalized event times, follows `SongTimer` logical time, renders behind gameplay, and disappears cleanly on every failure/lifecycle boundary.

- [ ] **Step 1: Add failing orchestration tests using one fake player.**

Reuse the current deterministic `PerformanceStage` reflection/test style; do not build a second stage harness.

Pin:

1. no video events -> player never starts;
2. before first event -> player not started;
3. crossing one event -> start exactly once with local media time based on `SongPositionMs - event.TimeMs`;
4. one update crossing several events -> only the **last due** event starts;
5. subsequent update -> player receives the current logical media position with no extra Play Speed multiplication;
6. unchanged song position (pause) -> unchanged media position;
7. unresolved/missing event -> static fallback and later valid event may still start;
8. retry/deactivate/dispose -> owned player stops.

Do not launch FFmpeg from these stage tests.

- [ ] **Step 2: Add the smallest stage scheduler state.**

Add only what the stage needs:

```text
IChartVideoPlayer player
nextVideoEventIndex
activeVideoEvent (or active event start time/path)
```

Reset this state for each performance activation.

During update, read `_songTimer.SongPositionMs` once and consume all due events. If multiple events became due, select only the last due one before starting/replacing the player so the stage never spins up obsolete FFmpeg processes after a hitch.

For the active event:

```text
mediaTimeMs = max(0, songTimeMs - event.TimeMs)
```

Pass that directly to the player.

Do not call `PlaybackModifiers.Speed` again; `SongTimer` already owns that transform.

- [ ] **Step 3: Keep lifecycle/failure behavior non-fatal.**

At a new event:

- stop/replace the previous video;
- if path is empty/missing, leave video inactive and continue gameplay;
- if player start/decode fails, log one diagnostic and continue;
- allow a later event to try again.

At performance retry, cancellation, deactivation, and disposal, stop/cancel the current player so FFmpeg cannot survive the stage.

Do not add a UI error/toast, config toggle, or retry queue.

- [ ] **Step 4: Draw video in the existing background layer.**

Keep the current static/fallback background draw first. If the player has a timely frame, draw it next using the player’s aspect-fit destination. Then let all existing lane/note/HUD/effect drawing continue unchanged.

Do not alter lane alpha or skin assets.

Both channel 54 and 5A use this same draw behavior.

- [ ] **Step 5: Run focused stage tests.**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore \
  --filter "FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~PerformanceRendererStateTests"
```

Expected: PASS.

- [ ] **Step 6: Run full platform test/build gates.**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
```

Windows CI:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --no-restore
```

Also run:

```bash
git diff --check
```

Expected: PASS / clean.

- [ ] **Step 7: Perform the representative Group C smoke on both supported platforms.**

Use the existing external corpus entry:

```text
DTXFiles.2/Test/mas.dtx
bg.avi
```

Verify manually or through the existing live-game automation surfaces where practical:

- AVI appears behind lanes/HUD;
- video starts at the chart-authored event;
- pause freezes frame and resume continues in alignment;
- default 100% Play Speed is aligned;
- 150% Play Speed advances against logical chart time, not 100% wall time;
- if decoding cannot stay current, fallback is static rather than a visibly stale independently-running movie;
- a chart with no video remains visually/functionally unchanged.

Record concise verification results in the PR description/comment. Do not commit the corpus or create a new permanent E2E harness solely for this feature.

---

## Final review checklist

Before marking the PR ready:

- [ ] No `AVIPAN`, MovieMode, video config, alpha, static BGA, embedded audio, generic media framework, or extra platform backend slipped into scope.
- [ ] Search confirms channels 54/5A use `ChartTimingMap` timing and no duplicate time calculator exists.
- [ ] Search confirms video update uses `SongTimer.SongPositionMs` and does not multiply Play Speed again.
- [ ] Mac FFmpeg builder still pins 7.0.2 and all prior audio capability tests pass.
- [ ] Video child process is terminated on retrigger and stage teardown.
- [ ] Missing/corrupt video never changes gameplay completion/result behavior.
- [ ] Full Mac + Windows test/build gates are green.
- [ ] Representative Group C smoke is recorded for Windows and Apple Silicon macOS.

## Estimate

- Task 1 parser/timing: ~0.5 day
- Task 2 FFmpeg player/runtime: ~1–1.5 days
- Task 3 stage wiring/verification: ~0.5–1 day

Target: **2–3 engineer days, one PR**.
