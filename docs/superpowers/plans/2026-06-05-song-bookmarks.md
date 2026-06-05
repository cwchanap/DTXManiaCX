# Song Bookmarks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players bookmark whole songs, toggle bookmarks with the `B` key or Floor Tom pad, browse bookmarks in a new alphabetically-sorted Bookmarks tab, and see a star marker on bookmarked song bars.

**Architecture:** A single `IsBookmarked` boolean column on the `Songs` table is the source of truth. The DB service gains read/write methods; `SongManager` exposes thin async wrappers; `SongSelectionStage` gains a third tab (`Bookmarks`) that mirrors the existing Recent-tab async-load machinery, plus a toggle action wired to a key and a drum pad. A render-agnostic helper decides when to draw the star; the draw itself lives in the song-list display.

**Tech Stack:** .NET 8, EF Core + SQLite (Microsoft.EntityFrameworkCore.Sqlite), MonoGame, xUnit + Moq.

**Reference patterns (read before starting):** the Recent-plays feature is the template this plan copies. Key files:
- `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs` — `GetRecentlyPlayedSongsAsync` (~line 545), init flow (~line 69), `ConfigureUtf8EncodingAsync`/`EnsureDatabaseVersionTableAsync` (~line 715–763).
- `DTXMania.Game/Lib/Song/SongManager.cs` — `GetRecentlyPlayedNodesAsync` (~line 1591), `CreateSongNodeFromDatabaseEntities` (~line 2221).
- `DTXMania.Game/Lib/Song/SongSelectionTab.cs` — the tab enum + extensions.
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs` — recent-plays fields (~line 59–80), `BeginRecentPlaysLoad`/`SwitchToNextTab` (~line 2014–2069), `RefreshSongListForActiveTab`/`PopulateRecentPlaysList` (~line 1009–1024), `HandleInput`/`DetectTabSwitchKey` (~line 1177–1223), `OnTabSwitchLaneHit`/`HandleLaneHitForTabSwitch` (~line 2071–2085), draw messages (~line 1138–1148), `DrawTabBar` (~line 1510).
- Tests to mirror: `DTXMania.Test/Song/SongDatabaseServiceRecentPlaysTests.cs`, `DTXMania.Test/Song/SongManagerRecentPlaysTests.cs`, `DTXMania.Test/Stage/SongSelectionStageTabTests.cs`.

**Conventions:**
- Build (Mac): `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
- Test one class (Mac): `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~CLASSNAME"`
- The Mac test csproj auto-includes new `*.cs` test files via a glob; no csproj edit needed as long as tests don't require a `GraphicsDevice`.
- Commit after every task with a Conventional Commit subject under 72 chars.

---

### Task 1: Add `IsBookmarked` to the Song entity and the DB read/write methods

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Entities/Song.cs` (add property after `UpdatedAt`, ~line 29)
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs` (add two methods next to `GetRecentlyPlayedSongsAsync`, ~line 583)
- Test: `DTXMania.Test/Song/SongDatabaseServiceBookmarkTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Song/SongDatabaseServiceBookmarkTests.cs`:

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
    public class SongDatabaseServiceBookmarkTests : IDisposable
    {
        private readonly SongDatabaseService _db;
        private readonly string _dbPath;

        public SongDatabaseServiceBookmarkTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"bookmark_db_{Guid.NewGuid()}.db");
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

        private async Task<int> AddSongAsync(string title)
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = title, Artist = "A" };
            var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            await _db.AddSongAsync(song, chart);
            return (await _db.GetSongsAsync()).Single(s => s.Title == title).Id;
        }

        [Fact]
        public async Task SetBookmarkAsync_TogglesFlagOnAndOff()
        {
            await _db.InitializeDatabaseAsync();
            var id = await AddSongAsync("Song");

            await _db.SetBookmarkAsync(id, true);
            Assert.True((await _db.GetSongsAsync()).Single(s => s.Id == id).IsBookmarked);

            await _db.SetBookmarkAsync(id, false);
            Assert.False((await _db.GetSongsAsync()).Single(s => s.Id == id).IsBookmarked);
        }

        [Fact]
        public async Task SetBookmarkAsync_WithUnknownId_DoesNotThrow()
        {
            await _db.InitializeDatabaseAsync();
            var ex = await Record.ExceptionAsync(() => _db.SetBookmarkAsync(999999, true));
            Assert.Null(ex);
        }

        [Fact]
        public async Task GetBookmarkedSongsAsync_ReturnsOnlyBookmarked_AlphabeticalByTitle()
        {
            await _db.InitializeDatabaseAsync();
            var charlie = await AddSongAsync("Charlie");
            var alpha = await AddSongAsync("Alpha");
            var bravo = await AddSongAsync("Bravo");
            await AddSongAsync("Unmarked");

            await _db.SetBookmarkAsync(charlie, true);
            await _db.SetBookmarkAsync(alpha, true);
            await _db.SetBookmarkAsync(bravo, true);

            var result = await _db.GetBookmarkedSongsAsync();

            Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, result.Select(s => s.Title).ToArray());
        }

        [Fact]
        public async Task GetBookmarkedSongsAsync_EagerLoadsChartsAndScores()
        {
            await _db.InitializeDatabaseAsync();
            var id = await AddSongAsync("Song");
            await _db.SetBookmarkAsync(id, true);

            var result = await _db.GetBookmarkedSongsAsync();

            Assert.Single(result);
            Assert.NotNull(result[0].Charts);
            Assert.Single(result[0].Charts);
            // Scores collection is eager-loaded (non-null) even if empty.
            Assert.NotNull(result[0].Charts.Single().Scores);
        }

        [Fact]
        public async Task GetBookmarkedSongsAsync_WhenNoneBookmarked_ReturnsEmptyList()
        {
            await _db.InitializeDatabaseAsync();
            await AddSongAsync("Song");

            var result = await _db.GetBookmarkedSongsAsync();

            Assert.Empty(result);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongDatabaseServiceBookmarkTests"`
Expected: FAIL — compile error (`IsBookmarked`, `SetBookmarkAsync`, `GetBookmarkedSongsAsync` do not exist).

- [ ] **Step 3: Add the entity property**

In `DTXMania.Game/Lib/Song/Entities/Song.cs`, after the `UpdatedAt` property (~line 29) inside the Timestamps region:

```csharp
        // Whether the player has bookmarked this song. Surfaced in the Bookmarks tab
        // and as a star marker in the All Songs list.
        public bool IsBookmarked { get; set; } = false;
```

- [ ] **Step 4: Add the DB service methods**

In `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`, immediately after the closing brace of `GetRecentlyPlayedSongsAsync` (~line 583):

```csharp
        /// <summary>
        /// Sets or clears the bookmark flag on a song. No-op if the song id is not found.
        /// </summary>
        public async Task SetBookmarkAsync(int songId, bool bookmarked)
        {
            using var context = CreateContext();
            var song = await context.Songs.FirstOrDefaultAsync(s => s.Id == songId);
            if (song == null) return;
            song.IsBookmarked = bookmarked;
            song.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Returns all bookmarked songs ordered alphabetically by Title (Id as a stable
        /// tiebreak). Each Song has its Charts and the charts' Scores eagerly loaded so
        /// callers can build fully-populated SongListNodes, mirroring
        /// <see cref="GetRecentlyPlayedSongsAsync"/>.
        /// </summary>
        public async Task<List<SongEntity>> GetBookmarkedSongsAsync()
        {
            using var context = CreateContext();
            return await context.Songs
                .Where(s => s.IsBookmarked)
                .Include(s => s.Charts)
                    .ThenInclude(c => c.Scores)
                .OrderBy(s => s.Title)
                .ThenBy(s => s.Id)
                .ToListAsync();
        }
```

(`SongEntity` is the file's alias for `Entities.Song`; `CreateContext`, `FirstOrDefaultAsync`, `Include`/`ThenInclude` are all already used in this file.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongDatabaseServiceBookmarkTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Song/Entities/Song.cs DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs DTXMania.Test/Song/SongDatabaseServiceBookmarkTests.cs
git commit -m "feat: add IsBookmarked column and bookmark DB methods"
```

---

### Task 2: Auto-add the `IsBookmarked` column to pre-existing databases

The project uses `EnsureCreatedAsync()`, which never alters an existing schema. Players with an existing song DB (that already has the Unicode version table, so it is NOT wiped on startup) need the column added by a guarded, idempotent `ALTER TABLE`.

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs` (call site inside `ConfigureUtf8EncodingAsync` ~line 728; new method after `EnsureDatabaseVersionTableAsync` ~line 763)
- Test: `DTXMania.Test/Song/SongDatabaseServiceBookmarkMigrationTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Song/SongDatabaseServiceBookmarkMigrationTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongDatabaseServiceBookmarkMigrationTests : IDisposable
    {
        private readonly string _dbPath;

        public SongDatabaseServiceBookmarkMigrationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"bookmark_mig_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        private async Task<int> ColumnCountAsync()
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Songs') WHERE name='IsBookmarked'";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_OnLegacyDbMissingColumn_AddsColumn()
        {
            // Create a normal DB (has the column + Unicode version table), then drop the
            // column to simulate a legacy database created before this feature.
            var first = new SongDatabaseService(_dbPath);
            await first.InitializeDatabaseAsync();
            first.Dispose();

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                await conn.OpenAsync();
                using var drop = conn.CreateCommand();
                drop.CommandText = "ALTER TABLE Songs DROP COLUMN IsBookmarked";
                await drop.ExecuteNonQueryAsync();
            }
            Assert.Equal(0, await ColumnCountAsync()); // confirm the legacy state

            // Re-initialize: the Unicode version table is present so the DB is NOT wiped;
            // the migration must re-add the column.
            var second = new SongDatabaseService(_dbPath);
            await second.InitializeDatabaseAsync();
            second.Dispose();

            Assert.Equal(1, await ColumnCountAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_OnFreshDb_IsIdempotentAndKeepsSingleColumn()
        {
            var svc = new SongDatabaseService(_dbPath);
            await svc.InitializeDatabaseAsync();
            svc.Dispose();

            // A second service initializing the same file must not error or duplicate.
            var again = new SongDatabaseService(_dbPath);
            await again.InitializeDatabaseAsync();
            again.Dispose();

            Assert.Equal(1, await ColumnCountAsync());
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongDatabaseServiceBookmarkMigrationTests"`
Expected: FAIL — `InitializeDatabaseAsync_OnLegacyDbMissingColumn_AddsColumn` ends with column count 0 (no migration yet). (The idempotent test may already pass because `EnsureCreated` adds the column on a fresh DB.)

- [ ] **Step 3: Add the migration call and method**

In `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`, inside `ConfigureUtf8EncodingAsync`, immediately after the `await EnsureDatabaseVersionTableAsync(context);` line (~line 728):

```csharp
                // Additive schema upgrade for existing databases: EnsureCreated never
                // alters an existing schema, so add new columns here, idempotently.
                await EnsureBookmarkColumnAsync(context);
```

Then add this method immediately after `EnsureDatabaseVersionTableAsync` (~line 763):

```csharp
        /// <summary>
        /// Ensures the Songs.IsBookmarked column exists. Fresh databases already have it via
        /// EnsureCreated; pre-existing databases get it added here exactly once. Idempotent and
        /// defensive: a duplicate-column error is treated as success.
        /// </summary>
        private async Task EnsureBookmarkColumnAsync(SongDbContext context)
        {
            try
            {
                var columnCount = await context.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM pragma_table_info('Songs') WHERE name='IsBookmarked'"
                ).ToListAsync();

                if (columnCount.FirstOrDefault() > 0)
                    return; // Column already present (fresh DB or prior upgrade).

                await context.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE Songs ADD COLUMN IsBookmarked INTEGER NOT NULL DEFAULT 0");

                System.Diagnostics.Debug.WriteLine("SongDatabaseService: Added Songs.IsBookmarked column");
            }
            catch (Exception ex) when (ex.Message.Contains("duplicate column"))
            {
                // Another caller added it concurrently; nothing to do.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Warning - Could not add IsBookmarked column: {ex.Message}");
            }
        }
```

(`SqlQueryRaw<int>` requires the projected column be named `Value`; the existing version-check queries in this file rely on the same convention. `ExecuteSqlRawAsync`, `ToListAsync`, and `FirstOrDefault()` are all already used here.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongDatabaseServiceBookmarkMigrationTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Run the Task 1 tests to confirm no regression**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongDatabaseServiceBookmarkTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs DTXMania.Test/Song/SongDatabaseServiceBookmarkMigrationTests.cs
git commit -m "feat: add idempotent IsBookmarked column migration"
```

---

### Task 3: SongManager wrappers (`GetBookmarkedNodesAsync`, `SetBookmarkAsync`)

**Files:**
- Modify: `DTXMania.Game/Lib/Song/SongManager.cs` (add after `GetRecentlyPlayedNodesAsync`, ~line 1606)
- Test: `DTXMania.Test/Song/SongManagerBookmarkTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Song/SongManagerBookmarkTests.cs`:

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
    public class SongManagerBookmarkTests : IDisposable
    {
        private readonly SongManager _manager;
        private readonly string _dbPath;

        public SongManagerBookmarkTests()
        {
            SongManager.ResetInstanceForTesting();
            _manager = SongManager.Instance;
            _dbPath = Path.Combine(Path.GetTempPath(), $"mgr_bookmark_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            _manager.Clear();
            SongManager.ResetInstanceForTesting();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        private async Task<int> AddSong(SongDatabaseService db, string title)
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = title, Artist = "A" };
            var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            await db.AddSongAsync(song, chart);
            return (await db.GetSongsAsync()).Single(s => s.Title == title).Id;
        }

        [Fact]
        public async Task GetBookmarkedNodesAsync_ReturnsScoreNodesAlphabetically()
        {
            await _manager.InitializeDatabaseServiceAsync(_dbPath, purgeDatabaseFirst: true);
            var db = _manager.DatabaseService!;
            var b = await AddSong(db, "Bravo");
            var a = await AddSong(db, "Alpha");
            await db.SetBookmarkAsync(b, true);
            await db.SetBookmarkAsync(a, true);

            var nodes = await _manager.GetBookmarkedNodesAsync();

            Assert.Equal(2, nodes.Count);
            Assert.All(nodes, n => Assert.Equal(NodeType.Score, n.Type));
            Assert.Equal("Alpha", nodes[0].DisplayTitle);
            Assert.Equal("Bravo", nodes[1].DisplayTitle);
        }

        [Fact]
        public async Task GetBookmarkedNodesAsync_WhenDatabaseServiceNull_ReturnsEmptyList()
        {
            // Not initializing the DB service: the null-guard returns empty, no throw.
            var nodes = await _manager.GetBookmarkedNodesAsync();
            Assert.Empty(nodes);
        }

        [Fact]
        public async Task SetBookmarkAsync_PersistsThroughManager()
        {
            await _manager.InitializeDatabaseServiceAsync(_dbPath, purgeDatabaseFirst: true);
            var db = _manager.DatabaseService!;
            var id = await AddSong(db, "Song");

            await _manager.SetBookmarkAsync(id, true);

            Assert.True((await db.GetSongsAsync()).Single(s => s.Id == id).IsBookmarked);
        }

        [Fact]
        public async Task SetBookmarkAsync_WhenDatabaseServiceNull_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() => _manager.SetBookmarkAsync(1, true));
            Assert.Null(ex);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongManagerBookmarkTests"`
Expected: FAIL — compile error (`GetBookmarkedNodesAsync`, `SetBookmarkAsync` do not exist on SongManager).

- [ ] **Step 3: Add the wrappers**

In `DTXMania.Game/Lib/Song/SongManager.cs`, immediately after the closing brace of `GetRecentlyPlayedNodesAsync` (~line 1606):

```csharp
        /// <summary>
        /// Returns bookmarked songs as flat Score nodes, alphabetical by title. Returns an
        /// empty list if the database is unavailable. Reuses the same node builder as the
        /// browse list so difficulty cycling, status panel, preview, and activation behave
        /// identically. Exceptions from the database layer propagate so the caller
        /// (BeginBookmarksLoad) can distinguish a genuine failure from an empty result.
        /// </summary>
        public async Task<List<SongListNode>> GetBookmarkedNodesAsync()
        {
            var db = GetDatabaseServiceSnapshot();
            if (db == null) return new List<SongListNode>();

            var songs = await db.GetBookmarkedSongsAsync().ConfigureAwait(false);
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

        /// <summary>
        /// Sets or clears the bookmark flag on a song. Safe no-op when the database is
        /// unavailable.
        /// </summary>
        public async Task SetBookmarkAsync(int songId, bool bookmarked)
        {
            var db = GetDatabaseServiceSnapshot();
            if (db == null) return;
            await db.SetBookmarkAsync(songId, bookmarked).ConfigureAwait(false);
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongManagerBookmarkTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/SongManager.cs DTXMania.Test/Song/SongManagerBookmarkTests.cs
git commit -m "feat: add SongManager bookmark node and toggle wrappers"
```

---

### Task 4: Extend the tab enum with `Bookmarks`

**Files:**
- Modify: `DTXMania.Game/Lib/Song/SongSelectionTab.cs`
- Test: `DTXMania.Test/Song/SongSelectionTabTests.cs` (create)

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
        public void Next_CyclesAllSongs_Recent_Bookmarks_AndWraps()
        {
            Assert.Equal(SongSelectionTab.RecentPlays, SongSelectionTab.AllSongs.Next());
            Assert.Equal(SongSelectionTab.Bookmarks, SongSelectionTab.RecentPlays.Next());
            Assert.Equal(SongSelectionTab.AllSongs, SongSelectionTab.Bookmarks.Next());
        }

        [Fact]
        public void DisplayLabel_ReturnsExpectedLabels()
        {
            Assert.Equal("All Songs", SongSelectionTab.AllSongs.DisplayLabel());
            Assert.Equal("Recent", SongSelectionTab.RecentPlays.DisplayLabel());
            Assert.Equal("Bookmarks", SongSelectionTab.Bookmarks.DisplayLabel());
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionTabTests"`
Expected: FAIL — compile error (`SongSelectionTab.Bookmarks` does not exist).

- [ ] **Step 3: Extend the enum and extensions**

In `DTXMania.Game/Lib/Song/SongSelectionTab.cs`, add the enum member:

```csharp
    public enum SongSelectionTab
    {
        AllSongs = 0,
        RecentPlays = 1,
        Bookmarks = 2
    }
```

Update `Next()` (note the switch is currently exhaustive without a default; add the new arm and re-point RecentPlays):

```csharp
        public static SongSelectionTab Next(this SongSelectionTab tab)
        {
            return tab switch
            {
                SongSelectionTab.AllSongs => SongSelectionTab.RecentPlays,
                SongSelectionTab.RecentPlays => SongSelectionTab.Bookmarks,
                SongSelectionTab.Bookmarks => SongSelectionTab.AllSongs,
                _ => SongSelectionTab.AllSongs
            };
        }
```

Update `DisplayLabel()`:

```csharp
        public static string DisplayLabel(this SongSelectionTab tab)
        {
            return tab switch
            {
                SongSelectionTab.AllSongs => "All Songs",
                SongSelectionTab.RecentPlays => "Recent",
                SongSelectionTab.Bookmarks => "Bookmarks",
                _ => tab.ToString()
            };
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionTabTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Run the existing tab tests to confirm no regression**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageTabTests"`
Expected: PASS — note `SwitchToNextTab_FromRecent_WrapsBackToAllSongs` now lands on `Bookmarks`, not `AllSongs`. **Update that existing test** in `DTXMania.Test/Stage/SongSelectionStageTabTests.cs` (~line 110): rename to `SwitchToNextTab_FromRecent_GoesToBookmarks` and change the assertion to `Assert.Equal(SongSelectionTab.Bookmarks, ...)`. Re-run; expected PASS.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Song/SongSelectionTab.cs DTXMania.Test/Song/SongSelectionTabTests.cs DTXMania.Test/Stage/SongSelectionStageTabTests.cs
git commit -m "feat: add Bookmarks tab to song-selection tab cycle"
```

---

### Task 5: Wire the Bookmarks tab into SongSelectionStage (async load + populate + draw)

This mirrors the recent-plays machinery. All new state is reset in `OnActivate` alongside the recent-plays reset.

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Test: `DTXMania.Test/Stage/SongSelectionStageBookmarkTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Stage/SongSelectionStageBookmarkTests.cs`:

```csharp
using System.Collections.Generic;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;

namespace DTXMania.Test.Stage
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongSelectionStageBookmarkTests
    {
        private static SongListNode ScoreNode(string title) => new SongListNode
        {
            Type = NodeType.Score,
            Title = title
        };

        [Fact]
        public void RefreshSongListForActiveTab_OnBookmarksTab_ShowsCachedNodes()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_bookmarkNodes",
                new List<SongListNode> { ScoreNode("B1"), ScoreNode("B2") });

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Equal(2, display.CurrentList.Count);
            Assert.Equal("B1", display.CurrentList[0].Title);
            Assert.False(GetPrivateField<bool>(stage, "_showEmptyBookmarksMessage"));
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnBookmarksTab_WhenEmpty_SetsEmptyFlag()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_bookmarkNodes", new List<SongListNode>());

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.Empty(display.CurrentList);
            Assert.True(GetPrivateField<bool>(stage, "_showEmptyBookmarksMessage"));
        }

        [Fact]
        public void RefreshSongListForActiveTab_OnBookmarksTab_WhenLoadFailed_DoesNotShowEmptyMessage()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_bookmarkNodes", new List<SongListNode>());
            SetPrivateField(stage, "_bookmarksLoadFailed", true);

            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.False(GetPrivateField<bool>(stage, "_showEmptyBookmarksMessage"));
            Assert.True(GetPrivateField<bool>(stage, "_bookmarksLoadFailed"));
        }

        [Fact]
        public void SwitchToNextTab_FromRecent_GoesToBookmarks()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);

            InvokePrivateMethod(stage, "SwitchToNextTab");

            Assert.Equal(SongSelectionTab.Bookmarks,
                GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
            Assert.True(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageBookmarkTests"`
Expected: FAIL — fields `_bookmarkNodes`, `_showEmptyBookmarksMessage`, `_bookmarksLoadFailed` do not exist.

- [ ] **Step 3: Add the bookmark state fields**

In `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`, immediately after the `_recentPlaysLoadFailed` field (~line 69):

```csharp
        // Cached bookmark nodes (flat Score nodes), refreshed on activation and on switching
        // into the Bookmarks tab. Null until first load. volatile: published from a background
        // load continuation and read on the update thread, paired with _tabListNeedsRefresh.
        private volatile List<SongListNode>? _bookmarkNodes;
        private bool _showEmptyBookmarksMessage;
        // True when the most recent BeginBookmarksLoad continuation failed (DB/IO error).
        private bool _bookmarksLoadFailed;
```

- [ ] **Step 4: Reset bookmark state in OnActivate**

In `OnActivate` (~line 260), immediately after the `_recentPlaysLoadFailed = false;` line (~line 263), add:

```csharp
            _bookmarkNodes = null;
            _showEmptyBookmarksMessage = false;
            _bookmarksLoadFailed = false;
```

- [ ] **Step 5: Route the Bookmarks tab through refresh + populate**

In `RefreshSongListForActiveTab` (~line 1009), change the body to handle the third tab:

```csharp
        private void RefreshSongListForActiveTab()
        {
            if (_activeTab == SongSelectionTab.RecentPlays)
                PopulateRecentPlaysList();
            else if (_activeTab == SongSelectionTab.Bookmarks)
                PopulateBookmarksList();
            else
                PopulateSongListForCurrentMode();
        }
```

Add `PopulateBookmarksList` immediately after `PopulateRecentPlaysList` (~line 1024):

```csharp
        private void PopulateBookmarksList()
        {
            var nodes = _bookmarkNodes ?? new List<SongListNode>();
            _songListDisplay.CurrentList = new List<SongListNode>(nodes);
            _showEmptyBookmarksMessage = !_bookmarksLoadFailed && nodes.Count == 0;
        }
```

- [ ] **Step 6: Kick the load on tab switch**

In `SwitchToNextTab` (~line 2014), after the `if (_activeTab == SongSelectionTab.RecentPlays) BeginRecentPlaysLoad();` block, add:

```csharp
            if (_activeTab == SongSelectionTab.Bookmarks)
                BeginBookmarksLoad();
```

Add `BeginBookmarksLoad` immediately after `BeginRecentPlaysLoad` (~line 2069):

```csharp
        /// <summary>
        /// Loads bookmark nodes in the background and flags a repopulate when done.
        /// Safe when the DB is unavailable (returns an empty list). Captures
        /// <see cref="_activationVersion"/> so a completion that lands after a later
        /// Deactivate/Activate cycle is discarded instead of overwriting fresh state.
        /// </summary>
        private void BeginBookmarksLoad()
        {
            int capturedVersion = _activationVersion;
            _ = SongManager.Instance.GetBookmarkedNodesAsync()
                .ContinueWith(task =>
                {
                    if (task.IsFaulted || task.IsCanceled)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"SongSelectionStage: bookmarks load failed:\n{task.Exception}");
                        if (capturedVersion == _activationVersion)
                        {
                            _bookmarksLoadFailed = true;
                            if (_activeTab == SongSelectionTab.Bookmarks)
                                _tabListNeedsRefresh = true;
                        }
                        return;
                    }
                    if (capturedVersion != _activationVersion)
                        return;
                    _bookmarksLoadFailed = false;
                    _bookmarkNodes = task.Result;
                    if (_activeTab == SongSelectionTab.Bookmarks)
                        _tabListNeedsRefresh = true;
                }, TaskScheduler.Default);
        }
```

- [ ] **Step 7: Warm the cache on activation**

In `OnActivate`, find the existing `BeginRecentPlaysLoad();` call (~line 271) and add directly after it:

```csharp
            BeginBookmarksLoad();
```

- [ ] **Step 8: Draw the Bookmarks empty/failed message**

In `OnDraw`, immediately after the Recent-tab status-message block (~line 1148, the `}` closing the `if (_activeTab == SongSelectionTab.RecentPlays ...)`), add:

```csharp
            if (_activeTab == SongSelectionTab.Bookmarks && _font != null
                && (_searchFilterModal == null || !_searchFilterModal.IsOpen)
                && (_bookmarksLoadFailed || _showEmptyBookmarksMessage))
            {
                string msg = _bookmarksLoadFailed
                    ? "Could not load bookmarks"
                    : "No bookmarks yet";
                _font.DrawString(_spriteBatch, msg,
                    new Vector2(SongSelectionUILayout.SongBars.UnselectedBarX + 100, SongSelectionUILayout.SongBars.SelectedBarY),
                    Microsoft.Xna.Framework.Color.LightGray);
            }
```

- [ ] **Step 9: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageBookmarkTests"`
Expected: PASS (4 tests).

- [ ] **Step 10: Build the game project to confirm it compiles**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded.

- [ ] **Step 11: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs DTXMania.Test/Stage/SongSelectionStageBookmarkTests.cs
git commit -m "feat: wire Bookmarks tab into song selection stage"
```

---

### Task 6: Toggle bookmark via `B` key and Floor Tom pad

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Test: `DTXMania.Test/Stage/SongSelectionStageBookmarkToggleTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Stage/SongSelectionStageBookmarkToggleTests.cs`:

```csharp
using System.Collections.Generic;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using Microsoft.Xna.Framework.Input;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;

namespace DTXMania.Test.Stage
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongSelectionStageBookmarkToggleTests
    {
        private static SongListNode BookmarkableNode(string title, bool bookmarked = false)
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Id = 42,
                Title = title,
                IsBookmarked = bookmarked
            };
            return new SongListNode
            {
                Type = NodeType.Score,
                Title = title,
                DatabaseSong = song
            };
        }

        private static SongListDisplay DisplayWithSelection(SongListNode node)
        {
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { node }
            };
            // CurrentList setter resets SelectedIndex to 0 -> SelectedSong == node.
            return display;
        }

        [Fact]
        public void ToggleBookmarkForSelectedSong_OnScoreNode_FlipsBookmarkFlag()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: false);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

            Assert.True(node.DatabaseSong!.IsBookmarked);

            InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");
            Assert.False(node.DatabaseSong!.IsBookmarked);
        }

        [Fact]
        public void ToggleBookmarkForSelectedSong_OnFolderNode_DoesNothing()
        {
            var stage = CreateStage();
            var folder = new SongListNode { Type = NodeType.Box, Title = "Folder" };
            var display = DisplayWithSelection(folder);
            AttachCoreUi(stage, display);

            var ex = Record.Exception(() =>
                InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong"));

            Assert.Null(ex);
        }

        [Fact]
        public void ToggleBookmarkForSelectedSong_OnBookmarksTab_Unbookmark_RemovesNodeAndFlagsRefresh()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: true);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.Bookmarks);
            SetPrivateField(stage, "_bookmarkNodes", new List<SongListNode> { node });

            InvokePrivateMethod(stage, "ToggleBookmarkForSelectedSong");

            Assert.False(node.DatabaseSong!.IsBookmarked);
            var remaining = GetPrivateField<List<SongListNode>>(stage, "_bookmarkNodes");
            Assert.Empty(remaining);
            Assert.True(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
        }

        [Fact]
        public void HandleLaneHitForBookmark_OnFloorTom_TogglesBookmark()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: false);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);

            InvokePrivateMethod(stage, "HandleLaneHitForBookmark", 1); // Floor Tom = lane 1

            Assert.True(node.DatabaseSong!.IsBookmarked);
        }

        [Fact]
        public void HandleLaneHitForBookmark_OnOtherLane_DoesNotToggle()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: false);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);

            InvokePrivateMethod(stage, "HandleLaneHitForBookmark", 4); // Snare

            Assert.False(node.DatabaseSong!.IsBookmarked);
        }

        [Fact]
        public void DetectBookmarkKey_WhenBPressed_TogglesBookmark()
        {
            var stage = CreateStage();
            var node = BookmarkableNode("Song", bookmarked: false);
            var display = DisplayWithSelection(node);
            AttachCoreUi(stage, display);
            SetPrivateField(stage, "_inputManager", new BookmarkKeyInputManager());

            InvokePrivateMethod(stage, "DetectBookmarkKey");

            Assert.True(node.DatabaseSong!.IsBookmarked);
        }

        [Fact]
        public void DetectBookmarkKey_WhenInputManagerNull_DoesNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_inputManager", null);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "DetectBookmarkKey"));
            Assert.Null(ex);
        }

        // Reports the B key as pressed exactly once.
        private sealed class BookmarkKeyInputManager : DTXMania.Game.Lib.Input.InputManager
        {
            private bool _consumed;
            public override bool IsKeyPressed(int keyCode)
            {
                if (keyCode == (int)Keys.B && !_consumed)
                {
                    _consumed = true;
                    return true;
                }
                return false;
            }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageBookmarkToggleTests"`
Expected: FAIL — methods `ToggleBookmarkForSelectedSong`, `HandleLaneHitForBookmark`, `DetectBookmarkKey` do not exist.

- [ ] **Step 3: Add the Floor Tom lane constant**

In `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`, immediately after the `LowTomLaneIndex` constant (~line 128):

```csharp
        // Floor Tom in the lane-hit (KeyBindings) numbering; used to toggle a bookmark on the
        // highlighted song. Distinct from the tab-switch pad (Low Tom = lane 8).
        private const int FloorTomLaneIndex = 1;
```

- [ ] **Step 4: Add the toggle method**

In `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`, add this method immediately after `BeginBookmarksLoad` (added in Task 5, ~within the Tab Switching region):

```csharp
        /// <summary>
        /// Toggles the bookmark flag on the highlighted song. No-op for non-song nodes
        /// (folders/back boxes). Updates the in-memory flag immediately so the star marker
        /// refreshes this frame, persists asynchronously, and—on the Bookmarks tab when
        /// un-bookmarking—removes the row from the cached list and requests a repopulate.
        /// </summary>
        private void ToggleBookmarkForSelectedSong()
        {
            var node = _songListDisplay?.SelectedSong;
            if (node == null || node.Type != NodeType.Score)
                return;

            var song = node.DatabaseSong;
            if (song == null)
                return;

            bool newState = !song.IsBookmarked;
            song.IsBookmarked = newState; // immediate, in-memory; star refreshes next draw.
            int songId = song.Id;

            // Persist asynchronously; log faults without crashing the stage.
            _ = SongManager.Instance.SetBookmarkAsync(songId, newState)
                .ContinueWith(task =>
                {
                    if (task.IsFaulted || task.IsCanceled)
                        System.Diagnostics.Debug.WriteLine(
                            $"SongSelectionStage: bookmark persist failed:\n{task.Exception}");
                }, TaskScheduler.Default);

            // On the Bookmarks tab, an un-bookmark should drop the row from view.
            if (_activeTab == SongSelectionTab.Bookmarks && !newState)
            {
                _bookmarkNodes?.RemoveAll(n =>
                    ReferenceEquals(n, node) || n.DatabaseSong?.Id == songId);
                _tabListNeedsRefresh = true;
            }

            PlayCursorMoveSound();
        }

        private void HandleLaneHitForBookmark(int lane)
        {
            if (lane == FloorTomLaneIndex)
                ToggleBookmarkForSelectedSong();
        }
```

- [ ] **Step 5: Wire the Floor Tom pad into the existing lane-hit handler**

In `OnTabSwitchLaneHit` (~line 2071), after the `HandleLaneHitForTabSwitch(e.Lane);` line, add:

```csharp
            HandleLaneHitForBookmark(e.Lane);
```

(The modal-open early-return above already suppresses both tab-switch and bookmark while the search modal is open.)

- [ ] **Step 6: Add the `B` key detection**

Add the method after `DetectTabSwitchKey` (~line 1223):

```csharp
        private void DetectBookmarkKey()
        {
            // 'B' toggles a bookmark on the highlighted song. Routed through InputManager so
            // MCP/E2E injected keys are honored. Suppressed implicitly while the search modal
            // is open because HandleInput early-returns before calling this.
            if (_inputManager != null &&
                _inputManager.IsKeyPressed((int)Microsoft.Xna.Framework.Input.Keys.B))
            {
                ToggleBookmarkForSelectedSong();
            }
        }
```

Then call it in `HandleInput` (~line 1192), immediately after `DetectTabSwitchKey();`:

```csharp
            DetectBookmarkKey();
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageBookmarkToggleTests"`
Expected: PASS (7 tests).

- [ ] **Step 8: Run the existing tab tests to confirm no regression**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageTabTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs DTXMania.Test/Stage/SongSelectionStageBookmarkToggleTests.cs
git commit -m "feat: toggle song bookmark via B key and Floor Tom pad"
```

---

### Task 7: Star marker on bookmarked song bars

A render-agnostic helper decides whether to show the star (unit-tested on Mac); the draw call lives in the graphics-bound display (not unit-tested on Mac, consistent with other rendering).

**Files:**
- Create: `DTXMania.Game/Lib/Song/Components/SongBookmarkIndicator.cs`
- Modify: `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs` (add star offset constants to the `SongBars` class, ~line 242)
- Modify: `DTXMania.Game/Lib/Song/Components/SongListDisplay.cs` (`DrawBarInfoWithPerspective`, ~line 885)
- Test: `DTXMania.Test/Song/SongBookmarkIndicatorTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Song/SongBookmarkIndicatorTests.cs`:

```csharp
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongBookmarkIndicatorTests
    {
        [Fact]
        public void ShouldShow_WhenNull_ReturnsFalse()
        {
            Assert.False(SongBookmarkIndicator.ShouldShow(null));
        }

        [Fact]
        public void ShouldShow_WhenFolderNode_ReturnsFalse()
        {
            var node = new SongListNode { Type = NodeType.Box };
            Assert.False(SongBookmarkIndicator.ShouldShow(node));
        }

        [Fact]
        public void ShouldShow_WhenScoreNotBookmarked_ReturnsFalse()
        {
            var node = new SongListNode
            {
                Type = NodeType.Score,
                DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song { IsBookmarked = false }
            };
            Assert.False(SongBookmarkIndicator.ShouldShow(node));
        }

        [Fact]
        public void ShouldShow_WhenScoreBookmarked_ReturnsTrue()
        {
            var node = new SongListNode
            {
                Type = NodeType.Score,
                DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song { IsBookmarked = true }
            };
            Assert.True(SongBookmarkIndicator.ShouldShow(node));
        }

        [Fact]
        public void ShouldShow_WhenScoreNodeHasNoDatabaseSong_ReturnsFalse()
        {
            var node = new SongListNode { Type = NodeType.Score, DatabaseSong = null };
            Assert.False(SongBookmarkIndicator.ShouldShow(node));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongBookmarkIndicatorTests"`
Expected: FAIL — `SongBookmarkIndicator` does not exist.

- [ ] **Step 3: Create the helper**

Create `DTXMania.Game/Lib/Song/Components/SongBookmarkIndicator.cs`:

```csharp
namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// Render-agnostic decision logic for the bookmark star marker on song bars.
    /// Kept separate from the renderer so it is unit-testable without a GraphicsDevice.
    /// </summary>
    public static class SongBookmarkIndicator
    {
        /// <summary>The glyph drawn on a bookmarked song bar.</summary>
        public const string Glyph = "★";

        /// <summary>
        /// True when the node is a real song whose database entity is bookmarked.
        /// </summary>
        public static bool ShouldShow(SongListNode? node)
        {
            return node != null
                && node.Type == NodeType.Score
                && node.DatabaseSong?.IsBookmarked == true;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongBookmarkIndicatorTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Add star layout offset constants**

In `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs`, inside the `SongBars` static class (~line 242, near `UnselectedBarX`), add:

```csharp
            // Bookmark star marker offsets, relative to the bar's top-left (itemBounds).
            public const int BookmarkStarOffsetX = 20;
            public const int BookmarkStarOffsetY = 6;
```

- [ ] **Step 6: Draw the star in the bar renderer path**

In `DTXMania.Game/Lib/Song/Components/SongListDisplay.cs`, inside `DrawBarInfoWithPerspective`, immediately before the method's closing brace (after the title is drawn), add:

```csharp
            // Bookmark star marker (drawn as an overlay so the cached title texture is not
            // invalidated when bookmark state changes).
            if (_font != null && SongBookmarkIndicator.ShouldShow(barInfo.SongNode))
            {
                var starPos = new Vector2(
                    itemBounds.X + SongSelectionUILayout.SongBars.BookmarkStarOffsetX,
                    itemBounds.Y + SongSelectionUILayout.SongBars.BookmarkStarOffsetY);
                spriteBatch.DrawString(_font, SongBookmarkIndicator.Glyph, starPos, Color.Gold * opacityFactor);
            }
```

(`_font` is the `SpriteFont` field already used in this file; `opacityFactor` is the method parameter; `barInfo.SongNode` is populated by `SongBarRenderer.GenerateBarInfo`. `SongBookmarkIndicator` is in the same `DTXMania.Game.Lib.Song.Components` namespace, so no extra `using` is needed.)

- [ ] **Step 7: Build the game project**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded. (If the font lacks the `★` glyph and renders as a missing-glyph box, that is a cosmetic follow-up, not a build failure — note it but proceed.)

- [ ] **Step 8: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongBookmarkIndicator.cs DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs DTXMania.Game/Lib/Song/Components/SongListDisplay.cs DTXMania.Test/Song/SongBookmarkIndicatorTests.cs
git commit -m "feat: draw star marker on bookmarked song bars"
```

---

### Task 8: Full regression pass

- [ ] **Step 1: Build the game**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run the full Mac test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass, including the new bookmark suites and the updated `SongSelectionStageTabTests`.

- [ ] **Step 3: Manual smoke (optional but recommended)**

Run: `dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj`
Verify: In Song Select, highlight a song and press `B` (or hit the Floor Tom pad) → a star appears on its bar. Press `Tab` twice to reach the Bookmarks tab → the song is listed. Un-bookmark it there → it disappears from the list. Restart the app → the bookmark persists.

- [ ] **Step 4: Final commit (only if Step 3 surfaced fixes)**

```bash
git add -A
git commit -m "test: verify song bookmarks end to end"
```

---

## Self-Review Notes

**Spec coverage:** Data-model boolean column (Task 1) ✓; idempotent schema upgrade for existing DBs (Task 2) ✓; DB read/write methods alphabetical by title, uncapped (Task 1) ✓; SongManager wrappers (Task 3) ✓; `B` key + Floor Tom toggle, modal-guarded, song-nodes-only (Task 6) ✓; Bookmarks tab with async load/stale guard/empty+failed states (Task 5) ✓; star marker in All Songs via testable helper (Task 7) ✓; un-bookmark removes row on Bookmarks tab (Task 6) ✓; DB + stage tests (all tasks) ✓.

**Type consistency:** `IsBookmarked` (entity), `SetBookmarkAsync`/`GetBookmarkedSongsAsync` (DB service), `GetBookmarkedNodesAsync`/`SetBookmarkAsync` (SongManager), `_bookmarkNodes`/`_showEmptyBookmarksMessage`/`_bookmarksLoadFailed` (stage fields), `BeginBookmarksLoad`/`PopulateBookmarksList`/`ToggleBookmarkForSelectedSong`/`HandleLaneHitForBookmark`/`DetectBookmarkKey` (stage methods), `FloorTomLaneIndex = 1`, `SongBookmarkIndicator.ShouldShow`/`.Glyph` — all referenced consistently across tasks.

**Known cross-task touch:** Task 4 changes the Recent→next-tab target, so the existing `SongSelectionStageTabTests.SwitchToNextTab_FromRecent_WrapsBackToAllSongs` is updated in Task 4 Step 5.
