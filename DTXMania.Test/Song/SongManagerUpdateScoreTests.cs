using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using static DTXMania.Test.TestData.ReflectionHelpers;
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
    public async Task UpdateScoreAsync_WithSummaryWhenDatabaseServiceThrows_ShouldReturnFalse()
    {
        SetPrivateField(_manager, "_databaseService", new SongDatabaseService(_testDbPath));

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

    [Fact]
    public async Task UpdateScoreAsync_WithSummary_ShouldRefreshInMemoryPlayHistoryBadge()
    {
        // Regression: after saving a CX play, the in-memory SongListNode's
        // PlayHistoryLines cache must reflect the just-finished play so the
        // play-history badge is correct when the user returns to SongSelect
        // without a full song-database reload.
        var songsRoot = Path.Combine(_testRoot, "BadgeSongs");
        var songFolder = Path.Combine(songsRoot, "Badge Song");
        Directory.CreateDirectory(songFolder);

        await CreateDtxFileAsync(Path.Combine(songFolder, "badge.dtx"), "Badge Song", "Test Artist", "Rock", 50);
        await InitializeAndEnumerateAsync(songsRoot);

        var db = _manager.DatabaseService!;
        var songs = await db.GetSongsAsync();
        var chart = Assert.Single(songs).Charts.First();

        // Find the in-memory score node and capture a reference to its score object.
        var (scoreNode, drumScore) = FindScoreByChartId(_manager.RootSongs, chart.Id);
        Assert.NotNull(scoreNode);
        Assert.NotNull(drumScore);
        Assert.Empty(drumScore!.PlayHistoryLines);
        Assert.Equal(0, drumScore.PlayCount);

        var summary = new PerformanceSummary
        {
            Score = 950_000,
            MaxCombo = 100,
            ClearFlag = true,
            PerfectCount = 95,
            GreatCount = 5,
            GoodCount = 0,
            PoorCount = 0,
            MissCount = 0,
            TotalNotes = 100,
            PlayingSkill = 95.0,
            GameSkill = 150.0,
            ChartLevel = 50,
            ChartLevelDec = 0
        };

        var result = await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);
        Assert.True(result);

        // The same in-memory score object (by reference) should now reflect the play.
        Assert.Equal(1, drumScore.PlayCount);
        Assert.NotEmpty(drumScore.PlayHistoryLines);
        Assert.Contains(drumScore.PlayHistoryLines, line => line.Contains("Cleared"));
        Assert.Equal(950_000, drumScore.BestScore);
    }

    [Fact]
    public async Task UpdateScoreAsync_WithSummary_ShouldRefreshInMemoryPlayHistoryOnSecondPlay()
    {
        // Two consecutive plays: the badge should show both, newest-first.
        var songsRoot = Path.Combine(_testRoot, "BadgeSongs2");
        var songFolder = Path.Combine(songsRoot, "Badge Song 2");
        Directory.CreateDirectory(songFolder);

        await CreateDtxFileAsync(Path.Combine(songFolder, "badge2.dtx"), "Badge Song 2", "Test Artist", "Rock", 50);
        await InitializeAndEnumerateAsync(songsRoot);

        var db = _manager.DatabaseService!;
        var songs = await db.GetSongsAsync();
        var chart = Assert.Single(songs).Charts.First();

        var (_, drumScore) = FindScoreByChartId(_manager.RootSongs, chart.Id);
        Assert.NotNull(drumScore);

        var summary1 = new PerformanceSummary
        {
            Score = 800_000, MaxCombo = 80, ClearFlag = true,
            PerfectCount = 80, GreatCount = 20, GoodCount = 0, PoorCount = 0, MissCount = 0,
            TotalNotes = 100, PlayingSkill = 80.0, GameSkill = 120.0,
            ChartLevel = 50, ChartLevelDec = 0
        };
        await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary1);

        Assert.Single(drumScore!.PlayHistoryLines);
        Assert.Equal(1, drumScore.PlayCount);

        var summary2 = new PerformanceSummary
        {
            Score = 900_000, MaxCombo = 90, ClearFlag = false,
            PerfectCount = 90, GreatCount = 10, GoodCount = 0, PoorCount = 0, MissCount = 0,
            TotalNotes = 100, PlayingSkill = 90.0, GameSkill = 135.0,
            ChartLevel = 50, ChartLevelDec = 0
        };
        await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary2);

        Assert.Equal(2, drumScore.PlayCount);
        Assert.Equal(2, drumScore.PlayHistoryLines.Count);
        // The most-recent play (DisplayOrder ascending after merger's newest-first
        // re-order) should mention "Failed" since summary2 had ClearFlag=false.
        Assert.Contains(drumScore.PlayHistoryLines, line => line.Contains("Failed"));
    }

    /// <summary>
    /// Walks the song-list tree to find the NodeType.Score node whose Scores array
    /// contains a drum entry matching <paramref name="chartId"/>. Returns the node
    /// and the score object (by reference) so the caller can assert on in-place
    /// mutations.
    /// </summary>
    private static (SongListNode? Node, SongScore? Score) FindScoreByChartId(
        System.Collections.Generic.IReadOnlyList<SongListNode> roots, int chartId)
    {
        foreach (var root in roots)
        {
            var found = FindScoreByChartIdRecursive(root, chartId);
            if (found.Node != null)
                return found;
        }
        return (null, null);
    }

    private static (SongListNode? Node, SongScore? Score) FindScoreByChartIdRecursive(
        SongListNode node, int chartId)
    {
        if (node.Type == NodeType.Score)
        {
            foreach (var score in node.Scores)
            {
                if (score != null && score.ChartId == chartId)
                    return (node, score);
            }
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var found = FindScoreByChartIdRecursive(child, chartId);
                if (found.Node != null)
                    return found;
            }
        }

        return (null, null);
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
