# NX Score Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users manually import existing DTXManiaNX `*.dtx.score.ini` drum scores into the CX SQLite database from the Config page, merging best-of with existing CX data without inflating play counts.

**Architecture:** A pure `NxScoreIniParser` (Shift-JIS INI → `NxScoreData` DTO) feeds an `NxScoreImporter` (DTO + `SongDbContext` → best-of merge with snapshot-delta counters + per-song history merge). `SongManager.ImportNxScoresAsync` orchestrates over all drum charts already in the DB. A new "Import NX Scores" `NavigationConfigItem` in `ConfigStage` triggers it asynchronously with a live status line. Two new `SongScore` columns track imported NX counts for idempotency.

**Tech Stack:** .NET 8, EF Core + SQLite (`Microsoft.EntityFrameworkCore.Sqlite`), xUnit, MonoGame (ConfigStage UI). Shift-JIS via the already-registered `CodePagesEncodingProvider`.

**Reference spec:** `docs/superpowers/specs/2026-06-08-nx-score-import-design.md`

---

## File Structure

- **Create** `DTXMania.Game/Lib/Song/NxScoreData.cs` — DTO (`NxScoreData`, `NxHistoryLine`).
- **Create** `DTXMania.Game/Lib/Song/NxScoreIniParser.cs` — static parser.
- **Create** `DTXMania.Game/Lib/Song/NxScoreImporter.cs` — merge logic + rank map.
- **Create** `DTXMania.Game/Lib/Song/NxImportResult.cs` — `NxImportResult` + `NxImportProgress`.
- **Modify** `DTXMania.Game/Lib/Song/Entities/SongScore.cs` — add 2 columns + Clone.
- **Modify** `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs` — additive migration.
- **Modify** `DTXMania.Game/Lib/Song/SongManager.cs` — `ImportNxScoresAsync`.
- **Modify** `DTXMania.Game/Lib/Stage/ConfigStage.cs` — menu item + status line.
- **Create** `DTXMania.Test/TestData/NxScores/{mas,ext,full}.dtx.score.ini` — fixtures.
- **Create** `DTXMania.Test/Song/NxScoreIniParserTests.cs`.
- **Create** `DTXMania.Test/Song/NxScoreImporterTests.cs`.
- **Create** `DTXMania.Test/Song/SongScoreNxColumnsMigrationTests.cs`.
- **Create** `DTXMania.Test/Song/SongManagerNxImportTests.cs`.
- **Modify** `DTXMania.Test/DTXMania.Test.csproj` and `DTXMania.Test/DTXMania.Test.Mac.csproj` — copy fixtures to output.

**Build/test commands (Mac):**
- Build game: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
- Run tests: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
- Filtered: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NxScore"`

---

## Task 1: Add NX-import snapshot columns to SongScore

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Entities/SongScore.cs`
- Test: `DTXMania.Test/Song/SongScoreNxColumnsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Song/SongScoreNxColumnsTests.cs`:

```csharp
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongScoreNxColumnsTests : System.IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SongDbContext> _options;

        public SongScoreNxColumnsTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            _connection.Open();
            _options = new DbContextOptionsBuilder<SongDbContext>().UseSqlite(_connection).Options;
            using var setup = new SongDbContext(_options);
            setup.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        [Fact]
        public async Task SongScore_PersistsNxImportedCounts()
        {
            int chartId;
            using (var ctx = new SongDbContext(_options))
            {
                var chart = new SongChart { Song = new SongEntity { Title = "S" }, FilePath = "a.dtx" };
                ctx.SongCharts.Add(chart);
                await ctx.SaveChangesAsync();
                chartId = chart.Id;
                ctx.SongScores.Add(new SongScore
                {
                    ChartId = chartId, Instrument = EInstrumentPart.DRUMS,
                    NxImportedPlayCount = 79, NxImportedClearCount = 72
                });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new SongDbContext(_options))
            {
                var saved = await ctx.SongScores.AsNoTracking().FirstAsync(s => s.ChartId == chartId);
                Assert.Equal(79, saved.NxImportedPlayCount);
                Assert.Equal(72, saved.NxImportedClearCount);
            }
        }

        [Fact]
        public void Clone_CopiesNxImportedCounts()
        {
            var score = new SongScore { NxImportedPlayCount = 5, NxImportedClearCount = 3 };
            var clone = score.Clone();
            Assert.Equal(5, clone.NxImportedPlayCount);
            Assert.Equal(3, clone.NxImportedClearCount);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongScoreNxColumnsTests"`
Expected: FAIL — compile error, `SongScore` has no `NxImportedPlayCount`/`NxImportedClearCount`.

- [ ] **Step 3: Add the columns to the entity**

In `DTXMania.Game/Lib/Song/Entities/SongScore.cs`, inside the `#region Statistics` block (right after `public int MaxCombo { get; set; }`), add:

```csharp
/// <summary>
/// Cumulative NX PlayCount already imported into PlayCount (snapshot for delta merge).
/// </summary>
public int NxImportedPlayCount { get; set; }

/// <summary>
/// Cumulative NX ClearCount already imported into ClearCount (snapshot for delta merge).
/// </summary>
public int NxImportedClearCount { get; set; }
```

In the same file, inside `Clone()` (the object initializer that returns `new SongScore { ... }`), add these two lines before the closing `};`:

```csharp
                NxImportedPlayCount = NxImportedPlayCount,
                NxImportedClearCount = NxImportedClearCount,
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongScoreNxColumnsTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Entities/SongScore.cs DTXMania.Test/Song/SongScoreNxColumnsTests.cs
git commit -m "feat: add NxImported snapshot columns to SongScore"
```

---

## Task 2: Additive schema migration for existing databases

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`
- Test: `DTXMania.Test/Song/SongScoreNxColumnsMigrationTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Song/SongScoreNxColumnsMigrationTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongScoreNxColumnsMigrationTests : IDisposable
    {
        private readonly string _dbPath;

        public SongScoreNxColumnsMigrationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"nxcols_mig_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) { try { File.Delete(_dbPath); } catch { } }
        }

        private async Task<int> ColumnCountAsync(string column)
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('SongScores') WHERE name='{column}'";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_OnLegacyDbMissingColumns_AddsThem()
        {
            var first = new SongDatabaseService(_dbPath);
            await first.InitializeDatabaseAsync();
            first.Dispose();

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                await conn.OpenAsync();
                foreach (var col in new[] { "NxImportedPlayCount", "NxImportedClearCount" })
                {
                    using var drop = conn.CreateCommand();
                    drop.CommandText = $"ALTER TABLE SongScores DROP COLUMN {col}";
                    await drop.ExecuteNonQueryAsync();
                }
            }
            Assert.Equal(0, await ColumnCountAsync("NxImportedPlayCount"));

            var second = new SongDatabaseService(_dbPath);
            await second.InitializeDatabaseAsync();
            second.Dispose();

            Assert.Equal(1, await ColumnCountAsync("NxImportedPlayCount"));
            Assert.Equal(1, await ColumnCountAsync("NxImportedClearCount"));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongScoreNxColumnsMigrationTests"`
Expected: FAIL — after re-init the columns are still missing (count 0), because nothing re-adds them.

- [ ] **Step 3: Add the migration**

In `DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs`, in `ConfigureUtf8EncodingAsync`, after the existing `await EnsureBookmarkColumnAsync(context);` line, add:

```csharp
            // Additive schema upgrade: NX-import snapshot columns.
            await EnsureNxImportColumnsAsync(context);
```

Then add this new method directly after `EnsureBookmarkColumnAsync`:

```csharp
        /// <summary>
        /// Ensures SongScores.NxImportedPlayCount / NxImportedClearCount exist. Fresh
        /// databases already have them via EnsureCreated; pre-existing databases get them
        /// added here exactly once. Idempotent; a concurrent duplicate-column error is
        /// treated as success, a genuine failure propagates.
        /// </summary>
        private async Task EnsureNxImportColumnsAsync(SongDbContext context)
        {
            foreach (var column in new[] { "NxImportedPlayCount", "NxImportedClearCount" })
            {
                var columnCount = await context.Database.SqlQueryRaw<int>(
                    $"SELECT COUNT(*) FROM pragma_table_info('SongScores') WHERE name='{column}'"
                ).ToListAsync();

                if (columnCount.FirstOrDefault() != 0)
                    continue;

                try
                {
                    await context.Database.ExecuteSqlRawAsync(
                        $"ALTER TABLE SongScores ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0");
                    System.Diagnostics.Debug.WriteLine($"SongDatabaseService: Added SongScores.{column} column");
                }
                catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
                {
                    // Concurrent initializer added it; nothing to do.
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"SongDatabaseService: Failed to add {column} column during schema migration.", ex);
                }
            }
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongScoreNxColumnsMigrationTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Entities/SongDatabaseService.cs DTXMania.Test/Song/SongScoreNxColumnsMigrationTests.cs
git commit -m "feat: migrate existing DBs to add NxImported columns"
```

---

## Task 3: NxScoreData DTO + NxScoreIniParser

**Files:**
- Create: `DTXMania.Game/Lib/Song/NxScoreData.cs`
- Create: `DTXMania.Game/Lib/Song/NxScoreIniParser.cs`
- Create: `DTXMania.Test/TestData/NxScores/{mas,ext,full}.dtx.score.ini`
- Modify: `DTXMania.Test/DTXMania.Test.csproj`, `DTXMania.Test/DTXMania.Test.Mac.csproj`
- Test: `DTXMania.Test/Song/NxScoreIniParserTests.cs`

- [ ] **Step 1: Copy the sample fixtures and wire them to copy to output**

Copy the three real samples into the test project (preserves Shift-JIS bytes verbatim):

```bash
mkdir -p DTXMania.Test/TestData/NxScores
cp data/sample_score/mas.dtx.score.ini DTXMania.Test/TestData/NxScores/
cp data/sample_score/ext.dtx.score.ini DTXMania.Test/TestData/NxScores/
cp data/sample_score/full.dtx.score.ini DTXMania.Test/TestData/NxScores/
```

In `DTXMania.Test/DTXMania.Test.csproj`, add a new `ItemGroup` before `</Project>`:

```xml
  <ItemGroup>
    <None Include="TestData/NxScores/**/*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

In `DTXMania.Test/DTXMania.Test.Mac.csproj`, add the identical `ItemGroup` before `</Project>` (the Mac csproj's `EnableDefaultCompileItems=false` only affects `Compile` items, so `None` copy items must still be declared explicitly):

```xml
  <ItemGroup>
    <None Include="TestData/NxScores/**/*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 2: Write the failing test**

Create `DTXMania.Test/Song/NxScoreIniParserTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class NxScoreIniParserTests
    {
        private static string Fixture(string name) =>
            Path.Combine(AppContext.BaseDirectory, "TestData", "NxScores", name);

        [Fact]
        public void Parse_Mas_ReadsDrumBestStats()
        {
            var data = NxScoreIniParser.Parse(Fixture("mas.dtx.score.ini"));
            Assert.NotNull(data);
            Assert.Equal(958247, data!.BestScore);
            Assert.Equal(2293, data.BestPerfect);
            Assert.Equal(271, data.BestGreat);
            Assert.Equal(11, data.BestGood);
            Assert.Equal(0, data.BestPoor);
            Assert.Equal(0, data.BestMiss);
            Assert.Equal(2575, data.BestMaxCombo);
            Assert.Equal(2575, data.TotalChips);
            Assert.Equal(79, data.PlayCount);
            Assert.Equal(72, data.ClearCount);
            Assert.Equal(1, data.BestRankOrdinal);
            Assert.True(data.UsedMidi);
            Assert.Equal(5, data.History.Count);
            Assert.True(data.HasDrumData);
        }

        [Fact]
        public void Parse_Mas_ReadsSkillAndLastPlay()
        {
            var data = NxScoreIniParser.Parse(Fixture("mas.dtx.score.ini"))!;
            Assert.Equal(154.774601941748, data.HighSkill, 4);
            Assert.Equal(94.3747572815534, data.BestAchievementRate, 4);
            Assert.NotNull(data.LastPlayedAt);
            Assert.Equal(new DateTime(2026, 5, 15), data.LastPlayedAt!.Value.Date);
            Assert.Equal(958247, data.LastScore);
        }

        [Fact]
        public void Parse_Mas_FirstHistoryLineParsesNewestDate()
        {
            var data = NxScoreIniParser.Parse(Fixture("mas.dtx.score.ini"))!;
            var newest = data.History.OrderByDescending(h => h.Date).First();
            Assert.Equal(new DateTime(2026, 5, 15), newest.Date);
            Assert.Contains("Cleared", newest.Text);
        }

        [Fact]
        public void Parse_Ext_ReadsNxVersionVariant()
        {
            var data = NxScoreIniParser.Parse(Fixture("ext.dtx.score.ini"));
            Assert.NotNull(data);
            Assert.Equal(811924, data!.BestScore);
            Assert.Equal(1, data.PlayCount);
            Assert.Equal(1, data.ClearCount);
            Assert.Single(data.History);
        }

        [Fact]
        public void Parse_Full_IgnoresMojibakeTitleAndReadsDrums()
        {
            // full.dtx.score.ini has a Shift-JIS (non-ASCII) Title; the parser must not
            // depend on decoding it. Drum stats must still parse.
            var data = NxScoreIniParser.Parse(Fixture("full.dtx.score.ini"));
            Assert.NotNull(data);
            Assert.Equal(707780, data!.BestScore);
            Assert.Equal(9, data.PlayCount);
        }

        [Fact]
        public void Parse_MissingFile_ReturnsNull()
        {
            Assert.Null(NxScoreIniParser.Parse(Fixture("does-not-exist.score.ini")));
        }

        [Fact]
        public void Parse_NoDrumData_ReturnsNull()
        {
            var path = Path.Combine(Path.GetTempPath(), $"nodrum_{Guid.NewGuid()}.score.ini");
            File.WriteAllText(path,
                "[File]\nPlayCountDrums=0\n[HiScore.Drums]\nScore=0\nPerfect=0\n");
            try { Assert.Null(NxScoreIniParser.Parse(path)); }
            finally { File.Delete(path); }
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NxScoreIniParserTests"`
Expected: FAIL — `NxScoreIniParser` / `NxScoreData` do not exist (compile error).

- [ ] **Step 4: Create the DTO**

Create `DTXMania.Game/Lib/Song/NxScoreData.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// One recent-play history line parsed from an NX score.ini [File] section.
    /// </summary>
    public sealed class NxHistoryLine
    {
        public string Text { get; init; } = "";
        public DateTime Date { get; init; }
    }

    /// <summary>
    /// Drum-only projection of a DTXManiaNX &lt;chart&gt;.score.ini file. Holds only the
    /// fields the importer consumes. See docs/superpowers/specs/2026-06-08-nx-score-import-design.md.
    /// </summary>
    public sealed class NxScoreData
    {
        // Best-by-score ([HiScore.Drums])
        public int BestScore { get; set; }
        public int BestPerfect { get; set; }
        public int BestGreat { get; set; }
        public int BestGood { get; set; }
        public int BestPoor { get; set; }
        public int BestMiss { get; set; }
        public int BestMaxCombo { get; set; }
        public int TotalChips { get; set; }
        public double BestAchievementRate { get; set; } // [HiScore.Drums] PlaySkill

        // Best-by-skill ([HiSkill.Drums])
        public double HighSkill { get; set; } // [HiSkill.Drums] Skill

        // [File]
        public int PlayCount { get; set; }
        public int ClearCount { get; set; }
        public int BestRankOrdinal { get; set; } = 99; // NX ERANK; 99 == UNKNOWN

        // [LastPlay.Drums]
        public int LastScore { get; set; }
        public double LastSkill { get; set; }
        public DateTime? LastPlayedAt { get; set; }
        public string LastProgress { get; set; } = "";

        // Input flags (from [HiScore.Drums])
        public bool UsedKeyboard { get; set; }
        public bool UsedMidi { get; set; }
        public bool UsedJoypad { get; set; }
        public bool UsedMouse { get; set; }

        public IReadOnlyList<NxHistoryLine> History { get; set; } = Array.Empty<NxHistoryLine>();

        /// <summary>True when there is meaningful drum data worth importing.</summary>
        public bool HasDrumData => PlayCount > 0 || BestScore > 0;
    }
}
```

- [ ] **Step 5: Create the parser**

Create `DTXMania.Game/Lib/Song/NxScoreIniParser.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Parses a DTXManiaNX &lt;chart&gt;.score.ini (Shift-JIS) into the drum-only
    /// <see cref="NxScoreData"/>. Returns null when the file is absent, unreadable, or
    /// has no drum data. Only ASCII-valued fields are consumed, so a non-ASCII Title is
    /// never decoded/depended upon.
    /// </summary>
    public static class NxScoreIniParser
    {
        public static NxScoreData? Parse(string scoreIniPath)
        {
            if (string.IsNullOrEmpty(scoreIniPath) || !File.Exists(scoreIniPath))
                return null;

            Dictionary<string, Dictionary<string, string>> sections;
            try
            {
                sections = ReadSections(scoreIniPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NxScoreIniParser: failed to read {scoreIniPath}: {ex.Message}");
                return null;
            }

            var file = Section(sections, "File");
            var hiScore = Section(sections, "HiScore.Drums");
            var hiSkill = Section(sections, "HiSkill.Drums");
            var lastPlay = Section(sections, "LastPlay.Drums");

            var data = new NxScoreData
            {
                BestScore = GetInt(hiScore, "Score"),
                BestPerfect = GetInt(hiScore, "Perfect"),
                BestGreat = GetInt(hiScore, "Great"),
                BestGood = GetInt(hiScore, "Good"),
                BestPoor = GetInt(hiScore, "Poor"),
                BestMiss = GetInt(hiScore, "Miss"),
                BestMaxCombo = GetInt(hiScore, "MaxCombo"),
                TotalChips = GetInt(hiScore, "TotalChips"),
                BestAchievementRate = GetDouble(hiScore, "PlaySkill"),
                HighSkill = GetDouble(hiSkill, "Skill"),
                PlayCount = GetInt(file, "PlayCountDrums"),
                ClearCount = GetInt(file, "ClearCountDrums"),
                BestRankOrdinal = GetInt(file, "BestRankDrums", 99),
                LastScore = GetInt(lastPlay, "Score"),
                LastSkill = GetDouble(lastPlay, "Skill"),
                LastPlayedAt = ParseDateTime(GetString(lastPlay, "DateTime")),
                LastProgress = GetString(lastPlay, "Progress"),
                UsedKeyboard = GetInt(hiScore, "UseKeyboard") != 0,
                UsedMidi = GetInt(hiScore, "UseMIDIIN") != 0,
                UsedJoypad = GetInt(hiScore, "UseJoypad") != 0,
                UsedMouse = GetInt(hiScore, "UseMouse") != 0,
                History = ParseHistory(file),
            };

            return data.HasDrumData ? data : null;
        }

        private static Dictionary<string, Dictionary<string, string>> ReadSections(string path)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string>? current = null;

            // Shift-JIS is registered globally (CodePagesEncodingProvider). Every consumed
            // field is ASCII, so even a decode hiccup on Title is harmless.
            var encoding = Encoding.GetEncoding("Shift_JIS");
            foreach (var raw in File.ReadAllLines(path, encoding))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    var name = line.Substring(1, line.Length - 2).Trim();
                    if (!result.TryGetValue(name, out current))
                    {
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        result[name] = current;
                    }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0 || current == null) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                current[key] = value; // last value wins
            }
            return result;
        }

        private static Dictionary<string, string> Section(
            Dictionary<string, Dictionary<string, string>> sections, string name)
            => sections.TryGetValue(name, out var s) ? s : new Dictionary<string, string>();

        private static int GetInt(Dictionary<string, string> s, string key, int fallback = 0)
            => s.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

        private static double GetDouble(Dictionary<string, string> s, string key)
            => s.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;

        private static string GetString(Dictionary<string, string> s, string key)
            => s.TryGetValue(key, out var v) ? v : "";

        private static DateTime? ParseDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt : (DateTime?)null;
        }

        private static IReadOnlyList<NxHistoryLine> ParseHistory(Dictionary<string, string> file)
        {
            var list = new List<NxHistoryLine>();
            for (int i = 0; i < 5; i++)
            {
                var text = GetString(file, $"History{i}");
                if (string.IsNullOrWhiteSpace(text)) continue;
                list.Add(new NxHistoryLine { Text = text, Date = ParseHistoryDate(text) });
            }
            return list;
        }

        // Format: "{playIndex}.{yy}/{m}/{d} {status} ({rank}: {skill})"
        // e.g. "79.26/5/15 Cleared (S: 94.37)" -> 2026-05-15
        private static DateTime ParseHistoryDate(string text)
        {
            try
            {
                int dot = text.IndexOf('.');
                if (dot < 0) return DateTime.MinValue;
                var afterDot = text.Substring(dot + 1);
                int space = afterDot.IndexOf(' ');
                var dateToken = (space < 0 ? afterDot : afterDot.Substring(0, space)).Trim();
                var parts = dateToken.Split('/');
                if (parts.Length != 3) return DateTime.MinValue;
                int yy = int.Parse(parts[0], CultureInfo.InvariantCulture);
                int m = int.Parse(parts[1], CultureInfo.InvariantCulture);
                int d = int.Parse(parts[2], CultureInfo.InvariantCulture);
                return new DateTime(2000 + yy, m, d);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NxScoreIniParserTests"`
Expected: PASS (7 tests).

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Song/NxScoreData.cs DTXMania.Game/Lib/Song/NxScoreIniParser.cs \
  DTXMania.Test/Song/NxScoreIniParserTests.cs DTXMania.Test/TestData/NxScores \
  DTXMania.Test/DTXMania.Test.csproj DTXMania.Test/DTXMania.Test.Mac.csproj
git commit -m "feat: add NX score.ini parser and fixtures"
```

---

## Task 4: NxScoreImporter (merge + rank map + history)

**Files:**
- Create: `DTXMania.Game/Lib/Song/NxScoreImporter.cs`
- Test: `DTXMania.Test/Song/NxScoreImporterTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Song/NxScoreImporterTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class NxScoreImporterTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SongDbContext> _options;
        private readonly NxScoreImporter _importer = new();

        public NxScoreImporterTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            _connection.Open();
            _options = new DbContextOptionsBuilder<SongDbContext>().UseSqlite(_connection).Options;
            using var setup = new SongDbContext(_options);
            setup.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private SongChart SeedChart(string title = "Song", string file = "a.dtx", int? songId = null)
        {
            using var ctx = new SongDbContext(_options);
            var song = songId.HasValue
                ? ctx.Songs.First(s => s.Id == songId.Value)
                : new SongEntity { Title = title };
            var chart = new SongChart { Song = song, FilePath = file, HasDrumChart = true, DrumLevel = 78 };
            ctx.SongCharts.Add(chart);
            ctx.SaveChanges();
            return chart;
        }

        private SongScore Load(int chartId)
        {
            using var ctx = new SongDbContext(_options);
            return ctx.SongScores.AsNoTracking().First(s => s.ChartId == chartId && s.Instrument == EInstrumentPart.DRUMS);
        }

        private static NxScoreData Mas() => new()
        {
            BestScore = 958247, BestPerfect = 2293, BestGreat = 271, BestGood = 11,
            BestMaxCombo = 2575, TotalChips = 2575, BestAchievementRate = 94.37,
            HighSkill = 154.77, PlayCount = 79, ClearCount = 72, BestRankOrdinal = 1,
            LastScore = 958247, LastSkill = 154.77,
            LastPlayedAt = new DateTime(2026, 5, 15, 17, 54, 24), LastProgress = "2222",
            UsedMidi = true,
        };

        private async Task<bool> Merge(SongChart chart, NxScoreData data)
        {
            using var ctx = new SongDbContext(_options);
            var tracked = ctx.SongCharts.Include(c => c.Song).First(c => c.Id == chart.Id);
            var wrote = await _importer.MergeAsync(ctx, tracked, data);
            return wrote;
        }

        [Fact]
        public async Task Merge_FirstImport_SeedsScoreAndSnapshot()
        {
            var chart = SeedChart();
            await Merge(chart, Mas());

            var s = Load(chart.Id);
            Assert.Equal(958247, s.BestScore);
            Assert.Equal(2293, s.BestPerfect);
            Assert.Equal(2575, s.MaxCombo);
            Assert.Equal(79, s.PlayCount);
            Assert.Equal(72, s.ClearCount);
            Assert.Equal(79, s.NxImportedPlayCount);
            Assert.Equal(72, s.NxImportedClearCount);
            Assert.Equal(90, s.BestRank);            // ordinal 1 -> bucket 90
            Assert.Equal("S", SongScore.RankString(s.BestRank));
            Assert.True(s.UsedMidi);
        }

        [Fact]
        public async Task Merge_RepeatedUnchanged_DoesNotInflateCounts()
        {
            var chart = SeedChart();
            await Merge(chart, Mas());
            await Merge(chart, Mas());

            var s = Load(chart.Id);
            Assert.Equal(79, s.PlayCount);
            Assert.Equal(72, s.ClearCount);
        }

        [Fact]
        public async Task Merge_IncreasedNxCounts_AddsOnlyDelta()
        {
            var chart = SeedChart();
            await Merge(chart, Mas());

            var bumped = Mas();
            bumped.PlayCount = 82; bumped.ClearCount = 74;
            await Merge(chart, bumped);

            var s = Load(chart.Id);
            Assert.Equal(82, s.PlayCount);   // 79 + 3
            Assert.Equal(74, s.ClearCount);  // 72 + 2
        }

        [Fact]
        public async Task Merge_CxHigherScore_RetainsCxBestButAddsCounts()
        {
            var chart = SeedChart();
            using (var ctx = new SongDbContext(_options))
            {
                ctx.SongScores.Add(new SongScore
                {
                    ChartId = chart.Id, Instrument = EInstrumentPart.DRUMS,
                    BestScore = 999999, BestPerfect = 9, PlayCount = 4, ClearCount = 4,
                    HighSkill = 200.0
                });
                ctx.SaveChanges();
            }

            await Merge(chart, Mas());

            var s = Load(chart.Id);
            Assert.Equal(999999, s.BestScore);   // CX best kept
            Assert.Equal(9, s.BestPerfect);      // CX best block kept
            Assert.Equal(200.0, s.HighSkill, 4); // max kept
            Assert.Equal(83, s.PlayCount);       // 4 + 79
            Assert.Equal(76, s.ClearCount);      // 4 + 72
        }

        [Theory]
        [InlineData(0, 95, "SS")]
        [InlineData(1, 90, "S")]
        [InlineData(2, 80, "A")]
        [InlineData(6, 40, "E")]
        public async Task Merge_MapsNxRankOrdinalToBucket(int ordinal, int bucket, string label)
        {
            var chart = SeedChart(file: $"r{ordinal}.dtx");
            var data = Mas();
            data.BestRankOrdinal = ordinal;
            await Merge(chart, data);

            var s = Load(chart.Id);
            Assert.Equal(bucket, s.BestRank);
            Assert.Equal(label, SongScore.RankString(s.BestRank));
        }

        [Fact]
        public async Task Merge_UnknownRank_LeavesRankUnchanged()
        {
            var chart = SeedChart();
            var data = Mas();
            data.BestRankOrdinal = 99;
            await Merge(chart, data);

            var s = Load(chart.Id);
            Assert.Equal(0, s.BestRank); // never raised from default
        }

        [Fact]
        public async Task Merge_CxLastPlayNewer_KeepsCxLastPlay()
        {
            var chart = SeedChart();
            using (var ctx = new SongDbContext(_options))
            {
                ctx.SongScores.Add(new SongScore
                {
                    ChartId = chart.Id, Instrument = EInstrumentPart.DRUMS,
                    LastScore = 123, LastPlayedAt = new DateTime(2030, 1, 1)
                });
                ctx.SaveChanges();
            }

            await Merge(chart, Mas());

            var s = Load(chart.Id);
            Assert.Equal(123, s.LastScore); // NX last-play is older -> not applied
        }

        [Fact]
        public async Task Merge_TwoChartsSameSong_MergesHistoryNewestFiveAcrossCharts()
        {
            var chart1 = SeedChart(title: "Shared", file: "mas.dtx");
            var chart2 = SeedChart(file: "ext.dtx", songId: chart1.SongId);

            // 4 lines from chart1 + 2 from chart2 = 6 distinct candidates -> top 5 drops the oldest.
            var d1 = Mas();
            d1.History = new[]
            {
                new NxHistoryLine { Text = "4.26/5/15 Cleared (S: 90)", Date = new DateTime(2026, 5, 15) },
                new NxHistoryLine { Text = "3.26/5/10 Cleared (A: 80)", Date = new DateTime(2026, 5, 10) },
                new NxHistoryLine { Text = "2.26/5/5 Cleared (A: 79)", Date = new DateTime(2026, 5, 5) },
                new NxHistoryLine { Text = "1.26/5/1 Cleared (A: 78)", Date = new DateTime(2026, 5, 1) },
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
            var rows = ctx.PerformanceHistory.AsNoTracking()
                .Where(p => p.SongId == chart1.SongId)
                .OrderBy(p => p.DisplayOrder).ToList();

            Assert.Equal(5, rows.Count);                       // capped at 5 of 6
            Assert.Equal("2.26/6/5 Cleared (B: 70)", rows[0].HistoryLine); // newest first
            Assert.Equal(1, rows[0].DisplayOrder);
            Assert.Equal(5, rows[4].DisplayOrder);
            Assert.DoesNotContain(rows, r => r.HistoryLine == "1.26/5/1 Cleared (A: 78)"); // oldest dropped
        }

        [Fact]
        public async Task Merge_RepeatedHistory_IsDeduped()
        {
            var chart = SeedChart();
            var data = Mas();
            data.History = new[]
            {
                new NxHistoryLine { Text = "1.26/5/15 Cleared (S: 90)", Date = new DateTime(2026, 5, 15) },
            };

            await Merge(chart, data);
            await Merge(chart, data);

            using var ctx = new SongDbContext(_options);
            var rows = ctx.PerformanceHistory.AsNoTracking().Where(p => p.SongId == chart.SongId).ToList();
            Assert.Single(rows);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NxScoreImporterTests"`
Expected: FAIL — `NxScoreImporter` does not exist (compile error).

- [ ] **Step 3: Create the importer**

Create `DTXMania.Game/Lib/Song/NxScoreImporter.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Merges a parsed <see cref="NxScoreData"/> into the DRUMS SongScore for one chart,
    /// plus the per-song PerformanceHistory. Best fields use idempotent max; PlayCount /
    /// ClearCount use snapshot-delta; last-play uses newest-wins. See the design spec.
    /// </summary>
    public sealed class NxScoreImporter
    {
        /// <summary>
        /// Maps an NX ERANK ordinal (SS=0..E=6, UNKNOWN=99) to the CX percentage bucket.
        /// Returns -1 for "no rank" (unknown / out of range) meaning "leave rank unchanged".
        /// </summary>
        public static int MapNxRankToBucket(int ordinal) => ordinal switch
        {
            0 => 95,
            1 => 90,
            2 => 80,
            3 => 70,
            4 => 60,
            5 => 50,
            6 => 40,
            _ => -1,
        };

        /// <summary>
        /// Applies the merge using the caller's tracked context. The chart must be tracked
        /// (its SongId is used for history). Returns true if any row was written.
        /// </summary>
        public async Task<bool> MergeAsync(SongDbContext ctx, SongChart chart, NxScoreData data)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (chart == null) throw new ArgumentNullException(nameof(chart));
            if (data == null) throw new ArgumentNullException(nameof(data));

            var score = await ctx.SongScores
                .FirstOrDefaultAsync(s => s.ChartId == chart.Id && s.Instrument == EInstrumentPart.DRUMS);
            if (score == null)
            {
                score = new SongScore { ChartId = chart.Id, Instrument = EInstrumentPart.DRUMS };
                ctx.SongScores.Add(score);
            }

            // Best fields (idempotent max).
            if (data.BestScore > score.BestScore)
            {
                score.BestScore = data.BestScore;
                score.BestPerfect = data.BestPerfect;
                score.BestGreat = data.BestGreat;
                score.BestGood = data.BestGood;
                score.BestPoor = data.BestPoor;
                score.BestMiss = data.BestMiss;
            }
            score.TotalNotes = Math.Max(score.TotalNotes, data.TotalChips);
            score.MaxCombo = Math.Max(score.MaxCombo, data.BestMaxCombo);
            score.BestAchievementRate = Math.Max(score.BestAchievementRate, data.BestAchievementRate);
            score.HighSkill = Math.Max(score.HighSkill, data.HighSkill);

            int existingNorm = SongScore.NormalizeStoredBestRank(score.BestRank);
            int nxBucket = MapNxRankToBucket(data.BestRankOrdinal);
            score.BestRank = nxBucket >= 0 ? Math.Max(existingNorm, nxBucket) : existingNorm;

            bool nxFullCombo = data.BestMaxCombo > 0 &&
                data.BestMaxCombo == data.BestPerfect + data.BestGreat + data.BestGood + data.BestPoor + data.BestMiss;
            score.FullCombo = score.FullCombo || nxFullCombo;

            score.UsedKeyboard |= data.UsedKeyboard;
            score.UsedMidi |= data.UsedMidi;
            score.UsedJoypad |= data.UsedJoypad;
            score.UsedMouse |= data.UsedMouse;

            // Counters (snapshot delta).
            score.PlayCount += Math.Max(0, data.PlayCount - score.NxImportedPlayCount);
            score.NxImportedPlayCount = data.PlayCount;
            score.ClearCount += Math.Max(0, data.ClearCount - score.NxImportedClearCount);
            score.NxImportedClearCount = data.ClearCount;

            // Last play (newest wins).
            if (data.LastPlayedAt.HasValue &&
                (!score.LastPlayedAt.HasValue || data.LastPlayedAt.Value > score.LastPlayedAt.Value))
            {
                score.LastScore = data.LastScore;
                score.LastSkillPoint = data.LastSkill;
                score.LastPlayedAt = data.LastPlayedAt;
                if (!string.IsNullOrEmpty(data.LastProgress))
                    score.ProgressBar = data.LastProgress;
            }

            await MergeHistoryAsync(ctx, chart.SongId, data.History);

            await ctx.SaveChangesAsync();
            return true;
        }

        private static async Task MergeHistoryAsync(SongDbContext ctx, int songId, IReadOnlyList<NxHistoryLine> nxHistory)
        {
            if (nxHistory == null || nxHistory.Count == 0) return;

            var existing = await ctx.PerformanceHistory.Where(p => p.SongId == songId).ToListAsync();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<(string Text, DateTime Date)>();
            foreach (var e in existing)
                if (seen.Add(e.HistoryLine)) candidates.Add((e.HistoryLine, e.PerformedAt));
            foreach (var h in nxHistory)
                if (seen.Add(h.Text)) candidates.Add((h.Text, h.Date));

            var top5 = candidates.OrderByDescending(c => c.Date).Take(5).ToList();

            // Delete then re-insert so the (SongId, DisplayOrder) unique index never collides.
            ctx.PerformanceHistory.RemoveRange(existing);
            await ctx.SaveChangesAsync();

            int order = 1;
            foreach (var c in top5)
            {
                ctx.PerformanceHistory.Add(new PerformanceHistory
                {
                    SongId = songId,
                    HistoryLine = c.Text,
                    PerformedAt = c.Date,
                    DisplayOrder = order++,
                });
            }
            // Saved by the caller's SaveChangesAsync (or the next history merge).
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~NxScoreImporterTests"`
Expected: PASS (all tests, including the `[Theory]` cases).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/NxScoreImporter.cs DTXMania.Test/Song/NxScoreImporterTests.cs
git commit -m "feat: add NxScoreImporter best-of merge with snapshot delta"
```

---

## Task 5: SongManager.ImportNxScoresAsync orchestrator

**Files:**
- Create: `DTXMania.Game/Lib/Song/NxImportResult.cs`
- Modify: `DTXMania.Game/Lib/Song/SongManager.cs`
- Test: `DTXMania.Test/Song/SongManagerNxImportTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DTXMania.Test/Song/SongManagerNxImportTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongManagerNxImportTests : IDisposable
    {
        private readonly string _root;
        private readonly string _dbPath;
        private readonly SongManager _manager;

        public SongManagerNxImportTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"nximport_{Guid.NewGuid()}");
            Directory.CreateDirectory(_root);
            _dbPath = Path.Combine(_root, "songs.db");
            SongManager.ResetInstanceForTesting();
            _manager = SongManager.Instance;
        }

        public void Dispose()
        {
            SongManager.ResetInstanceForTesting();
            try { Directory.Delete(_root, true); } catch { }
        }

        private string WriteChartAndScore(string fileName, int playCount, int score, bool withScoreIni = true)
        {
            var dtxPath = Path.Combine(_root, fileName);
            File.WriteAllText(dtxPath, "; dummy chart");
            if (withScoreIni)
            {
                File.WriteAllText(dtxPath + ".score.ini",
                    "[File]\n" +
                    $"PlayCountDrums={playCount}\nClearCountDrums={playCount}\nBestRankDrums=1\n" +
                    "[HiScore.Drums]\n" +
                    $"Score={score}\nPerfect=10\nMaxCombo=10\nTotalChips=10\nUseMIDIIN=1\n" +
                    "[HiSkill.Drums]\nSkill=100.0\n" +
                    "[LastPlay.Drums]\n" +
                    $"Score={score}\nSkill=100.0\nDateTime=5/15/2026 5:54:24 PM\n");
            }
            return dtxPath;
        }

        private async Task<int> SeedChartAsync(string dtxPath, string title)
        {
            var db = _manager.DatabaseService!;
            using var ctx = db.CreateContext();
            var chart = new SongChart
            {
                Song = new SongEntity { Title = title },
                FilePath = dtxPath, HasDrumChart = true, DrumLevel = 50
            };
            ctx.SongCharts.Add(chart);
            await ctx.SaveChangesAsync();
            return chart.Id;
        }

        [Fact]
        public async Task ImportNxScoresAsync_ImportsDrumScoreForSiblingIni()
        {
            Assert.True(await _manager.InitializeDatabaseServiceAsync(_dbPath));
            var dtx = WriteChartAndScore("mas.dtx", playCount: 79, score: 958247);
            var chartId = await SeedChartAsync(dtx, "Against The Wind");

            var result = await _manager.ImportNxScoresAsync();

            Assert.Equal(1, result.Scanned);
            Assert.Equal(1, result.Imported);

            using var ctx = _manager.DatabaseService!.CreateContext();
            var s = ctx.SongScores.AsNoTracking().First(x => x.ChartId == chartId);
            Assert.Equal(958247, s.BestScore);
            Assert.Equal(79, s.PlayCount);
        }

        [Fact]
        public async Task ImportNxScoresAsync_SkipsChartsWithoutScoreIni()
        {
            Assert.True(await _manager.InitializeDatabaseServiceAsync(_dbPath));
            var dtx = WriteChartAndScore("no.dtx", 0, 0, withScoreIni: false);
            await SeedChartAsync(dtx, "Lonely");

            var result = await _manager.ImportNxScoresAsync();

            Assert.Equal(1, result.Scanned);
            Assert.Equal(0, result.Imported);
            Assert.Equal(1, result.Skipped);
        }

        [Fact]
        public async Task ImportNxScoresAsync_RepeatedRun_DoesNotInflatePlayCount()
        {
            Assert.True(await _manager.InitializeDatabaseServiceAsync(_dbPath));
            var dtx = WriteChartAndScore("mas.dtx", playCount: 79, score: 958247);
            var chartId = await SeedChartAsync(dtx, "Against The Wind");

            await _manager.ImportNxScoresAsync();
            await _manager.ImportNxScoresAsync();

            using var ctx = _manager.DatabaseService!.CreateContext();
            var s = ctx.SongScores.AsNoTracking().First(x => x.ChartId == chartId);
            Assert.Equal(79, s.PlayCount);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongManagerNxImportTests"`
Expected: FAIL — `ImportNxScoresAsync` / `NxImportResult` do not exist (compile error).

- [ ] **Step 3: Create the result/progress types**

Create `DTXMania.Game/Lib/Song/NxImportResult.cs`:

```csharp
#nullable enable
namespace DTXMania.Game.Lib.Song
{
    /// <summary>Aggregate outcome of a bulk NX score import.</summary>
    public sealed class NxImportResult
    {
        public int Scanned { get; set; }
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
    }

    /// <summary>Progress snapshot reported during a bulk NX score import.</summary>
    public sealed class NxImportProgress
    {
        public int Scanned { get; set; }
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
        public string CurrentFile { get; set; } = "";
    }
}
```

- [ ] **Step 4: Add the orchestrator to SongManager**

In `DTXMania.Game/Lib/Song/SongManager.cs`, confirm these usings exist at the top (add any that are missing): `System`, `System.IO`, `System.Threading`, `System.Threading.Tasks`, `Microsoft.EntityFrameworkCore`, `System.Diagnostics`. Then add this method inside the `SongManager` class (e.g. right after the existing `UpdateScoreAsync` summary-forwarding method near line 1580):

```csharp
        /// <summary>
        /// Imports DTXManiaNX drum scores from sibling &lt;chart&gt;.score.ini files for every
        /// drum chart already in the database. Best-of merge with snapshot-delta counters;
        /// safe to run repeatedly. Reports progress per chart. See the design spec.
        /// </summary>
        public async Task<NxImportResult> ImportNxScoresAsync(
            IProgress<NxImportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new NxImportResult();
            var db = GetDatabaseServiceSnapshot();
            if (db == null)
            {
                Debug.WriteLine("SongManager: ImportNxScoresAsync called with no database service.");
                return result;
            }

            var importer = new NxScoreImporter();
            using var context = db.CreateContext();

            var charts = await context.SongCharts
                .Include(c => c.Song)
                .Where(c => c.HasDrumChart)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var chart in charts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Scanned++;
                try
                {
                    var iniPath = chart.FilePath + ".score.ini";
                    var data = NxScoreIniParser.Parse(iniPath);
                    if (data == null)
                    {
                        result.Skipped++;
                    }
                    else
                    {
                        await importer.MergeAsync(context, chart, data).ConfigureAwait(false);
                        result.Imported++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    Debug.WriteLine($"SongManager: NX import error for {chart.FilePath}: {ex.Message}");
                }

                progress?.Report(new NxImportProgress
                {
                    Scanned = result.Scanned,
                    Imported = result.Imported,
                    Skipped = result.Skipped,
                    Errors = result.Errors,
                    CurrentFile = Path.GetFileName(chart.FilePath)
                });
            }

            Debug.WriteLine($"SongManager: NX import complete — scanned {result.Scanned}, imported {result.Imported}, skipped {result.Skipped}, errors {result.Errors}.");
            return result;
        }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongManagerNxImportTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Song/NxImportResult.cs DTXMania.Game/Lib/Song/SongManager.cs DTXMania.Test/Song/SongManagerNxImportTests.cs
git commit -m "feat: add SongManager.ImportNxScoresAsync orchestrator"
```

---

## Task 6: ConfigStage "Import NX Scores" menu item + status line

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/ConfigStage.cs`

> No unit test: `ConfigStage` requires a live `GraphicsDevice`/`SpriteBatch` and is excluded from the Mac suite, consistent with the rest of the stage. The async trigger delegates to `SongManager.ImportNxScoresAsync`, which is covered by Task 5. Verification is a build plus a manual smoke check.

- [ ] **Step 1: Add the Song namespace using + status fields**

In `DTXMania.Game/Lib/Stage/ConfigStage.cs`, add this using with the other `using DTXMania.Game.Lib.*` lines near the top (it is not currently present, and is required for `SongManager` / `NxImportProgress`):

```csharp
using DTXMania.Game.Lib.Song;
```

Then, in the private field region near the other UI fields (around line 52-55 where `_spriteBatch`/`_font` are declared), add:

```csharp
        private volatile string _importStatus = "";
        private volatile bool _importRunning;
```

- [ ] **Step 2: Add the menu item**

In `SetupConfigItems()`, after the two key-mapping navigation items (after the `"System Key Mapping"` `_configItems.Add(...)` call, around line 353), add:

```csharp
            // NX score import (manual, non-destructive merge)
            _configItems.Add(new NavigationConfigItem("Import NX Scores",
                () => StartNxScoreImport()));
```

- [ ] **Step 3: Add the async trigger method**

Add this method to the `ConfigStage` class (e.g. in the Event Handlers region, after `OnSaveButtonClicked`):

```csharp
        /// <summary>
        /// Starts the NX score import asynchronously. Guarded against re-entry; updates
        /// <see cref="_importStatus"/> for the live status line. Non-destructive merge,
        /// so no confirmation prompt.
        /// </summary>
        private void StartNxScoreImport()
        {
            if (_importRunning)
                return;
            _importRunning = true;
            _importStatus = "Importing NX scores...";

            var progress = new System.Progress<NxImportProgress>(p =>
            {
                _importStatus = $"Importing... {p.Imported} imported / {p.Scanned} scanned";
            });

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await SongManager.Instance.ImportNxScoresAsync(progress);
                    _importStatus = $"Imported {result.Imported} scores ({result.Scanned} charts scanned" +
                        (result.Errors > 0 ? $", {result.Errors} errors)" : ")");
                }
                catch (System.Exception ex)
                {
                    _importStatus = "NX import failed (see log)";
                    System.Diagnostics.Debug.WriteLine($"ConfigStage: NX import failed: {ex.Message}");
                }
                finally
                {
                    _importRunning = false;
                }
            });
        }
```

- [ ] **Step 4: Draw the status line**

Find the draw method that renders the menu (the body of `OnUpdate`/`OnDraw` that calls `DrawConfigItems()` and `DrawButtons()` between `_spriteBatch.Begin()`/`End()`). Add a call to a new `DrawImportStatus()` right after `DrawButtons();`. Then add the method:

```csharp
        private void DrawImportStatus()
        {
            if (string.IsNullOrEmpty(_importStatus) || _font == null)
                return;

            int x = MenuX;
            int y = MenuY + (_configItems.Count * MenuItemHeight) + 60;
            _font.DrawString(_spriteBatch, _importStatus, new Vector2(x, y), Color.Cyan);
        }
```

> If `DrawButtons()` is not called from the same method you can see, search `ConfigStage.cs` for `DrawButtons()` and place `DrawImportStatus();` on the line after it (still inside the `_spriteBatch` Begin/End block).

- [ ] **Step 5: Verify build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Manual smoke check**

Run: `dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj`
Navigate Title → Config. Confirm an "Import NX Scores" row appears below "System Key Mapping". Activate it (Enter); confirm the status line shows "Importing..." then "Imported N scores (M charts scanned)". (With no NX score.ini files present, expect "Imported 0 scores".)

- [ ] **Step 7: Commit**

```bash
git add DTXMania.Game/Lib/Stage/ConfigStage.cs
git commit -m "feat: add Import NX Scores action to Config page"
```

---

## Task 7: Full-suite verification

**Files:** none (verification only).

- [ ] **Step 1: Run the full Mac test suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: PASS — all existing tests plus the new `NxScore*`, `SongScoreNxColumns*`, and `SongManagerNxImport*` tests.

- [ ] **Step 2: Build the game**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit (if any incidental fixes were needed)**

```bash
git add -A
git commit -m "test: verify NX score import full suite" --allow-empty
```

---

## Self-Review Notes

- **Spec coverage:** Trigger (Task 6), DB-charts iteration (Task 5), parser/Shift-JIS/sections/history-date (Task 3), best-of + snapshot-delta + last-play + rank map (Task 4), per-song newest-wins history merge (Task 4), schema entity + migration (Tasks 1-2), guards/edge cases (Tasks 3-5 tests), testing wiring incl. both csprojs (Task 3). All covered.
- **Type consistency:** `NxScoreData`, `NxHistoryLine`, `NxScoreImporter.MergeAsync(SongDbContext, SongChart, NxScoreData)`, `NxScoreImporter.MapNxRankToBucket(int)`, `SongManager.ImportNxScoresAsync(IProgress<NxImportProgress>?, CancellationToken)`, `NxImportResult { Scanned, Imported, Skipped, Errors }`, `NxImportProgress` — names used identically across tasks.
- **No hash gating, drums-only, manual trigger** — matches the approved spec.
