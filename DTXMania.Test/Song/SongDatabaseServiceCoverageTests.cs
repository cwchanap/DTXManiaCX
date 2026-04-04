using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.Song;

[Trait("Category", "Unit")]
public class SongDatabaseServiceCoverageTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _databasePath;
    private readonly SongDatabaseService _databaseService;

    public SongDatabaseServiceCoverageTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "DTXManiaCX_Tests",
            nameof(SongDatabaseServiceCoverageTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _databasePath = Path.Combine(_tempRoot, "songs.db");
        _databaseService = new SongDatabaseService(_databasePath);
    }

    [Fact]
    public void CreateContext_BeforeInitialization_ShouldThrowInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => _databaseService.CreateContext());
    }

    [Fact]
    public async Task InitializeDatabaseAsync_WithInvalidDatabaseFile_ShouldRecreateValidDatabase()
    {
        await File.WriteAllTextAsync(_databasePath, "not a sqlite database");

        await _databaseService.InitializeDatabaseAsync();

        Assert.True(await _databaseService.DatabaseExistsAsync());

        using var context = _databaseService.CreateContext();
        var versionTables = await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__DatabaseVersion'")
            .ToListAsync();

        Assert.Equal(1, versionTables.Single());
    }

    [Fact]
    public async Task PurgeDatabaseAsync_ShouldDeleteDatabaseAndRequireReinitialization()
    {
        await _databaseService.InitializeDatabaseAsync();

        Assert.True(File.Exists(_databasePath));

        await _databaseService.PurgeDatabaseAsync();

        Assert.False(File.Exists(_databasePath));
        Assert.Throws<InvalidOperationException>(() => _databaseService.CreateContext());
    }

    [Fact]
    public async Task BackupAndRestoreDatabase_ShouldRoundTripSongData()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Restore Me", "Coverage Bot", "restore-me.dtx");

        var backupPath = Path.Combine(_tempRoot, "songs.backup.db");

        await _databaseService.BackupDatabaseAsync(backupPath);
        await _databaseService.PurgeDatabaseAsync();
        await _databaseService.RestoreDatabaseAsync(backupPath);
        await _databaseService.InitializeDatabaseAsync();

        var songs = await _databaseService.GetSongsAsync();

        var restoredSong = Assert.Single(songs);
        Assert.Equal("Restore Me", restoredSong.Title);
        // RestoreDatabaseAsync reads from the backup but does not delete it
        Assert.True(File.Exists(backupPath));
    }

    [Fact]
    public async Task SearchSongsAsync_ShouldMatchTitleAndArtist()
    {
        await _databaseService.InitializeDatabaseAsync();
        await AddSongAsync("Blue Sky", "Alice", "blue-sky.dtx");
        await AddSongAsync("Red Moon", "Bob", "red-moon.dtx");

        var byTitle = await _databaseService.SearchSongsAsync("Blue");
        var byArtist = await _databaseService.SearchSongsAsync("Bob");

        var titleMatch = Assert.Single(byTitle);
        Assert.Equal("Blue Sky", titleMatch.Title);

        var artistMatch = Assert.Single(byArtist);
        Assert.Equal("Red Moon", artistMatch.Title);
    }

    [Fact]
    public async Task GetSongWithChartsAsync_WhenSongExists_ShouldReturnSongAndCharts()
    {
        await _databaseService.InitializeDatabaseAsync();

        var (songId, firstChart) = await AddSongAsync("Grouped Song", "Coverage Bot", "grouped-basic.dtx", drumLevel: 25);
        var secondChart = CreateChart("grouped-advanced.dtx", drumLevel: 65);

        await _databaseService.AddSongAsync(
            CreateSong("Grouped Song", "Coverage Bot"),
            secondChart);

        var result = await _databaseService.GetSongWithChartsAsync(songId);

        Assert.NotNull(result);
        Assert.Equal("Grouped Song", result!.Value.song.Title);
        Assert.Equal(2, result.Value.charts.Length);
        Assert.Contains(result.Value.charts, chart => chart.FilePath == firstChart.FilePath);
        Assert.Contains(result.Value.charts, chart => chart.FilePath == secondChart.FilePath);
    }

    [Fact]
    public async Task GetSongWithChartsAsync_WhenSongDoesNotExist_ShouldReturnNull()
    {
        await _databaseService.InitializeDatabaseAsync();

        var result = await _databaseService.GetSongWithChartsAsync(9999);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateScoreAsync_WhenNewBestScoreSubmitted_ShouldUpdateBestLastAndCounters()
    {
        await _databaseService.InitializeDatabaseAsync();
        var (_, chart) = await AddSongAsync("High Score", "Coverage Bot", "high-score.dtx", drumLevel: 75);

        await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 900_000, 95.5, fullCombo: true);

        var score = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

        Assert.Equal(900_000, score.BestScore);
        Assert.Equal(900_000, score.LastScore);
        Assert.Equal(95.5, score.BestAchievementRate);
        Assert.True(score.FullCombo);
        Assert.Equal(1, score.PlayCount);
        Assert.Equal(1, score.ClearCount);
        Assert.NotNull(score.LastPlayedAt);
    }

    [Fact]
    public async Task UpdateScoreAsync_WhenScoreIsNotANewBest_ShouldPreserveBestButAdvanceLastAndCounts()
    {
        await _databaseService.InitializeDatabaseAsync();
        var (_, chart) = await AddSongAsync("Score History", "Coverage Bot", "score-history.dtx", drumLevel: 70);

        await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 900_000, 95.5, fullCombo: true);
        await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 500_000, 55.0, fullCombo: false);

        var score = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

        Assert.Equal(900_000, score.BestScore);
        Assert.Equal(95.5, score.BestAchievementRate);
        Assert.True(score.FullCombo);
        Assert.Equal(500_000, score.LastScore);
        Assert.Equal(2, score.PlayCount);
        Assert.Equal(2, score.ClearCount);
    }

    [Fact]
    public async Task GetTopScoresAsync_ShouldReturnScoresSortedByBestScoreAndRespectLimit()
    {
        await _databaseService.InitializeDatabaseAsync();

        var (_, firstChart) = await AddSongAsync("First", "Coverage Bot", "first.dtx", drumLevel: 50);
        var (_, secondChart) = await AddSongAsync("Second", "Coverage Bot", "second.dtx", drumLevel: 50);
        var (_, thirdChart) = await AddSongAsync("Third", "Coverage Bot", "third.dtx", drumLevel: 50);

        await _databaseService.UpdateScoreAsync(firstChart.Id, EInstrumentPart.DRUMS, 600_000, 60.0, fullCombo: false);
        await _databaseService.UpdateScoreAsync(secondChart.Id, EInstrumentPart.DRUMS, 950_000, 98.0, fullCombo: true);
        await _databaseService.UpdateScoreAsync(thirdChart.Id, EInstrumentPart.DRUMS, 800_000, 83.0, fullCombo: false);

        var topScores = await _databaseService.GetTopScoresAsync(EInstrumentPart.DRUMS, limit: 2);

        Assert.Equal(2, topScores.Count);
        Assert.Equal(950_000, topScores[0].BestScore);
        Assert.Equal("Second", topScores[0].Chart.Song.Title);
        Assert.Equal(800_000, topScores[1].BestScore);
        Assert.Equal("Third", topScores[1].Chart.Song.Title);
    }

    [Fact]
    public async Task CleanupStaleChartsAsync_ShouldRemoveMissingChartsAndOrphanedSongs()
    {
        await _databaseService.InitializeDatabaseAsync();

        await AddSongAsync("Keep Me", "Coverage Bot", "keep-me.dtx", drumLevel: 40);

        var missingChartPath = Path.Combine(_tempRoot, "missing-chart.dtx");
        await _databaseService.AddSongAsync(
            CreateSong("Remove Me", "Coverage Bot"),
            new SongChart
            {
                FilePath = missingChartPath,
                Duration = 120,
                Bpm = 180,
                DrumLevel = 50,
                HasDrumChart = true
            });

        await _databaseService.CleanupStaleChartsAsync();

        var songs = await _databaseService.GetSongsAsync();

        var remainingSong = Assert.Single(songs);
        Assert.Equal("Keep Me", remainingSong.Title);
        Assert.Single(remainingSong.Charts);
        Assert.DoesNotContain(songs, song => song.Title == "Remove Me");
    }

    public void Dispose()
    {
        _databaseService.Dispose();

        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp test assets.
        }
    }

    private async Task<(int SongId, SongChart Chart)> AddSongAsync(
        string title,
        string artist,
        string fileName,
        int drumLevel = 40)
    {
        var song = CreateSong(title, artist);
        var chart = CreateChart(fileName, drumLevel);
        var songId = await _databaseService.AddSongAsync(song, chart);
        return (songId, chart);
    }

    private SongEntity CreateSong(string title, string artist)
    {
        return new SongEntity
        {
            Title = title,
            Artist = artist,
            Genre = "Coverage"
        };
    }

    private SongChart CreateChart(string fileName, int drumLevel)
    {
        var filePath = Path.Combine(_tempRoot, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "#TITLE:Coverage");

        return new SongChart
        {
            FilePath = filePath,
            Duration = 123.45,
            Bpm = 180.0,
            DrumLevel = drumLevel,
            HasDrumChart = drumLevel > 0
        };
    }

    private async Task<SongScoreEntity> LoadScoreAsync(int chartId, EInstrumentPart instrument)
    {
        using var context = _databaseService.CreateContext();
        return await context.SongScores.SingleAsync(score => score.ChartId == chartId && score.Instrument == instrument);
    }
}
