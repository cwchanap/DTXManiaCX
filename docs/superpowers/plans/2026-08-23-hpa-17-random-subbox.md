# HPA-17 Random Select From Sub-BOXes Implementation Plan

> **For implementation agents:** Use TDD and complete these tasks in order. Continue on this branch and this draft PR; do not open a second PR for HPA-17.

**Goal:** Make RANDOM SELECT reachable and add a persisted System toggle that lets it include `NodeType.Score` songs from descendant BOXes below the currently open All Songs hierarchy.

**Architecture:** Reuse the existing synthetic Random-node factory and activation switch, extend the existing scalar config switch/snapshot path, and add one `internal static` gated candidate collector to `SongSelectionStage`. `SelectRandomSong()` keeps the existing `Random` + `SelectSong()` -> `SongTransition` flow. No new service, cache, panel, difficulty UI, or RNG abstraction.

**Tech stack:** .NET 8, MonoGame, xUnit, SQLite-backed configuration.

**Spec:** `docs/superpowers/specs/2026-08-23-hpa-17-random-subbox-design.md`

## Global constraints

- Default Off preserves current CX direct-only RANDOM SELECT candidate behavior.
- The normal unfiltered All Songs hierarchy must expose the existing `NodeType.Random` action; otherwise HPA-17 is inert.
- Append the synthetic Random row to `displayList`; do not insert it into `_currentSongList` and do not put it before authored rows.
- RANDOM SELECT is not exposed on filtered, Recent Plays, or Bookmarks projections in HPA-17.
- Descendant search starts from `_currentSongList`; do not use `_filteredView` or the whole library.
- Direct `NodeType.Score` nodes remain eligible in both modes.
- Recurse only through `NodeType.Box.Children`; `Random` and `BackBox` are ignored.
- CX has no `ScoreMidi` or `Unknown` `NodeType`; do not invent tests/contracts for them.
- Keep the current per-action `Random` construction and `Next(count)` behavior.
- After choosing a node, call existing `SelectSong(node)` and stop; do not add difficulty-selection state, `_isSelected`, or cursor behavior.
- Treat missing config state as Off/direct-only.
- Accept DTXManiaNX `RandomFromSubBox` on legacy read only; write only `RandomSelectFromSubBox`.
- Do not add tree services, flattened caches, cycle tracking, overlays, or public test APIs.
- One HPA-17 PR only.

## Risks / guards

- **Reachability:** `CreateRandomNode()` currently has no production caller. Task 0 is required before config work has user-visible value.
- **Null config:** existing random tests construct `SongSelectionStage` without `Activate()`, so `_configManager` can be null. The selection path must retain null tolerance.
- **Projection mismatch:** special tabs and filtered views are not backed by `_currentSongList`. Keep RANDOM SELECT out of those projections and reject non-All-Songs activation.
- **Difficulty carry-over:** `StartSongSelection()` passes `_currentDifficulty` unchanged. This remains intentional; downstream `SongChartHelper.GetCurrentDifficultyChart()` already returns a sole chart directly or clamps the index.

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

## Task 0: Make RANDOM SELECT reachable

**Estimate:** 0.1 engineer-day

**Production file:**

- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

**Test files:**

- `DTXMania.Test/Stage/SongSelectionStageLogicTests.cs`
- `DTXMania.Test/Stage/SongSelectionStageCoverageTests.cs` only if needed for the activation-path assertion

### 0.1 Write the reachability tests

Pin the smallest user-visible contract:

1. normal unfiltered `PopulateSongList()` produces authored rows plus exactly one tail `NodeType.Random` row;
2. the synthetic row is not added to `_currentSongList`;
3. root index zero remains the first authored node; sub-BOX index zero remains `BackBox`;
4. activating the synthetic row reaches the existing random-selection path and a one-direct-score list can transition to `SongTransition`.

Update any existing exact-count/order assertions that become stale because a displayed Random row now exists. Do not loosen them into vague `Contains` assertions.

### 0.2 Confirm RED

Use the existing Logic/Coverage test filters. Expected behavioral RED: no production path currently adds `CreateRandomNode()` to the display list.

### 0.3 Implement the synthetic row

In `PopulateSongList()` keep the current BackBox and authored-list construction, then append:

```csharp
displayList.Add(SongListNode.CreateRandomNode());
```

Append it **after** `displayList.AddRange(_currentSongList)` so HPA-17 does not change the current default selected row.

Do not add Random to `_currentSongList`, `PopulateFilteredSongList()`, Recent Plays, or Bookmarks.

### 0.4 Re-run focused tests until GREEN

### 0.5 Commit

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs DTXMania.Test/Stage
git commit -m "feat: expose random song selection"
```

---

## Task 1: Persist and expose the System toggle

**Estimate:** 0.4 engineer-day

**Production files:**

- `DTXMania.Game/Lib/Config/ConfigData.cs`
- `DTXMania.Game/Lib/Config/IConfigManager.cs`
- `DTXMania.Game/Lib/Config/ConfigManager.cs`
- `DTXMania.Game/Lib/Stage/ConfigStage.cs`

**Test files:**

- `DTXMania.Test/Config/ConfigDataTests.cs`
- `DTXMania.Test/Config/ConfigManagerTests.cs`
- `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`
- `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs` — interface stub only

### 1.1 Write config tests

Pin this CX contract:

```csharp
public bool RandomSelectFromSubBox { get; set; } = false;
void SetRandomSelectFromSubBox(bool value);
```

Cover:

1. CX default is `false`;
2. canonical `RandomSelectFromSubBox=true`, `1`, or `on` uses the existing boolean parser;
3. legacy NX `RandomFromSubBox=1` also loads as `true`;
4. invalid input follows current `TryParseBool` behavior;
5. setter changes the value and schedules deferred persistence only when the value changes;
6. SQLite snapshot/round-trip writes the canonical `RandomSelectFromSubBox` key;
7. persisted output does not write `RandomFromSubBox` or `RandSubBox`.

The read alias is migration input only; it is not a second CX storage key.

### 1.2 Keep the RED gate meaningful across the interface change

Adding an `IConfigManager` member breaks `ConfigManager` and `DrumConfigStageTests.StubConfigManager` at compile time before behavioral tests can execute. Do not treat an empty filtered run as sufficient RED evidence.

After the tests are written, restore compilation immediately by adding the interface member plus its matching implementation/stub signatures. Then run the focused tests and use the missing parse/snapshot/UI behavior as the behavioral RED signal.

### 1.3 Write Config-stage tests

Update the existing System inventory test explicitly:

```text
Screen Resolution
Fullscreen
VSync Wait
Audio Latency Offset
Song Folders
Random Select Sub-BOXes   <-- new
System Key Mapping
Import NX Scores
```

Also prove:

- description is `Include songs inside descendant BOXes when using RANDOM SELECT.`;
- display is normal `OFF` / `ON`;
- keyboard activation changes only this config value through the manager setter;
- categories remain System / Drums / Exit.

### 1.4 Implement the existing config path only

`ConfigData.cs`:

```csharp
public bool RandomSelectFromSubBox { get; set; } = false;
```

`IConfigManager.cs`:

```csharp
void SetRandomSelectFromSubBox(bool value);
```

`ConfigManager.cs`:

1. add `RandomSelectFromSubBox` to `ParseConfigLine()` and parse through `TryParseBool()`;
2. add a read-only legacy `RandomFromSubBox` case that assigns the same property through the same parser;
3. add one canonical `entries["RandomSelectFromSubBox"] = Config.RandomSelectFromSubBox.ToString();` in `BuildPersistedEntries()`;
4. add `SetRandomSelectFromSubBox(bool)` with `SetMetronome()` semantics: early return if unchanged, otherwise assign + `MarkDirty()`.

There is no known-key allowlist, default-value mapping, `ApplyEntry`, or second persistence pipeline to modify.

### 1.5 Add the System row

Create one `ToggleConfigItem` in `ConfigStage.SetupConfigItems()` and insert it immediately after `songFoldersItem` in `systemItems`.

Getter: `Config.RandomSelectFromSubBox`  
Setter: `SetRandomSelectFromSubBox`

No new panel/category.

### 1.6 Run focused config tests until GREEN

```text
FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigManagerSqlitePersistenceTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests
```

### 1.7 Commit

```bash
git add DTXMania.Game/Lib/Config DTXMania.Game/Lib/Stage/ConfigStage.cs \
  DTXMania.Test/Config DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs
git commit -m "feat: add random sub-box config toggle"
```

---

## Task 2: Expand RANDOM SELECT candidates on demand

**Estimate:** 0.4 engineer-day

**Production file:**

- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

**Test files:**

- `DTXMania.Test/Stage/SongSelectionStageLogicTests.cs`
- `DTXMania.Test/Stage/SongSelectionStageCoverageTests.cs`

Do not move this work into `SongSelectionStageBasicTests`; random-selection behavior is already covered in Logic/Coverage suites.

### 2.1 Write direct collector tests

Add the intended internal seam:

```csharp
internal static List<SongListNode> CollectRandomCandidates(
    IEnumerable<SongListNode> nodes,
    bool includeSubBoxes)
```

`InternalsVisibleTo("DTXMania.Test")` and `InternalsVisibleTo("DTXMania.Test.Mac")` already exist, so test it directly.

Pin:

1. Off returns direct `Score` nodes and excludes nested scores;
2. On returns direct plus one-level and multi-level nested scores;
3. `Random` and `BackBox` are ignored;
4. empty input and BOX-only input return an empty list;
5. traversal does not mutate node/list/navigation state.

Do not test ordering, distribution, or RNG seed behavior.

Do not reuse either existing always-recursing walker:

- `SongListFilterService.Flatten` has filtered-projection semantics and returns `FilteredSongResult`;
- private `SongManager.FlattenScoreNodes` cannot gate recursion for Off mode.

### 2.2 Add narrow `SelectRandomSong` behavior tests

Extend the existing headless random-selection coverage. New tests that need On mode must install an `IConfigManager` whose `Config` is a non-null `ConfigData`; a bare Moq config manager is not sufficient.

Pin:

1. missing `_configManager` or null `Config` -> no exception and direct-only behavior;
2. Off + only nested score -> no `ChangeStage`;
3. On + one nested score -> exactly one `ChangeStage(StageType.SongTransition, ...)` with that node as `selectedSong`;
4. non-All-Songs active tab -> no transition from hidden `_currentSongList`.

For the On nested-song test, set a nonzero/out-of-range-looking `_currentDifficulty` and assert the same value is passed as `selectedDifficulty`. This pins that HPA-17 does **not** reset difficulty. Do not add a duplicate clamp test here; existing `SongChartHelperTests` already owns and tests the downstream clamp behavior.

Keep the existing direct-score and no-playable-song tests unchanged except for setup needed by the null-safe config read. Do not duplicate the stage-level Random/BackBox exclusion case.

### 2.3 Run focused stage tests and confirm RED

```text
FullyQualifiedName~SongSelectionStageLogicTests|FullyQualifiedName~SongSelectionStageCoverageTests
```

Expected behavioral RED: missing collector, unchanged direct-only candidate construction, and missing non-All-Songs guard.

### 2.4 Implement the local collector

Single-pass rules:

```text
Score -> add
Box + includeSubBoxes -> recurse into Children
anything else -> ignore
```

Return a fresh `List<SongListNode>`. Do not add a shared extension/service, cache, or visited set.

### 2.5 Wire `SelectRandomSong()` with the smallest safe diff

Before candidate construction:

```csharp
if (_activeTab != SongSelectionTab.AllSongs)
    return;
```

Resolve the toggle null-safely:

```csharp
var includeSubBoxes = _configManager?.Config?.RandomSelectFromSubBox ?? false;
var songNodes = CollectRandomCandidates(_currentSongList, includeSubBoxes);
```

Then keep the existing:

```csharp
var random = new Random();
var randomSong = songNodes[random.Next(songNodes.Count)];
SelectSong(randomSong);
```

Do not navigate into the selected node's parent BOX, use `_filteredView`, reset `_currentDifficulty`, or introduce difficulty-selection state.

### 2.6 Run focused stage tests until GREEN

Use the filter from 2.3.

### 2.7 Commit

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs \
  DTXMania.Test/Stage/SongSelectionStageLogicTests.cs \
  DTXMania.Test/Stage/SongSelectionStageCoverageTests.cs
git commit -m "feat: include sub-box songs in random selection"
```

---

## Task 3: Regression verification and PR closeout

**Estimate:** 0.1 engineer-day

### 3.1 Run all focused HPA-17 tests together

```text
FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigManagerSqlitePersistenceTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests|FullyQualifiedName~SongSelectionStageLogicTests|FullyQualifiedName~SongSelectionStageCoverageTests
```

### 3.2 Run the full platform suite and build

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

Do not claim Windows validation from a macOS run.

### 3.3 Final scope review

Production changes should stay in the five expected files. Reject accidental additions of:

- generic tree-query/service code;
- cached flattened candidates;
- filter-aware or special-tab random behavior;
- BOX navigation mutations;
- new panel/category;
- a second persisted legacy key;
- RNG API/dependency-injection changes;
- difficulty-selection state.

Interface-driven test-double edits are allowed only where needed to compile.

### 3.4 Manual smoke check

Use an All Songs BOX containing a child BOX with a playable chart:

1. confirm the normal hierarchy shows `Random Select` at the tail;
2. Off: RANDOM SELECT ignores the nested-only song;
3. On: RANDOM SELECT transitions to `SongTransition` for the nested song;
4. Recent Plays / Bookmarks / filtered projections do not expose the Random row;
5. re-enter Config and confirm the toggle persisted.

### 3.5 Finish on the same PR

Update the draft PR checklist and Linear issue with actual validation results. Mark the PR ready only after implementation tests/build pass; do not create another PR.
