#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Focused title-stage component that surfaces captured crash reports to the player. It owns a
    /// snapshot of the inbox plus its own open/closed state, selection, action focus, delete
    /// confirmation and bounded error text. Drawing is split from state/input so the pure logic is
    /// unit-testable with a fake <see cref="ICrashReportInbox"/> and no graphics device.
    ///
    /// The single input-ownership seam is <see cref="HandleInput"/>: when it returns true the title
    /// stage must NOT run its own menu/exit path for that frame (this is what keeps an open panel's
    /// Back/Escape from reaching the title's <c>RequestExit</c> path). F8 is a fixed, raw,
    /// edge-triggered, non-remappable title shortcut read directly from the polled keyboard
    /// snapshots; no <see cref="InputCommandType"/> is added for it.
    /// </summary>
    public sealed class CrashReportNotification
    {
        private const int ActionCount = 4;

        /// <summary>
        /// The four review actions, addressed by name rather than bare index. <see cref="ActionCount"/>
        /// is preserved for iteration/bounds; the enum values are contiguous from 0 so they cast to
        /// and from the int focus index used by keyboard navigation and hit-testing.
        /// </summary>
        private enum CrashAction
        {
            OpenGitHubIssue = 0,
            OpenReportFolder = 1,
            Dismiss = 2,
            Delete = 3
        }

        private static readonly Keys F8Key = Keys.F8;

        // The component owns its layout privately (no skin assets, no layout framework). The virtual
        // canvas matches the rest of the title (1280x720).
        private const int VirtualWidth = 1280;
        private const int VirtualHeight = 720;

        private static readonly Rectangle BannerRegion = new(940, 16, 324, 56);
        private static readonly Rectangle PanelRegion = new(160, 90, 960, 540);

        // Action-button geometry is pure constant arithmetic; compute once and reuse for both
        // drawing and hit-testing so neither path allocates per call.
        private static readonly Rectangle[] ActionRects = BuildActionRects();
        private static readonly Rectangle ActionRowRegion = Rectangle.Union(ActionRects[0], ActionRects[^1]);

        private readonly ICrashReportInbox _inbox;

        private IReadOnlyList<CrashReportInboxItem> _reports;
        private bool _isOpen;
        private int _selectedIndex;
        private int _actionFocus;
        private bool _deleteConfirming;
        private string? _errorText;

        // A launch-backed action (Open GitHub issue / Open report folder) runs OFF the game thread
        // so the bounded macOS `open` wait cannot freeze the title update loop. The task is polled
        // each frame from HandleInput; while it is in flight the panel consumes input but starts no
        // new action. Dismiss/Delete are fast filesystem ops and stay synchronous.
        private Task<CrashReportActionResult>? _pendingLaunch;

        public CrashReportNotification(ICrashReportInbox inbox)
        {
            _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
            _reports = SnapshotReports();
        }

        // internal test accessors (InternalsVisibleTo lets the test assembly observe state without
        // exposing it on the public surface).
        internal bool IsOpen => _isOpen;
        internal int SelectedIndex => _selectedIndex;
        internal int ActionFocus => _actionFocus;
        internal bool IsDeleteConfirming => _deleteConfirming;
        internal string? ErrorText => _errorText;
        internal IReadOnlyList<CrashReportInboxItem> Reports => _reports;
        internal bool IsBannerVisible => !_isOpen && AnyPending(_reports);
        internal bool IsLaunchPending => _pendingLaunch is not null;

        /// <summary>
        /// Test seam: blocks until the in-flight launch task completes and resolves its result on
        /// the calling (test) thread. Production resolves pending launches via <see cref="PollPendingLaunch"/>
        /// polled from <see cref="HandleInput"/> across frames; tests call this for determinism so a
        /// synchronous fake inbox need not be polled over real frames.
        /// </summary>
        internal void WaitForLaunchAndResolve()
        {
            if (_pendingLaunch is not { } task)
            {
                return;
            }

            _pendingLaunch = null;
            CrashReportActionResult result;
            try
            {
                result = task.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                result = new CrashReportActionResult(Succeeded: false, ErrorCode: "inbox_unexpected_failure");
            }

            ApplyLaunchResult(result);
        }

        /// <summary>
        /// The single notification input-ownership seam. Returns true when the notification consumes
        /// the frame's input, in which case the title stage must skip its own menu/exit handling for
        /// that same frame. F8 is read raw (non-remappable) and edge-triggered against the polled
        /// keyboard snapshots; panel navigation/actions use the remappable <see cref="IInputManager"/>
        /// commands.
        /// </summary>
        public bool HandleInput(
            KeyboardState currentKeyboard,
            KeyboardState previousKeyboard,
            IInputManager? inputManager,
            Point? virtualMouse,
            bool leftMouseClick)
        {
            bool f8Edge = IsF8Triggered(currentKeyboard, previousKeyboard);

            if (!_isOpen)
            {
                // Closed: only F8 or a banner click can open. F8 needs at least one retained report;
                // a banner click needs at least one pending report (the banner only surfaces pending).
                if (f8Edge && _reports.Count > 0)
                {
                    OpenPanel();
                    return true;
                }

                if (leftMouseClick && virtualMouse is { } point && AnyPending(_reports) &&
                    BannerRegion.Contains(point))
                {
                    OpenPanel();
                    return true;
                }

                return false;
            }

            // Open: the panel owns input for the frame. Every branch consumes.
            HandleOpenInput(inputManager, f8Edge, virtualMouse, leftMouseClick);
            return true;
        }

        /// <summary>
        /// Draws the banner (when closed and pending reports exist) and the review panel (when open).
        /// Render fields are strictly limited to position, report id, captured UTC, build id,
        /// OS/architecture, stage/milestone, exception type and the Pending/Acknowledged status —
        /// never the report body. Reuses the title's SpriteBatch/font/white-pixel resources.
        /// </summary>
        [ExcludeFromCodeCoverage]
        public void Draw(SpriteBatch spriteBatch, IFont? font, Texture2D? whitePixel)
        {
            if (spriteBatch == null || whitePixel == null)
            {
                return;
            }

            if (!_isOpen)
            {
                if (AnyPending(_reports))
                {
                    DrawBanner(spriteBatch, font, whitePixel);
                }

                return;
            }

            DrawPanel(spriteBatch, font, whitePixel);
        }

        private void HandleOpenInput(
            IInputManager? inputManager,
            bool f8Edge,
            Point? virtualMouse,
            bool leftMouseClick)
        {
            // Resolve any launch that completed since the last frame before accepting new input.
            // While a launch is in flight the panel consumes input but starts no new action, so the
            // bounded macOS `open` wait never blocks the title update loop.
            PollPendingLaunch();
            if (_pendingLaunch is not null)
            {
                return;
            }

            // F8 reopens/refreshes even while already open (resets selection to the newest pending).
            if (f8Edge)
            {
                OpenPanel();
                return;
            }

            // While confirming a delete, only Activate (confirm) and Back (cancel) resolve the
            // confirmation; other navigation is consumed but ignored so selection cannot drift.
            if (_deleteConfirming)
            {
                if (inputManager?.IsBackActionTriggered() == true)
                {
                    _deleteConfirming = false;
                    return;
                }

                if (inputManager?.IsCommandPressed(InputCommandType.Activate) == true)
                {
                    PerformDelete();
                    return;
                }

                // Mouse: a click on the Delete button confirms; a click outside the action row
                // cancels. The hit-test reuses the same cached button/row bounds as the normal
                // mouse path. Clicks inside the row but off Delete are consumed but ignored.
                if (leftMouseClick && virtualMouse is { } confirmPoint)
                {
                    if (HitTestAction(confirmPoint) == CrashAction.Delete)
                    {
                        PerformDelete();
                        return;
                    }

                    if (!ActionRowRegion.Contains(confirmPoint))
                    {
                        _deleteConfirming = false;
                    }

                    return;
                }

                return;
            }

            if (inputManager?.IsBackActionTriggered() == true)
            {
                ClosePanel();
                return;
            }

            if (inputManager?.IsCommandPressed(InputCommandType.MoveLeft) == true)
            {
                MoveSelection(-1);
                return;
            }

            if (inputManager?.IsCommandPressed(InputCommandType.MoveRight) == true)
            {
                MoveSelection(+1);
                return;
            }

            if (inputManager?.IsCommandPressed(InputCommandType.MoveUp) == true)
            {
                _actionFocus = (_actionFocus - 1 + ActionCount) % ActionCount;
                ClearError();
                return;
            }

            if (inputManager?.IsCommandPressed(InputCommandType.MoveDown) == true)
            {
                _actionFocus = (_actionFocus + 1) % ActionCount;
                ClearError();
                return;
            }

            // Mouse: clicking an action button focuses and invokes it in one motion.
            if (leftMouseClick && virtualMouse is { } point)
            {
                if (HitTestAction(point) is { } hit)
                {
                    _actionFocus = (int)hit;
                    ClearError();
                    InvokeFocusedAction();
                    return;
                }
            }

            if (inputManager?.IsCommandPressed(InputCommandType.Activate) == true)
            {
                InvokeFocusedAction();
                return;
            }
        }

        private void OpenPanel()
        {
            _reports = SnapshotReports();
            _isOpen = true;
            _deleteConfirming = false;
            _actionFocus = 0;
            ClearError();
            _selectedIndex = ResolveDefaultSelection(_reports);
        }

        private void ClosePanel()
        {
            _isOpen = false;
            _deleteConfirming = false;
            ClearError();
        }

        private void MoveSelection(int delta)
        {
            if (_reports.Count == 0)
            {
                return;
            }

            var next = _selectedIndex + delta;
            if (next < 0)
            {
                next = 0;
            }
            else if (next >= _reports.Count)
            {
                next = _reports.Count - 1;
            }

            _selectedIndex = next;
            ClearError();
        }

        private void InvokeFocusedAction()
        {
            switch ((CrashAction)_actionFocus)
            {
                case CrashAction.OpenGitHubIssue:
                    InvokeLaunch(openFolder: false);
                    break;
                case CrashAction.OpenReportFolder:
                    InvokeLaunch(openFolder: true);
                    break;
                case CrashAction.Dismiss:
                    InvokeDismiss();
                    break;
                case CrashAction.Delete:
                    // Delete is gated behind a confirmation; the first Activate only arms it.
                    _deleteConfirming = true;
                    break;
            }
        }

        private void InvokeLaunch(bool openFolder)
        {
            // Ignore re-entry while a launch is already in flight.
            if (_pendingLaunch is not null)
            {
                return;
            }

            if (!TryGetSelectedSummary(out var summary))
            {
                return;
            }

            var reportId = summary.ReportId;
            // Run the launch-backed action off the game thread so the bounded macOS `open` wait
            // cannot block HandleInput or title-frame updates. The result is marshalled back on
            // the game thread by PollPendingLaunch (polled each frame from HandleInput).
            _pendingLaunch = Task.Run(() => openFolder
                ? _inbox.OpenReportFolder(reportId)
                : _inbox.OpenGitHubIssue(reportId));
        }

        private void PollPendingLaunch()
        {
            if (_pendingLaunch is not { } task)
            {
                return;
            }

            if (!task.IsCompleted)
            {
                return;
            }

            _pendingLaunch = null;
            var result = task.IsCompletedSuccessfully
                ? task.Result
                : new CrashReportActionResult(Succeeded: false, ErrorCode: "inbox_unexpected_failure");
            ApplyLaunchResult(result);
        }

        private void ApplyLaunchResult(CrashReportActionResult result)
        {
            if (result.Succeeded)
            {
                // A successful launch acknowledges (never deletes); refresh the snapshot so the
                // status flips to Acknowledged and keep the panel open for review. The selection
                // stays on the same report if it is still retained.
                RefreshSnapshotPreservingSelection();
                ClearError();
                return;
            }

            SetError(result.ErrorCode);
        }

        private void InvokeDismiss()
        {
            if (!TryGetSelectedSummary(out var summary))
            {
                return;
            }

            var result = _inbox.Dismiss(summary.ReportId);
            if (result.Succeeded)
            {
                // Dismiss persists acknowledgement and dismisses the panel; the banner re-evaluates
                // against the refreshed snapshot on the next frame.
                _reports = SnapshotReports();
                ClosePanel();
                return;
            }

            SetError(result.ErrorCode);
        }

        private void PerformDelete()
        {
            // The confirmation is resolved either way; failure exposes a retryable error and leaves
            // the report retained.
            _deleteConfirming = false;

            if (!TryGetSelectedSummary(out var summary))
            {
                return;
            }

            var result = _inbox.Delete(summary.ReportId);
            _reports = SnapshotReports();

            if (!result.Succeeded)
            {
                SetError(result.ErrorCode);
                return;
            }

            ClearError();

            if (_reports.Count == 0)
            {
                // Nothing left to review -> close.
                _isOpen = false;
                return;
            }

            // Select the nearest remaining item: clamp the previous index into the new range.
            if (_selectedIndex >= _reports.Count)
            {
                _selectedIndex = _reports.Count - 1;
            }
        }

        private bool TryGetSelectedSummary([NotNullWhen(true)] out CrashReportSummary? summary)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _reports.Count)
            {
                summary = null;
                return false;
            }

            summary = _reports[_selectedIndex].Summary;
            return true;
        }

        private void RefreshSnapshotPreservingSelection()
        {
            var previousId = TryGetSelectedSummary(out var current) ? current.ReportId : null;
            _reports = SnapshotReports();

            if (previousId is null)
            {
                _selectedIndex = ResolveDefaultSelection(_reports);
                return;
            }

            var indexOfSame = IndexOfReport(_reports, previousId);
            _selectedIndex = indexOfSame >= 0 ? indexOfSame : ResolveDefaultSelection(_reports);
        }

        private static int IndexOfReport(IReadOnlyList<CrashReportInboxItem> reports, string reportId)
        {
            for (int i = 0; i < reports.Count; i++)
            {
                // Case-insensitive: a refreshed snapshot's id casing follows the on-disk name,
                // which need not match the previously selected id on a case-sensitive volume.
                if (string.Equals(reports[i].Summary.ReportId, reportId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        // The newest pending report (largest CapturedAtUtc among unacknowledged); if none pending,
        // the newest retained report. The snapshot is sorted ascending by capture time, so the
        // newest is simply the last match.
        private static int ResolveDefaultSelection(IReadOnlyList<CrashReportInboxItem> reports)
        {
            if (reports.Count == 0)
            {
                return 0;
            }

            int newestPending = -1;
            for (int i = 0; i < reports.Count; i++)
            {
                if (!reports[i].IsAcknowledged)
                {
                    newestPending = i;
                }
            }

            return newestPending >= 0 ? newestPending : reports.Count - 1;
        }

        private void ClearError() => _errorText = null;

        // Bound the error text so a pathological code can never produce an oversized UI string. Map
        // known codes to short messages; fall back to a trimmed copy of the code.
        private void SetError(string? code)
        {
            _errorText = code switch
            {
                "report_not_found" => "Report no longer available.",
                "launch_platform_unsupported" => "Cannot open on this platform.",
                "launch_target_rejected" => "Cannot open on this platform.",
                "launch_start_failed" or "launch_process_null" or "launch_nonzero_exit"
                    or "launch_timeout" =>
                    "Could not open the external handler.",
                "acknowledge_io_failure" => "Could not save acknowledgement.",
                "delete_io_failure" => "Could not delete the report file.",
                "inbox_unexpected_failure" => "Unexpected inbox error.",
                null or "" => "Action failed.",
                _ => Trim(code),
            };
        }

        private static string Trim(string value)
        {
            const int max = 80;
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private static bool IsF8Triggered(KeyboardState current, KeyboardState previous)
        {
            return current.IsKeyDown(F8Key) && !previous.IsKeyDown(F8Key);
        }

        private IReadOnlyList<CrashReportInboxItem> SnapshotReports()
        {
            IReadOnlyList<CrashReportInboxItem> raw;
            try
            {
                raw = _inbox.GetReports();
            }
            catch (Exception)
            {
                return Array.Empty<CrashReportInboxItem>();
            }

            if (raw == null || raw.Count == 0)
            {
                return Array.Empty<CrashReportInboxItem>();
            }

            // Deterministic ascending order by capture time then id, so "newest" is always the last
            // element regardless of how the store enumerates the directory. The id tiebreaker is
            // case-insensitive to match the case-insensitive retained-name contract.
            return raw
                .OrderBy(item => item.Summary.CapturedAtUtc)
                .ThenBy(item => item.Summary.ReportId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool AnyPending(IReadOnlyList<CrashReportInboxItem> reports)
        {
            for (int i = 0; i < reports.Count; i++)
            {
                if (!reports[i].IsAcknowledged)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------------------------------------
        // Layout / drawing (private geometry; no skin assets)
        // ---------------------------------------------------------------------------------------------

        [ExcludeFromCodeCoverage]
        private void DrawBanner(SpriteBatch spriteBatch, IFont? font, Texture2D whitePixel)
        {
            var pending = CountPending(_reports);
            spriteBatch.Draw(
                whitePixel,
                BannerRegion,
                new Color(120, 32, 32, 220));

            DrawHollowRect(spriteBatch, whitePixel, BannerRegion, new Color(240, 200, 120, 230));

            if (font != null)
            {
                var text = pending == 1
                    ? "1 pending crash report"
                    : pending + " pending crash reports";
                font.DrawString(
                    spriteBatch,
                    text,
                    new Vector2(BannerRegion.X + 12, BannerRegion.Y + 8),
                    Color.White);
                font.DrawString(
                    spriteBatch,
                    "F8 or click to review",
                    new Vector2(BannerRegion.X + 12, BannerRegion.Y + 28),
                    new Color(255, 230, 170));
            }
        }

        [ExcludeFromCodeCoverage]
        private void DrawPanel(SpriteBatch spriteBatch, IFont? font, Texture2D whitePixel)
        {
            // Dim the backdrop so the panel reads as modal.
            spriteBatch.Draw(
                whitePixel,
                new Rectangle(0, 0, VirtualWidth, VirtualHeight),
                new Color(0, 0, 0, 160));

            spriteBatch.Draw(
                whitePixel,
                PanelRegion,
                new Color(18, 20, 34, 245));
            DrawHollowRect(spriteBatch, whitePixel, PanelRegion, new Color(120, 110, 200, 230));

            if (!TryGetSelectedSummary(out var summary))
            {
                return;
            }

            if (font == null)
            {
                DrawActions(spriteBatch, font, whitePixel);
                return;
            }

            float x = PanelRegion.X + 24;
            float y = PanelRegion.Y + 20;

            font.DrawString(spriteBatch, "Crash report review", new Vector2(x, y), new Color(255, 230, 170));
            y += 30;

            // Render ONLY the mandated fields; never the report body.
            var item = _reports[_selectedIndex];
            var lines = new[]
            {
                "Report " + (_selectedIndex + 1) + " of " + _reports.Count,
                "Report ID: " + summary.ReportId,
                "Captured (UTC): " + summary.CapturedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                "Build ID: " + summary.BuildId,
                "OS / architecture: " + summary.OperatingSystem + " / " + summary.ProcessArchitecture,
                "Stage / milestone: " + summary.StageOrMilestone,
                "Exception type: " + summary.ExceptionType,
                "Status: " + (item.IsAcknowledged ? "Acknowledged" : "Pending")
            };

            foreach (var line in lines)
            {
                font.DrawString(spriteBatch, line, new Vector2(x, y), Color.White);
                y += 22;
            }

            if (!string.IsNullOrEmpty(_errorText))
            {
                font.DrawString(
                    spriteBatch,
                    _errorText,
                    new Vector2(x, y + 4),
                    new Color(255, 120, 120));
            }

            DrawActions(spriteBatch, font, whitePixel);
        }

        [ExcludeFromCodeCoverage]
        private void DrawActions(SpriteBatch spriteBatch, IFont? font, Texture2D whitePixel)
        {
            var labels = new[]
            {
                "Open GitHub issue",
                "Open report folder",
                "Dismiss",
                "Delete"
            };

            var actionRects = ActionRects;
            var color = new Color(40, 46, 78, 230);
            var focusedColor = new Color(90, 80, 160, 240);
            var confirmColor = new Color(150, 40, 40, 240);

            for (int i = 0; i < actionRects.Length; i++)
            {
                var rect = actionRects[i];
                var fill = _deleteConfirming && i == (int)CrashAction.Delete
                    ? confirmColor
                    : (i == _actionFocus ? focusedColor : color);
                spriteBatch.Draw(whitePixel, rect, fill);
                DrawHollowRect(spriteBatch, whitePixel, rect, Color.White * 0.6f);

                font?.DrawString(
                    spriteBatch,
                    labels[i],
                    new Vector2(rect.X + 10, rect.Y + 6),
                    Color.White);
            }

            if (_deleteConfirming)
            {
                font?.DrawString(
                    spriteBatch,
                    "Confirm delete? Activate to confirm, Back to cancel.",
                    new Vector2(PanelRegion.X + 24, actionRects[0].Y - 28),
                    new Color(255, 200, 120));
            }
        }

        [ExcludeFromCodeCoverage]
        private static void DrawHollowRect(SpriteBatch spriteBatch, Texture2D whitePixel, Rectangle rect, Color color)
        {
            spriteBatch.Draw(whitePixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
            spriteBatch.Draw(whitePixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
            spriteBatch.Draw(whitePixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
            spriteBatch.Draw(whitePixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
        }

        private static Rectangle[] BuildActionRects()
        {
            const int buttonWidth = 220;
            const int buttonHeight = 32;
            const int gap = 12;
            int totalWidth = ActionCount * buttonWidth + (ActionCount - 1) * gap;
            int startX = PanelRegion.X + (PanelRegion.Width - totalWidth) / 2;
            int y = PanelRegion.Bottom - buttonHeight - 24;

            var rects = new Rectangle[ActionCount];
            for (int i = 0; i < ActionCount; i++)
            {
                rects[i] = new Rectangle(startX + i * (buttonWidth + gap), y, buttonWidth, buttonHeight);
            }

            return rects;
        }

        private CrashAction? HitTestAction(Point point)
        {
            for (int i = 0; i < ActionRects.Length; i++)
            {
                if (ActionRects[i].Contains(point))
                {
                    return (CrashAction)i;
                }
            }

            return null;
        }

        [ExcludeFromCodeCoverage]
        private static int CountPending(IReadOnlyList<CrashReportInboxItem> reports)
        {
            int count = 0;
            for (int i = 0; i < reports.Count; i++)
            {
                if (!reports[i].IsAcknowledged)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
