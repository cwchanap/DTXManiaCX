# HPA-518 Gameplay Measure Lines Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render a visible two-pixel measure boundary across the gameplay lane panel, synchronized with notes and present through empty measures.

**Architecture:** Extract the current fixed 4/4 timing formula into one internal calculator, generate explicit measure-boundary events when `ParsedChart` is finalized, and copy/query those events independently in `ChartManager`. `PerformanceStage` supplies the current song time and visible window to `NoteRenderer`, which reuses its time-to-Y mapping and white texture to draw a layout-defined neutral-gray line.

**Tech Stack:** .NET 8, C#, MonoGame 3.8, xUnit, Moq, existing reflection-based renderer/stage test helpers.

## Global Constraints

- Use the current base-BPM, 4/4 calculation with exactly `192` ticks per measure; do not add channel `02`, `03`, or `08` timing support.
- Generate boundaries only from retained `ParsedChart.Notes` and `ParsedChart.BGMEvents`, for bars `0` through `highestOccupiedBar + 1` inclusive.
- Measure lines must not change `DurationMs`, song completion, judgement, autoplay, audio, scoring, or persistence.
- Keep measure-line queries independent from the note-only `_lastActiveIndex` cursor.
- Draw with `NoteRenderer._whiteTexture`, `new Color(169, 169, 169)`, height `2`, full `HitBar.Bounds` width, and depth `0.78f`; do not sample or modify skin assets.
- Convert the renderer's existing `20`-pixel drop grace to milliseconds from the active scroll speed; return zero grace when pixels-per-millisecond is zero or negative.
- Do not modify `DTXChartParser`, the generated E2E fixture, either project file, or any legacy file under `DTXManiaNX/`.
- Prefix every repository shell command with `rtk`.
- Execute implementation from an isolated worktree on branch `codex/hpa-518-measure-lines`, created with `superpowers:using-git-worktrees` after this plan is approved.
- Use xUnit names in `Scenario_ShouldExpect` form and preserve the existing Mac-safe test exclusions.
- Follow RED, focused GREEN, task review, then commit for every implementation task.
- Source of truth: [`2026-08-08-hpa-518-measure-lines-design.md`](../specs/2026-08-08-hpa-518-measure-lines-design.md).

## File Responsibility Map

- Create `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`: own the current fixed-clock bar/tick-to-millisecond formula.
- Create `DTXMania.Game/Lib/Song/Components/MeasureLine.cs`: immutable boundary value with `Bar` and `TimeMs`.
- Modify `DTXMania.Game/Lib/Song/Components/Note.cs`: delegate existing note timing to `ChartTimeCalculator`.
- Modify `DTXMania.Game/Lib/Song/Components/BGMEvent.cs`: delegate existing BGM timing to `ChartTimeCalculator`.
- Modify `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`: own and repeat-safely regenerate the explicit boundary list during finalization.
- Modify `DTXMania.Game/Lib/Song/Components/ChartManager.cs`: copy the finalized boundary list and expose a cursor-independent active-window query.
- Modify `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`: own measure-line destination geometry, color, and depth.
- Modify `DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs`: expose the speed-derived grace and draw active lines with the existing mapping and white texture.
- Modify `DTXMania.Game/Lib/Stage/PerformanceStage.cs`: query and draw measure lines during the base pass and correct the stale depth comment.
- Create `DTXMania.Test/Song/ChartTimeCalculatorTests.cs`: direct calculator contract tests.
- Modify `DTXMania.Test/Song/ParsedChartTests.cs`: boundary generation, empty/BGM-only, repeat-safety, and duration tests.
- Modify `DTXMania.Test/Song/ChartManagerTests.cs`: copy, active-window, grace, and query-isolation tests.
- Modify `DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs`: geometry, color, and real-depth ordering tests.
- Modify `DTXMania.Test/Stage/Performance/NoteRendererLogicTests.cs`: grace conversion and renderer guard tests.
- Modify `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`: playing/paused integration and missing-collaborator guards.

## Baseline Evidence

On 2026-08-08, the proposed combined filter for the seven existing affected
test classes passed `397` tests on `DTXMania.Test.Mac.csproj`. Package restore
required network access; the run reported only the repository's existing
warning set.

---

### Task 1: Extract the Current Chart-Time Formula

**Files:**

- Create: `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`
- Create: `DTXMania.Test/Song/ChartTimeCalculatorTests.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/Note.cs:84-98`
- Modify: `DTXMania.Game/Lib/Song/Components/BGMEvent.cs:69-82`

**Interfaces:**

- Consumes: integer `bar`, integer `tick`, and positive `double bpm` values already used by `Note` and `BGMEvent`.
- Produces: `internal static double ChartTimeCalculator.CalculateTimeMs(int bar, int tick, double bpm)` for Tasks 2–5.

- [ ] **Step 1: Add direct failing tests for the shared calculator**

Create `DTXMania.Test/Song/ChartTimeCalculatorTests.cs` with:

```csharp
using System;
using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Song")]
    public class ChartTimeCalculatorTests
    {
        [Theory]
        [InlineData(0, 0, 120.0, 0.0)]
        [InlineData(0, 96, 120.0, 1000.0)]
        [InlineData(1, 0, 120.0, 2000.0)]
        [InlineData(5, 48, 120.0, 10500.0)]
        [InlineData(1, 0, 60.0, 4000.0)]
        [InlineData(1, 0, 240.0, 1000.0)]
        public void CalculateTimeMs_ValidPosition_ShouldMatchCurrentClock(
            int bar,
            int tick,
            double bpm,
            double expectedMs)
        {
            var actualMs = ChartTimeCalculator.CalculateTimeMs(bar, tick, bpm);

            Assert.Equal(expectedMs, actualMs, precision: 3);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-120.0)]
        public void CalculateTimeMs_NonPositiveBpm_ShouldThrowArgumentException(
            double bpm)
        {
            Assert.Throws<ArgumentException>(
                () => ChartTimeCalculator.CalculateTimeMs(1, 0, bpm));
        }
    }
}
```

- [ ] **Step 2: Run the calculator test and observe RED**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ChartTimeCalculatorTests'
```

Expected: FAIL during compilation because `ChartTimeCalculator` does not exist.

- [ ] **Step 3: Implement the calculator and delegate both existing models**

Create `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`:

```csharp
using System;

namespace DTXMania.Game.Lib.Song.Components
{
    internal static class ChartTimeCalculator
    {
        private const int TicksPerMeasure = 192;

        internal static double CalculateTimeMs(int bar, int tick, double bpm)
        {
            if (bpm <= 0)
                throw new ArgumentException(
                    "BPM must be greater than 0",
                    nameof(bpm));

            var totalTicks = (bar * TicksPerMeasure) + tick;
            var measures = totalTicks / (double)TicksPerMeasure;
            return measures * (60000.0 / bpm) * 4.0;
        }
    }
}
```

Replace `Note.CalculateTimeMs(...)` with:

```csharp
public void CalculateTimeMs(double bpm)
{
    TimeMs = ChartTimeCalculator.CalculateTimeMs(Bar, Tick, bpm);
}
```

Replace `BGMEvent.CalculateTimeMs(...)` with:

```csharp
public void CalculateTimeMs(double bpm)
{
    TimeMs = ChartTimeCalculator.CalculateTimeMs(Bar, Tick, bpm);
}
```

- [ ] **Step 4: Run calculator and parity tests and observe GREEN**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ChartTimeCalculatorTests|FullyQualifiedName~NoteTests|FullyQualifiedName~BGMEventTests'
```

Expected: PASS; existing note/BGM results and invalid-BPM behavior remain unchanged.

- [ ] **Step 5: Review and commit the timing extraction**

Run:

```bash
rtk git diff --check
rtk git status --short
rtk sed -n '1,160p' DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs
rtk sed -n '1,160p' DTXMania.Test/Song/ChartTimeCalculatorTests.cs
rtk git diff -- DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs DTXMania.Game/Lib/Song/Components/Note.cs DTXMania.Game/Lib/Song/Components/BGMEvent.cs DTXMania.Test/Song/ChartTimeCalculatorTests.cs
rtk git add DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs DTXMania.Game/Lib/Song/Components/Note.cs DTXMania.Game/Lib/Song/Components/BGMEvent.cs DTXMania.Test/Song/ChartTimeCalculatorTests.cs
rtk git commit -m "refactor: share chart timing calculation"
```

### Task 2: Generate Explicit Measure Boundaries

**Files:**

- Create: `DTXMania.Game/Lib/Song/Components/MeasureLine.cs`
- Modify: `DTXMania.Game/Lib/Song/Components/ParsedChart.cs:22-34,228-253`
- Modify: `DTXMania.Test/Song/ParsedChartTests.cs:286-337`

**Interfaces:**

- Consumes: `ChartTimeCalculator.CalculateTimeMs(int, int, double)` from Task 1 and retained `Note.Bar`/`BGMEvent.Bar` values.
- Produces: `public List<MeasureLine> ParsedChart.MeasureLines { get; }`, populated during `FinalizeChart()` with ordered bars `0..highestOccupiedBar + 1`.

- [ ] **Step 1: Add failing ParsedChart boundary tests**

Add these methods to the existing `FinalizeChart Tests` region in `ParsedChartTests`:

```csharp
[Fact]
public void FinalizeChart_SparseNotes_ShouldGenerateEveryBoundaryThroughTerminal()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddNote(new Note(0, 0, 0, 0x11, "01"));
    chart.AddNote(new Note(0, 2, 0, 0x11, "01"));

    chart.FinalizeChart();

    Assert.Equal(new[] { 0, 1, 2, 3 },
        chart.MeasureLines.Select(line => line.Bar));
    Assert.Equal(new[] { 0.0, 2000.0, 4000.0, 6000.0 },
        chart.MeasureLines.Select(line => line.TimeMs));
}

[Fact]
public void FinalizeChart_BgmOnly_ShouldGenerateMeasureBoundaries()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddBGMEvent(new BGMEvent(1, 0, "01"));

    chart.FinalizeChart();

    Assert.Equal(new[] { 0, 1, 2 },
        chart.MeasureLines.Select(line => line.Bar));
}

[Fact]
public void FinalizeChart_EmptyChart_ShouldGenerateNoMeasureBoundaries()
{
    var chart = new ParsedChart();

    chart.FinalizeChart();

    Assert.Empty(chart.MeasureLines);
}

[Fact]
public void FinalizeChart_WhenCalledTwice_ShouldNotDuplicateMeasureBoundaries()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddNote(new Note(0, 1, 0, 0x11, "01"));
    chart.FinalizeChart();
    var firstBoundaries = chart.MeasureLines
        .Select(line => (line.Bar, line.TimeMs))
        .ToArray();

    chart.FinalizeChart();

    Assert.Equal(firstBoundaries,
        chart.MeasureLines.Select(line => (line.Bar, line.TimeMs)).ToArray());
}

[Fact]
public void FinalizeChart_TerminalBoundary_ShouldNotExtendDuration()
{
    var chart = new ParsedChart { Bpm = 120.0 };
    chart.AddNote(new Note(0, 2, 0, 0x11, "01"));

    chart.FinalizeChart();

    Assert.Equal(4500.0, chart.DurationMs, precision: 3);
    Assert.Equal(6000.0, chart.MeasureLines[^1].TimeMs, precision: 3);
}
```

The repeat-safety assertion deliberately covers only `MeasureLines`; do not change the existing behavior where every `FinalizeChart()` call adds the duration buffer.

- [ ] **Step 2: Run ParsedChart tests and observe RED**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ParsedChartTests'
```

Expected: FAIL during compilation because `MeasureLines` and `MeasureLine` do not exist.

- [ ] **Step 3: Add the model and repeat-safe generation**

Create `DTXMania.Game/Lib/Song/Components/MeasureLine.cs`:

```csharp
namespace DTXMania.Game.Lib.Song.Components
{
    public sealed class MeasureLine
    {
        public int Bar { get; init; }
        public double TimeMs { get; init; }
    }
}
```

Add this property beside `Notes` and `BGMEvents` in `ParsedChart`:

```csharp
/// <summary>
/// Ordered measure boundaries generated from retained chart events.
/// </summary>
public List<MeasureLine> MeasureLines { get; } = new List<MeasureLine>();
```

In `FinalizeChart()`, after sorting `Notes` and `BGMEvents` and before the debug summary, add:

```csharp
MeasureLines.Clear();

var highestOccupiedBar = -1;
if (Notes.Count > 0)
    highestOccupiedBar = Math.Max(highestOccupiedBar, Notes.Max(note => note.Bar));
if (BGMEvents.Count > 0)
    highestOccupiedBar = Math.Max(
        highestOccupiedBar,
        BGMEvents.Max(bgmEvent => bgmEvent.Bar));

for (var bar = 0; bar <= highestOccupiedBar + 1; bar++)
{
    MeasureLines.Add(new MeasureLine
    {
        Bar = bar,
        TimeMs = ChartTimeCalculator.CalculateTimeMs(bar, 0, Bpm)
    });
}
```

Do not assign `DurationMs` from the boundary list.

- [ ] **Step 4: Run ParsedChart tests and observe GREEN**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ParsedChartTests'
```

Expected: PASS, including the existing sorting and duration-buffer tests.

- [ ] **Step 5: Review and commit measure generation**

Run:

```bash
rtk git diff --check
rtk git status --short
rtk sed -n '1,120p' DTXMania.Game/Lib/Song/Components/MeasureLine.cs
rtk git diff -- DTXMania.Game/Lib/Song/Components/MeasureLine.cs DTXMania.Game/Lib/Song/Components/ParsedChart.cs DTXMania.Test/Song/ParsedChartTests.cs
rtk git add DTXMania.Game/Lib/Song/Components/MeasureLine.cs DTXMania.Game/Lib/Song/Components/ParsedChart.cs DTXMania.Test/Song/ParsedChartTests.cs
rtk git commit -m "feat: generate chart measure boundaries"
```

### Task 3: Add Cursor-Independent Runtime Queries

**Files:**

- Modify: `DTXMania.Game/Lib/Song/Components/ChartManager.cs:12-72,76-109`
- Modify: `DTXMania.Test/Song/ChartManagerTests.cs:12-174`

**Interfaces:**

- Consumes: finalized `ParsedChart.MeasureLines` from Task 2.
- Produces: `IReadOnlyList<MeasureLine> AllMeasureLines` and `IEnumerable<MeasureLine> GetActiveMeasureLines(double songTimeMs, double lookAheadMs, double gracePeriodMs)`.

- [ ] **Step 1: Add failing copy, window, clamp, and isolation tests**

Add these tests to `ChartManagerTests`:

```csharp
[Fact]
public void Constructor_FinalizedChart_ShouldCopyMeasureLines()
{
    var parsedChart = CreateTestChart();
    var manager = new ChartManager(parsedChart);
    var expectedBars = parsedChart.MeasureLines.Select(line => line.Bar).ToArray();

    parsedChart.MeasureLines.Clear();

    Assert.Equal(expectedBars, manager.AllMeasureLines.Select(line => line.Bar));
}

[Fact]
public void GetActiveMeasureLines_ShouldIncludeLookAheadAndPastGrace()
{
    var manager = new ChartManager(CreateTestChart());

    var active = manager
        .GetActiveMeasureLines(2100.0, 2000.0, 100.0)
        .Select(line => line.Bar)
        .ToArray();

    Assert.Equal(new[] { 1, 2 }, active);
}

[Fact]
public void GetActiveMeasureLines_NegativeGrace_ShouldClampToZero()
{
    var manager = new ChartManager(CreateTestChart());

    var active = manager
        .GetActiveMeasureLines(2000.0, 0.0, -500.0)
        .Select(line => line.Bar)
        .ToArray();

    Assert.Equal(new[] { 1 }, active);
}

[Fact]
public void GetActiveMeasureLines_ShouldExcludeLinesOutsideWindow()
{
    var manager = new ChartManager(CreateTestChart());

    var active = manager
        .GetActiveMeasureLines(2100.0, 1000.0, 50.0)
        .ToArray();

    Assert.Empty(active);
}

[Fact]
public void GetActiveMeasureLines_LaterQuery_ShouldNotAffectEarlierNoteQuery()
{
    var parsedChart = CreateTestChart();
    var manager = new ChartManager(parsedChart);
    var freshManager = new ChartManager(parsedChart);

    manager.GetActiveMeasureLines(4000.0, 2000.0, 0.0).ToArray();
    var actual = manager.GetActiveNotes(500.0, 1000.0)
        .Select(note => note.TimeMs)
        .ToArray();
    var expected = freshManager.GetActiveNotes(500.0, 1000.0)
        .Select(note => note.TimeMs)
        .ToArray();

    Assert.Equal(expected, actual);
}
```

- [ ] **Step 2: Run ChartManager tests and observe RED**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ChartManagerTests'
```

Expected: FAIL during compilation because `AllMeasureLines` and `GetActiveMeasureLines` do not exist.

- [ ] **Step 3: Copy and expose the measure-line collection**

Add a field beside `_notes`:

```csharp
private readonly List<MeasureLine> _measureLines;
```

Add a read-only property beside `AllNotes`:

```csharp
public IReadOnlyList<MeasureLine> AllMeasureLines => _measureLines.AsReadOnly();
```

In the constructor, immediately after copying notes, copy the finalized lines:

```csharp
_measureLines = new List<MeasureLine>(parsedChart.MeasureLines);
```

Add this independent ordered scan after `GetActiveNotes(...)`:

```csharp
public IEnumerable<MeasureLine> GetActiveMeasureLines(
    double songTimeMs,
    double lookAheadMs,
    double gracePeriodMs)
{
    var clampedGraceMs = Math.Max(0.0, gracePeriodMs);
    var startTime = songTimeMs - clampedGraceMs;
    var endTime = songTimeMs + lookAheadMs;

    foreach (var line in _measureLines)
    {
        if (line.TimeMs < startTime)
            continue;
        if (line.TimeMs > endTime)
            yield break;

        yield return line;
    }
}
```

Do not call `FindStartIndex(...)`, `BinarySearchStartIndex(...)`, or mutate `_lastActiveIndex` from this method.

- [ ] **Step 4: Run ChartManager tests and observe GREEN**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ChartManagerTests'
```

Expected: PASS; existing note-query tests remain green and the copied line list survives mutation of the parsed source list.

- [ ] **Step 5: Review and commit the runtime query**

Run:

```bash
rtk git diff --check
rtk git diff -- DTXMania.Game/Lib/Song/Components/ChartManager.cs DTXMania.Test/Song/ChartManagerTests.cs
rtk git add DTXMania.Game/Lib/Song/Components/ChartManager.cs DTXMania.Test/Song/ChartManagerTests.cs
rtk git commit -m "feat: query active measure lines"
```

### Task 4: Add Layout and Renderer Behavior

**Files:**

- Modify: `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs:758-781`
- Modify: `DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs:42-48,82-108,193-219,415-426`
- Modify: `DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs:333-363`
- Modify: `DTXMania.Test/Stage/Performance/NoteRendererLogicTests.cs:18-99,140-149`

**Interfaces:**

- Consumes: `MeasureLine.TimeMs` from Task 2 and the existing `NoteRenderer.GetNoteScreenY(...)`, `_whiteTexture`, `JudgementY`, and `_scrollPixelsPerMs`.
- Produces: `PerformanceUILayout.MeasureLine.GetDestinationRect(double)`, layout `Color`/`Depth`, `double NoteRenderer.MeasureLinePastGraceMs`, and `void NoteRenderer.DrawMeasureLines(SpriteBatch, IEnumerable<MeasureLine>, double)` for Task 5.

- [ ] **Step 1: Add failing layout and grace tests**

Add to `PerformanceUILayoutMoreTests`:

```csharp
[Fact]
public void MeasureLine_Layout_ShouldMatchLanePanelAndRealDepthOrder()
{
    var destination = PerformanceUILayout.MeasureLine.GetDestinationRect(601.25);

    Assert.Equal(PerformanceUILayout.HitBar.Bounds.X, destination.X);
    Assert.Equal(600, destination.Y);
    Assert.Equal(PerformanceUILayout.HitBar.Bounds.Width, destination.Width);
    Assert.Equal(2, destination.Height);
    Assert.Equal(new Color(169, 169, 169), PerformanceUILayout.MeasureLine.Color);
    Assert.True(0.8f > PerformanceUILayout.MeasureLine.Depth);
    Assert.True(PerformanceUILayout.MeasureLine.Depth > 0.70f);
}
```

Add to `NoteRendererLogicTests`:

```csharp
[Theory]
[InlineData(0.5, 40.0)]
[InlineData(0.25, 80.0)]
public void MeasureLinePastGraceMs_PositiveScroll_ShouldKeepTwentyPixels(
    double pixelsPerMs,
    double expectedMs)
{
    var renderer = CreateRenderer();
    ReflectionHelpers.SetPrivateField(renderer, "_scrollPixelsPerMs", pixelsPerMs);

    Assert.Equal(expectedMs, renderer.MeasureLinePastGraceMs, precision: 3);
}

[Theory]
[InlineData(0.0)]
[InlineData(-0.5)]
public void MeasureLinePastGraceMs_NonPositiveScroll_ShouldReturnZero(
    double pixelsPerMs)
{
    var renderer = CreateRenderer();
    ReflectionHelpers.SetPrivateField(renderer, "_scrollPixelsPerMs", pixelsPerMs);

    Assert.Equal(0.0, renderer.MeasureLinePastGraceMs);
}

[Fact]
public void DrawMeasureLines_WhenRendererIsNotReady_ShouldReturnWithoutThrowing()
{
    var renderer = CreateRenderer();

    var exception = Record.Exception(() => renderer.DrawMeasureLines(
        (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)),
        new[] { new MeasureLine { Bar = 0, TimeMs = 0.0 } },
        0.0));

    Assert.Null(exception);
}

[Fact]
public void DrawMeasureLines_WhenInputIsNull_ShouldReturnWithoutThrowing()
{
    var renderer = CreateReadyRenderer();
    var spriteBatch =
        (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));

    var exception = Record.Exception(() =>
    {
        renderer.DrawMeasureLines(null!, Array.Empty<MeasureLine>(), 0.0);
        renderer.DrawMeasureLines(spriteBatch, null!, 0.0);
    });

    Assert.Null(exception);
}

[Theory]
[InlineData(-10000.0)]
[InlineData(10000.0)]
public void DrawMeasureLines_WhenLineIsOffscreen_ShouldReturnWithoutThrowing(
    double currentSongTimeMs)
{
    var renderer = CreateReadyRenderer();
    var spriteBatch =
        (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));

    var exception = Record.Exception(() => renderer.DrawMeasureLines(
        spriteBatch,
        new[] { new MeasureLine { Bar = 0, TimeMs = 0.0 } },
        currentSongTimeMs));

    Assert.Null(exception);
}
```

- [ ] **Step 2: Run layout and renderer tests and observe RED**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~PerformanceUILayoutMoreTests|FullyQualifiedName~NoteRendererLogicTests'
```

Expected: FAIL during compilation because the measure-line layout, grace property, and draw method do not exist.

- [ ] **Step 3: Add the layout-owned geometry, color, and depth**

Add this nested class immediately after `PerformanceUILayout.HitBar`:

```csharp
public static class MeasureLine
{
    public const int Height = 2;
    public const float Depth = 0.78f;
    public static readonly Color Color = new Color(169, 169, 169);

    public static Rectangle GetDestinationRect(double centerY)
    {
        var top = (int)Math.Floor(centerY - (Height / 2.0));
        return new Rectangle(
            HitBar.Bounds.X,
            top,
            HitBar.Bounds.Width,
            Height);
    }
}
```

- [ ] **Step 4: Add the renderer grace property and thin draw loop**

Add this property beside `ScrollPixelsPerMs`:

```csharp
public double MeasureLinePastGraceMs =>
    _scrollPixelsPerMs > 0
        ? DropGracePeriod / _scrollPixelsPerMs
        : 0.0;
```

Add this method beside `DrawNotes(...)`:

```csharp
[ExcludeFromCodeCoverage]
public void DrawMeasureLines(
    SpriteBatch spriteBatch,
    IEnumerable<MeasureLine> measureLines,
    double currentSongTimeMs)
{
    if (!IsReady || spriteBatch == null || measureLines == null)
        return;

    foreach (var line in measureLines)
    {
        var lineY = GetNoteScreenY(line.TimeMs, currentSongTimeMs);
        var destination =
            PerformanceUILayout.MeasureLine.GetDestinationRect(lineY);
        if (destination.Bottom <= 0 ||
            lineY > JudgementY + DropGracePeriod)
        {
            continue;
        }

        spriteBatch.Draw(
            _whiteTexture,
            destination,
            null,
            PerformanceUILayout.MeasureLine.Color,
            0f,
            Vector2.Zero,
            SpriteEffects.None,
            PerformanceUILayout.MeasureLine.Depth);
    }
}
```

Do not read `_drumChipsTexture`, add a source rectangle, or add a texture-dimension branch.

- [ ] **Step 5: Run layout and renderer tests and observe GREEN**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~PerformanceUILayoutMoreTests|FullyQualifiedName~NoteRendererLogicTests'
```

Expected: PASS; the existing `GetNoteScreenY_AndShouldDropNote_ShouldUseConfiguredScrollSpeed` test remains the parity guard for line/note positioning.

- [ ] **Step 6: Review and commit renderer behavior**

Run:

```bash
rtk git diff --check
rtk git diff -- DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs DTXMania.Test/Stage/Performance/NoteRendererLogicTests.cs
rtk git add DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs DTXMania.Test/UI/PerformanceUILayoutMoreTests.cs DTXMania.Test/Stage/Performance/NoteRendererLogicTests.cs
rtk git commit -m "feat: render gameplay measure lines"
```

### Task 5: Wire Measure Lines into PerformanceStage

**Files:**

- Modify: `DTXMania.Game/Lib/Stage/PerformanceStage.cs:320-353,1014-1032`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs:2783-2880`

**Interfaces:**

- Consumes: `ChartManager.GetActiveMeasureLines(...)`, `NoteRenderer.EffectiveLookAheadMs`, `NoteRenderer.MeasureLinePastGraceMs`, and `NoteRenderer.DrawMeasureLines(...)` from Tasks 3–4.
- Produces: private `PerformanceStage.DrawMeasureLines()` called during the existing base render pass while the timer is playing or paused.

- [ ] **Step 1: Add failing stage guard and active-path tests**

Add to `PerformanceStageDeterministicTests` near the existing `DrawNotes` tests:

```csharp
[Fact]
public void DrawMeasureLines_WhenRequiredCollaboratorIsMissing_ShouldNotThrow()
{
    var stage = CreateStage();

    var exception = Record.Exception(
        () => ReflectionHelpers.InvokePrivateMethod(stage, "DrawMeasureLines"));

    Assert.Null(exception);
}

[Fact]
public void DrawMeasureLines_WhenTimerIsPlaying_ShouldQueryAndCullSafely()
{
    var stage = CreateStage();
    var renderer = CreateNoteRenderer();
    var parsedChart = new ParsedChart("draw-measure-lines.dtx") { Bpm = 120.0 };
    parsedChart.AddNote(new Note(0, 1, 0, 0x11, "01"));
    parsedChart.FinalizeChart();

    ReflectionHelpers.SetPrivateField(renderer, "_scrollPixelsPerMs", 5.0);
    ReflectionHelpers.SetPrivateField(
        renderer,
        "<EffectiveLookAheadMs>k__BackingField",
        3000.0);
    ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", renderer);
    ReflectionHelpers.SetPrivateField(
        stage,
        "_chartManager",
        new ChartManager(parsedChart));
    ReflectionHelpers.SetPrivateField(stage, "_songTimer", CreatePlayingSongTimer());
    ReflectionHelpers.SetPrivateField(
        stage,
        "_currentGameTime",
        new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.016)));
    ReflectionHelpers.SetPrivateField(
        stage,
        "_spriteBatch",
        CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));

    var exception = Record.Exception(
        () => ReflectionHelpers.InvokePrivateMethod(stage, "DrawMeasureLines"));

    Assert.Null(exception);
}

[Fact]
public void DrawMeasureLines_WhenTimerIsPaused_ShouldUseFrozenLogicalTime()
{
    var stage = CreateStage();
    var renderer = CreateNoteRenderer();
    var parsedChart = new ParsedChart("draw-paused-measure-lines.dtx")
    {
        Bpm = 120.0
    };
    parsedChart.AddNote(new Note(0, 1, 0, 0x11, "01"));
    parsedChart.FinalizeChart();
    var timer = new SongTimer(150);
    timer.Play(new GameTime(TimeSpan.Zero, TimeSpan.Zero));
    timer.Pause(new GameTime(TimeSpan.FromMilliseconds(1000), TimeSpan.Zero));

    ReflectionHelpers.SetPrivateField(renderer, "_scrollPixelsPerMs", 5.0);
    ReflectionHelpers.SetPrivateField(
        renderer,
        "<EffectiveLookAheadMs>k__BackingField",
        3000.0);
    ReflectionHelpers.SetPrivateField(stage, "_noteRenderer", renderer);
    ReflectionHelpers.SetPrivateField(
        stage,
        "_chartManager",
        new ChartManager(parsedChart));
    ReflectionHelpers.SetPrivateField(stage, "_songTimer", timer);
    ReflectionHelpers.SetPrivateField(
        stage,
        "_currentGameTime",
        new GameTime(TimeSpan.FromMilliseconds(5000), TimeSpan.FromSeconds(0.016)));
    ReflectionHelpers.SetPrivateField(
        stage,
        "_spriteBatch",
        CreateSpriteBatchStub(new Viewport(0, 0, 1280, 720)));

    var exception = Record.Exception(
        () => ReflectionHelpers.InvokePrivateMethod(stage, "DrawMeasureLines"));

    Assert.Null(exception);
    Assert.Equal(
        1500.0,
        timer.GetCurrentMs(new GameTime(
            TimeSpan.FromMilliseconds(5000),
            TimeSpan.Zero)));
}
```

- [ ] **Step 2: Run the stage tests and observe RED**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~PerformanceStageDeterministicTests'
```

Expected: FAIL because reflection cannot find the private `DrawMeasureLines` method.

- [ ] **Step 3: Add the stage query/draw method**

Add this method immediately before the existing private `DrawNotes()` method:

```csharp
private void DrawMeasureLines()
{
    if (_noteRenderer == null ||
        _chartManager == null ||
        _songTimer == null ||
        _currentGameTime == null)
    {
        return;
    }

    if (!_songTimer.IsPlaying && !_songTimer.IsPaused)
        return;

    var currentTimeMs = _songTimer.GetCurrentMs(_currentGameTime);
    var lookAheadMs = _noteRenderer.EffectiveLookAheadMs > 0
        ? _noteRenderer.EffectiveLookAheadMs
        : PerformanceUILayout.NoteDefaultLookAheadMs;
    var activeLines = _chartManager.GetActiveMeasureLines(
        currentTimeMs,
        lookAheadMs,
        _noteRenderer.MeasureLinePastGraceMs);

    _noteRenderer.DrawMeasureLines(
        _spriteBatch,
        activeLines,
        currentTimeMs);
}
```

- [ ] **Step 4: Insert the base-pass call and correct the stale depth comment**

Call `DrawMeasureLines()` immediately after `DrawLaneBackgrounds()` and before `DrawPads()`/`DrawNotes()`:

```csharp
// Draw lane backgrounds
DrawLaneBackgrounds();

// Draw scrolling measure boundaries above lanes and behind gameplay objects
DrawMeasureLines();

// Draw pad indicators
DrawPads();

// Draw scrolling notes
DrawNotes();
```

Replace the stale Z-order comment with the actual depth values:

```csharp
// BackToFront depth order:
// Background (1.0f) → Lanes (0.8f) → Measure lines (0.78f) →
// fallback notes (0.70f) → JudgementLine (0.6f) →
// Pads (0.1f) → sprite notes (0.05f).
```

Do not change `PadRenderer.BaseDepth`.

- [ ] **Step 5: Run stage and complete focused tests and observe GREEN**

Run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~PerformanceStageDeterministicTests'
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter 'FullyQualifiedName~ChartTimeCalculatorTests|FullyQualifiedName~NoteTests|FullyQualifiedName~BGMEventTests|FullyQualifiedName~ParsedChartTests|FullyQualifiedName~ChartManagerTests|FullyQualifiedName~PerformanceUILayoutMoreTests|FullyQualifiedName~NoteRendererLogicTests|FullyQualifiedName~PerformanceStageDeterministicTests'
```

Expected: PASS; no graphics-dependent test project changes are required.

- [ ] **Step 6: Review and commit stage integration**

Run:

```bash
rtk git diff --check
rtk git diff -- DTXMania.Game/Lib/Stage/PerformanceStage.cs DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
rtk git add DTXMania.Game/Lib/Stage/PerformanceStage.cs DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs
rtk git commit -m "feat: show measure lines during gameplay"
```

### Task 6: Complete Integration and Visual Verification

**Files:**

- Verify only: all files changed in Tasks 1–5.
- Do not modify: `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs`, skin PNG files, project files, or `DTXManiaNX/`.

**Interfaces:**

- Consumes: the complete HPA-518 implementation.
- Produces: build/test evidence plus screenshots demonstrating visible line behavior in both bundled skins.

- [ ] **Step 1: Verify the intended scope**

Run:

```bash
rtk git status --short
rtk git diff --stat main...HEAD
rtk git diff --check main...HEAD
rtk rg -n 'MeasureLine|ChartTimeCalculator' DTXMania.Game DTXMania.Test
```

Expected: only the files listed in the responsibility map changed; no asset, parser, E2E fixture, project, or legacy files appear.

- [ ] **Step 2: Build and run the complete Mac-safe suite**

Run:

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: build succeeds with zero errors and the full Mac-safe suite passes.

- [ ] **Step 3: Perform the real gameplay visual check**

Run:

```bash
rtk dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```

Using a chart spanning at least two occupied bars, capture evidence for both the default System skin and CX Neon that confirms:

- the neutral-gray line is visible and spans `HitBar.Bounds` from `x=295` for `558` pixels;
- empty measures retain their boundaries;
- lines render in front of lane backgrounds and behind notes, pads, and the judgement line;
- pausing freezes line motion;
- scroll speeds `50`, `100`, and `400` keep boundaries aligned with notes;
- crossing the judgement line retains approximately `20` pixels of below-line travel;
- gameplay reaches Result without duration or completion regressions.

- [ ] **Step 4: Use Windows CI as the authoritative graphics/E2E gate**

After the implementation branch is published, inspect the existing `Build and
Test` workflow without modifying it. Require green results for the Windows
build/full unit-test job and the gameplay-E2E job, which runs both
`Category=E2E` and `Category=E2E-Support` against the unchanged fixture.

Expected: Windows build, full unit suite, unchanged gameplay smoke, and support
tests pass. If the branch has not been published, record these as pending CI
evidence rather than claiming a local Windows pass.

- [ ] **Step 5: Final whole-branch review**

Review `main...HEAD` against every acceptance criterion in the design. Confirm especially that measure lines do not affect `DurationMs`, the query does not touch `_lastActiveIndex`, and rendering never reads the drum-chip texture. Resolve concrete findings with focused tests and a separate `fix:` commit, then repeat Steps 1–4 as applicable.

No verification-only commit is expected when the branch is already clean.
