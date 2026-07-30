using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

        Assert.True(result.IsSuccess);

        var scores = await _manager.GetTopScoresAsync(EInstrumentPart.DRUMS, limit: 1);
        var score = Assert.Single(scores);
        Assert.Equal(chart.Id, score.ChartId);
        Assert.Equal(800_000, score.BestScore);
        Assert.Equal(162.6, score.HighSkill, 1);
        Assert.Equal(162.6, score.SongSkill, 1);
        Assert.True(score.FullCombo);
    }

    [Fact]
    public async Task UpdateScoreAsync_WithSummaryWithoutDatabaseService_ShouldReturnFailed()
    {
        var result = await _manager.UpdateScoreAsync(1, EInstrumentPart.DRUMS, new PerformanceSummary());

        Assert.Equal(ScoreSaveStatus.Failed, result.Status);
    }

    [Fact]
    public async Task UpdateScoreAsync_WithSummaryWhenDatabaseServiceThrows_ShouldPropagate()
    {
        SetPrivateField(_manager, "_databaseService", new SongDatabaseService(_testDbPath));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.UpdateScoreAsync(
                1,
                EInstrumentPart.DRUMS,
                new PerformanceSummary()));
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
        var difficultyIndex = Array.IndexOf(scoreNode!.Scores, drumScore);
        Assert.True(difficultyIndex >= 0);
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
        Assert.True(result.IsSuccess);

        var refreshed = scoreNode.GetScore(difficultyIndex, 100);
        Assert.NotNull(refreshed);
        Assert.NotSame(drumScore, refreshed);
        Assert.Equal(0, drumScore.PlayCount);
        Assert.Equal(1, refreshed!.PlayCount);
        Assert.NotEmpty(refreshed.PlayHistoryLines);
        Assert.Contains(refreshed.PlayHistoryLines, line => line.Contains("Cleared"));
        Assert.Equal(950_000, refreshed.BestScore);
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

        var (scoreNode, drumScore) = FindScoreByChartId(_manager.RootSongs, chart.Id);
        Assert.NotNull(scoreNode);
        Assert.NotNull(drumScore);
        var difficultyIndex = Array.IndexOf(scoreNode!.Scores, drumScore);
        Assert.True(difficultyIndex >= 0);

        var summary1 = new PerformanceSummary
        {
            Score = 800_000, MaxCombo = 80, ClearFlag = true,
            PerfectCount = 80, GreatCount = 20, GoodCount = 0, PoorCount = 0, MissCount = 0,
            TotalNotes = 100, PlayingSkill = 80.0, GameSkill = 120.0,
            ChartLevel = 50, ChartLevelDec = 0
        };
        await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary1);

        var firstRefresh = scoreNode.GetScore(difficultyIndex, 100);
        Assert.NotNull(firstRefresh);
        Assert.Single(firstRefresh!.PlayHistoryLines);
        Assert.Equal(1, firstRefresh.PlayCount);

        var summary2 = new PerformanceSummary
        {
            Score = 900_000, MaxCombo = 90, ClearFlag = false,
            PerfectCount = 90, GreatCount = 10, GoodCount = 0, PoorCount = 0, MissCount = 0,
            TotalNotes = 100, PlayingSkill = 90.0, GameSkill = 135.0,
            ChartLevel = 50, ChartLevelDec = 0
        };
        await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary2);

        var secondRefresh = scoreNode.GetScore(difficultyIndex, 100);
        Assert.NotNull(secondRefresh);
        Assert.NotSame(firstRefresh, secondRefresh);
        Assert.Equal(1, firstRefresh.PlayCount);
        Assert.Equal(2, secondRefresh!.PlayCount);
        Assert.Equal(2, secondRefresh.PlayHistoryLines.Count);
        // The most-recent play (DisplayOrder ascending after merger's newest-first
        // re-order) should mention "Failed" since summary2 had ClearFlag=false.
        Assert.Contains(secondRefresh.PlayHistoryLines, line => line.Contains("Failed"));
    }

    [Fact]
    public async Task UpdateScoreAsync_WithSummary_OnSetDefNode_ShouldNotUpdateDifferentSongWithSameDifficulty()
    {
        // Regression: two set.def songs share the same drum difficulty level.
        // Durable chart IDs must keep the in-memory refresh scoped to the played chart.
        var songsRoot = Path.Combine(_testRoot, "SetDefSongs");

        // Two set.def folders, each with a single chart at DLEVEL 50 (collision).
        await CreateSetDefSongAsync(songsRoot, "First SetDef Song", "first.dtx");
        await CreateSetDefSongAsync(songsRoot, "Second SetDef Song", "second.dtx");
        await InitializeAndEnumerateAsync(songsRoot);

        // Both in-memory nodes should carry their committed chart identities.
        var firstNode = FindScoreNodeByTitle(_manager.RootSongs, "First SetDef Song");
        var secondNode = FindScoreNodeByTitle(_manager.RootSongs, "Second SetDef Song");
        Assert.NotNull(firstNode);
        Assert.NotNull(secondNode);
        var firstScore = Assert.Single(firstNode!.Scores.Where(s => s != null && s.Instrument == EInstrumentPart.DRUMS))!;
        var secondScore = Assert.Single(secondNode!.Scores.Where(s => s != null && s.Instrument == EInstrumentPart.DRUMS))!;
        var firstDifficultyIndex = Array.IndexOf(firstNode.Scores, firstScore);
        var secondDifficultyIndex = Array.IndexOf(secondNode.Scores, secondScore);

        var db = _manager.DatabaseService!;
        var persistedSongs = await db.GetSongsAsync();
        var firstSong = persistedSongs.Single(
            song => song.Title == "First SetDef Song");
        var secondSong = persistedSongs.Single(
            song => song.Title == "Second SetDef Song");
        var firstChart = firstSong.Charts.First();
        var secondChart = secondSong.Charts.First();
        Assert.Equal(firstChart.Id, firstScore.ChartId);
        Assert.Equal(secondChart.Id, secondScore.ChartId);
        Assert.NotEqual(firstScore.ChartId, secondScore.ChartId);

        var summary = new PerformanceSummary
        {
            Score = 910_000, MaxCombo = 90, ClearFlag = true,
            PerfectCount = 90, GreatCount = 10, GoodCount = 0, PoorCount = 0, MissCount = 0,
            TotalNotes = 100, PlayingSkill = 90.0, GameSkill = 140.0,
            ChartLevel = 50, ChartLevelDec = 0
        };
        var result = await _manager.UpdateScoreAsync(secondChart.Id, EInstrumentPart.DRUMS, summary);
        Assert.True(result.IsSuccess);

        // The played song's cached score must reflect the play ...
        var refreshedSecond = secondNode.GetScore(secondDifficultyIndex, 100);
        Assert.NotNull(refreshedSecond);
        Assert.Equal(1, refreshedSecond!.PlayCount);
        Assert.Equal(910_000, refreshedSecond.BestScore);
        Assert.Equal(secondChart.Id, refreshedSecond.ChartId);

        // ... and the OTHER song's cache must remain untouched (the bug would have
        // overwritten firstScore and stamped it with secondChart.Id).
        var refreshedFirst = firstNode.GetScore(firstDifficultyIndex, 100);
        Assert.NotNull(refreshedFirst);
        Assert.Equal(0, refreshedFirst!.PlayCount);
        Assert.Equal(0, refreshedFirst.BestScore);
        Assert.Equal(firstChart.Id, refreshedFirst.ChartId);
    }

    [Fact]
    public async Task UpdateScoreAsync_NonDefaultSpeed_RefreshesAllMatchingNodesOnly()
    {
        var songsRoot = Path.Combine(_testRoot, "VariantSongs");
        var songFolder = Path.Combine(songsRoot, "Variant Song");
        Directory.CreateDirectory(songFolder);

        await CreateDtxFileAsync(
            Path.Combine(songFolder, "variant.dtx"),
            "Variant Song",
            "Test Artist",
            "Rock",
            50);
        await InitializeAndEnumerateAsync(songsRoot);

        var db = _manager.DatabaseService!;
        var chart = Assert.Single(await db.GetSongsAsync()).Charts.First();
        var (canonicalNode, metadata) = FindScoreByChartId(_manager.RootSongs, chart.Id);
        Assert.NotNull(canonicalNode);
        Assert.NotNull(metadata);
        var difficultyIndex = Array.IndexOf(canonicalNode!.Scores, metadata);
        var defaultBefore = canonicalNode.GetScore(difficultyIndex, 100);
        Assert.NotNull(defaultBefore);

        var duplicateNode = new SongListNode
        {
            Type = NodeType.Score,
            DatabaseSongId = canonicalNode.DatabaseSongId,
        };
        duplicateNode.SetScore(difficultyIndex, metadata!.Clone());
        var roots = GetPrivateField<List<SongListNode>>(_manager, "_rootSongs");
        Assert.NotNull(roots);
        roots!.Add(duplicateNode);

        var summary = new PerformanceSummary
        {
            RunId = Guid.NewGuid(),
            PlaySpeedPercent = 75,
            PitchSemitones = -3,
            Score = 875_000,
            MaxCombo = 90,
            ClearFlag = true,
            PerfectCount = 90,
            GreatCount = 10,
            TotalNotes = 100,
            PlayingSkill = 90.0,
            GameSkill = 140.0,
            ChartLevel = 50,
            CompletionReason = CompletionReason.SongComplete,
        };

        var result = await _manager.UpdateScoreAsync(
            chart.Id,
            EInstrumentPart.DRUMS,
            summary);

        Assert.True(result.IsSuccess);
        var canonicalSlow = canonicalNode.GetScore(difficultyIndex, 75);
        var duplicateSlow = duplicateNode.GetScore(difficultyIndex, 75);
        Assert.NotNull(canonicalSlow);
        Assert.NotNull(duplicateSlow);
        Assert.Equal(875_000, canonicalSlow!.BestScore);
        Assert.Equal(875_000, duplicateSlow!.BestScore);
        Assert.Equal(-3, Assert.Single(canonicalSlow.PerformanceHistory).PitchSemitones);
        var defaultAfter = canonicalNode.GetScore(difficultyIndex, 100);
        Assert.NotNull(defaultAfter);
        Assert.Equal(defaultBefore!.BestScore, defaultAfter!.BestScore);
        Assert.Equal(0, defaultBefore.PlayCount);
        Assert.Equal(0, defaultAfter.PlayCount);
        Assert.Equal(100, canonicalNode.Scores[difficultyIndex].PlaySpeedPercent);
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

    /// <summary>
    /// Walks the song-list tree to find the first NodeType.Score node whose Title
    /// matches. Used to locate set.def nodes by title since set.def groups are
    /// keyed by set-definition path rather than individual chart ids.
    /// </summary>
    private static SongListNode? FindScoreNodeByTitle(
        System.Collections.Generic.IReadOnlyList<SongListNode> roots, string title)
    {
        foreach (var root in roots)
        {
            var found = FindScoreNodeByTitleRecursive(root, title);
            if (found != null)
                return found;
        }
        return null;
    }

    private static SongListNode? FindScoreNodeByTitleRecursive(SongListNode node, string title)
    {
        if (node.Type == NodeType.Score && string.Equals(node.Title, title, StringComparison.Ordinal))
            return node;

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var found = FindScoreNodeByTitleRecursive(child, title);
                if (found != null)
                    return found;
            }
        }
        return null;
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

    /// <summary>
    /// Creates a set.def-backed song folder with a single difficulty chart. The
    /// resulting in-memory node carries persisted non-zero ChartId values
    /// assigned by the database during enumeration. All such songs use the same
    /// drum level (50) so a difficulty-only fallback would collide across songs.
    /// </summary>
    private static async Task CreateSetDefSongAsync(string root, string title, string dtxFileName)
    {
        var folder = Path.Combine(root, title);
        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(Path.Combine(folder, "set.def"), $"""
#TITLE {title}
#L1FILE {dtxFileName}
""");

        await File.WriteAllTextAsync(Path.Combine(folder, dtxFileName), $"""
#TITLE: {title}
#ARTIST: SetDef Artist
#BPM: 120
#DLEVEL: 50
#00002:11111111
#00011:01010101
""");
    }

    [Fact]
    public async Task UpdateScoreAsync_WithSummary_WhenInMemoryRefreshPublishesNoNode_ShouldReturnFailed()
    {
        // Contract (plan invariant 8): "The Result stage cannot report a save as
        // successful until the database write and matching in-memory refresh have
        // completed." When the in-memory song list has no matching node for the
        // just-saved chart (e.g. it was cleared/rebuilt between the save and the
        // refresh), the refresh publishes nothing and UpdateScoreAsync must report
        // Failed even though the database write committed. The player can retry;
        // the retry is idempotent (RunId receipt) and will surface AlreadySaved
        // once the refresh finds a matching node.
        var songsRoot = Path.Combine(_testRoot, "NoNodeSongs");
        var songFolder = Path.Combine(songsRoot, "NoNode Song");
        Directory.CreateDirectory(songFolder);

        await CreateDtxFileAsync(Path.Combine(songFolder, "none.dtx"), "NoNode Song", "Test Artist", "Rock", 50);
        await InitializeAndEnumerateAsync(songsRoot);

        var db = _manager.DatabaseService!;
        var chart = Assert.Single(await db.GetSongsAsync()).Charts.First();

        // Simulate the in-memory list being empty (cleared/rebuilt between save
        // and refresh) so PublishNodeScoreVariant finds no matching node.
        var roots = GetPrivateField<List<SongListNode>>(_manager, "_rootSongs");
        Assert.NotNull(roots);
        var originalRoots = roots!.ToList();
        roots.Clear();

        try
        {
            var summary = new PerformanceSummary
            {
                RunId = Guid.NewGuid(),
                PlaySpeedPercent = 100,
                Score = 900_000,
                MaxCombo = 100,
                ClearFlag = true,
                PerfectCount = 100,
                TotalNotes = 100,
                PlayingSkill = 90.0,
                GameSkill = 140.0,
                ChartLevel = 50,
                CompletionReason = CompletionReason.SongComplete,
            };

            var result = await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            // The DB write committed, but no in-memory node was refreshed.
            Assert.Equal(ScoreSaveStatus.Failed, result.Status);
            Assert.Contains("could not be refreshed", result.ErrorMessage ?? string.Empty);
        }
        finally
        {
            // Restore the roots so Dispose can clean up cleanly.
            roots.AddRange(originalRoots);
        }
    }

    [Fact]
    public async Task UpdateScoreAsync_WithSummary_WhenInMemoryRefreshSucceeds_ShouldReturnSaved()
    {
        // Positive control for the refresh contract: when the in-memory list
        // contains the matching node, the save returns Saved (not Failed).
        var songsRoot = Path.Combine(_testRoot, "RefreshOkSongs");
        var songFolder = Path.Combine(songsRoot, "RefreshOk Song");
        Directory.CreateDirectory(songFolder);

        await CreateDtxFileAsync(Path.Combine(songFolder, "ok.dtx"), "RefreshOk Song", "Test Artist", "Rock", 50);
        await InitializeAndEnumerateAsync(songsRoot);

        var db = _manager.DatabaseService!;
        var chart = Assert.Single(await db.GetSongsAsync()).Charts.First();

        var summary = new PerformanceSummary
        {
            RunId = Guid.NewGuid(),
            PlaySpeedPercent = 100,
            Score = 900_000,
            MaxCombo = 100,
            ClearFlag = true,
            PerfectCount = 100,
            TotalNotes = 100,
            PlayingSkill = 90.0,
            GameSkill = 140.0,
            ChartLevel = 50,
            CompletionReason = CompletionReason.SongComplete,
        };

        var result = await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

        Assert.Equal(ScoreSaveStatus.Saved, result.Status);
    }

    /// <summary>
    /// Regression: a stale score snapshot that lost the publication race must
    /// not overwrite a newer snapshot already published by a concurrent refresh.
    /// The detached DB read in RefreshInMemoryScoreForChartAsync runs outside
    /// _lockObject, so a caller that read an older snapshot (e.g. PlayCount=1)
    /// can arrive at the publication lock after a concurrent caller has already
    /// published a newer snapshot (PlayCount=2). This test simulates that exact
    /// post-race state — a node already holding PlayCount=2, and a stale
    /// PlayCount=1 snapshot presented for publication — and asserts the guard
    /// skips the stale publication (returns AlreadyCurrent) instead of
    /// regressing the live variant.
    /// </summary>
    [Fact]
    public async Task PublishNodeScoreVariant_WhenStaleSnapshotArrivesAfterNewerPublication_ShouldSkipAndReportAlreadyCurrent()
    {
        var songsRoot = Path.Combine(_testRoot, "RaceGuardSongs");
        var songFolder = Path.Combine(songsRoot, "Race Song");
        Directory.CreateDirectory(songFolder);

        await CreateDtxFileAsync(Path.Combine(songFolder, "race.dtx"), "Race Song", "Test Artist", "Rock", 50);
        await InitializeAndEnumerateAsync(songsRoot);

        var db = _manager.DatabaseService!;
        var chart = Assert.Single(await db.GetSongsAsync()).Charts.First();

        // Two real saves so the DB and the in-memory node both reach PlayCount=2.
        // This is the state the winning concurrent caller would have published.
        var summary1 = new PerformanceSummary
        {
            RunId = Guid.NewGuid(), PlaySpeedPercent = 100,
            Score = 800_000, MaxCombo = 80, ClearFlag = true,
            PerfectCount = 80, GreatCount = 20, GoodCount = 0, PoorCount = 0, MissCount = 0,
            TotalNotes = 100, PlayingSkill = 80.0, GameSkill = 120.0,
            ChartLevel = 50, ChartLevelDec = 0, CompletionReason = CompletionReason.SongComplete,
        };
        var summary2 = new PerformanceSummary
        {
            RunId = Guid.NewGuid(), PlaySpeedPercent = 100,
            Score = 900_000, MaxCombo = 90, ClearFlag = true,
            PerfectCount = 90, GreatCount = 10, GoodCount = 0, PoorCount = 0, MissCount = 0,
            TotalNotes = 100, PlayingSkill = 90.0, GameSkill = 135.0,
            ChartLevel = 50, ChartLevelDec = 0, CompletionReason = CompletionReason.SongComplete,
        };
        await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary1);
        await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary2);

        var (scoreNode, drumScore) = FindScoreByChartId(_manager.RootSongs, chart.Id);
        Assert.NotNull(scoreNode);
        Assert.NotNull(drumScore);
        var difficultyIndex = Array.IndexOf(scoreNode!.Scores, drumScore);
        var liveVariant = scoreNode.GetScore(difficultyIndex, 100);
        Assert.NotNull(liveVariant);
        Assert.Equal(2, liveVariant!.PlayCount);
        Assert.Equal(900_000, liveVariant.BestScore);

        // Build the stale snapshot the losing caller would have read: PlayCount=1,
        // BestScore=800_000, older LastPlayedAt. This is exactly what
        // GetScoreWithHistoryAsync would have returned if the read happened before
        // the second save committed.
        var staleSnapshot = new SongScore
        {
            ChartId = chart.Id,
            Instrument = EInstrumentPart.DRUMS,
            PlaySpeedPercent = 100,
            PlayCount = 1,
            BestScore = 800_000,
            LastPlayedAt = liveVariant.LastPlayedAt!.Value.AddSeconds(-30),
            Chart = liveVariant.Chart,
            DifficultyLevel = liveVariant.DifficultyLevel,
        };

        // Invoke the private static guard directly with the stale snapshot. This
        // exercises the exact code path the losing caller would hit when it
        // acquires the lock after the winner has already published.
        var outcome = SongManager.PublishNodeScoreVariant(
            scoreNode,
            chart.Id,
            EInstrumentPart.DRUMS,
            100,
            staleSnapshot);

        Assert.Equal(ScoreRefreshOutcome.AlreadyCurrent, outcome);

        // The live variant must be unchanged — the stale snapshot did not overwrite it.
        var afterGuard = scoreNode.GetScore(difficultyIndex, 100);
        Assert.NotNull(afterGuard);
        Assert.Same(liveVariant, afterGuard);
        Assert.Equal(2, afterGuard!.PlayCount);
        Assert.Equal(900_000, afterGuard.BestScore);
    }

    /// <summary>
    /// Barrier-controlled concurrent regression: two UpdateScoreAsync calls for the
    /// same chart/instrument/speed released simultaneously must leave the in-memory
    /// PlayCount equal to the database PlayCount. Without the stale-snapshot guard,
    /// the caller whose detached DB read resolved first (older PlayCount) but whose
    /// lock acquisition landed last would overwrite the newer publication, leaving
    /// the in-memory PlayCount behind the database. With the guard, the older
    /// snapshot is skipped and the in-memory state stays consistent.
    /// </summary>
    [Fact]
    public async Task UpdateScoreAsync_ConcurrentSavesForSameScoreKey_ShouldNotRegressInMemoryPlayCount()
    {
        var songsRoot = Path.Combine(_testRoot, "ConcurrentRaceSongs");
        var songFolder = Path.Combine(songsRoot, "Concurrent Song");
        Directory.CreateDirectory(songFolder);

        await CreateDtxFileAsync(Path.Combine(songFolder, "concurrent.dtx"), "Concurrent Song", "Test Artist", "Rock", 50);
        await InitializeAndEnumerateAsync(songsRoot);

        var db = _manager.DatabaseService!;
        var chart = Assert.Single(await db.GetSongsAsync()).Charts.First();

        var barrier = new Barrier(participantCount: 2);

        PerformanceSummary MakeSummary(int score, int skill) => new()
        {
            RunId = Guid.NewGuid(), PlaySpeedPercent = 100,
            Score = score, MaxCombo = 90, ClearFlag = true,
            PerfectCount = 90, GreatCount = 10, GoodCount = 0, PoorCount = 0, MissCount = 0,
            TotalNotes = 100, PlayingSkill = skill, GameSkill = skill * 1.5,
            ChartLevel = 50, ChartLevelDec = 0, CompletionReason = CompletionReason.SongComplete,
        };

        var callerA = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, MakeSummary(810_000, 81));
        });
        var callerB = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await _manager.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, MakeSummary(920_000, 92));
        });

        var results = await Task.WhenAll(callerA, callerB);
        Assert.All(results, r => Assert.True(r.IsSuccess,
            $"Both concurrent saves should succeed; saw status={r.Status} message={r.ErrorMessage}"));

        // The database must reflect both plays.
        var dbScore = await db.GetScoreWithHistoryAsync(chart.Id, EInstrumentPart.DRUMS, 100);
        Assert.NotNull(dbScore);
        Assert.Equal(2, dbScore!.PlayCount);

        // The in-memory variant must agree with the database. Without the guard,
        // the losing caller's older snapshot (PlayCount=1) could overwrite the
        // winner's PlayCount=2, leaving the live variant behind the database.
        var (scoreNode, drumScore) = FindScoreByChartId(_manager.RootSongs, chart.Id);
        Assert.NotNull(scoreNode);
        Assert.NotNull(drumScore);
        var difficultyIndex = Array.IndexOf(scoreNode!.Scores, drumScore);
        var liveVariant = scoreNode.GetScore(difficultyIndex, 100);
        Assert.NotNull(liveVariant);
        Assert.Equal(dbScore.PlayCount, liveVariant!.PlayCount);
    }
}
