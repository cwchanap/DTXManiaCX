# HPA-408 Notation Core — Implementation Plan

> For the implementation agent: follow this plan in **one implementation PR**, no more than three engineer days. The current PR is documentation-only for design review; convert/continue it rather than creating one PR per step when implementation begins.

**Issue:** https://linear.app/cwchanap/issue/HPA-408/tab-mode-notation-core-port-dtxweb-drum-mapping-and-quantizer  
**Design:** `docs/superpowers/specs/2026-10-18-hpa-408-notation-core-design.md`  
**Source:** `cwchanap/DTXWeb` `packages/common/src/lib/notation/{drumMapping.ts,model.ts,quantize.ts,drumMapping.test.ts,quantize.test.ts}`. Check the latest source at implementation time, and record any meaningful divergence in the PR.

## Constraints / review focus

- Deliver **one PR for HPA-408** containing the complete C# notation core and tests; this draft PR starts with the spec/plan and can be used for implementation after review.
- No UI/SpriteFont/art assets, performance-stage integration, changes to gameplay timing/parsing decisions, or new package. The only existing-production-code edits allowed outside `Notation/` are the two authored slot metadata fields on `Note` and their assignment in `DTXChartParser.ParseNotesFromData`. HPA-409 owns the font; HPA-410 the strip renderer; HPA-411 config/gameplay.
- Port DTXWeb *behavior* into idiomatic, lightweight C#; don't copy `timing.ts`, VexFlow types, unrelated transport state, or a generic notation engine.
- Keep all code GPU-free so xUnit tests can run on Mac. Prefer internal types/records and the existing `ParsedChart`, `ChartTimingMap`, `Note`.
- **Core regression risks:** (1) recovering notation onsets from *truncated* `Note.Tick`, which corrupts 7/8 and >4/4 bars; (2) double-applying bar lengths when converting notation ticks into canonical timing coordinates; (3) copying `Note.Id` before `ChartManager` initializes it. Preserve authored slot fractions, use the existing timing map, and retain `Note` references instead of snapshot IDs.
- No migration or backward-compatibility scaffolding: tab mode is not shipped yet.

## Step 1 — Staff mapping + minimal data contract (roughly half day)

**Add:** 
- `DTXMania.Game/Lib/Song/Notation/DrumStaffMapping.cs`
- `DTXMania.Game/Lib/Song/Notation/NotationModel.cs`
- `DTXMania.Test/Song/Notation/DrumStaffMappingTests.cs`

- [ ] Read current DTXWeb mapping and model files as the ground truth. Map the twelve DTX `Note.Channel` values to staff step + head shape (normal/x/circled-x) via one exhaustive switch; unexpected `Note.Channel` throws clearly.
- [ ] Define structurally immutable result types just large enough for HPA-410: chart, measure, entry (note/rest), chord heads with `IReadOnlyList<Note>` source references (not copied IDs), triplet group. The referenced `Note` objects remain gameplay-owned and mutable.
- [ ] Add focused mapping tests, including distinguish 0x11/0x18, 0x1B/0x1C, and 0x16/0x19/0x1A.
- [ ] Ensure no production dependency on MonoGame, VexFlow, config, or UI.

**Done when:** data types are simple and stable, and the mapping tests pass.

## Step 2 — Pure measure quantizer (roughly one day)

**Add:**
- `DTXMania.Game/Lib/Song/Notation/NotationQuantizer.cs`
- `DTXMania.Test/Song/Notation/NotationQuantizerTests.cs`

- [ ] Translate the algorithm in `DTXWeb/packages/common/src/lib/notation/quantize.ts`, not just its output examples: greedy 192..3 binary durations; chord assembly at a common onset; ordered triplet candidate detection for groups of 24/48/96/192; leading/inner/trailing rest fill; sub-3-tick remainder policy.
- [ ] Accept integer notation tick onsets and already-mapped heads with source `Note` references. Quantizer should not know `ParsedChart`, BPM, `SpriteFont`, or geometry.
- [ ] Deduplicate identical displayed heads only while retaining **all** underlying `Note` references; keep x and circled-x at the same staff step distinct.
- [ ] Adapt DTXWeb's relevant `quantize.test.ts` cases as xUnit tests/theories; include tuplets after leading rests, consecutive groups, 3/4 and 2/4, empty bars, off-grid spans, and invariant tests for entries within measure boundaries.
- [ ] Preserve TS behavior when rests cannot exactly cover tiny 1–2-tick gaps; document those known exceptions instead of adding invented 1-tick glyph durations.

**Done when:** quantization is deterministic, pure (does not mutate source notes), and covered by the core ported cases.

## Step 3 — ParsedChart builder and time/ID integration (roughly half to one day)

**Add:**
- `DTXMania.Game/Lib/Song/Notation/NotationChartBuilder.cs`
- `DTXMania.Test/Song/Notation/NotationChartBuilderTests.cs`

**Tiny production edits:**
- `DTXMania.Game/Lib/Song/Components/Note.cs` — two `internal` original-slot fields (`PairIndex`, `PairCount`); `PairCount == 0` indicates a hand-built note with no slot metadata.
- `DTXMania.Game/Lib/Song/DTXChartParser.cs` — assign `PairIndex = i` and `PairCount = pairCount` at the existing `new Note(...)` construction site. Preserve existing `Tick`, `TimeMs`, constructor behavior and note filtering.

- [ ] Consume a `ParsedChart` already processed by `FinalizeChart()`. Use `MeasureLines` to determine the compiled measure horizon, and `TimingMap.GetMeasureLengthMultiplier(bar)` to read effective carry-forward lengths.
- [ ] Group parsed notes by normalized `Bar`/`Tick` in one pass; map with `Note.Channel` (fail fast for unexpected channel). Convert onsets from the original *authored slot fraction*, `Round(PairIndex * measureTicks / PairCount, AwayFromZero)`. For hand-built notes without slot metadata, use their normalized canonical tick fraction (`Tick / 192`). Never reconstruct parsed onsets from truncated `Note.Tick`. Include rest-only intermediate measures.
- [ ] Timestamp source onsets from their finalized `Note.TimeMs`; synthetic rest entries via canonical tick conversion (`notationTick * 192.0 / measureTicks`) followed by `TimingMap.CalculateTimeMs`. Never hand the scaled notation tick directly to `CalculateTimeMs`.
- [ ] Attach **source `Note` references** to heads (no copied IDs). Test `ParsedChart.FinalizeChart() -> NotationChartBuilder.Build(parsed) -> ChartManager(parsed)` and prove retained references expose the subsequently assigned IDs; optionally also assert the reverse build order. Neither quantizer nor builder may assign/renumber IDs. HPA-411 resolves `note.Id` only after the `ChartManager` has initialized gameplay.
- [ ] An empty chart without timing anchors returns an empty notation result; do not change `ParsedChart.FinalizeChart()` just to materialize empty measures.
- [ ] Add **real-parser** integration fixtures (DTX text → `DTXChartParser` → finalized chart → builder): 7/8 eighths/16ths (7 or 14 cells), plus 5/4 eighth-triplet slots (15 cells). Assert notation onsets are exact musical multiples (7/8 eighths: 0,24,48,...,144; 5/4 triplet row: 0,16,32,...,224) and a 48-tick triplet is detected. Pin unchanged parser `Tick` and resolved `TimeMs` alongside the new notation outputs.
- [ ] Include other integration fixtures with held 0.75 measure length across bars, a later override, in-bar tempo change, 4/4, hand-built note fallback, and simultaneous channels with identical head positions.

**Done when:** notation places odd-meter notes exactly on authored slots while preserving existing gameplay `Note.TimeMs`, and `Note` references survive chord merging without an initialization-order dependency.

## Step 4 — Verification, documentation, and PR polish (roughly half day)

- [ ] Run focused tests and verify nonzero discovery:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~Notation"
```

- [ ] Run the full locally supported test suite:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

- [ ] On Windows CI, run `dotnet test DTXMania.Test/DTXMania.Test.csproj`; inspect failures. There is no need for graphic snapshots or a running game for this slice.
- [ ] Inspect generated models for duplicate timing/mapping sources, accidental parser timing changes, and unnecessary abstractions. Keep four small notation files + tests and the narrow `Note`/parser slot metadata change.
- [ ] Update this plan's checklist / PR summary with tested commands, source parity caveats, and any deliberate variance from DTXWeb. Do not add product screens or art as verification.
- [ ] Mark HPA-408 complete **only after** implementation PR merges; HPA-410 then becomes eligible once HPA-409 is also completed.

## Acceptance checklist for reviewer

- [ ] Only `Lib/Song/Notation/`, its tests/docs, and the two slot-metadata additions in `Note.cs`/`DTXChartParser.cs` are changed.
- [ ] Twelve playable DTX channels use the correct staff and head shapes based on `Channel`, never `LaneIndex`; unexpected channels throw rather than disappearing silently.
- [ ] Quantization covers chords, binary notes/rests, triplets (24/48/96/192), and off-grid remainder semantics matching the DTXWeb port.
- [ ] Effective bar lengths come from the existing CX timing map; there is no second BPM or channel-02 implementation.
- [ ] Authored slot fractions, not truncated `Note.Tick`, determine notation onsets. Real-parser fixtures prove 7/8 and 5/4 slots/triplets remain aligned; `Note.Tick` and gameplay `TimeMs` stay unchanged.
- [ ] A 0.75 bar's canonical source tick 96 maps to notation tick 72 and keeps its actual gameplay timestamp (e.g. bar 1 at 2250 ms when starting after a 1500 ms bar at 120 BPM).
- [ ] Multiple source notes mapped to one visible head retain all original `Note` references; building notation before `ChartManager` is safe, and ID lookups reflect its later assignment.
- [ ] Empty chart and rest-only intermediate measures behave consistently.
- [ ] Mac unit tests and the full Windows CI test project are green.

## Future tickets (not this PR)

- **HPA-409**: Bravura font/glyph asset work, separately scoped (including any fallback PNG art).
- **HPA-410**: Scrolling layout and drawing, using the model generated here.
- **HPA-443**: Fix input binding coordinate mismatch before gameplay integration.
- **HPA-411**: Wire tab-mode selection and judgement coloring using `Note` references; never snapshot a note ID before gameplay initialization.
- **HPA-509**: Tab-mode HUD.
