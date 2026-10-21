# HPA-410 — Scrolling Notation Strip Renderer (Implementation Plan)

**Companion design:** [HPA-410 design](../specs/2026-10-20-hpa-410-notation-strip-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-410/tab-mode-scrolling-notation-strip-renderer  
**Delivery:** Continue **this same draft design PR** for implementation after review. One issue = one implementation PR; <=3 engineer days. Docs only at initial review.

## Current baseline (inspect before editing)

- HPA-408 merged as [#195](https://github.com/cwchanap/DTXManiaCX/pull/195). Its immutable notation records, `TimeMs`, source-`Note` references, `DrumStaffMapping` and quantizer already exist in `DTXMania.Game/Lib/Song/Notation/`.
- HPA-409 merged as [#199](https://github.com/cwchanap/DTXManiaCX/pull/199). Use `NotationGlyphs` and the actual Bravura-Notation SpriteFont; the release-specific glyph bboxes, stem anchors and baseline calibration are in its spec. Do **not** introduce a second glyph table, font parser or graphics backend.
- `PerformanceUILayout` is at `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`. `NoteRenderer` is at `DTXMania.Game/Lib/Stage/Performance/NoteRenderer.cs`; its velocity formula and 1x1 rectangle rendering are precedents.
- `ParsedChart.MeasureLines` and `NotationEntry.TimeMs` are already finalized timing sources. A chart may have many rest-only measures and note entries after the current time; index/cull the layout window.
- The repository currently targets its main-branch .NET SDK/framework choice (now net10.0 in game projects); **do not retarget** it under HPA-410.

## Task 1 — Pure frame layout + tests (roughly 1 day)

1. Add a small `PerformanceUILayout.NotationStrip` nested class for 1280x720 constants: `HitLineX=220`, key-column/content cutoff, staff bottom and spacing, line/stem/beam thickness and neutral colours. Keep font calibration expressed in staff spaces plus HPA-409's one global baseline/px-scale.
2. Add `Lib/Stage/Performance/NotationStripLayout.cs`. Consume a prepared `NotationChart` and the existing list of `MeasureLine` boundaries. Compute `x=220+(entry.TimeMs-nowMs)*PerformanceUILayout.NoteBaseScrollPixelsPerMs*(speedPercent/100.0)`, `y` from `Position.StaffStep`; reject nonpositive speeds as `NoteRenderer.SetScrollSpeed` does. Use HPA-409's `FromTicks/Notehead/Rest/Flag/GetMetrics`, including tuplets' `SlotTicks*3/2` displayed duration.
3. Return only the minimal typed placements needed to render: glyph + position, staff/ledger/barline/hit-line/stem/beam segments, and label positions. Names/types are implementation choices; avoid generic scene graphs or a drawing command framework. Preserve chord heads and group them under one rhythmic stem. The layout calculation does not touch GPU, fonts or mutable judgement data.
4. Use ordered entry times (optionally a single flattened index prepared with the chart) to find the visible span without scanning the entire chart each frame. Include a small margin for whole-head width, flags and beams; cap at the musical-content left boundary. Barlines use `MeasureLine.TimeMs` rather than synthetic BPM math.
5. Tests in `DTXMania.Test/Stage/Performance/NotationStripLayoutTests.cs` (or closest existing stage test folder): past/future/hit x, x1.0/x2.0/x0.5 speed, 12-channel positions via HPA-408 mapping, notehead shape, ledger lines, chords, rest-only bars, speed validation, measure-line timing, both edge windows. Prove at least one chart where a BPM/measure-length change would break a naive constant-time bar layout.

**Exit:** deterministic placement assertions pass on Mac without opening a graphics window.

## Task 2 — Engraving rules, bounded rendering primitives + tests (roughly 1 day)

1. Add flat stems and rhythm grouping to the **same** layout component. Treat chords as one onset and create one shared up-stem. Beam only adjacent note entries within the same notation quarter beat (48 ticks), bar and tuplet group. Never beam across a rest. For a single short entry, select the appropriate HPA-409 up-flag.
2. For mixed eighths and 16ths, draw shared primary beams plus the secondary short/partial beam on the shorter entry. Apply the same bounded level logic through 32nd/64th values, without a general VexFlow engraver or arbitrary beam slopes. Tuplets get a single `3` centered above their existing `NotationTuplet` range; triplet durations display via the HPA-409 documented base-tick rule.
3. Implement basic staff lines, ledger lines, measure lines and the vertical hit marker as pixel rectangles. Place heads/rests/flags and attach stems with `NotationGlyphs.GetMetrics` anchor data, adjusting only the **global** point-size/baseline calibration recorded by HPA-409. Key labels are fixed at the mapped staff steps on the left, not inferred from gameplay lane IDs.
4. Add focused cases for: one isolated eighth -> flag; two eighths -> one beam; eighth+16th+16th -> primary with a secondary pair; notes separated by a rest; beat/bar boundary; a 3-note tuplet; 32nd/64th flag/beam levels; culling midway through a beam/tuplet; no dangling decoration or label overlap.

**Exit:** the renderer has a simple, inspectable set of correctly positioned visual primitives for representative measures.

## Task 3 — Thin SpriteBatch renderer and verification (roughly 0.5–1 day)

1. Add `Lib/Stage/Performance/NotationStripRenderer.cs`. Acquire `IFont` via `IResourceManager.LoadFont("Bravura-Notation",40)`; validate with `NotationGlyphs.IsComplete(font.SupportsCharacter)` to reject silent default-font substitution. Reuse existing one-pixel/white texture creation and reference-counted release/disposal patterns; don't leak the font when validation fails.
2. Draw generated primitives using the stage's existing `SpriteBatch` conventions and `IFont.DrawString`, not direct `SpriteFont.DrawString`. Draw lines with `SpriteBatch.Draw`; avoid custom render targets/shaders. Draw consistently in virtual 1280x720 space. Ensure musical ink cannot overlap key labels when moving left of the fixed hit marker.
3. Expose a small constructor/ready/draw/dispose API suitable for HPA-411 to instantiate **after** its chart has been finalized and to call each `OnDraw`. Do **not** modify `PerformanceStage` or introduce config gates now. HPA-411 will handle selection, lifecycle, runtime font-failure fallback, per-note judgement colours and E2E.
4. Run a one-off/manual 1280x720 specimen using the compiled Bravura glyphs through `IFont.DrawString`; inspect screen position, scaling, all cymbal/open-head variants, rests, flags, beaming, tuplet number and key-column clipping. Capture a screenshot or concise pass/fail evidence in PR description; remove any temporary harness/test stage before implementation merge.

**Exit:** production renderer compiles, the glyphs draw correctly on a real MonoGame desktop context, and it can be called by the next ticket without changing existing lanes.

## Verification

Suggested commands from repository root (use installed SDK and existing tool restore):

```bash
dotnet tool restore
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NotationStrip|FullyQualifiedName~NotationGlyphs"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-incremental
```

Also run the Windows `DTXMania.Test.csproj` and Windows game build in CI. Check **nonzero** test discovery and confirm the new focused tests actually execute. A GPU-free test passing is not proof that SpriteFont actually draws; capture the manual visual verification separately.

## PR acceptance checklist

- [ ] Fixed x=220 hit line; x coordinates are time-derived with the **existing** scroll-speed scale
- [ ] Five staff lines and labels; mapped notes/chords, needed ledger lines, rests, measure boundaries and hit marker
- [ ] Stem-up, flags, primary/secondary/partial beams and tuplets for the supported rhythmic values
- [ ] Geometry from existing HPA-408 model and HPA-409 Bravura glyph metrics (no duplicated mapping or timing calculations)
- [ ] Left-column/left-and-right-edge culling and cheap bounded per-frame traversal
- [ ] Pure layout tests pass on Mac + Windows; Mac and Windows game builds/CI pass
- [ ] Manual IFont-rendered specimen checked and recorded
- [ ] Clean resource lifetime on successful and failed font validation
- [ ] No gameplay/config/HUD/judgement/parser/API architecture changes; one implementation PR only

## Agent guardrails

Implement the minimal visible strip, rather than a generic music notation library. Do not start HPA-411/HPA-509 early; no new animation assets or SFX; no extra caching until profiling shows a need; no separate spike PR. If you find a real incompatibility in glyph anchors or timing, record it in this PR and revise the spec before enlarging implementation scope. Avoid unrelated cleanups.
