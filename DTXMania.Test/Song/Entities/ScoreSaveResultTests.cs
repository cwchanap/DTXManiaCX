using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song.Entities
{
    [Trait("Category", "Unit")]
    public sealed class ScoreSaveResultTests
    {
        [Fact]
        public void Saved_SetsStatusAndSongScoreId()
        {
            var result = ScoreSaveResult.Saved(42);

            Assert.Equal(ScoreSaveStatus.Saved, result.Status);
            Assert.Equal(42, result.SongScoreId);
            Assert.Null(result.ErrorMessage);
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void AlreadySaved_SetsStatusAndPreservesSongScoreId()
        {
            var result = ScoreSaveResult.AlreadySaved(7);

            Assert.Equal(ScoreSaveStatus.AlreadySaved, result.Status);
            Assert.Equal(7, result.SongScoreId);
            Assert.Null(result.ErrorMessage);
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void AlreadySaved_WithNullSongScoreId_IsStillSuccess()
        {
            var result = ScoreSaveResult.AlreadySaved(null);

            Assert.Equal(ScoreSaveStatus.AlreadySaved, result.Status);
            Assert.Null(result.SongScoreId);
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void Failed_SetsStatusAndErrorMessage()
        {
            var result = ScoreSaveResult.Failed("database locked");

            Assert.Equal(ScoreSaveStatus.Failed, result.Status);
            Assert.Null(result.SongScoreId);
            Assert.Equal("database locked", result.ErrorMessage);
            Assert.False(result.IsSuccess);
        }

        [Theory]
        [InlineData(ScoreSaveStatus.Saved, true)]
        [InlineData(ScoreSaveStatus.AlreadySaved, true)]
        [InlineData(ScoreSaveStatus.Failed, false)]
        public void IsSuccess_ReflectsStatus(ScoreSaveStatus status, bool expected)
        {
            var result = status switch
            {
                ScoreSaveStatus.Saved => ScoreSaveResult.Saved(1),
                ScoreSaveStatus.AlreadySaved => ScoreSaveResult.AlreadySaved(1),
                ScoreSaveStatus.Failed => ScoreSaveResult.Failed("err"),
                _ => throw new System.ArgumentOutOfRangeException(nameof(status)),
            };

            Assert.Equal(expected, result.IsSuccess);
        }
    }
}
