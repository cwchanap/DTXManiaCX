# DTXManiaCX MCP

The MCP project hosts the Model Context Protocol (MCP) bridge that lets AI copilots talk to a running DTXManiaCX client. It exposes the game state and interaction tools over JSON-RPC so external MCP-compatible clients can inspect and manipulate gameplay.

## Project layout
- `Bridge/` – MonoGame-facing bridge components (`McpBridgeService`, `McpBridgeComponent`) that can be added to the game loop.
- `Server/` – Background service, JSON-RPC helpers, and the console harness entry point (`Program.cs`).
- `Tools/` – Definitions of MCP tools (`game_click`, `game_drag`, `game_get_state`, etc.) and the dispatcher that calls into the JSON-RPC layer.

## Prerequisites
- .NET 8 SDK (`dotnet --version` should report 8.x).
- A DTXManiaCX build exposing the JSON-RPC bridge at `http://localhost:8080/jsonrpc` (current default in `GameInteractionService`).
- An MCP client implementation (for example Anthropic's Claude Desktop, `@modelcontextprotocol/client`, or another MCP-compatible IDE integration).

## Build & restore
```bash
dotnet restore MCP/MCP.csproj
dotnet build MCP/MCP.csproj -c Debug
```
Use `-c Release` when packaging the server or distributing binaries.

## Running the MCP server
The default host is a console application that wires up logging, the game state manager, and tool registry.

```bash
dotnet run --project MCP/MCP.csproj
```

Use `-- --test` to launch the interactive `GameInteractionTestConsole`, which exercises the tool dispatcher without starting the long-running background service. This is handy while the MonoGame bridge and JSON-RPC endpoints are still being scaffolded.

### Server lifecycle
- On startup the server initializes `GameInteractionTools`, which currently registers six high-level actions (`game_click`, `game_drag`, `game_get_state`, `game_get_window_info`, `game_list_clients`, `game_send_key`).
- `GameInteractionService` forwards each tool invocation to the configured JSON-RPC endpoint (`http://localhost:8080/jsonrpc`). Adjust that URL inside `Server/GameInteractionService.cs` if your game build exposes a different port or path.
- `GameStateManager` collects state snapshots reported by the game bridge. The background service polls the manager every five seconds and logs the latest state; the concrete MCP transport will tap into the same manager.

## Configuring an MCP client
Model Context Protocol clients discover and launch servers via small JSON manifests. Regardless of the client, point the command to `dotnet run --project MCP/MCP.csproj` so the .NET host boots on demand. The exact shape of the manifest depends on the client, but every setup needs the command line, working directory, and optional environment variables. The examples below use `<repo-root>` as a placeholder—replace it with the absolute path to your local DTXManiaCX repository clone.

### Using the reference Node.js client (`@modelcontextprotocol/client`)
Create (or merge into) `~/.config/mcp/servers.json`:
```json
{
  "servers": {
    "dtxmaniacx": {
      "command": "dotnet run --project MCP/MCP.csproj",
      "cwd": "<repo-root>",
      "env": {
        "DOTNET_ENVIRONMENT": "Development"
      }
    }
  }
}
```
Then launch the client:
```bash
npx @modelcontextprotocol/client connect dtxmaniacx
```
The CLI will spawn the .NET process, negotiate tool availability, and expose the registered actions to your MCP-compatible application.

### Configuring Claude Desktop (macOS & Windows)
Claude Desktop loads `claude_desktop_config.json` from `~/Library/Application Support/Claude/` (macOS) or `%APPDATA%\Claude\`. Add a server entry:
```json
{
  "mcp_servers": [
    {
      "name": "DTXManiaCX",
      "description": "Interact with the DTXManiaCX MCP bridge",
      "command": "dotnet run --project MCP/MCP.csproj",
      "cwd": "<repo-root>",
      "env": {
        "DOTNET_ENVIRONMENT": "Development"
      }
    }
  ]
}
```
Restart Claude Desktop and pick the new "DTXManiaCX" server from the MCP sources list. Once connected, Claude can call the exposed tools (`game_click`, `game_get_state`, etc.) against your running game instance.

### Other clients
Most IDE integrations follow the same pattern—specify the command line (`dotnet run --project MCP/MCP.csproj`), supply the repo root as the working directory, and propagate any optional environment variables. If your client splits command/arguments instead of accepting a single string, set `command` to `dotnet` and `args` to `["run", "--project", "MCP/MCP.csproj"]`.

## MonoGame integration
Add `McpBridgeComponent` to your MonoGame `Game` implementation to push game state into the server:
```csharp
var bridgeComponent = new McpBridgeComponent(this);
Components.Add(bridgeComponent);
```
`McpBridgeComponent.Initialize` currently targets `http://localhost:3000` while the server scaffolding solidifies. Align that endpoint with wherever the MCP server listens once the networking layer is complete.

## Next steps
- Flesh out the MCP transport layer so `McpServerService` registers tools with the `ModelContextProtocol` host instead of just logging.
- Replace the hard-coded JSON-RPC URL with configuration (environment variables or `appsettings.json`).
- Extend the bridge to stream richer game telemetry (stage, chart, timing) and expose safe control surfaces for automated playtesting.
