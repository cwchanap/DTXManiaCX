# HPA-187 Windows Auto-Update — Design

**Date:** 2026-09-12  
**Status:** Approved for implementation planning  
**Linear:** HPA-187 — DTXManiaCX: Add Windows auto-update notification and installer flow  
**Scope:** Check for a newer stable GitHub Release after entering the main title stage, notify only on that stage, and let Windows users download, verify, install, and relaunch the update using the existing Inno Setup release path.

## Context

DTXManiaCX already has the required release infrastructure for a small updater:

- `.github/workflows/release.yml` creates semantic `vX.Y.Z` GitHub Releases;
- Windows publishes a self-contained `win-x64` build;
- `installer/windows/dtxmania.iss` produces `DTXMania-Setup-X.Y.Z.exe`;
- the Inno installer keeps a stable `AppId`, so a newer installer upgrades the existing installation;
- GitHub Release assets expose a SHA-256 `digest`;
- `TitleStage` is the main `GAME START / CONFIG / EXIT` stage and already owns a focused `CrashReportNotification` component;
- the title version line is currently hard-coded as `DTXManiaCX v1.0.0 - MonoGame Edition`, so the game does not yet have one trustworthy runtime release version.

This ticket should add update behavior without replacing the packaging stack. A Velopack/Squirrel migration would be substantially larger than the requested feature and is out of scope.

## Goals

- Check for an update only after the player reaches `TitleStage`.
- Perform at most one remote version check per game process.
- Keep a failed passive update check invisible and non-blocking.
- Show a small title-only notification when a newer stable Windows release is installable.
- Let the player explicitly choose `UPDATE NOW` or `LATER`.
- Download and SHA-256 verify the exact Windows installer asset before launching it.
- Reuse the current Inno installer to upgrade and relaunch the game.
- Keep update/network/process/file details out of `TitleStage`.
- Replace the hard-coded title version with the same runtime version used by the updater.

## Non-goals

- macOS automatic installation. The current DMG is ad-hoc signed and is not yet a suitable silent/self-update path.
- Linux update support.
- Delta packages.
- Automatic download before player consent.
- Periodic polling while the game is running.
- Persisted ignored/skipped versions.
- Prerelease/update channels.
- Release-note UI.
- Generic notification/modal infrastructure.
- A dedicated updater executable.
- Replacing Inno Setup with Velopack, Squirrel, MSIX, or another packaging framework.

## Product flow

```text
StartupStage
    |
    v
TitleStage reaches normal interactive phase
    |
    +--> update service CheckOnce()
             |
             +--> current >= latest ----------> UpToDate (no UI)
             |
             +--> network/parse failure ------> passive failure (no UI)
             |
             +--> newer usable release -------> Available
                                                   |
                                                   v
                                     title-only update banner
                                      [UPDATE NOW] [LATER]
                                         |             |
                                         |             +--> Dismissed for this process
                                         v
                                      Downloading
                                         |
                                      SHA-256
                                         |
                              mismatch/failure --> retryable title error
                                         |
                                         v
                                  launch Inno installer
                                         |
                                         v
                               TitleStage requests exit
                                         |
                                         v
                            Inno upgrades + relaunches game
```

Leaving `TitleStage` while the one-time check is running does not cancel the process-owned check. Its result is cached. No notification is rendered on another stage; if the player later returns to `TitleStage`, the cached `Available` state can be shown there.

## Release/version contract

### Release identity

Stable releases continue to use:

```text
vMAJOR.MINOR.PATCH
```

The updater accepts only this existing three-component numeric contract. Use `System.Version`; do not add a SemVer dependency solely for this feature.

### Build stamping

`release.yml` already normalizes a release tag into `APP_VERSION`. Pass that value to the SDK publish as the assembly `Version` for the Windows release. Apply the same version stamping to the macOS release publish because the shared `TitleStage` version display must not regress to the SDK default on macOS.

Add a small `ApplicationVersion` helper that reads the game assembly version and normalizes the display/update value to `major.minor.build`.

Both of these consumers use it:

- `TitleStage.DrawVersionInfo()`;
- `GameUpdateService` current-version comparison.

Do not add a second config file or generated runtime manifest for the version.

Local developer builds can keep the SDK/default assembly version. The updater is disabled on unsupported platforms and production update tests use injected version values rather than relying on a developer machine's assembly version.

## Latest-release contract

Use the public GitHub REST endpoint for this repository:

```text
GET https://api.github.com/repos/cwchanap/DTXManiaCX/releases/latest
```

One request per process is sufficient. Set a stable `User-Agent` and a bounded HTTP timeout. No authentication or GitHub SDK is required.

A usable Windows update requires all of the following:

1. `tag_name` parses as `vX.Y.Z`;
2. parsed version is greater than the current game version;
3. the release contains an exact asset named `DTXMania-Setup-X.Y.Z.exe`;
4. that asset has a `browser_download_url`;
5. `digest` is present and is exactly a valid `sha256:<64 hex chars>` value.

`releases/latest` is the stable-release selector. Do not enumerate every release or invent channel logic.

If the release is newer but the Windows asset/digest contract is incomplete, treat it as an unusable passive result: log a bounded diagnostic and show no install banner. Do not offer an update that cannot be verified and installed.

## Update service architecture

Keep the implementation under one focused subsystem, for example:

```text
DTXMania.Game/Lib/Update/
    ApplicationVersion.cs
    GameUpdateContracts.cs
    GitHubReleaseClient.cs
    GameUpdateService.cs
    WindowsUpdateInstallerLauncher.cs
```

Exact file splitting can remain small; do not create additional layers solely to match this sketch.

### Public stage-facing contract

The stage should see only a small process-owned facade:

```csharp
public interface IGameUpdateService
{
    GameUpdateSnapshot GetSnapshot();
    void CheckOnce();
    void BeginUpdate();
    void DismissForProcess();
}
```

`GameUpdateSnapshot` is immutable and contains only UI-relevant state, target version, and a bounded error code/message where applicable.

A suitable minimal state set is:

```text
Idle
Checking
UpToDate
Available
Downloading
LaunchingInstaller
InstallerLaunched
Failed
Dismissed
```

Do not expose `HttpClient`, URLs, paths, digests, `Process`, or temporary files to `TitleStage`.

### Process ownership and threading

`BaseGame` owns one update-service instance for the process and exposes it through `IStageGame`.

`IStageGame` supplies a default disabled/null implementation so existing test stubs do not all need to change. Production `BaseGame` returns the real service on Windows; macOS receives the disabled implementation in this ticket.

`CheckOnce()` must be idempotent. The service owns the one-shot gate, not `TitleStage`, because `StageManager` reuses/re-activates stages.

Remote check and installer download run off the game loop. The service publishes immutable snapshots behind one small synchronization boundary (`lock` or equivalent). `TitleStage` only polls `GetSnapshot()` during update/draw.

Do not marshal graphics/UI work from background tasks.

The service does not call `Game.Exit()` itself. After successful installer process launch it publishes `InstallerLaunched`; `TitleStage` observes that state and calls `_game.RequestExit()` on the game thread.

## Download and verification

Download into a process-owned temporary update directory, not the installation directory. A version-scoped filename is sufficient; no update cache database is needed.

Required order:

```text
download to temporary file
    -> compute SHA-256 from downloaded bytes
    -> compare to GitHub release asset digest
    -> only then launch installer
```

Use streamed file I/O; do not load the ~90 MB installer into one byte array.

On digest mismatch:

- delete/best-effort clean the bad temporary installer;
- set a bounded `Failed` state;
- keep the running game alive;
- allow the player to retry or choose Later.

Passive check failures remain silent. Failures after the player explicitly chooses `UPDATE NOW` are visible because otherwise the action appears to do nothing.

No download percentage is required in this ticket. `Downloading vX.Y.Z...` is enough.

## Windows installer launch

Use the verified `.exe` directly with `ProcessStartInfo`; do not invoke `cmd`, PowerShell, or another command shell.

The launcher builds one fixed auto-update argument set using standard Inno silent/update options plus a DTXManiaCX-specific marker:

```text
/VERYSILENT
/SUPPRESSMSGBOXES
/NORESTART
/CLOSEAPPLICATIONS
/AUTOUPDATE
```

Keep the exact argument construction in one testable Windows launcher class/helper.

`/CLOSEAPPLICATIONS` provides an additional safety net if the installer reaches file replacement before the game has completed its requested exit. The game still requests its own exit immediately after successful installer process start.

If Inno behavior during implementation proves this race cannot be handled reliably with the existing installer and standard close-application support, stop and revise the design. Do not silently expand this ticket into a helper/updater process architecture.

## Inno Setup integration

Keep the existing stable `AppId`, default installation directory, `[InstallDelete]`, `[Files]`, and custom-skin ownership boundaries unchanged.

Add a narrow `/AUTOUPDATE` marker parser in `dtxmania.iss` and two launch behaviors:

- normal interactive install keeps the existing final-page launch option;
- auto-update silent install launches `DTXMania.Game.Windows.exe` automatically after a successful install.

Conceptually:

```text
[Run]
interactive launch -> existing postinstall + skipifsilent behavior
auto-update launch -> Check: IsAutoUpdate, no postinstall/skipifsilent
```

`IsAutoUpdate` can scan `ParamStr(1..ParamCount)` for `/AUTOUPDATE` case-insensitively. Do not add registry state or a second installer mode file.

The installer continues to own application files and bundled default `System`. Per-user configuration, score data, song folders, and custom skins remain under the existing app-data ownership model and are not part of the replacement set.

## Title-stage UX

### Placement

`CrashReportNotification` currently uses the upper-right title area. Preserve its ownership and priority.

Use a second compact update banner directly below the crash banner area. Do not merge them into a notification framework in this ticket.

Example closed state:

```text
UPDATE AVAILABLE — v0.0.7
UPDATE NOW    LATER
```

During an explicit update:

```text
DOWNLOADING UPDATE — v0.0.7
```

On explicit failure:

```text
UPDATE FAILED
RETRY    LATER
```

Exact copy/layout may be adjusted to fit the current title skin; no new art asset is required.

### Input ownership

Add `GameUpdateNotification` as a small state/input/drawing component following the useful pattern already established by `CrashReportNotification`.

Priority inside `TitleStage.OnUpdate()`:

```text
1. CrashReportNotification if its panel consumes the frame
2. GameUpdateNotification if update UI consumes the frame
3. normal TitleStage menu input/mouse handling
```

The closed update banner must not consume unrelated title input. Clicking/activating an update action consumes the frame so `GAME START`, `CONFIG`, or `EXIT` cannot also trigger.

While `Downloading` or `LaunchingInstaller`, the update component owns title input for that operation. A cancellation flow is not required for this first implementation.

`LATER` calls the process-owned service dismissal state. Returning to Title from Config/Song Select must not re-show the banner until the application restarts.

## Failure behavior

### Passive check

These result in no player-visible banner:

- timeout/network failure;
- invalid JSON;
- malformed release version;
- no exact Windows installer asset;
- missing/invalid SHA-256 digest;
- current version is equal/newer.

Write bounded diagnostics through the existing logging/debug path; do not surface raw HTTP bodies or exception text.

### Explicit update

These produce a retryable title error:

- installer download failure;
- temporary-file write failure;
- SHA-256 mismatch;
- installer process-start failure.

The existing game remains running. `RETRY` starts a fresh download; `LATER` dismisses for the process.

## Testing strategy

### Pure release/version tests

Cover:

- `v0.0.7` parsing and normalized asset name;
- equal version -> no update;
- current newer than latest -> no downgrade;
- newer release -> available;
- malformed tags;
- missing/duplicate/wrongly named Windows assets;
- valid and invalid `sha256:` digest forms;
- API/network failure mapping to passive no-notification state.

Use a fake `HttpMessageHandler` or equivalent; normal tests never call GitHub.

### Download/launcher tests

Cover:

- streamed download to a temp test directory;
- digest match -> launcher invoked once;
- digest mismatch -> launcher not invoked and bad file removed/best-effort cleaned;
- launch exception/failure -> retryable failed state;
- exact Inno argument list;
- no command shell.

Do not run the real installer from unit tests.

### Process/state tests

Cover:

- `CheckOnce()` is idempotent;
- stage re-entry does not trigger another HTTP request;
- available result remains cached while another stage is active;
- Later persists for the current service/process lifetime only;
- successful installer launch yields `InstallerLaunched` and does not itself terminate the game.

### Title integration tests

Keep most logic in `GameUpdateNotification` tests and only thin `TitleStage` guards:

- unavailable/checking/up-to-date -> no banner action;
- available -> banner/action state;
- Later dismisses;
- Update Now begins update and consumes input;
- explicit failure exposes Retry/Later;
- crash-report panel consumption prevents update input in the same frame;
- update-consumed input never reaches the title menu;
- `InstallerLaunched` causes one `_game.RequestExit()` call;
- displayed title version is sourced from `ApplicationVersion`, not a hard-coded literal.

### Release/installer verification

The existing release workflow already compiles the Inno script for every Windows release. Keep that as the syntax/build gate and add focused source/contract assertions only if they can be done without a brittle environment-specific installer E2E harness.

## Risks and boundaries

1. **Installer starts before game fully exits** — launch with Inno close-application support, request exit immediately on the game thread, and stop/revise if real testing proves the existing installer cannot make this reliable.
2. **Version drift** — one assembly-derived `ApplicationVersion` drives both display and comparison; release workflow stamps it.
3. **Broken release asset offered to players** — require exact asset name plus GitHub SHA-256 digest before publishing `Available`.
4. **Game-loop stall** — network/download work stays off-thread; UI polls immutable snapshots.
5. **Title input leakage** — preserve explicit notification priority and bool-consumed input guard.
6. **Scope creep into packaging** — no helper executable or packaging migration in HPA-187.

## Acceptance criteria

- Windows release builds carry the `X.Y.Z` release version in assembly metadata, and the title displays that version through one shared helper.
- First normal `TitleStage` activation starts one non-blocking stable-release check for the process.
- No update or passive check failure leaves title behavior unchanged and shows no update UI.
- A newer release is offered only when the exact Windows installer and valid SHA-256 digest are present.
- Update UI renders only on `TitleStage`.
- `LATER` hides the offer until process restart.
- `UPDATE NOW` downloads to temporary storage, verifies SHA-256, launches the installer only after verification, and never uses a command shell.
- Successful installer launch causes the game to request exit from the title/game thread.
- Auto-update Inno mode silently upgrades using the existing stable installer identity and relaunches DTXManiaCX after success.
- A player-initiated download/verify/launch failure remains retryable on the title screen and does not unexpectedly terminate the game.
- Crash-report notification input keeps priority and no update action leaks into normal title menu input.
- User-owned app-data/custom skins remain outside installer replacement behavior.
