using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib;

/// <summary>
/// Simple HTTP server that exposes game API for MCP server to connect to
/// </summary>
public class GameApiServer : IDisposable
{
    private readonly IGameApi _gameApi;
    private readonly ILogger<GameApiServer>? _logger;
    private readonly int _port;
    private readonly string _apiKey;
    private IHost? _host;
    private bool _isRunning;
    private CancellationTokenSource? _cancellationTokenSource;

    public GameApiServer(IGameApi gameApi, int port = 8080, string apiKey = "")
    {
        _gameApi = gameApi ?? throw new ArgumentNullException(nameof(gameApi));
        _port = port;
        _apiKey = apiKey;

        // Try to get logger if available
        try
        {
            // This would be set up by the game's dependency injection if available
            _logger = null; // For now, we'll use Console.WriteLine
        }
        catch
        {
            // Logger not available
        }
    }

    /// <summary>
    /// Start the API server
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
            return;

        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(options =>
                    {
                        options.Listen(IPAddress.Loopback, _port);
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/health", async context =>
                            {
                                var response = new
                                {
                                    status = "ok",
                                    game_running = _gameApi.IsRunning,
                                    timestamp = DateTime.UtcNow
                                };
                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsJsonAsync(response);
                            });

                            endpoints.MapGet("/game/state", async context =>
                            {
                                try
                                {
                                    var gameState = await _gameApi.GetGameStateAsync();
                                    context.Response.ContentType = "application/json";
                                    await context.Response.WriteAsJsonAsync(gameState);
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    var error = new { error = ex.Message };
                                    await context.Response.WriteAsJsonAsync(error);
                                }
                            });

                            endpoints.MapGet("/game/window", async context =>
                            {
                                try
                                {
                                    var windowInfo = await _gameApi.GetWindowInfoAsync();
                                    context.Response.ContentType = "application/json";
                                    await context.Response.WriteAsJsonAsync(windowInfo);
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    var error = new { error = ex.Message };
                                    await context.Response.WriteAsJsonAsync(error);
                                }
                            });

                            endpoints.MapPost("/game/input", async context =>
                            {
                                try
                                {
                                    // Check request size limit (prevent large payloads)
                                    if (context.Request.ContentLength > 1024) // 1KB limit
                                    {
                                        context.Response.StatusCode = 413; // Payload Too Large
                                        var error = new { error = "Request payload too large" };
                                        await context.Response.WriteAsJsonAsync(error);
                                        return;
                                    }

                                    var input = await context.Request.ReadFromJsonAsync<GameInput>();
                                    
                                    // Check if input is null first
                                    if (input == null)
                                    {
                                        context.Response.StatusCode = 400;
                                        var error = new { error = "Input data is null" };
                                        await context.Response.WriteAsJsonAsync(error);
                                        return;
                                    }
                                    
                                    // Validate input
                                    var (isValid, errorMessage) = ValidateGameInput(input);
                                    if (!isValid)
                                    {
                                        context.Response.StatusCode = 400;
                                        var error = new { error = errorMessage };
                                        await context.Response.WriteAsJsonAsync(error);
                                        return;
                                    }

                                    var success = await _gameApi.SendInputAsync(input!);
                                    var response = new { success };
                                    context.Response.ContentType = "application/json";
                                    await context.Response.WriteAsJsonAsync(response);
                                }
                                catch (JsonException)
                                {
                                    context.Response.StatusCode = 400;
                                    var error = new { error = "Invalid JSON format" };
                                    await context.Response.WriteAsJsonAsync(error);
                                }
                                catch (Exception ex)
                                {
                                    context.Response.StatusCode = 500;
                                    var error = new { error = "Internal server error" }; // Don't leak implementation details
                                    await context.Response.WriteAsJsonAsync(error);
                                }
                            });
                        });
                    });
                })
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                })
                .Build();

            await _host.StartAsync(_cancellationTokenSource.Token);
            _isRunning = true;

            System.Diagnostics.Debug.WriteLine($"Game API server started on http://localhost:{_port}");
            _logger?.LogInformation("Game API server started on port {Port}", _port);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start game API server: {ex.Message}");
            _logger?.LogError(ex, "Failed to start game API server");
            throw;
        }
    }

    /// <summary>
    /// Stop the API server
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        try
        {
            _cancellationTokenSource?.Cancel();

            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
                _host = null;
            }

            _isRunning = false;
            System.Diagnostics.Debug.WriteLine("Game API server stopped");
            _logger?.LogInformation("Game API server stopped");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error stopping game API server: {ex.Message}");
            _logger?.LogError(ex, "Error stopping game API server");
        }
    }

    /// <summary>
    /// Get the server URL
    /// </summary>
    public string GetServerUrl()
    {
        return $"http://localhost:{_port}";
    }

    /// <summary>
    /// Check if server is running
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Validate game input for security and correctness
    /// </summary>
    private static (bool IsValid, string ErrorMessage) ValidateGameInput(GameInput input)
    {
        // Validate input type
        if (!Enum.IsDefined(typeof(InputType), input.Type))
            return (false, "Invalid input type");

        // Validate data based on input type
        switch (input.Type)
        {
            case InputType.MouseClick:
            case InputType.MouseMove:
                // For mouse input, data should contain position info
                if (input.Data == null)
                    return (false, "Mouse input requires position data");
                break;

            case InputType.KeyPress:
            case InputType.KeyRelease:
                // For key input, data should contain key info
                if (input.Data == null)
                    return (false, "Key input requires key data");
                
                // Basic sanitization - check if it's a reasonable string/number
                var keyData = input.Data.ToString();
                if (string.IsNullOrEmpty(keyData) || keyData.Length > 50)
                    return (false, "Invalid key data format");
                break;

            default:
                return (false, "Unsupported input type");
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        if (_isRunning)
        {
            StopAsync().Wait();
        }

        _cancellationTokenSource?.Dispose();
        _host?.Dispose();
    }
}