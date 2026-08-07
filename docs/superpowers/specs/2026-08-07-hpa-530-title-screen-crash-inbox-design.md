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

HPA-530 does reintroduce the name `ICrashReportInbox`, but only as a new narrow stage-facing facade for schema-v2 discovery and actions. This does **not** revive the obsolete HPA-519 ZIP/inbox-state design or its old persistence contract.

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
- A generic modal framework, generic process runner, or configurable GitHub destination.

## Existing integration points

The implementation extends the seams already introduced by HPA-529:

- `CrashReportStore` owns the crash-report root, atomic writes, cleanup, and latest-five retention.
- `CrashReportSummary` already models the safe fields required by the title UI.
- `CrashReportTextWriter` writes a fixed schema-v2 header before the free-form report sections.
- `CrashReportRuntime` is the process-owned diagnostics composition root.
- `IGameCrashDiagnostics` is injected into `BaseGame` and is the correct route for exposing a narrow inbox service.
- `IStageGame` is the stage-facing game contract used by `TitleStage` and already uses default interface members for optional diagnostics hooks.
- `TitleStage` already owns title rendering and keyboard/mouse handling.
- `AppPaths.GetCrashReportsRoot()` remains the only production path source for crash reports.

No static service locator should be introduced.

## Architecture

```text
CrashReportRuntime
  └─ CrashReportInbox : ICrashReportInbox
       ├─ CrashReportStore
       │    ├─ discover retained report names
       │    ├─ acknowledge by atomic rename
       │    ├─ delete by report ID
       │    └─ expose internal RootPath for composition
       ├─ CrashReportSummaryReader
       │    └─ bounded schema-v2 header parsing
       ├─ GitHubCrashIssueBuilder
       └─ IExternalLauncher

IGameCrashDiagnostics
  └─ CrashReportInbox

BaseGame
  └─ forwards real inbox

IStageGame
  └─ CrashReportInbox => EmptyCrashReportInbox.Instance by default

TitleStage
  └─ CrashReportNotification
       ├─ compact banner
       ├─ review panel state
       └─ input ownership via TryHandleInput(...)
```

`TitleStage` depends only on `ICrashReportInbox`. It must not receive `CrashReportStore`, `IExternalLauncher`, the crash-report root path, or a parser directly.

## Retained file identity and acknowledgement

Acknowledgement is encoded in the retained report filename.

Pending report:

```text
crash-20260806-123456Z-a1b2c3.txt
```

Acknowledged report:

```text
crash-20260806-123456Z-a1b2c3.ack.txt
```

The retained-name contract is normative and case-insensitive:

```text
^crash-\d{8}-\d{6}Z-[0-9a-f]{6}(?:\.ack)?\.txt$
```

### Identity rules

1. New reports created by HPA-529 always use the pending `.txt` form.
2. The logical report ID is always derived from the retained filename by stripping `.ack.txt` when present, otherwise `.txt`.
3. The filename-derived report ID is authoritative for inbox lookup and actions. A header `ReportId` is accepted only when it matches the filename-derived ID; otherwise the filename-derived value wins.
4. `CrashReportSummary.FileName` returned by discovery is the **current physical basename** (`*.txt` or `*.ack.txt`). The title UI should not display or depend on it.
5. Acknowledgement is an atomic same-directory rename from pending `.txt` to `.ack.txt`.
6. Both pending and acknowledged reports participate in the same latest-five retention policy.
7. `CrashReportStore` remains the only owner of retention and filesystem mutation.
8. Temporary `.tmp` behavior remains unchanged.

### Duplicate physical variants

A manually altered or interrupted directory may contain both forms for one report ID. Treat them as one logical report, never two inbox items.

- Discovery collapses duplicate variants by report ID and prefers the pending file as canonical.
- `Cleanup()` removes an acknowledged duplicate when its pending twin exists, then applies latest-five retention to logical report IDs.
- `Acknowledge(reportId)` resolves the pending form first. If an acknowledged duplicate already exists, remove that duplicate before the pending-to-ack rename; if that cleanup fails, return a bounded acknowledgement-conflict error and preserve the pending file.
- `Delete(reportId)` removes both approved physical variants for that report ID so a duplicate cannot reappear as a ghost item.

The timestamp portion precedes the acknowledgement suffix, so filename-derived chronological ordering remains cheap; no report body needs to be opened for retention ordering.

## Report discovery and safe summary parsing

Add one bounded schema-v2 header reader owned by the crash-reporting subsystem.

Discovery recognizes only the approved pending and acknowledged filename forms in the crash-report root. It never recursively scans directories and never follows arbitrary paths supplied by the UI.

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
- reads at most 32 header lines and 16 KiB of header text;
- normalizes each string field to at most 256 characters;
- replaces ASCII control characters with spaces before display or handoff;
- trims values and maps empty values to `Unknown`;
- never returns exception text or other report sections to the UI.

If the header is corrupt or incomplete, discovery still returns a usable inbox item:

- derive report ID from the filename;
- derive capture time from the filename timestamp when parseable;
- set unavailable summary fields to `Unknown`.

A corrupt report remains dismissible, openable in the report folder, and deletable.

The reader is deliberately not a general report parser.

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

The interface accepts report IDs rather than file paths. The implementation resolves the current retained item internally for every action, preventing `TitleStage` from constructing or retaining arbitrary filesystem paths.

`GetReports()` returns at most one item per logical report ID, newest-first, and remains bounded by the existing latest-five policy.

### Disabled runtime behavior

`EmptyCrashReportInbox` is a silent no-op singleton:

- `GetReports()` returns an empty list;
- all action methods return `Succeeded = true` without side effects.

The empty inbox never causes a banner because it never returns reports, and accidental calls do not create misleading “crash reporting disabled” UI errors.

## Acknowledgement semantics

A report becomes acknowledged only after one of these actions succeeds:

- browser launch for `OpenGitHubIssue`;
- report-directory launch for `OpenReportFolder`;
- `Dismiss`.

Opening GitHub or the report folder does not delete the report and does not mean the player submitted an issue.

For launcher-backed actions:

1. Resolve the report by ID.
2. Build and validate the target internally.
3. Launch the target.
4. Only after launch succeeds, persist acknowledgement.
5. Refresh the inbox snapshot.

If launch fails, the report remains pending and the action returns a retryable error.

If launch succeeds but acknowledgement persistence fails, the report remains pending and the UI displays an acknowledgement error. The external target may already be open; the user can retry acknowledgement through `Dismiss` rather than requiring hidden recovery logic.

Dismiss performs acknowledgement without launching anything and closes the review panel on success.

## GitHub issue handoff

`OpenGitHubIssue` constructs one HTTPS URL targeting exactly:

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

Every dynamic query value comes from the bounded normalized summary and is URI-escaped before assembly. Do not concatenate unescaped summary values into the query string.

Do not embed exception messages, stack traces, logs, breadcrumbs, arbitrary report text, full paths, usernames, song identity, configuration content, or report-file contents in the URL.

Before launch, validate that the final URI:

- uses `https`;
- has host `github.com`;
- has path `/cwchanap/DTXManiaCX/issues/new`.

The URL builder and validator are deterministic and unit-tested independently from process launching.

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

The directory launch target is always `CrashReportStore.RootPath`, which is initialized from `AppPaths.GetCrashReportsRoot()` in production. `RootPath` is internal to crash-reporting composition and is never exposed through `IStageGame`.

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
- does not block normal title-menu navigation while closed;
- one banner summarizes all pending reports;
- mouse click on the banner or F8 opens the review panel;
- F8 may reopen the panel while acknowledged retained reports still exist;
- if no retained reports exist, F8 is a no-op.

### F8 policy

F8 is a fixed, non-remappable title diagnostic shortcut for this ticket.

- Read raw `Keys.F8` and edge-trigger it from previous/current keyboard state.
- Do not add a new `InputCommandType` or key-assignment setting.
- If F8 is also mapped to another command, the crash notification consumes that title-screen frame when it activates/reopens the panel, so the mapped title action does not also execute.

This keeps the diagnostic shortcut explicit without expanding configuration scope.

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

`CrashReportNotification` owns all notification interaction state:

- open/closed;
- current selection;
- action focus;
- delete confirmation;
- retryable error text;
- banner hit-testing;
- raw F8 activation.

It exposes a focused input seam such as:

```csharp
bool TryHandleInput(...)
```

`true` means notification input was consumed and `TitleStage` must not call its normal `HandleInput()` or `HandleMouseInput()` for that frame.

When the panel is closed, `TryHandleInput` returns `true` only when F8 or the banner click opens the panel; otherwise title behavior is unchanged.

When the panel is open, notification input is always handled first and consumed before normal title input.

Use existing remappable commands for panel navigation where practical:

- MoveLeft / MoveRight: previous / next report;
- MoveUp / MoveDown: move action focus;
- Activate: execute focused action;
- Back or Escape: close panel.

**Back/Escape while the panel is open must close the panel and must never fall through to `TitleStage.RequestExit()`.**

When delete confirmation is visible, only confirmation/cancel input is consumed until the confirmation resolves.

Mouse buttons remain clickable through virtual-coordinate hit testing.

### Layout

Keep banner/panel rectangles and spacing as private constants owned by `CrashReportNotification` for this ticket. Do not create a new layout abstraction unless implementation reveals multiple consumers; there are none today.

## Error handling

Inbox and launcher failures are non-fatal.

The panel keeps a short visible error message for retryable failures such as:

- report no longer exists;
- report could not be acknowledged;
- duplicate acknowledgement state could not be reconciled;
- report could not be deleted;
- browser launch failed;
- file-manager launch failed;
- external launch unsupported on this platform.

A launcher failure leaves the report pending and keeps the panel open.

A corrupt report header does not produce an error panel by itself; it displays bounded `Unknown` summary values and remains actionable.

The title stage must not catch and expose raw exception messages from filesystem or process APIs.

## Testing strategy

### Storage and parsing

Unit tests cover:

- exact retained-name regex for pending and acknowledged forms;
- filename-derived report ID for both forms;
- `CrashReportSummary.FileName` tracking the current physical basename;
- discovery collapsing pending+ack duplicates to one logical inbox item with pending precedence;
- duplicate cleanup and delete behavior;
- newest-first ordering;
- shared latest-five retention across both filename forms;
- atomic pending-to-acknowledged rename;
- deletion of pending and acknowledged reports;
- corrupt/missing headers falling back to filename-derived ID/time plus `Unknown` fields;
- 32-line / 16-KiB header read bounds;
- 256-character field bounds and ASCII-control normalization;
- bounded parsing that never reads or returns the free-form exception section;
- existing capture still emits only pending `crash-*.txt` names.

### GitHub handoff and launcher

Unit tests cover:

- exact GitHub host/path validation;
- rejection of HTTP, alternate hosts, alternate repositories, and alternate paths;
- query content limited to approved summary fields;
- long and control-character-containing header values cannot produce unbounded or malformed launch URIs;
- dynamic query values are URI-escaped;
- Windows `UseShellExecute = true` and no command shell;
- macOS `/usr/bin/open`, separate `--`, separate target argument, and no shell concatenation;
- non-zero exit/start failure mapped to retryable errors;
- directory launch restricted to the store-owned crash-report root.

### Inbox actions

Tests using a temporary real store plus a fake launcher verify:

- successful GitHub launch acknowledges;
- successful folder launch acknowledges;
- failed launch leaves the report pending;
- dismiss acknowledges without deleting;
- delete removes the logical report, including a duplicate physical variant if present;
- acknowledgement persistence failure is surfaced without deleting the pending report;
- disabled/empty inbox returns no reports and silent successful no-op actions.

### Title UI

Component-first tests verify:

- no reports means no banner and unchanged menu behavior;
- pending reports produce one summarized banner;
- raw edge-triggered F8 and banner click open the panel;
- acknowledged reports remain reviewable but do not contribute to banner count;
- notification input consumption prevents title-menu input leakage;
- Back/Escape while open closes the panel and cannot request game exit;
- Previous/Next navigation works across retained reports;
- delete confirmation is required;
- launcher errors remain visible and retryable.

Keep most behavioral tests on `CrashReportNotification` with a fake `ICrashReportInbox`. Add only a thin `TitleStage` guard test for the consumed-input contract; do not inflate the existing title tests into a graphics-heavy fixture.

### Platform smoke verification

On supported macOS, manually verify that `/usr/bin/open -- <target>` works for both the GitHub URI and crash-report directory. Record the command shape and observed result in the implementation PR.

Windows behavior is covered by unit tests around `ProcessStartInfo`; no command-shell invocation is permitted.

## Risks and mitigations

1. **Pending/ack filename divergence or duplicate twins** — lock the regex/identity contract in store tests, collapse to one logical item, and reconcile duplicates in `Cleanup()`.
2. **Panel Back/Escape accidentally exits the game** — make `CrashReportNotification.TryHandleInput` the ownership boundary and verify open-panel Back/Escape never reaches title exit handling.
3. **Corrupt or hand-edited header creates hostile UI/URI values** — bound header bytes/lines/field lengths, normalize control characters, and URI-escape every dynamic query value.
4. **F8 conflicts with a remapped input command** — define F8 as a fixed raw title diagnostic shortcut and consume the frame when it opens/reopens the crash panel.

## Implementation boundaries

HPA-530 should not refactor unrelated title-menu code, generalize a cross-game modal framework, add asynchronous background scanning, or introduce a general-purpose process-execution abstraction.

The expected implementation remains small enough for one ticket and approximately two engineer days: one storage/handoff slice plus one title-UI integration slice, with focused tests around each boundary.

## Acceptance criteria

- No retained reports means no banner and unchanged title-menu behavior.
- Pending reports produce one non-blocking summarized banner.
- Schema-v2 `.txt` reports are the only supported report format.
- Acknowledgement persists through the `.ack.txt` filename state; no `inbox-state.json` exists.
- The exact retained-name contract supports only pending `crash-*.txt` and acknowledged `crash-*.ack.txt` forms.
- Filename-derived report ID is authoritative; dual pending+ack files collapse to one logical inbox item.
- Pending and acknowledged reports share the single latest-five `CrashReportStore` retention policy.
- Existing crash capture still creates pending `crash-*.txt`, never `.ack.txt`.
- Title UI displays only bounded allowlisted summary fields and never parses report sections directly.
- Raw edge-triggered F8 and banner click open the review panel without leaking input into normal title actions.
- F8 remains fixed/non-remappable for this ticket; no new `InputCommandType` is added.
- Back/Escape while the panel is open closes the panel and never requests game exit.
- Opening GitHub or the report folder acknowledges only after successful process launch and never deletes the report.
- Dismiss acknowledges without deleting.
- Delete requires in-panel confirmation and removes the logical report without leaving a duplicate variant behind.
- Disabled/bootstrap crash reporting yields an empty inbox, no banner, and silent no-op inbox actions.
- Launcher failures remain visible, pending, and retryable.
- GitHub URLs target only `https://github.com/cwchanap/DTXManiaCX/issues/new`, URI-escape all dynamic values, and contain only approved bounded summary fields plus manual `.txt` attachment instructions.
- Windows launcher uses `UseShellExecute = true` without a command shell.
- macOS launcher uses `/usr/bin/open` with separate `--` and target arguments and without shell concatenation.
- Unsupported platforms fail safely in-game.
