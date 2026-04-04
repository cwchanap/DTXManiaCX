using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.TestData;

namespace DTXMania.Test.Song;

[Collection("SongManager")]
[Trait("Category", "Unit")]
public class SongManagerCoverageTests : IDisposable
{
    private readonly SongManager _manager;
    private readonly string _testRoot;
    private readonly string _testDbPath;

    public SongManagerCoverageTests()
    {
        SongManager.ResetInstanceForTesting();
        _manager = SongManager.Instance;

        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "DTXManiaCX_Tests",
            nameof(SongManagerCoverageTests),
            Guid.NewGuid().ToString("N"));
        _testDbPath = Path.Combine(_testRoot, "songs.db");

        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public async Task BuildSongListFromDatabasePublicAsync_WithBoxAndMultipleCharts_ShouldRebuildHierarchy()
    {
        var songsRoot = Path.Combine(_testRoot, "Songs");
        var boxFolder = Path.Combine(songsRoot, "DTXFiles.Favorites");
        var songFolder = Path.Combine(boxFolder, "My Song");

        Directory.CreateDirectory(songFolder);
        await File.WriteAllTextAsync(Path.Combine(boxFolder, "box.def"), """
#TITLE: Favorites Box
#GENRE: Rock
#SKINPATH: skins/favorites
""");
        await CreateDtxFileAsync(Path.Combine(songFolder, "basic.dtx"), "My Song", "Coverage Bot", "Rock", 25);
        await CreateDtxFileAsync(Path.Combine(songFolder, "advanced.dtx"), "My Song", "Coverage Bot", "Rock", 60);

        await InitializeAndEnumerateAsync(songsRoot);
        ClearRootSongs();

        await _manager.BuildSongListFromDatabasePublicAsync(new[] { songsRoot });

        var boxNode = Assert.Single(_manager.RootSongs);
        Assert.Equal(NodeType.Box, boxNode.Type);
        Assert.Equal("Favorites Box", boxNode.Title);
        Assert.Equal("Rock", boxNode.Genre);
        Assert.Equal("skins/favorites", boxNode.SkinPath);

        var songNode = Assert.Single(boxNode.Children);
        Assert.Equal("My Song", songNode.Title);
        Assert.NotNull(songNode.DatabaseSong);
        Assert.Equal(2, songNode.DatabaseSong!.Charts.Count);
        Assert.True(songNode.Scores.Count(score => score != null) >= 2);
        Assert.Equal("Level 1", songNode.DifficultyLabels[0]);
        Assert.Equal("Level 2", songNode.DifficultyLabels[1]);
    }

    [Fact]
    public async Task BuildSongListFromDatabasePublicAsync_WithoutDatabaseService_ShouldLeaveRootSongsEmpty()
    {
        var songsRoot = Path.Combine(_testRoot, "NoDatabaseSongs");
        Directory.CreateDirectory(songsRoot);

        await _manager.BuildSongListFromDatabasePublicAsync(new[] { songsRoot });

        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task BuildSongListFromDatabasePublicAsync_WithInvalidSearchPath_ShouldSkipPath()
    {
        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        await _manager.BuildSongListFromDatabasePublicAsync(new[] { Path.Combine(_testRoot, "missing") });

        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task ParseBoxDefinitionAsync_WithMissingFile_ShouldReturnNull()
    {
        var task = ReflectionHelpers.InvokePrivateMethod<Task<BoxDefinition?>>(
            _manager,
            "ParseBoxDefinitionAsync",
            Path.Combine(_testRoot, "missing-box.def"),
            System.Threading.CancellationToken.None);

        Assert.NotNull(task);
        Assert.Null(await task!);
    }

    [Fact]
    public async Task ParseSetDefinitionAsync_WithMissingFile_ShouldReturnEmptyList()
    {
        var task = ReflectionHelpers.InvokePrivateMethod<Task<List<SongListNode>>>(
            _manager,
            "ParseSetDefinitionAsync",
            Path.Combine(_testRoot, "missing-set.def"),
            null!,
            System.Threading.CancellationToken.None);

        Assert.NotNull(task);
        Assert.Empty(await task!);
    }

    [Fact]
    public void TryParseColor_WithInvalidHexValues_ShouldReturnFalse()
    {
        var method = ReflectionHelpers.GetMethod(_manager.GetType(), "TryParseColor");
        Assert.NotNull(method);

        var shortHexArgs = new object?[] { "#12", null };
        var invalidHexArgs = new object?[] { "#GGGGGG", null };

        var shortHexResult = (bool)method!.Invoke(_manager, shortHexArgs)!;
        var invalidHexResult = (bool)method.Invoke(_manager, invalidHexArgs)!;

        Assert.False(shortHexResult);
        Assert.False(invalidHexResult);
    }

    [Fact]
    public async Task LoadScoreCacheAsync_WithWarmDatabase_ShouldRebuildRootSongs()
    {
        var songsRoot = Path.Combine(_testRoot, "CacheSongs");
        var songFolder = Path.Combine(songsRoot, "Cached Song");

        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(Path.Combine(songFolder, "cached.dtx"), "Cached Song", "Coverage Bot", "Fusion", 35);

        await InitializeAndEnumerateAsync(songsRoot);
        SetDatabaseLastWriteTime(DateTime.Now.AddMinutes(5));
        ClearRootSongs();

        var loaded = await _manager.LoadScoreCacheAsync(new[] { songsRoot });

        Assert.True(loaded);
        var rebuiltSong = Assert.Single(_manager.RootSongs);
        Assert.Equal("Cached Song", rebuiltSong.Title);
        Assert.NotNull(rebuiltSong.DatabaseSong);
    }

    [Fact]
    public async Task LoadScoreCacheAsync_WithEmptyDatabase_ShouldReturnFalse()
    {
        var songsRoot = Path.Combine(_testRoot, "EmptyCacheSongs");
        Directory.CreateDirectory(songsRoot);

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        var loaded = await _manager.LoadScoreCacheAsync(new[] { songsRoot });

        Assert.False(loaded);
        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithForceEnumeration_ShouldReturnTrue()
    {
        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { _testRoot }, forceEnumeration: true);

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithoutDatabaseService_ShouldReturnTrue()
    {
        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { _testRoot });

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithEmptyDatabase_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "NeedsEnumerationEmpty");
        Directory.CreateDirectory(songsRoot);

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { songsRoot });

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithNullSearchPathsOnPopulatedDatabase_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "NullSearchPathsSongs");
        var songFolder = Path.Combine(songsRoot, "Song");
        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(Path.Combine(songFolder, "song.dtx"), "Null Search Song", "Coverage Bot", "Rock", 30);

        await InitializeAndEnumerateAsync(songsRoot);

        var needsEnumeration = await _manager.NeedsEnumerationAsync(null!);

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithStableFilesystem_ShouldReturnFalse()
    {
        var songsRoot = Path.Combine(_testRoot, "StableSongs");
        var songFolder = Path.Combine(songsRoot, "Stable Song");

        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(Path.Combine(songFolder, "stable.dtx"), "Stable Song", "Coverage Bot", "Jazz", 40);

        await InitializeAndEnumerateAsync(songsRoot);
        SetDatabaseLastWriteTime(DateTime.Now.AddMinutes(5));

        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { songsRoot });

        Assert.False(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenFileCountChanges_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "ChangedSongs");
        var firstSongFolder = Path.Combine(songsRoot, "First Song");
        var secondSongFolder = Path.Combine(songsRoot, "Second Song");

        Directory.CreateDirectory(firstSongFolder);
        await CreateDtxFileAsync(Path.Combine(firstSongFolder, "first.dtx"), "First Song", "Coverage Bot", "Jazz", 40);

        await InitializeAndEnumerateAsync(songsRoot);
        SetDatabaseLastWriteTime(DateTime.Now.AddMinutes(5));

        Directory.CreateDirectory(secondSongFolder);
        await CreateDtxFileAsync(Path.Combine(secondSongFolder, "second.dtx"), "Second Song", "Coverage Bot", "Jazz", 55);

        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { songsRoot });

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task EnumerateSongsOnlyAsync_WithEmptyDirectory_ShouldReturnZero()
    {
        var songsRoot = Path.Combine(_testRoot, "EnumerateOnlyEmpty");
        Directory.CreateDirectory(songsRoot);

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        var result = await _manager.EnumerateSongsOnlyAsync(new[] { songsRoot });

        Assert.Equal(0, result);
        Assert.Equal(0, _manager.DiscoveredScoreCount);
        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task EnumerateSongsAsync_WithCancelledToken_ShouldReturnZero()
    {
        var songsRoot = Path.Combine(_testRoot, "CancelledSongs");
        Directory.CreateDirectory(songsRoot);
        await CreateDtxFileAsync(Path.Combine(songsRoot, "cancelled.dtx"), "Cancelled Song", "Coverage Bot", "Rock", 35);

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        using var cancellation = new System.Threading.CancellationTokenSource();
        cancellation.Cancel();

        var result = await _manager.EnumerateSongsAsync(new[] { songsRoot }, cancellationToken: cancellation.Token);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task EnumerateSongsAsync_WithEmptyBox_ShouldSkipEmptyBox()
    {
        var songsRoot = Path.Combine(_testRoot, "EmptyBoxSongs");
        Directory.CreateDirectory(Path.Combine(songsRoot, "DTXFiles.Empty"));

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var result = await _manager.EnumerateSongsAsync(new[] { songsRoot });

        Assert.Equal(0, result);
        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task CheckDatabaseFilesStillExist_WhenChartMoved_ShouldUpdateStoredPath()
    {
        var songsRoot = Path.Combine(_testRoot, "MovedSongs");
        var originalFolder = Path.Combine(songsRoot, "Original");
        var movedFolder = Path.Combine(songsRoot, "Moved");
        var originalPath = Path.Combine(originalFolder, "moved-song.dtx");
        var movedPath = Path.Combine(movedFolder, "moved-song.dtx");

        Directory.CreateDirectory(originalFolder);
        await CreateDtxFileAsync(originalPath, "Moved Song", "Coverage Bot", "Rock", 50);

        await InitializeAndEnumerateAsync(songsRoot);

        Directory.CreateDirectory(movedFolder);
        File.Move(originalPath, movedPath);

        ReflectionHelpers.SetPrivateField(_manager, "_currentSearchPaths", new[] { songsRoot });

        var checkTask = ReflectionHelpers.InvokePrivateMethod<Task<bool>>(_manager, "CheckDatabaseFilesStillExist");
        Assert.NotNull(checkTask);

        var changeDetected = await checkTask!;
        var songs = await _manager.DatabaseService!.GetSongsAsync();

        Assert.True(changeDetected);
        Assert.Equal(movedPath, songs.Single().Charts.Single().FilePath);
    }

    [Fact]
    public async Task PublicDatabaseHelpers_ShouldReturnStatsSearchResultsAndScores()
    {
        var songsRoot = Path.Combine(_testRoot, "HelperSongs");
        var rockFolder = Path.Combine(songsRoot, "Blue Sky");
        var popFolder = Path.Combine(songsRoot, "Red Moon");

        Directory.CreateDirectory(rockFolder);
        Directory.CreateDirectory(popFolder);

        await CreateDtxFileAsync(Path.Combine(rockFolder, "blue.dtx"), "Blue Sky", "Alice", "Rock", 30);
        await CreateDtxFileAsync(Path.Combine(popFolder, "red.dtx"), "Red Moon", "Bob", "Pop", 45);

        await InitializeAndEnumerateAsync(songsRoot);

        Assert.True(await _manager.DatabaseExistsAsync());

        var stats = await _manager.GetDatabaseStatsAsync();
        Assert.NotNull(stats);
        Assert.Equal(2, stats!.SongCount);
        Assert.Equal(2, stats.ScoreCount);

        var rockSongs = await _manager.GetSongsByGenreAsync("Rock");
        var foundSongs = await _manager.FindSongsBySearchAsync("Blue");

        var rockSong = Assert.Single(rockSongs);
        var foundSong = Assert.Single(foundSongs);
        Assert.Equal("Blue Sky", rockSong.Title);
        Assert.Equal("Blue Sky", foundSong.Title);

        var chartId = rockSong.Charts.Single().Id;
        var scoreUpdated = await _manager.UpdateScoreAsync(chartId, EInstrumentPart.DRUMS, 950_000, 98.5, fullCombo: true);
        var topScores = await _manager.GetTopScoresAsync(EInstrumentPart.DRUMS, limit: 1);

        Assert.True(scoreUpdated);
        Assert.Single(topScores);
        Assert.Equal(chartId, topScores[0].ChartId);
        Assert.Equal(950_000, topScores[0].BestScore);

        Assert.True(await _manager.SaveSongsDBAsync());
        Assert.True(await _manager.BuildSongListsAsync());
    }

    [Fact]
    public async Task PublicHelpers_WithUninitializedDatabaseServiceInstance_ShouldReturnSafeFallbacks()
    {
        ReflectionHelpers.SetPrivateField(_manager, "_databaseService", new SongDatabaseService(_testDbPath));

        Assert.Equal(0, await _manager.GetDatabaseScoreCountAsync());
        Assert.Null(await _manager.GetDatabaseStatsAsync());
        Assert.Empty(await _manager.GetTopScoresAsync(EInstrumentPart.DRUMS));
        Assert.False(await _manager.UpdateScoreAsync(1, EInstrumentPart.DRUMS, 123, 45.6, fullCombo: false));
        Assert.Empty(await _manager.FindSongsBySearchAsync("anything"));
        Assert.Empty(await _manager.GetSongsByGenreAsync("anything"));
    }

    [Fact]
    public async Task InitializeDatabaseServiceAsync_WithInvalidPath_ShouldReturnFalse()
    {
        var initialized = await _manager.InitializeDatabaseServiceAsync("\0invalid");

        Assert.False(initialized);
    }

    [Fact]
    public async Task InitializeDatabaseServiceAsync_WithPurgeRequested_ShouldClearExistingDatabase()
    {
        var songsRoot = Path.Combine(_testRoot, "PurgeSongs");
        var songFolder = Path.Combine(songsRoot, "Song");
        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(Path.Combine(songFolder, "song.dtx"), "Purge Song", "Coverage Bot", "Rock", 35);

        await InitializeAndEnumerateAsync(songsRoot);

        var reinitialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath, purgeDatabaseFirst: true);
        var stats = await _manager.GetDatabaseStatsAsync();

        Assert.True(reinitialized);
        Assert.NotNull(stats);
        Assert.True(await _manager.DatabaseExistsAsync());
    }

    [Fact]
    public async Task StateHelpers_ShouldHandleInitializationAndCorruptionChecks()
    {
        Assert.False(await _manager.IsDatabaseCorruptedAsync());

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);
        Assert.False(await _manager.IsDatabaseCorruptedAsync());

        _manager.SetInitialized();

        Assert.True(_manager.IsInitialized);
    }

    [Fact]
    public async Task PublicDatabaseHelpers_WithoutInitializedService_ShouldReturnSafeDefaults()
    {
        Assert.False(await _manager.DatabaseExistsAsync());
        Assert.Null(await _manager.GetDatabaseStatsAsync());
        Assert.False(await _manager.LoadScoreCacheAsync(new[] { Path.Combine(_testRoot, "none") }));
        Assert.False(await _manager.SaveSongsDBAsync());
        Assert.Empty(await _manager.GetTopScoresAsync(EInstrumentPart.DRUMS));
        Assert.False(await _manager.UpdateScoreAsync(1, EInstrumentPart.DRUMS, 1, 1.0, fullCombo: false));
        Assert.Empty(await _manager.FindSongsBySearchAsync("anything"));
        Assert.Empty(await _manager.GetSongsByGenreAsync("anything"));
        Assert.False(await _manager.BuildSongListsAsync());
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
            // Best-effort cleanup for temp test assets.
        }
    }

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

    private void SetDatabaseLastWriteTime(DateTime lastWriteTime)
    {
        Assert.NotNull(_manager.DatabaseService);
        File.SetLastWriteTime(_manager.DatabaseService!.DatabasePath, lastWriteTime);
    }
}
