# HPA-407 DTX Measure-Length Carry-Forward Design

**Issue:** [HPA-407](https://linear.app/cwchanap/issue/HPA-407/chart-carry-xxx02-bar-length-changes-forward-for-dtx-charts)  
**Date:** 2026-10-16  
**Status:** Draft for review

## Context

`ChartTimingMap` currently treats channel `02` measure lengths as one-bar overrides: during `Rebuild`, a bar without its own `_measureLengths` entry falls back to `1.0`.

That matches BMS/BME/BML behavior, but not DTX-family behavior. DTXManiaNX keeps the last `#xxx02` multiplier active for DTX charts until another `02` row changes it, and DTXCreator emits `02` only when the value changes. Real DTX charts can therefore hold a non-4/4 measure length across multiple bars without repeating the directive.

**GDA/G2D caveat:** CX's parser only recognizes DTX-style hexadecimal channel codes (`#MMMCC:`). Real GDA/G2D charts use two-letter channels (`#000BL:` for measure length, two-letter lanes), so those rows are dropped before timing and are unaffected by this change. GDA/G2D channel parsing is recorded as a follow-up on HPA-407 (a separate ticket could not be created); HPA-407 must not claim GDA/G2D compatibility.

HPA-407 fixes that compatibility gap before HPA-408 consumes the timing map for tab-mode notation.

## Goals

- Carry the last valid channel `02` measure multiplier forward for `.dtx` charts and every other non-BMS-family suffix the parser sees (see the GDA/G2D caveat above).
- Preserve existing per-bar reset behavior for `.bms`, `.bme`, and `.bml` charts; treat all other file extensions passed directly to `ParseAsync` as carry-forward, matching the NX default.
- Keep `ChartTimingMap` as the single timing authority used by `ParsedChart.FinalizeChart()`.
- Keep channel `02` parsing and validation unchanged.
- Cover the format split with focused unit/parser tests.

## Non-goals

- No changes to channels `03`/`08`, BPM resolution, note parsing, judgement, rendering, audio scheduling, or song-library metadata duration.
- No tab-mode implementation from HPA-408+.
- No parser architecture rewrite or DTXManiaNX port.
- No new chart-format abstraction or public API unless the implementation proves one is required.
- No DTXCreator changes.
- No GDA/G2D channel-parsing fixes: CX drops real GDA two-letter channel codes today (see the caveat above); follow-up recorded on HPA-407.

## Required Semantics

| Chart family | Missing `02` on a later bar |
| --- | --- |
| `.dtx`, `.gda`, `.g2d` | Reuse the most recently configured multiplier; default to `1.0` before the first `02`. |
| `.bms`, `.bme`, `.bml` | Reset that bar to `1.0`. |
| Other extensions passed directly to `ParseAsync` | Carry forward (DTX-first fallback); this does not extend the library scanner's supported extension list. |

An explicit later `02` always replaces the active DTX-family multiplier from that bar onward. Existing invalid-value behavior remains unchanged: invalid/non-positive/non-finite values are ignored and do not become a new active multiplier.

Notes on the table:

- `.bml` is a deliberate CX deviation: DTXManiaNX resets per bar only for `.bms`/`.bme` (`CDTX.cs:3775-3791`) and defines no `.bml` type, so NX would treat `.bml` as DTX (carry-forward). CX classifies `.bml` — a BMS dialect it already supports — with the BMS reset family.
- The `.gda`/`.g2d` row describes flag semantics only; CX drops real GDA channel codes today, so real GDA/G2D charts are unaffected (see the GDA/G2D caveat).

## Approaches Considered

### 1. Configure one carry-forward flag on `ChartTimingMap` — selected

`DTXChartParser` already knows the source file extension. Configure the chart's existing `TimingMap` once per parse attempt with whether measure lengths carry forward. `ChartTimingMap.Rebuild()` then applies either persistent or per-bar semantics while compiling anchors.

This keeps syntax/format detection in the parser and timing math in the timing map. It requires no second timing representation and no new public model.

### 2. Expand inherited measure lengths in the parser — rejected

The parser could synthesize `SetMeasureLength()` calls for every later bar. That duplicates timing semantics in parsing code, requires knowing the final horizon while parsing, and obscures which `02` rows were authored.

### 3. Derive behavior from `ParsedChart.FilePath` during finalization — rejected

This avoids one parser assignment but makes the otherwise format-agnostic chart model inspect filename extensions. The parser is already the correct boundary for format-specific behavior.

## Chosen Design

### `ChartTimingMap`

Add one internal configuration value, named for behavior rather than file format. It defaults to `true` (carry-forward) so the timing-map default agrees with the parser's default — only BMS-family formats opt out — and directly constructed charts get NX-style DTX semantics:

```csharp
private bool _carryMeasureLengthsForward = true;

internal bool CarryMeasureLengthsForward
{
    get => _carryMeasureLengthsForward;
    set
    {
        _carryMeasureLengthsForward = value;
        InvalidateAnchors();
    }
}
```

The setter invalidates compiled anchors, matching `SetMeasureLength` and `SetTempoChange`, so the flag can never be flipped after a build and silently leave stale timing.

`SetMeasureLength(int bar, double multiplier)` remains unchanged and stores only authored valid values.

During `Rebuild`:

1. Start the active multiplier at `1.0`.
2. If the current bar has an explicit valid multiplier, make it active.
3. If the current bar has no explicit multiplier and carry-forward is disabled, reset the active multiplier to `1.0`.
4. Build the bar's timing anchors using that active multiplier.

Tempo-change handling remains untouched.

### `DTXChartParser`

When each encoding attempt creates a fresh `ParsedChart`, configure `chart.TimingMap.CarryMeasureLengthsForward` by **excluding only BMS-family reset formats**. Compute the check once per `ParseAsync` call — outside the per-encoding retry loop — via a small static predicate placed next to `_supportedExtensions`/`IsSupportedFile`:

```csharp
private static bool ResetsMeasureLengthsPerBar(string filePath) =>
    Path.GetExtension(filePath).ToLowerInvariant() is ".bms" or ".bme" or ".bml";

// once per ParseAsync, before the encoding loop:
var carryMeasureLengthsForward = !ResetsMeasureLengthsPerBar(filePath);

// per fresh chart inside the loop:
chart.TimingMap.CarryMeasureLengthsForward = carryMeasureLengthsForward;
```

Thus `.dtx`, `.gda`, `.g2d` and any other file extension passed directly to `ParseAsync` carry forward; `.bms`, `.bme`, `.bml` reset (case-insensitive suffix match). `ParseAsync` currently accepts paths irrespective of extension, whereas library discovery filters with `IsSupportedFile`. Do not change the supported-extension list, add an enum, or create a format subsystem.

Because parser state is recreated per encoding attempt, the flag must be applied to each fresh chart instance before finalization.

### `ParsedChart`

No production changes are expected. `FinalizeChart()` continues rebuilding and consuming the existing timing map exactly once, so notes, BGM events, video events, measure lines, beat markers, and duration automatically receive the corrected timing.

## Tests

### Timing-map unit coverage

In `DTXMania.Test/Song/ChartTimingMapTests.cs`:

- Preserve `GetMeasureLengthMultiplier_ShouldReadCompiledBarStartAnchor` as the reset-branch pin by explicitly setting `CarryMeasureLengthsForward = false`; it keeps asserting an unspecified next bar uses `1.0` (the default is now carry-forward).
- Add a carry-forward test where a `0.75` multiplier on bar 0 remains active through bars 1 and 2; rebuild through bar 3 so later bars stay queryable.
- Include a later explicit multiplier distinct from `1.0` (e.g. `SetMeasureLength(2, 1.5)`) and assert bar 2 uses `1.5` and bar 3 also carries `1.5` — proving the carried value stops at the override *and* the replacement itself carries on. An override equal to the reset value cannot fail a broken implementation.

### Parser integration coverage

In `DTXMania.Test/Song/DTXChartParserTests.cs`:

- Generalize the existing temporary timing fixture helper to accept a file extension, defaulting to `.dtx`.
- Add a theory covering `.dtx`, `.gda`, and `.g2d` where a single `#00002:0.75` keeps bar 1 short and moves a bar-2 note/measure boundary to `3000 ms` at 120 BPM.
- Add a theory covering `.bms`, `.bme`, and `.bml` with the same authored rows where bar 1 resets to 4/4 and the bar-2 note/measure boundary remains `3500 ms`.
- Include at least one uppercase case in each family (e.g. `.BMS` resets, `.DTX` carries) so a future regression of the `ToLowerInvariant()` extension check fails a test.
- Include one unlisted extension (e.g. `.txt`) in the direct-parser carry-forward fixture coverage; it must resolve at `3000 ms` without making `.txt` a library-scanned format.
- The `.gda`/`.g2d` cases pin the extension predicate only; real GDA charts use non-hex channel codes CX does not parse yet (follow-up on HPA-407).

These integration tests prove the parser selects the correct behavior; unit tests prove the timing-map state transition itself.

## Known Limitations

- Song-select duration ignores channel `02` entirely: `DTXChartParser.CalculateDurationAsync` estimates duration as `(lastMeasureWithNotes + 1) * 4 * 60 / BPM`. With carry-forward, a held multiplier (e.g. `0.5` across a long chart) can make gameplay run roughly half the displayed estimate. The gap predates HPA-407 (it was previously limited to one bar); carry-forward widens it. Fixing duration estimation is out of scope here and warrants a follow-up.

## Release Note

The implementation PR should state that DTX charts with held `#xxx02` measure lengths may have corrected note/event timing compared with previous CX builds, matching DTXManiaNX/DTXCreator semantics. It must not claim GDA/G2D compatibility — CX drops real GDA channel codes today (follow-up on HPA-407). No separate changelog subsystem or release-note file is introduced for this ticket.

## Acceptance Criteria

- Non-BMS-family extensions (`.dtx`, `.gda`, `.g2d`, unlisted suffixes) carry a valid `02` multiplier until another valid `02` changes it.
- `.bms`, `.bme`, and `.bml` still use `1.0` for bars without their own `02` directive (case-insensitive extension match).
- Direct `ParseAsync` calls for other extensions use DTX-style carry-forward, without changing `IsSupportedFile`.
- Existing channel `03`/`08` and timing-map behavior remains unchanged.
- Focused timing/parser tests pass on the normal test target.
- Full suite passes: `DTXMania.Test.Mac.csproj` locally plus the full `DTXMania.Test` suite on Windows CI (the implementation PR must be green on CI before merge).
- One implementation PR contains the complete HPA-407 change.