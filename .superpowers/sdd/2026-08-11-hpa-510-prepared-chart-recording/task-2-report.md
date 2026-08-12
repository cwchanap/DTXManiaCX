# Task 2 report — prepared preview lifecycle, activation, and telemetry

## Implementation summary

- Added stage-owned prepared selection, preview state, and elapsed-time fields. `PrepareVideoChart` resolves the exact Task 1 chart, loads its declared preview file, projects the row/difficulty, explicitly disables the ordinary preview delay, and publishes `Prepared` only after every step succeeds.
- Added synchronous stage seams `PrepareVideoChart`, `StartPreparedPreview`, `ActivatePreparedChart`, and `CancelPreparedChart`, returning `(bool Success, string Error)` for Task 3 to map into its API result type.
- Added idempotent start/cancel/replace/deactivate cleanup through the existing stop, dispose, reference-release, and BGM-fade paths. Prepared playback is looped at the existing preview volume, starts only on command, does not auto-restart or substitute the primary preview, and marks itself `Failed` after an unexpected stop.
- Added navigation invalidation: projection suppresses teardown/loading; leaving the prepared row clears state and resumes ordinary preview behavior; leaving the prepared difficulty clears state and reloads the normal primary-chart preview.
- Refactored normal and prepared activation through one eligibility gate and one SongTransition start body. Blocked activation preserves the prepared resource/state for retry.
- Added producer telemetry for exact chart identity, prepared preview state, and actual-playing elapsed milliseconds. Chart IDs use `chart:<id>`; zero-ID charts use an active-root-relative normalized path, never an absolute path.

## Files changed

- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- `DTXMania.Game/Lib/GameTelemetrySnapshot.cs`
- `DTXMania.Test/Stage/SongSelectionStagePreparedChartLifecycleTests.cs`

## Tests and outputs

- Focused rebuild and test:
  `rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStagePreparedChartLifecycleTests" --no-restore -v:minimal`
  Result: **14 tests passed**. The rebuild printed **539 warnings**, all pre-existing outside the Task 2 additions; no warning referenced the new lifecycle test, telemetry fields, or lifecycle implementation region. Warning-code counts: `CS8625=177`, `CS8602=114`, `CS8632=54`, `CS8604=47`, `CS8600=23`, `CS0067=24`, `CS8603=13`, `CS8618=8`, `xUnit2013=14`, `xUnit1012=9`, `CS8620=6`, `EF1002=6`, `xUnit1031=6`, `xUnit1026=5`, `xUnit2031=4`, `SYSLIB0050=3`, `xUnit2000=3`, `CS8601=3`, `CA1416=2`, `CS0219=2`, `CS0649=2`, `xUnit2009=2`, `xUnit2012=7`, `xUnit2017=2`, `CS8605=1`, `CS8524=1`, `CS8123=1` (539 total).
- Clean focused GREEN evidence:
  `rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStagePreparedChartLifecycleTests" --no-build --no-restore -v:minimal`
  Result: **14 tests passed, 0 warnings**.
- Song Select/display regression:
  `rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStage|FullyQualifiedName~SongListDisplay" --no-build --no-restore -v:minimal`
  Result: **666 tests passed, 0 warnings**.
- Full host-appropriate Mac-safe suite:
  `rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-build --no-restore -v:minimal`
  Result: **8,185 tests passed, 0 warnings**.
- `rtk git diff --check` passed.

## TDD RED command/output and why expected

After writing the lifecycle/activation/telemetry tests first, before adding the Task 2 production state and seams:

`rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStagePreparedChartLifecycleTests" --no-build --no-restore -v:minimal`

Result: **0 passed, 11 failed**. The tests intentionally exercised the not-yet-present `PrepareVideoChart`, `StartPreparedPreview`, `CancelPreparedChart`, and `ActivatePreparedChart` seams plus `_preparedChartSelection`/`_preparedPreviewState`; the expected reflection/member failures demonstrated the missing lifecycle behavior before production implementation.

## TDD GREEN command/output

After the minimal stage and telemetry implementation (and the final three lifecycle coverage tests), the clean no-build command above passed **14 tests with 0 warnings**. The Song Select/display regression and full Mac-safe suite also passed with 0 warnings.

## Self-review

- Preparation failure clears old resources/state and publishes no active preparation. The normal delay timer is explicitly reset after the helper succeeds, so waiting cannot start prepared audio.
- Start is idempotent while the actual instance reports `Playing`; elapsed time advances only in that state, and an unexpected stop becomes `Failed` without fallback playback.
- One cleanup path is shared by cancellation, replacement, row/difficulty invalidation, blocked-success boundary, activation, and deactivation; repeated calls release each mock resource once.
- `_isProjectingPreparedSelection` covers the complete projection, preserving the exact preview and suppressing normal selection stop/load cycles. Outside prepared mode, existing delayed primary-chart preview behavior remains covered by regression tests.
- Activation checks row/difficulty identity, stage-manager availability, and transition debounce before cleanup; successful activation reuses the existing shared data and `InstantTransition` SongTransition construction.
- Telemetry exposes only the chart identity policy and actual prepared state/timing; no absolute filesystem path is published.
- No `DTXManiaNX` files were modified.

## Concerns

- Verification ran on the macOS-safe .NET test project. Windows build and out-of-process E2E execution remain for the stacked branch's integration verification.
- A successful real MonoGame `SoundEffectInstance` start cannot be fully unit-tested without an audio device because the underlying type is sealed; tests cover instance-creation failure, idempotent existing playback, cleanup, elapsed-state transitions, and the full normal-path state machine. Task 3/native E2E should provide the real-audio acceptance evidence.
- The focused rebuild still surfaces the repository's existing 539 compiler/analyzer warnings; the clean no-build focused, regression, and full-suite evidence is warning-free, and no Task 2 warning was introduced.

## Fix Round 1 — preserve ordinary preview ownership on no-op commands

### What changed

- Added `ClearPreparedPreviewStateIfOwned`, keyed by the non-null prepared selection identity. `CancelPreparedChart` now succeeds as an idempotent no-op when Song Select has only its ordinary primary-chart preview, and invalid `PrepareVideoChart` input no longer tears down that ordinary preview before validation.
- Prepared replacement still uses the existing cleanup path when a prepared selection is actually owned. Deactivation and navigation retain their existing regular preview teardown behavior.

### Tests

- Updated `DTXMania.Test/Stage/SongSelectionStagePreparedChartLifecycleTests.cs` with focused tests proving cancel-without-preparation and invalid-prepare-without-preparation preserve the preview sound, instance, delay timer, fade state, and disposal/reference counts.
- Changed production code only in `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`; this report was appended as required.

### TDD RED

After adding the two tests before the production guard:

`rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStagePreparedChartLifecycleTests" --no-restore -v:minimal`

Result: **14 passed, 2 failed**. Both new tests observed `_previewSound` cleared (`Expected: Mock<ISound>.Object; Actual: null`), demonstrating the unconditional cleanup defect. The rebuild emitted **539 repository-pre-existing warnings**; no warning referenced the new tests or guard.

### TDD GREEN

After the ownership guard:

`rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStagePreparedChartLifecycleTests" --no-build --no-restore -v:minimal`

Result: **16 tests passed, 0 warnings**.

`rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStage|FullyQualifiedName~SongListDisplay" --no-build --no-restore -v:minimal`

Result: **668 tests passed, 0 warnings**.

The only warning-bearing command was the required rebuild above; its 539 warnings match the pre-existing repository warning inventory from the Task 2 baseline and none point to this fix.
