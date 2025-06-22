# DTXManiaCX Development Guidelines

## Project Overview
DTXManiaCX is a cross-platform port of DTXMania to .NET 8 using MonoGame. Follow these guidelines when working with the codebase.

**Important**: Refer to CLAUDE.md for comprehensive project documentation and architecture details.

## Build and Verification
Always rebuild and test after making changes:
```bash
# Restore and build
dotnet restore DTXMania.sln
dotnet build DTXMania.sln --configuration Debug --no-restore

# Run tests to verify changes
dotnet test DTXMania.Test/DTXMania.Test.csproj --configuration Debug --no-build --verbosity normal
```

## Core Architecture Guidelines

### Project Structure
- **DTXMania.Shared.Game**: Main game logic, shared across platforms
- **DTXMania.Windows/Mac**: Platform-specific implementations
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
- Shared logic goes in DTXMania.Shared.Game
- Platform-specific code in respective platform projects
- Use IFontFactory for platform-specific font implementations
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

## Error Handling
- Implement comprehensive error handling with fallback resources
- Handle resource load failures gracefully
- Provide detailed logging for troubleshooting
- Support graceful degradation when assets are missing

## Graphics and Input
- Use render targets for consistent resolution
- Support fullscreen toggle with Alt+Enter
- Implement proper input state tracking
- Handle device lost/reset scenarios

## Verification Steps
1. Build the solution without errors
2. Run unit tests to ensure no regressions
3. Test on target platforms when possible
4. Verify DTXMania compatibility features still work

You are not required to run the application - the user will handle that.