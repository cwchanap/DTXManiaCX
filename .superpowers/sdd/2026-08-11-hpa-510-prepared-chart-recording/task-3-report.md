# Task 3 report — explicit Game API, JSON-RPC, Automation, and telemetry

## Implementation

Committed as `feat: expose prepared chart automation commands`.

Added the four `IGameApi` prepared-chart operations and `PreparedChartCommandResult`. `GameApiImplementation` now queues each operation to the game update thread, verifies that the current stage is `SongSelectionStage`, awaits the real stage result with `TaskCompletionSource` configured with `RunContinuationsAsynchronously`, and returns safe failure text for wrong-stage/queue failures.

Added explicit authenticated JSON-RPC routes for `prepareVideoChart`, `startPreparedPreview`, `activatePreparedChart`, and `cancelPreparedChart`. Prepare parameters require a non-blank fully qualified `chartPath`; domain failures remain `{ success, error }` results in the existing JSON-RPC envelope.

Added four `JsonRpcGameClient` wrappers over the existing private `SendAsync`, one command-result parser that throws `InvalidOperationException` with the server error text, and the three `GameStateSnapshot` telemetry accessors. Extended the producer/consumer camelCase round-trip contract with non-default prepared-chart values.

Changed files:

- `DTXMania.Game/Lib/GameApi.cs`
- `DTXMania.Game/Lib/GameApiImplementation.cs`
- `DTXMania.Game/Lib/JsonRpc/JsonRpcServer.cs`
- `DTXMania.Automation/JsonRpc/JsonRpcGameClient.cs`
- `DTXMania.Automation/Telemetry/GameStateSnapshot.cs`
- `DTXMania.Test/GameApi/GameApiPreparedChartCommandTests.cs`
- `DTXMania.Test/JsonRpc/JsonRpcServerInternalTests.cs`
- `DTXMania.Test/JsonRpc/JsonRpcServerValidationTests.cs`
- `DTXMania.Automation.Tests/JsonRpc/JsonRpcGameClientTests.cs`
- `DTXMania.E2E/AutomationContractTests.cs`

## TDD evidence

RED was observed before implementation:

```text
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~GameApiPreparedChartCommandTests" --no-restore
```

The build failed with the expected missing-contract errors (`GameApiImplementation` had no `Prepare/Start/Activate/Cancel...Async`, `IGameApi` had no corresponding methods, and `PreparedChartCommandResult` was undefined); 0 tests ran.

GREEN focused runs:

```text
rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~GameApiPreparedChartCommandTests" --no-restore
  4 tests passed (existing project warnings only)

rtk dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj \
  --filter "FullyQualifiedName~JsonRpcServerValidationTests.PreparedChart|FullyQualifiedName~JsonRpcServerInternalTests.HandlePrepareVideoChart|FullyQualifiedName~JsonRpcServerInternalTests.RouteMethodCall_PreparedChartCommands" --no-restore
  11 tests passed (existing project warnings only)

rtk dotnet test DTXMania.Automation.Tests/DTXMania.Automation.Tests.csproj --no-restore
  64 tests passed, 0 warnings

rtk dotnet test DTXMania.E2E/DTXMania.E2E.csproj \
  --filter "FullyQualifiedName~AutomationContractTests.GameTelemetrySnapshot_CamelCaseRoundTrip" --no-restore
  1 test passed, 0 warnings
```

The final full Mac-safe suite passed after all test additions: `8,203 tests passed, 539 existing warnings` (no new warning or error was reported for this change).

## Self-review

- Update-thread ownership is preserved: all four commands mutate only inside the queued action, and the returned task remains incomplete until that action runs.
- Wrong-stage, stage-domain, and debounce failures return `Success=false` with safe text; the actual stage result is not replaced by queued-success.
- The existing JSON-RPC authentication and envelope remain in force. No generic dispatcher, session abstraction, error-code enum, or unrelated route was added.
- RPC method names, `chartPath`, `success`, `error`, and the three telemetry camelCase names are covered by focused tests and the producer/consumer round trip.
- Telemetry identity remains the stage-provided non-absolute chart identity; command failures do not echo chart paths.

## Concerns

Unexpected exceptions in queueing or stage execution resolve to the deliberately generic safe message `The prepared chart command could not be completed.`; no structured error taxonomy was introduced because this ticket has no branching caller.
