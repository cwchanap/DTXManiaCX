# DTXmaniaCX Project Context

This document provides Gemini with a summary of the DTXmaniaCX project to ensure its actions are informed and consistent with the project's standards.

## Project Overview

DTXmaniaCX is a port of the original DTXMania to .NET 8 using MonoGame. The original DTXMania was written in C# and used the DirectX API. This port is being done using MonoGame for cross-platform compatibility.

**Note**: DTXManiaNX is the legacy codebase under the root project - avoid modifying it directly.

## Tech Stack

- **Language:** C#
- **Framework:** .NET 8
- **Game Engine:** MonoGame
- **Build System:** MSBuild (via `dotnet` CLI)
- **Test Framework:** xUnit with Moq

## Key Directories

- `DTXMania.Game/`: Unified project directory containing all shared game logic
- `DTXMania.Game/Lib/`: All shared game libraries (Resources, Stage, UI, Config, etc.)
- `DTXMania.Test/`: Comprehensive unit test suite using xUnit
- `docs/`: Includes important markdown documents detailing the architecture and implementation of various game systems.
- `DTXMania.Game/Content/`: Shared content files with symbolic links from platform builds

## Common Commands

### Build and Test on Mac
```bash
# Restore dependencies
dotnet restore DTXMania.Game/DTXMania.Game.Mac.csproj

# Build solution
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --no-restore

# Run tests
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --no-build --verbosity normal

# Run specific test class
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerTests"

# Run with coverage
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --collect:"XPlat Code Coverage" --results-directory ./TestResults
```

### Platform-Specific Builds
```bash
# Windows executable
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Release

# Mac executable  
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Release

# Run Mac application
dotnet launch --project DTXMania.Game/DTXMania.Game.Mac.csproj

# Run Windows application (on Windows)
dotnet launch --project DTXMania.Game/DTXMania.Game.Windows.csproj

# Publish Windows (self-contained)
dotnet publish DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Release --output ./publish/windows --self-contained false

# Publish Mac (self-contained)
dotnet publish DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Release --output ./publish/mac --self-contained false
```

## Development Notes

- **Coding Style:** The project uses an `.editorconfig` file to enforce coding conventions. Please adhere to these styles when modifying code.
- **Architecture:** Before making significant changes, consult the design documents in the `docs/` directory to understand the existing architecture and patterns.
- **Cross-Platform:** The project has separate `.csproj` files for Windows and Mac (`DTXMania.Game.Windows.csproj`, `DTXMania.Game.Mac.csproj`), indicating a need to consider cross-platform compatibility.
- **Centralize Configuration**: Store layout constants, positions, sizes, and colors in dedicated UI layout classes
- **Use TexturePath Constants**: Reference texture files through TexturePath class instead of hardcoded strings
- **Sprite-based Rendering**: Use ManagedSpriteTexture for spritesheet-based graphics like difficulty labels and level numbers
- **Modular Components**: Use component-based architecture for stage elements (Performance stage components as examples)