# HPA-17 Random Select From Sub-BOXes Design

**Status:** Proposed for implementation  
**Linear:** HPA-17  
**Scope:** DTXManiaCX System configuration and RANDOM SELECT candidate expansion  
**Estimate:** About 1 engineer day

## Context

`SongSelectionStage.SelectRandomSong()` currently collects only direct `NodeType.Score` entries from `_currentSongList`, chooses one with the existing per-action `Random`, and passes it to `SelectSong()`. `SelectSong()` validates the node and immediately transitions to `StageType.SongTransition` with the current difficulty.

HPA-17 adds the DTXManiaNX-style `RandSubBox` behavior as a CX System toggle: RANDOM SELECT may optionally include scores from descendant BOXes below the currently open list.

The feature fits existing seams. `SongListNode.Children` already models the tree, the stage already owns `_currentSongList` and `_configManager`, and configuration already persists through `ConfigData` + `ConfigManager` + SQLite `ConfigEntries`.

## Goals

- Add a persisted System toggle for including descendant BOXes in RANDOM SELECT.
- Preserve current direct-only behavior when the toggle is Off.
- When On, include direct scores plus scores inside descendant BOXes of the current list.
- Preserve the existing random choice and `SelectSong()` -> `SongTransition` flow.
- Keep the implementation local and directly testable without graphics.

## Non-goals

- Random selection across the whole library, parent BOXes, siblings, or unrelated song roots.
- Changing filter/search semantics; RANDOM SELECT continues to use `_currentSongList`, not `_filteredView`.
- Adding weights, recent-song avoidance, difficulty filtering, or retry policy.
- Adding node types that do not exist in CX.
- Adding a generic tree-query service, cached flattened candidates, panel, migration alias, cycle tracker, or RNG abstraction.
- Changing the current random-number construction or song-confirmation behavior.

## Reuse decisions

### Configuration

Add one scalar property:

```csharp
public bool RandomSelectFromSubBox { get; set; } = false;
```

Add `SetRandomSelectFromSubBox(bool value)` to `IConfigManager` and `ConfigManager`.

Follow the existing `Metronome` path exactly:

- parse one `RandomSelectFromSubBox` case in `ParseConfigLine()` through `TryParseBool()`;
- add one `RandomSelectFromSubBox` entry to `BuildPersistedEntries()`;
- implement the setter with the same early-return + `MarkDirty()` behavior as `SetMetronome()`.

Defaults remain on the `ConfigData` property initializer. There is no known-key allowlist, default-value map, or `ApplyEntry` seam to extend.

The canonical persisted key is exactly `RandomSelectFromSubBox`. Do not accept or write `RandSubBox` / `RandomFromSubBox` aliases.

### Config UI

Add one normal `ToggleConfigItem` to the existing **System** category immediately after `Song Folders`:

```text
Name: Random Select Sub-BOXes
Value: ON / OFF
Description: Include songs inside descendant BOXes when using RANDOM SELECT.
```

The row reads `Config.RandomSelectFromSubBox` and calls `SetRandomSelectFromSubBox`. No overlay, category, or navigation screen is added.

The setting is read when RANDOM SELECT is activated; do not cache it or subscribe to changes.

### Candidate collection

Add one `internal static` helper on `SongSelectionStage`, using the existing `InternalsVisibleTo("DTXMania.Test")` test seam:

```csharp
internal static List<SongListNode> CollectRandomCandidates(
    IEnumerable<SongListNode> nodes,
    bool includeSubBoxes)
```

Rules:

```text
Score -> add
Box + includeSubBoxes -> recurse into Children
anything else -> ignore
```

CX `NodeType` contains only `Score`, `Box`, `BackBox`, and `Random`. Tests should pin those real types only.

Additional constraints:

- traversal root is `_currentSongList`;
- direct scores are eligible in both modes;
- recursion can span any descendant BOX depth;
- no navigation/filter/list state is mutated;
- return a fresh list for the current RANDOM SELECT action;
- no cache or visited set is introduced for the authored parent/child tree.

Do not reuse `SongListFilterService.Flatten`; that API always recurses and produces `FilteredSongResult`, which does not match this gated candidate contract.

## Random choice and transition

`SelectRandomSong()` replaces only its current direct `FindAll(NodeType.Score)` candidate construction:

```csharp
var songNodes = CollectRandomCandidates(
    _currentSongList,
    _configManager.Config.RandomSelectFromSubBox);
```

Everything after candidate construction stays as it is today:

1. if the candidate list is empty, do nothing;
2. construct the current per-action `Random`;
3. choose `songNodes[random.Next(songNodes.Count)]`;
4. call `SelectSong(randomSong)`;
5. `SelectSong()` / `StartSongSelection()` transitions to `StageType.SongTransition` with `selectedSong` and `_currentDifficulty`.

There is no difficulty-selection phase, stage-level `_isSelected`, or cursor update in this flow. HPA-17 must not introduce any of them.

A descendant score is selected directly; RANDOM SELECT does not open its parent BOX first.

## Testing strategy

Extend existing suites; do not add a new harness.

### Configuration

Use `ConfigDataTests`, `ConfigManagerTests`, and `ConfigManagerSqlitePersistenceTests` to prove:

- default is Off;
- `true`, `1`, and `on` parse through the existing boolean parser;
- setter changes mark deferred persistence dirty, while repeated same-value calls do not;
- SQLite round-trip preserves Off and On under exactly `RandomSelectFromSubBox`;
- no compatibility alias is written or accepted.

Update `DrumConfigStageTests.StubConfigManager` for the new `IConfigManager` method so the interface edit compiles.

### Config UI

Extend `ConfigStageLogicTests`:

- update the existing `Assert.Collection` inventory so `Random Select Sub-BOXes` appears immediately after `Song Folders`;
- assert its ON/OFF display follows the config value;
- assert keyboard toggle uses the manager setter;
- keep the existing three-category structure unchanged.

### Song selection

Use the stage's existing logic/coverage suites instead of `SongSelectionStageBasicTests`.

Directly test `CollectRandomCandidates` through the internal test seam:

- Off: direct scores only;
- On: direct + nested + multi-level BOX scores;
- `Random` and `BackBox` are ignored;
- empty / BOX-only inputs return no candidates.

Extend existing `SelectRandomSong` tests with deterministic one-candidate trees:

- Off + only nested score -> no `ChangeStage`;
- On + one nested score -> one `StageType.SongTransition` whose shared data contains that nested node as `selectedSong`.

Keep the existing direct-score test as the regression for unchanged current behavior. Do not test random distribution.

## Acceptance criteria

- Off keeps RANDOM SELECT limited to direct scores in `_currentSongList`.
- On includes direct scores and scores under descendant BOXes of `_currentSongList`.
- RANDOM SELECT never escapes to ancestors, siblings, unrelated roots, or filtered-view projection.
- Existing random choice and `SelectSong()` -> `SongTransition` confirmation remain unchanged.
- The setting persists via the existing SQLite config path and appears after `Song Folders`.
- Tests cover the collector plus Off/On transition behavior without a graphics harness.

## Expected production touch points

- `DTXMania.Game/Lib/Config/ConfigData.cs`
- `DTXMania.Game/Lib/Config/IConfigManager.cs`
- `DTXMania.Game/Lib/Config/ConfigManager.cs`
- `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

Expected test touch points:

- `DTXMania.Test/Config/ConfigDataTests.cs`
- `DTXMania.Test/Config/ConfigManagerTests.cs`
- `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`
- `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- `DTXMania.Test/Stage/DrumConfig/DrumConfigStageTests.cs` (interface stub only)
- `DTXMania.Test/Stage/SongSelectionStageLogicTests.cs`
- `DTXMania.Test/Stage/SongSelectionStageCoverageTests.cs`

## Stop line

Keep this at about **1 engineer day** and one PR. Filter-aware random selection, whole-library selection, new node categories, RNG changes, or navigation behavior are separate tickets.