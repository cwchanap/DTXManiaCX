# Technology Stack

## Framework & Runtime
- **.NET 8** - Target framework for all projects
- **MonoGame 3.8** - Cross-platform game development framework
  - WindowsDX backend for Windows
  - DesktopGL backend for macOS

## Key Dependencies
- **Entity Framework Core 9.0.6** with SQLite provider for data persistence
- **FFMpegCore 5.2.0** for media processing
- **NVorbis 0.10.5** for OGG Vorbis audio support
- **System.Drawing.Common** for image processing
- **MMTools.Executables** for platform-specific multimedia tools

## Testing Framework
- **xUnit 2.9.2** for unit testing
- **Moq 4.20.70** for mocking
- **Coverlet** for code coverage

## Build Tools
- **MonoGame Content Builder (MGCB)** for asset pipeline
- **dotnet CLI** for build and package management

## Common Commands

### Building
```bash
# Restore tools and dependencies
dotnet tool restore
dotnet restore

# Build all projects
dotnet build

# Build specific platform
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
```

### Testing
```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Content Pipeline
```bash
# Build game content
mgcb DTXMania.Game/Content/Content.mgcb
```

### Publishing
```bash
# Publish for Windows
dotnet publish DTXMania.Game/DTXMania.Game.Windows.csproj -c Release

# Publish for macOS
dotnet publish DTXMania.Game/DTXMania.Game.Mac.csproj -c Release
```

## Development Environment
- Cross-platform development supported
- Visual Studio, VS Code, or JetBrains Rider recommended
- MonoGame Content Builder Editor for asset management