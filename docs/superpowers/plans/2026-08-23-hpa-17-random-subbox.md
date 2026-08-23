# HPA-17 Random Select From Sub-BOXes Implementation Plan

> **For implementation agents:** Use TDD and complete these tasks in order. Continue on this branch and this draft PR; do not open a second PR for HPA-17.

**Goal:** Add a persisted System toggle that lets RANDOM SELECT include `NodeType.Score` songs from descendant BOXes below the currently open song list.

**Architecture:** Extend the existing scalar config switch/snapshot path and System menu. Add one `internal static` candidate collector to `SongSelectionStage`; `SelectRandomSong()` uses it and then keeps the existing `Random` + `SelectSong()` -> `SongTransition` flow unchanged. No new service, cache, panel, migration layer, or RNG abstraction.

**Tech stack:** .NET 8, MonoGame, xUnit, SQLite-backed configuration.

**Spec:** `docs/superpowers/specs/2026-08-23-hpa-17-random-subbox-design.md`

## Global constraints

- Default Off preserves current direct-only RANDOM SELECT behavior.
- Descendant search starts from `_currentSongList`; do not use `_filteredView` or the whole library.
- Direct `NodeType.Score` nodes remain eligible in both modes.
- Recurse only through `NodeType.Box.Children`; `Random` and `BackBox` are ignored.
- CX has no `ScoreMidi` or `Unknown` `NodeType`; do not invent tests/contracts for them.
- Keep the current per-action `Random` construction and `Next(count)` behavior.
- After choosing a node, call existing `SelectSong(node)` and stop; do not add difficulty-selection state, `_isSelected`, or cursor behavior.
- Do not add tree services, flattened caches, cycle tracking, config aliases, overlays, or public test APIs.
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

### 1.1 Write failing config tests

Pin this contract:

```csharp
public bool RandomSelectFromSubBox { get; set; } = false;
void SetRandomSelectFromSubBox(bool value);
```

Cover:

1. default is `false`;
2. `RandomSelectFromSubBox=true`, `1`, and `on` parse through the existing boolean parser;
3. invalid input follows current `TryParseBool` behavior;
4. setter changes the value and schedules deferred persistence only when the value changes;
5. SQLite round-trip preserves Off and On under exactly `RandomSelectFromSubBox`;
6. no `RandSubBox` / `RandomFromSubBox` alias is accepted or written.

### 1.2 Write failing Config-stage tests

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

Update `DrumConfigStageTests.StubConfigManager` with the new interface method in the same change.

### 1.3 Run focused config tests and confirm RED

```text
FullyQualifiedName~ConfigDataTests|FullyQualifiedName~ConfigManagerTests|FullyQualifiedName~ConfigManagerSqlitePersistenceTests|FullyQualifiedName~ConfigStageLogicTests|FullyQualifiedName~DrumConfigStageTests
```

### 1.4 Implement the existing config path only

`ConfigData.cs`:

```csharp
public bool RandomSelectFromSubBox { get; set; } = false;
```

`IConfigManager.cs`:

```csharp
void SetRandomSelectFromSubBox(bool value);
```

`ConfigManager.cs` — mirror `Metronome`:

1. add one `case "RandomSelectFromSubBox"` to `ParseConfigLine()` and parse with `TryParseBool()`;
2. add one `entries["RandomSelectFromSubBox"] = Config.RandomSelectFromSubBox.ToString();` in `BuildPersistedEntries()`;
3. add `SetRandomSelectFromSubBox(bool)` with early return when unchanged, then assignment + `MarkDirty()`.

There is no known-key allowlist, default-value mapping, `ApplyEntry`, or second persistence pipeline to modify.

### 1.5 Add the System row

Create one `ToggleConfigItem` in `ConfigStage.SetupConfigItems()` and insert it immediately after `songFoldersItem` in `systemItems`.

Getter: `Config.RandomSelectFromSubBox`  
Setter: `SetRandomSelectFromSubBox`

No new panel/category.

### 1.6 Re-run focused config tests until GREEN

Use the filter from 1.3.

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

Do not add this work to `SongSelectionStageBasicTests`; random-selection behavior is already covered in Logic/Coverage suites.

### 2.1 Write failing direct collector tests

Add the intended test seam:

```csharp
internal static List<SongListNode> CollectRandomCandidates(
    IEnumerable<SongListNode> nodes,
    bool includeSubBoxes)
```

`InternalsVisibleTo("DTXMania.Test")` already exists, so test it directly.

Pin:

1. Off returns direct `Score` nodes and excludes nested scores;
2. On returns direct plus one-level and multi-level nested scores;
3. `Random` and `BackBox` are ignored;
4. empty input and BOX-only input return an empty list;
5. traversal does not mutate node/list/navigation state.

Do not test ordering, distribution, or RNG seed behavior.

### 2.2 Add the two behavior-changing `SelectRandomSong` tests

Extend the existing headless random-selection coverage with one child BOX containing exactly one score:

1. `RandomSelectFromSubBox=false` + no direct scores -> no `ChangeStage`;
2. `RandomSelectFromSubBox=true` + one nested score -> exactly one `ChangeStage(StageType.SongTransition, ...)`, and shared data `selectedSong` is that nested node.

Keep the existing single-direct-score test unchanged as the regression that current selection still works.

Do not assert `_selectedSong`, difficulty bars, a difficulty-selection phase, `_isSelected`, or a cursor; none belongs to this current flow.

### 2.3 Run focused stage tests and confirm RED

```text
FullyQualifiedName~SongSelectionStageLogicTests|FullyQualifiedName~SongSelectionStageCoverageTests
```

Expected RED reasons: missing collector and unchanged direct-only candidate construction.

### 2.4 Implement the local collector

Single-pass rules:

```text
Score -> add
Box + includeSubBoxes -> recurse into Children
anything else -> ignore
```

Return a fresh `List<SongListNode>`. Do not reuse `SongListFilterService.Flatten`, add a shared extension/service, cache results, or add a visited set.

### 2.5 Wire `SelectRandomSong()` with the smallest diff

Replace only:

```csharp
var songNodes = _currentSongList.FindAll(n => n.Type == NodeType.Score);
```

with:

```csharp
var songNodes = CollectRandomCandidates(
    _currentSongList,
    _configManager.Config.RandomSelectFromSubBox);
```

Keep the existing:

```csharp
var random = new Random();
var randomSong = songNodes[random.Next(songNodes.Count)];
SelectSong(randomSong);
```

Do not navigate into the selected node's parent BOX and do not touch `_filteredView`.

### 2.6 Re-run focused stage tests until GREEN

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

**Estimate:** 0.15 engineer-day

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
- filter/search changes;
- BOX navigation mutations;
- new panel/category;
- config migration/aliases;
- RNG API/dependency-injection changes;
- difficulty-selection state.

Interface-driven test-double edits are allowed only where needed to compile.

### 3.4 Optional manual smoke check

From a BOX containing a child BOX with a playable chart:

1. Off: RANDOM SELECT ignores the nested song.
2. On: RANDOM SELECT transitions to `SongTransition` for the nested song.
3. Re-enter Config and confirm the toggle persisted.

### 3.5 Finish on the same PR

Update the draft PR checklist and Linear issue with actual validation results. Mark the PR ready only after implementation tests/build pass; do not create another PR.