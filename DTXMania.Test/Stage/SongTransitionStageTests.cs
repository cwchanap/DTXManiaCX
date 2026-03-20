using System.Reflection;
using DTXMania.Game;
using DTXMania.Game.Lib.Stage;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for SongTransitionStage focusing on pure logic methods
    /// that do not require graphics initialization.
    /// </summary>
    public class SongTransitionStageTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new SongTransitionStage(null));
        }

        [Fact]
        public void Constructor_WithValidGame_ShouldNotThrow()
        {
            var mockGame = new Mock<BaseGame>();
            var stage = new SongTransitionStage(mockGame.Object);
            Assert.NotNull(stage);
        }

        #endregion

        #region Type Property Tests

        [Fact]
        public void Type_ShouldReturnSongTransition()
        {
            var mockGame = new Mock<BaseGame>();
            var stage = new SongTransitionStage(mockGame.Object);
            Assert.Equal(StageType.SongTransition, stage.Type);
        }

        #endregion

        #region GetDifficultyName Tests (via reflection)

        [Theory]
        [InlineData(0, "Basic")]
        [InlineData(1, "Advanced")]
        [InlineData(2, "Extreme")]
        [InlineData(3, "Master")]
        [InlineData(4, "Ultimate")]
        public void GetDifficultyName_ValidDifficulty_ShouldReturnCorrectName(int difficulty, string expectedName)
        {
            var mockGame = new Mock<BaseGame>();
            var stage = new SongTransitionStage(mockGame.Object);

            var result = InvokePrivateMethod<string>(stage, "GetDifficultyName", difficulty);

            Assert.Equal(expectedName, result);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        [InlineData(100)]
        [InlineData(int.MinValue)]
        public void GetDifficultyName_InvalidDifficulty_ShouldReturnUnknown(int difficulty)
        {
            var mockGame = new Mock<BaseGame>();
            var stage = new SongTransitionStage(mockGame.Object);

            var result = InvokePrivateMethod<string>(stage, "GetDifficultyName", difficulty);

            Assert.Equal("Unknown", result);
        }

        #endregion

        #region GetCurrentDifficultyLevel Tests (via reflection)

        [Fact]
        public void GetCurrentDifficultyLevel_WhenSelectedSongIsNull_ShouldReturnZero()
        {
            var mockGame = new Mock<BaseGame>();
            var stage = new SongTransitionStage(mockGame.Object);

            // _selectedSong is null by default (private field not set)
            var result = InvokePrivateMethod<float>(stage, "GetCurrentDifficultyLevel");

            Assert.Equal(0.0f, result);
        }

        #endregion

        #region Inheritance and Interface Tests

        [Fact]
        public void SongTransitionStage_ShouldInheritFromBaseStage()
        {
            Assert.True(typeof(BaseStage).IsAssignableFrom(typeof(SongTransitionStage)));
        }

        [Fact]
        public void SongTransitionStage_ShouldImplementIStage()
        {
            Assert.True(typeof(IStage).IsAssignableFrom(typeof(SongTransitionStage)));
        }

        [Fact]
        public void InitialPhase_ShouldBeInactive()
        {
            var mockGame = new Mock<BaseGame>();
            var stage = new SongTransitionStage(mockGame.Object);
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        #endregion

        #region Helper Methods

        private static T InvokePrivateMethod<T>(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (T)method!.Invoke(target, args)!;
        }

        #endregion
    }
}
