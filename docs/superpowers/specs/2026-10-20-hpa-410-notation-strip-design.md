# HPA-410 — Scrolling Drum Notation Strip (Design)

**Issue:** https://linear.app/cwchanap/issue/HPA-410/tab-mode-scrolling-notation-strip-renderer  
**Parent:** HPA-394 music tab mode  
**Delivery:** One implementation PR, continuing this documentation PR after review; <=3 engineer days.  
**Prerequisites:** HPA-408 (notation core, merged #195), HPA-409 (Bravura font/glyphs, merged #199).

## Objective and boundary

Implement one horizontal, time-scrolling, five-line drum-staff strip. It renders an already-built `NotationChart` using HPA-409's Bravura glyphs, and bar boundaries from `ParsedChart.MeasureLines`. It **does not** install a new game mode or alter input, judgement, timing, playback, score, chart parsing, configuration or HUD. HPA-411 owns the `PerformanceStage` integration, per-note judgement tint and graceful lane fallback; HPA-509 owns tab HUD/pad feedback. No new art is needed.

## Grounded reuse

- `Lib/Song/Notation/NotationModel.cs`: `NotationChart -> NotationMeasure(Entries, Tuplets) -> NotationEntry(StartTick, DurationTicks, TimeMs, Heads)`. A head already contains `DrumStaffPosition(StaffStep, Shape)` and source `Note` references; **do not** build a second drum-channel mapping or recompute note times.
- `Lib/Song/Notation/NotationGlyphs.cs`: `FromTicks`, `Notehead`, `Rest`, `Flag`, `ToChar`, `GetMetrics` and `IsComplete`. Glyph metrics are in Bravura 1.482 staff-space units. Reuse the recorded ~13.4–13.65 pixels/staff-space and font-origin/baseline calibration from HPA-409's design as the initial draw calibration; verify visually rather than hardcoding per-glyph pixel offsets.
- `Lib/UI/Layout/PerformanceUILayout.cs`: virtual 1280x720 and `NoteBaseScrollPixelsPerMs = 37.5*286/60000`. `NoteRenderer.SetScrollSpeed` uses this base times `scrollSpeedPercent/100.0`.
- `Lib/Resources/IResourceManager.cs` and `IFont.cs`: `LoadFont("Bravura-Notation",40)`, `NotationGlyphs.IsComplete(font.SupportsCharacter)`, `IFont.DrawString`, and established resource disposal. A missing font may silently resolve to another family; validate **all** glyphs before drawing.
- `Lib/Song/Components/ParsedChart.cs`: `MeasureLines` are compiled, sorted boundaries with `TimeMs`. `ChartManager.GetActiveNotes` gives the precedent for bounded lookahead, but does **not** contain rests or all notation entries, so cannot be used as the notation source.

## Layout contract

Put renderer-specific constants in a small `PerformanceUILayout.NotationStrip` nested class (no standalone settings system).

- Virtual screen width **0..1280**, fixed hit line **x=220**. At 100% scroll speed, `pxPerMs ~ 0.17875`, giving ~5.9 s of content to the right of the hit line. The concrete geometry below is the **HPA-410 initial layout contract**, chosen to make the approved full-width mockup C direction implementable without relying on the external mockup link. HPA-509 may refine HUD positions, but should not silently change this strip contract.
- Note onset x: `HitLineX + (entry.TimeMs - nowMs) * (NoteBaseScrollPixelsPerMs * scrollSpeedPercent/100.0)`. The same clock and speed move notes, rests, beams, tuplet markers and barlines. BPM/measure length changes are **already represented** by compiled `TimeMs`; do not add another timing transform.
- Five staff lines correspond to E4/G4/B4/D5/F5 (`StaffStep = 0,2,4,6,8`), with one diatonic step = half a staff space. Place a head from its existing `DrumStaffPosition.StaffStep`; render short ledger lines for outside-staff notes as appropriate. Draw key names `LC, CY, HH, RD, HT, LT, SN, FT, BD, LP` at the corresponding mapped staff steps; key names are *visual labels*, not interactive drum pads.
### Fixed 1280×720 strip geometry (initial values)

| Element | Initial value |
| --- | --- |
| Strip vertical bounds | y=290..580; horizontal bounds x=0..1280 |
| Drum-key label column | x=0..190, labels right-aligned at x=174, font ≈10px |
| Musical ink bounds | x=195..1280; clip all moving notation to this region (no overdraw into labels) |
| Hit line | x=220; 2px wide |
| Five staff lines | bottom E4 y=510, then y=490, 470, 450, 430 (20px/staff space; 10px/diatonic step) |
| Staff/measure line thickness | 1px / 1px |
| Stem/beam geometry | up-stem 2px wide; primary and secondary beams 5px thick with 8px beam-level separation |
| Stem vertical reach | top is **7 diatonic staff steps (3.5 staff spaces) above the highest head** in the chord, starting at the lowest stemmed head's Bravura `StemUpSE` anchor; a single B5 head (step 11) reaches step 18, not step 12 |
| Colours (initial neutral theme) | strip background `#10151D`; staff/barlines `#718096`; noteheads/rests `#F2F4F8`; labels `#CAD3DE`; hit line `#F0C15A` |

The label column spans the full strip height but draws only the 10 labels mapped to their existing staff steps. Keep the rest of the stage/HUD untouched: gauge, combo, score, progress and result tiles are HPA-509. These are selected **working dimensions**, not a claim of pixel-perfect dimensions from a separately hosted mockup. Store them in `PerformanceUILayout.NotationStrip` so an implementing agent can work solely from the repository. Scale Bravura with `20px / measured font-pixels-per-staff-space` (HPA-409 measured ≈13.4–13.65px at 40pt); retain the measured font baseline/origin adjustment and check alignment in the visual specimen.
- White/neutral head colours for now. No judgement colours/fade logic in HPA-410; HPA-411 will supply per-head visual state from the stored source notes.
- Draw order: strip/background/staff and key column; measure lines and hit line; stems/beams/ledger lines; glyphs (heads, rests, flags, tuplet 3). Keep drawing operations with the existing `SpriteBatch` and a 1x1 texture; no custom shaders or render targets.

## Geometry, rhythm and visibility

Split the implementation into **one pure layout component** and **one thin MonoGame draw component**:

- `Lib/Stage/Performance/NotationStripLayout.cs`: CPU-only layout math. Inputs: compiled notation, compiled measure-line boundaries, current `nowMs`, positive scroll speed and the fixed strip geometry. Output: a small set of typed glyph placements and straight segments/rectangles (plus labels), with x/y coordinates ready for drawing. No `GraphicsDevice`, `SpriteBatch`, `IFont`, resource acquisition, timing-map mutation or judgement state.
- `Lib/Stage/Performance/NotationStripRenderer.cs`: owns the validated Bravura font and white-pixel resource following existing lifetimes, draws layout output through `IFont.DrawString` and `SpriteBatch.Draw`, and exposes a simple readiness/lifetime contract for HPA-411. Don't add a generic render graph, cross-platform renderer interface or multi-font fallback pipeline.
- Centre noteheads on the onset and mapped staff step using `NotationGlyphs.GetMetrics`/the pinned global font calibration. One chord gets **one rhythmic stem** and one x position, but keeps all mapped head glyphs (including overlapping/open hi-hat variants). Stems use Bravura `StemUpSE` anchors rather than uniform guessed notehead width. For stemmed chords, start at the **lowest** head's up-stem anchor and set the stem top relative to the **highest** head: `highestStaffStep + 7` diatonic steps (=3.5 staff spaces above the highest head). This is not a fixed C6/step-12 ceiling; e.g. B5/step 11 tops out at step 18. Whole notes and rests have no stems.
- For non-tuplet entries, use `NotationGlyphs.FromTicks(entry.DurationTicks)`. For entries inside `NotationTuplet`, derive displayed base ticks as `SlotTicks * 3 / 2`, then use `FromTicks` (as HPA-409 requires). Draw rests at staff centre with their correct rest glyph; no stem on whole notes/rests.
- Beam consecutive short **note** entries in the same 48-tick notation beat, not across rests, bar boundaries or tuplet group boundaries. One beam per shared duration level (8th through 64th), with partial/secondary beams for mixed 8th/16th groupings. Where no eligible partner exists, draw a normal flag glyph. Each tuplet remains a coherent independent group with its `3` above; do not depend on VexFlow or recompute tuplets. Keep beam slope flat.
- Visibility bounds follow the actual strip x interval plus glyph/beam margin. Include a small look-behind past the hit line but prevent drawing over the key-label column. Cull whole grouped decorations consistently at both edges, so a surviving head is not left with a disconnected beam. A straightforward sorted time-window traversal or once-per-chart flattened/indexed view is enough; **do not iterate all notes/rests in every frame** and don't add a per-measure GPU cache.
- Draw measure lines from **`ParsedChart.MeasureLines`**, including irregular bars and tempo changes, rather than estimating bar duration. `NotationChartBuilder` already treats the last measure line as a **terminal boundary** (`measureCount = MeasureLines.Count - 1`): render only indices `0..Count-2` (the start of each compiled measure); **skip index `Count-1`**, the terminal marker, even if it falls inside the visible window.

## Public contract for HPA-411

Construct/initialize one renderer for a prepared chart, reuse it across frames, and provide a draw operation given `nowMs` and current positive scroll-speed setting. HPA-411 will build `NotationChart` only after chart finalization, select the active mode at run start, and handle missing glyph/font resources. Keep the renderer's external surface small; exposing an optional per-head colour selector later is preferable to building judgement or event subscriptions into this PR.

Do not extend `NotationChartBuilder` or change `Note.Id` handling here. Any unfinalized/invalid chart handling at mode activation belongs to HPA-411.

## Acceptance and verification

1. Deterministic GPU-free tests cover x at past/hit/future times, multiple speed settings, y for channels and ledger positions, glyph variant selection, chords and rests.
2. Tests cover eighth/16th/32nd/64th stems, ordinary and secondary/partial beams, isolated flags, triplet grouping, no beams across rests/barlines/tuplets, and odd-length measures. Pin the stem's top at `highestStaffStep + 7`: C5/step 5 reaches step 12, while B5/step 11 reaches step 18.
3. Tests prove measure boundaries align with compiled `MeasureLines.TimeMs`, including a variable-length bar and a BPM change. Assert the first `Count-1` measure lines are eligible and the terminal line is never drawn. Pin the table's staff/clip geometry; both culling edges retain complete visible decorations without drawing into the key column.
4. One manual desktop specimen using real `IFont.DrawString` and the Bravura font demonstrates legible heads, stems, rests, beams, barlines and tuplets in 1280x720. **Preserve a screenshot durably** (attach to the GitHub PR, or commit a small image under `docs/superpowers/evidence/`) and summarize the inspected cases in the PR body; a transient artifact link is insufficient. The specimen harness may be temporary and must not ship as a new stage.
5. Focused Mac/Windows test suites, Mac game build and Windows CI pass. Existing lane display and song judgement are unchanged; there is deliberately no Tab config toggle or in-game renderer invocation until HPA-411.

## Non-goals

No chart-core edits; no note/score/gameplay update loops; no configurable notation engraving system, arbitrary time signatures/time-signature glyphs, curved beams, persistent layout cache, runtime font atlas fallback, new project/dependency, mode telemetry or HUD integration. If a specific engraving detail would exceed three engineer days, retain readable basic notation and document the follow-up instead of expanding this ticket.
