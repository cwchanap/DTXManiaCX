# HPA-519 Managed Crash Reporting — Design Review Amendments

**Date:** 2026-08-02  
**Status:** Normative review addendum  
**Canonical design:** [`2026-08-02-hpa-519-managed-crash-report-design.md`](./2026-08-02-hpa-519-managed-crash-report-design.md)  
**Linear:** HPA-519, with implementation children HPA-529 and HPA-530

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

## Non-negotiable design invariants

1. **No automatic upload.** Crash artifacts are written locally. The player must explicitly open GitHub, inspect the report, and attach the ZIP manually.
2. **Process-boundary scope.** The first implementation captures only managed fatal exceptions that reach the executable entry point during game construction, startup, or `Game.Run()`.
3. **Crash-path isolation.** Fatal capture performs no network access, database queries, device enumeration, graphics work, shell launch, cross-thread dispatch, or asynchronous fire-and-forget work.
4. **Sanitize before persistence.** Secrets, user paths, usernames, song/chart identity, stable hardware identifiers, arbitrary rendered messages, and non-allowlisted configuration values never reach disk.
5. **Cached context only.** Context providers expose bounded snapshots prepared during normal operation. A failed provider cannot prevent the core exception report from being written.
6. **Bounded storage.** Reports use atomic finalization, emergency-text fallback, stale-temporary cleanup, and one shared latest-five limit across all completed report formats.
7. **Non-blocking recovery UX.** The title screen starts normally. Pending reports appear through one optional notification and a user-invoked review panel.
8. **Acknowledged is not submitted.** Opening GitHub, opening the folder, or dismissing the notification may acknowledge a report, but CX never claims an issue was created.

## Required HPA-529 implementation gates

### 1. Transfer logger-factory ownership completely

`CrashReportRuntime` becomes the sole process-lifetime owner of the shared `ILoggerFactory`.

Implementation review must verify all of the following:

- `BaseGame` receives the shared factory instead of creating its own.
- `_loggerFactory.Dispose()` is removed from `BaseGame.DisposeManagedResources()`.
- `BaseGame` does not dispose any provider owned by `CrashReportRuntime`.
- `CrashReportRuntime` disposes the factory exactly once after fatal capture and game cleanup have completed.
- Constructor and shutdown tests detect premature disposal and double disposal.

This is an explicit migration requirement, not merely a documentation preference.

### 2. Verify MonoGame exception propagation on both targets

The process-boundary design depends on exceptions thrown from MonoGame callbacks reaching the `Program` catch boundary.

Before HPA-529 is considered complete, verify on both supported backends:

- Windows x64 / MonoGame WindowsDX;
- macOS arm64 / MonoGame DesktopGL.

For each target, inject controlled exceptions from both `Update` and `Draw` and confirm:

- the exception reaches the executable process boundary exactly once;
- one completed crash report is written;
- the original exception remains the primary failure;
- no framework-level handler silently consumes the exception.

The internal application-runner seam covers pre-game and orchestration tests, but does not replace this backend-specific verification. If either backend consumes callback exceptions, implementation must stop and revise the capture boundary rather than silently declaring HPA-529 complete.

### 3. Degrade safely when crash-runtime bootstrap fails

Crash reporting is diagnostic infrastructure and must not prevent the game from launching because of a recoverable setup failure.

Use an explicit best-effort bootstrap contract, such as `CrashReportRuntime.TryCreate(...)`, that can return a disabled/no-op runtime when expected environment or setup failures occur. Requirements:

- path resolution, directory permission, report-store setup, or provider-initialization failures emit a minimal sanitized message to standard error;
- the game continues without crash capture when the failure is recoverable;
- report-directory creation remains lazy where practical so read-only startup does not fail unnecessarily;
- the disabled runtime still provides safe no-op logger/inbox dependencies required by composition;
- catastrophic runtime failures such as `OutOfMemoryException`, `StackOverflowException`, or `AccessViolationException` are not broadly swallowed under the graceful-degradation rule.

### 4. Define capture and game-disposal ordering

The review claim that `using var` necessarily leaves `Game1` undisposed is not correct; disposal behavior depends on the generated scope and catch/finally placement. The actual risk is ambiguous ordering and a disposal exception masking the original crash.

The entry-point lifecycle must therefore be explicit:

1. retain the original exception;
2. synchronously capture the crash from cached context **before** game disposal mutates or releases that context;
3. perform guarded best-effort `Game1.Dispose()` afterward;
4. record or write a sanitized secondary disposal failure without replacing the original exception;
5. dispose `CrashReportRuntime` last.

Crash capture must succeed without requiring successful `Game1` disposal. Open SQLite, graphics, audio, or other game resources must not block writing to the separate crash-report directory.

### 5. Reconcile `StartupTimingTrace` ownership

`StartupTimingTrace` remains responsible for startup-performance measurement; `CrashReportRuntime` remains responsible for crash diagnostics. They must not become competing process singletons.

Required composition order:

1. create the startup trace at process entry;
2. create the crash runtime and optionally register a read-only/cached startup-milestone context source;
3. construct `Game1` with the same startup trace.

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

## Required HPA-530 implementation gates

### 1. Specify cross-platform launcher behavior

`IExternalLauncher` remains a narrow abstraction, with platform implementations that avoid command-shell string construction:

- **Windows:** use `Process.Start` with `ProcessStartInfo.UseShellExecute = true` for the URI or directory path.
- **macOS:** execute `/usr/bin/open` with the URI or path supplied through `ArgumentList`.

Requirements:

- never invoke `cmd /c`, `sh -c`, or concatenate user-controlled values into a shell command;
- validate that GitHub URIs use HTTPS and the expected repository host/path;
- accept only the internally resolved crash-report directory for folder opening;
- treat non-zero exit, process-start failure, or unsupported platform as a retryable UI error;
- acknowledge a report only after the launcher reports successful process start.

### 2. Keep report retention unified

The latest-five limit counts completed `.zip` and emergency `.txt` reports together, ordered by authoritative capture metadata with a deterministic filename/timestamp fallback. ZIP and text reports do not receive separate quotas.

## Reviewer focus

### Architecture

- `CrashReportRuntime` is available before `Game1` and owns process-lifetime logging and crash-report services.
- `BaseGame` no longer creates or disposes the shared logger factory.
- Fatal capture occurs before guarded game disposal and preserves the original exception.
- `TitleStage` receives only `ICrashReportInbox`; ZIP creation, sanitization, retention, and shell operations remain outside the stage.
- No static service locator or broad global exception-handler behavior is introduced.

### Privacy and report format

- The ZIP contract remains versioned and machine-readable: `report.json`, `exception.txt`, `logs.ndjson`, `breadcrumbs.json`, and `README.txt`.
- Logs preserve safe templates, event metadata, and centrally allowlisted scalar properties rather than arbitrary rendered strings.
- Configuration capture uses only the concrete field mapping defined above; the full `ConfigData` object is never serialized.
- GitHub issue URLs contain only report ID, build identity, OS/architecture, stage or initialization milestone, exception type, and attachment instructions.

### Resilience

- Expected crash-runtime bootstrap failures degrade to a disabled runtime without blocking game startup.
- Each optional report section can fail independently.
- ZIP failure attempts a minimal sanitized emergency report.
- Report persistence failure does not trigger retry loops or obscure the original fatal exception.
- Inbox-state corruption is non-fatal and causes discovered reports to become unacknowledged again.

### User experience

- The notification never steals focus or blocks normal title-menu navigation.
- The review panel contains safe summary metadata only and does not render raw logs or stack traces.
- Delete requires confirmation.
- Browser or folder-launch failures remain visible, retryable, and unacknowledged.

## Delivery order

1. **HPA-529 — Process-boundary capture and sanitized local bundles**
   - Establish runtime bootstrap/degradation, explicit logger ownership, process/backend verification, capture/disposal ordering, safe-log policy, cached context, bundle contract, sanitization, retention, fallback, and tests.
2. **HPA-530 — Title-screen inbox and GitHub handoff**
   - Consume the stable report-store contract and add notification, review actions, inbox state, exact platform launch behavior, and UI tests.

HPA-530 remains blocked by HPA-529. Native dumps, arbitrary-thread fatal handlers, hang detection, screenshots, automatic telemetry, and managed upload remain future work.

## Approval gate

The design is ready for implementation planning when reviewers confirm:

- logger ownership and disposal migration are explicit;
- callback-exception propagation is verified on both MonoGame backends;
- crash-runtime bootstrap can degrade safely for recoverable failures;
- capture-before-dispose ordering preserves the original exception;
- startup-timing ownership is unambiguous;
- privacy rules are centralized, concrete, and enforceable through tests;
- completed report retention is unified across formats;
- cross-platform launcher behavior is explicit and avoids shell injection;
- the two implementation tickets have stable boundaries;
- the title-screen UX remains optional and non-blocking.
