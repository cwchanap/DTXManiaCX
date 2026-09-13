#nullable enable

using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Update;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public sealed class GameUpdateNotificationTests
{
    // The banner sits directly below the crash banner in the top-right of the 1280x720 virtual
    // canvas. The component owns its exact geometry privately; tests click the well-known area.
    private static readonly Point BannerClickPoint = new(1102, 108);
    private static readonly Point OffBannerPoint = new(10, 700);

    private static readonly KeyboardState NoKeys = default;
    private static readonly KeyboardState F9Down = new(new[] { Keys.F9 });

    // ---------------------------------------------------------------------------------------------
    // Component state
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void InitialState_WhenNotChecked_ShouldBeClosedAndHidden()
    {
        var notification = new GameUpdateNotification(CreateService(GameUpdateSnapshot.NotChecked));

        Assert.False(notification.IsOpen);
        Assert.False(notification.IsBannerVisible);
        Assert.Null(notification.StatusText);
    }

    [Fact]
    public void InitialState_WhenAvailable_ShouldShowBannerButStayClosed()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Available)));

        Assert.False(notification.IsOpen);
        Assert.True(notification.IsBannerVisible);
    }

    // ---------------------------------------------------------------------------------------------
    // Opening: raw edge-triggered F9 (non-remappable) and the banner click
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ClosedBanner_WhenF9Edge_ShouldOpenPanelAndConsumeFrame()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Available)));

        var consumed = notification.HandleInput(F9Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.True(consumed);
        Assert.True(notification.IsOpen);
    }

    [Theory]
    [InlineData(GameUpdateState.NotChecked)]
    [InlineData(GameUpdateState.UpToDate)]
    public void ClosedBanner_WhenF9EdgeButNothingToReview_ShouldConsumeNothing(GameUpdateState state)
    {
        var notification = new GameUpdateNotification(CreateService(Snap(state)));

        var consumed = notification.HandleInput(F9Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.False(consumed);
        Assert.False(notification.IsOpen);
    }

    [Fact]
    public void ClosedBanner_WhenF9HeldFromPreviousFrame_ShouldNotOpen()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Available)));

        var consumed = notification.HandleInput(F9Down, F9Down, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.False(consumed); // non-edge: held key must not reopen; frame is not consumed
        Assert.False(notification.IsOpen);
    }

    [Fact]
    public void ClosedBanner_WhenNoF9_ShouldNotOpen()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Available)));

        var consumed = notification.HandleInput(NoKeys, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.False(consumed);
        Assert.False(notification.IsOpen);
    }

    [Fact]
    public void ClosedBanner_WhenBannerClick_ShouldOpenPanelAndConsumeFrame()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Available)));

        var consumed = notification.HandleInput(NoKeys, NoKeys, inputManager: null, virtualMouse: BannerClickPoint, leftMouseClick: true);

        Assert.True(consumed);
        Assert.True(notification.IsOpen);
    }

    [Fact]
    public void ClosedBanner_WhenClickOutsideBanner_ShouldConsumeNothing()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Available)));

        var consumed = notification.HandleInput(NoKeys, NoKeys, inputManager: null, virtualMouse: OffBannerPoint, leftMouseClick: true);

        Assert.False(consumed);
        Assert.False(notification.IsOpen);
    }

    // ---------------------------------------------------------------------------------------------
    // Closed banner consumes nothing (required regression: title Activate must still work)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ClosedBanner_WhenActivatePressed_ShouldConsumeNothing()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Available)));

        var consumed = notification.HandleInput(NoKeys, NoKeys, CreateInput(InputCommandType.Activate), null, false);

        Assert.False(consumed);
        Assert.False(notification.IsOpen);
    }

    // ---------------------------------------------------------------------------------------------
    // Open panel: Back closes while idle (Available) and failed
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void OpenPanel_WhenBackWhileAvailable_ShouldCloseAndConsumeFrame()
    {
        var notification = CreateOpenNotification(Snap(GameUpdateState.Available));

        var consumed = notification.HandleInput(NoKeys, NoKeys, CreateBackInput(), null, false);

        Assert.True(consumed);
        Assert.False(notification.IsOpen);
    }

    [Fact]
    public void OpenPanel_WhenBackWhileFailed_ShouldCloseAndConsumeFrame()
    {
        var notification = CreateOpenNotification(Snap(GameUpdateState.Failed));

        var consumed = notification.HandleInput(NoKeys, NoKeys, CreateBackInput(), null, false);

        Assert.True(consumed);
        Assert.False(notification.IsOpen);
    }

    // ---------------------------------------------------------------------------------------------
    // Open panel: remappable actions (Update Now / Later; Retry / Later)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void OpenPanel_WhenUpdateNowActivated_ShouldBeginUpdateAndClose()
    {
        var (service, notification) = CreateOpenTracked(Snap(GameUpdateState.Available));

        var consumed = Activate(notification);

        Assert.True(consumed);
        service.Verify(x => x.BeginUpdate(), Times.Once);
        service.Verify(x => x.DismissForProcess(), Times.Never);
        Assert.False(notification.IsOpen); // the downloading status banner takes over
    }

    [Fact]
    public void OpenPanel_WhenRetryActivatedAfterFailure_ShouldBeginUpdateAndClose()
    {
        var (service, notification) = CreateOpenTracked(Snap(GameUpdateState.Failed));

        var consumed = Activate(notification);

        Assert.True(consumed);
        service.Verify(x => x.BeginUpdate(), Times.Once);
        Assert.False(notification.IsOpen);
    }

    [Fact]
    public void OpenPanel_WhenLaterActivated_ShouldDismissForProcessAndClose()
    {
        var (service, notification) = CreateOpenTracked(Snap(GameUpdateState.Available));
        notification.HandleInput(NoKeys, NoKeys, CreateInput(InputCommandType.MoveRight), null, false); // focus Later
        Assert.Equal(1, notification.ActionFocus);

        var consumed = Activate(notification);

        Assert.True(consumed);
        service.Verify(x => x.DismissForProcess(), Times.Once);
        service.Verify(x => x.BeginUpdate(), Times.Never);
        Assert.False(notification.IsOpen);
    }

    [Fact]
    public void OpenPanel_WhenLaterActivatedAfterFailure_ShouldCloseWithoutOfferingRetry()
    {
        var (service, notification) = CreateOpenTracked(Snap(GameUpdateState.Failed));
        notification.HandleInput(NoKeys, NoKeys, CreateInput(InputCommandType.MoveRight), null, false); // focus Later

        var consumed = Activate(notification);

        Assert.True(consumed);
        service.Verify(x => x.BeginUpdate(), Times.Never); // "Later" must not retry
        Assert.False(notification.IsOpen);
    }

    [Fact]
    public void OpenPanel_WhenMoveLeft_ShouldFocusPrimaryAction()
    {
        var notification = CreateOpenNotification(Snap(GameUpdateState.Available));

        notification.HandleInput(NoKeys, NoKeys, CreateInput(InputCommandType.MoveRight), null, false);
        notification.HandleInput(NoKeys, NoKeys, CreateInput(InputCommandType.MoveLeft), null, false);

        Assert.Equal(0, notification.ActionFocus);
    }

    // ---------------------------------------------------------------------------------------------
    // Downloading / InstallerLaunched: prevent Start/Config activation; Back falls through
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Downloading_WhenActivatePressed_ShouldConsumeFrame()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Downloading, percent: 43)));

        var consumed = notification.HandleInput(NoKeys, NoKeys, CreateInput(InputCommandType.Activate), null, false);

        Assert.True(consumed);
    }

    [Fact]
    public void Downloading_WhenF9Edge_ShouldConsumeButStayClosed()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Downloading, percent: 43)));

        var consumed = notification.HandleInput(F9Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.True(consumed);
        Assert.False(notification.IsOpen); // nothing to review while downloading; no cancel flow
    }

    [Fact]
    public void Downloading_WhenBackPressed_ShouldFallThroughToTitleExitPath()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Downloading, percent: 43)));

        var consumed = notification.HandleInput(NoKeys, NoKeys, CreateBackInput(), null, false);

        // Back is deliberately NOT consumed: the title's exit path stays reachable.
        Assert.False(consumed);
    }

    [Fact]
    public void InstallerLaunched_WhenAnyInput_ShouldConsumeExceptBack()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.InstallerLaunched)));

        Assert.True(notification.HandleInput(NoKeys, NoKeys, CreateInput(InputCommandType.Activate), null, false));
        Assert.False(notification.HandleInput(NoKeys, NoKeys, CreateBackInput(), null, false));
    }

    // ---------------------------------------------------------------------------------------------
    // Status text: DOWNLOADING UPDATE — vX.Y.Z (N%) — percent omitted when unknown
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void StatusText_WhenDownloadingWithKnownPercent_ShouldIncludePercent()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Downloading, percent: 43)));

        Assert.Equal("DOWNLOADING UPDATE — 1.2.3 (43%)", notification.StatusText);
    }

    [Fact]
    public void StatusText_WhenDownloadingWithoutPercent_ShouldOmitPercent()
    {
        var notification = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Downloading)));

        Assert.Equal("DOWNLOADING UPDATE — 1.2.3", notification.StatusText);
    }

    [Fact]
    public void StatusText_WhenNotDownloading_ShouldBeNull()
    {
        var available = new GameUpdateNotification(CreateService(Snap(GameUpdateState.Available)));
        var launched = new GameUpdateNotification(CreateService(Snap(GameUpdateState.InstallerLaunched)));

        Assert.Null(available.StatusText);
        Assert.Null(launched.StatusText);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static GameUpdateSnapshot Snap(GameUpdateState state, string version = "1.2.3", int? percent = null) =>
        new(state, version, "https://example.com/installer.exe", "sha256:" + new string('a', 64), null, percent);

    private static Mock<IGameUpdateService> CreateServiceMock(GameUpdateSnapshot snapshot)
    {
        var mock = new Mock<IGameUpdateService>(MockBehavior.Loose);
        mock.Setup(x => x.GetSnapshot()).Returns(snapshot);
        return mock;
    }

    private static IGameUpdateService CreateService(GameUpdateSnapshot snapshot) =>
        CreateServiceMock(snapshot).Object;

    private static GameUpdateNotification CreateOpenNotification(GameUpdateSnapshot snapshot)
    {
        var notification = new GameUpdateNotification(CreateService(snapshot));
        notification.HandleInput(F9Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        Assert.True(notification.IsOpen);
        return notification;
    }

    private static (Mock<IGameUpdateService> Service, GameUpdateNotification Notification) CreateOpenTracked(
        GameUpdateSnapshot snapshot)
    {
        var service = CreateServiceMock(snapshot);
        var notification = new GameUpdateNotification(service.Object);
        notification.HandleInput(F9Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        Assert.True(notification.IsOpen);
        return (service, notification);
    }

    private static bool Activate(GameUpdateNotification notification) =>
        notification.HandleInput(NoKeys, NoKeys, CreateInput(InputCommandType.Activate), null, false);

    private static IInputManager CreateInput(params InputCommandType[] commands)
    {
        var mock = new Mock<IInputManager>(MockBehavior.Loose);
        foreach (var command in commands)
        {
            mock.Setup(x => x.IsCommandPressed(command)).Returns(true);
        }

        return mock.Object;
    }

    private static IInputManager CreateBackInput()
    {
        var mock = new Mock<IInputManager>(MockBehavior.Loose);
        mock.Setup(x => x.IsBackActionTriggered()).Returns(true);
        return mock.Object;
    }
}
