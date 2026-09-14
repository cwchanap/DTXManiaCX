# HPA-187 Windows Auto-Update Implementation Plan

> **For agentic workers:** implement on this same PR. Task 0 is a hard gate: do not write the updater production code until the existing Inno path is proven.

**Goal:** On normal Windows launches, check once from `TitleStage` for a newer stable GitHub Release, notify only there, download and verify the current Inno installer, then upgrade and relaunch without replacing the packaging stack.

## Non-negotiable constraints

- One PR for HPA-187.
- Reuse GitHub Releases + Inno Setup + stable `AppId`.
- No Velopack/Squirrel/MSIX/helper updater.
- macOS auto-install stays out of scope.
- `CrashReportNotification` stays separate and keeps first input priority.
- `DTXMANIA_LAUNCH_TOKEN` processes never call GitHub.
- Closed update banner must not steal title `Activate` / Enter.
- No persisted skip, channels, release-notes UI, or cancel subsystem.
- Lightweight percentage text is allowed; no progress-bar framework.

## Expected code shape

Create/keep focused:

- `DTXMania.Game/Lib/Update/ApplicationVersion.cs`
- `DTXMania.Game/Lib/Update/GameUpdateContracts.cs`
- `DTXMania.Game/Lib/Update/GitHubReleaseClient.cs`
- `DTXMania.Game/Lib/Update/GameUpdateService.cs`
- `DTXMania.Game/Lib/Update/WindowsUpdateInstallerLauncher.cs`
- `DTXMania.Game/Lib/Stage/GameUpdateNotification.cs`
- focused tests under `DTXMania.Test/Update/` and `DTXMania.Test/Stage/`

Modify existing release/installer/stage/crash files only where required. Do not edit the Mac test project merely to include the new test folders; its current compile glob already picks them up.

---

## Task 0 — Prove the existing Inno update path first

**Purpose:** validate the one assumption that can invalidate the whole design before building C# around it.

Build two Windows installers from current `main` with different test versions and otherwise identical content.

### Current-user mode

- [ ] Install the older package for current user.
- [ ] Launch the installed game.
- [ ] Start the newer installer manually with:

```text
/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /AUTOUPDATE
```

- [ ] Confirm the old process releases installed files.
- [ ] Confirm the same installation root is upgraded.
- [ ] Confirm the new game relaunches from that root.

### All-users mode

Repeat from an all-users installation using the same stable `AppId` and baseline updater arguments.

- [ ] Confirm Inno reuses the previous all-users installation rather than creating a per-user shadow install.
- [ ] Confirm the required elevation flow is acceptable.
- [ ] Explicitly cancel/fail UAC once and record the behavior needed from the launcher; the current game must remain alive when launch/elevation fails.
- [ ] Confirm successful upgrade/relaunch from the same all-users install root.

Do **not** add `/DIR`, `/ALLUSERS`, `/CURRENTUSER`, registry inspection, or path-based mode inference unless this test proves the existing previous-install reuse is insufficient. If explicit overrides or a different close/exit sequence are needed, update the design before continuing.

If the flow cannot be reliable without a helper updater or packaging migration, stop HPA-187.

Record the proven argument/process-start contract in the PR before Task 1.

---

## Task 1 — Establish one normalized application-version reader

Create `ApplicationVersion` as the only game-code reader of assembly version metadata.

- [ ] Normalize both local assembly version and parsed GitHub tag to exactly three components before comparison.
- [ ] Expose `X.Y.Z` display value and informational/build identifier.
- [ ] Replace Title and Startup hard-coded version lines.
- [ ] Make `CrashContextPublisher` and `CrashReportStore.GetBuildId()` delegate to the helper; remove their private reflection copies.
- [ ] Update `StartupStageLogicTests` and other pinned version expectations.
- [ ] Pass existing `APP_VERSION` to both Windows and macOS publish commands with `-p:Version=<APP_VERSION>`.
- [ ] Search game/tests for stale `v1.0.0` production/test expectations.

Tests must include `1.2.3` versus an assembly-style `1.2.3.0` normalization case and equal-version behavior.

**Commit:** `feat: unify runtime application version`

---

## Task 2 — Add one-shot stable-release discovery and diagnostics

Keep the stage-facing facade small:

```csharp
IGameUpdateService
  GetSnapshot()
  CheckOnce()
  BeginUpdate()
  DismissForProcess()
```

- [ ] Reuse `GitHubCrashIssueBuilder.TargetOwner` / `TargetRepository` to build the latest-release API URL.
- [ ] Accept only stable `vX.Y.Z` and exact `DTXMania-Setup-X.Y.Z.exe` with valid `sha256:` digest.
- [ ] Compare only normalized three-component versions.
- [ ] `CheckOnce()` is process-idempotent; Later is memory-only.
- [ ] Passive rejection produces no player UI.
- [ ] Log one bounded reason code through the existing `LoggerFactory` path (`not_newer`, `asset_missing`, `digest_missing`, `invalid_tag`, HTTP/network class, etc.).
- [ ] Never log response bodies to the UI or add a telemetry subsystem.

Normal discovery tests use fake handlers and make no real GitHub requests.

**Commit:** `feat: add one-shot github update discovery`

---

## Task 3 — Stream download, redirect, hash, temp cleanup, and launch

### Temp/download behavior

- [ ] Use one version-scoped temp installer path.
- [ ] Before each attempt, delete/best-effort clean a stale file already at that path.
- [ ] Stream response bytes to disk; do not buffer the installer.
- [ ] Compute SHA-256 from the final downloaded bytes.
- [ ] Digest mismatch or explicit download/write failure -> retryable `Failed`; launcher is not called.
- [ ] Retry always starts from a fresh temp file.

### Lightweight progress

If `Content-Length` exists, update the immutable snapshot with an integer percent. If unavailable, leave it null. UI text may render `(43%)`; no progress bar/cancel/history.

### Redirect proof

Keep fake-handler tests for ordinary state/error cases, but add one loopback integration-style test using the production `HttpClient`/handler stack:

```text
GET /asset -> 302 Location: /real
GET /real  -> installer bytes
```

Hash must match `/real` bytes. This replaces a fake-handler redirect test plus an `AllowAutoRedirect` property assertion.

### Installer launcher

Do not reuse crash `ExternalLauncher`; copying its narrow injected process-start seam is fine.

- [ ] Build the process start from the exact contract proven in Task 0.
- [ ] Never invoke a shell.
- [ ] Catch `Win32Exception`/process-start failure, including elevation cancellation, as retryable `Failed`.
- [ ] Observe the started process's FIRST exit without a deadline — liveness is never commitment because an unanswered internal UAC prompt has no safe timeout: nonzero exit (whenever it happens) = refused elevation → retryable `Failed`; exit 0 = committed elevated respawn or finished no-elevation install → `InstallerCommitted`; still running = undecided, game stays alive at `InstallerLaunched` while Inno `/CLOSEAPPLICATIONS` owns closing it once an install proceeds.
- [ ] Failed or cancelled handoff does not publish `InstallerCommitted` and does not exit the game.
- [ ] Committed exit publishes `InstallerCommitted`; service does not call game APIs.

**Commit:** `feat: download and verify windows updates`

---

## Task 4 — Extend Inno auto-update mode

Preserve stable installer identity and existing install/app-data ownership.

- [ ] Detect `/AUTOUPDATE` case-insensitively without persisted mode state.
- [ ] Keep normal interactive `[Run]` launch behavior.
- [ ] Add silent auto-update relaunch behavior after successful install.
- [ ] Prevent normal install double-launch.
- [ ] Correct stale `MyAppURL` to `https://github.com/cwchanap/DTXManiaCX`.
- [ ] Keep the previous-directory/previous-privilege behavior proven in Task 0; do not introduce explicit mode/directory arguments unless the approved Task-0 result requires them.
- [ ] Ensure release workflow still compiles the script.

**Commit:** `feat: add inno auto-update mode`

---

## Task 5 — Compose service and title-only update UX

### Composition

Add the disabled updater default on `IStageGame`. `BaseGame` owns one service instance.

Create a pure composition predicate:

```csharp
ShouldEnable(bool isWindows, string? launchToken)
```

Test exactly:

- Windows + empty token -> enabled;
- Windows + token -> disabled;
- non-Windows -> disabled.

Use one game-side launch-token environment-variable constant from both BaseGame/updater composition and `JsonRpcServer`; do not introduce another game-code literal or a cross-project shared package.

Call `CheckOnce()` from `TitleStage.OnFirstUpdate()`; the service owns the one-shot gate.

### Notification UX

Follow `CrashReportNotification`'s input convention, not the earlier mouse-only variant.

Closed available banner:

```text
UPDATE AVAILABLE — vX.Y.Z
Press F9 or click to review
```

- [ ] closed banner consumes no title `Activate`, movement, or Back;
- [ ] raw edge-triggered non-remappable F9 opens the panel and consumes that frame;
- [ ] banner click is the mouse equivalent;
- [ ] required regression: banner visible + title Activate still selects GAME START.

Open panel:

- [ ] use existing remappable input commands for focus/action;
- [ ] Available actions: Update Now / Later;
- [ ] Failed actions: Retry / Later;
- [ ] Back closes the review panel while idle/failed, matching the focused component pattern;
- [ ] crash panel remains first priority.

Downloading/launching:

- [ ] render `DOWNLOADING UPDATE — vX.Y.Z (N%)` when percent is known, otherwise omit percent;
- [ ] prevent Start/Config navigation/activation during the operation;
- [ ] no cancel-update subsystem;
- [ ] normal title Back/exit remains available as specified by the final focused component behavior.

### Installer terminal state

At the top-level `TitleStage.OnUpdate()` lifecycle, independently of the `_titlePhase == Normal && _currentPhase == Normal` input gate:

- [ ] observe `InstallerCommitted` (never `InstallerLaunched` — an undecided handoff must not exit);
- [ ] request game exit exactly once.

Do not hide this terminal-state observation inside `GameUpdateNotification.HandleInput()`.

**Commit:** `feat: add title-stage update notification`

---

## Task 6 — Full verification and regression re-check

Run focused updater tests and both applicable full test suites.

macOS:

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Windows:

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

Verify:

- [ ] both release publish legs stamp `APP_VERSION`;
- [ ] launch-token-owned game processes use disabled updater/no GitHub;
- [ ] passive failures are player-silent but diagnostically logged;
- [ ] equal version normalization does not offer an update;
- [ ] loopback 302 test hashes the final body;
- [ ] stale temp file is replaced on a fresh attempt;
- [ ] closed banner does not steal Enter/GAME START;
- [ ] F9/click opens update panel;
- [ ] progress text appears when Content-Length exists;
- [ ] `InstallerCommitted` exits from `OnUpdate` once; `InstallerLaunched` does not exit;
- [ ] Inno compile succeeds.

Finally repeat the Task-0 current-user **and** all-users real upgrade/relaunch flows with the completed product path. If either mode regresses, stop and revise rather than adding a helper updater inside this PR.

## Done when

- Task 0 and final smoke tests prove both supported installer modes;
- one normalized application-version source serves every current consumer;
- normal Windows launches check once and harness-owned launches never check;
- update UI is Title-only, keyboard/controller accessible through F9, and never steals closed-banner GAME START input;
- redirect-downloaded installer is fresh-file SHA-256 verified with lightweight progress text;
- elevation/start/handoff failures are retryable without exiting the game;
- proven Inno auto-update upgrades and relaunches the same installation;
- PR remains one focused implementation PR.
