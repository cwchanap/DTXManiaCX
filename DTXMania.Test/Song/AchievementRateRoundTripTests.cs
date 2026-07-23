using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.TestData;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song;

/// <summary>
/// Integration tests verifying that BestAchievementRate survives the full DB load-back
/// path: UpdateScoreAsync (PerformanceSummary) → BuildSongListFromDatabasePublicAsync
/// → CreateSongNodeFromDatabaseEntities → PopulatePlayHistoryFromCharts
/// → SongListNode.Scores[i].BestAchievementRate.
/// </summary>
[Collection("SongManager")]
[Trait("Category", "Unit")]
public class AchievementRateRoundTripTests : IDisposable
{
    private readonly SongManager _manager;
    private readonly string _testRoot;
    private readonly string _testDbPath;

    public AchievementRateRoundTripTests()
    {
        SongManager.ResetInstanceForTesting();
        _manager = SongManager.Instance;

        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "DTXManiaCX_Tests",
            nameof(AchievementRateRoundTripTests),
            Guid.NewGuid().ToString("N"));
        _testDbPath = Path.Combine(_testRoot, "songs.db");

        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public async Task BuildSongListFromDatabasePublicAsync_AfterPerformanceSummary_ShouldLoadBackBestAchievementRate()
    {
        // Arrange: create a multi-chart song (two .dtx files with the same title)
        var songsRoot = Path.Combine(_testRoot, "Songs");
        var songFolder = Path.Combine(songsRoot, "RoundTrip Song");
        Directory.CreateDirectory(songFolder);

        const string songTitle = "RoundTrip Song";
        const string artist = "Test Bot";
        const string genre = "RoundTrip";

        await CreateDtxFileAsync(Path.Combine(songFolder, "basic.dtx"), songTitle, artist, genre, drumLevel: 30);
        await CreateDtxFileAsync(Path.Combine(songFolder, "advanced.dtx"), songTitle, artist, genre, drumLevel: 65);

        await InitializeAndEnumerateAsync(songsRoot);

        // Get the chart IDs from the database so we can record a performance
        var songs = await _manager.DatabaseService!.GetSongsAsync();
        var song = Assert.Single(songs);
        Assert.Equal(2, song.Charts.Count);

        var advancedChart = song.Charts.Single(c => c.DrumLevel == 65);
        Assert.True(advancedChart.Id > 0, "Chart should have a database-assigned Id");

        // Act: record a performance that sets BestAchievementRate via the PerformanceSummary overload.
        // BestAchievementRate is only written when GameSkill > score.HighSkill (initial HighSkill = 0).
        const double expectedAchievementRate = 87.53;
        var summary = new PerformanceSummary
        {
            Score = 900_000,
            PlayingSkill = expectedAchievementRate,
            GameSkill = 45.0, // > 0 (initial HighSkill), triggers the BestAchievementRate write
            ClearFlag = true,
            PerfectCount = 80,
            GreatCount = 15,
            GoodCount = 3,
            PoorCount = 1,
            MissCount = 1,
            MaxCombo = 80,
            TotalNotes = 100
        };

        var updated = await _manager.UpdateScoreAsync(advancedChart.Id, EInstrumentPart.DRUMS, summary);
        Assert.True(
            updated.IsSuccess,
            "UpdateScoreAsync (PerformanceSummary) should succeed");

        // Clear the in-memory song list and rebuild from the database
        ClearRootSongs();
        await _manager.BuildSongListFromDatabasePublicAsync(new[] { songsRoot });

        // Assert: the loaded SongListNode should carry the persisted BestAchievementRate
        var loadedSong = Assert.Single(_manager.RootSongs);
        Assert.Equal(songTitle, loadedSong.Title);

        // Find the score slot matching the advanced chart (DRUMS, difficulty 65)
        var advancedScore = loadedSong.Scores.FirstOrDefault(s =>
            s != null && s.Instrument == EInstrumentPart.DRUMS && s.DifficultyLevel == 65);
        Assert.NotNull(advancedScore);
        Assert.Equal(expectedAchievementRate, advancedScore!.BestAchievementRate);
    }

    [Fact]
    public async Task BuildSongListFromDatabasePublicAsync_AfterLowerGameSkill_ShouldNotOverwriteBestAchievementRate()
    {
        // Arrange: multi-chart song
        var songsRoot = Path.Combine(_testRoot, "Songs");
        var songFolder = Path.Combine(songsRoot, "NoOverwrite Song");
        Directory.CreateDirectory(songFolder);

        const string songTitle = "NoOverwrite Song";
        const string artist = "Test Bot";
        const string genre = "NoOverwrite";

        await CreateDtxFileAsync(Path.Combine(songFolder, "basic.dtx"), songTitle, artist, genre, drumLevel: 40);
        await CreateDtxFileAsync(Path.Combine(songFolder, "advanced.dtx"), songTitle, artist, genre, drumLevel: 70);

        await InitializeAndEnumerateAsync(songsRoot);

        var songs = await _manager.DatabaseService!.GetSongsAsync();
        var song = Assert.Single(songs);
        var advancedChart = song.Charts.Single(c => c.DrumLevel == 70);

        // First play: high GameSkill sets BestAchievementRate
        const double firstRate = 92.10;
        await _manager.UpdateScoreAsync(advancedChart.Id, EInstrumentPart.DRUMS, new PerformanceSummary
        {
            Score = 950_000,
            PlayingSkill = firstRate,
            GameSkill = 60.0,
            ClearFlag = true,
            TotalNotes = 100
        });

        // Second play: lower GameSkill should NOT overwrite BestAchievementRate (NX-faithful gate)
        await _manager.UpdateScoreAsync(advancedChart.Id, EInstrumentPart.DRUMS, new PerformanceSummary
        {
            Score = 800_000,
            PlayingSkill = 50.0,
            GameSkill = 30.0, // < 60.0 (current HighSkill), so BestAchievementRate stays
            ClearFlag = true,
            TotalNotes = 100
        });

        // Clear and reload from DB
        ClearRootSongs();
        await _manager.BuildSongListFromDatabasePublicAsync(new[] { songsRoot });

        // Assert: BestAchievementRate should still be the first (higher) value
        var loadedSong = Assert.Single(_manager.RootSongs);
        var advancedScore = loadedSong.Scores.FirstOrDefault(s =>
            s != null && s.Instrument == EInstrumentPart.DRUMS && s.DifficultyLevel == 70);
        Assert.NotNull(advancedScore);
        Assert.Equal(firstRate, advancedScore!.BestAchievementRate);
    }

    public void Dispose()
    {
        try
        {
            SongManager.ResetInstanceForTesting();
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp test assets.
        }
    }

    #region Helpers

    private async Task InitializeAndEnumerateAsync(string songsRoot)
    {
        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        var result = await _manager.EnumerateSongsAsync(new[] { songsRoot });
        Assert.True(result >= 1);
        Assert.NotNull(_manager.DatabaseService);
    }

    private async Task CreateDtxFileAsync(string path, string title, string artist, string genre, int drumLevel)
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

    private void ClearRootSongs()
    {
        var rootSongs = ReflectionHelpers.GetPrivateField<List<SongListNode>>(_manager, "_rootSongs");
        Assert.NotNull(rootSongs);
        rootSongs!.Clear();
    }

    #endregion
}
