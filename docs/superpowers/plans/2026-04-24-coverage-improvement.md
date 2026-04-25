# Coverage Improvement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise full-suite line coverage for `DTXMania.Test/DTXMania.Test.csproj` from 82.16% to at least 87.16% by extending deterministic unit tests in the stage-heavy logic suites first, then using a narrow fallback wave only if measurement still shows a shortfall.

**Architecture:** Keep the work centered in existing test files that already use `ReflectionHelpers`, fake game time, private-field inspection, and mock managers. Measure with the same authoritative full-suite coverage command after each wave so every added test is justified by the remaining gap, not by guesswork.

**Tech Stack:** .NET 8 test projects on xUnit + Moq + Coverlet, MonoGame game project, full-suite coverage command with `ALSOFT_DRIVERS=null`

---

### Task 1: Re-establish the authoritative baseline

**Files:**
- Modify: `docs/superpowers/specs/2026-04-24-coverage-improvement-design.md` (read-only reference)
- Test: `DTXMania.Test/DTXMania.Test.csproj`

- [ ] **Step 1: Restore the full test project**

Run:

```bash
cd .worktrees/coverage-improvement-20260423
dotnet restore DTXMania.Test/DTXMania.Test.csproj
```

- [ ] **Step 2: Run the authoritative full-suite coverage command**

Run:

```bash
cd .worktrees/coverage-improvement-20260423
rm -rf TestResults/coverage-authoritative
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

Expected: baseline near 82.16% line coverage and a fresh `TestResults/coverage-authoritative/coverage.cobertura.xml`

- [ ] **Step 3: Confirm the biggest uncovered stage files**

Use the coverage XML to confirm the leading Wave 1 targets remain:

```text
SongSelectionStage.cs
SongTransitionStage.cs
ConfigStage.cs
TitleStage.cs
ResultStage.cs
Game1.cs
```

- [ ] **Step 4: Commit**

No commit expected for the baseline-only task.

### Task 2: Expand `TitleStage`, `ResultStage`, and `BaseGame` logic coverage

**Files:**
- Modify: `DTXMania.Test/Stage/TitleStageLogicTests.cs`
- Modify: `DTXMania.Test/Stage/ResultStageTests.cs`
- Modify: `DTXMania.Test/BaseGameTests.cs`
- Test: `DTXMania.Test/DTXMania.Test.csproj`

- [ ] **Step 1: Write the failing tests**

Write failing tests for the remaining uncovered branches in these methods and line ranges from the authoritative coverage report:

- `TitleStage.LoadMenuTexture()` fallback path — `DTXMania.Game/Lib/Stage/TitleStage.cs:255-267`
- `TitleStage.LoadSoundEffects()` primary/fallback/error paths — `DTXMania.Game/Lib/Stage/TitleStage.cs:270-312`
- `TitleStage.HandleInput()` back action and menu select flow — `DTXMania.Game/Lib/Stage/TitleStage.cs:351-378`
- `ResultStage.InitializeComponents()` font exception path — `DTXMania.Game/Lib/Stage/ResultStage.cs:147-157`
- `ResultStage.ExecuteInputCommand()` activate/back transition gate — `DTXMania.Game/Lib/Stage/ResultStage.cs:186-197`
- `ResultStage.DrawBackground()` fallback fill path — `DTXMania.Game/Lib/Stage/ResultStage.cs:212-229`
- `BaseGame.TryInitializeGameApi()` disabled / missing-key / success branches — `DTXMania.Game/Game1.cs:215-233`
- `BaseGame.StartGameApiServerAsync()` success / cancellation / exception branches — `DTXMania.Game/Game1.cs:235-258`
- `BaseGame.Draw()` render-target recreation and screenshot completion paths — `DTXMania.Game/Game1.cs:322-350`

- [ ] **Step 2: Run the focused tests to verify the new assertions fail for the expected branch reason**

Run:

```bash
cd .worktrees/coverage-improvement-20260423
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~TitleStageLogicTests|FullyQualifiedName~ResultStageTests|FullyQualifiedName~BaseGameTests" \
  --no-restore
```

Expected: the newly added tests fail because the targeted branch or state assertion is not yet wired correctly.

- [ ] **Step 3: Write the minimal implementation**

Prefer test-only implementation: finish the smallest amount of setup, fake state, or helper reuse needed to reach the intended branch. Only touch production code if the failing test proves a deterministic branch is currently unreachable from any existing seam.

- [ ] **Step 4: Run the focused tests to verify they pass**

Run the same filter command again.

- [ ] **Step 5: Commit**

```bash
cd .worktrees/coverage-improvement-20260423
git add DTXMania.Test/Stage/TitleStageLogicTests.cs DTXMania.Test/Stage/ResultStageTests.cs DTXMania.Test/BaseGameTests.cs
git commit -m "test: extend stage and base game coverage" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 3: Expand `SongTransitionStage` and `ConfigStage` branch coverage

**Files:**
- Modify: `DTXMania.Test/Stage/SongTransitionStageLogicTests.cs`
- Modify: `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- Test: `DTXMania.Test/DTXMania.Test.csproj`

- [ ] **Step 1: Write the failing tests**

Write failing tests for the remaining uncovered branches in these methods and line ranges:

- `SongTransitionStage.InitializeUI()` panel population and activation — `DTXMania.Game/Lib/Stage/SongTransitionStage.cs:295-330`
- `SongTransitionStage.LoadPreviewImage()` primary / directory fallback / default preview branches — `DTXMania.Game/Lib/Stage/SongTransitionStage.cs:390-441`
- `SongTransitionStage.LoadDifficultySprite()` prior-sprite cleanup and load path — `DTXMania.Game/Lib/Stage/SongTransitionStage.cs:449-470`
- `ConfigStage` lifecycle paths that current logic tests do not hit yet — `DTXMania.Game/Lib/Stage/ConfigStage.cs:64-126`
- `ConfigStage.DrawBackground()` and `DrawTitle()` fallback text rendering paths — `DTXMania.Game/Lib/Stage/ConfigStage.cs:556-568`

Do not duplicate the already-covered `ConfigStage` save/back assertions; target uncovered lifecycle and drawing-adjacent logic only.

- [ ] **Step 2: Run the focused tests to verify RED**

Run:

```bash
cd .worktrees/coverage-improvement-20260423
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongTransitionStageLogicTests|FullyQualifiedName~ConfigStageLogicTests" \
  --no-restore
```

- [ ] **Step 3: Write the minimal implementation**

Keep the change inside the tests unless a missing deterministic seam is proven. Reuse `ReflectionHelpers`, `CreateStage`, and existing keyboard/input helpers instead of inventing new scaffolding.

- [ ] **Step 4: Run the focused tests to verify GREEN**

Run the same filter command again.

- [ ] **Step 5: Commit**

```bash
cd .worktrees/coverage-improvement-20260423
git add DTXMania.Test/Stage/SongTransitionStageLogicTests.cs DTXMania.Test/Config/ConfigStageLogicTests.cs
git commit -m "test: cover config and transition branches" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 4: Expand `SongSelectionStage` logic coverage

**Files:**
- Modify: `DTXMania.Test/Stage/SongSelectionStageLogicTests.cs`
- Test: `DTXMania.Test/DTXMania.Test.csproj`

- [ ] **Step 1: Write the failing tests**

Write failing tests for the remaining uncovered branches in these methods and line ranges:

- `SongSelectionStage.LoadUIGraphics()` header/footer texture failure paths — `DTXMania.Game/Lib/Stage/SongSelectionStage.cs:326-354`
- `SongSelectionStage.InitializeUI()` panel, label, and enhanced-rendering setup — `DTXMania.Game/Lib/Stage/SongSelectionStage.cs:360-408`
- `SongSelectionStage.ProcessInputCommands()` null-input and queue-drain path — `DTXMania.Game/Lib/Stage/SongSelectionStage.cs:866-876`
- `SongSelectionStage.ExecuteInputCommand()` status-panel navigation and title-stage return branches — `DTXMania.Game/Lib/Stage/SongSelectionStage.cs:886-963`
- `SongSelectionStage.HandleActivateInput()` score vs non-score activation path — `DTXMania.Game/Lib/Stage/SongSelectionStage.cs:1473-1499`

Use the existing `SongSelectionStageLogicTests` helpers and keep the tests focused on state transitions, navigation, and manager calls rather than rendering pixels.

- [ ] **Step 2: Run the focused tests to verify RED**

Run:

```bash
cd .worktrees/coverage-improvement-20260423
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~SongSelectionStageLogicTests" \
  --no-restore
```

- [ ] **Step 3: Write the minimal implementation**

Stay in the test file unless the failing test proves a tiny production seam is unavoidable. Prefer state injection and private helper invocation over new test-only APIs.

- [ ] **Step 4: Run the focused tests to verify GREEN**

Run the same filter command again.

- [ ] **Step 5: Commit**

```bash
cd .worktrees/coverage-improvement-20260423
git add DTXMania.Test/Stage/SongSelectionStageLogicTests.cs
git commit -m "test: extend song selection stage coverage" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 5: Re-measure coverage after Wave 1 and choose Wave 2 only if needed

**Files:**
- Modify: `TestResults/coverage-authoritative/coverage.cobertura.xml` (generated artifact)
- Test: `DTXMania.Test/DTXMania.Test.csproj`

- [ ] **Step 1: Run the authoritative full-suite coverage command again**

Use the same command from Task 1.

- [ ] **Step 2: Compare the measured line coverage against the 87.16% target**

If line coverage is **87.16% or higher**, skip Task 6 and go to Task 7.

- [ ] **Step 3: If the target is still unmet, choose the next deterministic hotspot**

Priority order:

1. `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
2. `DTXMania.Test/Stage/Performance/PerformanceRendererStateTests.cs`
3. `DTXMania.Test/Stage/Performance/DisplayComponentStateTests.cs`

- [ ] **Step 4: Commit**

No commit expected for the coverage checkpoint itself.

### Task 6: Execute the narrow fallback wave only if coverage still misses the target

**Files:**
- Modify: `DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/PerformanceRendererStateTests.cs`
- Modify: `DTXMania.Test/Stage/Performance/DisplayComponentStateTests.cs`
- Modify: `DTXMania.Test/UI/SongListDisplayLogicTests.cs`
- Test: `DTXMania.Test/DTXMania.Test.csproj`

- [ ] **Step 1: Write the next failing test in the top uncovered deterministic file**

Prefer `PerformanceStage` logic first. If it stops being deterministic or starts demanding graphics/audio behavior, switch to `SongListDisplayLogicTests.cs`.

```csharp
[Fact]
public async Task LoadBGMSoundsAsync_WhenParsedChartIsNull_ShouldLeaveSoundMapEmpty()
{
    var stage = CreateStage();
    ReflectionHelpers.SetPrivateField(stage, "_parsedChart", null);
    ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());

    var task = (Task)ReflectionHelpers.InvokePrivateMethod(stage, "LoadBGMSoundsAsync")!;
    await task;

    Assert.Empty(ReflectionHelpers.GetPrivateField<Dictionary<string, ISound>>(stage, "_bgmSounds"));
}
```

- [ ] **Step 2: Run the focused tests to verify RED**

Run the smallest relevant filter, for example:

```bash
cd .worktrees/coverage-improvement-20260423
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~SongListDisplayLogicTests" \
  --no-restore
```

- [ ] **Step 3: Write the minimal implementation**

Use existing helpers and deterministic state injection first. Do not broaden production seams just to hit coverage.

- [ ] **Step 4: Run the focused tests to verify GREEN**

Run the same filter again.

- [ ] **Step 5: Re-run the authoritative coverage command after each fallback batch**

After every successful fallback batch, run the same full-suite coverage command from Task 1 and compare against **87.16%**.

- If the target is reached, stop and go to Task 7.
- If the target is still unmet and there is another deterministic branch available, repeat Task 6 from Step 1.
- If deterministic branches are exhausted, record the verified ceiling and go to Task 7.

- [ ] **Step 6: Commit**

```bash
cd .worktrees/coverage-improvement-20260423
git add DTXMania.Test/Stage/Performance/PerformanceStageDeterministicTests.cs DTXMania.Test/Stage/Performance/PerformanceRendererStateTests.cs DTXMania.Test/Stage/Performance/DisplayComponentStateTests.cs DTXMania.Test/UI/SongListDisplayLogicTests.cs
git commit -m "test: close remaining coverage gaps" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 7: Final verification and handoff

**Files:**
- Modify: `TestResults/coverage-authoritative/coverage.cobertura.xml` (generated artifact)
- Test: `DTXMania.Test/DTXMania.Test.csproj`

- [ ] **Step 1: Run the authoritative full-suite coverage command one last time**

Use the same command from Task 1.

- [ ] **Step 2: Confirm the measured line coverage is at least 87.16% or record the verified stopping point**

Capture:

- final line coverage
- final branch coverage
- whether the +5.00 absolute target was fully reached

- [ ] **Step 3: Run the focused suites touched during the work if the final full run did not already prove them green after the last edit**

Example:

```bash
cd .worktrees/coverage-improvement-20260423
ALSOFT_DRIVERS=null dotnet test DTXMania.Test/DTXMania.Test.csproj \
  --filter "FullyQualifiedName~BaseGameTests|FullyQualifiedName~TitleStageLogicTests|FullyQualifiedName~ResultStageTests|FullyQualifiedName~SongTransitionStageLogicTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~SongSelectionStageLogicTests|FullyQualifiedName~PerformanceStageDeterministicTests|FullyQualifiedName~SongListDisplayLogicTests" \
  --no-restore
```

- [ ] **Step 4: Commit**

Create a final commit only if there are uncommitted code changes remaining after the task commits above.
