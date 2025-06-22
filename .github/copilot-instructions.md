# DTXManiaCX Development Guidelines

## Project Overview
DTXManiaCX is a cross-platform port of DTXMania to .NET 8 using MonoGame. The original DTXMania used DirectX, while this port uses MonoGame for cross-platform compatibility (Windows and Mac).

**Important**: Refer to CLAUDE.md for comprehensive project documentation and architecture details.

**Note**: DTXManiaNX is the legacy codebase - avoid modifying it directly.

## Build and Verification
Always rebuild and test after making changes:
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

# Platform-specific builds
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Release
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Release

# Run applications
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet run --project DTXMania.Game/DTXMania.Game.Windows.csproj
```

## Core Architecture Guidelines

### Project Structure
- **DTXMania.Game/**: Unified project directory containing all shared game logic
  - **DTXMania.Game.Windows.csproj**: Windows-specific build configuration (MonoGame.Framework.WindowsDX)
  - **DTXMania.Game.Mac.csproj**: Mac-specific build configuration (MonoGame.Framework.DesktopGL)
  - **Game1.cs**: Contains BaseGame (shared logic) and Game1 (platform entry point)
  - **Program.cs**: Application entry point
  - **Lib/**: All shared game libraries (Resources, Stage, UI, Config, etc.)
  - **Content/**: Shared content files with symbolic links from platform builds
- **DTXMania.Test**: Unit tests using xUnit framework
- **Avoid modifying DTXManiaNX** (legacy codebase)

### Key Systems to Follow

#### Stage Management
- Use `BaseStage` for all game stages
- Follow lifecycle: `OnActivate() -> OnFirstUpdate() -> OnUpdate()/OnDraw() -> OnTransitionOut() -> OnDeactivate()`
- Use StageManager for transitions with fade effects
- Implement proper phase management (Inactive, FadeIn, Normal, FadeOut)

#### Resource Management
- Always use `ResourceManager` for loading assets
- Resources are automatically cached and reference counted
- Provide fallback resources for missing files
- Place assets in appropriate directories (Graphics/, Fonts/, Sounds/)

#### Configuration System
- Use `ConfigManager` for all configuration access
- Follow type-safe `ConfigData` patterns
- Support Config.ini file format for DTXMania compatibility

#### UI Development
- Use component-based UI system (UILabel, UIButton, UIImage, UIPanel, UIList)
- Follow DTXMania On活性化/On進行描画 lifecycle patterns
- Implement proper input handling with state tracking

#### Graphics Management
- Use render targets for consistent resolution across different screen sizes
- Support fullscreen toggle with Alt+Enter
- Handle device lost/reset scenarios gracefully
- Implement DTXMania-compatible graphics settings

#### Font System
- Use SharedFontFactory for cross-platform font support
- Support both MonoGame SpriteFont and BitmapFont for DTXMania compatibility
- Provide fallback fonts when specific fonts are unavailable
- Japanese font support for authentic DTXMania experience

## Development Best Practices

### Code Conventions
- Follow C# naming conventions and .NET 8 patterns
- Maintain DTXMania compatibility where applicable
- Use existing libraries and frameworks (don't reinvent the wheel)
- Use MonoGame framework for graphics, input, and audio

### Testing Requirements
- Write unit tests for all new functionality using xUnit
- Use Moq for mocking dependencies
- Test both positive and negative scenarios
- Maintain high test coverage

### DTXMania Compatibility
- Respect original DTXMania patterns and naming conventions
- Support DTXMania skin system and directory structure
- Maintain compatibility with DTX files and configurations
- Follow eフェーズID patterns from DTXManiaNX

### Platform Considerations
- Shared logic goes in DTXMania.Game/ directory
- Platform-specific builds use different MonoGame frameworks (WindowsDX vs DesktopGL)
- Use SharedFontFactory for platform-specific font implementations
- Test on both Windows and Mac when possible

## Common Implementation Patterns

### Resource Loading
```csharp
// Always use ResourceManager
var texture = resourceManager.LoadTexture("Graphics/background.jpg");
var font = resourceManager.LoadFont("Fonts/arial.ttf", 16);
```

### Configuration Access
```csharp
// Access config through ConfigManager
var config = configManager.Config;
config.ScreenWidth = 1920;
configManager.SaveConfig("Config.ini");
```

### Stage Implementation
```csharp
// Extend BaseStage for new stages
public class MyStage : BaseStage
{
    public override void OnActivate() { /* Initialize stage */ }
    public override void OnUpdate() { /* Update logic */ }
    public override void OnDraw() { /* Render logic */ }
}
```

## Verification Steps
1. Build the solution without errors
2. Run unit tests to ensure no regressions
3. Test on target platforms when possible
4. Verify DTXMania compatibility features still work

You are not required to run the application - the user will handle that.