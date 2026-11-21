using System;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public class BaseStageExceptionalTeardownTests
{
    [Theory]
    [InlineData(StageType.Title)]
    [InlineData(StageType.Result)]
    public void NormalTeardown_ShouldReleaseBackgroundOnce(StageType type)
    {
        var background = new Mock<ITexture>();
        var stage = CreateStage(type, background.Object);
        stage.Activate();

        stage.Deactivate();
        stage.Deactivate();

        Assert.False(stage.IsActive);
        Assert.Equal(1, stage.DeactivateCalls);
        background.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Theory]
    [InlineData(StageType.Title)]
    [InlineData(StageType.Result)]
    public void ThrowingHook_ShouldReleaseBackgroundPreserveFirstFailureAndAllowRetry(StageType type)
    {
        var background = new Mock<ITexture>();
        var first = new InvalidOperationException("stage teardown");
        var second = new InvalidOperationException("background teardown");
        background.Setup(x => x.RemoveReference()).Throws(second);
        var stage = CreateStage(type, background.Object);
        stage.DeactivateAction = () => { if (stage.DeactivateCalls == 1) throw first; };
        stage.Activate();

        var caught = Assert.Throws<InvalidOperationException>(stage.Deactivate);

        Assert.Same(first, caught);
        Assert.Contains(nameof(ThrowingHook_ShouldReleaseBackgroundPreserveFirstFailureAndAllowRetry), caught.StackTrace);
        Assert.True(stage.IsActive);
        background.Verify(x => x.RemoveReference(), Times.Once);
        stage.Deactivate();
        Assert.False(stage.IsActive);
        Assert.Equal(2, stage.DeactivateCalls);
        background.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HookReactivation_ShouldKeepFreshBackgroundAndRethrowOldFailure(bool throws)
    {
        var old = new Mock<ITexture>();
        var fresh = new Mock<ITexture>();
        var stage = CreateStage(StageType.Title, old.Object, fresh.Object);
        var failure = new InvalidOperationException("old hook");
        stage.DeactivateAction = () =>
        {
            if (stage.DeactivateCalls != 1) return;
            stage.Deactivate();
            stage.Activate();
            if (throws) throw failure;
        };
        stage.Activate();

        if (throws) Assert.Same(failure, Assert.Throws<InvalidOperationException>(stage.Deactivate));
        else stage.Deactivate();

        Assert.True(stage.IsActive);
        Assert.True(stage.BackgroundReady);
        old.Verify(x => x.RemoveReference(), Times.Once);
        fresh.Verify(x => x.RemoveReference(), Times.Never);
        stage.Deactivate();
        fresh.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void BackgroundReleaseReactivation_ShouldPreserveFreshRunAndOldHookFailure()
    {
        var old = new Mock<ITexture>();
        var fresh = new Mock<ITexture>();
        var stage = CreateStage(StageType.Result, old.Object, fresh.Object);
        var failure = new InvalidOperationException("old hook");
        stage.DeactivateAction = () => { if (stage.DeactivateCalls == 1) throw failure; };
        old.Setup(x => x.RemoveReference()).Callback(() =>
        {
            stage.Deactivate();
            stage.Activate();
        });
        stage.Activate();

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(stage.Deactivate));

        Assert.True(stage.IsActive);
        Assert.True(stage.BackgroundReady);
        old.Verify(x => x.RemoveReference(), Times.Once);
        fresh.Verify(x => x.RemoveReference(), Times.Never);
        stage.Deactivate();
        fresh.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void BackgroundWorkRetirementReactivation_ShouldNotTearDownFreshRun()
    {
        var old = new Mock<ITexture>();
        var fresh = new Mock<ITexture>();
        var stage = CreateStage(StageType.Title, old.Object, fresh.Object);
        stage.Activate();
        stage.BackgroundWork!.Post(() => { }, () =>
        {
            stage.Deactivate();
            stage.Activate();
        });

        stage.Deactivate();

        Assert.True(stage.IsActive);
        Assert.Equal(1, stage.DeactivateCalls);
        old.Verify(x => x.RemoveReference(), Times.Once);
        fresh.Verify(x => x.RemoveReference(), Times.Never);
        stage.Deactivate();
        fresh.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void DisposeWhenDeactivateThrows_ShouldMarkDisposedAndSkipRetryOnSecondDispose()
    {
        var background = new Mock<ITexture>();
        var failure = new InvalidOperationException("teardown failed");
        var stage = CreateStage(StageType.Title, background.Object);
        stage.DeactivateAction = () => throw failure;
        stage.Activate();

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(stage.Dispose));

        Assert.Equal(1, stage.DeactivateCalls);
        background.Verify(x => x.RemoveReference(), Times.Once);
        stage.Dispose();
        Assert.Equal(1, stage.DeactivateCalls);
    }

    private delegate void TextureCallback(string path, out ITexture texture);

    private static TestStage CreateStage(StageType type, params ITexture[] textures)
    {
        var resources = new Mock<IResourceManager>();
        var game = new Mock<IStageGame>();
        game.SetupGet(x => x.ResourceManager).Returns(resources.Object);
        int index = 0;
        resources.Setup(x => x.TryLoadTexture(It.IsAny<string>(), out It.Ref<ITexture>.IsAny))
            .Callback(new TextureCallback((string _, out ITexture texture) => texture = textures[index++]))
            .Returns(true);
        return new TestStage(game.Object, type);
    }

    private sealed class TestStage(IStageGame game, StageType type) : BaseStage(game)
    {
        public override StageType Type => type;
        public Action? DeactivateAction { get; set; }
        public int DeactivateCalls { get; private set; }
        public bool BackgroundReady => IsBackgroundReady;
        protected override void OnDeactivate()
        {
            DeactivateCalls++;
            DeactivateAction?.Invoke();
        }
    }
}
