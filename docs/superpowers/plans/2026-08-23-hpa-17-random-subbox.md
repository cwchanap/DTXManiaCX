# HPA-17 Random Select From Sub-BOXes Implementation Plan

> **For implementation agents:** Use TDD and complete these tasks in order. Keep the feature on this branch and this draft PR; do not open a second PR for HPA-17.

**Goal:** Add a persisted System toggle that lets RANDOM SELECT include `NodeType.Score` songs from descendant BOXes below the currently open song list.

**Architecture:** Extend the existing scalar configuration pipeline and System config list. Keep recursive candidate enumeration as one private static helper in `SongSelectionStage`; it walks the current list on demand and follows only `NodeType.Box.Children`. The existing random choice and difficulty-selection transition remain stage-owned. No new service, cache, panel, or RNG abstraction is introduced.

**Tech stack:** .NET 8, MonoGame, xUnit, SQLite-backed configuration.

**Spec:** `docs/superpowers/specs/2026-08-23-hpa-17-random-subbox-design.md`

## Global constraints

- Default Off preserves current direct-only RANDOM SELECT behavior.
- “Sub-BOXes” means descendants of `_currentSongList`, not the whole library.
- Direct `NodeType.Score` entries remain eligible in both modes.
- Recurse only through `NodeType.Box`; do not include `ScoreMidi`, `Random`, `BackBox`, or `Unknown`.
- Preserve `_currentSongList` as the candidate root; do not switch RANDOM SELECT to `_filteredView`.
- Read the config when the user activates RANDOM SELECT; do not cache or subscribe to changes.
- Do not mutate BOX navigation state, add cycle tracking, or pre-flatten the song tree.
- Do not add a legacy alias, `Config.ini` migration, generic tree-query service, or injectable RNG.
- Preserve the current `Random` construction and selection behavior; this ticket changes candidate scope only.
- One HPA-17 PR only.

## Validation command matrix

Use the project matching the current machine.

**macOS:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "<FILTER>"
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

**Windows / CI:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "<FILTER>"
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
```

---

## Task 1: Persist and expose the System toggle

**Estimate:** 0.45 engineer-day

**Production files:**

- Modify: `DTXMania.Game/Lib/Config/ConfigData.cs`
- Modify: `DTXMania.Game/Lib/Config/IConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Config/ConfigManager.cs`
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`

**Test files:**

- Modify: `DTXMania.Test/Config/ConfigDataTests.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerTests.cs`
- Modify: `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`
- Modify: `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- Update existing concrete `IConfigManager` test doubles only where the interface addition causes compilation failures.

### 1.1 Write failing configuration tests

Add coverage for this contract:

```csharp
public bool RandomSelectFromSubBox { get; set; } = false;
void SetRandomSelectFromSubBox(bool value);
```

Pin:

1. `new ConfigData().RandomSelectFromSubBox` is `false`.
2. Applying `RandomSelectFromSubBox=true`, `1`, or `on` uses the existing boolean parser.
3. Invalid input follows the existing malformed-entry policy and does not need special handling.
4. The setter changes the value and marks deferred persistence dirty only on an actual change.
5. SQLite snapshot/round trip preserves Off and On under exactly the `RandomSelectFromSubBox` key.
6. No `RandSubBox` or NX-language alias is accepted or written.

### 1.2 Write failing Config-stage tests

Extend the existing System-category inventory and toggle tests. Prove:

1. `Random Select Sub-BOXes` appears immediately after `Song Folders`.
2. Its description is `Include songs inside descendant BOXes when using RANDOM SELECT.`
3. It displays `OFF`/`ON` from the current config value.
4. keyboard activation calls the manager setter and changes only this setting.
5. no new category, overlay, or navigation item appears.

### 1.3 Run focused tests and confirm RED

Use this filter with the platform-appropriate Game test project:

```text
FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigManagerSqlitePersistenceTests|FullyQualifiedName~ConfigStageLogicTests
```

Expected RED reasons are the missing property, manager API/persistence mapping, and System row.

### 1.4 Implement the scalar config path

Add `RandomSelectFromSubBox = false` alongside other general/system booleans.

In `ConfigManager`, follow an existing scalar boolean such as `Metronome` for all current SQLite-era seams:

- known-key allowlist;
- default-value lookup;
- `ApplyEntry` parsing;
- snapshot construction;
- `SetRandomSelectFromSubBox` using the existing `SetBool`/deferred-dirty path.

Do not add a second persistence path or touch schema creation.

### 1.5 Add the System row

Create one normal `ToggleConfigItem` in `SetupConfigItems()` and insert it after `songFoldersItem`:

```text
Name: Random Select Sub-BOXes
Description: Include songs inside descendant BOXes when using RANDOM SELECT.
```

Getter: `Config.RandomSelectFromSubBox`  
Setter: `SetRandomSelectFromSubBox`

### 1.6 Re-run focused tests until GREEN

Use the filter from 1.3.

### 1.7 Commit

```bash
git add DTXMania.Game/Lib/Config DTXMania.Game/Lib/Stage/ConfigStage.cs DTXMania.Test/Config
git commit -m "feat: add random sub-box config toggle"
```

---

## Task 2: Expand RANDOM SELECT candidates on demand

**Estimate:** 0.4 engineer-day

**Production file:**

- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

**Test file:**

- Modify: `DTXMania.Test/Stage/SongSelectionStageBasicTests.cs`

Keep these tests headless. Reuse the file's current uninitialized-stage and private-reflection conventions rather than adding a graphics fixture or exposing a public API.

### 2.1 Write failing candidate-collector tests

Extracting the helper is part of the production change, but write tests against its intended private-static shape first. Use small authored trees built from `SongListNode` and `AddChild`.

Pin:

1. Off returns direct `Score` nodes and excludes a one-level nested score.
2. On returns direct scores plus one-level and multi-level descendant scores.
3. `ScoreMidi`, `Random`, `BackBox`, and `Unknown` are ignored even when they have children/references.
4. empty input and BOX-only input return an empty list.
5. the input lists and node navigation state are not mutated.

Do not assert candidate ordering, random distribution, or seed behavior.

### 2.2 Add two narrow selection integration tests

Use a current list containing one child BOX with exactly one valid nested score so random choice is deterministic without an RNG seam.

Prove:

1. with `RandomSelectFromSubBox=false`, a list with no direct score remains in song-list selection and does not set `_selectedSong`;
2. with the setting `true`, the nested score becomes `_selectedSong` and the existing difficulty-selection state is entered.

Populate only the minimum `SongListNode.Score` data required by `CreateDifficultySelectionBars`. Reuse the existing reflection helpers; do not create a production test-only constructor.

### 2.3 Run focused stage tests and confirm RED

```text
FullyQualifiedName~SongSelectionStageBasicTests
```

Expected RED reasons are the missing collector and unchanged direct-only selection.

### 2.4 Implement the local collector

Add one private static helper in `SongSelectionStage`, for example:

```csharp
private static List<SongListNode> CollectRandomCandidates(
    IEnumerable<SongListNode> nodes,
    bool includeSubBoxes)
```

Single-pass rules:

```text
Score -> add
Box + includeSubBoxes -> recurse into Children
anything else -> ignore
```

Return a fresh list. Do not use a repository-wide extension, visited set, or cached state.

### 2.5 Wire `SelectRandomSong()`

Replace only the direct `FindAll(NodeType.Score)` candidate construction with the helper, passing:

```csharp
_configManager.Config.RandomSelectFromSubBox
```

Keep the existing per-action `Random` construction, `Next(candidates.Count)` choice, no-candidate/no-valid-score no-op, and difficulty transition. Do not navigate into the candidate's parent BOX and do not use `_filteredView`.

### 2.6 Re-run focused stage tests until GREEN

Use the filter from 2.3.

### 2.7 Commit

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs DTXMania.Test/Stage/SongSelectionStageBasicTests.cs
git commit -m "feat: include sub-box songs in random selection"
```

---

## Task 3: Regression verification and PR closeout

**Estimate:** 0.15 engineer-day

### 3.1 Run all focused HPA-17 tests together

```text
FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigManagerSqlitePersistenceTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~SongSelectionStageBasicTests
```

### 3.2 Run the full platform test suite and build

**macOS:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

**Windows / CI:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
```

Do not claim a Windows result from a macOS run; use CI for the other target.

### 3.3 Review the final diff against scope guards

Confirm the production diff is limited to the five expected files and contains none of the following:

- generic song-tree service or extension library;
- cached flattened candidates;
- filter/search behavior changes;
- navigation mutations;
- new panel/category;
- config migration/alias;
- RNG API or dependency-injection changes.

Interface-driven test-double edits are allowed when required only to compile.

### 3.4 Optional manual smoke check

From a BOX containing a child BOX with at least one playable chart:

1. Off: RANDOM SELECT considers only direct songs.
2. On: RANDOM SELECT can enter difficulty selection for a descendant song.
3. Re-enter Config and verify the toggle persisted.

### 3.5 Finish on the same PR

Update the draft PR checklist and Linear issue with actual validation results. Mark the PR ready only after tests/build pass; do not create a second implementation PR.
