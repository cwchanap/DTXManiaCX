# HPA-759 — Single-PR implementation plan (revised after design review)

**Design:** [HPA-759 design](../specs/2026-11-06-hpa-759-score-readability-design.md)  
**Linear:** https://linear.app/cwchanap/issue/HPA-759/fix-playtest-score-inconsistencies-and-cx-neon-readability  
**Workflow:** HPA-759's design, implementation and tests are on this same PR.
**Art prerequisite:** [HPA-794 — generate F rank badge](https://linear.app/cwchanap/issue/HPA-794/art-generate-cx-neon-f-rank-badge-for-result) supplied the checked-in F asset in a separate art PR.

## Task 0 — Confirm baseline and RED tests (~0.2 day)

- Re-read current `ResultScreenModel.ComputeRank`, `SongScore.ComputeRankIndex/NormalizeRankPercentage/RankString`, `SongDatabaseService` new-run BestRank/history paths, `SongStatusPanel.DrawSkillPointSection`, `ResultScreenRenderer`, `ConfigStage`, and the HPA-722 `DrumConfigRenderer`.
- Run existing relevant tests, recording the baseline. Add failing focused tests for: 85% and <40% grade divergence, the same 0.345 displayed differently by Result vs Song Select/history, and CX Neon title/category text colors.
- The Result policy intentionally shifts from NX's seven grades (SS–E) to the CX persisted eight grades (SS–F). **This visible regrade is a conscious product decision**, not a mistaken claim about NX. An NX-imported 85% S can retain S while a fresh CX 85% run is A; do not modify old imports or persisted buckets.
- The **separate HPA-794 art PR** supplied the 274×274 F badge via the existing `skingen` pipeline.

## Task 1 — One CX run-grade helper, explicit F texture (~0.5 day)

**Production:** `DTXMania.Game/Lib/Song/Entities/SongScore.cs`, `SongDatabaseService.cs`, `Stage/Result/ResultScreenModel.cs`, `Stage/Result/ResultScreenRenderer.cs`, `Resources/TexturePath.cs`.

**Tests:** `DTXMania.Test/Song/SongScoreTests.cs`, `SongDatabaseServiceTests.cs`, `Stage/Result/ResultScreenModelTests.cs`, `ResultScreenRendererTests.cs`, **and** `DTXMania.Test/Resources/TexturePathTests.cs`.

1. Use **one** `SongScore.RunRankBucket(double playingSkill)`: clamp finite skill to 0–100, convert to decimal and round to two places away from zero, then floor and normalize. Non-finite input maps to F. This matches displayed skill at boundaries such as 39.995 → 40.00 → E. Use the returned bucket in all **three** current-run paths: Result `ComputeRank` (via `SongScore.ComputeRankIndex`), `SongDatabaseService` BestRank update and history `RankString`. Preserve existing `BestRank` schema, `NormalizeStoredBestRank`, NX importer, `RankMultiplier`, and best-of-all semantics.
2. Add `ResultRank.F = 7` and `GetRankLabel` F. Replace/rename `Create_ShouldComputeNXRankFromPlayingSkill` so it tests the **chosen CX** contract. Parametrize exact/just-below 40/50/60/70/80/90/95 and 0/100; assert Result/current-run history and stored bucket agree. Include NX→CX regrade cases (e.g., 85% Result S→A, <40% E→F), and the valid case where latest run and historical **best** badge have different grades.
3. Include `TexturePath.ResultRankF` in `TexturePath.GetAllTexturePaths()` so pack validation requires it. `TexturePathTests` covers its path and required inclusion. F uses `8_rankF.png` and the existing E background; unknown ranks have no E fallback. A missing rank badge is traced once per Result load. Keep the existing texture/reveal code; **no F text renderer**, new font or procedural drawing in the game.
4. Ensure the F image is present in `System/CXNeon/Graphics` and covered by the existing `skingen` manifest and `CxNeonPackTests`. Release builds flatten this pack into their **bundled default** `System/Graphics`; repo-root NX reference art is **not shipped**. For the F badge, take real screenshots with **CX Neon selected** and with a **non-CX-Neon skin while the bundled/staged default System fallback contains `Graphics/8_rankF.png`**. A plain dev checkout's legacy NX `System/Graphics` does not contain F, and `ResourceManager.ResourceExists` checks active → fallback → bundled paths, **not** `System/CXNeon/` automatically; missing F in an un-staged NX-only development run is an environment setup issue, not a regression in the shipped skin. Do not copy an extra F badge into the NX reference pack or change ResourceManager's general fallback logic.

**Exit:** one shared SS–F classifier for *new* runs, correct F badge, unchanged historical storage.

## Task 2 — Three-site shared skill text, Mac-safe tests (~0.25 day)

**Production:** `DTXMania.Game/Lib/Song/SkillValueFormatter.cs` sits beside `ChartLevelFormatter.cs`. Call it from `ResultScreenModel.Create`, Song Select HighSkill, and the `SongDatabaseService` saved-history **PlayingSkill** text.

**Tests:** `DTXMania.Test/Song/SkillValueFormatterTests.cs`; a **Mac-included** `DTXMania.Test/UI/SongStatusPanelSkillTextTests.cs` (or included `SongStatusPanelAchievementRateTests.cs`); existing history test.

1. Use the current Result rule `Math.Max(0.0, value).ToString("0.00", CultureInfo.InvariantCulture)` as the single text formatter. The problem is custom `"0.00"` versus standard `"F2"`, **not SQLite precision**. The formatter does not convert values to `decimal`; the grade helper does so for matching midpoint rounding. Do not adjust the skill formula or mutate persisted values.
2. Extract a tiny `internal static` Song Select HighSkill **text resolver** returning shared-formatted skill for played scores and `"---"` otherwise. Keep GPU-only drawing separate. Test this resolver from a file **included in the Mac test project**; `UI/SongStatusPanelLogicTests.cs` is explicitly excluded.
3. Replace `{4:F2}` in the saved-history line with `{4}` fed a string from the shared formatter. Keep every other history field and record-saving behavior unchanged.
4. Mac-safe fixtures: `0.345 -> 0.35`, `1.005 -> 1.01`, `2.675 -> 2.68`, 0, a regular value, invariant culture and unplayed `---`. Assert Result, Song Select and saved-history consistency **when comparing the same numeric skill** (Result GameSkill and Song Select best HighSkill are different after a worse new run). No dedicated SQLite round-trip test is needed for this display issue.

## Task 3 — Theme-aware Config foregrounds; verify Drum Mapping (~0.25 day)

**Production:** `DTXMania.Game/Lib/Stage/ConfigStage.cs` only (unless verified theme token need).  
**Tests:** `DTXMania.Test/Config/ConfigStageThemeColorTests.cs` and any nearby affected draw logic tests.

1. Add narrow resolvers `ResolveSelectedMenuTextColor(theme)` and `ResolveDescriptionTitleTextColor(theme)`: `Config.*` specific key → palette token → existing NX dark constants. The palette token is `UI.Accent` for the selected-menu label (selected state, matching `ResolveValueColor`'s selected branch) and `UI.TextPrimary` for the description title. Wire only these two current draw calls; existing value-color resolution and layout remain untouched.
2. Test empty-theme defaults, generic palette fallback, and explicit Config overrides. CX Neon sets `Config.SelectedMenuText` to white for contrast on the cursor. Capture actual CX Neon and default/light screenshots; static color tests alone do not prove visibility.
3. Inspect the **new HPA-722** Drum Mapping screen with its palette-based text on both skins. Do not reinstate obsolete dark instruction styling, redo its layout, or reverse HPA-722's explicit **MIDI** caption decision. Touch only genuinely reproducing new contrast defects if any.

## Task 4 — Player verification and PR evidence (~0.25–0.5 day)

- Exercise a failed low-achievement run and a completed No Fail low-achievement run. Capture actual Result F badge in **CX Neon** and in a **packaged/staged default-fallback configuration** (e.g., the game's `<bin>/System/Graphics` populated from `System/CXNeon/Graphics`); capture Config/Drum Mapping separately on **CX Neon and the local light NX skin**. Do not expect F to load from an un-staged NX-only development `System/Graphics` tree. Also capture saved-history rank/value and Song Select best badge, including an intentionally changed NX→CX grade boundary (such as 85%). Check score saving and previous best retention. [Native gameplay and screenshots](https://github.com/cwchanap/DTXManiaCX/pull/225#issuecomment-6711317746) were recorded on this PR.
- Run focused tests (adapt to environment/current SDK):
  - `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ResultScreenModelTests|FullyQualifiedName~ResultScreenRendererTests|FullyQualifiedName~ResultScreenRendererWarningTests|FullyQualifiedName~SongScoreTests|FullyQualifiedName~SongDatabaseServiceSkillSaveTests|FullyQualifiedName~SongDatabaseServiceHighSkillTests|FullyQualifiedName~SkillValueFormatterTests|FullyQualifiedName~SongStatusPanelSkillTextTests|FullyQualifiedName~ConfigStageThemeColorTests|FullyQualifiedName~TexturePathTests|FullyQualifiedName~CxNeonPackTests"`
  - `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
  - `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
  - On Windows, corresponding `DTXMania.Game.Windows.csproj` and `DTXMania.Test.csproj` build/tests; use existing Windows E2E where practical.
- The out-of-process E2E project is Windows-only. On Mac, use the repository's existing game-MCP/native screenshot route rather than creating a new E2E or image-capture harness.
- Update **this same PR** with actual commands, test counts, screenshots, expected NX→CX grade difference and any remaining limits. Do not claim graphical validation based on headless tests.

## Guardrails and handoff

**Preserve:** existing SQLite best-rank buckets, legacy ordinal import mapping, NX imported labels, best-score qualification, score/history data shape, judgement/skill formulas, skin layout, font/resource lifecycle, timing/input rules. Reuse `SongScore` and `ISkinTheme`; no new grade engine or theme framework.

**Do not bundle:** HPA-794 badge generation (separate art PR), new SFX, navigation context, control hints, two-step Enter UI, Tab mode changes, Drum Mapping redesign, DTXCreator, schema changes, historical rank rewrites, new CI jobs. If the visible regrade away from NX is challenged, settle the product policy in design review rather than switching policies silently.

## Done means

- [x] One `RunRankBucket` governs all new-run grade uses; Result/current history agree at every boundary. Intentional NX→CX visible regrade is documented.
- [x] F badge from the **separate HPA-794 art PR** renders correctly in Result with CX Neon selected **and** via the staged/bundled default-skin fallback; no incorrect E badge or new glyph-render path. `TexturePathTests` explicitly covers the path and required inventory.
- [x] 0.345 displays 0.35 in all three text surfaces, using a common helper; Mac-included Song Select resolver tests run.
- [x] CX Neon/default Config colors are legible; redesigned Drum Mapping visually verified. The refreshed selected-category captures are local; earlier native screenshots are linked above.
- [x] Full Mac and focused tests plus native screenshots and failed/No Fail player paths are recorded for this PR. Windows checks were not run on Mac.
