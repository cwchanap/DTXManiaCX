using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DTXManiaCX.MCP.Server.Tools;

namespace DTXManiaCX.MCP.Server.Services;

/// <summary>
/// Background worker that performs periodic MCP maintenance/update processing (every 5 seconds).
/// </summary>
public class McpServerService : BackgroundService
{
    private readonly ILogger<McpServerService> _logger;
    private readonly GameStateManager _gameStateManager;
    private readonly GameInteractionTools _gameInteractionTools;
    
    public McpServerService(ILogger<McpServerService> logger, 
                          GameStateManager gameStateManager,
                          GameInteractionTools gameInteractionTools)
    {
        _logger = logger;
        _gameStateManager = gameStateManager;
        _gameInteractionTools = gameInteractionTools;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MCP Server starting...");
        
        try
        {
            // Initialize game interaction tools
            _gameInteractionTools.Initialize();
            
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("MCP Server running at: {time}", DateTimeOffset.Now);
                
                // Simulate processing game state updates
                await _gameStateManager.ProcessUpdatesAsync(stoppingToken);
                
                await Task.Delay(5000, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MCP Server is stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred in MCP Server");
            throw;
        }
        finally
        {
            _logger.LogInformation("MCP Server stopped");
        }
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MCP Server is stopping...");
        await base.StopAsync(cancellationToken);
    }
}
