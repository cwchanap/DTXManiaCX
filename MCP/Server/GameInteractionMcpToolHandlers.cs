using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DTXManiaCX.MCP.Server.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DTXManiaCX.MCP.Server.Tools;

/// <summary>
/// Model Context Protocol tool handlers that map MCP calls to the existing game interaction service.
/// </summary>
[McpServerToolType]
public class GameInteractionMcpToolHandlers
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly GameInteractionService _interactionService;

    public GameInteractionMcpToolHandlers(GameInteractionService interactionService)
    {
        _interactionService = interactionService;
    }

    [McpServerTool(Name = "game_click", Title = "Click in Game Window")]
    public async Task<CallToolResult> ClickAsync(
        string client_id,
        int x,
        int y,
        string button = "left",
        CancellationToken cancellationToken = default)
    {
        var (success, message) = await _interactionService.ClickAsync(client_id, x, y, button);

        var payload = new
        {
            action = "click",
            client_id,
            coordinates = new { x, y },
            button
        };

        return BuildResult(success, message, payload);
    }

    [McpServerTool(Name = "game_drag", Title = "Drag in Game Window")]
    public async Task<CallToolResult> DragAsync(
        string client_id,
        int start_x,
        int start_y,
        int end_x,
        int end_y,
        int duration_ms = 500,
        CancellationToken cancellationToken = default)
    {
        var (success, message) = await _interactionService.DragAsync(client_id, start_x, start_y, end_x, end_y, duration_ms);

        var payload = new
        {
            action = "drag",
            client_id,
            start_coordinates = new { x = start_x, y = start_y },
            end_coordinates = new { x = end_x, y = end_y },
            duration_ms
        };

        return BuildResult(success, message, payload);
    }

    [McpServerTool(Name = "game_get_state", Title = "Get Game State", ReadOnly = true, Idempotent = true)]
    public async Task<CallToolResult> GetStateAsync(
        string client_id,
        CancellationToken cancellationToken = default)
    {
        var (success, message, state) = await _interactionService.GetGameStateAsync(client_id);

        var payload = new
        {
            client_id,
            game_state = state
        };

        return BuildResult(success, message, payload);
    }

    [McpServerTool(Name = "game_get_window_info", Title = "Get Game Window", ReadOnly = true, Idempotent = true)]
    public async Task<CallToolResult> GetWindowInfoAsync(
        string client_id,
        CancellationToken cancellationToken = default)
    {
        var (success, message, window) = await _interactionService.GetWindowInfoAsync(client_id);

        var payload = new
        {
            client_id,
            window_info = window
        };

        return BuildResult(success, message, payload);
    }

    [McpServerTool(Name = "game_list_clients", Title = "List Game Clients", ReadOnly = true, Idempotent = true)]
    public async Task<CallToolResult> ListClientsAsync(
        CancellationToken cancellationToken = default)
    {
        // Note: This currently returns a single default client's state.
        // Multi-client support would require game API enhancements.
        var (success, message, clients) = await _interactionService.GetDefaultClientStateAsync();

        var payload = new
        {
            client_count = clients?.Count ?? 0,
            clients
        };

        return BuildResult(success, message, payload);
    }

    [McpServerTool(Name = "game_send_key", Title = "Send Key Input")]
    public async Task<CallToolResult> SendKeyAsync(
        string client_id,
        string key,
        int hold_duration_ms = 50,
        CancellationToken cancellationToken = default)
    {
        var (success, message) = await _interactionService.SendKeyAsync(client_id, key, hold_duration_ms);

        var payload = new
        {
            action = "send_key",
            client_id,
            key,
            hold_duration_ms
        };

        return BuildResult(success, message, payload);
    }

    private static CallToolResult BuildResult(bool success, string message, object? payload)
    {
        var result = new CallToolResult
        {
            IsError = !success,
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = message }
            }
        };

        if (payload is not null)
        {
            result.StructuredContent = JsonSerializer.SerializeToNode(payload, SerializerOptions);
        }

        return result;
    }
}
