# Gameplay E2E CI: AutoPlay Full-Journey Test

**Status:** Design approved, ready for implementation planning
**Date:** 2026-06-01
**Scope:** Add the first CI-run end-to-end gameplay test for DTXManiaCX by launching the real game process, generating an isolated short chart, enabling AutoPlay, navigating through the main game flow, and asserting the result summary through JSON-RPC telemetry.

## Problem

DTXManiaCX has strong unit and deterministic stage-level coverage, including performance manager simulations and Result-stage model tests. It does not yet have a CI-grade end-to-end test that proves a real built game process can launch, load a chart, enter gameplay, complete the song, and reach Result through the same runtime surfaces used by users.

Manual MCP probes are useful for debugging, but they are not enough for CI/CD. The first E2E lane must be automated, isolated from developer app data, and capable of producing actionable artifacts when it fails in GitHub Actions.

## Goals

1. Add a real process-level E2E test that runs in CI/CD, not a manual MCP workflow.
2. Exercise the full user-facing journey: Title -> SongSelect -> SongTransition -> Performance -> Result.
3. Use AutoPlay for deterministic gameplay completion in the first test.
4. Generate the DTX test chart and app-data sandbox fresh for each run.
5. Assert result correctness with structured telemetry instead of screenshots as the primary signal.
6. Upload enough artifacts to diagnose CI failures without needing an immediate local repro.

## Non-Goals

- Manual drum input timing tests. Those are a follow-up after the harness is stable.
- Visual snapshot comparisons. Screenshots are useful failure artifacts, not pass/fail assertions for v1.
- Reusing the developer's normal DTXManiaCX app data, config, songs database, or song directory.
- Depending on MCP as an interactive/manual test tool. The CI harness calls the game JSON-RPC API directly.
- Making the first E2E lane blocking on day one. It starts as non-blocking and can be promoted once stable.

## Selected Approach

Use a black-box CI harness through the existing JSON-RPC game API.

The test launches the real game project, writes a temporary configuration, creates a short generated chart, drives the game with API-injected keyboard commands, polls read-only game telemetry, and asserts the final Result-stage summary.

This is preferred over an in-process xUnit-only harness because it covers the real executable startup path, platform project, JSON-RPC bridge, input injection, stage transitions, song loading, performance loop, AutoPlay, and result handoff. It is preferred over a smoke-only hybrid because the first E2E lane should prove actual gameplay completion, not just API availability.

## Architecture

### Fixture Builder

Add reusable E2E support under `DTXMania.Test/E2E/`.

The fixture builder creates a unique temporary run directory containing:

- `appdata/Config.ini`
- `appdata/songs.db` if the game creates it during startup
- `DTXFiles/AutoPlaySmoke/autoplay-smoke.dtx`
- Optional run metadata such as the chosen API port and generated launch token

The generated config sets:

- `EnableGameApi=True`
- `GameApiKey=e2e-autoplay-smoke-key`
- `GameApiPort` to a dynamically selected unused loopback port
- `AutoPlay=True`
- `DTXPath` to the temp run root's `DTXFiles` directory
- `SkinPath` to the repository `System` directory
- `SystemSkinRoot` to the repository `System` directory
- windowed, small, stable graphics settings

The generated chart uses a tiny deterministic note pattern, the title `E2E AutoPlay Smoke`, and a short duration so CI does not spend excessive time in the Performance stage. The first fixture should not depend on external audio files; chip-sound and BGM coverage remain outside this first E2E lane.

### App Data Isolation

Add an environment override to `AppPaths`, for example:

```text
DTXMANIA_APPDATA_ROOT=/tmp/dtxmaniacx-e2e-12345/appdata
```

When present, `AppPaths.GetAppDataRoot()` returns that path instead of the OS default app-data location. This keeps CI and local E2E runs from mutating:

- `~/Library/Application Support/DTXManiaCX`
- `%LOCALAPPDATA%\DTXManiaCX`
- any developer song database or personal config

The override is a general test seam, not a gameplay feature.

### Game Process Driver

The harness starts the selected platform game project with `dotnet run --project DTXMania.Game/DTXMania.Game.Windows.csproj` for the initial Windows CI lane. The same driver can select `DTXMania.Game/DTXMania.Game.Mac.csproj` later if macOS E2E is enabled. It passes:

- `DTXMANIA_APPDATA_ROOT`
- any launch identity already used by the MCP launcher pattern
- the generated API URL/key to the test driver, not to the game process unless needed

The driver waits for `/health`, then sends JSON-RPC calls directly to `/jsonrpc`. It supports:

- polling `getGameState`
- sending key press/release pairs through `sendInput`
- optionally calling `takeScreenshot` on failure
- terminating the child process at the end of the run

This code belongs in reusable test support, not embedded in GitHub Actions YAML.

### Telemetry Assertions

Extend the existing game-state payload with read-only E2E telemetry. The goal is to make assertions strong without adding mutating test-only commands.

Useful telemetry:

- current stage name
- current stage phase, when available
- selected song title
- selected difficulty
- performance readiness
- AutoPlay enabled flag
- current score
- current combo and max combo
- current gauge/life
- judgement counts
- total notes
- completion reason
- result summary fields passed into `ResultStage`

Telemetry should be safe to expose through the existing authenticated Game API. It should not leak file-system paths unless explicitly useful for diagnostics; artifact files already capture generated paths.

## E2E Flow

1. Create a unique temp run root.
2. Write the generated `Config.ini`.
3. Write the generated DTX chart under the temp `DTXFiles` root.
4. Launch the platform game project with `DTXMANIA_APPDATA_ROOT` pointing at the generated `appdata` directory.
5. Wait for JSON-RPC `/health`.
6. Poll `getGameState` until startup reaches `TitleStage`.
7. Send `Enter` to start the game.
8. Poll until `SongSelectionStage`.
9. Send `Enter` once to enter the selected song's status panel.
10. Send `Enter` again to choose the chart.
11. Poll through `SongTransitionStage` into `PerformanceStage`.
12. Poll until stage becomes `ResultStage` or a timeout expires.
13. Assert the final telemetry:
    - selected song title is `E2E AutoPlay Smoke`
    - total notes is greater than zero
    - judgement counts sum to total notes
    - clear flag is true
    - score is greater than zero
    - completion reason is the successful song-ended value used by the project
14. Write logs, final telemetry, generated config, generated chart, and optional screenshot into `TestResults/e2e/`.
15. Stop the game process.

## CI Integration

Add a dedicated E2E job or step to `.github/workflows/build-and-test.yml`.

The first version should run on pull requests and uploads artifacts, but it should not block merges until the lane has proven stable. The harness itself should stay cross-platform, but the workflow can start with the platform most likely to support a real MonoGame window in GitHub Actions. Given the current project shape, Windows is the first candidate because the Windows job already runs the full test suite and uses `ALSOFT_DRIVERS=null`.

The E2E job should publish:

- `TestResults/e2e/final-state.json`
- `TestResults/e2e/game-stdout.log`
- `TestResults/e2e/game-stderr.log`
- `TestResults/e2e/config.ini`
- `TestResults/e2e/autoplay-smoke.dtx`
- `TestResults/e2e/failure-screenshot.png` when screenshot capture succeeds after failure
- a TRX or equivalent test result when the E2E is implemented as xUnit

## Failure Handling

Every boundary gets an explicit timeout:

- game process launch
- `/health` readiness
- `TitleStage`
- `SongSelectionStage`
- `PerformanceStage`
- `ResultStage`

Failures should classify the boundary that failed, include the last observed telemetry payload, and preserve stdout/stderr. If the process exits early, the failure should report the exit code and captured output. If an assertion fails at Result, the final state should be written before the assertion throws.

The driver must always attempt cleanup in `finally`, including process termination and disposal of temporary resources owned by the test run.

## Testing Strategy

The first test is an E2E test, but the support code should still have small unit coverage where useful:

- `AppPaths` respects `DTXMANIA_APPDATA_ROOT` and falls back to OS defaults when unset.
- The fixture builder writes a complete config and generated DTX chart.
- The JSON-RPC driver parses success/failure payloads without needing a live game.
- Telemetry projection returns stable defaults when a stage does not expose optional fields.

The actual full-journey test should run through the new E2E CI lane, not the normal Mac-safe unit suite. It may use xUnit for reporting, but it should be clearly categorized, for example with `[Trait("Category", "E2E")]`, so it can be run independently.

## Risks

**CI window/audio stability.** MonoGame may require platform-specific environment setup in CI. Mitigation: start as non-blocking, capture artifacts, and keep the workflow platform-selectable.

**Timing flakiness.** Stage transitions and song loading are asynchronous. Mitigation: poll telemetry with clear timeouts instead of sleeping fixed durations.

**Insufficient telemetry.** Basic `getGameState` currently cannot prove gameplay success. Mitigation: add read-only telemetry first, then keep the E2E assertions data-driven.

**App-data pollution.** The current path helper uses OS app-data locations. Mitigation: implement `DTXMANIA_APPDATA_ROOT` and assert it in tests.

**Fixture drift.** Committed fixture charts can become stale. Mitigation: generate the chart fresh from harness code and archive the generated chart on failure.

## Promotion Path

1. Land the E2E harness and non-blocking Windows CI job.
2. Observe several CI runs and fix environment-specific failures.
3. Promote the job to blocking once stable.
4. Add a second E2E case for injected manual drum input timing.
5. Evaluate whether macOS can run the same E2E lane reliably or should keep only Mac-safe unit coverage.
