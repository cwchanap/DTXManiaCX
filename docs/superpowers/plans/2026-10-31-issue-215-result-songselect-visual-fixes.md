# Issue #215 — Result / SongSelect Visual Fixes (Implementation Plan)

**Design:** [Issue #215 design](../specs/2026-10-31-issue-215-result-songselect-visual-fixes-design.md)  
**Issue:** https://github.com/cwchanap/DTXManiaCX/issues/215  
**Delivery:** **one PR total**. Continue in this docs-only draft after design review; no second implementation PR. Target ~2 engineer days, maximum 3.

## Ground rules for implementing agent

Read the linked design and the actual current source first; rebase/compare if `main` moved since the documented `752a4fb3` baseline. Change **presentation only**. Avoid new asset creation, generic UI infrastructure, refactors, and score/gameplay logic. Update tests only where changed behavior invalidates their expectations. Keep scope to the following tasks.

## Task 0 — Record composition baseline (up to 0.25 day)

1. Verify the default skin and CXNeon through the existing 1280x720 GameApi/headless playtest or a real desktop run, noting full-screen Result and SongSelect screenshots and current text/font widths. Prioritize the existing screenshots in issue #215 if reproducible.
2. Confirm the NX result skill art at `Graphics/7_SkillPanel.png`, the CXNeon art and overrides, the normal/failed save text widths, the 430x313 CXNeon preview frame, and selected/unselected baked thumbnail-slot bounds.
3. If the label's expected maximum supported playback-profile string exceeds 224px at the existing 16px font, decide a localized size/position adjustment now rather than letting text overflow.

**Exit:** 4 defects can be demonstrated or traced on the current branch; no speculative difficulty-panel adjustment.

## Task 1 — Result fallback and theme-aware group (~0.5 day)

**Files**
- `DTXMania.Game/Lib/UI/Layout/ResultUILayout.cs`
- `DTXMania.Game/Lib/Stage/Result/ResultScreenRenderer.cs`
- `System/CXNeon/Theme.ini`
- `DTXMania.Test/Stage/ResultScreenRendererThemeLayoutTests.cs`
- `DTXMania.Test/Stage/Result/ResultScreenRendererTests.cs`

1. Change built-in `LevelPosition.Y` from 550 to 525 and `PlayingSkillPosition.Y` from 537 to 555. Do not change existing `Result.LevelX/Y` / `Result.PlayingSkillX/Y` resolvers.
2. Set NX playback group origin to (30,108) with offsets 0/26/60/84 and existing guidance dimensions 420x60. Introduce a small pure group position resolver consulting `Result.PlaybackInfoX/Y` and use it at all four `DrawModelText` sites. Avoid independent keys for every row.
3. Add `Result.PlaybackInfoX=830` / `Result.PlaybackInfoY=310` to CXNeon to retain all its original group coordinates. Add a small black text shadow for profile, bucket, status and wrapped guidance to preserve contrast on the light NX backdrop; do not add draw resources.
4. Test NX defaults, both theme keys together and partial overrides, row separation, wrapped guidance geometry, and mock draw calls (including save color/status). Update existing expected draw positions. Keep the score and other Result text untouched.

**Exit:** existing CXNeon layout unchanged, default Result has independent skill rows and one theme-adjustable playback group.

## Task 2 — SongSelect label compositing (~0.25–0.5 day)

**Files**
- `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs`
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- focused `DTXMania.Test/UI/SongSelectionUILayoutTests.cs` / relevant SongSelection stage tests

1. Change the profile label bounds to (20,326,224,24). Keep the current text formatter, config synchronization, 16px font, color and size reassertion.
2. Move `_mainPanel.AddChild(_playbackProfileLabel)` below the song list/status/history/preview additions and **above** the search modal addition, preserving modal topmost behavior.
3. Add a narrow bounds/layout test: profile right < preview left X=250; profile bottom <= status top Y=350; header begins at Y=368. Do not introduce tests requiring a test-only graphics device.
4. Check the longest actual speed/pitch text in the real font. If it does not fit, use a localized smaller font or micro-adjust bounds, and test actual rendered width; do not alter `PlaybackProfileFormatter` or truncate the text.

**Exit:** complete label visible on both bundled skins; no change to song list, difficulty, preview content or modal behavior.

## Task 3 — Missing-jacket slot (~0.5 day)

**Files**
- `DTXMania.Game/Lib/Song/Components/SongListDisplay.cs`
- `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs` if slot padding constants are needed
- `DTXMania.Test/UI/SongListDisplayLogicTests.cs` (and existing renderer tests where appropriate)

1. Add a tiny private/shared rectangle helper for NX selected and unselected **score** thumbnail slots. Use existing `PreviewImageSize`/offsets and measure any extra rim from the current art. Respect distinct selected/unselected bar texture Y offsets.
2. Paint the missing-preview fill using existing `_whitePixel` and opacity, **after bar background, before clear lamp**, in both enhanced and basic paths when applicable. Preserve valid image drawing and cached image ownership.
3. Pin pure bounds and empty-versus-available preview decisions with focused tests. Check no white border remains, no hit to the lamp/title, and no placeholder on folder/other bars. Do not change `SongBarRenderer`'s loading rules.

**Exit:** no bright empty image slot on default NX bar artwork; CXNeon and actual jackets remain readable.

## Task 4 — Real screenshot acceptance and CI (~0.5–0.75 day)

1. Replay SongSelect and Result for **default NX System/ and CXNeon** at virtual 1280x720, with a second window size. Check ordinary score save and failed-save guidance. Capture missing/valid jacket in selected and unselected positions and a fast scroll. Confirm the search modal remains above the profile label.
2. Attach a compact before/after screenshot set (or a small committed evidence file) and state actual observations in this same PR. A unit test passing does **not** replace a visual check. If the suggested top-left Result group needs slight adjustment due to text contrast or plate overlap, change the same constants, not the architecture.
3. Run focused and full Mac tests, Mac build, and Windows tests/build in CI. Confirm focused filters discover >0 tests; report the results in the PR. Do not claim tests or screenshots as complete before running them.

Suggested local commands (adjust to the project's current SDK/toolchain; don't retarget frameworks):

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultScreenRenderer|FullyQualifiedName~SongSelectionUILayout|FullyQualifiedName~SongListDisplay"
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Windows CI: corresponding `DTXMania.Test.csproj` and Windows game build, using repository workflows.

## Completion checklist

- [ ] Default Result chart level and playing skill are 30px apart and visually distinct.
- [ ] Result playback/status/guidance group clears character art and remains readable on light NX backdrop.
- [ ] CXNeon Result playback group keeps its existing X=830/Y=310 composition.
- [ ] SongSelect full speed/pitch label is never painted beneath a later panel; modal remains in front.
- [ ] No-image **score** bars use a dark placeholder, without damaging loaded jackets, lamp or title.
- [ ] Both skins pass screenshot checks at 1280x720 and one scaled window size; failed-save guidance checked.
- [ ] Focused tests (nonzero discovery), full Mac tests/build and Windows CI pass.
- [ ] Before/after visual evidence linked in this same PR; no new art/SFX or out-of-scope runtime changes.

**Current state:** this PR starts with only design and implementation-plan documentation. No production fix, test execution, or visual re-play is claimed by the docs-only commit.
