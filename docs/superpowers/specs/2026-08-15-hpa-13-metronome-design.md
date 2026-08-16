# HPA-13 Chart-Synchronized Gameplay Metronome Design

**Issue:** [HPA-13](https://linear.app/cwchanap/issue/HPA-13/add-configurable-chart-synchronized-gameplay-metronome)  
**Date:** 2026-08-15  
**Status:** Draft

## Context

DTXManiaCX currently exposes drum gameplay settings such as Scroll Speed, Play Speed, Pitch, Auto Play, and No Fail, but it has no click track. HPA-13 adds a practice-oriented metronome that the player can enable from the Drums settings before entering gameplay.

This feature must stay synchronized with the authored chart rather than with a separate wall-clock timer. HPA-600 already made `ChartTimingMap` the timing authority for:

- base `#BPM`;
- channel `02` measure-length multipliers;
- channel `03` direct BPM changes;
- channel `08` BPM-table changes.

`ParsedChart.FinalizeChart()` uses that map to resolve notes, BGM events, and measure lines into absolute chart milliseconds. The metronome should extend the same finalization seam so every gameplay timing consumer remains on one chart clock.

## Goals

- Add a persisted **Metronome** On/Off toggle under **Settings → Drums**.
- Default the setting to Off.
- Play an accented click at each measure start and a regular click at each later quarter-note boundary in that measure.
- Follow channel `02`, `03`, and `08` timing, including tempo changes inside a measure.
- Follow configured Play Speed by scheduling against the existing rate-aware logical song clock.
- Start only when gameplay playback begins after READY, then pause, resume, reset, and stop with gameplay.
- Keep metronome behavior independent from Auto Play, input judgement, scoring, combo, gauge, and input-latency compensation.
- Skip stale clicks after a delayed frame instead of producing a catch-up burst.
- Allow skins to override the two click sounds through the existing resource path.
- Degrade to silence rather than fail gameplay when either click asset cannot be loaded or played.

## Non-goals

- Count-in before gameplay.
- Eighth-note, triplet, or sixteenth-note subdivisions.
- A metronome volume setting, custom click selection, or per-song override.
- A gameplay hotkey for changing the setting.
- Visual beat lines or other notation changes.
- Practice loops or a broader practice-mode redesign.
- Procedural click synthesis.
- A second timing event bus, recurring timer, worker thread, or sample-accurate audio scheduler.
- Any of the unrelated audio settings previously bundled into the old HPA-13 description.

## User experience

The Drums category gains one item near the other playback controls:

```text
Metronome    OFF / ON
```

Description:

```text
Plays an accented click on each quarter-note beat during gameplay.
```

The value applies immediately to configuration and is used the next time `PerformanceStage` activates. The setting is deliberately frozen for the active performance; there is no mid-song toggle.

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

A quarter-note boundary at integer beat offset `n` therefore maps to:

```text
measureTick = n * 192.0 / measureBeats
            = n * 48.0 / measureLengthMultiplier
```

Generate offsets `n = 0, 1, 2, ...` while `n < measureBeats`. Offset zero is the accented measure-start marker. The next measure contributes its own accent, so the current measure never adds a second marker at tick `192`.

Representative grids:

| Multiplier | Musical length | Marker ticks |
| --- | --- | --- |
| `1.0` | 4 quarter notes | `0, 48, 96, 144` |
| `0.75` | 3 quarter notes | `0, 64, 128` |
| `1.5` | 6 quarter notes | `0, 32, 64, 96, 128, 160` |

The timing seam must support fractional internal ticks because an arbitrary valid multiplier can place a quarter-note boundary between integer DTX ticks. Fractional positions remain private to `ChartTimingMap`; runtime receives only resolved `double TimeMs` values.

For a non-integral musical length such as `2.5` quarter notes, generate offsets `0`, `1`, and `2`, then accent the next measure start after the final half beat. Do not round the measure length or invent a click for the incomplete quarter note.

### BPM changes

Each marker time is resolved through the compiled timing anchors. A BPM change at a measure boundary affects all later markers. A BPM change inside a measure affects only the elapsed interval after its authored position.

Do not derive beat times by evenly dividing two adjacent measure-line timestamps: an in-measure BPM change makes those intervals unequal.

### Play Speed and audio latency

`SongTimer.GetCurrentMs(GameTime)` returns rate-adjusted logical chart time through `PlaybackClock`. Scheduling markers against that clock automatically keeps the click track aligned at non-default Play Speed.

Player input judgement separately subtracts `AudioLatencyOffsetMs`. The metronome must not use that compensated clock. It is gameplay audio scheduled on the same raw chart timeline as notes, BGM events, progress, and completion.

## Approaches considered

### Finalized chart-owned beat markers — selected

`ChartTimingMap` resolves quarter-note boundaries while `ParsedChart.FinalizeChart()` builds an ordered marker list. Runtime consumes only absolute milliseconds.

This preserves the existing architecture: parser/finalization owns authored timing math, and gameplay owns playback scheduling.

### Recurring runtime timer — rejected

A timer based on base BPM would drift when channels `02`, `03`, or `08` change the authored timing. It would also require custom pause, resume, Play Speed, and lifecycle synchronization.

### Fixed markers every 48 DTX ticks — rejected

Forty-eight ticks represents one quarter note only when the measure multiplier is `1.0`. It is wrong for shortened and extended measures.

### Evenly divide each rendered measure interval — rejected

This can represent measure length but cannot represent a tempo change inside the measure. It also makes a visual model (`MeasureLine`) the source of musical timing.

### Schedule directly from `ChartTimingMap` during gameplay — rejected

That would add a runtime dependency on parser timing state and repeat position-to-time work every frame. Finalized immutable markers are simpler and match notes, BGM events, and measure lines.

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

No lane, score, rendered position, or redundant BPM metadata is needed. `IsMeasureStart` chooses the accented sound; `TimeMs` is the complete runtime contract.

### 2. `ChartTimingMap` enumerates quarter-note times

Extend `ChartTimingMap` with one internal beat-generation seam, for example:

```csharp
internal IReadOnlyList<BeatMarker> BuildBeatMarkers(int throughBar);
```

`throughBar` is inclusive and represents the highest occupied gameplay bar, not the extra terminal measure-line bar.

Responsibilities:

- read each measure's retained length multiplier, defaulting to `1.0`;
- enumerate integer quarter-note offsets while the offset is less than the measure's musical length;
- resolve fractional positions against the already compiled timing anchors;
- mark only offset zero as `IsMeasureStart`;
- return markers ordered by absolute time.

The current interval helper accepts an integer tick delta. Generalize only the private timing math needed by marker resolution to `double`. Keep the public authored-event lookup on integer `(bar, tick)` positions and do not broaden parser models to fractional ticks.

A private helper can resolve `bar + fractional tick` by selecting the last compiled anchor at or before that position and integrating the remaining fractional delta with the anchor's BPM and measure multiplier. No second map or interpolation table is required.

### 3. `ParsedChart.FinalizeChart()` owns marker publication

Add an ordered `BeatMarkers` collection to `ParsedChart`.

During finalization:

1. Clear existing markers together with measure lines and duration state.
2. Determine the highest normalized bar occupied by a note or BGM event as today.
3. Rebuild `TimingMap` through the existing terminal horizon.
4. Resolve notes and BGM events.
5. Generate beat markers for bars `0..highestOccupiedBar`.
6. Generate measure lines through `highestOccupiedBar + 1` as today.

An empty chart retains no beat markers. Timing directives without notes or BGM events do not create a playable metronome timeline. Repeated finalization must reproduce the same marker count, order, timestamps, and accents.

`ChartManager` remains unchanged because `PerformanceStage` already owns the finalized `ParsedChart`. Do not add beat-marker query APIs to `ChartManager` unless implementation proves they are required.

### 4. Pure `MetronomePlayer` scheduling component

Add `Stage/Performance/MetronomePlayer.cs` as a small stateful scheduler. It owns:

- the ordered marker list;
- the index of the next unresolved marker;
- a callback that plays the selected marker;
- a fixed maximum late-play tolerance.

Suggested shape:

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

The scheduler should not own `ISound` or MonoGame audio objects. `PerformanceStage` already owns resource loading and cleanup, while an injected callback keeps scheduler tests deterministic and avoids a new audio abstraction.

On each update:

1. Consume every marker whose `TimeMs <= currentChartTimeMs`.
2. Remember only the latest consumed marker.
3. If that marker is no more than `MaxLatePlaybackMs` late, invoke the callback once.
4. Otherwise skip it.

This policy handles the marker at chart time zero on the first active frame while preventing several accumulated clicks from firing together after a frame stall. The tolerance is measured in logical chart milliseconds so scheduling remains consistent with Play Speed.

`Reset()` sets the next index to zero. No rewind detector, seeking API, or generalized transport state is needed for this ticket; `PerformanceStage` explicitly resets the component when beginning a new run.

### 5. `PerformanceStage` integration

Freeze `ConfigData.MetronomeEnabled` during `OnActivate`, like the existing playback profile and Auto Play setting.

After chart finalization and only when the setting is enabled:

- load the regular and accented sounds;
- create `MetronomePlayer` with `_parsedChart.BeatMarkers`;
- inject a callback that chooses the sound by `IsMeasureStart` and plays it at `SEVolume / 100f`.

In `UpdateGameplay`, call the scheduler beside `ProcessBGMEvents(currentTimeMs)` inside the existing `_songTimer.IsPlaying` block:

```text
raw currentTimeMs
├── BGM event scheduling
├── metronome scheduling
├── autoplay/visual/progress/completion timing
└── compensated judgement clock (player input only)
```

Do not call it during READY or while loading. Pause naturally suppresses updates because `IsPlaying` is false and the logical clock is frozen. Cleanup removes sound references and clears the scheduler with the rest of the performance resources.

### 6. Persisted configuration

Add:

```csharp
public bool MetronomeEnabled { get; set; } = false;
```

Extend `IConfigManager` and `ConfigManager` using the existing `AutoPlay` / `NoFail` pattern:

- parse the `MetronomeEnabled` key;
- write it during `SaveConfig`;
- expose `SetMetronomeEnabled(bool)` and mark the deferred config save dirty.

Add one `ToggleConfigItem` to the Drums list in `ConfigStage`, placed after Pitch and before Auto Play.

No migration or compatibility alias is required. A config file without the key keeps the default Off value and writes the key on the next normal save.

### 7. Skin-overridable click sounds

Add constants to `SoundPath`:

```csharp
public const string MetronomeBeat = "Sounds/Metronome Beat.ogg";
public const string MetronomeAccent = "Sounds/Metronome Accent.ogg";
```

Include both in `SoundPath.GetAllSoundPaths()` so the built-in sound-pack inventory stays complete.

Add short, low-latency OGG assets at:

```text
System/CXNeon/Sounds/Metronome Beat.ogg
System/CXNeon/Sounds/Metronome Accent.ogg
```

The accent should be recognizably higher, stronger, or otherwise distinct, but both clips should be short enough not to overlap at common tempos. Custom skins may override the same relative paths through `IResourceManager`.

### 8. Failure handling

Sound loading and one-shot playback are best-effort. If one asset fails:

- retain the scheduler;
- log through the existing performance/resource diagnostic path;
- skip only the unavailable click type;
- continue normal gameplay.

Do not substitute a drum chip, synthesize a tone, or fail chart initialization. Marker generation itself is deterministic chart data and should not depend on sound availability.

## Lifecycle

```text
ConfigStage edit
    ↓ persisted MetronomeEnabled
PerformanceStage.OnActivate
    ↓ freeze setting
chart/audio initialization
    ↓ finalized BeatMarkers + optional sounds/player
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

### Chart timing tests

Cover marker generation for:

- a normal `1.0` measure: four markers, first accented;
- channel `02` multiplier `0.75`: three markers at the correct times;
- channel `02` multiplier `1.5`: six markers at the correct times;
- a non-integral musical length without rounding;
- BPM change at a measure boundary;
- BPM change inside a measure;
- multiple measures without duplicate boundary clicks;
- empty chart behavior;
- repeated finalization idempotence.

Use time assertions with a small floating-point tolerance.

### Scheduler tests

Cover:

- no click before the next marker;
- accent/regular marker forwarding;
- marker zero played on the first reasonably current update;
- multiple overdue markers produce at most the latest click;
- a marker beyond the late tolerance is skipped;
- reset replays from marker zero;
- an empty marker list is a no-op.

### Configuration and stage tests

Cover:

- default Off;
- config load/save round trip;
- typed setter behavior;
- Drums category registration;
- disabled activation does not load metronome sounds;
- enabled gameplay uses the raw song clock and existing SE volume;
- READY and paused states do not schedule clicks;
- stage cleanup releases loaded sound references;
- missing assets do not fail gameplay.

## Risks and trade-offs

### Frame-level audio submission

Clicks are submitted from the game update loop, so timing precision is limited by frame cadence and the platform audio backend. This matches current BGM-event and autoplay scheduling and is acceptable for the first implementation. A dedicated scheduler would add substantial lifecycle and cross-platform complexity without evidence that it is needed.

### Very short sound clips

A long click sample can overlap at high BPM or Play Speed. Keep the bundled assets short and dry. This is an asset-quality requirement, not a reason to add instance pooling or explicit stop logic.

### Fractional chart positions

Fractional ticks are necessary for exact quarter-note placement under arbitrary channel `02` multipliers. They remain an internal calculation detail; notes, BGM events, parser directives, and runtime APIs continue using their existing contracts.

## Acceptance criteria

- Settings → Drums exposes a persisted Metronome toggle that defaults to Off.
- Disabled gameplay loads and plays no metronome audio.
- Enabled gameplay accents each measure start and clicks each later quarter-note boundary.
- Markers remain correct across representative channel `02`, `03`, and `08` timing changes.
- Non-default Play Speed remains synchronized through the raw logical song clock.
- Loading, READY, pause, result, and unrelated stages do not emit clicks.
- Resume, restart, and stage re-entry neither duplicate clicks nor produce catch-up bursts.
- Missing click assets do not crash or block gameplay.
- Focused tests cover configuration, marker generation, scheduling, integration, and cleanup.
