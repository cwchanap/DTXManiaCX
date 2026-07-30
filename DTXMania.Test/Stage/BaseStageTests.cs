using System.IO;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
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
using Moq;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class BaseStageTests
    {
        [Fact]
        public void Activate_ShouldSetPhaseLoadBackgroundAndInvokeHook()
        {
            var resourceManager = new Mock<IResourceManager>();
            var backgroundTexture = new Mock<ITexture>();
            resourceManager.Setup(x => x.LoadTexture(TexturePath.TitleBackground)).Returns(backgroundTexture.Object);

            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, TexturePath.TitleBackground);
            var sharedData = new Dictionary<string, object> { ["songId"] = 42 };

            stage.Activate(sharedData);

            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
            Assert.True(stage.IsActive);
            Assert.Equal(1, stage.ActivateCalls);
            Assert.True(stage.BackgroundReady);
            Assert.Equal(42, stage.ReadSharedData("songId", -1));
            resourceManager.Verify(x => x.LoadTexture(TexturePath.TitleBackground), Times.Once);
        }

        [Fact]
        public void Activate_WhenAlreadyActive_ShouldIgnoreSecondActivation()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns(new Mock<ITexture>().Object);
            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, TexturePath.TitleBackground);

            stage.Activate();
            stage.Activate();

            Assert.Equal(1, stage.ActivateCalls);
            resourceManager.Verify(x => x.LoadTexture(TexturePath.TitleBackground), Times.Once);
        }

        [Fact]
        public void Activate_WhenBackgroundLoadFails_ShouldStillInvokeActivationHook()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Throws(new InvalidOperationException("boom"));
            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, TexturePath.TitleBackground);

            stage.Activate();

            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
            Assert.Equal(1, stage.ActivateCalls);
            Assert.False(stage.BackgroundReady);
        }

        [Fact]
        public void Activate_WhenStartup_ShouldBeginAfterInactiveGuardAndEndAfterOnActivate()
        {
            var fixture = CreateCriticalPathFixture();
            var stage = new TestStage(
                fixture.Game,
                StageType.Startup,
                backgroundPath: null)
            {
                ActivateAction = () => fixture.Clock.Timestamp = 20
            };
            fixture.Clock.Timestamp = 10;

            stage.Activate();

            Assert.Equal(
                11,
                GetTimestamp(
                    fixture.Trace,
                    StartupCriticalPathMilestone.StartupActivateBegin));
            Assert.Equal(
                21,
                GetTimestamp(
                    fixture.Trace,
                    StartupCriticalPathMilestone.StartupActivateEnd));
            Assert.Equal(1, stage.ActivateCalls);
        }

        [Fact]
        public void Activate_WhenTitle_ShouldIncludeBackgroundAndOnActivate()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(manager => manager.LoadTexture(TexturePath.TitleBackground))
                .Returns(new Mock<ITexture>().Object);
            var fixture = CreateCriticalPathFixture(resourceManager.Object);
            var stage = new TestStage(
                fixture.Game,
                StageType.Title,
                TexturePath.TitleBackground)
            {
                ActivateAction = () => fixture.Clock.Timestamp = 20
            };

            stage.Activate();

            var activationBegin = GetTimestamp(
                fixture.Trace,
                StartupCriticalPathMilestone.TitleActivateBegin);
            var activationEnd = GetTimestamp(
                fixture.Trace,
                StartupCriticalPathMilestone.TitleActivateEnd);
            Assert.True(activationBegin < GetAggregateBegin(
                fixture.Trace,
                StartupCriticalPathAggregate.TitleBackground));
            Assert.True(GetAggregateBegin(
                    fixture.Trace,
                    StartupCriticalPathAggregate.TitleBackground) <
                activationEnd);
            Assert.Equal(
                1,
                GetAggregateTicks(
                    fixture.Trace,
                    StartupCriticalPathAggregate.TitleBackground));
            Assert.Equal(21, activationEnd);
            Assert.Equal(1, stage.ActivateCalls);
        }

        [Fact]
        public void Activate_WhenAlreadyActive_ShouldNotRecordAnotherActivation()
        {
            var fixture = CreateCriticalPathFixture();
            var stage = new TestStage(
                fixture.Game,
                StageType.Startup,
                backgroundPath: null);

            stage.Activate();
            stage.Activate();

            Assert.True(WasRecorded(
                fixture.Trace,
                StartupCriticalPathMilestone.StartupActivateBegin));
            Assert.True(WasRecorded(
                fixture.Trace,
                StartupCriticalPathMilestone.StartupActivateEnd));
            AssertTraceOpen(fixture.Trace);
            Assert.Equal(1, stage.ActivateCalls);
        }

        [Fact]
        public void LoadStageBackground_WhenTitleLoadThrows_ShouldCloseMeasuredSpan()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(manager => manager.LoadTexture(TexturePath.TitleBackground))
                .Throws(new InvalidOperationException("background failed"));
            var fixture = CreateCriticalPathFixture(resourceManager.Object);
            var stage = new TestStage(
                fixture.Game,
                StageType.Title,
                TexturePath.TitleBackground);

            var exception = Record.Exception(stage.Activate);

            Assert.Null(exception);
            Assert.False(IsAggregateActive(
                fixture.Trace,
                StartupCriticalPathAggregate.TitleBackground));
            Assert.Equal(
                1,
                GetAggregateTicks(
                    fixture.Trace,
                    StartupCriticalPathAggregate.TitleBackground));
            Assert.Equal(1, stage.ActivateCalls);
        }

        [Fact]
        public void Update_WhenStartupFirstCall_ShouldRecordWholeFirstUpdateOnce()
        {
            var fixture = CreateCriticalPathFixture();
            var updateAttempt = 0;
            var stage = new TestStage(
                fixture.Game,
                StageType.Startup,
                backgroundPath: null)
            {
                UpdateAction = () =>
                {
                    updateAttempt++;
                    if (updateAttempt == 1)
                    {
                        fixture.Clock.Timestamp = 110;
                        return;
                    }

                    throw new InvalidOperationException("update failed");
                }
            };
            stage.Activate();
            fixture.Clock.Timestamp = 100;

            stage.Update(0.016);
            fixture.Clock.Timestamp = 200;
            Assert.Throws<InvalidOperationException>(() => stage.Update(0.016));

            Assert.Equal(
                101,
                GetTimestamp(
                    fixture.Trace,
                    StartupCriticalPathMilestone.StartupFirstUpdateBegin));
            Assert.Equal(
                111,
                GetTimestamp(
                    fixture.Trace,
                    StartupCriticalPathMilestone.StartupFirstUpdateEnd));
            Assert.Equal(1, stage.FirstUpdateCalls);
            Assert.Equal(2, stage.UpdateCalls);
            Assert.Equal(
                2,
                ReflectionHelpers.GetPrivateField<long>(
                    fixture.Trace,
                    "_startupUpdateCount"));
            Assert.Equal(
                TimeSpan.FromSeconds(0.032).Ticks,
                ReflectionHelpers.GetPrivateField<long>(
                    fixture.Trace,
                    "_startupGameTimeTicks"));
            AssertTraceOpen(fixture.Trace);
        }

        [Fact]
        public void Update_WhenTitleFirstCall_ShouldRecordWholeFirstUpdateOnce()
        {
            var fixture = CreateCriticalPathFixture();
            var stage = new TestStage(
                fixture.Game,
                StageType.Title,
                backgroundPath: null)
            {
                UpdateAction = () => fixture.Clock.Timestamp = 110
            };
            stage.Activate();
            fixture.Clock.Timestamp = 100;

            stage.Update(0.016);
            fixture.Clock.Timestamp = 200;
            stage.Update(0.016);

            Assert.Equal(
                101,
                GetTimestamp(
                    fixture.Trace,
                    StartupCriticalPathMilestone.TitleFirstUpdateBegin));
            Assert.Equal(
                111,
                GetTimestamp(
                    fixture.Trace,
                    StartupCriticalPathMilestone.TitleFirstUpdateEnd));
            Assert.Equal(1, stage.FirstUpdateCalls);
            Assert.Equal(2, stage.UpdateCalls);
            AssertTraceOpen(fixture.Trace);
        }

        [Fact]
        public void Draw_WhenStartupFirstCall_ShouldRecordWholeFirstDrawOnce()
        {
            var fixture = CreateCriticalPathFixture();
            var stage = new TestStage(
                fixture.Game,
                StageType.Startup,
                backgroundPath: null)
            {
                DrawAction = () => fixture.Clock.Timestamp = 110
            };
            stage.Activate();
            fixture.Clock.Timestamp = 100;

            stage.Draw(0.016);
            fixture.Clock.Timestamp = 200;
            stage.Draw(0.016);

            Assert.Equal(
                101,
                GetTimestamp(
                    fixture.Trace,
                    StartupCriticalPathMilestone.StartupFirstDrawBegin));
            Assert.Equal(
                111,
                GetTimestamp(
                    fixture.Trace,
                    StartupCriticalPathMilestone.StartupFirstDrawEnd));
            Assert.Equal(2, stage.DrawCalls);
            AssertTraceOpen(fixture.Trace);
        }

        [Fact]
        public void Draw_WhenStartupCompletes_ShouldCountDrawsUntilSummaryClosesWindow()
        {
            var fixture = CreateCriticalPathFixture();
            var drawAttempt = 0;
            var stage = new TestStage(
                fixture.Game,
                StageType.Startup,
                backgroundPath: null)
            {
                DrawAction = () =>
                {
                    drawAttempt++;
                    if (drawAttempt == 2)
                        throw new InvalidOperationException("draw failed");
                }
            };
            stage.Activate();

            stage.Draw(0.016);
            Assert.Throws<InvalidOperationException>(() => stage.Draw(0.016));
            stage.Draw(0.016);
            fixture.Trace.RecordExactlyOnce(
                StartupCriticalPathMilestone.SummaryRequest);
            stage.Draw(0.016);

            Assert.Equal(
                2,
                ReflectionHelpers.GetPrivateField<long>(
                    fixture.Trace,
                    "_startupDrawCount"));
            Assert.Equal(4, stage.DrawCalls);
        }

        [Fact]
        public void Update_ShouldInvokeFirstUpdateOnlyOnceAndCallUpdateEachFrame()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns(new Mock<ITexture>().Object);
            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, TexturePath.TitleBackground);
            stage.Activate();

            stage.Update(0.016);
            stage.Update(0.016);

            Assert.Equal(1, stage.FirstUpdateCalls);
            Assert.Equal(2, stage.UpdateCalls);
        }

        [Fact]
        public void Draw_WhenInactive_ShouldNotInvokeDrawHook()
        {
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null);

            stage.Draw(0.016);

            Assert.Equal(0, stage.DrawCalls);
        }

        [Fact]
        public void Deactivate_ShouldCleanupBackgroundResetStateAndClearSharedData()
        {
            var resourceManager = new Mock<IResourceManager>();
            var backgroundTexture = new Mock<ITexture>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns(backgroundTexture.Object);

            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, TexturePath.TitleBackground);
            stage.Activate(new Dictionary<string, object> { ["songId"] = 7 });

            stage.Deactivate();

            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            Assert.False(stage.IsActive);
            Assert.Equal(1, stage.DeactivateCalls);
            Assert.False(stage.ContainsSharedData("songId"));
            backgroundTexture.Verify(x => x.RemoveReference(), Times.Once);
        }

        [Fact]
        public void TransitionLifecycle_ShouldUpdatePhaseAndInvokeHooks()
        {
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null);
            var fadeTransition = new FadeTransition();
            var crossfadeTransition = new CrossfadeTransition();

            stage.OnTransitionIn(fadeTransition);
            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
            Assert.Equal(1, stage.TransitionInCalls);
            Assert.Same(fadeTransition, stage.LastTransition);

            stage.OnTransitionOut(crossfadeTransition);
            Assert.Equal(StagePhase.FadeOut, stage.CurrentPhase);
            Assert.Equal(1, stage.TransitionOutCalls);
            Assert.Same(crossfadeTransition, stage.LastTransition);

            stage.OnTransitionComplete();
            Assert.Equal(StagePhase.Normal, stage.CurrentPhase);
            Assert.Equal(1, stage.TransitionCompleteCalls);
        }

        [Fact]
        public void SharedDataHelpers_ShouldReturnDefaultsForMissingOrInvalidValues()
        {
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null);

            stage.WriteSharedData("difficulty", "hard");

            Assert.True(stage.ContainsSharedData("difficulty"));
            Assert.Equal("hard", stage.ReadSharedData("difficulty", string.Empty));
            Assert.Equal(3, stage.ReadSharedData("missing", 3));
            Assert.Equal(9, stage.ReadSharedData("difficulty", 9));
        }

        [Fact]
        public void ChangeStage_ShouldForwardDefaultInstantTransition()
        {
            var stageManager = new Mock<IStageManager>();
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null)
            {
                StageManager = stageManager.Object,
            };

            stage.ForwardChangeStage(StageType.Result);

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Result,
                    It.Is<IStageTransition>(transition => transition is InstantTransition)),
                Times.Once);
        }

        [Fact]
        public void ChangeStage_WithSharedDataAndNullTransition_ShouldUseInstantTransition()
        {
            var stageManager = new Mock<IStageManager>();
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null)
            {
                StageManager = stageManager.Object,
            };
            var sharedData = new Dictionary<string, object> { ["mode"] = "preview" };

            stage.ForwardChangeStage(StageType.SongTransition, null!, sharedData);

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.Is<IStageTransition>(transition => transition is InstantTransition),
                    sharedData),
                Times.Once);
        }

        [Fact]
        public void ChangeStage_WhenStageManagerIsNull_ShouldNotThrow()
        {
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null);
            // StageManager defaults to null — the guarded null-conditional call must be a no-op
            var ex = Record.Exception(() => stage.ForwardChangeStage(StageType.Result));
            Assert.Null(ex);
        }

        [Fact]
        public void Deactivate_WhenAlreadyInactive_ShouldBeNoOp()
        {
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null);

            stage.Deactivate();

            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            Assert.Equal(0, stage.DeactivateCalls);
        }

        [Fact]
        public void Update_WhenInactive_ShouldNotInvokeHooks()
        {
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null);

            stage.Update(0.016);

            Assert.Equal(0, stage.UpdateCalls);
            Assert.Equal(0, stage.FirstUpdateCalls);
        }

        [Fact]
        public void Draw_WhenActive_ShouldInvokeDrawHook()
        {
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null);
            stage.Activate();

            stage.Draw(0.016);

            Assert.Equal(1, stage.DrawCalls);
        }

        [Fact]
        public void Activate_AfterDeactivate_ShouldReloadBackground()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns(new Mock<ITexture>().Object);
            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, TexturePath.TitleBackground);

            stage.Activate();
            stage.Deactivate();
            stage.Activate();

            resourceManager.Verify(x => x.LoadTexture(TexturePath.TitleBackground), Times.Exactly(2));
        }

        [Fact]
        public void Dispose_WhenActive_ShouldDeactivateAndSuppressPhase()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns(new Mock<ITexture>().Object);
            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, null);
            stage.Activate();

            stage.Dispose();

            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            Assert.Equal(1, stage.DeactivateCalls);
        }

        [Fact]
        public void Dispose_WhenInactive_ShouldNotThrow()
        {
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null);

            var ex = Record.Exception(() => stage.Dispose());

            Assert.Null(ex);
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        [Fact]
        public void SharedDataHelpers_EmptyKey_ShouldBeGuarded()
        {
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null);
            stage.Activate();

            // SetSharedData with empty key should be silently ignored
            stage.WriteSharedData("", "ignored");
            Assert.False(stage.ContainsSharedData(""));

            // GetSharedData with empty key should return default
            Assert.Equal(-1, stage.ReadSharedData("", -1));

            // HasSharedData with null key should return false (null is IsNullOrEmpty)
            Assert.False(stage.ContainsSharedData(null!));
        }

        [Fact]
        public void SharedDataHelpers_BeforeActivation_ShouldReturnDefault()
        {
            // _sharedData is null before any Activate call
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null);

            Assert.Equal(-1, stage.ReadSharedData("missing", -1));
            Assert.False(stage.ContainsSharedData("missing"));
        }

        [Theory]
        [InlineData(StageType.Startup, TexturePath.StartupBackground)]
        [InlineData(StageType.Title, TexturePath.TitleBackground)]
        [InlineData(StageType.SongSelect, TexturePath.SongSelectionBackground)]
        [InlineData(StageType.SongTransition, TexturePath.SongTransitionBackground)]
        [InlineData(StageType.Performance, TexturePath.PerformanceBackground)]
        [InlineData(StageType.Result, TexturePath.ResultBackground)]
        public void GetBackgroundTexturePath_BaseSwitch_ShouldLoadCorrectTexture(StageType stageType, string expectedPath)
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(expectedPath)).Returns(new Mock<ITexture>().Object);
            // PassiveStage does not override GetBackgroundTexturePath, exercising the base switch
            var stage = new PassiveStage(ReflectionHelpers.CreateGame(resourceManager.Object), stageType);

            stage.Activate();

            resourceManager.Verify(x => x.LoadTexture(expectedPath), Times.Once);
        }

        [Fact]
        public void GetBackgroundTexturePath_Config_ShouldReturnNull_NoLoadAttempted()
        {
            var resourceManager = new Mock<IResourceManager>();
            var stage = new PassiveStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Config);

            stage.Activate();

            resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void Activate_WhenCriticalPathHostGetterThrows_ShouldSwallowAndContinueActivation()
        {
            // ResolveCriticalPathTrace wraps StartupCriticalPathHost.Resolve in a try/catch so a
            // throwing host getter degrades to a null trace rather than failing activation. The
            // Title stage still activates and loads its background.
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns(new Mock<ITexture>().Object);
            var game = new ThrowingCriticalPathHostStageGame(resourceManager.Object);
            var stage = new TestStage(game, StageType.Title, TexturePath.TitleBackground);

            var exception = Record.Exception(() => stage.Activate());

            Assert.Null(exception);
            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
            Assert.True(stage.BackgroundReady);
        }

        [Fact]
        public void FullLifecycle_WithBaseNoOpHooks_ShouldNotThrow()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns(new Mock<ITexture>().Object);
            // PassiveStage exercises all base-class virtual no-op hooks
            var stage = new PassiveStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title);

            stage.Activate();
            stage.Update(0.016); // OnFirstUpdate + OnUpdate
            stage.Update(0.016); // OnUpdate
            stage.Draw(0.016);   // OnDraw
            stage.OnTransitionIn(new FadeTransition());      // OnTransitionInStarted
            stage.OnTransitionOut(new CrossfadeTransition()); // OnTransitionOutStarted
            stage.OnTransitionComplete();                     // OnTransitionCompleted
            stage.Deactivate();                              // OnDeactivate

            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        private static CriticalPathFixture CreateCriticalPathFixture(
            IResourceManager? resourceManager = null)
        {
            var clock = new ManualMonotonicClock();
            var trace = StartupCriticalPathTrace.Start(
                clock,
                new FixedUtcMicrosecondClock(),
                entryTimestamp: 0,
                entryUnixMicroseconds: 1_000_000,
                exitAfterPublication: false);
            var manager = resourceManager ?? new Mock<IResourceManager>().Object;
            return new CriticalPathFixture(
                trace,
                clock,
                new CriticalPathHostStageGame(trace, manager));
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

        private static long GetAggregateBegin(
            StartupCriticalPathTrace trace,
            StartupCriticalPathAggregate aggregate)
        {
            var timestamps = ReflectionHelpers.GetPrivateField<long[]>(
                trace,
                "_aggregateBeginTimestamps");
            Assert.NotNull(timestamps);
            return timestamps![(int)aggregate];
        }

        private static long GetAggregateTicks(
            StartupCriticalPathTrace trace,
            StartupCriticalPathAggregate aggregate)
        {
            var ticks = ReflectionHelpers.GetPrivateField<long[]>(
                trace,
                "_aggregateTimestampTicks");
            Assert.NotNull(ticks);
            return ticks![(int)aggregate];
        }

        private static bool IsAggregateActive(
            StartupCriticalPathTrace trace,
            StartupCriticalPathAggregate aggregate)
        {
            var active = ReflectionHelpers.GetPrivateField<bool[]>(
                trace,
                "_aggregateActive");
            Assert.NotNull(active);
            return active![(int)aggregate];
        }

        private static void AssertTraceOpen(StartupCriticalPathTrace trace)
        {
            using var writer = new StringWriter();
            Assert.False(trace.TryPublishTerminal(writer));
            Assert.Equal(string.Empty, writer.ToString());
        }

        private sealed record CriticalPathFixture(
            StartupCriticalPathTrace Trace,
            ManualMonotonicClock Clock,
            CriticalPathHostStageGame Game);

        private sealed class ManualMonotonicClock : IMonotonicClock
        {
            public long TimestampFrequency => 1_000;
            public long Timestamp { get; set; }
            public long GetTimestamp() => ++Timestamp;
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

            public CriticalPathHostStageGame(
                StartupCriticalPathTrace trace,
                IResourceManager resourceManager)
            {
                _trace = trace;
                ResourceManager = resourceManager;
                ConfigManager = new ConfigManager();
            }

            StartupCriticalPathTrace? IStartupCriticalPathHost.StartupCriticalPathTrace =>
                _trace;

            public GraphicsDevice GraphicsDevice => null!;
            public IStageManager StageManager => null!;
            public IConfigManager ConfigManager { get; }
            public InputManagerCompat InputManager => null!;
            public IGraphicsManager GraphicsManager => null!;
            public IResourceManager ResourceManager { get; }
            public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;
            public bool CanPerformStageTransition() => false;
            public void MarkStageTransition() { }
            public Point? MapMouseToVirtual(Point windowPoint) => null;
            public ITextInputSource? GetTextInputSource() => null;
            public void RequestExit() { }
        }

        /// <summary>
        /// <see cref="IStageGame"/> + <see cref="IStartupCriticalPathHost"/> whose trace getter
        /// throws, used to exercise <see cref="BaseStage"/>'s defensive catch in
        /// <c>ResolveCriticalPathTrace</c>.
        /// </summary>
        private sealed class ThrowingCriticalPathHostStageGame :
            IStageGame,
            IStartupCriticalPathHost
        {
            public ThrowingCriticalPathHostStageGame(IResourceManager resourceManager)
            {
                ResourceManager = resourceManager;
                ConfigManager = new ConfigManager();
            }

            StartupCriticalPathTrace? IStartupCriticalPathHost.StartupCriticalPathTrace =>
                throw new InvalidOperationException("trace resolution failed");

            public GraphicsDevice GraphicsDevice => null!;
            public IStageManager StageManager => null!;
            public IConfigManager ConfigManager { get; }
            public InputManagerCompat InputManager => null!;
            public IGraphicsManager GraphicsManager => null!;
            public IResourceManager ResourceManager { get; }
            public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;
            public bool CanPerformStageTransition() => false;
            public void MarkStageTransition() { }
            public Point? MapMouseToVirtual(Point windowPoint) => null;
            public ITextInputSource? GetTextInputSource() => null;
            public void RequestExit() { }
        }

        /// <summary>
        /// Minimal stage that relies on all base-class virtual no-op hook implementations.
        /// </summary>
        private sealed class PassiveStage : BaseStage
        {
            private readonly StageType _type;

            public PassiveStage(IStageGame game, StageType type) : base(game)
            {
                _type = type;
            }

            public override StageType Type => _type;
            // Intentionally does not override GetBackgroundTexturePath or any hook —
            // this is what exercises the base-class switch and the no-op virtuals.
        }

        private sealed class TestStage : BaseStage
        {
            private readonly StageType _type;
            private readonly string? _backgroundPath;

            public TestStage(
                IStageGame game,
                StageType type,
                string? backgroundPath)
                : base(game)
            {
                _type = type;
                _backgroundPath = backgroundPath;
            }

            public override StageType Type => _type;

            public int ActivateCalls { get; private set; }
            public int DeactivateCalls { get; private set; }
            public int FirstUpdateCalls { get; private set; }
            public int UpdateCalls { get; private set; }
            public int DrawCalls { get; private set; }
            public int TransitionInCalls { get; private set; }
            public int TransitionOutCalls { get; private set; }
            public int TransitionCompleteCalls { get; private set; }
            public IStageTransition? LastTransition { get; private set; }
            public bool BackgroundReady => IsBackgroundReady;
            public Action? ActivateAction { get; init; }
            public Action? UpdateAction { get; init; }
            public Action? DrawAction { get; init; }

            public T ReadSharedData<T>(string key, T defaultValue)
            {
                return GetSharedData(key, defaultValue);
            }

            public void WriteSharedData(string key, object value)
            {
                SetSharedData(key, value);
            }

            public bool ContainsSharedData(string key)
            {
                return HasSharedData(key);
            }

            public void ForwardChangeStage(StageType stageType, IStageTransition? transition = null)
            {
                ChangeStage(stageType, transition);
            }

            public void ForwardChangeStage(StageType stageType, IStageTransition? transition, Dictionary<string, object> sharedData)
            {
                ChangeStage(stageType, transition!, sharedData);
            }

            protected override string? GetBackgroundTexturePath()
            {
                return _backgroundPath;
            }

            protected override void OnActivate()
            {
                ActivateCalls++;
                ActivateAction?.Invoke();
            }

            protected override void OnDeactivate()
            {
                DeactivateCalls++;
            }

            protected override void OnFirstUpdate(double deltaTime)
            {
                FirstUpdateCalls++;
            }

            protected override void OnUpdate(double deltaTime)
            {
                UpdateCalls++;
                UpdateAction?.Invoke();
            }

            protected override void OnDraw(double deltaTime)
            {
                DrawCalls++;
                DrawAction?.Invoke();
            }

            protected override void OnTransitionInStarted(IStageTransition transition)
            {
                TransitionInCalls++;
                LastTransition = transition;
            }

            protected override void OnTransitionOutStarted(IStageTransition transition)
            {
                TransitionOutCalls++;
                LastTransition = transition;
            }

            protected override void OnTransitionCompleted()
            {
                TransitionCompleteCalls++;
            }
        }
    }
}
