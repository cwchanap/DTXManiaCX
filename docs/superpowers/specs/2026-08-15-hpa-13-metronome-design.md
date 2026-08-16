# HPA-13 Chart-Synchronized Gameplay Metronome Design

**Issue:** [HPA-13](https://linear.app/cwchanap/issue/HPA-13/add-configurable-chart-synchronized-gameplay-metronome)  
**Date:** 2026-08-15  
**Status:** Draft

## Context

DTXManiaCX currently exposes drum gameplay settings such as Scroll Speed, Play Speed, Pitch, Auto Play, and No Fail, but it has no click track. HPA-13 adds a practice-oriented metronome that the player can enable from the Drums settings before entering gameplay.

HPA-600 already made `ChartTimingMap` the position-to-time authority for base `#BPM`, channel `02` measure-length multipliers, channel `03` direct BPM changes, and channel `08` BPM-table changes. `ParsedChart.FinalizeChart()` consumes that timing map and publishes finalized gameplay collections such as measure lines.

The metronome should follow the same split: `ChartTimingMap` answers timing questions, `ParsedChart.FinalizeChart()` owns musical beat-grid enumeration, and `PerformanceStage` owns runtime playback.

## Goals

- Add a persisted **Metronome** On/Off toggle under **Settings → Drums**.
- Default the setting to Off.
- Play an accented click at each measure start and a regular click at each later quarter-note boundary in that measure.
- Follow channel `02`, `03`, and `08` timing, including tempo changes inside a measure.
- Follow configured Play Speed by scheduling against the existing rate-aware logical song clock.
- Start only when gameplay playback begins after READY, then pause, resume, reset, and stop with gameplay.
- Keep metronome behavior independent from Auto Play, judgement, scoring, combo, gauge, and input-latency compensation.
- Skip stale clicks after a delayed frame instead of producing a catch-up burst.
- Allow skins to override the two click sounds through the existing resource path.
- Degrade to silence rather than fail gameplay when a click asset cannot be loaded.

## Non-goals

- Count-in before gameplay.
- Eighth-note, triplet, or sixteenth-note subdivisions.
- A metronome volume setting, custom click selection, or per-song override.
- A gameplay hotkey for changing the setting.
- Visual beat lines or notation changes.
- Practice loops or a broader practice-mode redesign.
- Procedural click synthesis.
- Parsing NX beat-line channel `51`; CX intentionally does not need that channel for this feature.
- A second timing event bus, recurring timer, worker thread, or sample-accurate audio scheduler.
- Master/BGM/SE volume routing changes.

## User experience

The Drums category gains one item after Pitch and before Auto Play:

```text
Metronome    OFF / ON
```

Description:

```text
Accents each measure start and clicks later quarter-note beats during gameplay.
```

The setting is persisted immediately through the existing deferred config-save path and is frozen when `PerformanceStage` activates. There is no mid-song toggle.

When enabled:

- the first audible click is the accented marker at chart time `0`;
- READY, loading, result, and other stages remain silent;
- pausing freezes the logical song clock and therefore freezes click scheduling;
- resuming continues from the next unresolved marker without replaying the previous click;
- restarting or re-entering gameplay begins again from marker zero.

## Timing semantics

### Quarter-note grid inside a DTX measure

CX retains the DTX timeline coordinate of `192` ticks per authored measure. Channel `02` does not change that coordinate range; it changes how many musical quarter-note beats the measure contains:

```text
measureBeats = 4.0 * measureLengthMultiplier
```

A quarter-note boundary at integer beat offset `n` maps to:

```text
measureTick = n * 192.0 / measureBeats
            = n * 48.0 / measureLengthMultiplier
```

Generate offsets `n = 0, 1, 2, ...` while `n < measureBeats`. Offset zero is the accented measure-start marker. The current measure never adds a second marker at tick `192`; the next measure contributes its own accent.

Representative grids:

| Multiplier | Musical length | Marker ticks |
| --- | --- | --- |
| `1.0` | 4 quarter notes | `0, 48, 96, 144` |
| `0.75` | 3 quarter notes | `0, 64, 128` |
| `1.5` | 6 quarter notes | `0, 32, 64, 96, 128, 160` |

An arbitrary valid multiplier can place a quarter-note boundary between integer DTX ticks. Fractional ticks therefore exist only inside timing resolution; parser-authored positions and runtime marker data remain unchanged.

For a non-integral musical length such as `2.5` quarter notes, emit offsets `0`, `1`, and `2`, then accent the next measure start after the remaining half beat. Do not round the measure length or invent a click for the incomplete quarter note.

### BPM changes

Each marker time is resolved through the compiled timing anchors. A BPM change at a measure boundary affects all later markers. A BPM change inside a measure affects only the elapsed interval after its authored position.

Do not derive beat times by evenly dividing adjacent `MeasureLine` timestamps: an in-measure BPM change makes those intervals unequal.

### Play Speed and audio latency

`SongTimer.GetCurrentMs(GameTime)` returns the rate-aware raw logical chart clock. Scheduling markers against that clock keeps clicks aligned at non-default Play Speed.

Player input judgement separately uses latency-compensated timing. The metronome must not use that compensated clock; it is gameplay audio scheduled on the same raw timeline as BGM events, autoplay, visuals, progress, and completion.

## Approaches considered

### Finalized chart-owned beat markers — selected

`ParsedChart.FinalizeChart()` enumerates the musical quarter-note grid and asks `ChartTimingMap` to resolve each authored position into milliseconds. Runtime consumes only finalized `BeatMarker` values.

This matches the existing measure-line seam: finalization publishes gameplay collections while the timing map remains a reusable position-to-time calculator.

### Recurring runtime timer — rejected

A timer based on base BPM would drift when channels `02`, `03`, or `08` alter authored timing and would require separate pause, resume, Play Speed, and lifecycle synchronization.

### Fixed markers every 48 DTX ticks — rejected

Forty-eight ticks is one quarter note only when the measure multiplier is `1.0`.

### Evenly divide each rendered measure interval — rejected

This cannot model tempo changes inside a measure and would make a visual model the source of musical timing.

### Parse NX channel `51` — rejected

NX can click authored bar/beat-line chips, but CX intentionally does not parse channel `51`. The timing map already contains enough information to derive the quarter-note grid without expanding the parser for this ticket.

## Chosen architecture

### 1. Resolved beat-marker model

Add a small chart component:

```csharp
public sealed class BeatMarker
{
    public double TimeMs { get; init; }
    public bool IsMeasureStart { get; init; }
}
```

No lane, BPM, bar, display position, or scoring metadata is needed. `IsMeasureStart` selects the accented sound; `TimeMs` is the complete runtime timing contract.

### 2. `ChartTimingMap` stays the position-to-time authority

Do not add `BuildBeatMarkers` to `ChartTimingMap`. Beat-grid policy belongs to finalization, not the timing compiler.

Extend only the timing seams that finalization needs:

```csharp
internal double CalculateTimeMs(int bar, double tick);
internal double GetMeasureLengthMultiplier(int bar);
```

Implementation notes:

- retain the existing integer `CalculateTimeMs(int bar, int tick)` contract for authored note/BGM positions;
- generalize private `CalculateIntervalMs` from `int tickDelta` to `double tickDelta`;
- fractional lookup is only for an in-measure tick in `[0, 192)`;
- select the last timing anchor at or before the fractional position by using the integer floor for `FindAnchorIndex`;
- integrate the remaining fractional tick delta using that anchor's BPM and measure multiplier;
- expose only a read-only effective multiplier lookup from the existing `_measureLengths` data, defaulting to `1.0`.

Do not introduce a fractional chart-position type or duplicate timing table.

### 3. `ParsedChart.FinalizeChart()` owns marker enumeration

Add an ordered `BeatMarkers` collection to `ParsedChart` and clear it with `MeasureLines` at the start of finalization.

After `TimingMap.Rebuild` and before final sorting/completion bookkeeping, enumerate bars `0..highestOccupiedBar`:

```text
for each bar:
    multiplier = TimingMap.GetMeasureLengthMultiplier(bar)
    measureBeats = 4.0 * multiplier

    for beatOffset = 0 while beatOffset < measureBeats:
        tick = beatOffset * 192.0 / measureBeats
        BeatMarkers.Add(
            TimeMs = TimingMap.CalculateTimeMs(bar, tick),
            IsMeasureStart = beatOffset == 0)
```

The extra terminal `MeasureLine` at `highestOccupiedBar + 1` does not create another metronome measure.

An empty chart retains no beat markers. Timing directives without notes or BGM events do not invent a playable metronome timeline. Repeated finalization must reproduce the same marker count, order, timestamps, and accents.

`ChartManager` remains unchanged; `PerformanceStage` already owns the finalized `ParsedChart`.

### 4. Pure `MetronomePlayer` scheduling component

Add `Stage/Performance/MetronomePlayer.cs` as a small stateful scheduler:

```csharp
internal sealed class MetronomePlayer
{
    internal const double MaxLatePlaybackMs = 100.0;

    internal MetronomePlayer(
        IReadOnlyList<BeatMarker> markers,
        Action<BeatMarker> playClick);

    internal void Reset();
    internal void Update(double currentChartTimeMs);
}
```

On each update:

1. Consume every marker whose `TimeMs <= currentChartTimeMs`.
2. Remember only the latest consumed marker.
3. If it is no more than `MaxLatePlaybackMs` late, invoke the callback once.
4. Otherwise skip it.

This handles marker zero on the first active frame while preventing accumulated clicks from firing as a burst after a hitch.

The scheduler owns no `ISound`, clock, pause state, seeking API, interface, or disposal logic. `Reset()` only moves the next-marker index back to zero.

### 5. Persisted configuration

Use the existing CX boolean naming style:

```csharp
public bool Metronome { get; set; } = false;
```

Extend `IConfigManager` and `ConfigManager` following `AutoPlay` / `NoFail`:

- parse the `Metronome` key using `TryParseBool`;
- serialize `Metronome=<value>` in `[Game]`;
- expose `SetMetronome(bool)` and mark the normal deferred save dirty.

A config file without the key naturally keeps the default Off value. No migration alias is required.

### 6. `PerformanceStage` owns sounds and playback

Freeze `ConfigData.Metronome` during `OnActivate`, the same way Auto Play is frozen.

After chart finalization, and only when enabled:

- load `SoundPath.MetronomeBeat` and `SoundPath.MetronomeAccent` through `IResourceManager.LoadSound`;
- create `MetronomePlayer` from `_parsedChart.BeatMarkers`;
- pass a narrow stage playback hook as its callback.

Use a small overridable test seam:

```csharp
protected virtual void PlayMetronomeClick(BeatMarker marker)
{
    var sound = marker.IsMeasureStart ? _metronomeAccentSound : _metronomeBeatSound;
    sound?.SoundEffect?.Play(1.0f, 0.0f, 0.0f);
}
```

The direct `SoundEffect.Play` overload is intentional for these short fire-and-forget clicks: it does not create caller-owned `SoundEffectInstance` objects that would require another active-instance collection. Tests can override the narrow playback hook instead of constructing real MonoGame audio objects.

Do **not** route clicks through `SEVolume`. `ConfigData.SEVolume` is not currently parsed, persisted, or consistently applied to gameplay audio. Wiring only the metronome to it would create inconsistent volume behavior. Full master/BGM/SE volume routing should be handled as a separate cross-audio change.

In `UpdateGameplay`, call the scheduler beside `ProcessBGMEvents(currentTimeMs)` inside the existing `_songTimer.IsPlaying` block and before latency-compensated judgement timing is derived.

Cleanup clears the player and balances `RemoveReference()` for both loaded sounds using the existing serialized audio lifecycle/generation guard. Do not add another cancellation mechanism.

### 7. Skin-overridable click assets

Add constants to `SoundPath`:

```csharp
public const string MetronomeBeat = "Sounds/Metronome Beat.ogg";
public const string MetronomeAccent = "Sounds/Metronome Accent.ogg";
```

Include both in `SoundPath.GetAllSoundPaths()`.

Add matching entries to `tools/sfxgen/manifest.json` and commit the generated files:

```text
System/CXNeon/Sounds/Metronome Beat.ogg
System/CXNeon/Sounds/Metronome Accent.ogg
```

The clips should be short, dry transients with a clearly distinguishable accent and minimal tail. Custom skins may override the same relative paths.

### 8. Failure handling

Use the existing resource contract rather than inventing a null-on-missing contract. `ResourceManager.LoadSound` reports load failure and normally returns a silent fallback sound.

Therefore:

- load through `IResourceManager.LoadSound` like other stage sounds;
- keep defensive null handling only for the fallback-creation failure path;
- let a missing/invalid asset become silent through the existing fallback;
- catch unexpected playback exceptions locally and continue gameplay;
- do not synthesize a tone, reuse a drum chip, or fail chart initialization.

Marker generation is deterministic chart data and never depends on sound availability.

## Lifecycle

```text
ConfigStage edit
    ↓ persisted Metronome
PerformanceStage.OnActivate
    ↓ freeze setting
chart/audio initialization
    ↓ finalized BeatMarkers + optional loaded click sounds/player
StartSong
    ↓ raw SongTimer begins at chart time 0
UpdateGameplay while IsPlaying
    ↓ consume due marker; play at most one current click
Pause
    ↓ raw clock freezes; no scheduler update
Resume
    ↓ continue from next marker
OnDeactivate/restart
    ↓ clear player and release sound references
```

## Testing strategy

### `ChartTimingMapTests`

Cover only the new timing resolver contract:

- fractional tick resolution at base BPM;
- fractional tick before/at/after an in-measure BPM anchor;
- measure multiplier lookup defaults and configured values.

### `ParsedChartTests`

Cover musical-grid behavior:

- normal `1.0` measure: four markers, first accented;
- channel `02` multiplier `0.75`: three markers;
- channel `02` multiplier `1.5`: six markers;
- non-integral musical length without rounding;
- BPM change at a measure boundary;
- BPM change inside a measure;
- adjacent measures without duplicate boundary clicks;
- no marker for the extra terminal measure line;
- empty chart behavior;
- repeated finalization idempotence.

Use time assertions with a small floating-point tolerance.

### Scheduler tests

Cover no-early playback, accent/regular forwarding, marker zero, stale-marker skipping, one-click maximum after a hitch, no replay after consumption, reset, and empty input.

### Configuration and stage tests

Cover:

- default Off and config round trip for `Metronome`;
- typed setter/deferred-save behavior;
- Drums collection order and toggle activation through existing `ConfigStageLogicTests`;
- concrete `IConfigManager` test stubs compiling with `SetMetronome`;
- disabled activation does not load click sounds;
- enabled activation requests both sound paths;
- the scheduler uses raw song time only while playing;
- the scheduler callback reaches the overridable `PlayMetronomeClick` hook;
- READY and paused states do not advance the scheduler;
- cleanup balances sound references;
- missing assets remain non-fatal through the existing fallback contract;
- `CxNeonPackTests` sees both sounds in the shipped pack.

## Risks and trade-offs

### Frame-level audio submission

Clicks are submitted from the game update loop, so precision is limited by frame cadence and the platform audio backend. This matches current BGM/autoplay scheduling and is acceptable for the first implementation. A dedicated scheduler would add lifecycle and cross-platform complexity without evidence that it is needed.

### Very short sound clips

Long click samples can overlap at high BPM or Play Speed. Keep the bundled assets short and dry rather than adding instance pooling or explicit stop logic.

### Fractional chart positions

Fractional ticks are necessary for exact quarter-note placement under arbitrary channel `02` multipliers. They remain private to timing resolution; notes, BGM events, parser directives, and runtime APIs continue using their existing contracts.

## Acceptance criteria

- Settings → Drums exposes a persisted `Metronome` toggle that defaults to Off.
- Disabled gameplay loads and plays no metronome audio.
- Enabled gameplay accents each measure start and clicks each later quarter-note boundary.
- Markers remain correct across representative channel `02`, `03`, and `08` timing changes.
- Non-default Play Speed stays synchronized through the raw logical song clock.
- Loading, READY, pause, result, and unrelated stages do not emit clicks.
- Resume, restart, and stage re-entry neither duplicate clicks nor produce catch-up bursts.
- Missing click assets do not crash or block gameplay.
- Both click assets are represented in `SoundPath`, the SFX manifest, and the committed CXNeon pack.
- No channel `51` parser, volume routing, new audio service, or second timing scheduler is introduced.