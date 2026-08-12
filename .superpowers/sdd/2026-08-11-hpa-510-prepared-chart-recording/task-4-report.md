# Task 4 report — prepared-chart regression suite and live smoke

## Status

Implemented and verified the HPA-510 fixture contract plus one narrow prepared-chart `Category=E2E` smoke. The test uses the existing fixture builder, game launch bundle, JSON-RPC client, `Eventually`, and artifact writer; it does not add a recorder, fake OBS service, persistence assertions, or a second E2E harness.

## Implementation and files

- `DTXMania.E2E/Fixtures/E2EFixtureBuilder.cs`
  - Declares the existing generated `autoplay-tone.wav` as `#PREVIEW` in the generated chart.
- `DTXMania.E2E/Fixtures/E2EFixtureBuilderTests.cs`
  - Asserts the generated chart contains the `#PREVIEW` declaration using `E2EFixtureBuilder.AudioFileName`.
- `DTXMania.E2E/PreparedChartRecordingSmokeTests.cs`
  - Launches an isolated fixture and enters Song Select through the normal Title → SongSelect path.
  - Prepares the exact absolute chart path, requires and saves a non-empty screenshot, waits beyond the one-second automatic preview delay, and asserts `Prepared` with elapsed `0`.
  - Starts the prepared preview and polls until `Playing` with elapsed `>= 10,000` ms.
  - Activates the chart and observes `SongTransition` before `Performance`.
  - Writes prepared/playing/transition/performance state, fixture, stdout/stderr, and failure artifacts through existing seams.
- `.superpowers/sdd/2026-08-11-hpa-510-prepared-chart-recording/task-4-report.md`
  - This report.

## TDD RED/GREEN evidence

RED (before fixture implementation):

```text
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "FullyQualifiedName~E2EFixtureBuilderTests.Build_ShouldWriteConfigAndGeneratedChart" --logger "console;verbosity=normal"
```

The focused test failed as intended at the new assertion: `Assert.Contains() Failure`, not found `#PREVIEW: autoplay-tone.wav` (0 passed, 1 failed).

GREEN (after the one-line `BuildChart()` implementation):

```text
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --no-restore --filter "FullyQualifiedName~E2EFixtureBuilderTests.Build_ShouldWriteConfigAndGeneratedChart" --logger "console;verbosity=normal"
```

Result: 1 test passed, 0 warnings.

## Verification commands and results

All .NET test commands below were run with the host test-runner permissions required for VSTest localhost communication.

```text
DTXMANIA_E2E_GAME_PROJECT=DTXMania.Game/DTXMania.Game.Mac.csproj rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "FullyQualifiedName~PreparedChartRecordingSmokeTests" --logger "console;verbosity=normal"
```

Result: 1 test passed, 0 warnings, 25.0 s. This is the documented Mac live run; no platform/audio blocker prevented the HPA-510 flow.

```text
DTXMANIA_E2E_GAME_PROJECT=DTXMania.Game/DTXMania.Game.Mac.csproj rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "FullyQualifiedName~GameplayAutoPlaySmokeTests" --logger "console;verbosity=minimal"
```

Result: 1 existing autoplay smoke passed in 78.4 s (139 existing nullable warnings), confirming the shared fixture `#PREVIEW` declaration did not regress the score-bucket flow.

```text
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PreparedChart" --logger "console;verbosity=normal"
```

Result: 38 tests passed (existing nullable warnings: 400).

```text
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStagePreviewAndBgm|FullyQualifiedName~SongSelectionStageFilter|FullyQualifiedName~SongSelectionStageRootFilterAdditional|FullyQualifiedName~SongSelectionStageBreadcrumb|FullyQualifiedName~SongSelectionStageTab|FullyQualifiedName~SongSelectionStageNavigation|FullyQualifiedName~SongTransitionStage" --logger "console;verbosity=normal"
```

Result: 260 tests passed, 0 warnings.

```text
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~GameApi|FullyQualifiedName~JsonRpcServer" --logger "console;verbosity=normal"
```

Result: 374 tests passed, 0 warnings.

```text
rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --logger "console;verbosity=normal"
```

Result: 64 tests passed, 0 warnings.

```text
rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj --filter "Category=E2E-Support" --logger "console;verbosity=normal"
```

Result: 16 tests passed, 0 warnings.

```text
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --logger "console;verbosity=minimal"
```

Result: 8,203 tests passed, 0 warnings.

```text
rtk git diff --check
```

Result: clean.

## Live-run evidence

The successful Mac run left artifacts under:

`/var/folders/_k/lkrpcd8516s5x5szmkq1mbvc0000gn/T/dtxmaniacx-e2e-prepared-chart-6f090ea38b5347e5a7e525d8678886a5/TestResults/e2e/`

- `prepared-song-select.png` is non-empty (104 KiB).
- `prepared-state.json`: `stageType=SongSelect`, `preparedChartIdentity=chart:1`, `preparedPreviewState=Prepared`, `preparedPreviewElapsedMs=0`.
- `playing-state.json`: `preparedPreviewState=Playing`, `preparedPreviewElapsedMs=10083.353499999941`.
- `song-transition-state.json` records `stageType=SongTransition`.
- `performance-state.json` records `stageType=Performance`.
- `game-stderr.log` contains only 3 bytes; stdout records the expected SongSelect → SongTransition → Performance lifecycle.

Windows native gameplay E2E was not available on this Mac host; the existing Windows `Category=E2E` workflow remains the authoritative native/CI run.

## Self-review

- The fixture uses a unique temp run root and an OS-selected API port, so chart/audio/config state is isolated per test.
- The smoke waits for the generated song title before preparing, uses the exact absolute fixture chart path, and never navigates by title or index.
- The two-second wait is longer than the normal one-second automatic preview delay; `Prepared` plus exact zero elapsed catches accidental auto-start.
- The preview poll requires actual `Playing` state and at least 10 seconds of measured elapsed time before activation.
- Activation observes `SongTransition` with a 100 ms poll before waiting for `Performance`, avoiding a stage-observation race while keeping the assertion narrow.
- The test stops at `Performance`; it does not run to `Result`, inspect score persistence, involve OBS, or duplicate the autoplay score-bucket flow.
- `git diff --check` is clean and no unrelated production files were changed.

## Concerns

- The Mac game stdout includes the existing `PerformanceError` fallback (`AudioLoader: Cannot create SongTimer - no audio loaded`) after the transition. It does not affect this smoke, which intentionally stops at Performance; Windows CI should remain the authoritative native audio check.
- The Windows live run and CI result are not observable from this host and must be confirmed by the existing workflow.
