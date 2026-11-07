# HPA-759 — Single-PR implementation plan

**Design:** [HPA-759 score and readability contract](../specs/2026-11-06-hpa-759-score-readability-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-759/fix-playtest-score-inconsistencies-and-cx-neon-readability  
**PR workflow:** this starts as a **docs-only draft**. After design approval, add production changes and tests on **this same branch and PR**. Never create a second implementation PR for this ticket. Estimated total: **2–2.5 engineer days**, hard cap **3 days**.

## Task 0 — Baseline and test seams (~0.25 day)

1. Re-read current `ResultScreenModel.ComputeRank`, `SongScore` grade utilities, `SongDatabaseService` rank/history save, `ResultScreenRenderer` rank asset selection, `SongStatusPanel.DrawSkillPointSection`, and `ConfigStage` theme resolution. Confirm which files shifted after recent PRs; do not implement from old line numbers.
2. Run the existing result/score/Config focused test suites and note failures before editing. Establish **RED** tests for: Result grade F/85% mismatch against stored policy; 0.345 skill display mismatch after persistence; Neon selected-category/description title colors.
3. Check the new HPA-722 `DrumConfigRenderer`. It already uses `SkinPalette`; the former dark-on-dark instruction source cited in HPA-759 no longer exists. Verify visually rather than reintroduce obsolete code.

**Exit:** documented baseline and reproducible focused regressions.

## Task 1 — Make Result grade match persisted rank (~0.75 day)

**Production:** `DTXMania.Game/Lib/Stage/Result/ResultScreenModel.cs`, `ResultScreenRenderer.cs`. Use `DTXMania.Game/Lib/Song/Entities/SongScore.cs` helpers as-is whenever possible.

**Tests:** `DTXMania.Test/Stage/Result/ResultScreenModelTests.cs`, `ResultScreenRendererTests.cs`, existing `DTXMania.Test/Song/SongScoreTests.cs` and `SongDatabaseServiceTests.cs` as needed.

1. Expand `ResultRank` to include F (index 7). Delegate Result rank classification to `SongScore.ComputeRankIndex` with the same integer-floor treatment already used by `SongDatabaseService`. Do not copy the eight thresholds into Result, replace the stored bucket policy, or alter `RankMultiplier`.
2. Update Result's rank label mapping and all relevant model expectations. Add parameterized exact-boundary and just-below-boundary tests for 40, 50, 60, 70, 80, 90, 95, plus 0 and 100. Assert alignment with `SongScore.RankString` for the **same run**.
3. Result has no F sprite: have the renderer load no wrong E **badge** for F, retain the existing E-tier **background**, and render an F text fallback with existing font, theme-aware foreground, badge anchor and reveal timing. SS–E sprite selection must remain unchanged. Test the load/selection behavior without a GPU, then verify the F glyph on a real screenshot.
4. Add or adjust a focused saved-history test proving Result and the new persisted history line agree on E/F and 85% boundary runs, while the best-rank record remains best-of-all. Keep imported/old rank semantics unchanged; rely on existing NX import and legacy-rank regressions.

**Exit:** new-run grade policy SS–F is shared. Existing score bucket data and imported ranks are not reinterpreted.

## Task 2 — Shared two-decimal skill formatting (~0.5 day)

**Production:** add a tiny pure `DTXMania.Game/Lib/UI/SkillValueFormatter.cs` (or an equivalent existing small UI helper); wire `ResultScreenModel.FormatDecimal` and `SongStatusPanel.DrawSkillPointSection`.

**Tests:** `DTXMania.Test/Stage/Result/ResultScreenModelTests.cs`, `DTXMania.Test/UI/SongStatusPanelLogicTests.cs` and a focused formatter test file if appropriate.

1. Implement one `Format` for finite nonnegative skill values. Round via `decimal` to two decimals with `MidpointRounding.AwayFromZero` and format in invariant culture. Do not change raw scores or the EF Core persistence path.
2. Test 0.345 -> 0.35, 0.344 -> 0.34, 0, high values, invariant locale, and stored SQLite round trip. Preserve the unplayed `"---"` placeholder. Ensure Result and Song Select consume exactly the same helper.
3. Compare the two screens only when the current run is also the best (Result GameSkill versus Song Select HighSkill can otherwise validly differ). Do not alter score, level, percentage or achievement computations.

**Exit:** same input value gives the same text across screens and after persistence.

## Task 3 — Theme-aware Config titles, verify Drum Mapping (~0.5 day)

**Production:** `DTXMania.Game/Lib/Stage/ConfigStage.cs`. Only edit `System/CXNeon/Theme.ini` if a deliberate explicit token is necessary; `UI.TextPrimary` already exists.

**Tests:** `DTXMania.Test/Config/ConfigStageThemeColorTests.cs` and nearby Config draw tests that currently expect fixed dark text.

1. Add narrowly scoped `ResolveSelectedMenuTextColor(theme)` and `ResolveDescriptionTitleTextColor(theme)` functions following the approved fallback order from the spec. Replace only the two dark hard-coded draw-site selections. Retain NX defaults for `SkinTheme.Empty`.
2. Assert empty-theme defaults, CX Neon `UI.TextPrimary`, and explicit `Config.*` overrides. Keep existing `ResolveValueColor` and category/item positioning intact.
3. Run the game with CX Neon and a default/light skin; capture Config category selection and description panel, plus the **new** HPA-722 Drum Mapping header/instructions. Confirm text contrast at 1280×720. If the Drum Mapping issue is already gone, do **not** change its renderer. Do not undo HPA-722's approved MIDI wording.

**Exit:** Config is readable on both skins; Drum Mapping is verified without redundant refactoring.

## Task 4 — Regression evidence and completion (~0.5 day)

- Exercise a failed low-achievement chart and a completed No Fail low-achievement chart. Confirm the Result rank, current-run history line, best badge semantics, visible 0.345 example (using a deterministic fixture when helpful), and save status. On the F case, capture a real Result screenshot showing **F**, not E.
- Confirm the same chart's score is still saved, repeated runs do not lower `BestRank`, NX imported score tests pass, and no changes occur in the input/timing/judgement systems.
- Run applicable suites (adapt to environment and current SDK; record actual command/exit status):
  - `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultScreenModelTests|FullyQualifiedName~ResultScreenRendererTests|FullyQualifiedName~SongScoreTests|FullyQualifiedName~SongDatabaseServiceTests|FullyQualifiedName~ConfigStageThemeColorTests"`
  - `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
  - `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
  - On Windows, run corresponding `DTXMania.Game.Windows.csproj` and `DTXMania.Test.csproj` builds/tests.
  - Use existing graphical E2E/native player flow if available; don't add a new screenshot harness or workflow.
- Update this same PR with actual tests, screenshots, and a note that player-navigation/hint enhancements are deferred. Request review only after implementation evidence is attached.

## Scope control and escalation

The score+format+Config fixes are small and independently testable, but remain **one ticket and one PR** under the three-day limit. Preserve existing SQLite rank bucket values/shape and history row format; use existing `SongScore` policies and `ISkinTheme` instead of new managers or generalized grade/theme frameworks.

**Do not bundle**: navigation-context retention, keyboard hint redesign, Tab mode effects/BPM HUD, DTXCreator, HPA-722 drum page redesign, rank art generation, new sounds, new schema migrations or legacy rank repair. If actual F sprite art is desired, scope generation to a separate art ticket. If a newly discovered persistence migration or broad legacy-rank incompatibility makes this exceed three days, surface it for product review rather than silently expanding this PR.

## Done means

- [ ] Results and freshly saved history share the existing SS–F grade bands (including F art fallback), with all boundaries tested.
- [ ] 0.345 renders 0.35 consistently in Result and Song Select without modifying saved skill.
- [ ] Neon and light-skin Config selected labels/titles are readable; Drum Mapping's post-HPA-722 design verified.
- [ ] Prior best rank and NX import semantics unchanged; failed/No Fail journeys validated.
- [ ] Tests and actual screenshots recorded; only **one PR** associated with HPA-759.