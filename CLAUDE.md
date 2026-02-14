# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DTXManiaCX is a port of DTXMania (a drum simulation game) to .NET 8 using MonoGame for cross-platform compatibility (Windows/Mac). The original DTXMania used DirectX.

- **Framework**: .NET 8.0, MonoGame 3.8.*
- **Windows**: MonoGame.Framework.WindowsDX
- **Mac**: MonoGame.Framework.DesktopGL

**Note**: DTXManiaNX (root directory) is the legacy codebase - avoid modifying it directly. Reference it for understanding original DTXMania patterns (eフェーズID, channel mappings, etc.).

## Development Commands

### Build and Test (Mac)
```bash
# Build
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj

# Run tests (Mac-specific project excludes graphics-dependent tests)
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj

# Run specific test class
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~ConfigManagerTests"

# Run tests by xUnit trait (currently only "Audio" category exists)
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "Category=Audio"

# Run with coverage
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --collect:"XPlat Code Coverage" --results-directory ./TestResults

# Run application
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```

### Build and Test (Windows)
```bash
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet test DTXMania.Test/DTXMania.Test.csproj
dotnet run --project DTXMania.Game/DTXMania.Game.Windows.csproj
```

### MCP Server
```bash
dotnet build MCP/MCP.csproj
dotnet run --project MCP/MCP.csproj
dotnet run --project MCP/MCP.csproj -- --test  # Test mode without MCP client
```

### Test Projects
- `DTXMania.Test.csproj` - Full test suite (Windows CI, references all tests)
- `DTXMania.Test.Mac.csproj` - Mac-safe subset with `EnableDefaultCompileItems=false` and explicit `Compile Include` that excludes: Graphics/\*\*, BitmapFontTests, StressTestRunner, QA tests, SongBarRendererTests, SongStatusPanelTests, JudgementTextPopupTests, ResourceManagerTests, and helpers requiring GraphicsDevice. Defines `MAC_BUILD` constant.

## Architecture Overview

### Project Structure
- **DTXMania.Game/** - Unified game project with platform-specific .csproj files
  - `Game1.cs` - BaseGame (shared logic) and Game1 (platform entry point)
  - `Lib/` - All shared libraries organized by subsystem
  - `Content/` - Shared content files (fonts, textures)
- **DTXMania.Test/** - xUnit test suite with Moq
- **MCP/** - Model Context Protocol server for AI copilot integration
- **DTXManiaNX/** - Legacy codebase (reference only)

### Lib Namespace Organization
```text
DTXMania.Game.Lib           - GameApiServer, GameApiImplementation, IGameApi, IGameContext
DTXMania.Game.Lib.Config    - ConfigManager, ConfigData, GameConstants
DTXMania.Game.Lib.Graphics  - GraphicsManager, GraphicsSettings, RenderTargetManager
DTXMania.Game.Lib.Input     - InputManagerCompat, ModularInputManager, InputRouter
DTXMania.Game.Lib.JsonRpc   - JsonRpcServer, JsonRpcMessage
DTXMania.Game.Lib.Resources - ResourceManager, ManagedTexture/Sound/Font, SkinManager, AudioLoader, TexturePath
DTXMania.Game.Lib.Song      - SongManager, DTXChartParser, ChartManager
DTXMania.Game.Lib.Song.Entities - Song, SongScore, PerformanceHistory
DTXMania.Game.Lib.Stage     - BaseStage, StageManager, 7 stage types, 6 transition types
DTXMania.Game.Lib.Stage.Performance - 18 specialized performance components
DTXMania.Game.Lib.UI        - UIManager, UIElement, UIContainer, DTXManiaVisualTheme
DTXMania.Game.Lib.UI.Components - UILabel, UIButton, UIImage, UIPanel, UIList
DTXMania.Game.Lib.UI.Layout - SongSelectionUILayout, PerformanceUILayout, ResultUILayout
DTXMania.Game.Lib.Utilities - AppPaths, CacheManager, PathValidator
```

### Core Systems

**Stage Management** (`Lib/Stage/`): Orchestrates game flow
- Stage lifecycle: `OnActivate() -> OnFirstUpdate() -> OnUpdate()/OnDraw() -> OnTransitionOut() -> OnDeactivate()`
- 7 stages: Startup, Title, Config, SongSelect, SongTransition, Performance, Result
- 6 transition types: Fade, DTXManiaFade, Crossfade, Instant, StartupToTitle, generic
- Phase management follows DTXManiaNX eフェーズID patterns (Inactive, FadeIn, Normal, FadeOut)
- BaseStage is the abstract base class; all stages implement IStage

**Performance Stage** (`Lib/Stage/Performance/`): The most complex stage, composed of 18 specialized components:
- AudioLoader, BackgroundRenderer, ComboDisplay, ComboManager, EffectsManager
- GaugeDisplay, GaugeManager, JudgementLineRenderer, JudgementManager, JudgementTextPopup
- LaneBackgroundRenderer, NoteRenderer, PadRenderer, PerformanceSummary
- PooledEffectsManager, ScoreDisplay, ScoreManager, SongTimer
- Event-driven: JudgementManager raises events consumed by ScoreManager, ComboManager, GaugeManager

**Resource Management** (`Lib/Resources/`): Reference-counted caching with statistics tracking
- Interfaces: ITexture, ISound, IFont with managed wrappers (ManagedTexture, ManagedSpriteTexture, ManagedSound, ManagedFont, BitmapFont)
- TexturePath constants for all texture file paths (use these, not hardcoded strings)
- SkinManager + SkinDiscoveryService: skin fallback chain (current skin -> fallback -> system)
- AudioLoader: WAV (native), MP3 (FFMpegCore), OGG/Vorbis (NVorbis)

**Song System** (`Lib/Song/`): DTX file parsing and song database
- SongManager (Singleton): Central song database management
- DTXChartParser: Parses .dtx, .gda, .g2d, .bms, .bme, .bml files
- 10 NX drum lane mapping: LC, HH, LP, SN, HT, BD, LT, FT, CY, RD
- SQLite integration via Microsoft.EntityFrameworkCore.Sqlite
- SET.def parsing with robust regex handling

**Configuration** (`Lib/Config/`): INI-based config system
- ConfigManager loads/saves Config.ini with auto-generation
- GameConstants for game-wide constants
- Auto-generates secure API key for MCP communication

**Graphics** (`Lib/Graphics/`): Resolution and render target management
- GraphicsManager: resolution, fullscreen toggle, render targets
- Events: SettingsChanged, DeviceLost, DeviceReset

**Input** (`Lib/Input/`): Layered input architecture
- InputManagerCompat wraps legacy InputManager, delegates to ModularInputManager
- InputRouter distributes input; supports lane-hit events for gameplay

**UI System** (`Lib/UI/`): Component-based with layout classes
- Components: UILabel, UIButton, UIImage, UIPanel, UIList
- Layout constants centralized in dedicated classes (SongSelectionUILayout, PerformanceUILayout, etc.)
- DTXManiaVisualTheme for consistent styling

**MCP Integration**: JSON-RPC bridge for AI copilots
- GameApiServer (`Lib/GameApiServer.cs`) exposes HTTP endpoint (port configurable via Config.ini `GameApiPort`)
- GameApiImplementation implements IGameContext providing access to all managers
- MCP server uses ModelContextProtocol SDK with stdio transport
- Tools: game_click, game_drag, game_get_state, game_send_key
- Environment variables: `DTXMANIA_API_URL`, `DTXMANIA_API_KEY`

### Key Interfaces
- `IGameContext` - Game state access (StageManager, ConfigManager, InputManager, GraphicsManager, ResourceManager)
- `IStageManager` / `IStage` / `IStageTransition` - Stage lifecycle and transitions
- `IResourceManager` - Resource loading and caching
- `IConfigManager` - Configuration access
- `IGraphicsManager` - Graphics and render targets
- `IInputManagerCompat` / `IInputManager` / `IInputSource` - Input handling

### Design Patterns
- **Singleton**: SongManager
- **Factory**: ResourceManagerFactory, ManagedFont
- **Strategy**: Transition types (FadeTransition, CrossfadeTransition, etc.)
- **Observer**: Stage lifecycle events, Graphics device events, lane hit events
- **Component**: PerformanceStage composition of 18 subsystems
- **Reference Counting**: Resource management (AddReference/RemoveReference)

## Development Guidelines

### Code Conventions
- Follow `.editorconfig`: 4-space indentation, spaces only, LF endings, UTF-8
- Naming: PascalCase for types, camelCase for parameters, `_camelCase` for private fields
- Centralize layout constants in UI layout classes
- Use TexturePath constants instead of hardcoded texture strings
- Use existing libraries - don't reinvent the wheel

### DTXMania Compatibility
- Respect original DTXMania patterns and naming conventions
- Support DTXMania skin system and directory structure
- Maintain compatibility with DTX files and configurations
- Follow eフェーズID (phase ID) patterns from DTXManiaNX

### Testing
- Use xUnit with Moq for mocking
- Name methods: `Scenario_ShouldExpect`
- Use `[Trait("Category", "...")]` for test categorization (xUnit traits)
- Place reusable fixtures in `DTXMania.Test/TestData/`

### Commit Guidelines
- Follow Conventional Commits: `feat:`, `fix:`, `refactor:`
- Keep subjects under 72 characters, imperative mood

## Key File Locations

- `DTXMania.Game/Game1.cs` - BaseGame and Game1 classes
- `DTXMania.Game/Lib/Stage/StageManager.cs` - Stage transitions
- `DTXMania.Game/Lib/Resources/ResourceManager.cs` - Resource caching
- `DTXMania.Game/Lib/Resources/TexturePath.cs` - Texture path constants
- `DTXMania.Game/Lib/Config/ConfigManager.cs` - Configuration system
- `DTXMania.Game/Lib/Song/SongManager.cs` - Song database (singleton)
- `DTXMania.Game/Lib/Song/DTXChartParser.cs` - DTX file parser
- `DTXMania.Game/Lib/Stage/Performance/` - All 18 performance components
- `DTXMania.Game/Lib/GameApiServer.cs` - HTTP/JSON-RPC server for MCP
- `MCP/Tools/GameInteractionTools.cs` - MCP tool definitions
- `MCP/Server/GameInteractionService.cs` - MCP game communication

## CI

GitHub Actions (`.github/workflows/build-and-test.yml`):
- **Windows job**: Builds Windows project, runs full test suite (`DTXMania.Test.csproj`) with `ALSOFT_DRIVERS=null` for audio driver workaround, Coverlet MSBuild coverage
- **macOS job**: Builds Mac project, runs Mac test suite (`DTXMania.Test.Mac.csproj`) with XPlat Code Coverage
- **Artifacts job**: Manual dispatch only, builds release artifacts for both platforms

## Troubleshooting

- **Missing Assets**: Check skin directory structure and SkinManager fallback chain
- **MCP Connection**: Ensure GameApiServer is running before starting MCP server; check `GameApiPort` in Config.ini
- **macOS Test Crashes**: SDL threading errors are expected for graphics tests; the Mac .csproj excludes them. If adding new tests that need GraphicsDevice, exclude them from `DTXMania.Test.Mac.csproj`
