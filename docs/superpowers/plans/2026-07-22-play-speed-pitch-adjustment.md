# Playback Speed, Independent Pitch, and Speed-Scoped Scores Implementation Plan

**Status:** Implementation-ready after repository exploration, three review passes, and the contract freeze below. Begin with Phase A and do not cross a phase gate while its required verification is red.

**Goal:** Let the player choose gameplay speed from `0.50x` through `1.50x` in `0.05x` steps, choose pitch independently, and maintain a complete saved score aggregate for every chart/instrument/speed combination.

**Architecture:** Represent a play profile with integer-backed immutable modifiers, prepare pitch-preserving audio variants before gameplay, and drive chart logic from a rate-aware logical clock that is independent of audio-instance completion. A session-scoped prepared-audio set exclusively owns gameplay sounds; playback components borrow them and own only their active instances. Persist speed as part of the `SongScore` identity while keeping pitch as per-run metadata. The Result stage owns and observes score saving so a failed write is visible and retryable.

**Tech stack:** .NET 8, MonoGame 3.8, FFMpegCore with the bundled FFmpeg executable, EF Core SQLite, xUnit, and the existing JSON-RPC E2E harness.

---

## Execution Environment

Commands in this repository are prefixed with `rtk` because the repository imports `/Users/chanwaichan/.codex/RTK.md`, whose execution rule requires it. A contributor working outside that Codex environment can run the command after removing only the leading `rtk`.

---

## Plan Authority

This document is the accepted design and implementation plan for the feature. There is no separate design specification to reconcile. If implementation evidence requires a contract change, update this document and obtain review before proceeding past the current phase gate.

---

## Global Constraints

- Work only in the CX codebase. `DTXManiaNX/` is reference-only.
- Preserve existing `100% / 0 st` behavior and compatibility paths unless a task explicitly changes their contract.
- Keep database changes additive and data-preserving; never purge or rebuild a valid user database as a migration shortcut.
- Write or update the named focused tests before production behavior in each task, run them red where practical, implement the minimum behavior, then rerun the task verification.
- Stage only the exact task files after inspecting the diff. Do not use a broad repository-wide staging command.
- Keep new logic tests graphics-free when they belong in the Mac-safe test project.
- Treat WindowsDX and the Windows-targeted E2E project as CI/Windows completion gates, not as locally satisfied Mac checks.

---

## Product Decisions

| Decision | v1 behavior |
|---|---|
| Gameplay speed range | `50..150` percent, step `5`, default `100` |
| Gameplay speed display | Two decimal places, for example `0.50x`, `1.00x`, `1.50x` |
| Pitch unit | Semitones |
| Pitch range | `-12..+12` semitones, step `1`, default `0` |
| When modifiers take effect | Frozen when `PerformanceStage` activates; config edits affect the next run |
| Scroll speed | Remains independent and unchanged |
| Score identity | `(ChartId, Instrument, PlaySpeedPercent)` |
| Pitch and score identity | Pitch does **not** create another score bucket |
| Pitch history | Stored as structured metadata for each new CX history row |
| Existing CX scores | Migrated to `1.00x` |
| NX-imported scores | Treated as legacy aggregate data at `1.00x`; do not invent unavailable speed partitions |
| Empty speed buckets | Created lazily on first saved play; do not create 21 rows per chart |
| Modified-audio failure | Fail visibly before play; never silently play the wrong profile and save into the requested speed bucket |
| Prepared-audio format | Versioned header plus raw signed 16-bit little-endian PCM; no WAV container |
| Active-session PCM budget | Hard cap of `512 MiB`, checked before constructing `SoundEffect` objects |
| Preparation feedback | Show `Preparing audio n/N`, cache hits, and cancellation while non-default audio is prepared |
| Receipt retention | Retain score-save receipts across score/history cleanup so an old `RunId` never becomes writable again; an explicit whole-database purge resets this guarantee |

The pitch range was not specified in the original request. `-12..+12` semitones is the implementation assumption for v1 and must live behind one range helper so it can be changed without touching persistence or audio-routing code.

---

## Non-Goals

- Changing speed or pitch after a run has started.
- Replacing or reinterpreting the existing visual `Scroll Speed` setting.
- Splitting scores by pitch.
- Reconstructing historical NX speed variants that NX did not persist in a recoverable form.
- Adding online leaderboards or changing any future leaderboard contract.
- Modifying the reference-only `DTXManiaNX/` tree.
- Redefining the chart's judgement windows. Existing windows remain logical-song-time values.

---

## Critical Invariants

1. All gameplay systems consume the same logical song time: notes, BGM events, autoplay, misses, progress, completion, lane-hit telemetry, and judgement timestamps.
2. A `100% / 0 st` run follows the existing direct audio path and timing behavior as closely as possible.
3. All audio roles use the same frozen modifiers: background music, scheduled BGM, and playable/autoplay chip sounds.
4. A non-default run cannot begin until all existing referenced audio has a valid prepared variant.
5. A score write is atomic across the score aggregate, play count, clear count, idempotency marker, and five-row history update.
6. Every score read that affects gameplay or UI specifies a play speed explicitly.
7. Pitch changes the audible pitch without changing the requested final gameplay duration.
8. The Result stage cannot report a save as successful until the database write and matching in-memory refresh have completed.
9. The active prepared-audio set cannot exceed the v1 `512 MiB` decoded-PCM budget.
10. Published in-memory score variants are immutable snapshots; readers never observe a dictionary or `SongScore` being mutated in place.
11. `PreparedGameplayAudioSet` is the sole owner of session `ISound` objects; `ChipSoundCache` and background/BGM players borrow sounds and own only instances they create.
12. A score receipt survives deletion of its related `SongScore` and retains enough identity columns to validate a repeated `RunId`.

---

## Frozen API Contracts

These are the minimum target shapes. Names and parameter order are fixed for the implementation unless a compile-time constraint discovered in the relevant phase requires this plan to be amended.

### Modifiers and logical clock

```csharp
public readonly record struct PlaybackModifiers(
    int PlaySpeedPercent,
    int PitchSemitones);

public sealed class PlaybackClock
{
    public PlaybackClock(int playSpeedPercent);
    public bool IsRunning { get; }
    public bool IsPaused { get; }
    public double GetLogicalTimeMs(GameTime gameTime);
    public void Start(GameTime gameTime, double logicalPositionMs = 0);
    public void Pause(GameTime gameTime);
    public void Resume(GameTime gameTime);
    public void Stop();
    public void SetLogicalPosition(double logicalPositionMs, GameTime gameTime);
}
```

`SongTimer.IsPlaying` means the logical clock is running. `SongTimer.IsPaused` means it has a cached resumable logical position. Neither property inspects `SoundEffectInstance.State`. Remove `SongTimer.IsFinished`; chart progression owns completion, and the current property has no production caller. `SongTimer.GetCurrentMs(GameTime)` returns the cached logical position while paused and `0` only before start or after an explicit stop/reset.

### Audio preparation and ownership

```csharp
Task<PreparedAudioArtifact> PrepareAsync(
    string sourcePath,
    PlaybackModifiers modifiers,
    CancellationToken cancellationToken);

public sealed class PreparedGameplayAudioSet : IDisposable
{
    public ISound? MainBackground { get; }
    public IReadOnlyDictionary<string, ISound> ScheduledBgmBySourcePath { get; }
    public IReadOnlyDictionary<string, ISound> ChipSoundsByWavId { get; }
    public float RuntimePitch { get; }
    public long DecodedPcmBytes { get; }
}

ChipSoundCache(
    IReadOnlyDictionary<string, ISound> borrowedSoundsByWavId);

void Play(string wavId);
void Play(string wavId, float volume, float pitch, float pan);
```

`PreparedGameplayAudioSet` exposes the prepared main background sound, scheduled-BGM lookup, chip-sound lookup by WAV ID, runtime pitch, and total decoded PCM bytes. It is the sole disposer of those `ISound` objects. `ChipSoundCache` no longer loads or disposes sounds: it borrows the WAV-ID map and disposes only active `SoundEffectInstance` objects. On stage teardown, stop and dispose `ChipSoundCache` and other borrowed playback instances before disposing `PreparedGameplayAudioSet`.

The default profile still goes through `PreparedGameplayAudioSet`, but its entries wrap the existing direct/original-path loading result and do not create generated cache artifacts.

### Score persistence and reads

```csharp
Task<ScoreSaveResult> UpdateScoreAsync(
    int chartId,
    EInstrumentPart instrument,
    PerformanceSummary summary,
    CancellationToken cancellationToken = default);

Task<SongScore?> GetScoreWithHistoryAsync(
    int chartId,
    EInstrumentPart instrument,
    int playSpeedPercent,
    CancellationToken cancellationToken = default);

SongScore? GetScore(int difficultyIndex, int playSpeedPercent);
```

The summary carries immutable `RunId`, `PlaySpeedPercent`, and `PitchSemitones`. Gameplay write APIs never reread current config. The one-argument `SongListNode.GetScore(int difficultyIndex)` remains only as an explicit `100%` compatibility shim for legacy/NX callers.

`ScoreSaveReceipt` stores `RunId`, `ChartId`, `Instrument`, `PlaySpeedPercent`, nullable `SongScoreId`, and `SavedAtUtc`. Its optional score relationship uses `ON DELETE SET NULL`; `ChartId` is stored identity rather than a required foreign key. A repeated `RunId` is `AlreadySaved` only when all stored identity fields match the submitted run.

---

## Phase Gates

| Phase | Tasks | Gate before continuing |
|---|---|---|
| A: configuration and clock | 1–2 | Focused config/clock tests and the Mac build pass; default clock behavior remains compatible |
| B: audio and gameplay timing | 3–5 | Audio tests pass, ownership tests prove single disposal, cold/warm benchmarks meet budget, and the manual DesktopGL timing smoke passes |
| C: persistence and score consumers | 6–10 | Fresh/legacy/partial migration tests, persistence/read/UI tests, and full Mac suite pass with no cross-speed leakage |
| D: presentation and proof | 11–12 | Cross-platform support tests pass; WindowsDX smoke and both Windows-only E2E categories pass in CI or on a Windows machine |

Each phase should end in a reviewable green commit series. If a gate fails because a frozen contract is infeasible, stop at that phase and amend the plan rather than pushing the uncertainty downstream.

---

## Timing and Audio Model

### Logical clock

Let:

```text
s = PlaySpeedPercent / 100.0
logicalElapsedMs = realElapsedMs * s
```

The chart and judgement systems continue to use their existing millisecond values against `logicalElapsedMs`.

Consequences:

- At `1.50x`, a 100 ms logical judgement window lasts about 66.7 ms in real time.
- At `0.50x`, the same logical window lasts 200 ms in real time.
- `AudioLatencyOffsetMs` remains a real-world device measurement and converts to logical time before judgement:

```text
judgementTimeMs = logicalSongTimeMs - AudioLatencyOffsetMs * s
```

- Pause/resume preserves logical position.
- The ready countdown and post-chart result delay remain real-time UX durations.
- Audio-instance `Stopped` state must not stop the logical clock; chart completion owns run completion.
- While paused, the clock returns its cached logical position rather than snapping to `0.0`.

### Independent tempo and pitch

MonoGame's `SoundEffectInstance.Pitch` changes both frequency and playback rate. Use FFmpeg tempo preparation to cancel the unwanted rate component:

```text
pitchFactor = 2 ^ (PitchSemitones / 12)
ffmpegTempoFactor = s / pitchFactor
monoGamePitch = PitchSemitones / 12
```

FFmpeg prepares audio with `atempo=ffmpegTempoFactor`, which preserves pitch. MonoGame then applies `monoGamePitch`. The combined result is:

```text
final tempo = ffmpegTempoFactor * pitchFactor = s
final pitch = requested PitchSemitones
```

Build an `atempo` chain whose individual factors remain in `0.5..2.0`. The full v1 modifier range can require total preparation factors from `0.25` through `3.0`.

At `0.50x / +12 st`, the prepared buffer can be four times the source PCM size before MonoGame's runtime pitch restores the requested final duration. Memory preflight must therefore use the prepared PCM byte count, not source-file size or final playback duration.

Examples:

| Profile | Pitch factor | FFmpeg tempo | MonoGame pitch | Final tempo |
|---|---:|---:|---:|---:|
| `0.75x`, `0 st` | 1.000 | 0.750 | 0.000 | 0.750 |
| `1.00x`, `+12 st` | 2.000 | 0.500 | 1.000 | 1.000 |
| `1.50x`, `-12 st` | 0.500 | 3.000 | -1.000 | 1.500 |

Applying the profile to short percussion chips is deliberately consistent but may make tails sound stretched at slow speeds. Note onsets would remain synchronized even if natural chip durations were retained, so this is a product choice rather than a timing necessity. v1 chooses consistent application to every audio role unless a later product decision explicitly relaxes it.

---

## Planned File Structure

### New production files

- `DTXMania.Game/Lib/Config/PlaySpeedRange.cs`
- `DTXMania.Game/Lib/Config/PitchRange.cs`
- `DTXMania.Game/Lib/Stage/Performance/PlaybackModifiers.cs`
- `DTXMania.Game/Lib/Stage/Performance/PlaybackClock.cs`
- `DTXMania.Game/Lib/Resources/AudioVariantKey.cs`
- `DTXMania.Game/Lib/Resources/FfmpegRuntime.cs`
- `DTXMania.Game/Lib/Resources/IAudioVariantProcessor.cs`
- `DTXMania.Game/Lib/Resources/FfmpegAudioVariantProcessor.cs`
- `DTXMania.Game/Lib/Resources/PreparedAudioArtifact.cs`
- `DTXMania.Game/Lib/Resources/PlaybackAudioVariantCache.cs`
- `DTXMania.Game/Lib/Stage/Performance/AudioPreparationProgress.cs`
- `DTXMania.Game/Lib/Stage/Performance/PreparedGameplayAudioSet.cs`
- `DTXMania.Game/Lib/Song/ScoreVariantKey.cs`
- `DTXMania.Game/Lib/Song/Entities/ScoreSaveResult.cs`
- `DTXMania.Game/Lib/Song/Entities/ScoreSaveReceipt.cs`

### New test files

- `DTXMania.Test/Config/PlaySpeedAndPitchConfigTests.cs`
- `DTXMania.Test/Stage/Performance/PlaybackModifiersTests.cs`
- `DTXMania.Test/Stage/Performance/PlaybackClockTests.cs`
- `DTXMania.Test/Resources/FfmpegRuntimeTests.cs`
- `DTXMania.Test/Resources/FfmpegAudioVariantProcessorTests.cs`
- `DTXMania.Test/Resources/PreparedAudioArtifactTests.cs`
- `DTXMania.Test/Resources/PlaybackAudioVariantCacheTests.cs`
- `DTXMania.Test/Song/PlaybackSpeedScoreMigrationTests.cs`
- `DTXMania.Test/Song/PlaybackSpeedScorePersistenceTests.cs`
- `DTXMania.Test/Song/SongListNodeScoreVariantTests.cs`
- `DTXMania.Test/Song/SongStatusPanelSpeedLogicTests.cs`
- `DTXMania.Test/Song/SongBarRendererSpeedLogicTests.cs`
- `DTXMania.Test/Stage/Result/ResultStageScoreSaveTests.cs`

The Mac test project already includes `**/*.cs`, so new C# test files do not require a project-file edit. Add a test-data copy entry only if implementation chooses checked-in binary audio fixtures instead of generated temporary fixtures.

---

## Task 1: Add Canonical Modifier Types and Persisted Config

**Files:**

- Create `DTXMania.Game/Lib/Config/PlaySpeedRange.cs`
- Create `DTXMania.Game/Lib/Config/PitchRange.cs`
- Create `DTXMania.Game/Lib/Stage/Performance/PlaybackModifiers.cs`
- Modify `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify the `IConfigManager` stub in `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`
- Test `DTXMania.Test/Config/PlaySpeedAndPitchConfigTests.cs`
- Test `DTXMania.Test/Stage/Performance/PlaybackModifiersTests.cs`

- [ ] Add `PlaySpeedRange` with `Min = 50`, `Max = 150`, `Step = 5`, `Default = 100`, `SnapAndClamp`, and invariant `Format`.
- [ ] Add `PitchRange` with `Min = -12`, `Max = 12`, `Step = 1`, `Default = 0`, `SnapAndClamp`, and invariant `Format`.
- [ ] Add immutable `PlaybackModifiers` carrying integer `PlaySpeedPercent` and `PitchSemitones`.
- [ ] Give `PlaybackModifiers` derived `Speed`, `PitchFactor`, `FfmpegTempoFactor`, and `MonoGamePitch` values.
- [ ] Add `PlaySpeedPercent` and `PitchSemitones` to `ConfigData` with default values.
- [ ] Parse both settings from `Config.ini`, snap invalid step values, clamp out-of-range values, and save canonical integer values.
- [ ] Add persist-on-edit setters to `IConfigManager` and `ConfigManager`, following the existing deferred-save pattern.
- [ ] Add `Play Speed` and `Pitch` integer config items in the gameplay/Drums category next to `Scroll Speed`.
- [ ] Keep Page Up/Page Down gameplay controls assigned only to visual scroll speed.
- [ ] Add the new interface members to all test doubles.
- [ ] Leave FFmpeg availability behavior out of this task. Task 3 deliberately revisits the same `ConfigStage` controls and layers availability/warning state onto this foundation.

Tests must cover:

- Defaults, min/max, step snapping, and invariant display.
- Config save/load round-trip.
- Malformed and out-of-range config input.
- No deferred write for unchanged values.
- All derived audio factors at the range boundaries.
- Exact default bypass detection for `100% / 0 st`.

Verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PlaySpeedAndPitchConfigTests|FullyQualifiedName~PlaybackModifiersTests"
```

Suggested commit: `feat: add playback speed and pitch configuration`

Before committing any task, stage only the exact files listed for that task and confirm the staged diff contains no unrelated user changes.

---

## Task 2: Introduce a Pure Rate-Aware Playback Clock

**Files:**

- Create `DTXMania.Game/Lib/Stage/Performance/PlaybackClock.cs`
- Modify `DTXMania.Game/Lib/Stage/Performance/SongTimer.cs`
- Test `DTXMania.Test/Stage/Performance/PlaybackClockTests.cs`
- Update `DTXMania.Test/Stage/Performance/SongTimerStateTests.cs`

- [ ] Implement `PlaybackClock` without MonoGame audio dependencies.
- [ ] Support `Start`, `Pause`, `Resume`, `Stop`, logical-position setting, and `GetLogicalTimeMs`.
- [ ] Keep speed immutable for the lifetime of the clock.
- [ ] Make pause/resume use the same `GameTime` source used by normal updates.
- [ ] Return the cached logical position while paused; never reuse the current `SongTimer` behavior that returns `0.0` whenever `IsPlaying` is false.
- [ ] Compose `PlaybackClock` inside `SongTimer` so existing stage code can migrate incrementally.
- [ ] Remove the rule that stops timing when the wrapped background `SoundEffectInstance` reports `Stopped`.
- [ ] Define `SongTimer.IsPlaying` as `PlaybackClock.IsRunning` and add `SongTimer.IsPaused` as the logical paused state; neither may read `SoundEffectInstance.State`.
- [ ] Remove `SongTimer.IsFinished` and update its tests because chart progression, not background-audio completion, owns the finished condition.
- [ ] Add a clamped `SongTimer.Pitch` pass-through beside the existing `Volume`, `Pan`, and `IsLooped` properties so the background instance is configured before `Play()`.
- [ ] Preserve a default-speed constructor or factory for existing callers and tests.
- [ ] Keep audio transport controls in `SongTimer`, but make chart timing come only from `PlaybackClock`.
- [ ] Audit the existing `IsPlaying` guards in `PerformanceStage` and `AudioLoader`; each remaining guard must intentionally mean logical-clock running rather than audio-instance playing.

Tests must cover:

- `1000` real ms produces `500`, `1000`, and `1500` logical ms at the three representative speeds.
- Pause freezes logical time, paused reads return that position, and resume does not jump.
- Pitch is assigned to the wrapped instance before playback and respects the configured range.
- Stop resets the exposed playing state.
- `IsPlaying` remains true when a short audio instance stops but the logical clock is running; `IsPaused` distinguishes a paused resumable position.
- A stopped/short background sound does not stop logical chart time.
- Position changes are interpreted as logical milliseconds.
- Negative or non-monotonic `GameTime` input cannot move logical time backward.

Verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PlaybackClockTests|FullyQualifiedName~SongTimerStateTests"
```

Suggested commit: `feat: add rate-aware gameplay clock`

---

## Task 3: Build the Audio Variant Processor and Bounded Cache

**Files:**

- Create `DTXMania.Game/Lib/Resources/AudioVariantKey.cs`
- Create `DTXMania.Game/Lib/Resources/FfmpegRuntime.cs`
- Create `DTXMania.Game/Lib/Resources/IAudioVariantProcessor.cs`
- Create `DTXMania.Game/Lib/Resources/FfmpegAudioVariantProcessor.cs`
- Create `DTXMania.Game/Lib/Resources/PreparedAudioArtifact.cs`
- Create `DTXMania.Game/Lib/Resources/PlaybackAudioVariantCache.cs`
- Modify `DTXMania.Game/Lib/Resources/ManagedSound.cs`
- Reuse `DTXMania.Game/Lib/Resources/XaDecoder.cs`
- Modify `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- Modify `DTXMania.Game/Lib/Utilities/AppPaths.cs` to expose the dedicated cache root
- Test `DTXMania.Test/Resources/FfmpegRuntimeTests.cs`
- Test `DTXMania.Test/Resources/FfmpegAudioVariantProcessorTests.cs`
- Test `DTXMania.Test/Resources/PreparedAudioArtifactTests.cs`
- Test `DTXMania.Test/Resources/PlaybackAudioVariantCacheTests.cs`
- Update `DTXMania.Test/Resources/ManagedSoundFFmpegPathTests.cs`

### Shared FFmpeg runtime

FFmpeg discovery currently runs only from `ManagedSound`'s lazy static constructor. The new processor must not depend on `ManagedSound` happening to initialize first.

- [ ] Move bundled-binary candidate discovery and `GlobalFFOptions.Configure` into a shared, thread-safe, idempotent `FfmpegRuntime.EnsureConfigured()`.
- [ ] Call `EnsureConfigured()` from both `ManagedSound` before MP3 probe/decode and `FfmpegAudioVariantProcessor` before any FFmpeg operation.
- [ ] Expose a non-throwing availability probe with a diagnostic reason.
- [ ] In ConfigStage, show an `FFmpeg unavailable` warning for non-default speed/pitch and prevent selecting a non-default value through the UI while unavailable.
- [ ] Extend the `ConfigStage` controls introduced in Task 1 instead of replacing or duplicating them.
- [ ] Keep PerformanceStage validation authoritative so manually edited config still fails closed.

### Processor contract

- Input: an existing source path, immutable `PlaybackModifiers`, and a cancellation token.
- Output: a validated `PreparedAudioArtifact` for a non-default profile. `PreparedGameplayAudioSet` owns the exact-default bypass and loads the original path without invoking the processor.
- Errors: typed preparation failures with the source path and requested profile; no silent fallback.

- [ ] Deduplicate equivalent in-flight requests so the same source/profile is transformed once.
- [ ] Use FFMpegCore argument APIs rather than shell command strings.
- [ ] Normalize output to MonoGame-compatible raw signed 16-bit little-endian PCM, preserving a supported sample rate and mono/stereo channel count.
- [ ] Let FFmpeg read WAV, MP3, and OGG inputs.
- [ ] For XA, pass `XaDecoder` output to FFmpeg with explicit raw-input arguments: `-f s16le -ar <decoded sample rate> -ac <decoded channel count>`.
- [ ] Build an `atempo` chain with individual factors in `0.5..2.0`.
- [ ] Give each FFmpeg operation a `60 second` per-source timeout in addition to stage cancellation.
- [ ] Write to a unique temporary raw file, validate alignment/non-empty output, package the final artifact, then atomically move it into the cache.
- [ ] Delete incomplete temporary files on cancellation or failure.
- [ ] Limit transform concurrency to two jobs to avoid saturating startup.
- [ ] Bypass FFmpeg and the generated-file cache for exactly `100% / 0 st`.

### Prepared artifact contract

Use one atomic, versioned binary artifact rather than a WAV plus sidecar:

```text
magic
artifact version
sample rate
channel count
PCM byte length
raw s16le PCM payload
```

- [ ] Reject an invalid magic/version, unsupported sample rate/channel count, odd PCM byte length, declared-length mismatch, or truncated payload.
- [ ] Keep sample rate and channel metadata in the header and include decoder/pipeline identity in the cache key.
- [ ] Load the payload with `new SoundEffect(pcm, sampleRate, channels)` and wrap that instance in `ManagedSound`; never route prepared artifacts through `SoundEffect.FromStream`.

### Cache contract

Use a versioned key containing:

- A SHA-256 source-content fingerprint.
- Canonical source extension/decoder identity.
- `PlaySpeedPercent`.
- `PitchSemitones`.
- A pipeline-version constant.

Store generated files below a dedicated application-cache directory, never alongside chart files. Use last-access time for pruning and enforce a `1 GiB` size cap after a successful write and during startup cleanup. Cache cleanup is best-effort; preparation correctness must not depend on cleanup succeeding.

Tests must cover:

- Factor-chain construction for `0.25`, `0.50`, `1.00`, `2.00`, and `3.00`.
- Default-profile bypass.
- Generated-tone output duration and preservation of pre-runtime pitch.
- Runtime-pitch mapping from semitones to MonoGame's `-1..1` range.
- WAV, MP3, OGG, and synthetic XA normalization paths, including XA raw-input arguments.
- Prepared-artifact header round-trip and corruption rejection.
- Cache hit, source-content invalidation, pipeline-version invalidation, and LRU pruning.
- Cancellation, concurrent duplicate requests, missing FFmpeg, corrupt input, and empty output.
- Shared FFmpeg initialization works when the processor is the first audio-related type touched.
- No partial cache artifact after failure.

Verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~FfmpegRuntimeTests|FullyQualifiedName~FfmpegAudioVariantProcessorTests|FullyQualifiedName~PreparedAudioArtifactTests|FullyQualifiedName~PlaybackAudioVariantCacheTests|FullyQualifiedName~ManagedSoundFFmpegPathTests"
```

FFmpeg-spawning tests must use `[Trait("Category", "Audio")]`; pure factor/cache-key tests remain normal unit tests.

Suggested commit: `feat: prepare independent tempo and pitch audio variants`

---

## Task 4: Route Every Gameplay Audio Role Through the Frozen Profile

**Files:**

- Create `DTXMania.Game/Lib/Stage/Performance/AudioPreparationProgress.cs`
- Create `DTXMania.Game/Lib/Stage/Performance/PreparedGameplayAudioSet.cs`
- Modify `DTXMania.Game/Lib/Stage/Performance/AudioLoader.cs`
- Modify `DTXMania.Game/Lib/Stage/Performance/ChipSoundCache.cs`
- Modify `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`
- Update `DTXMania.Test/Stage/Performance/AudioLoaderTests.cs`
- Update `DTXMania.Test/Stage/Performance/ChipSoundCacheTests.cs`
- Update `DTXMania.Test/Stage/Performance/ChipSoundCacheAdditionalTests.cs`
- Update `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

- [ ] Capture `PlaybackModifiers` once at the beginning of `PerformanceStage.OnActivate`.
- [ ] Keep `SongTransitionStage` presentation-only in v1: it displays the profile that will be prepared, while `PerformanceStage` owns the actual preparation task, progress, cancellation, and prepared-set lifetime.
- [ ] Store the initialization `Task` and a stage-owned `CancellationTokenSource`; cancel and observe it on deactivation.
- [ ] Inject the audio-variant processor/cache through testable seams rather than static calls.
- [ ] Enumerate and deduplicate every existing referenced audio source before marking gameplay ready.
- [ ] Prepare the background track, scheduled BGM files, and chip sounds with the same modifiers.
- [ ] Report `AudioPreparationProgress` with completed count, total count, current role, cache-hit count, elapsed time, and decoded-byte estimate.
- [ ] Render `Preparing audio n/N` on the Performance loading screen after `250 ms`; keep the stage cancellable while progress is visible.
- [ ] Sum prepared artifact payload lengths before constructing any `SoundEffect`; abort visibly if the active set would exceed `512 MiB`.
- [ ] Let `PreparedGameplayAudioSet` exclusively own one strict `ISound` per deduplicated source and its exact decoded-byte count, then dispose the session set as one unit.
- [ ] Give `PreparedGameplayAudioSet` explicit main-background, scheduled-BGM-by-source, and chip-by-WAV-ID views over those owned sounds so callers never reload by path.
- [ ] Construct non-default sounds with `new SoundEffect(pcm, sampleRate, channels)` wrapped by `ManagedSound(SoundEffect, sourcePath)`; do not pass prepared artifacts through `ResourceManager.LoadSound`, whose current contract may return a fallback sound after a decode failure.
- [ ] Set `SoundEffectInstance.Pitch` before `Play()` for every prepared sound instance.
- [ ] Replace `ChipSoundCache` loading ownership with the frozen borrowed-map constructor; the prepared set has already loaded every valid chip sound before the cache is constructed.
- [ ] Remove the gameplay path's call to `ChipSoundCache.PreloadAsync`; do not retain a second owner for prepared or default sounds.
- [ ] Change `ChipSoundCache.Play` to accept runtime pitch. Its common `sound.Play()` fast path is allowed only when volume, pan, **and pitch** are all default; otherwise call `sound.Play(volume, pitch, pan)`.
- [ ] Make `ChipSoundCache.Dispose()` stop/dispose active instances without disposing borrowed `ISound` objects.
- [ ] Ensure `TriggerBGMEvent` uses the prepared mapping instead of loading the original path.
- [ ] Keep missing chart references on their current warning/silence path, but abort startup if an existing referenced source cannot be transformed for a non-default profile.
- [ ] Surface a concise load error and return path to Song Select.
- [ ] Do not create a `PerformanceSummary` or allow score persistence for a run that never became ready.
- [ ] On deactivation, first stop/dispose chip, background, and scheduled-BGM instances; then dispose `PreparedGameplayAudioSet`. Do not delete disk-cache entries.

### Preparation performance gate

Before considering this task complete, record cold- and warm-cache timings for:

- One five-minute stereo background track.
- A representative chart with roughly 32 unique chips.
- A dense chart or generated fixture with at least 128 unique chips.
- Profiles `0.50x / +12 st`, `0.75x / 0 st`, and `1.50x / -12 st`.

The v1 acceptance budget is:

- Progress becomes visible within `250 ms`.
- The dense cold-cache case completes within `30 seconds` on the development Mac.
- The same warm-cache case completes within `2 seconds`.
- The active prepared set remains at or below `512 MiB`.

If the cold-cache budget fails, stop before shipping and measure batching, earlier transition-stage preparation, or an explicitly approved BGM-role-only policy. Do not silently relax the all-audio invariant.

Tests must prove:

- All three audio roles receive the same profile and runtime pitch.
- `PreparedGameplayAudioSet` disposes each owned `ISound` exactly once, while disposing `ChipSoundCache` never disposes a borrowed sound.
- Non-zero pitch never takes `ChipSoundCache`'s zero-pitch fast path.
- The stage does not become ready before preparation completes.
- Progress counts cache hits and completed transforms accurately.
- The PCM preflight rejects an over-budget set before allocating `SoundEffect` objects.
- Preparation cancellation cannot publish late sounds into a deactivated stage.
- A transform failure prevents gameplay and score creation.
- Default-profile loading continues to use original paths.
- A short background track does not end a longer chart.

Verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~AudioLoaderTests|FullyQualifiedName~ChipSoundCache|FullyQualifiedName~PerformanceStageDeterministicTests"
```

Suggested commit: `feat: apply playback modifiers to all gameplay audio`

---

## Task 5: Move Gameplay Timing to the Logical Clock

**Files:**

- Modify `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify `DTXMania.Game/Lib/Stage/Performance/PerformanceSummary.cs`
- Update `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Update `DTXMania.Test/Stage/Performance/PerformanceStageJudgementIntegrationTests.cs`
- Update `DTXMania.Test/Stage/Performance/TimingVerificationTest.cs`
- Update `DTXMania.Test/Stage/Performance/PerformanceSummaryTests.cs`

- [ ] Replace raw elapsed-time reads with the rate-aware `SongTimer`/`PlaybackClock` value.
- [ ] Use logical time for note position, autoplay, BGM events, miss processing, progress, chart-end detection, and lane-hit timestamps.
- [ ] Revisit every `PerformanceStage` branch guarded by `SongTimer.IsPlaying`; preserve it only when “logical gameplay clock is running” is the intended condition.
- [ ] Convert `AudioLatencyOffsetMs` from real milliseconds to logical milliseconds before judgement.
- [ ] Keep the ready countdown and post-chart completion buffer on real `GameTime`.
- [ ] Freeze the play profile into `PerformanceSummary`.
- [ ] Generate a new immutable `RunId` when the run summary is finalized.
- [ ] Ensure early failure/abort summaries cannot be mistaken for savable completed runs.
- [ ] Expose logical song time, frozen speed, and frozen pitch through performance telemetry.

Tests must cover:

- Judgement-time latency conversion at `0.50x`, `1.00x`, and `1.50x`.
- BGM/autoplay trigger order at representative speeds.
- Note progress and chart completion use logical time.
- Ready and result delays remain the same real duration at all speeds.
- Summary copies the frozen profile rather than rereading mutable config.
- Telemetry reports the frozen profile during the run.

Verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceStage|FullyQualifiedName~PerformanceSummary"
```

Suggested commit: `feat: drive gameplay from logical playback time`

---

## Task 6: Migrate the Database to Speed-Scoped Scores

**Files:**

- Create `DTXMania.Game/Lib/Song/ScoreVariantKey.cs`
- Modify `DTXMania.Game/Lib/Song/Entities/SongScore.cs`
- Modify `DTXMania.Game/Lib/Song/Entities/PerformanceHistory.cs`
- Create `DTXMania.Game/Lib/Song/Entities/ScoreSaveReceipt.cs`
- Modify `DTXMania.Game/Lib/Song/Entities/SongDbContext.cs`
- Modify `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Test `DTXMania.Test/Song/PlaybackSpeedScoreMigrationTests.cs`
- Update `DTXMania.Test/Song/SongDatabaseServicePerformanceHistoryMigrationTests.cs`

### Entity changes

Add:

```text
SongScore.PlaySpeedPercent  INTEGER NOT NULL DEFAULT 100
PerformanceHistory.PitchSemitones INTEGER NOT NULL DEFAULT 0
ScoreSaveReceipts.RunId     TEXT PRIMARY KEY
ScoreSaveReceipts.ChartId   INTEGER NOT NULL
ScoreSaveReceipts.Instrument INTEGER NOT NULL
ScoreSaveReceipts.PlaySpeedPercent INTEGER NOT NULL
ScoreSaveReceipts.SongScoreId INTEGER NULL
ScoreSaveReceipts.SavedAtUtc TEXT NOT NULL
```

Update `SongScore.Clone()` for `PlaySpeedPercent`.

Change the unique score index:

```text
old: (ChartId, Instrument)
new: (ChartId, Instrument, PlaySpeedPercent)
```

`ScoreSaveReceipts.RunId` is the durable idempotency key. The receipt stores chart/instrument/speed identity directly and has an optional `SongScores.Id` relationship with `ON DELETE SET NULL`. It therefore survives the repository's stale-score cleanup and can still validate a repeated `RunId` after the score row disappears. Do not add a required `SongChart` foreign key to `ChartId`, because stale-chart cleanup must not delete the receipt.

Receipt growth is intentionally unbounded in v1: each completed run adds one small local SQLite row. Do not implement a “last N per chart” cleanup because that breaks the durable idempotency contract. An explicit whole-database purge intentionally resets receipts along with all other local score data. Any future selective retention policy must define an idempotency horizon or archive old keys in another durable form.

### EF Core mapping

- [ ] Add `DbSet<ScoreSaveReceipt> ScoreSaveReceipts` to `SongDbContext`.
- [ ] Configure `RunId` as the primary key.
- [ ] Map the stored `ChartId`, `Instrument`, and `PlaySpeedPercent` identity fields as required values without required chart navigation.
- [ ] Configure the optional `ScoreSaveReceipt.SongScoreId -> SongScore.Id` relationship with `DeleteBehavior.SetNull`.
- [ ] Add a non-unique lookup index on `SongScoreId`.
- [ ] Add the matching navigation collection on `SongScore` only if it is needed by the persistence flow; do not eagerly load receipts in normal song queries.
- [ ] Keep the EF model and hand-written additive migration structurally identical.

### Additive migration

- [ ] Run the migration from the existing database-initialization upgrade hook after `EnsureCreatedAsync`, following `EnsureNxImportColumnsAsync` and `EnsurePerformanceHistoryScoreScopeAsync` rather than introducing a separate migration framework.
- [ ] Add missing columns with the defaults above.
- [ ] Drop `IX_SongScores_ChartId_Instrument` only when present.
- [ ] Create `IX_SongScores_ChartId_Instrument_PlaySpeedPercent` only when absent or incorrect.
- [ ] Create `ScoreSaveReceipts`, its optional `SongScoreId` relationship, and its lookup index when absent.
- [ ] Inspect `pragma_table_info` and `pragma_index_list` so fresh, legacy, and partially migrated databases converge on the same schema.
- [ ] Execute the column/index transition transactionally where SQLite permits it.
- [ ] Preserve every `SongScore.Id`; existing `PerformanceHistory.SongScoreId` foreign keys remain valid.
- [ ] Keep chart-scan precreation limited to the default `100` row; create non-default rows only when a completed run is saved.
- [ ] Fail with a diagnostic instead of deleting data if an unexpected duplicate would prevent new-index creation.
- [ ] Do not rebuild or purge a valid user database.

Tests must cover:

- A pre-feature database migrates all existing rows to `100`.
- Existing score IDs and performance-history foreign keys survive.
- Fresh database creation has the new columns and indexes.
- Fresh EF creation exposes the receipt `DbSet`, primary key, stored identity fields, nullable foreign key with set-null behavior, and lookup index.
- A partially migrated database converges safely.
- Running initialization twice is idempotent.
- Two rows for one chart/instrument are allowed at different speeds.
- Duplicate rows at the same speed are rejected.
- Duplicate receipt run IDs are rejected, including after more than five later history rows.
- Deleting a related `SongScore` sets `SongScoreId` to null without deleting the receipt, and a repeated matching `RunId` remains identifiable.
- Explicit whole-database purge removes receipts as part of deleting the entire local database.

Verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PlaybackSpeedScoreMigrationTests|FullyQualifiedName~SongDatabaseServicePerformanceHistoryMigrationTests"
```

Suggested commit: `feat: scope persisted scores by play speed`

---

## Task 7: Make Score Saving Explicit, Atomic, Idempotent, and Observable

**Files:**

- Create `DTXMania.Game/Lib/Song/Entities/ScoreSaveResult.cs`
- Modify `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Modify `DTXMania.Game/Lib/Song/PerformanceHistoryMerger.cs`
- Modify `DTXMania.Game/Lib/Song/SongManager.cs`
- Modify `DTXMania.Game/Lib/Stage/ResultStage.cs`
- Test `DTXMania.Test/Song/PlaybackSpeedScorePersistenceTests.cs`
- Update `DTXMania.Test/Song/SongManagerUpdateScoreTests.cs`
- Test `DTXMania.Test/Stage/Result/ResultStageScoreSaveTests.cs`

- [ ] Make all gameplay score-write APIs require `PlaySpeedPercent`; do not infer it from current config.
- [ ] Query or lazily create the row by `(ChartId, Instrument, PlaySpeedPercent)`.
- [ ] Keep the entire aggregate speed-scoped: best score, best judgement counts, best achievement, high skill, last score, last skill, play/clear counts, full combo, max combo, and five-row history.
- [ ] Store `PerformanceSummary.PitchSemitones` on the new CX history row.
- [ ] Include speed and pitch in the human-readable history line while retaining structured columns as the source of truth.
- [ ] Check `ScoreSaveReceipts.RunId` inside the transaction before incrementing anything.
- [ ] Return `AlreadySaved` for a repeated `RunId`, treating it as success without a second aggregate mutation.
- [ ] Insert the `ScoreSaveReceipt` with chart/instrument/speed identity and the resulting `SongScoreId` in the same transaction as score and history updates.
- [ ] If an existing receipt stores a different chart/instrument/speed from the submitted summary, return a collision failure rather than treating it as a successful retry; this check must still work when `SongScoreId` has been nulled by stale-score cleanup.
- [ ] Replace `SongManager.UpdateScoreAsync`'s swallowed-error boolean contract with a result that distinguishes `Saved`, `AlreadySaved`, and `Failed`.
- [ ] Keep exceptions observable in tests and preserve a user-facing error message at the Result-stage boundary.

### Result-stage save state

Add an explicit state machine:

```text
NotStarted -> Saving -> Saved
                    \-> Failed -> Saving (retry)
```

- [ ] Start one observed save task for the summary's `RunId`.
- [ ] Display `Saving...` while it runs.
- [ ] Prevent the normal exit transition while the save is in flight.
- [ ] On failure, display the error with `Retry` and an explicit leave-without-saving path.
- [ ] Treat `AlreadySaved` as `Saved`.
- [ ] Refresh only the matching in-memory speed variant after persistence succeeds.
- [ ] Do not clear or replace the frozen summary while a retry is possible.

Tests must cover:

- Same chart/speed updates one row and increments once per distinct run.
- Same chart at `0.75x` and `1.00x` creates two independent aggregates.
- Different pitches at the same speed update the same aggregate and retain per-run pitch metadata.
- Repeating one `RunId` does not increment or duplicate history.
- Repeating an old `RunId` remains idempotent after more than five newer plays rotate the visible history.
- Repeating an old matching `RunId` after its `SongScore` was removed by stale cleanup still returns `AlreadySaved`; a mismatched identity still reports a collision.
- Transaction rollback leaves both score and history unchanged.
- Result-stage save failure is visible and retry succeeds.
- Stage reactivation with the same summary is idempotent.

Verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PlaybackSpeedScorePersistenceTests|FullyQualifiedName~SongManagerUpdateScoreTests|FullyQualifiedName~ResultStageScoreSaveTests"
```

Suggested commit: `feat: save speed-scoped results reliably`

---

## Task 8: Make Score Read APIs and In-Memory Caches Speed-Aware

**Files:**

- Modify `DTXMania.Game/Lib/Song/SongListNode.cs`
- Modify `DTXMania.Game/Lib/Song/SongManager.cs`
- Modify `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Test `DTXMania.Test/Song/SongListNodeScoreVariantTests.cs`
- Update `DTXMania.Test/Song/SongManagerUpdateScoreTests.cs`
- Update `DTXMania.Test/Song/SongDatabaseServiceTests.cs`
- Update `DTXMania.Test/Song/SongDatabaseServiceRecentPlaysTests.cs`
- Update `DTXMania.Test/Song/SongManagerRecentPlaysTests.cs`

- [ ] Add an eager in-memory score-variant map keyed by difficulty/chart and `PlaySpeedPercent`.
- [ ] Add `GetScore(difficulty, playSpeedPercent)` and matching set/refresh helpers.
- [ ] Retain the one-argument `GetScore(difficulty)` only as an explicit `1.00x` compatibility path for legacy/NX code.
- [ ] Stop projecting the currently selected speed into the fixed five-entry `Scores` array.
- [ ] Keep `Scores` temporarily for difficulty metadata/default-speed compatibility, not as the source of truth for active-profile performance.
- [ ] Hydrate every persisted speed variant and its own `PerformanceHistory` rows when songs load.
- [ ] Add speed to `GetScoreWithHistoryAsync` and `RefreshInMemoryScoreForChartAsync`.
- [ ] Audit every `SongScores` query: require an explicit speed for profile-specific reads, or document and test intentional aggregation across speeds.
- [ ] Let Recent Plays queries intentionally aggregate across speeds and return the speed of the selected latest variant.

### Publication and thread-safety contract

`SongManager.UpdateScoreAsync` currently resumes after `ConfigureAwait(false)`, so score refresh can publish from a pool thread while the game thread reads song nodes.

- [ ] Never mutate a published variant dictionary or published `SongScore` in place.
- [ ] Readers take one atomic reference snapshot and perform lock-free lookups.
- [ ] Writers serialize under the existing SongManager lock, clone the dictionary, insert a cloned fresh `SongScore`, and atomically replace the published dictionary reference.
- [ ] Do not require UI readers to acquire SongManager's private lock.
- [ ] Add a concurrency test that repeatedly reads one snapshot while another task publishes refreshed variants; readers must see either the complete old or complete new entry.

Tests must cover:

- `0.75x` and `1.00x` lookups return different score objects.
- An unplayed speed returns an empty/unplayed state without borrowing another speed.
- Refreshing one variant does not mutate another.
- Concurrent publication exposes no partially updated dictionary or score.
- Recent Plays query chooses and returns the most recently played variant across speeds.

Verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListNodeScoreVariantTests|FullyQualifiedName~SongManagerUpdateScoreTests|FullyQualifiedName~SongDatabaseService"
```

Suggested commit: `feat: add speed-aware score read model`

---

## Task 9: Wire Speed-Aware Scores Into UI Consumers

**Files:**

- Modify `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs`
- Modify `DTXMania.Game/Lib/Song/Components/PlayHistoryPanel.cs`
- Modify `DTXMania.Game/Lib/Song/Components/SongBarRenderer.cs`
- Modify `DTXMania.Game/Lib/Song/Components/SongListDisplay.cs`
- Modify `DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs`
- Modify `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Modify `DTXMania.Game/Lib/Stage/ResultStage.cs`
- Test `DTXMania.Test/Song/SongStatusPanelSpeedLogicTests.cs`
- Test `DTXMania.Test/Song/SongBarRendererSpeedLogicTests.cs`
- Update `DTXMania.Test/UI/PlayHistoryPanelLogicTests.cs`
- Update `DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs`
- Update `DTXMania.Test/Stage/SongSelectionStageFilterTests.cs`
- Update `DTXMania.Test/Stage/Result/ResultScreenModelTests.cs`
- Update `DTXMania.Test/Stage/ResultStageTests.cs`

- [ ] Pass active speed explicitly into `SongStatusPanel`, `PlayHistoryPanel`, bar/list render models, and all selected-score helpers.
- [ ] Update score/skill filters to use the selected speed rather than an arbitrary first row.
- [ ] Label Recent Plays with the speed returned by the cross-speed aggregation query.
- [ ] Change `ResultStage.ResolvePreviousScore` specifically: remove its `_selectedSong.Scores` scan and one-argument fallback for gameplay results, and look up the frozen summary speed.
- [ ] Ensure Result new-record comparison uses that resolved speed-specific score.
- [ ] Keep the one-argument/default-speed path only for explicitly legacy/NX consumers.

The Mac project excludes the existing `UI/SongStatusPanelTests.cs`, `UI/SongStatusPanelLogicTests.cs`, `UI/SongBarRendererTests.cs`, and `UI/SongBarRendererLogicTests.cs`. Put new speed-selection logic tests in the Mac-included `DTXMania.Test/Song/` files listed above and keep them free of `GraphicsDevice`.

Tests must cover:

- History panels show only the selected speed's five rows.
- Status, bars, and filter values change when active speed changes.
- `ResolvePreviousScore` compares a `0.75x` result only with the `0.75x` row.
- Recent Plays labels the actual latest variant.
- An unplayed speed never borrows the `1.00x` display score.

Mac-safe verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongStatusPanelSpeedLogicTests|FullyQualifiedName~SongBarRendererSpeedLogicTests|FullyQualifiedName~PlayHistoryPanelLogicTests|FullyQualifiedName~SongFilter|FullyQualifiedName~ResultScreenModel|FullyQualifiedName~ResultStage"
```

Windows/CI verification for the existing graphics-dependent suites:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~SongStatusPanel|FullyQualifiedName~SongBarRenderer"
```

Suggested commit: `feat: wire speed-scoped scores into song UI`

---

## Task 10: Update NX Import Compatibility

**Files:**

- Modify `DTXMania.Game/Lib/Song/NxScoreImporter.cs`
- Modify `DTXMania.Game/Lib/Song/SongManager.cs`
- Update `DTXMania.Test/Song/NxScoreImporterTests.cs`
- Update `DTXMania.Test/Stage/ConfigStageNxImportTests.cs`

- [ ] Route every NX import lookup and merge to `PlaySpeedPercent = 100`.
- [ ] Preserve the existing best-of and snapshot-delta semantics inside that `1.00x` aggregate.
- [ ] Leave `PitchSemitones = 0` for imported history.
- [ ] Keep repeated imports idempotent.
- [ ] Document in the Config-stage completion text or nearby help that NX data is imported as legacy `1.00x` aggregate data.
- [ ] Verify importing NX data does not modify score rows for any other speed.

Verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NxScoreImporterTests|FullyQualifiedName~ConfigStageNxImportTests"
```

Suggested commit: `fix: map NX imports to default play speed`

---

## Task 11: Show the Active Profile and Save State in the UI

**Files:**

- Modify `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Modify `DTXMania.Game/Lib/Stage/SongTransitionStage.cs`
- Modify `DTXMania.Game/Lib/Stage/Result/ResultScreenModel.cs`
- Modify `DTXMania.Game/Lib/Stage/Result/ResultScreenRenderer.cs`
- Modify `DTXMania.Game/Lib/UI/Layout/ResultUILayout.cs`
- Modify `DTXMania.Game/Lib/Stage/ResultStage.cs`
- Update `DTXMania.Test/Stage/SongSelectionStageLogicTests.cs`
- Update `DTXMania.Test/Stage/SongTransitionStageLogicTests.cs`
- Update `DTXMania.Test/Stage/Result/ResultScreenModelTests.cs`
- Update `DTXMania.Test/Stage/Result/ResultScreenRendererTests.cs`
- Update `DTXMania.Test/Stage/ResultScreenRendererThemeLayoutTests.cs`
- Update `DTXMania.Test/Stage/Result/ResultStageScoreSaveTests.cs`

- [ ] Show the current config profile on Song Select.
- [ ] Show the profile about to be prepared on Song Transition; show count-based preparation progress on PerformanceStage's loading screen, where preparation is actually owned.
- [ ] Show the frozen run profile on Result, for example `PLAY 0.75x · PITCH +3 st`.
- [ ] Show the score bucket independently from the audible pitch so users understand that pitch does not split the score.
- [ ] Render `Saving...`, `Saved`, and retryable failure states without overlapping existing result content.
- [ ] Keep layout constants in the appropriate UI layout class.
- [ ] Use invariant formatting and avoid culture-dependent decimal commas in profile labels.
- [ ] Deliberately retain `0.50x` suffix formatting for Play Speed because it exposes the requested `0.05` precision and matches the product request; do not change the legacy visual Scroll Speed `x1.0` format in this feature.

Tests must cover exact display strings at min/default/max speed and negative/zero/positive pitch, plus Result save-state rendering.

Verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultScreen|FullyQualifiedName~SongTransitionStageLogicTests|FullyQualifiedName~SongSelectionStage"
```

Suggested commit: `feat: display playback profile and save status`

---

## Task 12: Extend Telemetry and Add End-to-End Coverage

**Files:**

- Modify `DTXMania.Game/Lib/GameTelemetrySnapshot.cs`
- Modify `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Modify `DTXMania.Game/Lib/Stage/SongTransitionStage.cs`
- Modify `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- Modify `DTXMania.Game/Lib/Stage/ResultStage.cs`
- Modify `DTXMania.E2E/Telemetry/E2EGameState.cs`
- Modify `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs`
- Modify `DTXMania.E2E/GameplayAutoPlaySmokeTests.cs`
- Update `DTXMania.E2E/Telemetry/E2EGameStateTests.cs`
- Update `DTXMania.Test/GameApi/GameApiImplementationTests.cs`

Add telemetry fields:

```text
PlaySpeedPercent
PitchSemitones
PlaybackProfileFrozen
ScoreSaveStatus
ScoreSaveError
AudioPreparationCompleted
AudioPreparationTotal
AudioPreparationCacheHits
PreparedAudioBytes
```

- [ ] Song Select reports current config values with `PlaybackProfileFrozen = false`.
- [ ] Performance and Result report frozen summary/session values.
- [ ] Current song time remains logical song time.
- [ ] Performance loading telemetry exposes preparation counts, cache hits, and prepared PCM bytes.
- [ ] Keep pure telemetry serialization and database assertions in the cross-platform `DTXMania.Test` project; reserve `DTXMania.E2E` for the Windows-targeted process harness.
- [ ] Extend the isolated E2E fixture to write speed/pitch config and include deterministic generated audio.
- [ ] Run the same chart at two distinct speeds against one sandbox database.
- [ ] Verify both runs reach Result and telemetry reports the intended frozen profiles.
- [ ] Query the sandbox SQLite database and verify two `SongScores` rows with independent play counts.
- [ ] Run a same-speed/different-pitch case and verify it reuses the score row while recording pitch in history.
- [ ] Capture logs, final telemetry, and database evidence through the existing E2E artifact writer on failure.

Cross-platform support verification:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~GameApiImplementationTests|FullyQualifiedName~GameTelemetry"
```

The E2E project targets `net8.0-windows7.0`; both support and gameplay categories are Windows/CI-only gates:

```bash
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"
```

Task 12 is not complete from a Mac-only run. Record the successful Windows/CI run that satisfies both commands before closing Phase D.

Suggested commit: `test: cover playback profiles end to end`

---

## Final Verification

Run focused audio tests first so FFmpeg/audio-environment failures are easy to identify:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "Category=Audio"
```

Then run the full Mac build and test suite:

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

On Windows/CI only, run both categories from the `net8.0-windows7.0` E2E project:

```bash
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support"
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E"
```

Check formatting and diff integrity:

```bash
rtk dotnet format DTXMania.Game/DTXMania.Game.Mac.csproj --verify-no-changes
rtk git diff --check
rtk git status --short
```

Manual smoke matrix:

| Speed | Pitch | Expected |
|---:|---:|---|
| `0.50x` | `0 st` | Twice the real playback duration, original pitch, `0.50x` score bucket |
| `1.00x` | `0 st` | Existing default behavior and migrated score bucket |
| `1.50x` | `0 st` | Two-thirds the real playback duration, original pitch, `1.50x` score bucket |
| `1.00x` | `+12 st` | Original gameplay duration, one octave higher |
| `1.00x` | `-12 st` | Original gameplay duration, one octave lower |
| `0.75x` | `+3 st` | Independent slower tempo and higher pitch |

For each matrix row, confirm:

- Notes, autoplay, BGM, chips, progress, and completion remain synchronized.
- Ready and result delays feel unchanged in real time.
- Result displays the frozen profile.
- Save completes visibly.
- Song Select shows only the matching speed's score and five-row history.
- Preparation progress appears for a cold non-default profile and stays below the PCM budget.

On both DesktopGL and WindowsDX, use the same generated calibration tone to confirm that measured final duration is within `±2%` of the requested tempo and that `+12 st`/`-12 st` is audibly and spectrally one octave above/below the source. Also listen for backend-specific clipping, truncation, or severe artifacts. Record both platform results; the WindowsDX result is a Phase D CI/Windows gate.

---

## Requirement Traceability

| Requirement or invariant | Owning tasks | Completion evidence |
|---|---|---|
| Select `0.50x..1.50x` in `0.05x` steps | 1, 11 | Range/config tests and exact UI strings |
| Adjust pitch independently | 1, 3–5, 11 | Factor tests, generated-tone proof, and profile display |
| Freeze modifiers per run | 1, 4–5, 12 | Summary and telemetry tests |
| Keep all gameplay systems synchronized | 2, 4–5 | Clock/integration tests and manual timing matrix |
| Keep default behavior/direct path | 2–4 | Default bypass and compatibility tests |
| Save a distinct complete aggregate per speed | 6–9 | Migration, persistence, read-model, and UI tests |
| Keep pitch out of score identity but in history | 6–7, 9 | Persistence and history-panel tests |
| Make saving atomic, observable, retryable, and durable | 6–7, 11–12 | Receipt, rollback, Result-state, telemetry, and E2E tests |
| Preserve NX data at `1.00x` | 6, 10 | Migration and importer tests |
| Bound preparation resources and expose progress | 3–4, 12 | Cache tests, benchmark record, budget rejection, and telemetry |
| Publish score variants safely across threads | 8 | Immutable snapshot concurrency test |
| Prove platform behavior | 3–5, 12 | Mac suite plus DesktopGL/WindowsDX calibration and Windows E2E |

---

## Definition of Done

- [ ] [Tasks 1, 11] Speed is selectable only in `0.05x` increments from `0.50x` through `1.50x`.
- [ ] [Tasks 1, 3–5, 11] Pitch is independently selectable and does not change final gameplay duration.
- [ ] [Tasks 1, 4–5] Modifiers cannot change during an active run.
- [ ] [Tasks 2, 4–5] Background, scheduled BGM, and chip audio stay synchronized with chart time.
- [ ] [Tasks 3–4] Default playback bypasses generated variants.
- [ ] [Tasks 3–4] Modified playback fails visibly when correct audio cannot be prepared.
- [ ] [Tasks 3–4, 12] Preparation progress is visible, cancellable, benchmarked, and accurately reports cache hits.
- [ ] [Task 4] No active prepared-audio set exceeds `512 MiB`, and every session sound has exactly one owner.
- [ ] [Task 3] Prepared audio uses the strict versioned raw-PCM artifact rather than a WAV container.
- [ ] [Tasks 6, 10] Existing scores and NX imports appear under `1.00x`.
- [ ] [Tasks 6–9] Every complete score aggregate is isolated by play speed.
- [ ] [Tasks 6–7, 9] Pitch variants share the speed bucket and retain per-run pitch metadata.
- [ ] [Tasks 6–7, 11] Score writes are transactional, observed, retryable, and idempotent across score cleanup.
- [ ] [Tasks 8–9, 11] Song Select, Result, Recent Plays, history, filters, and new-record detection use the intended speed.
- [ ] [Task 12] Telemetry and E2E tests expose and verify the frozen profile.
- [ ] [Task 8] Published score variants are immutable snapshots safe for background refresh and game-thread reads.
- [ ] [Final verification] Mac build/tests pass, DesktopGL/WindowsDX calibration is recorded, and both Windows-only E2E categories pass in CI or on Windows.