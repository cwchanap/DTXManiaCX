# HPA-600 Full DTX Timing Map Design

**Issue:** [HPA-600](https://linear.app/cwchanap/issue/HPA-600/full-dtx-tempo-and-measure-length-timing-map-channels-020308)  
**Date:** 2026-08-09  
**Status:** Revised after second design review

## Context

HPA-518 added gameplay measure lines while deliberately preserving CX's existing fixed timing model. That temporary model now lives behind `ChartTimeCalculator.CalculateTimeMs(bar, tick, bpm)` and assumes every measure is four quarter-note beats at one constant base BPM.

HPA-600 replaces that seam with the DTX timing behavior required by gameplay:

- channel `02`: per-measure length multiplier;
- channel `03`: direct hexadecimal BPM changes;
- channel `08`: BPM-table references through `#BPMxx` definitions.

The migration must move notes, BGM events, measure lines, and gameplay chart duration together. Fixing only one collection would create incompatible clocks.

Repository review also identified three compatibility constraints that are easy to miss:

1. `ParseFileContentAsync` stops normal header routing after timeline data begins, so a `#BPMxx` definition may appear after the channel `08` row that references it.
2. Tests currently contain both direct `Note.CalculateTimeMs(...)` callers and finalized charts that seed `TimeMs`. Those fixtures must migrate when finalization becomes authoritative.
3. Existing test helpers sometimes encode positions as `bar = 0` with `tick >= 192`. The old fixed calculator accepted that because it effectively used `bar * 192 + tick`. The new map therefore needs an explicit position-normalization contract; otherwise these inputs either receive the wrong measure-length semantics or fall outside the compiled horizon.

## Goals

- Parse channels `02`, `03`, and `08` into one retained chart timing configuration.
- Support `#BPMxx` definitions whether they appear before or after the channel `08` row that references them.
- Preserve source-line ordering when channel `03` and channel `08` target the same `(bar, tick)`; the later source directive wins.
- Make one chart-owned `ChartTimingMap` the only `(bar, tick) -> TimeMs` authority.
- Normalize non-negative ticks beyond one measure into their enclosing canonical bar/tick position before lookup.
- Recalculate every `Note.TimeMs`, `BGMEvent.TimeMs`, and `MeasureLine.TimeMs` from the map in one `ParsedChart.FinalizeChart()` pass.
- Derive the finalization/rebuild horizon from normalized event positions so legacy non-canonical test inputs remain calculable.
- Recompute gameplay `ParsedChart.DurationMs` from finalized event times and make repeated finalization idempotent.
- Preserve CX's normalized `192` ticks-per-measure coordinate for parser-produced chart events.
- Keep gameplay/rendering/audio consumers on resolved absolute `TimeMs`; do not introduce runtime timing-map dependencies.
- Migrate hand-built tests toward canonical `Bar`/`Tick` positions instead of relying on oversized ticks or pre-seeded `TimeMs`.
- Emit debug diagnostics whenever malformed timing syntax is ignored, so timing drift is diagnosable during chart development.

## Non-goals

- STOP events or other BMS/DTX timing extensions outside channels `02`, `03`, and `08`.
- Beat-line rendering, metronome behavior, or other HPA-518-adjacent UI work.
- Judgement-window, scroll, play-speed, audio-device scheduling, scoring, persistence, or skin changes.
- Runtime re-clocking or a second timing event bus.
- Porting DTXManiaNX parser architecture.
- Min/max/average BPM metadata for song selection.
- `#BASEBPM` support; CX has no existing use of that directive.
- `#BPM00` compatibility alias. Base BPM remains the existing exact `#BPM` directive for this ticket.
- Making `SongChart.Duration` timing-map accurate. `ParseSongEntitiesAsync` / `CalculateDurationAsync` retain their existing base-BPM approximation in HPA-600. This is an explicit known limitation of the metadata/song-library path, not a second gameplay clock.
- Reworking E2E fixtures. The current E2E fixture generates DTX text and therefore flows through the production parser rather than hand-building `ParsedChart` state.

## DTX Timing Semantics

### Canonical chart position

Parser-produced timeline events use:

```text
(bar, tick)
```

where `bar >= 0` and `tick` is normalized to `0..191`.

For defensive compatibility, timing lookup accepts any **non-negative** tick and canonicalizes it as:

```text
normalizedBar  = bar + (tick / 192)
normalizedTick = tick % 192
```

Examples:

```text
(0, 192) -> (1, 0)
(2, 240) -> (3, 48)
(0, 960) -> (5, 0)
```

This is not linear extrapolation inside the original measure. After normalization, every crossed bar uses that bar's own channel `02` multiplier and active tempo history.

Negative bars or ticks are invalid.

Parser code should continue creating canonical positions directly. Oversized-tick support exists as a narrow compatibility/defensive contract, especially for current test helpers; those helpers should still be migrated to canonical positions when touched by HPA-600.

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

map an opaque uppercase two-character object code to a positive numeric BPM. Object codes are strings; do not parse channel `08` IDs as hexadecimal BPM values.

A non-`00` pair in channel `08` references that definition.

Example:

```text
#00108:0001
#BPM01:180.5
```

must resolve successfully even though the definition appears after timeline data has begun.

### Duplicate tempo positions

When multiple channel `03` / `08` directives target the same `(bar, tick)`, the directive appearing later in the source file wins.

This rule applies across channel types. A single pending list appended during the forward file scan already preserves that order; no sequence number or secondary sort is required.

## Approaches Considered

### Compiled chart-owned timing map — selected

Add one `ChartTimingMap` owned by `ParsedChart`. The parser records measure multipliers and resolved tempo changes. `ParsedChart.FinalizeChart()` compiles the timing map through the terminal gameplay measure and resolves all retained event times.

The compiled representation stores ordered timing anchors at measure starts and tempo-change positions. Repeated lookups are cheap and deterministic.

This keeps parsing syntax-specific, timing math format-agnostic, and gameplay runtime unchanged.

### Walk directives from bar zero for every event — rejected

This repeats cumulative timing work for every note, BGM event, and measure line and grows the temporary `ChartTimeCalculator` instead of replacing it.

### Assign absolute time while parsing — rejected

Correct timing must not depend on source-file line order. A channel `08` reference may appear before its `#BPMxx` definition, and playable rows may appear before timing rows for the same measure. Finalization is the correct absolute-time seam.

### Preserve non-zero pre-seeded `TimeMs` — rejected

That creates two timing authorities and makes a legitimate event at time zero indistinguishable from an uncalculated event. HPA-600 intentionally pays the fixture migration once instead.

## Chosen Architecture

### 1. Internal `ChartTimingMap` replaces `ChartTimeCalculator`

Create:

```text
DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs
```

The type is assembly-internal. Tests already have access through the game's existing `InternalsVisibleTo` declarations.

```csharp
internal sealed class ChartTimingMap
{
    internal const int TicksPerMeasure = 192;

    internal static (int Bar, int Tick) NormalizePosition(int bar, int tick);
    internal void SetMeasureLength(int bar, double multiplier);
    internal void SetTempoChange(int bar, int tick, double bpm);
    internal void Rebuild(double baseBpm, int throughBar);
    internal double CalculateTimeMs(int bar, int tick);
}
```

Responsibilities:

- retain valid per-bar measure multipliers;
- retain one resolved BPM per exact canonical `(bar, tick)`;
- use the last `SetTempoChange` call for duplicate positions;
- normalize non-negative lookup positions before anchor selection;
- compile measure-start and tempo-change anchors in `Rebuild`;
- calculate absolute milliseconds from compiled anchors;
- replace compiled anchors on every rebuild.

`SetTempoChange` receives parser-produced canonical ticks and rejects values outside `0..191`. Oversized-tick normalization is a lookup/finalization input compatibility rule, not a parser directive-storage rule.

Delete `ChartTimeCalculator.cs` once callers migrate. Do not keep two timing authorities.

### 2. Position normalization contract

`NormalizePosition(bar, tick)`:

- rejects `bar < 0`;
- rejects `tick < 0`;
- returns `(bar + tick / 192, tick % 192)`.

`CalculateTimeMs` normalizes first, then requires the normalized bar to be inside the compiled range.

This matters when measure lengths differ. For example, `(0, 384)` means canonical `(2, 0)` and must integrate measure 0 and measure 1 independently. It must **not** apply measure 0's multiplier across 384 ticks.

### 3. Timing-map compilation

Inputs:

- base BPM from `ParsedChart.Bpm`;
- per-bar measure multiplier, default `1.0`;
- resolved tempo changes keyed by canonical `(bar, tick)`;
- inclusive `throughBar` covering the terminal gameplay measure-line bar.

For each measure from `0` through `throughBar`:

1. Select the measure multiplier.
2. Add a measure-start anchor at `(bar, 0)` using current time and BPM.
3. Walk tempo changes in ascending tick order.
4. Advance from the previous tick to the change tick using the BPM active over that interval.
5. Apply the new BPM at the change position and replace/add the effective anchor there.
6. Advance the remaining ticks to `192` to obtain the next measure start.
7. Carry final BPM into the next measure.

Interval math:

```text
beats = (tickDelta / 192.0) * 4.0 * measureLengthMultiplier
milliseconds = beats * (60000.0 / bpm)
```

Because every measure has a start anchor, a normalized lookup only needs the last anchor in that measure at or before its tick.

A tempo change at tick `0` replaces the effective BPM of the measure-start anchor for all subsequent ticks in that measure.

### 4. Parser syntax collection and late `#BPMxx` support

The parser must not resolve channel `08` while reading its row.

For each encoding attempt, create fresh parser-local state alongside the existing WAV/volume/pan dictionaries:

```text
bpmDefinitions: Dictionary<string, double>
pendingTempoDirectives: List<PendingTempoDirective>
```

`PendingTempoDirective` is parser-private and contains:

```text
Bar
Tick
Kind: DirectBpm | ReferencedBpm
DirectBpm or ReferenceId
```

List insertion order is source order. Do not add a `Sequence` property and do not sort the list before resolution.

#### Extended BPM definitions are recognized anywhere

Before the existing `inDataSection` header-vs-measure routing, recognize valid `#BPMxx:value` definitions on every non-comment line.

If recognized:

- normalize the two-character suffix to uppercase;
- parse a positive invariant-culture `double`;
- store/replace it in `bpmDefinitions`;
- consume the line and continue.

Exact `#BPM:value` remains handled by the existing base-BPM header path.

This is intentionally a narrow late-header exception; do not generalize every DTX header command.

#### Channel 02

After measure/channel parsing and before BGM/drum routing:

- parse the whole row body as a positive invariant-culture `double`;
- call `TimingMap.SetMeasureLength(measure, multiplier)`;
- return.

Duplicate valid channel `02` rows use the last parsed multiplier.

#### Channels 03 and 08

Parse pair positions using the same normalized tick formula already used by notes/BGM events.

For each non-`00` pair:

- channel `03`: parse a positive hexadecimal integer BPM and append a direct pending directive;
- channel `08`: normalize the pair as an uppercase object-code string and append a referenced pending directive.

Do **not** call `TimingMap.SetTempoChange` from either channel during the file scan.

After the successful complete scan, iterate `pendingTempoDirectives` directly in insertion order:

- direct BPM -> use its numeric BPM;
- referenced BPM -> look up the final `bpmDefinitions` table;
- unresolved/invalid reference -> emit a debug diagnostic and ignore;
- valid directive -> `TimingMap.SetTempoChange(bar, tick, bpm)`.

Applying both channel types in list order preserves cross-channel last-source-line-wins semantics while supporting late `#BPMxx` definitions.

Like WAV dictionaries, both BPM parser collections are recreated for each encoding attempt.

### 5. Ignored timing syntax is diagnosable

Ignored timing syntax must produce `Debug.WriteLine` diagnostics. This is required rather than optional because a malformed timing directive otherwise creates silent audio/chart drift.

At minimum diagnose:

- invalid, zero, or negative channel `02` values;
- non-`00` channel `03` pairs that cannot be parsed as a positive hexadecimal BPM;
- invalid/non-positive `#BPMxx` definitions;
- unresolved channel `08` object codes during the post-scan resolution pass;
- malformed timing rows where the parser intentionally ignores an incomplete pair/body.

Normal `00` no-op pairs are not errors and should not log.

Use existing `System.Diagnostics.Debug` behavior only. Do not add production logging infrastructure.

### 6. `ParsedChart.FinalizeChart()` is the only absolute-time pass

After HPA-600:

- `AddNote(...)` adds the note and updates lane statistics only;
- `AddBGMEvent(...)` adds the event only;
- neither method updates `DurationMs`;
- `ParsedChart` owns one internal `TimingMap`;
- `FinalizeChart()` clears measure lines and resets duration;
- it normalizes each note/BGM position when determining the highest occupied bar;
- if retained events exist, rebuild through `highestNormalizedOccupiedBar + 1`;
- overwrite every note/BGM `TimeMs` from `TimingMap.CalculateTimeMs(rawBar, rawTick)`; lookup performs normalization;
- regenerate measure lines for canonical bars `0..highestNormalizedOccupiedBar + 1`;
- sort notes and BGM by finalized time;
- recompute `DurationMs = max(finalized note/BGM time) + 500 ms`.

For an event at bar `0`, tick `0`, finalized time is legitimately `0`. Zero is never an "uncalculated" sentinel.

Timing directives alone do not create gameplay duration. Empty charts keep no measure lines and duration zero.

Repeated finalization produces the same event times, measure lines, ordering, and duration.

### 7. `Note` and `BGMEvent` become position/data models

Remove their fixed-clock `CalculateTimeMs(double bpm)` methods and stale formula comments.

They retain authored `Bar`/`Tick`, lane/channel/value or WAV data, and finalized `TimeMs` populated by `ParsedChart.FinalizeChart()`.

Tests that need timing exercise `ChartTimingMap` directly or finalize a `ParsedChart`; they do not recreate a per-event fixed clock.

### 8. `ChartManager` consumes finalized state only

Remove the constructor fallback that recalculates notes when `TimeMs == 0`.

Production already finalizes parser output before constructing `ChartManager`. Runtime queries continue consuming resolved `TimeMs` only.

`ChartManager.Bpm` remains the base `#BPM` value for existing display/statistics use.

## Test Fixture Migration Contract

Two rules apply to hand-built chart fixtures:

1. If a test calls `FinalizeChart()`, `TimeMs` is output only. Do not rely on a pre-seeded value surviving finalization.
2. Prefer canonical `Bar`/`Tick` (`tick < 192`) when authoring fixtures, even though the timing map defensively normalizes oversized non-negative ticks.

Current `MockGameplayComponents` helpers contain formulas that keep `bar = 0` while allowing ticks to grow beyond `191`, and one comment incorrectly states that 96 ticks equals 500 ms at 120 BPM. At 120 BPM, 192 ticks is a 2000 ms 4/4 measure, so 96 ticks is 1000 ms and 48 ticks is 500 ms.

HPA-600 should migrate those helpers to split a desired total tick count into canonical `(bar, tick)` rather than depending on oversized-tick compatibility.

The implementation plan must scan the full test tree for:

- direct `CalculateTimeMs(...)` calls;
- `FinalizeChart()` calls;
- seeded `TimeMs` values;
- `Tick`/local `tick` authoring that can exceed `191`.

Known affected files include:

- `DTXMania.Test/Helpers/MockGameplayComponents.cs`;
- `DTXMania.Test/Stage/Performance/TimingVerificationTest.cs`;
- `DTXMania.Test/Stage/Performance/AutomatedPlaySimulationTests.cs`;
- `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`.

The scan, not this list, is authoritative.

The E2E tree has no direct `ParsedChart`/`FinalizeChart` timing fixture path; its chart fixture is DTX text and remains parser-driven.

## Data Flow

```text
DTX text
  -> DTXChartParser
       -> ParsedChart.Bpm                         (#BPM)
       -> parser-local bpmDefinitions            (#BPMxx anywhere)
       -> parser-local pending tempo directives  (03 / 08 in list/source order)
       -> ParsedChart.TimingMap
            -> measure multiplier by bar         (02)
       -> Notes / BGMEvents with bar + tick
  -> successful end-of-file parse
       -> resolve pending 03/08 directives in insertion order
       -> TimingMap.SetTempoChange(...)
  -> ParsedChart.FinalizeChart
       -> normalize event positions for rebuild horizon
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

- Negative bars/ticks passed to timing lookup are rejected.
- Non-negative ticks `>= 192` are normalized into later bars before lookup.
- Lookup rejects a normalized position beyond the compiled `throughBar`; finalization prevents this by deriving its horizon from normalized event positions.
- Non-positive base BPM retains the current failure behavior when the map is rebuilt.
- Invalid/non-positive channel `02` values are ignored with a debug diagnostic; that bar uses multiplier `1.0`.
- Channel `03` pair `00` means no change; malformed/non-positive non-`00` pairs are ignored with a debug diagnostic.
- Invalid/non-positive `#BPMxx` definitions are ignored with a debug diagnostic.
- Channel `08` pair `00` means no change.
- Missing channel `08` definitions are ignored during post-scan resolution with a debug diagnostic.
- Tempo changes persist into later measures until replaced.
- Channel `02` affects only its own measure.
- Same-position tempo changes use the last valid directive appended in source order.
- Timing directives without playable/BGM events do not create gameplay duration.

## Performance Characteristics

For `B` relevant bars and `T` tempo changes:

- map rebuild: O(B + T log T) with simple global sorting, or O(B + T) after equivalent grouping;
- lookup: O(log A) over compiled anchors, where `A` is roughly `B + T`;
- finalization: O(events log A) plus existing event sorts.

No additional runtime cache is needed.

## Testing Strategy

### `ChartTimingMapTests`

Cover:

- base BPM only reproduces current in-range clock results;
- shortened measure;
- extended measure;
- a measure multiplier on **bar 1** leaves bar 0 unchanged and shifts bar 2 correctly;
- direct BPM change halfway through a measure;
- tempo change persists into the next measure;
- measure multiplier and tempo change combine correctly;
- tempo change at tick `0` applies immediately;
- duplicate tempo changes use last call;
- repeated `Rebuild` is deterministic;
- invalid base BPM is rejected;
- oversized lookup ticks normalize across measure boundaries;
- normalization uses each crossed measure's own length rather than extrapolating the source bar's multiplier;
- negative position input is rejected.

Representative normalization case at base 120 BPM:

```text
bar 0 multiplier = 0.5  -> 1000 ms
bar 1 multiplier = 1.5  -> 3000 ms
CalculateTimeMs(0, 384) normalizes to (2, 0) -> 4000 ms
```

### `ParsedChartTests`

Cover:

- `AddNote` / `AddBGMEvent` defer time and duration;
- finalization overwrites pre-seeded time;
- note/BGM/measure lines share the map;
- duration is recomputed and idempotent;
- a bar-0/tick-0 event still produces a `500 ms` buffered duration;
- highest occupied bar is based on normalized positions;
- e.g. bar `0`, tick `960` rebuilds through normalized bar `5` plus terminal bar and does not throw.

### Parser tests

Use temporary inline DTX files for:

- channel `02` short and extended measures;
- channel `03` direct BPM;
- channel `08` definition before reference;
- channel `08` definition after timeline/reference;
- opaque `#BPMAA` / `AA` reference;
- channel `03` + `08` same-position ordering in both source orders;
- note/BGM/measure-line alignment across a tempo change;
- malformed channel `02`, channel `03`, `#BPMxx`, and unresolved `08` fallback behavior.

Debug output itself does not need brittle text assertions; code review should verify every ignore branch emits `Debug.WriteLine`.

### Fixture/regression tests

Before Task 2 is green, inspect all test hits for direct timing calls, seeded times, finalized charts, and potentially oversized ticks. Migrate production-style chart fixtures to canonical bar/tick authoring.

Run Song and Performance test namespaces after fixture migration, then the complete Mac-safe suite.

## Files Expected to Change

### Production

Create:

- `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`

Modify:

- `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`
- `DTXMania.Game/Lib/Song/Components/Note.cs`
- `DTXMania.Game/Lib/Song/Components/BGMEvent.cs`
- `DTXMania.Game/Lib/Song/Components/ChartManager.cs`
- `DTXMania.Game/Lib/Song/DTXChartParser.cs`

Delete:

- `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`

No Performance production file is expected to change.

### Tests

Create:

- `DTXMania.Test/Song/ChartTimingMapTests.cs`

Modify/delete the existing Song timing tests as required, plus fixture-only changes discovered by the mandatory scan. Known migration files include:

- `DTXMania.Test/Helpers/MockGameplayComponents.cs`
- `DTXMania.Test/Stage/Performance/TimingVerificationTest.cs`
- `DTXMania.Test/Stage/Performance/AutomatedPlaySimulationTests.cs`
- `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

Delete:

- `DTXMania.Test/Song/ChartTimeCalculatorTests.cs`

No project-file change is expected.

## Acceptance Criteria

- Channels `02`, `03`, and `08` affect gameplay note, BGM, measure-line, and duration timing through one map.
- `#BPMxx` references resolve even when definitions occur after timeline data begins.
- Channel `03` and `08` collisions use later source-list order.
- Channel `08` object IDs such as `AA` remain opaque strings.
- `ChartTimingMap` is internal and replaces the fixed `ChartTimeCalculator` entirely.
- Timing lookup normalizes non-negative ticks beyond `191` before applying measure/tempo semantics.
- Finalization rebuilds far enough to cover every normalized event position plus the terminal measure boundary.
- Finalization always overwrites `TimeMs` and recomputes duration idempotently.
- Current oversized-tick test helpers are migrated to canonical bar/tick positions where HPA-600 touches them.
- Ignored malformed timing syntax emits `Debug.WriteLine` diagnostics.
- `ChartManager` contains no `TimeMs == 0` recalculation fallback.
- `SongChart.Duration` remains explicitly approximate and unchanged.
- Song + Performance regressions and the full Mac-safe suite pass.
- Gameplay renderer/stage production code remains unchanged.

## References

- HPA-600 Linear issue
- HPA-518 design and implementation plan
- PR #120 review that surfaced the timing-map requirement
- `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`
- `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`
- `DTXMania.Game/Lib/Song/DTXChartParser.cs`
- `DTXMania.Test/Helpers/MockGameplayComponents.cs`
