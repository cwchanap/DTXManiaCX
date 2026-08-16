# Task 1 Report: Persist and expose the Metronome toggle

## Status

Complete.

## Files changed

- `DTXMania.Game/Lib/Config/ConfigData.cs`
- `DTXMania.Game/Lib/Config/IConfigManager.cs`
- `DTXMania.Game/Lib/Config/ConfigManager.cs`
- `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- `DTXMania.Test/Config/ConfigDataTests.cs`
- `DTXMania.Test/Config/ConfigManagerTests.cs`
- `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs`
- `.superpowers/sdd/2026-08-15-hpa-13-metronome/task-1-report.md`

## TDD RED

Command:

```text
rtk proxy /usr/local/share/dotnet/dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests" --verbosity normal
```

Relevant output:

```text
DTXMania.Test/Config/ConfigStageLogicTests.cs(994,48): error CS0117: 'ConfigData' does not contain a definition for 'Metronome'
1 Error(s); exit code 1
```

The new tests failed at compilation because the requested production property did not exist yet. This is the expected red result for the test-first change. The isolated worktree required one approved `rtk dotnet restore DTXMania.Test/DTXMania.Test.csproj` before the no-restore test command could build.

## GREEN / focused verification

Command:

```text
rtk proxy /usr/local/share/dotnet/dotnet test DTXMania.Test/DTXMania.Test.csproj --no-restore --filter "FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests" --verbosity minimal
```

Output:

```text
Passed!  - Failed:     0, Passed:   265, Skipped:     0, Total:   265, Duration: 869 ms - DTXMania.Test.dll (net8.0)
```

`rtk git diff --check` also passed.

## Self-review

- Added the default-off `ConfigData.Metronome` property and the `IConfigManager.SetMetronome` contract.
- Reused `TryParseBool`, persisted one `Metronome` line in `[Game]`, and preserved save/load round trips.
- Made `SetMetronome` an early-return no-op when unchanged and used the existing deferred `MarkDirty` path when changed.
- Added the Drums toggle immediately after Pitch with the required description.
- Updated the existing inventory test and the required drum-stage stub; no second inventory test or migration alias was added.
- The worktree diff contains only the eight requested source/test files plus this report.

## Commit SHA(s)

8c1fb41 (implementation commit; report metadata correction follows in a report-only commit).

## Concerns

No feature concerns. The focused suite ran on the macOS/.NET 8 configuration; the Windows CI/full suite was not run locally.
