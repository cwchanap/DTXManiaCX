# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DTXManiaCX is a port of the original DTXMania to .NET 8 using MonoGame. The original DTXMania was written in C# and used the DirectX API. This port is being done using MonoGame for cross-platform compatibility.

**Note**: DTXManiaNX is the legacy codebase under the root project - avoid modifying it directly.

## Development Commands

### Build and Test
```bash
# Restore dependencies
dotnet restore DTXMania.sln

# Build solution (Debug)
dotnet build DTXMania.sln --configuration Debug --no-restore

# Build solution (Release)
dotnet build DTXMania.sln --configuration Release --no-restore

# Run tests
dotnet test DTXMania.Test/DTXMania.Test.csproj --configuration Debug --no-build --verbosity normal

# Run specific test class
dotnet test DTXMania.Test/DTXMania.Test.csproj --filter "ClassName=ConfigManagerTests"

# Run with coverage
dotnet test DTXMania.Test/DTXMania.Test.csproj --collect:"XPlat Code Coverage" --results-directory ./TestResults
```

### Platform-Specific Builds
```bash
# Windows executable
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Release

# Mac executable  
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Release

# Run Mac application
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj

# Run Windows application (on Windows)
dotnet run --project DTXMania.Game/DTXMania.Game.Windows.csproj

# Publish Windows (self-contained)
dotnet publish DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Release --output ./publish/windows --self-contained false

# Publish Mac (self-contained)
dotnet publish DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Release --output ./publish/mac --self-contained false
```

## Architecture Overview

### Core Structure
- **DTXMania.Game/**: Unified project directory containing all shared game logic
  - **DTXMania.Game.Windows.csproj**: Windows-specific build configuration (MonoGame.Framework.WindowsDX)
  - **DTXMania.Game.Mac.csproj**: Mac-specific build configuration (MonoGame.Framework.DesktopGL)
  - **Game1.cs**: Contains BaseGame (shared logic) and Game1 (platform entry point)
  - **Program.cs**: Application entry point
  - **Lib/**: All shared game libraries (Resources, Stage, UI, Config, etc.)
  - **Content/**: Shared content files with symbolic links from platform builds
- **DTXMania.Test**: Comprehensive unit test suite using xUnit

### Key Architectural Components

#### 1. Game Loop (`BaseGame` class)
- Entry point for MonoGame framework
- Manages core systems: Graphics, Config, Input, Stage Management
- Handles Alt+Enter fullscreen toggle
- Implements render target system for consistent resolution

#### 2. Stage Management System
- **StageManager**: Orchestrates stage transitions with fade effects
- **BaseStage**: Abstract base for all game stages
- **Stage Types**: Startup, Title, Config, SongSelect, UITest
- **Transition System**: Supports DTXMania-style fade transitions with easing curves
- **Phase Management**: Implements DTXManiaNX eフェーズID patterns (Inactive, FadeIn, Normal, FadeOut)

#### 3. Resource Management
- **ResourceManager**: Handles textures, fonts, sounds with caching and reference counting
- **Skin System**: DTXMania-compatible skin loading with box.def support  
- **Fallback Resources**: Automatic fallback for missing assets
- **Memory Management**: Reference counting with automatic cleanup

#### 4. Configuration System
- **ConfigManager**: Loads/saves Config.ini files
- **ConfigData**: Type-safe configuration with validation
- **IConfigItem**: Interface for config screen items (dropdowns, toggles, integers)

#### 5. Graphics Management
- **GraphicsManager**: Handles device settings, fullscreen toggle, device events
- **RenderTargetManager**: Manages render targets for consistent resolution
- **GraphicsSettings**: Resolution, fullscreen, and display configuration

#### 6. UI System
- **Component-based**: UILabel, UIButton, UIImage, UIPanel, UIList
- **DTXMania Patterns**: Follows On活性化/On進行描画 lifecycle
- **Layout System**: Automatic positioning with multiple layout modes
- **Input Handling**: Mouse and keyboard with proper state tracking

#### 7. Song Management
- **SongManager**: Handles song discovery and metadata parsing
- **DTXMetadataParser**: Parses DTX files for song information
- **SongListNode**: Tree structure for organizing songs

### Key Design Patterns

#### Stage Lifecycle
```csharp
// Each stage follows this pattern:
OnActivate() -> OnFirstUpdate() -> OnUpdate()/OnDraw() -> OnTransitionOut() -> OnDeactivate()
```

#### Resource Management
```csharp
// Always use ResourceManager for loading assets:
var texture = resourceManager.LoadTexture("Graphics/background.jpg");
// Resources are automatically reference counted and cached
```

#### Configuration Access
```csharp
// Access config through ConfigManager:
var config = configManager.Config;
config.ScreenWidth = 1920;
configManager.SaveConfig("Config.ini");
```

## Development Guidelines

### Code Conventions
- Use existing libraries and frameworks when possible (don't reinvent the wheel)
- Follow C# naming conventions and .NET 8 patterns
- Maintain DTXMania compatibility where applicable
- Use MonoGame framework for graphics, input, and audio

### Testing Requirements
- Write unit tests for all new functionality
- Use xUnit framework with Moq for mocking
- Maintain high test coverage
- Test both positive and negative scenarios

### DTXMania Compatibility
- Respect original DTXMania patterns and naming conventions
- Support DTXMania skin system and directory structure
- Maintain compatibility with existing DTX files and configurations
- Follow eフェーズID (phase ID) patterns from DTXManiaNX

### Platform Considerations
- All shared game logic is in the DTXMania.Game/ directory
- Platform-specific builds use different MonoGame frameworks (WindowsDX vs DesktopGL)
- SharedFontFactory provides cross-platform font support
- Test on both Windows and Mac when possible
- Use platform-specific .csproj files for building/running

### Resource Guidelines
- Place assets in appropriate directories (Graphics/, Fonts/, Sounds/)
- Use skin system for customizable assets
- Implement proper resource disposal
- Provide fallback resources for missing files

## Important Implementation Notes

### Graphics System
- Uses render targets for consistent resolution across different screen sizes
- Supports fullscreen toggle with Alt+Enter
- Handles device lost/reset scenarios gracefully
- Implements DTXMania-compatible graphics settings

### Input System
- Enhanced InputManager with state tracking
- Supports both keyboard and mouse input
- Proper key press detection (not just held state)
- Gamepad support for back button

### Font System
- SharedFontFactory provides cross-platform font support using MonoGame SpriteFont
- BitmapFont support for DTXMania-style text rendering
- Fallback fonts when specific fonts are unavailable
- Japanese font support for authentic DTXMania experience
- Font files centralized in DTXMania.Game/Content/ with symbolic links

### Error Handling
- Comprehensive error handling with fallback resources
- Resource load failure events for debugging
- Graceful degradation when assets are missing
- Detailed logging for troubleshooting

## Common File Locations

### Core Game Logic
- `DTXMania.Game/Game1.cs` - Contains BaseGame and Game1 classes
- `DTXMania.Game/Program.cs` - Application entry point
- `DTXMania.Game/Lib/Stage/` - Stage management system
- `DTXMania.Game/Lib/Resources/` - Resource management including SharedFontFactory
- `DTXMania.Game/Lib/Config/` - Configuration system

### UI Components
- `DTXMania.Game/Lib/UI/Components/` - UI components
- `DTXMania.Game/Lib/UI/` - UI core system

### Platform-Specific Project Files
- `DTXMania.Game/DTXMania.Game.Windows.csproj` - Windows build configuration
- `DTXMania.Game/DTXMania.Game.Mac.csproj` - Mac build configuration

### Content and Assets
- `DTXMania.Game/Content/` - Shared content files (fonts, textures, etc.)
- `DTXMania.Game/Content/NotoSerifJP.*` - Font files (linked from platform builds)

### Testing
- `DTXMania.Test/` - All unit tests organized by component
- `DTXMania.Test/Helpers/` - Test utilities and mocks

## Debugging and Troubleshooting

### Debug Output
The application outputs detailed debug information to help with troubleshooting:
- Graphics manager initialization and settings changes
- Stage transitions and lifecycle events
- Resource loading and caching statistics
- Configuration loading and validation

### Common Issues
- **Missing Assets**: Check skin directory structure and fallback resources
- **Font Loading**: Ensure platform-specific font factory is available
- **Graphics Issues**: Check device availability and render target validity
- **Stage Transitions**: Verify stage lifecycle methods are properly implemented