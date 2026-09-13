# HPA-187 Windows Auto-Update — Design

**Date:** 2026-09-12  
**Status:** Approved with review amendments  
**Linear:** HPA-187 — DTXManiaCX: Add Windows auto-update notification and installer flow  
**Scope:** On normal Windows launches, check once for a newer stable GitHub Release after entering `TitleStage`, notify only there, then download, verify, install, and relaunch through the existing Inno Setup path.

## Decision

Keep the design lean: GitHub Releases + the existing Inno installer, one process-owned update service, and one focused title notification. Do **not** migrate packaging or add a helper updater.

The second review is accepted with one correction: the claimed automatic shadow-install failure is not the current Inno default behavior. With the stable `AppId`, Inno normally reuses the previous install directory and previous privilege mode. We therefore will **not** preemptively add `/DIR`, `/ALLUSERS`, or `/CURRENTUSER`, nor infer privilege mode from the executable path. Instead, the existing installer behavior is proven first in Task 0 for both per-user and all-users installs. If that proof shows explicit overrides are required, revise this design before implementing the C# updater.

## Task-0 architecture gate

Before any updater production code is written, prove the existing Inno path with two release-like installers built from current `main` using different versions.

Test both supported install modes:

1. current-user install;
2. all-users install.

For each mode:

```text
install older build
 -> launch old game
 -> start newer installer with proposed silent auto-update arguments
 -> old process releases installed files
 -> installer upgrades the same installation
 -> new game relaunches from the same install root
```

For the all-users case also exercise UAC cancellation/failure. The running game must not be terminated merely because installer launch/elevation failed.

The baseline arguments are:

```text
/VERYSILENT
/SUPPRESSMSGBOXES
/NORESTART
/CLOSEAPPLICATIONS
/AUTOUPDATE
```

Do not add `/DIR`, `/ALLUSERS`, or `/CURRENTUSER` unless Task 0 demonstrates that Inno's existing previous-install reuse is insufficient. If the close/upgrade/relaunch flow cannot be made reliable without introducing a helper updater or packaging migration, stop and revise HPA-187.

## Existing seams to reuse

- `.github/workflows/release.yml` already owns semantic `vX.Y.Z` releases and `APP_VERSION`.
- Windows releases already contain `DTXMania-Setup-X.Y.Z.exe` with GitHub `sha256:` digest metadata.
- `installer/windows/dtxmania.iss` has a stable `AppId` and supports current-user/all-users selection.
- `TitleStage` already composes `CrashReportNotification`; update UI follows that focused component pattern but stays separate.
- `GitHubCrashIssueBuilder.TargetOwner` / `TargetRepository` already own canonical repository identity.
- `CrashContextPublisher` and `CrashReportStore` independently read assembly informational version today.
- `StartupStage` and `TitleStage` still contain the old `v1.0.0` display contract.
- automation/MCP-owned processes already use `DTXMANIA_LAUNCH_TOKEN`.

## One application-version source

Create one small `ApplicationVersion` helper as the only game-code reader of assembly version metadata.

It exposes:

- normalized release version and `X.Y.Z` display text;
- informational/build identifier for crash reports, preserving revision metadata when present.

Consumers:

- `TitleStage.DrawVersionInfo()`;
- `StartupStage.DrawVersionInfo()`;
- `GameUpdateService` current-version comparison;
- `CrashContextPublisher`;
- `CrashReportStore.GetBuildId()`.

Both the assembly/current version and GitHub target tag must normalize to exactly three components before comparison, e.g. `new Version(major, minor, patch)`. Do not rely on `1.2.3` versus `1.2.3.0` `System.Version` ordering semantics.

`release.yml` passes the existing `APP_VERSION` to both Windows and macOS publish commands using `-p:Version=<APP_VERSION>`. Search the game/test tree for stale `v1.0.0` expectations, including `StartupStageLogicTests`.

## GitHub release discovery

`GitHubReleaseClient` builds repository URLs from:

```csharp
GitHubCrashIssueBuilder.TargetOwner
GitHubCrashIssueBuilder.TargetRepository
```

Do not add a third owner/repository copy or a repository-identity service.

A usable update requires:

1. exact stable `vX.Y.Z` tag;
2. normalized target version greater than normalized current version;
3. exact `DTXMania-Setup-X.Y.Z.exe` asset;
4. non-empty `browser_download_url`;
5. valid `sha256:<64 hex chars>` digest.

Passive rejection remains invisible to the player, but it is not silent diagnostically. Log one bounded reason code through the existing logger, for example `not_newer`, `asset_missing`, `digest_missing`, `invalid_tag`, `http_403`, or `network_failure`. Do not log response bodies or raw exception text into the UI.

## Runtime service and automation boundary

Keep one process-owned `IGameUpdateService` with immutable snapshots. Network/file/process work stays off the MonoGame loop.

`IStageGame` exposes a default disabled singleton, matching the existing `CrashReportInbox` pattern.

Use one game-side constant for the launch-token environment variable and reuse it from both update composition and `JsonRpcServer`; do not add a second literal in game code.

Extract the composition decision as a pure predicate:

```csharp
ShouldEnable(bool isWindows, string? launchToken)
```

It returns true only for Windows with null/empty launch token. Test the three cases directly. This is a harness boundary, not a user feature flag.

`CheckOnce()` is called from `TitleStage.OnFirstUpdate()` and remains idempotent across title re-entry.

## Download, redirect, temp-file, and progress contract

Download to one version-scoped temporary installer path.

Before every new attempt, delete/best-effort clean any existing file at that path. This bounds an interrupted download to at most one stale installer file per target version.

Flow:

```text
browser_download_url
 -> HTTP redirect(s)
 -> final installer response body
 -> stream to temp file
 -> SHA-256 final bytes
 -> compare expected digest
 -> launch only on match
```

Normal parsing/state tests can use fake handlers. Redirect behavior must have one loopback integration-style test using the real production `HttpClient`/handler stack: local `/asset` returns 302 to `/real`, and the expected digest is computed from `/real`'s body. Do not count a fake-handler 302 test or an `AllowAutoRedirect == true` assertion as redirect coverage.

The snapshot may expose an optional integer download percent derived from `Content-Length`. Render only text such as:

```text
DOWNLOADING UPDATE — vX.Y.Z (43%)
```

If content length is unavailable, omit the percent. Do not add a progress bar, history, or cancellation subsystem.

Digest mismatch or I/O/network failure after explicit Update produces a bounded retryable `Failed` state and keeps the current game alive.

## Windows installer launch

`WindowsUpdateInstallerLauncher` remains independent from crash-report `ExternalLauncher`. Reuse the existing launcher's narrow delegate/test-seam shape if useful, not its URI/folder behavior.

Launch the verified installer directly; never invoke a command shell. Catch `Win32Exception`/process-start failures, including elevation cancellation, as retryable failure. A failed start must never publish `InstallerLaunched` and must never exit the game.

Process creation alone is not a committed launch: with `PrivilegesRequired=lowest` + `PrivilegesRequiredOverridesAllowed=dialog` the bootstrapper starts unelevated and re-launches itself elevated via UAC from inside the started process when it reuses a previous all-users install, so `Process.Start` can return a process before that outcome is known. The launcher therefore observes the started process through a bounded elevation-decision window: a quick nonzero exit is a refused elevation (retryable failure, game stays alive), a quick exit 0 is a successful elevated respawn, and a process still running at the deadline is an in-progress install that needed no elevation. The wait never extends to install completion.

The final process-start semantics and arguments are those proven by Task 0. Do not infer install privilege mode from path unless Task 0 forces a revised design.

## Inno Setup changes

Preserve:

- stable `AppId`;
- previous installation directory/privilege behavior;
- bundled System replacement semantics;
- per-user app-data/custom-skin ownership;
- normal interactive install behavior.

Add `/AUTOUPDATE` detection and an auto-update `[Run]` entry that relaunches the game after successful silent installation. Correct the stale `MyAppURL` to `https://github.com/cwchanap/DTXManiaCX` while touching the script.

## Title UX and input ownership

Use the same interaction model already established by `CrashReportNotification` rather than a mouse-only second convention.

### Closed banner

When an update is available:

```text
UPDATE AVAILABLE — vX.Y.Z
Press F9 or click to review
```

The closed banner consumes **nothing** from normal title navigation. `Activate` / Enter still reaches `GAME START`. This regression is required.

A fixed raw, edge-triggered, non-remappable **F9** shortcut or banner click opens the update panel and consumes that frame. F9 is diagnostic/update UI, not a new configurable gameplay command.

### Open panel

The panel uses existing remappable title/input commands for focus/action and follows the same boolean frame-ownership pattern as the crash panel. Actions are `UPDATE NOW` / `LATER`, or `RETRY` / `LATER` after explicit failure.

Priority remains:

```text
1. CrashReportNotification
2. GameUpdateNotification
3. normal TitleStage menu
```

While downloading/launching, prevent Start/Config navigation/activation. Back/Escape may still use the existing title exit behavior; there is no cancel-update flow.

### Installer terminal state

`InstallerLaunched` is observed from `TitleStage.OnUpdate()` independently of the phase-gated input path. It must request game exit exactly once even if title input handling is temporarily outside `Normal` phase.

## Testing contract

Cover:

- one normalized application-version reader used by Title, Startup, crash metadata, updater;
- exact three-component equality/newer/older comparison;
- repository identity reused from `GitHubCrashIssueBuilder`;
- one request per process and bounded passive rejection logging;
- real update disabled for non-Windows and launch-token-owned processes via pure `ShouldEnable` tests;
- stale temp file removed before a new download attempt;
- loopback 302 -> final-body hash test through the production client stack;
- digest match/mismatch and launcher start failure;
- optional download-percent text;
- closed update banner + title Activate still selects GAME START;
- raw F9/banner click opens and consumes only that frame;
- crash panel remains first priority;
- `InstallerLaunched` is observed from `OnUpdate` and requests exit once;
- Task 0 and final Windows regression smoke tests cover current-user and all-users installs.

## Out of scope

- macOS auto-install/signing/notarization;
- Linux updater;
- delta updates;
- periodic polling;
- automatic download before user action;
- persisted skip state;
- prerelease channels;
- release-notes UI;
- generic notification framework;
- helper updater executable;
- packaging migration.

## Acceptance criteria

- Task 0 proves the chosen Inno update flow in both current-user and all-users modes before C# updater implementation proceeds.
- One normalized `ApplicationVersion` source serves Title, Startup, crash metadata, and updater comparison.
- Release binaries stamp `APP_VERSION` on Windows and macOS.
- Harness-owned processes never perform update network work.
- Passive failures show no player UI but emit one bounded diagnostic reason.
- A newer release is offered only with the exact installer and valid SHA-256 digest.
- Closed update banner never steals title Activate; F9/click explicitly opens update controls.
- Download follows real HTTP redirects, cleans stale temp state, and displays lightweight percentage when available.
- Failed/elevation-cancelled installer start is retryable and does not exit the game.
- Successful installer launch is observed from `OnUpdate`; the proven Inno flow upgrades and relaunches the same installation.
- `LATER` remains process-local and app-data/custom skins remain untouched.
