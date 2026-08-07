# HPA-530 Title-Screen Crash Inbox and GitHub Handoff Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a non-blocking title-screen crash inbox for retained schema-v2 text reports, with persistent acknowledgement, safe GitHub/file-manager handoff, dismissal, and confirmed deletion.

**Architecture:** Extend the shipped HPA-529 text-only crash-reporting subsystem. `CrashReportStore` remains the storage/retention owner, `CrashReportRuntime` composes a narrow `ICrashReportInbox`, `IStageGame` exposes a default empty facade plus the production `BaseGame` forwarding, and `TitleStage` delegates notification state/input to one focused component.

**Tech Stack:** .NET 8, C#, MonoGame, xUnit, Moq, `System.Diagnostics.Process`, existing DTXManiaCX crash-reporting/stage/input abstractions.

## Global constraints

- Support only `DTXMANIACX-CRASH-REPORT 2` text reports.
- Do not add ZIP handling, emergency-report branching, or `inbox-state.json`.
- Keep one latest-five logical retention policy in `CrashReportStore`.
- Valid retained-name regex is case-insensitive: `^crash-\d{8}-\d{6}Z-[0-9a-f]{6}(?:\.ack)?\.txt$`.
- Filename-derived report ID is authoritative.
- Acknowledge with same-directory `File.Move(..., overwrite: true)` from pending `.txt` to `.ack.txt`.
- If both variants exist, discovery returns one logical item; acknowledgement overwrites the stale ack twin; delete removes both variants. Do not add a duplicate-reconciliation pass or conflict error state.
- `CrashReportSummary.FileName` represents the physical basename represented by that summary; title UI does not need it.
- Header reads stop at 32 lines, 16 KiB, or `--- EXCEPTION ---`, whichever comes first; one overlong line must also respect the character budget.
- Normalize header strings to 256 characters, replace ASCII controls with spaces, trim, and map empty values to `Unknown`.
- Never expose paths, parser, store, or launcher directly to `TitleStage`.
- GitHub handoff is manual; URI-escape all dynamic values and validate exact HTTPS host/path before launch.
- Never invoke `cmd /c`, PowerShell, `sh -c`, or concatenate shell commands.
- Keep both Windows/macOS launcher command builders in shared `Lib/Diagnostics/CrashReporting` code, not the `Platform/` compile split.
- Windows uses `UseShellExecute = true`; macOS uses `/usr/bin/open` with separate `--` and target arguments.
- Successful GitHub **and report-folder** launches acknowledge after process launch, matching HPA-530 acceptance criteria.
- F8 is a fixed raw, non-remappable title shortcut; do not add `InputCommandType` or configuration.
- `CrashReportNotification.HandleInput(...) -> bool` is the single notification input-ownership seam; consumed input must not reach title actions.
- Do not add a second `InputStateManager` to `TitleStage` solely for this feature.
- `EmptyCrashReportInbox` returns no reports and silent successful no-op actions.
- Linux launcher support is out of scope.

---

## File structure

**Create:**

- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportSummaryReader.cs`
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportInbox.cs`
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/GitHubCrashIssueBuilder.cs`
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/ExternalLauncher.cs`
- `DTXMania.Game/Lib/Stage/CrashReportNotification.cs`
- focused tests under `DTXMania.Test/CrashReporting/` and `DTXMania.Test/Stage/`

**Modify:**

- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs`
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportStore.cs`
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportRuntime.cs`
- `DTXMania.Game/Lib/Stage/IStageGame.cs`
- `DTXMania.Game/Game1.cs`
- `DTXMania.Game/Lib/Stage/TitleStage.cs`
- focused existing contract tests as needed

Do not move/generalize `IConfigOverlayPanel`, create a generic modal/process/layout abstraction, or introduce a new key-binding setting.

---

## Platform-aware test commands

Use the test project that matches the environment:

**macOS local development:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

**Windows / Windows CI:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

For each filtered command below, use the same project substitution. New tests under `DTXMania.Test/CrashReporting/` and `DTXMania.Test/Stage/` are picked up automatically by the Mac test project's glob unless they introduce excluded graphics dependencies; these tests must remain graphics-independent.

---

### Task 1: Add schema-v2 discovery and acknowledgement

**Files:**
- Create: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportSummaryReader.cs`
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs`
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportStore.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportSummaryReaderTests.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportStoreTests.cs`

**Produces:** `CrashReportInboxItem`, `CrashReportActionResult`, `ICrashReportInbox`, `EmptyCrashReportInbox`, plus internal store discovery/ack/delete/root behavior used later.

- [ ] **Step 1: Lock filename and logical-ID behavior with failing tests**

Use exact valid examples:

```text
crash-20260806-123456Z-a1b2c3.txt
crash-20260806-123456Z-a1b2c3.ack.txt
```

Assert:

```text
pending ID -> crash-20260806-123456Z-a1b2c3
ack ID     -> crash-20260806-123456Z-a1b2c3
```

Ignore malformed names and `.tmp` files.

- [ ] **Step 2: Lock the minimal duplicate behavior**

Create both variants for one ID and assert:

- discovery returns one logical item;
- pending wins while both exist;
- `Acknowledge(reportId)` moves pending onto `.ack.txt` with overwrite and leaves one acknowledged artifact;
- `Delete(reportId)` removes both approved variants if both exist;
- retention counts the pair as one logical report.

Do **not** add `Cleanup()` duplicate reconciliation or an acknowledgement-conflict error.

- [ ] **Step 3: Add bounded summary-reader tests**

Cover:

- valid schema-v2 header;
- missing/corrupt header;
- filename/header ID mismatch;
- more than 32 header lines;
- more than 16 KiB before the exception marker;
- one single line larger than 16 KiB;
- 10,000-character field value;
- embedded ASCII controls/newlines;
- empty field values;
- huge exception body after `--- EXCEPTION ---`.

Expected fallback:

```text
ReportId            <- filename-derived
CapturedAtUtc       <- valid header, else filename timestamp when parseable
BuildId             <- normalized header or Unknown
OperatingSystem     <- normalized header or Unknown
ProcessArchitecture <- normalized header or Unknown
StageOrMilestone    <- normalized header or Unknown
ExceptionType       <- normalized header or Unknown
FileName            <- current physical basename
```

- [ ] **Step 4: Implement `CrashReportSummaryReader` minimally**

Implement one bounded schema-v2 header parser. The 16-KiB cap must be enforced while reading, not after an unbounded `ReadLine()` has already allocated a huge line.

Do not parse report-body sections.

- [ ] **Step 5: Extend `CrashReportStore` around one filename policy**

Use one retained-name helper for discovery, retention, acknowledgement, and delete.

Required behavior:

- expose `internal string RootPath => _rootPath` for composition;
- discover newest-first logical reports;
- group physical variants by filename-derived ID;
- pending wins if both variants exist;
- acknowledge pending with same-directory `File.Move(..., overwrite: true)`;
- already-acknowledged report is idempotent success;
- delete both approved variants for the requested ID;
- latest-five retention operates on logical IDs and deletes all variants for stale IDs;
- no report-body reads for ordering.

- [ ] **Step 6: Add public inbox contracts and empty singleton**

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

`EmptyCrashReportInbox`:

```text
GetReports()               -> empty
OpenGitHubIssue(...)        -> success/no-op
OpenReportFolder(...)       -> success/no-op
Dismiss(...)                -> success/no-op
Delete(...)                 -> success/no-op
```

- [ ] **Step 7: Run Task 1 tests**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~CrashReporting"
```

Windows/CI:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~CrashReporting"
```

Verify existing capture tests still create only pending `crash-*.txt` names and logical retention remains five.

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

**Produces:** production `CrashReportInbox` and shared, testable launcher command builders.

- [ ] **Step 1: Write GitHub URL tests first**

Generated destination must be exactly:

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
- schema-v2 identifier;
- manual `.txt` attachment instructions.

Assert every dynamic value is escaped. Keep rejection tests for HTTP, wrong host, wrong repository, and wrong path because the Linear acceptance criteria explicitly require target validation.

- [ ] **Step 2: Implement `GitHubCrashIssueBuilder`**

Keep it deterministic and side-effect free. Return a validated `Uri`; do not accept a configurable destination.

- [ ] **Step 3: Write shared launcher command-shape tests**

`ExternalLauncher.cs` stays outside `Platform/` so both builders compile into the Mac and Windows game projects.

Expose internal pure builders:

```csharp
CreateWindowsStartInfo(string target)
CreateMacStartInfo(string target)
```

Windows assertions:

```text
FileName = <target>
UseShellExecute = true
no cmd.exe
no powershell
```

macOS assertions:

```text
FileName = /usr/bin/open
UseShellExecute = false
ArgumentList[0] = --
ArgumentList[1] = <target>
no sh -c
```

This deliberately does **not** follow the folder-picker `Platform/` compile split, because the opposite platform file is removed at compile time and would make cross-platform command-shape tests impossible from one test assembly.

- [ ] **Step 4: Implement `ExternalLauncher`**

Branch at runtime (`RuntimeInformation`/equivalent), not by platform-specific source-file inclusion.

Support only:

- validated GitHub URI;
- internally resolved crash-report root directory.

Map unsupported platform, process-start exception, null process, and non-zero macOS exit to stable failure codes. Do not introduce a general process execution framework.

- [ ] **Step 5: Write inbox action tests**

Use a temporary real store and fake launcher.

Assert:

- successful GitHub launch -> acknowledge;
- successful folder launch -> acknowledge;
- failed launch -> remain pending;
- launch success + acknowledgement failure -> bounded retryable acknowledgement error;
- Dismiss -> acknowledge without launch/delete;
- Delete -> remove the logical report.

The external-launch acknowledgement behavior is intentional and required by HPA-530; do not change it to Dismiss-only semantics in this task.

- [ ] **Step 6: Implement `CrashReportInbox` orchestration**

Every action resolves by report ID at call time. Never accept a path from the caller.

Launcher-backed order:

```text
resolve -> build/validate -> launch -> acknowledge -> refresh
```

Never surface raw filesystem/process exception messages.

- [ ] **Step 7: Run Task 2 tests**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~CrashReporting"
```

Windows/CI:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~CrashReporting"
```

- [ ] **Step 8: Commit**

```bash
git add DTXMania.Game/Lib/Diagnostics/CrashReporting DTXMania.Test/CrashReporting
git commit -m "feat: add crash report github handoff"
```

---

### Task 3: Compose through existing game/stage seams

**Files:**
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs`
- Modify: `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportRuntime.cs`
- Modify: `DTXMania.Game/Lib/Stage/IStageGame.cs`
- Modify: `DTXMania.Game/Game1.cs`
- Test: `DTXMania.Test/CrashReporting/CrashReportRuntimeTests.cs`
- Test: `DTXMania.Test/Stage/IStageGameContractTests.cs`
- Test: `DTXMania.Test/BaseGameTests.cs`

- [ ] **Step 1: Add diagnostics contract tests**

`IGameCrashDiagnostics` exposes only:

```csharp
ICrashReportInbox CrashReportInbox { get; }
```

for this feature. Do not expose store, launcher, parser, root path, or runtime lifetime operations.

- [ ] **Step 2: Extend enabled/disabled runtime behavior**

Enabled runtime exposes the production inbox. Disabled/bootstrap-degraded runtime exposes `EmptyCrashReportInbox.Instance`.

- [ ] **Step 3: Compose the production inbox in `CrashReportRuntime`**

Reuse the runtime-owned `CrashReportStore`; use `store.RootPath` for folder handoff so the open-directory target cannot diverge from the injected store.

Compose summary reader, GitHub builder, launcher, and inbox here only.

- [ ] **Step 4: Add default stage property and production forwarding**

`IStageGame`:

```csharp
ICrashReportInbox CrashReportInbox => EmptyCrashReportInbox.Instance;
```

`BaseGame` explicitly forwards the real diagnostics inbox.

Do not modify every test stub solely for this property.

- [ ] **Step 5: Run Task 3 tests**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~CrashReportRuntime|FullyQualifiedName~IStageGameContractTests|FullyQualifiedName~BaseGameTests"
```

Windows/CI:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~CrashReportRuntime|FullyQualifiedName~IStageGameContractTests|FullyQualifiedName~BaseGameTests"
```

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportContracts.cs DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportRuntime.cs DTXMania.Game/Lib/Stage/IStageGame.cs DTXMania.Game/Game1.cs DTXMania.Test
git commit -m "feat: expose crash inbox to title stage"
```

---

### Task 4: Add title banner and review panel

**Files:**
- Create: `DTXMania.Game/Lib/Stage/CrashReportNotification.cs`
- Modify: `DTXMania.Game/Lib/Stage/TitleStage.cs`
- Test: `DTXMania.Test/Stage/CrashReportNotificationTests.cs`
- Test: `DTXMania.Test/Stage/TitleStageTests.cs`

**Produces:** one title-specific notification component with `HandleInput(...) -> bool consumed`.

- [ ] **Step 1: Write component state tests before drawing code**

Use fake `ICrashReportInbox`; no filesystem, process, or graphics device.

Cover:

- zero reports -> hidden/closed;
- pending reports -> one summarized banner;
- acknowledged-only -> no banner but F8 can review retained items;
- open defaults to newest pending, else newest retained;
- Previous/Next clamps at ends;
- successful Dismiss closes and refreshes;
- delete requires confirmation;
- delete selects nearest remaining item or closes when empty;
- action errors stay visible/retryable.

- [ ] **Step 2: Lock input ownership and F8 behavior**

Tests assert:

- raw F8 uses the title's current/previous keyboard snapshots and is edge-triggered;
- no new `InputCommandType` is needed;
- F8 with no reports returns not-consumed;
- F8/banner click that opens/reopens returns consumed;
- while open, panel navigation/actions consume input;
- Back/Escape closes and consumes;
- delete confirmation consumes confirm/cancel until resolved.

- [ ] **Step 3: Implement `CrashReportNotification` as a focused title component**

Own:

- snapshot/selected index;
- open/closed state;
- action focus;
- delete confirmation;
- bounded error text;
- banner/panel hit regions;
- draw helpers.

Expose:

```csharp
bool HandleInput(...)
```

Use existing `InputCommandType` checks for remappable panel commands and the title's already-polled keyboard/mouse snapshots for F8/click edges.

Do not instantiate a second `InputStateManager`. Do not move/generalize `IConfigOverlayPanel`. `UIElement.HandleInput` is the naming/consumption precedent, not a requirement to restructure `TitleStage` input around `IInputState`.

- [ ] **Step 4: Integrate activation/refresh into `TitleStage`**

Create/refresh after title resources are available. Clear only stage-owned notification state during deactivation/disposal; runtime/inbox remains process-owned.

Use existing `MapMouseToVirtual` for notification mouse coordinates.

- [ ] **Step 5: Add one explicit consumed-input guard**

After the title updates current/previous keyboard and mouse states:

```csharp
if (_crashReportNotification?.HandleInput(/* current title input */) == true)
{
    return;
}
```

Only then run the existing title `HandleInput()` / `HandleMouseInput()` path.

Panel commands:

```text
MoveLeft / MoveRight -> previous / next
MoveUp / MoveDown    -> action focus
Activate             -> focused action
Back / Escape        -> close panel
raw F8               -> open/reopen
```

Critical regression: open-panel Back/Escape closes the panel and **never calls `RequestExit()` in the same frame**.

- [ ] **Step 6: Draw after the existing title menu**

Reuse title font/white-pixel/SpriteBatch resources. Keep geometry private to `CrashReportNotification`; do not add skin assets or a layout framework.

Render only:

```text
position
report ID
captured UTC
build ID
OS / architecture
stage / milestone
exception type
Pending / Acknowledged
```

Never render report-body content.

- [ ] **Step 7: Add one thin `TitleStage` leakage regression test**

Keep detailed state tests in `CrashReportNotificationTests`. `TitleStageTests` need only prove:

- consumed notification input skips GAME START / CONFIG / EXIT;
- open-panel Back/Escape cannot call `RequestExit()`;
- non-consumed closed state still reaches existing title menu behavior.

- [ ] **Step 8: Run Task 4 tests**

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~DTXMania.Test.Stage|FullyQualifiedName~CrashReporting"
```

Windows/CI:

```bash
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "FullyQualifiedName~DTXMania.Test.Stage|FullyQualifiedName~CrashReporting"
```

- [ ] **Step 9: Perform platform smoke verification**

macOS: verify production `/usr/bin/open -- <target>` for both GitHub URI and crash-report directory and record OS/runtime/results in the implementation PR.

Windows: verify the shared command-builder tests assert `UseShellExecute = true` and no shell construction; CI supplies Windows execution coverage.

- [ ] **Step 10: Commit**

```bash
git add DTXMania.Game/Lib/Stage/CrashReportNotification.cs DTXMania.Game/Lib/Stage/TitleStage.cs DTXMania.Test/Stage
git commit -m "feat: add title crash report inbox ui"
```

---

## Final verification

Before completing HPA-530:

- [ ] On macOS run `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`; on Windows/CI run `dotnet test DTXMania.Test/DTXMania.Test.csproj`.
- [ ] Confirm new crash captures still create pending `crash-*.txt`, never `.ack.txt`.
- [ ] Confirm exact retained-name policy rejects arbitrary files and groups pending/ack variants by logical ID.
- [ ] Confirm acknowledgement overwrites a stale ack twin without a separate reconciliation/error subsystem.
- [ ] Confirm pending+ack logical reports together never exceed latest five.
- [ ] Confirm reader stops at 32 lines, 16 KiB, and the exception marker; a single giant line is bounded.
- [ ] Confirm long/control-containing fields are normalized and cannot create malformed UI/URI values.
- [ ] Confirm all GitHub dynamic values are escaped and exact HTTPS host/path validation rejects unexpected targets.
- [ ] Confirm shared launcher code exposes testable Windows and macOS `ProcessStartInfo` command shapes without `Platform/` compile splitting.
- [ ] Confirm disabled/bootstrap crash reporting produces no banner and no-op inbox behavior.
- [ ] Confirm raw F8 is edge-triggered/non-remappable and consumes only when opening/reopening the panel.
- [ ] Confirm Back/Escape with the panel open closes it and does **not** call `RequestExit()`.
- [ ] Confirm notification-consumed input cannot trigger GAME START / CONFIG / EXIT in the same frame.
- [ ] Confirm successful GitHub/folder launch acknowledges but does not delete.
- [ ] Confirm failed launch remains pending/retryable.
- [ ] Confirm Dismiss persists acknowledgement.
- [ ] Confirm Delete requires confirmation and removes all approved variants for that logical ID.
- [ ] Confirm macOS uses `/usr/bin/open`, separate `--`, and separate target; Windows uses `UseShellExecute = true`; neither uses a command shell.
- [ ] Confirm no `inbox-state.json`, ZIP support, automatic upload, generic modal/process/layout framework, new key-binding setting, or Linux launcher was introduced.
