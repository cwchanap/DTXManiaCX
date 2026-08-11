using System.Net;
using System.Net.Sockets;
using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;

namespace DTXMania.E2E.Fixtures;

/// <summary>
/// Owns the repository-local policy used by every out-of-process E2E launch.
/// </summary>
public static class E2EGameLaunch
{
    private const string ApiPortEnvironmentVariable = "DTXMANIA_E2E_API_PORT";
    internal const string SimulatedMidiEnvironmentVariable = "DTXMANIA_ENABLE_SIMULATED_MIDI";
    private const int MaxPortAttempts = 5;

    public static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DTXMania.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from current directory.");
    }

    /// <summary>
    /// Resolves a free localhost port for the game API server. The port is obtained
    /// from the OS ephemeral range by binding a temporary listener and immediately
    /// releasing it; the game then binds the same port on startup. An inherent
    /// (low-probability) race window exists between release and the game's bind —
    /// callers that detect an address-in-use failure should retry with a new port.
    /// </summary>
    public static int ResolveApiPort()
    {
        var raw = Environment.GetEnvironmentVariable(ApiPortEnvironmentVariable);
        if (int.TryParse(raw, out var port) && port is >= 1 and <= 65535)
            return port;

        for (var attempt = 0; attempt < MaxPortAttempts; attempt++)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            catch (SocketException) when (attempt < MaxPortAttempts - 1)
            {
                // Retry when a port probe races another process.
            }
        }

        throw new InvalidOperationException(
            "Failed to resolve an available API port after " + MaxPortAttempts + " attempts.");
    }

    /// <summary>
    /// Creates the shared driver + HTTP client + JSON-RPC client bundle used by
    /// every E2E test, preserving the no-cookie SocketsHttpHandler, 5-second
    /// timeout, and fixture API credentials.
    /// </summary>
    public static E2EClientBundle CreateClientBundle(E2EFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return new E2EClientBundle(fixture);
    }

    public static GameProcessStartOptions CreateOptions(
        E2EFixture fixture,
        bool enableSimulatedMidi = false,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        if (extraEnvironment is not null)
        {
            foreach (var entry in extraEnvironment)
            {
                if (string.Equals(
                        entry.Key,
                        SimulatedMidiEnvironmentVariable,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Environment override '{entry.Key}' is reserved for E2E launch policy.",
                        nameof(extraEnvironment));
                }
            }
        }

        var environment = extraEnvironment is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(extraEnvironment, StringComparer.Ordinal);
        environment[SimulatedMidiEnvironmentVariable] = enableSimulatedMidi ? "1" : null;

        return new GameProcessStartOptions(
            ResolveRepoRoot(),
            E2EGameProject.ResolveLaunchTarget(),
            fixture.AppDataRoot,
            Guid.NewGuid().ToString("N"),
            environment);
    }
}

/// <summary>
/// Bundles the <see cref="GameProcessDriver"/>, <see cref="HttpClient"/>, and
/// <see cref="JsonRpcGameClient"/> that every E2E test constructs, so the
/// boilerplate (no-cookie SocketsHttpHandler, 5-second timeout, fixture API
/// credentials) lives in one place. Dispose via <see cref="DisposeAsync"/> to
/// tear down both the process and the HTTP client.
/// </summary>
public sealed class E2EClientBundle : IAsyncDisposable
{
    public GameProcessDriver Process { get; }
    public HttpClient HttpClient { get; }
    public JsonRpcGameClient Client { get; }

    public E2EClientBundle(E2EFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        Process = new GameProcessDriver();
        HttpClient = new HttpClient(new SocketsHttpHandler { UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        Client = new JsonRpcGameClient(
            HttpClient,
            new GameApiConnectionOptions(fixture.ApiBaseUri, fixture.ApiKey));
    }

    public async ValueTask DisposeAsync()
    {
        await Process.DisposeAsync().ConfigureAwait(false);
        HttpClient.Dispose();
    }
}
