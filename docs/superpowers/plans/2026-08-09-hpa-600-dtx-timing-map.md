# HPA-600 Full DTX Timing Map Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Parse DTX channels `02`, `03`, and `08` and resolve notes, BGM events, measure lines, and chart duration from one compiled chart timing map.

**Architecture:** Introduce `ChartTimingMap` as the only `(bar, tick) -> TimeMs` authority, first proving it against the existing constant 4/4 clock. Move absolute-time assignment into `ParsedChart.FinalizeChart()` so all retained events are recalculated together, then extend `DTXChartParser` to populate measure-length and tempo changes from channels `02`, `03`, and `08`. Runtime systems continue consuming finalized absolute times and do not receive timing-map dependencies.

**Tech Stack:** .NET 8, C#, MonoGame 3.8, xUnit, existing DTX parser and Mac-safe test projects.

## Global Constraints

- Source of truth: [`2026-08-09-hpa-600-dtx-timing-map-design.md`](../specs/2026-08-09-hpa-600-dtx-timing-map-design.md).
- Channel `02` is a positive decimal measure-length multiplier; it affects only its own measure and defaults to `1.0`.
- Channel `03` uses non-`00` hexadecimal byte BPM values at pair-derived ticks.
- Channel `08` resolves non-`00` pair IDs through positive numeric `#BPMxx` definitions and stores the resolved BPM in the timing map.
- Tempo changes persist until another tempo change; duplicate changes at one `(bar, tick)` use last-parsed-wins semantics.
- Keep the existing normalized `192` ticks per measure; channel `02` scales musical duration, not stored tick count.
- `ParsedChart.FinalizeChart()` is the only production pass that assigns `Note.TimeMs`, `BGMEvent.TimeMs`, `MeasureLine.TimeMs`, and `DurationMs`.
- `DurationMs` is `max(finalized note/BGM time) + 500 ms` when at least one retained event exists; measure lines never extend duration.
- `ParsedChart.Bpm` and `ChartManager.Bpm` remain the base `#BPM` value.
- Do not add gameplay-time timing calculation, a second clock, STOP support, renderer/stage changes, project-file changes, skin changes, or legacy `DTXManiaNX` changes.
- Remove the fixed-clock `ChartTimeCalculator` once all production callers have moved to `ChartTimingMap`; do not keep two timing authorities.
- Unit tests that hand-build `ParsedChart` objects must call `FinalizeChart()` before constructing `ChartManager`.
- Prefix repository shell commands with `rtk`, matching the existing project implementation plans.
- Use xUnit names in `Scenario_ShouldExpect` form and preserve the existing Mac-safe test exclusions.
- Follow RED -> focused GREEN -> review -> commit for each task.

## File Responsibility Map

- Create `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`: retain normalized timing directives, compile timing anchors, and calculate absolute milliseconds.
- Create `DTXMania.Test/Song/ChartTimingMapTests.cs`: direct timing-math and rebuild-contract tests.
- Modify `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`: own the timing map and make finalization authoritative for all absolute times and duration.
- Modify `DTXMania.Game/Lib/Song/Components/Note.cs`: remove the fixed base-BPM timing responsibility and stale formula documentation.
- Modify `DTXMania.Game/Lib/Song/Components/BGMEvent.cs`: remove the fixed base-BPM timing responsibility and stale formula documentation.
- Modify `DTXMania.Game/Lib/Song/Components/ChartManager.cs`: remove zero-time fallback calculation; consume only finalized chart state.
- Delete `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`: HPA-518 temporary fixed-clock seam replaced by `ChartTimingMap`.
- Modify `DTXMania.Game/Lib/Song/DTXChartParser.cs`: parse `#BPMxx`, channel `02`, channel `03`, and channel `08` into the chart timing map.
- Modify `DTXMania.Test/Song/ParsedChartTests.cs`: deferred-time, map-driven finalization, duration, sorting, and idempotence coverage.
- Modify `DTXMania.Test/Song/NoteTests.cs`: remove direct fixed-clock timing tests; retain note-model responsibilities.
- Modify `DTXMania.Test/Song/BGMEventTests.cs`: remove direct fixed-clock timing tests; retain BGM-model responsibilities.
- Modify `DTXMania.Test/Song/ChartManagerTests.cs`: prove finalized time-zero events are copied without fallback recalculation.
- Delete `DTXMania.Test/Song/ChartTimeCalculatorTests.cs`: replaced by `ChartTimingMapTests`.
- Modify `DTXMania.Test/Song/DTXChartParserTests.cs`: inline-file integration coverage for channels `02`, `03`, and `08` and cross-collection alignment.

---

### Task 1: Build the Compiled Chart Timing Map

**Files:**

- Create: `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs`
- Create: `DTXMania.Test/Song/ChartTimingMapTests.cs`

**Interfaces:**

- Consumes: base BPM, positive per-bar measure multipliers, and positive BPM changes at normalized `(bar, tick)` positions.
- Produces:

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

Later tasks rely on these exact names and signatures.

- [ ] **Step 1: Write direct timing-map tests and observe RED**

Create `DTXMania.Test/Song/ChartTimingMapTests.cs` with:

```csharp
using System;
using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Song")]
    public class ChartTimingMapTests
    {
        [Fact]
        public void CalculateTimeMs_BaseBpmOnly_ShouldMatchCurrentClock()
        {
            var map = new ChartTimingMap();
            map.Rebuild(120.0, throughBar: 2);

            Assert.Equal(0.0, map.CalculateTimeMs(0, 0), 3);
            Assert.Equal(1000.0, map.CalculateTimeMs(0, 96), 3);
            Assert.Equal(2000.0, map.CalculateTimeMs(1, 0), 3);
            Assert.Equal(5000.0, map.CalculateTimeMs(2, 96), 3);
        }

        [Fact]
        public void CalculateTimeMs_ShortMeasure_ShouldShiftOnlyFollowingTimeline()
        {
            var map = new ChartTimingMap();
            map.SetMeasureLength(0, 0.5);
            map.Rebuild(120.0, throughBar: 2);

            Assert.Equal(1000.0, map.CalculateTimeMs(1, 0), 3);
            Assert.Equal(3000.0, map.CalculateTimeMs(2, 0), 3);
        }

        [Fact]
        public void CalculateTimeMs_ExtendedMeasure_ShouldShiftFollowingBar()
        {
            var map = new ChartTimingMap();
            map.SetMeasureLength(0, 1.5);
            map.Rebuild(120.0, throughBar: 1);

            Assert.Equal(3000.0, map.CalculateTimeMs(1, 0), 3);
        }

        [Fact]
        public void CalculateTimeMs_TempoChangeHalfway_ShouldIntegrateAndPersist()
        {
            var map = new ChartTimingMap();
            map.SetTempoChange(0, 96, 240.0);
            map.Rebuild(120.0, throughBar: 1);

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
            map.Rebuild(120.0, throughBar: 1);

            Assert.Equal(500.0, map.CalculateTimeMs(0, 96), 3);
            Assert.Equal(750.0, map.CalculateTimeMs(1, 0), 3);
        }

        [Fact]
        public void Rebuild_WhenCalledAgain_ShouldReplaceCompiledTimeline()
        {
            var map = new ChartTimingMap();
            map.SetMeasureLength(0, 0.5);
            map.Rebuild(120.0, throughBar: 1);
            var first = map.CalculateTimeMs(1, 0);

            map.Rebuild(120.0, throughBar: 1);

            Assert.Equal(first, map.CalculateTimeMs(1, 0), 3);
        }

        [Fact]
        public void SetTempoChange_SamePosition_ShouldUseLastValue()
        {
            var map = new ChartTimingMap();
            map.SetTempoChange(0, 96, 180.0);
            map.SetTempoChange(0, 96, 240.0);
            map.Rebuild(120.0, throughBar: 1);

            Assert.Equal(1500.0, map.CalculateTimeMs(1, 0), 3);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-120.0)]
        public void Rebuild_NonPositiveBaseBpm_ShouldThrow(double bpm)
        {
            var map = new ChartTimingMap();

            Assert.Throws<ArgumentException>(() => map.Rebuild(bpm, throughBar: 1));
        }
    }
}
```

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ChartTimingMapTests'
```

Expected: FAIL during compilation because `ChartTimingMap` does not exist.

- [ ] **Step 2: Implement retained timing configuration and compiled anchors**

Create `DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Game.Lib.Song.Components
{
    public sealed class ChartTimingMap
    {
        public const int TicksPerMeasure = 192;

        private readonly Dictionary<int, double> _measureLengths = new();
        private readonly Dictionary<(int Bar, int Tick), double> _tempoChanges = new();
        private readonly List<TimingAnchor> _anchors = new();

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

        internal void Rebuild(double baseBpm, int throughBar)
        {
            if (baseBpm <= 0)
                throw new ArgumentException("BPM must be greater than 0", nameof(baseBpm));
            if (throughBar < 0)
                throw new ArgumentOutOfRangeException(nameof(throughBar));

            _anchors.Clear();

            var orderedChanges = _tempoChanges
                .Select(pair => new TempoEntry(pair.Key.Bar, pair.Key.Tick, pair.Value))
                .OrderBy(change => change.Bar)
                .ThenBy(change => change.Tick)
                .ToArray();

            var changeIndex = 0;
            var currentBpm = baseBpm;
            var currentTimeMs = 0.0;

            for (var bar = 0; bar <= throughBar; bar++)
            {
                var multiplier = _measureLengths.TryGetValue(bar, out var value)
                    ? value
                    : 1.0;
                var currentTick = 0;

                AddAnchor(bar, 0, currentTimeMs, currentBpm, multiplier);

                while (changeIndex < orderedChanges.Length && orderedChanges[changeIndex].Bar == bar)
                {
                    var change = orderedChanges[changeIndex++];
                    currentTimeMs += CalculateIntervalMs(
                        change.Tick - currentTick,
                        multiplier,
                        currentBpm);
                    currentTick = change.Tick;
                    currentBpm = change.Bpm;
                    AddAnchor(bar, currentTick, currentTimeMs, currentBpm, multiplier);
                }

                currentTimeMs += CalculateIntervalMs(
                    TicksPerMeasure - currentTick,
                    multiplier,
                    currentBpm);
            }
        }

        public double CalculateTimeMs(int bar, int tick)
        {
            if (_anchors.Count == 0)
                throw new InvalidOperationException("Timing map has not been built.");

            var index = FindAnchorIndex(bar, tick);
            if (index < 0 || _anchors[index].Bar != bar)
                throw new InvalidOperationException("Timing map was not built through the requested bar.");

            var anchor = _anchors[index];
            return anchor.TimeMs + CalculateIntervalMs(
                tick - anchor.Tick,
                anchor.MeasureLengthMultiplier,
                anchor.Bpm);
        }

        private void AddAnchor(
            int bar,
            int tick,
            double timeMs,
            double bpm,
            double measureLengthMultiplier)
        {
            var anchor = new TimingAnchor(
                bar,
                tick,
                timeMs,
                bpm,
                measureLengthMultiplier);

            if (_anchors.Count > 0 &&
                _anchors[^1].Bar == bar &&
                _anchors[^1].Tick == tick)
            {
                _anchors[^1] = anchor;
                return;
            }

            _anchors.Add(anchor);
        }

        private int FindAnchorIndex(int bar, int tick)
        {
            var left = 0;
            var right = _anchors.Count - 1;
            var result = -1;

            while (left <= right)
            {
                var mid = left + ((right - left) / 2);
                var anchor = _anchors[mid];
                var isAtOrBefore =
                    anchor.Bar < bar ||
                    (anchor.Bar == bar && anchor.Tick <= tick);

                if (isAtOrBefore)
                {
                    result = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return result;
        }

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

        private sealed class TimingAnchor
        {
            internal TimingAnchor(
                int bar,
                int tick,
                double timeMs,
                double bpm,
                double measureLengthMultiplier)
            {
                Bar = bar;
                Tick = tick;
                TimeMs = timeMs;
                Bpm = bpm;
                MeasureLengthMultiplier = measureLengthMultiplier;
            }

            internal int Bar { get; }
            internal int Tick { get; }
            internal double TimeMs { get; }
            internal double Bpm { get; }
            internal double MeasureLengthMultiplier { get; }
        }

        private sealed class TempoEntry
        {
            internal TempoEntry(int bar, int tick, double bpm)
            {
                Bar = bar;
                Tick = tick;
                Bpm = bpm;
            }

            internal int Bar { get; }
            internal int Tick { get; }
            internal double Bpm { get; }
        }
    }
}
```

Do not expose raw anchor collections publicly and do not add runtime caching beyond `_anchors`.

- [ ] **Step 3: Run timing-map tests and observe GREEN**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ChartTimingMapTests'
```

Expected: PASS.

- [ ] **Step 4: Review Task 1 and commit**

Run:

```bash
rtk git diff --check
rtk git status --short
rtk git diff -- DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs DTXMania.Test/Song/ChartTimingMapTests.cs
rtk git add DTXMania.Game/Lib/Song/Components/ChartTimingMap.cs DTXMania.Test/Song/ChartTimingMapTests.cs
rtk git commit -m "feat: add compiled chart timing map"
```

Expected: one focused commit containing only the new timing abstraction and its direct tests.

---

### Task 2: Make ParsedChart Finalization the Timing Authority

**Files:**

- Modify: `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/Note.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/BGMEvent.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/ChartManager.cs`
- Delete: `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`
- Modify: `DTXMania.Test/Song/ParsedChartTests.cs`
- Modify: `DTXMania.Test/Song/NoteTests.cs`
- Modify: `DTXMania.Test/Song/BGMEventTests.cs`
- Modify: `DTXMania.Test/Song/ChartManagerTests.cs`
- Delete: `DTXMania.Test/Song/ChartTimeCalculatorTests.cs`

**Interfaces:**

- Consumes: `ChartTimingMap` from Task 1.
- Produces: `internal ChartTimingMap ParsedChart.TimingMap { get; }`, fully resolved `Note.TimeMs`, `BGMEvent.TimeMs`, `MeasureLine.TimeMs`, and idempotent `DurationMs` after `FinalizeChart()`.
- Production invariant after this task: only `ParsedChart.FinalizeChart()` assigns absolute chart time.

- [ ] **Step 1: Rewrite ParsedChart tests for deferred timing and idempotent finalization**

Replace tests that expect `AddNote`/`AddBGMEvent` to calculate time immediately with:

```csharp
[Fact]
public void AddNote_ShouldDeferTimingUntilFinalize()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    var note = new Note(3, 1, 0, 0x12, "01");

    chart.AddNote(note);

    Assert.Equal(0.0, note.TimeMs);
    Assert.Equal(0.0, chart.DurationMs);
    Assert.Single(chart.Notes);
    Assert.Equal(1, chart.NotesPerLane[3]);
}

[Fact]
public void AddBGMEvent_ShouldDeferTimingUntilFinalize()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    var bgm = new BGMEvent(2, 0, "01");

    chart.AddBGMEvent(bgm);

    Assert.Equal(0.0, bgm.TimeMs);
    Assert.Equal(0.0, chart.DurationMs);
    Assert.Single(chart.BGMEvents);
}

[Fact]
public void FinalizeChart_ShouldOverwriteProvisionalTimesFromTimingMap()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    var note = new Note(3, 1, 0, 0x12, "01") { TimeMs = 123.0 };
    var bgm = new BGMEvent(1, 96, "01") { TimeMs = 456.0 };
    chart.AddNote(note);
    chart.AddBGMEvent(bgm);

    chart.FinalizeChart();

    Assert.Equal(2000.0, note.TimeMs, 3);
    Assert.Equal(3000.0, bgm.TimeMs, 3);
}

[Fact]
public void FinalizeChart_WhenCalledTwice_ShouldKeepDurationStable()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddNote(new Note(0, 1, 0, 0x11, "01"));

    chart.FinalizeChart();
    var firstDuration = chart.DurationMs;
    chart.FinalizeChart();

    Assert.Equal(firstDuration, chart.DurationMs, 3);
    Assert.Equal(2500.0, chart.DurationMs, 3);
}

[Fact]
public void FinalizeChart_TimeZeroOnlyEvent_ShouldStillApplyEndBuffer()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddNote(new Note(0, 0, 0, 0x11, "01"));

    chart.FinalizeChart();

    Assert.Equal(500.0, chart.DurationMs, 3);
}
```

Update the existing sorting tests to build notes/BGM events in unsorted bar/tick order, call `FinalizeChart()`, and assert ascending resolved `TimeMs`; do not seed `TimeMs` to control sorting anymore. Keep the existing sparse/BGM-only/empty/terminal measure-line cases, but let `FinalizeChart()` generate all expected times from bar/tick.

In `ChartManagerTests`, add:

```csharp
[Fact]
public void Constructor_FinalizedTimeZeroNote_ShouldKeepResolvedZeroTime()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddNote(new Note(0, 0, 0, 0x11, "01"));
    chart.FinalizeChart();

    var manager = new ChartManager(chart);

    Assert.Equal(0.0, manager.AllNotes.Single().TimeMs, 3);
}
```

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ParsedChartTests|FullyQualifiedName~ChartManagerTests'
```

Expected: RED because current add/finalize/constructor behavior still uses the fixed calculator and zero-time sentinel.

- [ ] **Step 2: Add the timing map to ParsedChart and defer absolute-time work**

Add beside the retained chart collections:

```csharp
internal ChartTimingMap TimingMap { get; } = new ChartTimingMap();
```

Change `AddNote(...)` to:

```csharp
public void AddNote(Note note)
{
    if (note == null)
        return;

    Notes.Add(note);

    if (note.LaneIndex >= 0 && note.LaneIndex < 10)
        NotesPerLane[note.LaneIndex]++;
}
```

Change `AddBGMEvent(...)` to:

```csharp
public void AddBGMEvent(BGMEvent bgmEvent)
{
    if (bgmEvent == null)
        return;

    BGMEvents.Add(bgmEvent);
}
```

Do not calculate `TimeMs` or `DurationMs` in either add method.

- [ ] **Step 3: Rebuild all finalized time from one map**

Refactor the timing portion of `FinalizeChart()` to this flow, preserving the existing DEBUG summary after timing is resolved:

```csharp
public void FinalizeChart()
{
    var highestOccupiedBar = -1;
    if (Notes.Count > 0)
        highestOccupiedBar = Math.Max(highestOccupiedBar, Notes.Max(note => note.Bar));
    if (BGMEvents.Count > 0)
        highestOccupiedBar = Math.Max(
            highestOccupiedBar,
            BGMEvents.Max(bgmEvent => bgmEvent.Bar));

    MeasureLines.Clear();
    DurationMs = 0.0;

    if (highestOccupiedBar < 0)
        return;

    var terminalBar = highestOccupiedBar + 1;
    TimingMap.Rebuild(Bpm, terminalBar);

    foreach (var note in Notes)
        note.TimeMs = TimingMap.CalculateTimeMs(note.Bar, note.Tick);

    foreach (var bgmEvent in BGMEvents)
        bgmEvent.TimeMs = TimingMap.CalculateTimeMs(bgmEvent.Bar, bgmEvent.Tick);

    for (var bar = 0; bar <= terminalBar; bar++)
    {
        MeasureLines.Add(new MeasureLine
        {
            Bar = bar,
            TimeMs = TimingMap.CalculateTimeMs(bar, 0)
        });
    }

    Notes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
    BGMEvents.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));

    var contentEndMs = 0.0;
    if (Notes.Count > 0)
        contentEndMs = Math.Max(contentEndMs, Notes[^1].TimeMs);
    if (BGMEvents.Count > 0)
        contentEndMs = Math.Max(contentEndMs, BGMEvents[^1].TimeMs);

    DurationMs = contentEndMs + DurationEndBufferMs;

    // Keep the existing DEBUG parse summary here, using the recalculated values.
}
```

Do not derive duration from `MeasureLines`.

- [ ] **Step 4: Remove the old fixed-clock escape hatches**

Delete `ChartTimeCalculator.cs`.

Remove `Note.CalculateTimeMs(double bpm)` and the fixed formula from its XML comments. Remove `BGMEvent.CalculateTimeMs(double bpm)` and its fixed formula comments. Do not replace them with another per-event calculator method; `ParsedChart.FinalizeChart()` already owns that responsibility.

In `ChartManager`, delete this constructor fallback entirely:

```csharp
foreach (var note in _notes)
{
    if (note.TimeMs == 0)
        note.CalculateTimeMs(_bpm);
}
```

Keep copying and sorting finalized notes/measure lines, assigning note IDs, and exposing base BPM as before.

Delete `ChartTimeCalculatorTests.cs`. In `NoteTests` and `BGMEventTests`, delete only tests dedicated to `CalculateTimeMs(double bpm)`; keep constructor, formatting, property, and invalid-input tests unrelated to fixed timing. In `DTXChartParserTests`, remove the standalone `Note_CalculateTimeMs_CalculatesCorrectly` test because timing-map and parser integration coverage supersede it.

- [ ] **Step 5: Run focused migration tests and observe GREEN**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ChartTimingMapTests|FullyQualifiedName~ParsedChartTests|FullyQualifiedName~NoteTests|FullyQualifiedName~BGMEventTests|FullyQualifiedName~ChartManagerTests'
```

Expected: PASS. Existing constant-4/4 chart expectations remain numerically unchanged except tests intentionally updated for deferred add-time behavior and idempotent duration.

- [ ] **Step 6: Review Task 2 and commit**

Run:

```bash
rtk git diff --check
rtk git status --short
rtk rg -n 'ChartTimeCalculator|CalculateTimeMs\(double bpm\)|TimeMs == 0' DTXMania.Game DTXMania.Test
rtk git diff -- DTXMania.Game/Lib/Song/Components DTXMania.Test/Song
```

Expected: no production reference to `ChartTimeCalculator`, no per-note/base-BPM timing method, and no constructor-side zero-time timing fallback.

Commit:

```bash
rtk git add -A -- \
  DTXMania.Game/Lib/Song/Components \
  DTXMania.Test/Song
rtk git commit -m "refactor: finalize chart timing from timing map"
```

---

### Task 3: Parse DTX Channels 02, 03, and 08 and Validate End-to-End Timing

**Files:**

- Modify: `DTXMania.Game/Lib/Song/DTXChartParser.cs`
- Modify: `DTXMania.Test/Song/DTXChartParserTests.cs`

**Interfaces:**

- Consumes: `ParsedChart.TimingMap.SetMeasureLength(...)` and `SetTempoChange(...)` from Tasks 1-2.
- Produces: format-correct timing directives retained before `ParseAsync()` calls `ParsedChart.FinalizeChart()`.
- No runtime interface changes after parsing.

- [ ] **Step 1: Add a deterministic temporary-DTX test helper**

Add this private helper to `DTXChartParserTests`:

```csharp
private static async Task<ParsedChart> ParseTemporaryDtxAsync(string content)
{
    var tempDir = Path.Combine(
        Path.GetTempPath(),
        $"dtx-timing-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    var dtxPath = Path.Combine(tempDir, "timing.dtx");

    try
    {
        File.WriteAllText(dtxPath, content);
        return await DTXChartParser.ParseAsync(dtxPath);
    }
    finally
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }
}
```

The timing tests do not need real WAV files because they assert parsed timeline positions rather than successful audio-file loading.

- [ ] **Step 2: Add failing inline-file parser tests**

Add:

```csharp
[Fact]
public async Task ParseAsync_Channel02_ShouldShortenMeasureForAllTimelineEvents()
{
    var dtx =
        "#BPM: 120\n" +
        "#WAV01: bgm.wav\n" +
        "#00001: 01\n" +
        "#00011: 01\n" +
        "#00002: 0.5\n" +
        "#00111: 01\n";

    var chart = await ParseTemporaryDtxAsync(dtx);

    Assert.Equal(1000.0, chart.Notes.Single(note => note.Bar == 1).TimeMs, 3);
    Assert.Equal(1000.0, chart.MeasureLines.Single(line => line.Bar == 1).TimeMs, 3);
}

[Fact]
public async Task ParseAsync_Channel03_ShouldApplyHexBpmAtPairPosition()
{
    var dtx =
        "#BPM: 120\n" +
        "#00003: 00F0\n" +
        "#00111: 01\n";

    var chart = await ParseTemporaryDtxAsync(dtx);

    // First half measure: 1000 ms at 120 BPM.
    // Second half: 500 ms at 240 BPM.
    Assert.Equal(1500.0, chart.Notes.Single().TimeMs, 3);
}

[Fact]
public async Task ParseAsync_Channel08_ShouldResolveFractionalBpmDefinition()
{
    var dtx =
        "#BPM: 120\n" +
        "#BPM01: 180.5\n" +
        "#00008: 0001\n" +
        "#00111: 01\n";

    var chart = await ParseTemporaryDtxAsync(dtx);

    var expected = 1000.0 + (2.0 * 60000.0 / 180.5);
    Assert.Equal(expected, chart.Notes.Single().TimeMs, 3);
}

[Fact]
public async Task ParseAsync_Channel08MissingDefinition_ShouldKeepCurrentTempo()
{
    var dtx =
        "#BPM: 120\n" +
        "#00008: 0099\n" +
        "#00111: 01\n";

    var chart = await ParseTemporaryDtxAsync(dtx);

    Assert.Equal(2000.0, chart.Notes.Single().TimeMs, 3);
}

[Fact]
public async Task ParseAsync_TimingChangeInEmptyMeasure_ShouldAffectLaterNote()
{
    var dtx =
        "#BPM: 120\n" +
        "#00102: 0.5\n" +
        "#00211: 01\n";

    var chart = await ParseTemporaryDtxAsync(dtx);

    Assert.Equal(3000.0, chart.Notes.Single().TimeMs, 3);
    Assert.Equal(3000.0, chart.MeasureLines.Single(line => line.Bar == 2).TimeMs, 3);
}

[Fact]
public async Task ParseAsync_SharedPosition_ShouldAlignNoteBgmAndMeasureLine()
{
    var dtx =
        "#BPM: 120\n" +
        "#BPM01: 240\n" +
        "#00008: 0001\n" +
        "#WAV01: bgm.wav\n" +
        "#00101: 01\n" +
        "#00111: 01\n";

    var chart = await ParseTemporaryDtxAsync(dtx);
    var note = chart.Notes.Single(note => note.Bar == 1 && note.Tick == 0);
    var bgm = chart.BGMEvents.Single(bgm => bgm.Bar == 1 && bgm.Tick == 0);
    var line = chart.MeasureLines.Single(line => line.Bar == 1);

    Assert.Equal(line.TimeMs, note.TimeMs, 3);
    Assert.Equal(line.TimeMs, bgm.TimeMs, 3);
}
```

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~DTXChartParserTests'
```

Expected: RED because timing channels and `#BPMxx` are currently discarded.

- [ ] **Step 3: Add per-encoding BPM-definition state**

In `ParseAsync(...)`, add:

```csharp
Dictionary<string, double> bpmDefinitions = null!;
```

Recreate it inside every encoding attempt next to the WAV dictionaries:

```csharp
bpmDefinitions = new Dictionary<string, double>();
```

Pass it through `ParseFileContentAsync(...)` and `ParseHeaderCommand(...)`. Do not make it static or reuse it between encoding attempts.

Extend header parsing after the exact `case "#BPM"` handling and before generic WAV handling:

```csharp
if (command.StartsWith("#BPM") && command.Length > 4)
{
    var bpmId = command.Substring(4).ToUpperInvariant();
    if (TryParseDouble(value, out var extendedBpm) && extendedBpm > 0)
        bpmDefinitions[bpmId] = extendedBpm;
}
```

Ensure the exact `#BPM` base header continues to set `chart.Bpm` and is not mistaken for a table entry.

- [ ] **Step 4: Parse channel 02 before pair-based note/BGM branches**

Change `ParseMeasureData(...)` to accept `bpmDefinitions`. Immediately after `measure`, `channel`, and `noteData` are available, add:

```csharp
if (channel == 0x02)
{
    if (TryParseDouble(noteData, out var multiplier) && multiplier > 0)
        chart.TimingMap.SetMeasureLength(measure, multiplier);
    return;
}
```

Use the parser's existing invariant-culture `TryParseDouble` helper. Do not parse channel `02` as pairs.

- [ ] **Step 5: Parse channel 03 direct BPM changes**

Add:

```csharp
private static void ParseDirectBpmChanges(
    string noteData,
    int measure,
    ParsedChart chart)
{
    var pairCount = noteData.Length / 2;
    if (pairCount == 0)
        return;

    for (var i = 0; i < pairCount; i++)
    {
        var pair = noteData.Substring(i * 2, 2);
        if (pair == "00")
            continue;

        if (!int.TryParse(
                pair,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var bpm) || bpm <= 0)
        {
            continue;
        }

        var tick = (int)((double)i / pairCount * ChartTimingMap.TicksPerMeasure);
        chart.TimingMap.SetTempoChange(measure, tick, bpm);
    }
}
```

Call it and return when `channel == 0x03`:

```csharp
if (channel == 0x03)
{
    ParseDirectBpmChanges(noteData, measure, chart);
    return;
}
```

- [ ] **Step 6: Parse channel 08 BPM-table changes**

Add:

```csharp
private static void ParseReferencedBpmChanges(
    string noteData,
    int measure,
    ParsedChart chart,
    IReadOnlyDictionary<string, double> bpmDefinitions)
{
    var pairCount = noteData.Length / 2;
    if (pairCount == 0)
        return;

    for (var i = 0; i < pairCount; i++)
    {
        var pair = noteData.Substring(i * 2, 2).ToUpperInvariant();
        if (pair == "00")
            continue;

        if (!bpmDefinitions.TryGetValue(pair, out var bpm) || bpm <= 0)
            continue;

        var tick = (int)((double)i / pairCount * ChartTimingMap.TicksPerMeasure);
        chart.TimingMap.SetTempoChange(measure, tick, bpm);
    }
}
```

Call it and return when `channel == 0x08`:

```csharp
if (channel == 0x08)
{
    ParseReferencedBpmChanges(noteData, measure, chart, bpmDefinitions);
    return;
}
```

Keep channel `01` and drum-lane parsing unchanged after these timing-channel branches.

- [ ] **Step 7: Run parser and timing tests and observe GREEN**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ChartTimingMapTests|FullyQualifiedName~DTXChartParserTests|FullyQualifiedName~ParsedChartTests|FullyQualifiedName~ChartManagerTests'
```

Expected: PASS, including short/extended measures, direct BPM, referenced fractional BPM, empty-measure timing changes, and cross-collection alignment.

- [ ] **Step 8: Run the broader timing-sensitive regression set**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~TimingVerificationTest|FullyQualifiedName~AutomatedPlaySimulationTests|FullyQualifiedName~Song'
```

If the filter selects no general Song tests in the local xUnit runner, rerun the named Song test classes changed by this plan explicitly; do not broaden implementation scope to test-infrastructure changes.

Expected: PASS with no constant-chart timing regression.

- [ ] **Step 9: Run full Mac-safe validation**

Run:

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
rtk git diff --check
rtk git status --short
```

Expected: build succeeds, complete Mac-safe tests pass, and no whitespace errors are reported.

- [ ] **Step 10: Review the complete implementation against HPA-600 acceptance criteria**

Run:

```bash
rtk rg -n '0x02|0x03|0x08|#BPM' DTXMania.Game/Lib/Song/DTXChartParser.cs
rtk rg -n 'ChartTimeCalculator|CalculateTimeMs\(double bpm\)|TimeMs == 0' DTXMania.Game DTXMania.Test
rtk git diff --stat main...HEAD
rtk git diff main...HEAD -- DTXMania.Game/Lib/Song DTXMania.Test/Song
```

Expected:

- timing channels are parser-owned and feed `ParsedChart.TimingMap`;
- no fixed base-BPM production timing path remains;
- renderer/stage/layout files are untouched;
- notes, BGM events, measure lines, and duration all resolve in `FinalizeChart()`;
- no TODO/TBD placeholders or unrelated refactors are present.

- [ ] **Step 11: Commit parser support**

```bash
rtk git add DTXMania.Game/Lib/Song/DTXChartParser.cs DTXMania.Test/Song/DTXChartParserTests.cs
rtk git commit -m "feat: parse DTX timing channels"
```

## Final Validation Gate

Before marking HPA-600 implementation ready for review, record the exact results of:

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-restore
rtk git diff --check
```

Also manually inspect at least one real DTX chart that contains timing channel `02`, `03`, or `08` if such a chart is already available in the developer's local song library. This is verification only; do not add copyrighted community charts to the repository.

## Expected Implementation Size

This plan is intentionally one migration, not a new timing subsystem project:

- one new production timing class;
- one replacement focused test class;
- one parser extension;
- one finalization cleanup;
- no gameplay/rendering architecture change.

A single engineer or agentic worker should be able to complete the implementation within roughly 1-2 focused engineering days, with Task 1 and Task 2 independently reviewable before parser-format behavior is enabled.
