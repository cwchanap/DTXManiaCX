#nullable enable

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
    private int _boundPort;
    private IHost? _host;
    private bool _isRunning;
    private CancellationTokenSource? _cancellationTokenSource;
    private const long MaxRequestBodyBytes = 1024;

    public GameApiServer(IGameApi gameApi, int port = 8080, string apiKey = "", ILogger<GameApiServer>? logger = null)
    {
        _gameApi = gameApi ?? throw new ArgumentNullException(nameof(gameApi));
        _port = port;
        _apiKey = apiKey;
        _logger = logger;
        _boundPort = port;
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
            // Use HostBuilder directly to avoid resolving the current working directory,
            // which can fail if the process cwd has been deleted or changed unexpectedly.
            _host = new HostBuilder()
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(options =>
                    {
                        options.Listen(IPAddress.Loopback, _port);
                        options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
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
                                var apiKeyBytes = Encoding.UTF8.GetBytes(_apiKey);

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
                                    _logger?.LogError(ex, "Error processing /game/state request");
                                    context.Response.StatusCode = 500;
                                    var error = new { error = "Internal server error" };
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
                                    _logger?.LogError(ex, "Error processing /game/window request");
                                    context.Response.StatusCode = 500;
                                    var error = new { error = "Internal server error" };
                                    await context.Response.WriteAsJsonAsync(error);
                                }
                            });

                            endpoints.MapPost("/game/input", async context =>
                            {
                                try
                                {
                                    var maxRequestBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                                    if (maxRequestBodySizeFeature != null && !maxRequestBodySizeFeature.IsReadOnly)
                                    {
                                        maxRequestBodySizeFeature.MaxRequestBodySize = MaxRequestBodyBytes;
                                    }

                                    // Check request size limit (prevent large payloads)
                                    if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > MaxRequestBodyBytes) // 1KB limit
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
                                catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
                                {
                                    context.Response.StatusCode = 413;
                                    var error = new { error = "Request payload too large" };
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
            _boundPort = ResolveBoundPort();
            _isRunning = true;

            System.Diagnostics.Debug.WriteLine($"Game API server started on http://localhost:{_boundPort}");
            _logger?.LogInformation("Game API server started on port {Port}", _boundPort);
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
        return $"http://localhost:{_boundPort}";
    }

    /// <summary>
    /// Check if server is running
    /// </summary>
    public bool IsRunning => _isRunning;

    private int ResolveBoundPort()
    {
        if (_host == null)
        {
            return _port;
        }

        var server = _host.Services.GetService<IServer>();
        var address = server?.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();

        if (address != null && Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return uri.Port;
        }

        return _port;
    }

        /// <summary>
        /// Validate game input for security and correctness.
        /// Delegates to the shared <see cref="GameInputValidator"/> so the REST and JSON-RPC
        /// endpoints enforce identical rules.
        /// </summary>
        private static (bool IsValid, string ErrorMessage) ValidateGameInput(GameInput input)
            => GameInputValidator.ValidateGameInput(input);

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
