# HPA-600 Full DTX Timing Map Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Parse DTX timing channels `02`, `03`, and `08` and resolve notes, BGM events, measure lines, and gameplay duration from one compiled timing map.

**Architecture:** Replace the temporary fixed `ChartTimeCalculator` with an internal `ChartTimingMap`. Make `ParsedChart.FinalizeChart()` the only production absolute-time pass, including normalization of oversized non-negative ticks and rebuild-horizon calculation. Migrate affected fixtures to canonical bar/tick positions, then extend `DTXChartParser` to collect timing syntax and resolve ordered `03`/`08` directives after the complete file is known.

**Tech Stack:** .NET 8, C#, MonoGame 3.8, xUnit, existing DTX parser and Mac-safe test projects.

## Global Constraints

- Source of truth: [`2026-08-09-hpa-600-dtx-timing-map-design.md`](../specs/2026-08-09-hpa-600-dtx-timing-map-design.md).
- Channel `02` is a positive decimal per-measure length multiplier; it affects only its own measure and defaults to `1.0`.
- Channel `03` uses non-`00` hexadecimal BPM pairs.
- Channel `08` uses opaque uppercase two-character object IDs resolved through positive numeric `#BPMxx` definitions; do not parse IDs such as `AA` as hexadecimal BPM values.
- `#BPMxx` definitions must resolve even when they appear after the timeline row that references them.
- Channel `03` and `08` at the same position use later **source-list insertion order**, independent of channel type.
- Keep the parser's canonical `192` ticks per measure.
- Timing lookup accepts non-negative ticks `>= 192` by normalizing them into later bars; negative positions are rejected.
- `ParsedChart.FinalizeChart()` derives its rebuild horizon from normalized note/BGM positions so every lookup is covered.
- `ParsedChart.FinalizeChart()` is the only production pass that assigns `Note.TimeMs`, `BGMEvent.TimeMs`, `MeasureLine.TimeMs`, and gameplay `DurationMs`.
- Finalization always overwrites pre-existing `TimeMs`; zero is a valid finalized timestamp, never an uncalculated sentinel.
- Gameplay `DurationMs` is `max(finalized note/BGM time) + 500 ms` when at least one retained event exists; measure lines never extend duration.
- `ParsedChart.Bpm` and `ChartManager.Bpm` remain the base exact `#BPM` value.
- `SongChart.Duration` / `CalculateDurationAsync` remain the existing base-BPM approximation. Do not expand HPA-600 into metadata timing-map parsing.
- Ignored malformed timing syntax must emit `Debug.WriteLine` diagnostics; normal `00` no-op pairs do not log.
- `ChartTimingMap` is `internal`; do not expose it as a public runtime API.
- Do not add `#BASEBPM`, `#BPM00`, STOP support, beat lines, runtime re-clocking, renderer/stage production changes, project-file changes, skins, workflows, or legacy DTXManiaNX changes.
- Delete the fixed `ChartTimeCalculator` once production callers move to `ChartTimingMap`; do not keep two timing authorities.
- Any hand-built `ParsedChart` that calls `FinalizeChart()` treats `TimeMs` as output. Prefer canonical `Bar`/`Tick` with `0 <= Tick < 192`.
- Existing oversized-tick test helpers touched by this ticket must be migrated to canonical bar/tick authoring even though `ChartTimingMap` defensively normalizes oversized ticks.
- Prefix repository shell commands with `rtk`, matching existing implementation plans.
- Preserve current Mac-safe exclusions.
- Follow RED -> focused GREEN -> review -> commit for every task.

## File Responsibility Map

### Production

- Create `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`: internal timing configuration, normalization, compiled anchors, absolute-time lookup.
- Modify `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`: own `TimingMap`; normalize positions for rebuild horizon; make finalization authoritative and duration idempotent.
- Modify `DTXMania.Game/Lib/Song/Components/Note.cs`: remove fixed-clock timing method and stale formula comments.
- Modify `DTXMania.Game/Lib/Song/Components/BGMEvent.cs`: remove fixed-clock timing method and stale formula comments.
- Modify `DTXMania.Game/Lib/Song/Components/ChartManager.cs`: remove `TimeMs == 0` fallback calculation.
- Delete `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`.
- Modify `DTXMania.Game/Lib/Song/DTXChartParser.cs`: parse channel `02`, collect ordered `03`/`08` directives, recognize `#BPMxx` anywhere, resolve tempo directives after full parse, diagnose ignored timing syntax.

### Core tests

- Create `DTXMania.Test/Song/ChartTimingMapTests.cs`.
- Delete `DTXMania.Test/Song/ChartTimeCalculatorTests.cs`.
- Modify `DTXMania.Test/Song/ParsedChartTests.cs`.
- Modify `DTXMania.Test/Song/NoteTests.cs`.
- Modify `DTXMania.Test/Song/BGMEventTests.cs`.
- Modify `DTXMania.Test/Song/ChartManagerTests.cs`.
- Modify `DTXMania.Test/Song/DTXChartParserTests.cs`.

### Known fixture migration surface

The implementation-time scans are authoritative. Review already identified at least:

- `DTXMania.Test/Helpers/MockGameplayComponents.cs`
- `DTXMania.Test/Stage/Performance/TimingVerificationTest.cs`
- `DTXMania.Test/Stage/Performance/AutomatedPlaySimulationTests.cs`
- `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

Other test files returned by the mandatory scans may need fixture-only changes. Do not change Performance production code for fixture failures.

The `DTXMania.E2E` tree was checked during planning and does not directly construct `ParsedChart` or call the timing helpers/finalizer. Its generated fixture is DTX text, so no E2E fixture migration is planned.

---

## Task 1: Build the Internal Compiled `ChartTimingMap`

**Files:**

- Create: `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`
- Create: `DTXMania.Test/Song/ChartTimingMapTests.cs`

**Interfaces produced:**

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

The test assemblies can access this internal type through the existing `InternalsVisibleTo("DTXMania.Test")` and `InternalsVisibleTo("DTXMania.Test.Mac")` declarations.

### Step 1: Write direct map tests

- [ ] Create `ChartTimingMapTests.cs` before production implementation.

Required base-clock parity:

```csharp
[Fact]
public void CalculateTimeMs_BaseBpmOnly_ShouldMatchOldClock()
{
    var map = new ChartTimingMap();
    map.Rebuild(120.0, throughBar: 2);

    Assert.Equal(0.0, map.CalculateTimeMs(0, 0), 3);
    Assert.Equal(500.0, map.CalculateTimeMs(0, 48), 3);
    Assert.Equal(1000.0, map.CalculateTimeMs(0, 96), 3);
    Assert.Equal(2000.0, map.CalculateTimeMs(1, 0), 3);
    Assert.Equal(5000.0, map.CalculateTimeMs(2, 96), 3);
}
```

Required measure-length tests:

```csharp
[Fact]
public void CalculateTimeMs_ShortMeasure_ShouldShiftFollowingBar()
{
    var map = new ChartTimingMap();
    map.SetMeasureLength(0, 0.5);
    map.Rebuild(120.0, 1);

    Assert.Equal(1000.0, map.CalculateTimeMs(1, 0), 3);
}

[Fact]
public void CalculateTimeMs_ExtendedMeasure_ShouldShiftFollowingBar()
{
    var map = new ChartTimingMap();
    map.SetMeasureLength(0, 1.5);
    map.Rebuild(120.0, 1);

    Assert.Equal(3000.0, map.CalculateTimeMs(1, 0), 3);
}

[Fact]
public void CalculateTimeMs_MeasureLengthOnBarOne_ShouldNotAffectBarZero()
{
    var map = new ChartTimingMap();
    map.SetMeasureLength(1, 0.5);
    map.Rebuild(120.0, 2);

    Assert.Equal(2000.0, map.CalculateTimeMs(1, 0), 3);
    Assert.Equal(3000.0, map.CalculateTimeMs(2, 0), 3);
}
```

Required tempo tests:

```csharp
[Fact]
public void CalculateTimeMs_HalfwayTempoChange_ShouldIntegrateSegments()
{
    var map = new ChartTimingMap();
    map.SetTempoChange(0, 96, 240.0);
    map.Rebuild(120.0, 1);

    Assert.Equal(1000.0, map.CalculateTimeMs(0, 96), 3);
    Assert.Equal(1500.0, map.CalculateTimeMs(1, 0), 3);
    Assert.Equal(2000.0, map.CalculateTimeMs(1, 96), 3);
}

[Fact]
public void CalculateTimeMs_TempoChangeAtTickZero_ShouldApplyImmediately()
{
    var map = new ChartTimingMap();
    map.SetTempoChange(0, 0, 240.0);
    map.Rebuild(120.0, 1);

    Assert.Equal(500.0, map.CalculateTimeMs(0, 96), 3);
    Assert.Equal(1000.0, map.CalculateTimeMs(1, 0), 3);
}

[Fact]
public void SetTempoChange_SamePosition_ShouldUseLastValue()
{
    var map = new ChartTimingMap();
    map.SetTempoChange(0, 96, 180.0);
    map.SetTempoChange(0, 96, 240.0);
    map.Rebuild(120.0, 1);

    Assert.Equal(1500.0, map.CalculateTimeMs(1, 0), 3);
}
```

Required composition and normalization tests:

```csharp
[Fact]
public void CalculateTimeMs_MeasureLengthAndTempoChange_ShouldCompose()
{
    var map = new ChartTimingMap();
    map.SetMeasureLength(0, 0.5);
    map.SetTempoChange(0, 96, 240.0);
    map.Rebuild(120.0, 1);

    Assert.Equal(500.0, map.CalculateTimeMs(0, 96), 3);
    Assert.Equal(750.0, map.CalculateTimeMs(1, 0), 3);
}

[Fact]
public void NormalizePosition_OversizedTick_ShouldFoldIntoLaterBar()
{
    Assert.Equal((1, 0), ChartTimingMap.NormalizePosition(0, 192));
    Assert.Equal((3, 48), ChartTimingMap.NormalizePosition(2, 240));
    Assert.Equal((5, 0), ChartTimingMap.NormalizePosition(0, 960));
}

[Fact]
public void CalculateTimeMs_OversizedTick_ShouldUseEachCrossedMeasureLength()
{
    var map = new ChartTimingMap();
    map.SetMeasureLength(0, 0.5); // 1000 ms
    map.SetMeasureLength(1, 1.5); // 3000 ms
    map.Rebuild(120.0, 2);

    // (0, 384) canonicalizes to (2, 0), so both measures contribute.
    Assert.Equal(4000.0, map.CalculateTimeMs(0, 384), 3);
    Assert.Equal(4000.0, map.CalculateTimeMs(2, 0), 3);
}
```

Required validation/idempotence:

```csharp
[Fact]
public void Rebuild_Repeated_ShouldBeDeterministic()
{
    var map = new ChartTimingMap();
    map.SetMeasureLength(0, 0.5);
    map.SetTempoChange(0, 96, 240.0);
    map.Rebuild(120.0, 1);
    var expected = map.CalculateTimeMs(1, 0);

    map.Rebuild(120.0, 1);

    Assert.Equal(expected, map.CalculateTimeMs(1, 0), 3);
}

[Theory]
[InlineData(0.0)]
[InlineData(-120.0)]
public void Rebuild_NonPositiveBaseBpm_ShouldThrow(double bpm)
{
    var map = new ChartTimingMap();
    Assert.Throws<ArgumentException>(() => map.Rebuild(bpm, 1));
}

[Fact]
public void NormalizePosition_NegativeTick_ShouldThrow()
{
    Assert.Throws<ArgumentOutOfRangeException>(
        () => ChartTimingMap.NormalizePosition(0, -1));
}
```

- [ ] Run RED:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter 'FullyQualifiedName~ChartTimingMapTests'
```

Expected: compile failure because `ChartTimingMap` does not exist.

### Step 2: Implement normalization and retained configuration

- [ ] Create `ChartTimingMap.cs` as `internal sealed class`.

Use:

```csharp
internal const int TicksPerMeasure = 192;

private readonly Dictionary<int, double> _measureLengths = new();
private readonly Dictionary<(int Bar, int Tick), double> _tempoChanges = new();
private readonly List<TimingAnchor> _anchors = new();
```

Implement normalization exactly as:

```csharp
internal static (int Bar, int Tick) NormalizePosition(int bar, int tick)
{
    if (bar < 0)
        throw new ArgumentOutOfRangeException(nameof(bar));
    if (tick < 0)
        throw new ArgumentOutOfRangeException(nameof(tick));

    return (
        bar + (tick / TicksPerMeasure),
        tick % TicksPerMeasure);
}
```

`SetMeasureLength` keeps positive values for non-negative bars and ignores invalid values.

`SetTempoChange` accepts only canonical positions: `bar >= 0`, `0 <= tick < 192`, positive BPM. It replaces an existing value at the same key.

### Step 3: Implement rebuild and lookup

- [ ] Use a private anchor containing:

```text
Bar
Tick
TimeMs
Bpm
MeasureLengthMultiplier
```

- [ ] `Rebuild(baseBpm, throughBar)` must:

1. reject non-positive base BPM;
2. reject negative `throughBar`;
3. clear compiled anchors but retain configured directives;
4. sort tempo changes by canonical bar/tick;
5. walk bars `0..throughBar`;
6. add a measure-start anchor;
7. integrate intervals before each tempo change;
8. replace the effective anchor at tick-zero changes;
9. carry final BPM into the next bar.

Use exactly:

```csharp
private static double CalculateIntervalMs(
    int tickDelta,
    double measureLengthMultiplier,
    double bpm)
{
    var beats =
        (tickDelta / (double)TicksPerMeasure) *
        4.0 *
        measureLengthMultiplier;

    return beats * (60000.0 / bpm);
}
```

- [ ] `CalculateTimeMs(bar, tick)` must:

1. call `NormalizePosition`;
2. reject a normalized bar beyond the compiled `throughBar`;
3. binary-search the ordered anchors for the last anchor at or before the normalized position;
4. integrate only the remainder from that anchor to normalized tick.

Do not clamp, extrapolate using the original bar's multiplier, or add another cache.

### Step 4: Verify and commit Task 1

- [ ] Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter 'FullyQualifiedName~ChartTimingMapTests'
rtk git diff --check
rtk git status --short
```

Expected: all map tests pass and only Task 1 files are modified.

- [ ] Stage explicitly:

```bash
rtk git add \
  DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs \
  DTXMania.Test/Song/ChartTimingMapTests.cs
rtk git commit -m "feat: add compiled chart timing map"
```

---

## Task 2: Make Finalization Authoritative and Migrate Fixtures

**Files:**

Production:

- Modify: `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/Note.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/BGMEvent.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/ChartManager.cs`
- Delete: `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`

Core tests:

- Modify: `DTXMania.Test/Song/ParsedChartTests.cs`
- Modify: `DTXMania.Test/Song/NoteTests.cs`
- Modify: `DTXMania.Test/Song/BGMEventTests.cs`
- Modify: `DTXMania.Test/Song/ChartManagerTests.cs`
- Delete: `DTXMania.Test/Song/ChartTimeCalculatorTests.cs`

Known fixture migration:

- Modify: `DTXMania.Test/Helpers/MockGameplayComponents.cs`
- Modify: `DTXMania.Test/Stage/Performance/TimingVerificationTest.cs`
- Modify: `DTXMania.Test/Stage/Performance/AutomatedPlaySimulationTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Modify any additional test file discovered by Step 1.

**Interface consumed:** `ChartTimingMap` from Task 1.

### Step 1: Inventory the full migration surface before editing

- [ ] Run:

```bash
rtk rg -n 'CalculateTimeMs\(' DTXMania.Game DTXMania.Test
rtk rg -n 'FinalizeChart\(\)' DTXMania.Test
rtk rg -n 'TimeMs\s*=' DTXMania.Test
rtk rg -n '(Tick\s*=|tick\s*=|new Note\()' DTXMania.Test
```

Classify hits as:

1. production fixed-clock path to remove;
2. timing test to move to `ChartTimingMap` / finalization;
3. finalized fixture that incorrectly seeds `TimeMs`;
4. chart fixture that computes/assigns ticks that may exceed `191`;
5. runtime-only note setup that intentionally does not pass through `FinalizeChart()`.

The fourth scan is mandatory. The first three do not detect helpers that already use `Bar`/`Tick` but encode `bar=0` with an unbounded tick.

- [ ] Reconfirm the E2E scope once:

```bash
rtk rg -n 'CalculateTimeMs\(|FinalizeChart\(|new ParsedChart|TimeMs\s*=' DTXMania.E2E
```

Expected: no direct timing fixture requiring migration; generated DTX text remains parser-driven.

### Step 2: Write finalization lifecycle tests

- [ ] Add tests in `ParsedChartTests` that establish deferred timing:

```csharp
[Fact]
public void AddNote_ShouldDeferTimeAndDurationUntilFinalize()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    var note = new Note(3, 1, 0, 0x12, "01");

    chart.AddNote(note);

    Assert.Equal(0.0, note.TimeMs);
    Assert.Equal(0.0, chart.DurationMs);
}

[Fact]
public void AddBGMEvent_ShouldDeferTimeAndDurationUntilFinalize()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    var bgm = new BGMEvent(1, 0, "01");

    chart.AddBGMEvent(bgm);

    Assert.Equal(0.0, bgm.TimeMs);
    Assert.Equal(0.0, chart.DurationMs);
}
```

- [ ] Add overwrite and idempotence coverage:

```csharp
[Fact]
public void FinalizeChart_ShouldOverwriteSeededTimeFromAuthoredPosition()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    var note = new Note(0, 1, 0, 0x11, "01") { TimeMs = 12345.0 };
    chart.AddNote(note);

    chart.FinalizeChart();

    Assert.Equal(2000.0, note.TimeMs, 3);
}

[Fact]
public void FinalizeChart_Repeated_ShouldKeepDurationStable()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddNote(new Note(0, 1, 0, 0x11, "01"));

    chart.FinalizeChart();
    var first = chart.DurationMs;
    chart.FinalizeChart();

    Assert.Equal(first, chart.DurationMs, 3);
}

[Fact]
public void FinalizeChart_TimeZeroNote_ShouldStillReceiveEndBuffer()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddNote(new Note(0, 0, 0, 0x11, "01"));

    chart.FinalizeChart();

    Assert.Equal(0.0, chart.Notes[0].TimeMs, 3);
    Assert.Equal(500.0, chart.DurationMs, 3);
}
```

- [ ] Add the normalization-horizon regression:

```csharp
[Fact]
public void FinalizeChart_OversizedTick_ShouldBuildThroughNormalizedBar()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddNote(new Note(0, 0, 960, 0x11, "01"));

    chart.FinalizeChart();

    Assert.Equal(10000.0, chart.Notes[0].TimeMs, 3);
    Assert.Equal(10500.0, chart.DurationMs, 3);
    Assert.Equal(6, chart.MeasureLines[^1].Bar);
    Assert.Equal(12000.0, chart.MeasureLines[^1].TimeMs, 3);
}
```

At base 120, `(0, 960)` normalizes to bar `5`, tick `0`; terminal measure line is therefore bar `6`.

- [ ] Run RED on `ParsedChartTests` before production changes.

### Step 3: Make `ParsedChart.FinalizeChart()` authoritative

- [ ] Add one chart-owned map:

```csharp
internal ChartTimingMap TimingMap { get; } = new ChartTimingMap();
```

- [ ] Simplify `AddNote`:

- null -> no-op;
- add note;
- update `NotesPerLane` only;
- do not assign `TimeMs`;
- do not update `DurationMs`.

- [ ] Simplify `AddBGMEvent`:

- null -> no-op;
- add event only;
- do not assign time or duration.

- [ ] Rewrite `FinalizeChart()` in this order:

1. clear `MeasureLines`;
2. reset `DurationMs = 0`;
3. for every note/BGM, call `ChartTimingMap.NormalizePosition(Bar, Tick)` only to determine its normalized occupied bar;
4. set `highestOccupiedBar` from those normalized bars;
5. if no retained event exists, keep lines empty and duration zero;
6. `TimingMap.Rebuild(Bpm, highestOccupiedBar + 1)`;
7. overwrite every note `TimeMs = TimingMap.CalculateTimeMs(note.Bar, note.Tick)`;
8. overwrite every BGM event the same way;
9. regenerate measure lines for canonical bars `0..highestOccupiedBar + 1` at tick `0`;
10. sort notes and BGM by recalculated `TimeMs`;
11. set `DurationMs = max(note/BGM finalized time) + 500.0`.

Do not derive duration from measure lines. Do not preserve non-zero seeded times.

### Step 4: Remove old fixed-clock escape paths

- [ ] Remove `Note.CalculateTimeMs(double bpm)`.
- [ ] Remove `BGMEvent.CalculateTimeMs(double bpm)`.
- [ ] Remove stale fixed-formula XML comments.
- [ ] Remove `ChartManager` constructor's `TimeMs == 0` recalculation loop.
- [ ] Delete `ChartTimeCalculator.cs` and `ChartTimeCalculatorTests.cs`.
- [ ] Rewrite/remove direct timing tests in `NoteTests`, `BGMEventTests`, and `TimingVerificationTest`; map math belongs in `ChartTimingMapTests`, lifecycle timing belongs in finalized-chart tests.

Do **not** add a compatibility helper that recreates fixed base-BPM timing per note.

### Step 5: Migrate `MockGameplayComponents` to canonical positions

Current helpers convert time to a growing tick while keeping `bar = 0`, and one comment says `96 ticks = 500 ms` at 120 BPM. Correct 120-BPM 4/4 values are:

```text
192 ticks = 2000 ms
96 ticks  = 1000 ms
48 ticks  = 500 ms
```

- [ ] Add/reuse one test-helper-only conversion that returns canonical coordinates:

```csharp
private static (int Bar, int Tick) ToPositionAt120Bpm(double timeMs)
{
    const double measureMs = 2000.0;
    var totalTicks = (int)Math.Round(
        timeMs * ChartTimingMap.TicksPerMeasure / measureMs);

    return (
        totalTicks / ChartTimingMap.TicksPerMeasure,
        totalTicks % ChartTimingMap.TicksPerMeasure);
}
```

If exact rounding expectations differ in an existing timing-window test, preserve that test's intended timestamp using explicit bar/tick data rather than restoring the incorrect `96 ticks = 500 ms` formula.

- [ ] Use the helper in `CreateSingleNoteChart`, `CreateMultipleNotesChart`, `AddSyntheticNote` when the synthetic note becomes chart input, and any scenario builders discovered by the scan that can exceed tick `191`.

- [ ] Fix misleading tick/time comments while touching those lines.

Normalization in production is a defensive contract, not a reason to keep non-canonical fixture authoring.

### Step 6: Migrate the remaining known fixtures

- [ ] `TimingVerificationTest.cs`: remove direct `note.CalculateTimeMs(120)` coverage. Use a finalized chart or delete redundant math assertions now covered by `ChartTimingMapTests`.

- [ ] `AutomatedPlaySimulationTests.cs`: replace direct per-note timing calls with a `ParsedChart` that receives bar/tick-authored notes and is finalized once before the simulation consumes resolved notes.

- [ ] `PerformanceStageDeterministicTests.cs`: any chart that both seeds `TimeMs` and calls `FinalizeChart()` must instead encode the intended position. Example: `4000 ms` at 120 BPM -> `Bar = 2`, `Tick = 0`.

- [ ] Apply the same rule to every additional Step 1 hit.

Runtime-only notes may still seed `TimeMs` if they never pass through `FinalizeChart()` and the test is explicitly about a post-finalization runtime component.

### Step 7: Verify the complete Task 2 migration surface

- [ ] Re-run all four test scans:

```bash
rtk rg -n 'CalculateTimeMs\(' DTXMania.Game DTXMania.Test
rtk rg -n 'FinalizeChart\(\)' DTXMania.Test
rtk rg -n 'TimeMs\s*=' DTXMania.Test
rtk rg -n '(Tick\s*=|tick\s*=|new Note\()' DTXMania.Test
```

Expected:

- no call to deleted `Note/BGMEvent.CalculateTimeMs`;
- seeded `TimeMs` is not passed through finalization;
- finalized chart fixtures are authored with meaningful positions;
- dynamic tick conversions that can exceed `191` have been canonicalized or are intentionally runtime-only.

- [ ] Run Song + Performance regressions:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter 'FullyQualifiedName~DTXMania.Test.Song|FullyQualifiedName~DTXMania.Test.Stage.Performance'
```

If filter syntax rejects the combined expression, run the namespaces separately. Performance coverage is mandatory because Task 2 changes fixture contracts used by those tests.

- [ ] Run repository checks:

```bash
rtk git diff --check
rtk git status --short
```

### Step 8: Review and commit Task 2 safely

- [ ] Review `git status --short` and the full diff. Production changes must remain limited to Song components; Performance changes must be tests/helpers only.
- [ ] Confirm no non-zero `TimeMs` preservation branch or new sentinel exists.
- [ ] Stage the known files explicitly:

```bash
rtk git add \
  DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs \
  DTXMania.Game/Lib/Song/Components/ParsedChart.cs \
  DTXMania.Game/Lib/Song/Components/Note.cs \
  DTXMania.Game/Lib/Song/Components/BGMEvent.cs \
  DTXMania.Game/Lib/Song/Components/ChartManager.cs \
  DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs \
  DTXMania.Test/Song/ChartTimingMapTests.cs \
  DTXMania.Test/Song/ChartTimeCalculatorTests.cs \
  DTXMania.Test/Song/ParsedChartTests.cs \
  DTXMania.Test/Song/NoteTests.cs \
  DTXMania.Test/Song/BGMEventTests.cs \
  DTXMania.Test/Song/ChartManagerTests.cs \
  DTXMania.Test/Helpers/MockGameplayComponents.cs \
  DTXMania.Test/Stage/Performance/TimingVerificationTest.cs \
  DTXMania.Test/Stage/Performance/AutomatedPlaySimulationTests.cs \
  DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
```

`git add` accepts deleted paths, so the two deleted calculator files can be listed explicitly.

- [ ] If Step 1 discovered additional Task 2 fixture files, inspect each one and add each exact path explicitly after checking `git status --short`.

Do **not** use `git add -A`.

- [ ] Commit:

```bash
rtk git commit -m "refactor: finalize chart timing from timing map"
```

---

## Task 3: Parse DTX Channels `02` / `03` / `08` with Late BPM Resolution

**Files:**

- Modify: `DTXMania.Game/Lib/Song/DTXChartParser.cs`
- Modify: `DTXMania.Test/Song/DTXChartParserTests.cs`

**Interfaces consumed:**

```csharp
chart.TimingMap.SetMeasureLength(bar, multiplier);
chart.TimingMap.SetTempoChange(bar, tick, bpm);
```

No timing-map dependency should escape parser/finalization into gameplay runtime.

### Step 1: Add parser integration tests before parser code

Use temporary files following the existing parser-test pattern. Every fixture must retain at least one note/BGM event so finalization builds a gameplay timeline.

- [ ] **Short measure:** base 120, bar 0 `02 = 0.5`, note at bar 1 start -> `1000 ms`.

```text
#BPM: 120
#00002:0.5
#00111:01
```

- [ ] **Extended measure:** base 120, bar 0 `02 = 1.5`, note at bar 1 start -> `3000 ms`.

- [ ] **Direct 03:** base 120, tempo -> hex `F0` (=240) halfway through bar 0; bar 1 start -> `1500 ms`.

```text
#BPM: 120
#00003:00F0
#00111:01
```

- [ ] **Referenced 08, definition first:** `#BPM01:180.5`, channel `08` reference `01`, assert expected timing.

- [ ] **Referenced 08, definition late:**

```text
#BPM: 120
#00008:0001
#00111:01
#BPM01:180.5
```

The reference must resolve although `#BPM01` appears after timeline data begins.

- [ ] **Opaque ID:** use `#BPMAA:210.25` and channel `08` pair `AA`; assert it resolves.

- [ ] **Cross-channel same-position source order:** use different direct/referenced BPMs and two fixtures with reversed row order. The later row wins in each fixture.

- [ ] **Alignment:** chart containing a note, BGM event, and generated measure boundary around a tempo change; all must use the same timeline.

- [ ] **Malformed fallback:** invalid/zero `02`, invalid non-`00` `03`, invalid `#BPMxx`, missing `08` reference, and `00` no-op pairs do not crash and retain fallback behavior.

Run parser tests and observe RED before implementation.

### Step 2: Add per-encoding parser state

- [ ] In `ParseAsync`, create fresh state inside each encoding attempt:

```csharp
var bpmDefinitions = new Dictionary<string, double>();
var pendingTempoDirectives = new List<PendingTempoDirective>();
```

Do not share either collection across encoding retries.

- [ ] Add only parser-private syntax types:

```csharp
private enum PendingTempoKind
{
    DirectBpm,
    ReferencedBpm
}

private sealed class PendingTempoDirective
{
    public int Bar { get; init; }
    public int Tick { get; init; }
    public PendingTempoKind Kind { get; init; }
    public double DirectBpm { get; init; }
    public string ReferenceId { get; init; } = "";
}
```

There is intentionally **no `Sequence` field**. The list is appended during one forward scan, so insertion order already is source order.

### Step 3: Recognize `#BPMxx` definitions anywhere

- [ ] Add a focused pre-routing helper:

```csharp
private static bool TryHandleExtendedBpmDefinition(
    string line,
    IDictionary<string, double> bpmDefinitions)
```

Required behavior:

- exact `#BPM:...` -> return `false` so base BPM remains on the existing header path;
- valid `#BPMxx:value` -> uppercase the two-character suffix, parse invariant positive `double`, store/replace, return `true`;
- malformed/non-positive `#BPMxx` -> emit `Debug.WriteLine` and return `true` so the line is consumed rather than misrouted;
- work before or after timeline data begins.

Conceptual loop:

```csharp
line = line.Trim();
if (skip comment/empty)
    continue;

if (TryHandleExtendedBpmDefinition(line, bpmDefinitions))
    continue;

if (line starts timeline/data marker)
    inDataSection = true;

if (!inDataSection)
    ParseHeaderCommand(...);
else
    ParseMeasureData(..., pendingTempoDirectives);
```

Do not generalize all late header commands.

### Step 4: Parse channel `02`

- [ ] In `ParseMeasureData`, after extracting measure/channel and before channel `01`/drum routing:

```csharp
if (channel == 0x02)
{
    if (TryParseDouble(noteData, out var multiplier) && multiplier > 0)
    {
        chart.TimingMap.SetMeasureLength(measure, multiplier);
    }
    else
    {
        Debug.WriteLine(
            $"DTXChartParser: Ignoring invalid measure length at bar {measure}: '{noteData}'");
    }

    return;
}
```

Reuse the project's invariant numeric helper. Duplicate valid rows naturally use last parsed value.

### Step 5: Collect channel `03` and `08` directives in list order

- [ ] Reuse the existing pair-grid tick formula:

```text
pairCount = data.Length / 2
tick = (int)((double)i / pairCount * ChartTimingMap.TicksPerMeasure)
```

- [ ] If a timing row has malformed/incomplete pair data that is being ignored, emit `Debug.WriteLine` with bar/channel context.

- [ ] Channel `03`:

- `00` -> no-op, no log;
- valid positive hexadecimal pair -> append `PendingTempoDirective { Kind = DirectBpm, ... }`;
- invalid/non-positive non-`00` pair -> `Debug.WriteLine` and ignore.

- [ ] Channel `08`:

- `00` -> no-op, no log;
- otherwise uppercase the pair and append `PendingTempoDirective { Kind = ReferencedBpm, ReferenceId = pair, ... }`;
- do not convert the ID to an integer.

Do not call `TimingMap.SetTempoChange` yet. Do not add a sequence counter.

### Step 6: Resolve pending tempo directives after successful full scan

- [ ] Add:

```csharp
private static void ResolvePendingTempoDirectives(
    ParsedChart chart,
    IReadOnlyDictionary<string, double> bpmDefinitions,
    IReadOnlyList<PendingTempoDirective> directives)
```

- [ ] Iterate the list **directly**:

```text
foreach directive in directives:
    DirectBpm -> bpm = DirectBpm
    ReferencedBpm -> bpmDefinitions.TryGetValue(ReferenceId)
    unresolved reference -> Debug.WriteLine and continue
    valid -> chart.TimingMap.SetTempoChange(Bar, Tick, bpm)
```

Do not call `OrderBy`, do not add `Sequence`, and do not group by channel. `SetTempoChange` replacement plus insertion-order iteration is the cross-channel last-source-line-wins contract.

- [ ] Call resolution only after the encoding attempt has read the complete file successfully and before `chart.FinalizeChart()`.

### Step 7: Inspect required diagnostics

- [ ] Before GREEN, inspect the parser diff and confirm `Debug.WriteLine` exists for every ignored malformed timing path:

- invalid/non-positive channel `02`;
- invalid/non-positive non-`00` channel `03` pair;
- invalid/non-positive `#BPMxx`;
- unresolved channel `08` reference;
- malformed/incomplete timing body/pair that is intentionally ignored.

Do not log normal `00` no-op pairs.

Tests should verify fallback behavior, not exact debug strings.

### Step 8: Run focused parser + timing regressions

- [ ] Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter 'FullyQualifiedName~ChartTimingMapTests|FullyQualifiedName~ParsedChartTests|FullyQualifiedName~DTXChartParserTests|FullyQualifiedName~ChartManagerTests'
```

- [ ] Re-run Performance coverage from Task 2.

Expected: all timing, parser, and fixture regressions pass.

### Step 9: Full validation

- [ ] Build the Mac game project:

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
```

- [ ] Run the complete Mac-safe suite:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
```

- [ ] Run repository checks:

```bash
rtk git diff --check
rtk git status --short
```

- [ ] Confirm by inspection:

- no `ChartTimeCalculator` file remains;
- `ChartTimingMap` is internal;
- no `Note.CalculateTimeMs` / `BGMEvent.CalculateTimeMs` caller remains;
- finalized test charts treat `TimeMs` as output;
- touched helpers use canonical bar/tick authoring;
- oversized lookup ticks normalize correctly and finalization's rebuild horizon uses normalized bars;
- `#BPMxx` is recognized before and after timeline rows;
- both `03` and `08` resolve through one insertion-ordered pending list;
- ignored malformed timing syntax emits debug diagnostics;
- `SongChart.Duration` / metadata duration code is unchanged;
- renderer/stage production files are unchanged;
- no STOP/beat-line/unrelated timing feature was added.

### Step 10: Review and commit Task 3

- [ ] Review parser diff for syntax-only responsibilities and no gameplay coupling.
- [ ] Stage exactly:

```bash
rtk git add \
  DTXMania.Game/Lib/Song/DTXChartParser.cs \
  DTXMania.Test/Song/DTXChartParserTests.cs
rtk git commit -m "feat: parse DTX tempo and measure timing"
```

---

## Implementation Risks and Review Gates

### Risk 1: Test fixture blast radius

Removing event-level timing methods is compile-wide, and finalized fixtures that seed `TimeMs` may still compile while silently changing behavior.

**Gate:** Task 2 cannot commit until direct-call, finalization, seeded-time, and tick-authoring scans are reviewed and Song + Performance coverage is green.

### Risk 2: Oversized tick positions

Current helpers can produce `bar = 0`, `tick >= 192`. Linear extrapolation from bar 0 is wrong once later measures have different channel `02` values; clamping is also wrong.

**Gate:** `ChartTimingMap` normalization test plus `ParsedChart` normalized-horizon test must pass, and touched fixture helpers must convert desired times to canonical bar/tick positions.

### Risk 3: Late `#BPMxx` definitions

The current parser stops normal header routing after timeline data begins. Resolving channel `08` immediately drops references whose definitions are later in the file.

**Gate:** parser test with timeline/reference first and `#BPM01` later must pass.

### Risk 4: Cross-channel duplicate ordering

If channel `03` is applied immediately while `08` is replayed later, `08` always wins same-position collisions regardless of source order.

**Gate:** collect both channel types in one insertion-ordered list and test reversed source row order.

### Risk 5: Silent timing syntax failure

Ignoring malformed timing data without diagnostics produces chart/audio drift that looks like runtime latency.

**Gate:** parser review confirms every malformed/ignored timing branch emits `Debug.WriteLine`, except normal `00` no-op pairs.

### Risk 6: Two duration concepts remain

Gameplay `ParsedChart.DurationMs` becomes timing-map accurate while metadata `SongChart.Duration` remains approximate.

**Gate:** metadata duration code remains unchanged and the PR/release notes state this limitation explicitly.

## Completion Checklist

Before marking HPA-600 implementation complete:

- [ ] one internal `ChartTimingMap` owns chart-position timing;
- [ ] fixed `ChartTimeCalculator` and event-level fixed timing methods are deleted;
- [ ] lookup normalizes non-negative oversized ticks;
- [ ] finalization rebuild horizon uses normalized event positions;
- [ ] notes, BGM, measure lines, and gameplay duration all resolve from the same map;
- [ ] `02`, `03`, `08`, late `#BPMxx`, opaque `AA`, and source-order collision tests pass;
- [ ] non-zero-bar measure-length test passes;
- [ ] tick-zero tempo test passes;
- [ ] malformed timing syntax has debug diagnostics;
- [ ] fixture scans are clean and touched helpers use canonical positions;
- [ ] no `git add -A` was used for the migration commit;
- [ ] focused Song + Performance tests pass;
- [ ] full Mac-safe suite passes;
- [ ] Mac game build passes;
- [ ] E2E fixture path remains parser-driven and unchanged;
- [ ] metadata duration remains explicitly deferred;
- [ ] no unrelated runtime/UI/timing extension entered scope.
