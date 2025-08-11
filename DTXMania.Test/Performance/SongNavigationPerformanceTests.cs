using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.Performance
{
    /// <summary>
    /// Performance tests for song navigation optimization
    /// </summary>
    public class SongNavigationPerformanceTests : IDisposable
    {
        private readonly List<SongListNode> _testSongs;
        private readonly SongListDisplay _songListDisplay;

        public SongNavigationPerformanceTests()
        {
            // Create test songs for performance testing
            _testSongs = CreateTestSongs(100); // 100 songs for performance testing
            _songListDisplay = new SongListDisplay();
        }

        [Fact]
        public void Navigation_WithManyItems_ShouldBeResponsive()
        {
            // Arrange
            _songListDisplay.CurrentList = _testSongs;
            var stopwatch = new Stopwatch();

            // Act - Measure navigation performance
            stopwatch.Start();
            for (int i = 0; i < 50; i++) // Simulate rapid navigation
            {
                _songListDisplay.MoveNext();
            }
            stopwatch.Stop();

            // Assert - Navigation should complete quickly (under 100ms for 50 moves)
            Assert.True(stopwatch.ElapsedMilliseconds < 100, 
                $"Navigation took {stopwatch.ElapsedMilliseconds}ms, expected < 100ms");
        }

        [Fact]
        public void SelectionChanged_Event_ShouldNotBlock()
        {
            // Arrange
            _songListDisplay.CurrentList = _testSongs;
            var eventFired = false;
            var stopwatch = new Stopwatch();

            _songListDisplay.SelectionChanged += (sender, e) =>
            {
                eventFired = true;
            };

            // Act - Measure event handling performance
            stopwatch.Start();
            _songListDisplay.MoveNext();
            stopwatch.Stop();

            // Assert
            Assert.True(eventFired, "SelectionChanged event should fire");
            Assert.True(stopwatch.ElapsedMilliseconds < 50, 
                $"Selection change took {stopwatch.ElapsedMilliseconds}ms, expected < 50ms");
        }

        [Fact]
        public void CachePerformance_ShouldImproveWithRepeatedAccess()
        {
            // Arrange
            _songListDisplay.CurrentList = _testSongs;
            var firstAccessTime = 0L;
            var secondAccessTime = 0L;

            // Act - First access (cache miss)
            var stopwatch = Stopwatch.StartNew();
            _songListDisplay.MoveNext();
            _songListDisplay.MovePrevious(); // Return to original position
            stopwatch.Stop();
            firstAccessTime = stopwatch.ElapsedMilliseconds;

            // Second access (cache hit)
            stopwatch.Restart();
            _songListDisplay.MoveNext();
            _songListDisplay.MovePrevious(); // Return to original position
            stopwatch.Stop();
            secondAccessTime = stopwatch.ElapsedMilliseconds;

            // Assert - Second access should be faster due to caching
            Assert.True(secondAccessTime <= firstAccessTime, 
                $"Second access ({secondAccessTime}ms) should be <= first access ({firstAccessTime}ms)");
        }

        [Fact]
        public void RapidNavigation_ShouldNotCauseMemoryLeaks()
        {
            // Arrange
            _songListDisplay.CurrentList = _testSongs;
            var initialMemory = GC.GetTotalMemory(true);

            // Act - Perform many navigation operations
            for (int i = 0; i < 1000; i++)
            {
                _songListDisplay.MoveNext();
                if (i % 100 == 0) // Occasionally move backwards
                {
                    _songListDisplay.MovePrevious();
                }
            }

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = finalMemory - initialMemory;

            // Assert - Memory increase should be reasonable (less than 10MB)
            Assert.True(memoryIncrease < 10 * 1024 * 1024, 
                $"Memory increased by {memoryIncrease / 1024 / 1024}MB, expected < 10MB");
        }

        private List<SongListNode> CreateTestSongs(int count)
        {
            var songs = new List<SongListNode>();

            for (int i = 0; i < count; i++)
            {
                // Create test song and chart
                var testSong = new DTXMania.Game.Lib.Song.Entities.Song
                {
                    Title = $"Test Song {i:D3}",
                    Artist = $"Test Artist {i % 10}",
                    Genre = "Test Genre"
                };
                
                var testChart = new SongChart
                {
                    FilePath = $"test{i}.dtx",
                    BPM = 120.0 + (i % 80),
                    DrumLevel = 50 + (i % 50)
                };
                
                songs.Add(new SongListNode
                {
                    Type = NodeType.Score,
                    Title = $"Test Song {i:D3}",
                    DatabaseSong = testSong,
                    DatabaseChart = testChart,
                    Scores = new SongScore[]
                    {
                        new SongScore
                        {
                            Instrument = EInstrumentPart.DRUMS,
                            BestScore = 800000 + (i * 1000),
                            BestRank = 70 + (i % 30),
                            FullCombo = i % 5 == 0,
                            PlayCount = i % 20
                        }
                    }
                });
            }

            return songs;
        }

        public void Dispose()
        {
            _songListDisplay?.Dispose();
        }
    }
}
