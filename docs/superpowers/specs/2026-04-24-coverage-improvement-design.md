# Coverage Improvement Design

**Goal:** Increase full-suite line coverage for `DTXMania.Test/DTXMania.Test.csproj` by **5.00 absolute points**, moving from the measured **82.16%** baseline to at least **87.16%**.

## Baseline and measurement

Use the full CI-style coverage command against the fresh worktree:

```bash
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --configuration Debug \
  --no-restore \
  --verbosity minimal \
  /p:CollectCoverage=true \
  /p:CoverletOutput=/Users/chanwaichan/workspace/DTXmaniaCX/.worktrees/coverage-improvement-20260423/TestResults/coverage-authoritative/coverage \
  /p:CoverletOutputFormat=cobertura \
  /p:IncludeTestAssembly=false \
  /p:Exclude="[DTXMania.Test*]*"
```

Measured baseline:

- Line coverage: **82.16%**
- Branch coverage: **77.33%**
- Test count: **4049**
- Uncovered lines: **3777**

## Options considered

### 1. Stage-centric expansion (**chosen**)

Extend the existing stage logic suites where helper patterns, fake game time, reflection hooks, and mock managers already exist.

- **Pros:** highest deterministic yield, best test ergonomics, lowest production-risk
- **Cons:** may still need a second wave for the final gap to +5.00

### 2. UI/resource expansion

Target `SongListDisplay`, `SongStatusPanel`, `ManagedFont`, `BitmapFont`, `ManagedSound`, and related helpers.

- **Pros:** good secondary yield
- **Cons:** more fragmented, more resource/graphics seams

### 3. Performance-heavy expansion

Target `PerformanceStage`, `NoteRenderer`, `ComboDisplay`, `GaugeDisplay`, and neighboring performance components.

- **Pros:** largest raw uncovered pockets
- **Cons:** highest complexity and determinism risk

## Chosen design

Use a two-wave coverage push centered on the existing stage logic suites.

### Wave 1: highest-yield stage suites

Primary files to extend:

- `DTXMania.Test/Stage/SongSelectionStageLogicTests.cs`
- `DTXMania.Test/Stage/SongTransitionStageLogicTests.cs`
- `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- `DTXMania.Test/Stage/TitleStageLogicTests.cs`
- `DTXMania.Test/Stage/ResultStageTests.cs`
- `DTXMania.Test/BaseGameTests.cs`

Production files expected to benefit most:

- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- `DTXMania.Game/Lib/Stage/SongTransitionStage.cs`
- `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- `DTXMania.Game/Lib/Stage/TitleStage.cs`
- `DTXMania.Game/Lib/Stage/ResultStage.cs`
- `DTXMania.Game/Game1.cs`

Wave 1 should target deterministic branches such as:

- selection wrapping and index bounds
- debounce timing and transition guards
- shared-data construction for stage changes
- config save/cancel/apply behavior
- result calculation and navigation branches
- BaseGame orchestration branches that do not require live graphics or audio devices

### Wave 2: conditional follow-up

Only run Wave 2 if the authoritative post-Wave-1 coverage report still falls short of **87.16%**.

Priority order:

1. Deterministic `PerformanceStage` logic branches in `DTXMania.Test/Stage/Performance/*`
2. `DTXMania.Test/UI/SongListDisplayLogicTests.cs`

Wave 2 exists to close the remaining measured gap, not to broaden scope.

## Test design constraints

- Follow TDD for each new coverage slice: add a failing test first, confirm the expected failure, then add the minimal test code needed to reach the branch.
- Prefer existing repo patterns:
  - `ReflectionHelpers`
  - mock `IStageManager` and other managers
  - fake game time via `ReflectionHelpers.CreateGame(...)`
  - direct assertions on private field state when the existing logic suites already use that pattern
- Do not introduce broad production seams purely to manufacture coverage.
- Skip graphics-device-heavy or timing-fragile paths unless the current suite already has a deterministic seam.

## Why this design

The remaining uncovered surface is concentrated enough that a stage-first pass has a realistic path to the target:

- the major stage-layer targets account for roughly **1358 uncovered lines**
- the existing logic suites already cover the same kinds of stateful private helpers and transition logic
- this keeps the work focused on maintainable unit tests instead of brittle render or audio integration behavior

## Success criteria

- Full-suite line coverage reaches **87.16%** or higher
- Existing test behavior remains green
- New tests stay deterministic and aligned with the current stage-logic testing style
- If +5.00 proves unreachable without distorting production code, stop at the highest verified increase and report the measured ceiling
