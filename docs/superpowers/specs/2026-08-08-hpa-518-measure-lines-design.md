# HPA-518 NX-Style Gameplay Measure Lines Design

**Issue:** [HPA-518](https://linear.app/cwchanap/issue/HPA-518/measure-line-in-game-play-stage)  
**Date:** 2026-08-08  
**Status:** Revised after asset and reuse review

## Context

The Performance stage scrolls playable drum notes over the ten-lane panel but
does not show measure boundaries. HPA-518 asks for a measure line similar to
DTXManiaNX.

NX represents measure and beat lines as timeline chips. It synthesizes a bar
line at each measure boundary, scrolls that chip with the same distance model
as playable chips, and draws a two-pixel strip across the lane panel. NX also
supports beat lines, measure-number text,
metronome sounds, reverse mode, and lane-visibility settings; those adjacent
features are not implied by the singular measure-line request.

DTXManiaCX already has the relevant visual seams:

- `NoteRenderer.GetNoteScreenY(...)` owns the current time-to-screen mapping.
- `NoteRenderer.SetScrollSpeed(...)` owns scroll speed and visible look-ahead.
- `PerformanceUILayout.HitBar.Bounds` defines the full lane-panel width at
  `x=295`, width `558`.
- `NoteRenderer` already owns a one-pixel white texture for solid rectangle
  rendering.
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
- Match the NX drum-lane geometry with a visible neutral-gray two-pixel line
  in both bundled skins.
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
    private const int TicksPerMeasure = 192;

    internal static double CalculateTimeMs(int bar, int tick, double bpm);
}
```

`Note.CalculateTimeMs(...)`, `BGMEvent.CalculateTimeMs(...)`, and measure-line
generation delegate to this helper. Moving the current formula must be
behavior-preserving; existing note and BGM timing tests remain parity guards.
The helper name deliberately avoids claiming support for a complete DTX tempo
map.

`DTXChartParser` keeps its existing private parse-grid constant and remains
unchanged. It already calls `ParsedChart.FinalizeChart()` after parsing. This
feature neither reads raw discarded channels nor expands parser semantics.

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

- Clear `MeasureLines` before generation so a repeated finalization cannot
  duplicate boundaries. This makes measure-line generation repeat-safe; it
  does not change the existing behavior where `FinalizeChart()` adds the
  duration buffer on every call.
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
time window. If `ScrollPixelsPerMs` is zero or negative, the exposed grace is
zero; negative query input is also clamped to zero.

`GetActiveMeasureLines(...)` performs a plain ordered scan of `_measureLines`,
skipping entries before the lower bound and stopping after the upper bound.
The expected collection is only a few hundred entries, so a second custom
binary-search implementation is unnecessary. The method must not call the
note-only `FindStartIndex(...)`, reuse `_lastActiveIndex`, or introduce one
cursor shared by both collections. A line query therefore cannot change the
result of a later note query, including a note query that moves backward in
time.

### Layout and rendering

Add `PerformanceUILayout.MeasureLine` constants and a pure destination helper:

- destination `X`: `HitBar.Bounds.X` (`295`)
- destination width: `HitBar.Bounds.Width` (`558`)
- destination height: `2`
- color: neutral gray `new Color(169, 169, 169)`, matching the upper row of
  NX's bundled strip while remaining visible in both bundled skins
- layer depth: `0.78f`
- `GetDestinationRect(double centerY)`: floor `centerY - 1` for the top edge
  and return the full-width two-pixel destination rectangle

`NoteRenderer` gains an `[ExcludeFromCodeCoverage]` thin draw loop named
`DrawMeasureLines(...)`. It uses
`GetNoteScreenY(line.TimeMs, currentSongTimeMs)` and centers the two-pixel
destination on that Y position. It draws destination
`(295, centeredY, 558, 2)` from the renderer's existing `_whiteTexture` with
the layout color and no source rectangle. If `_whiteTexture` is unavailable,
drawing is a no-op.

This asset-independent path is required by the shipped files: the default
System texture has visible pixels at source rows `769`–`770`, but the same
rows are fully transparent in both the CX Neon texture and its skin-generator
source. A dimension-only texture guard would therefore accept CX Neon's
`718x776` image and still draw an invisible line.

The renderer applies top-of-screen culling and a measure-line-specific
20-pixel below-judgement grace. Current stage note queries start at
`songTimeMs`, so they do not actually supply past notes to `DrawNote`; this
line grace is intentional visibility behavior for the two-pixel strip, not a
claim of note-query parity. `NoteRenderer` exposes the corresponding
time-domain grace through a read-only property for the stage query so callers
do not duplicate the conversion.

Measure-line drawing must not change the ready state for playable notes.
Invalid or null inputs remain safe no-ops, matching existing `NoteRenderer`
draw methods.

### Performance-stage integration

`PerformanceStage.OnDraw(...)` calls `DrawMeasureLines()` after
`DrawLaneBackgrounds()` and before `DrawNotes()`. Its call order relative to
`DrawPads()` is not a layering contract because `SpriteSortMode.BackToFront`
uses depth. Measure lines render at `0.78f`: numerically below lane backgrounds
at `0.8f` and above the note depths actually in use (`0.70f` for rectangle
fallbacks and `0.05f` for sprites), the judgement line at `0.6f`, and pads at
`0.1f`. The distinct value also avoids reusing the stale `0.75f` pad depth in
the current stage comment. The implementation corrects that comment but does
not change `PadRenderer.BaseDepth` or pad behavior. The stage method
uses the same values as note rendering:

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
  -> solid neutral-gray two-pixel line
```

## Error and Compatibility Behavior

- Empty charts produce no measure lines and no draw calls.
- Sparse charts still produce every intervening boundary.
- A BGM-only chart produces boundaries from its occupied measures.
- Unsupported DTX tempo and measure-length channels keep their current CX
  behavior; HPA-518 neither worsens nor silently claims to fix that limitation.
- The highest occupied measure comes only from the current gameplay model's
  `Notes` and `BGMEvents`; discarded raw DTX channels do not extend the list.
- Measure-line visibility does not depend on drum-chip texture dimensions or
  alpha content; custom and bundled skins use the same layout-defined color.
- The default and CX Neon assets remain unchanged.
- No configuration or persisted-data migration is required.
- Existing `TimeMs == 0` recalculation gates in `AddNote` and `ChartManager`
  remain unchanged. Measure-line generation always calls
  `ChartTimeCalculator.CalculateTimeMs(...)` directly, including bar `0`, and
  does not copy that sentinel pattern.

## Testing

### Timing and model tests

Extend `NoteTests`, `BGMEventTests`, and/or add focused calculator tests to
prove moving the formula preserves current results at multiple BPMs and tick
positions.

Extend `ParsedChartTests` with:

- events in measures `0` and `2` produce lines for bars `0`, `1`, `2`, and `3`;
- a BGM-only chart generates boundaries;
- an empty chart generates none;
- calling `FinalizeChart()` twice does not duplicate boundaries, without
  changing the method's existing duration-buffer behavior;
- line times use the fixed current clock;
- terminal lines do not extend `DurationMs`.

Tests that hand-build a `ParsedChart` for measure-line behavior call
`FinalizeChart()` before constructing `ChartManager`, matching the production
parser path. Generation remains in `ParsedChart` because the collection is
derived chart state and is the intended seam for a future timing map.

### Runtime query tests

Extend `ChartManagerTests` to verify:

- `AllMeasureLines` is a copied read-only runtime view;
- the active query includes future lines inside look-ahead;
- the supplied grace window retains a just-passed line;
- lines outside both bounds are excluded;
- note-query state remains unaffected;
- querying measure lines at a later time and then notes at an earlier time
  returns the same notes as a fresh `ChartManager`, proving the line query
  cannot poison `_lastActiveIndex`.

### Rendering and stage tests

Extend `PerformanceUILayoutMoreTests` and `NoteRendererLogicTests` to verify:

- destination geometry is `x=295`, width `558`, height `2`;
- layout constants expose the neutral-gray color and `0.78f` depth;
- line Y uses the same calculation as a note at the same `TimeMs`;
- the time-domain grace corresponds to 20 pixels at multiple scroll speeds;
- zero or negative scroll pixels produce zero past-grace milliseconds;
- depth constants satisfy `0.8f > MeasureLine.Depth > 0.70f`, placing the line
  visually in front of lane backgrounds and behind every note, judgement, and
  pad depth currently in use under `BackToFront` sorting;
- offscreen and null inputs are safe.

Extend `PerformanceStageDeterministicTests` to verify the stage supplies the
current song time, renderer look-ahead, and active chart lines without drawing
when required collaborators are absent.

### End-to-end and visual verification

Leave the generated gameplay E2E fixture unchanged. It already contains events
in bars `000` and `001`, which generate boundaries at bars `0`, `1`, and `2`;
the automated smoke remains behavioral rather than pixel-based and must still
reach Result successfully. Capture a gameplay screenshot during manual
verification and confirm:

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
- `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`
- `DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs`
- `DTXMania.Game/Lib/Stage/PerformanceStage.cs` — add measure-line wiring and
  correct the stale depth-order comment without changing pad depth
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
- Default, CX Neon, and custom skins render a visible solid measure line
  without depending on drum-chip texture content.
- Empty/malformed chart state and missing renderer resources do not crash.
- Measure lines do not affect chart duration, judgement, audio, scoring, or
  persistence.
- Focused model, runtime-query, layout, renderer, and stage tests pass on macOS.
- The full Mac-safe suite and Windows gameplay E2E remain green.
- Beat lines, measure numbers, metronome behavior, reverse mode, visibility
  configuration, and complete DTX tempo-map support remain explicitly outside
  HPA-518.
