# HPA-17 Random Select From Sub-BOXes Design

**Status:** Proposed for implementation  
**Linear:** HPA-17  
**Scope:** DTXManiaCX System configuration and RANDOM SELECT candidate expansion  
**Estimate:** About 1 engineer day

## Context

`SongSelectionStage.SelectRandomSong()` currently builds candidates only from direct `NodeType.Score` entries in `_currentSongList`. Songs below child `NodeType.Box` nodes are therefore excluded even though `SongListNode` already exposes the required tree through `Children`.

HPA-17 restores the DTXManiaNX `RandSubBox` behavior as a System-menu toggle. The setting controls whether RANDOM SELECT stays at the current BOX level or also considers songs in descendant BOXes.

The current stage already owns both the active song-list context and `IConfigManager`. Configuration already flows through `ConfigData`, `ConfigManager`, SQLite `ConfigEntries`, and `ConfigStage`, so this feature does not need a new service or data model.

## Goals

- Add a persisted System toggle for including descendant BOXes in RANDOM SELECT.
- Preserve today's direct-only behavior when the toggle is Off.
- When On, include eligible songs from every descendant BOX below the current list.
- Reuse the existing song-selection transition once a candidate is chosen.
- Keep candidate collection independently testable without graphics or random-number injection.

## Non-goals

- Random selection across the entire song library regardless of the current BOX.
- Searching parent BOXes, sibling BOXes, or other configured song roots.
- Changing normal BOX navigation or opening BOXes during random selection.
- Changing active filter/search semantics. RANDOM SELECT continues to use `_currentSongList`, not `_filteredView`.
- Adding weights, history avoidance, difficulty filtering, or retry rules.
- Including `NodeType.ScoreMidi`; the existing random-pick contract remains `NodeType.Score` only.
- Adding a generic song-tree query service, cached flattened index, RNG abstraction, or new configuration panel.
- Migrating a legacy key or adding a `Config.ini` alias.

## Approaches considered

### 1. Local depth-first collector in `SongSelectionStage` — chosen

Extract a private static helper that collects direct scores and, when enabled, recursively visits child `Box.Children`. `SelectRandomSong()` consumes that list and keeps its existing random choice and transition behavior.

This uses the data already owned by the stage, is easy to characterize with headless tests, and adds no lifecycle or synchronization concerns.

### 2. Generic song-library tree query service

A reusable service could expose recursive searches for multiple future features. HPA-17 has only one caller and one predicate, so this would introduce an abstraction before a second use case exists.

### 3. Cached flattened candidate lists

The stage could precompute descendant candidates per BOX. The candidate set is only needed on a RANDOM SELECT action, while song reloads and tree replacement would create cache invalidation work. An on-demand traversal is simpler and easily fast enough for a user-triggered action.

## Configuration contract

Add one property to `ConfigData`:

```csharp
public bool RandomSelectFromSubBox { get; set; } = false;
```

`false` preserves current CX behavior for existing and fresh configurations.

Add `SetRandomSelectFromSubBox(bool value)` to `IConfigManager` and `ConfigManager`. Follow the existing scalar-boolean path:

- include `RandomSelectFromSubBox` in the known-key allowlist;
- return `false` from the default-value mapping;
- parse it through the existing boolean parser in `ApplyEntry`;
- include it in the SQLite snapshot written to `ConfigEntries`;
- mark deferred persistence dirty only when the value actually changes.

The canonical persisted key is exactly `RandomSelectFromSubBox`. There is no compatibility alias because CX has not previously shipped this setting.

## Config-stage interaction

Add one `ToggleConfigItem` to the existing **System** category:

```text
Name: Random Select Sub-BOXes
Value: ON / OFF
Description: Include songs inside descendant BOXes when using RANDOM SELECT.
```

Place it after `Song Folders`, keeping song-library behavior together before key mapping and score import. The row directly calls `SetRandomSelectFromSubBox`; no overlay or navigation item is added.

The setting is read when RANDOM SELECT is activated. There is no stage-level cached copy and no change event because the Config stage and Song Selection stage are not active simultaneously.

## Candidate traversal contract

Keep candidate collection private to `SongSelectionStage`, conceptually:

```text
CollectRandomCandidates(nodes, includeSubBoxes):
    for each node in nodes:
        if node.Type == Score:
            add node
        else if includeSubBoxes and node.Type == Box:
            recursively visit node.Children
```

Rules:

- the traversal root is the current `_currentSongList`;
- direct `Score` nodes are always candidates;
- recursion follows only `Box` nodes and may span any descendant depth;
- `ScoreMidi`, `Random`, `BackBox`, and `Unknown` nodes are ignored;
- the existing `Score != null` guard remains before entering difficulty selection;
- duplicate/cycle protection is not added because the loaded song model is an authored tree with parent/child ownership;
- no navigation state, current BOX, filter state, or list contents are mutated.

The helper returns a new local list for one action. No candidate cache is retained.

## Random choice and transition

`SelectRandomSong()` asks the helper for candidates using the live configuration value. When there are no candidates, it remains a no-op, matching current behavior.

When candidates exist, keep the current per-action `Random` construction and `Next(candidates.Count)` selection. HPA-17 changes only candidate scope; it does not change random-number behavior or add an injectable random service. Tests use a single-candidate tree for the narrow selection integration case.

After choosing a valid score, preserve the existing sequence:

1. assign `_selectedSong`;
2. build difficulty-selection bars;
3. move to `SelectionStage.DifficultySelection`;
4. set `_isSelected = true`;
5. reset the difficulty cursor to zero.

A descendant song is selected directly; RANDOM SELECT does not navigate into or visually open its parent BOX first.

## Testing strategy

Extend existing suites; do not add a new harness.

### Configuration

In `ConfigDataTests`, `ConfigManagerTests`, and `ConfigManagerSqlitePersistenceTests`, prove:

- default is Off;
- the setter changes the value and uses normal deferred persistence;
- repeated no-op assignment does not create extra dirty work;
- SQLite round trip preserves both Off and On;
- malformed boolean input follows the existing config policy rather than adding special parsing.

### Config UI

In `ConfigStageLogicTests`, prove:

- the System category contains `Random Select Sub-BOXes` after `Song Folders`;
- its initial ON/OFF display reflects the config value;
- activating/toggling it mutates the value through the manager setter;
- no new category, overlay, or navigation row is introduced.

### Song selection

Use `SongSelectionStageBasicTests` and its existing reflection/headless conventions. Characterize the private collector with small `SongListNode` trees:

- Off returns direct scores only;
- On returns direct plus one-level and multi-level descendant scores;
- non-Score node types are ignored;
- an empty tree produces no candidates;
- a single nested valid score is selectable only when the setting is On and reaches the existing difficulty-selection state.

Do not test random distribution or seed behavior.

## Acceptance criteria

- A persisted Off setting leaves RANDOM SELECT limited to direct songs in the current list.
- A persisted On setting includes direct songs and songs under descendant BOXes of the current list.
- RANDOM SELECT never escapes to ancestors, siblings, or unrelated roots.
- Existing direct-score selection, random choice, and difficulty transition remain unchanged.
- Configuration and candidate behavior are covered by headless unit tests.
- The macOS test/build projects and Windows CI build without a new service, panel, cache, or schema migration.

## Expected production touch points

- `DTXMania.Game/Lib/Config/ConfigData.cs`
- `DTXMania.Game/Lib/Config/IConfigManager.cs`
- `DTXMania.Game/Lib/Config/ConfigManager.cs`
- `DTXMania.Game/Lib/Stage/ConfigStage.cs`
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

Expected tests:

- `DTXMania.Test/Config/ConfigDataTests.cs`
- `DTXMania.Test/Config/ConfigManagerTests.cs`
- `DTXMania.Test/Config/ConfigManagerSqlitePersistenceTests.cs`
- `DTXMania.Test/Config/ConfigStageLogicTests.cs`
- `DTXMania.Test/Stage/SongSelectionStageBasicTests.cs`

## Size and scope guard

The feature should remain about **1 engineer day** and one implementation PR. If implementation reveals that RANDOM SELECT must also honor filtered views, cross-root selection, or another node type, stop and create a separate ticket rather than expanding HPA-17.
