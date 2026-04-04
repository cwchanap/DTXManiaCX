using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class StageManagerTransitionTests : IDisposable
    {
        private readonly StageManager _stageManager;
        private readonly TestStage _titleStage;
        private readonly TestStage _configStage;
        private readonly TestStage _resultStage;

        public StageManagerTransitionTests()
        {
            _stageManager = new StageManager(ReflectionHelpers.CreateGame());
            _titleStage = new TestStage(StageType.Title);
            _configStage = new TestStage(StageType.Config);
            _resultStage = new TestStage(StageType.Result);

            RegisterStage(_titleStage);
            RegisterStage(_configStage);
            RegisterStage(_resultStage);
        }

        public void Dispose()
        {
            _stageManager.Dispose();
        }

        [Fact]
        public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new StageManager(null!));
        }

        [Fact]
        public void ChangeStage_WithoutExplicitTransition_ShouldUseInstantTransition()
        {
            _stageManager.ChangeStage(StageType.Title);

            Assert.Same(_titleStage, _stageManager.CurrentStage);
            Assert.False(_stageManager.IsTransitioning);
            Assert.Equal(StagePhase.Normal, _stageManager.CurrentPhase);
            Assert.Equal(1, _titleStage.ActivateCount);
            Assert.Equal(1, _titleStage.TransitionInCount);
            Assert.Equal(1, _titleStage.TransitionCompleteCount);
        }

        [Fact]
        public void ChangeStage_WithInstantTransition_ShouldDeactivatePreviousStageAndPassSharedData()
        {
            var sharedData = new Dictionary<string, object> { ["Mode"] = "Config" };
            SetCurrentStage(_titleStage);

            _stageManager.ChangeStage(StageType.Config, new InstantTransition(), sharedData);

            Assert.Same(_configStage, _stageManager.CurrentStage);
            Assert.False(_stageManager.IsTransitioning);
            Assert.Equal(1, _titleStage.TransitionOutCount);
            Assert.Equal(1, _titleStage.DeactivateCount);
            Assert.Equal(1, _configStage.ActivateWithSharedDataCount);
            Assert.Equal("Config", _configStage.GetSharedData<string>("Mode"));
            Assert.Equal(1, _configStage.TransitionInCount);
            Assert.Equal(1, _configStage.TransitionCompleteCount);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(_stageManager, "_pendingSharedData"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(_stageManager, "_currentTransition"));
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(_stageManager, "_previousStage"));
        }

        [Fact]
        public void ChangeStage_WhenDisposed_ShouldIgnoreRequestedTransition()
        {
            SetCurrentStage(_titleStage);
            ReflectionHelpers.SetPrivateField(_stageManager, "_disposed", true);

            _stageManager.ChangeStage(StageType.Config);

            Assert.Same(_titleStage, _stageManager.CurrentStage);
            Assert.False(_stageManager.IsTransitioning);
            Assert.Equal(0, _configStage.ActivateCount);
        }

        [Fact]
        public void ChangeStage_WhenAlreadyTransitioning_ShouldIgnoreNewRequest()
        {
            var transition = new TestTransition(isComplete: false, fadeOutAlpha: 1.0f);
            SetCurrentStage(_titleStage);

            _stageManager.ChangeStage(StageType.Config, transition);
            _stageManager.ChangeStage(StageType.Result);

            Assert.True(_stageManager.IsTransitioning);
            Assert.Equal(StageType.Config, ReflectionHelpers.GetPrivateField<StageType>(_stageManager, "_targetStageType"));
            Assert.Equal(1, _titleStage.TransitionOutCount);
            Assert.Equal(0, _resultStage.ActivateCount);
        }

        [Fact]
        public void ChangeStage_WithUnknownStageType_ShouldThrowArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => _stageManager.ChangeStage((StageType)999));

            Assert.Contains("Unknown stage type", ex.Message);
        }

        [Fact]
        public void Update_WhenTransitionCompletes_ShouldActivateTargetAndUpdateIt()
        {
            var sharedData = new Dictionary<string, object> { ["Screen"] = "Options" };
            var transition = new TestTransition(isComplete: false, fadeOutAlpha: 1.0f)
            {
                CompleteAfterUpdate = true
            };
            SetCurrentStage(_titleStage);

            _stageManager.ChangeStage(StageType.Config, transition, sharedData);
            _stageManager.Update(0.25);

            Assert.False(_stageManager.IsTransitioning);
            Assert.Same(_configStage, _stageManager.CurrentStage);
            Assert.Equal(1, _titleStage.DeactivateCount);
            Assert.Equal(1, _configStage.ActivateWithSharedDataCount);
            Assert.Equal("Options", _configStage.GetSharedData<string>("Screen"));
            Assert.Equal(1, _configStage.TransitionInCount);
            Assert.Equal(1, _configStage.TransitionCompleteCount);
            Assert.Equal(1, _configStage.UpdateCount);
        }

        [Fact]
        public void Update_WhenDisposed_ShouldNotUpdateCurrentStage()
        {
            SetCurrentStage(_titleStage);
            ReflectionHelpers.SetPrivateField(_stageManager, "_disposed", true);

            _stageManager.Update(0.25);

            Assert.Equal(0, _titleStage.UpdateCount);
        }

        [Fact]
        public void Draw_WhenNotTransitioning_ShouldDrawCurrentStage()
        {
            SetCurrentStage(_titleStage);

            _stageManager.Draw(0.1);

            Assert.Equal(1, _titleStage.DrawCount);
        }

        [Fact]
        public void Draw_WhenTransitionFadeOutAlphaPositive_ShouldDrawCurrentStage()
        {
            SetCurrentStage(_titleStage);
            _stageManager.ChangeStage(StageType.Config, new TestTransition(isComplete: false, fadeOutAlpha: 0.5f));

            _stageManager.Draw(0.1);

            Assert.Equal(1, _titleStage.DrawCount);
            Assert.Equal(0, _configStage.DrawCount);
        }

        [Fact]
        public void Draw_WhenTransitionFadeOutAlphaZero_ShouldSkipCurrentStageDraw()
        {
            SetCurrentStage(_titleStage);
            _stageManager.ChangeStage(StageType.Config, new TestTransition(isComplete: false, fadeOutAlpha: 0.0f));

            _stageManager.Draw(0.1);

            Assert.Equal(0, _titleStage.DrawCount);
        }

        [Fact]
        public void Draw_WhenDisposed_ShouldNotDrawCurrentStage()
        {
            SetCurrentStage(_titleStage);
            ReflectionHelpers.SetPrivateField(_stageManager, "_disposed", true);

            _stageManager.Draw(0.1);

            Assert.Equal(0, _titleStage.DrawCount);
        }

        [Fact]
        public void Dispose_ShouldDeactivateCurrentStageAndDisposeCachedStages()
        {
            SetCurrentStage(_titleStage);

            _stageManager.Dispose();

            Assert.Equal(1, _titleStage.DeactivateCount);
            Assert.Equal(1, _titleStage.DisposeCount);
            Assert.Equal(1, _configStage.DisposeCount);
            Assert.Equal(1, _resultStage.DisposeCount);
            Assert.Null(ReflectionHelpers.GetPrivateField<object>(_stageManager, "_currentStage"));

            var stages = ReflectionHelpers.GetPrivateField<Dictionary<StageType, IStage>>(_stageManager, "_stages");
            Assert.NotNull(stages);
            Assert.Empty(stages!);
        }

        private void RegisterStage(TestStage stage)
        {
            stage.StageManager = _stageManager;
            var stages = ReflectionHelpers.GetPrivateField<Dictionary<StageType, IStage>>(_stageManager, "_stages");
            Assert.NotNull(stages);
            stages![stage.Type] = stage;
        }

        private void SetCurrentStage(TestStage stage)
        {
            stage.StageManager = _stageManager;
            stage.Activate();
            ReflectionHelpers.SetPrivateField(_stageManager, "_currentStage", stage);
        }

        private sealed class TestStage : IStage
        {
            private readonly Dictionary<string, object> _sharedData = new();

            public TestStage(StageType type)
            {
                Type = type;
            }

            public StageType Type { get; }
            public StagePhase CurrentPhase { get; private set; } = StagePhase.Inactive;
            public IStageManager StageManager { get; set; } = null!;
            public int ActivateCount { get; private set; }
            public int ActivateWithSharedDataCount { get; private set; }
            public int DeactivateCount { get; private set; }
            public int UpdateCount { get; private set; }
            public int DrawCount { get; private set; }
            public int TransitionInCount { get; private set; }
            public int TransitionOutCount { get; private set; }
            public int TransitionCompleteCount { get; private set; }
            public int DisposeCount { get; private set; }

            public void Activate()
            {
                ActivateCount++;
                CurrentPhase = StagePhase.Normal;
            }

            public void Activate(Dictionary<string, object> sharedData)
            {
                ActivateWithSharedDataCount++;
                foreach (var pair in sharedData ?? new Dictionary<string, object>())
                {
                    _sharedData[pair.Key] = pair.Value;
                }

                Activate();
            }

            public void Deactivate()
            {
                DeactivateCount++;
                CurrentPhase = StagePhase.Inactive;
            }

            public void Update(double deltaTime)
            {
                UpdateCount++;
            }

            public void Draw(double deltaTime)
            {
                DrawCount++;
            }

            public void OnTransitionIn(IStageTransition transition)
            {
                TransitionInCount++;
                CurrentPhase = StagePhase.FadeIn;
            }

            public void OnTransitionOut(IStageTransition transition)
            {
                TransitionOutCount++;
                CurrentPhase = StagePhase.FadeOut;
            }

            public void OnTransitionComplete()
            {
                TransitionCompleteCount++;
                CurrentPhase = StagePhase.Normal;
            }

            public void Dispose()
            {
                DisposeCount++;
            }

            public T GetSharedData<T>(string key)
            {
                return (T)_sharedData[key];
            }
        }

        private sealed class TestTransition : IStageTransition
        {
            private readonly float _fadeOutAlpha;

            public TestTransition(bool isComplete, float fadeOutAlpha)
            {
                IsComplete = isComplete;
                _fadeOutAlpha = fadeOutAlpha;
            }

            public bool CompleteAfterUpdate { get; set; }
            public double Duration => 1.0;
            public double Progress { get; private set; }
            public bool IsComplete { get; private set; }
            public int StartCount { get; private set; }
            public int UpdateCount { get; private set; }

            public void Start()
            {
                StartCount++;
            }

            public void Update(double deltaTime)
            {
                UpdateCount++;
                Progress += deltaTime;
                if (CompleteAfterUpdate)
                {
                    IsComplete = true;
                }
            }

            public float GetFadeOutAlpha() => _fadeOutAlpha;

            public float GetFadeInAlpha() => 1.0f - _fadeOutAlpha;

            public void Reset()
            {
                Progress = 0;
                IsComplete = false;
            }
        }
    }
}
