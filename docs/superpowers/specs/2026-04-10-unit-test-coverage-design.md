# Unit Test Coverage Improvement Design

## Problem

The full CI-style unit test suite currently reports **67.43% line coverage** for `DTXMania.Test/DTXMania.Test.csproj` when run with `ALSOFT_DRIVERS=null` and Coverlet. The target for this work is an **absolute increase of 5 percentage points**, which means raising line coverage to at least **72.43%**.

The codebase has several large uncovered files, but not all of them are practical unit test targets. In particular, `PerformanceStage`, `NoteRenderer`, and other rendering-heavy paths have poor ROI for a unit-test-only coverage push because they depend heavily on graphics behavior rather than deterministic business logic.

## Constraints

- Optimize for the **full CI-style coverage baseline**, not the macOS-safe subset.
- Prefer **new unit tests** over production changes.
- Allow only **minimal production refactors for testability** when a small seam is necessary.
- Reuse the repo's established test patterns:
  - reflection-based logic tests
  - temp-directory filesystem tests
  - mock-based resource and UI tests
- Avoid broad refactors or graphics-driven test work unless absolutely necessary.

## Existing Test Infrastructure

The repo already has strong scaffolding for non-rendering tests:

- `DTXMania.Test/TestData/ReflectionHelpers.cs` for private member access and uninitialized-object setup
- `DTXMania.Test/Resources/ManagedFontLogicTests.cs` for logic-first font tests
- `DTXMania.Test/Resources/ResourceManagerTests.cs` for filesystem- and skin-path behavior
- `DTXMania.Test/UI/SongListDisplayLogicTests.cs` for complex UI logic without graphics
- `DTXMania.Test/UI/PreviewImagePanelTests.cs`, `UIListTests.cs`, and `UIButtonLogicTests.cs` for additional deterministic UI logic

These files are the preferred expansion points because they already match the production code's seams.

## Proposed Coverage Strategy

The coverage increase will be delivered in **batches**, ordered by ROI and testability.

### Batch 1: ManagedFont logic expansion

Primary target: `DTXMania.Game/Lib/Resources/ManagedFont.cs`

Add tests to `DTXMania.Test/Resources/ManagedFontLogicTests.cs` for:

- character-range cache building
- Japanese character support and fallback behavior
- `SanitizeText`
- `GetCharacterReplacement`
- `WrapText`
- cache-key generation
- text-render cache bookkeeping and disposal-safe behavior where reachable without graphics

Why first:

- already has a logic-test pattern in place
- contains a large amount of deterministic string and cache logic
- avoids renderer-bound code paths while still exposing many uncovered lines

### Batch 2: ResourceManager logic expansion

Primary target: `DTXMania.Game/Lib/Resources/ResourceManager.cs`

Add tests to `DTXMania.Test/Resources/ResourceManagerTests.cs` for:

- box.def skin override toggling and effective-skin selection
- fallback resource lookup
- `UnloadAll`
- `UnloadByPattern`
- `GetUsageInfo`
- `CollectUnusedResources`
- default skin initialization and directory bootstrap behavior where safely testable

Why second:

- high uncovered surface in logic-heavy resource management
- existing tests already cover temp-directory and path-resolution scenarios
- can reuse mock/disposable resource objects to cover cache cleanup paths

### Batch 3: SongListDisplay logic expansion

Primary target: `DTXMania.Game/Lib/Song/Components/SongListDisplay.cs`

Add tests to `DTXMania.Test/UI/SongListDisplayLogicTests.cs` for:

- `CalculateBarTextureBounds`
- `CalculateArtistNamePosition`
- additional truncation edge cases
- additional wrapping edge cases
- fallback/comment-bar behavior that does not require a live graphics device

Why third:

- existing logic test file is already extensive and proven
- layout math is deterministic and high leverage
- good gap-closing surface after the resource-focused batches

### Batch 4: Gap closers if coverage is still short

Secondary targets:

- `DTXMania.Test/UI/PreviewImagePanelTests.cs`
- `DTXMania.Test/UI/UIListTests.cs`
- `DTXMania.Test/UI/UIButtonLogicTests.cs`
- small additions to existing stage logic tests only where the remaining path is already isolated from rendering

This batch exists only to close the final gap if the first three batches do not clear 72.43%.

## Minimal Refactor Policy

Production changes are allowed only when they are both:

1. necessary to expose deterministic logic to unit tests, and
2. small enough to preserve behavior and existing structure

Acceptable examples:

- extracting a private calculation helper from a method that currently mixes setup and logic
- loosening visibility to `internal` when the repo already uses `InternalsVisibleTo`
- introducing a tiny wrapper seam around a dependency if the current code has no testable entry point

Unacceptable examples:

- rewriting rendering pipelines for testability
- reorganizing large classes unrelated to the coverage target
- changing behavior just to make tests easier to write

## Risks and Mitigations

### Risk: chasing raw uncovered lines in rendering-heavy files

Mitigation: explicitly exclude `PerformanceStage`, `NoteRenderer`, and similar draw-heavy surfaces from the main plan.

### Risk: spending too long on one large file

Mitigation: work in coverage batches and remeasure after each batch rather than finishing every possible test in one module before checking the global impact.

### Risk: cache and disposal tests becoming brittle

Mitigation: favor behavior-visible assertions such as reference counts, disposal calls, cache counts, and resolved paths over private implementation details unless reflection-based inspection is already an established pattern in nearby tests.

## Validation Plan

Use the full CI-style command as the source of truth:

```bash
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.csproj --configuration Debug --no-restore --verbosity minimal /p:CollectCoverage=true /p:CoverletOutput=./TestResults/coverage-baseline/coverage /p:CoverletOutputFormat=cobertura /p:IncludeTestAssembly=false /p:Exclude="[DTXMania.Test*]*"
```

After each batch:

1. run the targeted tests for the files being changed
2. rerun the full CI-style coverage command
3. stop once line coverage reaches **72.43% or higher**

## Expected Outcome

The primary implementation should come from expanding tests in:

- `ManagedFontLogicTests`
- `ResourceManagerTests`
- `SongListDisplayLogicTests`

with `PreviewImagePanelTests`, `UIListTests`, `UIButtonLogicTests`, and selective stage logic used only as final gap-closers. This keeps the work tightly aligned with existing test infrastructure while maximizing the probability of achieving the required **+5 absolute coverage increase** with unit-test-friendly changes.
