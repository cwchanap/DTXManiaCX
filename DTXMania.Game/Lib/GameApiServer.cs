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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib;

/// <summary>
/// Simple HTTP server that exposes game API for MCP server to connect to
/// </summary>
public class GameApiServer : IDisposable, IAsyncDisposable
{
    private readonly IGameApi _gameApi;
    private readonly ILogger<GameApiServer>? _logger;
    private readonly int _port;
    private readonly string _apiKey;
    private IHost? _host;
    private bool _isRunning;
    private CancellationTokenSource? _cancellationTokenSource;

    public GameApiServer(IGameApi gameApi, int port = 8080, string apiKey = "", ILogger<GameApiServer>? logger = null)
    {
        _gameApi = gameApi ?? throw new ArgumentNullException(nameof(gameApi));
        _port = port;
        _apiKey = apiKey;
        _logger = logger;
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
                        // Add API key validation middleware for protected endpoints
                        app.Use(async (context, next) =>
                        {
                            // Skip authentication for health endpoint
                            if (context.Request.Path.StartsWithSegments("/health"))
                            {
                                await next();
                                return;
                            }

                            // Validate API key if configured
                            if (!string.IsNullOrEmpty(_apiKey))
                            {
                                var providedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
                                var providedKeyBytes = Encoding.UTF8.GetBytes(providedKey ?? string.Empty);
                                var apiKeyBytes = Encoding.UTF8.GetBytes(_apiKey ?? string.Empty);

                                var maxLength = Math.Max(providedKeyBytes.Length, apiKeyBytes.Length);
                                var providedKeyPadded = new byte[maxLength];
                                var apiKeyPadded = new byte[maxLength];
                                Buffer.BlockCopy(providedKeyBytes, 0, providedKeyPadded, 0, providedKeyBytes.Length);
                                Buffer.BlockCopy(apiKeyBytes, 0, apiKeyPadded, 0, apiKeyBytes.Length);

                                var isMatch = CryptographicOperations.FixedTimeEquals(providedKeyPadded, apiKeyPadded);
                                isMatch &= providedKeyBytes.Length == apiKeyBytes.Length;

                                if (!isMatch)
                                {
                                    context.Response.StatusCode = 401;
                                    await context.Response.WriteAsJsonAsync(new { error = "Unauthorized: Invalid or missing API key" });
                                    return;
                                }
                            }

                            await next();
                        });

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
                                    _logger?.LogError(ex, "Error processing /game/input request");
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
    // NOTE: This validation logic is duplicated in JsonRpcServer.cs. Consider extracting it into a shared validator
    // to keep behavior consistent and avoid having to update multiple copies when the rules change.
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
                if (input.Data is null || input.Data.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    return (false, "Mouse input requires position data");

                if (input.Data.Value.ValueKind != JsonValueKind.Object)
                    return (false, "Mouse input data must be an object");
                break;

            case InputType.KeyPress:
            case InputType.KeyRelease:
                // For key input, data should contain key info
                if (input.Data is null || input.Data.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    return (false, "Key input requires key data");

                if (input.Data.Value.ValueKind == JsonValueKind.String)
                {
                    var keyData = input.Data.Value.GetString();
                    if (string.IsNullOrEmpty(keyData) || keyData.Length > 50)
                        return (false, "Invalid key data format");
                }
                else if (input.Data.Value.ValueKind == JsonValueKind.Number)
                {
                    if (!input.Data.Value.TryGetInt32(out var keyCode) || keyCode < 0 || keyCode > 255)
                        return (false, "Invalid key data format");
                }
                else
                {
                    return (false, "Invalid key data format");
                }
                break;

            default:
                return (false, "Unsupported input type");
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// Dispose of resources asynchronously
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isRunning)
        {
            await StopAsync();
        }

        _cancellationTokenSource?.Dispose();
        _host?.Dispose();
    }

    /// <summary>
    /// Dispose of resources synchronously
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}