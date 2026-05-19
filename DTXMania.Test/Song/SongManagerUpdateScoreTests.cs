using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song;

[Collection("SongManager")]
[Trait("Category", "Unit")]
public class SongManagerUpdateScoreTests : IDisposable
{
    private readonly SongManager _manager;
    private readonly string _testRoot;
    private readonly string _testDbPath;

    public SongManagerUpdateScoreTests()
    {
        SongManager.ResetInstanceForTesting();
        _manager = SongManager.Instance;

        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "DTXManiaCX_Tests",
            nameof(SongManagerUpdateScoreTests),
            Guid.NewGuid().ToString("N"));
        _testDbPath = Path.Combine(_testRoot, "songs.db");

        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        _manager.Clear();
        SongManager.ResetInstanceForTesting();

        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task UpdateScoreAsync_WithValidChartId_ShouldUpdateScore()
    {
        var songsRoot = Path.Combine(_testRoot, "ScoreSongs");
        var songFolder = Path.Combine(songsRoot, "Score Song");
        Directory.CreateDirectory(songFolder);

        await CreateDtxFileAsync(Path.Combine(songFolder, "score.dtx"), "Score Song", "Test Artist", "Rock", 50);
        await InitializeAndEnumerateAsync(songsRoot);

        var db = _manager.DatabaseService!;
        var songs = await db.GetSongsAsync();
        var chart = Assert.Single(songs).Charts.First();

        var result = await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 880_000, 92.5, fullCombo: false);

        Assert.True(result);

        var scores = await _manager.GetTopScoresAsync(EInstrumentPart.DRUMS, limit: 1);
        var score = Assert.Single(scores);
        Assert.Equal(chart.Id, score.ChartId);
        Assert.Equal(880_000, score.BestScore);
    }

    [Fact]
    public async Task UpdateScoreAsync_WithPerformanceSummary_ShouldPersistSkill()
    {
        var songsRoot = Path.Combine(_testRoot, "SkillSongs");
        var songFolder = Path.Combine(songsRoot, "Skill Song");
        Directory.CreateDirectory(songFolder);

        await CreateDtxFileAsync(Path.Combine(songFolder, "skill.dtx"), "Skill Song", "Test Artist", "Pop", 78);
        await InitializeAndEnumerateAsync(songsRoot);

        var db = _manager.DatabaseService!;
        var songs = await db.GetSongsAsync();
        var chart = Assert.Single(songs).Charts.First();

        var summary = new PerformanceSummary
        {
            Score = 800_000,
            MaxCombo = 120,
            ClearFlag = true,
            PerfectCount = 100,
            GreatCount = 20,
            GoodCount = 0,
            PoorCount = 0,
            MissCount = 0,
            TotalNotes = 120,
            PlayingSkill = 100.0,
            GameSkill = 162.6,
            ChartLevel = 78,
            ChartLevelDec = 33
        };

        var result = await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

        Assert.True(result);

        var scores = await _manager.GetTopScoresAsync(EInstrumentPart.DRUMS, limit: 1);
        var score = Assert.Single(scores);
        Assert.Equal(chart.Id, score.ChartId);
        Assert.Equal(800_000, score.BestScore);
        Assert.Equal(162.6, score.HighSkill, 1);
        Assert.Equal(162.6, score.SongSkill, 1);
        Assert.True(score.FullCombo);
    }

    [Fact]
    public async Task UpdateScoreAsync_WithSummaryWithoutDatabaseService_ShouldReturnFalse()
    {
        var result = await _manager.UpdateScoreAsync(1, EInstrumentPart.DRUMS, new PerformanceSummary());

        Assert.False(result);
    }

    [Fact]
    public async Task UpdateScoreAsync_WithInvalidChartId_ShouldStillReturnTrue()
    {
        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var result = await _manager.UpdateScoreAsync(99999, EInstrumentPart.DRUMS, 100_000, 50.0, fullCombo: false);

        Assert.True(result);
    }

    private async Task InitializeAndEnumerateAsync(string songsRoot)
    {
        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        var result = await _manager.EnumerateSongsAsync(new[] { songsRoot });
        Assert.True(result >= 1);
        Assert.NotNull(_manager.DatabaseService);
    }

    private static async Task CreateDtxFileAsync(string path, string title, string artist, string genre, int drumLevel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, $"""
#TITLE: {title}
#ARTIST: {artist}
#GENRE: {genre}
#BPM: 120
#DLEVEL: {drumLevel}
#00002:11111111
#00011:01010101
""");
    }
}
