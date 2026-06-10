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
        public async Task PersistingNxImportedCounts_ShouldPersist()
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
        public void Clone_WhenCalled_ShouldCopyNxImportedCounts()
        {
            var score = new SongScore { NxImportedPlayCount = 5, NxImportedClearCount = 3 };
            var clone = score.Clone();
            Assert.Equal(5, clone.NxImportedPlayCount);
            Assert.Equal(3, clone.NxImportedClearCount);
        }
    }
}
