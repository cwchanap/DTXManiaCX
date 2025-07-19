# DTXmaniaCX Project Context

This document provides Gemini with a summary of the DTXmaniaCX project to ensure its actions are informed and consistent with the project's standards.

## Project Overview

DTXmaniaCX is a rhythm game, likely a custom implementation or fork of the popular DTXMania simulator. It is built using C# on the .NET platform, with MonoGame for its graphics and game loop framework. The project is structured into a main game application (`DTXMania.Game`) and a corresponding test project (`DTXMania.Test`).

## Tech Stack

- **Language:** C#
- **Framework:** .NET
- **Game Engine:** MonoGame
- **Build System:** MSBuild (via `dotnet` CLI)

## Key Directories

- `DTXMania.Game/`: Contains the source code for the main game application.
- `DTXMania.Game/Lib/`: Houses the core logic, including graphics, resource management, song handling, and UI systems.
- `DTXMania.Test/`: Contains all unit, integration, and performance tests for the project.
- `docs/`: Includes important markdown documents detailing the architecture and implementation of various game systems.
- `DTXMania.Game/Content/`: Game assets and the MonoGame Content Builder (`.mgcb`) file.

## Common Commands

When asked to build, run, or test the project, use the following commands:

- **Build Project:**
  ```bash
  dotnet build DTXMania.sln
  ```
- **Run Tests:**
  ```bash
  dotnet test DTXMania.sln
  ```

## Development Notes

- **Coding Style:** The project uses an `.editorconfig` file to enforce coding conventions. Please adhere to these styles when modifying code.
- **Architecture:** Before making significant changes, consult the design documents in the `docs/` directory to understand the existing architecture and patterns.
- **Cross-Platform:** The project has separate `.csproj` files for Windows and Mac (`DTXMania.Game.Windows.csproj`, `DTXMania.Game.Mac.csproj`), indicating a need to consider cross-platform compatibility.
