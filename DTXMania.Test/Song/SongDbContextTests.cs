using System.Linq;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;
using Song = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SongDbContext covering:
    /// - Default OnConfiguring (in-memory SQLite fallback path when no provider is set)
    /// - Configured context (explicit SQLite in-memory options)
    /// - DbSet accessibility for all five entity types
    /// - Basic CRUD operations with AsNoTracking reads to verify actual DB persistence
    /// - Cascade-delete relationships
    /// - Unique-index and FK-constraint enforcement
    ///
    /// All tests are synchronous and avoid the C# "using var" pattern for any local
    /// variable whose compile-time type implements IAsyncDisposable (SongDbContext and
    /// SqliteCommand both inherit types that implement IAsyncDisposable in .NET 6+).
    /// coverlet.msbuild v6 mis-instruments "using var" locals whose type implements
    /// IAsyncDisposable — even in synchronous methods — producing invalid IL that causes
    /// the CLR to throw InvalidProgramException when the test assembly is loaded, which
    /// crashes the test process before any TRX output is written.
    /// </summary>
    [Trait("Category", "SongDbContext")]
    public class SongDbContextTests : System.IDisposable
    {
        // Shared connection so every context for the same test sees the same
        // in-memory SQLite database (SQLite :memory: databases are per-connection).
        private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
        private readonly DbContextOptions<SongDbContext> _options;
        private readonly SongDbContext _context;

        public SongDbContextTests()
        {
            // Use connection-string-level FK enforcement so the flag is set
            // before the connection is opened (most reliable cross-platform approach).
            _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            _connection.Open();

            // Belt-and-suspenders: also set via PRAGMA after open in case the
            // connection-string option is ignored by an older SQLitePCLRaw bundle.
            // Avoid "using var" here: SqliteCommand inherits DbCommand which
            // implements IAsyncDisposable — coverlet.msbuild would mis-instrument it.
            var cmd = _connection.CreateCommand();
            try
            {
                cmd.CommandText = "PRAGMA foreign_keys = ON;";
                cmd.ExecuteNonQuery();
            }
            finally
            {
                cmd.Dispose();
            }

            _options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new SongDbContext(_options);
            _context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }

        /// <summary>
        /// Creates a fresh SongDbContext that shares the same SQLite connection
        /// (and therefore the same in-memory database) but has an empty change tracker.
        /// Use this to verify that values read back are truly loaded from SQLite, not
        /// from EF Core's first-level cache.
        ///
        /// Callers must dispose the returned context manually via try/finally rather
        /// than "using var", because coverlet.msbuild v6 mis-instruments "using var"
        /// for types that implement IAsyncDisposable.
        /// </summary>
        private SongDbContext NewContext() => new SongDbContext(_options);

        // ---------------------------------------------------------------------------
        // Default OnConfiguring path (no provider → OnConfiguring adds ":memory:" SQLite)
        // ---------------------------------------------------------------------------

        [Fact]
        public void OnConfiguring_WithUnconfiguredOptions_ShouldFallBackToInMemorySQLite()
        {
            // Pass empty options so OnConfiguring takes the "!IsConfigured" branch.
            var emptyOptions = new DbContextOptionsBuilder<SongDbContext>().Options;
            // Avoid "using var ctx": SongDbContext implements IAsyncDisposable.
            var ctx = new SongDbContext(emptyOptions);
            try
            {
                // EnsureCreated triggers context initialization, which calls OnConfiguring.
                // If OnConfiguring successfully adds a provider this will not throw.
                ctx.Database.EnsureCreated();
            }
            finally
            {
                ctx.Dispose();
            }
        }

        // ---------------------------------------------------------------------------
        // DbSet accessibility
        // ---------------------------------------------------------------------------

        [Fact]
        public void Songs_DbSet_ShouldBeAccessible()
            => Assert.NotNull(_context.Songs);

        [Fact]
        public void SongCharts_DbSet_ShouldBeAccessible()
            => Assert.NotNull(_context.SongCharts);

        [Fact]
        public void SongScores_DbSet_ShouldBeAccessible()
            => Assert.NotNull(_context.SongScores);

        [Fact]
        public void SongHierarchy_DbSet_ShouldBeAccessible()
            => Assert.NotNull(_context.SongHierarchy);

        [Fact]
        public void PerformanceHistory_DbSet_ShouldBeAccessible()
            => Assert.NotNull(_context.PerformanceHistory);

        // ---------------------------------------------------------------------------
        // Song CRUD  – all reads use a fresh context to bypass the change tracker
        // ---------------------------------------------------------------------------

        [Fact]
        public void Song_AddAndRetrieve_ShouldRoundTrip()
        {
            var song = new Song { Title = "Test Song", Artist = "Test Artist", Genre = "Rock" };
            _context.Songs.Add(song);
            _context.SaveChanges();

            // Fresh context → values come from SQLite, not the change tracker.
            var ctx2 = NewContext();
            try
            {
                var retrieved = ctx2.Songs.AsNoTracking()
                    .FirstOrDefault(s => s.Title == "Test Song");

                Assert.NotNull(retrieved);
                Assert.Equal("Test Artist", retrieved!.Artist);
                Assert.Equal("Rock", retrieved.Genre);
            }
            finally
            {
                ctx2.Dispose();
            }
        }

        [Fact]
        public void Song_Delete_ShouldRemoveFromDatabase()
        {
            var song = new Song { Title = "To Delete", Artist = "Artist" };
            _context.Songs.Add(song);
            _context.SaveChanges();

            _context.Songs.Remove(song);
            _context.SaveChanges();

            var ctx2 = NewContext();
            try
            {
                var count = ctx2.Songs.Count(s => s.Title == "To Delete");
                Assert.Equal(0, count);
            }
            finally
            {
                ctx2.Dispose();
            }
        }

        [Fact]
        public void Song_Update_ShouldPersistChanges()
        {
            var song = new Song { Title = "Original Title", Artist = "Artist" };
            _context.Songs.Add(song);
            _context.SaveChanges();

            song.Title = "Updated Title";
            _context.SaveChanges();

            // Detach so Find cannot return the cached entity.
            _context.Entry(song).State = EntityState.Detached;
            var retrieved = _context.Songs.AsNoTracking()
                .FirstOrDefault(s => s.Id == song.Id);
            Assert.Equal("Updated Title", retrieved!.Title);
        }

        // ---------------------------------------------------------------------------
        // SongChart CRUD + Song relationship
        // ---------------------------------------------------------------------------

        [Fact]
        public void SongChart_AddWithSong_ShouldCreateRelationship()
        {
            var song = new Song { Title = "Charted", Artist = "Artist" };
            _context.Songs.Add(song);
            _context.SaveChanges();

            var chart = new SongChart
            {
                SongId       = song.Id,
                FilePath     = "/songs/charted/basic.dtx",
                FileFormat   = "DTX",
                Bpm          = 145.0,
                Duration     = 180.0,
                HasDrumChart = true
            };
            _context.SongCharts.Add(chart);
            _context.SaveChanges();

            var ctx2 = NewContext();
            try
            {
                var loaded = ctx2.SongCharts.AsNoTracking()
                    .Include(c => c.Song)
                    .FirstOrDefault(c => c.FilePath == "/songs/charted/basic.dtx");

                Assert.NotNull(loaded);
                Assert.Equal("Charted", loaded!.Song!.Title);
            }
            finally
            {
                ctx2.Dispose();
            }
        }

        [Fact]
        public void SongChart_CascadeDeleteWithSong_ShouldDeleteCharts()
        {
            var song = new Song { Title = "Cascade", Artist = "A" };
            _context.Songs.Add(song);
            _context.SaveChanges();

            var chart = new SongChart { SongId = song.Id, FilePath = "/c/song.dtx" };
            _context.SongCharts.Add(chart);
            _context.SaveChanges();

            _context.Songs.Remove(song);
            _context.SaveChanges();

            var ctx2 = NewContext();
            try
            {
                var chartCount = ctx2.SongCharts.Count(c => c.FilePath == "/c/song.dtx");
                Assert.Equal(0, chartCount);
            }
            finally
            {
                ctx2.Dispose();
            }
        }

        [Fact]
        public void SongChart_AddWithInvalidSongId_ShouldFail()
        {
            // Attempt to insert a chart whose SongId references a non-existent Song.
            var chart = new SongChart { SongId = 99999, FilePath = "/fk/bad_song.dtx" };
            _context.SongCharts.Add(chart);
            Assert.Throws<DbUpdateException>(() => _context.SaveChanges());
        }

        // ---------------------------------------------------------------------------
        // SongScore – enum storage, unique index, and FK constraint
        // ---------------------------------------------------------------------------

        [Fact]
        public void SongScore_AddWithChart_ShouldPersistInstrumentEnum()
        {
            var song = new Song { Title = "Scored", Artist = "A" };
            _context.Songs.Add(song);
            _context.SaveChanges();

            var chart = new SongChart { SongId = song.Id, FilePath = "/s/scored.dtx" };
            _context.SongCharts.Add(chart);
            _context.SaveChanges();

            var score = new SongScore
            {
                ChartId    = chart.Id,
                Instrument = EInstrumentPart.DRUMS,
                BestScore  = 950000
            };
            _context.SongScores.Add(score);
            _context.SaveChanges();

            var ctx2 = NewContext();
            try
            {
                var loaded = ctx2.SongScores.AsNoTracking()
                    .FirstOrDefault(s => s.ChartId == chart.Id);

                Assert.NotNull(loaded);
                Assert.Equal(EInstrumentPart.DRUMS, loaded!.Instrument);
                Assert.Equal(950000, loaded.BestScore);
            }
            finally
            {
                ctx2.Dispose();
            }
        }

        [Fact]
        public void SongScore_UniqueIndex_ShouldPreventDuplicateChartInstrument()
        {
            var song = new Song { Title = "Dupe", Artist = "A" };
            _context.Songs.Add(song);
            _context.SaveChanges();

            var chart = new SongChart { SongId = song.Id, FilePath = "/d/dupe.dtx" };
            _context.SongCharts.Add(chart);
            _context.SaveChanges();

            _context.SongScores.Add(new SongScore { ChartId = chart.Id, Instrument = EInstrumentPart.GUITAR });
            _context.SaveChanges();

            // Second score for the same chart/instrument should violate the unique index.
            _context.SongScores.Add(new SongScore { ChartId = chart.Id, Instrument = EInstrumentPart.GUITAR });
            Assert.Throws<DbUpdateException>(() => _context.SaveChanges());
        }

        [Fact]
        public void SongScore_AddWithInvalidChartId_ShouldFail()
        {
            // ChartId references a non-existent SongChart.
            var score = new SongScore { ChartId = 99999, Instrument = EInstrumentPart.BASS };
            _context.SongScores.Add(score);
            Assert.Throws<DbUpdateException>(() => _context.SaveChanges());
        }

        // ---------------------------------------------------------------------------
        // SongHierarchy – self-referencing relationship
        // ---------------------------------------------------------------------------

        [Fact]
        public void SongHierarchy_ParentChild_ShouldPersistRelationship()
        {
            var parent = new SongHierarchy
            {
                Title        = "Parent Folder",
                NodeType     = ENodeType.Box,
                DisplayOrder = 0
            };
            _context.SongHierarchy.Add(parent);
            _context.SaveChanges();

            var child = new SongHierarchy
            {
                ParentId     = parent.Id,
                Title        = "Child Song",
                NodeType     = ENodeType.Song,
                DisplayOrder = 1
            };
            _context.SongHierarchy.Add(child);
            _context.SaveChanges();

            var ctx2 = NewContext();
            try
            {
                var loaded = ctx2.SongHierarchy.AsNoTracking()
                    .Include(h => h.Children)
                    .FirstOrDefault(h => h.Id == parent.Id);

                Assert.NotNull(loaded);
                Assert.Single(loaded!.Children);
                Assert.Equal("Child Song", loaded.Children.First().Title);
            }
            finally
            {
                ctx2.Dispose();
            }
        }

        // ---------------------------------------------------------------------------
        // PerformanceHistory
        // ---------------------------------------------------------------------------

        [Fact]
        public void PerformanceHistory_AddWithSong_ShouldPersist()
        {
            var song = new Song { Title = "History Song", Artist = "A" };
            _context.Songs.Add(song);
            _context.SaveChanges();

            _context.PerformanceHistory.Add(new PerformanceHistory
            {
                SongId       = song.Id,
                DisplayOrder = 0,
                HistoryLine  = "Test run"
            });
            _context.SaveChanges();

            var ctx2 = NewContext();
            try
            {
                var count = ctx2.PerformanceHistory.Count(p => p.SongId == song.Id);
                Assert.Equal(1, count);
            }
            finally
            {
                ctx2.Dispose();
            }
        }

        [Fact]
        public void PerformanceHistory_CascadeDeleteWithSong_ShouldDeleteHistory()
        {
            var song = new Song { Title = "Cascade History", Artist = "A" };
            _context.Songs.Add(song);
            _context.SaveChanges();

            _context.PerformanceHistory.Add(new PerformanceHistory { SongId = song.Id, DisplayOrder = 0 });
            _context.SaveChanges();

            _context.Songs.Remove(song);
            _context.SaveChanges();

            var ctx2 = NewContext();
            try
            {
                var count = ctx2.PerformanceHistory.Count(p => p.SongId == song.Id);
                Assert.Equal(0, count);
            }
            finally
            {
                ctx2.Dispose();
            }
        }

        [Fact]
        public void PerformanceHistory_AddWithInvalidSongId_ShouldFail()
        {
            // SongId references a non-existent Song.
            var history = new PerformanceHistory { SongId = 99999, DisplayOrder = 0 };
            _context.PerformanceHistory.Add(history);
            Assert.Throws<DbUpdateException>(() => _context.SaveChanges());
        }
    }
}
