# Unit Test Coverage Improvement Design

## Problem

The full CI-style unit test suite currently reports **81.31% line coverage** for `DTXMania.Test/DTXMania.Test.csproj` when run with the same Coverlet settings used by Windows CI. If a local baseline artifact is needed and not already present, regenerate it with the authoritative coverage command in the Validation Plan and use the resulting `coverage.cobertura.xml` as the starting measurement. The goal of this work is an **absolute increase of 5 percentage points** over that authoritative baseline, which makes the current completion threshold **86.31% or higher**. If a fresh authoritative baseline is re-established and differs materially from 81.31%, recompute the threshold as `baseline + 5.00`.

At this baseline, the gap is large enough that the work cannot be finished with a few isolated helper tests. Reaching the target requires roughly **1,058 newly covered production lines**, so the implementation needs to focus on high-yield files that already have usable unit-test seams.

## Constraints

- Measure success against the **full CI-style suite**, not the macOS-safe subset.
- Prefer **new or expanded unit tests** over production changes.
- Allow **small production refactors for testability** only when they unlock meaningful deterministic coverage.
- Stay aligned with the repo's established test style:
  - reflection-based logic tests
  - uninitialized-object setup where constructors are graphics-heavy
  - Moq-based manager and resource dependencies
  - logic-path assertions instead of rendering assertions
- Avoid broad refactors and avoid graphics-device-heavy test work unless a tiny seam makes the logic independently testable.

## Current Coverage Shape

The largest missed surfaces in the current report include:

- `Lib/Stage/SongSelectionStage.cs`
- `Lib/Song/Components/SongListDisplay.cs`
- `Lib/Stage/SongTransitionStage.cs`
- `Lib/Stage/ConfigStage.cs`
- `Lib/Stage/TitleStage.cs`
- `Lib/Stage/ResultStage.cs`
- `Game1.cs`
- `Lib/Stage/StartupStage.cs`

These are preferable to rendering-heavy files such as `PerformanceStage` and `NoteRenderer` because they already have logic-oriented test coverage and existing test scaffolding that can be extended without inventing a new testing style.

## Approved Coverage Strategy

The implementation will use a **two-wave coverage push**.

### Wave 1: Stage and song-UI logic expansion

Primary targets:

- `DTXMania.Test/Stage/SongSelectionStageLogicTests.cs`
- `DTXMania.Test/UI/SongListDisplayLogicTests.cs`
- `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- `DTXMania.Test/Stage/SongTransitionStageLogicTests.cs`
- `DTXMania.Test/Stage/TitleStageLogicTests.cs`
- `DTXMania.Test/Stage/ResultStageTests.cs`
- `DTXMania.Test/Stage/StartupStageLogicTests.cs`
- `DTXMania.Test/BaseGameTests.cs`

This wave is the main plan because these files already cover:

- navigation and selection state machines
- transition/debounce behavior
- fallback and failure branches
- progress-state advancement
- queueing, breadcrumb, and difficulty sync logic
- shared-data extraction and handoff behavior

The implementation focus is to densify branch coverage in existing helpers and lifecycle methods rather than chase top-level draw paths.

### Wave 2: Logic-only buffer files

Secondary targets, only if Wave 1 does not yet clear 86.31%:

- `DTXMania.Test/Graphics/GraphicsManagerLogicTests.cs`
- `DTXMania.Test/UI/UILabelLogicTests.cs`
- `DTXMania.Test/Utilities/AppPathsTests.cs`

This buffer wave exists to close the remaining gap with smaller deterministic files rather than force risky rendering-heavy test work.

## Test Design Tactics

The coverage push should prioritize **branch-dense helper flows** over setup-heavy code paths.

Examples of desired test additions:

- extra navigation wraparound, debounce, and transition branches in stage logic
- fallback and error-handling branches in stage resource-loading helpers
- additional song-list selection, difficulty, queueing, and cache-invalidating paths
- BaseGame lifecycle and Game API branches that can be exercised without a real graphics device
- edge cases in configuration save/cancel/panel activation flows

Tests should reuse existing helpers and nearby patterns before introducing new infrastructure.

## Minimal Refactor Policy

Production changes are allowed only when they are both:

1. directly tied to the coverage target, and
2. small enough to preserve current behavior and code structure.

Acceptable refactors:

- extracting a deterministic helper from a method that currently mixes orchestration and logic
- relaxing visibility to `internal` where the repo already supports test access through `InternalsVisibleTo`
- adding a tiny seam around an otherwise unreachable dependency when the logic behind it is already stable

Unacceptable refactors:

- broad cleanup unrelated to coverage
- changing behavior just to simplify a test
- reworking rendering pipelines to fabricate unit-test reachability

## Risks and Mitigations

### Risk: stage/UI work still falls short of the 5-point target

Mitigation: use the Wave 2 buffer files as a deliberate backstop rather than waiting until the end to discover the shortfall.

### Risk: spending too long on graphics-coupled branches

Mitigation: bias toward lifecycle, state, and helper logic that can be tested with mocks or reflection; skip paths that require real rendering behavior unless a tiny seam makes them deterministic.

### Risk: test additions become brittle by asserting internals too aggressively

Mitigation: prefer externally visible state transitions, selected items, queued work, shared-data payloads, and dependency calls. Use private-field inspection only where that pattern is already established in adjacent tests.

## Validation Plan

Use the full CI-style command as the source of truth:

```bash
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.csproj --configuration Debug --no-restore --verbosity minimal /p:CollectCoverage=true /p:CoverletOutput=./TestResults/Coverage/ /p:CoverletOutputFormat=cobertura /p:IncludeTestAssembly=false /p:Exclude="[DTXMania.Test*]*"
```

Implementation should proceed in batches:

1. extend the highest-yield stage/UI test files
2. rerun targeted tests while iterating on the current development platform
3. use the full CI-style coverage command above as the authoritative repo-wide measurement whenever the environment supports it; otherwise rely on targeted local iteration and treat the Windows-style full-suite run as the final gate
4. stop early if the authoritative full-suite report reaches **86.31%** or higher
5. use Wave 2 only if the authoritative full-suite report is still below **86.31%**

## Expected Outcome

The final implementation should achieve the target primarily by expanding existing stage/UI logic tests instead of introducing a new testing strategy. If successful, the repo will cross **86.31% full-suite line coverage** while staying inside the established unit-test patterns already used throughout `DTXMania.Test`.
