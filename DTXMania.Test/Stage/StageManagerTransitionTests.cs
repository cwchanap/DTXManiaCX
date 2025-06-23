using System;
using System.Collections.Generic;
using Xunit;
using DTX.Stage;
using DTXMania.Game;
using Microsoft.Xna.Framework;
using DTX.Input;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for StageManager transition functionality
    /// Tests stage transitions, shared data passing, and phase management
    /// </summary>
    public class StageManagerTransitionTests : IDisposable
    {
        private readonly MockStageManager _stageManager;

        public StageManagerTransitionTests()
        {
            _stageManager = new MockStageManager();
        }

        public void Dispose()
        {
            _stageManager?.Dispose();
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
            var transition = new FadeTransition(0.05, 0.05); // Short duration for test (0.1 total)

            // Verify initial state
            Assert.Null(_stageManager.CurrentStage);
            Assert.False(_stageManager.IsTransitioning);

            _stageManager.ChangeStage(StageType.Title, transition);

            // Verify transition started
            Assert.True(_stageManager.IsTransitioning);

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
            var titleStage = _stageManager.CurrentStage as MockStage;
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

        #region Helper Classes        /// <summary>
        /// Mock stage manager for testing without MonoGame dependencies
        /// </summary>
        private class MockStageManager : IStageManager, IDisposable
        {
            private readonly Dictionary<StageType, IStage> _stages;
            private IStage? _currentStage;
            private bool _isTransitioning;
            private StagePhase _currentPhase = StagePhase.Inactive;
            private IStageTransition? _currentTransition;
            private double _transitionElapsed;
            private IStage? _targetStage;
            private Dictionary<string, object>? _targetSharedData;
            
            public MockStageManager()
            {
                _stages = new Dictionary<StageType, IStage>
                {
                    [StageType.Title] = new MockStage(StageType.Title),
                    [StageType.Config] = new MockStage(StageType.Config)
                };

                foreach (var stage in _stages.Values)
                {
                    stage.StageManager = this;
                }
            }

            public IStage CurrentStage => _currentStage;
            public StagePhase CurrentPhase => _currentPhase;
            public bool IsTransitioning => _isTransitioning;

            public void ChangeStage(StageType stageType)
            {
                ChangeStage(stageType, null, null);
            }

            public void ChangeStage(StageType stageType, IStageTransition transition)
            {
                ChangeStage(stageType, transition, null);
            }

            public void ChangeStage(StageType stageType, IStageTransition transition, Dictionary<string, object> sharedData)
            {
                if (_isTransitioning)
                    return;

                if (!_stages.TryGetValue(stageType, out var newStage))
                    return;

                if (transition == null || transition is InstantTransition)
                {
                    // Instant transition
                    _currentStage?.Deactivate();
                    _currentStage = newStage;
                    _currentPhase = StagePhase.Normal;

                    _currentStage.Activate(sharedData);
                }
                else
                {
                    // Transition with effects
                    _isTransitioning = true;
                    _currentTransition = transition;
                    _currentTransition.Start(); // Start the transition!
                    _transitionElapsed = 0;
                    _currentPhase = StagePhase.FadeOut;

                    // Store the target stage for later
                    _targetStage = newStage;
                    _targetSharedData = sharedData;
                }
            }

            public void Update(double deltaTime)
            {
                if (_isTransitioning && _currentTransition != null)
                {
                    // Update the transition
                    _currentTransition.Update(deltaTime);

                    if (_currentTransition.IsComplete)
                    {
                        // Complete transition
                        _currentStage?.Deactivate();
                        _currentStage = _targetStage;

                        _currentStage.Activate(_targetSharedData);

                        _isTransitioning = false;
                        _currentPhase = StagePhase.Normal;
                        _currentTransition = null;
                        _transitionElapsed = 0;
                        _targetStage = null;
                        _targetSharedData = null;
                    }
                }

                _currentStage?.Update(deltaTime);
            }

            public void Draw(double deltaTime)
            {
                _currentStage?.Draw(deltaTime);
            }

            public void Dispose()
            {
                foreach (var stage in _stages.Values)
                {
                    stage?.Dispose();
                }
                _stages.Clear();
            }
        }

        /// <summary>
        /// Mock stage implementation for testing
        /// </summary>
        private class MockStage : IStage
        {
            private Dictionary<string, object> _sharedData = new Dictionary<string, object>();
            private bool _isActive;

            public MockStage(StageType type)
            {
                Type = type;
            }

            public StageType Type { get; }
            public StagePhase CurrentPhase { get; private set; } = StagePhase.Inactive;
            public IStageManager? StageManager { get; set; }

            public void Activate()
            {
                _isActive = true;
                CurrentPhase = StagePhase.Normal;
            }

            public void Activate(Dictionary<string, object> sharedData)
            {
                if (sharedData != null)
                    SetSharedData(sharedData);
                Activate();
            }

            public void Deactivate()
            {
                _isActive = false;
                CurrentPhase = StagePhase.Inactive;
                _sharedData.Clear();
            }

            public void Update(double deltaTime) { }
            public void Draw(double deltaTime) { }

            public void OnTransitionIn(IStageTransition transition)
            {
                CurrentPhase = StagePhase.FadeIn;
            }

            public void OnTransitionOut(IStageTransition transition)
            {
                CurrentPhase = StagePhase.FadeOut;
            }

            public void OnTransitionComplete()
            {
                CurrentPhase = StagePhase.Normal;
            }

            public void SetSharedData(Dictionary<string, object> sharedData)
            {
                _sharedData = new Dictionary<string, object>(sharedData);
            }

            // Expose shared data methods for testing
            public T GetSharedData<T>(string key, T defaultValue = default(T))
            {
                if (_sharedData.TryGetValue(key, out var value) && value is T typedValue)
                    return typedValue;
                return defaultValue;
            }

            public bool HasSharedData(string key)
            {
                return _sharedData.ContainsKey(key);
            }

            public void Dispose() { }
        }

        #endregion
    }
}
