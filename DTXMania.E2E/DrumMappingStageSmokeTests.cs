using System.Net;
using System.Net.Sockets;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.JsonRpc;
using DTXMania.E2E.Process;
using DTXMania.E2E.Support;
using DTXMania.E2E.Telemetry;

namespace DTXMania.E2E;

/// <summary>
/// Black-box smoke for the visual drum-mapping stage (<c>StageType.DrumConfig</c>): navigate to
/// the stage, open a piece's capture popup, hit a key to bind it, then Back (= save &amp; exit),
/// and assert the new binding was persisted to the sandbox <c>Config.ini</c>. This exercises the
/// live render + capture + save round-trip that the headless unit suite structurally cannot reach.
/// </summary>
[Trait("Category", "E2E")]
public sealed class DrumMappingStageSmokeTests
{
    // Lane 0 (Splash/Crash) defaults to "Key.A"; we append "Key.Z", a key not otherwise bound
    // and not reserved for navigation, so the capture is accepted (append model).
    private const string BindKey = "Z";
    private const string ExpectedBindingId = "Key.Z";

    [Fact(Timeout = 180_000)]
    public async Task DrumMapping_BindKeyThenBack_PersistsBindingToConfig()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var repoRoot = FindRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-drum-" + Guid.NewGuid().ToString("N"));
        var apiPort = GetPortFromEnvironmentOrDefault();
        var fixture = E2EFixtureBuilder.Build(runRoot, repoRoot, apiPort);
        await using var process = new GameProcessDriver();

        using var httpClient = new HttpClient(new SocketsHttpHandler { UseCookies = false })
        {
            BaseAddress = fixture.ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(5)
        };
        var client = new JsonRpcGameClient(httpClient, fixture.ApiKey);

        try
        {
            var projectPath = Environment.GetEnvironmentVariable("DTXMANIA_E2E_GAME_PROJECT")
                ?? (OperatingSystem.IsWindows()
                    ? "DTXMania.Game/DTXMania.Game.Windows.csproj"
                    : "DTXMania.Game/DTXMania.Game.Mac.csproj");

            process.Start(repoRoot, projectPath, fixture);

            await Eventually.UntilAsync(
                _ => client.IsHealthyAsync(cancellation.Token),
                healthy => healthy,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromMilliseconds(500),
                "JSON-RPC health",
                cancellation.Token);

            // Let the game boot fully before jumping stages.
            await WaitForStageAsync(client, "Title", TimeSpan.FromSeconds(45), cancellation.Token);

            // Jump straight to the drum-mapping stage via the API (the menu wiring itself is
            // covered by unit tests); this keeps the smoke independent of menu ordering.
            await client.ChangeStageAsync("DrumConfig", cancellation.Token);
            await WaitForStageAsync(client, "DrumConfig", TimeSpan.FromSeconds(45), cancellation.Token);
            // The stage reports its type as soon as the transition is queued; this settle covers
            // the fade transition + OnActivate (popup/focus/skip-flag init) before we send input.
            await Task.Delay(500, cancellation.Token);

            // Activate the focused lane (lane 0) to open its capture popup.
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            // Let the popup open and the one-frame capture-skip clear before sending the bind key.
            await Task.Delay(700, cancellation.Token);

            // Hit the key to bind — captured and appended to lane 0.
            await client.SendKeyAsync(BindKey, TimeSpan.FromMilliseconds(50), cancellation.Token);
            // Let the capture register before closing the popup.
            await Task.Delay(700, cancellation.Token);

            // First Back closes the popup (acts as "Done")...
            await client.SendKeyAsync("Escape", TimeSpan.FromMilliseconds(50), cancellation.Token);
            // Let the popup Close() propagate before the second Back triggers save-and-exit.
            await Task.Delay(700, cancellation.Token);

            // ...second Back exits the stage; Back = Save, so the working copy is committed.
            await client.SendKeyAsync("Escape", TimeSpan.FromMilliseconds(50), cancellation.Token);

            // Returning to Config proves the save-and-exit path ran.
            await WaitForStageAsync(client, "Config", TimeSpan.FromSeconds(45), cancellation.Token);

            // Save() writes Config.ini synchronously before changing stage, so by the time we are
            // back in Config the file already contains the new binding.
            var configText = await File.ReadAllTextAsync(fixture.ConfigPath, cancellation.Token);
            await E2EArtifactWriter.WriteTextAsync(fixture, "drum-config.ini", configText);

            // [KeyBindings] serializes entries as "<buttonId>=<lane>"; assert the lane too so an
            // unbound-button entry that merely contains the id can't pass.
            Assert.Contains($"{ExpectedBindingId}=0", configText);
        }
        catch (Exception ex)
        {
            await E2EArtifactWriter.WriteTextAsync(fixture, "failure.txt", ex.ToString());
            await TryWriteScreenshotAsync(client, fixture);
            throw;
        }
        finally
        {
            E2EArtifactWriter.CopyFixtureFiles(fixture);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stdout.log", process.StandardOutput);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stderr.log", process.StandardError);
        }
    }

    private static async Task<E2EGameState> WaitForStageAsync(
        JsonRpcGameClient client,
        string expectedStageType,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, expectedStageType, StringComparison.Ordinal),
            timeout,
            TimeSpan.FromMilliseconds(500),
            expectedStageType,
            cancellationToken);
    }

    private static async Task TryWriteScreenshotAsync(JsonRpcGameClient client, E2EFixture fixture)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var imageData = await client.TakeScreenshotBase64Async(cancellation.Token);
            if (string.IsNullOrWhiteSpace(imageData))
                return;

            var imageBytes = Convert.FromBase64String(imageData);
            Directory.CreateDirectory(fixture.ArtifactRoot);
            await File.WriteAllBytesAsync(Path.Combine(fixture.ArtifactRoot, "drum-failure-screenshot.png"), imageBytes, cancellation.Token);
        }
        catch
        {
            // Failure artifacts should never hide the original E2E assertion or launch error.
        }
    }

    private static int GetPortFromEnvironmentOrDefault()
    {
        var raw = Environment.GetEnvironmentVariable("DTXMANIA_E2E_API_PORT");
        if (int.TryParse(raw, out var port))
            return port;

        // Bind an ephemeral port and verify it is usable to avoid TOCTOU races.
        const int maxAttempts = 5;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var chosen = ((IPEndPoint)listener.LocalEndpoint).Port;

            // Quick bind check: release and immediately re-bind to confirm availability.
            listener.Stop();

            try
            {
                var verify = new TcpListener(IPAddress.Loopback, chosen);
                verify.Start();
                verify.Stop();
                return chosen;
            }
            catch (SocketException)
            {
                // Port was snatched up; try again.
            }
        }

        // Fallback: just return a fresh ephemeral port.
        using var fallback = new TcpListener(IPAddress.Loopback, 0);
        fallback.Start();
        return ((IPEndPoint)fallback.LocalEndpoint).Port;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DTXMania.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from current directory.");
    }
}
