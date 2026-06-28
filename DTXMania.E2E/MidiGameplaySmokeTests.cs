using System.Net;
using System.Net.Sockets;
using System.Text;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.JsonRpc;
using DTXMania.E2E.Process;
using DTXMania.E2E.Support;
using DTXMania.E2E.Telemetry;

namespace DTXMania.E2E;

[Trait("Category", "E2E")]
public sealed class MidiGameplaySmokeTests
{
    private const int MidiNote = 36;
    private const int MidiLane = 5;
    private const int MidiVelocityThreshold = 20;
    private const double TargetNoteTimeMs = 10_000.0;

    [Fact(Timeout = 180_000)]
    public async Task SimulatedMidiNote_ShouldRegisterGameplayJudgement()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var repoRoot = FindRepoRoot();
        var runRoot = Path.Combine(Path.GetTempPath(), "dtxmaniacx-e2e-midi-" + Guid.NewGuid().ToString("N"));
        var apiPort = GetPortFromEnvironmentOrDefault();
        var fixture = E2EFixtureBuilder.Build(runRoot, repoRoot, apiPort);
        ConfigureMidiGameplayFixture(fixture);
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

            process.Start(repoRoot, projectPath, fixture, enableSimulatedMidi: true);

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

            await WaitForPerformanceReadyAsync(client, TimeSpan.FromSeconds(60), cancellation.Token);
            await WaitForSongClockAsync(client, TimeSpan.FromSeconds(15), cancellation.Token);
            await SendMidiNearNoteAsync(client, TargetNoteTimeMs, cancellation.Token);

            var laneHit = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.StageType, "Performance", StringComparison.Ordinal)
                    && state.LastLaneHitLane == MidiLane
                    && string.Equals(state.LastLaneHitButtonId, $"MIDI.{MidiNote}", StringComparison.Ordinal),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromMilliseconds(20),
                "simulated MIDI lane hit",
                cancellation.Token);

            await E2EArtifactWriter.WriteJsonAsync(fixture, "midi-lane-hit-state.json", laneHit);

            var judged = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => state.TotalJudgements > 0 && state.Score > 0,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(20),
                "simulated MIDI gameplay judgement",
                cancellation.Token);

            await E2EArtifactWriter.WriteJsonAsync(fixture, "midi-final-state.json", judged);

            Assert.False(judged.AutoPlayEnabled);
            Assert.Equal(1, judged.TotalNotes);
            Assert.Equal(1, judged.TotalJudgements);
            Assert.True(judged.Score > 0);
            Assert.True(judged.MaxCombo >= 1);
            Assert.Equal(0, judged.MissCount);
        }
        catch (Exception ex)
        {
            await E2EArtifactWriter.WriteTextAsync(fixture, "failure.txt", ex.ToString());
            try
            {
                var failureState = await client.GetGameStateAsync(CancellationToken.None);
                await E2EArtifactWriter.WriteJsonAsync(fixture, "failure-state.json", failureState);
            }
            catch
            {
            }
            throw;
        }
        finally
        {
            E2EArtifactWriter.CopyFixtureFiles(fixture);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stdout.log", process.StandardOutput);
            await E2EArtifactWriter.WriteTextAsync(fixture, "game-stderr.log", process.StandardError);
        }
    }

    private static void ConfigureMidiGameplayFixture(E2EFixture fixture)
    {
        var config = File.ReadAllText(fixture.ConfigPath, Encoding.UTF8)
            .Replace("AutoPlay=True", "AutoPlay=False", StringComparison.Ordinal);

        config += string.Join('\n', new[]
        {
            string.Empty,
            "[KeyBindings]",
            $"MIDI.{MidiNote}={MidiLane}",
            string.Empty,
            "[MidiVelocityThresholds]",
            $"MidiVelocity.{MidiNote}={MidiVelocityThreshold}",
            string.Empty
        });

        File.WriteAllText(fixture.ConfigPath, config, Encoding.UTF8);
        File.WriteAllText(fixture.ChartPath, BuildMidiChart(), Encoding.UTF8);
    }

    private static string BuildMidiChart()
    {
        return string.Join('\n', new[]
        {
            "#TITLE: E2E MIDI Gameplay Smoke",
            "#ARTIST: CI",
            "#BPM: 120",
            "#DLEVEL: 10",
            string.Empty,
            "; Single bass-drum lane note at measure 5, about ten seconds into playback.",
            "#00513: 01",
            string.Empty
        });
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

    private static async Task<E2EGameState> WaitForPerformanceReadyAsync(
        JsonRpcGameClient client,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, "Performance", StringComparison.Ordinal)
                && state.PerformanceReady
                && !state.AutoPlayEnabled
                && state.TotalNotes == 1,
            timeout,
            TimeSpan.FromMilliseconds(250),
            "manual MIDI Performance readiness",
            cancellationToken);
    }

    private static async Task<E2EGameState> WaitForSongClockAsync(
        JsonRpcGameClient client,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, "Performance", StringComparison.Ordinal)
                && state.CurrentSongTimeMs > 100,
            timeout,
            TimeSpan.FromMilliseconds(50),
            "manual MIDI song clock start",
            cancellationToken);
    }

    private static async Task SendMidiNearNoteAsync(
        JsonRpcGameClient client,
        double targetSongTimeMs,
        CancellationToken cancellationToken)
    {
        // Lead time must account for JSON-RPC round-trip latency, poll granularity (up to 50 ms),
        // and CI scheduling jitter. 100 ms keeps the note comfortably inside the ±150 ms Poor
        // window even under load: worst case (50 ms poll + ~195 ms round trip) lands at ~+145 ms,
        // while the fast path lands at ~−95 ms — both non-Miss.
        const double leadMs = 100.0;
        var sendAtMs = targetSongTimeMs - leadMs;

        while (true)
        {
            var state = await client.GetGameStateAsync(cancellationToken);
            if (!string.Equals(state.StageType, "Performance", StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected Performance stage before MIDI note injection, got {state.StageType}.");

            var delayMs = sendAtMs - state.CurrentSongTimeMs;
            if (delayMs <= 0)
                break;

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(delayMs, 50.0)), cancellationToken);
        }

        await client.SendMidiNoteAsync(MidiNote, velocity: 100, TimeSpan.FromMilliseconds(20), cancellationToken);
    }

    private static int GetPortFromEnvironmentOrDefault()
    {
        var raw = Environment.GetEnvironmentVariable("DTXMANIA_E2E_API_PORT");
        // Only honor the override when it parses to a valid TCP port (1-65535); otherwise fall
        // through to an ephemeral OS-assigned port so a misconfigured value can't break the run.
        const int minValidTcpPort = 1;
        const int maxValidTcpPort = 65535;
        if (int.TryParse(raw, out var port) &&
            port >= minValidTcpPort && port <= maxValidTcpPort)
        {
            return port;
        }

        const int maxAttempts = 5;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var chosen = ((IPEndPoint)listener.LocalEndpoint).Port;
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
            }
        }

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
