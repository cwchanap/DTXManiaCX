# HPA-519 Managed Process-Boundary Crash Reporting — Design

**Date:** 2026-08-02  
**Status:** Approved  
**Linear:** HPA-519 — Crash report  
**Scope:** Capture managed fatal exceptions observed at the executable process boundary, retain a small sanitized diagnostic bundle, and offer a non-blocking GitHub issue handoff on the next launch.

## Summary

DTXManiaCX will add a dedicated crash-reporting subsystem that is available before MonoGame initialization and remains alive for the full process lifetime. When a managed exception escapes game construction, startup, or `Game.Run()` to the executable entry point, the subsystem writes a sanitized local crash bundle. On the next successful visit to the title screen, the player sees a non-blocking notification and may review the report, open a prefilled GitHub issue, open the report folder, dismiss the notification, or delete the report.

The game never uploads crash data automatically. Opening GitHub includes only a small allowlisted summary in the issue URL; the player must inspect and manually attach the ZIP.

## Approved decisions

| Area | Decision |
|---|---|
| Collection policy | Always capture locally; ask for user action on the next launch |
| Delivery channel | Prefilled GitHub issue; player manually attaches the ZIP |
| Failure scope | Managed fatal exceptions reaching the executable process boundary |
| Diagnostic depth | Balanced, structured, and sanitized |
| Recovery UX | Non-blocking title-screen notification |
| Retention | Keep the latest five completed reports |
| Architecture | Dedicated crash-reporting subsystem with narrow service boundaries |
| Automatic upload | Not allowed |
| Native dumps / hangs | Deferred to separate future work |

## Goals

- Capture exceptions thrown during game construction, startup, or the MonoGame run loop when they reach the executable entry point.
- Preserve useful evidence even when graphics, audio, input, configuration, or SQLite initialization is incomplete.
- Include recent structured log templates, semantic breadcrumbs, safe runtime context, and an exception chain.
- Sanitize sensitive values before anything is persisted.
- Avoid blocking or delaying normal startup after a previous crash.
- Give players a low-friction path to report the problem through GitHub while preserving explicit consent.
- Keep crash reporting isolated, testable, and reusable for later native-dump, arbitrary-thread fatal-handler, or hang-report extensions.

## Non-goals

- Native Windows minidumps, macOS crash logs, signals, or hard process failures.
- Hang, freeze, or watchdog detection.
- Additional global fatal handlers for arbitrary background threads in the first implementation.
- `TaskScheduler.UnobservedTaskException`, which is not a reliable fatal-crash signal.
- Automatic telemetry, background upload, or direct GitHub attachment upload.
- Screenshots, recordings, save databases, song files, chart files, audio, or arbitrary user documents.
- An in-game log viewer or a full crash-report management stage.
- GitHub authentication or confirmation that an issue was actually submitted.

## Existing integration points

The current executable entry point constructs `Game1` and calls `Run()` directly. The process boundary is therefore a narrow place to introduce crash capture without scattering fatal handlers through the game.

`BaseGame` currently creates a shared `ILoggerFactory` with console logging. Installed Windows and macOS builds need a bounded in-memory provider because console output is not a reliable post-crash artifact for end users.

`AppPaths` already centralizes the cross-platform application-data root. Crash reports should use that abstraction rather than introduce platform-specific path logic elsewhere.

`TitleStage` already owns title-screen rendering and input. It should present the recovery notification, while all storage, sanitization, and external-launch behavior remains behind narrow interfaces supplied by the game context.

## Architecture

### Process-level ownership

Create a thread-safe `CrashReportRuntime` before constructing `Game1`:

```text
Program
 ├─ create CrashReportRuntime
 ├─ create Game1(runtime)
 ├─ run game
 └─ catch fatal Exception
      ├─ runtime.CaptureFatal(exception)
      └─ return a non-zero process exit code
```

The runtime exists before MonoGame, graphics, audio, input, configuration, song storage, or SQLite are initialized. It owns the crash-report service, log buffer, breadcrumb buffer, context-provider registry, report store, inbox state, and sanitization policy.

`CrashReportRuntime` also owns the shared logger factory. `BaseGame` consumes that factory and must not dispose it independently. This ensures logs from construction through shutdown remain available if a fatal exception occurs.

Only exceptions reaching the top-level process boundary produce a report in HPA-519. Recoverable exceptions that are caught and logged by game code do not create crash bundles.

### Components

| Component | Responsibility |
|---|---|
| `CrashReportRuntime` | Process-lifetime composition root and owner of crash diagnostics |
| `ICrashReportService` | Capture a fatal exception and query retained reports |
| `CrashReportStore` | Atomic report creation, discovery, cleanup, and latest-five retention |
| `CrashReportSanitizer` | Redact sensitive paths, secrets, song data, and user content |
| `CrashContextCollector` | Collect independent cached snapshots from registered providers |
| `ICrashContextProvider` | Return one bounded immutable context section without I/O |
| `CrashBreadcrumbBuffer` | Retain recent semantic lifecycle and device events |
| `CrashLogBufferProvider` | Retain recent structured `ILogger` state in memory |
| `ICrashReportInbox` | Narrow read/action API exposed to title-screen UI |
| `CrashReportNotification` | Render and operate the title-screen banner and review panel |
| `IExternalLauncher` | Open a URI or local report directory using platform shell integration |

### Dependency direction

```text
Program
  → CrashReportRuntime
      → storage / sanitization / logs / breadcrumbs / context

BaseGame and subsystems
  → shared ILoggerFactory
  → breadcrumb writer
  → context-provider registration

TitleStage
  → ICrashReportInbox
      → report discovery and acknowledgement
      → IExternalLauncher
```

No static service locator is introduced. `IStageGame` exposes only the narrow `ICrashReportInbox` needed by `TitleStage`; it does not expose the complete crash runtime. `TitleStage` cannot create or inspect ZIP internals and does not know the storage format.

## Fatal-crash lifecycle

When a managed exception escapes to `Program`:

1. Freeze a bounded snapshot of recent structured logs and breadcrumbs.
2. Capture the exception type, sanitized message, sanitized stack trace, and bounded inner-exception chain.
3. Read the latest cached snapshot from each registered context provider independently.
4. Sanitize every persisted text value.
5. Write each bundle entry to a uniquely named temporary archive.
6. Flush and close the archive.
7. Atomically rename it to the final report filename.
8. Remove stale temporary files and enforce latest-five retention on completed reports.
9. Return control to the entry point and terminate with a non-zero exit code.

The capture path is synchronous, bounded, dependency-light, and best effort. It does not require the network, SQLite, graphics, audio, shell integration, or the MonoGame update loop.

Context providers must expose already-cached state. Fatal capture never performs active device enumeration, filesystem scanning, database queries, network access, or waits on another thread.

## Storage and bundle contract

### Storage location

Add `AppPaths.GetCrashReportsRoot()`:

```text
<DTXManiaCX app data>/CrashReports/
├─ crash-20260802-211530Z-a4f29c.zip
├─ crash-20260801-074122Z-81de0b.zip
└─ inbox-state.json
```

Temporary files use a distinct extension such as `.tmp`, so discovery and retention never treat incomplete writes as valid reports.

### ZIP contents

Schema version 1 uses the following entries:

```text
report.json
exception.txt
logs.ndjson
breadcrumbs.json
README.txt
```

#### `report.json`

Contains:

- report schema version;
- report ID and UTC capture timestamp;
- application/build identifier from `AssemblyInformationalVersionAttribute`, falling back to assembly version;
- source commit only when it is already embedded in assembly informational metadata;
- .NET runtime, operating system, and process architecture;
- process uptime and initialization milestone;
- current stage when available;
- safe graphics, audio, and input/MIDI summaries;
- allowlisted configuration values;
- sanitized context-provider status and collection-error categories;
- included entry names and their schema versions;
- truncation indicators for logs, breadcrumbs, exception depth, or fields.

Provider status must distinguish:

- `Available`;
- `NotInitialized`;
- `Unavailable`;
- `CollectionFailed`.

#### `exception.txt`

Contains the exception chain in a human-readable form:

- type;
- sanitized message;
- sanitized stack trace, including removal of source-file paths;
- recursively captured inner exceptions up to a fixed depth.

#### `logs.ndjson`

Contains recent structured log records with:

- UTC timestamp;
- log level;
- category;
- event ID when available;
- message template or event name;
- allowlisted scalar properties only;
- sanitized exception type and stack summary when attached.

Do not persist arbitrary rendered log messages or arbitrary string-valued structured properties. These may contain song titles, filenames, search text, usernames, or other user content that cannot be identified reliably after interpolation. Unknown dynamic string values are replaced with `[REDACTED]`; unstructured log messages without a safe template are represented as `[UNCLASSIFIED MESSAGE OMITTED]`.

Preserve at most 500 entries and approximately 512 KB after serialization. Newest useful entries take precedence when a limit is reached.

#### `breadcrumbs.json`

Contains at most 100 semantic events with:

- UTC timestamp;
- stable event name;
- allowlisted scalar properties.

#### `README.txt`

Explains that:

- the bundle was generated locally;
- no data was uploaded automatically;
- the player should inspect the bundle before attaching it to GitHub;
- the report ID should be retained in the GitHub issue;
- reports may contain technical device and runtime information despite sanitization.

### Size limits

- Target normal ZIP size: below 1 MB.
- Individual messages and fields are truncated defensively.
- Large or cyclic exception chains are bounded.
- Context-provider snapshots have fixed output limits.
- The bundle never includes screenshots, native dumps, databases, songs, charts, audio, or complete configuration files.

## Sanitization and privacy

Sanitization occurs before writing every bundle entry. It is not deferred until GitHub handoff.

### Always redact

- `GameApiKey` and any future secret-bearing configuration values;
- user home and application-data path prefixes;
- absolute song, chart, skin, cache, configuration, source-code, and temporary paths;
- song titles, filenames, folder names, chart identifiers derived from user content, and search text;
- MIDI hardware identifiers, serial numbers, network addresses, and OS usernames;
- command-line arguments and environment variables unless explicitly allowlisted;
- URI query strings or headers that may contain credentials.

Device summaries may include counts, backend type, and non-unique product/model names, but never stable hardware IDs or serial numbers.

### Configuration policy

Configuration capture is allowlist-only. Initial safe values may include:

- screen resolution and fullscreen state;
- VSync state;
- audio buffer size;
- autoplay and no-fail flags;
- whether the Game API is enabled, never its key;
- selected input mode;
- counts of bound and unbound controls.

The full `ConfigData` object must never be serialized.

### Sanitizer failure

If a field cannot be safely classified or sanitized, persist `[REDACTED]`. A sanitizer error must never cause the original unsanitized value to be written.

## Logs, breadcrumbs, and context providers

### Shared logging

The process logger factory should include:

- the existing console provider for developer diagnostics;
- `CrashLogBufferProvider` for bounded in-memory retention.

The crash buffer is not a permanent rolling log. It exists solely to preserve recent crash-safe templates and metadata if the process terminates unexpectedly. Buffer mutation and snapshotting must be thread-safe and bounded without waiting on asynchronous work.

### Semantic breadcrumbs

Breadcrumbs are deliberately sparse and are not a second general-purpose logging system. Initial events include:

- process and initialization milestones;
- stage transition requested, started, and completed;
- configuration screen opened or closed;
- graphics device lost or reset;
- audio or MIDI device attached, detached, or selected;
- entry into song selection or gameplay without song identity;
- orderly exit requested.

Stage transitions should be emitted centrally by `StageManager`. Device events should be emitted by the subsystem that owns the cached device state.

### Context providers

Providers register as systems become available:

```text
process/runtime provider       always available
application/build provider     always available
configuration provider         after config load
graphics provider              after graphics initialization
stage provider                 after StageManager exists
input/MIDI provider            after input initialization
audio provider                 after audio initialization
```

Each provider:

- returns an immutable cached snapshot;
- produces only allowlisted and sanitized fields;
- performs no I/O, enumeration, blocking waits, or cross-thread dispatch during capture;
- has a strict output-size bound;
- is read independently;
- cannot prevent other sections from being captured.

A provider that throws is recorded as `CollectionFailed`; the original fatal exception remains the primary report failure.

## Title-screen recovery experience

### Notification

When `TitleStage` activates, it queries `ICrashReportInbox` for unacknowledged reports. If one or more exist, show one compact upper-right banner:

```text
CRASH REPORT AVAILABLE
2 reports saved · Click or press F8 to review
```

The banner:

- does not delay startup;
- does not automatically take keyboard focus;
- leaves normal title-menu navigation unchanged while closed;
- summarizes all unacknowledged reports;
- remains visible while the title stage is active;
- is absent when no unacknowledged reports exist.

### Review panel

Clicking the banner or pressing `F8` opens a lightweight panel for the newest report:

```text
DTXManiaCX stopped unexpectedly
August 2, 2026 at 9:15 PM

Report ID: a4f29c
Failure: NullReferenceException
Location: DrumConfig

[Open GitHub Issue]  [Open Report Folder]
[Delete Report]      [Dismiss]
```

When several reports exist, Previous and Next navigate the retained reports. The panel consumes title-screen input while open. `Escape` or Dismiss returns to the normal title menu immediately.

Delete Report opens a compact confirmation inside the panel before deleting the selected report.

The panel displays only safe summary metadata already stored in `report.json`; it does not render raw logs or stack traces.

### Report state

Storage and notification state are distinct:

- **Unacknowledged:** included in the title-screen notification count.
- **Acknowledged:** report remains on disk but no longer triggers the notification.
- **Deleted:** report file and corresponding inbox metadata are removed.

The following successful actions acknowledge a report:

- opening the GitHub issue;
- opening the report folder;
- selecting Dismiss.

Acknowledged never means submitted. DTXManiaCX cannot verify that a GitHub issue was completed or the ZIP was attached.

State is stored in a versioned, atomically written `inbox-state.json`. Missing or corrupted state is non-fatal; discovered report files become unacknowledged again.

### GitHub issue handoff

Open `https://github.com/cwchanap/DTXManiaCX/issues/new` with a prefilled title and body containing only:

- report ID;
- app version/build identifier;
- OS and architecture;
- current stage or initialization milestone;
- exception type;
- concise instructions to inspect and drag the ZIP into the issue.

Do not place stack traces, logs, paths, device IDs, exception messages, or ZIP content in the URL.

Opening GitHub does not delete the report. Reports remain until explicit deletion or retention removes them.

### External launcher failures

`IExternalLauncher` provides only:

- `OpenUri(...)`;
- `OpenDirectory(...)`.

If browser or file-manager launch fails:

- keep the panel open;
- show a brief in-game error;
- leave the report unacknowledged;
- allow the player to retry.

## Retention and cleanup

- Retain the five newest completed `.zip` or emergency `.txt` reports by capture timestamp.
- Run retention after a successful capture and during normal startup discovery.
- Delete the oldest excess reports and their inbox-state entries.
- Ignore and remove stale temporary files during normal startup.
- A cleanup or state-write failure is logged but never blocks game startup.
- Explicit UI deletion requires the confirmation defined above.

## Crash-path resilience

Crash reporting must never replace the original failure.

- A failing context provider cannot prevent the core exception section from being written.
- A failing optional ZIP entry is listed in `report.json` when possible.
- If ZIP creation fails, write a minimal sanitized emergency `.txt` report containing report ID, timestamp, app/runtime information, and exception chain.
- If the report directory cannot be created or written, emit a last-resort sanitized message to standard error and terminate; do not retry indefinitely.
- Use unique temporary names and atomic finalization.
- Avoid asynchronous fire-and-forget work during fatal capture.
- Avoid graphics, SQLite, network, shell-launch, or game-loop dependencies on the crash path.

## Testing strategy

### Unit tests

- exception-chain serialization and depth limits;
- deterministic schema versioning;
- redaction of home paths, app-data paths, source paths, API keys, usernames, song names, and user filenames;
- omission of arbitrary rendered log messages and dynamic string properties;
- allowlisted configuration capture only;
- structured log count and byte truncation;
- breadcrumb count and field truncation;
- thread-safe bounded buffer snapshotting;
- one failing context provider does not affect other providers;
- provider statuses distinguish unavailable, not initialized, and failed;
- temporary-write finalization and emergency-text fallback;
- six captures retain exactly the newest five;
- stale temporary cleanup;
- corrupt or missing `inbox-state.json` recovery;
- acknowledgement, deletion confirmation, and retention-state cleanup;
- GitHub issue URL contains only allowlisted summary fields;
- failed URI or directory launch preserves unacknowledged state.

### Stage/UI tests

Use fake `ICrashReportInbox` and `IExternalLauncher` implementations:

- no unacknowledged report means no banner;
- multiple reports show one summarized banner;
- normal title-menu input remains functional while the banner is closed;
- F8 and mouse activation open the panel;
- panel input does not leak into normal menu actions;
- Previous and Next select the expected report;
- Dismiss acknowledges without deleting;
- Delete requires confirmation, then removes the selected report and refreshes counts;
- failed browser or folder launch displays an error and does not acknowledge.

### Integration verification

Use `DTXMANIA_APPDATA_ROOT` to isolate test output. Introduce an internal injectable application-runner seam for tests; do not ship a production command-line or environment-variable crash trigger.

1. Make the test runner throw before graphics initialization and verify a report is produced.
2. Make the test runner throw after reaching the title stage and verify stage context and recent safe log templates are present.
3. Restart and verify the non-blocking notification appears.
4. Generate six reports and confirm only five completed reports remain.
5. Inspect all bundle entries for prohibited path, key, username, song, filename, rendered-message, and arbitrary-string data.
6. Verify GitHub handoff does not transmit the ZIP or raw diagnostic content.

## Delivery decomposition

HPA-519 remains the parent feature. Implementation is split into two child issues, each sized for a junior AI implementation agent and no more than approximately three engineer days.

### Child 1 — Crash capture foundation and sanitized bundle storage

**Scope**

- Process-level managed fatal exception boundary.
- `CrashReportRuntime` and shared logger-factory ownership.
- Bounded thread-safe structured log-template and breadcrumb buffers.
- Cached context-provider registration and independent collection.
- Sanitization and allowlisted configuration capture.
- Versioned ZIP contract and emergency-text fallback.
- Atomic persistence, stale-temp cleanup, and latest-five retention.
- Unit and integration tests for capture, privacy, resilience, and retention.

**Acceptance criteria**

- A synthetic exception before MonoGame initialization produces a valid sanitized report.
- A synthetic exception during the game loop includes available context, safe log templates, and breadcrumbs.
- One failed provider does not prevent report creation.
- No secret, user path, username, song identity, arbitrary rendered message, or arbitrary configuration value is persisted.
- Six reports result in exactly the newest five completed reports.
- Failure to build a ZIP attempts the minimal emergency-text fallback.

**Estimate:** 2–3 engineer days.

### Child 2 — Title-screen crash inbox and GitHub handoff

**Depends on:** Child 1.

**Scope**

- Versioned inbox-state persistence.
- `ICrashReportInbox` facade.
- Non-blocking title-screen notification and review panel.
- Previous/Next navigation, acknowledgement, dismissal, confirmed deletion, and errors.
- Prefilled sanitized GitHub issue URL.
- Report-folder and browser launching through `IExternalLauncher`.
- UI and launcher-failure tests.

**Acceptance criteria**

- The title screen behaves normally when no report exists.
- Unacknowledged reports produce one non-blocking summarized banner.
- Opening or dismissing a report acknowledges it without deleting it.
- Confirmed deletion removes the report and updates notification state.
- GitHub handoff includes only the approved summary fields and never uploads data.
- Browser or file-manager launch failure leaves the report unacknowledged and retryable.

**Estimate:** approximately 2 engineer days.

## Future follow-up candidates

- Exactly-once `AppDomain.UnhandledException` capture for fatal exceptions on arbitrary managed threads.
- Native Windows and macOS crash artifacts.
- Hang/watchdog detection with bounded thread-state evidence.
- Optional one-time telemetry consent and managed upload service.
- Crash-signature grouping and duplicate detection.
- Release/build symbol publishing for improved stack resolution.
- A developer-only diagnostic command to generate a synthetic crash report without terminating the game.

## Parent acceptance criteria

HPA-519 is complete when both child issues are done and the following end-to-end flow passes on supported Windows and macOS builds:

1. A managed fatal exception reaching the executable entry point produces a completed sanitized local report.
2. The next launch reaches the title screen without being blocked.
3. A non-blocking notification exposes the retained report.
4. The player can open a prefilled GitHub issue and manually attach the ZIP.
5. No report data is uploaded without the player’s explicit action.
6. Only the latest five completed reports are retained.
