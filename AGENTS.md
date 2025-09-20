# Repository Guidelines

## Project Structure & Module Organization
- `DTXMania.Game/` hosts the MonoGame runtime (base game loop, content pipeline, config loaders) with platform-specific csproj files (`DTXMania.Game.Mac.csproj`, `DTXMania.Game.Windows.csproj`), plus `Content/` for compiled assets and `Lib/` for engine subsystems.
- `DTXMania.Test/` mirrors the runtime namespaces with unit tests under domain folders (e.g., `Resources/`, `Stage/`) and reuses fixtures from `TestData/`.
- `System/` and root `Config.ini` provide skin, script, and sound resources used at runtime; keep large assets out of source control when possible.
- `docs/` contains architecture notes and should be updated when systems change; `MCP/` houses the Procyon bridge and server tooling for AI-assisted features.

## Build, Test, and Development Commands
- `dotnet restore DTXMania.sln` restores shared dependencies (MonoGame, xUnit, coverlet).
- `dotnet build DTXMania.sln -c Debug` compiles all projects; use `-c Release` for publish-ready binaries.
- `dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj` runs the game on desktop GL (swap to the Windows project on that platform).
- `dotnet test DTXMania.Test/DTXMania.Test.csproj --collect:"XPlat Code Coverage"` executes the xUnit suite and emits coverlet coverage data in `TestResults/`.

## Coding Style & Naming Conventions
- Follow `.editorconfig`: 4-space indentation, spaces only, CRLF endings, UTF-8 without BOM. Keep trailing whitespace only when intentional.
- Keep namespaces aligned with directory layout (`DTXMania.Game.Lib.*`, `DTXMania.Test.Resources`), and prefer PascalCase for types, camelCase for parameters, `_camelCase` for private fields, matching existing patterns in `Game1.cs`.
- Favor explicit method summaries for complex systems, and use `var` where the inferred type is obvious in tests and glue code.

## Testing Guidelines
- Tests use xUnit facts/theories; name methods with `Scenario_ShouldExpect` to match existing suites (`ManagedTextureTests.cs`).
- Stage integration or asset-dependent tests belong under matching folders; place reusable fixtures in `TestData/`.
- Maintain coverage signals: extend or update assertions when modifying resource or stage managers, and ensure new behaviors are covered before opening a PR.

## Commit & Pull Request Guidelines
- History follows Conventional Commits (`feat:`, `refactor:`, `fix:`); keep subjects under 72 characters and write imperative summaries.
- For PRs, describe the gameplay or tooling impact, call out updated docs/config, link tracking issues, and attach screenshots or clips when UI/visual behavior changes.
- Confirm builds and tests locally (`dotnet build`, `dotnet test`) and note any skipped checks; include MCP integration steps if changes touch `MCP/`.