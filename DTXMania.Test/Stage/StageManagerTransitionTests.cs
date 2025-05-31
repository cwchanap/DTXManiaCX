using System;
using System.Collections.Generic;
using Xunit;
using DTX.Stage;
using DTXMania.Shared.Game;
using Microsoft.Xna.Framework;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for StageManager transition functionality
    /// Tests stage transitions, shared data passing, and phase management
    /// </summary>
    public class StageManagerTransitionTests : IDisposable
    {
        private readonly TestGame _game;
        private readonly StageManager _stageManager;

        public StageManagerTransitionTests()
        {
            _game = new TestGame();
            _stageManager = new StageManager(_game);
        }

        public void Dispose()
        {
            _stageManager?.Dispose();
            _game?.Dispose();
        }

        #region Basic Transition Tests

        [Fact]
        public void StageManager_ShouldSupportInstantTransition()
        {
            // Act
            _stageManager.ChangeStage(StageType.Title);

            // Assert
            Assert.Equal(StageType.Title, _stageManager.CurrentStage?.Type);
            Assert.False(_stageManager.IsTransitioning);
        }

        [Fact]
        public void StageManager_ShouldSupportCustomTransition()
        {
            // Arrange
            var transition = new FadeTransition(0.5);

            // Act
            _stageManager.ChangeStage(StageType.Title, transition);

            // Assert
            Assert.True(_stageManager.IsTransitioning);
            Assert.Equal(StagePhase.FadeOut, _stageManager.CurrentPhase);
        }

        [Fact]
        public void StageManager_ShouldCompleteTransitionAfterDuration()
        {
            // Arrange
            var transition = new FadeTransition(0.1); // Short duration for test
            _stageManager.ChangeStage(StageType.Title, transition);

            // Act - Update beyond transition duration
            _stageManager.Update(0.2);

            // Assert
            Assert.False(_stageManager.IsTransitioning);
            Assert.Equal(StageType.Title, _stageManager.CurrentStage?.Type);
        }

        #endregion

        #region Shared Data Tests

        [Fact]
        public void StageManager_ShouldPassSharedDataBetweenStages()
        {
            // Arrange
            var sharedData = new Dictionary<string, object>
            {
                ["TestKey"] = "TestValue",
                ["Number"] = 42
            };

            // Act
            _stageManager.ChangeStage(StageType.Title, new InstantTransition(), sharedData);

            // Assert
            var titleStage = _stageManager.CurrentStage as TestStage;
            Assert.NotNull(titleStage);
            Assert.True(titleStage.HasSharedData("TestKey"));
            Assert.Equal("TestValue", titleStage.GetSharedData<string>("TestKey"));
            Assert.Equal(42, titleStage.GetSharedData<int>("Number"));
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public void StageManager_ShouldIgnoreTransitionWhenAlreadyTransitioning()
        {
            // Arrange
            var longTransition = new FadeTransition(1.0);
            _stageManager.ChangeStage(StageType.Title, longTransition);
            var originalStage = _stageManager.CurrentStage;

            // Act - Try to change stage while transitioning
            _stageManager.ChangeStage(StageType.Config);

            // Assert - Should ignore the second change
            Assert.True(_stageManager.IsTransitioning);
            Assert.Same(originalStage, _stageManager.CurrentStage);
        }

        [Fact]
        public void StageManager_ShouldHandleInvalidStageType()
        {
            // Arrange
            var invalidStageType = (StageType)999;

            // Act
            _stageManager.ChangeStage(invalidStageType);

            // Assert - Should not crash, current stage should remain null
            Assert.Null(_stageManager.CurrentStage);
            Assert.False(_stageManager.IsTransitioning);
        }

        #endregion

        #region Phase Management Tests

        [Fact]
        public void StageManager_ShouldReportCorrectPhase()
        {
            // Arrange
            _stageManager.ChangeStage(StageType.Title);

            // Assert
            Assert.Equal(StagePhase.Normal, _stageManager.CurrentPhase);
        }

        [Fact]
        public void StageManager_ShouldReportInactiveWhenNoStage()
        {
            // Assert
            Assert.Equal(StagePhase.Inactive, _stageManager.CurrentPhase);
        }

        #endregion

        #region Disposal Tests

        [Fact]
        public void StageManager_ShouldHandleDisposalDuringTransition()
        {
            // Arrange
            var transition = new FadeTransition(1.0);
            _stageManager.ChangeStage(StageType.Title, transition);

            // Act & Assert - Should not throw
            _stageManager.Dispose();
        }

        #endregion

        #region Helper Classes

        /// <summary>
        /// Test game implementation for testing
        /// </summary>
        private class TestGame : BaseGame
        {
            public TestGame()
            {
                // Initialize minimal game for testing
            }

            protected override void LoadContent()
            {
                // No content loading needed for tests
            }

            protected override void Update(GameTime gameTime)
            {
                // No update logic needed for tests
            }

            protected override void Draw(GameTime gameTime)
            {
                // No drawing needed for tests
            }
        }

        /// <summary>
        /// Test stage implementation for testing shared data
        /// </summary>
        private class TestStage : BaseStage
        {
            public override StageType Type => StageType.Title;

            public TestStage(BaseGame game) : base(game) { }

            // Expose protected methods for testing
            public new T GetSharedData<T>(string key, T defaultValue = default(T))
            {
                return base.GetSharedData(key, defaultValue);
            }

            public new bool HasSharedData(string key)
            {
                return base.HasSharedData(key);
            }
        }

        #endregion
    }
}
