using System.IO;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
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
            var backgroundTextureObj = backgroundTexture.Object;
            resourceManager.Setup(x => x.TryLoadTexture(TexturePath.TitleBackground, out backgroundTextureObj)).Returns(true);

            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, TexturePath.TitleBackground);
            var payload = new SongPlayRequest(new SongListNode { Title = "Song" }, 2);

            stage.Activate(payload);

            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
            Assert.True(stage.IsActive);
            Assert.Equal(1, stage.ActivateCalls);
            Assert.True(stage.BackgroundReady);
            Assert.Same(payload, stage.LastPayload);
            resourceManager.Verify(x => x.TryLoadTexture(TexturePath.TitleBackground, out It.Ref<ITexture>.IsAny), Times.Once);
        }

        [Fact]
        public void Activate_WhenAlreadyActive_ShouldIgnoreSecondActivation()
        {
            var resourceManager = new Mock<IResourceManager>();
            var anyTexture = new Mock<ITexture>().Object;
            resourceManager.Setup(x => x.TryLoadTexture(It.IsAny<string>(), out anyTexture)).Returns(true);
            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, TexturePath.TitleBackground);

            stage.Activate();
            stage.Activate();

            Assert.Equal(1, stage.ActivateCalls);
            resourceManager.Verify(x => x.TryLoadTexture(TexturePath.TitleBackground, out It.Ref<ITexture>.IsAny), Times.Once);
        }

        [Fact]
        public void Activate_WhenBackgroundLoadFails_ShouldStillInvokeActivationHook()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.TryLoadTexture(It.IsAny<string>(), out It.Ref<ITexture>.IsAny)).Throws(new InvalidOperationException("boom"));
            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, TexturePath.TitleBackground);

            stage.Activate();

            Assert.Equal(StagePhase.FadeIn, stage.CurrentPhase);
            Assert.Equal(1, stage.ActivateCalls);
            Assert.False(stage.BackgroundReady);
        }

        [Fact]
        public void Update_ShouldInvokeFirstUpdateOnlyOnceAndCallUpdateEachFrame()
        {
            var resourceManager = new Mock<IResourceManager>();
            var anyTexture = new Mock<ITexture>().Object;
            resourceManager.Setup(x => x.TryLoadTexture(It.IsAny<string>(), out anyTexture)).Returns(true);
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
        public void Deactivate_ShouldCleanupBackgroundResetStateAndClearPayload()
        {
            var resourceManager = new Mock<IResourceManager>();
            var backgroundTexture = new Mock<ITexture>();
            var backgroundTextureObj = backgroundTexture.Object;
            resourceManager.Setup(x => x.TryLoadTexture(It.IsAny<string>(), out backgroundTextureObj)).Returns(true);

            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, TexturePath.TitleBackground);
            stage.Activate(new SongPlayRequest(new SongListNode { Title = "Song" }, 0));

            stage.Deactivate();

            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            Assert.False(stage.IsActive);
            Assert.Equal(1, stage.DeactivateCalls);
            Assert.Null(stage.LastPayload);
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
        public void ChangeStage_WithPayloadAndNullTransition_ShouldUseInstantTransition()
        {
            var stageManager = new Mock<IStageManager>();
            var stage = new TestStage(ReflectionHelpers.CreateGame(new Mock<IResourceManager>().Object), StageType.Title, null)
            {
                StageManager = stageManager.Object,
            };
            var payload = new SongPlayRequest(new SongListNode { Title = "Song" }, 1);

            stage.ForwardChangeStage(StageType.SongTransition, null!, payload);

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.Is<IStageTransition>(transition => transition is InstantTransition),
                    payload),
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
            var anyTexture = new Mock<ITexture>().Object;
            resourceManager.Setup(x => x.TryLoadTexture(It.IsAny<string>(), out anyTexture)).Returns(true);
            var stage = new TestStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Title, TexturePath.TitleBackground);

            stage.Activate();
            stage.Deactivate();
            stage.Activate();

            resourceManager.Verify(x => x.TryLoadTexture(TexturePath.TitleBackground, out It.Ref<ITexture>.IsAny), Times.Exactly(2));
        }

        [Fact]
        public void Dispose_WhenActive_ShouldDeactivateAndSuppressPhase()
        {
            var resourceManager = new Mock<IResourceManager>();
            var anyTexture = new Mock<ITexture>().Object;
            resourceManager.Setup(x => x.TryLoadTexture(It.IsAny<string>(), out anyTexture)).Returns(true);
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
            var expectedTexture = new Mock<ITexture>().Object;
            resourceManager.Setup(x => x.TryLoadTexture(expectedPath, out expectedTexture)).Returns(true);
            // PassiveStage does not override GetBackgroundTexturePath, exercising the base switch
            var stage = new PassiveStage(ReflectionHelpers.CreateGame(resourceManager.Object), stageType);

            stage.Activate();

            resourceManager.Verify(x => x.TryLoadTexture(expectedPath, out It.Ref<ITexture>.IsAny), Times.Once);
        }

        [Fact]
        public void GetBackgroundTexturePath_Config_ShouldReturnNull_NoLoadAttempted()
        {
            var resourceManager = new Mock<IResourceManager>();
            var stage = new PassiveStage(ReflectionHelpers.CreateGame(resourceManager.Object), StageType.Config);

            stage.Activate();

            resourceManager.Verify(x => x.TryLoadTexture(It.IsAny<string>(), out It.Ref<ITexture>.IsAny), Times.Never);
        }

        [Fact]
        public void FullLifecycle_WithBaseNoOpHooks_ShouldNotThrow()
        {
            var resourceManager = new Mock<IResourceManager>();
            var anyTexture = new Mock<ITexture>().Object;
            resourceManager.Setup(x => x.TryLoadTexture(It.IsAny<string>(), out anyTexture)).Returns(true);
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

            public StageTransitionPayload? LastPayload => _transitionPayload;

            public void ForwardChangeStage(StageType stageType, IStageTransition? transition = null)
            {
                ChangeStage(stageType, transition);
            }

            public void ForwardChangeStage(StageType stageType, IStageTransition? transition, StageTransitionPayload payload)
            {
                ChangeStage(stageType, transition!, payload);
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
