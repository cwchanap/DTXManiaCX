# HPA-17 Random Select From Sub-BOXes Design

**Status:** Proposed for implementation  
**Linear:** HPA-17  
**Scope:** DTXManiaCX RANDOM SELECT reachability, System configuration, and descendant-BOX candidate expansion  
**Estimate:** About 1 engineer day

## Context

`SongSelectionStage.SelectRandomSong()` currently collects only direct `NodeType.Score` entries from `_currentSongList`, chooses one with the existing per-action `Random`, and passes it to `SelectSong()`. `SelectSong()` validates the node and immediately transitions to `StageType.SongTransition` with the current difficulty.

However, that path is not currently reachable in the shipped UI. `HandleSongActivation()` has a `NodeType.Random` arm and `SongListNode.CreateRandomNode()` exists, but production code never inserts that synthetic node into a displayed song list. HPA-17 must expose the existing RANDOM SELECT action as well as configure its sub-BOX scope; otherwise the new setting would be inert.

HPA-17 adds the DTXManiaNX-style `RandSubBox` behavior as a CX System toggle: RANDOM SELECT may optionally include scores from descendant BOXes below the currently open list.

The feature fits existing seams. `SongListNode.Children` already models the tree, the stage already owns `_currentSongList` and `_configManager`, and configuration already persists through `ConfigData` + `ConfigManager` + SQLite `ConfigEntries` with a legacy `Config.ini` import path.

## Goals

- Make the existing RANDOM SELECT action reachable from the normal unfiltered All Songs hierarchy.
- Add a persisted System toggle for including descendant BOXes in RANDOM SELECT.
- Preserve current direct-only candidate behavior when the toggle is Off.
- When On, include direct scores plus scores inside descendant BOXes of the current list.
- Preserve the existing random choice and `SelectSong()` -> `SongTransition` flow.
- Keep the implementation local and directly testable without graphics.

## Non-goals

- Random selection across the whole library, parent BOXes, siblings, or unrelated song roots.
- Filter-aware random selection.
- RANDOM SELECT rows on Recent Plays or Bookmarks projections.
- Adding weights, recent-song avoidance, difficulty filtering, or retry policy.
- Adding node types that do not exist in CX.
- Adding a generic tree-query service, cached flattened candidates, panel, cycle tracker, or RNG abstraction.
- Changing the current random-number construction or song-confirmation behavior.

## Reuse decisions

### RANDOM SELECT row

Reuse the existing `SongListNode.CreateRandomNode()` factory and `HandleSongActivation()` `NodeType.Random` switch arm.

`PopulateSongList()` should add one synthetic RANDOM SELECT row to the displayed hierarchy. Append it **after** `_currentSongList` rather than placing it before the songs:

```text
root:    [current songs / BOXes..., Random]
sub-BOX: [BackBox, current songs / BOXes..., Random]
```

Appending preserves the current initial/selected index behavior: root lists still start on their first authored node, and sub-BOX lists retain `BackBox` at index zero. The synthetic Random row lives only in `displayList`; it is not inserted into `_currentSongList`, so it cannot become its own random candidate.

This row is intentionally supplied only by the normal hierarchical `PopulateSongList()` path. Recent Plays, Bookmarks, and filtered projections use different population methods and do not expose RANDOM SELECT in HPA-17. Filter-aware or special-tab random selection is separate work.

### Configuration

Add one scalar property:

```csharp
public bool RandomSelectFromSubBox { get; set; } = false;
```

Add `SetRandomSelectFromSubBox(bool value)` to `IConfigManager` and `ConfigManager`.

Follow the existing `Metronome` path:

- parse `RandomSelectFromSubBox` in `ParseConfigLine()` through `TryParseBool()`;
- also accept legacy DTXManiaNX `RandomFromSubBox` on **read only** through the same parser;
- add one canonical `RandomSelectFromSubBox` entry to `BuildPersistedEntries()`;
- implement the setter with the same early-return + `MarkDirty()` behavior as `SetMetronome()`.

Defaults remain on the `ConfigData` property initializer. There is no known-key allowlist, default-value map, or `ApplyEntry` seam to extend.

CX deliberately defaults this new setting Off to preserve current CX candidate behavior. When importing an NX `Config.ini`, an explicit `RandomFromSubBox=1` is honored. CX writes only the canonical `RandomSelectFromSubBox` key after import; it does not preserve or emit the legacy name.

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

Add one `internal static` helper on `SongSelectionStage`, using the existing `InternalsVisibleTo("DTXMania.Test")` and Mac friend-assembly seams:

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

Two existing walkers were considered and are deliberately not reused:

- `SongListFilterService.Flatten` always recurses and returns `FilteredSongResult` for filtering/projection.
- `SongManager.FlattenScoreNodes` is a private always-recursing score collector.

Neither represents the required per-action **gated recursion** from the currently open list. A small local collector is narrower than changing either shared walker.

Additional constraints:

- traversal root is `_currentSongList`;
- direct scores are eligible in both modes;
- recursion can span any descendant BOX depth;
- no navigation/filter/list state is mutated;
- return a fresh list for the current RANDOM SELECT action;
- no cache or visited set is introduced for the authored parent/child tree.

## Random choice and transition

`SelectRandomSong()` first rejects non-All-Songs tabs. The UI does not expose a Random row there, and the guard prevents stale/programmatic activation from selecting from the hidden `_currentSongList` while Recent Plays or Bookmarks is visible.

For All Songs, resolve the toggle null-safely:

```csharp
var includeSubBoxes = _configManager?.Config?.RandomSelectFromSubBox ?? false;
var songNodes = CollectRandomCandidates(_currentSongList, includeSubBoxes);
```

The null fallback is intentionally direct-only. Headless tests and partially initialized stages already tolerate a missing config manager; HPA-17 must preserve that contract.

Everything after candidate construction stays as it is today:

1. if the candidate list is empty, do nothing;
2. construct the current per-action `Random`;
3. choose `songNodes[random.Next(songNodes.Count)]`;
4. call `SelectSong(randomSong)`;
5. `SelectSong()` / `StartSongSelection()` transitions to `StageType.SongTransition` with `selectedSong` and `_currentDifficulty`.

There is no difficulty-selection phase, stage-level `_isSelected`, or cursor update in this flow. The current difficulty index is deliberately carried through unchanged. Downstream chart resolution already handles this safely: `SongChartHelper.GetCurrentDifficultyChart()` returns the sole drum chart directly or clamps the requested index to the available chart range. HPA-17 therefore must not invent a second difficulty-selection step.

A descendant score is selected directly; RANDOM SELECT does not open its parent BOX first.

## Testing strategy

Extend existing suites; do not add a new harness.

### Reachability

Use existing `SongSelectionStage` logic/coverage tests to prove:

- normal unfiltered All Songs display appends one `NodeType.Random` row;
- the row remains outside `_currentSongList` and at the tail so existing first-node selection behavior is preserved;
- activating that row reaches the existing `SelectRandomSong()` path and can transition for a single direct score;
- existing exact-list/count assertions affected by the synthetic row are updated intentionally.

Do not add the Random row to filtered, Recent Plays, or Bookmarks projections in this ticket.

### Configuration

Use `ConfigDataTests`, `ConfigManagerTests`, and `ConfigManagerSqlitePersistenceTests` to prove:

- CX default is Off;
- `RandomSelectFromSubBox=true`, `1`, and `on` parse through the existing boolean parser;
- legacy `RandomFromSubBox=1` imports as On;
- setter changes mark deferred persistence dirty, while repeated same-value calls do not;
- SQLite round-trip writes/reads the canonical `RandomSelectFromSubBox` key;
- the legacy alias is read-only and is not emitted by the persisted snapshot.

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

- missing config manager/config -> direct-only behavior, no exception;
- Off + only nested score -> no `ChangeStage`;
- On + one nested score -> one `StageType.SongTransition` whose shared data contains that nested node as `selectedSong`;
- non-All-Songs tab -> no transition from the hidden All Songs tree.

Keep the existing direct-score and no-playable-song tests as regressions; do not duplicate their Random/BackBox assertions at the stage level.

No new chart-clamping unit test is required for HPA-17 because `SongChartHelperTests` already pins out-of-range difficulty clamping. The nested-selection test should only prove that HPA-17 preserves the existing `selectedDifficulty` handoff rather than resetting it.

## Risks and guards

- **Reachability:** without a synthetic Random row, the entire feature is inert. Reachability is an explicit prerequisite, not optional polish.
- **Partially initialized stages:** `_configManager` can be null outside `Activate()`. Treat null as the Off/default mode rather than dereferencing it.
- **Projection mismatch:** Recent Plays, Bookmarks, and filtered displays are not backed by `_currentSongList`. RANDOM SELECT stays hidden there; non-All-Songs activation is guarded.
- **Difficulty carry-over:** descendant songs may have fewer charts than the visible cursor context. Preserve `_currentDifficulty`; downstream chart resolution already clamps it.

## Acceptance criteria

- Normal unfiltered All Songs lists expose a synthetic RANDOM SELECT row without changing the first authored selection position.
- Activating that row reaches the existing `SelectRandomSong()` path.
- Off keeps RANDOM SELECT limited to direct scores in `_currentSongList`.
- On includes direct scores and scores under descendant BOXes of `_currentSongList`.
- RANDOM SELECT never escapes to ancestors, siblings, unrelated roots, filtered projections, Recent Plays, or Bookmarks.
- Missing config state falls back to current direct-only behavior without throwing.
- Existing random choice and `SelectSong()` -> `SongTransition` confirmation remain unchanged.
- The current difficulty handoff remains unchanged; no new difficulty-selection state is added.
- The setting persists via the existing SQLite config path, accepts NX `RandomFromSubBox` on legacy import, and writes only the CX canonical key.
- The System toggle appears after `Song Folders`.
- Tests cover reachability, collector behavior, null/config guards, and Off/On transitions without a graphics harness.

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

Keep this at about **1 engineer day** and one PR. Filter-aware random selection, special-tab random selection, whole-library selection, new node categories, RNG changes, or new navigation behavior are separate tickets.
