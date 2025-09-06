# Procyon - MCP Bridge for MonoGame

Procyon is a Model Context Protocol (MCP) integration solution for MonoGame applications, split into two packages:

## 📦 Packages

### 1. Procyon.McpBridge
A library package that provides MCP integration for MonoGame applications.

**Features:**
- Easy integration with MonoGame projects
- Game state synchronization with MCP servers
- AI assistance requests from within games
- Built-in MonoGame component for seamless integration

**Installation:**
```bash
dotnet add package Procyon.McpBridge
```

### 2. Procyon.McpServer
A console application that acts as an MCP server for processing game data.

**Features:**
- Hosted service architecture
- Game state management
- Extensible processing pipeline
- Logging and monitoring

## 🚀 Quick Start

### Using the MCP Bridge in MonoGame

```csharp
public class MyGame : Game
{
    private McpBridgeComponent _mcpBridge;
    
    protected override void LoadContent()
    {
        // Add MCP bridge component to your game
        _mcpBridge = new McpBridgeComponent(this);
        Components.Add(_mcpBridge);
    }
    
    protected override void Update(GameTime gameTime)
    {
        // Your game logic here
        base.Update(gameTime);
    }
}
```

### Running the MCP Server

```bash
cd src/Procyon.McpServer
dotnet run
```

## 🛠️ Development

### Building the Projects

```bash
# Build the entire solution
dotnet build

# Build individual projects
dotnet build src/Procyon.McpBridge/Procyon.McpBridge.csproj
dotnet build src/Procyon.McpServer/Procyon.McpServer.csproj
```

### Creating Packages

```bash
# Create NuGet packages
dotnet pack src/Procyon.McpBridge/Procyon.McpBridge.csproj -c Release
dotnet pack src/Procyon.McpServer/Procyon.McpServer.csproj -c Release
```

## 📋 Requirements

- .NET 9.0 or later
- MonoGame Framework (for the bridge library)
- Model Context Protocol package

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.
