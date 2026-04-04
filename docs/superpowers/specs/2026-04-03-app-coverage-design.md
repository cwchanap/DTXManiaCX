# App Coverage Improvement Design

## Problem

The current Windows CI coverage command measures more than app code. A baseline run showed:

- Raw overall line coverage: **24.52%**
- App assembly line coverage: **47.89%**
- Test assembly is included in the denominator at **0%**, which distorts the metric for "coverage for the App"

The user wants to optimize for the Windows CI coverage metric, treat the goal as a **10% relative increase**, and replace low-value tests with behavior-focused ones where possible.

After correcting the metric, the app-only baseline is expected to be roughly **47-50%** and must be measured immediately after the CI filter change before any new tests are added.

## Chosen Approach

Use a corrected app-only coverage metric, then raise meaningful coverage in logic-heavy areas with existing test seams.

This means:

1. Update the Windows CI coverage command to exclude the test assembly from Coverlet output using the assembly filter syntax `/p:Exclude="[DTXMania.Test*]*"`.
2. Treat the corrected Windows app-coverage result as the new baseline.
3. Remove clearly low-value tests and replace placeholder tests with real behavior tests.
4. Add new unit tests only in areas that can be exercised reliably without building a graphics-host integration harness.

## Why This Approach

Keeping the current raw percentage would make the metric noisy and misleading because new test code increases the denominator while the test assembly is reported as uncovered. Correcting the metric first makes the coverage number representative of the application itself and aligns with the user's stated goal.

## Scope

### In scope

- Windows CI coverage command in `.github/workflows/build-and-test.yml`
- Behavior-focused coverage additions in:
  - `DTXMania.Game/Lib/GameApiImplementation.cs`
  - `DTXMania.Game/Lib/Stage/Performance/SongTimer.cs`
  - `DTXMania.Game/Lib/Stage/Performance/JudgementManager.cs`
  - `DTXMania.Game/Lib/Resources/SkinDiscoveryService.cs`
- Removal or replacement of low-value tests in the current test project
- Re-running existing build and test commands plus a coverage run to measure the corrected baseline and final lift

### Out of scope

- Large graphics-host or MonoGame integration harnesses
- Full rendering-path coverage work for graphics-heavy classes
- Unrelated refactors outside the touched coverage targets

## Coverage Targets

### 1. GameApiImplementation

Target uncovered parsing and dispatch branches, especially:

- `ParseButtonId`
- `SendInputAsync` branches for supported and unsupported `InputType` values

This code is logic-heavy, pure C# for most branches, and already has Moq-based infrastructure in `GameApiImplementationTests.cs`.

### 2. SongTimer

Target inactive and disposed-state logic using the repository's established `FormatterServices` + `ReflectionHelpers` pattern to bypass MonoGame constructor constraints. This avoids brittle graphics/audio setup while still covering meaningful behavior.

### 3. JudgementManager

Replace the existing placeholder tests that currently only assert `true` with real timing and hit-processing behavior tests using the existing mock input event infrastructure.

### 4. SkinDiscoveryService

Add tests for metadata parsing from `SkinConfig.ini` and skin completeness calculations. This is file-system based logic with existing temp-directory patterns already present in the test suite.

## Low-Value Test Cleanup

### Remove entirely

- None. `GameConstantsTests` is retained in this scope.

### Remove individual low-value tests

- Enum distinctness tautology in `DTXMania.Test/UI/UIEventArgsTests.cs`
- Literal constant assertion for `"DTXFiles"` in `DTXMania.Test/Resources/ConstantsTests.cs`
- Exception inheritance tautology in `DTXMania.Test/Resources/ResourceInterfaceTests.cs`
- Pure DTO default-value test in `DTXMania.Test/GameApi/GameApiImplementationTests.cs`
- POCO passthrough tests for `CompletionReason` and `ClearFlag` in `DTXMania.Test/Stage/Performance/PerformanceSummaryTests.cs`

### Replace instead of delete

- The three placeholder tests in `DTXMania.Test/Stage/Performance/JudgementManagerTests.cs`

## Design Constraints and Existing Patterns

- Use existing build/test commands from the repository; do not introduce new tooling.
- Follow current test patterns, especially:
  - `ReflectionHelpers` for private field access
  - `FormatterServices.GetUninitializedObject(...)` for classes with hard MonoGame constructor dependencies
  - Existing Moq patterns around `IGameContext`
  - Existing temp-directory fixture style for file-system tests
- Keep Mac-safe local validation in place even though the target metric is Windows CI coverage.
- Ensure new tests remain compatible with `DTXMania.Test.Mac.csproj`, which includes `**/*.cs` by default and excludes only a targeted list of graphics-dependent files.

## Verification Plan

1. Run a coverage collection command using the corrected Coverlet filter so the report measures app code only.
2. Record the corrected Windows app-only baseline before adding any new tests.
3. Run the repository build and test commands already used for macOS validation.
4. Confirm the coverage report no longer counts the test assembly in the denominator.
5. Compare corrected baseline vs post-change result and confirm the lift direction and magnitude.

## Implementation Sequence

1. Correct the Windows CI coverage filter.
2. Remove or replace low-value tests.
3. Add new tests for `GameApiImplementation`.
4. Add new tests for `SongTimer`.
5. Replace placeholder `JudgementManager` tests with real behavior checks.
6. Add `SkinDiscoveryService` metadata and completeness tests.
7. Re-run build, tests, and coverage measurement.

## Risks

- Some high-complexity classes remain graphics-heavy and are not good unit-test targets in this pass.
- Correcting the metric changes the meaning of the headline percentage, so the new baseline must be treated as the authoritative app-coverage baseline going forward.

## Expected Outcome

The final result should be a cleaner and more meaningful coverage metric for the application, a smaller set of low-value tests, and additional behavior-focused coverage in logic-heavy areas that are practical to exercise within the existing test architecture.
