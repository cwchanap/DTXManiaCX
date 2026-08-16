# HPA-13 Chart-Synchronized Gameplay Metronome Design

**Issue:** [HPA-13](https://linear.app/cwchanap/issue/HPA-13/add-configurable-chart-synchronized-gameplay-metronome)  
**Date:** 2026-08-15  
**Status:** Draft

## Context

DTXManiaCX has no gameplay click track. HPA-13 adds one persisted **Metronome** toggle under the Drums settings and keeps the feature synchronized to authored DTX timing.

The existing seams already define the right ownership split:

- `ChartTimingMap` compiles base BPM plus channels `02`, `03`, and `08` and resolves chart positions to logical milliseconds.
- `ParsedChart.FinalizeChart()` publishes finalized gameplay collections such as measure lines.
- `PerformanceStage.UpdateGameplayManagers(logicalSongTimeMs, pendingHitTimeMs)` already receives both the raw chart clock and the latency-compensated judgement clock.
- `ResourceManager` owns skin-relative sound resolution and silent fallback behavior.

The metronome extends those seams rather than adding another clock, parser channel, transport abstraction, or audio service.

## Goals

- Persist `Metronome=false` by default.
- Expose **Metronome OFF/ON** under **Settings → Drums**, after Pitch and before Auto Play.
- Accent each measure start and click each later quarter-note boundary in that measure.
- Follow base BPM and channels `02`, `03`, and `08`, including in-measure BPM changes.
- Follow Play Speed.
- Start only when gameplay playback starts after READY; remain silent while loading, READY, paused, and outside performance.
- Skip stale accumulated clicks after a frame hitch instead of producing a catch-up burst.
- Keep click playback independent of Auto Play, judgement, scoring, combo, gauge, and input-latency compensation.
- Ship deterministic, skin-overridable click assets whose onset is suitable for a timing feature.

## Non-goals

- Count-in.
- Eighth/triplet/sixteenth subdivisions.
- Gameplay hotkey or per-song override.
- Metronome volume UI or master/BGM/SE routing changes.
- Visual beat lines.
- Practice loops or a practice-mode redesign.
- Parsing NX channel `51` beat-line chips.
- **Runtime** click synthesis.
- A second timer, event bus, worker thread, or sample-accurate audio scheduler.

## User experience

The Drums category gains:

```text
Metronome    OFF / ON
```

Description:

```text
Accents each measure start and clicks later quarter-note beats during gameplay.
```

The setting uses the existing deferred config-save path and is frozen when `PerformanceStage` activates. There is no mid-song toggle in this ticket.

## Timing semantics

### Quarter-note grid

CX uses `192` authored ticks per measure. Channel `02` changes the musical duration of those 192 ticks, not the coordinate range.

```text
measureBeats = 4.0 * measureLengthMultiplier
measureTick  = beatOffset * 192.0 / measureBeats
             = beatOffset * 48.0 / measureLengthMultiplier
```

Examples:

| Multiplier | Quarter-note markers |
| --- | --- |
| `1.0` | `0, 48, 96, 144` |
| `0.75` | `0, 64, 128` |
| `1.5` | `0, 32, 64, 96, 128, 160` |

`beatOffset == 0` is the measure accent. The current measure never emits tick `192`; the next measure emits its own accent.

A non-integral length such as `0.625` gives `2.5` quarter notes, so the grid contains offsets `0`, `1`, and `2`, followed by the next measure accent after the remaining half beat.

### Floating-point boundary guard

`measureLengthMultiplier` is a `double`. A value mathematically intended to produce an integer number of beats can land marginally above that integer after parsing/calculation. Without a guard, the grid can emit an extra marker immediately before the next measure accent.

Use a small quarter-beat epsilon:

```text
BeatGridEpsilon = 1e-6
```

Enumerate integer offsets while preserving the mandatory measure-start marker:

```text
for beatOffset = 0;
    beatOffset == 0 || beatOffset < measureBeats - BeatGridEpsilon;
    beatOffset++
```

This treats `3.0000000001` beats as three beats while still emitting the accent for any valid positive, very short measure.

### BPM changes

Every marker position is resolved through `ChartTimingMap`. Do not divide adjacent `MeasureLine` timestamps evenly: an in-measure BPM change makes the beat intervals unequal.

### Play Speed and stale-click tolerance

`SongTimer.GetCurrentMs()` is logical chart time, so at Play Speed `s` a real interval of `R` milliseconds advances `R * s` logical milliseconds.

The late-click threshold is a perceptual **real-time** limit. Use `100 ms` real time for the MVP and convert it once at the stage boundary:

```text
maxLateChartMs = 100.0 * frozenPlaySpeed
```

Examples:

- `0.50x` → `50` logical ms = `100` real ms.
- `1.00x` → `100` logical ms = `100` real ms.
- `2.00x` → `200` logical ms = `100` real ms.

The pure scheduler receives the already-scaled logical threshold; it does not know about Play Speed.

## Chosen architecture

### 1. Minimal finalized marker

Add:

```csharp
public sealed class BeatMarker
{
    public double TimeMs { get; init; }
    public bool IsMeasureStart { get; init; }
}
```

No BPM, bar, lane, display position, or scoring data is needed at runtime.

### 2. `ChartTimingMap` remains only a timing calculator

Do not add `BuildBeatMarkers`.

Replace the current integer-only timing method with one implementation:

```csharp
internal double CalculateTimeMs(int bar, double tick);
```

Existing integer callers continue to pass `int` ticks through normal numeric conversion.

The method must preserve the current normalization contract for computed double positions:

- reject negative bar/tick and non-finite tick values;
- carry tick values `>= 192` into later bars;
- preserve the fractional remainder;
- reject positions beyond the compiled timing horizon.

Keep the existing integer `NormalizePosition(int, int)` for authored parser/tempo-map paths. Add only a private fractional normalizer for `CalculateTimeMs`; do not change `_tempoChanges` or `TimingAnchor.Tick` away from integer ticks.

Generalize private `CalculateIntervalMs` to `double tickDelta`. To select a compiled anchor for a fractional position, use the integer floor of the normalized tick; all tempo anchors are authored at integer ticks.

Expose one additional query:

```csharp
internal double GetMeasureLengthMultiplier(int bar);
```

This must read the multiplier from the compiled bar-start `TimingAnchor`, not re-read `_measureLengths`. The compiled anchor is the timing map's source of truth and gives the same horizon/error behavior as time lookup.

### 3. `ParsedChart.FinalizeChart()` owns grid policy

Add `BeatMarkers` and clear it with `MeasureLines` at the start of finalization.

After `TimingMap.Rebuild` and normal note/BGM time resolution, enumerate bars `0..highestOccupiedBar`:

```text
for each bar:
    multiplier = TimingMap.GetMeasureLengthMultiplier(bar)
    measureBeats = 4.0 * multiplier

    for integer beatOffset using BeatGridEpsilon:
        tick = beatOffset * 192.0 / measureBeats
        BeatMarkers.Add(
            TimeMs = TimingMap.CalculateTimeMs(bar, tick),
            IsMeasureStart = beatOffset == 0)
```

The extra terminal `MeasureLine` at `highestOccupiedBar + 1` does not create another metronome measure.

An empty chart remains empty. Timing directives alone do not invent a playable timeline. Repeated finalization must produce identical markers.

`ChartManager` remains unchanged.

### 4. Pure `MetronomePlayer`

Add a small ordered-event cursor:

```csharp
internal sealed class MetronomePlayer
{
    internal MetronomePlayer(
        IReadOnlyList<BeatMarker> markers,
        double maxLateChartMs,
        Action<BeatMarker> playClick);

    internal void Update(double currentChartTimeMs);
}
```

On update:

1. Consume all markers with `TimeMs <= currentChartTimeMs`.
2. Retain only the latest consumed marker.
3. Play it once when its lateness is within `maxLateChartMs`.
4. Otherwise drop it.

Do not add `Reset`, pause/resume, seeking, clock ownership, `IDisposable`, an interface, or an event. `PerformanceStage` constructs a fresh player for each performance initialization and clears it during teardown, so a standalone reset API would have no caller.

This cursor remains separate from BGM and Auto Play because all three intentionally have different overdue-event policies.

### 5. Persisted configuration

Follow the existing CX boolean style:

```csharp
public bool Metronome { get; set; } = false;
```

Extend `IConfigManager`/`ConfigManager` like `AutoPlay` and `NoFail`:

- parse `Metronome` with `TryParseBool`;
- save `Metronome=<value>` under `[Game]`;
- expose `SetMetronome(bool)` using the existing deferred-save mechanism.

No migration alias is required.

### 6. `PerformanceStage` owns runtime audio

Freeze `ConfigData.Metronome` in `OnActivate`; Play Speed is already frozen in `_playbackModifiers`.

After chart finalization, when enabled:

- load `SoundPath.MetronomeBeat` and `SoundPath.MetronomeAccent` through `IResourceManager.LoadSound`;
- convert the real `100 ms` late threshold to logical milliseconds using the frozen Play Speed;
- construct `MetronomePlayer` with `_parsedChart.BeatMarkers` and `PlayMetronomeClick`.

Drive it from the existing dual-clock test seam:

```csharp
private void UpdateGameplayManagers(
    double logicalSongTimeMs,
    double pendingHitTimeMs)
{
    _metronomePlayer?.Update(logicalSongTimeMs);
    ... ProcessAutoPlay(logicalSongTimeMs) ...
    ... judgement uses pendingHitTimeMs ...
}
```

This makes the raw-clock dependency directly testable with mismatched raw/judgement values without constructing a live `SongTimer` or renderer graph. The outer `_songTimer.IsPlaying` block still controls whether `UpdateGameplayManagers` runs during real gameplay.

Use one narrow playback seam:

```csharp
protected virtual void PlayMetronomeClick(BeatMarker marker)
{
    var sound = marker.IsMeasureStart
        ? _metronomeAccentSound
        : _metronomeBeatSound;

    sound?.SoundEffect?.Play(1.0f, 0.0f, 0.0f);
}
```

Direct `SoundEffect.Play` is intentional for these fire-and-forget clicks: `ISound.Play(...)` creates a caller-owned `SoundEffectInstance`, which would require another active-instance cleanup collection.

Do not wire `SEVolume`; it is not currently persisted/applied consistently across gameplay audio.

Cleanup clears the player and balances `RemoveReference()` for both sounds through the existing serialized audio lifecycle/generation guard. Re-entry creates a new player at marker zero.

### 7. Deterministic click assets through `tools/sfxgen`

The existing SFX generator uses ElevenLabs for normal UI sounds. That is unsuitable as the sole source for a metronome click because onset timing is not deterministic and the current pack validator checks compatibility, not transient onset.

Keep `tools/sfxgen/manifest.json` as the sound-pack inventory, but extend the generator minimally with a second source type for deterministic tone clicks. Existing entries remain backward-compatible and default to ElevenLabs.

Suggested manifest shape:

```json
{
  "file": "Metronome Beat.ogg",
  "generator": "ffmpeg_sine",
  "duration_seconds": 0.03,
  "frequency_hz": 1000
}
```

The accent uses the same duration with a clearly different frequency, for example `1600 Hz`.

`sfxgen.py generate --only <metronome file>` should:

- dispatch `ffmpeg_sine` entries directly to ffmpeg without requiring `ELEVENLABS_API_KEY`;
- generate a ~30 ms sine burst beginning at sample zero;
- fade rapidly to zero and encode Ogg/Vorbis at a supported sample rate;
- write directly to `System/CXNeon/Sounds`.

Existing ElevenLabs entries continue using their current path. `generate all` may still require the API key because it includes ElevenLabs sounds; generating either metronome file alone must not.

Update `test_sfxgen.py` so manifest validation is conditional by generator type and so the deterministic command/duration are covered. Extend `validate_pack` just enough to reject a deterministic click whose duration materially exceeds its manifest duration. Do not add a generalized DSP framework.

The committed OGGs, `SoundPath` constants, and manifest entries land in the same implementation step so `CxNeonPackTests` never observes an intentionally incomplete sound inventory.

### 8. Failure handling

Use the existing resource contract:

- `LoadSound` reports failures and normally returns a silent fallback;
- defensive null handling remains only for fallback-creation failure;
- unexpected playback exceptions are logged locally;
- gameplay never fails because a click asset is missing.

Do not synthesize a runtime replacement or substitute a drum/menu sound.

## Testing strategy

### `ChartTimingMapTests`

Cover:

- one `CalculateTimeMs(int, double)` path with integer and fractional ticks;
- negative/non-finite rejection and `tick >= 192` carry semantics;
- fractional lookup before/at/after an integer BPM anchor;
- compiled measure multiplier lookup from the bar-start anchor;
- out-of-horizon behavior.

### `ParsedChartTests`

Cover:

- multiplier `1.0` → four markers;
- `0.75` → three;
- `1.5` → six;
- `0.625` → 2.5-beat behavior without rounding;
- a value marginally above an integer beat count does not add a near-boundary extra marker;
- a very short positive measure still emits its measure-start accent;
- boundary and in-measure BPM changes;
- adjacent measures with one shared-boundary accent;
- no marker for the terminal measure line;
- empty chart;
- repeated finalization idempotence.

### `MetronomePlayerTests`

Cover:

- no early playback;
- accent/regular forwarding;
- marker zero;
- consume-many/play-latest behavior;
- stale-marker drop using the constructor-supplied logical threshold;
- no replay;
- empty input.

No reset test: the component is recreated per performance.

### Configuration and stage tests

Cover:

- default Off, parse/save round trip, typed setter;
- exact Drums collection order and toggle activation using existing `ConfigStageLogicTests`;
- concrete `IConfigManager` test doubles compiling with `SetMetronome`;
- disabled stage does not request click sounds;
- enabled stage requests both paths and constructs a player;
- `UpdateGameplayManagers(raw, compensated)` uses **raw** time for the metronome;
- real late tolerance scales to logical time at `0.5x`, `1x`, and `2x`;
- READY/paused real gameplay does not reach manager updates through the outer transport guard;
- cleanup balances sound references and re-entry constructs a fresh cursor;
- fallback/missing assets are non-fatal;
- CXNeon inventory includes both files.

### SFX tooling tests

Cover:

- manifest inventory still matches `SoundPath`;
- legacy entries default to ElevenLabs;
- `ffmpeg_sine` entries do not require an API key when generated individually;
- deterministic command includes configured frequency, ~30 ms duration, supported sample rate, and Vorbis output;
- validation rejects an overlong metronome click.

## Manual verification precondition

Before judging timing, confirm the two committed click files are actually reachable through the **active runtime skin resolution chain**. For a development run, point the active skin at the repository CXNeon pack or copy/install the generated sounds into the active/fallback System skin as appropriate.

First confirm one audible metronome click. Only then run the BPM/measure/Play Speed timing matrix; otherwise ResourceManager's silent fallback can make an asset-resolution problem look like a scheduler bug.

## Risks and trade-offs

- **Frame-level submission:** click timing remains bounded by update cadence/audio backend. A separate sample-accurate scheduler is deferred until audible evidence requires it.
- **Fractional positions:** kept private to timing resolution; authored note/BGM/tempo anchors remain integer based.
- **Generated asset quality:** deterministic ffmpeg clicks trade decorative sound design for precise onset, reproducibility, and key-free regeneration. That is the correct trade for the MVP metronome.

## Acceptance criteria

- Persisted `Metronome` toggle defaults Off and appears in the Drums settings at the specified location.
- Enabled gameplay accents measure starts and clicks later quarter notes correctly across representative channels `02`, `03`, and `08` timing.
- Grid generation has no duplicate near-boundary click from floating-point drift.
- Play Speed changes preserve a constant real-time late-drop threshold.
- Metronome scheduling uses the raw logical clock through `UpdateGameplayManagers`.
- Loading, READY, pause, result, and unrelated stages remain silent.
- Re-entry starts from marker zero through fresh player construction; no dead `Reset()` API exists.
- Missing assets do not crash gameplay.
- Click assets are deterministic, short, committed, represented in `SoundPath` + manifest, and individually regenerable without an ElevenLabs key.
- No channel `51` parser, `SEVolume` routing, runtime synthesis, new audio service, or second timing scheduler is introduced.
