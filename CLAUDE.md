# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DTXManiaCX is a port of DTXMania to .NET 8 using MonoGame for cross-platform compatibility (Windows/Mac). The original DTXMania used DirectX.

- **Framework**: .NET 8.0, MonoGame 3.8.*
- **Windows**: MonoGame.Framework.WindowsDX
- **Mac**: MonoGame.Framework.DesktopGL

**Note**: DTXManiaNX (root directory) is the legacy codebase - avoid modifying it directly.

## Development Commands

### Build and Test (Mac)
```bash
# Build
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj

# Run tests
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj

# Run specific test class
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerTests"

# Run tests by category (Performance, Stage, Resources, etc.)
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "TestCategory=Performance"

# Run with coverage
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --collect:"XPlat Code Coverage" --results-directory ./TestResults

# Run application
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```

### Build and Test (Windows)
```bash
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet run --project DTXMania.Game/DTXMania.Game.Windows.csproj
```

### MCP Server
```bash
# Build and run MCP server
dotnet build MCP/MCP.csproj
dotnet run --project MCP/MCP.csproj

# Test mode (without MCP client)
dotnet run --project MCP/MCP.csproj -- --test
```

**Note**: On macOS, tests may crash with SDL threading errors due to MonoGame graphics initialization. This is expected - graphics-related tests are excluded from the Mac test project.

## Architecture Overview

### Project Structure
- **DTXMania.Game/**: Unified game project with platform-specific .csproj files
  - `Game1.cs`: Contains BaseGame (shared logic) and Game1 (platform entry point)
  - `Lib/`: All shared libraries (Resources, Stage, UI, Config, Input, Song, Graphics)
  - `Content/`: Shared content files (fonts, textures)
- **DTXMania.Test/**: xUnit test suite with Moq
- **MCP/**: Model Context Protocol server for AI copilot integration
  - `Server/`: JSON-RPC client and game interaction services
  - `Tools/`: MCP tool definitions (game_click, game_get_state, game_send_key, etc.)
  - `Bridge/`: MonoGame-facing components

### Core Systems

**Stage Management**: Orchestrates game flow with fade transitions
- Stage lifecycle: `OnActivate() -> OnFirstUpdate() -> OnUpdate()/OnDraw() -> OnTransitionOut() -> OnDeactivate()`
- Stage types: Startup, Title, Config, SongSelect, SongTransition, Performance, Result
- Phase management follows DTXManiaNX eフェーズID patterns (Inactive, FadeIn, Normal, FadeOut)

**Resource Management**: Reference-counted caching system
```csharp
var texture = resourceManager.LoadTexture("Graphics/background.jpg");
var spriteTexture = new ManagedSpriteTexture(graphicsDevice, baseTexture.Texture, sourcePath, spriteWidth, spriteHeight);
```
- Use TexturePath constants for texture file paths
- Audio support: WAV (native), MP3 (FFMpegCore), OGG/Vorbis (NVorbis)

**Configuration**: INI-based config system
```csharp
var config = configManager.Config;
configManager.SaveConfig("Config.ini");
```

**UI System**: Component-based with layout classes
- Components: UILabel, UIButton, UIImage, UIPanel, UIList
- Layout constants in dedicated classes: SongSelectionUILayout, PerformanceUILayout, etc.
- Input handling via ModularInputManager

**MCP Integration**: JSON-RPC bridge for AI copilots
- GameApiServer exposes endpoint at `http://localhost:8080/jsonrpc`
- Environment variables: `DTXMANIA_API_URL`, `DTXMANIA_API_KEY`

## Development Guidelines

### Code Conventions
- Use existing libraries - don't reinvent the wheel
- Follow `.editorconfig`: 4-space indentation, spaces only, CRLF endings, UTF-8
- Naming: PascalCase for types, camelCase for parameters, `_camelCase` for private fields
- Centralize layout constants in UI layout classes
- Use TexturePath constants instead of hardcoded texture strings

### DTXMania Compatibility
- Respect original DTXMania patterns and naming conventions
- Support DTXMania skin system and directory structure
- Maintain compatibility with DTX files and configurations
- Follow eフェーズID (phase ID) patterns from DTXManiaNX

### Testing
- Use xUnit with Moq for mocking
- Name methods: `Scenario_ShouldExpect`
- Test both positive and negative scenarios
- Place reusable fixtures in `DTXMania.Test/TestData/`

### Commit Guidelines
- Follow Conventional Commits: `feat:`, `fix:`, `refactor:`
- Keep subjects under 72 characters, imperative mood
- Describe gameplay/tooling impact in PR descriptions

## Key File Locations

### Core
- `DTXMania.Game/Game1.cs` - BaseGame and Game1 classes
- `DTXMania.Game/Lib/Stage/StageManager.cs` - Stage transitions
- `DTXMania.Game/Lib/Resources/ResourceManager.cs` - Resource caching
- `DTXMania.Game/Lib/Resources/TexturePath.cs` - Texture path constants
- `DTXMania.Game/Lib/Config/ConfigManager.cs` - Configuration system

### Stage Components
- `DTXMania.Game/Lib/Stage/Performance/` - NoteRenderer, ScoreDisplay, GaugeManager, JudgementManager, etc.

### MCP
- `DTXMania.Game/Lib/GameApiServer.cs` - HTTP/JSON-RPC server
- `MCP/Tools/GameInteractionTools.cs` - MCP tool definitions
- `MCP/Server/GameInteractionService.cs` - Game communication

## Troubleshooting

- **Missing Assets**: Check skin directory structure and fallback resources
- **MCP Connection**: Ensure GameApiServer is running on port 8080 before starting MCP server
- **macOS Test Crashes**: SDL threading errors are expected; use `--filter` to run specific test categories
