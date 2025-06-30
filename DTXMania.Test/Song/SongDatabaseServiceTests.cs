using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SongDatabaseService class
    /// Tests song grouping functionality and database operations
    /// </summary>
    public class SongDatabaseServiceTests : IDisposable
    {
        private readonly SongDatabaseService _databaseService;
        private readonly string _testDbPath;

        public SongDatabaseServiceTests()
        {
            // Use unique database path for each test instance
            _testDbPath = Path.Combine(Path.GetTempPath(), $"test_song_db_{Guid.NewGuid()}.db");
            _databaseService = new SongDatabaseService(_testDbPath);
        }

        public void Dispose()
        {
            // Clean up after each test
            _databaseService?.Dispose();
            
            // Clean up test database file
            if (File.Exists(_testDbPath))
            {
                try
                {
                    File.Delete(_testDbPath);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }

        #region Song Grouping Tests

        [Fact]
        public async Task AddSongAsync_WithSameTitleAndArtist_ShouldGroupChartsIntoSingleSong()
        {
            // Arrange
            await _databaseService.InitializeDatabaseAsync();

            var song1 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Test Genre"
            };

            var song2 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song", // Same title
                Artist = "Test Artist", // Same artist
                Genre = "Test Genre"
            };

            var chart1 = new SongChart
            {
                FilePath = "/path/to/bas.dtx",
                Duration = 120.5,
                Bpm = 140,
                DrumLevel = 30,
                HasDrumChart = true
            };

            var chart2 = new SongChart
            {
                FilePath = "/path/to/adv.dtx",
                Duration = 180.7,
                Bpm = 140,
                DrumLevel = 50,
                HasDrumChart = true
            };

            // Act
            var songId1 = await _databaseService.AddSongAsync(song1, chart1);
            var songId2 = await _databaseService.AddSongAsync(song2, chart2);

            // Assert
            Assert.Equal(songId1, songId2); // Should return the same song ID

            // Verify database has only one song with two charts
            var songs = await _databaseService.GetSongsAsync();
            var testSong = songs.Single(s => s.Title == "Test Song");
            
            Assert.Equal("Test Song", testSong.Title);
            Assert.Equal("Test Artist", testSong.Artist);
            Assert.Equal(2, testSong.Charts.Count);

            // Verify charts have different file paths and properties
            var chartPaths = testSong.Charts.Select(c => c.FilePath).ToArray();
            Assert.Contains("/path/to/bas.dtx", chartPaths);
            Assert.Contains("/path/to/adv.dtx", chartPaths);

            var chart1Retrieved = testSong.Charts.Single(c => c.FilePath == "/path/to/bas.dtx");
            var chart2Retrieved = testSong.Charts.Single(c => c.FilePath == "/path/to/adv.dtx");
            
            Assert.Equal(120.5, chart1Retrieved.Duration);
            Assert.Equal(180.7, chart2Retrieved.Duration);
            Assert.Equal(30, chart1Retrieved.DrumLevel);
            Assert.Equal(50, chart2Retrieved.DrumLevel);
        }

        [Fact]
        public async Task AddSongAsync_WithDifferentTitles_ShouldCreateSeparateSongs()
        {
            // Arrange
            await _databaseService.InitializeDatabaseAsync();

            var song1 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Song One",
                Artist = "Same Artist",
                Genre = "Test Genre"
            };

            var song2 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Song Two", // Different title
                Artist = "Same Artist", // Same artist
                Genre = "Test Genre"
            };

            var chart1 = new SongChart
            {
                FilePath = "/path/to/song1.dtx",
                Duration = 120.5,
                Bpm = 140
            };

            var chart2 = new SongChart
            {
                FilePath = "/path/to/song2.dtx",
                Duration = 180.7,
                Bpm = 150
            };

            // Act
            var songId1 = await _databaseService.AddSongAsync(song1, chart1);
            var songId2 = await _databaseService.AddSongAsync(song2, chart2);

            // Assert
            Assert.NotEqual(songId1, songId2); // Should be different songs

            // Verify database has two separate songs
            var songs = await _databaseService.GetSongsAsync();
            var retrievedSongs = songs.Where(s => s.Artist == "Same Artist").ToList();
            
            Assert.Equal(2, retrievedSongs.Count);

            var song1Retrieved = retrievedSongs.Single(s => s.Title == "Song One");
            var song2Retrieved = retrievedSongs.Single(s => s.Title == "Song Two");

            Assert.Single(song1Retrieved.Charts);
            Assert.Single(song2Retrieved.Charts);
        }

        [Fact]
        public async Task AddSongAsync_WithDifferentArtists_ShouldCreateSeparateSongs()
        {
            // Arrange
            await _databaseService.InitializeDatabaseAsync();

            var song1 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Same Title",
                Artist = "Artist One",
                Genre = "Test Genre"
            };

            var song2 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Same Title", // Same title
                Artist = "Artist Two", // Different artist
                Genre = "Test Genre"
            };

            var chart1 = new SongChart
            {
                FilePath = "/path/to/artist1.dtx",
                Duration = 120.5,
                Bpm = 140
            };

            var chart2 = new SongChart
            {
                FilePath = "/path/to/artist2.dtx",
                Duration = 180.7,
                Bpm = 150
            };

            // Act
            var songId1 = await _databaseService.AddSongAsync(song1, chart1);
            var songId2 = await _databaseService.AddSongAsync(song2, chart2);

            // Assert
            Assert.NotEqual(songId1, songId2); // Should be different songs

            // Verify database has two separate songs
            var songs = await _databaseService.GetSongsAsync();
            var retrievedSongs = songs.Where(s => s.Title == "Same Title").ToList();
            
            Assert.Equal(2, retrievedSongs.Count);

            var song1Retrieved = retrievedSongs.Single(s => s.Artist == "Artist One");
            var song2Retrieved = retrievedSongs.Single(s => s.Artist == "Artist Two");

            Assert.Single(song1Retrieved.Charts);
            Assert.Single(song2Retrieved.Charts);
        }

        [Fact]
        public async Task AddSongAsync_WithSameFilePath_ShouldReturnExistingSongId()
        {
            // Arrange
            await _databaseService.InitializeDatabaseAsync();

            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Duplicate File Test",
                Artist = "Test Artist",
                Genre = "Test Genre"
            };

            var chart = new SongChart
            {
                FilePath = "/path/to/duplicate.dtx",
                Duration = 120.5,
                Bpm = 140
            };

            // Act - Add the same song twice
            var songId1 = await _databaseService.AddSongAsync(song, chart);
            var songId2 = await _databaseService.AddSongAsync(song, chart);

            // Assert
            Assert.Equal(songId1, songId2); // Should return the same ID

            // Verify database has only one song with one chart
            var songs = await _databaseService.GetSongsAsync();
            var testSongs = songs.Where(s => s.Title == "Duplicate File Test").ToList();
            
            Assert.Single(testSongs);
            Assert.Single(testSongs[0].Charts);
        }

        [Fact]
        public async Task AddSongAsync_WithMultipleChartsForSameSong_ShouldMaintainCorrectDurations()
        {
            // Arrange - Simulate "My Hope Is Gone" scenario
            await _databaseService.InitializeDatabaseAsync();

            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "My Hope Is Gone",
                Artist = "GALNERYUS",
                Genre = "Rock"
            };

            var basChart = new SongChart
            {
                FilePath = "/dtx/My Hope Is Gone/bas.dtx",
                Duration = 123.116636039934,
                Bpm = 184.0,
                DrumLevel = 30
            };

            var advChart = new SongChart
            {
                FilePath = "/dtx/My Hope Is Gone/adv.dtx",
                Duration = 123.116636039934,
                Bpm = 184.0,
                DrumLevel = 50
            };

            var fullChart = new SongChart
            {
                FilePath = "/dtx/My Hope Is Gone/full.dtx",
                Duration = 398.326086956521,
                Bpm = 184.0,
                DrumLevel = 70
            };

            // Act
            var songId1 = await _databaseService.AddSongAsync(song, basChart);
            var songId2 = await _databaseService.AddSongAsync(song, advChart);
            var songId3 = await _databaseService.AddSongAsync(song, fullChart);

            // Assert
            Assert.Equal(songId1, songId2);
            Assert.Equal(songId2, songId3);

            // Verify database structure
            var songs = await _databaseService.GetSongsAsync();
            var myHopeIsGone = songs.Single(s => s.Title == "My Hope Is Gone");
            
            Assert.Equal("GALNERYUS", myHopeIsGone.Artist);
            Assert.Equal(3, myHopeIsGone.Charts.Count);

            // Verify durations are preserved correctly
            var charts = myHopeIsGone.Charts.ToList();
            var basChartRetrieved = charts.Single(c => c.FilePath.EndsWith("bas.dtx"));
            var advChartRetrieved = charts.Single(c => c.FilePath.EndsWith("adv.dtx"));
            var fullChartRetrieved = charts.Single(c => c.FilePath.EndsWith("full.dtx"));

            Assert.Equal(123.116636039934, basChartRetrieved.Duration, 6);
            Assert.Equal(123.116636039934, advChartRetrieved.Duration, 6);
            Assert.Equal(398.326086956521, fullChartRetrieved.Duration, 6);

            // Verify BPM is consistent
            Assert.Equal(184.0, basChartRetrieved.Bpm);
            Assert.Equal(184.0, advChartRetrieved.Bpm);
            Assert.Equal(184.0, fullChartRetrieved.Bpm);

            // Verify difficulty levels
            Assert.Equal(30, basChartRetrieved.DrumLevel);
            Assert.Equal(50, advChartRetrieved.DrumLevel);
            Assert.Equal(70, fullChartRetrieved.DrumLevel);
        }

        #endregion
    }
}