# HPA-519 Managed Crash Reporting — Design Review Notes

**Date:** 2026-08-02  
**Status:** Draft review companion  
**Canonical design:** [`2026-08-02-hpa-519-managed-crash-report-design.md`](./2026-08-02-hpa-519-managed-crash-report-design.md)  
**Linear:** HPA-519, with implementation children HPA-529 and HPA-530

## Purpose

This document provides a compact review checklist for the approved HPA-519 managed crash-reporting design. It does not replace or override the canonical specification. Product decisions, architecture, report schema, privacy rules, failure handling, and acceptance criteria remain authoritative in the canonical design.

## Non-negotiable design invariants

1. **No automatic upload.** Crash artifacts are written locally. The player must explicitly open GitHub, inspect the report, and attach the ZIP manually.
2. **Process-boundary scope.** The first implementation captures only managed fatal exceptions that reach the executable entry point during game construction, startup, or `Game.Run()`.
3. **Crash-path isolation.** Fatal capture performs no network access, database queries, device enumeration, graphics work, shell launch, cross-thread dispatch, or asynchronous fire-and-forget work.
4. **Sanitize before persistence.** Secrets, user paths, usernames, song/chart identity, stable hardware identifiers, arbitrary rendered messages, and non-allowlisted configuration values never reach disk.
5. **Cached context only.** Context providers expose bounded immutable snapshots prepared during normal operation. A failed provider cannot prevent the core exception report from being written.
6. **Bounded storage.** Reports use atomic finalization, emergency text fallback, stale-temporary cleanup, and latest-five retention.
7. **Non-blocking recovery UX.** The title screen starts normally. Pending reports appear through one optional notification and a user-invoked review panel.
8. **Acknowledged is not submitted.** Opening GitHub, opening the folder, or dismissing the notification may acknowledge a report, but CX never claims an issue was created.

## Reviewer focus

### Architecture

- `CrashReportRuntime` is created before `Game1` and owns process-lifetime logging and crash-report services.
- `BaseGame` consumes the shared logger factory without owning or prematurely disposing it.
- `TitleStage` receives only `ICrashReportInbox`; ZIP creation, sanitization, retention, and shell operations remain outside the stage.
- No static service locator or broad global exception-handler behavior is introduced.

### Privacy and report format

- The ZIP contract remains versioned and machine-readable: `report.json`, `exception.txt`, `logs.ndjson`, `breadcrumbs.json`, and `README.txt`.
- Logs preserve safe templates, event metadata, and allowlisted scalar properties rather than arbitrary rendered strings.
- Configuration capture is allowlist-only; the full `ConfigData` object is never serialized.
- GitHub issue URLs contain only report ID, build identity, OS/architecture, stage or initialization milestone, exception type, and attachment instructions.

### Resilience

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
   - Establish the runtime, logging/breadcrumb buffers, cached context, bundle contract, sanitization, retention, fallback, and tests.
2. **HPA-530 — Title-screen inbox and GitHub handoff**
   - Consume the stable report-store contract and add notification, review actions, inbox state, launch behavior, and UI tests.

HPA-530 remains blocked by HPA-529. Native dumps, arbitrary-thread fatal handlers, hang detection, screenshots, automatic telemetry, and managed upload remain future work.

## Approval gate

The design is ready for implementation planning when reviewers confirm:

- the process-boundary scope is explicit;
- privacy rules are enforceable through allowlists and tests;
- no fatal-path dependency can block report persistence;
- the two implementation tickets have stable boundaries;
- the title-screen UX remains optional and non-blocking.
