# HPA-529 macOS DesktopGL crash propagation

## Recorded environment

- Commit: `dc50aad9c2cb6e748a74496b0399b726683cb6e4`
- Host: macOS 26.5.2 (`25F84`), `arm64`
- .NET SDK: 10.0.100; .NET host: 10.0.0, `arm64`
- Target: `DTXMania.Game/DTXMania.Game.Mac.csproj` (`net8.0`, DesktopGL)

The app-data roots used below were fresh ephemeral temporary directories. Their
absolute paths and the non-production API-key sentinel used for the privacy
check are deliberately omitted from this committed record.

## Build and release-strip check

Commands run:

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug --no-restore
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Release --no-restore
```

Both completed successfully with zero errors and zero warnings.

A byte-wise UTF-16LE metadata search was then performed against
`DTXMania.Game/bin/Debug/net8.0/DTXMania.Game.Mac.dll` and
`DTXMania.Game/bin/Release/net8.0/DTXMania.Game.Mac.dll`, rather than relying
on an ASCII-only `strings` scan. The Debug DLL contained both
`DTXMANIA_E2E_CRASH_INJECTION` and `DTXMANIA_E2E_CONTROLLED_CRASH`; the
Release DLL contained neither literal.

## Real DesktopGL callback results

Each command used a new `DTXMANIA_APPDATA_ROOT` and ran the previously built
Debug target:

```bash
rtk env DTXMANIA_APPDATA_ROOT="<fresh ephemeral root>" \
  DTXMANIA_E2E_CRASH_INJECTION=update \
  dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj \
  --configuration Debug --no-build

rtk env DTXMANIA_APPDATA_ROOT="<fresh ephemeral root>" \
  DTXMANIA_E2E_CRASH_INJECTION=draw \
  dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj \
  --configuration Debug --no-build
```

Both real DesktopGL processes exited with code `1` after initialization. The
observable console output reached graphics initialization before the injected
callback exception. Each root contained exactly one completed ZIP report and
no `.tmp` file:

| Callback | Exit code | Report | Captured UTC |
| --- | ---: | --- | --- |
| `Update` | 1 | `crash-20260803-191629Z-ef59cd.zip` | `2026-08-03T19:16:29.156976+00:00` |
| `Draw` | 1 | `crash-20260803-192036Z-832ca8.zip` | `2026-08-03T19:20:36.447106+00:00` |

The ZIP summaries reported the matching report IDs, build identifier
`1.0.0+dc50aad9c2cb6e748a74496b0399b726683cb6e4`, Darwin/ARM64 platform
metadata, `StartupActivation`, and `System.InvalidOperationException`. The
fixed controlled-crash marker appeared exactly once in each inspected exception
artifact.

Each observed ZIP contained exactly these five entries:

```text
report.json
exception.txt
logs.ndjson
breadcrumbs.json
README.txt
```

## Privacy inspection

The report files were unpacked only into ephemeral inspection directories.
No report contents were copied into this document. A content scan of the
Update report found no home path, username, app-data root, song/skin path, or
rendered startup-console message. The Draw run pre-seeded a non-production
`Config.ini` API-key sentinel; its report scan also found no home path,
username, app-data root, song/skin path, rendered startup-console message, or
the sentinel. Both scans reported `clean`.

The Draw artifact was additionally checked for the approved fixed marker
(count: one). Its `logs.ndjson` and `breadcrumbs.json` payloads had no
`renderedMessage` or `message` field. The observed log schema was limited to
the allowlisted record fields: timestamp, level, event ID/name, message
template, properties, and exception type.

## Local final gates and source/schema audit

Commands run:

```bash
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug --no-restore --logger "console;verbosity=normal"
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug --no-build \
  --filter "FullyQualifiedName~CrashReportStoreTests.Capture_SixMixedReports_ShouldRetainNewestFiveAcrossFormats|FullyQualifiedName~CrashReportIntegrationTests.CaptureFatal_WhenZipArchiveFails_ShouldDiscoverZipAndEmergencySummaries|FullyQualifiedName~CrashReportIntegrationTests.CreateBestEffort_WhenBootstrapDegrades_ShouldKeepConsoleLoggingAndRunnerFunctional|FullyQualifiedName~CrashReportIntegrationTests.CaptureFatal_WhenContextCollectionFails_ShouldPersistOnlySanitizedCachedData" \
  --logger "console;verbosity=normal"
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --configuration Debug --no-build \
  --filter "FullyQualifiedName~CrashReportStoreTests.Capture_ShouldWriteTemporaryFileInReportDirectoryBeforeFinalization|FullyQualifiedName~CrashReportStoreTests.Capture_EmergencyText_ShouldUseVersionedHeaderAndBeDiscoverable|FullyQualifiedName~CrashReportStoreTests.DiscoverCompletedReports_ShouldNeverExposeFreeFormEmergencyBody" \
  --logger "console;verbosity=normal"
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Release --no-restore
```

Results:

- Full Mac unit suite: 7,337 passed, 0 warnings.
- Mixed-format retention/privacy/console-fallback integration filter: 4 passed,
  0 warnings.
- Temporary-file/emergency-header/inbox boundary filter: 3 passed, 0 warnings.
- Final Mac Release build: passed, 0 errors, 0 warnings.

The local source audit confirmed that `BaseGame` and `Game1` accept injected
diagnostics only; neither has a parameterless constructor that creates crash
diagnostics. `CrashReportRuntime` is the only runtime owner that disposes the
shared logger factory, while `Program.cs` delegates to `GameEntryPoint.Run`
for explicit capture-before-game-dispose-before-runtime-dispose ordering.
There is no `using var game` and no `AppDomain.UnhandledException`,
`FirstChanceException`, or `TaskScheduler.UnobservedTaskException` handler.

`CrashContextPublisher` builds cached allowlisted snapshots; the fatal runtime
captures snapshots and buffers rather than querying graphics, devices,
SQLite, the network, or a dispatcher. The writer serializes its own sanitized
records rather than a `ConfigData` instance. `CrashReportStore` applies one
latest-five quota after discovering both `.zip` and `.txt` reports, and the
mixed-format test verifies ordering and eviction. Emergency report discovery
reads only its versioned allowlisted header, not the free-form body.

## Windows boundary

This is macOS/DesktopGL evidence only. No Windows process was launched from
this host. The Windows unit suite, native Windows Release build, and the
real-process `CrashReportingSmokeTests` update/draw cases remain pending the
existing Windows CI job, where `Category=E2E` selects the crash smoke tests.
