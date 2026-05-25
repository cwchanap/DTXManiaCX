# DTXManiaCX MCP

The MCP project hosts the Model Context Protocol (MCP) bridge that lets AI copilots talk to a running DTXManiaCX client. It exposes the game state and interaction tools over JSON-RPC so external MCP-compatible clients can inspect and manipulate gameplay.

## Project layout
- `Bridge/` – MonoGame-facing bridge components (`McpBridgeService`, `McpBridgeComponent`) that can be added to the game loop.
- `Server/` – Stdio MCP server entry point, JSON-RPC helpers, and the console harness (`Program.cs`).
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

### Configuration via environment variables
The MCP server can be configured using the following environment variables:

| Variable | Description | Default |
|----------|-------------|---------|
| `DTXMANIA_API_URL` | The JSON-RPC endpoint URL for the game API | `http://localhost:8080/jsonrpc` |
| `DTXMANIA_API_KEY` | The API key for authenticating with the game API (required if the game has `EnableGameApi` with `GameApiKey` set) | _(none)_ |
| `DTXMANIA_PROJECT_PATH` | The game project path used by `game_launch` and `game_restart` | _(none)_ |

Example with configuration:
```bash
DTXMANIA_API_KEY="your-api-key" dotnet run --project MCP/MCP.csproj
```

Use `-- --test` to launch the interactive `GameInteractionTestConsole`, which exercises the tool dispatcher without starting the long-running MCP stdio session. This is handy while validating the MonoGame bridge and JSON-RPC endpoints.

### Server lifecycle
- On startup the server starts the ModelContextProtocol stdio transport and exposes the registered tool handlers, including `game_launch`, `game_restart`, `game_change_stage`, `game_get_state`, `game_send_key`, `game_click`, `game_drag`, `game_screenshot`, `game_get_window_info`, and `game_list_clients`.
- `GameInteractionService` forwards each tool invocation to the configured JSON-RPC endpoint (default: `http://localhost:8080/jsonrpc`). Configure the URL via the `DTXMANIA_API_URL` environment variable if your game build exposes a different port or path.
- If the game API requires authentication (when `EnableGameApi` is enabled with a `GameApiKey` in `Config.ini`), set the `DTXMANIA_API_KEY` environment variable to the same key value. Without this, MCP tool calls will receive HTTP 401 Unauthorized errors.
- Set `DTXMANIA_PROJECT_PATH` when you want MCP clients to launch or restart the game process instead of connecting to a game that is already running.

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
        "DOTNET_ENVIRONMENT": "Development",
        "DTXMANIA_API_KEY": "<your-api-key-from-Config.ini>"
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
        "DOTNET_ENVIRONMENT": "Development",
        "DTXMANIA_API_KEY": "<your-api-key-from-Config.ini>"
      }
    }
  ]
}
```
Restart Claude Desktop and pick the new "DTXManiaCX" server from the MCP sources list. Once connected, Claude can call the exposed tools (`game_click`, `game_get_state`, etc.) against your running game instance.

### Other clients
Most IDE integrations follow the same pattern—specify the command line (`dotnet run --project MCP/MCP.csproj`), supply the repo root as the working directory, and propagate the `DTXMANIA_API_KEY` environment variable (if the game API requires authentication). If your client splits command/arguments instead of accepting a single string, set `command` to `dotnet` and `args` to `["run", "--project", "MCP/MCP.csproj"]`.

## MonoGame integration
Add `McpBridgeComponent` to your MonoGame `Game` implementation to push game state into the server:
```csharp
var bridgeComponent = new McpBridgeComponent(this);
Components.Add(bridgeComponent);
```

**Note:** `McpBridgeComponent.Initialize` currently targets a placeholder endpoint (`http://localhost:3000`) for future MCP client functionality. This is separate from the JSON-RPC server endpoint (`http://localhost:8080/jsonrpc`) used by `GameInteractionService`. The bridge component will be updated to use configurable endpoints once the MCP transport layer is complete.

## Next steps
- Extend the bridge to stream richer game telemetry (stage, chart, timing) and expose safe control surfaces for automated playtesting.
