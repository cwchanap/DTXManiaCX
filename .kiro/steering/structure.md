# Project Structure

## Solution Organization
- **DTXMania.sln** - Main solution file
- **DTXMania.Game/** - Main game project with platform-specific builds
- **DTXMania.Test/** - Unit test project
- **System/** - Game assets (graphics, sounds, scripts)
- **docs/** - Technical documentation

## Main Game Project (`DTXMania.Game/`)
```
DTXMania.Game/
├── Lib/                    # Core game libraries organized by domain
│   ├── Config/            # Configuration management
│   ├── Graphics/          # Graphics and rendering
│   ├── Input/             # Input handling
│   ├── Resources/         # Resource management (textures, sounds, fonts)
│   ├── Song/              # Song data and DTX file parsing
│   ├── Stage/             # Stage system and transitions
│   ├── UI/                # User interface framework
│   └── Utilities/         # Common utilities
├── Content/               # MonoGame content pipeline assets
├── Game1.cs              # Main game class
├── Program.cs            # Entry point
└── *.csproj              # Platform-specific project files
```

## Architecture Patterns

### Namespace Convention
- **DTX.{Domain}** - Core library namespaces (e.g., `DTX.Config`, `DTX.Graphics`)
- **DTXMania.Game** - Main game namespace

### Interface-Driven Design
- All major systems use interfaces (IConfigManager, IGraphicsManager, IStageManager)
- Dependency injection pattern for manager classes
- Clear separation of concerns between domains

### Stage-Based Architecture
- Game flow managed through stage system
- Each stage represents a screen/mode (Title, Config, SongSelect, etc.)
- Smooth transitions between stages with fade effects
- Phase-based lifecycle management

### Resource Management
- Managed resource pattern for textures, sounds, and fonts
- Automatic disposal and cleanup
- Skin system for customizable graphics

## Test Project (`DTXMania.Test/`)
- Mirrors main project structure
- Domain-specific test folders
- Mock helpers for testing graphics-dependent code
- Performance test suite

## Asset Organization (`System/`)
- **Graphics/** - UI textures, backgrounds, sprites
- **Sounds/** - Audio files for UI and game sounds
- **Script/** - Game configuration scripts

## Documentation (`docs/`)
- Architecture documentation
- Implementation guides
- System design specifications

## Coding Conventions
- C# naming conventions (PascalCase for public members)
- Interface prefix with 'I'
- Manager suffix for service classes
- Comprehensive XML documentation for public APIs
- Event-driven architecture for system communication