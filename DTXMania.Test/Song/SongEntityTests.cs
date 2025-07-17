using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for Song and SongChart EF Core entities
    /// Tests entity properties, calculated properties, and helper methods
    /// </summary>
    public class SongEntityTests
    {
        #region SongChart Entity Tests

        [Theory]
        [InlineData("INVALID")]
        [InlineData("")]
        [InlineData(null)]
        public void SongChart_GetDifficultyLevel_WithInvalidInstrument_ShouldReturnNull(string instrument)
        {
            // Arrange
            var chart = new SongChart
            {
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 65
            };

            // Act
            var result = chart.GetDifficultyLevel(instrument);

            // Assert
            Assert.Null(result);
        }





        #endregion

        #region Integration Tests

        [Fact]
        public void Song_WithChart_ShouldProvideAvailableInstruments()
        {
            // Arrange
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song"
            };
            
            var chart = new SongChart
            {
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 0, // Bass not available
                HasDrumChart = true,
                HasGuitarChart = true,
                HasBassChart = false
            };
            
            song.Charts.Add(chart);

            // Act
            var availableInstruments = song.AvailableInstruments;

            // Assert
            Assert.Contains("DRUMS", availableInstruments);
            Assert.Contains("GUITAR", availableInstruments);
            Assert.DoesNotContain("BASS", availableInstruments);
        }



        #endregion
    }
}