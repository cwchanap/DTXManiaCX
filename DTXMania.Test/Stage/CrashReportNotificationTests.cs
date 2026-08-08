#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public sealed class CrashReportNotificationTests
{
    // The banner lives in the top-right of the 1280x720 virtual canvas. The component owns its
    // exact geometry privately; tests click the well-known top-right banner area to open.
    private static readonly Point BannerClickPoint = new(1180, 40);
    private static readonly Point OffBannerPoint = new(10, 700);

    private static readonly KeyboardState NoKeys = default;
    private static readonly KeyboardState F8Down = new(new[] { Keys.F8 });
    private static readonly KeyboardState F8HeldFromPrev = new(new[] { Keys.F8 });

    // ---------------------------------------------------------------------------------------------
    // Step 1: component state
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ZeroReports_InitialState_ShouldBeClosedAndHidden()
    {
        var notification = new CrashReportNotification(CreateInbox());

        Assert.False(notification.IsOpen);
        Assert.False(notification.IsBannerVisible);
        Assert.Empty(notification.Reports);
    }

    [Fact]
    public void PendingReports_InitialState_ShouldShowBannerButStayClosed()
    {
        var notification = new CrashReportNotification(CreateInbox(
            Pending("report-1", capturedUtc: T(1))));

        Assert.False(notification.IsOpen);
        Assert.True(notification.IsBannerVisible);
    }

    [Fact]
    public void AcknowledgedOnly_InitialState_ShouldNotShowBannerButF8CanReviewRetained()
    {
        var notification = new CrashReportNotification(CreateInbox(Acknowledged("report-1", capturedUtc: T(1))));

        // No pending -> no banner...
        Assert.False(notification.IsBannerVisible);

        // ...but F8 still opens the panel so the player can review retained (acknowledged) items.
        var consumed = notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.True(consumed);
        Assert.True(notification.IsOpen);
        Assert.Single(notification.Reports);
    }

    [Fact]
    public void OpenPanel_WhenPendingReportsExist_ShouldDefaultToNewestPending()
    {
        var notification = new CrashReportNotification(CreateInbox(
            Pending("old-pending", capturedUtc: T(1)),
            Acknowledged("middle-ack", capturedUtc: T(2)),
            Pending("new-pending", capturedUtc: T(3))));

        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.True(notification.IsOpen);
        Assert.Equal("new-pending", notification.Reports[notification.SelectedIndex].Summary.ReportId);
    }

    [Fact]
    public void OpenPanel_WhenNoPending_ShouldDefaultToNewestRetained()
    {
        var notification = new CrashReportNotification(CreateInbox(
            Acknowledged("old-ack", capturedUtc: T(1)),
            Acknowledged("new-ack", capturedUtc: T(2))));

        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.True(notification.IsOpen);
        Assert.Equal("new-ack", notification.Reports[notification.SelectedIndex].Summary.ReportId);
    }

    [Fact]
    public void MoveLeft_AtStart_ShouldClamp()
    {
        var notification = new CrashReportNotification(CreateInbox(
            Pending("a", capturedUtc: T(1)),
            Pending("b", capturedUtc: T(2))));
        var input = CreateInput(InputCommandType.MoveLeft);

        notification.HandleInput(F8Down, NoKeys, input, null, false); // open -> newest (index 1)
        Assert.Equal(1, notification.SelectedIndex);

        notification.HandleInput(NoKeys, NoKeys, input, null, false); // MoveLeft -> index 0
        Assert.Equal(0, notification.SelectedIndex);

        notification.HandleInput(NoKeys, NoKeys, input, null, false); // clamp at 0
        Assert.Equal(0, notification.SelectedIndex);
    }

    [Fact]
    public void MoveRight_AtEnd_ShouldClamp()
    {
        var notification = new CrashReportNotification(CreateInbox(
            Pending("a", capturedUtc: T(1)),
            Pending("b", capturedUtc: T(2))));
        var input = CreateInput(InputCommandType.MoveRight);

        notification.HandleInput(F8Down, NoKeys, input, null, false); // open at newest (index 1)

        notification.HandleInput(NoKeys, NoKeys, input, null, false); // already at end -> clamp
        Assert.Equal(1, notification.SelectedIndex);
    }

    [Fact]
    public void SuccessfulDismiss_ShouldClosePanelAndRefreshSnapshot()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        // Dismiss success -> the inbox marks the report acknowledged on refresh.
        inboxMock.Setup(x => x.Dismiss(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports, AcknowledgedItem("report-1", capturedUtc: T(1))));
        var notification = new CrashReportNotification(inboxMock.Object);

        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        // Focus Dismiss (third action) then Activate.
        FocusAction(notification, actionIndex: 2);

        var consumed = Activate(notification);

        Assert.True(consumed);
        Assert.False(notification.IsOpen); // closed + refreshed
        Assert.True(notification.Reports[0].IsAcknowledged);
        Assert.False(notification.IsBannerVisible); // no longer pending
    }

    [Fact]
    public void Delete_RequiresConfirmationBeforeRemoving()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.Delete(It.IsAny<string>())).Returns(new CrashReportActionResult(Succeeded: true));
        var notification = new CrashReportNotification(inboxMock.Object);

        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 3); // Delete

        var firstActivate = Activate(notification);

        Assert.True(firstActivate);
        Assert.True(notification.IsDeleteConfirming); // confirmation now required
        Assert.Single(notification.Reports); // NOT deleted yet
    }

    [Fact]
    public void DeleteConfirm_WhenOthersRemain_ShouldSelectNearestAndClearConfirmation()
    {
        var (inboxMock, reports) = CreateInboxMock(
            Pending("a", capturedUtc: T(1)),
            Pending("b", capturedUtc: T(2)),
            Pending("c", capturedUtc: T(3)));
        // After deleting "c" (the selected newest), the snapshot drops it.
        inboxMock.Setup(x => x.Delete(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports,
                PendingItem("a", capturedUtc: T(1)),
                PendingItem("b", capturedUtc: T(2))));
        var notification = new CrashReportNotification(inboxMock.Object);

        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        // newest pending = "c" at index 2.
        Assert.Equal("c", notification.Reports[notification.SelectedIndex].Summary.ReportId);
        FocusAction(notification, actionIndex: 3); // Delete
        Activate(notification); // enter confirmation

        // Confirm with Activate.
        var consumed = Activate(notification);

        Assert.True(consumed);
        Assert.False(notification.IsDeleteConfirming);
        Assert.Equal(2, notification.Reports.Count);
        // Selection clamped to the new last remaining ("b").
        Assert.Equal("b", notification.Reports[notification.SelectedIndex].Summary.ReportId);
        Assert.True(notification.IsOpen);
    }

    [Fact]
    public void DeleteConfirm_WhenEmpty_ShouldClosePanel()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("only", capturedUtc: T(1)));
        inboxMock.Setup(x => x.Delete(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports));
        var notification = new CrashReportNotification(inboxMock.Object);

        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 3);
        Activate(notification); // confirm prompt
        var consumed = Activate(notification); // confirm delete

        Assert.True(consumed);
        Assert.False(notification.IsDeleteConfirming);
        Assert.Empty(notification.Reports);
        Assert.False(notification.IsOpen); // nothing left -> close
    }

    [Fact]
    public void DeleteConfirm_Back_ShouldCancelConfirmationWithoutClosingPanel()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.Delete(It.IsAny<string>())).Returns(new CrashReportActionResult(Succeeded: true));
        var notification = new CrashReportNotification(inboxMock.Object);

        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 3);
        Activate(notification); // enter confirmation
        Assert.True(notification.IsDeleteConfirming);

        var input = CreateBackInput();
        var consumed = notification.HandleInput(NoKeys, NoKeys, input, null, false);

        Assert.True(consumed);
        Assert.False(notification.IsDeleteConfirming); // cancelled
        Assert.True(notification.IsOpen); // panel stays open
        Assert.Single(notification.Reports); // nothing deleted
    }

    [Fact]
    public void ActionFailure_ShouldKeepPanelOpenAndExposeRetryableError()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.OpenGitHubIssue(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: false, ErrorCode: "launch_nonzero_exit"));
        var notification = new CrashReportNotification(inboxMock.Object);

        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 0); // GitHub
        var consumed = Activate(notification);

        Assert.True(consumed);
        Assert.True(notification.IsOpen); // still open / retryable
        Assert.False(string.IsNullOrEmpty(notification.ErrorText));
        Assert.False(notification.Reports[0].IsAcknowledged); // still pending
    }

    [Fact]
    public void ActionSuccess_AfterPriorFailure_ShouldClearError()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.OpenGitHubIssue(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: false, ErrorCode: "launch_nonzero_exit"));
        var notification = new CrashReportNotification(inboxMock.Object);

        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 0); // GitHub fails
        Activate(notification);
        Assert.False(string.IsNullOrEmpty(notification.ErrorText));

        // Now make the next GitHub attempt succeed and acknowledge.
        inboxMock.Setup(x => x.OpenGitHubIssue(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports, AcknowledgedItem("report-1", capturedUtc: T(1))));
        FocusAction(notification, actionIndex: 0);
        var consumed = Activate(notification);

        Assert.True(consumed);
        Assert.True(string.IsNullOrEmpty(notification.ErrorText)); // cleared on success
    }

    [Fact]
    public void DeleteFailure_ShouldKeepReportAndExposeError()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.Delete(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: false, ErrorCode: "delete_io_failure"));
        var notification = new CrashReportNotification(inboxMock.Object);

        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 3);
        Activate(notification); // confirm prompt
        var consumed = Activate(notification); // confirm -> fails

        Assert.True(consumed);
        Assert.False(notification.IsDeleteConfirming); // confirmation resolved either way
        Assert.True(notification.IsOpen);
        Assert.False(string.IsNullOrEmpty(notification.ErrorText));
        Assert.Single(notification.Reports); // still present
    }

    // ---------------------------------------------------------------------------------------------
    // Step 2: input ownership + raw F8
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void F8_IsEdgeTriggeredFromKeyboardSnapshotsAndNonRemappable()
    {
        var notification = new CrashReportNotification(CreateInbox(Pending("r", capturedUtc: T(1))));
        // InputManager reports NO commands (F8 is not an InputCommandType) -> F8 must still work
        // because it is read raw from the keyboard snapshots.
        var input = CreateInput();

        // Edge: current F8 down, previous not down -> consumed (opens).
        Assert.True(notification.HandleInput(F8Down, NoKeys, input, null, false));
        Assert.True(notification.IsOpen);

        // Close it so we can re-test the edge.
        notification.HandleInput(NoKeys, NoKeys, CreateBackInput(), null, false);
        Assert.False(notification.IsOpen);

        // Held (previous also down) -> NOT edge, NOT consumed.
        var consumed = notification.HandleInput(F8HeldFromPrev, F8HeldFromPrev, input, null, false);
        Assert.False(consumed);
        Assert.False(notification.IsOpen);
    }

    [Fact]
    public void F8_WithNoReports_ShouldReturnNotConsumed()
    {
        var notification = new CrashReportNotification(CreateInbox());

        var consumed = notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.False(consumed);
        Assert.False(notification.IsOpen);
    }

    [Fact]
    public void F8_OpensPanelAndReturnsConsumed()
    {
        var notification = new CrashReportNotification(CreateInbox(Pending("r", capturedUtc: T(1))));

        var consumed = notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.True(consumed);
        Assert.True(notification.IsOpen);
    }

    [Fact]
    public void BannerClick_OnBannerRegion_ShouldOpenAndConsume()
    {
        var notification = new CrashReportNotification(CreateInbox(Pending("r", capturedUtc: T(1))));

        var consumed = notification.HandleInput(NoKeys, NoKeys, inputManager: null, virtualMouse: BannerClickPoint, leftMouseClick: true);

        Assert.True(consumed);
        Assert.True(notification.IsOpen);
    }

    [Fact]
    public void BannerClick_OutsideBanner_ShouldReturnNotConsumed()
    {
        var notification = new CrashReportNotification(CreateInbox(Pending("r", capturedUtc: T(1))));

        var consumed = notification.HandleInput(NoKeys, NoKeys, inputManager: null, virtualMouse: OffBannerPoint, leftMouseClick: true);

        Assert.False(consumed);
        Assert.False(notification.IsOpen);
    }

    [Fact]
    public void Closed_WithNoF8AndNoClick_ShouldReturnNotConsumed()
    {
        var notification = new CrashReportNotification(CreateInbox(Pending("r", capturedUtc: T(1))));

        var consumed = notification.HandleInput(NoKeys, NoKeys, CreateInput(), virtualMouse: null, leftMouseClick: false);

        Assert.False(consumed);
    }

    [Fact]
    public void WhileOpen_AnyInputIsConsumed()
    {
        var notification = new CrashReportNotification(CreateInbox(Pending("r", capturedUtc: T(1))));
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        Assert.True(notification.IsOpen);

        // No meaningful input at all while open -> still consumed (panel owns input).
        var consumed = notification.HandleInput(NoKeys, NoKeys, CreateInput(), virtualMouse: null, leftMouseClick: false);

        Assert.True(consumed);
        Assert.True(notification.IsOpen);
    }

    [Fact]
    public void WhileOpen_MoveLeftConsumesAndAdvancesSelection()
    {
        var notification = new CrashReportNotification(CreateInbox(
            Pending("a", capturedUtc: T(1)),
            Pending("b", capturedUtc: T(2))));
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        Assert.Equal(1, notification.SelectedIndex);

        var consumed = notification.HandleInput(NoKeys, NoKeys, CreateInput(InputCommandType.MoveLeft), null, false);

        Assert.True(consumed);
        Assert.Equal(0, notification.SelectedIndex);
    }

    [Fact]
    public void WhileOpen_MoveUpDownCyclesActionFocus()
    {
        var notification = new CrashReportNotification(CreateInbox(Pending("r", capturedUtc: T(1))));
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        var initialFocus = notification.ActionFocus;

        notification.HandleInput(NoKeys, NoKeys, CreateInput(InputCommandType.MoveDown), null, false);
        var nextFocus = notification.ActionFocus;

        Assert.NotEqual(initialFocus, nextFocus);

        // MoveUp returns toward the first action (cycle).
        notification.HandleInput(NoKeys, NoKeys, CreateInput(InputCommandType.MoveUp), null, false);
        Assert.Equal(initialFocus, notification.ActionFocus);
    }

    [Fact]
    public void WhileOpen_BackClosesPanelAndConsumes()
    {
        var notification = new CrashReportNotification(CreateInbox(Pending("r", capturedUtc: T(1))));
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        Assert.True(notification.IsOpen);

        var consumed = notification.HandleInput(NoKeys, NoKeys, CreateBackInput(), null, false);

        Assert.True(consumed);
        Assert.False(notification.IsOpen);
    }

    [Fact]
    public void DeleteConfirmation_ConsumesActivateUntilResolved()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("r", capturedUtc: T(1)));
        inboxMock.Setup(x => x.Delete(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 3);
        Activate(notification); // enter confirmation

        // Navigation inputs while confirming are still consumed but do not move selection.
        var navConsumed = notification.HandleInput(
            NoKeys, NoKeys, CreateInput(InputCommandType.MoveLeft), null, false);

        Assert.True(navConsumed);
        Assert.True(notification.IsDeleteConfirming);

        // Activate confirms and resolves.
        var confirmConsumed = Activate(notification);
        Assert.True(confirmConsumed);
        Assert.False(notification.IsDeleteConfirming);
    }

    [Fact]
    public void DeleteConfirmation_MouseClickOnDelete_ShouldConfirmDelete()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("r", capturedUtc: T(1)));
        inboxMock.Setup(x => x.Delete(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 3);
        Activate(notification); // arms confirmation
        Assert.True(notification.IsDeleteConfirming);

        // A mouse click on the Delete button confirms (same as Activate).
        var deleteCenter = ActionButtonCenter(actionIndex: 3);
        var consumed = notification.HandleInput(
            NoKeys, NoKeys, inputManager: null, virtualMouse: deleteCenter, leftMouseClick: true);

        Assert.True(consumed);
        Assert.False(notification.IsDeleteConfirming); // resolved
        Assert.False(notification.IsOpen); // empty after delete -> closed
    }

    [Fact]
    public void DeleteConfirmation_MouseClickOutsideActionRow_ShouldCancelConfirmation()
    {
        var notification = new CrashReportNotification(CreateInbox(Pending("r", capturedUtc: T(1))));
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 3);
        Activate(notification); // arms confirmation
        Assert.True(notification.IsDeleteConfirming);

        // A click inside the panel but outside the action row cancels the confirmation.
        var consumed = notification.HandleInput(
            NoKeys, NoKeys, inputManager: null, virtualMouse: new Point(200, 200), leftMouseClick: true);

        Assert.True(consumed);
        Assert.False(notification.IsDeleteConfirming);
        Assert.True(notification.IsOpen); // still open, report retained
        Assert.Single(notification.Reports);
    }

    [Fact]
    public void LaunchAction_ShouldRunAsynchronouslyAndResolveOnTheGameThread()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.OpenGitHubIssue(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports, AcknowledgedItem("report-1", capturedUtc: T(1))));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 0);

        // Start the launch WITHOUT the Activate helper's auto-resolve so the pending task is
        // observable: the launch-backed action runs off the game thread so the bounded macOS wait
        // cannot block HandleInput.
        notification.HandleInput(
            NoKeys, NoKeys, CreateInput(InputCommandType.Activate), null, false);
        Assert.True(notification.IsLaunchPending);

        notification.WaitForLaunchAndResolve();

        Assert.False(notification.IsLaunchPending);
        Assert.True(notification.Reports[0].IsAcknowledged);
        Assert.True(string.IsNullOrEmpty(notification.ErrorText));
    }

    [Fact]
    public void OpenPanel_RefreshesSnapshotFromInbox()
    {
        // A report captured after construction must appear when the panel is opened (F8 refreshes).
        var (inboxMock, reports) = CreateInboxMock(Pending("first", capturedUtc: T(1)));
        var notification = new CrashReportNotification(inboxMock.Object);
        Assert.Single(notification.Reports);

        SetReports(reports, Pending("first", capturedUtc: T(1)), Pending("second", capturedUtc: T(2)));

        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.Equal(2, notification.Reports.Count);
    }

    // ---------------------------------------------------------------------------------------------
    // Constructor guard
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Constructor_WithNullInbox_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new CrashReportNotification(null!));
    }

    // ---------------------------------------------------------------------------------------------
    // F8 while open, folder action, mouse-click actions, error code mapping
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void F8_WhilePanelOpen_ShouldRefreshSnapshotAndResetSelection()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("old", capturedUtc: T(1)));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        Assert.True(notification.IsOpen);

        // A new report appears between frames; F8 while open refreshes and selects the newest pending.
        SetReports(reports, Pending("old", capturedUtc: T(1)), Pending("new", capturedUtc: T(2)));
        var consumed = notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        Assert.True(consumed);
        Assert.True(notification.IsOpen);
        Assert.Equal(2, notification.Reports.Count);
        Assert.Equal("new", notification.Reports[notification.SelectedIndex].Summary.ReportId);
    }

    [Fact]
    public void OpenReportFolder_WhenLaunchSucceeds_ShouldAcknowledgeAndKeepPanelOpen()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.OpenReportFolder(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports, AcknowledgedItem("report-1", capturedUtc: T(1))));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 1); // Open report folder

        var consumed = Activate(notification);

        Assert.True(consumed);
        Assert.True(notification.IsOpen); // stays open for review
        Assert.True(notification.Reports[0].IsAcknowledged);
        Assert.True(string.IsNullOrEmpty(notification.ErrorText));
    }

    [Fact]
    public void OpenReportFolder_WhenLaunchFails_ShouldKeepPanelOpenAndExposeError()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.OpenReportFolder(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: false, ErrorCode: "launch_platform_unsupported"));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 1);

        var consumed = Activate(notification);

        Assert.True(consumed);
        Assert.True(notification.IsOpen);
        Assert.False(notification.Reports[0].IsAcknowledged);
        Assert.Equal("Cannot open on this platform.", notification.ErrorText);
    }

    [Fact]
    public void Dismiss_WhenInboxFails_ShouldKeepPanelOpenAndExposeError()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.Dismiss(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: false, ErrorCode: "acknowledge_io_failure"));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 2); // Dismiss

        var consumed = Activate(notification);

        Assert.True(consumed);
        Assert.True(notification.IsOpen);
        Assert.Equal("Could not save acknowledgement.", notification.ErrorText);
    }

    [Fact]
    public void MouseClick_OnActionButton_ShouldFocusAndInvokeInOneMotion()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.OpenGitHubIssue(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports, AcknowledgedItem("report-1", capturedUtc: T(1))));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        // Click the first action button (GitHub) at its centre.
        var actionCenter = ActionButtonCenter(actionIndex: 0);
        var consumed = notification.HandleInput(
            NoKeys, NoKeys, inputManager: null, virtualMouse: actionCenter, leftMouseClick: true);
        notification.WaitForLaunchAndResolve();

        Assert.True(consumed);
        Assert.Equal(0, notification.ActionFocus);
        Assert.True(notification.Reports[0].IsAcknowledged);
    }

    [Fact]
    public void MouseClick_OffActionButtons_ShouldConsumeButNotInvoke()
    {
        var notification = new CrashReportNotification(CreateInbox(Pending("report-1", capturedUtc: T(1))));
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        // Click somewhere inside the panel but not on any action button.
        var consumed = notification.HandleInput(
            NoKeys, NoKeys, inputManager: null, virtualMouse: new Point(200, 200), leftMouseClick: true);

        Assert.True(consumed); // panel still owns input
        Assert.True(notification.IsOpen);
        Assert.False(notification.Reports[0].IsAcknowledged); // nothing invoked
    }

    [Theory]
    [InlineData("report_not_found", "Report no longer available.")]
    [InlineData("launch_platform_unsupported", "Cannot open on this platform.")]
    [InlineData("launch_target_rejected", "Cannot open on this platform.")]
    [InlineData("launch_start_failed", "Could not open the external handler.")]
    [InlineData("launch_process_null", "Could not open the external handler.")]
    [InlineData("launch_nonzero_exit", "Could not open the external handler.")]
    [InlineData("launch_timeout", "Could not open the external handler.")]
    [InlineData("acknowledge_io_failure", "Could not save acknowledgement.")]
    [InlineData("delete_io_failure", "Could not delete the report file.")]
    [InlineData("inbox_unexpected_failure", "Unexpected inbox error.")]
    [InlineData(null, "Action failed.")]
    [InlineData("", "Action failed.")]
    public void SetError_WithKnownCode_ShouldMapToShortMessage(string? code, string expected)
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.OpenGitHubIssue(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: false, ErrorCode: code));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 0);

        Activate(notification);

        Assert.Equal(expected, notification.ErrorText);
    }

    [Fact]
    public void SetError_WithUnknownCode_ShouldTrimToEightyCharacters()
    {
        var longCode = new string('x', 120);
        var (inboxMock, reports) = CreateInboxMock(Pending("report-1", capturedUtc: T(1)));
        inboxMock.Setup(x => x.OpenGitHubIssue(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: false, ErrorCode: longCode));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        FocusAction(notification, actionIndex: 0);

        Activate(notification);

        Assert.Equal(80, notification.ErrorText!.Length);
        Assert.Equal(new string('x', 80), notification.ErrorText);
    }

    [Fact]
    public void Constructor_WhenInboxThrowsOnGetReports_ShouldStartWithEmptySnapshot()
    {
        var inboxMock = new Mock<ICrashReportInbox>(MockBehavior.Loose);
        inboxMock.Setup(x => x.GetReports()).Throws(new InvalidOperationException("inbox unavailable"));

        var notification = new CrashReportNotification(inboxMock.Object);

        Assert.False(notification.IsOpen);
        Assert.Empty(notification.Reports);
        Assert.False(notification.IsBannerVisible);
    }

    [Fact]
    public void Constructor_WhenInboxReturnsNull_ShouldStartWithEmptySnapshot()
    {
        var inboxMock = new Mock<ICrashReportInbox>(MockBehavior.Loose);
        inboxMock.Setup(x => x.GetReports()).Returns((IReadOnlyList<CrashReportInboxItem>)null!);

        var notification = new CrashReportNotification(inboxMock.Object);

        Assert.Empty(notification.Reports);
        Assert.False(notification.IsBannerVisible);
    }

    [Fact]
    public void RefreshAfterAction_WhenSelectedReportIsGone_ShouldDefaultToNewestPending()
    {
        var (inboxMock, reports) = CreateInboxMock(
            Pending("a", capturedUtc: T(1)),
            Pending("b", capturedUtc: T(2)));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        // Default selection = newest pending = "b" at index 1.
        Assert.Equal("b", notification.Reports[notification.SelectedIndex].Summary.ReportId);

        // GitHub succeeds and the snapshot drops "b" entirely (e.g. deleted externally).
        inboxMock.Setup(x => x.OpenGitHubIssue(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports,
                PendingItem("a", capturedUtc: T(1)),
                PendingItem("c", capturedUtc: T(3))));
        FocusAction(notification, actionIndex: 0);
        Activate(notification);

        // "b" is gone -> selection falls back to the newest pending ("c").
        Assert.Equal("c", notification.Reports[notification.SelectedIndex].Summary.ReportId);
    }

    [Fact]
    public void RefreshAfterAction_WhenNoSelection_ShouldDefaultToNewestPending()
    {
        var (inboxMock, reports) = CreateInboxMock(Pending("a", capturedUtc: T(1)));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);

        // GitHub succeeds and the snapshot replaces the single report with a new pending one.
        inboxMock.Setup(x => x.OpenGitHubIssue(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports, PendingItem("b", capturedUtc: T(2))));
        FocusAction(notification, actionIndex: 0);
        Activate(notification);

        // The previously selected id "a" is gone; the refresh falls back to the newest pending.
        Assert.Equal("b", notification.Reports[notification.SelectedIndex].Summary.ReportId);
    }

    [Fact]
    public void MoveSelection_WithNoReports_ShouldBeNoOp()
    {
        // The notification owns its own snapshot, captured at OpenPanel time. The only flow that
        // refreshes the snapshot AND leaves the panel open is a successful launch
        // (ApplyLaunchResult -> RefreshSnapshotPreservingSelection), so drive the empty-while-open
        // state by emptying the inbox from inside the launch callback. The Delete path closes the
        // panel when it empties, so it cannot reach this state.
        var (inboxMock, reports) = CreateInboxMock(Pending("a", capturedUtc: T(1)));
        inboxMock.Setup(x => x.OpenGitHubIssue(It.IsAny<string>()))
            .Returns(new CrashReportActionResult(Succeeded: true))
            .Callback<string>(_ => SetReports(reports));
        var notification = new CrashReportNotification(inboxMock.Object);
        notification.HandleInput(F8Down, NoKeys, inputManager: null, virtualMouse: null, leftMouseClick: false);
        // Default action focus is OpenGitHubIssue (index 0); Activate fires the launch, then the
        // resolve refreshes the snapshot to empty while keeping the panel open.
        Activate(notification);

        // Confirm the snapshot is genuinely empty before exercising the guard.
        Assert.Empty(notification.Reports);
        Assert.True(notification.IsOpen);

        // MoveLeft with zero reports should not throw and should not change state.
        var consumed = notification.HandleInput(
            NoKeys, NoKeys, CreateInput(InputCommandType.MoveLeft), null, false);

        Assert.True(consumed);
        Assert.True(notification.IsOpen);
        Assert.Empty(notification.Reports);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static bool Activate(CrashReportNotification notification)
    {
        var consumed = notification.HandleInput(
            NoKeys, NoKeys, CreateInput(InputCommandType.Activate), null, false);
        // Launch-backed actions run off the game thread; resolve the result deterministically so
        // tests can assert the post-action state. A no-op for Dismiss/Delete (no pending task).
        notification.WaitForLaunchAndResolve();
        return consumed;
    }

    private static void FocusAction(CrashReportNotification notification, int actionIndex)
    {
        // Drive MoveUp/MoveDown until the desired action focus is reached. MoveDown advances focus
        // by one (mod action count); starting from the default first action, MoveDown * actionIndex
        // lands on the target.
        int moves = actionIndex % 4;
        for (int i = 0; i < moves; i++)
        {
            notification.HandleInput(
                NoKeys, NoKeys, CreateInput(InputCommandType.MoveDown), null, false);
        }

        Assert.Equal(actionIndex, notification.ActionFocus);
    }

    private static DateTimeOffset T(int seconds) =>
        new DateTimeOffset(2026, 1, 1, 0, 0, second: seconds, TimeSpan.Zero);

    /// <summary>
    /// Computes the centre of the action button at the given index, matching the private
    /// <c>GetActionRects</c> geometry (buttonWidth=220, buttonHeight=32, gap=12, centred in the
    /// 960-wide panel at x=160).
    /// </summary>
    private static Point ActionButtonCenter(int actionIndex)
    {
        const int buttonWidth = 220;
        const int buttonHeight = 32;
        const int gap = 12;
        const int panelX = 160;
        const int panelWidth = 960;
        const int panelBottom = 90 + 540;
        int totalWidth = 4 * buttonWidth + 3 * gap;
        int startX = panelX + (panelWidth - totalWidth) / 2;
        int y = panelBottom - buttonHeight - 24;
        return new Point(startX + actionIndex * (buttonWidth + gap) + buttonWidth / 2, y + buttonHeight / 2);
    }

    private static CrashReportInboxItem Pending(string id, DateTimeOffset capturedUtc) =>
        PendingItem(id, capturedUtc);

    private static CrashReportInboxItem Acknowledged(string id, DateTimeOffset capturedUtc) =>
        AcknowledgedItem(id, capturedUtc);

    internal static CrashReportInboxItem PendingItem(string id, DateTimeOffset capturedUtc) =>
        new(Summary(id, capturedUtc), IsAcknowledged: false);

    internal static CrashReportInboxItem AcknowledgedItem(string id, DateTimeOffset capturedUtc) =>
        new(Summary(id, capturedUtc), IsAcknowledged: true);

    private static CrashReportSummary Summary(string id, DateTimeOffset capturedUtc) =>
        new(
            ReportId: id,
            CapturedAtUtc: capturedUtc,
            BuildId: "build-1",
            OperatingSystem: "macOS",
            ProcessArchitecture: "arm64",
            StageOrMilestone: "Performance",
            ExceptionType: "System.InvalidOperationException",
            FileName: id + ".txt");

    /// <summary>
    /// Creates a loose <see cref="Mock{ICrashReportInbox}"/> with all action methods defaulting
    /// to <c>Succeeded: true</c> and <see cref="GetReports"/> returning a mutable list. Tests that
    /// need to change action results or post-action report snapshots re-setup the relevant method
    /// with <c>.Returns(...).Callback(...)</c>; tests that need to replace the report set call
    /// <see cref="SetReports"/> on the returned list.
    /// </summary>
    private static (Mock<ICrashReportInbox> mock, List<CrashReportInboxItem> reports) CreateInboxMock(
        params CrashReportInboxItem[] initialReports)
    {
        var reports = initialReports.ToList();
        var mock = new Mock<ICrashReportInbox>(MockBehavior.Loose);
        mock.Setup(x => x.GetReports()).Returns(reports);
        mock.Setup(x => x.OpenGitHubIssue(It.IsAny<string>())).Returns(new CrashReportActionResult(Succeeded: true));
        mock.Setup(x => x.OpenReportFolder(It.IsAny<string>())).Returns(new CrashReportActionResult(Succeeded: true));
        mock.Setup(x => x.Dismiss(It.IsAny<string>())).Returns(new CrashReportActionResult(Succeeded: true));
        mock.Setup(x => x.Delete(It.IsAny<string>())).Returns(new CrashReportActionResult(Succeeded: true));
        return (mock, reports);
    }

    /// <summary>
    /// Convenience wrapper for tests that only need the inbox object and never modify state.
    /// </summary>
    private static ICrashReportInbox CreateInbox(params CrashReportInboxItem[] reports) =>
        CreateInboxMock(reports).mock.Object;

    /// <summary>
    /// Replaces the contents of a shared report list in place so the mock's <see cref="GetReports"/>
    /// setup observes the new snapshot on the next call.
    /// </summary>
    private static void SetReports(List<CrashReportInboxItem> reports, params CrashReportInboxItem[] newReports)
    {
        reports.Clear();
        reports.AddRange(newReports);
    }

    /// <summary>
    /// Creates a loose <see cref="Mock{IInputManager}"/> with the given commands configured as
    /// pressed. All other members return their default values (false / null), matching the
    /// behaviour of the former hand-rolled fake.
    /// </summary>
    private static IInputManager CreateInput(params InputCommandType[] commands)
    {
        var mock = new Mock<IInputManager>(MockBehavior.Loose);
        foreach (var command in commands)
            mock.Setup(x => x.IsCommandPressed(command)).Returns(true);
        return mock.Object;
    }

    /// <summary>
    /// Creates a loose <see cref="Mock{IInputManager}"/> with the back action triggered.
    /// </summary>
    private static IInputManager CreateBackInput()
    {
        var mock = new Mock<IInputManager>(MockBehavior.Loose);
        mock.Setup(x => x.IsBackActionTriggered()).Returns(true);
        return mock.Object;
    }
}
