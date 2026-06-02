using System.Net;
using System.Net.Sockets;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.JsonRpc;
using DTXMania.E2E.Process;
using DTXMania.E2E.Support;
using DTXMania.E2E.Telemetry;

namespace DTXMania.E2E;

[Trait("Category", "E2E")]
public sealed class GameplayAutoPlaySmokeTests
{
    [Fact(Timeout = 180_000)]
    public async Task AutoPlaySmoke_ShouldNavigateToResultAndReportClearSummary()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var repoRoot = FindRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-" + Guid.NewGuid().ToString("N"));
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

            await WaitForStageAsync(client, "Title", TimeSpan.FromSeconds(45), cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);

            await WaitForStageAsync(client, "SongSelect", TimeSpan.FromSeconds(45), cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);
            await Task.Delay(500, cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);

            await WaitForStageAsync(client, "Performance", TimeSpan.FromSeconds(60), cancellation.Token);
            var resultState = await WaitForStageAsync(client, "Result", TimeSpan.FromSeconds(90), cancellation.Token);

            await E2EArtifactWriter.WriteJsonAsync(fixture, "final-state.json", resultState);

            Assert.Equal(E2EFixtureBuilder.SongTitle, resultState.SelectedSongTitle);
            Assert.True(resultState.StageCompleted);
            Assert.True(resultState.TotalNotes > 0, "Expected generated chart to contain notes.");
            Assert.Equal(resultState.TotalNotes, resultState.TotalJudgements);
            Assert.True(resultState.ClearFlag);
            Assert.True(resultState.Score > 0);
            Assert.Equal("SongComplete", resultState.CompletionReason);
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
            await File.WriteAllBytesAsync(Path.Combine(fixture.ArtifactRoot, "failure-screenshot.png"), imageBytes, cancellation.Token);
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

        // Bind an ephemeral port and verify it is usable to avoid TOCTOU races
        const int maxAttempts = 5;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var chosen = ((IPEndPoint)listener.LocalEndpoint).Port;

            // Quick bind check: release and immediately re-bind to confirm availability
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
                // Port was snatched up; try again
            }
        }

        // Fallback: just return a fresh ephemeral port
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
