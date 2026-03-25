using System;
using System.Linq;
using System.Threading.Tasks;
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
    /// </summary>
    public class SongDbContextTests : IAsyncLifetime
    {
        // Shared connection so every context for the same test sees the same
        // in-memory SQLite database (SQLite :memory: databases are per-connection).
        private Microsoft.Data.Sqlite.SqliteConnection _connection = null!;
        private DbContextOptions<SongDbContext> _options = null!;
        private SongDbContext _context = null!;

        // ---------------------------------------------------------------------------
        // IAsyncLifetime
        // ---------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            // Foreign Keys=True enables FK constraint enforcement on the shared connection.
            _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await _connection.OpenAsync();

            _options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new SongDbContext(_options);
            await _context.Database.EnsureCreatedAsync();
        }

        public async Task DisposeAsync()
        {
            await _context.DisposeAsync();
            await _connection.DisposeAsync();
        }

        /// <summary>
        /// Creates a fresh SongDbContext that shares the same SQLite connection
        /// (and therefore the same in-memory database) but has an empty change tracker.
        /// Use this to verify that values read back are truly loaded from SQLite, not
        /// from EF Core's first-level cache.
        /// </summary>
        private SongDbContext NewContext() => new SongDbContext(_options);

        // ---------------------------------------------------------------------------
        // Default OnConfiguring path (no provider → OnConfiguring adds ":memory:" SQLite)
        // ---------------------------------------------------------------------------

        [Fact]
        public async Task OnConfiguring_WithUnconfiguredOptions_ShouldFallBackToInMemorySQLite()
        {
            // Pass empty options so OnConfiguring takes the "!IsConfigured" branch.
            var emptyOptions = new DbContextOptions<SongDbContext>();
            using var ctx = new SongDbContext(emptyOptions);
            await ctx.Database.OpenConnectionAsync();
            // If OnConfiguring successfully adds a provider this will not throw.
            await ctx.Database.EnsureCreatedAsync();
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
        public async Task Song_AddAndRetrieve_ShouldRoundTrip()
        {
            var song = new Song { Title = "Test Song", Artist = "Test Artist", Genre = "Rock" };
            _context.Songs.Add(song);
            await _context.SaveChangesAsync();

            // Fresh context → values come from SQLite, not the change tracker.
            await using var ctx2 = NewContext();
            var retrieved = await ctx2.Songs.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Title == "Test Song");

            Assert.NotNull(retrieved);
            Assert.Equal("Test Artist", retrieved!.Artist);
            Assert.Equal("Rock", retrieved.Genre);
        }

        [Fact]
        public async Task Song_Delete_ShouldRemoveFromDatabase()
        {
            var song = new Song { Title = "To Delete", Artist = "Artist" };
            _context.Songs.Add(song);
            await _context.SaveChangesAsync();

            _context.Songs.Remove(song);
            await _context.SaveChangesAsync();

            await using var ctx2 = NewContext();
            var count = await ctx2.Songs.CountAsync(s => s.Title == "To Delete");
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task Song_Update_ShouldPersistChanges()
        {
            var song = new Song { Title = "Original Title", Artist = "Artist" };
            _context.Songs.Add(song);
            await _context.SaveChangesAsync();

            song.Title = "Updated Title";
            await _context.SaveChangesAsync();

            // Detach so FindAsync cannot return the cached entity.
            _context.Entry(song).State = EntityState.Detached;
            var retrieved = await _context.Songs.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == song.Id);
            Assert.Equal("Updated Title", retrieved!.Title);
        }

        // ---------------------------------------------------------------------------
        // SongChart CRUD + Song relationship
        // ---------------------------------------------------------------------------

        [Fact]
        public async Task SongChart_AddWithSong_ShouldCreateRelationship()
        {
            var song = new Song { Title = "Charted", Artist = "Artist" };
            _context.Songs.Add(song);
            await _context.SaveChangesAsync();

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
            await _context.SaveChangesAsync();

            await using var ctx2 = NewContext();
            var loaded = await ctx2.SongCharts.AsNoTracking()
                .Include(c => c.Song)
                .FirstOrDefaultAsync(c => c.FilePath == "/songs/charted/basic.dtx");

            Assert.NotNull(loaded);
            Assert.Equal("Charted", loaded!.Song!.Title);
        }

        [Fact]
        public async Task SongChart_CascadeDeleteWithSong_ShouldDeleteCharts()
        {
            var song = new Song { Title = "Cascade", Artist = "A" };
            _context.Songs.Add(song);
            await _context.SaveChangesAsync();

            var chart = new SongChart { SongId = song.Id, FilePath = "/c/song.dtx" };
            _context.SongCharts.Add(chart);
            await _context.SaveChangesAsync();

            _context.Songs.Remove(song);
            await _context.SaveChangesAsync();

            await using var ctx2 = NewContext();
            var chartCount = await ctx2.SongCharts.CountAsync(c => c.FilePath == "/c/song.dtx");
            Assert.Equal(0, chartCount);
        }

        [Fact]
        public async Task SongChart_AddWithInvalidSongId_ShouldFail()
        {
            // Attempt to insert a chart whose SongId references a non-existent Song.
            var chart = new SongChart { SongId = 99999, FilePath = "/fk/bad_song.dtx" };
            _context.SongCharts.Add(chart);
            await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        }

        // ---------------------------------------------------------------------------
        // SongScore – enum storage, unique index, and FK constraint
        // ---------------------------------------------------------------------------

        [Fact]
        public async Task SongScore_AddWithChart_ShouldPersistInstrumentEnum()
        {
            var song = new Song { Title = "Scored", Artist = "A" };
            _context.Songs.Add(song);
            await _context.SaveChangesAsync();

            var chart = new SongChart { SongId = song.Id, FilePath = "/s/scored.dtx" };
            _context.SongCharts.Add(chart);
            await _context.SaveChangesAsync();

            var score = new SongScore
            {
                ChartId    = chart.Id,
                Instrument = EInstrumentPart.DRUMS,
                BestScore  = 950000
            };
            _context.SongScores.Add(score);
            await _context.SaveChangesAsync();

            await using var ctx2 = NewContext();
            var loaded = await ctx2.SongScores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ChartId == chart.Id);

            Assert.NotNull(loaded);
            Assert.Equal(EInstrumentPart.DRUMS, loaded!.Instrument);
            Assert.Equal(950000, loaded.BestScore);
        }

        [Fact]
        public async Task SongScore_UniqueIndex_ShouldPreventDuplicateChartInstrument()
        {
            var song = new Song { Title = "Dupe", Artist = "A" };
            _context.Songs.Add(song);
            await _context.SaveChangesAsync();

            var chart = new SongChart { SongId = song.Id, FilePath = "/d/dupe.dtx" };
            _context.SongCharts.Add(chart);
            await _context.SaveChangesAsync();

            _context.SongScores.Add(new SongScore { ChartId = chart.Id, Instrument = EInstrumentPart.GUITAR });
            await _context.SaveChangesAsync();

            // Second score for the same chart/instrument should violate the unique index.
            _context.SongScores.Add(new SongScore { ChartId = chart.Id, Instrument = EInstrumentPart.GUITAR });
            await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        }

        [Fact]
        public async Task SongScore_AddWithInvalidChartId_ShouldFail()
        {
            // ChartId references a non-existent SongChart.
            var score = new SongScore { ChartId = 99999, Instrument = EInstrumentPart.BASS };
            _context.SongScores.Add(score);
            await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        }

        // ---------------------------------------------------------------------------
        // SongHierarchy – self-referencing relationship
        // ---------------------------------------------------------------------------

        [Fact]
        public async Task SongHierarchy_ParentChild_ShouldPersistRelationship()
        {
            var parent = new SongHierarchy
            {
                Title        = "Parent Folder",
                NodeType     = ENodeType.Box,
                DisplayOrder = 0
            };
            _context.SongHierarchy.Add(parent);
            await _context.SaveChangesAsync();

            var child = new SongHierarchy
            {
                ParentId     = parent.Id,
                Title        = "Child Song",
                NodeType     = ENodeType.Song,
                DisplayOrder = 1
            };
            _context.SongHierarchy.Add(child);
            await _context.SaveChangesAsync();

            await using var ctx2 = NewContext();
            var loaded = await ctx2.SongHierarchy.AsNoTracking()
                .Include(h => h.Children)
                .FirstOrDefaultAsync(h => h.Id == parent.Id);

            Assert.NotNull(loaded);
            Assert.Single(loaded!.Children);
            Assert.Equal("Child Song", loaded.Children.First().Title);
        }

        // ---------------------------------------------------------------------------
        // PerformanceHistory
        // ---------------------------------------------------------------------------

        [Fact]
        public async Task PerformanceHistory_AddWithSong_ShouldPersist()
        {
            var song = new Song { Title = "History Song", Artist = "A" };
            _context.Songs.Add(song);
            await _context.SaveChangesAsync();

            _context.PerformanceHistory.Add(new PerformanceHistory
            {
                SongId       = song.Id,
                DisplayOrder = 0,
                HistoryLine  = "Test run"
            });
            await _context.SaveChangesAsync();

            await using var ctx2 = NewContext();
            var count = await ctx2.PerformanceHistory.CountAsync(p => p.SongId == song.Id);
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task PerformanceHistory_CascadeDeleteWithSong_ShouldDeleteHistory()
        {
            var song = new Song { Title = "Cascade History", Artist = "A" };
            _context.Songs.Add(song);
            await _context.SaveChangesAsync();

            _context.PerformanceHistory.Add(new PerformanceHistory { SongId = song.Id, DisplayOrder = 0 });
            await _context.SaveChangesAsync();

            _context.Songs.Remove(song);
            await _context.SaveChangesAsync();

            await using var ctx2 = NewContext();
            var count = await ctx2.PerformanceHistory.CountAsync(p => p.SongId == song.Id);
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task PerformanceHistory_AddWithInvalidSongId_ShouldFail()
        {
            // SongId references a non-existent Song.
            var history = new PerformanceHistory { SongId = 99999, DisplayOrder = 0 };
            _context.PerformanceHistory.Add(history);
            await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        }
    }
}
