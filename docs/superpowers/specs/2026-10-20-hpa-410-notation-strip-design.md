# HPA-410 — Scrolling Drum Notation Strip (Design)

**Issue:** https://linear.app/cwchanap/issue/HPA-410/tab-mode-scrolling-notation-strip-renderer  
**Parent:** HPA-394 music tab mode  
**Delivery:** One implementation PR, continuing this docs draft after approval; <=3 engineer days.  
**Prerequisites:** HPA-408 (notation core, #195) and HPA-409 (Bravura font, #199), both merged.

## Objective and ownership

Draw one full-width five-line drum staff that scrolls right-to-left past a fixed hit marker. Consume existing `NotationChart`, `NotationGlyphs`, and `ParsedChart.MeasureLines` without modifying chart parsing, note timing or scoring. Keep **one GPU-free layout** and **one thin MonoGame renderer**.

This ticket **does not** wire the renderer into `PerformanceStage`, add config/mode selection, judgement tint, pad feedback or telemetry (HPA-411); nor does it rearrange the tab HUD (HPA-509). Do not introduce VexFlow, shaders, render targets, extra font families or new graphics abstractions.

## Verified reuse points

- `Lib/Song/Notation/NotationModel.cs` has `NotationChart.Measures`, entry `StartTick`, `DurationTicks`, final `TimeMs`, chord heads and `NotationTuplet`. `DrumStaffMapping.GetPosition(channel)` already determines staff steps/shapes. Do not create another channel map.
- `Lib/Song/Notation/NotationGlyphs.cs` supplies `FromTicks`, `Notehead`, `Rest`, `Flag`, `ToChar`, `GetMetrics`, `IsComplete`; glyph metrics/anchors are in staff spaces. The HPA-409 baseline measurements were at **40pt** and must be remeasured if the baked size changes.
- `Lib/UI/Layout/PerformanceUILayout.cs` is the home for the new `NotationStrip` constants. `NoteBaseScrollPixelsPerMs = 37.5*286/60000 = 0.17875` is the **lane** base; use a distinct notation base to avoid dense-note collisions, while honoring the existing user speed percentage.
- `PerformanceStage.OnDraw` uses `SpriteSortMode.BackToFront`. Larger depth is farther back; call order alone does not control layering. Use nine-argument `IFont.DrawString` with explicit depth, plus `SpriteBatch.Draw` with depth.
- `ManagedFont` maps every Bravura request to its one baked asset, currently **40pt**; `LoadFont` can silently substitute another family. Validate `NotationGlyphs.IsComplete(font.SupportsCharacter)` and release the loaded resource on failure.
- `ParsedChart.MeasureLines` stores each compiled measure start followed by one terminal boundary. Measure `i` corresponds to `[MeasureLines[i].TimeMs, MeasureLines[i+1].TimeMs)`; `NotationChartBuilder` already enforces `Count-1`. The measures suffice for visibility indexing, unlike `ChartManager.GetActiveNotes`, which omits rests.

## Self-contained layout constants (initial contract)

These are **chosen implementation defaults**, not claims of pixel-perfect measurement from an external mockup.

| Element | Value |
| --- | --- |
| Screen and strip | virtual 1280×720, strip x=0..1280 and y=290..580 |
| Column and visible ink | opaque key label column x=0..195; labels right-aligned at x=174; moving ink begins at x=195 |
| Hit marker | fixed x=220; 2px wide |
| Five staff lines | E4 y=510, G4=490, B4=470, D5=450, F5=430 (20px/staff space, 10px/diatonic step) |
| Thicknesses | staff/barlines 1px; up-stems 2px; flat beams 5px, 8px between beam levels |
| Stem top | `highestHead.StaffStep + 7` diatonic steps (3.5 staff spaces beyond highest head), with start from lowest head's Bravura `StemUpSE`; whole notes/rests have no stems |
| Colours | background `#10151D`, staff/bar `#718096`, notation `#F2F4F8`, labels `#CAD3DE`, marker `#F0C15A` |
| Depths (BackToFront) | background **0.95**, staff/bars **0.80**, hit marker **0.72**, stems/beams **0.65**, glyphs/rests/tuplets **0.55**, key mask **0.40**, labels **0.30** |
| Scroll speed | `NotationStrip.ScrollPixelsPerMs = 0.38` at user 100%; scale by user `speedPercent/100` and reject nonpositive speeds |
| Fonts | one `Bravura-Notation` font targeted at native ~20px/staff-space, initially **60pt**; bundled `ShareTechMono-14` for ~10px text labels |

### Density and native glyph scale decisions

A black notehead is 1.18 staff spaces wide: `1.18 × 20 = 23.6px`. At 160 BPM, consecutive 16ths are 93.75ms apart. Using the lane base gives ~16.8px gaps (overlap); **0.38px/ms gives 35.6px**, meeting the acceptance rule of at least 1.5 black-head widths at 160 BPM / user speed 100%. The hit marker to right edge is 1060px, so this gives ~2.79 seconds (~1.9 bars at 160 BPM). At user speed 50%, 16ths will overlap (~17.8px)—the player has deliberately reduced speed; do not time-warp.

The baked Bravura 40pt asset measured ~13.4–13.65px/staff-space. Upscaling a SpriteFont by ~1.5 can blur fine notation strokes. **Task 0 must verify a single baked 60pt asset** (`Content/Bravura-Notation.spritefont`, `ManagedFont` resolved-size mapping/comments and existing mapping tests updated in this same PR), then calibrate pixels/staff-space and the SMuFL origin in a real rendering specimen. Draw at **scale 1** when the native result matches ~20px. If the platform's real metrics differ, make **one global font-size/geometry decision** from the specimen before locking tests; do not add multiple assets or hand-tuned per-glyph offsets.

## Pure engraving contract

Implement `Lib/Stage/Performance/NotationStripLayout.cs` with immutable chart input and output containing only the minimal glyph and line placements. Keep geometry CPU-only; no `GraphicsDevice`, `IFont`, `SpriteBatch`, owned resources or judgement state.

- **Clock/x:** geometry is anchored as (`TimeMs`, pixel `dx`, y); a beam spanning two onsets may carry two time anchors. Map screen x at projection/draw time using `ToScreenX(t, nowMs, speedPercent) = 220 + (t - nowMs) × (0.38 × speedPercent/100)`. This single pure helper owns x/speed testing; engraving itself is independent of frame clock and speed.
- **Visibility:** use binary search on sorted measure end times to locate the first measure intersecting the left-edge time, then walk measures until starts exceed the right-edge time. Engrave complete selected measures; beams and tuplets stay intact naturally because they do not cross measure boundaries. No chart-wide flattened index, special look-behind, group-culling flags or scissor batch reopen. Optionally omit fully off-screen primitives; anything scrolling left of x=195 is hidden by the front key-column mask.
- **Barlines:** each selected measure draws only its start marker, `MeasureLines[measure.Index]`; `MeasureLines[^1]` is solely the last interval's end and is never drawn. Barlines use compiled `TimeMs`, never synthesized BPM.
- **Heads/chords:** preserve all notehead shapes at their existing `StaffStep`; one shared up-stem per chord, anchored using `NotationGlyphs.GetMetrics(...).StemUpSE`; top at `highestStaffStep + 7`, not a fixed C6. Draw necessary ledger lines. Overlapping heads are acceptable.
- **Rests:** select the glyph via `NotationGlyphs.Rest(FromTicks(...))`; **whole rest hangs from the D5 line (staff step 6)**, while all other rests anchor at **B4 (step 4)**. Respect Bravura bbox/origin rather than centering every glyph geometrically.
- **Tuplets:** `NotationTuplet` is an **independent beaming group**, even when its 3-note span crosses a 48-tick beat; `NotationQuantizer.FindTriplet` does not constrain start to beats. Use `FromTicks(SlotTicks*3/2)` for displayed value and place one `3` over its group.
- **Ordinary beaming:** consecutive *non-tuplet* short note entries beam within the same 48-tick notation beat; never across rests or bars. Draw shared primary and secondary/partial beams for eighth through 64th values; draw a flag when an eligible partner is absent. Flat beams only.
- **Labels:** define one small channel→label list for LC/CY/HH/RD/HT/LT/SN/FT/BD/LP, resolving each y using `DrumStaffMapping.GetPosition` (HH covers open/closed, BD covers both bass variants). The list is not a new staff-step table.

## Thin draw and ownership contract

`Lib/Stage/Performance/NotationStripRenderer.cs` owns Bravura and `ShareTechMono-14` fonts plus an existing-style white pixel texture; it validates Bravura coverage, disposes/releases resources on success and failure, projects `TimeMs` anchors into x, and draws in the current `BackToFront` batch with distinct explicit depths. Mask the key column in **front of** moving notation and place labels **in front of** the mask. Do not let default 4-argument `IFont.DrawString` depth 0 bypass the mask. The public seam for HPA-411 is constructor/ready/draw(`SpriteBatch, nowMs, speedPercent`)/dispose. HPA-411 owns activation, invalid chart/font fallback and judgement colouring.

## Acceptance

1. **Upfront Task 0 specimen**, in a `BackToFront` batch using real `IFont.DrawString`: staff + labels, 160 BPM bar of 16ths at 100% **and** 50%, whole/short rests, a triplet crossing a beat; validate sharpness at native font size, layering/mask and origin. Fix one global geometry/size assumption before further tests.
2. GPU-free tests: x at hit/past/future and 50/100/200% speed, staff/label steps, whole rest step 6 vs other rests step 4, chord head positions, stem `highestStep+7`, 32nd/64th shapes, ledger lines.
3. Musical tests: mixed beams, isolated flags, ordinary beat/rest/bar boundaries, **across-beat tuplet kept whole**, variable-length bars, BPM changes, terminal boundary not rendered.
4. Visibility tests: measure-window binary search, opaque key mask/label depth ordering, no costly chart-wide traversal; no special tests for partial-group culling.
5. **Final specimen screenshot retained durably** (GitHub PR attachment or small committed `docs/superpowers/evidence/` image), linked in the PR with observations. Do not ship the temporary specimen harness/stage.
6. Full Mac tests and build; Windows tests/build CI, with nonzero new test discovery. No user-visible gameplay changes.

## Non-goals

No stage integration, mode/config change, scoring/input/HUD/editing/timing change, generic engraving framework, dynamic beam angles, separate font variants, GPU cache, custom shaders, scissor state reopening or second drawing backend. Keep this at one PR and <=3 engineer days; defer optional polish rather than expanding scope.

## Task 0 measured results (2026-10-20)

Measured from a real-GPU specimen: a temporary DesktopGL `Game` (not committed) drawing through the nine-arg `IFont.DrawString` in a `SpriteSortMode.BackToFront` batch into a 1280x720 `RenderTarget2D`, saved to `.superpowers/sdd/2026-10-20-hpa-410-notation-strip/task0-specimen.png` (kept for Task 3 reference; harness removed).

- **Bake size: 60pt confirmed, no geometry change.** Pixels/staff-space at scale 1 measured **20.00** from the clef bBox height (40px / 2.0 spaces) and **20.05** from the quarter rest (60px / 2.992 spaces); width-based checks gave 20.29/19.52 (±0.5px scan rounding). The 20px-space / 10px-step layout constants stand.
- **Origin/baseline rule:** the SpriteFont places glyphs in its line box, so the SMuFL baseline sits at `drawY + SpriteFont.LineSpacing / 2` — **161px at 60pt** (LineSpacing=322; measured 161.0 clef / 160.8 quarter rest). `NotationStripRenderer` must draw each glyph at `staffY - LineSpacing / 2` to put its origin on the staff position. Left bearing is 0 for glyphs whose bBoxSW.X is 0 (clef starts exactly at drawX).
- **Density:** 16ths at 160 BPM / user 100% render at 35.63px spacing (93.75ms x 0.38), satisfying the >= 35.4px rule; visually clean. At 50% (17.8px) noteheads overlap as accepted — no time warping.
- **Mask/depth observation:** opaque `#2A3341` key-column mask at depth 0.40 fully hides staff lines and ink at x < 195; lane labels (ShareTechMono-14, depth 0.30) draw in front of the mask and stay legible. BackToFront with the contracted depths behaved exactly as specified.
- **Sharpness:** Bravura glyphs are crisp at native 60pt scale 1 (no upscale blur). ShareTechMono-14 downscaled to 0.7 (~10px labels) remains sharp. Lane labels are 10px apart on the staff grid, so adjacent ~10px labels just touch — acceptable; revisit only in optional Task 3 polish.
