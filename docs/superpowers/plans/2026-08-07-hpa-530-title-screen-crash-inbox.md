# HPA-530 Title-Screen Crash Inbox and GitHub Handoff Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a non-blocking title-screen crash inbox for retained schema-v2 text reports, with persistent acknowledgement, safe GitHub/file-manager handoff, dismissal, and confirmed deletion.

**Architecture:** Extend the existing HPA-529 crash-reporting subsystem instead of recreating its superseded ZIP/inbox-state design. `CrashReportStore` remains the storage/retention owner, `CrashReportRuntime` composes a narrow `ICrashReportInbox`, `IStageGame` exposes a default empty facade plus the production override from `BaseGame`, and `TitleStage` delegates all crash-notification interaction state to a focused `CrashReportNotification` component.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, Moq, `System.Diagnostics.Process`, existing DTXManiaCX crash-reporting and stage abstractions.

## Global Constraints

- Support only the shipped `DTXMANIACX-CRASH-REPORT 2` plain-text format.
- Do not add ZIP handling, emergency-report branching, or `inbox-state.json`.
- Keep one latest-five retention policy owned by `CrashReportStore`.
- Retained filename regex is case-insensitive: `^crash-\d{8}-\d{6}Z-[0-9a-f]{6}(?:\.ack)?\.txt$`.
- Filename-derived report ID is authoritative for inbox identity and actions.
- Encode acknowledgement by atomically renaming pending `*.txt` to `*.ack.txt`.
- Collapse a pending+ack duplicate pair to one logical inbox item; pending wins until cleanup reconciles the duplicate.
- `CrashReportSummary.FileName` from discovery is the current physical basename; title UI must not display or depend on it.
- Header reads are bounded to 32 lines / 16 KiB; string values are normalized to at most 256 characters with ASCII controls replaced by spaces and empty values mapped to `Unknown`.
- Never expose crash-report paths, parsers, `CrashReportStore`, or `IExternalLauncher` directly to `TitleStage`.
- The title UI may render only `CrashReportSummary` plus acknowledgement state.
- GitHub handoff is manual: open the new-issue page and instruct the player to inspect and attach the `.txt` report themselves.
- URI-escape every dynamic GitHub query value before launch.
- Never invoke `cmd /c`, PowerShell, `sh -c`, or concatenate dynamic values into shell commands.
- Windows launcher uses `ProcessStartInfo.UseShellExecute = true`.
- macOS launcher uses `/usr/bin/open` with separate `--` and target arguments.
- Linux launcher support is out of scope.
- F8 is a fixed, raw, non-remappable title diagnostic shortcut for this ticket; do not add a new `InputCommandType`.
- `CrashReportNotification` owns notification input and returns whether it consumed the frame; consumed input must never reach normal title actions.
- `EmptyCrashReportInbox` returns no reports and successful no-op action results.
- All failures are non-fatal and must not block normal title-screen startup.

---

## File structure

Expected new files:

- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportSummaryReader.cs` — bounded schema-v2 header parsing and value normalization.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportInbox.cs` — inbox facade and action orchestration.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/ExternalLauncher.cs` — validated Windows/macOS external launch behavior.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/GitHubCrashIssueBuilder.cs` — deterministic GitHub URL construction/validation.
- `DTXMania.Game/Lib/Stage/CrashReportNotification.cs` — title-only banner/panel state, hit-testing, input ownership, and rendering coordination.
- focused tests under `DTXMania.Test/CrashReporting/` and `DTXMania.Test/Stage/`.

Expected modified files:

- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs` — public inbox contract, `IGameCrashDiagnostics.CrashReportInbox`, and no-op implementation.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportStore.cs` — discovery, filename identity, duplicate reconciliation, acknowledgement, deletion, internal `RootPath`, and retention recognition for `.ack.txt`.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportRuntime.cs` — compose inbox and expose it through `IGameCrashDiagnostics`.
- `DTXMania.Game/Lib/Stage/IStageGame.cs` — default `CrashReportInbox => EmptyCrashReportInbox.Instance` property.
- `DTXMania.Game/Game1.cs` — forward the injected production inbox through `BaseGame`.
- `DTXMania.Game/Lib/Stage/TitleStage.cs` — instantiate/update/draw the notification component and honor its consumed-input result.
- existing crash/stage contract tests where required by public interface changes.

Do not create a generic modal framework, generic process runner, new layout framework, new key-binding configuration, or configurable GitHub destination.

## Risks to keep visible during implementation

1. **Duplicate pending/ack variants** can create ghost items or inconsistent retention if storage methods invent different filename rules. Lock identity behavior in Task 1 before other work.
2. **Back/Escape leakage** can call `TitleStage.RequestExit()` while the panel is open. Make `CrashReportNotification` the single input-ownership boundary and verify the consumed-frame guard.
3. **Hand-edited/corrupt headers** can create very large or control-character-containing UI/URI values. Clamp and normalize in the reader, then URI-escape in the builder.
4. **F8 can also be mapped elsewhere**. Treat raw F8 as the title diagnostic shortcut and consume the frame when it opens/reopens the panel; do not expand the key-binding system.

---

### Task 1: Add schema-v2 discovery and persistent acknowledgement

**Files:**
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportSummaryReader.cs`
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs`
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportStore.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportSummaryReaderTests.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportStoreTests.cs`

**Interfaces:**
- Produces: `CrashReportInboxItem`, `CrashReportActionResult`, `ICrashReportInbox`, and `EmptyCrashReportInbox` used by later tasks.
- Produces: internal `CrashReportStore.GetReports()`, `Acknowledge(reportId)`, `Delete(reportId)`, and `RootPath` behavior used by Task 2/3.
- Consumes: existing `CrashReportSummary`, `CrashReportTextWriter.Header`, and the store root configured by HPA-529.

- [ ] **Step 1: Lock retained filename and logical identity behavior with failing tests**

Use these exact valid examples:

```text
crash-20260806-123456Z-a1b2c3.txt
crash-20260806-123456Z-a1b2c3.ack.txt
```

Assert:

```text
regex: ^crash-\d{8}-\d{6}Z-[0-9a-f]{6}(?:\.ack)?\.txt$
report id from pending: crash-20260806-123456Z-a1b2c3
report id from ack:     crash-20260806-123456Z-a1b2c3
```

Also assert malformed names and `.tmp` files are ignored.

- [ ] **Step 2: Lock duplicate-variant recovery behavior**

Create both physical variants for one report ID and assert:

- discovery returns one logical item, not two;
- pending is canonical and `IsAcknowledged == false`;
- `CrashReportSummary.FileName` is the pending basename;
- `Cleanup()` removes the acknowledged duplicate;
- `Delete(reportId)` removes both approved variants if both still exist;
- the duplicate pair counts as one logical report for latest-five retention.

- [ ] **Step 3: Add bounded header-reader tests**

Cover:

- valid schema-v2 header;
- missing keys;
- corrupt header version;
- filename/header `ReportId` mismatch;
- report containing a very large exception section;
- more than 32 header lines;
- more than 16 KiB before the exception marker;
- 10,000-character `BuildId`;
- control characters in `ExceptionType` and other string fields;
- empty string fields.

Expected normalization:

```text
ReportId            <- filename-derived authoritative value
CapturedAtUtc       <- header when valid, otherwise filename timestamp when parseable
BuildId             <- bounded normalized value or Unknown
OperatingSystem     <- bounded normalized value or Unknown
ProcessArchitecture <- bounded normalized value or Unknown
StageOrMilestone    <- bounded normalized value or Unknown
ExceptionType       <- bounded normalized value or Unknown
FileName            <- current physical basename
```

The reader must stop before `--- EXCEPTION ---` and never return free-form report content.

- [ ] **Step 4: Implement the minimal `CrashReportSummaryReader`**

Implement only the schema-v2 header contract:

```text
max header lines: 32
max header characters: 16 KiB
max normalized string field: 256 chars
ASCII controls: replace with spaces
trimmed empty value: Unknown
```

Do not add a general crash-report parser.

- [ ] **Step 5: Extend `CrashReportStore` around one filename policy**

Use one retained-name helper/regex for discovery, cleanup, acknowledgement, deletion, and retention.

Required behavior:

- expose `internal string RootPath => _rootPath` for crash-runtime composition;
- discover pending and acknowledged forms newest-first;
- group by filename-derived report ID;
- cleanup acknowledged duplicate when a pending twin exists;
- acknowledge by same-directory rename;
- if acknowledgement sees both variants, remove the ack duplicate first; return a bounded conflict error if reconciliation fails rather than deleting the pending canonical file;
- treat already-acknowledged reports as idempotent success;
- delete both approved variants for a report ID;
- apply latest-five retention to logical reports after duplicate reconciliation.

Do not open report bodies for retention ordering; the timestamp prefix is already sortable.

- [ ] **Step 6: Add the public inbox contract and silent empty implementation**

Add:

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

`EmptyCrashReportInbox` behavior is exact:

```text
GetReports()                  -> empty
OpenGitHubIssue(reportId)     -> success, no-op
OpenReportFolder(reportId)    -> success, no-op
Dismiss(reportId)             -> success, no-op
Delete(reportId)              -> success, no-op
```

- [ ] **Step 7: Run Task 1 verification**

Run:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~CrashReporting"
```

Verify existing HPA-529 capture tests still create pending `crash-*.txt`, never `.ack.txt`, and the combined retained logical set stays capped at five.

- [ ] **Step 8: Commit**

```bash
git add DTXMania.Game/Lib/Diagnostics/CrashReporting DTXMania.Test/CrashReporting
git commit -m "feat: add crash report inbox storage state"
```

---

### Task 2: Add safe GitHub and platform handoff

**Files:**
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/GitHubCrashIssueBuilder.cs`
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/ExternalLauncher.cs`
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportInbox.cs`
- Test: `DTXMania.Test/CrashReporting/GitHubCrashIssueBuilderTests.cs`
- Test: `DTXMania.Test/CrashReporting/ExternalLauncherTests.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportInboxTests.cs`

**Interfaces:**
- Consumes: Task 1 store operations, `RootPath`, normalized `CrashReportSummary`, and `ICrashReportInbox` contract.
- Produces: production `CrashReportInbox` used by `CrashReportRuntime` in Task 3.
- Produces: internal `IExternalLauncher` seam used only by inbox composition and tests.

- [ ] **Step 1: Write GitHub URL-builder tests first**

Assert the generated URL targets exactly:

```text
https://github.com/cwchanap/DTXManiaCX/issues/new
```

Allow only:

- report ID;
- build ID;
- OS;
- architecture;
- stage/milestone;
- exception type;
- `DTXMANIACX-CRASH-REPORT 2`;
- manual `.txt` attachment instructions.

Add tests proving:

- HTTP is rejected;
- alternate host is rejected;
- alternate repository/path is rejected;
- 10,000-character source values are already clamped by the reader/builder boundary;
- embedded control/newline-style input cannot break query structure;
- every dynamic query value is URI-escaped;
- report-body content never appears in the URI.

- [ ] **Step 2: Implement `GitHubCrashIssueBuilder`**

Keep it deterministic and side-effect free. Build from the normalized summary, URI-escape dynamic values, return a `Uri`, and validate scheme/host/path in this component before returning a launchable target.

Do not add configuration for the one supported repository.

- [ ] **Step 3: Write external-launcher tests around `ProcessStartInfo`**

Use the smallest injectable process-start seam necessary to inspect the launch request without starting a real process.

Windows assertions:

```text
UseShellExecute = true
no cmd.exe
no powershell
no composed command shell string
```

macOS assertions:

```text
FileName = /usr/bin/open
UseShellExecute = false
ArgumentList[0] = --
ArgumentList[1] = <validated URI or CrashReportStore.RootPath>
no sh -c
```

Also cover unsupported platform, process-start exception, and non-zero macOS result as bounded failures.

- [ ] **Step 4: Implement `IExternalLauncher`**

Support URI and directory launch only. Directory launch receives `CrashReportStore.RootPath` from crash-reporting composition; do not accept arbitrary stage-supplied paths.

Follow the existing repository pattern of creating testable `ProcessStartInfo` rather than introducing a general process-execution service.

- [ ] **Step 5: Write inbox action tests with a real temporary store plus fake launcher**

Assert:

- successful GitHub launch acknowledges the selected pending report;
- successful folder launch acknowledges the selected pending report;
- launcher failure leaves it pending;
- `Dismiss` acknowledges without deleting;
- `Delete` removes the logical report;
- dual pending+ack delete leaves no ghost variant;
- launcher success followed by acknowledgement failure returns a bounded acknowledgement error and preserves the pending canonical file;
- missing report returns a bounded stable error rather than a raw exception.

- [ ] **Step 6: Implement `CrashReportInbox` orchestration**

Each public action resolves the report by ID at call time. Never accept a path from the caller.

Launcher-backed order is exact:

```text
resolve -> build/validate target -> launch -> acknowledge -> refresh
```

Map raw filesystem/process exceptions to short stable error codes; never surface raw exception messages through the public result.

- [ ] **Step 7: Run Task 2 verification**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~CrashReporting"
```

- [ ] **Step 8: Commit**

```bash
git add DTXMania.Game/Lib/Diagnostics/CrashReporting DTXMania.Test/CrashReporting
git commit -m "feat: add crash report github handoff"
```

---

### Task 3: Compose the inbox through the existing game/stage seams

**Files:**
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs`
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportRuntime.cs`
- Modify: `DTXMania.Game/Lib/Stage/IStageGame.cs`
- Modify: `DTXMania.Game/Game1.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportRuntimeTests.cs`
- Test: `DTXMania.Test/Stage/IStageGameContractTests.cs`
- Test: `DTXMania.Test/BaseGameTests.cs`

**Interfaces:**
- Consumes: production `CrashReportInbox` from Task 2.
- Produces: `IGameCrashDiagnostics.CrashReportInbox` and production `BaseGame.CrashReportInbox` used by `TitleStage` in Task 4.
- Keeps test-only/simple `IStageGame` implementations source-compatible through a default empty inbox property.

- [ ] **Step 1: Add contract tests for the narrow diagnostics property**

Assert `IGameCrashDiagnostics` exposes:

```csharp
ICrashReportInbox CrashReportInbox { get; }
```

and does not expose `CrashReportStore`, launcher, parser, root path, or crash-runtime lifetime operations.

- [ ] **Step 2: Extend `IGameCrashDiagnostics` and disabled runtime behavior**

Enabled runtime returns the composed production inbox.

Disabled/bootstrap-degraded runtime returns `EmptyCrashReportInbox.Instance`; verify it produces no reports and never creates title-visible action errors.

- [ ] **Step 3: Compose the production inbox in `CrashReportRuntime`**

Reuse the same `CrashReportStore` instance already owned by the runtime. Construct the summary reader, GitHub builder, external launcher, and inbox there.

Use `CrashReportStore.RootPath` for directory handoff so production and injected test stores cannot diverge from the path opened by the inbox.

Do not create crash-report services from `BaseGame` or `TitleStage`.

- [ ] **Step 4: Add the default `IStageGame` property and production forwarding**

Add:

```csharp
ICrashReportInbox CrashReportInbox => EmptyCrashReportInbox.Instance;
```

to `IStageGame` as a default interface member.

`BaseGame` explicitly forwards the real injected diagnostics inbox.

Do **not** update every test stub solely to satisfy this property; existing minimal implementations should inherit the default behavior.

- [ ] **Step 5: Run Task 3 verification**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~CrashReportRuntime|FullyQualifiedName~IStageGameContractTests|FullyQualifiedName~BaseGameTests"
```

Verify the crash-disabled bootstrap path still starts successfully and stage stubs that do not override `CrashReportInbox` receive the empty singleton.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportRuntime.cs DTXMania.Game/Lib/Stage/IStageGame.cs DTXMania.Game/Game1.cs DTXMania.Test
git commit -m "feat: expose crash inbox to title stage"
```

---

### Task 4: Add the non-blocking title banner and review panel

**Files:**
- Create: `DTXMania.Game/Lib/Stage/CrashReportNotification.cs`
- Modify: `DTXMania.Game/Lib/Stage/TitleStage.cs`
- Test: `DTXMania.Test/Stage/CrashReportNotificationTests.cs`
- Test: `DTXMania.Test/Stage/TitleStageTests.cs`

**Interfaces:**
- Consumes: `IStageGame.CrashReportInbox` from Task 3.
- Produces: final player-facing title-screen recovery UX.
- Produces: `CrashReportNotification.TryHandleInput(...) -> bool`, where `true` means the normal title input path must not run that frame.

- [ ] **Step 1: Write notification state-machine tests before rendering code**

Use a fake `ICrashReportInbox` and test without filesystem, graphics-device, or real process launches.

Cover:

- zero reports -> hidden banner and closed panel;
- pending reports -> one banner with pending count;
- acknowledged-only reports -> no banner but retained reports remain reviewable through F8;
- opening defaults to newest pending report, otherwise newest retained report;
- Previous/Next clamps at ends;
- successful dismiss closes the panel and refreshes counts;
- delete requires confirmation;
- successful delete selects the nearest remaining report or closes when empty;
- action failures keep the panel open and expose a retryable bounded error;
- private component layout constants produce stable banner/panel hit regions without a new layout abstraction.

- [ ] **Step 2: Lock raw F8 and consumed-input semantics in tests**

Assert:

- F8 is detected from raw previous/current `KeyboardState`, edge-triggered once;
- F8 does not require an `InputCommandType` mapping;
- F8 with no retained reports is a no-op and does not consume normal title input;
- F8 opening/reopening the panel returns `true` from `TryHandleInput` and consumes that frame;
- banner click opening the panel also consumes that frame;
- when the panel is open, all handled navigation/actions return consumed;
- Back/Escape while open closes the panel and returns consumed;
- delete confirmation consumes confirm/cancel input until resolved.

- [ ] **Step 3: Implement `CrashReportNotification` as the sole notification state owner**

Keep it focused on:

- current inbox snapshot;
- selected index;
- panel open/closed state;
- focused action;
- delete confirmation state;
- short visible error code/message;
- banner hit-testing;
- raw F8 edge detection;
- private banner/panel geometry constants;
- draw helpers.

Do not turn it into a reusable modal framework.

- [ ] **Step 4: Integrate activation and refresh into `TitleStage`**

On title activation, create/refresh the notification from `_game.CrashReportInbox` after normal title resources are available. The lookup remains bounded to the latest-five retained logical reports.

On deactivation/disposal, clear stage-owned notification state only; the inbox/runtime remains process-owned.

- [ ] **Step 5: Make input gating one explicit guard**

After updating keyboard/mouse state in `TitleStage.OnUpdate`, call the notification first:

```csharp
if (_crashReportNotification?.TryHandleInput(/* current input state */) == true)
{
    return;
}
```

Only if it returns `false` may existing title `HandleInput()` / `HandleMouseInput()` run.

Required behavior:

```text
MoveLeft / MoveRight -> previous / next report
MoveUp / MoveDown    -> action focus
Activate             -> execute focused action
Back / Escape        -> close panel when open
raw F8               -> open/reopen crash panel
```

The critical regression case is explicit: **Back/Escape with the panel open closes the panel and must never call `RequestExit()` in the same frame.**

- [ ] **Step 6: Draw the banner and panel after the existing title menu**

Keep drawing inside the existing title `SpriteBatch` flow. Reuse existing title font/primitive resources; do not add skin assets.

The panel may render only:

```text
Report position
Report ID
Captured UTC
Build ID
OS / architecture
Stage or milestone
Exception type
Pending / Acknowledged
```

Never render `CrashReportSummary.FileName` or report-body content.

- [ ] **Step 7: Add one thin `TitleStage` input-isolation regression test**

Keep the detailed state tests in `CrashReportNotificationTests`. Add only enough `TitleStageTests` coverage to prove:

- notification-consumed input skips GAME START / CONFIG / EXIT handling;
- open-panel Back/Escape cannot call `RequestExit()`;
- closed notification with no interaction still reaches the existing menu path.

Avoid creating a real MonoGame graphics device merely for state transitions.

- [ ] **Step 8: Run Task 4 verification**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~DTXMania.Test.Stage"
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~CrashReporting"
```

- [ ] **Step 9: Perform supported-platform smoke verification**

On macOS, verify the production launcher can open both:

```text
/usr/bin/open -- https://github.com/cwchanap/DTXManiaCX/issues/new
/usr/bin/open -- <resolved CrashReports directory>
```

Record OS/runtime details and observed results in the implementation PR. Do not claim automated macOS E2E coverage.

On Windows, confirm the unit tests inspect `UseShellExecute = true` and prove no command-shell construction exists.

- [ ] **Step 10: Commit**

```bash
git add DTXMania.Game/Lib/Stage/CrashReportNotification.cs DTXMania.Game/Lib/Stage/TitleStage.cs DTXMania.Test/Stage
git commit -m "feat: add title crash report inbox ui"
```

---

## Final verification

Before marking HPA-530 complete:

- [ ] Run `dotnet test DTXMania.Test/DTXMania.Test.csproj`.
- [ ] Run the repository's normal Windows/macOS unit-test CI commands where available.
- [ ] Confirm HPA-529 crash capture still creates pending `crash-*.txt`, never `.ack.txt`, for a newly captured crash.
- [ ] Confirm the exact retained-name regex rejects arbitrary files and recognizes both pending/ack forms.
- [ ] Confirm dual pending+ack files for the same ID collapse to one inbox item and cleanup reconciles the duplicate.
- [ ] Confirm acknowledged and pending logical reports together never exceed the existing latest-five retention limit.
- [ ] Confirm a 10,000-character or control-character-containing header field cannot become an unbounded/malformed GitHub URI or UI value.
- [ ] Confirm GitHub dynamic query values are URI-escaped and only approved summary fields are present.
- [ ] Confirm title activation with no reports is behaviorally unchanged.
- [ ] Confirm disabled/bootstrap crash reporting produces no banner and silent no-op inbox actions.
- [ ] Confirm one pending report shows one non-blocking banner.
- [ ] Confirm raw F8 is edge-triggered, non-remappable, and consumes the frame only when it opens/reopens the crash panel.
- [ ] Confirm Back/Escape with the panel open closes the panel and does **not** call `RequestExit()`.
- [ ] Confirm panel activation/navigation input cannot leak into GAME START / CONFIG / EXIT in the same frame.
- [ ] Confirm successful GitHub/folder launch acknowledges but does not delete.
- [ ] Confirm launcher failure keeps the report pending and retryable.
- [ ] Confirm dismiss persists acknowledgement across restart.
- [ ] Confirm confirmed delete removes the logical report without leaving a duplicate physical variant.
- [ ] Confirm macOS uses `/usr/bin/open`, separate `--`, and separate target argument.
- [ ] Confirm Windows uses `UseShellExecute = true` without a command shell.
- [ ] Confirm no `inbox-state.json`, ZIP support, shell command construction, automatic upload, generic modal/process framework, new key-binding setting, or Linux launcher code was introduced.
