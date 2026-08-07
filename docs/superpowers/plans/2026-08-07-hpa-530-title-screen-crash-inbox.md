# HPA-530 Title-Screen Crash Inbox and GitHub Handoff Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a non-blocking title-screen crash inbox for retained schema-v2 text reports, with persistent acknowledgement, safe GitHub/file-manager handoff, dismissal, and confirmed deletion.

**Architecture:** Extend the existing HPA-529 crash-reporting subsystem instead of recreating its superseded ZIP/inbox-state design. `CrashReportStore` remains the storage/retention owner, `CrashReportRuntime` composes a narrow `ICrashReportInbox`, `IStageGame` exposes only that facade, and `TitleStage` delegates banner/panel behavior to a focused notification component.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, Moq, `System.Diagnostics.Process`, existing DTXManiaCX crash-reporting and stage abstractions.

## Global Constraints

- Support only the shipped `DTXMANIACX-CRASH-REPORT 2` plain-text format.
- Do not add ZIP handling, emergency-report branching, or `inbox-state.json`.
- Keep one latest-five retention policy owned by `CrashReportStore`.
- Encode acknowledgement by atomically renaming `*.txt` to `*.ack.txt`.
- Never expose crash-report paths, parsers, `CrashReportStore`, or `IExternalLauncher` directly to `TitleStage`.
- The title UI may render only `CrashReportSummary` plus acknowledgement state.
- GitHub handoff is manual: open the new-issue page and instruct the player to inspect and attach the `.txt` report themselves.
- Never invoke `cmd /c`, PowerShell, `sh -c`, or concatenate dynamic values into shell commands.
- Windows launcher uses `ProcessStartInfo.UseShellExecute = true`.
- macOS launcher uses `/usr/bin/open` with separate `--` and target arguments.
- Linux launcher support is out of scope.
- All failures are non-fatal and must not block normal title-screen startup.

---

## File structure

Expected new files:

- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportSummaryReader.cs` — bounded schema-v2 header parsing.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportInbox.cs` — inbox facade and action orchestration.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/ExternalLauncher.cs` — validated Windows/macOS external launch behavior.
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/GitHubCrashIssueBuilder.cs` — deterministic GitHub URL construction/validation.
- `DTXMania.Game/Lib/Stage/CrashReportNotification.cs` — title-only banner/panel state and rendering/input coordination.
- focused tests under `DTXMania.Test/CrashReporting/` and `DTXMania.Test/Stage/`.

Expected modified files:

- `CrashReportContracts.cs` — public inbox contract and no-op implementation.
- `CrashReportStore.cs` — discovery, acknowledgement, deletion, and retention recognition for `.ack.txt`.
- `CrashReportRuntime.cs` — compose inbox and expose it through `IGameCrashDiagnostics`.
- `IStageGame.cs` — expose `ICrashReportInbox`.
- `Game1.cs` — forward the injected inbox through `BaseGame`.
- `TitleStage.cs` — instantiate/update/draw the notification component and gate normal input while the panel is open.
- existing crash/stage contract tests where required by public interface changes.

Do not create a generic modal framework or a generic process runner beyond the smallest test seam required by this ticket.

---

### Task 1: Add schema-v2 discovery and persistent acknowledgement

**Files:**
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportSummaryReader.cs`
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs`
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportStore.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportSummaryReaderTests.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportStoreTests.cs`

**Interfaces:**
- Produces: `CrashReportInboxItem`, `ICrashReportInbox` contract types used by later tasks.
- Produces: `CrashReportStore.GetReports()`, `Acknowledge(reportId)`, and `Delete(reportId)`-style internal operations; exact method visibility should follow existing store testability conventions.
- Consumes: existing `CrashReportSummary`, `CrashReportTextWriter.Header`, and `AppPaths.GetCrashReportsRoot()` behavior.

- [ ] **Step 1: Lock the filename/state contract with failing tests**

Add tests proving the store recognizes both:

```text
crash-20260806-123456Z-a1b2c3.txt
crash-20260806-123456Z-a1b2c3.ack.txt
```

The tests must assert that both forms participate in one newest-first retained set and one latest-five retention limit.

- [ ] **Step 2: Add bounded header-reader tests**

Cover a valid schema-v2 header, missing keys, corrupt header version, and a report containing a very large exception section. The reader must stop at `--- EXCEPTION ---` and return only the approved summary fields.

For corrupt headers assert:

```text
ReportId            <- filename-derived
CapturedAtUtc       <- filename timestamp when parseable
BuildId             <- Unknown
OperatingSystem     <- Unknown
ProcessArchitecture <- Unknown
StageOrMilestone    <- Unknown
ExceptionType       <- Unknown
```

- [ ] **Step 3: Implement the minimal header reader**

Parse only the leading schema-v2 header with a small fixed line/character bound. Do not expose a general report parser and do not read report sections after the exception marker.

- [ ] **Step 4: Implement discovery and acknowledgement in `CrashReportStore`**

Update filename recognition so pending and acknowledged forms are both valid retained reports. Acknowledge by same-directory atomic rename from the pending form to `.ack.txt`; treat an already acknowledged report as success/idempotent.

Deletion resolves the current filename by report ID and deletes only files inside the configured crash-report root.

- [ ] **Step 5: Add the public inbox data contract and no-op implementation**

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

Also add an empty implementation returning no reports and bounded failure/no-op action results suitable for a disabled crash runtime.

- [ ] **Step 6: Run focused tests**

Run the crash-report summary/store tests plus the existing crash-reporting suite. Verify existing HPA-529 capture still writes the pending `.txt` form and retention still caps the combined set at five.

- [ ] **Step 7: Commit**

Commit this slice independently with a message similar to:

```text
feat: add crash report inbox storage state
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
- Consumes: Task 1 store operations and `ICrashReportInbox` contract.
- Produces: production `CrashReportInbox` used by `CrashReportRuntime` in Task 3.
- Produces: internal `IExternalLauncher` seam used only by the inbox and tests.

- [ ] **Step 1: Write GitHub URL-builder tests first**

Assert the generated URL targets exactly:

```text
https://github.com/cwchanap/DTXManiaCX/issues/new
```

Allow only report ID, build ID, OS, architecture, stage/milestone, exception type, schema-v2 format identifier, and manual `.txt` attachment instructions in the title/body query values.

Add rejection tests for HTTP, alternate hosts, alternate repositories, and alternate GitHub paths.

- [ ] **Step 2: Implement `GitHubCrashIssueBuilder`**

Keep URL construction deterministic and side-effect free. Return a `Uri`; keep validation in the same focused component so callers cannot bypass host/path constraints accidentally.

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
ArgumentList[1] = <validated URI or internal directory>
no sh -c
```

Also cover unsupported platform, start exception, and non-zero macOS result as bounded failures.

- [ ] **Step 4: Implement `IExternalLauncher`**

Support URI and directory launch only. Directory launch receives the internally resolved crash-report root from composition; do not accept arbitrary stage-supplied paths.

- [ ] **Step 5: Write inbox action tests**

Using a real `CrashReportStore` rooted in a temporary test directory plus a fake launcher, assert:

- successful GitHub launch acknowledges the selected report;
- successful folder launch acknowledges the selected report;
- launcher failure leaves it pending;
- `Dismiss` acknowledges without deleting;
- `Delete` deletes whichever filename currently represents the report;
- launcher success followed by acknowledgement failure returns an acknowledgement error and leaves the report pending.

- [ ] **Step 6: Implement `CrashReportInbox` orchestration**

Each public action resolves the report by ID at call time. Never accept a path from the caller. For launcher-backed actions, perform `validate -> launch -> acknowledge` in that order.

Map raw filesystem/process exceptions to short stable error codes; never surface raw exception messages through the public result.

- [ ] **Step 7: Run focused tests**

Run all new crash-inbox/launcher tests plus the existing crash-reporting suite.

- [ ] **Step 8: Commit**

Commit this slice independently with a message similar to:

```text
feat: add crash report github handoff
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
- Produces: `IGameCrashDiagnostics.CrashReportInbox` and `IStageGame.CrashReportInbox` used by `TitleStage` in Task 4.

- [ ] **Step 1: Add contract tests for the new narrow property**

Extend diagnostics/stage contract tests so `BaseGame` exposes an injected `ICrashReportInbox` without exposing `CrashReportStore`, launcher, or runtime lifetime methods.

- [ ] **Step 2: Extend `IGameCrashDiagnostics`**

Add:

```csharp
ICrashReportInbox CrashReportInbox { get; }
```

The disabled runtime must expose the no-op inbox created in Task 1.

- [ ] **Step 3: Compose the production inbox in `CrashReportRuntime`**

Reuse the same `CrashReportStore` instance already owned by the runtime. Compose `GitHubCrashIssueBuilder`, the platform launcher, and the internally resolved crash-report root there.

Do not create crash-report services from `BaseGame` or `TitleStage`.

- [ ] **Step 4: Forward through `BaseGame` / `IStageGame`**

Add a read-only `CrashReportInbox` property to `IStageGame` and forward the injected diagnostics inbox from `BaseGame`.

Update test-only `IStageGame` stubs with the no-op inbox; do not add nullable checks throughout stage code.

- [ ] **Step 5: Run contract/runtime tests**

Run the diagnostics runtime, `IStageGame`, and `BaseGame` tests. Verify the crash-disabled bootstrap path still starts successfully with an empty inbox.

- [ ] **Step 6: Commit**

Commit this slice independently with a message similar to:

```text
feat: expose crash inbox to title stage
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

- [ ] **Step 1: Write notification state-machine tests before rendering code**

Use a fake `ICrashReportInbox` and test the component without filesystem or real process launches.

Cover:

- zero reports -> hidden banner and closed panel;
- pending reports -> one banner with pending count;
- acknowledged-only reports -> no banner but retained reports remain reviewable through F8;
- open panel -> selected report defaults to newest pending report, otherwise newest retained report;
- Previous/Next wraps or clamps consistently; choose one behavior and assert it in tests;
- successful dismiss closes the panel and refreshes counts;
- delete requires confirmation;
- successful delete selects the nearest remaining report or closes when empty;
- action failures keep the panel open and expose a retryable error state.

Prefer clamp-at-ends navigation unless an existing title UI convention clearly favors wrapping.

- [ ] **Step 2: Implement `CrashReportNotification` as a title-specific component**

Keep it focused on:

- current inbox snapshot;
- selected index;
- panel open/closed state;
- focused action;
- delete confirmation state;
- short visible error code/message;
- banner/panel geometry and draw helpers.

Do not turn it into a reusable modal framework.

- [ ] **Step 3: Integrate activation and refresh into `TitleStage`**

On title activation, create/refresh the notification from `_game.CrashReportInbox` after normal title resources are available. The lookup is bounded to the retained latest-five reports.

On deactivation/disposal, clear stage-owned notification state only; the inbox/runtime remains process-owned.

- [ ] **Step 4: Gate input correctly**

In `OnUpdate`:

1. update keyboard/mouse states as today;
2. process F8/banner activation;
3. if the crash panel is open, route input to it first and skip normal title-menu `HandleInput()` / `HandleMouseInput()` for that frame;
4. otherwise preserve current title behavior exactly.

Use existing virtual mouse mapping and existing remappable commands where practical:

```text
MoveLeft / MoveRight -> previous / next report
MoveUp / MoveDown    -> action focus
Activate             -> execute focused action
Back / Escape        -> close panel
```

When delete confirmation is active, consume only confirm/cancel actions.

- [ ] **Step 5: Draw the banner and panel after the existing title menu**

Keep drawing inside the existing title `SpriteBatch` flow. Use existing title font/primitive resources where possible instead of introducing new skin assets in this ticket.

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

Never render report-body content.

- [ ] **Step 6: Extend `TitleStageTests` for input leakage**

Add tests around extracted/internal helper seams as needed so:

- F8 opens the crash panel without selecting a title menu item;
- panel activation input cannot trigger GAME START / CONFIG / EXIT in the same frame;
- closed-panel behavior still uses the existing title menu path unchanged.

Avoid constructing real MonoGame graphics devices merely to test state transitions.

- [ ] **Step 7: Run focused and full relevant tests**

Run the new stage/component tests, existing `TitleStageTests`, stage contract tests, and crash-reporting tests. Then run the repository's standard unit-test command used by CI.

- [ ] **Step 8: Perform supported-platform smoke verification**

On macOS, verify the production launcher can open both:

```text
/usr/bin/open -- https://github.com/cwchanap/DTXManiaCX/issues/new
/usr/bin/open -- <resolved CrashReports directory>
```

Record OS/runtime details and observed results in the implementation PR. Do not claim automated macOS E2E coverage.

On Windows, confirm the unit tests inspect `UseShellExecute = true` and prove no command-shell construction exists.

- [ ] **Step 9: Commit**

Commit this slice independently with a message similar to:

```text
feat: add title crash report inbox ui
```

---

## Final verification

Before marking HPA-530 complete:

- [ ] Run the complete crash-reporting test suite.
- [ ] Run the complete title/stage test suite.
- [ ] Run the repository's normal Windows/macOS unit-test CI commands locally where available.
- [ ] Confirm HPA-529 crash capture still creates `crash-*.txt`, never `.ack.txt`, for a newly captured crash.
- [ ] Confirm title activation with no reports is behaviorally unchanged.
- [ ] Confirm one pending report shows one non-blocking banner.
- [ ] Confirm successful GitHub/folder launch acknowledges but does not delete.
- [ ] Confirm launcher failure keeps the report pending and retryable.
- [ ] Confirm dismiss persists acknowledgement across restart.
- [ ] Confirm confirmed delete removes the report and refreshes the count.
- [ ] Confirm acknowledged and pending reports together never exceed the existing latest-five retention limit.
- [ ] Confirm no `inbox-state.json`, ZIP support, shell command construction, automatic upload, or Linux launcher code was introduced.
