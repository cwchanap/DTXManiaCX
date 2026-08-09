# HPA-518 NX-Style Gameplay Measure Lines Design

**Issue:** [HPA-518](https://linear.app/cwchanap/issue/HPA-518/measure-line-in-game-play-stage)  
**Date:** 2026-08-08  
**Status:** Draft for review

## Context

The Performance stage scrolls playable drum notes over the ten-lane panel but
does not show measure boundaries. HPA-518 asks for a measure line similar to
DTXManiaNX.

NX represents measure and beat lines as timeline chips. It synthesizes a bar
line at each measure boundary, scrolls that chip with the same distance model
as playable chips, and draws a two-pixel strip from the drum-chip texture
across the lane panel. NX also supports beat lines, measure-number text,
metronome sounds, reverse mode, and lane-visibility settings; those adjacent
features are not implied by the singular measure-line request.

DTXManiaCX already has the relevant visual seams:

- `NoteRenderer.GetNoteScreenY(...)` owns the current time-to-screen mapping.
- `NoteRenderer.SetScrollSpeed(...)` owns scroll speed and visible look-ahead.
- `PerformanceUILayout.HitBar.Bounds` defines the full lane-panel width at
  `x=295`, width `558`.
- `TexturePath.DrumChips` resolves the active skin's
  `Graphics/7_chips_drums.png`; the bundled skins retain NX's bar-line strip at
  source `y=769`, height `2`.
- `PerformanceStage.OnDraw(...)` already draws lane backgrounds before notes,
  leaving a stable layer for measure lines between them.

The missing seam is chart state. `ParsedChart` retains playable `Note` objects
and channel `01` `BGMEvent` objects, while `DTXChartParser` discards every other
unmapped timeline channel. A renderer cannot reliably infer boundaries from
the currently visible notes because an entire measure may contain no playable
drum notes.

## Goals

- Show one horizontal measure line at every measure boundary represented by
  the current parsed gameplay chart.
- Preserve empty measures between occupied measures.
- Keep line motion exactly aligned with note motion at every configured scroll
  speed, play speed, pause state, and frame time.
- Match the NX drum-lane geometry and use the active skin's NX-compatible line
  strip when it is available.
- Degrade safely to a solid two-pixel line when a custom skin lacks the
  expected source strip.
- Keep parsing, model, rendering, and stage wiring independently testable in
  the Mac-safe unit suite.

## Non-goals

- Beat/subdivision lines corresponding to NX channel `0x51`.
- Measure-number text or performance-information UI.
- Metronome sounds at measure boundaries.
- A setting to hide measure lines or other NX lane-display modes.
- Reverse-mode rendering; CX does not currently expose reverse gameplay.
- Parsing channel `0xC2` bar/beat-line visibility directives.
- Full DTX timing support for channel `02` measure lengths, channel `03`
  direct BPM changes, or channel `08` BPM-table changes.
- Changing judgement, autoplay, audio scheduling, score persistence, or song
  completion timing.
- Adding or modifying skin assets.

## Approaches Considered

### Explicit measure-line events in `ParsedChart` (selected)

Add a small `MeasureLine` timeline model, generate the ordered boundary list
when the chart is finalized, copy it into `ChartManager`, and render its active
window with `NoteRenderer`.

This preserves empty measures, makes timing testable without graphics, and
gives a future DTX timing-map implementation one explicit collection to
recalculate. It adds only the state required by HPA-518.

### Infer boundaries from playable notes during rendering

Rejected. Grouping visible notes by `Note.Bar` cannot recover a measure that
contains no playable drum note. It also makes the renderer responsible for
chart semantics and can produce duplicate or disappearing boundaries as the
visible note window changes.

### Build a complete DTX tempo and measure-length map first

Deferred. It is the correct long-term timing architecture, but it expands this
visual feature into a parser and playback-timing migration. HPA-518 will use
the same fixed 4/4, base-BPM clock that current CX notes and BGM events use, so
the new lines remain aligned with current gameplay. The design leaves one
calculator seam for the future timing work.

## Chosen Architecture

### Shared current-timing calculator

Add a focused `ChartTimeCalculator` under
`DTXMania.Game/Lib/Song/Components/`. It contains the existing
`192`-ticks-per-measure, base-BPM 4/4 calculation:

```csharp
internal static class ChartTimeCalculator
{
    internal const int TicksPerMeasure = 192;

    internal static double CalculateTimeMs(int bar, int tick, double bpm);
}
```

`Note.CalculateTimeMs(...)`, `BGMEvent.CalculateTimeMs(...)`, and measure-line
generation delegate to this helper. Moving the current formula must be
behavior-preserving; existing note and BGM timing tests remain parity guards.
The helper name deliberately avoids claiming support for a complete DTX tempo
map.

### Measure-line model and generation

Add `DTXMania.Game/Lib/Song/Components/MeasureLine.cs`:

```csharp
public sealed class MeasureLine
{
    public int Bar { get; init; }
    public double TimeMs { get; init; }
}
```

`ParsedChart` owns a get-only `List<MeasureLine> MeasureLines`. During
`FinalizeChart()` it determines the highest occupied measure across both
`Notes` and `BGMEvents`:

- If neither collection has an event, `MeasureLines` stays empty.
- Otherwise, generate bars `0` through `highestOccupiedBar + 1`, inclusive.
- Every line uses tick `0` and `ChartTimeCalculator.CalculateTimeMs(...)`.
- Sort by `TimeMs`, matching the other chart collections.
- Measure lines do not contribute to `DurationMs`; adding the terminal boundary
  must not lengthen gameplay or delay Result.

The inclusive terminal boundary matches NX's post-final boundary and lets it
enter the look-ahead window when the existing gameplay duration permits. A
later timing-map change can replace each `TimeMs` without changing rendering.

`ChartManager` copies the finalized collection and exposes:

```csharp
public IReadOnlyList<MeasureLine> AllMeasureLines { get; }

public IEnumerable<MeasureLine> GetActiveMeasureLines(
    double songTimeMs,
    double lookAheadMs,
    double gracePeriodMs);
```

The active range is
`[songTimeMs - gracePeriodMs, songTimeMs + lookAheadMs]`. The small past
window prevents a two-pixel line from disappearing one frame before it crosses
the judgement line. `NoteRenderer` exposes the existing 20-pixel drop grace as
milliseconds (`DropGracePeriod / ScrollPixelsPerMs`), and `PerformanceStage`
passes that speed-derived value into this query. This keeps the below-line
distance stable at every scroll speed instead of applying an arbitrary fixed
time window. Negative input grace is clamped to zero. The list is sorted and
queried without mutating note iteration state.

### Layout and rendering

Add `PerformanceUILayout.MeasureLine` constants/helpers:

- destination `X`: `HitBar.Bounds.X` (`295`)
- destination width: `HitBar.Bounds.Width` (`558`)
- destination height: `2`
- source `Y`: `769`
- source height: `2`
- layer depth: `0.75f`, between lane backgrounds (`0.8f`) and notes (`0.7f`)

`NoteRenderer` gains `DrawMeasureLines(...)`. It uses
`GetNoteScreenY(line.TimeMs, currentSongTimeMs)` and centers the two-pixel
destination on that Y position. The renderer draws the active skin's drum-chip
source strip when the underlying texture is large enough. If the texture is
missing, disposed, narrower than one pixel, or shorter than source row `770`,
it draws the same destination rectangle with the renderer's existing white
texture. If neither texture is available, drawing is a no-op.

The renderer applies the same top-of-screen culling and 20-pixel
below-judgement grace used by scrolling notes. It exposes the corresponding
time-domain grace through a read-only property for the stage query; callers do
not duplicate the conversion.

Measure-line drawing must not change the ready state for playable notes and
must share the existing texture reload/disposal lifecycle. Invalid or null
inputs remain safe no-ops, matching existing `NoteRenderer` draw methods.

### Performance-stage integration

`PerformanceStage.OnDraw(...)` calls `DrawMeasureLines()` after
`DrawLaneBackgrounds()` and before `DrawPads()` / `DrawNotes()`. The stage
method uses the same values as note rendering:

1. Read `currentTimeMs` from `SongTimer`.
2. Read look-ahead from `NoteRenderer.EffectiveLookAheadMs`.
3. Read the speed-derived past-grace window from `NoteRenderer`.
4. Ask `ChartManager` for active measure lines.
5. Pass those lines and `currentTimeMs` to `NoteRenderer.DrawMeasureLines(...)`.

The method draws while the timer is playing or paused, using the same guard as
`DrawNotes()`. Because the renderer maps absolute chart times on every frame,
pausing freezes lines, play-speed changes remain synchronized through the song
clock, and scroll-speed changes reposition notes and lines together.

## Data Flow

```text
DTXChartParser
  -> ParsedChart Notes + BGMEvents
  -> ParsedChart.FinalizeChart
       -> ChartTimeCalculator
       -> ordered MeasureLines
  -> ChartManager copied runtime collections
  -> PerformanceStage current time + visible window
  -> NoteRenderer shared time-to-Y mapping
  -> active-skin NX strip or solid fallback
```

## Error and Compatibility Behavior

- Empty charts produce no measure lines and no draw calls.
- Sparse charts still produce every intervening boundary.
- A BGM-only chart produces boundaries from its occupied measures.
- Unsupported DTX tempo and measure-length channels keep their current CX
  behavior; HPA-518 neither worsens nor silently claims to fix that limitation.
- Custom skins with an NX-compatible `7_chips_drums.png` retain authored line
  appearance. Short or missing custom textures receive the solid fallback.
- The default and CX Neon assets are reused unchanged.
- No configuration or persisted-data migration is required.

## Testing

### Timing and model tests

Extend `NoteTests`, `BGMEventTests`, and/or add focused calculator tests to
prove moving the formula preserves current results at multiple BPMs and tick
positions.

Extend `ParsedChartTests` with:

- events in measures `0` and `2` produce lines for bars `0`, `1`, `2`, and `3`;
- a BGM-only chart generates boundaries;
- an empty chart generates none;
- line times use the fixed current clock;
- terminal lines do not extend `DurationMs`.

### Runtime query tests

Extend `ChartManagerTests` to verify:

- `AllMeasureLines` is a copied read-only runtime view;
- the active query includes future lines inside look-ahead;
- the supplied grace window retains a just-passed line;
- lines outside both bounds are excluded;
- note-query state remains unaffected.

### Rendering and stage tests

Extend `PerformanceUILayoutMoreTests` and `NoteRendererLogicTests` to verify:

- destination geometry is `x=295`, width `558`, height `2`;
- source geometry is `y=769`, height `2`;
- line Y uses the same calculation as a note at the same `TimeMs`;
- the time-domain grace corresponds to 20 pixels at multiple scroll speeds;
- line depth stays between lane and note depths;
- offscreen and null inputs are safe;
- short/missing textures select the solid fallback.

Extend `PerformanceStageDeterministicTests` to verify the stage supplies the
current song time, renderer look-ahead, and active chart lines without drawing
when required collaborators are absent.

### End-to-end and visual verification

Use the existing generated gameplay E2E fixture with a sparse multi-measure
note pattern. The automated smoke remains behavioral rather than pixel-based:
it must still reach Result successfully. Capture a gameplay screenshot during
manual verification and confirm:

- the measure line spans the full drum panel;
- it is behind notes and above lane backgrounds;
- it crosses the judgement line in sync with the authored measure;
- pause freezes it;
- scroll speeds `50`, `100`, and `400` keep it aligned with notes;
- both the default System skin and CX Neon display a valid line.

Run the focused tests first, then the complete Mac-safe suite. Windows CI
remains the authoritative graphics and gameplay-E2E platform gate.

## Files Expected to Change

**Create:**

- `DTXMania.Game/Lib/Song/Components/ChartTimeCalculator.cs`
- `DTXMania.Game/Lib/Song/Components/MeasureLine.cs`

**Modify:**

- `DTXMania.Game/Lib/Song/Components/Note.cs`
- `DTXMania.Game/Lib/Song/Components/BGMEvent.cs`
- `DTXMania.Game/Lib/Song/Components/ParsedChart.cs`
- `DTXMania.Game/Lib/Song/Components/ChartManager.cs`
- `DTXMania.Game/Lib/Song/DTXChartParser.cs`
- `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`
- `DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs`
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs`
- existing focused test files under `DTXMania.Test/Song/`,
  `DTXMania.Test/UI/`, and `DTXMania.Test/Stage/Performance/`

No project-file change is expected because both test projects already include
these source trees, with the Mac project excluding only its known
graphics-dependent tests.

## Acceptance Criteria

- A horizontal two-pixel measure line is visible at each generated measure
  boundary in Performance.
- Empty measures between occupied measures retain their boundaries.
- The line uses the same time-to-Y mapping and visible look-ahead as notes.
- The line spans the current `HitBar.Bounds` width and renders below notes.
- Default and CX Neon skins use the NX bar-line source strip; incompatible
  custom skins receive a safe solid fallback.
- Empty/malformed chart state and missing texture resources do not crash.
- Measure lines do not affect chart duration, judgement, audio, scoring, or
  persistence.
- Focused model, runtime-query, layout, renderer, and stage tests pass on macOS.
- The full Mac-safe suite and Windows gameplay E2E remain green.
- Beat lines, measure numbers, metronome behavior, reverse mode, visibility
  configuration, and complete DTX tempo-map support remain explicitly outside
  HPA-518.
