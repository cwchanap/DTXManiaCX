# HPA-187 Windows Auto-Update Implementation Plan

> **For agentic workers:** implement on this same PR. Keep this as a focused Windows auto-update feature, not a packaging migration or notification framework.

**Goal:** On normal Windows launches, check once from `TitleStage` for a newer stable GitHub Release, show a title-only offer, download and verify the existing Inno installer, then silently upgrade and relaunch.

## Non-negotiable constraints

- One PR for HPA-187.
- Reuse GitHub Releases + Inno Setup + stable `AppId`.
- No Velopack/Squirrel/MSIX/helper updater.
- Update UI exists only on `TitleStage`.
- Passive failures are invisible; explicit update failures are retryable.
- Exact asset: `DTXMania-Setup-X.Y.Z.exe` with valid GitHub `sha256:` digest.
- No download progress %, persisted skip, channels, release-notes UI, or cancel flow.
- `CrashReportNotification` remains separate and has first input priority.
- Processes carrying `DTXMANIA_LAUNCH_TOKEN` use the disabled updater and never call GitHub.
- Available/Failed update actions are mouse-only; never bind title `Activate`/Enter or Back.
- Real Windows old-version -> new-version silent upgrade/relaunch is a merge gate.

## Expected files

**Create/keep focused:**

- `DTXMania.Game/Lib/Update/ApplicationVersion.cs`
- `DTXMania.Game/Lib/Update/GameUpdateContracts.cs`
- `DTXMania.Game/Lib/Update/GitHubReleaseClient.cs`
- `DTXMania.Game/Lib/Update/GameUpdateService.cs`
- `DTXMania.Game/Lib/Update/WindowsUpdateInstallerLauncher.cs`
- `DTXMania.Game/Lib/Stage/GameUpdateNotification.cs`
- focused tests under `DTXMania.Test/Update/` and `DTXMania.Test/Stage/`

**Modify:**

- `.github/workflows/release.yml`
- `installer/windows/dtxmania.iss`
- `DTXMania.Game/Lib/Stage/TitleStage.cs`
- `DTXMania.Game/Lib/Stage/StartupStage.cs`
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashContextPublisher.cs`
- `DTXMania.Game/Lib/Diagnostics/CrashReporting/CrashReportStore.cs`
- `DTXMania.Game/Lib/Stage/IStageGame.cs`
- `DTXMania.Game/Game1.cs`
- existing focused tests that pin these contracts

Combine trivially small update files rather than adding layers.

---

## Task 1 — Establish one application-version reader

### Scope

Create `ApplicationVersion` as the only code that reads assembly version metadata.

It must expose:

- normalized `System.Version` / `X.Y.Z` display value;
- informational/build identifier for crash reports, preserving revision metadata when present.

### Work

- [ ] Add normalization tests for release/informational version values and safe fallback.
- [ ] Replace Title's hard-coded `DTXManiaCX v1.0.0 - MonoGame Edition` with `ApplicationVersion` display output.
- [ ] Replace Startup's hard-coded version line with the same helper.
- [ ] Update `StartupStageLogicTests` and any Title expectations.
- [ ] Change `CrashContextPublisher` to obtain build/application version through `ApplicationVersion`; no direct `AssemblyInformationalVersionAttribute` read remains there.
- [ ] Change `CrashReportStore.GetBuildId()` to obtain its value through `ApplicationVersion`; no private duplicate reflection remains.
- [ ] Pass the existing `APP_VERSION` into both Windows and macOS release publish commands with `-p:Version=<APP_VERSION>`.
- [ ] Search the whole game/test tree for stale `v1.0.0` literals and remove/update relevant production/test expectations.

### Tests

- pure `ApplicationVersion` tests;
- existing Startup/Title version-display tests;
- focused crash tests proving the build ID still contains the helper-provided informational value.

### Commit

```text
feat: unify runtime application version
```

---

## Task 2 — Add one-shot stable-release discovery

### Contract

Keep the stage-facing facade small:

```csharp
public interface IGameUpdateService
{
    GameUpdateSnapshot GetSnapshot();
    void CheckOnce();
    void BeginUpdate();
    void DismissForProcess();
}
```

Use immutable snapshots and a disabled singleton.

### GitHub identity

- [ ] Build the GitHub API repository path from `GitHubCrashIssueBuilder.TargetOwner` and `TargetRepository`.
- [ ] Do not add another GitHub identity service/config setting.
- [ ] Request only `/repos/{owner}/{repo}/releases/latest` with a stable User-Agent and bounded timeout.

### Parsing/state

- [ ] Accept only `vX.Y.Z`.
- [ ] Require exact `DTXMania-Setup-X.Y.Z.exe`.
- [ ] Require valid `sha256:<64 hex>` digest before publishing `Available`.
- [ ] Equal/newer current version -> no offer.
- [ ] Malformed/network/JSON/unusable release -> silent passive state.
- [ ] `CheckOnce()` is idempotent for the service/process lifetime.
- [ ] `DismissForProcess()` is memory-only.

Normal tests use fakes and make zero real GitHub requests.

### Commit

```text
feat: add one-shot github update discovery
```

---

## Task 3 — Download, follow redirects, verify, and launch

### HTTP/download

Production composition must use an `HttpClientHandler` with automatic redirects explicitly enabled and a bounded redirect/timeout policy.

- [ ] Stream the installer to a version-scoped temp `.exe`.
- [ ] SHA-256 the final downloaded bytes; do not buffer the whole installer in memory.
- [ ] Match digest case-insensitively.
- [ ] Mismatch -> launcher not called, bad temp file cleaned best-effort, retryable `Failed`.
- [ ] Retry starts a fresh download.

### Required redirect test

Add one focused test/integration-style test where:

```text
browser_download_url -> redirect -> final installer body
```

The expected digest must be calculated from the **final redirected body**. Also pin production redirect configuration so a fake 200 response on the original URL cannot be the only coverage.

### Installer launcher

Do **not** reuse crash-report `ExternalLauncher`.

Build a direct `ProcessStartInfo` for the verified `.exe`:

```text
/VERYSILENT
/SUPPRESSMSGBOXES
/NORESTART
/CLOSEAPPLICATIONS
/AUTOUPDATE
```

- [ ] no `cmd`, PowerShell, or shell strings;
- [ ] arguments are separate `ArgumentList` items;
- [ ] successful `Process.Start` -> `InstallerLaunched`;
- [ ] service does not exit the game or wait for installer completion.

### Commit

```text
feat: download and verify windows updates
```

---

## Task 4 — Extend Inno auto-update mode

Preserve installer identity and existing file/app-data ownership.

- [ ] Detect `/AUTOUPDATE` case-insensitively without persistent mode state.
- [ ] Keep normal interactive `[Run]` launch behavior.
- [ ] Add silent auto-update `[Run]` behavior that relaunches `{app}\DTXMania.Game.Windows.exe` after successful installation.
- [ ] Ensure normal install cannot double-launch.
- [ ] Correct `MyAppURL` from the old owner to `https://github.com/cwchanap/DTXManiaCX` while touching the installer.
- [ ] Ensure the Inno script still compiles in the release workflow.

Do not change stable `AppId`, install location, default skin replacement, or per-user custom-data ownership.

### Commit

```text
feat: add inno auto-update mode
```

---

## Task 5 — Compose updater and title-only UX

### IStageGame / BaseGame

- [ ] Add default `IGameUpdateService GameUpdateService => DisabledGameUpdateService.Instance` to `IStageGame`.
- [ ] `BaseGame` owns one service instance.
- [ ] Use the real service only when:

```text
OperatingSystem.IsWindows()
&& string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DTXMANIA_LAUNCH_TOKEN"))
```

- [ ] All non-Windows and launch-token-owned processes use the disabled singleton.
- [ ] Do not add a user config setting or another updater environment variable.
- [ ] Add composition tests for Windows/no-token, Windows/token, and non-Windows decisions using a narrow testable helper if direct OS testing is awkward.

This keeps E2E/video-recorder/MCP/benchmark launches off `api.github.com` and prevents update UI from perturbing automation.

### Start hook

- [ ] Call `CheckOnce()` from `TitleStage.OnFirstUpdate()`.
- [ ] Do not wait for `OnTransitionCompleted()`.
- [ ] Re-entry calls are harmless because the service gate is idempotent.

### Notification

`GameUpdateNotification` owns only update UI state/input/drawing. Reuse Title's font/SpriteBatch/white pixel and private geometry below the crash banner.

#### Available / Failed

Update actions are **mouse hit-test only**.

- [ ] Mouse `UPDATE NOW`, `LATER`, `RETRY` work and consume only the click frame.
- [ ] Never consume/bind `Activate`, Enter, Move commands, or Back in Available/Failed.
- [ ] Required regression: Available banner visible + title `Activate` -> GAME START still receives the action.

#### Downloading / LaunchingInstaller

- [ ] Consume title menu movement/activation so Start/Config cannot be entered immediately before installer launch.
- [ ] Back/Escape must still fall through to Title's existing exit path.
- [ ] No cancel-update flow.

#### Priority

```text
1. CrashReportNotification
2. GameUpdateNotification
3. normal TitleStage menu
```

Crash consumption means updater does not inspect the same frame.

- [ ] `InstallerLaunched` causes exactly one `_game.RequestExit()` from the game thread.

### Commit

```text
feat: add title-stage update notification
```

---

## Task 6 — Verification gate

Run focused tests first, then platform full suites.

**macOS:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

**Windows:**

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

Also verify:

- [ ] release workflow stamps `APP_VERSION` on Windows + macOS publish;
- [ ] automation-owned launches have disabled updater/no GitHub access;
- [ ] Available banner does not steal Enter/GAME START;
- [ ] redirect download hashes final content;
- [ ] Inno compile succeeds.

### Real Windows smoke test — hard stop

Use two real release-like builds:

```text
install older version
 -> launch old game
 -> reach Title
 -> offer newer release / or invoke verified updater path
 -> download + verify
 -> launch installer with exact arguments
 -> old game exits/releases files
 -> installer upgrades successfully
 -> new game relaunches
 -> Title/Startup version show new X.Y.Z
```

If `/CLOSEAPPLICATIONS` plus Title's exit request is unreliable, **stop implementation and revise HPA-187**. Do not add a helper process or packaging migration within this ticket.

## Done when

- one `ApplicationVersion` reader serves Title, Startup, crash reporting, and updater comparison;
- normal Windows launches check once; `DTXMANIA_LAUNCH_TOKEN` launches never check;
- update banner is Title-only and does not steal normal Activate/Enter;
- exact release asset is redirect-downloaded and SHA-256 verified before installer launch;
- Inno silent auto-update upgrades and relaunches reliably in a real Windows smoke test;
- PR remains one focused implementation PR.
