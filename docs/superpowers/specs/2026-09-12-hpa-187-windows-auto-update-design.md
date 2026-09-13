# HPA-187 Windows Auto-Update — Design

**Date:** 2026-09-12  
**Status:** Approved with review amendments  
**Linear:** HPA-187 — DTXManiaCX: Add Windows auto-update notification and installer flow  
**Scope:** Check once for a newer stable GitHub Release after entering the main title stage, notify only there, and let Windows users download, verify, install, and relaunch through the existing Inno Setup path.

## Decision

Keep the original small shape: GitHub Releases + the existing Inno installer, one process-owned update service, and one title-specific notification component. Do **not** migrate packaging or add a helper updater.

The review found five real integration constraints that are now part of the contract:

1. update actions must not steal the title menu's normal `Activate` / Enter path;
2. automation/MCP-owned game processes must never call GitHub or render update UI;
3. application version reading must be unified across Title, Startup, crash reporting, and updater comparison;
4. the updater must reuse the repository identity already owned by `GitHubCrashIssueBuilder` rather than hard-code a third copy;
5. download tests must prove GitHub asset redirects are followed before hashing the final body.

## Existing seams to reuse

- `.github/workflows/release.yml` already owns semantic `vX.Y.Z` releases and `APP_VERSION`.
- Windows release asset is `DTXMania-Setup-X.Y.Z.exe` and includes a GitHub `sha256:` digest.
- `installer/windows/dtxmania.iss` has a stable `AppId` and already upgrades installer-owned files in place.
- `TitleStage` already composes a focused `CrashReportNotification`; the updater follows that pattern but stays separate.
- `DTXMANIA_LAUNCH_TOKEN` marks harness-owned processes launched by `GameProcessDriver`, MCP, and existing automation tooling.
- `CrashContextPublisher` and `CrashReportStore` already read `AssemblyInformationalVersionAttribute` independently; HPA-187 removes that duplicate assembly-reading logic.
- `StartupStage` and `TitleStage` both currently expose the old hard-coded `v1.0.0` display contract.
- `GitHubCrashIssueBuilder.TargetOwner` / `TargetRepository` already define the canonical GitHub repository identity.

## Product behavior

```text
normal Windows process
    -> TitleStage.OnFirstUpdate()
    -> IGameUpdateService.CheckOnce()
         -> no update / passive failure -> no UI
         -> newer usable release -> Available
              -> mouse UPDATE NOW -> download + verify + launch installer
              -> mouse LATER      -> dismissed for this process

harness-owned process (DTXMANIA_LAUNCH_TOKEN set)
    -> DisabledGameUpdateService
    -> no GitHub traffic
    -> no update UI
```

The check remains process-owned and idempotent. Leaving Title while the check is running does not cancel it; a cached Available result is rendered only when Title is active again.

## One application-version source

Create one small `ApplicationVersion` helper as the **only** place in game code that reads assembly version metadata.

It should expose two useful views without introducing another version service:

- normalized release version (`System.Version`) / display text for `X.Y.Z`;
- build/informational identifier for crash reports, preserving source-revision metadata when present.

Consumers:

- `TitleStage.DrawVersionInfo()` uses the display version;
- `StartupStage.DrawVersionInfo()` uses the same display version;
- `GameUpdateService` compares the normalized release version;
- `CrashContextPublisher` gets its application/build identifier through `ApplicationVersion`;
- `CrashReportStore.GetBuildId()` gets its build identifier through `ApplicationVersion`.

Neither crash class may keep a private reflection copy of the assembly-version read.

`release.yml` must pass its existing `APP_VERSION` into **both** Windows and macOS `dotnet publish` commands using `-p:Version=<APP_VERSION>`. This fixes the shared version display on both packaged builds even though update behavior remains Windows-only.

Task verification must search the whole game/test tree for the old `v1.0.0` literal and update the existing `StartupStageLogicTests` expectation as well as Title tests.

## GitHub release identity

`GitHubReleaseClient` must build the REST repository path from:

```csharp
GitHubCrashIssueBuilder.TargetOwner
GitHubCrashIssueBuilder.TargetRepository
```

Do not add a repository-identity service or another configurable setting just for this feature.

The resulting request is still:

```text
GET https://api.github.com/repos/cwchanap/DTXManiaCX/releases/latest
```

The existing Inno `MyAppURL` currently points at the old owner. Correct it to the canonical repository while touching the installer, but do not attempt cross-language configuration generation.

A usable update requires:

1. exact `vX.Y.Z` stable tag;
2. target version greater than current;
3. exact `DTXMania-Setup-X.Y.Z.exe` asset;
4. non-empty `browser_download_url`;
5. valid `sha256:<64 hex chars>` digest.

Incomplete/malformed passive results are silent.

## Runtime service

Keep one focused subsystem under `DTXMania.Game/Lib/Update/`:

```text
ApplicationVersion
GameUpdateContracts / DisabledGameUpdateService
GitHubReleaseClient
GameUpdateService
WindowsUpdateInstallerLauncher
```

The stage-facing contract remains small:

```csharp
public interface IGameUpdateService
{
    GameUpdateSnapshot GetSnapshot();
    void CheckOnce();
    void BeginUpdate();
    void DismissForProcess();
}
```

The service owns the one-shot gate, remote check, download, SHA-256 verification, temporary file, and installer launch. It publishes immutable UI snapshots. Network/file/process work stays off the game loop.

### Production composition gate

`BaseGame` returns the real updater **only** when both are true:

```text
OperatingSystem.IsWindows()
AND DTXMANIA_LAUNCH_TOKEN is null/empty
```

Otherwise use `DisabledGameUpdateService.Instance`.

This is a harness boundary, not a user-facing feature flag. Do not add config, command-line options, or another environment variable for updater enablement.

The service never exits the game. Successful installer start publishes `InstallerLaunched`; `TitleStage` observes that snapshot and calls `_game.RequestExit()` on the game thread.

## Check timing

Call `CheckOnce()` from `TitleStage.OnFirstUpdate()`.

Do not wait for `OnTransitionCompleted()`: the service is idempotent and network work is background-only, so starting during the title fade is harmless and avoids extra lifecycle coupling.

## Download and redirect contract

Use a production `HttpClientHandler` with automatic redirects explicitly enabled. Keep the timeout bounded.

GitHub `browser_download_url` normally redirects to GitHub's asset host, so the download contract is:

```text
browser_download_url
    -> HTTP redirect(s)
    -> final installer response body
    -> stream to temp file
    -> SHA-256 final bytes
    -> compare release digest
    -> launch only on match
```

Tests must include a redirect case where the first asset response redirects and the expected digest is computed from the **redirected final body**. A test that returns 200 directly from the original `browser_download_url` is not sufficient by itself.

No progress percentage/cache database is needed. Retry starts a fresh download. Digest mismatch removes/best-effort cleans the bad temp file and produces a bounded retryable failure.

## Windows installer launch

Launch the verified installer directly; never invoke a shell.

Fixed arguments:

```text
/VERYSILENT
/SUPPRESSMSGBOXES
/NORESTART
/CLOSEAPPLICATIONS
/AUTOUPDATE
```

`WindowsUpdateInstallerLauncher` is independent from crash-report `ExternalLauncher`; the latter is URI/folder shell-launch behavior and is the wrong abstraction for installer execution.

After `Process.Start` succeeds, publish `InstallerLaunched` and let Title request game exit. Do not wait for the installer process.

## Inno Setup

Preserve stable `AppId`, destination, file ownership, bundled skin replacement, and per-user app-data boundaries.

Add a narrow `/AUTOUPDATE` marker and separate `[Run]` behavior:

- normal install keeps the current interactive post-install launch;
- silent auto-update launches `DTXMania.Game.Windows.exe` after successful installation.

Correct `MyAppURL` to the canonical `cwchanap/DTXManiaCX` repository as part of this edit.

### Hard gate

Run a real Windows old-version -> new-version smoke test with the exact arguments. If `/CLOSEAPPLICATIONS` plus the game-thread exit request cannot reliably release the installed files and relaunch the new build, stop and revise HPA-187. Do not add a helper updater or packaging migration inside this PR.

## Title UX and input ownership

Keep `GameUpdateNotification` separate from `CrashReportNotification` and place its banner directly below the crash banner.

States:

```text
Available:   UPDATE AVAILABLE — vX.Y.Z | UPDATE NOW | LATER
Downloading: DOWNLOADING UPDATE — vX.Y.Z
Failed:      UPDATE FAILED | RETRY | LATER
```

No new art is required.

### Available / Failed

These states are **mouse hit-test only** for update actions in this ticket.

They must not consume or bind:

- title `Activate` / Enter;
- Move Up/Down/Left/Right;
- Back/Escape.

Therefore an update banner can be visible while keyboard/game-pad/drum input continues to behave exactly like the normal title menu. Clicking an update action consumes only that mouse-click frame.

A required regression test is:

```text
Available banner visible + Activate pressed -> GAME START still receives Activate
```

### Downloading / LaunchingInstaller

While a user-triggered operation is in flight, block title menu navigation/activation so the player cannot enter Game Start or Config immediately before installer launch. **Back/Escape must still fall through to the existing Title exit path.** Do not add a cancel-update flow.

Crash-report panel input remains first priority. If it consumes the frame, update input is not evaluated.

## Testing

### Version

- normalization/display and informational build identifier;
- Title and Startup use `ApplicationVersion`;
- `CrashContextPublisher` and `CrashReportStore` delegate version reads to the helper;
- no stale `v1.0.0` literal remains in game/tests;
- both release publish legs stamp `APP_VERSION`.

### Discovery/state

- newer/equal/older/malformed release cases;
- exact asset + valid digest requirements;
- one request per process;
- passive failures stay silent;
- repository URL built from existing GitHub constants.

### Download/launch

- asset redirect followed to final body;
- final body hash match/mismatch;
- streamed temp file handling;
- exact installer argument list;
- no shell;
- retry starts fresh.

### Composition

- non-Windows -> disabled updater;
- Windows + `DTXMANIA_LAUNCH_TOKEN` -> disabled updater;
- Windows without token -> real updater;
- automation/E2E does not reach GitHub or paint the banner.

### Title

- Available/Failed mouse actions work;
- Available + Activate still selects GAME START;
- update action click does not leak into title menu;
- crash panel keeps priority;
- busy updater blocks Start/Config inputs but leaves Back/Escape available;
- `InstallerLaunched` requests exit once.

## Out of scope

- macOS auto-install/signing/notarization;
- Linux updater;
- delta updates;
- periodic polling;
- automatic download before user action;
- persisted skipped versions;
- prerelease channels;
- release-notes UI;
- generic notification framework;
- helper updater executable;
- packaging migration.

## Acceptance criteria

- One `ApplicationVersion` assembly reader drives Title, Startup, updater comparison, and crash build metadata.
- Release builds stamp `APP_VERSION` into both Windows and macOS binaries.
- A normal Windows process checks once from `TitleStage.OnFirstUpdate()`; harness-owned processes never perform update network work.
- No update/passive failure produces no update UI.
- A newer release is offered only with the exact installer asset and valid SHA-256 digest.
- Available/Failed update actions are mouse-only and never steal title `Activate`/Enter or Back.
- Download follows GitHub redirects and verifies the final installer bytes before launch.
- Successful installer launch causes Title to request exit; Inno upgrades and relaunches the new build.
- `LATER` is process-local.
- Real Windows upgrade/relaunch smoke test passes before merge.
- Existing app-data/custom skins remain outside installer replacement behavior.
