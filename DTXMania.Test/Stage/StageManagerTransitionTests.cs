using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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
        public void ChangeStage_ShouldRecordRequestedAndCompletedBreadcrumbs()
        {
            var breadcrumbs = new RecordingBreadcrumbSink();
            var contexts = new RecordingContextSink();
            using var manager = new StageManager(
                ReflectionHelpers.CreateGame(),
                NullLogger<StageManager>.Instance,
                breadcrumbs,
                contexts);
            var title = new TestStage(StageType.Title) { StageManager = manager };
            var stages = ReflectionHelpers.GetPrivateField<Dictionary<StageType, IStage>>(manager, "_stages");
            Assert.NotNull(stages);
            stages![StageType.Title] = title;

            manager.ChangeStage(StageType.Title);

            Assert.Contains(breadcrumbs.Events, item => item.EventName == "stage_transition_requested");
            Assert.Contains(breadcrumbs.Events, item => item.EventName == "stage_transition_completed");
            var stage = Assert.Single(contexts.Snapshots, item => item.Kind == CrashContextKind.Stage);
            Assert.Equal(StageType.Title, stage.Fields["Stage"]);
        }

        [Fact]
        public void ChangeStage_WhenRejected_ShouldUseTheFixedRejectedBreadcrumb()
        {
            var breadcrumbs = new RecordingBreadcrumbSink();
            using var manager = new StageManager(
                ReflectionHelpers.CreateGame(),
                NullLogger<StageManager>.Instance,
                breadcrumbs,
                EmptyCrashContextSink.Instance);
            manager.Dispose();

            manager.ChangeStage(StageType.Title);

            var rejected = Assert.Single(
                breadcrumbs.Events,
                item => item.EventName == "stage_transition_rejected");
            Assert.IsAssignableFrom<Enum>(rejected.Properties["Reason"]);
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
        public void DrawTransition_WhenCurrentStageIsNull_ShouldNotThrow()
        {
            // Set up a transition state with null current stage
            ReflectionHelpers.SetPrivateField(_stageManager, "_currentStage", null);
            ReflectionHelpers.SetPrivateField(_stageManager, "_isTransitioning", true);
            ReflectionHelpers.SetPrivateField(_stageManager, "_currentTransition", new TestTransition(isComplete: false, fadeOutAlpha: 0.5f));

            // Should not throw
            var exception = Record.Exception(() => _stageManager.Draw(0.1));
            Assert.Null(exception);
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

        [Fact]
        public void GetOrCreateStage_DrumConfig_LazilyCreatesAndCachesDrumConfigStage()
        {
            // DrumConfigStage's constructor needs a ConfigManager on the game; GetOrCreateStage
            // only constructs (it does not Activate), so this exercises the switch arm without a
            // GraphicsDevice and without triggering stage activation.
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), new ConfigManager());
            using var manager = new StageManager(game);

            var stage = (IStage)ReflectionHelpers.InvokePrivateMethod(manager, "GetOrCreateStage", StageType.DrumConfig)!;

            Assert.IsType<DrumConfigStage>(stage);
            Assert.Same(manager, stage.StageManager);

            // The created stage is cached: a second lookup returns the same instance.
            var stage2 = (IStage)ReflectionHelpers.InvokePrivateMethod(manager, "GetOrCreateStage", StageType.DrumConfig)!;
            Assert.Same(stage, stage2);
        }

        [Fact]
        public void GetOrCreateStage_WhenStartupCacheMiss_ShouldRecordConstructionAroundWiring()
        {
            var fixture = CreateCriticalPathFixture();
            using var manager = new StageManager(fixture.Game);
            var wiredAndCachedAtEnd = false;
            fixture.Clock.OnTimestamp = timestamp =>
            {
                if (timestamp != 2)
                    return;

                var stagesAtEnd = GetStages(manager);
                wiredAndCachedAtEnd =
                    stagesAtEnd.TryGetValue(StageType.Startup, out var cachedStage) &&
                    ReferenceEquals(manager, cachedStage.StageManager);
            };

            var stage = (IStage)ReflectionHelpers.InvokePrivateMethod(
                manager,
                "GetOrCreateStage",
                StageType.Startup)!;

            var stages = GetStages(manager);
            Assert.Same(manager, stage.StageManager);
            Assert.Same(stage, stages[StageType.Startup]);
            Assert.True(wiredAndCachedAtEnd);
            Assert.True(
                GetTimestamp(fixture.Trace, StartupCriticalPathMilestone.StartupConstructBegin) <
                GetTimestamp(fixture.Trace, StartupCriticalPathMilestone.StartupConstructEnd));
        }

        [Fact]
        public void ChangeStage_WhenTitleCacheMiss_ShouldConstructBeforeTransitionStart()
        {
            var fixture = CreateCriticalPathFixture();
            using var manager = new StageManager(fixture.Game);
            var startupStage = SetCurrentStage(manager, StageType.Startup);

            manager.ChangeStage(
                StageType.Title,
                new TestTransition(isComplete: false, fadeOutAlpha: 1.0f));

            Assert.Equal(1, startupStage.TransitionOutCount);
            Assert.True(
                GetTimestamp(fixture.Trace, StartupCriticalPathMilestone.TitleConstructBegin) <
                GetTimestamp(fixture.Trace, StartupCriticalPathMilestone.TitleConstructEnd));
            Assert.True(
                GetTimestamp(fixture.Trace, StartupCriticalPathMilestone.TitleConstructEnd) <
                GetTimestamp(fixture.Trace, StartupCriticalPathMilestone.TransitionStart));
        }

        [Fact]
        public void CompleteTransition_WhenTitleLookupRepeats_ShouldRequireCacheHit()
        {
            var fixture = CreateCriticalPathFixture();
            using var manager = new StageManager(fixture.Game);
            SetCurrentStage(manager, StageType.Startup);
            var transition = new TestTransition(isComplete: false, fadeOutAlpha: 1.0f)
            {
                CompleteAfterUpdate = true
            };

            manager.ChangeStage(StageType.Title, transition);
            GetStages(manager)[StageType.Title] = new TestStage(StageType.Title)
            {
                StageManager = manager
            };
            manager.Update(0.25);

            Assert.True(GetTraceBoolean(fixture.Trace, "_titleCompletionLookupRecorded"));
            Assert.True(GetTraceBoolean(fixture.Trace, "_titleCompletionLookupCacheHit"));
            AssertTraceOpen(fixture.Trace);
        }

        [Fact]
        public void Update_WhenTitleTransitionRuns_ShouldAccumulateCountAndGameTime()
        {
            var fixture = CreateCriticalPathFixture();
            using var manager = new StageManager(fixture.Game);
            SetCurrentStage(manager, StageType.Startup);

            manager.ChangeStage(
                StageType.Title,
                new TestTransition(isComplete: false, fadeOutAlpha: 1.0f));
            manager.Update(0.25);
            manager.Update(0.50);

            Assert.Equal(2, GetTraceLong(fixture.Trace, "_transitionUpdateCount"));
            Assert.Equal(
                TimeSpan.FromSeconds(0.75).Ticks,
                GetTraceLong(fixture.Trace, "_transitionGameTimeTicks"));
        }

        [Fact]
        public void CompleteTransition_ShouldRecordCompletionBeforeStartupDeactivation()
        {
            var fixture = CreateCriticalPathFixture();
            using var manager = new StageManager(fixture.Game);
            SetCurrentStage(manager, StageType.Startup);
            var transition = new TestTransition(isComplete: false, fadeOutAlpha: 1.0f)
            {
                CompleteAfterUpdate = true
            };

            manager.ChangeStage(StageType.Title, transition);
            GetStages(manager)[StageType.Title] = new TestStage(StageType.Title)
            {
                StageManager = manager
            };
            manager.Update(0.25);

            var transitionComplete = GetTimestamp(
                fixture.Trace,
                StartupCriticalPathMilestone.TransitionComplete);
            var deactivateBegin = GetTimestamp(
                fixture.Trace,
                StartupCriticalPathMilestone.StartupDeactivateBegin);
            var deactivateEnd = GetTimestamp(
                fixture.Trace,
                StartupCriticalPathMilestone.StartupDeactivateEnd);
            Assert.True(transitionComplete < deactivateBegin);
            Assert.True(deactivateBegin < deactivateEnd);
        }

        [Fact]
        public void CompleteTransition_WhenTitleConstructsTwice_ShouldInvalidateTrace()
        {
            var fixture = CreateCriticalPathFixture();
            using var manager = new StageManager(fixture.Game);
            SetCurrentStage(manager, StageType.Startup);
            var transition = new TestTransition(isComplete: false, fadeOutAlpha: 1.0f)
            {
                CompleteAfterUpdate = true
            };

            manager.ChangeStage(StageType.Title, transition);
            GetStages(manager).Remove(StageType.Title);
            _ = Record.Exception(() => manager.Update(0.25));
            using var writer = new StringWriter();

            Assert.True(fixture.Trace.TryPublishTerminal(writer));
            Assert.Contains(
                "outcome=failure error=title_completion_cache_miss",
                writer.ToString());
        }

        [Fact]
        public void ChangeStage_WhenInitialStartupInstant_ShouldNotRecordStartupToTitleTransition()
        {
            var fixture = CreateCriticalPathFixture();
            using var manager = new StageManager(fixture.Game);
            RegisterStage(manager, new TestStage(StageType.Startup));

            manager.ChangeStage(StageType.Startup);

            Assert.False(
                WasRecorded(
                    fixture.Trace,
                    StartupCriticalPathMilestone.TransitionStart));
            Assert.False(
                WasRecorded(
                    fixture.Trace,
                    StartupCriticalPathMilestone.TransitionComplete));
        }

        private void SetCurrentStage(TestStage stage)
        {
            stage.StageManager = _stageManager;
            stage.Activate();
            ReflectionHelpers.SetPrivateField(_stageManager, "_currentStage", stage);
        }

        private static TestStage SetCurrentStage(
            StageManager manager,
            StageType stageType)
        {
            var stage = new TestStage(stageType)
            {
                StageManager = manager
            };
            stage.Activate();
            GetStages(manager)[stageType] = stage;
            ReflectionHelpers.SetPrivateField(manager, "_currentStage", stage);
            return stage;
        }

        private static void RegisterStage(StageManager manager, TestStage stage)
        {
            stage.StageManager = manager;
            GetStages(manager)[stage.Type] = stage;
        }

        private static Dictionary<StageType, IStage> GetStages(StageManager manager)
        {
            var stages = ReflectionHelpers.GetPrivateField<Dictionary<StageType, IStage>>(
                manager,
                "_stages");
            Assert.NotNull(stages);
            return stages!;
        }

        private static CriticalPathFixture CreateCriticalPathFixture()
        {
            var clock = new IncrementingMonotonicClock();
            var trace = StartupCriticalPathTrace.Start(
                clock,
                new FixedUtcMicrosecondClock(),
                entryTimestamp: 0,
                entryUnixMicroseconds: 1_000_000,
                exitAfterPublication: false);
            return new CriticalPathFixture(
                trace,
                clock,
                new CriticalPathHostStageGame(trace));
        }

        private static long GetTimestamp(
            StartupCriticalPathTrace trace,
            StartupCriticalPathMilestone milestone)
        {
            var timestamps = ReflectionHelpers.GetPrivateField<long[]>(
                trace,
                "_timestamps");
            Assert.NotNull(timestamps);
            return timestamps![(int)milestone];
        }

        private static bool WasRecorded(
            StartupCriticalPathTrace trace,
            StartupCriticalPathMilestone milestone)
        {
            var recorded = ReflectionHelpers.GetPrivateField<bool[]>(
                trace,
                "_recorded");
            Assert.NotNull(recorded);
            return recorded![(int)milestone];
        }

        private static bool GetTraceBoolean(
            StartupCriticalPathTrace trace,
            string fieldName) =>
            ReflectionHelpers.GetPrivateField<bool>(trace, fieldName);

        private static long GetTraceLong(
            StartupCriticalPathTrace trace,
            string fieldName) =>
            ReflectionHelpers.GetPrivateField<long>(trace, fieldName);

        private static void AssertTraceOpen(StartupCriticalPathTrace trace)
        {
            using var writer = new StringWriter();
            Assert.False(trace.TryPublishTerminal(writer));
            Assert.Equal(string.Empty, writer.ToString());
        }

        private sealed record CriticalPathFixture(
            StartupCriticalPathTrace Trace,
            IncrementingMonotonicClock Clock,
            CriticalPathHostStageGame Game);

        private sealed class IncrementingMonotonicClock : IMonotonicClock
        {
            private long _timestamp;

            public long TimestampFrequency => 1_000;
            public Action<long>? OnTimestamp { get; set; }

            public long GetTimestamp()
            {
                var timestamp = ++_timestamp;
                OnTimestamp?.Invoke(timestamp);
                return timestamp;
            }
        }

        private sealed class FixedUtcMicrosecondClock : IUtcMicrosecondClock
        {
            public long GetUnixMicroseconds() => 2_000_000;
        }

        private sealed class CriticalPathHostStageGame :
            IStageGame,
            IStartupCriticalPathHost
        {
            private readonly StartupCriticalPathTrace _trace;

            public CriticalPathHostStageGame(StartupCriticalPathTrace trace)
            {
                _trace = trace;
                ConfigManager = new ConfigManager();
            }

            StartupCriticalPathTrace? IStartupCriticalPathHost.StartupCriticalPathTrace =>
                _trace;

            public GraphicsDevice GraphicsDevice => null!;
            public IStageManager StageManager => null!;
            public IConfigManager ConfigManager { get; }
            public InputManagerCompat InputManager => null!;
            public IGraphicsManager GraphicsManager => null!;
            public IResourceManager ResourceManager => null!;
            public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;
            public bool CanPerformStageTransition() => false;
            public void MarkStageTransition() { }
            public Point? MapMouseToVirtual(Point windowPoint) => null;
            public ITextInputSource? GetTextInputSource() => null;
            public void RequestExit() { }
        }

        private sealed class RecordingBreadcrumbSink : ICrashBreadcrumbSink
        {
            private readonly List<CrashBreadcrumb> _events = new();

            public IReadOnlyList<CrashBreadcrumb> Events => _events;

            public void Record(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
            {
                _events.Add(new CrashBreadcrumb(
                    DateTimeOffset.UtcNow,
                    eventName,
                    properties ?? new Dictionary<string, object?>()));
            }
        }

        private sealed class RecordingContextSink : ICrashContextSink
        {
            private readonly List<CrashContextSnapshot> _snapshots = new();

            public IReadOnlyList<CrashContextSnapshot> Snapshots => _snapshots;

            public void SetSnapshot(CrashContextSnapshot snapshot)
            {
                _snapshots.Add(snapshot);
            }
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
