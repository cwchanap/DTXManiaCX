# HPA-187 Windows Auto-Update Implementation Plan

> **For agentic workers:** implement this plan on this same PR. Keep the architecture intentionally small; do not turn HPA-187 into a packaging migration or generic notification framework.

**Goal:** On Windows, check once for a newer stable GitHub Release after entering `TitleStage`, show a title-only update offer, and let the player download, verify, silently upgrade through the existing Inno installer, then relaunch the game.

**Architecture:** A process-owned `IGameUpdateService` handles one-shot release discovery, download, SHA-256 verification, and installer launch. `IStageGame` exposes only that facade. `TitleStage` owns a small `GameUpdateNotification` UI component and requests game exit only after the service reports that the installer was launched successfully. The existing GitHub Release workflow and Inno installer remain the packaging source of truth.

**Tech stack:** .NET 8, C#, MonoGame, `HttpClient`, `System.Text.Json`, `System.Security.Cryptography`, `System.Diagnostics.Process`, xUnit/Moq, existing GitHub Actions release workflow, Inno Setup 6.

## Global constraints

- One PR for HPA-187; implementation continues on this draft PR.
- Main game stage means `TitleStage` only. Do not render update UI on Startup, Song Select, Config, gameplay, or Result stages.
- Run at most one remote update check per game process.
- Start the check only once `TitleStage` becomes interactive; do not add network work to `StartupStage`.
- Passive check failures are silent to the player.
- Player-initiated update failures are visible and retryable.
- Use GitHub `releases/latest`; do not add a custom manifest/backend or enumerate release history.
- Stable version contract remains `vX.Y.Z`; use `System.Version`, no SemVer package.
- Exact Windows asset is `DTXMania-Setup-X.Y.Z.exe`.
- Require a valid GitHub `sha256:` asset digest before offering an installable update.
- Download/verify off the game loop and stream the installer to disk.
- Never invoke `cmd`, PowerShell, `sh`, or shell-concatenated commands.
- Reuse Inno Setup and stable `AppId`; no Velopack/Squirrel/MSIX/helper-updater executable.
- `LATER` is process-local only; no persisted skip state.
- No download percentage/cancel flow in this ticket; a simple downloading state is enough.
- `CrashReportNotification` keeps input priority over the update component.
- Do not generalize the two title notifications into a common framework.
- macOS auto-update remains out of scope. The shared title version display may be fixed on macOS by stamping the existing mac release publish version.

---

## Expected file shape

Keep file count small. A reasonable target is:

**Create:**

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
- `DTXMania.Game/Lib/Stage/IStageGame.cs`
- `DTXMania.Game/Game1.cs`
- `DTXMania.Game/Lib/Stage/TitleStage.cs`
- existing title/base-game test helpers only where required

If two of the update files are trivially small, combining them is preferred to inventing more abstractions.

---

## Platform-aware test commands

Use the game test project for the current platform:

**macOS:**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

**Windows / Windows CI:**

```powershell
dotnet test DTXMania.Test/DTXMania.Test.csproj
```

Keep new update-domain tests platform-neutral wherever possible so they run from both projects. Isolate actual Windows process-start behavior behind pure/testable `ProcessStartInfo` construction plus a launcher seam; unit tests must never run the real installer.

---

## Task 1: Establish one trustworthy application version

**Files:**
- Create: `DTXMania.Game/Lib/Update/ApplicationVersion.cs`
- Modify: `.github/workflows/release.yml`
- Modify: `DTXMania.Game/Lib/Stage/TitleStage.cs`
- Test: `DTXMania.Test/Update/ApplicationVersionTests.cs`
- Adjust: focused `TitleStage` version tests if present/needed

### Required behavior

- [ ] Add pure normalization tests around assembly-style versions:

```text
0.0.6.0 -> version 0.0.6, display "0.0.6"
1.2.3.4 -> version 1.2.3, display "1.2.3"
missing/invalid assembly version -> safe fallback, never throw while drawing title
```

- [ ] Implement `ApplicationVersion` as the single runtime source used by both updater and title display. Keep a testable normalization helper rather than requiring tests to mutate the process assembly.

- [ ] Replace the literal `DTXManiaCX v1.0.0 - MonoGame Edition` in `TitleStage.DrawVersionInfo()` with the helper's display value.

- [ ] In `release.yml`, pass the already-normalized `APP_VERSION` into SDK publish version metadata for both Windows and macOS release publish commands. Do not create another version file.

Target shape is equivalent to:

```text
dotnet publish ... -p:Version=<APP_VERSION>
```

Use shell-appropriate environment syntax and retain every existing publish flag.

- [ ] Keep release/tag generation unchanged. This task only makes the produced binaries carry the version the workflow already selected.

### Verification

- [ ] Run focused version tests on the local platform.
- [ ] Search `TitleStage.cs` and confirm no hard-coded `v1.0.0` remains.
- [ ] Review both release publish legs and confirm they use the same `APP_VERSION` generated by `prepare-release`.

### Commit

```bash
git add .github/workflows/release.yml DTXMania.Game/Lib/Update/ApplicationVersion.cs DTXMania.Game/Lib/Stage/TitleStage.cs DTXMania.Test
git commit -m "feat: stamp runtime application version"
```

---

## Task 2: Add one-shot stable-release discovery

**Files:**
- Create: `DTXMania.Game/Lib/Update/GameUpdateContracts.cs`
- Create: `DTXMania.Game/Lib/Update/GitHubReleaseClient.cs`
- Create/modify: `DTXMania.Game/Lib/Update/GameUpdateService.cs`
- Test: `DTXMania.Test/Update/GitHubReleaseClientTests.cs`
- Test: `DTXMania.Test/Update/GameUpdateServiceTests.cs`

### Contract

Keep the stage-facing API small:

```csharp
public interface IGameUpdateService
{
    GameUpdateSnapshot GetSnapshot();
    void CheckOnce();
    void BeginUpdate();
    void DismissForProcess();
}
```

Use an immutable snapshot and only the state/data the UI needs. A minimal state set:

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

Provide a disabled singleton/null service for unsupported platforms/test stubs.

### Release parsing tests first

- [ ] Fake `HttpMessageHandler`; normal tests make zero real network requests.
- [ ] Cover exact stable JSON shapes for:
  - newer `vX.Y.Z` + exact installer asset + valid `sha256:` digest -> usable `UpdateInfo`;
  - equal version -> no update;
  - current version newer -> no downgrade;
  - malformed/non-three-component tag -> unusable;
  - missing or wrongly named installer asset -> unusable;
  - missing/invalid digest -> unusable;
  - timeout/HTTP/JSON failure -> bounded failure result.

The client needs only the fields it consumes; use narrow DTOs or direct JSON parsing, not the full GitHub schema.

### GitHub request

- [ ] Request exactly:

```text
https://api.github.com/repos/cwchanap/DTXManiaCX/releases/latest
```

- [ ] Set a stable User-Agent and bounded timeout in production composition.
- [ ] Do not authenticate, enumerate releases, or add channel logic.

### One-shot service behavior

- [ ] `CheckOnce()` starts work at most once for the service/process lifetime. Repeated calls while checking or after completion are no-ops.
- [ ] Run HTTP work asynchronously/off the game thread.
- [ ] Publish state through one small lock/synchronization boundary as immutable snapshots.
- [ ] Passive network/parse/unusable-release failures end in a non-banner state (`UpToDate` or an internal silent state). Do not expose a title error before the player presses Update Now.
- [ ] Cache `Available` until update, dismissal, or process exit.
- [ ] `DismissForProcess()` moves the service to `Dismissed` and does not touch config/disk.

At the end of Task 2, `BeginUpdate()` may still return/set a temporary not-implemented failure in tests; download/install lands in Task 3. Do not wire title UI yet.

### Verification

```bash
dotnet test <platform-test-project> --filter "FullyQualifiedName~Update"
```

### Commit

```bash
git add DTXMania.Game/Lib/Update DTXMania.Test/Update
git commit -m "feat: add one-shot github update discovery"
```

---

## Task 3: Download, verify, and launch the existing Windows installer

**Files:**
- Modify: `DTXMania.Game/Lib/Update/GameUpdateService.cs`
- Create: `DTXMania.Game/Lib/Update/WindowsUpdateInstallerLauncher.cs`
- Test: `DTXMania.Test/Update/GameUpdateServiceTests.cs`
- Test: `DTXMania.Test/Update/WindowsUpdateInstallerLauncherTests.cs`

### Test seams

Do not introduce a general networking/filesystem/process framework. Add only the narrow seams needed to make destructive/external work fakeable, for example:

```text
IGitHubReleaseClient / focused client dependency
IUpdateInstallerLauncher
optional temp-root or file helper seam only if tests cannot use a real temporary directory cleanly
```

Prefer real temporary files for download/hash tests and a fake launcher.

### Download/verification behavior

- [ ] `BeginUpdate()` is accepted only from `Available` or retryable `Failed` with retained update metadata. Ignore duplicate presses while already downloading/launching.
- [ ] Set `Downloading`, then stream the asset to a version-scoped temporary `.exe` path.
- [ ] Compute SHA-256 from downloaded bytes without loading the entire installer into memory.
- [ ] Compare case-insensitively to the validated expected hex digest.
- [ ] Digest mismatch:
  - launcher is never called;
  - bad temp file is deleted/best-effort cleaned;
  - state becomes retryable `Failed` with a bounded UI-safe error code/message.
- [ ] Download/write failure -> retryable `Failed` and game remains alive.

No percentage progress is required.

### Windows launcher contract

Build `ProcessStartInfo` directly for the verified installer:

```text
FileName = <verified installer path>
UseShellExecute = false
ArgumentList:
  /VERYSILENT
  /SUPPRESSMSGBOXES
  /NORESTART
  /CLOSEAPPLICATIONS
  /AUTOUPDATE
```

- [ ] Assert no command shell is used.
- [ ] Assert every argument is a separate `ArgumentList` element.
- [ ] `Process.Start` null/exception maps to retryable failure.
- [ ] Successful `Process.Start` publishes `InstallerLaunched`; the service does **not** call `Environment.Exit` or game APIs.

Do not wait for the installer process; once it has started successfully the game is expected to exit from the title thread.

### Retry behavior

- [ ] Retry starts a fresh download/verification; do not trust an earlier failed partial file.
- [ ] `DismissForProcess()` from `Failed` remains available and clears the title offer for this process.

### Verification

```bash
dotnet test <platform-test-project> --filter "FullyQualifiedName~Update"
```

### Commit

```bash
git add DTXMania.Game/Lib/Update DTXMania.Test/Update
git commit -m "feat: download and verify windows updates"
```

---

## Task 4: Reuse Inno Setup for silent auto-update and relaunch

**Files:**
- Modify: `installer/windows/dtxmania.iss`
- Modify/add: installer/release contract checks only if they stay simple

### Preserve existing installer ownership

Do not change:

- stable `AppId`;
- `DefaultDirName`;
- bundled System skin replacement behavior;
- app-data/custom-skin ownership boundaries;
- normal interactive install flow.

### Add auto-update marker

- [ ] Add a tiny Pascal helper that detects `/AUTOUPDATE` case-insensitively by scanning `ParamStr(1..ParamCount)` (or the equivalent existing Inno facility). Do not persist mode state.

- [ ] Keep the current interactive `[Run]` entry for the normal installer:

```text
postinstall + skipifsilent
```

- [ ] Add an auto-update `[Run]` entry checked by `IsAutoUpdate` that launches `{app}\DTXMania.Game.Windows.exe` after successful silent installation. It must not use `postinstall`/`skipifsilent`, because auto-update runs silent.

- [ ] Ensure the auto-update entry does not also make normal interactive installs double-launch the game.

### Fast-fail boundary

Manually/CI verify the existing Inno build still compiles the script. Run one real local Windows smoke test before marking the implementation ready:

```text
installed older build
 -> launch newer installer with the exact updater args
 -> old app closes/exits
 -> files upgrade successfully
 -> new app relaunches
 -> version line shows the new version
```

If standard Inno `/CLOSEAPPLICATIONS` plus immediate game exit is not reliable, stop here and revise HPA-187. Do **not** add a helper process silently inside this task.

### Commit

```bash
git add installer/windows/dtxmania.iss
git commit -m "feat: add inno auto-update mode"
```

---

## Task 5: Compose the service and add title-only notification UX

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/IStageGame.cs`
- Modify: `DTXMania.Game/Game1.cs`
- Create: `DTXMania.Game/Lib/Stage/GameUpdateNotification.cs`
- Modify: `DTXMania.Game/Lib/Stage/TitleStage.cs`
- Test: `DTXMania.Test/Stage/GameUpdateNotificationTests.cs`
- Modify/add: focused `DTXMania.Test/Stage/TitleStageTests.cs`
- Modify/add: focused `DTXMania.Test/BaseGameTests.cs` or `IStageGame` contract tests only as necessary

### Process composition

- [ ] Add to `IStageGame` a default disabled update-service property so existing stubs remain source-compatible:

```csharp
IGameUpdateService GameUpdateService => DisabledGameUpdateService.Instance;
```

- [ ] `BaseGame` owns exactly one production update service for its lifetime.
- [ ] Windows production composition uses `ApplicationVersion.Current`, the real GitHub client, temp storage, and Windows launcher.
- [ ] macOS composition uses the disabled service in this ticket.
- [ ] Dispose any owned `HttpClient`/service resource with `BaseGame` if the final implementation owns disposable resources. Do not modify `Program.cs` constructor signatures unless code inspection proves it is materially simpler than BaseGame-local composition.

### Start timing

- [ ] Call `CheckOnce()` from `TitleStage` only when the stage first reaches its normal interactive path (`OnFirstUpdate` or equivalent), not from constructor/Startup.
- [ ] Calling it again on Title reactivation is safe because the service owns the one-shot gate.

### Notification component

Create `GameUpdateNotification` with state/input/drawing only. It reads `IGameUpdateService`; it does not own HTTP/download/process implementation.

Required UI behavior:

```text
Available:
  UPDATE AVAILABLE — vX.Y.Z
  UPDATE NOW    LATER

Downloading/Launching:
  DOWNLOADING UPDATE — vX.Y.Z
  (operation owns title input)

Failed after explicit action:
  UPDATE FAILED
  RETRY    LATER
```

No new art asset is required. Reuse title `SpriteBatch`, `_versionFont`, and `_whitePixel`.

Use private constant geometry directly below the crash-report banner area. Do not create shared title notification layout infrastructure.

### Input priority

Preserve this order in `TitleStage.OnUpdate()`:

```text
1. CrashReportNotification.HandleInput(...) if it consumes
2. GameUpdateNotification.HandleInput(...) if it consumes
3. normal HandleInput()/HandleMouseInput()
```

- [ ] Closed/no-update state consumes nothing.
- [ ] Update Now / Later / Retry consumes the frame.
- [ ] Downloading/Launching consumes title menu input so the player cannot simultaneously start/config/exit while installation is being prepared.
- [ ] Crash review panel remains authoritative if open.

### Installer-launched exit

- [ ] When the update snapshot becomes `InstallerLaunched`, `TitleStage` requests exit exactly once through `_game.RequestExit()` on the game thread.
- [ ] Do not call process/game exit from the update worker.

### Tests

`GameUpdateNotificationTests` should cover the state machine without a graphics device as much as the existing crash notification pattern allows:

- available actions;
- Later -> dismissed;
- Update Now -> begin update;
- Downloading consumes input;
- Failed -> Retry/Later;
- no update -> no consumption/banner state.

Thin Title tests cover:

- first interactive title update calls `CheckOnce`;
- Title re-entry cannot cause a second actual check (service fake can assert call/idempotence contract as appropriate);
- crash notification consumption prevents update handling that frame;
- update consumption prevents normal menu action;
- `InstallerLaunched` -> one `RequestExit`;
- no update state preserves current title navigation.

### Verification

Run focused tests first, then the full platform suite:

```bash
dotnet test <platform-test-project> --filter "FullyQualifiedName~Update|FullyQualifiedName~TitleStage"
dotnet test <platform-test-project>
```

### Commit

```bash
git add DTXMania.Game/Lib/Stage DTXMania.Game/Game1.cs DTXMania.Test
git commit -m "feat: add title-stage auto-update flow"
```

---

## Task 6: Release-path verification and cleanup

This is verification/cleanup inside the same PR, not a separate PR.

- [ ] Build/publish Windows release locally or through the existing workflow path with an explicit test version and confirm `ApplicationVersion` resolves it.
- [ ] Compile the modified Inno installer.
- [ ] Perform the real Windows old-version -> new-version auto-update smoke test described in Task 4.
- [ ] Confirm custom/user data locations are unchanged after upgrade.
- [ ] Confirm a Mac build/test still compiles with the disabled updater and dynamic title version.
- [ ] Confirm no update code runs from `StartupStage`.
- [ ] Confirm source contains no persisted skip-version setting, release polling timer, generic notification framework, second manifest service, command shell, or packaging migration.
- [ ] Run the full relevant test suite and existing CI gates.
- [ ] Update the draft PR body with actual verification performed and any accepted limitation.

Do not expand the ticket for unrelated release-pipeline cleanup discovered during testing. Record unrelated findings separately.

---

## Done definition

HPA-187 is ready for review when:

- release binaries carry the release version and title uses that source;
- one process-owned Windows updater checks only after entering Title;
- a usable newer release shows a Title-only update offer;
- Later dismisses for the process;
- Update Now downloads and verifies the GitHub SHA-256 digest before launching anything;
- the verified Inno installer is started directly with fixed silent/update arguments;
- successful launch triggers game exit from the Title/game thread;
- Inno auto-update mode upgrades and relaunches the new game in a real Windows smoke test;
- failures after user action stay retryable and passive failures remain invisible;
- crash notification priority and normal title input remain correct;
- full tests pass without real GitHub calls or installer execution in unit tests.
