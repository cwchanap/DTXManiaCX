# HPA-530 Title-Screen Crash Inbox and GitHub Handoff — Design

**Date:** 2026-08-07  
**Status:** Approved for implementation planning  
**Linear:** HPA-530 — Add title-screen crash inbox and GitHub issue handoff  
**Depends on:** HPA-529 — Add process-boundary crash capture and sanitized local bundles  
**Scope:** Surface retained schema-v2 crash reports on the title screen and provide an explicit manual GitHub reporting handoff without reintroducing the superseded ZIP/inbox-state architecture.

## Context

HPA-529 shipped a deliberately simplified crash-reporting format:

- one plain-text `DTXMANIACX-CRASH-REPORT 2` artifact per crash;
- one latest-five retention policy in `CrashReportStore`;
- no ZIP bundle;
- no emergency fallback format;
- no `inbox-state.json`.

The older HPA-519/HPA-530 text that describes ZIPs, emergency reports, or a persisted inbox-state file is obsolete. HPA-530 reintroduces only a **narrow title-facing inbox facade** over the shipped text reports; it does not revive the obsolete inbox architecture.

## Goals

- Notify the player non-blockingly when retained crash reports have not been acknowledged.
- Review only the safe schema-v2 header summary in-game.
- Open a prefilled DTXManiaCX GitHub issue and let the player manually attach the selected `.txt` report.
- Open the crash-report directory, dismiss a report, or delete it after confirmation.
- Persist acknowledgement without a second state file.
- Keep parsing, paths, process launching, and retention out of `TitleStage`.
- Keep failures retryable and non-fatal.

## Non-goals

- Automatic upload, GitHub API use, authentication, or submission confirmation.
- Viewing exception bodies, stack traces, logs, breadcrumbs, or context sections in-game.
- ZIP or emergency-report compatibility.
- Linux launcher support in this ticket.
- A reusable crash-management stage, generic modal framework, generic process runner, or configurable GitHub destination.

## Architecture

```text
CrashReportRuntime
  └─ CrashReportInbox : ICrashReportInbox
       ├─ CrashReportStore
       │    ├─ discover pending/acknowledged text reports
       │    ├─ acknowledge by same-directory rename
       │    ├─ delete by logical report ID
       │    └─ retain latest five logical reports
       ├─ CrashReportSummaryReader
       │    └─ bounded schema-v2 header parsing
       ├─ GitHubCrashIssueBuilder
       └─ ExternalLauncher

IGameCrashDiagnostics
  └─ CrashReportInbox

BaseGame
  └─ forwards production inbox

IStageGame
  └─ CrashReportInbox => EmptyCrashReportInbox.Instance by default

TitleStage
  └─ CrashReportNotification
       ├─ banner/review-panel state
       └─ HandleInput(...) -> bool consumed
```

`TitleStage` receives only `ICrashReportInbox`. It never receives `CrashReportStore`, a parser, a launcher, or a crash-report path.

## Retained file identity and acknowledgement

Pending form:

```text
crash-20260806-123456Z-a1b2c3.txt
```

Acknowledged form:

```text
crash-20260806-123456Z-a1b2c3.ack.txt
```

The retained-name contract is case-insensitive:

```text
^crash-\d{8}-\d{6}Z-[0-9a-f]{6}(?:\.ack)?\.txt$
```

Rules:

1. New HPA-529 captures always create the pending `.txt` form.
2. Logical report ID is derived from the filename by stripping `.ack.txt` or `.txt`.
3. Filename-derived report ID is authoritative for inbox lookup/actions. A header `ReportId` is used only when it matches.
4. `CrashReportSummary.FileName` means the physical artifact basename represented by that summary at that time. Capture summaries therefore contain `.txt`; discovery of an acknowledged artifact may contain `.ack.txt`. The title UI does not need this field.
5. Acknowledgement uses `File.Move(pendingPath, acknowledgedPath, overwrite: true)` in the same directory.
6. If both physical variants exist because the directory was manually modified, discovery groups them into one logical item and prefers pending state. A subsequent acknowledgement overwrites the stale acknowledged twin. No separate reconciliation pass or acknowledgement-conflict state is added.
7. Delete removes both approved physical variants for the requested logical report ID.
8. Retention groups by logical report ID and keeps the latest five logical reports. When a stale logical report is removed, delete all of its approved variants.
9. Temporary `.tmp` behavior remains unchanged.

The timestamp appears before the acknowledgement suffix, so logical ordering remains filename-derived; report bodies never need to be opened for retention ordering.

## Safe summary discovery

`CrashReportSummaryReader` reads only the schema-v2 header and never becomes a general report parser.

It recognizes only these allowlisted fields:

- `ReportId`;
- `CapturedAtUtc`;
- `BuildId`;
- `OperatingSystem`;
- `ProcessArchitecture`;
- `StageOrMilestone`;
- `ExceptionType`.

The reader:

- stops at `--- EXCEPTION ---`;
- stops after 32 header lines;
- stops after 16 KiB of header text, including a malformed single overlong line;
- never performs an unbounded `ReadLine()` on attacker/hand-edited content beyond the remaining character budget;
- normalizes each string field to at most 256 characters;
- replaces ASCII control characters with spaces;
- trims values and maps empty/missing values to `Unknown`;
- never returns exception/report-body text to the UI.

Both the line and character bounds are intentional: the line bound limits normal parser work, while the character bound prevents one malformed line from allocating unbounded text.

For a corrupt or incomplete header:

- use filename-derived report ID;
- use the filename timestamp when parseable;
- use `Unknown` for unavailable summary fields.

The report remains dismissible, folder-openable, and deletable.

## Inbox contract

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

The public contract takes report IDs, never paths. Every action resolves the current physical artifact internally.

`GetReports()` returns the latest retained logical reports newest-first.

### Disabled runtime behavior

`EmptyCrashReportInbox` follows the existing null-object diagnostics pattern:

```text
GetReports()               -> empty
OpenGitHubIssue(...)        -> success, no-op
OpenReportFolder(...)       -> success, no-op
Dismiss(...)                -> success, no-op
Delete(...)                 -> success, no-op
```

These action calls are unreachable from the title UI because there are no items, and the no-op contract avoids introducing a separate disabled-feature state solely for defensive callers.

## Acknowledgement semantics

A report becomes acknowledged only after one of these succeeds:

- GitHub browser launch;
- report-directory launch;
- `Dismiss`.

This preserves the existing HPA-530 product requirement: successful external handoff counts as acknowledgement even though it does **not** prove an issue was submitted.

Launcher-backed order is:

```text
resolve -> build/validate target -> launch -> acknowledge -> refresh
```

If launch fails, the report remains pending and the panel stays retryable.

If launch succeeds but acknowledgement rename fails, return a bounded acknowledgement error and keep the report pending. The external target may already be open; the user can retry or use Dismiss.

Delete removes the report rather than acknowledging it.

## GitHub issue handoff

The only destination is:

```text
https://github.com/cwchanap/DTXManiaCX/issues/new
```

The generated title/body may contain only:

- report ID;
- app/build ID;
- OS;
- process architecture;
- stage/startup milestone;
- exception type;
- `DTXMANIACX-CRASH-REPORT 2`;
- manual instructions to inspect and attach the `.txt` report.

Every dynamic query value is URI-escaped. Do not embed exception messages, stacks, logs, breadcrumbs, arbitrary report text, paths, usernames, song identity, configuration content, or report contents.

Even though the destination is constant today, validate the final URI before launch because HPA-530 explicitly requires rejection of unexpected launch targets:

- scheme must be `https`;
- host must be `github.com`;
- path must be `/cwchanap/DTXManiaCX/issues/new`.

Keep focused validator tests for wrong scheme, host, repository, and path. They are cheap tripwires on a security-sensitive boundary and match the ticket acceptance criteria.

## External launcher

Keep platform launching in shared crash-reporting code:

```text
DTXMania.Game/Lib/Diagnostics/CrashReporting/ExternalLauncher.cs
```

Do **not** place Windows/macOS implementations under the existing `Platform/` compile split. The game projects compile-remove the opposite folder-picker implementation, which would prevent one test project from exercising both launcher command shapes.

`ExternalLauncher` branches at runtime and exposes internal pure builders such as:

```csharp
internal static ProcessStartInfo CreateWindowsStartInfo(string target);
internal static ProcessStartInfo CreateMacStartInfo(string target);
```

Both builders compile into the shared game assembly and are unit-testable on either development platform.

### Windows

Use `Process.Start` with:

```text
UseShellExecute = true
FileName = <validated URI or internally resolved directory>
```

Never invoke `cmd`, PowerShell, or construct a command-shell string.

### macOS

Use `/usr/bin/open` with `UseShellExecute = false` and separate `ArgumentList` entries:

```text
/usr/bin/open -- <target>
```

Never invoke `sh -c` or concatenate a shell command.

The directory target is always the store-owned crash-report root. Unsupported platform, process-start exception, or non-zero macOS result maps to a bounded retryable error.

## Title-screen UX

### Banner

On title activation, query the inbox and show one compact upper-right banner only when pending reports exist:

```text
CRASH REPORT AVAILABLE
2 reports saved · Click or press F8 to review
```

The banner never steals focus or blocks normal title navigation while closed.

### F8

F8 is a fixed, raw, non-remappable diagnostic shortcut for this ticket.

- Edge-trigger from the already available title keyboard state.
- Do not add an `InputCommandType` or key-binding setting.
- F8 with no retained reports is a no-op.
- If F8 opens/reopens the panel, that frame is consumed so another mapped title action cannot also fire.

### Review panel

Display only:

- report position;
- report ID;
- captured UTC;
- build ID;
- OS / architecture;
- stage/milestone;
- exception type;
- Pending/Acknowledged state.

Actions:

- Previous;
- Next;
- Open GitHub Issue;
- Open Report Folder;
- Dismiss;
- Delete Report.

Delete requires in-panel confirmation. After deletion, select the nearest remaining report; close when none remain.

### Input ownership

`CrashReportNotification` owns its open/closed state, selection, focused action, delete confirmation, error text, and banner/panel hit regions.

Expose one focused method:

```csharp
bool HandleInput(...)
```

`true` means the notification consumed this frame and `TitleStage` must skip its normal `HandleInput()` / `HandleMouseInput()` calls.

Use the existing `InputCommandType` path for remappable panel navigation and the title's existing keyboard/mouse snapshots for F8 and mouse edge detection:

```text
MoveLeft / MoveRight -> previous / next
MoveUp / MoveDown    -> action focus
Activate             -> focused action
Back / Escape        -> close panel
raw F8               -> open/reopen
```

**Back/Escape while the panel is open must close the panel and must never fall through to `TitleStage.RequestExit()` in the same frame.**

### Existing UI abstraction decision

The existing `UIElement/IInputState` and config-overlay patterns were reviewed, but HPA-530 should not force either abstraction into `TitleStage`:

- `UIElement.HandleInput(IInputState)` provides a useful bool-consumed convention and hit testing, but `IInputState` exposes raw input only and not the remappable `InputCommandType` commands required by this panel.
- `InputStateManager` polls keyboard/mouse/gamepad itself; adding a second polling owner beside `TitleStage` would duplicate input state and timing solely for this feature.
- `IConfigOverlayPanel` is config-specific and has no mouse/remappable-command contract; moving/generalizing it would be unrelated refactoring.

Therefore reuse the **single consumed-input pattern and existing virtual-coordinate mapping**, but keep `CrashReportNotification` as a small title-specific component. Geometry remains private constants; no new layout framework is needed.

## Error handling

Show short retryable errors for:

- report no longer exists;
- acknowledgement failed;
- delete failed;
- browser launch failed;
- folder launch failed;
- unsupported external launch.

Never expose raw filesystem/process exception messages.

A corrupt summary renders normalized `Unknown` values and remains actionable.

## Testing strategy

### Storage and reader

Cover:

- pending/ack filename regex and ID derivation;
- newest-first logical ordering and latest-five retention;
- pending+ack duplicate discovery collapsing to one item;
- acknowledgement overwrite of a stale ack twin;
- deletion removing both approved variants;
- valid/corrupt/mismatched headers;
- 32-line and 16-KiB header bounds, including a single huge line;
- 256-character field clamp/control normalization;
- no reads past the exception marker;
- HPA-529 capture still emitting pending `.txt` only.

### GitHub and launcher

Cover:

- allowed summary fields only;
- dynamic URI escaping;
- exact scheme/host/path validation and rejection cases;
- Windows `UseShellExecute = true`, with no command shell;
- macOS `/usr/bin/open`, separate `--` and target arguments;
- unsupported/start/non-zero failures;
- folder target restricted to the store-owned root.

### Inbox actions

Using a temporary real store and fake launcher:

- successful GitHub/folder launch acknowledges;
- failed launch leaves pending;
- launch success + acknowledgement failure is retryable;
- Dismiss acknowledges without launch;
- Delete removes the logical report.

### Title UI

Keep state tests on `CrashReportNotification` and only a thin guard test in `TitleStage`:

- no reports -> no banner;
- pending count banner;
- raw F8/banner click opens and consumes the frame;
- acknowledged reports remain reviewable;
- Back/Escape closes without requesting exit;
- menu actions cannot leak through consumed notification input;
- delete confirmation and retryable errors.

## Risks

1. **Filename-policy drift** — use one helper/regex for discovery, retention, acknowledge, and delete.
2. **Back/Escape leakage** — one bool-consumed notification input guard plus regression test.
3. **Corrupt header UI/URI abuse** — bounded reads, normalized fields, URI escaping.
4. **Platform test blind spot** — keep both command builders in shared code rather than platform compile-split files.

## Acceptance criteria

- No retained reports means no banner and unchanged title behavior.
- Pending reports produce one non-blocking summarized banner.
- Only schema-v2 text reports are supported; no `inbox-state.json` or ZIP path is reintroduced.
- Filename-derived report ID is authoritative; pending/ack forms share one latest-five logical retention policy.
- New crashes still create pending `.txt`, never `.ack.txt`.
- Title UI reads only bounded allowlisted header summary fields.
- Raw F8 and banner click open the panel without input leakage.
- Back/Escape while open closes the panel and never requests game exit.
- Successful GitHub **or report-folder** launch acknowledges only after successful process launch and never deletes the report.
- Dismiss acknowledges without deleting.
- Delete requires confirmation.
- Disabled crash reporting exposes an empty no-op inbox and no banner.
- GitHub target is exactly validated HTTPS `github.com/cwchanap/DTXManiaCX/issues/new`; dynamic query values are escaped.
- Windows uses `UseShellExecute = true`; macOS uses `/usr/bin/open` with separate `--` and target arguments; neither uses a command shell.
- Both Windows and macOS command shapes are unit-testable from shared code.
