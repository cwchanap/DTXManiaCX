using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Support;
using DTXMania.Automation.Telemetry;
using DTXMania.E2E.Fixtures;
using DTXMania.E2E.Support;

namespace DTXMania.E2E;

[Trait("Category", "AudioE2E")]
public sealed class PreparedChartRecordingSmokeTests
{
    [Fact(Timeout = 180_000)]
    public async Task PreparedChartRecording_ShouldHoldPreviewUntilStartedAndActivateThroughSongTransition()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(165));
        var repoRoot = E2EGameLaunch.ResolveRepoRoot();
        var runRoot = Path.Combine(
            Path.GetTempPath(),
            "dtxmaniacx-e2e-prepared-chart-" + Guid.NewGuid().ToString("N"));
        var fixture = E2EFixtureBuilder.Build(
            runRoot,
            repoRoot,
            E2EGameLaunch.ResolveApiPort());

        await using var bundle = E2EGameLaunch.CreateClientBundle(fixture);
        var process = bundle.Process;
        var client = bundle.Client;

        try
        {
            process.Start(E2EGameLaunch.CreateOptions(fixture));
            await process.WaitForStartupAsync(
                client.GetHealthAsync,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromMilliseconds(500),
                cancellation.Token);

            await WaitForStageAsync(client, "Title", TimeSpan.FromSeconds(45), cancellation.Token);
            await client.SendKeyAsync("Enter", TimeSpan.FromMilliseconds(50), cancellation.Token);

            var songSelect = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.StageType, "SongSelect", StringComparison.Ordinal)
                    && string.Equals(
                        state.SelectedSongTitle,
                        E2EFixtureBuilder.SongTitle,
                        StringComparison.Ordinal),
                TimeSpan.FromSeconds(45),
                TimeSpan.FromMilliseconds(250),
                "SongSelect fixture chart",
                cancellation.Token);
            Assert.Equal(E2EFixtureBuilder.SongTitle, songSelect.SelectedSongTitle);

            await client.PrepareVideoChartAsync(fixture.ChartPath, cancellation.Token);

            var prepared = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => !string.IsNullOrWhiteSpace(state.PreparedChartIdentity)
                    && string.Equals(state.PreparedPreviewState, "Prepared", StringComparison.Ordinal)
                    && state.PreparedPreviewElapsedMs == 0.0,
                TimeSpan.FromSeconds(20),
                TimeSpan.FromMilliseconds(100),
                "prepared chart state",
                cancellation.Token);
            await SaveStateAsync(
                Path.Combine(fixture.ArtifactRoot, "prepared-state.json"),
                prepared,
                cancellation.Token);

            await client.CaptureScreenshotAsync(
                Path.Combine(fixture.ArtifactRoot, "prepared-song-select.png"),
                cancellation.Token);
            Assert.True(new FileInfo(Path.Combine(fixture.ArtifactRoot, "prepared-song-select.png")).Length > 0);

            await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token);
            var stillPrepared = await client.GetGameStateAsync(cancellation.Token);
            Assert.Equal("Prepared", stillPrepared.PreparedPreviewState);
            Assert.Equal(0.0, stillPrepared.PreparedPreviewElapsedMs);

            await client.StartPreparedPreviewAsync(cancellation.Token);

            var playing = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.PreparedPreviewState, "Playing", StringComparison.Ordinal)
                    && state.PreparedPreviewElapsedMs >= 10_000.0,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(100),
                "prepared preview reaches ten seconds",
                cancellation.Token);
            await SaveStateAsync(
                Path.Combine(fixture.ArtifactRoot, "playing-state.json"),
                playing,
                cancellation.Token);

            await client.ActivatePreparedChartAsync(cancellation.Token);

            var transition = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.StageType, "SongTransition", StringComparison.Ordinal),
                TimeSpan.FromSeconds(20),
                TimeSpan.FromMilliseconds(100),
                "SongTransition after prepared activation",
                cancellation.Token);
            await SaveStateAsync(
                Path.Combine(fixture.ArtifactRoot, "song-transition-state.json"),
                transition,
                cancellation.Token);

            var performance = await Eventually.UntilAsync(
                token => client.GetGameStateAsync(token),
                state => string.Equals(state.StageType, "Performance", StringComparison.Ordinal),
                TimeSpan.FromSeconds(45),
                TimeSpan.FromMilliseconds(250),
                "Performance after prepared activation",
                cancellation.Token);
            await SaveStateAsync(
                Path.Combine(fixture.ArtifactRoot, "performance-state.json"),
                performance,
                cancellation.Token);
        }
        catch (Exception ex)
        {
            await E2EArtifactWriter.WriteTextAsync(
                Path.Combine(fixture.ArtifactRoot, "failure.txt"),
                ex.ToString(),
                cancellation.Token);

            try
            {
                var failureState = await client.GetGameStateAsync(cancellation.Token);
                await SaveStateAsync(
                    Path.Combine(fixture.ArtifactRoot, "failure-state.json"),
                    failureState,
                    cancellation.Token);
            }
            catch
            {
                // Preserve the primary failure when telemetry is no longer available.
            }

            throw;
        }
        finally
        {
            try
            {
                await process.StopAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            finally
            {
                await E2EArtifactWriter.CopyIfExistsAsync(
                    fixture.ConfigPath,
                    Path.Combine(fixture.ArtifactRoot, "config.ini"),
                    CancellationToken.None);
                await E2EArtifactWriter.CopyIfExistsAsync(
                    fixture.ChartPath,
                    Path.Combine(fixture.ArtifactRoot, "autoplay-smoke.dtx"),
                    CancellationToken.None);
                await E2EArtifactWriter.CopyIfExistsAsync(
                    fixture.AudioPath,
                    Path.Combine(fixture.ArtifactRoot, E2EFixtureBuilder.AudioFileName),
                    CancellationToken.None);
                await E2EArtifactWriter.CopyIfExistsAsync(
                    process.StandardOutputPath,
                    Path.Combine(fixture.ArtifactRoot, "game-stdout.log"),
                    CancellationToken.None);
                await E2EArtifactWriter.CopyIfExistsAsync(
                    process.StandardErrorPath,
                    Path.Combine(fixture.ArtifactRoot, "game-stderr.log"),
                    CancellationToken.None);
            }
        }
    }

    private static Task<GameStateSnapshot> WaitForStageAsync(
        JsonRpcGameClient client,
        string stageType,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return Eventually.UntilAsync(
            token => client.GetGameStateAsync(token),
            state => string.Equals(state.StageType, stageType, StringComparison.Ordinal),
            timeout,
            TimeSpan.FromMilliseconds(250),
            $"stage {stageType}",
            cancellationToken);
    }

    private static Task SaveStateAsync(
        string path,
        GameStateSnapshot state,
        CancellationToken cancellationToken)
    {
        return E2EArtifactWriter.WriteJsonAsync(path, state, cancellationToken);
    }
}
