# HPA-600 Full DTX Timing Map Design

**Issue:** [HPA-600](https://linear.app/cwchanap/issue/HPA-600/full-dtx-tempo-and-measure-length-timing-map-channels-020308)  
**Date:** 2026-08-09  
**Status:** Proposed for implementation

## Context

HPA-518 added gameplay measure lines while intentionally preserving the timing model already used by notes and BGM events. That model now lives behind `ChartTimeCalculator.CalculateTimeMs(bar, tick, bpm)` and assumes every measure is four quarter-note beats at one constant base BPM.

That assumption is not sufficient for DTX/BMS timing data. The parser currently retains channel `01` BGM events and playable drum channels, but discards timing channels that change how `(bar, tick)` maps to elapsed time:

- channel `02`: measure-length multiplier for one measure, such as `0.75` for a measure that is 75% of the normal four-beat length;
- channel `03`: direct BPM changes whose two-character values are hexadecimal BPM integers;
- channel `08`: BPM-table references whose two-character values resolve through `#BPMxx` definitions, allowing fractional BPM values and values beyond channel `03`'s byte range.

The HPA-518 review correctly identified that changing only measure-line timing would be wrong: notes, BGM events, measure boundaries, duration, and every runtime consumer must share one timeline.

## Goals

- Parse DTX timing channels `02`, `03`, and `08` into retained chart timing state.
- Resolve `#BPMxx` definitions used by channel `08`.
- Make one chart-owned timing map the source of truth for converting `(bar, tick)` to absolute milliseconds.
- Recalculate every `Note.TimeMs`, `BGMEvent.TimeMs`, and `MeasureLine.TimeMs` from that map in one finalization pass.
- Preserve the existing `192`-tick normalized position within a measure so note/BGM parsing and render positions do not need a new coordinate system.
- Recompute `DurationMs` from the finalized timeline rather than from provisional times assigned while parsing.
- Keep gameplay/rendering/audio consumers unchanged: they should continue to consume already-resolved absolute `TimeMs` values.
- Keep the implementation small enough for one focused migration task and straightforward for agentic workers to review.

## Non-goals

- Beat-line rendering, metronome behavior, or other HPA-518-adjacent UI work.
- DTX timing directives other than channels `02`, `03`, and `08`.
- STOP events or other BMS extensions not already requested by HPA-600.
- Changing judgement windows, scroll math, play-speed behavior, audio-device scheduling, scoring, or persistence.
- Computing min/max/average BPM metadata for song selection UI. `ParsedChart.Bpm` and `ChartManager.Bpm` continue to represent the chart's base `#BPM` value.
- Rewriting the general DTX parser or porting DTXManiaNX parser architecture.
- Adding a second runtime clock or recalculating timing during gameplay.
- Preserving the current `Note.CalculateTimeMs(double)` / `BGMEvent.CalculateTimeMs(double)` fixed-clock API if it conflicts with the new single-source timing model.

## Format Semantics Used by This Design

### Normalized chart position

CX already represents timeline positions as:

```text
(bar, tick)
```

where `tick` is normalized to `0..191` by the parser. Keep that representation. A tick remains a fraction of the current measure; channel `02` changes how much musical time the full 192-tick measure represents, not the number of normalized ticks stored on notes.

### Channel 02: measure-length multiplier

For `#mmm02:value`, `value` is a positive decimal multiplier of the standard four-beat measure.

Examples at 120 BPM:

- `1.0` -> four beats -> `2000 ms`;
- `0.5` -> two beats -> `1000 ms`;
- `0.75` -> three beats -> `1500 ms`;
- `1.5` -> six beats -> `3000 ms`.

This is intentionally modeled as a multiplier, not as a numerator/denominator pair. Measures without channel `02` use `1.0`.

### Channel 03: direct BPM

Channel `03` uses the normal two-character timeline grid. Each non-`00` pair is parsed as a hexadecimal integer BPM.

Example:

```text
#00003:00F0
```

has two positions in measure 0 and changes the BPM to hexadecimal `F0` = decimal `240` at tick `96`.

### Channel 08: BPM-table reference

Header definitions such as:

```text
#BPM01:180.5
```

are retained in a parser-local BPM definition table. A non-`00` pair in channel `08` resolves that exact uppercase identifier and inserts the resolved numeric BPM at the pair's normalized tick.

Example:

```text
#BPM01:180.5
#00108:0001
```

changes to `180.5 BPM` halfway through measure 1.

The normalized timing map stores the resolved BPM value, not the original `#BPMxx` identifier. That keeps DTX syntax concerns in `DTXChartParser` and leaves timing math format-agnostic.

## Approaches Considered

### Compiled chart-owned timing map (selected)

Add one `ChartTimingMap` owned by `ParsedChart`. The parser records measure multipliers and normalized tempo changes into it. `ParsedChart.FinalizeChart()` compiles the map through the terminal measure, then resolves every retained chart event from that compiled timeline.

The compiled map stores a small ordered set of timing anchors: every measure start plus every tempo-change position. Each anchor knows its `(bar, tick)`, absolute `TimeMs`, active BPM, and the current measure-length multiplier. Looking up an event finds the last anchor at or before that position and advances only the remaining fraction of that measure.

**Why selected:** one source of truth, deterministic finalization, fast repeated lookups, no gameplay-time work, and a clear replacement for HPA-518's temporary fixed-clock calculator.

### Walk raw timing directives for every event

Keep `ChartTimeCalculator` static and pass all measure/BPM directives into each call, integrating from bar 0 every time a note, BGM event, or measure line asks for `TimeMs`.

**Rejected:** simple initially but repeats the same cumulative work for every event. Song loading already performs enough per-chart work that intentionally making timing resolution O(events x bars/tempo-changes) is not a good long-term seam.

### Assign absolute time while parsing each line

Maintain a mutable parser clock and calculate `TimeMs` as timeline rows are read.

**Rejected:** DTX timeline semantics should not depend on source-file line order. It also makes channel `02`, `03`, and `08` interact with playable channels inside parser control flow and makes later recalculation difficult. Finalization is already the natural point where the complete chart is available.

## Chosen Architecture

### 1. Replace the temporary fixed-clock calculator with `ChartTimingMap`

Create:

```text
DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs
```

`ChartTimingMap` owns only chart-position timing concerns. Its public surface should stay small; parser mutation methods remain `internal`.

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

`SetTempoChange` stores one resolved BPM per exact `(bar, tick)`. If the source defines more than one tempo change at the same position, the last parsed definition wins. This keeps behavior deterministic without introducing a second event-order abstraction.

`Rebuild(...)` replaces previously compiled anchors so repeated `ParsedChart.FinalizeChart()` calls remain deterministic.

Delete `ChartTimeCalculator.cs` once all callers move to `ChartTimingMap`. HPA-518 introduced that static helper specifically as a temporary seam before a complete timing map existed; retaining both abstractions after HPA-600 would duplicate responsibility.

### 2. Timing-map compilation algorithm

Compilation is sequential and bounded by the highest measure needed by the finalized chart.

Inputs:

- base BPM from `ParsedChart.Bpm`;
- per-bar measure-length multipliers, default `1.0`;
- tempo changes keyed by `(bar, tick)`;
- `throughBar`, inclusive, which must include the terminal measure-line bar.

For each bar from `0` through `throughBar`:

1. Read that bar's multiplier or `1.0`.
2. Add a measure-start anchor at `(bar, 0)` with the current absolute time and BPM.
3. Walk the bar's tempo changes in ascending tick order.
4. Before each change, advance time by the fraction of the measure between the previous tick and the change tick using the BPM active over that interval.
5. Update the current BPM at the change position and add/replace an anchor at that position.
6. Advance the remaining ticks to `192` to obtain the next measure's start time.
7. Carry the final BPM into the next measure.

For an interval inside one measure:

```text
beats = ((endTick - startTick) / 192.0) * 4.0 * measureLengthMultiplier
milliseconds = beats * (60000.0 / bpm)
```

A lookup for `(bar, tick)` finds the last compiled anchor in that same bar at or before `tick`, then applies the same interval formula from the anchor tick to the requested tick.

Because compilation adds every measure start, a lookup never needs to integrate across multiple measures.

### 3. Parser changes remain syntax-focused

Extend `DTXChartParser` without changing playable-note parsing.

#### Header parsing

Keep the existing exact `#BPM` handling for the base BPM. Before falling through to WAV/volume/pan parsing, recognize `#BPMxx` definitions where the identifier suffix is non-empty and store positive numeric values in a case-normalized dictionary for the current encoding attempt.

Like WAV/volume/pan parser state, the BPM-definition dictionary must be recreated for each encoding attempt so a failed attempt cannot leak state into the next attempt.

#### Measure-data parsing

After `measure` and `channel` are decoded but before the current BGM/drum-lane branches:

- channel `0x02`: parse the whole row body as an invariant-culture positive `double` and call `TimingMap.SetMeasureLength(measure, multiplier)`;
- channel `0x03`: parse the row as two-character pairs, skip `00`, parse each pair as hexadecimal BPM, normalize its tick exactly as note/BGM parsing does, and call `TimingMap.SetTempoChange(...)` for positive values;
- channel `0x08`: parse pairs the same way, skip `00`, resolve each uppercase pair against the per-attempt `#BPMxx` table, and call `TimingMap.SetTempoChange(...)` with the resolved positive BPM.

Then return from the timing-channel branch so timing data never reaches drum-lane lookup.

No separate raw `TempoChange` collection on `ParsedChart` is required; `ChartTimingMap` is the retained structured representation.

### 4. `ParsedChart.FinalizeChart()` becomes the only absolute-time resolution pass

Today `AddNote` and `AddBGMEvent` calculate timing immediately when `TimeMs == 0`, which means parsing assigns absolute time before all chart timing directives have been processed. Remove that responsibility.

After HPA-600:

- `AddNote(...)` adds the note and updates lane counts only;
- `AddBGMEvent(...)` adds the event only;
- neither method updates `DurationMs`;
- `FinalizeChart()` determines the highest occupied bar across notes and BGM events;
- if the chart has retained events, rebuild `TimingMap` through `highestOccupiedBar + 1` so the terminal measure boundary is calculable;
- recalculate every note and BGM event from `TimingMap.CalculateTimeMs(bar, tick)` regardless of their previous `TimeMs` value;
- regenerate all measure lines from bar `0` through `highestOccupiedBar + 1` using tick `0` on the same map;
- sort notes and BGM events by recalculated `TimeMs`;
- recompute `DurationMs` from the maximum finalized note/BGM event time plus the existing `500 ms` end buffer.

If no note or BGM event exists, keep `MeasureLines` empty and `DurationMs = 0`. Timing directives alone do not create gameplay duration.

This intentionally makes chart finalization idempotent for timing and duration. Repeated finalization produces the same `TimeMs`, measure lines, ordering, and duration instead of adding the duration buffer again.

### 5. `Note` and `BGMEvent` become position/data models

Remove their fixed-clock `CalculateTimeMs(double bpm)` methods, or otherwise remove all production use of them. They should retain:

- raw chart position (`Bar`, `Tick`);
- channel/value data;
- resolved `TimeMs` populated by `ParsedChart.FinalizeChart()`.

This keeps musical timeline logic out of individual event models and prevents future callers from accidentally bypassing the timing map with only a base BPM.

Update XML comments that still describe the old fixed formula.

### 6. `ChartManager` consumes only finalized chart state

Remove the constructor fallback that recalculates any note whose `TimeMs == 0` using the base BPM. A real event at bar `0`, tick `0` legitimately has `TimeMs == 0`, so zero is not a valid "not calculated" sentinel once finalization is authoritative.

Production parsing already calls `ParsedChart.FinalizeChart()` before constructing runtime state. Unit tests that manually build `ParsedChart` objects should follow that production lifecycle.

`ChartManager.Bpm` remains the base BPM for existing statistics/display purposes. No current runtime query needs a `ChartTimingMap` reference after finalization.

## Data Flow

```text
DTX text
  -> DTXChartParser
       -> ParsedChart.Bpm                    (#BPM)
       -> parser-local BPM definitions       (#BPMxx)
       -> ParsedChart.TimingMap
            -> measure multiplier by bar     (channel 02)
            -> resolved BPM by bar/tick      (channel 03 / 08)
       -> Notes / BGMEvents with bar + tick
  -> ParsedChart.FinalizeChart
       -> TimingMap.Rebuild(baseBpm, terminalBar)
       -> resolve Note.TimeMs
       -> resolve BGMEvent.TimeMs
       -> regenerate MeasureLine.TimeMs
       -> sort events
       -> recompute DurationMs
  -> ChartManager / PerformanceStage / audio scheduling
       -> consume resolved TimeMs only
```

## Validation and Error Behavior

Keep malformed timing handling narrow and predictable:

- non-positive base BPM retains the current failure behavior when finalization attempts to build a timeline;
- channel `02` values that are missing, non-numeric, zero, or negative are ignored and the measure falls back to multiplier `1.0`;
- channel `03` pair `00` means no change; malformed/non-positive pairs are ignored;
- invalid/non-positive `#BPMxx` definitions are not added to the definition table;
- channel `08` references missing from the definition table are ignored;
- tempo changes at the same `(bar, tick)` use last-parsed-wins semantics;
- a tempo change persists into later measures until another change replaces it;
- channel `02` affects only its own measure and does not persist;
- no new exceptions are introduced for ordinary unsupported/missing timing references.

Debug-only diagnostics may be added for ignored timing values if useful, but production logging infrastructure is out of scope.

## Performance Characteristics

The map is compiled once per finalization. For a chart with `B` relevant bars and `T` tempo changes, compilation is O(B + T) after grouping/sorting tempo changes. Each note/BGM/measure-line lookup should be O(log A) or O(log T_bar) depending on the internal anchor representation, where `A` is the small compiled-anchor count.

Do not add caching beyond the compiled anchors. The chart is static after parsing, and the expected event counts do not justify a more complex indexing layer.

## Testing Strategy

### `ChartTimingMapTests`

Add focused math tests independent of parsing:

- base BPM only reproduces current 4/4 results;
- shortened measure (`0.5`) changes the following measure start correctly;
- extended measure (`1.5`) changes the following measure start correctly;
- direct BPM change halfway through a measure integrates the two tempo segments correctly;
- tempo change persists into the next measure;
- measure multiplier and tempo change combine correctly in the same measure;
- repeated `Rebuild(...)` produces the same results;
- invalid base BPM is rejected;
- duplicate tempo changes at one position use the last value.

Representative combined case at base 120 BPM:

```text
measure 0 multiplier = 0.5
measure 0 tempo changes to 240 BPM at tick 96

0 -> 96: 1 beat at 120 BPM = 500 ms
96 -> 192: 1 beat at 240 BPM = 250 ms
bar 1 start = 750 ms
```

### `DTXChartParserTests`

Use temporary inline DTX files, matching the current parser-test style, to prove syntax integration:

- channel `02` parses a decimal multiplier and shifts note/BGM/measure-line times;
- channel `03` parses hexadecimal direct BPM at the correct pair-derived tick;
- `#BPMxx` + channel `08` resolves a fractional BPM value;
- unresolved channel `08` references do not crash and do not change tempo;
- a sparse chart with timing changes in an otherwise empty measure still applies those changes to later notes;
- notes, BGM events, and measure lines at the same chart position resolve to identical `TimeMs`.

### `ParsedChartTests`

Update current tests to reflect deferred final timing:

- `AddNote` / `AddBGMEvent` no longer calculate `TimeMs` or duration;
- `FinalizeChart` recalculates all event times from the timing map even if a test pre-populated an incorrect `TimeMs`;
- `FinalizeChart` recomputes duration after tempo/measure changes;
- `FinalizeChart` is idempotent for `DurationMs` and measure lines;
- terminal measure lines still do not extend duration.

### `NoteTests`, `BGMEventTests`, and `ChartTimeCalculatorTests`

Remove or rewrite tests that directly exercise the fixed base-BPM formula. Replace `ChartTimeCalculatorTests` with `ChartTimingMapTests`. Keep model tests for construction, lane names, string formatting, and other non-timing responsibilities.

### `ChartManagerTests`

Ensure every helper chart calls `FinalizeChart()`. Add one guard test proving a finalized chart containing a real time-zero note is copied without any constructor-side recalculation.

No renderer/stage test changes are expected because those layers already operate on absolute `TimeMs` and are intentionally outside this migration.

## Expected File Changes

**Create:**

- `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`
- `DTXMania.Test/Song/ChartTimingMapTests.cs`

**Delete:**

- `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`
- `DTXMania.Test/Song/ChartTimeCalculatorTests.cs`

**Modify:**

- `DTXMania.Game/Lib/Song/DTXChartParser.cs`
- `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`
- `DTXMania.Game/Lib/Song/Components/Note.cs`
- `DTXMania.Game/Lib/Song/Components/BGMEvent.cs`
- `DTXMania.Game/Lib/Song/Components/ChartManager.cs`
- `DTXMania.Test/Song/DTXChartParserTests.cs`
- `DTXMania.Test/Song/ParsedChartTests.cs`
- `DTXMania.Test/Song/NoteTests.cs`
- `DTXMania.Test/Song/BGMEventTests.cs`
- `DTXMania.Test/Song/ChartManagerTests.cs`

No project-file, renderer, stage, layout, skin, E2E fixture, or legacy `DTXManiaNX` change is expected.

## Acceptance Criteria

- Channel `02` measure-length multipliers affect note, BGM, and measure-line timing in the target measure and all later absolute times.
- Channel `03` direct BPM changes take effect at their authored in-measure position and persist until replaced.
- Channel `08` resolves `#BPMxx` definitions, including fractional BPM values, and takes effect at the authored position.
- Notes, BGM events, and measure lines use exactly the same timing map and remain aligned across tempo and measure-length changes.
- Timing changes in otherwise empty measures still affect later events.
- `DurationMs` reflects finalized map-driven event timing plus the existing 500 ms buffer and is stable across repeated finalization.
- `ChartManager` performs no base-BPM fallback timing calculation.
- Existing constant-4/4 charts retain their current timing within floating-point tolerance.
- Focused song tests, the complete Mac-safe test suite, and the Mac game build pass.

## References

- HPA-600 issue: <https://linear.app/cwchanap/issue/HPA-600/full-dtx-tempo-and-measure-length-timing-map-channels-020308>
- HPA-518 design: `docs/superpowers/specs/2026-08-08-hpa-518-measure-lines-design.md`
- HPA-518 implementation plan: `docs/superpowers/plans/2026-08-08-hpa-518-gameplay-measure-lines.md`
- Current parser: `DTXMania.Game/Lib/Song/DTXChartParser.cs`
- Current fixed calculator: `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`
- BMS measure-length example (`#00102:0.75`): <https://bmson-spec-fork.readthedocs.io/en/latest/doc/index.html>
- Extended BPM syntax overview for channels `03` and `08`: <https://fileformats.fandom.com/wiki/Be-Music_Script>
