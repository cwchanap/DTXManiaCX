# HPA-410 — Scrolling Notation Strip Renderer (Implementation Plan)

**Companion spec:** [HPA-410 design](../specs/2026-10-20-hpa-410-notation-strip-design.md)  
**Issue:** https://linear.app/cwchanap/issue/HPA-410/tab-mode-scrolling-notation-strip-renderer  
**Delivery:** Implement in **this existing docs-draft PR** after review; no second PR, <=3 engineer days. Documentation-only until implementation starts.

## Baseline to reuse (inspect the actual code before changes)

- HPA-408 (#195): `Lib/Song/Notation/NotationModel.cs`, `NotationChartBuilder`, `NotationQuantizer`, `DrumStaffMapping`.
- HPA-409 (#199): `NotationGlyphs`, `Content/Bravura-Notation.spritefont` (currently **40pt**), `ManagedFont` family mapping, pinned Bravura anchor metadata and the 40pt historical calibration.
- `PerformanceUILayout` lives at `DTXMania.Game/Lib/UI/Layout/PerformanceUILayout.cs`; the existing stage uses 1280×720 virtual pixels and **`SpriteSortMode.BackToFront`** (larger `layerDepth` draws farther back).
- `ParsedChart.MeasureLines` and `NotationChart.Measures` form the aligned measure intervals, with the last measure line a **terminal bound**. `NoteRenderer` has scroll/speed validation and white-pixel drawing precedents, but its `GetActiveNotes` index is not a notation source.
- Reuse `IResourceManager.LoadFont`, the nine-argument `IFont.DrawString`, `NotationGlyphs.IsComplete(font.SupportsCharacter)`, and `ShareTechMono-14` for the key-label font.
- Do not retarget SDK/frameworks under this issue. Run tests using the current main-branch project settings.

## Task 0 — Real-GPU specimen before geometry tests (up to ~0.5 day)

1. Change the **single** `Content/Bravura-Notation.spritefont` bake from 40pt to initial **60pt**. Update `ManagedFont.GetBestSpriteFontAssetName`'s Bravura resolved size, its descriptive 40pt comments, and `DTXMania.Test/Resources/ManagedFontAssetMappingTests.cs`. Do not add a second Bravura asset, a runtime size selector or new font abstraction.
2. Using a temporary desktop graphics/dev path (the existing headless GPU snapshot tests cannot load `.xnb` content), draw **through real `IFont.DrawString` in a `BackToFront` SpriteBatch**, not Deferred. Show five staff lines, ShareTechMono labels, a bar of 16ths at 160 BPM at user speed 100% and 50%, whole and shorter rests, a triplet crossing a beat, and a colored opaque left mask.
3. Verify effective Bravura pixels/staff space and global origin/baseline at native drawing scale 1. The 60pt asset should produce ~20px/staff space; if not, adjust *one global* bake size or staff-space constant and record the measured result in these docs **before Task 1**. Verify glyph sharpness, depth mask order, y positions and the 100%-speed density rule: 16ths at 160 BPM are at least `1.5 × 23.6 = 35.4px` apart (initial `0.38px/ms` gives ~35.6px). At 50%, overlapping is an expected trade-off, not grounds for time warping.
4. Retain the final verification screenshot **after Task 3**, not necessarily the temporary Task 0 harness; record the concrete size and baseline decision immediately in the PR discussion. Remove temporary harness from the implementation diff.

**Exit:** pin one working font asset and renderer constants from actual draw results, not unit-test assumptions. If the native glyph size cannot be made usable quickly, keep one font path and document the minimal single-font fallback; do not start a second rendering engine.

## Task 1 — Pure layout, window by measure, test x separately (~0.75 day)

1. Add `PerformanceUILayout.NotationStrip` nested constants from the design table: strip y=290..580, E4 baseline y=510, 20px staff spaces, key mask cutoff x=195, hit x=220, distinct depth values, colors, `ScrollPixelsPerMs=0.38` and final native font size measured in Task 0.
2. Add `Lib/Stage/Performance/NotationStripLayout.cs` with one pure `ToScreenX(timeMs, nowMs, speedPercent)` formula. Reject nonpositive speed like `NoteRenderer.SetScrollSpeed`. Keep engraving positions as `(TimeMs, dxPx, yPx)` anchors independent of `nowMs` and speed; a beam spanning notes retains both endpoint times.
3. Find visible **measures** by binary search over existing ordered `MeasureLines[i+1].TimeMs`; traverse up to the first measure whose start lies beyond the visible right edge. For each selected measure, produce its whole group of note/rest/beam candidates, rather than iterating every note every frame. No flattened chart index, look-behind or partial-group culling state. Front opaque key column masks ink at x<195; screen bounds cover the right edge. Optional per-primitive skip only if entire projected primitive is offscreen.
4. Draw barlines from each selected `MeasureLines[measure.Index].TimeMs`; terminal `MeasureLines[^1]` is only a measure **end bound** (no independent special-case barline pass).
5. Focused headless tests: normal/half/double speed and hit/past/future, staff and label step coordinates, positive speed check, time-based barline alignment with BPM changes and variable-length bars, visible measure start/end selection and terminal boundary not drawn.

**Exit:** a small GPU-free, measure-indexed placement contract; don't open a test-only graphics device.

## Task 2 — Focused engraving tests (~0.75–1 day)

1. Use HPA-408 heads, shapes, quantized durations and tuplets; select glyphs with HPA-409 `FromTicks/Notehead/Rest/Flag`. Chords share one up-stem: begin at lowest head's Bravura `StemUpSE`; top = `highestHead.StaffStep + 7` diatonic steps. Pin low/middle/high and mixed chord tests; whole notes have no stem.
2. Map resting position by value: **whole rest on D5 line / step 6**, all other rests at **B4 / step 4**, using HPA-409 bbox offsets. Test empty-bar whole rests and shorter rests.
3. For ordinary (non-tuplet) entries, group adjacent short **notes** inside a 48-tick beat, without crossing bar/rest boundaries. Support primary and secondary/partial beams through 64ths and isolated flags; avoid sloped beams or VexFlow.
4. For each `NotationTuplet`, disregard the ordinary beat boundary: preserve the whole group even if its start tick is 24 and it crosses tick 48. Derive display `NoteValue` from `SlotTicks * 3 / 2`; draw one tuplet `3`. Add a cross-beat triplet regression.
5. Add short ledger lines when staff steps fall outside 0..8; derive key-column label y from `DrumStaffMapping.GetPosition` and a small label/channel lookup (not a duplicated staff mapping). Overlapping heads remain allowed.

**Exit:** independent, deterministic engraving tests for representative rhythms and grouped durations on Mac + Windows.

## Task 3 — Thin BackToFront renderer, ownership and final evidence (~0.5–0.75 day)

1. Add `Lib/Stage/Performance/NotationStripRenderer.cs`: load the **one** Bravura font at the chosen native size, verify all symbols with `NotationGlyphs.IsComplete(font.SupportsCharacter)`, acquire bundled `ShareTechMono-14` label font and an existing-style 1×1 texture. On any failure release everything already acquired; follow `NoteRenderer`/resource manager ownership patterns.
2. Use the existing `SpriteBatch` and explicit depths for background **0.95**, staff/bars **0.80**, hit marker **0.72**, beams/stems **0.65**, Bravura glyphs **0.55**, left opaque column mask **0.40** and labels **0.30**. **Always** use the nine-argument `IFont.DrawString` for glyphs and labels so none default to depth 0. Draw rectangle primitives using depth-aware `SpriteBatch.Draw`.
3. Expose only constructor / `IsReady` / `Draw(SpriteBatch, nowMs, speedPercent)` / `Dispose` for HPA-411. No `PerformanceStage` or config changes; HPA-411 later handles activation, chart readiness, judgement tint, fallback and E2E.
4. Re-run a final real-Bravura/ShareTechMono specimen **in a BackToFront batch** including the 160 BPM 16ths at 100% and 50%, whole and short rests, cross-beat triplets, stems, flags, clipping via key mask and correct font scale. **Retain screenshot durably** as a GitHub PR attachment or a small checked-in `docs/superpowers/evidence/` file. Link it and summarize pass/fail and final font measurements in the PR body. Remove temporary development harness.

**Exit:** renderer can be called by HPA-411 and is validated visually, without changing existing lanes.

## Commands and exit criteria

```bash
dotnet tool restore
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NotationStrip|FullyQualifiedName~NotationGlyphs|FullyQualifiedName~ManagedFontAssetMapping"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-incremental
```

Run Windows `DTXMania.Test.csproj` and Windows game build on CI. Verify the focused filter runs **nonzero** tests. Compile and inspect the font content asset on supported desktops: passing pure tests does not prove `IFont` real GPU rendering.

- [ ] Task 0 specimen locks one baked Bravura size, origin calibration, default density and depth composition before heavy tests
- [ ] No reliance on draw-call order; all Bravura and label draw calls have explicit layer depth
- [ ] Measure window and terminal boundary use current `NotationChart.Measures` / `MeasureLines`; no chart-wide per-frame scan
- [ ] Correct rest placement; tuplets that cross a beat stay in the same group
- [ ] Chords, ledger lines, flags and beams use existing HPA-408/409 contracts
- [ ] Clean resource lifetime on successful and invalid/missing font load
- [ ] Final screenshot is retained, linked and explained in PR
- [ ] Mac + Windows test/build verification; no lane gameplay integration or new framework

## Keep out of scope

No new config, UI/HUD, E2E mode, judgement integration, pixel-art work, cache framework, scissor batch reopening, more than one font asset/backend, or large engine refactor. One ticket, one PR, <=3 engineer days.
