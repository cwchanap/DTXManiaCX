# Play History Badge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the DTXManiaNX-style play history badge to song selection, showing the five most recent rows for the currently selected difficulty only.

**Architecture:** Make play history score-scoped at the persistence boundary, copy scoped rows into song-selection score nodes, then render them with a focused `PlayHistoryPanel` sibling of `SongStatusPanel`. A shared merge helper keeps NX import and CX result persistence consistent.

**Tech Stack:** .NET 8, C#, EF Core SQLite, MonoGame UI components, xUnit, Moq.

---

## File Map

- Modify: `DTXMania.Game/Lib/Song/Entities/PerformanceHistory.cs`
  - Add nullable `SongScoreId` and `SongScore` navigation.
- Modify: `DTXMania.Game/Lib/Song/Entities/SongScore.cs`
  - Add `PerformanceHistory` navigation and non-mapped `PlayHistoryLines` display cache.
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDbContext.cs`
  - Configure score-scoped history relationship and indexes.
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
  - Add schema upgrade, eager loading, CX history-row append, and scoped row trimming.
- Create: `DTXMania.Game/Lib/Song/PerformanceHistoryMerger.cs`
  - Shared newest-five/de-dup merge helper for importer and save path.
- Modify: `DTXMania.Game/Lib/Song/NxScoreImporter.cs`
  - Write imported history to the resolved score, not the song-wide pool.
- Modify: `DTXMania.Game/Lib/Song/SongListNode.cs`
  - Copy scoped history display lines from persisted scores into UI scores.
- Modify: `DTXMania.Game/Lib/Resources/TexturePath.cs`
  - Add `PlayHistoryPanel`.
- Modify: `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs`
  - Add play-history panel coordinates and row layout constants.
- Create: `DTXMania.Game/Lib/Song/Components/PlayHistoryPanel.cs`
  - Render the badge and rows.
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
  - Instantiate, initialize, update, and dispose the panel through the UI tree.
- Modify tests:
  - `DTXMania.Test/Song/PerformanceHistoryAndHierarchyTests.cs`
  - `DTXMania.Test/Song/NxScoreImporterTests.cs`
  - `DTXMania.Test/Song/SongDatabaseServiceHighSkillTests.cs`
  - `DTXMania.Test/Song/SongListNodePlayHistoryTests.cs`
  - `DTXMania.Test/Resources/TexturePathTests.cs`
- Create tests:
  - `DTXMania.Test/UI/PlayHistoryPanelLogicTests.cs`

---

### Task 1: Score-Scoped History Schema

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Entities/PerformanceHistory.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/SongScore.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDbContext.cs`
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Test: `DTXMania.Test/Song/PerformanceHistoryAndHierarchyTests.cs`

- [ ] **Step 1: Write failing model tests**

Append these tests to `DTXMania.Test/Song/PerformanceHistoryAndHierarchyTests.cs` inside `PerformanceHistoryTests`:

```csharp
[Fact]
public void PerformanceHistory_ScoreScopeProperties_ShouldRetainValues()
{
    var score = new SongScore { Id = 42, ChartId = 7, Instrument = EInstrumentPart.DRUMS };
    var history = new PerformanceHistory
    {
        SongId = 9,
        SongScoreId = 42,
        SongScore = score,
        DisplayOrder = 3,
        HistoryLine = "3.26/6/13 Cleared (S: 91.23)"
    };

    Assert.Equal(42, history.SongScoreId);
    Assert.Same(score, history.SongScore);
    Assert.Equal("3.26/6/13 Cleared (S: 91.23)", history.HistoryLine);
}

[Fact]
public void SongScore_PlayHistoryLines_ShouldDefaultToEmptyList()
{
    var score = new SongScore();

    Assert.NotNull(score.PlayHistoryLines);
    Assert.Empty(score.PlayHistoryLines);
}
```

- [ ] **Step 2: Run the model tests and verify they fail**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceHistoryTests"
```

Expected: FAIL because `SongScoreId`, `SongScore`, and `PlayHistoryLines` do not exist.

- [ ] **Step 3: Add score-scoped entity properties**

Replace `DTXMania.Game/Lib/Song/Entities/PerformanceHistory.cs` with:

```csharp
using System;
using System.ComponentModel.DataAnnotations;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Performance History Entity
    /// </summary>
    public class PerformanceHistory
    {
        public int Id { get; set; }

        public int SongId { get; set; }
        public virtual Song Song { get; set; } = null!;

        public int? SongScoreId { get; set; }
        public virtual SongScore? SongScore { get; set; }

        public DateTime PerformedAt { get; set; }

        [MaxLength(500)]
        public string HistoryLine { get; set; } = "";

        public int DisplayOrder { get; set; } // 1-5 for 5 history lines
    }
}
```

In `DTXMania.Game/Lib/Song/Entities/SongScore.cs`, add `using System.Collections.Generic;` at the top. Then add this navigation block after the `Instrument` property:

```csharp
public virtual ICollection<PerformanceHistory> PerformanceHistory { get; set; } = new List<PerformanceHistory>();
```

Add this non-mapped display cache near the other legacy compatibility properties:

```csharp
[System.ComponentModel.DataAnnotations.Schema.NotMapped]
public List<string> PlayHistoryLines { get; set; } = new();
```

In `Clone()`, add:

```csharp
PlayHistoryLines = new List<string>(PlayHistoryLines)
```

inside the object initializer, with a trailing comma on the preceding property.

- [ ] **Step 4: Configure EF relationship and indexes**

In `DTXMania.Game/Lib/Song/Entities/SongDbContext.cs`, replace the current PerformanceHistory constraints block with:

```csharp
// PerformanceHistory constraints
modelBuilder.Entity<PerformanceHistory>()
    .HasIndex(p => p.SongId);

modelBuilder.Entity<PerformanceHistory>()
    .HasIndex(p => new { p.SongScoreId, p.DisplayOrder })
    .IsUnique()
    .HasFilter("SongScoreId IS NOT NULL");

modelBuilder.Entity<PerformanceHistory>()
    .HasOne(p => p.Song)
    .WithMany()
    .HasForeignKey(p => p.SongId)
    .OnDelete(DeleteBehavior.Cascade);

modelBuilder.Entity<PerformanceHistory>()
    .HasOne(p => p.SongScore)
    .WithMany(s => s.PerformanceHistory)
    .HasForeignKey(p => p.SongScoreId)
    .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 5: Add additive schema upgrade**

In `ConfigureUtf8EncodingAsync`, after `await EnsureNxImportColumnsAsync(context);`, add:

```csharp
// Additive schema upgrade: score-scoped play history.
await EnsurePerformanceHistoryScoreScopeAsync(context);
```

Add this method below `EnsureNxImportColumnAsync`:

```csharp
private static async Task EnsurePerformanceHistoryScoreScopeAsync(SongDbContext context)
{
    var columnCount = await context.Database.SqlQueryRaw<int>(
        "SELECT COUNT(*) FROM pragma_table_info('PerformanceHistory') WHERE name='SongScoreId'"
    ).ToListAsync();

    if (columnCount.FirstOrDefault() == 0)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE PerformanceHistory ADD COLUMN SongScoreId INTEGER NULL");
            System.Diagnostics.Debug.WriteLine("SongDatabaseService: Added PerformanceHistory.SongScoreId column");
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
        {
            System.Diagnostics.Debug.WriteLine("SongDatabaseService: PerformanceHistory.SongScoreId already exists");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "SongDatabaseService: Failed to add PerformanceHistory.SongScoreId during schema migration.", ex);
        }
    }

    await context.Database.ExecuteSqlRawAsync(
        "DROP INDEX IF EXISTS IX_PerformanceHistory_SongId_DisplayOrder");
    await context.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS IX_PerformanceHistory_SongId ON PerformanceHistory(SongId)");
    await context.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS IX_PerformanceHistory_SongScoreId_DisplayOrder " +
        "ON PerformanceHistory(SongScoreId, DisplayOrder) WHERE SongScoreId IS NOT NULL");
}
```

- [ ] **Step 6: Run tests and commit**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceHistoryTests"
```

Expected: PASS.

Commit:

```bash
git add DTXMania.Game/Lib/Song/Entities/PerformanceHistory.cs DTXMania.Game/Lib/Song/Entities/SongScore.cs DTXMania.Game/Lib/Song/Entities/SongDbContext.cs DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs DTXMania.Test/Song/PerformanceHistoryAndHierarchyTests.cs
git commit -m "feat: scope play history to scores"
```

---

### Task 2: Shared History Merge And NX Import

**Files:**
- Create: `DTXMania.Game/Lib/Song/PerformanceHistoryMerger.cs`
- Modify: `DTXMania.Game/Lib/Song/NxScoreImporter.cs`
- Test: `DTXMania.Test/Song/NxScoreImporterTests.cs`

- [ ] **Step 1: Replace importer history tests with score-scoped expectations**

In `DTXMania.Test/Song/NxScoreImporterTests.cs`, replace `TwoChartsSameSong_ShouldMergeNewestFiveHistoryAcrossCharts` with:

```csharp
[Fact]
public async Task TwoChartsSameSong_ShouldKeepHistoryPerChartScore()
{
    var chart1 = SeedChart(title: "Shared", file: "mas.dtx");
    var chart2 = SeedChart(file: "ext.dtx", songId: chart1.SongId);

    var d1 = Mas();
    d1.History = new[]
    {
        new NxHistoryLine { Text = "4.26/5/15 Cleared (S: 90)", Date = new DateTime(2026, 5, 15) },
        new NxHistoryLine { Text = "3.26/5/10 Cleared (A: 80)", Date = new DateTime(2026, 5, 10) },
    };
    var d2 = Mas();
    d2.History = new[]
    {
        new NxHistoryLine { Text = "2.26/6/5 Cleared (B: 70)", Date = new DateTime(2026, 6, 5) },
        new NxHistoryLine { Text = "1.26/6/1 Cleared (B: 68)", Date = new DateTime(2026, 6, 1) },
    };

    await Merge(chart1, d1);
    await Merge(chart2, d2);

    using var ctx = new SongDbContext(_options);
    var score1 = ctx.SongScores.AsNoTracking().Single(s => s.ChartId == chart1.Id);
    var score2 = ctx.SongScores.AsNoTracking().Single(s => s.ChartId == chart2.Id);

    var rows1 = ctx.PerformanceHistory.AsNoTracking()
        .Where(p => p.SongScoreId == score1.Id)
        .OrderBy(p => p.DisplayOrder)
        .Select(p => p.HistoryLine)
        .ToList();
    var rows2 = ctx.PerformanceHistory.AsNoTracking()
        .Where(p => p.SongScoreId == score2.Id)
        .OrderBy(p => p.DisplayOrder)
        .Select(p => p.HistoryLine)
        .ToList();

    Assert.Equal(new[] { "4.26/5/15 Cleared (S: 90)", "3.26/5/10 Cleared (A: 80)" }, rows1);
    Assert.Equal(new[] { "2.26/6/5 Cleared (B: 70)", "1.26/6/1 Cleared (B: 68)" }, rows2);
}
```

Add this cap test:

```csharp
[Fact]
public async Task SixHistoryRowsForOneScore_ShouldKeepNewestFive()
{
    var chart = SeedChart();
    var data = Mas();
    data.History = Enumerable.Range(1, 6)
        .Select(i => new NxHistoryLine
        {
            Text = $"{i}.26/6/{i} Cleared (A: {70 + i})",
            Date = new DateTime(2026, 6, i)
        })
        .ToArray();

    await Merge(chart, data);

    using var ctx = new SongDbContext(_options);
    var score = ctx.SongScores.AsNoTracking().Single(s => s.ChartId == chart.Id);
    var rows = ctx.PerformanceHistory.AsNoTracking()
        .Where(p => p.SongScoreId == score.Id)
        .OrderBy(p => p.DisplayOrder)
        .ToList();

    Assert.Equal(5, rows.Count);
    Assert.Equal("6.26/6/6 Cleared (A: 76)", rows[0].HistoryLine);
    Assert.DoesNotContain(rows, r => r.HistoryLine == "1.26/6/1 Cleared (A: 71)");
    Assert.All(rows, row => Assert.Equal(score.Id, row.SongScoreId));
}
```

- [ ] **Step 2: Run importer tests and verify failures**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NxScoreImporterTests"
```

Expected: FAIL because history is still merged by `SongId`.

- [ ] **Step 3: Create shared merge helper**

Create `DTXMania.Game/Lib/Song/PerformanceHistoryMerger.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTXMania.Game.Lib.Song
{
    internal readonly record struct PerformanceHistoryCandidate(string Text, DateTime Date);

    internal static class PerformanceHistoryMerger
    {
        public static async Task MergeAsync(
            SongDbContext context,
            int songId,
            int songScoreId,
            IEnumerable<PerformanceHistoryCandidate> incoming,
            CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (songId <= 0) throw new ArgumentOutOfRangeException(nameof(songId));
            if (songScoreId <= 0) throw new ArgumentOutOfRangeException(nameof(songScoreId));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            var existing = await context.PerformanceHistory
                .Where(p => p.SongScoreId == songScoreId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<PerformanceHistoryCandidate>();

            foreach (var row in existing)
            {
                if (!string.IsNullOrWhiteSpace(row.HistoryLine) && seen.Add(row.HistoryLine))
                    candidates.Add(new PerformanceHistoryCandidate(row.HistoryLine, row.PerformedAt));
            }

            foreach (var row in incoming)
            {
                if (!string.IsNullOrWhiteSpace(row.Text) && seen.Add(row.Text))
                    candidates.Add(row);
            }

            var top5 = candidates
                .OrderByDescending(c => c.Date)
                .Take(5)
                .ToList();

            context.PerformanceHistory.RemoveRange(existing);

            int order = 1;
            foreach (var row in top5)
            {
                context.PerformanceHistory.Add(new PerformanceHistory
                {
                    SongId = songId,
                    SongScoreId = songScoreId,
                    PerformedAt = row.Date,
                    HistoryLine = row.Text,
                    DisplayOrder = order++,
                });
            }
        }
    }
}
```

- [ ] **Step 4: Use helper in `NxScoreImporter`**

In `DTXMania.Game/Lib/Song/NxScoreImporter.cs`, delete `MergeHistoryAsync` and replace the call:

```csharp
await MergeHistoryAsync(ctx, chart.SongId, data.History);
```

with:

```csharp
if (score.Id == 0)
{
    await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}

await PerformanceHistoryMerger.MergeAsync(
    ctx,
    chart.SongId,
    score.Id,
    data.History.Select(h => new PerformanceHistoryCandidate(h.Text, h.Date)),
    cancellationToken).ConfigureAwait(false);
```

Keep the final `await ctx.SaveChangesAsync(cancellationToken);`.

- [ ] **Step 5: Run importer tests and commit**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NxScoreImporterTests"
```

Expected: PASS.

Commit:

```bash
git add DTXMania.Game/Lib/Song/PerformanceHistoryMerger.cs DTXMania.Game/Lib/Song/NxScoreImporter.cs DTXMania.Test/Song/NxScoreImporterTests.cs
git commit -m "fix: import play history per score"
```

---

### Task 3: Append CX Play History On Result Save

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Test: `DTXMania.Test/Song/SongDatabaseServiceHighSkillTests.cs`

- [ ] **Step 1: Write failing save-path tests**

Add these usings to `DTXMania.Test/Song/SongDatabaseServiceHighSkillTests.cs`:

```csharp
using System;
using System.Linq;
```

Append to `DTXMania.Test/Song/SongDatabaseServiceHighSkillTests.cs`:

```csharp
[Fact]
public async Task UpdateScoreAsync_WithPerformanceSummary_ShouldAppendScopedPlayHistory()
{
    var chart = await SeedChartAsync();
    await SeedScoreAsync(chart.Id, new SongScore { PlayCount = 0 });

    var summary = new PerformanceSummary
    {
        Score = 800000,
        ClearFlag = true,
        TotalNotes = 100,
        PerfectCount = 80,
        GreatCount = 10,
        MaxCombo = 90,
        PlayingSkill = 91.25,
        GameSkill = 142.0
    };

    await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

    using var ctx = new SongDbContext(_options);
    var score = await ctx.SongScores.AsNoTracking().SingleAsync(s => s.ChartId == chart.Id);
    var row = await ctx.PerformanceHistory.AsNoTracking().SingleAsync(p => p.SongScoreId == score.Id);

    Assert.Equal(chart.SongId, row.SongId);
    Assert.Equal(1, row.DisplayOrder);
    Assert.Contains("Cleared (S: 91.25)", row.HistoryLine);
}

[Fact]
public async Task UpdateScoreAsync_WithSixPerformanceSummaries_ShouldKeepNewestFiveHistoryRows()
{
    var chart = await SeedChartAsync();
    await SeedScoreAsync(chart.Id, new SongScore { PlayCount = 0 });

    for (int i = 0; i < 6; i++)
    {
        await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, new PerformanceSummary
        {
            Score = 100000 + i,
            ClearFlag = i % 2 == 0,
            TotalNotes = 100,
            PerfectCount = 50 + i,
            GreatCount = 10,
            MaxCombo = 60 + i,
            PlayingSkill = 70 + i,
            GameSkill = 100 + i
        });
    }

    using var ctx = new SongDbContext(_options);
    var score = await ctx.SongScores.AsNoTracking().SingleAsync(s => s.ChartId == chart.Id);
    var rows = await ctx.PerformanceHistory.AsNoTracking()
        .Where(p => p.SongScoreId == score.Id)
        .OrderBy(p => p.DisplayOrder)
        .ToListAsync();

    Assert.Equal(5, rows.Count);
    Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Select(r => r.DisplayOrder).ToArray());
    Assert.DoesNotContain(rows, r => r.HistoryLine.StartsWith("1.", StringComparison.Ordinal));
    Assert.Contains(rows, r => r.HistoryLine.StartsWith("6.", StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run save-path tests and verify failures**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongDatabaseServiceHighSkillTests"
```

Expected: FAIL because no CX history rows are appended.

- [ ] **Step 3: Add history row formatting and append call**

Add `using System.Globalization;` to `SongDatabaseService.cs`.

In `UpdateScoreAsync(int chartId, EInstrumentPart instrument, PerformanceSummary summary)`, change the score query to include the chart:

```csharp
var score = await context.SongScores
    .Include(s => s.Chart)
    .FirstOrDefaultAsync(s => s.ChartId == chartId && s.Instrument == instrument);
```

When creating a missing score, load the chart and attach it:

```csharp
if (score == null)
{
    var chart = await context.SongCharts.FirstAsync(c => c.Id == chartId);
    score = new SongScoreEntity { ChartId = chartId, Chart = chart, Instrument = instrument };
    context.SongScores.Add(score);
}
```

Before the final save, after incrementing `PlayCount` and `ClearCount`, insert:

```csharp
await context.SaveChangesAsync();

var nowUtc = DateTime.UtcNow;
var localDate = nowUtc.ToLocalTime();
var rank = SongScoreEntity.RankString((int)Math.Floor(summary.PlayingSkill));
var status = summary.ClearFlag ? "Cleared" : "Failed";
var line = string.Format(
    CultureInfo.InvariantCulture,
    "{0}.{1:yy/M/d} {2} ({3}: {4:F2})",
    score.PlayCount,
    localDate,
    status,
    rank,
    summary.PlayingSkill);

await PerformanceHistoryMerger.MergeAsync(
    context,
    score.Chart.SongId,
    score.Id,
    new[] { new PerformanceHistoryCandidate(line, nowUtc) });
```

Leave the existing final `await context.SaveChangesAsync();` in place so the inserted history rows persist.

- [ ] **Step 4: Run save-path tests and commit**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongDatabaseServiceHighSkillTests"
```

Expected: PASS.

Commit:

```bash
git add DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs DTXMania.Test/Song/SongDatabaseServiceHighSkillTests.cs
git commit -m "feat: append play history after results"
```

---

### Task 4: Load Scoped History Into Song Selection Nodes

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Modify: `DTXMania.Game/Lib/Song/SongListNode.cs`
- Test: `DTXMania.Test/Song/SongListNodePlayHistoryTests.cs`

- [ ] **Step 1: Write failing node-copy test**

Append to `DTXMania.Test/Song/SongListNodePlayHistoryTests.cs`:

```csharp
[Fact]
public void PopulatePlayHistoryFromCharts_PersistedMatch_ShouldCopyScopedHistoryLines()
{
    var node = new SongListNode();
    node.Scores[0] = new SongScore
    {
        ChartId = 7,
        Instrument = EInstrumentPart.DRUMS,
        DifficultyLevel = 78
    };

    var persisted = new SongScore
    {
        Id = 42,
        ChartId = 7,
        Instrument = EInstrumentPart.DRUMS,
        DifficultyLevel = 78,
        PerformanceHistory = new List<PerformanceHistory>
        {
            new() { SongScoreId = 42, DisplayOrder = 2, HistoryLine = "2.26/6/12 Cleared (A: 80.00)" },
            new() { SongScoreId = 42, DisplayOrder = 1, HistoryLine = "3.26/6/13 Cleared (S: 90.00)" },
            new() { SongScoreId = null, DisplayOrder = 1, HistoryLine = "legacy song-wide row" },
        }
    };
    var chart = new SongChart { Id = 7, Scores = new List<SongScore> { persisted } };

    node.PopulatePlayHistoryFromCharts(new[] { chart });

    Assert.Equal(new[]
    {
        "3.26/6/13 Cleared (S: 90.00)",
        "2.26/6/12 Cleared (A: 80.00)"
    }, node.Scores[0].PlayHistoryLines);
}
```

- [ ] **Step 2: Run node tests and verify failure**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListNodePlayHistoryTests"
```

Expected: FAIL because `PlayHistoryLines` is not copied.

- [ ] **Step 3: Eager load score history rows**

In `SongDatabaseService.cs`, update every query that loads `Charts -> Scores` for song-selection nodes to include history:

```csharp
.Include(s => s.Charts)
    .ThenInclude(c => c.Scores)
        .ThenInclude(sc => sc.PerformanceHistory)
```

Apply this to:

- `GetSongsAsync`
- `GetSongWithChartsAsync`
- `GetRecentlyPlayedSongsAsync`
- `GetBookmarkedSongsAsync`

Do not change top-score queries that are not used to build song-selection nodes.

- [ ] **Step 4: Copy scoped history lines into in-memory scores**

In `SongListNode.PopulatePlayHistoryFromCharts`, after copying `LastSkillPoint`, add:

```csharp
score.PlayHistoryLines = persisted.PerformanceHistory?
    .Where(h => h.SongScoreId.HasValue)
    .OrderBy(h => h.DisplayOrder)
    .Select(h => h.HistoryLine)
    .Where(line => !string.IsNullOrWhiteSpace(line))
    .Take(5)
    .ToList() ?? new List<string>();
```

Keep legacy rows with null `SongScoreId` out of the display cache.

- [ ] **Step 5: Run node tests and commit**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListNodePlayHistoryTests"
```

Expected: PASS.

Commit:

```bash
git add DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs DTXMania.Game/Lib/Song/SongListNode.cs DTXMania.Test/Song/SongListNodePlayHistoryTests.cs
git commit -m "feat: load scoped play history for song selection"
```

---

### Task 5: Texture Path And Layout Constants

**Files:**
- Modify: `DTXMania.Game/Lib/Resources/TexturePath.cs`
- Modify: `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs`
- Test: `DTXMania.Test/Resources/TexturePathTests.cs`

- [ ] **Step 1: Write failing texture-path test**

Append to the UI panel section of `TexturePathTests`:

```csharp
[Fact]
public void PlayHistoryPanel_ShouldUseNXPathAndBeIncludedInPanelTextures()
{
    Assert.Equal("Graphics/5_play history panel.png", TexturePath.PlayHistoryPanel);
    Assert.Contains(TexturePath.PlayHistoryPanel, TexturePath.GetAllTexturePaths());
    Assert.Contains(TexturePath.PlayHistoryPanel, TexturePath.GetPanelTextures());
}
```

- [ ] **Step 2: Run texture-path test and verify failure**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~TexturePathTests"
```

Expected: FAIL because `TexturePath.PlayHistoryPanel` does not exist.

- [ ] **Step 3: Add texture constant and layout constants**

In `TexturePath.cs`, after `SkillPointPanel`, add:

```csharp
/// <summary>
/// Play history panel background texture (458x151 natural size).
/// NX: drawn at (700,570) when the status panel texture is available.
/// </summary>
public const string PlayHistoryPanel = "Graphics/5_play history panel.png";
```

Add `PlayHistoryPanel` to `GetAllTexturePaths()` immediately after `SkillPointPanel`.

Add `PlayHistoryPanel` to `GetPanelTextures()` immediately after `SkillPointPanel`.

In `SongSelectionUILayout.cs`, after `SkillPointSection`, add:

```csharp
#region Play History Panel

/// <summary>
/// DTXManiaNX play history panel layout.
/// </summary>
public static class PlayHistoryPanel
{
    public const int X = 700;
    public const int Y = 570;
    public const int Width = 458;
    public const int Height = 151;
    public const int TextOffsetX = 18;
    public const int TextOffsetY = 32;
    public const int RowSpacing = 18;
    public const int MaxRows = 5;

    public static Vector2 Position => new Vector2(X, Y);
    public static Vector2 Size => new Vector2(Width, Height);
    public static Rectangle Bounds => new Rectangle(X, Y, Width, Height);
}

#endregion
```

- [ ] **Step 4: Run texture tests and commit**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~TexturePathTests"
```

Expected: PASS.

Commit:

```bash
git add DTXMania.Game/Lib/Resources/TexturePath.cs DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs DTXMania.Test/Resources/TexturePathTests.cs
git commit -m "feat: add play history panel assets"
```

---

### Task 6: PlayHistoryPanel Component

**Files:**
- Create: `DTXMania.Game/Lib/Song/Components/PlayHistoryPanel.cs`
- Test: `DTXMania.Test/UI/PlayHistoryPanelLogicTests.cs`

- [ ] **Step 1: Write failing panel logic tests**

Create `DTXMania.Test/UI/PlayHistoryPanelLogicTests.cs`:

```csharp
using System.Linq;
using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Moq;
using Xunit;

namespace DTXMania.Test.UI;

[Trait("Category", "UI")]
public class PlayHistoryPanelLogicTests
{
    [Fact]
    public void UpdateSongInfo_WithHistoryForSelectedDifficulty_ShouldShowRows()
    {
        var panel = new PlayHistoryPanel();
        var node = new SongListNode { Type = NodeType.Score };
        node.Scores[0] = new SongScore { PlayHistoryLines = ["1.26/6/13 Cleared (S: 90.00)"] };
        node.Scores[1] = new SongScore { PlayHistoryLines = ["2.26/6/12 Failed (B: 70.00)"] };

        panel.UpdateSongInfo(node, 1);

        Assert.True(panel.Visible);
        Assert.Equal(new[] { "2.26/6/12 Failed (B: 70.00)" }, GetRows(panel));
    }

    [Fact]
    public void UpdateSongInfo_WithNoHistory_ShouldHide()
    {
        var panel = new PlayHistoryPanel { Visible = true };
        var node = new SongListNode { Type = NodeType.Score };
        node.Scores[0] = new SongScore();

        panel.UpdateSongInfo(node, 0);

        Assert.False(panel.Visible);
        Assert.Empty(GetRows(panel));
    }

    [Fact]
    public void UpdateSongInfo_WithFolder_ShouldHide()
    {
        var panel = new PlayHistoryPanel { Visible = true };

        panel.UpdateSongInfo(new SongListNode { Type = NodeType.Box }, 0);

        Assert.False(panel.Visible);
    }

    [Fact]
    public void Initialize_WhenTextureLoadFails_ShouldNotThrow()
    {
        var panel = new PlayHistoryPanel();
        var rm = new Mock<IResourceManager>();
        rm.Setup(r => r.LoadTexture(TexturePath.PlayHistoryPanel)).Throws(new System.Exception("missing"));

        var ex = Record.Exception(() => panel.Initialize(rm.Object));

        Assert.Null(ex);
    }

    private static string[] GetRows(PlayHistoryPanel panel)
    {
        var field = typeof(PlayHistoryPanel).GetField("_historyLines", BindingFlags.Instance | BindingFlags.NonPublic);
        return ((string[])field!.GetValue(panel)!).ToArray();
    }
}
```

- [ ] **Step 2: Run panel tests and verify failure**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PlayHistoryPanelLogicTests"
```

Expected: FAIL because `PlayHistoryPanel` does not exist.

- [ ] **Step 3: Implement the component**

Create `DTXMania.Game/Lib/Song/Components/PlayHistoryPanel.cs`:

```csharp
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// DTXManiaNX-style five-row play history panel for song selection.
    /// </summary>
    public sealed class PlayHistoryPanel : UIElement
    {
        private IResourceManager? _resourceManager;
        private ITexture? _panelTexture;
        private SpriteFont? _font;
        private IFont? _managedFont;
        private string[] _historyLines = Array.Empty<string>();

        public PlayHistoryPanel()
        {
            Position = SongSelectionUILayout.PlayHistoryPanel.Position;
            Size = SongSelectionUILayout.PlayHistoryPanel.Size;
            Visible = false;
        }

        public SpriteFont? Font
        {
            get => _font;
            set => _font = value;
        }

        public IFont? ManagedFont
        {
            get => _managedFont;
            set
            {
                _managedFont = value;
                _font = value?.SpriteFont;
            }
        }

        public void Initialize(IResourceManager resourceManager)
        {
            _resourceManager = resourceManager;
            ReleaseTexture();

            if (_resourceManager == null)
                return;

            try
            {
                _panelTexture = _resourceManager.LoadTexture(TexturePath.PlayHistoryPanel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlayHistoryPanel: Failed to load panel texture: {ex.Message}");
                _panelTexture = null;
            }
        }

        public void UpdateSongInfo(SongListNode? song, int difficulty)
        {
            if (song == null || song.Type != NodeType.Score)
            {
                _historyLines = Array.Empty<string>();
                Visible = false;
                return;
            }

            var score = song.GetScore(difficulty);
            _historyLines = score?.PlayHistoryLines?
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(SongSelectionUILayout.PlayHistoryPanel.MaxRows)
                .ToArray() ?? Array.Empty<string>();

            Visible = _historyLines.Length > 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ReleaseTexture();
            base.Dispose(disposing);
        }

        private void ReleaseTexture()
        {
            _panelTexture?.RemoveReference();
            _panelTexture = null;
        }

        [ExcludeFromCodeCoverage]
        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible || _historyLines.Length == 0)
                return;

            var origin = AbsolutePosition;

            if (_panelTexture != null)
            {
                try
                {
                    _panelTexture.Draw(spriteBatch, origin);
                }
                catch (ObjectDisposedException)
                {
                    _panelTexture = null;
                }
            }

            var textOrigin = new Vector2(
                origin.X + SongSelectionUILayout.PlayHistoryPanel.TextOffsetX,
                origin.Y + SongSelectionUILayout.PlayHistoryPanel.TextOffsetY);

            for (int i = 0; i < _historyLines.Length; i++)
            {
                DrawText(spriteBatch, _historyLines[i], textOrigin + new Vector2(0, i * SongSelectionUILayout.PlayHistoryPanel.RowSpacing));
            }

            base.OnDraw(spriteBatch, deltaTime);
        }

        [ExcludeFromCodeCoverage]
        private void DrawText(SpriteBatch spriteBatch, string text, Vector2 position)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var shadow = position + DTXManiaVisualTheme.FontEffects.DefaultShadowOffset;
            if (_font != null)
            {
                spriteBatch.DrawString(_font, text, shadow, DTXManiaVisualTheme.FontEffects.DefaultShadowColor);
                spriteBatch.DrawString(_font, text, position, Color.Yellow);
            }
            else if (_managedFont != null)
            {
                _managedFont.DrawString(spriteBatch, text, shadow, DTXManiaVisualTheme.FontEffects.DefaultShadowColor);
                _managedFont.DrawString(spriteBatch, text, position, Color.Yellow);
            }
        }
    }
}
```

- [ ] **Step 4: Run panel tests and commit**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PlayHistoryPanelLogicTests"
```

Expected: PASS.

Commit:

```bash
git add DTXMania.Game/Lib/Song/Components/PlayHistoryPanel.cs DTXMania.Test/UI/PlayHistoryPanelLogicTests.cs
git commit -m "feat: add play history panel component"
```

---

### Task 7: Wire Panel Into SongSelectionStage

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Test: `DTXMania.Test/Stage/SongSelectionStageLogicTests.cs`

- [ ] **Step 1: Write failing stage wiring test**

Append to `DTXMania.Test/Stage/SongSelectionStageLogicTests.cs`:

```csharp
[Fact]
public void OnDifficultyChanged_ShouldUpdatePlayHistoryPanel()
{
    var stage = CreateStage();
    var statusPanel = new SongStatusPanel();
    var historyPanel = new PlayHistoryPanel();
    SetPrivateField(stage, "_statusPanel", statusPanel);
    SetPrivateField(stage, "_playHistoryPanel", historyPanel);

    var node = new SongListNode { Type = NodeType.Score };
    node.Scores[2] = new SongScore { PlayHistoryLines = ["3.26/6/13 Cleared (S: 90.00)"] };
    var args = new DifficultyChangedEventArgs(node, 2);

    InvokePrivateMethod(stage, "OnDifficultyChanged", null!, args);

    Assert.True(historyPanel.Visible);
}
```

If this file has no `using DTXMania.Game.Lib.Song.Components;`, add it.

- [ ] **Step 2: Run stage test and verify failure**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageLogicTests"
```

Expected: FAIL because `_playHistoryPanel` does not exist and the difficulty handler does not update it.

- [ ] **Step 3: Add field and initialization**

In `SongSelectionStage`, add a field near `_statusPanel`:

```csharp
private PlayHistoryPanel _playHistoryPanel;
```

In `InitializeUI`, after `_statusPanel` is created, create the panel:

```csharp
_playHistoryPanel = new PlayHistoryPanel
{
    Font = uiFont?.SpriteFont,
    ManagedFont = uiFont
};
```

After `_statusPanel.InitializeAuthenticGraphics(_resourceManager);`, initialize the history panel:

```csharp
_playHistoryPanel.Initialize(_resourceManager);
```

After `_statusPanel.Visible = false;`, set:

```csharp
_playHistoryPanel.Visible = false;
```

When adding children, add it after `_statusPanel` and before `_previewImagePanel`:

```csharp
_mainPanel.AddChild(_playHistoryPanel);
```

- [ ] **Step 4: Update selection and difficulty handlers**

In `OnSongSelectionChanged`, when showing a score, after `_statusPanel.Visible = true;`, add:

```csharp
_playHistoryPanel?.UpdateSongInfo(e.SelectedSong, e.CurrentDifficulty);
```

When hiding the status panel for non-score nodes, add:

```csharp
_playHistoryPanel?.UpdateSongInfo(e.SelectedSong, e.CurrentDifficulty);
```

Where the status panel content updates during scrolling, add:

```csharp
_playHistoryPanel?.UpdateSongInfo(e.SelectedSong, e.CurrentDifficulty);
```

Where the status panel updates after scrolling completes, add:

```csharp
_playHistoryPanel?.UpdateSongInfo(e.SelectedSong, e.CurrentDifficulty);
```

In `OnDifficultyChanged`, after `_statusPanel.UpdateSongInfo(e.Song, e.NewDifficulty);`, add:

```csharp
_playHistoryPanel?.UpdateSongInfo(e.Song, e.NewDifficulty);
```

In `HandleActivateInput`, when entering status panel mode, do not force the history panel visible. It owns visibility through `UpdateSongInfo`.

- [ ] **Step 5: Run stage tests and commit**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageLogicTests"
```

Expected: PASS.

Commit:

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs DTXMania.Test/Stage/SongSelectionStageLogicTests.cs
git commit -m "feat: show play history in song selection"
```

---

### Task 8: Verification Sweep

**Files:**
- Review touched source and test files from Tasks 1-7.

- [ ] **Step 1: Run focused test suite**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~PerformanceHistoryTests|FullyQualifiedName~NxScoreImporterTests|FullyQualifiedName~SongDatabaseServiceHighSkillTests|FullyQualifiedName~SongListNodePlayHistoryTests|FullyQualifiedName~PlayHistoryPanelLogicTests|FullyQualifiedName~TexturePathTests|FullyQualifiedName~SongSelectionStageLogicTests"
```

Expected: PASS.

- [ ] **Step 2: Run Mac build**

Run:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

Expected: PASS.

- [ ] **Step 3: Run broad Mac tests if focused tests pass**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: PASS.

- [ ] **Step 4: Inspect final diff**

Run:

```bash
git status --short
git diff --stat
```

Expected: only intended source and test files are modified.

- [ ] **Step 5: Commit verification-only adjustments if any were needed**

If Task 8 required code or test adjustments, commit them:

```bash
git add DTXMania.Game DTXMania.Test
git commit -m "test: verify play history badge"
```

If Task 8 produced no file changes, do not create an empty commit.
