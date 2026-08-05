# HPA-519 Managed Crash Reporting — Design Review Amendments

**Date:** 2026-08-02  
**Status:** Normative review addendum  
**Canonical design:** [`2026-08-02-hpa-519-managed-crash-report-design.md`](./2026-08-02-hpa-519-managed-crash-report-design.md)  
**Linear:** HPA-519, with implementation children HPA-529 and HPA-530

## Schema v2 amendment (2026-08-05) — text-only reports

**Status:** Normative. The "Required HPA-530 implementation gates" and "Reviewer focus" sections below still reference ZIP bundles, emergency-text fallback, `ICrashReportInbox`, `inbox-state.json`, and manual ZIP attachment. Those references are **obsolete** under schema v2. See the canonical design's "Schema v2 amendment" section for the shipped text-only format, privacy model, and HPA-530 implications.

In particular, under schema v2:

- HPA-530 gate 2 ("Define emergency-report inbox and review behavior") no longer applies — there is no emergency-text format and no `ICrashReportInbox`. HPA-530 discovers `.txt` reports directly.
- HPA-530 gate 1 (cross-platform launcher) still applies if GitHub/folder handoff is implemented, but the attachment is a `.txt` file, not a ZIP.
- The "Privacy and report format" reviewer-focus bullets describing `report.json`, `exception.txt`, `logs.ndjson`, `breadcrumbs.json`, and `README.txt` are obsolete; the shipped report is a single `.txt` with header + sections.
- The sanitization contract is now: registered path prefixes → `[PATH]`, home segments → `[USER]`, registered secrets (including `GameApiKey`) → `[REDACTED]`, and URI credentials (userinfo + credential-bearing query params) → `[REDACTED]`. Exception messages are otherwise preserved verbatim.

## Purpose

This document records implementation constraints discovered while reviewing the approved HPA-519 managed crash-reporting design against the current DTXManiaCX codebase.

The canonical design remains authoritative for product scope, architecture, report schema, privacy policy, failure handling, and UX. The clarifications and verification gates below are **required amendments** where the canonical design was ambiguous or did not name an existing implementation trap.

## Validated codebase findings

- `Program.cs` currently constructs `Game1`, starts `StartupTimingTrace`, and calls `Run()` directly without a process-level exception boundary.
- `BaseGame` currently creates the shared console-only `ILoggerFactory`.
- `BaseGame.DisposeManagedResources()` currently calls `_loggerFactory.Dispose()`.
- `AppPaths` owns the application-data-root policy and the `DTXMANIA_APPDATA_ROOT` test override.
- `StageManager` already centralizes stage-transition logging and is the correct breadcrumb insertion point.
- `ConfigData` contains `GameApiKey`, but does not contain a concrete `InputMode` property.
- No existing reusable browser/directory launcher exists in `DTXMania.Game`.
- `DTXMania.E2E` targets `net8.0-windows7.0`; the gameplay process harness runs in the Windows CI job, while macOS CI runs the macOS unit-test project rather than gameplay E2E.

## Non-negotiable design invariants

1. **No automatic upload.** Crash artifacts are written locally. The player must explicitly open GitHub, inspect the report, and attach the ZIP manually.
2. **Process-boundary scope.** The first implementation captures only managed fatal exceptions that reach the executable entry point during game construction, startup, or `Game.Run()`.
3. **Crash-path isolation.** Fatal capture performs no network access, database queries, device enumeration, graphics work, shell launch, cross-thread dispatch, or asynchronous fire-and-forget work.
4. **Sanitize before persistence.** Secrets, user paths, usernames, song/chart identity, stable hardware identifiers, arbitrary rendered messages, and non-allowlisted configuration values never reach disk.
5. **Cached context only.** Context providers expose bounded snapshots prepared during normal operation. A failed provider cannot prevent the core exception report from being written.
6. **Bounded storage.** Reports use atomic finalization, emergency-text fallback, stale-temporary cleanup, and one shared latest-five limit across all completed report formats.
7. **Non-blocking recovery UX.** The title screen starts normally. Pending reports appear through one optional notification and a user-invoked review panel.
8. **Acknowledged is not submitted.** Opening GitHub, opening the folder, or dismissing the notification may acknowledge a report, but CX never claims an issue was created.
9. **Logging survives diagnostic degradation.** Losing crash persistence must never disable the existing console logging path.

## Required HPA-529 implementation gates

### 1. Transfer logger-factory ownership completely and define the constructor contract

`CrashReportRuntime` becomes the sole process-lifetime owner of the shared `ILoggerFactory`.

The target production composition contract is:

```text
Program
  ├─ StartupTimingTrace.StartProcess()
  ├─ CrashReportRuntime.TryCreate(...)
  └─ new Game1(startupTrace, crashRuntime.GameDiagnostics)

Game1(StartupTimingTrace, IGameCrashDiagnostics)
  └─ BaseGame(StartupTimingTrace, IGameCrashDiagnostics)
```

`IGameCrashDiagnostics` is a narrow game-facing facade, not a service locator. It exposes only the dependencies consumed during normal game operation:

- `ILoggerFactory`;
- breadcrumb sink;
- cached-context registration/update surface;
- `ICrashReportInbox`.

The complete crash runtime, report store, capture operation, and lifetime controls are not exposed through `IStageGame` or to stages.

Implementation review must verify all of the following:

- `BaseGame` receives the shared factory through `IGameCrashDiagnostics` instead of creating its own.
- `_loggerFactory.Dispose()` is removed from `BaseGame.DisposeManagedResources()`.
- `BaseGame` does not dispose any provider owned by `CrashReportRuntime`.
- `CrashReportRuntime` disposes the factory exactly once after fatal capture and game cleanup have completed.
- Production `BaseGame()` and `Game1()` constructors that self-create diagnostics are removed; there is no fallback constructor with dual ownership.
- Tests inject an explicit enabled or console-only diagnostics facade. Tests that intentionally bypass construction may continue using the existing uninitialized-object seam.
- Constructor and shutdown tests detect premature disposal and double disposal.

This is an explicit migration requirement, not merely a documentation preference.

### 2. Verify MonoGame exception propagation with achievable platform evidence

The process-boundary design depends on exceptions thrown from MonoGame callbacks reaching the `Program` catch boundary.

For both `Update` and `Draw`, verify that:

- the exception reaches the executable process boundary exactly once;
- one completed crash report is written;
- the original exception remains the primary failure;
- no framework-level handler silently consumes the exception.

Verification delivery is platform-specific:

- **Windows x64 / WindowsDX:** add or extend the existing Windows gameplay E2E harness so the check runs automatically in CI.
- **macOS arm64 / DesktopGL:** perform a manual local verification on Apple Silicon using the real game process. Record the exact commands, app build identifier, OS/runtime details, and observed result in the implementation PR or a checked-in verification note.

The current repository has no cross-platform gameplay E2E harness for macOS. HPA-529 does not expand into an E2E infrastructure migration. A reusable macOS CI gameplay harness may be tracked separately. The internal application-runner seam covers pre-game and orchestration tests, but does not replace either real-backend check.

If either backend consumes callback exceptions, implementation must stop and revise the capture boundary rather than silently declaring HPA-529 complete.

### 3. Degrade safely when crash-runtime bootstrap fails without losing console logs

Crash reporting is diagnostic infrastructure and must not prevent the game from launching because of a recoverable setup failure.

Use an explicit best-effort bootstrap contract, such as `CrashReportRuntime.TryCreate(...)`, that returns either:

- an enabled runtime with console logging plus the bounded crash provider; or
- a disabled runtime whose `IGameCrashDiagnostics` still owns a fully functional console `ILoggerFactory`, while crash persistence, crash buffering, breadcrumbs/context capture, and inbox behavior degrade to safe no-ops.

Requirements:

- an unwritable crash directory or failed report-store setup must not silence ordinary game logs;
- path resolution, directory permission, report-store setup, or provider-initialization failures emit a minimal sanitized message to standard error;
- the game continues without crash capture when the failure is recoverable;
- report-directory creation remains lazy where practical so read-only startup does not fail unnecessarily;
- the disabled runtime still provides safe composition dependencies, including the console logger factory and empty inbox;
- catastrophic runtime failures such as `OutOfMemoryException`, `StackOverflowException`, or `AccessViolationException` are not broadly swallowed under the graceful-degradation rule.

This bootstrap rule applies before a game crash occurs. It does not conflict with fatal-capture behavior: once the game has already failed, inability to create or write the report emits a last-resort sanitized stderr message and process termination still follows from the original game exception.

### 4. Replace compiler-generated `using` disposal with an explicit entry-point lifecycle

The review claim that `using var` necessarily leaves `Game1` undisposed is not correct. However, compiler-generated disposal cannot express the required capture-before-dispose ordering or safely prevent a disposal exception from masking the original crash.

`Program.cs` must therefore replace the existing `using var game` with an explicit nullable game variable and explicit `try`/`catch`/`finally` lifecycle:

1. retain the original exception;
2. synchronously capture the crash from cached context **before** game disposal mutates or releases that context;
3. perform guarded best-effort `Game1.Dispose()` afterward;
4. record or write a sanitized secondary disposal failure without replacing the original exception;
5. dispose `CrashReportRuntime` last;
6. return a non-zero exit code for the original fatal exception.

Crash capture must succeed without requiring successful `Game1` disposal. Open SQLite, graphics, audio, or other game resources must not block writing to the separate crash-report directory.

### 5. Reconcile `StartupTimingTrace` ownership

`StartupTimingTrace` remains responsible for startup-performance measurement; `CrashReportRuntime` remains responsible for crash diagnostics. They must not become competing process singletons.

Required composition order:

1. create the startup trace at process entry;
2. create the crash runtime and optionally register a read-only/cached startup-milestone context source;
3. construct `Game1` with the same startup trace and the injected diagnostics facade.

The crash runtime may observe cached milestone data but does not own or replace startup timing.

### 6. Centralize the safe-log property policy

The allowlist for structured log properties must live in one immutable, unit-tested policy component, for example `CrashLogFieldPolicy`.

The policy defines:

- allowed property names;
- permitted scalar types;
- maximum serialized lengths;
- replacement behavior for unknown strings and unstructured messages;
- any field-specific normalization.

Individual logging call sites must not make ad-hoc privacy decisions. Unknown dynamic strings remain `[REDACTED]`, and unsafe unstructured messages remain omitted.

### 7. Pin configuration capture to concrete fields

The first configuration snapshot must map directly to existing `ConfigData` members:

- `ScreenWidth`;
- `ScreenHeight`;
- `FullScreen`;
- `VSyncWait`;
- `BufferSizeMs`;
- `AutoPlay`;
- `NoFail`;
- `EnableGameApi`;
- counts only for `KeyBindings`, `SystemKeyBindings`, `UnboundDrumLanes`, `UnboundDrumButtons`, and `MidiVelocityThresholds`.

Do not capture `GameApiKey`, paths, binding names, MIDI note identities, or complete dictionaries/sets.

The earlier phrase **selected input mode** is removed from the initial configuration allowlist because `ConfigData` has no such field. A future input context provider may expose a stable non-identifying enum if one is introduced explicitly.

### 8. Clarify buffer and snapshot semantics

The log and breadcrumb buffers are bounded, thread-safe, and mutable during normal operation. Fatal capture takes an immutable copy/snapshot. Context-provider snapshots are immutable cached values replaced atomically during normal operation.

No design wording should imply that the live buffers themselves are immutable.

### 9. Keep report retention unified inside `CrashReportStore`

Retention belongs to HPA-529 and the storage boundary, not the title-screen ticket.

The latest-five limit counts completed `.zip` and emergency `.txt` reports together, ordered by authoritative capture metadata with a deterministic filename/timestamp fallback. ZIP and text reports do not receive separate quotas. `ICrashReportInbox` consumes the retained set and never implements its own retention policy.

### 10. Give emergency-text reports a discoverable summary contract

Emergency `.txt` fallback reports must participate in discovery and the next-launch inbox even though they do not contain `report.json`.

The emergency writer must prepend a small, versioned, allowlisted header that `CrashReportStore` can parse without reading arbitrary exception text. Initial header fields are:

- format/schema version;
- report ID;
- UTC capture timestamp;
- application/build identifier;
- OS and architecture;
- stage or initialization milestone when cached;
- exception type.

After the header separator, the file may contain the sanitized human-readable emergency report. If the header is missing or corrupt, discovery falls back to the report ID/timestamp encoded in the filename and exposes `Unknown` for unavailable summary fields.

`CrashReportStore` returns one format-neutral `CrashReportSummary` model for ZIP and emergency-text reports. The UI never parses report files directly.

## Required HPA-530 implementation gates

### 1. Specify cross-platform launcher behavior

`IExternalLauncher` remains a narrow abstraction, with platform implementations that avoid command-shell string construction:

- **Windows:** use `Process.Start` with `ProcessStartInfo.UseShellExecute = true` for the URI or directory path.
- **macOS:** execute `/usr/bin/open` with arguments supplied through `ArgumentList`; add `--` as a separate argument before the validated URI or directory target so a leading-hyphen target cannot be interpreted as an option.

Requirements:

- never invoke `cmd /c`, `sh -c`, or concatenate user-controlled values into a shell command;
- validate that GitHub URIs use HTTPS and the expected repository host/path;
- accept only the internally resolved crash-report directory for folder opening;
- verify the `/usr/bin/open -- <target>` invocation on the supported macOS release during HPA-530 platform smoke testing;
- treat non-zero exit, process-start failure, or unsupported platform as a retryable UI error;
- acknowledge a report only after the launcher reports successful process start.

### 2. Define emergency-report inbox and review behavior

Completed ZIP and emergency-text reports both:

- appear in the unacknowledged notification count;
- participate in Previous/Next navigation;
- can be acknowledged, dismissed, and deleted;
- can open the crash-report folder;
- can open a prefilled GitHub issue using their format-neutral `CrashReportSummary`.

For a ZIP report, the panel renders the safe summary produced from `report.json`. For an emergency-text report, it renders the parsed allowlisted header and an **Emergency fallback report** label. Missing values display as `Unknown`; the panel never reads or displays the free-form exception body.

The attachment instruction must name the actual retained file type: attach the `.zip` bundle for normal reports or the `.txt` fallback for emergency reports.

## Reviewer focus

### Architecture

- `CrashReportRuntime` is available before `Game1` and owns process-lifetime logging and crash-report services.
- A disabled crash runtime preserves fully functional console logging.
- `BaseGame` no longer creates or disposes the shared logger factory.
- The explicit constructor contract prevents dual diagnostics ownership.
- Fatal capture occurs before guarded game disposal and preserves the original exception.
- `TitleStage` receives only `ICrashReportInbox`; report parsing, sanitization, retention, and shell operations remain outside the stage.
- No static service locator or broad global exception-handler behavior is introduced.

### Privacy and report format

- The ZIP contract remains versioned and machine-readable: `report.json`, `exception.txt`, `logs.ndjson`, `breadcrumbs.json`, and `README.txt`.
- Emergency text has a minimal versioned allowlisted summary header.
- Logs preserve safe templates, event metadata, and centrally allowlisted scalar properties rather than arbitrary rendered strings.
- Configuration capture uses only the concrete field mapping defined above; the full `ConfigData` object is never serialized.
- GitHub issue URLs contain only report ID, build identity, OS/architecture, stage or initialization milestone, exception type, and attachment instructions.

### Resilience

- Expected crash-runtime bootstrap failures degrade to console-only diagnostics without blocking game startup.
- Each optional report section can fail independently.
- ZIP failure attempts a minimal sanitized emergency report that remains discoverable by the inbox.
- Report persistence failure does not trigger retry loops or obscure the original fatal exception.
- Inbox-state corruption is non-fatal and causes discovered reports to become unacknowledged again.

### User experience

- The notification never steals focus or blocks normal title-menu navigation.
- ZIP and emergency-text reports have a consistent review and action flow.
- The review panel contains safe summary metadata only and does not render raw logs or stack traces.
- Delete requires confirmation.
- Browser or folder-launch failures remain visible, retryable, and unacknowledged.

## Delivery order

1. **HPA-529 — Process-boundary capture and sanitized local bundles**
   - Establish runtime bootstrap/degradation, explicit constructor and logger ownership, Windows automated and macOS manual backend verification, capture/disposal ordering, safe-log policy, cached context, ZIP/emergency summary contracts, sanitization, unified retention, fallback, and tests.
2. **HPA-530 — Title-screen inbox and GitHub handoff**
   - Consume the stable format-neutral report summary and retained set; add notification, review actions, inbox state, exact platform launch behavior, emergency-report UX, and UI tests.

HPA-530 remains blocked by HPA-529. Native dumps, arbitrary-thread fatal handlers, hang detection, screenshots, automatic telemetry, managed upload, and a reusable macOS gameplay E2E CI harness remain future work.

## Approval gate

The design is ready for implementation planning when reviewers confirm:

- logger ownership and the injected constructor contract are explicit;
- disabled crash capture preserves console logging;
- Windows callback propagation is automated and macOS propagation has recorded real-backend evidence;
- crash-runtime bootstrap can degrade safely for recoverable failures;
- explicit capture-before-dispose ordering preserves the original exception;
- startup-timing ownership is unambiguous;
- privacy rules are centralized, concrete, and enforceable through tests;
- completed report retention is unified inside the storage ticket;
- emergency text has a complete inbox path;
- cross-platform launcher behavior is explicit and avoids option/shell injection;
- the canonical design links to this normative addendum;
- the two implementation tickets have stable boundaries;
- the title-screen UX remains optional and non-blocking.
