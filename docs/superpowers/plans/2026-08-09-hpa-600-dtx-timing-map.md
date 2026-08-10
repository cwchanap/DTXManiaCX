# HPA-600 Full DTX Timing Map Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Parse DTX timing channels `02`, `03`, and `08` and resolve notes, BGM events, measure lines, and gameplay chart duration from one compiled timing map.

**Architecture:** Introduce `ChartTimingMap` as the only `(bar, tick) -> TimeMs` authority. Make `ParsedChart.FinalizeChart()` the only production absolute-time pass, migrate tests away from pre-seeded `TimeMs`, then extend `DTXChartParser` to collect timing syntax and resolve `03`/`08` tempo directives after the complete file is known. Runtime gameplay systems keep consuming finalized `TimeMs` and do not receive timing-map dependencies.

**Tech Stack:** .NET 8, C#, MonoGame 3.8, xUnit, existing DTX parser and Mac-safe test projects.

## Global Constraints

- Source of truth: [`2026-08-09-hpa-600-dtx-timing-map-design.md`](../specs/2026-08-09-hpa-600-dtx-timing-map-design.md).
- Channel `02` is a positive decimal measure-length multiplier; it affects only its own measure and defaults to `1.0`.
- Channel `03` uses non-`00` hexadecimal BPM pairs.
- Channel `08` uses opaque uppercase two-character object IDs resolved through positive numeric `#BPMxx` definitions; do not parse IDs such as `AA` as hex BPM.
- `#BPMxx` definitions must resolve even when they appear after the timeline row that references them.
- Channel `03` and `08` directives at the same `(bar, tick)` use later **source-file order**, independent of channel type.
- Preserve the existing normalized `192` ticks per measure; channel `02` scales musical duration, not the stored coordinate grid.
- `ParsedChart.FinalizeChart()` is the only production pass that assigns `Note.TimeMs`, `BGMEvent.TimeMs`, `MeasureLine.TimeMs`, and gameplay `DurationMs`.
- Finalization always overwrites pre-existing `TimeMs`; zero is a valid finalized timestamp, never an uncalculated sentinel.
- Gameplay `DurationMs` is `max(finalized note/BGM time) + 500 ms` when at least one retained event exists; measure lines never extend duration.
- `ParsedChart.Bpm` and `ChartManager.Bpm` remain the base exact `#BPM` value.
- `SongChart.Duration` / `CalculateDurationAsync` remain the existing base-BPM approximation. Do not expand HPA-600 into metadata timing-map parsing.
- Do not add `#BASEBPM`, `#BPM00`, STOP support, beat lines, runtime re-clocking, renderer/stage production changes, project-file changes, skins, workflows, or legacy DTXManiaNX changes.
- Delete the fixed `ChartTimeCalculator` once production callers move to `ChartTimingMap`; do not keep two timing authorities.
- Any hand-built `ParsedChart` that calls `FinalizeChart()` must author event position using `Bar`/`Tick`. Never rely on seeded `TimeMs` surviving finalization.
- Use the production-style fixture lifecycle already used by chart helpers: build bar/tick positions -> `FinalizeChart()` -> `ChartManager`.
- Prefix repository shell commands with `rtk`, matching existing implementation plans.
- Use xUnit names in `Scenario_ShouldExpect` form where touching tests and preserve current Mac-safe exclusions.
- Follow RED -> focused GREEN -> task review -> commit for every task.

## File Responsibility Map

### Production

- Create `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`: retain timing configuration, compile anchors, calculate absolute milliseconds.
- Modify `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`: own `TimingMap`; make finalization authoritative and duration idempotent.
- Modify `DTXMania.Game/Lib/Song/Components/Note.cs`: remove fixed-clock timing method and stale formula comments.
- Modify `DTXMania.Game/Lib/Song/Components/BGMEvent.cs`: remove fixed-clock timing method and stale formula comments.
- Modify `DTXMania.Game/Lib/Song/Components/ChartManager.cs`: remove `TimeMs == 0` fallback calculation.
- Delete `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`.
- Modify `DTXMania.Game/Lib/Song/DTXChartParser.cs`: parse channel `02`, collect ordered `03`/`08` directives, recognize `#BPMxx` anywhere, resolve tempo directives after full parse.

### Core tests

- Create `DTXMania.Test/Song/ChartTimingMapTests.cs`.
- Delete `DTXMania.Test/Song/ChartTimeCalculatorTests.cs`.
- Modify `DTXMania.Test/Song/ParsedChartTests.cs`.
- Modify `DTXMania.Test/Song/NoteTests.cs`.
- Modify `DTXMania.Test/Song/BGMEventTests.cs`.
- Modify `DTXMania.Test/Song/ChartManagerTests.cs`.
- Modify `DTXMania.Test/Song/DTXChartParserTests.cs`.

### Known fixture migration surface

The implementation-time scan is authoritative, but review already identified these files:

- `DTXMania.Test/Stage/Performance/TimingVerificationTest.cs`
- `DTXMania.Test/Stage/Performance/AutomatedPlaySimulationTests.cs`
- `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`

Other test files returned by the required `FinalizeChart` / `TimeMs` scan may also need fixture-only changes. Do not change Performance production code for those failures.

---

## Task 1: Build the Compiled `ChartTimingMap`

**Files:**

- Create: `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`
- Create: `DTXMania.Test/Song/ChartTimingMapTests.cs`

**Interfaces:**

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

`SetTempoChange` must replace an existing value for the same `(bar, tick)`. Parser source-order semantics in Task 3 rely on calls being applied in source order.

### Step 1: Add direct map tests

- [ ] Create `ChartTimingMapTests.cs` with the following contracts before production implementation:

```csharp
[Fact]
public void CalculateTimeMs_BaseBpmOnly_ShouldMatchOldClock()
{
    var map = new ChartTimingMap();
    map.Rebuild(120.0, throughBar: 2);

    Assert.Equal(0.0, map.CalculateTimeMs(0, 0), 3);
    Assert.Equal(1000.0, map.CalculateTimeMs(0, 96), 3);
    Assert.Equal(2000.0, map.CalculateTimeMs(1, 0), 3);
    Assert.Equal(5000.0, map.CalculateTimeMs(2, 96), 3);
}

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
```

- [ ] Run RED:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ChartTimingMapTests'
```

Expected: compile failure because `ChartTimingMap` does not exist.

### Step 2: Implement the retained configuration

- [ ] Create `ChartTimingMap.cs` with:

```csharp
private readonly Dictionary<int, double> _measureLengths = new();
private readonly Dictionary<(int Bar, int Tick), double> _tempoChanges = new();
private readonly List<TimingAnchor> _anchors = new();
```

Validation contract:

```csharp
internal void SetMeasureLength(int bar, double multiplier)
{
    if (bar < 0 || multiplier <= 0)
        return;

    _measureLengths[bar] = multiplier;
}

internal void SetTempoChange(int bar, int tick, double bpm)
{
    if (bar < 0 || tick < 0 || tick >= TicksPerMeasure || bpm <= 0)
        return;

    _tempoChanges[(bar, tick)] = bpm;
}
```

Do not expose dictionaries publicly.

### Step 3: Implement rebuild and lookup

- [ ] Use a private immutable anchor with:

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
3. clear compiled anchors but retain configured measure/tempo directives;
4. sort configured tempo changes by bar/tick;
5. walk bars `0..throughBar` sequentially;
6. add a measure-start anchor;
7. integrate intervals before each tempo change;
8. replace the effective anchor at the same position for tick-zero changes;
9. carry final BPM into the next bar.

Use exactly this interval formula:

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

- [ ] `CalculateTimeMs(bar, tick)` must binary-search the ordered anchors for the last anchor at or before `(bar, tick)`. Reject requests outside the compiled bar range rather than silently using a different measure.

Do not add a second cache or public anchor API.

### Step 4: Verify and commit Task 1

- [ ] Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ChartTimingMapTests'
rtk git diff --check
```

Expected: map tests pass.

- [ ] Review only Task 1 files, then commit:

```bash
rtk git add \
  DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs \
  DTXMania.Test/Song/ChartTimingMapTests.cs
rtk git commit -m "feat: add compiled chart timing map"
```

---

## Task 2: Make Finalization the Only Timing Authority and Migrate Fixtures

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

Fixture migration, at minimum:

- Modify: `DTXMania.Test/Stage/Performance/TimingVerificationTest.cs`
- Modify: `DTXMania.Test/Stage/Performance/AutomatedPlaySimulationTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Modify any additional test helper discovered by the mandatory scan below.

**Interfaces produced:**

```csharp
internal ChartTimingMap TimingMap { get; }
```

`ParsedChart.FinalizeChart()` becomes the only production assignment point for all absolute timing.

### Step 1: Inventory the migration surface before editing

- [ ] Run all three searches and keep the results visible while implementing Task 2:

```bash
rtk rg -n 'CalculateTimeMs\(' DTXMania.Game DTXMania.Test
rtk rg -n 'FinalizeChart\(\)' DTXMania.Test
rtk rg -n 'TimeMs\s*=' DTXMania.Test
```

Classify hits as:

1. production fixed-clock path to remove;
2. timing unit test to move to `ChartTimingMap` / finalization;
3. finalized chart fixture that incorrectly seeds `TimeMs`;
4. pure already-finalized runtime object that intentionally does not call `FinalizeChart()`.

Do not treat the known-file list as exhaustive.

### Step 2: Add failing finalization tests

- [ ] Update `ParsedChartTests` first to establish the new lifecycle:

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

[Fact]
public void FinalizeChart_ShouldOverwriteSeededTimeFromBarAndTick()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    var note = new Note(0, 1, 0, 0x11, "01") { TimeMs = 12345.0 };
    chart.AddNote(note);

    chart.FinalizeChart();

    Assert.Equal(2000.0, note.TimeMs, 3);
}

[Fact]
public void FinalizeChart_TimeZeroOnlyEvent_ShouldStillAddDurationBuffer()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddNote(new Note(0, 0, 0, 0x11, "01"));

    chart.FinalizeChart();

    Assert.Equal(500.0, chart.DurationMs, 3);
}

[Fact]
public void FinalizeChart_Repeated_ShouldKeepDurationIdempotent()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddNote(new Note(0, 1, 0, 0x11, "01"));
    chart.FinalizeChart();
    var first = chart.DurationMs;

    chart.FinalizeChart();

    Assert.Equal(first, chart.DurationMs, 3);
}
```

Also retain/update sparse measure-line, BGM-only, sorting, and terminal-boundary tests so they assert map-driven values.

- [ ] Run RED against `ParsedChartTests`.

### Step 3: Move `ParsedChart` to the timing map

- [ ] Add one chart-owned map:

```csharp
internal ChartTimingMap TimingMap { get; } = new ChartTimingMap();
```

- [ ] Simplify `AddNote`:

- ignore null;
- add note;
- update `NotesPerLane` only;
- do not assign `TimeMs`;
- do not update `DurationMs`.

- [ ] Simplify `AddBGMEvent`:

- ignore null;
- add event only;
- do not assign time/duration.

- [ ] Rewrite `FinalizeChart()` in this order:

1. clear `MeasureLines`;
2. reset `DurationMs = 0`;
3. determine highest occupied bar across notes/BGM;
4. if none, sort/return with no lines and zero duration;
5. `TimingMap.Rebuild(Bpm, highestOccupiedBar + 1)`;
6. overwrite every `Note.TimeMs` from `TimingMap.CalculateTimeMs(note.Bar, note.Tick)`;
7. overwrite every `BGMEvent.TimeMs` the same way;
8. regenerate measure lines for bars `0..highestOccupiedBar + 1` from tick `0`;
9. sort notes and BGM by recalculated time;
10. set `DurationMs = max(note/BGM finalized time) + 500.0`.

Do not derive duration from measure lines.

### Step 4: Remove old fixed-clock escape paths

- [ ] Remove `Note.CalculateTimeMs(double bpm)`.
- [ ] Remove `BGMEvent.CalculateTimeMs(double bpm)`.
- [ ] Remove stale fixed-formula XML comments from both models.
- [ ] Remove `ChartManager` constructor's `TimeMs == 0` recalculation loop.
- [ ] Delete `ChartTimeCalculator.cs` and `ChartTimeCalculatorTests.cs`.
- [ ] Remove/rewrite direct timing tests in `NoteTests`, `BGMEventTests`, and `TimingVerificationTest` so timing math is tested through `ChartTimingMap` or finalized charts.

Do **not** add a replacement per-note helper.

### Step 5: Migrate hand-built fixtures

Mandatory fixture policy:

> If a test calls `FinalizeChart()`, `Bar`/`Tick` are the authored inputs and `TimeMs` is an output.

- [ ] `TimingVerificationTest.cs`: remove the direct `note.CalculateTimeMs(120)` path. Keep lifecycle coverage by adding note bar/tick -> `ParsedChart.FinalizeChart()` -> `ChartManager` assertions, or remove redundant math coverage now owned by `ChartTimingMapTests`.

- [ ] `AutomatedPlaySimulationTests.cs`: its helper currently computes every note with `note.CalculateTimeMs(bpm)`. Replace the helper with a small `ParsedChart`, add bar/tick-authored notes, finalize once, then return/copy the finalized notes required by the simulation.

- [ ] `PerformanceStageDeterministicTests.cs`: convert any chart that both seeds `TimeMs` and calls `FinalizeChart()` to meaningful bar/tick coordinates. Example: a desired `4000 ms` note at 120 BPM should use `Bar = 2`, `Tick = 0`, not `{ TimeMs = 4000 }` before finalization.

- [ ] Inspect every additional hit from Step 1. Apply the same rule. If a test truly models an already-finalized runtime note list and never needs chart derivation, it may keep explicit `TimeMs` **only if it does not pass through `FinalizeChart()`**.

Do not weaken finalization to preserve old fixtures.

### Step 6: Verify the Task 2 migration surface

- [ ] Re-run source scans:

```bash
rtk rg -n 'CalculateTimeMs\(' DTXMania.Game DTXMania.Test
rtk rg -n 'FinalizeChart\(\)' DTXMania.Test
rtk rg -n 'TimeMs\s*=' DTXMania.Test
```

Expected:

- no production or test call to deleted `Note/BGMEvent.CalculateTimeMs`;
- any remaining seeded `TimeMs` is either expected assertion/setup after finalization or belongs to a runtime-only object that does not get finalized;
- finalized charts are bar/tick authored.

- [ ] Run focused Song + Performance regression coverage:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter 'FullyQualifiedName~DTXMania.Test.Song|FullyQualifiedName~DTXMania.Test.Stage.Performance'
```

If the current test runner/filter syntax rejects that expression, run the two namespaces separately. Do not skip Performance coverage because the production change is in Song code; the fixture migration is part of Task 2.

- [ ] Run:

```bash
rtk git diff --check
```

### Step 7: Review and commit Task 2

- [ ] Review that production changes are limited to Song components; Performance changes are tests/helpers only.
- [ ] Confirm no non-zero `TimeMs` preservation branch or new sentinel exists.
- [ ] Commit all Task 2 production and fixture migrations together because deleting the API without migrating compile/runtime consumers is not independently green:

```bash
rtk git add -A
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

Use temporary files following the existing parser-test pattern. Each fixture must contain at least one retained note/BGM event so `FinalizeChart()` builds a gameplay timeline.

- [ ] **Short measure:** base 120, bar 0 channel `02 = 0.5`, note at bar 1 start -> `1000 ms`.

```text
#BPM: 120
#00002:0.5
#00111:01
```

- [ ] **Extended measure:** base 120, bar 0 channel `02 = 1.5`, note at bar 1 start -> `3000 ms`.

- [ ] **Direct channel 03:** base 120, tempo changes to hex `F0` (=240) halfway through bar 0; bar 1 start -> `1500 ms`.

```text
#BPM: 120
#00003:00F0
#00111:01
```

- [ ] **Referenced channel 08, definition first:** `#BPM01:180.5` and channel `08` reference `01` at a known tick; assert expected note time.

- [ ] **Referenced channel 08, definition late:** channel `08` row appears first, then `#BPM01:180.5` later in the source file, then/around other timeline rows. The final note must still use `180.5`.

Example order that specifically proves the current `inDataSection` limitation is fixed:

```text
#BPM: 120
#00008:0001
#00111:01
#BPM01:180.5
```

The test must resolve the reference despite `#BPM01` appearing after timeline data has begun.

- [ ] **Opaque object ID:** use `#BPMAA:210.25` and channel `08` pair `AA`; assert it resolves. This prevents accidental hex parsing of channel `08` IDs.

- [ ] **Cross-channel same-position source order:** create a chart where channel `03` and `08` both target the same tick. The later source row must win. Prefer two tests with reversed source order if concise:

```text
#00003:00B4
#00008:0001
```

versus:

```text
#00008:0001
#00003:00B4
```

Use a `#BPM01` value different from direct hex `B4` so the winning tempo is observable.

- [ ] **Alignment:** one chart containing a note, BGM event, and measure boundaries across a tempo change; assert all use the same map-derived timeline.

- [ ] **Malformed fallback:** invalid/zero measure multiplier, bad/missing BPM reference, and `00` pairs do not crash and retain base/default behavior.

Run parser tests and observe RED before implementation.

### Step 2: Add per-encoding parser state

- [ ] In `ParseAsync`, create fresh state inside each encoding attempt alongside WAV dictionaries:

```csharp
Dictionary<string, double> bpmDefinitions;
List<PendingTempoDirective> pendingTempoDirectives;
```

Initialize both anew for every attempt. Do not share them across retries.

- [ ] Add a parser-private sequence number, either stored in each directive as list insertion index or an explicit `Sequence` integer. The order must be stable across channel `03` and `08` rows.

A minimal private model is enough:

```csharp
private enum PendingTempoKind
{
    DirectBpm,
    ReferencedBpm
}

private sealed class PendingTempoDirective
{
    public int Sequence { get; init; }
    public int Bar { get; init; }
    public int Tick { get; init; }
    public PendingTempoKind Kind { get; init; }
    public double DirectBpm { get; init; }
    public string ReferenceId { get; init; } = "";
}
```

Do not make this public or move it into `Components`; it is DTX syntax state only.

### Step 3: Recognize `#BPMxx` definitions anywhere in the file

- [ ] Add a focused helper called before the existing `inDataSection` routing on every non-comment line:

```csharp
private static bool TryParseExtendedBpmDefinition(
    string line,
    IDictionary<string, double> bpmDefinitions)
```

Required behavior:

- return `false` for exact `#BPM:...` so existing base BPM parsing remains unchanged;
- recognize only `#BPMxx:value` with a non-empty two-character suffix;
- uppercase the suffix;
- parse value using invariant-culture positive `double`;
- valid duplicate definition -> replace previous dictionary value;
- malformed/non-positive definition -> consume/ignore or return handled consistently, but must not accidentally become timeline/header data;
- work before and after timeline data has begun.

`ParseFileContentAsync` flow should conceptually become:

```csharp
line = line.Trim();
if (skip comment/empty) continue;

if (TryParseExtendedBpmDefinition(line, bpmDefinitions))
    continue;

if (line starts timeline/data marker)
    inDataSection = true;

if (!inDataSection)
    ParseHeaderCommand(...); // exact #BPM still here
else
    ParseMeasureData(..., pendingTempoDirectives);
```

Do not generalize all late header commands.

### Step 4: Parse channel 02 into the timing map

- [ ] In `ParseMeasureData`, after extracting measure/channel and before channel `01`/drum routing:

```csharp
if (channel == 0x02)
{
    if (TryParseDouble(noteData, out var multiplier) && multiplier > 0)
        chart.TimingMap.SetMeasureLength(measure, multiplier);
    return;
}
```

Use the project's invariant numeric helper. Duplicate valid rows naturally use last parsed value through dictionary assignment.

### Step 5: Collect channel 03 and 08 directives without resolving them

- [ ] Reuse one pair-loop helper/tick formula rather than duplicating note-grid math:

```text
pairCount = data.Length / 2
tick = (int)((double)i / pairCount * ChartTimingMap.TicksPerMeasure)
```

- [ ] Channel `03`:

- skip `00`;
- parse pair as hexadecimal integer BPM;
- require positive value;
- append `PendingTempoDirective { Kind = DirectBpm, DirectBpm = value, ... }`.

- [ ] Channel `08`:

- skip `00`;
- uppercase the two-character pair;
- do not convert it to an integer;
- append `PendingTempoDirective { Kind = ReferencedBpm, ReferenceId = pair, ... }`.

- [ ] Both channels increment/share one source sequence in parse order. Do not call `TimingMap.SetTempoChange` yet.

This deferred handling is required both for late definitions and correct same-position source-order precedence.

### Step 6: Resolve pending tempo directives after successful full-file scan

- [ ] Add:

```csharp
private static void ResolvePendingTempoDirectives(
    ParsedChart chart,
    IReadOnlyDictionary<string, double> bpmDefinitions,
    IEnumerable<PendingTempoDirective> directives)
```

Implementation contract:

```text
for directives ordered by Sequence:
    DirectBpm -> bpm = DirectBpm
    ReferencedBpm -> bpmDefinitions.TryGetValue(ReferenceId)
    invalid/unresolved -> continue
    chart.TimingMap.SetTempoChange(Bar, Tick, bpm)
```

Because `SetTempoChange` replaces the same position, applying valid directives in source order makes the later source row win across channels.

- [ ] Call resolution only after the encoding attempt has read the complete file successfully and before `chart.FinalizeChart()`.

Keep resolution within the successful attempt's state lifetime. Failed-attempt dictionaries/directives must be discarded with that attempt.

### Step 7: Run focused parser + timing regressions

- [ ] Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter 'FullyQualifiedName~ChartTimingMapTests|FullyQualifiedName~ParsedChartTests|FullyQualifiedName~DTXChartParserTests|FullyQualifiedName~ChartManagerTests'
```

Expected: all new timing/parser behavior passes.

- [ ] Re-run the Task 2 Performance filter to ensure parser integration did not alter fixture behavior unexpectedly.

### Step 8: Full validation

- [ ] Build the Mac game project:

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
```

- [ ] Run the complete Mac-safe test suite:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
```

- [ ] Run repository checks:

```bash
rtk git diff --check
rtk git status --short
```

- [ ] Confirm by inspection:

- no `ChartTimeCalculator` production/test file remains;
- no `Note.CalculateTimeMs` / `BGMEvent.CalculateTimeMs` caller remains;
- finalized test charts use bar/tick-authored positions;
- `#BPMxx` is recognized before and after timeline rows;
- both `03` and `08` are resolved through the ordered pending-directive path;
- `SongChart.Duration` / metadata duration code was not expanded;
- renderer/stage **production** files are unchanged;
- no STOP/beat-line/unrelated timing feature was added.

### Step 9: Review and commit Task 3

- [ ] Review parser diff for syntax-only responsibilities and no gameplay coupling.
- [ ] Commit:

```bash
rtk git add \
  DTXMania.Game/Lib/Song/DTXChartParser.cs \
  DTXMania.Test/Song/DTXChartParserTests.cs
rtk git commit -m "feat: parse DTX tempo and measure timing"
```

---

## Implementation Risks and Review Gates

### Risk 1: Test fixture blast radius

Removing `Note/BGMEvent.CalculateTimeMs` is a compile-wide change even when a filtered xUnit run is requested. More importantly, finalized tests that seed `TimeMs` may still compile but silently change behavior.

**Gate:** Task 2 cannot commit until direct-call and `TimeMs`/`FinalizeChart` scans are reviewed and Song + Performance test coverage is green.

### Risk 2: Late `#BPMxx` definitions

The current parser stops normal header routing after the first timeline row. Resolving channel `08` immediately would drop references whose definitions are later in the file.

**Gate:** parser test with timeline row first and `#BPM01` later must pass.

### Risk 3: Cross-channel duplicate ordering

If channel `03` is applied immediately but `08` is replayed later, `08` would always win same-position collisions regardless of source order.

**Gate:** collect both channel types in one ordered pending list and test reversed source order.

### Risk 4: Two duration concepts remain

Gameplay `ParsedChart.DurationMs` becomes timing-map accurate, while metadata `SongChart.Duration` remains approximate.

**Gate:** keep this divergence explicit in code/design review; do not opportunistically build a second metadata timing parser in HPA-600.

### Risk 5: Accidentally retaining two clocks

Keeping non-zero seeded `TimeMs`, retaining `ChartTimeCalculator`, or adding a replacement event-level timing method recreates the original problem.

**Gate:** final source scan and diff review must show one timing authority.

---

## Completion Checklist

- [ ] `ChartTimingMap` passes base, measure-length, mid-measure tempo, tick-zero, duplicate, and rebuild tests.
- [ ] `ParsedChart.FinalizeChart()` always recalculates all retained event times.
- [ ] Finalization duration is idempotent and handles a lone time-zero event as `500 ms` duration.
- [ ] `ChartManager` no longer treats zero time as uncalculated.
- [ ] `ChartTimeCalculator` and event-level fixed-clock APIs are removed.
- [ ] Known and scan-discovered finalized test fixtures use authored bar/tick positions.
- [ ] Channel `02` supports shortened and extended measures.
- [ ] Channel `03` supports direct hex BPM changes.
- [ ] Channel `08` supports fractional/extended `#BPMxx` values.
- [ ] Late `#BPMxx` definition resolves after timeline data has begun.
- [ ] `AA` object ID resolves through channel `08` without hex conversion.
- [ ] Same-position `03`/`08` uses later source-file order.
- [ ] Notes, BGM, and measure lines remain aligned on the same map.
- [ ] `SongChart.Duration` remains an explicitly deferred approximate metadata path.
- [ ] No renderer/stage production, audio scheduling, judgement, scoring, persistence, skin, project, workflow, or legacy changes.
- [ ] Focused Song/Performance regressions pass.
- [ ] Full Mac-safe suite passes.
- [ ] Mac game build passes.
