# Recent Plays Tab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a top tab bar to the Song Selection stage with an "All Songs" tab (existing browse view) and a "Recent" tab that shows the 20 most-recently-played songs, switchable via the Tab key and the Low Tom drum pad.

**Architecture:** A new DB query returns the most-recently-played songs (grouped per song by max `LastPlayedAt`); `SongManager` converts them into flat `SongListNode`s using the existing node builder. The stage holds an `_activeTab` enum, swaps `SongListDisplay.CurrentList` between the browse/filter view and the cached recent-plays list, renders a small tab bar, and routes Tab-key/Low-Tom input to a `SwitchToNextTab()` method.

**Tech Stack:** .NET 8, EF Core (SQLite), MonoGame, xUnit + Moq. Tests live in `DTXMania.Test/` and must stay graphics-free (the existing `SongSelectionStage*` and `SongDatabaseService*` tests already run headlessly on the Mac project).

---

## Spec reference

`docs/superpowers/specs/2026-06-03-recent-plays-tab-design.md`

## Key facts discovered during planning (read before starting)

- **Data source:** `SongScore.LastPlayedAt` (`DateTime?`) is set on every play via `SongDatabaseService.UpdateScoreAsync`. The `PerformanceHistory` table is unused and stays untouched.
- **Entity relationships:** `Song.Id` → `SongChart.SongId`/`SongChart.Song` (`ICollection<SongChart> Song.Charts`); `SongChart.Id` → `SongScore.ChartId`/`SongScore.Chart` (`ICollection<SongScore> SongChart.Scores`). DbSets: `context.Songs`, `context.SongCharts`, `context.SongScores`.
- **Node builder:** `SongManager.CreateSongNodeFromDatabaseEntities(Song song, SongChart[] charts)` (private, returns `SongListNode?`) is the canonical full-node builder; it calls `node.PopulatePlayHistoryFromCharts(charts)` which reads `chart.Scores`.
- **Singleton:** `SongManager.Instance`; the stage already uses it (e.g. `ResultStage` calls `SongManager.Instance.UpdateScoreAsync`).
- **List display:** set the visible list via `_songListDisplay.CurrentList = <List<SongListNode>>`. `SongListDisplay.SelectedIndex` is settable.
- **Stage list population:** `PopulateSongListForCurrentMode()` chooses between `PopulateFilteredSongList()` and `PopulateSongList()`. The empty-filter message is gated by `_showEmptyFilterMessage` and drawn in `OnDraw` (line ~1040).
- **Input nuance:** `InputManager` enqueues commands for mapped keys; the queue is drained only by `ProcessInputCommands()`, which is **skipped while the search modal is open** (`HandleInput` returns early). Therefore Tab must **not** be added to the command key-map (queued `NextTab` commands would accumulate during modal use and fire on close). Instead, raw-poll Tab in the non-modal path, mirroring `DetectOpenSearchKey()` which raw-polls `Keys.Back`. `_inputManager.IsKeyPressed((int)key)` also picks up MCP/E2E injected keys (the compat override includes injected state).
- **Drum input:** the game's `InputManager` is an `InputManagerCompat` exposing `ModularInputManager` (`public ModularInputManager ModularInputManager`), which raises `public event EventHandler<LaneHitEventArgs> OnLaneHit`. `LaneHitEventArgs.Lane` uses the **KeyBindings lane scheme where Low Tom = lane 8** (shared with Right Cymbal; default key "L"). This is NOT the visual `PerformanceUILayout.LaneType.LT = 6`.
- **Test helpers** (in `DTXMania.Test/Stage/SongSelectionStageLogicTests.cs`): `CreateStage()`, `AttachCoreUi(stage, display: ...)`, plus `SetPrivateField`/`GetPrivateField`/`InvokePrivateMethod` from `DTXMania.Test.TestData.ReflectionHelpers`. DB tests (`DTXMania.Test/Song/SongDatabaseServiceTests.cs`) use a temp-file `SongDatabaseService`, call `InitializeDatabaseAsync()`, then `AddSongAsync(song, chart)`.

## File structure

- **Create:** `DTXMania.Game/Lib/Song/SongSelectionTab.cs` — the `SongSelectionTab` enum + a pure `NextTab` cycle helper. One responsibility: tab identity and ordering.
- **Modify:** `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs` — add `GetRecentlyPlayedSongsAsync`.
- **Modify:** `DTXMania.Game/Lib/Song/SongManager.cs` — add `GetRecentlyPlayedNodesAsync`.
- **Modify:** `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs` — add a `Tabs` layout section.
- **Modify:** `DTXMania.Game/Lib/Stage/SongSelectionStage.cs` — tab state, list refresh, input wiring, tab-bar render, empty-state.
- **Create tests:**
  - `DTXMania.Test/Song/SongSelectionTabTests.cs`
  - `DTXMania.Test/Song/SongDatabaseServiceRecentPlaysTests.cs`
  - `DTXMania.Test/Song/SongManagerRecentPlaysTests.cs`
  - `DTXMania.Test/Stage/SongSelectionStageTabTests.cs`

Each task is TDD: write the failing test, run it red, implement, run it green, commit.

---

## Task 1: `SongSelectionTab` enum + `NextTab` cycle helper

**Files:**
- Create: `DTXMania.Game/Lib/Song/SongSelectionTab.cs`
- Test: `DTXMania.Test/Song/SongSelectionTabTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Song/SongSelectionTabTests.cs`:

```csharp
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongSelectionTabTests
    {
        [Fact]
        public void Next_FromAllSongs_ReturnsRecentPlays()
        {
            Assert.Equal(SongSelectionTab.RecentPlays,
                SongSelectionTabExtensions.Next(SongSelectionTab.AllSongs));
        }

        [Fact]
        public void Next_FromRecentPlays_WrapsToAllSongs()
        {
            Assert.Equal(SongSelectionTab.AllSongs,
                SongSelectionTabExtensions.Next(SongSelectionTab.RecentPlays));
        }

        [Fact]
        public void DisplayLabel_ReturnsHumanReadableNames()
        {
            Assert.Equal("All Songs", SongSelectionTab.AllSongs.DisplayLabel());
            Assert.Equal("Recent", SongSelectionTab.RecentPlays.DisplayLabel());
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionTabTests"`
Expected: FAIL to compile — `SongSelectionTab` / `SongSelectionTabExtensions` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `DTXMania.Game/Lib/Song/SongSelectionTab.cs`:

```csharp
namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Identifies which tab is active in the Song Selection stage.
    /// Ordering here defines the cycle order used by <see cref="SongSelectionTabExtensions.Next"/>.
    /// </summary>
    public enum SongSelectionTab
    {
        AllSongs = 0,
        RecentPlays = 1
    }

    public static class SongSelectionTabExtensions
    {
        /// <summary>Cycles to the next tab, wrapping back to the first.</summary>
        public static SongSelectionTab Next(SongSelectionTab tab)
        {
            return tab switch
            {
                SongSelectionTab.AllSongs => SongSelectionTab.RecentPlays,
                SongSelectionTab.RecentPlays => SongSelectionTab.AllSongs,
                _ => SongSelectionTab.AllSongs
            };
        }

        /// <summary>Human-readable label shown on the tab bar.</summary>
        public static string DisplayLabel(this SongSelectionTab tab)
        {
            return tab switch
            {
                SongSelectionTab.AllSongs => "All Songs",
                SongSelectionTab.RecentPlays => "Recent",
                _ => tab.ToString()
            };
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionTabTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/SongSelectionTab.cs DTXMania.Test/Song/SongSelectionTabTests.cs
git commit -m "feat: add SongSelectionTab enum and cycle helper"
```

---

## Task 2: `GetRecentlyPlayedSongsAsync` DB query

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs` (add method after `GetTopScoresAsync`, ~line 535)
- Test: `DTXMania.Test/Song/SongDatabaseServiceRecentPlaysTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Song/SongDatabaseServiceRecentPlaysTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongDatabaseServiceRecentPlaysTests : IDisposable
    {
        private readonly SongDatabaseService _db;
        private readonly string _dbPath;

        public SongDatabaseServiceRecentPlaysTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"recent_db_{Guid.NewGuid()}.db");
            _db = new SongDatabaseService(_dbPath);
        }

        public void Dispose()
        {
            _db?.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        // Adds a song with one drum chart and records a play (sets LastPlayedAt = UtcNow).
        private async Task<int> AddAndPlayAsync(string title)
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = title, Artist = "A" };
            var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            await _db.AddSongAsync(song, chart);
            var stored = (await _db.GetSongsAsync()).Single(s => s.Title == title);
            var chartId = stored.Charts.Single().Id;
            await _db.UpdateScoreAsync(chartId, EInstrumentPart.DRUMS, 100000, 0.9, true);
            return stored.Id;
        }

        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_OrdersByMostRecentFirst()
        {
            await _db.InitializeDatabaseAsync();
            await AddAndPlayAsync("First");
            await Task.Delay(10);
            await AddAndPlayAsync("Second");
            await Task.Delay(10);
            await AddAndPlayAsync("Third");

            var recent = await _db.GetRecentlyPlayedSongsAsync(20);

            Assert.Equal(new[] { "Third", "Second", "First" }, recent.Select(s => s.Title).ToArray());
        }

        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_ExcludesNeverPlayedSongs()
        {
            await _db.InitializeDatabaseAsync();
            await AddAndPlayAsync("Played");
            // A song with a chart but no recorded play:
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Unplayed", Artist = "A" };
            await _db.AddSongAsync(song, new SongChart { FilePath = "/c/unplayed.dtx", HasDrumChart = true, DrumLevel = 20 });

            var recent = await _db.GetRecentlyPlayedSongsAsync(20);

            Assert.Single(recent);
            Assert.Equal("Played", recent[0].Title);
        }

        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_RespectsLimit()
        {
            await _db.InitializeDatabaseAsync();
            for (int i = 0; i < 25; i++)
            {
                await AddAndPlayAsync($"Song {i:D2}");
                await Task.Delay(2);
            }

            var recent = await _db.GetRecentlyPlayedSongsAsync(20);

            Assert.Equal(20, recent.Count);
        }

        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_GroupsMultiChartSongIntoSingleRow()
        {
            await _db.InitializeDatabaseAsync();
            // Same title+artist => grouped into one Song with two charts.
            var s1 = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Multi", Artist = "A" };
            var s2 = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Multi", Artist = "A" };
            await _db.AddSongAsync(s1, new SongChart { FilePath = "/c/multi-bas.dtx", HasDrumChart = true, DrumLevel = 30 });
            await _db.AddSongAsync(s2, new SongChart { FilePath = "/c/multi-adv.dtx", HasDrumChart = true, DrumLevel = 60 });
            var stored = (await _db.GetSongsAsync()).Single(s => s.Title == "Multi");
            var charts = stored.Charts.OrderBy(c => c.DrumLevel).ToArray();

            // Play the easier chart, then later the harder chart.
            await _db.UpdateScoreAsync(charts[0].Id, EInstrumentPart.DRUMS, 50000, 0.5, false);
            await Task.Delay(10);
            await _db.UpdateScoreAsync(charts[1].Id, EInstrumentPart.DRUMS, 90000, 0.9, true);

            var recent = await _db.GetRecentlyPlayedSongsAsync(20);

            Assert.Single(recent);
            Assert.Equal("Multi", recent[0].Title);
            Assert.Equal(2, recent[0].Charts.Count);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongDatabaseServiceRecentPlaysTests"`
Expected: FAIL to compile — `GetRecentlyPlayedSongsAsync` does not exist.

- [ ] **Step 3: Write minimal implementation**

In `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`, add this method directly after `GetTopScoresAsync` (after line ~535):

```csharp
/// <summary>
/// Returns the most-recently-played songs, one row per song. A song with multiple
/// difficulty charts is collapsed to a single entry using the maximum LastPlayedAt
/// across its charts. Songs that have never been played (no score with a LastPlayedAt)
/// are excluded. Results are ordered newest-first and limited to <paramref name="limit"/>.
/// Each returned Song has its Charts and the charts' Scores eagerly loaded so callers
/// can build fully-populated SongListNodes.
/// </summary>
public async Task<List<SongEntity>> GetRecentlyPlayedSongsAsync(int limit = 20)
{
    if (limit <= 0) return new List<SongEntity>();

    using var context = CreateContext();

    // Group plays by song, taking the latest play per song, newest first.
    var recent = await context.SongScores
        .Where(s => s.LastPlayedAt != null)
        .GroupBy(s => s.Chart.SongId)
        .Select(g => new { SongId = g.Key, LastPlayed = g.Max(s => s.LastPlayedAt) })
        .OrderByDescending(x => x.LastPlayed)
        .Take(limit)
        .ToListAsync();

    var orderedIds = recent.Select(x => x.SongId).ToList();
    if (orderedIds.Count == 0) return new List<SongEntity>();

    var songs = await context.Songs
        .Where(s => orderedIds.Contains(s.Id))
        .Include(s => s.Charts)
            .ThenInclude(c => c.Scores)
        .ToListAsync();

    // Re-order the loaded songs to match the recency ordering (the IN query above
    // does not preserve order).
    var byId = songs.ToDictionary(s => s.Id);
    var result = new List<SongEntity>(orderedIds.Count);
    foreach (var id in orderedIds)
    {
        if (byId.TryGetValue(id, out var song))
            result.Add(song);
    }
    return result;
}
```

`SongEntity`, `List`, `Include`, `ThenInclude` are already imported at the top of the file (`using SongEntity = ...Song;`, `using Microsoft.EntityFrameworkCore;`, `using System.Collections.Generic;`, `using System.Linq;`).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongDatabaseServiceRecentPlaysTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs DTXMania.Test/Song/SongDatabaseServiceRecentPlaysTests.cs
git commit -m "feat: add GetRecentlyPlayedSongsAsync DB query"
```

---

## Task 3: `SongManager.GetRecentlyPlayedNodesAsync`

**Files:**
- Modify: `DTXMania.Game/Lib/Song/SongManager.cs` (add after the `UpdateScoreAsync(summary)` overload, ~line 1579)
- Test: `DTXMania.Test/Song/SongManagerRecentPlaysTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Song/SongManagerRecentPlaysTests.cs`. Use the existing `SongManager` collection to serialize singleton access:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongManagerRecentPlaysTests : IDisposable
    {
        private readonly string _dbPath;

        public SongManagerRecentPlaysTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"mgr_recent_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_ReturnsScoreNodesNewestFirst()
        {
            var manager = SongManager.Instance;
            await manager.InitializeDatabaseServiceAsync(_dbPath, purgeDatabaseFirst: true);
            var db = manager.DatabaseService!;

            async Task AddAndPlay(string title)
            {
                var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = title, Artist = "A" };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.AddSongAsync(song, chart);
                var stored = (await db.GetSongsAsync()).Single(s => s.Title == title);
                await db.UpdateScoreAsync(stored.Charts.Single().Id, EInstrumentPart.DRUMS, 100000, 0.9, true);
            }

            await AddAndPlay("Older");
            await Task.Delay(10);
            await AddAndPlay("Newer");

            var nodes = await manager.GetRecentlyPlayedNodesAsync(20);

            Assert.Equal(2, nodes.Count);
            Assert.All(nodes, n => Assert.Equal(NodeType.Score, n.Type));
            Assert.Equal("Newer", nodes[0].DisplayTitle);
            Assert.Equal("Older", nodes[1].DisplayTitle);
        }

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_WhenNothingPlayed_ReturnsEmptyList()
        {
            var manager = SongManager.Instance;
            await manager.InitializeDatabaseServiceAsync(_dbPath, purgeDatabaseFirst: true);

            var nodes = await manager.GetRecentlyPlayedNodesAsync(20);

            Assert.Empty(nodes);
        }
    }
}
```

> Note: confirmed signature — `public async Task<bool> InitializeDatabaseServiceAsync(string? databasePath = null, bool purgeDatabaseFirst = false)` (SongManager.cs:215); `DatabaseService` is the existing public accessor (~line 99).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongManagerRecentPlaysTests"`
Expected: FAIL to compile — `GetRecentlyPlayedNodesAsync` does not exist.

- [ ] **Step 3: Write minimal implementation**

In `DTXMania.Game/Lib/Song/SongManager.cs`, add after the `UpdateScoreAsync(..., PerformanceSummary summary)` overload (~line 1579):

```csharp
/// <summary>
/// Builds a flat list of Score nodes for the most-recently-played songs, newest first.
/// One node per song (multi-chart songs collapse to a single node carrying all charts),
/// limited to <paramref name="limit"/>. Returns an empty list when the database service
/// is unavailable or nothing has been played. Reuses the same node builder as the browse
/// list so difficulty cycling, status panel, preview, and activation behave identically.
/// </summary>
public async Task<List<SongListNode>> GetRecentlyPlayedNodesAsync(int limit = 20)
{
    var db = GetDatabaseServiceSnapshot();
    if (db == null) return new List<SongListNode>();

    try
    {
        var songs = await db.GetRecentlyPlayedSongsAsync(limit).ConfigureAwait(false);
        var nodes = new List<SongListNode>(songs.Count);
        foreach (var song in songs)
        {
            var charts = song.Charts?.ToArray() ?? Array.Empty<SongChart>();
            var node = CreateSongNodeFromDatabaseEntities(song, charts);
            if (node != null)
                nodes.Add(node);
        }
        return nodes;
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"SongManager: Error getting recent plays: {ex.Message}");
        return new List<SongListNode>();
    }
}
```

`GetDatabaseServiceSnapshot()`, `CreateSongNodeFromDatabaseEntities`, `Debug`, `System.Linq`, and `SongChart` are all already available in this file.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongManagerRecentPlaysTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/SongManager.cs DTXMania.Test/Song/SongManagerRecentPlaysTests.cs
git commit -m "feat: add SongManager.GetRecentlyPlayedNodesAsync"
```

---

## Task 4: Tab state + list refresh in the stage

This task adds the `_activeTab` field, the cached recent-plays nodes, and a single refresh entry point that swaps the displayed list per active tab. No input or rendering yet.

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Test: `DTXMania.Test/Stage/SongSelectionStageTabTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Stage/SongSelectionStageTabTests.cs`:

```csharp
using System.Collections.Generic;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongSelectionStageTabTests
    {
        private static SongListNode ScoreNode(string title) => new SongListNode
        {
            Type = NodeType.Score,
            Title = title
        };

        [Fact]
        public void RefreshSongListForActiveTab_OnRecentTab_ShowsCachedRecentNodes()
        {
            var stage = SongSelectionStageTabTestHelper.CreateStage();
            var display = new SongListDisplay();
            SongSelectionStageTabTestHelper.AttachCoreUi(stage, display);

            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            SetPrivateField(stage, "_recentPlayNodes", new List<SongListNode> { ScoreNode("R1"), ScoreNode("R2") });

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Equal(2, display.CurrentList.Count);
            Assert.Equal("R1", display.CurrentList[0].Title);
            Assert.False(GetPrivateField<bool>(stage, "_showEmptyRecentMessage"));
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnRecentTab_WhenEmpty_SetsEmptyFlag()
        {
            var stage = SongSelectionStageTabTestHelper.CreateStage();
            var display = new SongListDisplay();
            SongSelectionStageTabTestHelper.AttachCoreUi(stage, display);

            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            SetPrivateField(stage, "_recentPlayNodes", new List<SongListNode>());

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Empty(display.CurrentList);
            Assert.True(GetPrivateField<bool>(stage, "_showEmptyRecentMessage"));
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnAllSongsTab_UsesBrowseList()
        {
            var stage = SongSelectionStageTabTestHelper.CreateStage();
            var display = new SongListDisplay();
            SongSelectionStageTabTestHelper.AttachCoreUi(stage, display);

            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { ScoreNode("A1") });
            // Ensure no filter view is active so PopulateSongList() path runs.
            SetPrivateField<object?>(stage, "_filteredView", null);

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Single(display.CurrentList);
            Assert.Equal("A1", display.CurrentList[0].Title);
        }
    }
}
```

Add a tiny helper class in the same file to reuse the stage/UI construction (mirrors the private helpers in `SongSelectionStageLogicTests`). Place at the bottom of the file:

```csharp
namespace DTXMania.Test.Stage
{
    internal static class SongSelectionStageTabTestHelper
    {
        public static SongSelectionStage CreateStage()
        {
            // CreateGame() builds a headless BaseGame mock as used across the
            // SongSelectionStage test suite. Reuse the existing factory.
            return SongSelectionStageTestFactory.CreateStage();
        }

        public static void AttachCoreUi(SongSelectionStage stage, SongListDisplay display)
        {
            SongSelectionStageTestFactory.AttachCoreUi(stage, display);
        }
    }
}
```

> Implementation note for the engineer: `SongSelectionStageLogicTests` already contains `private static SongSelectionStage CreateStage()` and `AttachCoreUi(...)`. To avoid duplicating the headless `BaseGame` setup, **extract those two helpers (and the `CreateGame()` they depend on) into a new shared internal static class** `DTXMania.Test/Stage/SongSelectionStageTestFactory.cs` and have `SongSelectionStageLogicTests` call into it. If extraction proves noisy, instead copy the minimal `CreateStage()`/`AttachCoreUi()` bodies (they only construct a stage and `SetPrivateField` the four UI fields) directly into `SongSelectionStageTabTestHelper` and drop the factory indirection. Either way, the tab tests must construct a stage and attach a `SongListDisplay` exactly as `SongSelectionStageLogicTests` does.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageTabTests"`
Expected: FAIL — fields `_activeTab`, `_recentPlayNodes`, `_showEmptyRecentMessage` and method `RefreshSongListForActiveTab` do not exist (and the factory/helper if extracted).

- [ ] **Step 3: Write minimal implementation**

In `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`, add fields near the other selection state (next to `_filterCriteria` / `_showEmptyFilterMessage`, ~line 56):

```csharp
        // Tab state. The stage instance is cached per game; reset to AllSongs on Activate.
        private SongSelectionTab _activeTab = SongSelectionTab.AllSongs;
        // Cached recent-plays nodes (flat Score nodes), refreshed on activation and on
        // switching into the Recent tab. Null until first load.
        private System.Collections.Generic.List<SongListNode>? _recentPlayNodes;
        private bool _showEmptyRecentMessage;
```

Add the refresh entry point in the "Song List Management" region (next to `PopulateSongListForCurrentMode`, ~line 952):

```csharp
        /// <summary>
        /// Repopulates the visible song list according to the active tab.
        /// AllSongs uses the existing hierarchical/filtered view; RecentPlays shows the
        /// cached flat recent-plays list (and toggles the empty-state flag).
        /// </summary>
        private void RefreshSongListForActiveTab()
        {
            if (_activeTab == SongSelectionTab.RecentPlays)
                PopulateRecentPlaysList();
            else
                PopulateSongListForCurrentMode();
        }

        private void PopulateRecentPlaysList()
        {
            var nodes = _recentPlayNodes ?? new System.Collections.Generic.List<SongListNode>();
            _songListDisplay.CurrentList = new System.Collections.Generic.List<SongListNode>(nodes);
            _showEmptyRecentMessage = nodes.Count == 0;
        }
```

Add the `using` for the enum if not already present (it is in namespace `DTXMania.Game.Lib.Song`, which the file already references via `using DTXMania.Game.Lib.Song;` — verify and add if missing).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageTabTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs DTXMania.Test/Stage/SongSelectionStageTabTests.cs DTXMania.Test/Stage/SongSelectionStageTestFactory.cs
git commit -m "feat: add tab state and per-tab list refresh to song select"
```

(Omit `SongSelectionStageTestFactory.cs` from the add if you copied helpers inline instead of extracting.)

---

## Task 5: `SwitchToNextTab` + async recent-plays load

Adds the tab-switch action and the async loader that warms `_recentPlayNodes`. Cross-thread safety: `SwitchToNextTab` mutates only stage fields and sets a "needs repopulate" flag consumed in `OnUpdate`; the async loader assigns `_recentPlayNodes` then flags a repopulate. This avoids mutating `SongListDisplay` from a background continuation.

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Test: `DTXMania.Test/Stage/SongSelectionStageTabTests.cs` (append)

- [ ] **Step 1: Write the failing test**

Append to `DTXMania.Test/Stage/SongSelectionStageTabTests.cs` (inside the test class):

```csharp
        [Fact]
        public void SwitchToNextTab_TogglesActiveTabAndRequestsRepopulate()
        {
            var stage = SongSelectionStageTabTestHelper.CreateStage();
            var display = new SongListDisplay();
            SongSelectionStageTabTestHelper.AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            InvokePrivateMethod(stage, "SwitchToNextTab");

            Assert.Equal(SongSelectionTab.RecentPlays, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
            Assert.True(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
        }

        [Fact]
        public void SwitchToNextTab_FromRecent_WrapsBackToAllSongs()
        {
            var stage = SongSelectionStageTabTestHelper.CreateStage();
            var display = new SongListDisplay();
            SongSelectionStageTabTestHelper.AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);

            InvokePrivateMethod(stage, "SwitchToNextTab");

            Assert.Equal(SongSelectionTab.AllSongs, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageTabTests"`
Expected: FAIL — `SwitchToNextTab` and `_tabListNeedsRefresh` do not exist.

- [ ] **Step 3: Write minimal implementation**

Add the flag field next to the tab fields from Task 4:

```csharp
        // Set when the active tab's list must be repopulated on the next OnUpdate.
        // Used so background recent-plays loads and lane-hit/Tab switches never mutate
        // SongListDisplay off the update thread.
        private bool _tabListNeedsRefresh;
```

Add the methods in the "Input Handling" region (near `OpenSearchFilterModal`, ~line 1340):

```csharp
        /// <summary>
        /// Cycles to the next tab, exits status-panel mode, kicks a recent-plays reload
        /// when entering the Recent tab, and requests a list repopulate on the next update.
        /// </summary>
        private void SwitchToNextTab()
        {
            _activeTab = SongSelectionTabExtensions.Next(_activeTab);
            _isInStatusPanel = false;

            if (_activeTab == SongSelectionTab.RecentPlays)
                BeginRecentPlaysLoad();

            _tabListNeedsRefresh = true;
            PlayCursorMoveSound();
        }

        /// <summary>
        /// Loads recent-plays nodes in the background and flags a repopulate when done.
        /// Safe to call when the singleton DB is unavailable (returns an empty list).
        /// </summary>
        private void BeginRecentPlaysLoad()
        {
            _ = SongManager.Instance.GetRecentlyPlayedNodesAsync(RecentPlaysLimit)
                .ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"SongSelectionStage: recent-plays load failed: {task.Exception?.GetBaseException().Message}");
                        return;
                    }
                    _recentPlayNodes = task.Result;
                    _tabListNeedsRefresh = true;
                }, TaskScheduler.Default);
        }
```

Add the limit constant near the other stage constants (~line 119):

```csharp
        private const int RecentPlaysLimit = 20;
```

Consume the flag in `OnUpdate` (insert right after `HandleInput();`, ~line 1019):

```csharp
            // Apply any pending tab list repopulate on the update thread.
            if (_tabListNeedsRefresh)
            {
                _tabListNeedsRefresh = false;
                RefreshSongListForActiveTab();
            }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageTabTests"`
Expected: PASS (5 tests total in the class).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs DTXMania.Test/Stage/SongSelectionStageTabTests.cs
git commit -m "feat: add SwitchToNextTab and async recent-plays load"
```

---

## Task 6: Wire Tab key + Low Tom pad to `SwitchToNextTab`; disable search on Recent

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Test: `DTXMania.Test/Stage/SongSelectionStageTabTests.cs` (append)

- [ ] **Step 1: Write the failing test**

Append to the test class. These test the guard logic that can be exercised headlessly (the raw keyboard / hardware lane paths are integration-level and covered by the E2E smoke separately):

```csharp
        [Fact]
        public void OpenSearchFilterModal_OnRecentTab_DoesNotOpen()
        {
            var stage = SongSelectionStageTabTestHelper.CreateStage();
            var display = new SongListDisplay();
            SongSelectionStageTabTestHelper.AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);

            // No modal is attached; method must early-return without throwing.
            InvokePrivateMethod(stage, "OpenSearchFilterModal");

            // Nothing to assert beyond "did not throw and did not switch state";
            // confirm the tab is unchanged.
            Assert.Equal(SongSelectionTab.RecentPlays, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void HandleLaneHitForTabSwitch_OnLowTom_SwitchesTab()
        {
            var stage = SongSelectionStageTabTestHelper.CreateStage();
            var display = new SongListDisplay();
            SongSelectionStageTabTestHelper.AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            // Low Tom is lane 8 in the lane-hit (KeyBindings) scheme.
            InvokePrivateMethod(stage, "HandleLaneHitForTabSwitch", 8);

            Assert.Equal(SongSelectionTab.RecentPlays, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void HandleLaneHitForTabSwitch_OnOtherLane_DoesNotSwitch()
        {
            var stage = SongSelectionStageTabTestHelper.CreateStage();
            var display = new SongListDisplay();
            SongSelectionStageTabTestHelper.AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            InvokePrivateMethod(stage, "HandleLaneHitForTabSwitch", 4); // Snare

            Assert.Equal(SongSelectionTab.AllSongs, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }
```

> `HandleLaneHitForTabSwitch(int lane)` is split out from the `EventHandler<LaneHitEventArgs>` subscriber specifically so it is callable by `InvokePrivateMethod` with a plain `int`. The event subscriber just forwards `e.Lane`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageTabTests"`
Expected: FAIL — `HandleLaneHitForTabSwitch` does not exist; `OpenSearchFilterModal` does not yet guard on tab.

- [ ] **Step 3: Write minimal implementation**

(a) Add the Low Tom lane constant near `RecentPlaysLimit`:

```csharp
        // Low Tom in the lane-hit (KeyBindings) numbering; shared with Right Cymbal.
        // NOTE: distinct from the visual PerformanceUILayout.LaneType.LT (=6).
        private const int LowTomLaneIndex = 8;
```

(b) Add a Tab-key raw-poll, mirroring `DetectOpenSearchKey`. In `HandleInput()`, after the `DetectOpenSearchKey();` call and its modal-just-opened guard (~line 1097, before `ProcessInputCommands();`), add:

```csharp
            DetectTabSwitchKey();
```

Then add the method next to `DetectOpenSearchKey` (~line 1107):

```csharp
        private void DetectTabSwitchKey()
        {
            // Tab is not in the InputCommandType key-map on purpose: queued commands are
            // not drained while the search modal is open, so a mapped Tab would accumulate
            // and fire stale tab-switches on modal close. Raw-poll here (non-modal path
            // only), matching DetectOpenSearchKey's handling of Backspace. IsKeyPressed
            // also surfaces MCP/E2E injected keys.
            if (_inputManager != null &&
                _inputManager.IsKeyPressed((int)Microsoft.Xna.Framework.Input.Keys.Tab))
            {
                SwitchToNextTab();
            }
        }
```

(c) Guard `OpenSearchFilterModal()` so search is unavailable on the Recent tab. At the top of the method (~line 1326):

```csharp
            if (_activeTab != SongSelectionTab.AllSongs) return;
```

(d) Add the lane-hit handler + a forwarding subscriber, in the "Input Handling" region:

```csharp
        private void OnTabSwitchLaneHit(object? sender, DTXMania.Game.Lib.Input.LaneHitEventArgs e)
        {
            HandleLaneHitForTabSwitch(e.Lane);
        }

        private void HandleLaneHitForTabSwitch(int lane)
        {
            if (lane == LowTomLaneIndex)
                SwitchToNextTab();
        }
```

(e) Subscribe/unsubscribe to lane hits. In `Activate()` (after the input manager is assigned; place near the end, before `_currentPhase = StagePhase.FadeIn;`, ~line 226) add:

```csharp
            SubscribeTabSwitchLaneHits();
```

In `Deactivate()` (near the input-manager teardown, before `_inputManager = null;`, ~line 290) add:

```csharp
            UnsubscribeTabSwitchLaneHits();
```

Then add the two helpers in the "Input Handling" region:

```csharp
        private void SubscribeTabSwitchLaneHits()
        {
            if (_inputManager is DTXMania.Game.Lib.Input.InputManagerCompat compat
                && compat.ModularInputManager != null)
            {
                // Defensive: remove before adding so re-Activate never double-subscribes.
                compat.ModularInputManager.OnLaneHit -= OnTabSwitchLaneHit;
                compat.ModularInputManager.OnLaneHit += OnTabSwitchLaneHit;
            }
        }

        private void UnsubscribeTabSwitchLaneHits()
        {
            if (_inputManager is DTXMania.Game.Lib.Input.InputManagerCompat compat
                && compat.ModularInputManager != null)
            {
                compat.ModularInputManager.OnLaneHit -= OnTabSwitchLaneHit;
            }
        }
```

(f) Also warm the recent-plays cache and reset the tab on activation. In `Activate()`, right after `_selectionPhase = SongSelectionPhase.FadeIn;` (~line 228) add:

```csharp
            // Always start on All Songs for predictability; warm the recent-plays cache so
            // the Recent tab is populated the moment the user switches to it.
            _activeTab = SongSelectionTab.AllSongs;
            _recentPlayNodes = null;
            _showEmptyRecentMessage = false;
            BeginRecentPlaysLoad();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageTabTests"`
Expected: PASS (8 tests total in the class).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs DTXMania.Test/Stage/SongSelectionStageTabTests.cs
git commit -m "feat: wire Tab key and Low Tom pad to tab switching"
```

---

## Task 7: Tab bar rendering + layout constants + empty-state message

This task is rendering. It draws the tab labels and the "No recent plays yet" message. Rendering is exercised via the E2E smoke / manual run, not unit tests; we add one trait-free assertion that the layout constants exist and a logic guard for the empty message.

**Files:**
- Modify: `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs`
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

- [ ] **Step 1: Add the layout constants**

In `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs`, add a nested static class (place it near the other nested layout classes such as `SongBars`/`UILabels`; match the file's existing nesting style):

```csharp
        /// <summary>
        /// Layout for the song-select tab bar (All Songs / Recent).
        /// Coordinates are in the stage's 1280x720 design space.
        /// </summary>
        public static class Tabs
        {
            // Top-left origin of the tab strip.
            public const int X = 40;
            public const int Y = 8;
            // Horizontal gap between tab labels.
            public const int Spacing = 24;
            // Active vs. inactive label tint.
            public static readonly Microsoft.Xna.Framework.Color ActiveColor = Microsoft.Xna.Framework.Color.White;
            public static readonly Microsoft.Xna.Framework.Color InactiveColor = new Microsoft.Xna.Framework.Color(150, 150, 150);
        }
```

> If `SongSelectionUILayout` already imports `Microsoft.Xna.Framework`, use the short `Color` form to match surrounding style.

- [ ] **Step 2: Draw the tab bar and empty-state message**

In `SongSelectionStage.OnDraw` (after `_uiManager?.Draw(...)`, ~line 1036, within the existing `_spriteBatch.Begin()/End()` block), add the tab-bar draw and replace/extend the empty-state block:

```csharp
            // Draw the tab bar (skip while the search modal is open to avoid overlap).
            if (_font != null && (_searchFilterModal == null || !_searchFilterModal.IsOpen))
            {
                DrawTabBar();
            }

            // Draw empty-state message for the Recent tab when it has no entries.
            if (_activeTab == SongSelectionTab.RecentPlays && _showEmptyRecentMessage && _font != null
                && (_searchFilterModal == null || !_searchFilterModal.IsOpen))
            {
                string msg = "No recent plays yet";
                _font.DrawString(_spriteBatch, msg,
                    new Vector2(SongSelectionUILayout.SongBars.UnselectedBarX + 100, SongSelectionUILayout.SongBars.SelectedBarY),
                    Microsoft.Xna.Framework.Color.LightGray);
            }
```

Add the `DrawTabBar` method in the "Update and Draw" region:

```csharp
        private void DrawTabBar()
        {
            float x = SongSelectionUILayout.Tabs.X;
            float y = SongSelectionUILayout.Tabs.Y;

            foreach (SongSelectionTab tab in System.Enum.GetValues(typeof(SongSelectionTab)))
            {
                string label = tab.DisplayLabel();
                var color = tab == _activeTab
                    ? SongSelectionUILayout.Tabs.ActiveColor
                    : SongSelectionUILayout.Tabs.InactiveColor;

                _font.DrawString(_spriteBatch, label, new Vector2(x, y), color);

                var size = _font.MeasureString(label);
                x += size.X + SongSelectionUILayout.Tabs.Spacing;
            }
        }
```

> Font API confirmed: `_font` is `IFont`, which exposes `void DrawString(SpriteBatch, string, Vector2, Color)` (used by the existing empty-filter draw at SongSelectionStage.cs:1044) and `Vector2 MeasureString(string)` (IFont.cs:88). Both are used by `DrawTabBar` exactly as written.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Run the existing tab tests to confirm no regression**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageTabTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs DTXMania.Game/Lib/Stage/SongSelectionStage.cs
git commit -m "feat: render song-select tab bar and recent-plays empty state"
```

---

## Task 8: Full verification + manual smoke

**Files:** none (verification only).

- [ ] **Step 1: Run the full Mac test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: PASS — all pre-existing tests plus the new `SongSelectionTabTests`, `SongDatabaseServiceRecentPlaysTests`, `SongManagerRecentPlaysTests`, `SongSelectionStageTabTests`. Confirm zero failures and that no previously-passing test regressed.

- [ ] **Step 2: Build the game**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Manual smoke (optional but recommended)**

Run: `dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj`
Verify by observation:
- Song Select shows a tab bar with "All Songs" highlighted and "Recent" dimmed.
- Pressing Tab switches to "Recent"; the list shows recently played songs (or "No recent plays yet" on a fresh profile), and Tab again returns to All Songs.
- Hitting the Low Tom pad (default key "L") also switches tabs.
- Selecting a song on the Recent tab plays it normally; returning to Song Select and reopening Recent shows that song at the top.
- The search/filter modal does not open while on the Recent tab.

- [ ] **Step 4: Commit (if any doc tweaks)**

If `CLAUDE.md` or the spec needs a note about the new feature, update and commit:

```bash
git add -A
git commit -m "docs: note Recent Plays tab in song select"
```

(Skip if no doc changes are needed.)

---

## Self-review notes (already reconciled)

- **Spec coverage:** tab bar (Tasks 4/7), recent data one-row-per-song aggregate (Task 2 grouping test), 20-cap newest-first (Tasks 2/3), Tab + Low Tom input (Task 6), empty state (Tasks 4/7), search disabled on Recent (Task 6), refresh on activation + on switch (Tasks 5/6), reset-to-All-Songs on entry (Task 6f). Testing matrix from the spec maps to Tasks 2, 3, 4, 5, 6.
- **Type consistency:** `SongSelectionTab` / `SongSelectionTabExtensions.Next` / `DisplayLabel` used consistently; `GetRecentlyPlayedSongsAsync(int)` → `List<Song>`; `GetRecentlyPlayedNodesAsync(int)` → `List<SongListNode>`; stage fields `_activeTab`, `_recentPlayNodes`, `_showEmptyRecentMessage`, `_tabListNeedsRefresh`; methods `RefreshSongListForActiveTab`, `PopulateRecentPlaysList`, `SwitchToNextTab`, `BeginRecentPlaysLoad`, `DetectTabSwitchKey`, `HandleLaneHitForTabSwitch`, `OnTabSwitchLaneHit`, `SubscribeTabSwitchLaneHits`, `UnsubscribeTabSwitchLaneHits`, `DrawTabBar`; constants `RecentPlaysLimit = 20`, `LowTomLaneIndex = 8`.
- **Input-queue hazard:** documented why Tab is raw-polled rather than command-mapped.
- **Lane numbering hazard:** documented Low Tom = lane 8 (lane-hit scheme), not 6 (visual scheme).
- **Verification points to confirm during implementation (flagged inline):** exact `InitializeDatabaseServiceAsync` parameter name; `_font.MeasureString` API name; `SongSelectionUILayout` nesting/`Color` import style; the shared stage-test-factory extraction vs. inline-copy decision.
