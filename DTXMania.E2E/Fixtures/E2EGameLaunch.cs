using System.Net;
using System.Net.Sockets;
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

    public static int ResolveApiPort()
    {
        var raw = Environment.GetEnvironmentVariable(ApiPortEnvironmentVariable);
        if (int.TryParse(raw, out var port) && port is >= 1 and <= 65535)
            return port;

        for (var attempt = 0; attempt < MaxPortAttempts; attempt++)
        {
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var chosen = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                listener = null;

                using var verify = new TcpListener(IPAddress.Loopback, chosen);
                verify.Start();
                verify.Stop();
                return chosen;
            }
            catch (SocketException)
            {
                // Retry when a port probe races another process.
            }
            finally
            {
                listener?.Stop();
            }
        }

        using var fallback = new TcpListener(IPAddress.Loopback, 0);
        fallback.Start();
        return ((IPEndPoint)fallback.LocalEndpoint).Port;
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
