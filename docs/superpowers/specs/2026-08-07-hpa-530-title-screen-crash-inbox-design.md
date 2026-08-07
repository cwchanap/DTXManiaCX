# HPA-530 Title-Screen Crash Inbox and GitHub Handoff — Design

**Date:** 2026-08-07  
**Status:** Approved for implementation planning  
**Linear:** HPA-530 — Add title-screen crash inbox and GitHub issue handoff  
**Depends on:** HPA-529 — Add process-boundary crash capture and sanitized local bundles  
**Scope:** Surface retained schema-v2 crash reports on the title screen and provide an explicit manual GitHub reporting handoff without reintroducing the superseded ZIP/inbox-state architecture.

## Context

HPA-529 shipped a deliberately simplified crash-reporting format:

- one retained artifact per crash;
- plain-text `DTXMANIACX-CRASH-REPORT 2` files only;
- one latest-five retention policy in `CrashReportStore`;
- no ZIP bundle;
- no emergency fallback format;
- no `inbox-state.json`.

The original HPA-530 ticket predates that schema-v2 amendment and still references ZIP reports, emergency fallback reports, and a separate inbox-state file. Those statements are obsolete for this implementation. HPA-530 must extend the shipped text-only model rather than recreate the removed architecture.

## Goals

- Notify the player non-blockingly when retained crash reports have not been acknowledged.
- Let the player review only the safe, allowlisted report header summary.
- Let the player open a prefilled DTXManiaCX GitHub issue and manually attach the selected `.txt` report.
- Let the player open the crash-report directory, dismiss a report, or delete it after confirmation.
- Keep acknowledgement persistent across restarts without adding a second state file.
- Keep title-screen code independent of crash-report parsing, filesystem paths, process launching, and retention rules.
- Keep all failure modes recoverable in-game and never block normal startup.

## Non-goals

- Automatic upload or GitHub API submission.
- GitHub authentication or submission confirmation.
- Viewing exception bodies, stack traces, logs, breadcrumbs, or context sections in-game.
- ZIP handling or emergency-report special cases.
- A second retention policy.
- A dedicated crash-management stage.
- Linux external-launch support in this ticket.
- Native dumps, hang reports, screenshots, or telemetry.

## Existing integration points

The implementation should extend the seams already introduced by HPA-529:

- `CrashReportStore` owns the crash-report root, atomic writes, cleanup, and latest-five retention.
- `CrashReportSummary` already models the safe fields required by the title UI.
- `CrashReportTextWriter` writes a fixed schema-v2 header before the free-form report sections.
- `CrashReportRuntime` is the process-owned diagnostics composition root.
- `IGameCrashDiagnostics` is injected into `BaseGame` and is the correct route for exposing a narrow inbox service.
- `IStageGame` is the stage-facing game contract used by `TitleStage`.
- `TitleStage` already owns title rendering and keyboard/mouse handling.

No static service locator should be introduced.

## Architecture

```text
CrashReportRuntime
  └─ ICrashReportInbox
       ├─ CrashReportStore
       │    ├─ discover schema-v2 .txt reports
       │    ├─ parse safe header summaries
       │    ├─ acknowledge by atomic rename
       │    └─ delete
       ├─ GitHubCrashIssueBuilder
       └─ IExternalLauncher

IGameCrashDiagnostics
  └─ CrashReportInbox

BaseGame / IStageGame
  └─ CrashReportInbox

TitleStage
  └─ CrashReportNotification
       ├─ compact banner
       └─ lightweight review panel
```

`TitleStage` depends only on `ICrashReportInbox`. It must not receive `CrashReportStore`, `IExternalLauncher`, the crash-report root path, or a parser directly.

## Report acknowledgement without `inbox-state.json`

Acknowledgement is encoded in the retained report filename.

Pending report:

```text
crash-20260806-123456Z-a1b2c3.txt
```

Acknowledged report:

```text
crash-20260806-123456Z-a1b2c3.ack.txt
```

Rules:

1. New reports created by HPA-529 always use the pending form.
2. Acknowledgement is an atomic same-directory rename to the `.ack.txt` form.
3. The report ID inside the schema-v2 header never changes.
4. Both pending and acknowledged reports count toward the single latest-five retention limit.
5. `CrashReportStore` remains the only owner of retention.
6. Temporary `.tmp` behavior remains unchanged.
7. Deleting a report removes whichever physical filename currently represents that report ID.

This keeps the report itself as the single durable artifact while still preserving acknowledgement across restarts.

## Report discovery and safe summary parsing

Add one bounded schema-v2 header reader owned by the crash-reporting subsystem.

Discovery recognizes only the two approved filename forms in the crash-report root. It never recursively scans directories and never follows arbitrary paths supplied by the UI.

The header reader:

- reads only the beginning of the selected report;
- requires `DTXMANIACX-CRASH-REPORT 2` when available;
- parses only these allowlisted keys:
  - `ReportId`;
  - `CapturedAtUtc`;
  - `BuildId`;
  - `OperatingSystem`;
  - `ProcessArchitecture`;
  - `StageOrMilestone`;
  - `ExceptionType`;
- stops before `--- EXCEPTION ---`;
- applies a small line/character bound so a malformed file cannot cause unbounded reads;
- never returns exception text or other report sections to the UI.

If the header is corrupt or incomplete, discovery still returns a usable inbox item:

- derive report ID from the filename;
- derive capture time from the filename timestamp when parseable;
- set unavailable summary fields to `Unknown`.

A corrupt report remains dismissible, openable in the report folder, and deletable.

## Inbox contract

Expose a narrow public contract suitable for `IStageGame`:

```csharp
public sealed record CrashReportInboxItem(
    CrashReportSummary Summary,
    bool IsAcknowledged);

public readonly record struct CrashReportActionResult(
    bool Succeeded,
    string? ErrorCode = null);

public interface ICrashReportInbox
{
    IReadOnlyList<CrashReportInboxItem> GetReports();
    CrashReportActionResult OpenGitHubIssue(string reportId);
    CrashReportActionResult OpenReportFolder(string reportId);
    CrashReportActionResult Dismiss(string reportId);
    CrashReportActionResult Delete(string reportId);
}
```

The interface intentionally accepts report IDs rather than file paths. The implementation resolves the current retained item internally each time, preventing `TitleStage` from constructing or retaining arbitrary filesystem paths.

`GetReports()` returns the current retained set newest-first. The set is bounded by the existing latest-five policy.

Provide a no-op implementation for the disabled crash runtime so title-screen code requires no feature flag or null checks.

## Acknowledgement semantics

A report becomes acknowledged only after one of these actions succeeds:

- browser launch for `OpenGitHubIssue`;
- report-directory launch for `OpenReportFolder`;
- `Dismiss`.

Opening GitHub or the report folder does not delete the report and does not mean the player submitted an issue.

For launcher-backed actions:

1. Resolve and validate the target internally.
2. Launch the target.
3. Only after launch succeeds, rename the selected pending report to the acknowledged filename.
4. Refresh the inbox snapshot.

If launch fails, the report remains pending and the action returns a retryable error.

If launch succeeds but acknowledgement persistence fails, the report remains pending and the UI displays an acknowledgement error. The external target may already be open; the user can retry acknowledgement through `Dismiss` rather than requiring hidden recovery logic.

Dismiss performs acknowledgement without launching anything and closes the review panel on success.

## GitHub issue handoff

`OpenGitHubIssue` constructs one HTTPS URL targeting exactly the DTXManiaCX new-issue path:

```text
https://github.com/cwchanap/DTXManiaCX/issues/new
```

The generated title/body may contain only:

- report ID;
- app/build ID;
- operating system;
- process architecture;
- stage or startup milestone;
- exception type;
- report format identifier `DTXMANIACX-CRASH-REPORT 2`;
- instructions telling the player to inspect and manually attach the selected `.txt` crash report.

Do not embed exception messages, stack traces, logs, breadcrumbs, arbitrary report text, full paths, usernames, song identity, configuration content, or report-file contents in the URL.

Before launch, validate that the final URI:

- uses `https`;
- has host `github.com`;
- has path `/cwchanap/DTXManiaCX/issues/new`.

The URL builder and validator should be deterministic and unit-tested independently from process launching.

## External launcher

Keep platform process details behind `IExternalLauncher`.

Suggested contract:

```csharp
internal interface IExternalLauncher
{
    ExternalLaunchResult OpenUri(Uri uri);
    ExternalLaunchResult OpenDirectory(string directoryPath);
}
```

The production launcher validates platform support and returns a bounded error code rather than throwing into the title stage.

### Windows

Use `Process.Start` with `ProcessStartInfo.UseShellExecute = true` for the already validated HTTPS URI or internally resolved crash-report directory.

Never invoke `cmd.exe`, `cmd /c`, PowerShell, or build a command shell string.

### macOS

Use `/usr/bin/open` through `ProcessStartInfo` with `UseShellExecute = false` and `ArgumentList`:

```text
/usr/bin/open -- <target>
```

`--` and the target must be separate arguments. Never invoke `sh -c` or concatenate dynamic values into a shell command.

A non-zero exit code, process-start exception, or unsupported platform returns a retryable failure.

The directory launch target is always the internally resolved `AppPaths.GetCrashReportsRoot()` value. The stage cannot request another directory.

## Title-screen UX

### Banner

When `TitleStage` activates, query the inbox once and show a compact upper-right notification only when one or more reports are pending.

Example:

```text
CRASH REPORT AVAILABLE
2 reports saved · Click or press F8 to review
```

Requirements:

- never delays startup;
- never takes initial focus;
- does not block normal title-menu navigation while the panel is closed;
- one banner summarizes all pending reports;
- mouse click on the banner or F8 opens the review panel;
- F8 may also reopen the panel while acknowledged retained reports still exist;
- if no retained reports exist, F8 is a no-op.

### Review panel

The panel displays only `CrashReportSummary` plus acknowledgement state:

- report position, for example `2 / 5`;
- report ID;
- captured UTC time;
- build ID;
- OS / architecture;
- stage or milestone;
- exception type;
- `Pending` or `Acknowledged` state.

Actions:

- Previous;
- Next;
- Open GitHub Issue;
- Open Report Folder;
- Dismiss;
- Delete Report.

Delete requires a second in-panel confirmation before calling `Delete`.

After deletion, select the nearest remaining report. If no report remains, close the panel and remove the banner.

### Input ownership

When the panel is closed, existing title-menu behavior remains unchanged except for the dedicated F8 shortcut and banner hit-test.

When the panel is open, panel input is processed before the normal title-menu input path and the title stage returns without executing menu actions for that frame.

Use existing remappable commands for panel navigation where practical:

- MoveLeft / MoveRight: previous / next report;
- MoveUp / MoveDown: move action focus;
- Activate: execute focused action;
- Back or Escape: close panel.

Mouse buttons remain clickable through virtual-coordinate hit testing.

When delete confirmation is visible, only confirmation/cancel input is consumed until the confirmation resolves.

## Error handling

Inbox and launcher failures are non-fatal.

The panel keeps a short visible error message for retryable failures such as:

- report no longer exists;
- report could not be acknowledged;
- report could not be deleted;
- browser launch failed;
- file-manager launch failed;
- external launch unsupported on this platform.

A launcher failure leaves the report pending and keeps the panel open.

A corrupt report header does not produce an error panel by itself; it displays `Unknown` summary values and remains actionable.

The title stage must not catch and expose raw exception messages from filesystem or process APIs.

## Testing strategy

### Storage and parsing

Unit tests should cover:

- discovery of pending and acknowledged schema-v2 files;
- newest-first ordering;
- shared latest-five retention across both filename forms;
- atomic pending-to-acknowledged rename;
- deletion of pending and acknowledged reports;
- corrupt/missing headers falling back to filename-derived ID/time plus `Unknown` fields;
- bounded parsing that never reads or returns the free-form exception section.

### GitHub handoff and launcher

Unit tests should cover:

- exact GitHub host/path validation;
- rejection of HTTP, alternate hosts, alternate repositories, and alternate paths;
- query content limited to approved summary fields;
- Windows `UseShellExecute = true` and no command shell;
- macOS `/usr/bin/open`, separate `--`, separate target argument, and no shell concatenation;
- non-zero exit/start failure mapped to retryable errors;
- directory launch restricted to the internally resolved crash-report root.

### Inbox actions

Tests using a temporary real store plus a fake launcher should verify:

- successful GitHub launch acknowledges;
- successful folder launch acknowledges;
- failed launch leaves the report pending;
- dismiss acknowledges without deleting;
- delete removes the selected report;
- acknowledgement persistence failure is surfaced without deleting the report.

### Title UI

Stage/component tests should verify:

- no reports means no banner and unchanged menu behavior;
- pending reports produce one summarized banner;
- F8 and banner click open the panel;
- panel input does not leak into title-menu actions;
- Previous/Next navigation works across retained reports;
- acknowledged reports remain reviewable but do not contribute to banner count;
- delete confirmation is required;
- launcher errors remain visible and retryable;
- Escape/Back closes the panel.

Prefer fake `ICrashReportInbox` implementations for title tests so MonoGame graphics and filesystem setup are not required for the behavioral state machine.

### Platform smoke verification

On supported macOS, manually verify that `/usr/bin/open -- <target>` works for both the GitHub URI and crash-report directory. Record the command shape and observed result in the implementation PR.

Windows behavior is covered by unit tests around `ProcessStartInfo`; no command-shell invocation is permitted.

## Implementation boundaries

HPA-530 should not refactor unrelated title-menu code, generalize a cross-game modal framework, add asynchronous background scanning, or introduce a general-purpose process-execution abstraction.

The expected implementation remains small enough for one ticket and approximately two engineer days: one storage/handoff slice plus one title-UI integration slice, with focused tests around each boundary.

## Acceptance criteria

- No retained reports means no banner and unchanged title-menu behavior.
- Pending reports produce one non-blocking summarized banner.
- Schema-v2 `.txt` reports are the only supported report format.
- Acknowledgement persists through the `.ack.txt` filename state; no `inbox-state.json` exists.
- Pending and acknowledged reports share the single latest-five `CrashReportStore` retention policy.
- Title UI displays only allowlisted summary fields and never parses report sections directly.
- F8 and banner click open the review panel without leaking input into normal title actions.
- Opening GitHub or the report folder acknowledges only after successful process launch and never deletes the report.
- Dismiss acknowledges without deleting.
- Delete requires in-panel confirmation.
- Launcher failures remain visible, pending, and retryable.
- GitHub URLs target only `https://github.com/cwchanap/DTXManiaCX/issues/new` and contain only approved summary fields plus manual `.txt` attachment instructions.
- Windows launcher uses `UseShellExecute = true` without a command shell.
- macOS launcher uses `/usr/bin/open` with separate `--` and target arguments and without shell concatenation.
- Unsupported platforms fail safely in-game.
