# HPA-600 Full DTX Timing Map Design

**Issue:** [HPA-600](https://linear.app/cwchanap/issue/HPA-600/full-dtx-tempo-and-measure-length-timing-map-channels-020308)  
**Date:** 2026-08-09  
**Status:** Revised after design review

## Context

HPA-518 added gameplay measure lines while deliberately preserving CX's existing fixed timing model. That temporary model now lives behind `ChartTimeCalculator.CalculateTimeMs(bar, tick, bpm)` and assumes every measure is four quarter-note beats at one constant base BPM.

HPA-600 replaces that seam with the DTX timing behavior needed by gameplay:

- channel `02`: per-measure length multiplier;
- channel `03`: direct hexadecimal BPM changes;
- channel `08`: BPM-table references through `#BPMxx` definitions.

The change must move notes, BGM events, measure lines, and gameplay chart duration together. Fixing only one collection would create multiple incompatible clocks.

The current parser also has two migration constraints that the first design revision under-scoped:

1. `ParseFileContentAsync` permanently switches from header parsing to measure-data parsing after the first timeline row, so a `#BPMxx` definition that appears later in the file is currently invisible to header parsing.
2. Several tests either call `Note.CalculateTimeMs(...)` directly or seed `TimeMs` and then call `FinalizeChart()`. Once finalization always recomputes time, those fixtures must migrate to authored `Bar`/`Tick` positions.

## Goals

- Parse channels `02`, `03`, and `08` into one retained chart timing configuration.
- Support `#BPMxx` definitions whether they appear before or after the channel `08` row that references them.
- Preserve source-line ordering when channel `03` and channel `08` define tempo changes at the same `(bar, tick)`; the later source directive wins.
- Make one chart-owned `ChartTimingMap` the only `(bar, tick) -> TimeMs` authority.
- Recalculate every `Note.TimeMs`, `BGMEvent.TimeMs`, and `MeasureLine.TimeMs` from that map in one `ParsedChart.FinalizeChart()` pass.
- Recompute gameplay `ParsedChart.DurationMs` from finalized event times and make repeated finalization idempotent.
- Preserve CX's normalized `192` ticks per measure representation.
- Keep gameplay/rendering/audio consumers on resolved absolute `TimeMs`; do not introduce runtime timing-map dependencies.
- Migrate hand-built tests to the same bar/tick + finalization lifecycle used by production parsing.

## Non-goals

- STOP events or other BMS/DTX timing extensions outside channels `02`, `03`, and `08`.
- Beat-line rendering, metronome behavior, or other HPA-518-adjacent UI work.
- Judgement-window, scroll, play-speed, audio-device scheduling, scoring, persistence, or skin changes.
- Runtime re-clocking or a second timing event bus.
- Porting DTXManiaNX parser architecture.
- Min/max/average BPM metadata for song selection.
- `#BASEBPM` support; CX has no existing use of that directive.
- `#BPM00` compatibility alias. Base BPM remains the existing exact `#BPM` directive for this ticket.
- Making `SongChart.Duration` timing-map accurate. `ParseSongEntitiesAsync` / `CalculateDurationAsync` retain their existing base-BPM approximation in HPA-600. This is an explicit known limitation of the metadata/song-library path, not a second gameplay clock. If song-select duration parity becomes important, handle it in a separate ticket rather than expanding this parser/playback migration.

## DTX Timing Semantics

### Normalized chart position

CX stores timeline events as `(bar, tick)` with `tick` normalized to `0..191`. Keep that representation.

Channel `02` changes the musical duration represented by the full `192` ticks; it does not change the stored tick coordinate system.

### Channel 02: measure-length multiplier

For `#mmm02:value`, `value` is a positive invariant-culture decimal multiplier of the standard four-beat measure.

At 120 BPM:

- `1.0` -> 4 beats -> `2000 ms`;
- `0.5` -> 2 beats -> `1000 ms`;
- `0.75` -> 3 beats -> `1500 ms`;
- `1.5` -> 6 beats -> `3000 ms`.

A multiplier affects only its own measure. Missing or invalid values use `1.0`.

### Channel 03: direct BPM

Channel `03` uses the standard pair grid. Each non-`00` pair is parsed as a hexadecimal integer BPM.

Example:

```text
#00003:00F0
```

The second pair is at normalized tick `96` and changes tempo to hexadecimal `F0` = decimal `240` BPM.

### Channel 08: BPM-table reference

Definitions such as:

```text
#BPM01:180.5
#BPMAA:210.25
```

map an opaque, uppercase two-character object code to a positive numeric BPM. Object codes are retained as strings; do not parse channel `08` IDs as hexadecimal numbers.

A non-`00` pair in channel `08` references that definition.

Example:

```text
#00108:0001
#BPM01:180.5
```

must resolve successfully even though the definition appears after the timeline row.

### Duplicate tempo positions

When multiple channel `03` / `08` directives target the same `(bar, tick)`, the directive appearing later in the source file wins.

This rule applies across channel types, not separately per channel. Therefore parser resolution must preserve source order rather than applying all channel `08` references after all direct channel `03` changes without ordering information.

## Approaches Considered

### Compiled chart-owned timing map — selected

Add one `ChartTimingMap` owned by `ParsedChart`. The parser records measure multipliers and resolved tempo changes. `ParsedChart.FinalizeChart()` compiles the timing map through the terminal gameplay measure and resolves all retained event times.

The compiled representation stores a small ordered set of timing anchors at measure starts and tempo-change positions. Repeated lookups are then cheap and deterministic.

This keeps parsing syntax-specific, timing math format-agnostic, and gameplay runtime unchanged.

### Walk directives from bar zero for every event — rejected

This is conceptually simple but repeats cumulative timing work for every note, BGM event, and measure line. It also grows the temporary `ChartTimeCalculator` instead of replacing it with the intended long-term seam.

### Assign absolute time while parsing — rejected

Correct timing must not depend on source-file line order. A channel `08` reference may appear before its `#BPMxx` definition, and playable rows may appear before timing rows for the same measure. Finalization is the correct point to resolve absolute time after the full chart is known.

## Chosen Architecture

### 1. `ChartTimingMap` replaces `ChartTimeCalculator`

Create:

```text
DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs
```

Conceptual interface:

```csharp
public sealed class ChartTimingMap
{
    public const int TicksPerMeasure = 192;

    internal void SetMeasureLength(int bar, double multiplier);
    internal void SetTempoChange(int bar, int tick, double bpm);
    internal void Rebuild(double baseBpm, int throughBar);

    public double CalculateTimeMs(int bar, int tick);
}
```

Responsibilities:

- retain valid per-bar measure multipliers;
- retain one resolved BPM per exact `(bar, tick)`;
- use last `SetTempoChange` call for duplicate positions;
- compile measure-start and tempo-change anchors in `Rebuild`;
- calculate absolute milliseconds from compiled anchors;
- replace compiled anchors on every rebuild.

Delete `ChartTimeCalculator.cs` once all production callers migrate. Do not keep two timing authorities.

### 2. Timing-map compilation

Inputs:

- base BPM from `ParsedChart.Bpm`;
- per-bar measure multiplier, default `1.0`;
- resolved tempo changes keyed by `(bar, tick)`;
- inclusive `throughBar` covering the terminal measure-line bar.

For each measure from `0` through `throughBar`:

1. Select the measure multiplier.
2. Add a measure-start anchor at `(bar, 0)` using the current absolute time and current BPM.
3. Walk tempo changes in ascending tick order.
4. Advance from the previous tick to the change tick using the BPM active over that interval.
5. Apply the new BPM at the change position and add/replace the anchor there.
6. Advance the remaining ticks to `192` to obtain the next measure start.
7. Carry the final BPM into the next measure.

Interval math:

```text
beats = (tickDelta / 192.0) * 4.0 * measureLengthMultiplier
milliseconds = beats * (60000.0 / bpm)
```

Because every measure has a start anchor, `CalculateTimeMs(bar, tick)` only needs the last anchor in that same measure at or before the requested tick.

A tempo change at tick `0` replaces the effective BPM of the measure-start anchor for all subsequent ticks in that measure.

### 3. Parser syntax collection and late `#BPMxx` support

The parser should not resolve channel `08` while reading the row.

For each encoding attempt, create fresh parser-local state alongside the existing WAV/volume/pan dictionaries:

```text
bpmDefinitions: Dictionary<string, double>
pendingTempoDirectives: List<PendingTempoDirective>
sourceSequence: increasing integer
```

`PendingTempoDirective` is parser-private and contains only what is needed to preserve source order:

```text
Sequence
Bar
Tick
Kind: DirectBpm | ReferencedBpm
DirectBpm or ReferenceId
```

#### Extended BPM definitions are recognized anywhere

Before the current `inDataSection` header-vs-measure routing, attempt to recognize a valid `#BPMxx:value` definition on every non-comment line.

If recognized:

- normalize the two-character suffix to uppercase;
- parse a positive invariant-culture `double`;
- store/replace it in `bpmDefinitions`;
- consume the line and continue.

This narrow pre-routing check is required because the existing parser stops calling `ParseHeaderCommand` after the first timeline row. Do not generalize all header commands to late-header support in this ticket.

Exact `#BPM:value` remains handled by the existing base-BPM header path and must not be consumed as an extended definition.

#### Channel 02

After measure/channel parsing and before BGM/drum routing:

- parse the whole row body as a positive invariant-culture `double`;
- call `TimingMap.SetMeasureLength(measure, multiplier)`;
- return from measure-data parsing.

Duplicate channel `02` rows for one measure use the last parsed valid multiplier.

#### Channels 03 and 08

Parse pair positions using the same normalized tick formula already used by notes/BGM events.

For each non-`00` pair:

- channel `03`: parse the pair as a positive hexadecimal integer BPM and append a `DirectBpm` pending directive with the current source sequence;
- channel `08`: normalize the pair as an uppercase object-code string and append a `ReferencedBpm` pending directive with the current source sequence.

Do **not** call `TimingMap.SetTempoChange` from either channel while scanning the file.

After `ParseFileContentAsync` completes successfully for the encoding attempt, resolve pending tempo directives in ascending `Sequence` order:

- direct BPM -> use its stored numeric BPM;
- referenced BPM -> look up the final `bpmDefinitions` table;
- unresolved/invalid references -> ignore;
- valid directive -> call `TimingMap.SetTempoChange(bar, tick, bpm)`.

Applying both channel types in original source order preserves the cross-channel last-source-line-wins rule while still allowing channel `08` references to definitions that occur later in the file.

Like WAV dictionaries, both BPM parser collections are recreated for each encoding attempt so failed-attempt state cannot leak.

### 4. `ParsedChart.FinalizeChart()` is the only absolute-time pass

Today `AddNote` / `AddBGMEvent` assign absolute time before the complete timing configuration is known. HPA-600 removes that responsibility.

After the migration:

- `AddNote(...)` adds the note and updates lane statistics only;
- `AddBGMEvent(...)` adds the event only;
- neither method updates `DurationMs`;
- `ParsedChart` owns one `TimingMap`;
- `FinalizeChart()` finds the highest occupied bar across notes and BGM events;
- if there are retained events, rebuild through `highestOccupiedBar + 1`;
- overwrite every note and BGM event `TimeMs` from `TimingMap.CalculateTimeMs(...)`, regardless of any pre-existing value;
- clear/regenerate measure lines for bars `0..highestOccupiedBar + 1` using the same map;
- sort notes and BGM events by finalized time;
- reset and recompute `DurationMs` from the latest finalized note/BGM event plus the existing `500 ms` buffer.

For an event at bar `0`, tick `0`, finalized time is legitimately `0`. Zero is never an "uncalculated" sentinel.

If no note or BGM event exists:

- `MeasureLines` stays empty;
- `DurationMs` is `0`;
- timing directives alone do not create gameplay duration.

Repeated finalization produces the same event times, measure lines, ordering, and duration.

### 5. `Note` and `BGMEvent` become position/data models

Remove their fixed-clock `CalculateTimeMs(double bpm)` production API and stale formula comments.

They retain:

- `Bar` / `Tick` authored position;
- lane/channel/value or WAV data;
- finalized `TimeMs` populated by `ParsedChart.FinalizeChart()`.

Tests that need timing should exercise `ChartTimingMap` directly or finalize a `ParsedChart`; they should not recreate a per-event fixed clock.

### 6. `ChartManager` consumes finalized state only

Remove the constructor fallback that recalculates notes when `TimeMs == 0`.

Production already calls `FinalizeChart()` before constructing `ChartManager`. Tests must follow the same lifecycle when they are modeling a parsed chart.

`ChartManager.Bpm` remains the base `#BPM` value for existing display/statistics use. Runtime queries do not receive `ChartTimingMap`.

## Test Fixture Migration Contract

This migration intentionally invalidates tests that use `TimeMs` as authored chart input and then call `FinalizeChart()`.

Mandatory rule:

> Any hand-built `ParsedChart` that is finalized must author note/BGM positions with `Bar` and `Tick`. `FinalizeChart()` owns `TimeMs` and may overwrite every pre-seeded value.

Use the existing production-style helper pattern where charts are populated with bar/tick positions, finalized, and only then passed to `ChartManager`.

For pure runtime simulations that currently call `Note.CalculateTimeMs(...)`, either:

- build/finalize a small `ParsedChart` and take its resolved notes; or
- test `ChartTimingMap` directly when the subject is timing math.

Do not preserve non-zero pre-seeded `TimeMs` through finalization. That would restore dual timing authorities and make real time-zero events ambiguous again.

The implementation plan must scan the full test tree for both direct timing-method calls and `TimeMs`-seeded finalized charts before Task 2 is considered green.

Known affected files include:

- `DTXMania.Test/Stage/Performance/TimingVerificationTest.cs`;
- `DTXMania.Test/Stage/Performance/AutomatedPlaySimulationTests.cs`;
- `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`.

The scan, not this list, is authoritative because other chart helpers may use the same pattern.

## Data Flow

```text
DTX text
  -> DTXChartParser
       -> ParsedChart.Bpm                         (#BPM)
       -> parser-local bpmDefinitions            (#BPMxx anywhere)
       -> parser-local pending tempo directives  (03 / 08 + source order)
       -> ParsedChart.TimingMap
            -> measure multiplier by bar         (02)
       -> Notes / BGMEvents with bar + tick
  -> successful end-of-file parse
       -> resolve pending 03/08 directives in source order
       -> TimingMap.SetTempoChange(...)
  -> ParsedChart.FinalizeChart
       -> TimingMap.Rebuild(baseBpm, terminalBar)
       -> resolve Note.TimeMs
       -> resolve BGMEvent.TimeMs
       -> regenerate MeasureLine.TimeMs
       -> sort events
       -> recompute gameplay DurationMs
  -> ChartManager / PerformanceStage / audio scheduling
       -> consume finalized TimeMs only
```

## Validation and Error Behavior

- Non-positive base BPM retains the existing failure behavior when the map is rebuilt.
- Invalid/non-positive channel `02` values are ignored; that bar uses multiplier `1.0`.
- Channel `03` pair `00` means no change; malformed/non-positive direct BPM pairs are ignored.
- Invalid/non-positive `#BPMxx` definitions are not added.
- Channel `08` pair `00` means no change.
- Missing channel `08` references are ignored after the complete file has been scanned.
- `#BPMxx` object IDs are case-normalized opaque two-character strings; IDs such as `AA` must work and must not be treated as hex BPM values.
- Tempo changes persist until another tempo change.
- Channel `02` affects only its own measure.
- Duplicate tempo positions resolve by source order across channels `03` and `08`.
- No new production exceptions are introduced for malformed optional timing rows/references.

Debug-only diagnostics for ignored timing syntax are acceptable but not required.

## Performance

For `B` compiled measures and `T` tempo changes:

- rebuild: `O(B + T log T)` or equivalent small ordered processing;
- lookup: binary search over compiled anchors, `O(log A)`, where `A` is the anchor count.

The chart is static after parsing. Do not add a second cache/index beyond compiled anchors.

## Testing Strategy

### `ChartTimingMapTests`

Cover:

- base-BPM-only parity with the old fixed 4/4 clock;
- shortened measure (`0.5`);
- extended measure (`1.5`);
- halfway tempo change;
- tempo persistence across measures;
- measure multiplier + tempo change in one measure;
- tempo change at tick `0`;
- duplicate position uses last value;
- repeated `Rebuild(...)` is deterministic;
- invalid base BPM fails.

Representative combined case at base 120 BPM:

```text
measure 0 multiplier = 0.5
measure 0 changes to 240 BPM at tick 96

0 -> 96: 1 beat at 120 BPM = 500 ms
96 -> 192: 1 beat at 240 BPM = 250 ms
bar 1 start = 750 ms
```

### `ParsedChartTests`

Cover:

- `AddNote` / `AddBGMEvent` do not assign time or duration;
- finalization overwrites stale pre-seeded `TimeMs` from bar/tick;
- notes, BGM events, and measure lines use one map;
- duration is max finalized note/BGM time + `500 ms`, including a lone event at time zero;
- repeated finalization does not add another buffer;
- sorting occurs after recalculation;
- empty chart stays zero-duration with no measure lines.

### `DTXChartParserTests`

Use temporary inline files to cover:

- channel `02` shortened measure;
- channel `02` extended measure;
- channel `03` direct BPM change;
- channel `08` with definition before the row;
- channel `08` with `#BPMxx` definition **after** the row;
- `#BPMAA` referenced by `AA`, proving the ID path is not hex-only;
- channel `03` and `08` targeting the same `(bar, tick)`, proving later source directive wins;
- note + BGM + measure-line alignment across a tempo-changing chart;
- malformed/missing timing values retain safe fallback behavior.

### Fixture/regression tests

Before completing the finalization migration, scan the full test tree for:

```bash
rg -n 'CalculateTimeMs\(' DTXMania.Test
rg -n 'FinalizeChart\(\)' DTXMania.Test
rg -n 'TimeMs\s*=' DTXMania.Test
```

Inspect every finalized-chart fixture that seeds `TimeMs`. Convert it to authored bar/tick positions or remove finalization only when the test intentionally models already-finalized runtime data.

At minimum, migrate the known Performance timing/simulation/deterministic helpers surfaced during review.

## Expected Files

Create:

- `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`
- `DTXMania.Test/Song/ChartTimingMapTests.cs`

Modify:

- `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`
- `DTXMania.Game/Lib/Song/Components/Note.cs`
- `DTXMania.Game/Lib/Song/Components/BGMEvent.cs`
- `DTXMania.Game/Lib/Song/Components/ChartManager.cs`
- `DTXMania.Game/Lib/Song/DTXChartParser.cs`
- `DTXMania.Test/Song/ParsedChartTests.cs`
- `DTXMania.Test/Song/NoteTests.cs`
- `DTXMania.Test/Song/BGMEventTests.cs`
- `DTXMania.Test/Song/ChartManagerTests.cs`
- `DTXMania.Test/Song/DTXChartParserTests.cs`
- affected test helpers discovered by the fixture migration scan, including the known Performance files.

Delete:

- `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`
- `DTXMania.Test/Song/ChartTimeCalculatorTests.cs`

No renderer, stage production, project, skin, workflow, or DTXManiaNX file change is expected.

## Acceptance Criteria

- A chart using only base `#BPM` retains current note/BGM/measure timing.
- Channel `02` correctly shifts event and later measure times for shortened and extended measures.
- Channel `03` correctly changes tempo within a measure and persists afterward.
- Channel `08` resolves positive fractional/extended BPM values from `#BPMxx`.
- A channel `08` reference resolves when its definition appears later in the source file.
- IDs such as `AA` work as channel `08` object codes.
- Same-position `03`/`08` changes use later source order, independent of channel type.
- Notes, BGM events, and measure lines at the same authored position resolve to the same `TimeMs`.
- `ParsedChart.FinalizeChart()` always recalculates event times and produces deterministic duration on repeated calls.
- `ChartManager` never uses `TimeMs == 0` as an uncalculated sentinel.
- Production and hand-built finalized charts use the same bar/tick-authored lifecycle.
- Gameplay production renderer/stage/audio scheduling code does not require timing-map changes.
- `SongChart.Duration` remains explicitly documented as approximate base-BPM metadata behavior outside this ticket.
- Focused timing/parser tests, affected Song + Performance regressions, the full Mac-safe test suite, and Mac game build pass before implementation is considered complete.
