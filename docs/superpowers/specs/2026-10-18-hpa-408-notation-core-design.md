# HPA-408 Tab-Mode Notation Core — Design

**Issue:** https://linear.app/cwchanap/issue/HPA-408/tab-mode-notation-core-port-dtxweb-drum-mapping-and-quantizer  
**Date:** 2026-10-18  
**Status:** Draft for review  
**Parent:** HPA-394; prerequisite HPA-407 is complete; produces the model consumed by HPA-410.

## Goal

Create a GPU-free C# model and quantizer for a scrolling drum-notation strip. Given an already-finalized CX `ParsedChart`, emit ordered measures containing drum-note chords, rests, triplet groups, meaningful duration ticks, absolute event times, and the gameplay `Note.Id` values associated with each drawn notehead.

Keep the product slice to one implementation PR (<=3 engineer days). Do not wire the result into gameplay yet; HPA-410 draws it, HPA-411 integrates it, and HPA-409 supplies the glyphs.

## Verified Code Contracts

- CX uses 192 *canonical* chart ticks per bar. `ChartTimingMap` internally applies each bar's effective channel-02 multiplier, including the DTX carry-forward behavior added in HPA-407.
- CX parser stores authored integer `Note.Bar`/`Note.Tick` and the original numeric `Note.Channel` (0x11–0x1C). `Note.LaneIndex` merges open/closed hi-hat and maps left bass into the left-pedal gameplay lane; it must **not** drive staff mapping.
- `ParsedChart.FinalizeChart()` builds the timing map, resolves `Note.TimeMs`, and emits `MeasureLines` with a final end boundary. A chart with no notes, BGM, or video has no compiled timing map.
- `ChartManager(ParsedChart)` creates a list of references to those same `Note` objects and assigns `Note.Id` only **after** sorting that list. Parsed notes otherwise retain default ID 0.
- DTXWeb's source implementation is `packages/common/src/lib/notation/{drumMapping.ts,model.ts,quantize.ts}`; it rounds fractional note positions into notation ticks, detects triplet groups of 24/48/96/192 ticks, and decomposes binary rests. Its `buildChartTiming`/`timing.ts` and VexFlow/page layout are not reusable timing authorities for CX.

## Design

### Files / responsibilities

Create in `DTXMania.Game/Lib/Song/Notation/`:

1. `DrumStaffMapping.cs` — one read-only mapping from numeric DTX `Note.Channel` to diatonic staff step, notehead type (normal, x, circled x), and optional label. Reproduce DTXWeb's twelve playable channels (11–1C) only. Use an explicit, documented numeric staff-step origin (e.g. E4=0) rather than retaining VexFlow key strings at runtime.
2. `NotationModel.cs` — small immutable records for a chart, measure, entry (note chord or rest), head, and tuplet. A measure includes `Index`, `MeasureTicks`, and entries in order. An entry includes `StartTick`, `DurationTicks`, `TimeMs`, and, for a chord, heads with their source note IDs. A tuplet refers to the entry range and slot duration. Keep this internal to the game assembly initially; no renderer/MonoGame references.
3. `NotationQuantizer.cs` — pure measure quantization. Accept measured tick onsets with mapped heads and IDs; produce notes, rests and triplet indices. Port `ticksToDurations`, greedy binary spans, candidate ranking, group detection, rest filling, and the off-grid 1–2 tick remainder behavior from DTXWeb. A small helper type for input onsets is fine; no general notation framework.
4. `NotationChartBuilder.cs` — assemble a finalized `ParsedChart` into measures, grouping source notes once by normalized bar, determining effective measure lengths from `chart.TimingMap`, feeding the quantizer, and assigning times.

### Source channel → staff position

Port DTXWeb's channel mapping without depending on `LaneIndex`:
- 0x13 (bass) and 0x1C (left bass) → F4 normal
- 0x12 snare → C5 normal; 0x14 high tom → E5 normal; 0x15 low tom → D5 normal; 0x17 floor tom → A4 normal
- 0x11 closed hat → G5 x; 0x18 open hat → G5 circled x; 0x1B pedal hat → D4 x
- 0x16 right crash → A5 x; 0x1A left crash → B5 x; 0x19 ride → F5 x.

Distinct channel/head combinations must survive chord deduplication: open and closed hi-hat at the same onset are different heads. When two source notes map to the *same* head (e.g. bass and left bass at one time), the visual head can be shared but retain **all** source `Note.Id` values so either judgement can highlight it.

### Two tick coordinate systems — critical

1. CX canonical bar coordinates: `Note.Tick` and `ChartTimingMap.CalculateTimeMs(bar, tick)` use 0..192 **independent of measure-length multiplier**. The timing map itself applies the multiplier.
2. Notation coordinates: `measureTicks = Round(192 * effectiveMultiplier)` (use positive-tick JavaScript-compatible midpoint rounding). Convert a source tick to an onset via `Round(sourceTick * measureTicks / 192)`; this is the DTXWeb-style scaled measure geometry.

The HPA-408 Linear issue loosely says to scale ticks and stamp `TimeMs` through `CalculateTimeMs`; this specification supersedes that wording. Only canonical coordinates may be passed to the timing map.

Do **not** pass a scaled notation tick unchanged to `CalculateTimeMs`. That would multiply the length twice. For a rest/synthetic entry at notation tick `t`, convert to canonical tick `t * 192.0 / measureTicks`, then ask the existing map for its time. For a source-note onset, prefer the finalized `Note.TimeMs` (or the original canonical tick) so rounding of notation coordinates cannot move judgement-time correspondence. If multiple authored onsets collapse to one quantized onset, use the earliest source time deterministically and keep all note IDs.

Example: at 120 BPM, bar 0 and bar 1 inherit `#00002:0.75`, so bar 1 begins at 1500 ms and a source note at (bar 1, canonical tick 96) is at 2250 ms. Its notation coordinate is tick 72 in a 144-tick bar. Calling `CalculateTimeMs(1, 72)` would incorrectly yield 2062.5 ms.

Timing map may include BPM changes within measures: never interpolate a single bar-start/bar-end duration or implement a second BPM timeline.

### Lifecycle / IDs

`NotationChartBuilder.Build(ParsedChart)` expects a finalized chart. For a result intended for gameplay highlighting, **construct `ChartManager` first**, then build notation from the same `ParsedChart`: `ChartManager` assigns IDs on the source `Note` references. No ID generation/renumbering in the notation subsystem, and no changes to `ChartManager`, `JudgementManager`, or the parser. Document this precondition on the builder and verify the result after `ChartManager` construction in tests. HPA-411 must respect this order.

For a chart with no compiled timing map (no finalized timed events), return an empty notation chart; do not try to look up measure lengths from unbuilt anchors.

Use `MeasureLines.Count - 1` as the compiled measure count (terminal boundary excluded); include empty measures between the first and final timed event as rests. Normalize any noncanonical test-note positions using `ChartTimingMap.NormalizePosition` before grouping. Do not invent extra measures beyond CX's compiled chart horizon.

### Quantization behavior / limitations

Adapt DTXWeb's actual `quantize.ts` algorithm instead of designing a new quantizer. Detect 24/48/96/192-tick triplet groups, preserve tuplet `startIndex/count/slotTicks`, and retain the existing rules for binary duration decomposition, onsets/chords, and rest fill. Preserve its intentional tolerance for nonrepresentable 1–2-tick gaps rather than claiming perfect rhythmic coverage for those exceptional spans. The quantizer produces only data; glyph selection, positioning, VexFlow semantics, and drawing remain out of scope.

The existing parser truncates odd source pair subdivisions to integer canonical ticks (`CalculatePairTick`); DTXWeb rounds fractional positions. Port expected behavior from fractional inputs in focused quantizer tests, and write separate integration cases through actual CX integer ticks. Never adjust the parser's timing semantics to make tests match the TS fixtures.

## Tests / acceptance

Add `DTXMania.Test/Song/Notation/` tests. Adapt DTXWeb's `drumMapping.test.ts` and applicable `quantize.test.ts` coverage (the TS suite has about 53 quantization/build cases; xUnit theories may consolidate examples rather than copying code line for line).

At minimum prove:
- All 12 channel mappings including 0x18 vs 0x11, 0x1C vs 0x1B, and ride vs crashes; unsupported channels excluded.
- Empty, full, sparse and chorded measures; binary durations, rests, and triplet groups (24/48/96/192) with leading rests and consecutive triplets; tuplets refer to actual entry indices.
- Off-grid remainder handling remains compatible with the TS quantizer, without silently dropping real note IDs.
- Quantized entry timestamps remain monotonic and correct for BPM changes within a bar.
- 3/4 held over successive bars through HPA-407: measureTicks 144 in both; a note at canonical tick 96 resolves to 2250 ms in bar 1, not double-scaled.
- A later explicit bar-length override is respected; default 4/4 still yields 192 ticks.
- Open+closed hi-hat in one chord stay distinct; bass+left-bass at one onset retain both IDs.
- A chart with no compiled events returns an empty result. A bar with no playable notes but within compiled `MeasureLines` gets rests.
- Build after `ChartManager` assignment preserves the same actual IDs used by gameplay. No independent IDs assigned by notation.

Run `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~Notation"` locally and the full Mac suite; Windows CI must run the regular full `DTXMania.Test` suite before implementation merge. Verify that the new test names are discovered (a zero-test filtered run is not proof).

## Out of scope

No graphics/rendering, `ResourceManager`, SpriteFont/Bravura, UI, config, gameplay judgement changes, song-selection controls, audio scheduling, JSON serialization/persistence, chart parsing updates, DTXWeb/NX edits, or a separate notation package. No GDA/G2D compatibility claim: the CX parser does not yet accept their real two-letter channels.

## Source references

- CX: `DTXMania.Game/Lib/Song/Components/{Note.cs,ParsedChart.cs,ChartTimingMap.cs,ChartManager.cs}`
- CX: `DTXMania.Game/Lib/Song/DTXChartParser.cs`
- DTXWeb: https://github.com/cwchanap/DTXWeb/tree/main/packages/common/src/lib/notation
- Related roadmap: HPA-394 → HPA-408 and HPA-409 → HPA-410 → HPA-411 → HPA-509; HPA-443 separately blocks HPA-411.
