# Issue #215 — Result / SongSelect Visual Fixes (Implementation Plan)

**Design:** [Issue #215 design](../specs/2026-10-31-issue-215-result-songselect-visual-fixes-design.md)  
**Issue:** https://github.com/cwchanap/DTXManiaCX/issues/215  
**Delivery:** **one PR total**. Continue in this docs-only draft after design review; no second implementation PR. Target ~2 engineer days, maximum 3.

## Ground rules for implementing agent

Read the linked design and the actual current source first; rebase/compare if `main` moved since the documented `752a4fb3` baseline. Change **presentation only**. Avoid new asset creation, generic UI infrastructure, refactors, and score/gameplay logic. Update tests only where changed behavior invalidates their expectations. Keep scope to the following tasks.

## Task 0 — Record composition baseline (up to 0.25 day)

1. Confirm current 1280×720 Result and SongSelect composition in default NX skin and (if convenient) CXNeon, via existing GameApi/desktop screenshots. Baseline is **not** a substitute for screenshots at each task's exit.
2. Inspect real art metrics: NX `7_SkillPanel.png`, default Result `8_background.jpg` brightness at (30,108), CXNeon result placement, `5_BPM.png` **natural 187×67** at (32,275), status panel X=130/Y=350, difficulty header Y=368, CXNeon preview frame X=250..680/Y=34..347 and both skins' existing `5_preimage default.png`. The latter is intentionally bright on NX, dark on CXNeon.
3. Check maximum `PlaybackProfileFormatter` output width in the actual 16px UI font against the proposed 224px slot; decide a small local correction **before** changing code if it cannot fit.
4. Compare the two source paths to the legacy NX behavior: original result level badge + padded playing-skill text, and default-preview substitution on missing file. Do not change `SongBarRenderer` null/cache rules.

**Exit:** the four issues are traced, real drawn image bounds are recorded and the proposed coordinates are ready for a first implementation screenshot; no speculative grid move.

## Task 1 — Result fallback and theme-aware group (~0.5 day)

**Files**
- `DTXMania.Game/Lib/UI/Layout/ResultUILayout.cs`
- `DTXMania.Game/Lib/Stage/Result/ResultScreenRenderer.cs`
- `DTXMania.Game/Lib/Stage/ResultStage.cs` (pass borrowed existing pixel)
- `System/CXNeon/Theme.ini`
- `DTXMania.Test/Stage/ResultScreenRendererThemeLayoutTests.cs`
- `DTXMania.Test/Stage/Result/ResultScreenRendererTests.cs`

1. Change built-in `LevelPosition.Y` from 550 to 525 and `PlayingSkillPosition.Y` from 537 to 555, retaining X/font/format and existing position keys. NX originally positioned bitmap badge and padded skill **side by side**; stacking ordinary CX text is deliberate. Test glyph-bounds non-overlap against actual value-font line height, not merely `ΔY == 30`.
2. Set NX playback group origin to (30,108), with offsets (0,0)/(0,26)/(0,60)/(0,84) and existing guidance size 420x60. Implement one pure group resolver using `Result.PlaybackInfoX/Y` independent `GetInt` values; use it at all four `DrawModelText` sites.
3. Add `Result.PlaybackInfoX=830` / `Result.PlaybackInfoY=310` to CXNeon, retaining its current placement. Pass `ResultStage`'s existing `_whitePixel` **non-owningly** to the renderer and draw a translucent dark group rectangle **before the group text**. Size to visible rows and to the 420x60 guidance only when present; clear skill panel Y=260. No new texture allocation, custom font shadows, independent row keys or theme colour override unless visual evidence requires it.
4. Update pure resolver and renderer-mock tests (NX defaults, themed X/Y independently, guidance presence, status colours, text coordinates, backing bounds/ownership). Preserve old stage reveal and resource lifetime semantics.
5. **Screenshot exit gate — NX 1280×720 Result:** inspect real level/play separation and text contrast on bright truss, then adjust the **same constants** if needed. Include failed-save guidance when practical; do not defer visual contrast to Task 4.

**Exit:** NX Result screenshot is legible; theme override preserves CXNeon coordinates, to be confirmed in Task 4.

## Task 2 — SongSelect label compositing (~0.25–0.5 day)

**Files**
- `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs`
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- focused `DTXMania.Test/UI/SongSelectionUILayoutTests.cs` / relevant SongSelection stage tests

1. Set profile label bounds to **(20,342,224,24)**. Keep formatter, config synchronization, current 16px font, colours and size reassertion.
2. Move `_mainPanel.AddChild(_playbackProfileLabel)` **after** song list/status/history/preview but **before** modal addition. This directly fixes the 70%-black full-screen song-list wash over the label and keeps modal topmost.
3. Test drawn geometry using the **native 5_BPM.png height 67**, e.g. a named `BPMSection.SkinTextureHeight = 67` (or equivalent explicit asset metric), never `BPMSection.Height=50`. Require profile top >= (275+67), right < preview frame X=250, bottom <= difficulty header Y=368. The status background begins Y=350 but is near-transparent; verify no text drawn over its content.
4. Check full supported speed/pitch text against the real font width. Use a small local font/position adjustment if necessary, never truncate or change `PlaybackProfileFormatter`.
5. **Screenshot exit gate — NX 1280×720 SongSelect:** confirm full profile, BPM badge, status values and search modal. Tweak only local coordinates/font if needed; no grid/preview-frame changes.

**Exit:** NX profile is visibly readable and clear of drawn neighbouring art, with correct modal layering.

## Task 3 — Restore missing-jacket default preview (~0.25–0.5 day)

**Files**
- `DTXMania.Game/Lib/Song/Components/SongListDisplay.cs`
- `DTXMania.Test/UI/SongListDisplayLogicTests.cs` (and existing renderer tests as relevant)

1. Load optional `TexturePath.ResultDefaultPreview` in `SongListDisplay` alongside its skin bar textures (`ResourceExists` then `LoadTexture`). Maintain a **single shared resource-manager reference** and release it on reinitialization and disposal alongside the bar textures; do not introduce per-song caching.
2. At the existing selected/unselected 44×44 preview draw call, use `barInfo.PreviewImage ?? (barInfo.BarType == BarType.Score ? _defaultBarPreview : null)`. For the basic/fallback score path showing a skin bar, display the same shared fallback at the existing slot. Valid song art takes priority; folders/other bars do not receive it. Respect current opacity/lamp/title drawing.
3. Test default resource existence/missing asset, lifetime, real-preview precedence, and score-only fallback decisions. Missing `#PREIMAGE`, non-existent file or fast-scroll suppression should not trigger file loads or change `SongBarRenderer`'s null/ref-count contract. **No** rectangle helper, colour/theme key, dark overlay or new graphics.
4. **Screenshot exit gate — NX 1280×720 SongSelect:** capture selected/unselected bar with missing and real jackets. The NX fallback art is colorful; verify its small thumbnail is recognizably intentional rather than visually white. Record any remaining issue instead of inventing an art task.

**Exit:** missing-jacket score bars use the bundled skin's own default image and actual jackets retain priority.

## Task 4 — CXNeon, scaled-window evidence and CI (~0.5 day)

1. With Tasks 1–3 already visually checked on NX, perform the remaining **CXNeon 1280×720** pass and **one second window size** for each touched screen. Confirm Result group stays at original (830,310), shared translucent backing does not spoil the CXNeon style, the non-failed/failed-save guidance works, the new CXNeon dark default jacket reads well, and the SongSelect profile does not collide with the preview frame/BPM/status. Check search modal precedence.
2. Attach/link a compact **before/after** screenshot set for NX and CXNeon (including missing/valid jacket and normal/failed Result) and note observed differences. If only the shared backdrop is ugly on CXNeon, add the smallest skin colour override **after** this observation; avoid a new theme facility.
3. Run focused and full Mac tests, Mac build and Windows tests/build in CI. Verify focused filters discover **more than zero** tests and summarize results. Do not claim screenshots or tests as complete before running them.

Suggested local commands (follow current SDK/project settings; do not retarget frameworks):

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultScreenRenderer|FullyQualifiedName~SongSelectionUILayout|FullyQualifiedName~SongListDisplay"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Windows CI: corresponding `DTXMania.Test.csproj` and Windows game build through existing workflows.

## Completion checklist

- [ ] Default Result level and playing-skill rendered text bounds do not overlap (not just Y constant comparison).
- [ ] NX Result playback/status/guidance group has an appropriately sized translucent backdrop with sufficient contrast against pale art, no overlap with skill panel.
- [ ] CXNeon retains existing Result group X=830/Y=310; backdrop appearance checked visually.
- [ ] SongSelect label foreground at safe Y>=342, X=20..244, clear of **actual** BPM 67px plate, preview art, status content, header and modal.
- [ ] No-preview **score** bars draw the skin's shared `5_preimage default.png`; real jackets take priority; folder/other bars and lamp/title intact.
- [ ] NX 1280×720 screenshots reviewed **after Tasks 1, 2 and 3**; final CXNeon and second window size checked.
- [ ] Focused tests (nonzero discovery), full Mac tests/build and Windows CI pass.
- [ ] Before/after screenshot evidence linked in this same PR; no new art/SFX or unrelated runtime work.

**Current state:** this PR starts with only design and implementation-plan documentation. No production fix, test execution, or visual re-play is claimed by the docs-only commit.
