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

- Virtual screen width **0..1280**, fixed hit line **x=220**. Reserve a left key-name column ending near x=190; musical content is bounded on the left so scrolling glyphs do not overdraw the labels. At 100% scroll speed, `pxPerMs ~ 0.17875`, giving ~5.9 s of content to the right of the hit line.
- Note onset x: `HitLineX + (entry.TimeMs - nowMs) * (NoteBaseScrollPixelsPerMs * scrollSpeedPercent/100.0)`. The same clock and speed move notes, rests, beams, tuplet markers and barlines. BPM/measure length changes are **already represented** by compiled `TimeMs`; do not add another timing transform.
- Five staff lines correspond to E4/G4/B4/D5/F5 (`StaffStep = 0,2,4,6,8`), with one diatonic step = half a staff space. Place a head from its existing `DrumStaffPosition.StaffStep`; render short ledger lines for outside-staff notes as appropriate. Draw key names `LC, CY, HH, RD, HT, LT, SN, FT, BD, LP` at the corresponding mapped staff steps; key names are *visual labels*, not interactive drum pads.
- Use the approved mockup C's strip footprint; keep staff baseline, line/beam/stem thickness, left content boundary, text font/size and colours centralized. Avoid moving the HUD: HPA-509 will position gauge, combo, score and stats.
- White/neutral head colours for now. No judgement colours/fade logic in HPA-410; HPA-411 will supply per-head visual state from the stored source notes.
- Draw order: strip/background/staff and key column; measure lines and hit line; stems/beams/ledger lines; glyphs (heads, rests, flags, tuplet 3). Keep drawing operations with the existing `SpriteBatch` and a 1x1 texture; no custom shaders or render targets.

## Geometry, rhythm and visibility

Split the implementation into **one pure layout component** and **one thin MonoGame draw component**:

- `Lib/Stage/Performance/NotationStripLayout.cs`: CPU-only layout math. Inputs: compiled notation, compiled measure-line boundaries, current `nowMs`, positive scroll speed and the fixed strip geometry. Output: a small set of typed glyph placements and straight segments/rectangles (plus labels), with x/y coordinates ready for drawing. No `GraphicsDevice`, `SpriteBatch`, `IFont`, resource acquisition, timing-map mutation or judgement state.
- `Lib/Stage/Performance/NotationStripRenderer.cs`: owns the validated Bravura font and white-pixel resource following existing lifetimes, draws layout output through `IFont.DrawString` and `SpriteBatch.Draw`, and exposes a simple readiness/lifetime contract for HPA-411. Don't add a generic render graph, cross-platform renderer interface or multi-font fallback pipeline.
- Centre noteheads on the onset and mapped staff step using `NotationGlyphs.GetMetrics`/the pinned global font calibration. One chord gets **one rhythmic stem** and one x position, but keeps all mapped head glyphs (including overlapping/open hi-hat variants). Stems use the Bravura stem-attachment metadata rather than uniform guessed notehead width.
- For non-tuplet entries, use `NotationGlyphs.FromTicks(entry.DurationTicks)`. For entries inside `NotationTuplet`, derive displayed base ticks as `SlotTicks * 3 / 2`, then use `FromTicks` (as HPA-409 requires). Draw rests at staff centre with their correct rest glyph; no stem on whole notes/rests.
- Beam consecutive short **note** entries in the same 48-tick notation beat, not across rests, bar boundaries or tuplet group boundaries. One beam per shared duration level (8th through 64th), with partial/secondary beams for mixed 8th/16th groupings. Where no eligible partner exists, draw a normal flag glyph. Each tuplet remains a coherent independent group with its `3` above; do not depend on VexFlow or recompute tuplets. Keep beam slope flat.
- Visibility bounds follow the actual strip x interval plus glyph/beam margin. Include a small look-behind past the hit line but prevent drawing over the key-label column. Cull whole grouped decorations consistently at both edges, so a surviving head is not left with a disconnected beam. A straightforward sorted time-window traversal or once-per-chart flattened/indexed view is enough; **do not iterate all notes/rests in every frame** and don't add a per-measure GPU cache.
- Draw measure lines from **`ParsedChart.MeasureLines`**, including irregular bars and tempo changes, rather than estimating bar duration. Exclude the terminal boundary only if it should not visibly mark the end.

## Public contract for HPA-411

Construct/initialize one renderer for a prepared chart, reuse it across frames, and provide a draw operation given `nowMs` and current positive scroll-speed setting. HPA-411 will build `NotationChart` only after chart finalization, select the active mode at run start, and handle missing glyph/font resources. Keep the renderer's external surface small; exposing an optional per-head colour selector later is preferable to building judgement or event subscriptions into this PR.

Do not extend `NotationChartBuilder` or change `Note.Id` handling here. Any unfinalized/invalid chart handling at mode activation belongs to HPA-411.

## Acceptance and verification

1. Deterministic GPU-free tests cover x at past/hit/future times, multiple speed settings, y for channels and ledger positions, glyph variant selection, chords and rests.
2. Tests cover eighth/16th/32nd/64th stems, ordinary and secondary/partial beams, isolated flags, triplet grouping, no beams across rests/barlines/tuplets, and odd-length measures.
3. Tests prove measure boundaries align with compiled `MeasureLines.TimeMs`, including a variable-length bar and a BPM change. Both culling edges retain complete visible decorations without drawing into the key column.
4. One manual desktop specimen using real `IFont.DrawString` and the Bravura font demonstrates legible heads, stems, rests, beams, barlines and tuplets in 1280x720. Record the result in the PR. The specimen harness may be temporary and must not ship as a new stage.
5. Focused Mac/Windows test suites, Mac game build and Windows CI pass. Existing lane display and song judgement are unchanged; there is deliberately no Tab config toggle or in-game renderer invocation until HPA-411.

## Non-goals

No chart-core edits; no note/score/gameplay update loops; no configurable notation engraving system, arbitrary time signatures/time-signature glyphs, curved beams, persistent layout cache, runtime font atlas fallback, new project/dependency, mode telemetry or HUD integration. If a specific engraving detail would exceed three engineer days, retain readable basic notation and document the follow-up instead of expanding this ticket.
